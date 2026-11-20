namespace Jige.Core.Adapters;

internal sealed class RazerFeatureAdapter(TimeSpan? settleDelay = null, TimeSpan? busyRetryDelay = null)
    : IDeviceAdapter, IPresenceAdapter
{
    // D4（#19）：雷蛇协议状态 0x01 表示无线事务正忙（不是被其他程序占用句柄），短暂等待后最多重试 3 次。
    // 若回复的是合法雷蛇 90B 帧但属于其他命令（如 Synapse / 灯效软件的 0F:03），同样短暂等待后重发，最多重试 3 次。
    internal const int MaxBusyRetries = 3;
    private readonly TimeSpan? settleOverride = settleDelay;
    private readonly TimeSpan retryWait = busyRetryDelay ?? TimeSpan.FromMilliseconds(80);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || DeviceCatalog.IsDisabledPath(endpoint, out _) ||
            Match(endpoint) is not { } model)
            return null;
        using var session = transport.OpenHid(endpoint with { RequiredFeatureReportId = 0, FeatureOnly = true }, token);
        var (status, _, foreign) = await QueryWithBusyRetryAsync(session, model, RazerFeatureQuery.Battery, null, token)
            .ConfigureAwait(false);
        if (foreign)
            return new(endpoint.ReceiverId, null, target.Device.Id, LinkState.Busy, ReadIssue.Busy)
            {
                Connection = target.Device.Connection
            };
        return (status switch
        {
            2 => new PresenceUpdate(endpoint.ReceiverId, null, target.Device.Id, LinkState.Online),
            4 => new PresenceUpdate(endpoint.ReceiverId, null, target.Device.Id, LinkState.Offline),
            // 协议忙重试耗尽不报告为 Busy（避免误报「被其他程序占用」），保持 Unknown/Timeout。
            1 => new PresenceUpdate(endpoint.ReceiverId, null, target.Device.Id, LinkState.Unknown, ReadIssue.Timeout),
            _ => new PresenceUpdate(endpoint.ReceiverId, null, target.Device.Id, LinkState.Unknown, ReadIssue.InvalidResponse)
        }) with { Connection = target.Device.Connection };
    }

    /// <summary>雷蛇 90B 在 Windows 上既可能挂在厂商页（FF00+），也可能直接挂在标准鼠标/键盘页（0001:0002 / 0006）的 91 字节特征通道上。</summary>
    internal static bool IsFeatureCollection(HidInterface endpoint) =>
        endpoint is { VendorId: 0x1532, FeatureLength: RazerFeatureProtocol.ReportLength, MetadataIssue: ReadIssue.None } &&
        (endpoint.UsagePage is >= 0xFF00 || endpoint is { UsagePage: 0x0001, Usage: 0x0002 or 0x0006 });

    private static RazerFeatureModel? Match(HidInterface endpoint)
    {
        if (DeviceCatalog.IsDisabledPath(endpoint, out _))
            return null;
        var model = RazerFeatureProtocol.Find(endpoint.ProductId);
        return model is not null && IsFeatureCollection(endpoint) &&
            (endpoint.InterfaceNumber == model.InterfaceNumber ||
                model.InterfaceNumber == 0 && endpoint.InterfaceNumber is null)
            ? model : null;
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(hid => Match(hid) is not null)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.ToArray();
            // 若同一接收器同时暴露厂商页与标准页的 91B 特征集合，优先选唯一的厂商页集合；否则选唯一的标准页集合。
            var vendor = candidates.Where(hid => hid.UsagePage is >= 0xFF00).ToArray();
            var picked = vendor.Length > 0 ? vendor : candidates;
            if (picked.Length != 1)
                continue;
            var endpoint = picked[0];
            var model = Match(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"razer-90b:{endpoint.ReceiverId}", $"Razer {model.Name}", model.Kind,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "雷蛇 USB 直连" : "雷蛇无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "雷蛇 90B 接收器控制集合不匹配");
        if (DeviceCatalog.IsDisabledPath(endpoint, out var disabled))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}");
        if (Match(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "雷蛇 90B 接收器控制集合不匹配");

        using var session = transport.OpenHid(endpoint with { RequiredFeatureReportId = 0, FeatureOnly = true }, token);
        var battery = await QueryWithBusyRetryAsync(session, model, RazerFeatureQuery.Battery, trace, token)
            .ConfigureAwait(false);
        if (battery.Foreign)
            return AdapterRegistry.Failure(target, ReadIssue.Busy,
                "雷蛇 90B 通道正被其他程序（如 Synapse 或灯效软件）高频占用，回复属于其他命令");
        if (battery.Status == 4)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "接收器报告无线设备未响应（07:80 状态 04）");
        if (battery.Status == 1)
            // D4：协议返回忙（01）在重试 3 次后按超时结束，不得归为 ReadIssue.Busy / LinkState.Busy（"被其他程序占用"）。
            return AdapterRegistry.Failure(target, ReadIssue.Timeout,
                $"雷蛇 07:80 协议忙（状态 01），已重试 {MaxBusyRetries} 次仍未就绪");
        if (battery.Status != 2)
        {
            var detail = battery.Status == 0 ? "雷蛇 07:80 回复的事务、长度或校验不符" : $"雷蛇 07:80 状态 {battery.Status:X2}";
            return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, detail);
        }

        var charging = ChargingState.Unknown;
        string? chargingDetail = null;
        if (model.HasCharging)
        {
            try
            {
                var power = await QueryWithBusyRetryAsync(session, model, RazerFeatureQuery.Charging, trace, token)
                    .ConfigureAwait(false);
                charging = power switch
                {
                    { Foreign: false, Status: 2, Value: 0 } => ChargingState.NotCharging,
                    { Foreign: false, Status: 2, Value: 1 } => ChargingState.Charging,
                    _ => ChargingState.Unknown
                };
                if (charging == ChargingState.Unknown)
                    chargingDetail = power.Foreign
                        ? "雷蛇 07:84 通道被其他程序占用，未确认充电状态"
                        : $"雷蛇 07:84 未确认充电状态（状态 {power.Status:X2}，值 {power.Value:X2}）";
            }
            catch (TransportException error) when (error.Issue is not ReadIssue.Disconnected)
            {
                chargingDetail = error.Message;
            }
        }
        return new(target.Device, RazerFeatureProtocol.Percentage(battery.Value), false, charging,
            LinkState.Online, DateTimeOffset.UtcNow, Detail: chargingDetail);
    }

    private async ValueTask<(byte Status, byte Value, bool Foreign)> QueryWithBusyRetryAsync(
        IHidSession session, RazerFeatureModel model, RazerFeatureQuery query, FrameTrace? trace, CancellationToken token)
    {
        var request = RazerFeatureProtocol.Encode(model.Pid, query);
        var response = new byte[RazerFeatureProtocol.ReportLength];
        var settle = settleOverride ?? TimeSpan.FromMilliseconds(model.SettleMs);
        bool lastForeign = false;
        for (int attempt = 0; attempt <= MaxBusyRetries; attempt++)
        {
            if (attempt > 0 && retryWait > TimeSpan.Zero)
                await Task.Delay(retryWait, token).ConfigureAwait(false);
            await session.WriteFeatureAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            if (settle > TimeSpan.Zero)
                await Task.Delay(settle, token).ConfigureAwait(false);
            Array.Clear(response);
            await session.ReadFeatureAsync(response, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Received, response);
            if (RazerFeatureProtocol.TryDecode(response, model, query, out byte status, out byte value))
            {
                lastForeign = false;
                if (status != 1)
                    return (status, value, false);
                continue;
            }
            if (RazerFeatureProtocol.IsForeignReply(response, model, query))
            {
                lastForeign = true;
                continue;
            }
            return (0, 0, false);
        }
        return lastForeign ? ((byte)0, (byte)0, true) : ((byte)1, (byte)0, false);
    }
}

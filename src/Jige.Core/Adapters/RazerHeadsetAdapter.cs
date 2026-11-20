namespace Jige.Core.Adapters;

internal sealed class RazerHeadsetAdapter(TimeSpan? featureSettleDelay = null) : IDeviceAdapter, IPresenceAdapter
{
    private const int MaxBlackShark2020Polls = 3;
    private static int nextSequence;
    private readonly TimeSpan featureSettle = featureSettleDelay ?? TimeSpan.FromMilliseconds(100);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || DeviceCatalog.IsDisabledPath(endpoint, out _) || !Matches(endpoint))
            return null;
        if (IsBlackShark2020(endpoint))
        {
            var reading = await ReadBlackShark2020Async(target, endpoint, transport, null, token).ConfigureAwait(false);
            return new(endpoint.ReceiverId, null, target.Device.Id, reading.Link, reading.Issue);
        }

        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 2, RequiredOutputReportId = 2 }, token);
        var sequence = (byte)(Interlocked.Increment(ref nextSequence) & 0x1F);
        var request = RazerHeadsetProtocol.EncodeMediaTek(0x0565, RazerHeadsetQuery.Link, sequence);
        var frame = await HidQuery.OutputAsync(session, session, request, RazerHeadsetProtocol.ReportLength,
            packet => RazerHeadsetProtocol.TryMediaTek(packet, request, 0x0565, out _), null, token).ConfigureAwait(false);
        // R1：查询超时一律算休眠；仅链路字节明确返回 0 时才判定离线。
        if (frame is null || !RazerHeadsetProtocol.TryMediaTek(frame, request, 0x0565, out byte linkValue))
            return new(endpoint.ReceiverId, null, target.Device.Id, LinkState.Asleep, ReadIssue.Timeout);
        return new(endpoint.ReceiverId, null, target.Device.Id, linkValue == 0 ? LinkState.Offline : LinkState.Online);
    }

    // PA 需切换远程模式，V3 系列冷启动有会话与断链风险。仅注册纯只读路径：V2 HyperSpeed（0565）与 V2 Pro 2020（0528）。
    private static bool IsV2HyperSpeed(HidInterface hid) => !DeviceCatalog.IsDisabledPath(hid, out _) && hid is
    {
        VendorId: 0x1532, ProductId: 0x0565, UsagePage: 0xFF14, Usage: 1, InterfaceNumber: 3,
        InputLength: RazerHeadsetProtocol.ReportLength, OutputLength: RazerHeadsetProtocol.ReportLength,
        MetadataIssue: ReadIssue.None
    };

    private static bool IsBlackShark2020(HidInterface hid) => !DeviceCatalog.IsDisabledPath(hid, out _) && hid is
    {
        VendorId: 0x1532, ProductId: RazerHeadsetProtocol.BlackShark2020Pid,
        UsagePage: 0xFF00, Usage: 1, FeatureLength: RazerHeadsetProtocol.ReportLength,
        MetadataIssue: ReadIssue.None
    };

    private static bool Matches(HidInterface hid) => IsV2HyperSpeed(hid) || IsBlackShark2020(hid);

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(Matches)))
        {
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            string name = IsBlackShark2020(endpoint)
                ? "Razer BlackShark V2 Pro (2020)"
                : "Razer BlackShark V2 HyperSpeed";
            yield return new(new($"razer-headset:{endpoint.ReceiverId}", name,
                DeviceKind.Headset, AdaptationSource.Catalog, tier)
            {
                SourceLabel = "雷蛇无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "雷蛇耳机控制集合不匹配");
        if (DeviceCatalog.IsDisabledPath(endpoint, out var disabled))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}");
        if (IsBlackShark2020(endpoint))
            return await ReadBlackShark2020Async(target, endpoint, transport, trace, token).ConfigureAwait(false);
        if (!IsV2HyperSpeed(endpoint))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "BlackShark 控制集合不匹配");

        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 2, RequiredOutputReportId = 2 }, token);
        var link = await QueryAsync(RazerHeadsetQuery.Link).ConfigureAwait(false);
        if (link == 0)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "BlackShark 接收器报告无线链路断开");

        var level = await QueryAsync(RazerHeadsetQuery.Battery).ConfigureAwait(false);
        if (level is null)
            return AdapterRegistry.Failure(target, ReadIssue.Timeout, "未收到匹配的 BlackShark 21 电量回复");
        if (level == 0xFF)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "BlackShark 无线链路尚未就绪");
        if (level > 100)
            return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "BlackShark 百分比超出范围");

        var power = await QueryAsync(RazerHeadsetQuery.Charging).ConfigureAwait(false);
        var charging = power switch { null or 0xFF => ChargingState.Unknown, 0 => ChargingState.NotCharging, _ => ChargingState.Charging };
        return new(target.Device, level.Value, false, charging, LinkState.Online, DateTimeOffset.UtcNow,
            Detail: charging == ChargingState.Unknown ? "BlackShark 2A 未确认充电状态" : null);

        async ValueTask<byte?> QueryAsync(RazerHeadsetQuery query)
        {
            var sequence = (byte)(Interlocked.Increment(ref nextSequence) & 0x1F);
            var request = RazerHeadsetProtocol.EncodeMediaTek(0x0565, query, sequence);
            var frame = await HidQuery.OutputAsync(session, session, request, RazerHeadsetProtocol.ReportLength,
                packet => RazerHeadsetProtocol.TryMediaTek(packet, request, 0x0565, out _), trace, token)
                .ConfigureAwait(false);
            return frame is not null && RazerHeadsetProtocol.TryMediaTek(frame, request, 0x0565, out byte value) ? value : null;
        }
    }

    private async ValueTask<BatteryReading> ReadBlackShark2020Async(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(
            endpoint with { RequiredFeatureReportId = RazerHeadsetProtocol.BlackShark2020ReportId }, token);
        var request = RazerHeadsetProtocol.EncodeBlackShark2020();
        await session.WriteFeatureAsync(request, token).ConfigureAwait(false);
        trace?.Invoke(FrameDirection.Sent, request);

        var response = new byte[RazerHeadsetProtocol.ReportLength];
        bool sawStale = false;
        for (int attempt = 0; attempt < MaxBlackShark2020Polls; attempt++)
        {
            if (featureSettle > TimeSpan.Zero)
                await Task.Delay(featureSettle, token).ConfigureAwait(false);
            Array.Clear(response);
            response[0] = RazerHeadsetProtocol.BlackShark2020ReportId;
            await session.ReadFeatureAsync(response, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Received, response);

            if (!RazerHeadsetProtocol.TryDecodeBlackShark2020(
                    response, out bool offline, out int? percentage, out var charging, out ushort millivolts))
                continue;
            if (!offline)
            {
                string? detail = charging == ChargingState.Charging
                    ? "充电中固件上报百分比不稳定，暂不显示具体数值"
                    : charging == ChargingState.Unknown
                        ? "BlackShark V2 Pro (2020) 未确认充电状态"
                        : null;
                return new(target.Device, percentage, percentage.HasValue, charging, LinkState.Online,
                    DateTimeOffset.UtcNow, Detail: detail);
            }
            if (millivolts == 0 && response[1] == 0x0F)
                return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                    Detail: "BlackShark V2 Pro (2020) 接收器报告耳机未连接（电压为 0）");
            sawStale = true;
        }

        if (sawStale)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "BlackShark V2 Pro (2020) 接收器未收到耳机新回复（关机或超出范围）");
        return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "BlackShark V2 Pro (2020) Feature FF 回复格式不符");
    }
}

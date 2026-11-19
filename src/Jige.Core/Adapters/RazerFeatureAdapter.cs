namespace Jige.Core.Adapters;

internal sealed class RazerFeatureAdapter : IDeviceAdapter
{
    private static RazerFeatureModel? Match(HidInterface endpoint)
    {
        var model = RazerFeatureProtocol.Find(endpoint.ProductId);
        return model is not null &&
            endpoint is { VendorId: 0x1532, UsagePage: >= 0xFF00, FeatureLength: RazerFeatureProtocol.ReportLength,
                MetadataIssue: ReadIssue.None } &&
            endpoint.InterfaceNumber == model.InterfaceNumber ? model : null;
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(hid => Match(hid) is not null)))
        {
            var model = Match(endpoint)!;
            yield return new(new($"razer-90b:{endpoint.ReceiverId}", $"Razer {model.Name}", model.Kind), endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "雷蛇 90B 接收器控制集合不匹配");

        using var session = transport.OpenHid(endpoint with { RequiredFeatureReportId = 0 }, token);
        var battery = await QueryAsync(RazerFeatureQuery.Battery).ConfigureAwait(false);
        if (battery.Status == 4)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "接收器报告无线设备未响应（07:80 状态 04）");
        if (battery.Status != 2)
        {
            var issue = battery.Status == 1 ? ReadIssue.Busy : ReadIssue.InvalidResponse;
            var detail = battery.Status == 0 ? "雷蛇 07:80 回复的事务、长度或校验不符" : $"雷蛇 07:80 状态 {battery.Status:X2}";
            return AdapterRegistry.Failure(target, issue, detail);
        }

        var charging = ChargingState.Unknown;
        string? chargingDetail = null;
        if (model.HasCharging)
        {
            try
            {
                var power = await QueryAsync(RazerFeatureQuery.Charging).ConfigureAwait(false);
                charging = power switch { (2, 0) => ChargingState.NotCharging, (2, 1) => ChargingState.Charging, _ => ChargingState.Unknown };
                if (charging == ChargingState.Unknown)
                    chargingDetail = $"雷蛇 07:84 未确认充电状态（状态 {power.Status:X2}，值 {power.Value:X2}）";
            }
            catch (TransportException error) when (error.Issue is not ReadIssue.Disconnected)
            {
                chargingDetail = error.Message;
            }
        }
        return new(target.Device, RazerFeatureProtocol.Percentage(battery.Value), false, charging,
            LinkState.Online, DateTimeOffset.UtcNow, Detail: chargingDetail);

        async ValueTask<(byte Status, byte Value)> QueryAsync(RazerFeatureQuery query)
        {
            var request = RazerFeatureProtocol.Encode(model.Pid, query);
            await session.WriteFeatureAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            // 等待无线往返后只读一次同 ID 的 Feature，便于区分忙、超时和不支持。
            await Task.Delay(60, token).ConfigureAwait(false);
            var response = new byte[RazerFeatureProtocol.ReportLength];
            await session.ReadFeatureAsync(response, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Received, response);
            return RazerFeatureProtocol.TryDecode(response, model, query, out byte status, out byte value)
                ? (status, value) : ((byte)0, (byte)0);
        }
    }
}

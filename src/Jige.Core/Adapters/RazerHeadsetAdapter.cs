namespace Jige.Core.Adapters;

internal sealed class RazerHeadsetAdapter : IDeviceAdapter
{
    private static int nextSequence;

    // PA 需切换远程模式，V3 系列冷启动有会话与断链风险。只注册 V2 HyperSpeed 纯查询路径。
    private static bool Matches(HidInterface hid) => hid is
    {
        VendorId: 0x1532, ProductId: 0x0565, UsagePage: 0xFF14, Usage: 1, InterfaceNumber: 3,
        InputLength: RazerHeadsetProtocol.ReportLength, OutputLength: RazerHeadsetProtocol.ReportLength,
        MetadataIssue: ReadIssue.None
    };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(Matches)))
            yield return new(new($"razer-headset:{endpoint.ReceiverId}", "Razer BlackShark V2 HyperSpeed", DeviceKind.Headset),
                endpoint, "0");
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !Matches(endpoint))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "BlackShark V2 HyperSpeed 控制集合不匹配");

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
}

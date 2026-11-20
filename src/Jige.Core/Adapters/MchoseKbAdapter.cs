namespace Jige.Core.Adapters;

internal static class MchoseKbProtocol
{
    public const ushort VendorId = 0x41E4;
    public const ushort K87ReceiverPid = 0x2001;
    public const int ReportLength = 20;
    public const byte ReportId = 0x13;
    public const byte Command = 0x4A;

    /// <summary>迈从 K87 2.4G 接收器（41E4:2001）纯只读电量查询（来源：HaloBattery mchose_kb.py MIT / M HUB）。</summary>
    public static byte[] EncodeBattery()
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = Command;
        frame[2] = 0x01;
        frame[5] = 0x02;
        return frame;
    }

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte value in data)
            sum += value;
        return unchecked((byte)sum);
    }

    public static bool TryDecodeBattery(ReadOnlySpan<byte> frame, out int level)
    {
        level = 0;
        if (frame.Length != ReportLength || frame[0] != ReportId || frame[1] != Command || frame[5] > 100)
            return false;
        if (frame[19] != 0 && Checksum(frame[..19]) != frame[19])
            return false;
        level = frame[5];
        return true;
    }
}

internal sealed class MchoseKbAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static bool Matches(HidInterface hid) => !DeviceCatalog.IsDisabledPath(hid, out _) && hid is
    {
        VendorId: MchoseKbProtocol.VendorId,
        ProductId: MchoseKbProtocol.K87ReceiverPid,
        UsagePage: 0xFF02,
        Usage: 0x0002,
        InputLength: MchoseKbProtocol.ReportLength,
        OutputLength: MchoseKbProtocol.ReportLength,
        MetadataIssue: ReadIssue.None
    };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(Matches)))
        {
            if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            {
                gates.RecordRejection(endpoint, 3, $"已拦截（{blockedReason}）");
                continue;
            }
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"mchose-kb:{endpoint.ReceiverId}", "MCHOSE K87", DeviceKind.Keyboard,
                AdaptationSource.Catalog, tier)
            {
                SourceLabel = "迈从无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !Matches(endpoint))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "迈从键盘控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        using var session = transport.OpenHid(
            endpoint with { RequiredInputReportId = MchoseKbProtocol.ReportId, RequiredOutputReportId = MchoseKbProtocol.ReportId }, token);
        var request = MchoseKbProtocol.EncodeBattery();
        var frame = await HidQuery.OutputAsync(session, session, request, MchoseKbProtocol.ReportLength,
            packet => MchoseKbProtocol.TryDecodeBattery(packet, out _), trace, token).ConfigureAwait(false);
        if (frame is null || !MchoseKbProtocol.TryDecodeBattery(frame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, ReadIssue.Timeout, "迈从接收器查询超时，键盘可能正在休眠");
        // HaloBattery 实机记录显示满电无线状态下状态位低 4 位仍为 1，不误报为充电中。
        return new(target.Device, level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !Matches(endpoint) ||
            gates?.IsBlocked(endpoint, out _) == true)
            return null;
        var reading = await ReadAsync(target, transport, null, token).ConfigureAwait(false);
        return new(endpoint.ReceiverId, null, target.Device.Id, reading.Link, reading.Issue)
        {
            Battery = reading.Link == LinkState.Online ? reading : null
        };
    }
}

namespace Jige.Core.Adapters;

internal sealed class LogitechHeadsetAdapter : IDeviceAdapter
{
    private static string? Model(ushort? pid) => pid switch
    {
        0x0AF7 => "Logitech G PRO X 2 LIGHTSPEED",
        0x0B18 => "Logitech G522 LIGHTSPEED",
        0x0B45 => "Logitech G PRO X 3 LIGHTSPEED",
        0x0B21 => "Logitech G325 LIGHTSPEED",
        0x0B1C => "ASTRO A50 Gen 5",
        _ => null
    };

    private static bool Matches(HidInterface hid) =>
        hid is { VendorId: 0x046D, InputLength: 64, OutputLength: 64, MetadataIssue: ReadIssue.None } &&
        Model(hid.ProductId) is not null && (hid.ProductId switch
        {
            0x0B1C => hid is { UsagePage: 0xFF32, Usage: 0x0074, InterfaceNumber: 8 },
            _ => hid is { UsagePage: 0xFFA0, Usage: 1, InterfaceNumber: 3 }
        });

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(Matches)))
            yield return new(new($"logitech-headset:{endpoint.ReceiverId}", Model(endpoint.ProductId)!, DeviceKind.Headset),
                endpoint, "0");
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !Matches(endpoint))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "罗技耳机控制集合不匹配");

        ushort pid = endpoint.ProductId!.Value;
        byte reportId = pid switch { 0x0B1C => 0x02, 0x0AF7 => 0x51, _ => 0x50 };
        using var session = transport.OpenHid(endpoint with
            { RequiredInputReportId = reportId, RequiredOutputReportId = reportId }, token);
        if (pid is 0x0B21 or 0x0B1C)
            return await ReadFixedAsync(LogitechHeadsetQuery.Battery).ConfigureAwait(false);

        var bridgeReply = await ReadCenturionAsync(LogitechHeadsetQuery.Bridge).ConfigureAwait(false);
        if (bridgeReply is not [var bridge, ..] || !LogitechHeadsetProtocol.ValidIndex(bridge))
            return Failed("Centurion 接收器未返回有效桥索引");

        var featureReply = await ReadCenturionAsync(LogitechHeadsetQuery.BatteryIndex, bridge).ConfigureAwait(false);
        if (featureReply is not [var index, ..])
            return Failed("Centurion 耳机未返回电量 feature 索引");
        if (index == 0 && pid == 0x0AF7)
            return await ReadFixedAsync(LogitechHeadsetQuery.LegacyBattery).ConfigureAwait(false);
        if (!LogitechHeadsetProtocol.ValidIndex(index))
            return Failed("Centurion 耳机未提供可用的 0104");

        var battery = await ReadCenturionAsync(LogitechHeadsetQuery.Battery, bridge, index).ConfigureAwait(false);
        if (battery is not [<= 100, _, _, ..])
            return Failed("Centurion 0104 未返回有效电量");
        return Reading(battery[0], LogitechHidppProtocol.Charging(battery[2]), $"Centurion 0104，状态 {battery[2]:X2}");

        async ValueTask<byte[]?> ReadCenturionAsync(LogitechHeadsetQuery query, byte bridgeIndex = 0, byte batteryIndex = 0)
        {
            var request = LogitechHeadsetProtocol.Encode(pid, query, bridgeIndex, batteryIndex);
            var frame = await HidQuery.OutputAsync(session, session, request, LogitechHeadsetProtocol.ReportLength,
                packet => LogitechHeadsetProtocol.TryCenturion(packet, pid, query, bridgeIndex, batteryIndex, out _),
                trace, token).ConfigureAwait(false);
            return frame is not null &&
                LogitechHeadsetProtocol.TryCenturion(frame, pid, query, bridgeIndex, batteryIndex, out var parameters)
                ? parameters.ToArray() : null;
        }

        async ValueTask<BatteryReading> ReadFixedAsync(LogitechHeadsetQuery query)
        {
            var frame = await HidQuery.OutputAsync(session, session, LogitechHeadsetProtocol.Encode(pid, query),
                LogitechHeadsetProtocol.ReportLength, packet => LogitechHeadsetProtocol.TryFixedBattery(packet, pid, out _, out _),
                trace, token).ConfigureAwait(false);
            return frame is not null && LogitechHeadsetProtocol.TryFixedBattery(frame, pid, out int level, out var charging)
                ? Reading(level, charging) : Failed("未收到匹配的耳机电量回复");
        }

        BatteryReading Failed(string detail) => AdapterRegistry.Failure(target, ReadIssue.Timeout, detail);
        BatteryReading Reading(int level, ChargingState charging, string? detail = null) =>
            new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow, Detail: detail);
    }
}

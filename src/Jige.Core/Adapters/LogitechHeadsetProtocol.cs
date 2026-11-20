namespace Jige.Core.Adapters;

internal enum LogitechHeadsetQuery { Bridge, BatteryIndex, Battery, LegacyBattery }

internal static class LogitechHeadsetProtocol
{
    public const int ReportLength = 64;

    private static ReadOnlySpan<byte> Prefix(ushort pid) => pid switch
    {
        0x0AF7 => [0x51],
        0x0B18 => [0x50, 0x23],
        _ => throw new ArgumentOutOfRangeException(nameof(pid))
    };

    public static byte[] Encode(ushort pid, LogitechHeadsetQuery query, byte bridge = 0, byte index = 0)
    {
        ReadOnlySpan<byte> bytes = (pid, query) switch
        {
            (0x0B1C, LogitechHeadsetQuery.Battery) => [0x02, 0x0C, 0x03, 0, 0x06, 0x0C],
            (0x0B21, LogitechHeadsetQuery.Battery) => [0x50, 0x23, 0x0B, 0, 3, 0x1A, 0, 3, 0, 7, 0x0A],
            (0x0AF7, LogitechHeadsetQuery.LegacyBattery) => [0x51, 8, 0, 3, 0x1A, 0, 3, 0, 4, 0x0A],
            (0x0AF7 or 0x0B18, _) => [],
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
        var report = new byte[ReportLength];
        if (!bytes.IsEmpty)
        {
            if (bridge != 0 || index != 0)
                throw new ArgumentOutOfRangeException(nameof(bridge));
            bytes.CopyTo(report);
            return report;
        }

        var prefix = Prefix(pid);
        prefix.CopyTo(report);
        ReadOnlySpan<byte> body = query switch
        {
            LogitechHeadsetQuery.Bridge when bridge == 0 && index == 0 => [5, 0, 0, 1, 0, 3],
            LogitechHeadsetQuery.BatteryIndex when ValidIndex(bridge) && index == 0 =>
                [10, 0, bridge, 0x11, 0, 5, 0, 0, 1, 1, 4],
            LogitechHeadsetQuery.Battery when ValidIndex(bridge) && ValidIndex(index) =>
                [8, 0, bridge, 0x11, 0, 3, 0, index, 1],
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
        body.CopyTo(report.AsSpan(prefix.Length));
        return report;
    }

    public static bool ValidIndex(byte value) => value is > 0 and < 0xF0;

    public static bool TryCenturion(ReadOnlySpan<byte> frame, ushort pid, LogitechHeadsetQuery query,
        byte bridge, byte index, out ReadOnlySpan<byte> parameters)
    {
        parameters = default;
        var prefix = Prefix(pid);
        if (frame.Length != ReportLength || !frame.StartsWith(prefix))
            return false;

        int length = frame[prefix.Length];
        int payloadStart = prefix.Length + 2;
        if (length < 1 || length - 1 > frame.Length - payloadStart || frame[prefix.Length + 1] != 0)
            return false;
        var data = frame.Slice(payloadStart, length - 1);
        if (query == LogitechHeadsetQuery.Bridge)
        {
            if (data.Length < 3 || data[0] != 0 || data[1] != 1)
                return false;
            parameters = data[2..];
            return true;
        }

        // 11 回显是桥的 ACK。实际内层回复由 10 事件承载，并回显内部索引和事务。
        if (query is not (LogitechHeadsetQuery.BatteryIndex or LogitechHeadsetQuery.Battery) || !ValidIndex(bridge))
            return false;
        int minimum = query == LogitechHeadsetQuery.BatteryIndex ? 8 : 10;
        if (data.Length < minimum || data[0] != bridge || data[1] != 0x10 ||
            data[2] != 0 || data[3] != data.Length - 4 || data[4] != 0 || data[5] != index || data[6] != 1)
            return false;
        parameters = data[7..];
        return true;
    }

    public static bool TryFixedBattery(ReadOnlySpan<byte> frame, ushort pid, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != ReportLength)
            return false;

        switch (pid)
        {
            case 0x0B21 when frame.StartsWith<byte>([0x50, 0x23, 0x0B, 0, 3, 0x10, 0, 6, 0, 7, 0x0A]):
                level = frame[12];
                charging = LegacyCharging(frame[13]);
                break;
            case 0x0AF7 when frame[0] == 0x51 && frame[1] == 0x0B && frame[8] == 4:
                level = frame[10];
                charging = LegacyCharging(frame[12]);
                break;
            case 0x0B1C when frame.StartsWith<byte>([0x02, 0x0C]) &&
                frame[2] is >= 6 and <= ReportLength - 3 && frame[3] == 0 && frame[4] == 6 && frame[5] is 0 or 0x0C:
                level = frame[6];
                charging = frame[8] switch { 0 => ChargingState.NotCharging, 1 => ChargingState.Charging, _ => ChargingState.Unknown };
                break;
            default:
                return false;
        }
        return level <= 100;
    }

    private static ChargingState LegacyCharging(byte status) => status switch
    {
        0 => ChargingState.NotCharging,
        2 => ChargingState.Charging,
        _ => ChargingState.Unknown
    };
}

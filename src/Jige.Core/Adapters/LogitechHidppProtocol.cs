namespace Jige.Core.Adapters;

internal enum HidppFeature : ushort { DeviceName = 0x0005, Battery = 0x1000, Voltage = 0x1001, Unified = 0x1004 }

internal static class LogitechHidppProtocol
{
    public const int LongLength = 20;
    public const int ShortLength = 7;

    public static byte[] Ping(byte slot, byte marker) => Encode(slot, 0, 1, 0, 0, marker);

    public static byte[] GetFeature(byte slot, HidppFeature feature)
    {
        if (feature is not (HidppFeature.DeviceName or HidppFeature.Battery or HidppFeature.Voltage or HidppFeature.Unified))
            throw new ArgumentOutOfRangeException(nameof(feature));
        // Root 回复不回显 feature ID。不同查表请求使用不同 software ID，隔离超时后的迟到回复。
        byte softwareId = feature switch
        {
            HidppFeature.Unified => 0x0A,
            HidppFeature.DeviceName => 0x0B,
            HidppFeature.Battery => 0x0C,
            _ => 0x0D
        };
        return Encode(slot, 0, 0, (byte)((ushort)feature >> 8), (byte)feature, softwareId: softwareId);
    }

    // 动态索引只从同一接收器同一槽的 IRoot 回复获取。参数和功能号按 feature 限制。
    public static byte[] ReadFeature(byte slot, HidppFeature feature, byte index, byte function, byte offset = 0)
    {
        bool allowed = feature switch
        {
            HidppFeature.DeviceName => function == 1 ? offset != byte.MaxValue : function is 0 or 2 && offset == 0,
            HidppFeature.Unified => function <= 1 && offset == 0,
            HidppFeature.Battery or HidppFeature.Voltage => function == 0 && offset == 0,
            _ => false
        };
        if (!allowed || !IsFeatureIndex(index))
            throw new ArgumentOutOfRangeException(nameof(function), "HID++ 查询超出只读白名单");
        return Encode(slot, index, function, offset);
    }

    // 排除 HID++ 1.0 寄存器子命令及保留索引，防止设备换槽后误解释电量读取。
    public static bool IsFeatureIndex(byte index) => index is > 0 and < 0xF0 and not (>= 0x80 and <= 0x8F);

    private static byte[] Encode(byte slot, byte index, byte function, byte p0, byte p1 = 0, byte p2 = 0, byte softwareId = 0x0A)
    {
        if (slot is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var report = new byte[LongLength];
        report[0] = 0x11;
        report[1] = slot;
        report[2] = index;
        report[3] = (byte)((function << 4) | softwareId);
        report[4] = p0;
        report[5] = p1;
        report[6] = p2;
        return report;
    }

    public static byte[] ReadPairing(byte slot, ushort pid)
    {
        if (slot is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(slot));
        byte selector = pid switch
        {
            0xC548 => (byte)(0x50 + slot),
            0xC52B or 0xC532 => (byte)(0x20 + slot - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(pid))
        };
        return [0x10, 0xFF, 0x83, 0xB5, selector, 0, 0];
    }

    public static bool Matches(ReadOnlySpan<byte> report, ReadOnlySpan<byte> request)
    {
        if (!((report.Length == LongLength && report[0] == 0x11) ||
            (report.Length is ShortLength or LongLength && report[0] == 0x10)) || report[1] != request[1])
            return false;
        if (report[2] is 0x8F or 0xFF)
            return report[3] == request[2] && report[4] == request[3];
        if (report[2] != request[2] || report[3] != request[3])
            return false;
        if (request[2] == 0x83 && request[1] == 0xFF)
            return report[0] == 0x11 && report[4] == request[4];
        return request[2] != 0 || request[3] != 0x1A || report[6] == request[6];
    }

    public static bool IsSuccess(byte[]? frame) => frame is not null && frame[2] is not (0x8F or 0xFF);

    public static DeviceKind NameKind(byte type) => type switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 5 => DeviceKind.Mouse,
        _ => DeviceKind.Other
    };

    public static DeviceKind PairingKind(byte type) => type switch
    {
        1 or 3 => DeviceKind.Keyboard,
        2 or 8 => DeviceKind.Mouse,
        0x0D => DeviceKind.Headset,
        _ => DeviceKind.Other
    };

    public static ChargingState Charging(byte status, bool legacy = false) => status switch
    {
        0 or 3 => ChargingState.NotCharging,
        1 or 2 => ChargingState.Charging,
        4 when legacy => ChargingState.Charging,
        _ => ChargingState.Unknown
    };

    public static int? EstimateVoltage(int millivolts)
    {
        // 未掌握适用的型号曲线。仅对常见单节锂电的 3.5–4.2 V 工作区间线性估算。
        return millivolts is >= 3500 and <= 4200 ? ((millivolts - 3500) * 100 + 350) / 700 : null;
    }
}

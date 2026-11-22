namespace Jige.Core.Adapters;

internal enum HidppFeature : ushort { DeviceInfo = 0x0003, DeviceName = 0x0005, Battery = 0x1000, Voltage = 0x1001, Unified = 0x1004, Adc = 0x1F20 }

internal static class LogitechHidppProtocol
{
    public const int LongLength = 20;
    public const int ShortLength = 7;

    public static byte[] Ping(byte slot, byte marker) => Encode(slot, 0, 1, 0, 0, marker);

    public static byte[] GetFeature(byte slot, HidppFeature feature)
    {
        if (feature is not (HidppFeature.DeviceInfo or HidppFeature.DeviceName or HidppFeature.Battery or HidppFeature.Voltage or HidppFeature.Unified or HidppFeature.Adc))
            throw new ArgumentOutOfRangeException(nameof(feature));
        // Root 回复不回显 feature ID。不同查表请求使用不同 software ID，隔离超时后的迟到回复。
        byte softwareId = feature switch
        {
            HidppFeature.Unified => 0x0A,
            HidppFeature.DeviceName => 0x0B,
            HidppFeature.Battery => 0x0C,
            HidppFeature.DeviceInfo => 0x0E,
            HidppFeature.Adc => 0x0F,
            _ => 0x0D
        };
        return Encode(slot, 0, 0, (byte)((ushort)feature >> 8), (byte)feature, softwareId: softwareId);
    }

    // 动态索引只从同一接收器同一槽的 IRoot 回复获取。参数和功能号按 feature 限制。
    public static byte[] ReadFeature(byte slot, HidppFeature feature, byte index, byte function, byte offset = 0)
    {
        bool allowed = feature switch
        {
            HidppFeature.DeviceName => function == 1 ? offset < 64 : function is 0 or 2 && offset == 0,
            HidppFeature.Unified => function <= 1 && offset == 0,
            HidppFeature.DeviceInfo or HidppFeature.Battery or HidppFeature.Voltage or HidppFeature.Adc => function == 0 && offset == 0,
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
        if (!IsDeviceAddress(slot))
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

    public static bool IsDeviceAddress(byte address) => address is >= 1 and <= 6 or 0xFF;

    public static byte[] ReadRegister(byte slot, byte register) => EncodeRegister(slot, 0x81, register);

    internal static byte[] EncodeRegister(byte slot, byte subId, byte register)
    {
        if (!IsDeviceAddress(slot) || subId != 0x81 ||
            !(register is 0x07 or 0x0D || register == 0xB5 && slot == 0xFF))
            throw new ArgumentOutOfRangeException(nameof(register), "HID++ 寄存器查询超出只读白名单");
        return [0x10, slot, subId, register, 0, 0, 0];
    }

    public static bool HasReportShape(ReadOnlySpan<byte> report) =>
        (report.Length == LongLength && report[0] == 0x11) ||
        (report.Length is ShortLength or LongLength && report[0] == 0x10);

    public static byte? Error(byte[]? report) =>
        report is { Length: >= 7 } && report[2] is 0x8F or 0xFF ? report[5] : null;

    public static byte? LegacyError(byte[]? report) =>
        report is { Length: >= 7 } && report[2] == 0x8F ? report[5] : null;

    public static bool IsUnreachable(byte[]? report) => LegacyError(report) is 0x04 or 0x09;

    public static bool IsPing(ReadOnlySpan<byte> request) => request[2] == 0 && request[3] == 0x1A;

    public static bool TryRegisterBattery(ReadOnlySpan<byte> reply, byte register,
        out int percentage, out bool estimated, out ChargingState charging)
    {
        percentage = 0;
        estimated = register == 0x07;
        charging = ChargingState.Unknown;
        if (!HasReportShape(reply) || reply[2] != 0x81 || reply[3] != register)
            return false;
        if (register == 0x0D)
        {
            percentage = reply[4];
            // HID++ 1.0 BATTERY_MILEAGE (0x0D)：reply[6] 高 2 位为充放电状态（0 放电、1 充电、2 充满、3 错误），
            // bit 5..4 为剩余续航时间单位（秒/分/时/天），不能用 & 0xF0 写死时间单位，否则低电量单位切为小时/分钟时误丢有效电量。
            charging = (reply[6] >> 6) switch
            {
                0 => ChargingState.NotCharging,
                1 => ChargingState.Charging,
                2 => ChargingState.Full,
                _ => ChargingState.Unknown
            };
            return percentage <= 100 && charging != ChargingState.Unknown;
        }
        if (register != 0x07)
            return false;
        percentage = reply[4] switch { 7 => 100, 5 => 50, 3 => 20, 1 => 5, _ => -1 };
        charging = (reply[5] & 0x21) == 0x21 ? ChargingState.Charging :
            (reply[5] & 0x22) == 0x22 ? ChargingState.Full :
            reply[5] == 0 ? ChargingState.NotCharging : ChargingState.Unknown;
        return percentage >= 0;
    }

    public static bool Matches(ReadOnlySpan<byte> report, ReadOnlySpan<byte> request)
    {
        if (!HasReportShape(report) || report[1] != request[1])
            return false;
        if (report[2] is 0x8F or 0xFF)
            return report[3] == request[2] && report[4] == request[3];
        if (report[2] != request[2] || report[3] != request[3])
            return false;
        return !IsPing(request) || report[6] == request[6];
    }

    public static bool IsSuccess(byte[]? frame) => frame is not null && frame[2] is not (0x8F or 0xFF);

    /// <summary>
    /// 解析罗技接收器被动推送的连接/断开事件（HID++ 1.0 sub_id 0x41 / 0x40，只读监听）。
    /// 参考 Solaar notifications.py 与 Linux hid-logitech-hidpp.c：0x41 载荷字节 4 的 bit6 (0x40) 置位表示链路断开。
    /// </summary>
    public static bool TryParseConnectionEvent(ReadOnlySpan<byte> report, out byte slot, out bool connected)
    {
        slot = 0;
        connected = false;
        if (!HasReportShape(report) || report[0] != 0x10 || report[1] is < 1 or > 6)
            return false;
        if (report[2] == 0x40)
        {
            slot = report[1];
            connected = false;
            return true;
        }
        if (report[2] == 0x41 && report[3] > 0)
        {
            slot = report[1];
            connected = (report[4] & 0x40) == 0;
            return true;
        }
        return false;
    }

    public static DeviceKind NameKind(byte type) => type switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 5 => DeviceKind.Mouse,
        8 => DeviceKind.Headset,
        _ => DeviceKind.Other
    };

    public static ChargingState Charging(byte status, bool legacy = false) => status switch
    {
        0 => ChargingState.NotCharging,
        3 => ChargingState.Full,
        1 or 2 => ChargingState.Charging,
        4 when legacy => ChargingState.Charging,
        _ => ChargingState.Unknown
    };

    public static int? EstimateVoltage(int millivolts)
    {
        // 未掌握适用的型号曲线。对单节锂电 3.5–4.2 V 区间线性估算；
        // 充电末端常见 4.21–4.38 V 钳位至 100%，极低电 3.20–3.49 V 钳位至 0%，拒绝干电池或异常电压。
        if (millivolts is < 3200 or > 4380)
            return null;
        if (millivolts <= 3500)
            return 0;
        if (millivolts >= 4200)
            return 100;
        return ((millivolts - 3500) * 100 + 350) / 700;
    }
}

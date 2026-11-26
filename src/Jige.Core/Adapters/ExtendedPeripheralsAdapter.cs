namespace Jige.Core.Adapters;

internal enum ExtendedProtocolKind
{
    PulsarSonix,
    Eweadn30,
    AjazzV2Max,
    AjazzGearHubV5,
    MchoseV3_4D,
    AulaF87Kb,
    KeychronB3,
    AudezeMaxwell,
    Scyrox2E
}

internal sealed record ExtendedPeripheralModel(
    ushort VendorId,
    ushort ProductId,
    string Name,
    DeviceKind Kind,
    ExtendedProtocolKind Protocol,
    string Source,
    DeviceConnection Connection = DeviceConnection.Receiver,
    byte? InterfaceNumber = null);

/// <summary>
/// 扩展品牌只读电量协议集合：Pulsar Sonix、EWEADN 前行者、AJAZZ 黑爵、MCHOSE V3 4D、AULA 狼蛛键盘、Keychron、Audeze Maxwell、Scyrox 0x395A。
/// 每条协议均有严格白名单，绝不发送任何设置/配对/模式切换命令。
/// </summary>
internal static class ExtendedPeripheralProtocol
{
    public const ushort ScyroxVendorId = 0x395A;
    public const int ScyroxReportLength = 64;

    private static readonly byte[] AudezeBatteryOnlyPrefix = [0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];
    private static readonly byte[] AudezeBatteryMarker = [0xD6, 0x0C, 0x00, 0x00];
    private static readonly byte[] MchoseV3Prefix = [0x4D, 0x01, 0x01, 0x00, 0x00, 0x09, 0x00, 0x00, 0x08];

    private static readonly ExtendedPeripheralModel[] Models =
    [
        // Pulsar X2 V3 Mini（Sonix 65B Feature）
        new(0x3710, 0x5403, "Pulsar X2 V3 Mini", DeviceKind.Mouse, ExtendedProtocolKind.PulsarSonix,
            "pulsar-x3-python / HaloBattery（MIT）", DeviceConnection.Receiver, 3),
        new(0x3710, 0x3402, "Pulsar X2 V3 Mini", DeviceKind.Mouse, ExtendedProtocolKind.PulsarSonix,
            "pulsar-x3-python / HaloBattery（MIT）", DeviceConnection.UsbCable, 3),

        // EWEADN 前行者 S9 / H2（33B Output/Input 命令 0x30）
        new(0x089D, 0x062F, "EWEADN S9", DeviceKind.Mouse, ExtendedProtocolKind.Eweadn30,
            "EWEADN E HUB / HaloBattery（MIT）"),
        new(0x088D, 0x062E, "EWEADN S9", DeviceKind.Mouse, ExtendedProtocolKind.Eweadn30,
            "EWEADN E HUB / HaloBattery（MIT）", DeviceConnection.UsbCable),

        // AJAZZ 黑爵 AJ179 V2 MAX（33B Output/Input 命令 0x10 与 0xC0 推送）
        new(0x249A, 0x5C2F, "AJAZZ AJ179 V2 MAX", DeviceKind.Mouse, ExtendedProtocolKind.AjazzV2Max,
            "aj179-linux（MIT）/ HaloBattery（MIT）", DeviceConnection.Receiver, 2),
        new(0x248A, 0x5D2E, "AJAZZ AJ179 V2 MAX", DeviceKind.Mouse, ExtendedProtocolKind.AjazzV2Max,
            "aj179-linux（MIT）/ HaloBattery（MIT）", DeviceConnection.UsbCable, 2),

        // AJAZZ 黑爵 AJ179 Pro（GearHub V5 65B Feature GET_STATUS 0xF7）
        new(0x3151, 0x402D, "AJAZZ AJ179 Pro", DeviceKind.Mouse, ExtendedProtocolKind.AjazzGearHubV5,
            "OpenMouse GearHub V5 实机记录（research/domestic-mice-webdriver.md）"),

        // MCHOSE 迈从 V3 共享接收器（64B Output/Input Report ID 4D）
        new(0x3837, 0x1014, "MCHOSE V3 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.MchoseV3_4D,
            "OpenMouse MCHOSE V3（research/domestic-mice-webdriver.md）"),
        new(0x3837, 0x1016, "MCHOSE V3 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.MchoseV3_4D,
            "OpenMouse MCHOSE V3（research/domestic-mice-webdriver.md）"),
        new(0x3837, 0x1018, "MCHOSE V3 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.MchoseV3_4D,
            "OpenMouse MCHOSE V3（research/domestic-mice-webdriver.md）"),

        // AULA 狼蛛 F87 Pro V2C 2.4G 键盘（FF80:0001 33B AA 10 -> 55 10）
        new(0x0C45, 0xFEFC, "AULA F87 Pro V2C", DeviceKind.Keyboard, ExtendedProtocolKind.AulaF87Kb,
            "AULA HUB / HaloBattery（MIT）"),

        // Keychron 无线接收器与鼠标（64B Feature B3 06 -> Input B4 06）
        new(0x3434, 0xD028, "Keychron Ultra-Link 8K", DeviceKind.Mouse, ExtendedProtocolKind.KeychronB3,
            "keychron-battery-dkms / HaloBattery（MIT）", DeviceConnection.Receiver, 4),
        new(0x3434, 0xD048, "Keychron M5", DeviceKind.Mouse, ExtendedProtocolKind.KeychronB3,
            "keychron-battery-dkms / HaloBattery（MIT）", DeviceConnection.UsbCable, 4),
        new(0x3434, 0xD026, "Keychron Nape Pro", DeviceKind.Mouse, ExtendedProtocolKind.KeychronB3,
            "HaloBattery（MIT）", DeviceConnection.Receiver, 1),

        // Audeze Maxwell（仅发单帧严格只读 BATTERY_ONLY 0x0CD6，绝不发送未核实写位的初始化序列）
        new(0x3329, 0x4B19, "Audeze Maxwell", DeviceKind.Headset, ExtendedProtocolKind.AudezeMaxwell,
            "HeadsetControl / HaloBattery（MIT）"),
        new(0x3329, 0x4B18, "Audeze Maxwell (Xbox)", DeviceKind.Headset, ExtendedProtocolKind.AudezeMaxwell,
            "HeadsetControl / HaloBattery（MIT）"),
        new(0x3329, 0x4B1A, "Audeze Maxwell", DeviceKind.Headset, ExtendedProtocolKind.AudezeMaxwell,
            "HeadsetControl / HaloBattery（MIT）", DeviceConnection.UsbCable),
        new(0x3329, 0x4B1E, "Audeze Maxwell (Xbox)", DeviceKind.Headset, ExtendedProtocolKind.AudezeMaxwell,
            "HeadsetControl / HaloBattery（MIT）", DeviceConnection.UsbCable),
        new(0x3329, 0x4B29, "Audeze Maxwell 2", DeviceKind.Headset, ExtendedProtocolKind.AudezeMaxwell,
            "HeadsetControl / HaloBattery（MIT）"),
        new(0x3329, 0x4B28, "Audeze Maxwell 2 (Xbox)", DeviceKind.Headset, ExtendedProtocolKind.AudezeMaxwell,
            "HeadsetControl / HaloBattery（MIT）"),

        // Scyrox 新代 0x395A（64B Output/Input 只读状态查询 0x2E 00 03；出处：Scyrox 官方网页驱动 gaming.scyrox.net）
        new(ScyroxVendorId, 0x0002, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x0004, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x0006, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x0008, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x000A, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x000C, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x000E, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x0010, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）"),
        new(ScyroxVendorId, 0x0003, "Scyrox V12L", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable),
        new(ScyroxVendorId, 0x0005, "Scyrox V12M", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable),
        new(ScyroxVendorId, 0x0007, "Scyrox V12L-U", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable),
        new(ScyroxVendorId, 0x0009, "Scyrox V12M-U", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable),
        new(ScyroxVendorId, 0x000B, "Scyrox V6S", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable),
        new(ScyroxVendorId, 0x000D, "Scyrox V8S", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable),
        new(ScyroxVendorId, 0x000F, "Scyrox 鼠标", DeviceKind.Mouse, ExtendedProtocolKind.Scyrox2E,
            "Scyrox 官方网页驱动（gaming.scyrox.net）", DeviceConnection.UsbCable)
    ];

    public static IReadOnlyList<ExtendedPeripheralModel> AllModels => Models;

    public static ExtendedPeripheralModel? Find(ushort? vid, ushort? pid) =>
        vid is ushort v && pid is ushort p ? Array.Find(Models, m => m.VendorId == v && m.ProductId == p) : null;

    public static byte[] EncodeRequest(ExtendedProtocolKind protocol, int length)
    {
        var frame = new byte[length];
        switch (protocol)
        {
            case ExtendedProtocolKind.PulsarSonix:
                frame[0] = 0x00;
                frame[1] = 0x00;
                frame[2] = 0x08;
                frame[3] = 0x81;
                frame[4] = 0x01;
                frame[63] = 0x8A; // sum(wire[0..61]) = 0x08 + 0x81 + 0x01 = 0x008A (LE)
                frame[64] = 0x00;
                break;
            case ExtendedProtocolKind.Eweadn30:
                frame[0] = 0x00;
                frame[1] = 0x30;
                break;
            case ExtendedProtocolKind.AjazzV2Max:
                frame[0] = 0x00;
                frame[1] = 0x10;
                break;
            case ExtendedProtocolKind.AjazzGearHubV5:
                frame[0] = 0x00;
                frame[1] = 0xF7;
                break;
            case ExtendedProtocolKind.MchoseV3_4D:
                MchoseV3Prefix.CopyTo(frame, 0);
                break;
            case ExtendedProtocolKind.AulaF87Kb:
                frame[0] = 0x00;
                frame[1] = 0xAA;
                frame[2] = 0x10;
                break;
            case ExtendedProtocolKind.KeychronB3:
                frame[0] = 0xB3;
                frame[1] = 0x06;
                break;
            case ExtendedProtocolKind.AudezeMaxwell:
                AudezeBatteryOnlyPrefix.CopyTo(frame, 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol));
        }
        return frame;
    }

    private static int MinRequestLength(ExtendedProtocolKind protocol) => protocol switch
    {
        ExtendedProtocolKind.PulsarSonix => 65,
        ExtendedProtocolKind.MchoseV3_4D or ExtendedProtocolKind.AudezeMaxwell => 9,
        ExtendedProtocolKind.AulaF87Kb => 3,
        _ => 2
    };

    public static bool AllowsRequest(ExtendedProtocolKind protocol, ReadOnlySpan<byte> frame) =>
        frame.Length >= MinRequestLength(protocol) && frame.SequenceEqual(EncodeRequest(protocol, frame.Length));

    public static bool TryDecodePulsarSonix(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length < 8 || r[2] != 0x08 || r[3] != 0x81 || r[4] != 0x01 || r[7] > 100)
            return false;
        // 拒绝原样请求回显（含完整 65B 特征回显，以及尾部校验为 0x8A 0x00 且 [5..7] 全零的回显）及无载荷零填充头。
        if (r.Length >= 65 && (AllowsRequest(ExtendedProtocolKind.PulsarSonix, r) ||
            (r[5] == 0 && r[6] == 0 && r[7] == 0 && r[63] == 0x8A && r[64] == 0x00)))
            return false;
        if (r[7] == 0 && !r[5..].ContainsAnyExcept((byte)0))
            return false;
        level = r[7];
        return true;
    }

    public static bool TryDecodeEweadn(ReadOnlySpan<byte> raw, out bool asleep, out int level, out ChargingState charging, out string? resolvedName)
    {
        asleep = false;
        level = 0;
        charging = ChargingState.Unknown;
        resolvedName = null;
        if (raw.Length < 15)
            return false;
        var r = raw.Length == 33 && raw[0] == 0x00 ? raw[1..] : raw;
        if (r.Length < 15)
            return false;
        if (r[0] == 0x02)
        {
            asleep = true;
            return true;
        }
        if (r[0] != 0x30 || r[1] != 0x00 || r[13] > 100 || !r[1..].ContainsAnyExcept((byte)0))
            return false;
        level = r[13];
        charging = r[12] != 0 ? ChargingState.Charging : ChargingState.NotCharging;
        if (r[4..8].SequenceEqual("M156"u8))
            resolvedName = "EWEADN S9";
        else if (r[4..8].SequenceEqual("M302"u8))
            resolvedName = "EWEADN H2";
        return true;
    }

    public static bool TryDecodeAjazzV2Max(ReadOnlySpan<byte> raw, bool wired, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (raw.Length < 32)
            return false;
        var r = raw.Length == 33 && raw[0] == 0x00 ? raw[1..] : raw;
        if (r.Length < 32)
            return false;

        if (r[0] == 0xC0)
        {
            if (r[1] != 0x01 || r[2] is 0 or > 100)
                return false;
            level = r[2];
            charging = ChargingState.Unknown;
            return true;
        }
        if (r[0] != 0x10 || r[1] != 0x00 || r[2] != 0x01 || r[3] != 0x0B)
            return false;
        int sum = 0;
        for (int i = 4; i < 31; i++)
            sum += r[i];
        if ((byte)sum != r[31] || r[13] is 0 or > 100)
            return false;
        level = r[13];
        charging = wired && r[14] == 0x00 ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool TryDecodeAjazzGearHubV5(ReadOnlySpan<byte> r, out bool asleep, out int level)
    {
        asleep = false;
        level = 0;
        if (r.Length < 6 || r[0] != 0x00)
            return false;
        if (!r[1..].ContainsAnyExcept((byte)0) || !r[1..].ContainsAnyExcept(byte.MaxValue) || r[1] == 0xF7)
        {
            asleep = true;
            return true;
        }
        if (r[5] != 0)
        {
            asleep = true;
            return true;
        }
        if (r[3] is 0 or > 100)
            return false;
        level = r[3];
        return true;
    }

    public static bool TryDecodeMchoseV3(ReadOnlySpan<byte> r, out bool asleep, out int level, out ChargingState charging)
    {
        asleep = false;
        level = 0;
        charging = ChargingState.Unknown;
        if (r.Length < 22 || r[0] != 0x4D || r[5] != 0x09 || r[6] != 0x00)
            return false;
        int dataLen = r[7];
        if (dataLen < 13 || 8 + dataLen >= r.Length)
            return false;
        byte xor = 0;
        for (int i = 2; i < 8 + dataLen; i++)
            xor ^= r[i];
        if (xor != r[8 + dataLen])
            return false;
        if (r[18] == 0)
        {
            asleep = true;
            return true;
        }
        if (r[20] > 100)
            return false;
        level = r[20];
        charging = MouseBatteryProtocol.Charge(r[19]);
        return true;
    }

    public static bool TryDecodeAulaF87(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length > 26 && r[0] == 0x00 && r[1] == 0x55 && r[2] == 0x10 && r[26] is >= 1 and <= 100)
        {
            level = r[26];
            return true;
        }
        if (r.Length > 25 && r[0] == 0x55 && r[1] == 0x10 && r[25] is >= 1 and <= 100)
        {
            level = r[25];
            return true;
        }
        return false;
    }

    public static bool TryDecodeKeychron(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length <= 20 || r[0] != 0xB4 || r[1] != 0x06 || r[20] > 100)
            return false;
        level = r[20];
        return true;
    }

    public static bool TryDecodeAudeze(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length < 6 || r[0] != 0x07)
            return false;
        int start = 0;
        while (start <= r.Length - 5)
        {
            int idx = r[start..].IndexOf(AudezeBatteryMarker);
            if (idx < 0)
                return false;
            int pos = start + idx;
            if (pos + 4 >= r.Length)
                return false;
            byte candidate = r[pos + 4];
            if (candidate <= 100)
            {
                level = candidate;
                return true;
            }
            start = pos + 1;
        }
        return false;
    }

    public static bool NeedsReportIds(ushort? vid, ushort? pid, ushort usagePage, ushort inputLength, ushort outputLength) =>
        vid == ScyroxVendorId && Find(vid, pid) is { Protocol: ExtendedProtocolKind.Scyrox2E } &&
        usagePage == 0xFF00 && inputLength == ScyroxReportLength && outputLength == ScyroxReportLength;

    public static bool TryResolveSingleReportId(string? declaredIds, out byte reportId)
    {
        reportId = 0;
        if (string.IsNullOrWhiteSpace(declaredIds))
            return false;
        if (declaredIds.Equals("00(raw)", StringComparison.OrdinalIgnoreCase))
            return true;
        if (declaredIds.Contains(','))
            return false;
        return byte.TryParse(declaredIds.Trim(), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out reportId);
    }

    public static bool TryResolveScyroxReportIds(HidInterface hid, out byte inputReportId, out byte outputReportId)
    {
        inputReportId = 0;
        outputReportId = 0;
        return TryResolveSingleReportId(hid.InputReportIds, out inputReportId) &&
               TryResolveSingleReportId(hid.OutputReportIds, out outputReportId);
    }

    /// <summary>
    /// 构造 Scyrox 0x395A 64B 只读状态查询帧（[rid, 0x2E, 0x00, 0x03, 0x00×59, sum]）。
    /// 校验和按官方网页驱动 gaming.scyrox.net 规则为 (reportId + payload 字节累加) &amp; 0xFF。
    /// </summary>
    public static byte[] EncodeScyrox2E(byte outputReportId)
    {
        var frame = new byte[ScyroxReportLength];
        frame[0] = outputReportId;
        frame[1] = 0x2E;
        frame[2] = 0x00;
        frame[3] = 0x03;
        frame[63] = unchecked((byte)(outputReportId + 0x2E + 0x00 + 0x03));
        return frame;
    }

    public static bool AllowsScyrox2E(ReadOnlySpan<byte> frame) =>
        frame.Length == ScyroxReportLength && frame.SequenceEqual(EncodeScyrox2E(frame[0]));

    /// <summary>依据 gaming.scyrox.net 的 mouseNameMap 将 0x2E 回复中的 linkPid 映射为具体型号。</summary>
    public static string? LookupScyroxModel(ushort linkPid) => linkPid switch
    {
        0x0003 => "Scyrox V12L",
        0x0005 => "Scyrox V12M",
        0x0007 => "Scyrox V12L-U",
        0x0009 => "Scyrox V12M-U",
        0x000B => "Scyrox V6S",
        0x000D => "Scyrox V8S",
        _ => null
    };

    /// <summary>
    /// 解析 Scyrox 0x395A 64B 线帧回复（raw[0]=Input Report ID，raw[1..63] 对应 WebHID payload[0..62]）：
    /// raw[1]=0x2E，raw[4]=电量(0..100)，raw[5]=充电(0/1)，raw[6]=连接(0/1)，raw[12..15]=linkVid/linkPid(LE)，raw[62]=错误标志(1)。
    /// 不读取 raw[16..28] 中的设备序列号。
    /// </summary>
    public static bool TryDecodeScyrox2E(
        ReadOnlySpan<byte> raw, byte expectedInputReportId,
        out bool asleep, out int level, out ChargingState charging, out string? resolvedName)
    {
        asleep = false;
        level = 0;
        charging = ChargingState.Unknown;
        resolvedName = null;
        if (raw.Length != ScyroxReportLength || raw[0] != expectedInputReportId || raw[1] != 0x2E)
            return false;
        if (raw.SequenceEqual(EncodeScyrox2E(raw[0])))
            return false;
        if (raw[62] == 1)
        {
            asleep = true;
            return true;
        }
        if (raw[62] != 0 || raw[5] > 1 || raw[6] > 1)
            return false;
        if (raw[6] == 0)
        {
            asleep = true;
            return true;
        }
        if (raw[4] > 100)
            return false;
        level = raw[4];
        charging = MouseBatteryProtocol.Charge(raw[5]);
        ushort linkVid = (ushort)(raw[12] | (raw[13] << 8));
        ushort linkPid = (ushort)(raw[14] | (raw[15] << 8));
        if (linkVid is 0 or ScyroxVendorId)
            resolvedName = LookupScyroxModel(linkPid);
        return true;
    }

    public static string FamilyName(ExtendedProtocolKind protocol) => protocol switch
    {
        ExtendedProtocolKind.PulsarSonix => "Pulsar Sonix Feature",
        ExtendedProtocolKind.Eweadn30 => "EWEADN 30",
        ExtendedProtocolKind.AjazzV2Max => "AJAZZ 10/C0",
        ExtendedProtocolKind.AjazzGearHubV5 => "GearHub V5 Feature",
        ExtendedProtocolKind.MchoseV3_4D => "MCHOSE V3 4D",
        ExtendedProtocolKind.AulaF87Kb => "AULA AA10 键盘",
        ExtendedProtocolKind.KeychronB3 => "Keychron B3/B4",
        ExtendedProtocolKind.AudezeMaxwell => "Audeze 0CD6",
        ExtendedProtocolKind.Scyrox2E => "Scyrox 2E (64B)",
        _ => "扩展外设"
    };
}

internal sealed class ExtendedPeripheralsAdapter(SafetyGateStore? gates = null, TimeSpan? featureDelay = null)
    : IDeviceAdapter, IPresenceAdapter
{
    private readonly TimeSpan settle = featureDelay ?? TimeSpan.FromMilliseconds(50);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static ExtendedPeripheralModel? Match(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _) || hid.MetadataIssue != ReadIssue.None)
            return null;
        var model = ExtendedPeripheralProtocol.Find(hid.VendorId, hid.ProductId);
        if (model is null)
            return null;
        if (model.InterfaceNumber is byte mi && hid.InterfaceNumber != mi)
            return null;

        return model.Protocol switch
        {
            ExtendedProtocolKind.PulsarSonix => hid is { UsagePage: >= 0xFF00, FeatureLength: 65 } ? model : null,
            ExtendedProtocolKind.Eweadn30 => hid is { UsagePage: 0xFF01, Usage: 0x0010, InputLength: 33, OutputLength: 33 } ? model : null,
            ExtendedProtocolKind.AjazzV2Max => hid is { UsagePage: >= 0xFF00, InputLength: 33, OutputLength: 33 } ? model : null,
            ExtendedProtocolKind.AjazzGearHubV5 => hid is { UsagePage: 0xFFFF, Usage: 0x0002, FeatureLength: 65 } ? model : null,
            ExtendedProtocolKind.MchoseV3_4D => hid is { UsagePage: 0xFF01, Usage: 0x0001, InputLength: 64, OutputLength: 64 } ? model : null,
            ExtendedProtocolKind.AulaF87Kb => hid is { UsagePage: 0xFF80, Usage: 0x0001, InputLength: 33, OutputLength: 33 } ? model : null,
            ExtendedProtocolKind.KeychronB3 => hid is { UsagePage: >= 0xFF00, FeatureLength: >= 64, InputLength: >= 64 } ? model : null,
            ExtendedProtocolKind.AudezeMaxwell => hid is { UsagePage: 0xFF13, Usage: 0x0001, InputLength: 62, OutputLength: 62 } ? model : null,
            ExtendedProtocolKind.Scyrox2E => hid is { UsagePage: 0xFF00, InputLength: ExtendedPeripheralProtocol.ScyroxReportLength, OutputLength: ExtendedPeripheralProtocol.ScyroxReportLength } &&
                ExtendedPeripheralProtocol.TryResolveScyroxReportIds(hid, out _, out _) ? model : null,
            _ => null
        };
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(hid => Match(hid) is not null)))
        {
            if (gates?.IsBlocked(endpoint, out _) == true)
                continue;
            var model = Match(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"ext:{endpoint.ReceiverId}", model.Name, model.Kind,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "USB 充电线" : "无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "扩展外设控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        return model.Protocol switch
        {
            ExtendedProtocolKind.PulsarSonix => await ReadPulsarSonixAsync(target, endpoint, model, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.Eweadn30 => await ReadEweadnAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.AjazzV2Max => await ReadAjazzV2MaxAsync(target, endpoint, model, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.AjazzGearHubV5 => await ReadAjazzGearHubV5Async(target, endpoint, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.MchoseV3_4D => await ReadMchoseV3Async(target, endpoint, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.AulaF87Kb => await ReadAulaF87Async(target, endpoint, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.KeychronB3 => await ReadKeychronAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.AudezeMaxwell => await ReadAudezeAsync(target, endpoint, model, transport, trace, token).ConfigureAwait(false),
            ExtendedProtocolKind.Scyrox2E => await ReadScyrox2EAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            _ => AdapterRegistry.Failure(target, ReadIssue.IoError, "不支持的扩展协议")
        };
    }

    private static byte[] VerifiedRequest(ExtendedProtocolKind protocol, int length)
    {
        var req = ExtendedPeripheralProtocol.EncodeRequest(protocol, length);
        if (!ExtendedPeripheralProtocol.AllowsRequest(protocol, req))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
        return req;
    }

    private async ValueTask<BatteryReading> ReadPulsarSonixAsync(
        DeviceTarget target, HidInterface endpoint, ExtendedPeripheralModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = VerifiedRequest(ExtendedProtocolKind.PulsarSonix, 65);
        using var session = transport.OpenHid(endpoint with { FeatureOnly = true, RequiredFeatureReportId = 0x00 }, token);
        await session.WriteFeatureAsync(req, token).ConfigureAwait(false);
        trace?.Invoke(FrameDirection.Sent, req);
        if (settle > TimeSpan.Zero)
            await Task.Delay(settle, token).ConfigureAwait(false);
        var reply = new byte[65];
        await session.ReadFeatureAsync(reply, token).ConfigureAwait(false);
        trace?.Invoke(FrameDirection.Received, reply);
        if (!ExtendedPeripheralProtocol.TryDecodePulsarSonix(reply, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        var charging = model.Connection == DeviceConnection.UsbCable ? ChargingState.Charging : ChargingState.NotCharging;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadEweadnAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = VerifiedRequest(ExtendedProtocolKind.Eweadn30, 33);
        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 0x00, RequiredOutputReportId = 0x00 }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, 33,
            p => ExtendedPeripheralProtocol.TryDecodeEweadn(p, out _, out _, out _, out _), trace, token, milliseconds: 600).ConfigureAwait(false);
        if (frame is null || !ExtendedPeripheralProtocol.TryDecodeEweadn(frame, out bool asleep, out int level, out var charging, out var resolvedName))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, Detail: "EWEADN 鼠标正在休眠");
        var device = resolvedName is not null ? target.Device with { Name = resolvedName } : target.Device;
        return new(device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadAjazzV2MaxAsync(
        DeviceTarget target, HidInterface endpoint, ExtendedPeripheralModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        bool wired = model.Connection == DeviceConnection.UsbCable;
        var req = VerifiedRequest(ExtendedProtocolKind.AjazzV2Max, 33);
        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 0x00, RequiredOutputReportId = 0x00 }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, 33,
            p => ExtendedPeripheralProtocol.TryDecodeAjazzV2Max(p, wired, out _, out _), trace, token, milliseconds: 700).ConfigureAwait(false);
        if (frame is null || !ExtendedPeripheralProtocol.TryDecodeAjazzV2Max(frame, wired, out int level, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private async ValueTask<BatteryReading> ReadAjazzGearHubV5Async(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = VerifiedRequest(ExtendedProtocolKind.AjazzGearHubV5, 65);
        using var session = transport.OpenHid(endpoint with { FeatureOnly = true, RequiredFeatureReportId = 0x00 }, token);
        await session.WriteFeatureAsync(req, token).ConfigureAwait(false);
        trace?.Invoke(FrameDirection.Sent, req);
        if (settle > TimeSpan.Zero)
            await Task.Delay(settle, token).ConfigureAwait(false);
        var reply = new byte[65];
        await session.ReadFeatureAsync(reply, token).ConfigureAwait(false);
        trace?.Invoke(FrameDirection.Received, reply);
        if (!ExtendedPeripheralProtocol.TryDecodeAjazzGearHubV5(reply, out bool asleep, out int level) || asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow);
        return new(target.Device, level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadMchoseV3Async(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = VerifiedRequest(ExtendedProtocolKind.MchoseV3_4D, 64);
        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 0x4D, RequiredOutputReportId = 0x4D }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, 64,
            p => ExtendedPeripheralProtocol.TryDecodeMchoseV3(p, out _, out _, out _), trace, token, milliseconds: 700).ConfigureAwait(false);
        if (frame is null || !ExtendedPeripheralProtocol.TryDecodeMchoseV3(frame, out bool asleep, out int level, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, Detail: "迈从 V3 鼠标正在休眠");
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadAulaF87Async(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = VerifiedRequest(ExtendedProtocolKind.AulaF87Kb, 33);
        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 0x00, RequiredOutputReportId = 0x00 }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, 33,
            p => ExtendedPeripheralProtocol.TryDecodeAulaF87(p, out _), trace, token, milliseconds: 700).ConfigureAwait(false);
        if (frame is null || !ExtendedPeripheralProtocol.TryDecodeAulaF87(frame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        return new(target.Device, level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadKeychronAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        int featLen = endpoint.FeatureLength!.Value;
        int inLen = endpoint.InputLength!.Value;
        var req = VerifiedRequest(ExtendedProtocolKind.KeychronB3, featLen);
        using var inputSession = transport.OpenHid(endpoint with { InputOnly = true, RequiredInputReportId = 0xB4 }, token);
        using var featureSession = transport.OpenHid(endpoint with { FeatureOnly = true, RequiredFeatureReportId = 0xB3 }, token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(800);
        var dt = deadline.Token;
        var buffer = new byte[inLen];
        Task<int>? pending = null;
        try
        {
            pending = inputSession.ReadInputAsync(buffer, dt).AsTask();
            if (pending.IsFaulted || pending.IsCanceled)
                await pending.ConfigureAwait(false);
            await featureSession.WriteFeatureAsync(req, dt).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, req);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                dt.ThrowIfCancellationRequested();
                int count = pending is null
                    ? await inputSession.ReadInputAsync(buffer, dt).ConfigureAwait(false)
                    : await pending.ConfigureAwait(false);
                pending = null;
                var frame = count == buffer.Length ? buffer : buffer[..count];
                trace?.Invoke(FrameDirection.Received, frame);
                if (ExtendedPeripheralProtocol.TryDecodeKeychron(frame, out int level))
                    return new(target.Device, level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
        finally
        {
            if (pending is not null)
            {
                deadline.Cancel();
                await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
    }

    private static async ValueTask<BatteryReading> ReadAudezeAsync(
        DeviceTarget target, HidInterface endpoint, ExtendedPeripheralModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        // Audeze 接收器在耳机未连接时产品字符串以 "Dongle" 结尾，此时直接判定离线，不读取缓存旧值（HaloBattery #244）。
        if (model.Connection == DeviceConnection.Receiver &&
            endpoint.Product is { Length: > 0 } product &&
            product.Trim().EndsWith("Dongle", StringComparison.OrdinalIgnoreCase))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "Audeze 接收器报告耳机未连接");

        var req = VerifiedRequest(ExtendedProtocolKind.AudezeMaxwell, 62);
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x06, RequiredInputReportId = 0x07 }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, 62,
            p => ExtendedPeripheralProtocol.TryDecodeAudeze(p, out _), trace, token, milliseconds: 800).ConfigureAwait(false);
        if (frame is null || !ExtendedPeripheralProtocol.TryDecodeAudeze(frame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        var charging = model.Connection == DeviceConnection.UsbCable ? ChargingState.Charging : ChargingState.NotCharging;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadScyrox2EAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (!ExtendedPeripheralProtocol.TryResolveScyroxReportIds(endpoint, out byte inputReportId, out byte outputReportId))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "Scyrox 集合未声明唯一的输入/输出报告 ID");
        var req = ExtendedPeripheralProtocol.EncodeScyrox2E(outputReportId);
        if (!ExtendedPeripheralProtocol.AllowsScyrox2E(req))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
        using var session = transport.OpenHid(
            endpoint with { RequiredInputReportId = inputReportId, RequiredOutputReportId = outputReportId }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, ExtendedPeripheralProtocol.ScyroxReportLength,
            p => ExtendedPeripheralProtocol.TryDecodeScyrox2E(p, inputReportId, out _, out _, out _, out _),
            trace, token, milliseconds: 700).ConfigureAwait(false);
        if (frame is null || !ExtendedPeripheralProtocol.TryDecodeScyrox2E(
            frame, inputReportId, out bool asleep, out int level, out var charging, out var resolvedName))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, Detail: "Scyrox 鼠标未连接或正在休眠");
        var device = resolvedName is not null ? target.Device with { Name = resolvedName } : target.Device;
        return new(device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is null ||
            gates?.IsBlocked(endpoint, out _) == true)
            return null;
        var reading = await ReadAsync(target, transport, null, token).ConfigureAwait(false);
        return new(endpoint.ReceiverId, null, target.Device.Id, reading.Link, reading.Issue)
        {
            Battery = reading.Link == LinkState.Online ? reading : null,
            Connection = target.Device.Connection
        };
    }
}

namespace Jige.Core.Adapters;

internal enum MouseFamily { CompxOut, CompxFeature, BitMouse, XviOld, XviNew, Unknown }
internal enum MouseEvidence { MultipleSources, SingleSource, Unverified }
internal enum MouseChannel { OutputInput, FeatureInput, Feature }
internal enum BitMouseReply { WithoutChecksum, WithChecksum, Auto }

internal sealed record MouseProtocolShape(ushort UsagePage, ushort? Usage, ushort Length, byte ReportId, MouseChannel Channel)
{
    public HidInterface QueryEndpoint(HidInterface endpoint) => Channel switch
    {
        MouseChannel.Feature or MouseChannel.FeatureInput => endpoint with { FeatureOnly = true, RequiredFeatureReportId = ReportId },
        _ => endpoint with { RequiredOutputReportId = ReportId, RequiredInputReportId = ReportId }
    };

    public HidInterface InputEndpoint(HidInterface endpoint) =>
        endpoint with { InputOnly = true, RequiredInputReportId = ReportId };

    public bool Matches(HidInterface hid) =>
        hid.MetadataIssue == ReadIssue.None && hid.UsagePage == UsagePage && hid.Usage.HasValue &&
        (!Usage.HasValue || hid.Usage == Usage) &&
        (Channel == MouseChannel.Feature || hid.InputLength == Length) &&
        (Channel == MouseChannel.OutputInput ? hid.OutputLength == Length : hid.FeatureLength == Length);
}

internal sealed record MouseReceiver(
    string Model, ushort VendorId, ushort? ProductId, MouseFamily Family, MouseEvidence Evidence,
    bool Enabled = false, BitMouseReply BitReply = BitMouseReply.WithoutChecksum,
    bool ResolveModelByCidMid = false, bool IsGeneric = false,
    byte? InterfaceNumber = null, bool ExactCollectionOnly = false,
    DeviceConnection Connection = DeviceConnection.Receiver)
{
    public MouseProtocolShape? Shape => MouseReceiverCatalog.Shape(Family, VendorId);
    // 游狼的既有 XVI 路径要求 MI_02，新厂商可按型号单独指定。
    private byte? RequiredInterface => InterfaceNumber ??
        (VendorId == MouseReceiverCatalog.GWolves && Family is (MouseFamily.XviOld or MouseFamily.XviNew) ? (byte)2 : null);

    public bool Matches(HidInterface hid) => (IsGeneric || ProductId.HasValue && hid.ProductId == ProductId) &&
        hid.VendorId == VendorId && Shape?.Matches(hid) == true &&
        (RequiredInterface is null || hid.InterfaceNumber == RequiredInterface);
}

internal static class MouseReceiverCatalog
{
    internal const ushort Atk = 0x373B;
    internal const ushort GWolves = 0x33E4;
    internal const string DefaultAtkModel = "ATK 鼠标";

    public static MouseProtocolShape? Shape(MouseFamily family, ushort vendor) => family switch
    {
        MouseFamily.CompxOut when vendor == GWolves => new(0xFF02, null, 17, 8, MouseChannel.OutputInput),
        MouseFamily.CompxOut => new(0xFF02, 2, 17, 8, MouseChannel.OutputInput),
        MouseFamily.CompxFeature => new(0xFF04, null, 17, 8, MouseChannel.FeatureInput),
        MouseFamily.BitMouse => new(0xFF05, 1, 64, 8, MouseChannel.OutputInput),
        MouseFamily.XviOld or MouseFamily.XviNew => new(0xFFFF, 0, 65, 0, MouseChannel.Feature),
        _ => null
    };

    // 通用协议家族识别规则：仅当厂商和接口特征唯一对应某个已证实家族时允许通用匹配。
    // 游狼 XVI 两代共享 FFFF:0000 65B Feature 特征，属于一对多冲突，不开放通用识别，依赖官方型号表。
    private static readonly MouseFamily[] GenericCandidateFamilies =
        [MouseFamily.CompxOut, MouseFamily.BitMouse, MouseFamily.XviOld, MouseFamily.XviNew];

    internal static IReadOnlyList<(MouseReceiver Receiver, string Source)> DomesticModels { get; } =
    [
        (Compx("VXE R1 系列", 0x3554, 0xF58A, MouseEvidence.MultipleSources), "atk-control / HaloBattery"),
        (Compx("VXE / VGN 鼠标", 0x3554, 0xF505, MouseEvidence.MultipleSources), "atk-control / HaloBattery / VGN HUB"),
        (Compx("VXE 鼠标", 0x3554, 0xF58E), "mouse_tray 接收器记录"),
        (Compx("MAD 鼠标", Atk, 0x1040, MouseEvidence.MultipleSources), "ATK HUB / HaloBattery"),
        (Compx("VXE R1 SE+", Atk, 0x1085), "HaloBattery"),
        (Compx("Scyrox V8", 0x3554, 0xF5F7, MouseEvidence.MultipleSources), "Scyrox S-center / HaloBattery"),
        (Compx("Scyrox 鼠标", 0x3554, 0xF5F4), "Scyrox S-center"),
        (Compx("Compx 方案鼠标", 0x3554, 0xF517, MouseEvidence.MultipleSources), "Scyrox S-center / Control HUB / OpenMouse"),
        (Compx("VGN F2 Master", 0x3554, 0xFB56), "VGN HUB"),
        (Compx("VGN Y2 Ultra", 0x3554, 0xFB3E), "VGN HUB"),
        (Compx("VGN Y2 Master", 0x3554, 0xFB52), "VGN HUB"),
        (Compx("VGN Y2 Pro Max", 0x3554, 0xFB54), "VGN HUB"),
        (Compx("VGN Dragonfly 3 Master", 0x3554, 0xFB58), "VGN HUB"),
        (Compx("VGN 鼠标", 0x3554, 0xF503), "VGN HUB"),
        (Compx("VGN 鼠标", 0x3554, 0xF50B), "VGN HUB"),
        (Compx("Compx 方案鼠标", 0x3554, 0xF50D, MouseEvidence.MultipleSources), "Lamzu Aurora / Control HUB"),

        (Xvi("Lamzu Maya X 8K", 0x373E, 0x001E, MouseEvidence.MultipleSources, 2), "Lamzu Aurora / HaloBattery"),
        (Xvi("Lamzu Maya X 1K", 0x373E, 0x001D, MouseEvidence.SingleSource, 2), "Lamzu Aurora"),
        (Xvi("WLmouse Beast X Max", 0x36A7, 0xA880, MouseEvidence.MultipleSources), "WL MOUSE HUB / HaloBattery"),
        (Xvi("WLmouse Beast X Mini Pro", 0x36A7, 0xA868, MouseEvidence.SingleSource), "WL MOUSE HUB"),
        (Xvi("WLmouse Beast X", 0x36A7, 0xA883, MouseEvidence.SingleSource), "WL MOUSE HUB"),
        (Xvi("WLmouse Beast X 接收器", 0x36A7, 0xA887, MouseEvidence.SingleSource), "HaloBattery"),
        (Xvi("WLmouse 鼠标", 0x36A7, 0xA882, MouseEvidence.SingleSource), "WL MOUSE HUB"),
        (Xvi("MCHOSE A5 Pro Max 1K", 0x2023, 0xF013, MouseEvidence.MultipleSources), "HaloBattery / mchose-macos"),
        (Xvi("MCHOSE A5 Pro Max 4K", 0x2023, 0xF015, MouseEvidence.MultipleSources), "HaloBattery / mchose-macos")
    ];

    /// <summary>复用 Compx-OUT 或 XVI-NEW 协议家族的跨品牌已取证型号（来源：HaloBattery MIT / python-pulsar-mouse-tool / hitscan-battery / GloriousBatteryMonitor）。</summary>
    internal static IReadOnlyList<(MouseReceiver Receiver, string Source)> BrandModels { get; } =
    [
        // Pulsar（Compx-OUT：3554 / 3710）
        (Compx("Pulsar X2 V2 Mini", 0x3554, 0xF508, MouseEvidence.MultipleSources), "python-pulsar-mouse-tool / HaloBattery（MIT）"),
        (Compx("Pulsar X2 V2 Mini", 0x3554, 0xF507, MouseEvidence.MultipleSources, DeviceConnection.UsbCable), "python-pulsar-mouse-tool / HaloBattery（MIT）"),
        (Compx("Pulsar X2 CrazyLight Mini", 0x3710, 0x5406, MouseEvidence.MultipleSources), "python-pulsar-mouse-tool / HaloBattery（MIT）"),
        (Compx("Pulsar X2 CrazyLight Mini", 0x3710, 0x3414, MouseEvidence.MultipleSources, DeviceConnection.UsbCable), "python-pulsar-mouse-tool / HaloBattery（MIT）"),

        // Hitscan Hyperlight（Compx-OUT：3770）
        (Compx("Hitscan Hyperlight 8K", 0x3770, 0x0300, MouseEvidence.MultipleSources), "hitscan-battery / HaloBattery（MIT）"),
        (Compx("Hitscan Hyperlight", 0x3770, 0x0200, MouseEvidence.MultipleSources), "hitscan-battery / HaloBattery（MIT）"),
        (Compx("Hitscan Hyperlight", 0x3770, 0x0100, MouseEvidence.MultipleSources, DeviceConnection.UsbCable), "hitscan-battery / HaloBattery（MIT）"),

        // Cougar 骨伽（Compx-OUT：3554）
        (Compx("Cougar Revenger Pro 4K", 0x3554, 0xF5DF, MouseEvidence.SingleSource), "HaloBattery（MIT）"),
        (Compx("Cougar Revenger Pro 4K", 0x3554, 0xF5DE, MouseEvidence.SingleSource, DeviceConnection.UsbCable), "HaloBattery（MIT）"),
        (Compx("Cougar Revenger Pro", 0x3554, 0xF5DC, MouseEvidence.SingleSource), "HaloBattery（MIT）"),

        // Redragon 红龙（有线 3554:F55E；接收器 3554:F5D5 与 Zaopin 共享且分支未核实，继续保持在 DisabledPaths）
        (Compx("Redragon M991", 0x3554, 0xF55E, MouseEvidence.SingleSource, DeviceConnection.UsbCable), "HaloBattery（MIT）"),

        // Corsair Sabre v2 Pro Ultralight（Compx-OUT：1B1C:2B2A）
        (Compx("Corsair Sabre v2 Pro Ultralight", 0x1B1C, 0x2B2A, MouseEvidence.SingleSource), "HaloBattery（MIT）"),

        // Glorious Model O Wireless（Sinowealth / XVI-NEW 65B Feature：258A:2022）
        (Xvi("Glorious Model O Wireless", 0x258A, 0x2022, MouseEvidence.MultipleSources), "GloriousBatteryMonitor / HaloBattery（MIT）")
    ];

    private static MouseReceiver Compx(string model, ushort vid, ushort pid,
        MouseEvidence evidence = MouseEvidence.SingleSource, DeviceConnection connection = DeviceConnection.Receiver) =>
        new(model, vid, pid, MouseFamily.CompxOut, evidence, Enabled: true, ExactCollectionOnly: true, Connection: connection);

    private static MouseReceiver Xvi(string model, ushort vid, ushort pid, MouseEvidence evidence, byte? mi = null) =>
        new(model, vid, pid, MouseFamily.XviNew, evidence, Enabled: true, InterfaceNumber: mi, ExactCollectionOnly: true);

    private static readonly MouseReceiver[] Models =
    [
        // ATK 多款鼠标共用同一接收器 PID，默认显示「ATK 鼠标」，仅在协议读出 CID/MID 后细化为具体型号。
        new(DefaultAtkModel, Atk, 0x101A, MouseFamily.CompxOut, MouseEvidence.SingleSource, true, ResolveModelByCidMid: true),
        new("ATK X1 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK X1 Ultra", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK X1 Ultimate", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Pro", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Ultimate", Atk, 0x1031, MouseFamily.CompxOut, MouseEvidence.SingleSource, true),
        new("ATK F1 Ultimate 2.0", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK F1 Ultra", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK A9", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK A9 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK A9 Ultra", Atk, 0x111A, MouseFamily.CompxFeature, MouseEvidence.SingleSource),
        new(DefaultAtkModel, Atk, 0x101B, MouseFamily.Unknown, MouseEvidence.Unverified),
        // 373B:11D9 为多款 ATK 8K 鼠标（如 A9 Ultimate、F1 Ultimate 2.0）共用接收器，不再写死为 A9 Ultimate。
        new(DefaultAtkModel, Atk, 0x11D9, MouseFamily.CompxOut, MouseEvidence.MultipleSources, true, ResolveModelByCidMid: true),
        new(DefaultAtkModel, Atk, 0x1278, MouseFamily.CompxOut, MouseEvidence.SingleSource, true, ResolveModelByCidMid: true),
        new("ATK U2 Pro Max", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK U2 Ultra", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK U2 Ultimate", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK U2 Mini", Atk, null, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("ATK ZERO", Atk, 0x1155, MouseFamily.BitMouse, MouseEvidence.MultipleSources, true),
        new("ATK NK NANO 接收器", Atk, 0x1216,
            MouseFamily.BitMouse, MouseEvidence.SingleSource, true, BitMouseReply.Auto),
        new("ATK A9 Plus NK 接收器（星闪）", Atk, 0x10C9, MouseFamily.CompxOut, MouseEvidence.SingleSource, true, ResolveModelByCidMid: true),
        new("ATK A9 Pro Max 2.0 NK", Atk, 0x11DC, MouseFamily.BitMouse, MouseEvidence.SingleSource, true, BitMouseReply.Auto),
        new("ATK A9 Pro Max 2.0 NK（另一候选）", Atk, 0x11DB, MouseFamily.BitMouse, MouseEvidence.SingleSource, true, BitMouseReply.Auto),
        new("ATK A9 Ultimate+", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK X1 V2 Ultimate 2.0+", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK F1 V3 Ultimate+", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK F1 V3 Ultra Max", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),
        new("ATK U2 V2 Ultimate", Atk, null, MouseFamily.Unknown, MouseEvidence.Unverified),
        new("ATK U2 Plus NK", Atk, null, MouseFamily.BitMouse, MouseEvidence.Unverified),

        // 游狼共享接收器 3854（XVI=0）存在 Compx 与 XVI-NEW 候选冲突，保持禁用直到精确 Usage 实测确认。
        new("G-Wolves Receiver RS（Fenrir Pro / HTX Ultra 实测）", GWolves, 0x3854,
            MouseFamily.CompxOut, MouseEvidence.MultipleSources),
        new("G-Wolves Receiver RS（Lycan / HTS Plus / HTS Plus Pro / Warg / HTX Mini / HTX Mini Asym / Fenrir Asym 候选）",
            GWolves, 0x3854, MouseFamily.CompxOut, MouseEvidence.Unverified),
        new("G-Wolves Receiver RS", GWolves, 0x3854, MouseFamily.XviNew, MouseEvidence.Unverified),

        // 游狼 XVI=1 型号表导入自官方网页驱动 https://mouse.xyz/Config/env-models.json（AppVer 1.0.0.51）。
        // IsNewProtocol=1 → XVI-NEW，IsNewProtocol=0 → XVI-OLD。3517 因社区存在两代协议冲突记录，保持禁用。
        new("G-Wolves HTM Plus", GWolves, 0x3817, MouseFamily.XviNew, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Pro 2.0 8K", GWolves, 0x6817, MouseFamily.XviNew, MouseEvidence.SingleSource, true),
        // 2026-11-19 用户 Windows 11 实机验证读到电量。
        new("G-Wolves HTX Ultra 8K", GWolves, 0x5617, MouseFamily.XviNew, MouseEvidence.SingleSource, true),
        new("G-Wolves Fenrir Pro 8K", GWolves, 0x3617, MouseFamily.XviNew, MouseEvidence.SingleSource, true),
        new("G-Wolves Fenrir Asym 8K", GWolves, 0x3517, MouseFamily.XviOld, MouseEvidence.Unverified),
        new("G-Wolves Fenrir Asym 8K", GWolves, 0x3517, MouseFamily.XviNew, MouseEvidence.Unverified),
        new("G-Wolves VUK", GWolves, 0x3917, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HT-S2", GWolves, 0x7913, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves Fenrir Max 8K", GWolves, 0x3717, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTS Ultra", GWolves, 0x5317, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTR", GWolves, 0x7713, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HT-S2 Pro", GWolves, 0x7917, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Pro 8K", GWolves, 0x5817, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Pro 4K", GWolves, 0x5807, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTX Mini 8K", GWolves, 0x2717, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTS Plus 8K", GWolves, 0x5417, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTS Plus 4K", GWolves, 0x5407, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTX 8K", GWolves, 0x5717, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTX 4K", GWolves, 0x5707, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Plus 8K", GWolves, 0x5917, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Plus 4K", GWolves, 0x5907, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Lite", GWolves, 0x7203, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTR Pro", GWolves, 0x7717, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Pro ACE", GWolves, 0x5803, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTS Plus ACE", GWolves, 0x5403, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HTX ACE", GWolves, 0x5703, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Plus ACE", GWolves, 0x5903, MouseFamily.XviOld, MouseEvidence.SingleSource, true),
        new("G-Wolves HSK Plus ACE-2", GWolves, null, MouseFamily.XviOld, MouseEvidence.Unverified),
        .. DomesticModels.Select(model => model.Receiver),
        .. BrandModels.Select(model => model.Receiver)
    ];

    public static bool IsVendor(ushort? vid, ushort? pid) => vid is Atk or GWolves || IsListed(vid, pid);
    public static bool IsListed(ushort? vid, ushort? pid) =>
        pid.HasValue && Models.Any(row => row.VendorId == vid && row.ProductId == pid);

    public static IEnumerable<MouseReceiver> Candidates(ushort? vid, ushort? pid) =>
        Models.Where(row => row.VendorId == vid && row.ProductId == pid && pid.HasValue);

    /// <summary>按厂商和接口特征列出所有可能匹配的协议家族（用于安全门 ① 唯一性检查）。</summary>
    public static IEnumerable<MouseFamily> MatchingGenericFamilies(HidInterface hid)
    {
        if (hid.MetadataIssue != ReadIssue.None || hid.VendorId is not ushort vid || vid is not (Atk or GWolves))
            yield break;

        foreach (var family in GenericCandidateFamilies)
        {
            // 游狼 Compx-OUT 尚未核实精确 Usage，不走通用匹配；ATK Compx-OUT 与 BitMouse 具有唯一 Usage 和长度。
            if (vid == GWolves && family is MouseFamily.CompxOut or MouseFamily.BitMouse)
                continue;
            if (vid == Atk && family is MouseFamily.XviOld or MouseFamily.XviNew)
                continue;
            var shape = Shape(family, vid);
            if (shape is null || shape.Usage is null || !shape.Matches(hid))
                continue;
            if (family is MouseFamily.XviOld or MouseFamily.XviNew && hid.InterfaceNumber != 2)
                continue;
            yield return family;
        }
    }

    public static MouseReceiver? GenericRoute(HidInterface hid)
    {
        if (hid.VendorId != Atk || hid.ProductId is null || DeviceCatalog.IsDisabledPath(hid, out _))
            return null;
        // 若目录里明确登记了该 PID 且处于禁用/特殊布局（如 1216 带校验位或 111A Compx-FEAT），不绕过目录强制通用匹配。
        var listed = Candidates(hid.VendorId, hid.ProductId).ToArray();
        if (listed.Length > 0 && listed.All(row => row.Family != MouseFamily.Unknown))
            return null;

        var families = MatchingGenericFamilies(hid).ToArray();
        if (families.Length != 1)
            return null;
        var family = families[0];
        return family switch
        {
            MouseFamily.CompxOut => new(DefaultAtkModel, Atk, hid.ProductId, MouseFamily.CompxOut,
                MouseEvidence.SingleSource, Enabled: true, ResolveModelByCidMid: true, IsGeneric: true),
            MouseFamily.BitMouse => new(DefaultAtkModel, Atk, hid.ProductId, MouseFamily.BitMouse,
                MouseEvidence.SingleSource, Enabled: true, BitReply: BitMouseReply.Auto, IsGeneric: true),
            _ => null
        };
    }

    /// <summary>依据 Compx 命令 0x10 返回的 CID/MID 识别具体 ATK 型号（来源：OpenMouse / atk-control）。</summary>
    public static string? LookupAtkModel(byte cid, byte mid) => (cid, mid) switch
    {
        (1, 3) => "ATK A9 Ultimate",
        (1, 8) => "ATK F1 Ultimate 2.0",
        (1, 31) or (1, 52) => "ATK A9 Mini +",
        (2, 36) => "ATK F1 Pro",
        (2, 37) => "ATK F1 Pro Max",
        (2, 42) => "ATK F1 Ultimate",
        (2, 43) => "ATK X1 Ultra",
        (2, 45) => "ATK F1 Ultra",
        (2, 46) => "ATK X1 Ultimate",
        (2, 64) => "ATK U2 Mini",
        (2, 65) => "ATK U2 Ultimate",
        (2, 66) => "ATK U2 Ultra",
        (2, 85) => "ATK A9 Pro Max",
        (2, 99) => "ATK A9",
        (2, 100) => "ATK U2 Pro Max",
        _ => null
    };
}

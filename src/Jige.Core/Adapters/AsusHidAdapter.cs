namespace Jige.Core.Adapters;

internal enum AsusBatteryLayout { Byte6, Byte11, Auto }

internal enum AsusMouseScale { Percent, FiveStep }

internal sealed record AsusKeyboardModel(
    ushort Pid,
    string Name,
    DeviceConnection Connection,
    AsusBatteryLayout Layout = AsusBatteryLayout.Byte6);

internal sealed record AsusMouseModel(
    ushort Pid,
    string Name,
    DeviceConnection Connection,
    AsusMouseScale Scale = AsusMouseScale.Percent,
    byte InterfaceNumber = 0,
    ushort? UsagePage = null,
    byte ReportId = 0x00);

internal sealed record AsusHeadsetModel(
    ushort Pid,
    string Name,
    DeviceConnection Connection);

internal static class AsusHidProtocol
{
    public const ushort VendorId = 0x0B05;
    public const int ReportLength = 65;
    public const ushort OmniReceiverPid = 0x1ACE;
    public const byte HeadsetReportId = 0xFF;
    public const int HeadsetMinLength = 64;

    private static readonly byte[] StrixGoPrefix = [0xFF, 0x08, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x03, 0x52, 0x01];

    // 无线键盘 PID 依据 G-Helper（app/Peripherals/Keyboard/Models）与实机诊断报告（0B05:1934）。
    private static readonly AsusKeyboardModel[] Keyboards =
    [
        new(0x1934, "ASUS ROG 无线键盘", DeviceConnection.UsbCable, AsusBatteryLayout.Auto),
        new(0x193C, "ROG Falchion", DeviceConnection.UsbCable, AsusBatteryLayout.Byte11),
        new(0x193E, "ROG Falchion", DeviceConnection.Receiver, AsusBatteryLayout.Byte11),
        new(0x1B04, "ROG Falchion RX", DeviceConnection.UsbCable, AsusBatteryLayout.Byte11),
        new(0x196B, "ROG Claymore II", DeviceConnection.Receiver),
        new(0x1A83, "ROG Azoth", DeviceConnection.UsbCable),
        new(0x1A85, "ROG Azoth", DeviceConnection.Receiver),
        new(0x1B3F, "ROG Azoth Extreme", DeviceConnection.UsbCable),
        new(0x1C24, "ROG Azoth X", DeviceConnection.UsbCable),
        new(0x1C25, "ROG Azoth X", DeviceConnection.Receiver),
        new(0x1AAE, "ROG Strix Scope II 96 Wireless", DeviceConnection.UsbCable),
        new(0x1B78, "ROG Strix Scope II 96 RX Wireless", DeviceConnection.UsbCable)
    ];

    // 无线/有线鼠标 PID 依据 G-Helper（app/Peripherals/Mouse/Models）与 HaloBattery（MIT）。
    private static readonly AsusMouseModel[] Mice =
    [
        new(0x1A72, "ROG Gladius III Aimpoint", DeviceConnection.Receiver),
        new(0x1A70, "ROG Gladius III Aimpoint", DeviceConnection.UsbCable),
        new(0x1B0C, "ROG Gladius III Eva 2", DeviceConnection.Receiver),
        new(0x1B0A, "ROG Gladius III Eva 2", DeviceConnection.UsbCable),
        new(0x197F, "ROG Gladius III Wireless", DeviceConnection.Receiver),
        new(0x197D, "ROG Gladius III Wireless", DeviceConnection.UsbCable),
        new(0x1A1A, "ROG Chakram X", DeviceConnection.Receiver),
        new(0x1A18, "ROG Chakram X", DeviceConnection.UsbCable),
        new(0x1A94, "ROG Harpe Ace Aim Lab Edition", DeviceConnection.Receiver),
        new(0x1A92, "ROG Harpe Ace Aim Lab Edition", DeviceConnection.UsbCable),
        new(0x1A68, "ROG Keris Wireless Aimpoint", DeviceConnection.Receiver),
        new(0x1A66, "ROG Keris Wireless Aimpoint", DeviceConnection.UsbCable),
        new(0x1979, "ROG Spatha X", DeviceConnection.Receiver),
        new(0x1977, "ROG Spatha X", DeviceConnection.UsbCable),
        new(0x19F4, "TUF Gaming M4 Wireless", DeviceConnection.Receiver),
        new(0x1A8D, "TX Gaming Mouse", DeviceConnection.Receiver),
        new(0x1AF5, "TX Gaming Mouse Mini", DeviceConnection.Receiver),
        new(0x1AF3, "TX Gaming Mouse Mini", DeviceConnection.UsbCable),
        new(0x1C5A, "TX Gaming Mouse Mini Miku", DeviceConnection.Receiver),
        new(0x1C57, "TUF Gaming Mini Miku Edition", DeviceConnection.Receiver),
        new(0x1C56, "TUF Gaming Mini Miku Edition", DeviceConnection.UsbCable),
        // Harpe II Ace / Extreme（G-Helper / HaloBattery #364：SPEEDNOVA 接收器走 MI_02 FF01 Report ID 03）
        new(0x1AD0, "ROG Harpe II Ace", DeviceConnection.Receiver, AsusMouseScale.Percent, 2, 0xFF01, 0x03),
        new(0x1C69, "ROG Harpe II Ace", DeviceConnection.UsbCable),
        // 旧代 5 档（0–4 × 25%，标为估算）
        new(0x18E5, "ROG Chakram", DeviceConnection.Receiver, AsusMouseScale.FiveStep),
        new(0x18E3, "ROG Chakram", DeviceConnection.UsbCable, AsusMouseScale.FiveStep),
        new(0x1960, "ROG Keris Wireless", DeviceConnection.Receiver, AsusMouseScale.FiveStep),
        new(0x195E, "ROG Keris Wireless", DeviceConnection.UsbCable, AsusMouseScale.FiveStep),
        new(0x1A59, "ROG Keris EVA Edition", DeviceConnection.Receiver, AsusMouseScale.FiveStep),
        new(0x1A57, "ROG Keris EVA Edition", DeviceConnection.UsbCable, AsusMouseScale.FiveStep),
        new(0x1908, "ROG Pugio II", DeviceConnection.Receiver, AsusMouseScale.FiveStep),
        new(0x1906, "ROG Pugio II", DeviceConnection.UsbCable, AsusMouseScale.FiveStep),
        new(0x1949, "ROG Strix Impact II Wireless", DeviceConnection.Receiver, AsusMouseScale.FiveStep),
        new(0x1947, "ROG Strix Impact II Wireless", DeviceConnection.UsbCable, AsusMouseScale.FiveStep)
    ];

    private static readonly AsusHeadsetModel[] Headsets =
    [
        new(0x18D6, "ROG Strix Go 2.4", DeviceConnection.Receiver),
        new(0x18D7, "ROG Strix Go 2.4", DeviceConnection.UsbCable)
    ];

    internal static readonly Dictionary<ushort, string> OmniMice = new()
    {
        [0x1B65] = "ROG Harpe Ace Mini",
        [0x1C0E] = "ROG Keris II Origin",
        [0x1D4E] = "ROG Keris II Origin KJP",
        [0x1A94] = "ROG Harpe Ace Aim Lab Edition",
        [0x1AD7] = "ROG Strix Impact III Wireless",
        [0x1A72] = "ROG Gladius III Aimpoint",
        [0x1A68] = "ROG Keris Wireless Aimpoint",
        [0x1A6A] = "ROG Keris Wireless Aimpoint",
        [0x1B1A] = "ROG Keris II Ace",
        [0x1B18] = "ROG Keris II Ace",
        [0x1B68] = "ROG Harpe Ace Extreme",
        [0x1B69] = "ROG Harpe Ace Extreme"
    };

    internal static readonly Dictionary<ushort, (string Name, AsusBatteryLayout Layout)> OmniKeyboards = new()
    {
        [0x1A85] = ("ROG Azoth", AsusBatteryLayout.Byte6),
        [0x1B42] = ("ROG Azoth Extreme", AsusBatteryLayout.Byte6),
        [0x1CF1] = ("ROG Azoth Extreme SE", AsusBatteryLayout.Byte6),
        [0x1AB0] = ("ROG Strix Scope II 96 Wireless", AsusBatteryLayout.Byte6),
        [0x1B7A] = ("ROG Strix Scope II 96 RX Wireless", AsusBatteryLayout.Byte6),
        [0x1B06] = ("ROG Falchion RX Low Profile", AsusBatteryLayout.Byte11),
        [0x1CEA] = ("ROG Strix Morph 96 Wireless", AsusBatteryLayout.Byte6)
    };

    public static IReadOnlyList<AsusKeyboardModel> AllKeyboards => Keyboards;
    public static IReadOnlyList<AsusMouseModel> AllMice => Mice;
    public static IReadOnlyList<AsusHeadsetModel> AllHeadsets => Headsets;

    public static AsusKeyboardModel? FindKeyboard(ushort? pid) =>
        pid.HasValue ? Array.Find(Keyboards, model => model.Pid == pid.Value) : null;

    public static AsusMouseModel? FindMouse(ushort? pid) =>
        pid.HasValue ? Array.Find(Mice, model => model.Pid == pid.Value) : null;

    public static AsusHeadsetModel? FindHeadset(ushort? pid) =>
        pid.HasValue ? Array.Find(Headsets, model => model.Pid == pid.Value) : null;

    internal static bool IsStrixGoFeatureCandidate(ushort? vid, ushort? pid, ushort? featureLength) =>
        vid == VendorId && featureLength is >= HeadsetMinLength && FindHeadset(pid) is not null;

    public static bool IsKnownPid(ushort? pid) =>
        pid == OmniReceiverPid || FindKeyboard(pid) is not null || FindMouse(pid) is not null || FindHeadset(pid) is not null;

    /// <summary>华硕 ROG 键盘纯只读电量查询（Report ID 0x00，命令 12 01，补零至 65 字节）。</summary>
    public static byte[] EncodeKeyboardBattery(int length = ReportLength, byte reportId = 0x00)
    {
        var frame = new byte[length];
        frame[0] = reportId;
        frame[1] = 0x12;
        frame[2] = 0x01;
        return frame;
    }

    /// <summary>华硕 ROG/TUF 鼠标纯只读电量查询（命令 12 07，补零至报告长度）。</summary>
    public static byte[] EncodeMouseBattery(int length = ReportLength, byte reportId = 0x00)
    {
        var frame = new byte[length];
        frame[0] = reportId;
        frame[1] = 0x12;
        frame[2] = 0x07;
        return frame;
    }

    /// <summary>华硕 ROG OMNI 接收器只读配对表查询（Report ID 0x01，命令 A0 00 00）。</summary>
    public static byte[] EncodeOmniPairList(int length = 64)
    {
        var frame = new byte[length];
        frame[0] = 0x01;
        frame[1] = 0xA0;
        return frame;
    }

    /// <summary>华硕 ROG Strix Go 2.4 耳机纯只读 Feature FF 电量电压查询。</summary>
    public static byte[] EncodeHeadsetBattery(int length = HeadsetMinLength)
    {
        var frame = new byte[length];
        StrixGoPrefix.CopyTo(frame, 0);
        return frame;
    }

    public static bool AllowsRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length >= HeadsetMinLength && frame.StartsWith(StrixGoPrefix))
            return !frame[StrixGoPrefix.Length..].ContainsAnyExcept((byte)0);
        if (frame.Length is < 64 or > 65)
            return false;
        if (frame[0] == 0x00 && frame[1] == 0x12 && frame[2] is 0x01 or 0x07)
            return !frame[3..].ContainsAnyExcept((byte)0);
        if (frame[0] == 0x01 && frame[1] == 0xA0)
            return !frame[2..].ContainsAnyExcept((byte)0);
        if (frame[0] == 0x02 && frame[1] == 0x12 && frame[2] == 0x01)
            return !frame[3..].ContainsAnyExcept((byte)0);
        if (frame[0] == 0x03 && frame[1] == 0x12 && frame[2] == 0x07)
            return !frame[3..].ContainsAnyExcept((byte)0);
        return false;
    }

    public static bool TryDecodeKeyboardBattery(
        ReadOnlySpan<byte> frame, AsusKeyboardModel model,
        out bool asleep, out int level, out ChargingState charging) =>
        TryDecodeKeyboardBattery(frame, model.Layout, 0x00, out asleep, out level, out charging);

    public static bool TryDecodeKeyboardBattery(
        ReadOnlySpan<byte> frame, AsusBatteryLayout layout, byte expectedReportId,
        out bool asleep, out int level, out ChargingState charging)
    {
        asleep = false;
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length < 12)
            return false;

        int header;
        if (expectedReportId != 0x00)
        {
            if (frame[0] != expectedReportId)
                return false;
            header = 1;
        }
        else
        {
            header = frame[0] == 0x00 ? 1 : frame[0] is 0x12 or 0xFF ? 0 : -1;
            if (header < 0)
                return false;
        }

        if (frame.Length <= header + 10)
            return false;
        if (frame[header] == 0xFF && frame[header + 1] == 0xAA)
        {
            asleep = true;
            return true;
        }
        if (frame[header] != 0x12 || frame[header + 1] != 0x01)
            return false;

        byte b6 = frame[header + 5];
        byte b9 = frame[header + 8];
        byte b11 = frame[header + 10];
        int raw = layout switch
        {
            AsusBatteryLayout.Byte11 => b11,
            // Falchion 系列在 [6] 上报 0–10 档位并在 [11] 上报真实百分比，其余 ROG 键盘直接在 [6] 上报百分比。
            AsusBatteryLayout.Auto => b6 <= 10 && b11 is > 10 and <= 100 ? b11 : b6,
            _ => b6
        };
        if (raw > 100)
            return false;
        if (raw == 0 && b9 == 0)
        {
            asleep = true;
            return true;
        }
        level = raw;
        charging = b9 != 0 ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool TryDecodeMouseBattery(
        ReadOnlySpan<byte> frame, AsusMouseScale scale, byte expectedReportId,
        out bool asleep, out int level, out bool isEstimated, out ChargingState charging)
    {
        asleep = false;
        level = 0;
        isEstimated = false;
        charging = ChargingState.Unknown;
        if (frame.Length < 10)
            return false;

        int header;
        if (expectedReportId != 0x00)
        {
            if (frame[0] != expectedReportId)
                return false;
            header = 1;
        }
        else
        {
            header = frame[0] == 0x00 ? 1 : frame[0] is 0x12 or 0xFF ? 0 : -1;
            if (header < 0)
                return false;
        }

        if (frame.Length <= header + 9)
            return false;
        if (frame[header] == 0xFF && frame[header + 1] == 0xAA)
        {
            asleep = true;
            return true;
        }
        if (!frame[header..].ContainsAnyExcept((byte)0))
        {
            asleep = true;
            return true;
        }
        if (frame[header] != 0x12 || frame[header + 1] != 0x07)
            return false;

        byte raw = frame[header + 4];
        bool isCharging = frame[header + 9] != 0;
        if (raw == 0 && !isCharging)
        {
            asleep = true;
            return true;
        }
        if (scale == AsusMouseScale.FiveStep)
        {
            if (raw > 4)
                return false;
            level = raw * 25;
            isEstimated = true;
            charging = isCharging ? ChargingState.Charging : ChargingState.NotCharging;
            return true;
        }
        if (raw > 100)
            return false;
        level = raw;
        charging = isCharging ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool TryDecodeHeadsetBattery(
        ReadOnlySpan<byte> frame, bool wired,
        out bool offline, out int level, out ChargingState charging)
    {
        offline = false;
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length < 13)
            return false;

        int skip = frame[0] == HeadsetReportId ? 0 : 1;
        if (12 - skip >= frame.Length)
            return false;

        byte b1 = frame[1 - skip];
        byte b2 = frame[2 - skip];
        byte b3 = frame[3 - skip];
        if ((b1 == 0xFF && b2 == 0xAA) || (b1 == 0 && b2 == 0 && b3 == 0) || b1 != 0x1B)
        {
            offline = true;
            return true;
        }

        int mv = frame[11 - skip] | (frame[12 - skip] << 8);
        bool isCharging = wired || frame[9 - skip] == 0x0A;
        // 充电时会抬升端电压约 160 mV，在恒压满电平台（4100 mV）以下扣除抬升避免跳档（依据 HaloBattery #190）。
        if (isCharging && mv < 4100)
            mv = Math.Max(0, mv - 160);
        int msb = mv >> 8;

        level = (msb >= 0x10 || mv >= 4100) ? 100
            : (msb >= 0x0F || mv >= 3800) ? 75
            : mv >= 3700 ? 50
            : (msb >= 0x0E || mv >= 3500) ? 25
            : 10;
        charging = isCharging ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static List<ushort> DecodeOmniPairList(ReadOnlySpan<byte> frame)
    {
        List<ushort> pids = [];
        if (frame.Length < 7 || frame[0] != 0x01 || frame[1] != 0xA0)
            return pids;
        for (int i = 5; i + 1 < frame.Length; i += 4)
        {
            ushort pid = (ushort)(frame[i] | (frame[i + 1] << 8));
            if (pid == 0)
                break;
            pids.Add(pid);
        }
        return pids;
    }
}

internal sealed class AsusHidAdapter(SafetyGateStore? gates = null, TimeSpan? headsetSettleDelay = null) : IDeviceAdapter, IPresenceAdapter
{
    private readonly TimeSpan headsetSettle = headsetSettleDelay ?? TimeSpan.FromMilliseconds(35);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static AsusKeyboardModel? MatchKeyboard(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _))
            return null;
        if (hid is not
            {
                VendorId: AsusHidProtocol.VendorId,
                UsagePage: 0xFF00,
                Usage: 0x0001,
                InputLength: AsusHidProtocol.ReportLength,
                OutputLength: AsusHidProtocol.ReportLength,
                InterfaceNumber: 1,
                MetadataIssue: ReadIssue.None
            })
            return null;
        return AsusHidProtocol.FindKeyboard(hid.ProductId);
    }

    private static AsusMouseModel? MatchMouse(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _) || hid.VendorId != AsusHidProtocol.VendorId ||
            hid.MetadataIssue != ReadIssue.None || hid.UsagePage is not >= 0xFF00 ||
            hid.InputLength is not (64 or 65) || hid.OutputLength is not (64 or 65))
            return null;
        var model = AsusHidProtocol.FindMouse(hid.ProductId);
        if (model is null || hid.InterfaceNumber != model.InterfaceNumber)
            return null;
        if (model.UsagePage is ushort expectedPage && hid.UsagePage != expectedPage)
            return null;
        return model;
    }

    private static AsusHeadsetModel? MatchHeadset(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _) || hid.MetadataIssue != ReadIssue.None ||
            !AsusHidProtocol.IsStrixGoFeatureCandidate(hid.VendorId, hid.ProductId, hid.FeatureLength) ||
            hid.FeatureReportIds is not null && !hid.FeatureReportIds.Split(',').Contains("FF", StringComparer.OrdinalIgnoreCase))
            return null;
        return AsusHidProtocol.FindHeadset(hid.ProductId);
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(hid => MatchKeyboard(hid) is not null)))
        {
            if (gates?.IsBlocked(endpoint, out _) == true)
                continue;
            var model = MatchKeyboard(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"asus:{endpoint.ReceiverId}", model.Name, DeviceKind.Keyboard,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "华硕 USB 直连" : "华硕无线接收器"
            }, endpoint, "0");
        }

        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(hid => MatchMouse(hid) is not null)))
        {
            if (gates?.IsBlocked(endpoint, out _) == true)
                continue;
            var model = MatchMouse(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"asus-mouse:{endpoint.ReceiverId}", model.Name, DeviceKind.Mouse,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "华硕 USB 直连" : "华硕无线接收器"
            }, endpoint, "0");
        }

        // 优先使用声明 FF 的集合；旧报告缺少编号时仍可按厂商页识别唯一候选。
        foreach (var group in inventory.Hid.Where(hid => MatchHeadset(hid) is not null)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var preferred = group.GroupBy(hid => (Declared: hid.FeatureReportIds is not null, VendorPage: hid.UsagePage is >= 0xFF00))
                .OrderByDescending(rank => rank.Key.Declared)
                .ThenByDescending(rank => rank.Key.VendorPage)
                .First();
            if (preferred.Take(2).ToArray() is not [var endpoint] || gates?.IsBlocked(endpoint, out _) == true)
                continue;
            var model = MatchHeadset(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"asus-headset:{endpoint.ReceiverId}", model.Name, DeviceKind.Headset,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "华硕 USB 直连" : "华硕无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "华硕外设控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        if (MatchKeyboard(endpoint) is { } keyboard)
            return await ReadKeyboardAsync(target, endpoint, keyboard, transport, trace, token).ConfigureAwait(false);
        if (MatchMouse(endpoint) is { } mouse)
            return await ReadMouseAsync(target, endpoint, mouse, transport, trace, token).ConfigureAwait(false);
        if (MatchHeadset(endpoint) is { } headset)
            return await ReadHeadsetAsync(target, endpoint, headset, transport, trace, token).ConfigureAwait(false);

        return AdapterRegistry.Failure(target, ReadIssue.IoError, "华硕外设控制集合不匹配");
    }

    private static async ValueTask<BatteryReading> ReadKeyboardAsync(
        DeviceTarget target, HidInterface endpoint, AsusKeyboardModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(
            endpoint with { RequiredInputReportId = 0, RequiredOutputReportId = 0 }, token);
        var request = AsusHidProtocol.EncodeKeyboardBattery();
        if (!AsusHidProtocol.AllowsRequest(request))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");
        var frame = await HidQuery.OutputAsync(session, session, request, AsusHidProtocol.ReportLength,
            packet => AsusHidProtocol.TryDecodeKeyboardBattery(packet, model, out _, out _, out _),
            trace, token).ConfigureAwait(false);
        if (frame is null || !AsusHidProtocol.TryDecodeKeyboardBattery(frame, model, out bool asleep, out int level, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, ReadIssue.Timeout, "华硕键盘查询超时，设备可能正在休眠");
        if (asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, Detail: "华硕键盘报告待机或暂未响应电量查询");
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadMouseAsync(
        DeviceTarget target, HidInterface endpoint, AsusMouseModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        int outLen = endpoint.OutputLength!.Value;
        int inLen = endpoint.InputLength!.Value;
        var request = AsusHidProtocol.EncodeMouseBattery(outLen, model.ReportId);
        if (!AsusHidProtocol.AllowsRequest(request))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");

        using var session = transport.OpenHid(
            endpoint with { RequiredInputReportId = model.ReportId, RequiredOutputReportId = model.ReportId }, token);
        var frame = await HidQuery.OutputAsync(session, session, request, inLen,
            packet => AsusHidProtocol.TryDecodeMouseBattery(packet, model.Scale, model.ReportId, out _, out _, out _, out _),
            trace, token).ConfigureAwait(false);
        if (frame is null || !AsusHidProtocol.TryDecodeMouseBattery(
            frame, model.Scale, model.ReportId, out bool asleep, out int level, out bool estimated, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, ReadIssue.Timeout, "华硕鼠标查询超时，设备可能正在休眠");
        if (asleep)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, Detail: "华硕鼠标报告待机或暂未响应电量查询");
        return new(target.Device, level, estimated, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private async ValueTask<BatteryReading> ReadHeadsetAsync(
        DeviceTarget target, HidInterface endpoint, AsusHeadsetModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        int length = endpoint.FeatureLength!.Value;
        var request = AsusHidProtocol.EncodeHeadsetBattery(length);
        if (!AsusHidProtocol.AllowsRequest(request))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");

        using var session = transport.OpenHid(
            endpoint with { FeatureOnly = true, RequiredFeatureReportId = AsusHidProtocol.HeadsetReportId }, token);
        var reply = new byte[length];
        bool wired = model.Connection == DeviceConnection.UsbCable;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await session.WriteFeatureAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            if (headsetSettle > TimeSpan.Zero)
                await Task.Delay(headsetSettle, token).ConfigureAwait(false);
            Array.Clear(reply);
            reply[0] = AsusHidProtocol.HeadsetReportId;
            await session.ReadFeatureAsync(reply, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Received, reply);
            if (AsusHidProtocol.TryDecodeHeadsetBattery(reply, wired, out bool offline, out int level, out var charging))
            {
                if (offline)
                    return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline,
                        DateTimeOffset.UtcNow, Detail: "ROG Strix Go 2.4 耳机未连接或已关机");
                return new(target.Device, level, true, charging, LinkState.Online, DateTimeOffset.UtcNow);
            }
        }
        return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
            DateTimeOffset.UtcNow, ReadIssue.Timeout, "ROG Strix Go 2.4 耳机未返回有效电量帧");
    }

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint ||
            (MatchKeyboard(endpoint) is null && MatchMouse(endpoint) is null && MatchHeadset(endpoint) is null) ||
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

/// <summary>ASUS ROG OMNI 接收器（0B05:1ACE）多设备适配器：通过 MI_02 三个厂商集合只读查询已配对的鼠标与键盘。</summary>
internal sealed class AsusOmniAdapter(SafetyGateStore? gates = null) : IMultiDeviceAdapter
{
    private static bool IsOmniCollection(HidInterface hid) =>
        !DeviceCatalog.IsDisabledPath(hid, out _) && hid is
        {
            VendorId: AsusHidProtocol.VendorId,
            ProductId: AsusHidProtocol.OmniReceiverPid,
            InterfaceNumber: 2,
            UsagePage: 0xFF00 or 0xFF01 or 0xFF02,
            InputLength: 64 or 65,
            OutputLength: 64 or 65,
            MetadataIssue: ReadIssue.None
        };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(IsOmniCollection)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var control = group.FirstOrDefault(hid => hid.UsagePage == 0xFF02);
            if (control is null || gates?.IsBlocked(control, out _) == true)
                continue;
            var related = group.Where(hid => hid.UsagePage is 0xFF00 or 0xFF01).ToArray();
            if (related.Length == 0)
                continue;
            var tier = DeviceCatalog.VerificationFor(control.VendorId, control.ProductId);
            yield return new(new($"asus-omni:{control.ReceiverId}", "ASUS ROG OMNI 接收器", DeviceKind.Other,
                AdaptationSource.Catalog, tier)
            {
                SourceLabel = "华硕 OMNI 接收器"
            }, control, "0")
            {
                RelatedHids = related
            };
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var all = await ReadDevicesAsync(target, transport, trace, token).ConfigureAwait(false);
        return all.Count > 0
            ? all[0]
            : new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, ReadIssue.Timeout, "OMNI 接收器暂未读到已配对设备");
    }

    public async ValueTask<IReadOnlyList<BatteryReading>> ReadDevicesAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface control || !IsOmniCollection(control))
            return [AdapterRegistry.Failure(target, ReadIssue.IoError, "OMNI 控制集合不匹配")];
        if (gates?.IsBlocked(control, out var blockedReason) == true)
            return [AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}")];

        List<ushort> paired;
        using (var session = transport.OpenHid(control with { RequiredInputReportId = 1, RequiredOutputReportId = 1 }, token))
        {
            var req = AsusHidProtocol.EncodeOmniPairList(control.OutputLength!.Value);
            if (!AsusHidProtocol.AllowsRequest(req))
                return [AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单")];
            var frame = await HidQuery.OutputAsync(session, session, req, control.InputLength!.Value,
                packet => AsusHidProtocol.DecodeOmniPairList(packet).Count > 0, trace, token).ConfigureAwait(false);
            paired = frame is null ? [] : AsusHidProtocol.DecodeOmniPairList(frame);
        }

        List<BatteryReading> readings = [];
        var tier = DeviceCatalog.VerificationFor(control.VendorId, control.ProductId);

        ushort? mousePid = paired.Cast<ushort?>().FirstOrDefault(pid => AsusHidProtocol.OmniMice.ContainsKey(pid!.Value));
        var mouseHid = target.RelatedHids.FirstOrDefault(hid => hid.UsagePage == 0xFF01);
        if (mousePid is ushort mPid && mouseHid is not null)
        {
            var identity = new DeviceIdentity($"asus-omni:{control.ReceiverId}:mouse", AsusHidProtocol.OmniMice[mPid],
                DeviceKind.Mouse, AdaptationSource.Catalog, tier)
            {
                SourceLabel = "华硕 OMNI 接收器"
            };
            readings.Add(await ReadOmniMouseAsync(identity, mouseHid, transport, trace, token).ConfigureAwait(false));
        }

        ushort? kbPid = paired.Cast<ushort?>().FirstOrDefault(pid => AsusHidProtocol.OmniKeyboards.ContainsKey(pid!.Value));
        var kbHid = target.RelatedHids.FirstOrDefault(hid => hid.UsagePage == 0xFF00);
        if (kbPid is ushort kPid && kbHid is not null)
        {
            var (name, layout) = AsusHidProtocol.OmniKeyboards[kPid];
            var identity = new DeviceIdentity($"asus-omni:{control.ReceiverId}:kb", name,
                DeviceKind.Keyboard, AdaptationSource.Catalog, tier)
            {
                SourceLabel = "华硕 OMNI 接收器"
            };
            readings.Add(await ReadOmniKeyboardAsync(identity, layout, kbHid, transport, trace, token).ConfigureAwait(false));
        }

        return readings;
    }

    private static async ValueTask<BatteryReading> ReadOmniMouseAsync(
        DeviceIdentity identity, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 3, RequiredOutputReportId = 3 }, token);
        var req = AsusHidProtocol.EncodeMouseBattery(endpoint.OutputLength!.Value, 0x03);
        if (!AsusHidProtocol.AllowsRequest(req))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
        var frame = await HidQuery.OutputAsync(session, session, req, endpoint.InputLength!.Value,
            packet => AsusHidProtocol.TryDecodeMouseBattery(packet, AsusMouseScale.Percent, 0x03, out _, out _, out _, out _),
            trace, token).ConfigureAwait(false);
        if (frame is null || !AsusHidProtocol.TryDecodeMouseBattery(
            frame, AsusMouseScale.Percent, 0x03, out bool asleep, out int level, out bool estimated, out var charging) || asleep)
            return new(identity, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, frame is null ? ReadIssue.Timeout : ReadIssue.None);
        return new(identity, level, estimated, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadOmniKeyboardAsync(
        DeviceIdentity identity, AsusBatteryLayout layout, HidInterface endpoint,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 2, RequiredOutputReportId = 2 }, token);
        var req = AsusHidProtocol.EncodeKeyboardBattery(endpoint.OutputLength!.Value, 0x02);
        if (!AsusHidProtocol.AllowsRequest(req))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
        var frame = await HidQuery.OutputAsync(session, session, req, endpoint.InputLength!.Value,
            packet => AsusHidProtocol.TryDecodeKeyboardBattery(packet, layout, 0x02, out _, out _, out _),
            trace, token).ConfigureAwait(false);
        if (frame is null || !AsusHidProtocol.TryDecodeKeyboardBattery(
            frame, layout, 0x02, out bool asleep, out int level, out var charging) || asleep)
            return new(identity, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, frame is null ? ReadIssue.Timeout : ReadIssue.None);
        return new(identity, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }
}

namespace Jige.Core.Adapters;

internal enum SteelSeriesProtocolKind
{
    Nova7,
    Nova7Discrete,
    Nova5,
    Arctis7Plus,
    GameBuds,
    Arctis1,
    Arctis7_2018,
    Arctis7,
    Arctis9,
    ArctisProWireless,
    NovaProWireless,
    AeroxMouse,
    Rival3Mouse
}

internal sealed record SteelSeriesModel(
    ushort Pid,
    string Name,
    DeviceKind Kind,
    SteelSeriesProtocolKind Protocol,
    byte[] Interfaces,
    ushort? UsagePage = null,
    ushort? Usage = null);

/// <summary>
/// SteelSeries 赛睿无线耳机与无线鼠标纯只读电量协议。
/// 协议事实依据 Sapd/HeadsetControl（GPL 事实）、Linux hid-steelseries-arctis、rivalcfg、alloyctl 与 HaloBattery（MIT）独立编写。
/// </summary>
internal static class SteelSeriesProtocol
{
    public const ushort VendorId = 0x1038;
    public const ushort ConfigUsagePage = 0xFFC0;
    public const ushort ConfigUsage = 0x0001;

    private static readonly byte[] If3 = [3];
    private static readonly byte[] If5 = [5];
    private static readonly byte[] If0 = [0];
    private static readonly byte[] If34 = [3, 4];
    private static readonly byte[] AnyIf = [];

    private static readonly SteelSeriesModel[] Models =
    [
        // === 现代 0xB0 无线耳机（FFC0:0001，优先 MI_03） ===
        HeadsetB0(0x22A1, "SteelSeries Arctis Nova 7", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x2202, "SteelSeries Arctis Nova 7", SteelSeriesProtocolKind.Nova7Discrete),
        HeadsetB0(0x227E, "SteelSeries Arctis Nova 7 Gen 2", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x2206, "SteelSeries Arctis Nova 7X", SteelSeriesProtocolKind.Nova7Discrete),
        HeadsetB0(0x2258, "SteelSeries Arctis Nova 7X", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x229E, "SteelSeries Arctis Nova 7X", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x22AD, "SteelSeries Arctis Nova 7X", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x22A4, "SteelSeries Arctis Nova 7X", SteelSeriesProtocolKind.Nova7Discrete),
        HeadsetB0(0x22A5, "SteelSeries Arctis Nova 7X", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x223A, "SteelSeries Arctis Nova 7 Diablo IV", SteelSeriesProtocolKind.Nova7Discrete),
        HeadsetB0(0x22A9, "SteelSeries Arctis Nova 7 Diablo IV", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x227A, "SteelSeries Arctis Nova 7 WoW Edition", SteelSeriesProtocolKind.Nova7Discrete),
        HeadsetB0(0x2232, "SteelSeries Arctis Nova 5", SteelSeriesProtocolKind.Nova5),
        HeadsetB0(0x2253, "SteelSeries Arctis Nova 5X", SteelSeriesProtocolKind.Nova5),
        HeadsetB0(0x220A, "SteelSeries Arctis Nova 7P", SteelSeriesProtocolKind.Nova7Discrete),
        HeadsetB0(0x22A7, "SteelSeries Arctis Nova 7P", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x2298, "SteelSeries Arctis Nova 7P", SteelSeriesProtocolKind.Nova7),
        HeadsetB0(0x2269, "SteelSeries Arctis Nova 3P Wireless", SteelSeriesProtocolKind.Nova5),
        HeadsetB0(0x226D, "SteelSeries Arctis Nova 3X Wireless", SteelSeriesProtocolKind.Nova5),
        HeadsetB0(0x220E, "SteelSeries Arctis 7+", SteelSeriesProtocolKind.Arctis7Plus),
        HeadsetB0(0x2212, "SteelSeries Arctis 7+ PS5", SteelSeriesProtocolKind.Arctis7Plus),
        HeadsetB0(0x2216, "SteelSeries Arctis 7+ Xbox", SteelSeriesProtocolKind.Arctis7Plus),
        HeadsetB0(0x2236, "SteelSeries Arctis 7+ Destiny", SteelSeriesProtocolKind.Arctis7Plus),
        HeadsetB0(0x230A, "SteelSeries Arctis GameBuds", SteelSeriesProtocolKind.GameBuds),
        HeadsetB0(0x2317, "SteelSeries Arctis GameBuds X", SteelSeriesProtocolKind.GameBuds),

        // === 经典 Arctis 与 Nova Pro Wireless 底座耳机 ===
        new(0x12B3, "SteelSeries Arctis 1 Wireless", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis1, If3, 0xFF43),
        new(0x12B6, "SteelSeries Arctis 1 Wireless Xbox", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis1, If3, 0xFF43),
        new(0x12D7, "SteelSeries Arctis 7X", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis1, If3, 0xFF43),
        new(0x12D5, "SteelSeries Arctis 7P", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis1, If3, 0xFF43),
        new(0x12AD, "SteelSeries Arctis 7 (2018)", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis7_2018, If5),
        new(0x1260, "SteelSeries Arctis 7", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis7, If5),
        new(0x1252, "SteelSeries Arctis Pro Wireless 2019", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis7, If5),
        new(0x12C2, "SteelSeries Arctis 9", DeviceKind.Headset, SteelSeriesProtocolKind.Arctis9, If0),
        new(0x1290, "SteelSeries Arctis Pro Wireless", DeviceKind.Headset, SteelSeriesProtocolKind.ArctisProWireless, If0),
        new(0x12E0, "SteelSeries Arctis Nova Pro Wireless", DeviceKind.Headset, SteelSeriesProtocolKind.NovaProWireless, If34),
        new(0x12E5, "SteelSeries Arctis Nova Pro Wireless X", DeviceKind.Headset, SteelSeriesProtocolKind.NovaProWireless, If34),

        // === 无线鼠标（FFC0:0001，优先 MI_03） ===
        Mouse(0x1830, "SteelSeries Rival 3 Wireless", SteelSeriesProtocolKind.Rival3Mouse),
        Mouse(0x1872, "SteelSeries Rival 3 Wireless Gen 2", SteelSeriesProtocolKind.Rival3Mouse),
        Mouse(0x1838, "SteelSeries Aerox 3 Wireless", SteelSeriesProtocolKind.AeroxMouse),
        Mouse(0x1878, "SteelSeries Aerox 3 Wireless CS2 Dragon Lore", SteelSeriesProtocolKind.AeroxMouse),
        Mouse(0x1852, "SteelSeries Aerox 5 Wireless", SteelSeriesProtocolKind.AeroxMouse),
        Mouse(0x185C, "SteelSeries Aerox 5 Wireless Destiny 2 Edition", SteelSeriesProtocolKind.AeroxMouse),
        Mouse(0x1860, "SteelSeries Aerox 5 Wireless Diablo IV Edition", SteelSeriesProtocolKind.AeroxMouse),
        Mouse(0x1858, "SteelSeries Aerox 9 Wireless", SteelSeriesProtocolKind.AeroxMouse),
        Mouse(0x1874, "SteelSeries Aerox 9 Wireless WoW Edition", SteelSeriesProtocolKind.AeroxMouse)
    ];

    private static SteelSeriesModel HeadsetB0(ushort pid, string name, SteelSeriesProtocolKind protocol) =>
        new(pid, name, DeviceKind.Headset, protocol, AnyIf, ConfigUsagePage, ConfigUsage);

    private static SteelSeriesModel Mouse(ushort pid, string name, SteelSeriesProtocolKind protocol) =>
        new(pid, name, DeviceKind.Mouse, protocol, AnyIf, ConfigUsagePage, ConfigUsage);

    public static IReadOnlyList<SteelSeriesModel> AllModels => Models;

    public static SteelSeriesModel? Find(ushort? pid) =>
        pid.HasValue ? Array.Find(Models, model => model.Pid == pid.Value) : null;

    public static byte[] Encode(int length, params byte[] prefix)
    {
        var frame = new byte[length];
        prefix.CopyTo(frame, 0);
        if (!AllowsRequest(frame))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
        return frame;
    }

    public static bool AllowsRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
            return false;
        // 严格只读查询白名单：禁止任何灯效、EQ、侧音、DPI 或配对写入。
        if (frame[0] == 0x00 && frame[1] is 0xB0 or 0xD2 or 0x20)
            return !frame[2..].ContainsAnyExcept((byte)0);
        if (frame.Length >= 3 && frame[0] == 0x00 && frame[1] == 0xAA && frame[2] == 0x01)
            return !frame[3..].ContainsAnyExcept((byte)0);
        if (frame[0] == 0x06 && frame[1] is 0xB0 or 0x12 or 0x14 or 0x18)
            return !frame[2..].ContainsAnyExcept((byte)0);
        if (frame[0] is 0x40 or 0x41 && frame[1] == 0xAA)
            return !frame[2..].ContainsAnyExcept((byte)0);
        return false;
    }

    internal readonly record struct DecodedBattery(bool Matched, bool Online, int? Level, bool IsEstimated, ChargingState Charging);

    private static ReadOnlySpan<byte> StripZeroReportId(ReadOnlySpan<byte> frame, byte expectedFirst) =>
        frame.Length > 1 && frame[0] == 0x00 && frame[1] == expectedFirst ? frame[1..] : frame;

    public static DecodedBattery DecodeB0(ReadOnlySpan<byte> raw, SteelSeriesProtocolKind protocol)
    {
        var r = StripZeroReportId(raw, 0xB0);
        if (r.Length < 4 || r[0] != 0xB0)
            return default;
        switch (protocol)
        {
            case SteelSeriesProtocolKind.Nova7:
                if (r[1] != 0x03 || r[3] == 0x00)
                    return new(true, false, null, false, ChargingState.Unknown);
                return new(true, true, Math.Min(r[2], (byte)100), false,
                    r[3] is 0x01 or 0x02 ? ChargingState.Charging : ChargingState.NotCharging);
            case SteelSeriesProtocolKind.Nova7Discrete:
                if (r[1] != 0x03 || r[3] == 0x00)
                    return new(true, false, null, true, ChargingState.Unknown);
                return new(true, true, Math.Min(r[2], (byte)4) * 25, true,
                    r[3] is 0x01 or 0x02 ? ChargingState.Charging : ChargingState.NotCharging);
            case SteelSeriesProtocolKind.Nova5:
                if (r.Length < 5)
                    return default;
                if (r[1] == 0x02)
                    return new(true, false, null, false, ChargingState.Unknown);
                return new(true, true, Math.Min(r[3], (byte)100), false,
                    r[4] == 0x01 ? ChargingState.Charging : ChargingState.NotCharging);
            case SteelSeriesProtocolKind.Arctis7Plus:
                if (r[1] == 0x01)
                    return new(true, false, null, true, ChargingState.Unknown);
                return new(true, true, Math.Min(r[2], (byte)4) * 25, true,
                    r[3] == 0x01 ? ChargingState.Charging : ChargingState.NotCharging);
            case SteelSeriesProtocolKind.GameBuds:
                if (r.Length < 7)
                    return default;
                int? minLevel = null;
                if (r[3] == 0x03)
                    minLevel = Math.Min(r[5], (byte)100);
                if (r[4] == 0x03)
                {
                    int right = Math.Min(r[6], (byte)100);
                    minLevel = minLevel.HasValue ? Math.Min(minLevel.Value, right) : right;
                }
                return minLevel.HasValue
                    ? new(true, true, minLevel.Value, false, ChargingState.NotCharging)
                    : new(true, false, null, false, ChargingState.Unknown);
            default:
                return default;
        }
    }

    public static DecodedBattery DecodeArctis1(ReadOnlySpan<byte> r)
    {
        if (r.Length < 4 || r[0] != 0x06 || r[1] != 0x12)
            return default;
        if (r[2] == 0x01)
            return new(true, false, null, false, ChargingState.Unknown);
        return new(true, true, Math.Min(r[3], (byte)100), false, ChargingState.Unknown);
    }

    public static bool TryDecodeArctis7Link(ReadOnlySpan<byte> r, out bool connected)
    {
        connected = false;
        if (r.Length < 4 || r[0] != 0x06 || r[1] != 0x14)
            return false;
        connected = r[2] == 0x03;
        return true;
    }

    public static DecodedBattery DecodeArctis7Level(ReadOnlySpan<byte> r, bool zeroMeansOffline)
    {
        if (r.Length < 4 || r[0] != 0x06 || r[1] != 0x18)
            return default;
        if (zeroMeansOffline && r[2] == 0)
            return new(true, false, null, false, ChargingState.Unknown);
        return new(true, true, Math.Min(r[2], (byte)100), false, ChargingState.Unknown);
    }

    public static DecodedBattery DecodeArctis9(ReadOnlySpan<byte> raw)
    {
        var r = raw.Length > 1 && raw[0] == 0x00 && raw[1] is 0xAA or 0x55 ? raw[1..] : raw;
        if (r.Length < 5 || r[0] is not (0xAA or 0x55))
            return default;
        if (r[0] != 0xAA || r[1] != 0x01)
            return new(true, false, null, false, ChargingState.Unknown);
        int clamped = Math.Clamp((int)r[3], 0x64, 0x9A);
        int level = (clamped - 0x64) * 100 / (0x9A - 0x64);
        return new(true, true, level, false, r[4] == 0x01 ? ChargingState.Charging : ChargingState.NotCharging);
    }

    public static bool TryDecodeProWirelessState(ReadOnlySpan<byte> raw, out bool online)
    {
        online = false;
        var r = raw.Length > 1 && raw[0] == 0x00 && raw[1] is 0x02 or 0x04 ? raw[1..] : raw;
        if (r.Length < 1 || r[0] is not (0x02 or 0x04))
            return false;
        online = r[0] == 0x04;
        return true;
    }

    public static bool TryDecodeProWirelessLevel(ReadOnlySpan<byte> raw, out int level)
    {
        level = 0;
        var r = raw.Length > 1 && raw[0] == 0x00 && raw[1] <= 4 ? raw[1..] : raw;
        if (r.Length < 1 || r[0] > 4)
            return false;
        level = r[0] * 25;
        return true;
    }

    public static DecodedBattery DecodeNovaPro(ReadOnlySpan<byte> r)
    {
        if (r.Length < 16 || r[15] is not (0x01 or 0x02 or 0x08))
            return default;
        if (r[15] == 0x01)
            return new(true, false, null, true, ChargingState.Unknown);
        if (r[6] > 8)
            return default;
        int level = r[6] * 100 / 8;
        return new(true, true, level, true, r[15] == 0x02 ? ChargingState.Charging : ChargingState.NotCharging);
    }

    public static DecodedBattery DecodeAerox(ReadOnlySpan<byte> raw)
    {
        var r = StripZeroReportId(raw, 0xD2);
        if (r.Length < 2 || r[0] != 0xD2)
            return default;
        byte value = r[1];
        int rawLevel = value & 0x7F;
        if (rawLevel == 0)
            return new(true, false, null, false, ChargingState.Unknown);
        bool estimated = rawLevel <= 21;
        int level = estimated ? (rawLevel - 1) * 5 : Math.Min(rawLevel, 100);
        return new(true, true, level, estimated,
            (value & 0x80) != 0 ? ChargingState.Charging : ChargingState.NotCharging);
    }

    public static DecodedBattery DecodeRival3(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty)
            return default;
        var r = raw.Length > 1 && raw[0] == 0x00 ? raw[1..] : raw;
        if (r.Length >= 4 && r[0] == 0xAA)
        {
            if (r[1] > 100)
                return default;
            return new(true, true, r[1], false, r[3] != 0 ? ChargingState.Charging : ChargingState.NotCharging);
        }
        if (r.Length >= 3 && r[0] <= 100 && r[2] is 0 or 1)
            return new(true, true, r[0], false, r[2] == 1 ? ChargingState.Charging : ChargingState.NotCharging);
        return default;
    }

    public static string FamilyName(SteelSeriesProtocolKind protocol) => protocol switch
    {
        SteelSeriesProtocolKind.Nova7 or SteelSeriesProtocolKind.Nova7Discrete or
            SteelSeriesProtocolKind.Nova5 or SteelSeriesProtocolKind.Arctis7Plus or
            SteelSeriesProtocolKind.GameBuds => "SteelSeries B0",
        SteelSeriesProtocolKind.NovaProWireless => "SteelSeries Nova Pro",
        SteelSeriesProtocolKind.AeroxMouse => "SteelSeries Aerox D2",
        SteelSeriesProtocolKind.Rival3Mouse => "SteelSeries Rival AA",
        _ => "SteelSeries Arctis"
    };
}

internal sealed class SteelSeriesAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static SteelSeriesModel? Match(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _) || hid.VendorId != SteelSeriesProtocol.VendorId ||
            hid.MetadataIssue != ReadIssue.None || hid.UsagePage is not >= 0xFF00 ||
            hid.InputLength is not >= 8 || hid.OutputLength is not >= 8)
            return null;
        var model = SteelSeriesProtocol.Find(hid.ProductId);
        if (model is null)
            return null;
        if (model.UsagePage is ushort page && hid.UsagePage != page)
            return null;
        if (model.Usage is ushort usage && hid.Usage != usage)
            return null;
        if (model.Interfaces.Length > 0 && (hid.InterfaceNumber is not byte mi || !model.Interfaces.Contains(mi)))
            return null;
        if (model.Protocol == SteelSeriesProtocolKind.ArctisProWireless && hid.OutputLength < 31)
            return null;
        if (model.Protocol == SteelSeriesProtocolKind.NovaProWireless && hid.InputLength < 16)
            return null;
        return model;
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(hid => Match(hid) is not null)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            // 对同接收器的多个厂商集合：FFC0 优先选 MI_03；其余取匹配集合（含经典底座多集合时保留首选）。
            var ordered = group.OrderBy(hid => hid.InterfaceNumber == 3 ? 0 : 1)
                .ThenBy(hid => hid.InterfaceNumber ?? 99).ToArray();
            var endpoint = ordered[0];
            if (gates?.IsBlocked(endpoint, out _) == true)
                continue;
            var model = Match(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"steelseries:{endpoint.ReceiverId}", model.Name, model.Kind,
                AdaptationSource.Catalog, tier)
            {
                SourceLabel = "赛睿无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "赛睿控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        int outLen = endpoint.OutputLength!.Value;
        int inLen = endpoint.InputLength!.Value;

        if (model.Protocol == SteelSeriesProtocolKind.Arctis7_2018)
            return await ReadArctis7_2018Async(target, endpoint, outLen, inLen, transport, trace, token).ConfigureAwait(false);
        if (model.Protocol == SteelSeriesProtocolKind.ArctisProWireless)
            return await ReadArctisProAsync(target, endpoint, outLen, inLen, transport, trace, token).ConfigureAwait(false);

        byte[] request = model.Protocol switch
        {
            SteelSeriesProtocolKind.Arctis1 => SteelSeriesProtocol.Encode(outLen, 0x06, 0x12),
            SteelSeriesProtocolKind.Arctis7 => SteelSeriesProtocol.Encode(outLen, 0x06, 0x18),
            SteelSeriesProtocolKind.Arctis9 => SteelSeriesProtocol.Encode(outLen, 0x00, 0x20),
            SteelSeriesProtocolKind.NovaProWireless => SteelSeriesProtocol.Encode(outLen, 0x06, 0xB0),
            SteelSeriesProtocolKind.AeroxMouse => SteelSeriesProtocol.Encode(outLen, 0x00, 0xD2),
            SteelSeriesProtocolKind.Rival3Mouse => SteelSeriesProtocol.Encode(outLen, 0x00, 0xAA, 0x01),
            _ => SteelSeriesProtocol.Encode(outLen, 0x00, 0xB0)
        };
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = request[0] }, token);
        var frame = await HidQuery.OutputAsync(session, session, request, inLen,
            packet => Decode(packet, model.Protocol).Matched, trace, token, milliseconds: 800).ConfigureAwait(false);
        if (frame is null)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep,
                DateTimeOffset.UtcNow, ReadIssue.Timeout, "赛睿接收器查询超时，设备可能正在休眠或已关机");

        var decoded = Decode(frame, model.Protocol);
        if (!decoded.Matched)
            return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "赛睿回复格式不符");
        if (!decoded.Online || decoded.Level is null)
        {
            var offlineLink = model.Kind == DeviceKind.Mouse ? LinkState.Asleep : LinkState.Offline;
            return new(target.Device, null, false, ChargingState.Unknown, offlineLink,
                DateTimeOffset.UtcNow, Detail: model.Kind == DeviceKind.Mouse ? "赛睿鼠标处于休眠状态" : "赛睿耳机未连接或已关机");
        }
        return new(target.Device, decoded.Level.Value, decoded.IsEstimated, decoded.Charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadArctis7_2018Async(
        DeviceTarget target, HidInterface endpoint, int outLen, int inLen,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x06 }, token);
        var linkReq = SteelSeriesProtocol.Encode(outLen, 0x06, 0x14);
        var linkFrame = await HidQuery.OutputAsync(session, session, linkReq, inLen,
            packet => SteelSeriesProtocol.TryDecodeArctis7Link(packet, out _), trace, token, milliseconds: 800).ConfigureAwait(false);
        if (linkFrame is null)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (!SteelSeriesProtocol.TryDecodeArctis7Link(linkFrame, out bool connected) || !connected)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "Arctis 7 未连接或已关机");

        var battReq = SteelSeriesProtocol.Encode(outLen, 0x06, 0x18);
        var battFrame = await HidQuery.OutputAsync(session, session, battReq, inLen,
            packet => SteelSeriesProtocol.DecodeArctis7Level(packet, false).Matched, trace, token, milliseconds: 800).ConfigureAwait(false);
        if (battFrame is null)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        var decoded = SteelSeriesProtocol.DecodeArctis7Level(battFrame, false);
        return new(target.Device, decoded.Level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadArctisProAsync(
        DeviceTarget target, HidInterface endpoint, int outLen, int inLen,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint, token);
        var stateReq = SteelSeriesProtocol.Encode(outLen, 0x41, 0xAA);
        var stateFrame = await HidQuery.OutputAsync(session, session, stateReq, inLen,
            packet => SteelSeriesProtocol.TryDecodeProWirelessState(packet, out _), trace, token, milliseconds: 800).ConfigureAwait(false);
        if (stateFrame is null)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (!SteelSeriesProtocol.TryDecodeProWirelessState(stateFrame, out bool online) || !online)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "Arctis Pro Wireless 未连接或已关机");

        var levelReq = SteelSeriesProtocol.Encode(outLen, 0x40, 0xAA);
        var levelFrame = await HidQuery.OutputAsync(session, session, levelReq, inLen,
            packet => SteelSeriesProtocol.TryDecodeProWirelessLevel(packet, out _), trace, token, milliseconds: 800).ConfigureAwait(false);
        if (levelFrame is null || !SteelSeriesProtocol.TryDecodeProWirelessLevel(levelFrame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        return new(target.Device, level, true, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    internal static SteelSeriesProtocol.DecodedBattery Decode(ReadOnlySpan<byte> frame, SteelSeriesProtocolKind protocol) => protocol switch
    {
        SteelSeriesProtocolKind.Arctis1 => SteelSeriesProtocol.DecodeArctis1(frame),
        SteelSeriesProtocolKind.Arctis7 => SteelSeriesProtocol.DecodeArctis7Level(frame, true),
        SteelSeriesProtocolKind.Arctis9 => SteelSeriesProtocol.DecodeArctis9(frame),
        SteelSeriesProtocolKind.NovaProWireless => SteelSeriesProtocol.DecodeNovaPro(frame),
        SteelSeriesProtocolKind.AeroxMouse => SteelSeriesProtocol.DecodeAerox(frame),
        SteelSeriesProtocolKind.Rival3Mouse => SteelSeriesProtocol.DecodeRival3(frame),
        _ => SteelSeriesProtocol.DecodeB0(frame, protocol)
    };

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is null ||
            gates?.IsBlocked(endpoint, out _) == true)
            return null;
        var reading = await ReadAsync(target, transport, null, token).ConfigureAwait(false);
        return new(endpoint.ReceiverId, null, target.Device.Id, reading.Link, reading.Issue)
        {
            Battery = reading.Link == LinkState.Online ? reading : null
        };
    }
}

namespace Jige.Core.Adapters;

internal enum HyperXProtocolKind
{
    Cloud2Hp,
    Cloud2Kingston,
    CloudFlight,
    CloudAlpha,
    Cloud3,
    Cloud3S
}

internal sealed record HyperXModel(
    ushort VendorId,
    ushort Pid,
    string Name,
    HyperXProtocolKind Protocol);

/// <summary>
/// HyperX 无线耳机纯只读电量协议。
/// 协议事实依据 Sapd/HeadsetControl（GPL 事实）、HyperHeadset、HyperXBatteryMonitor 与 HaloBattery（MIT）独立编写。
/// </summary>
internal static class HyperXHeadsetProtocol
{
    public const ushort HpVendorId = 0x03F0;
    public const ushort KingstonVendorId = 0x0951;

    private static readonly byte[] KingstonPreamble =
        [0x06, 0x00, 0x02, 0x00, 0x9A, 0x00, 0x00, 0x68, 0x4A, 0x8E, 0x0A, 0x00, 0x00, 0x00, 0xBB];

    private static readonly HyperXModel[] Models =
    [
        new(HpVendorId, 0x0696, "HyperX Cloud II Wireless", HyperXProtocolKind.Cloud2Hp),
        new(HpVendorId, 0x018B, "HyperX Cloud II Wireless", HyperXProtocolKind.Cloud2Hp),
        new(KingstonVendorId, 0x1718, "HyperX Cloud II Wireless", HyperXProtocolKind.Cloud2Kingston),
        new(KingstonVendorId, 0x16C4, "HyperX Cloud Flight Wireless", HyperXProtocolKind.CloudFlight),
        new(KingstonVendorId, 0x1723, "HyperX Cloud Flight Wireless", HyperXProtocolKind.CloudFlight),
        new(HpVendorId, 0x098D, "HyperX Cloud Alpha Wireless", HyperXProtocolKind.CloudAlpha),
        new(HpVendorId, 0x05B7, "HyperX Cloud III Wireless", HyperXProtocolKind.Cloud3),
        new(HpVendorId, 0x0C9D, "HyperX Cloud III Wireless", HyperXProtocolKind.Cloud3),
        new(HpVendorId, 0x0995, "HyperX Cloud II Core Wireless", HyperXProtocolKind.Cloud3),
        new(HpVendorId, 0x0795, "HyperX Cloud II Core Wireless", HyperXProtocolKind.Cloud3),
        new(HpVendorId, 0x02CC, "HyperX Cloud III S Wireless", HyperXProtocolKind.Cloud3S),
        new(HpVendorId, 0x06BE, "HyperX Cloud III S Wireless", HyperXProtocolKind.Cloud3S)
    ];

    public static IReadOnlyList<HyperXModel> AllModels => Models;

    public static HyperXModel? Find(ushort? vid, ushort? pid) =>
        vid is ushort v && pid is ushort p ? Array.Find(Models, m => m.VendorId == v && m.Pid == p) : null;

    internal static bool IsCloud3SOutputCandidate(ushort? vid, ushort? pid, ushort? usagePage,
        byte? interfaceNumber, ushort? outputLength) =>
        vid == HpVendorId && Find(vid, pid)?.Protocol == HyperXProtocolKind.Cloud3S &&
        usagePage is >= 0xFF00 && interfaceNumber is null or 3 && outputLength is >= 64;

    public static byte[] EncodeCloud2Hp(int length, byte cmd)
    {
        var frame = new byte[length];
        frame[0] = 0x06;
        frame[1] = 0xFF;
        frame[2] = 0xBB;
        frame[3] = cmd;
        return frame;
    }

    public static byte[] EncodeCloud2Kingston(int length, byte cmd)
    {
        var frame = new byte[length];
        KingstonPreamble.CopyTo(frame, 0);
        frame[15] = cmd;
        return frame;
    }

    public static byte[] EncodeCloudFlight(int length)
    {
        var frame = new byte[length];
        frame[0] = 0x21;
        frame[1] = 0xFF;
        frame[2] = 0x05;
        return frame;
    }

    public static byte[] EncodeCloudAlpha(int length, byte selector)
    {
        var frame = new byte[length];
        frame[0] = 0x21;
        frame[1] = 0xBB;
        frame[2] = selector;
        return frame;
    }

    public static byte[] EncodeCloud3(int length, byte cmd)
    {
        var frame = new byte[length];
        frame[0] = 0x66;
        frame[1] = cmd;
        return frame;
    }

    public static byte[] EncodeCloud3S(int length, byte cmd)
    {
        // Byte 3 = 0x01 表示只读 GET（0x00 为 SET 写入，严禁发送）。
        var frame = new byte[length];
        frame[0] = 0x0C;
        frame[1] = 0x02;
        frame[2] = 0x03;
        frame[3] = 0x01;
        frame[4] = 0x00;
        frame[5] = cmd;
        return frame;
    }

    public static bool AllowsRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length >= 52 && frame[0] == 0x06 && frame[1] == 0xFF && frame[2] == 0xBB && frame[3] is 0x02 or 0x03)
            return !frame[4..].ContainsAnyExcept((byte)0);
        if (frame.Length >= 62 && frame[..KingstonPreamble.Length].SequenceEqual(KingstonPreamble) && frame[15] is 0x02 or 0x03)
            return !frame[16..].ContainsAnyExcept((byte)0);
        if (frame.Length >= 20 && frame[0] == 0x21 && frame[1] == 0xFF && frame[2] == 0x05)
            return !frame[3..].ContainsAnyExcept((byte)0);
        if (frame.Length >= 31 && frame[0] == 0x21 && frame[1] == 0xBB && frame[2] is 0x03 or 0x0B or 0x0C)
            return !frame[3..].ContainsAnyExcept((byte)0);
        if (frame.Length >= 62 && frame[0] == 0x66 && frame[1] is 0x89 or 0x8A)
            return !frame[2..].ContainsAnyExcept((byte)0);
        if (frame.Length >= 64 && frame[0] == 0x0C && frame[1] == 0x02 && frame[2] == 0x03 &&
            frame[3] == 0x01 && frame[4] == 0x00 && frame[5] is 0x06 or 0x48)
            return !frame[6..].ContainsAnyExcept((byte)0);
        return false;
    }

    public static bool TryDecodeCloud2HpLevel(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length < 8 || r[0] != 0x06 || r[1] != 0xFF || r[2] != 0xBB || r[3] != 0x02 || r[7] > 100)
            return false;
        level = r[7];
        return true;
    }

    public static bool TryDecodeCloud2HpCharging(ReadOnlySpan<byte> r, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (r.Length < 5 || r[0] != 0x06 || r[1] != 0xFF || r[2] != 0xBB || r[3] != 0x03)
            return false;
        charging = r[4] == 1 ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool TryDecodeCloud2KingstonLevel(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length < 8 || r[0] != 0x0B || r[2] != 0xBB || r[3] != 0x02 || r[7] > 100)
            return false;
        level = r[7];
        return true;
    }

    public static bool TryDecodeCloud2KingstonCharging(ReadOnlySpan<byte> r, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (r.Length < 5 || r[0] != 0x0B || r[2] != 0xBB || r[3] != 0x03)
            return false;
        charging = r[4] switch
        {
            0 => ChargingState.NotCharging,
            1 => ChargingState.Charging,
            2 => ChargingState.Full,
            _ => ChargingState.Unknown
        };
        return r[4] <= 2;
    }

    public static bool TryDecodeCloudFlight(ReadOnlySpan<byte> r, out bool offline, out int? level, out ChargingState charging)
    {
        offline = false;
        level = null;
        charging = ChargingState.Unknown;
        // 与 Match(InputLength >= 15) 对齐：兼容 15B、20B 以及 Windows HID 集合按更大 caps 补零后的输入报告。
        if (r.Length < 15 || !r.ContainsAnyExcept((byte)0))
            return false;
        // 拒绝静音/电源事件（0x64）与随机无头噪声；允许 21 FF 05 回复、0x65 状态报告，以及零头非零电压测试帧。
        bool validHeader = (r[0] == 0x21 && r[1] == 0xFF && r[2] == 0x05)
            || r[0] == 0x65
            || (r[0] == 0x00 && r[1] == 0x00 && r[2] == 0x00 && (r[3] != 0 || r[4] != 0));
        if (!validHeader)
            return false;
        int voltage = (r[3] << 8) | r[4];
        if (voltage == 0)
        {
            offline = true;
            return true;
        }
        if (voltage > 0x100B)
        {
            charging = ChargingState.Charging;
            return true;
        }
        level = EstimateCloudFlightLevel(voltage);
        charging = ChargingState.NotCharging;
        return true;
    }

    public static int EstimateCloudFlightLevel(int voltage)
    {
        if (voltage <= 3648)
            return Math.Clamp((int)Math.Round(0.00125 * voltage), 0, 100);
        if (voltage > 3975)
            return 100;
        double v = voltage;
        double estimated = 0.00000002547505 * v * v * v * v
            - 0.0003900299 * v * v * v
            + 2.238321 * v * v
            - 5706.256 * v
            + 5452299.0;
        return Math.Clamp((int)Math.Round(estimated), 0, 100);
    }

    public static bool TryDecodeCloudAlphaConnected(ReadOnlySpan<byte> r, out bool connected)
    {
        connected = false;
        if (r.Length < 4 || r[0] != 0x21 || r[1] != 0xBB || r[2] != 0x03)
            return false;
        connected = r[3] != 1;
        return true;
    }

    public static bool TryDecodeCloudAlphaLevel(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length < 4 || r[0] != 0x21 || r[1] != 0xBB || r[2] != 0x0B || r[3] > 100)
            return false;
        level = r[3];
        return true;
    }

    public static bool TryDecodeCloudAlphaCharging(ReadOnlySpan<byte> r, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (r.Length < 4 || r[0] != 0x21 || r[1] != 0xBB || r[2] != 0x0C || r[3] > 1)
            return false;
        charging = r[3] == 1 ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool TryDecodeCloud3Level(ReadOnlySpan<byte> r, out bool noReading, out int level)
    {
        noReading = false;
        level = 0;
        if (r.Length < 5 || r[0] != 0x66 || r[1] is not (0x89 or 0x0D))
            return false;
        if (r[2] == 0 && r[3] == 0)
        {
            noReading = true;
            return true;
        }
        if (r[4] > 100)
            return false;
        level = r[4];
        return true;
    }

    public static bool TryDecodeCloud3Charging(ReadOnlySpan<byte> r, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (r.Length < 3 || r[0] != 0x66 || r[1] is not (0x8A or 0x0C) || r[2] > 2)
            return false;
        charging = r[2] switch
        {
            0 => ChargingState.NotCharging,
            1 => ChargingState.Charging,
            _ => ChargingState.Full
        };
        return true;
    }

    private static bool IsCloud3SRequestEcho(ReadOnlySpan<byte> r, byte cmd) =>
        r.Length >= 6 && r[0] == 0x0C && r[1] == 0x02 && r[2] == 0x03 &&
        r[3] == 0x01 && r[4] == 0x00 && r[5] == cmd && !r[6..].ContainsAnyExcept((byte)0);

    public static bool TryDecodeCloud3SLevel(ReadOnlySpan<byte> r, out int level)
    {
        level = 0;
        if (r.Length >= 7 && r[0] == 0x0C && r[5] == 0x06 && r[6] <= 100)
        {
            if (IsCloud3SRequestEcho(r, 0x06))
                return false;
            level = r[6];
            return true;
        }
        if (r.Length >= 6 && r[0] == 0x0D && r[4] == 1 && r[5] <= 100)
        {
            level = r[5];
            return true;
        }
        return false;
    }

    public static bool TryDecodeCloud3SCharging(ReadOnlySpan<byte> r, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        byte state;
        if (r.Length >= 7 && r[0] == 0x0C && r[5] == 0x48)
        {
            if (IsCloud3SRequestEcho(r, 0x48))
                return false;
            state = r[6];
        }
        else if (r.Length >= 6 && r[0] == 0x0D && r[4] == 10)
            state = r[5];
        else
            return false;
        if (state > 2)
            return false;
        charging = state switch
        {
            0 => ChargingState.NotCharging,
            1 => ChargingState.Charging,
            _ => ChargingState.Full
        };
        return true;
    }

    public static string FamilyName(HyperXProtocolKind protocol) => protocol switch
    {
        HyperXProtocolKind.Cloud2Hp => "HyperX Cloud II (HP)",
        HyperXProtocolKind.Cloud2Kingston => "HyperX Cloud II (Kingston)",
        HyperXProtocolKind.CloudFlight => "HyperX Cloud Flight",
        HyperXProtocolKind.CloudAlpha => "HyperX Cloud Alpha",
        HyperXProtocolKind.Cloud3 => "HyperX Cloud III",
        HyperXProtocolKind.Cloud3S => "HyperX Cloud III S",
        _ => "HyperX"
    };
}

internal sealed class HyperXHeadsetAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static HyperXModel? Match(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _) || hid.MetadataIssue != ReadIssue.None)
            return null;
        var model = HyperXHeadsetProtocol.Find(hid.VendorId, hid.ProductId);
        if (model is null)
            return null;
        return model.Protocol switch
        {
            HyperXProtocolKind.Cloud2Hp => hid is { UsagePage: 0xFF90, Usage: 0x0303, OutputLength: >= 52, InputLength: >= 20 } ? model : null,
            HyperXProtocolKind.Cloud2Kingston => hid is { UsagePage: 0xFF13, Usage: 0x0001, OutputLength: >= 62, InputLength: >= 8 } ? model : null,
            HyperXProtocolKind.CloudFlight => hid is { UsagePage: 0xFF53 or 0xFF90, Usage: 0x0303, OutputLength: >= 20, InputLength: >= 15 } ? model : null,
            HyperXProtocolKind.CloudAlpha => hid is { UsagePage: 0xFF43, Usage: 0x0202, OutputLength: >= 31, InputLength: >= 4 } ? model : null,
            HyperXProtocolKind.Cloud3 => hid is { UsagePage: 0xFF13, Usage: 0x0001, InputLength: >= 5 } &&
                (hid.OutputLength >= 62 || hid.FeatureLength >= 62) ? model : null,
            HyperXProtocolKind.Cloud3S => HyperXHeadsetProtocol.IsCloud3SOutputCandidate(
                hid.VendorId, hid.ProductId, hid.UsagePage, hid.InterfaceNumber, hid.OutputLength) &&
                (hid.OutputReportIds is null || hid.OutputReportIds.Split(',').Contains("0C", StringComparer.OrdinalIgnoreCase)) ? model : null,
            _ => null
        };
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(hid => HyperXHeadsetProtocol.Find(hid.VendorId, hid.ProductId) is not null)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var preferred = group.Select(hid => (Hid: hid, Model: Match(hid))).Where(item => item.Model is not null)
                .GroupBy(item => (Declared: item.Model!.Protocol == HyperXProtocolKind.Cloud3S && item.Hid.OutputReportIds is not null,
                    Interface3: item.Hid.InterfaceNumber == 3))
                .OrderByDescending(rank => rank.Key.Declared)
                .ThenByDescending(rank => rank.Key.Interface3)
                .FirstOrDefault();
            if (preferred is null)
                continue;
            var (endpoint, model) = preferred.First();
            bool cloud3S = model!.Protocol == HyperXProtocolKind.Cloud3S;
            if ((cloud3S && preferred.Skip(1).Any()) || gates?.IsBlocked(endpoint, out _) == true)
                continue;

            HidInterface[] related = cloud3S
                ? group.Where(hid => hid != endpoint && hid.VendorId == endpoint.VendorId && hid.ProductId == endpoint.ProductId &&
                    hid.MetadataIssue == ReadIssue.None && hid.UsagePage is >= 0xFF00 && hid.InputLength is >= 6).ToArray()
                : [];
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"hyperx:{endpoint.ReceiverId}", model.Name, DeviceKind.Headset,
                AdaptationSource.Catalog, tier)
            {
                SourceLabel = "HyperX 无线接收器"
            }, endpoint, "0")
            {
                RelatedHids = related
            };
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "HyperX 控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        return model.Protocol switch
        {
            HyperXProtocolKind.Cloud2Hp => await ReadCloud2HpAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            HyperXProtocolKind.Cloud2Kingston => await ReadCloud2KingstonAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            HyperXProtocolKind.CloudFlight => await ReadCloudFlightAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            HyperXProtocolKind.CloudAlpha => await ReadCloudAlphaAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            HyperXProtocolKind.Cloud3 => await ReadCloud3Async(target, endpoint, transport, trace, token).ConfigureAwait(false),
            HyperXProtocolKind.Cloud3S => await ReadCloud3SAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            _ => AdapterRegistry.Failure(target, ReadIssue.IoError, "不支持的 HyperX 协议")
        };
    }

    private static async ValueTask<byte[]?> QueryAllowedOutputAsync(
        IHidSession session, byte[] request, int inputLength, Func<byte[], bool> matches,
        FrameTrace? trace, CancellationToken token, int milliseconds)
    {
        if (!HyperXHeadsetProtocol.AllowsRequest(request))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
        return await HidQuery.OutputAsync(session, session, request, inputLength, matches, trace, token, milliseconds)
            .ConfigureAwait(false);
    }

    private static async ValueTask<BatteryReading> ReadCloud2HpAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x06, RequiredInputReportId = 0x06 }, token);
        var levelReq = HyperXHeadsetProtocol.EncodeCloud2Hp(endpoint.OutputLength!.Value, 0x02);
        var levelFrame = await QueryAllowedOutputAsync(session, levelReq, endpoint.InputLength!.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloud2HpLevel(p, out _), trace, token, 900).ConfigureAwait(false);
        if (levelFrame is null || !HyperXHeadsetProtocol.TryDecodeCloud2HpLevel(levelFrame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);

        var chgReq = HyperXHeadsetProtocol.EncodeCloud2Hp(endpoint.OutputLength.Value, 0x03);
        var chgFrame = await QueryAllowedOutputAsync(session, chgReq, endpoint.InputLength.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloud2HpCharging(p, out _), trace, token, 700).ConfigureAwait(false);
        var charging = chgFrame is not null && HyperXHeadsetProtocol.TryDecodeCloud2HpCharging(chgFrame, out var state)
            ? state : ChargingState.Unknown;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadCloud2KingstonAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x06, RequiredInputReportId = 0x0B }, token);
        var levelReq = HyperXHeadsetProtocol.EncodeCloud2Kingston(endpoint.OutputLength!.Value, 0x02);
        var levelFrame = await QueryAllowedOutputAsync(session, levelReq, endpoint.InputLength!.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloud2KingstonLevel(p, out _), trace, token, 900).ConfigureAwait(false);
        if (levelFrame is null || !HyperXHeadsetProtocol.TryDecodeCloud2KingstonLevel(levelFrame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);

        var chgReq = HyperXHeadsetProtocol.EncodeCloud2Kingston(endpoint.OutputLength.Value, 0x03);
        var chgFrame = await QueryAllowedOutputAsync(session, chgReq, endpoint.InputLength.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloud2KingstonCharging(p, out _), trace, token, 700).ConfigureAwait(false);
        var charging = chgFrame is not null && HyperXHeadsetProtocol.TryDecodeCloud2KingstonCharging(chgFrame, out var state)
            ? state : ChargingState.Unknown;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadCloudFlightAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x21 }, token);
        var req = HyperXHeadsetProtocol.EncodeCloudFlight(endpoint.OutputLength!.Value);
        var frame = await QueryAllowedOutputAsync(session, req, endpoint.InputLength!.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloudFlight(p, out _, out _, out _), trace, token, 1200).ConfigureAwait(false);
        if (frame is null || !HyperXHeadsetProtocol.TryDecodeCloudFlight(frame, out bool offline, out int? level, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (offline)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "Cloud Flight 耳机未连接或已关机");
        return new(target.Device, level, level.HasValue, charging, LinkState.Online, DateTimeOffset.UtcNow,
            Detail: level is null ? "Cloud Flight 充电中，电压高于放电曲线上限" : null);
    }

    private static async ValueTask<BatteryReading> ReadCloudAlphaAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x21, RequiredInputReportId = 0x21 }, token);
        var connReq = HyperXHeadsetProtocol.EncodeCloudAlpha(endpoint.OutputLength!.Value, 0x03);
        var connFrame = await QueryAllowedOutputAsync(session, connReq, endpoint.InputLength!.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloudAlphaConnected(p, out _), trace, token, 900).ConfigureAwait(false);
        if (connFrame is not null && HyperXHeadsetProtocol.TryDecodeCloudAlphaConnected(connFrame, out bool connected) && !connected)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "Cloud Alpha Wireless 未连接或已关机");

        var levelReq = HyperXHeadsetProtocol.EncodeCloudAlpha(endpoint.OutputLength.Value, 0x0B);
        var levelFrame = await QueryAllowedOutputAsync(session, levelReq, endpoint.InputLength.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloudAlphaLevel(p, out _), trace, token, 900).ConfigureAwait(false);
        if (levelFrame is null || !HyperXHeadsetProtocol.TryDecodeCloudAlphaLevel(levelFrame, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);

        var chgReq = HyperXHeadsetProtocol.EncodeCloudAlpha(endpoint.OutputLength.Value, 0x0C);
        var chgFrame = await QueryAllowedOutputAsync(session, chgReq, endpoint.InputLength.Value,
            p => HyperXHeadsetProtocol.TryDecodeCloudAlphaCharging(p, out _), trace, token, 700).ConfigureAwait(false);
        var charging = chgFrame is not null && HyperXHeadsetProtocol.TryDecodeCloudAlphaCharging(chgFrame, out var state)
            ? state : ChargingState.Unknown;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadCloud3Async(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var channel = new Cloud3Channel(endpoint, transport);
        var levelFrame = await channel.QueryAsync(0x89,
            p => HyperXHeadsetProtocol.TryDecodeCloud3Level(p, out _, out _), trace, token).ConfigureAwait(false);
        if (levelFrame is null || !HyperXHeadsetProtocol.TryDecodeCloud3Level(levelFrame, out bool noReading, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (noReading)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "HyperX 接收器尚未收到耳机电量（可能已关机）");

        var chgFrame = await channel.QueryAsync(0x8A,
            p => HyperXHeadsetProtocol.TryDecodeCloud3Charging(p, out _), trace, token).ConfigureAwait(false);
        var charging = chgFrame is not null && HyperXHeadsetProtocol.TryDecodeCloud3Charging(chgFrame, out var state)
            ? state : ChargingState.Unknown;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private sealed class Cloud3Channel(HidInterface endpoint, IReceiverTransport transport) : IDisposable
    {
        private IHidSession? session;
        private IHidSession? featureSession;
        private bool useOutput = endpoint.OutputLength is >= 62;

        public async ValueTask<byte[]?> QueryAsync(byte command, Func<byte[], bool> matches, FrameTrace? trace, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(900);
            var dt = deadline.Token;
            try
            {
                if (useOutput)
                {
                    var request = Encode(command, endpoint.OutputLength!.Value);
                    try
                    {
                        session ??= transport.OpenHid(endpoint with { RequiredInputReportId = 0x66, RequiredOutputReportId = 0x66 }, dt);
                        await session.WriteOutputAsync(request, dt).ConfigureAwait(false);
                        trace?.Invoke(FrameDirection.Sent, request);
                    }
                    catch (TransportException error) when (endpoint.FeatureLength is >= 62 && error.Issue == ReadIssue.IoError &&
                        (session is not null || error.NativeCode == TransportException.ReportIdNotFound))
                    {
                        session?.Dispose();
                        session = null;
                        useOutput = false;
                    }
                }
                if (!useOutput)
                {
                    featureSession ??= transport.OpenHid(endpoint with { FeatureOnly = true, RequiredFeatureReportId = 0x66 }, dt);
                    session ??= transport.OpenHid(endpoint with { InputOnly = true, RequiredInputReportId = 0x66 }, dt);
                    var request = Encode(command, endpoint.FeatureLength!.Value);
                    await featureSession.WriteFeatureAsync(request, dt).ConfigureAwait(false);
                    trace?.Invoke(FrameDirection.Sent, request);
                }

                var report = new byte[endpoint.InputLength!.Value];
                while (true)
                {
                    dt.ThrowIfCancellationRequested();
                    int count = await session!.ReadInputAsync(report, dt).ConfigureAwait(false);
                    if (count == 0)
                        throw new TransportException(ReadIssue.Disconnected, "HID 输入通道已关闭");
                    trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                    var frame = count == report.Length ? report : report[..count];
                    if (matches(frame))
                        return frame;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return null;
            }
        }

        private static byte[] Encode(byte command, int length)
        {
            var request = HyperXHeadsetProtocol.EncodeCloud3(length, command);
            if (!HyperXHeadsetProtocol.AllowsRequest(request))
                throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");
            return request;
        }

        public void Dispose()
        {
            session?.Dispose();
            session = null;
            featureSession?.Dispose();
            featureSession = null;
        }
    }

    private static async ValueTask<BatteryReading> ReadCloud3SAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x0C }, token);
        List<(IHidSession Session, int InputLength)> siblings = [];
        try
        {
            foreach (var sib in target.RelatedHids)
            {
                if (sib.InputLength is not >= 6)
                    continue;
                try
                {
                    siblings.Add((transport.OpenHid(sib with { InputOnly = true }, token), sib.InputLength.Value));
                }
                catch (TransportException error) when (error.Issue != ReadIssue.Disconnected)
                {
                }
            }

            var levelReq = HyperXHeadsetProtocol.EncodeCloud3S(endpoint.OutputLength!.Value, 0x06);
            var levelFrame = await QueryCloud3SAsync(session, endpoint, siblings, levelReq,
                p => HyperXHeadsetProtocol.TryDecodeCloud3SLevel(p, out _), trace, token).ConfigureAwait(false);
            if (levelFrame is null || !HyperXHeadsetProtocol.TryDecodeCloud3SLevel(levelFrame, out int level))
                return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);

            var chgReq = HyperXHeadsetProtocol.EncodeCloud3S(endpoint.OutputLength.Value, 0x48);
            var chgFrame = await QueryCloud3SAsync(session, endpoint, siblings, chgReq,
                p => HyperXHeadsetProtocol.TryDecodeCloud3SCharging(p, out _), trace, token).ConfigureAwait(false);
            var charging = chgFrame is not null && HyperXHeadsetProtocol.TryDecodeCloud3SCharging(chgFrame, out var state)
                ? state : ChargingState.Unknown;
            return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
        }
        finally
        {
            foreach (var (sibSession, _) in siblings)
                sibSession.Dispose();
        }
    }

    private static async ValueTask<byte[]?> QueryCloud3SAsync(
        IHidSession primary, HidInterface endpoint, List<(IHidSession Session, int InputLength)> siblings,
        byte[] request, Func<byte[], bool> matches, FrameTrace? trace, CancellationToken token)
    {
        if (!HyperXHeadsetProtocol.AllowsRequest(request))
            throw new TransportException(ReadIssue.IoError, "报文未通过只读白名单");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(900);
        var dt = deadline.Token;
        List<(IHidSession Session, bool IsPrimary, byte[] Buffer, Task<int> Read)> listeners = [];
        TransportException? disconnected = null;
        try
        {
            if (endpoint.InputLength is >= 6)
            {
                var buf = new byte[endpoint.InputLength.Value];
                listeners.Add((primary, true, buf, primary.ReadInputAsync(buf, dt).AsTask()));
            }
            for (int i = 0; i < siblings.Count;)
            {
                var (sibSession, inputLength) = siblings[i];
                try
                {
                    var buf = new byte[inputLength];
                    listeners.Add((sibSession, false, buf, sibSession.ReadInputAsync(buf, dt).AsTask()));
                    i++;
                }
                catch (TransportException error)
                {
                    sibSession.Dispose();
                    siblings.RemoveAt(i);
                    if (error.Issue == ReadIssue.Disconnected)
                        disconnected ??= error;
                }
            }
            if (listeners.Count == 0)
            {
                if (disconnected is not null)
                    throw disconnected;
                return null;
            }

            await primary.WriteOutputAsync(request, dt).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);

            for (int attempt = 0; attempt < 16 && listeners.Count > 0; attempt++)
            {
                dt.ThrowIfCancellationRequested();
                var finished = await Task.WhenAny(listeners.Select(l => l.Read)).ConfigureAwait(false);
                int idx = listeners.FindIndex(l => l.Read == finished);
                if (idx < 0)
                    continue;
                var item = listeners[idx];
                try
                {
                    int count = await item.Read.ConfigureAwait(false);
                    if (count == 0)
                        throw new TransportException(ReadIssue.Disconnected, "HID 输入通道已关闭");
                    var frame = count == item.Buffer.Length ? item.Buffer : item.Buffer[..count];
                    trace?.Invoke(FrameDirection.Received, frame);
                    if (matches(frame))
                        return frame.ToArray();
                    // 本集合仍有排队通报时，先处理其他集合已经到达的应答。
                    var next = item.Session.ReadInputAsync(item.Buffer, dt).AsTask();
                    listeners.RemoveAt(idx);
                    listeners.Add((item.Session, item.IsPrimary, item.Buffer, next));
                }
                catch (TransportException error)
                {
                    listeners.RemoveAt(idx);
                    if (!item.IsPrimary)
                    {
                        int sibIdx = siblings.FindIndex(s => ReferenceEquals(s.Session, item.Session));
                        if (sibIdx >= 0)
                        {
                            siblings[sibIdx].Session.Dispose();
                            siblings.RemoveAt(sibIdx);
                        }
                    }
                    if (error.Issue == ReadIssue.Disconnected)
                    {
                        if (item.IsPrimary)
                            throw;
                        disconnected ??= error;
                    }
                }
            }
            if (listeners.Count == 0 && disconnected is not null)
                throw disconnected;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
        finally
        {
            deadline.Cancel();
            foreach (var item in listeners)
                await ((Task)item.Read).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        return null;
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
            Battery = reading.Link == LinkState.Online ? reading : null
        };
    }
}

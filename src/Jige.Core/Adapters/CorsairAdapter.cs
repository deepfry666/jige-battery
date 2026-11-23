namespace Jige.Core.Adapters;

internal enum CorsairProtocolKind
{
    VoidElite,
    BragiFramed,
    BragiBare,
    BragiBareSession,
    Hs80Usb,
    VoidV2
}

internal sealed record CorsairModel(
    ushort Pid,
    string Name,
    DeviceKind Kind,
    CorsairProtocolKind Protocol,
    DeviceConnection Connection = DeviceConnection.Receiver);

/// <summary>
/// Corsair 海盗船无线耳机、鼠标与键盘纯只读电量协议。
/// 严禁发送任何软件模式切换（01 03 00 02）、灯效或配置写入指令。
/// 协议事实依据 Sapd/HeadsetControl（GPL 事实）、ckb-next、OpenLinkHub、HS80_Control 与 HaloBattery（MIT）独立编写。
/// </summary>
internal static class CorsairProtocol
{
    public const ushort VendorId = 0x1B1C;
    public const ushort BragiUsagePage = 0xFF42;
    public const ushort BragiUsage = 0x0001;
    public const ushort VoidUsagePage = 0xFFC5;
    public const ushort VoidUsage = 0x0001;

    private static readonly CorsairModel[] Models =
    [
        new(0x0A55, "Corsair Void Elite Wireless", DeviceKind.Headset, CorsairProtocolKind.VoidElite),
        new(0x0A69, "Corsair HS80 RGB Wireless", DeviceKind.Headset, CorsairProtocolKind.Hs80Usb, DeviceConnection.UsbCable),
        new(0x1B7F, "Corsair Dark Core RGB Pro SE", DeviceKind.Mouse, CorsairProtocolKind.BragiFramed),
        new(0x1BDC, "Corsair M75 Wireless", DeviceKind.Mouse, CorsairProtocolKind.BragiFramed),
        new(0x2B07, "Corsair K65 PLUS Wireless", DeviceKind.Keyboard, CorsairProtocolKind.BragiFramed),
        new(0x0A40, "Corsair Virtuoso RGB Wireless SE", DeviceKind.Headset, CorsairProtocolKind.BragiBareSession),
        new(0x0A3E, "Corsair Virtuoso RGB Wireless SE", DeviceKind.Headset, CorsairProtocolKind.BragiBare),
        new(0x0A3D, "Corsair Virtuoso RGB Wireless SE", DeviceKind.Headset, CorsairProtocolKind.BragiBare, DeviceConnection.UsbCable),
        new(0x0A64, "Corsair Virtuoso RGB Wireless XT", DeviceKind.Headset, CorsairProtocolKind.BragiBare),
        new(0x0A62, "Corsair Virtuoso RGB Wireless XT", DeviceKind.Headset, CorsairProtocolKind.BragiBare, DeviceConnection.UsbCable),
        new(0x2A08, "Corsair Void v2 Wireless", DeviceKind.Headset, CorsairProtocolKind.VoidV2),
        new(0x2A02, "Corsair Virtuoso Max Wireless", DeviceKind.Headset, CorsairProtocolKind.VoidV2),
        new(0x0A97, "Corsair HS80 Max Wireless", DeviceKind.Headset, CorsairProtocolKind.VoidV2)
    ];

    public static IReadOnlyList<CorsairModel> AllModels => Models;

    public static CorsairModel? Find(ushort? pid) =>
        pid.HasValue ? Array.Find(Models, m => m.Pid == pid.Value) : null;

    public static byte[] EncodeVoidElite(int length)
    {
        var frame = new byte[length];
        frame[0] = 0xC9;
        frame[1] = 0x64;
        return frame;
    }

    public static byte[] EncodeBragiFramed(int length, byte route, byte prop)
    {
        var frame = new byte[length];
        frame[0] = 0x00;
        frame[1] = route;
        frame[2] = 0x02;
        frame[3] = prop;
        return frame;
    }

    public static byte[] EncodeBragiBare(int length, byte route, byte prop)
    {
        var frame = new byte[length];
        if (length == 65)
        {
            frame[0] = 0x00;
            frame[1] = 0x02;
            frame[2] = route;
            frame[3] = 0x02;
            frame[4] = prop;
        }
        else
        {
            frame[0] = 0x02;
            frame[1] = route;
            frame[2] = 0x02;
            frame[3] = prop;
        }
        return frame;
    }

    public static bool AllowsRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
            return false;
        // Void Elite 只读查询 C9 64（最短 2 字节，部分固件补零至 OutputLength）。
        if (frame[0] == 0xC9 && frame[1] == 0x64)
            return !frame[2..].ContainsAnyExcept((byte)0);
        if (frame.Length < 4)
            return false;
        // Bragi 有 Report ID 00 前缀（65B）：仅允许 CMD_GET (0x02) + 只读属性 0x0F/0x10/0x12/0x13。
        if (frame.Length == 65 && frame[0] == 0x00 && frame[1] is 0x08 or 0x09 &&
            frame[2] == 0x02 && frame[3] is 0x0F or 0x10 or 0x12 or 0x13)
            return !frame[4..].ContainsAnyExcept((byte)0);
        // Bragi/Void v2/HS80 裸帧（64B Report ID 02 或 65B 00 02）：仅允许 CMD_GET (0x02) + 只读属性。
        if (frame.Length == 64 && frame[0] == 0x02 && frame[1] is 0x08 or 0x09 &&
            frame[2] == 0x02 && frame[3] is 0x0F or 0x10 or 0x12 or 0x13)
            return !frame[4..].ContainsAnyExcept((byte)0);
        if (frame.Length == 65 && frame[0] == 0x00 && frame[1] == 0x02 && frame[2] is 0x08 or 0x09 &&
            frame[3] == 0x02 && frame[4] is 0x0F or 0x10 or 0x12 or 0x13)
            return !frame[5..].ContainsAnyExcept((byte)0);
        return false;
    }

    public static bool TryDecodeVoidElite(ReadOnlySpan<byte> r, out bool offline, out int level, out ChargingState charging)
    {
        offline = false;
        level = 0;
        charging = ChargingState.Unknown;
        if (r.Length < 5 || r[0] != 0x64 || r[3] != 0xB1)
            return false;
        if (r[4] == 0x00)
        {
            offline = true;
            return true;
        }
        if (r[2] > 100)
            return false;
        level = r[2];
        charging = r[4] is 4 or 5 ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool TryDecodeBragiValue(ReadOnlySpan<byte> raw, byte expectedSource, out int rawValue)
    {
        rawValue = 0;
        if (raw.Length < 5)
            return false;
        var data = raw.Length >= 65 && raw[0] == 0x00 ? raw[1..] : raw;
        if (data.Length < 5)
            return false;

        // 形态 1（Dark Core / M75 / K65 PLUS）：01 02 00 <v LE>
        if (expectedSource == 0x01 && data[0] == 0x01 && data[1] == 0x02 && data[2] == 0x00)
        {
            rawValue = data[3] | (data[4] << 8);
            return true;
        }
        // 形态 2（Virtuoso / HS80 / Void v2）：01 <source> 02 00 <v LE>
        if (data.Length >= 6 && data[0] == 0x01 && data[1] == expectedSource && data[2] == 0x02 && data[3] == 0x00)
        {
            rawValue = data[4] | (data[5] << 8);
            return true;
        }
        return false;
    }

    public static bool TryDecodeBragiBattery(ReadOnlySpan<byte> raw, byte expectedSource, bool rejectZero, out int level)
    {
        level = 0;
        if (!TryDecodeBragiValue(raw, expectedSource, out int tenths))
            return false;
        if (tenths > 1000 || (rejectZero && tenths == 0))
            return false;
        level = tenths / 10;
        return true;
    }

    public static bool TryDecodeBragiCharge(ReadOnlySpan<byte> raw, byte expectedSource, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (!TryDecodeBragiValue(raw, expectedSource, out int value))
            return false;
        if (value == 1)
        {
            charging = ChargingState.Charging;
            return true;
        }
        if (value == 2)
        {
            charging = ChargingState.NotCharging;
            return true;
        }
        return false;
    }

    public static string FamilyName(CorsairProtocolKind protocol) => protocol switch
    {
        CorsairProtocolKind.VoidElite => "Corsair Void C9",
        CorsairProtocolKind.Hs80Usb => "Corsair HS80 USB",
        CorsairProtocolKind.VoidV2 => "Corsair Void v2",
        _ => "Corsair Bragi"
    };
}

internal sealed class CorsairAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    private static CorsairModel? Match(HidInterface hid)
    {
        if (DeviceCatalog.IsDisabledPath(hid, out _) || hid.VendorId != CorsairProtocol.VendorId ||
            hid.MetadataIssue != ReadIssue.None || hid.UsagePage is not >= 0xFF00)
            return null;
        var model = CorsairProtocol.Find(hid.ProductId);
        if (model is null)
            return null;
        return model.Protocol switch
        {
            CorsairProtocolKind.VoidElite => hid is
            {
                UsagePage: CorsairProtocol.VoidUsagePage, Usage: CorsairProtocol.VoidUsage,
                InputLength: >= 5, OutputLength: >= 2
            } && (hid.InterfaceNumber is null or 3) ? model : null,
            CorsairProtocolKind.Hs80Usb => hid is
            {
                UsagePage: CorsairProtocol.BragiUsagePage, InputLength: 64, OutputLength: 64
            } && (hid.InterfaceNumber is null or 3) ? model : null,
            CorsairProtocolKind.BragiFramed => hid is
            {
                UsagePage: CorsairProtocol.BragiUsagePage, Usage: CorsairProtocol.BragiUsage,
                InputLength: 64 or 65, OutputLength: 65
            } ? model : null,
            CorsairProtocolKind.BragiBare or CorsairProtocolKind.BragiBareSession => hid is
            {
                UsagePage: CorsairProtocol.BragiUsagePage, Usage: CorsairProtocol.BragiUsage,
                InputLength: 64, OutputLength: 64
            } ? model : null,
            CorsairProtocolKind.VoidV2 => hid is
            {
                InputLength: 64 or 65, OutputLength: 64 or 65
            } && hid.InterfaceNumber == 4 ? model : null,
            _ => null
        };
    }

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(hid => Match(hid) is not null)
            .GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var endpoint = group.OrderBy(hid => hid.Usage == CorsairProtocol.BragiUsage ? 0 : 1)
                .ThenBy(hid => hid.InterfaceNumber ?? 99).First();
            if (gates?.IsBlocked(endpoint, out _) == true)
                continue;
            var model = Match(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"corsair:{endpoint.ReceiverId}", model.Name, model.Kind,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "海盗船 USB 直连" : "海盗船无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || Match(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "海盗船控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        return model.Protocol switch
        {
            CorsairProtocolKind.VoidElite => await ReadVoidEliteAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            CorsairProtocolKind.BragiFramed => await ReadBragiFramedAsync(target, endpoint, transport, trace, token).ConfigureAwait(false),
            CorsairProtocolKind.Hs80Usb => await ReadBragiBareAsync(target, endpoint, model, transport, trace, token).ConfigureAwait(false),
            CorsairProtocolKind.BragiBare or CorsairProtocolKind.BragiBareSession =>
                await ReadBragiBareAsync(target, endpoint, model, transport, trace, token).ConfigureAwait(false),
            CorsairProtocolKind.VoidV2 => await ReadVoidV2Async(target, endpoint, transport, trace, token).ConfigureAwait(false),
            _ => AdapterRegistry.Failure(target, ReadIssue.IoError, "不支持的海盗船协议")
        };
    }

    private static async ValueTask<BatteryReading> ReadVoidEliteAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = CorsairProtocol.EncodeVoidElite(endpoint.OutputLength!.Value);
        if (!CorsairProtocol.AllowsRequest(req))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0xC9, RequiredInputReportId = 0x64 }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, endpoint.InputLength!.Value,
            p => CorsairProtocol.TryDecodeVoidElite(p, out _, out _, out _), trace, token, milliseconds: 800).ConfigureAwait(false);
        if (frame is null || !CorsairProtocol.TryDecodeVoidElite(frame, out bool offline, out int level, out var charging))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        if (offline)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                Detail: "Corsair Void Elite 未连接或已关机");
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadBragiFramedAsync(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        var req = CorsairProtocol.EncodeBragiFramed(endpoint.OutputLength!.Value, 0x09, 0x0F);
        if (!CorsairProtocol.AllowsRequest(req))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x00 }, token);
        var frame = await HidQuery.OutputAsync(session, session, req, endpoint.InputLength!.Value,
            p => CorsairProtocol.TryDecodeBragiBattery(p, 0x01, rejectZero: true, out _), trace, token, milliseconds: 1500).ConfigureAwait(false);
        if (frame is null || !CorsairProtocol.TryDecodeBragiBattery(frame, 0x01, rejectZero: true, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        return new(target.Device, level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadBragiBareAsync(
        DeviceTarget target, HidInterface endpoint, CorsairModel model,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        int outLen = endpoint.OutputLength!.Value;
        int inLen = endpoint.InputLength!.Value;
        byte route = model.Connection == DeviceConnection.UsbCable ? (byte)0x08 : (byte)0x09;
        byte source = (byte)(route - 0x08);
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = 0x02, RequiredInputReportId = 0x01 }, token);

        if (model.Protocol == CorsairProtocolKind.BragiBareSession)
            await SendReadOnlySessionAsync(session, outLen, inLen, trace, token).ConfigureAwait(false);

        var battReq = CorsairProtocol.EncodeBragiBare(outLen, route, 0x0F);
        if (!CorsairProtocol.AllowsRequest(battReq))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");
        var battFrame = await HidQuery.OutputAsync(session, session, battReq, inLen,
            p => CorsairProtocol.TryDecodeBragiBattery(p, source, rejectZero: model.Protocol != CorsairProtocolKind.Hs80Usb, out _),
            trace, token, milliseconds: 900).ConfigureAwait(false);
        if (battFrame is null || !CorsairProtocol.TryDecodeBragiBattery(
            battFrame, source, rejectZero: model.Protocol != CorsairProtocolKind.Hs80Usb, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);

        if (model.Protocol == CorsairProtocolKind.Hs80Usb)
            return new(target.Device, level, false, ChargingState.Charging, LinkState.Online, DateTimeOffset.UtcNow);

        var chgReq = CorsairProtocol.EncodeBragiBare(outLen, route, 0x10);
        if (!CorsairProtocol.AllowsRequest(chgReq))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");
        var chgFrame = await HidQuery.OutputAsync(session, session, chgReq, inLen,
            p => CorsairProtocol.TryDecodeBragiCharge(p, source, out _), trace, token, milliseconds: 700).ConfigureAwait(false);
        var charging = chgFrame is not null && CorsairProtocol.TryDecodeBragiCharge(chgFrame, source, out var state)
            ? state : ChargingState.Unknown;
        return new(target.Device, level, false, charging, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask<BatteryReading> ReadVoidV2Async(
        DeviceTarget target, HidInterface endpoint, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        int outLen = endpoint.OutputLength!.Value;
        int inLen = endpoint.InputLength!.Value;
        byte? reqOutId = outLen == 64 ? (byte)0x02 : (byte)0x00;
        using var session = transport.OpenHid(endpoint with { RequiredOutputReportId = reqOutId }, token);

        await SendReadOnlySessionAsync(session, outLen, inLen, trace, token).ConfigureAwait(false);
        var battReq = CorsairProtocol.EncodeBragiBare(outLen, 0x09, 0x0F);
        if (!CorsairProtocol.AllowsRequest(battReq))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "报文未通过只读白名单");
        var battFrame = await HidQuery.OutputAsync(session, session, battReq, inLen,
            p => CorsairProtocol.TryDecodeBragiBattery(p, 0x01, rejectZero: true, out _), trace, token, milliseconds: 900).ConfigureAwait(false);
        if (battFrame is null || !CorsairProtocol.TryDecodeBragiBattery(battFrame, 0x01, rejectZero: true, out int level))
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow, ReadIssue.Timeout);
        return new(target.Device, level, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
    }

    private static async ValueTask SendReadOnlySessionAsync(
        IHidSession session, int outLen, int inLen, FrameTrace? trace, CancellationToken token)
    {
        // 只读握手序列：固件查询 (0x13) + 接收器心跳 (0x12) + 耳机心跳 (0x12)，严禁软件模式切换。
        (byte Route, byte Prop)[] steps = [(0x08, 0x13), (0x08, 0x12), (0x09, 0x12)];
        foreach (var (route, prop) in steps)
        {
            var req = CorsairProtocol.EncodeBragiBare(outLen, route, prop);
            if (!CorsairProtocol.AllowsRequest(req))
                continue;
            await HidQuery.OutputAsync(session, session, req, inLen, _ => true, trace, token, milliseconds: 250).ConfigureAwait(false);
        }
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

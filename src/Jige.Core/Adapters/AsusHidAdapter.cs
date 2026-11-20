namespace Jige.Core.Adapters;

internal enum AsusBatteryLayout { Byte6, Byte11, Auto }

internal sealed record AsusKeyboardModel(
    ushort Pid,
    string Name,
    DeviceConnection Connection,
    AsusBatteryLayout Layout = AsusBatteryLayout.Byte6);

internal static class AsusHidProtocol
{
    public const ushort VendorId = 0x0B05;
    public const int ReportLength = 65;

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

    public static IReadOnlyList<AsusKeyboardModel> AllKeyboards => Keyboards;

    public static AsusKeyboardModel? FindKeyboard(ushort? pid) =>
        pid.HasValue ? Array.Find(Keyboards, model => model.Pid == pid.Value) : null;

    /// <summary>华硕 ROG 键盘纯只读电量查询（Report ID 0x00，命令 12 01，补零至 65 字节）。</summary>
    public static byte[] EncodeKeyboardBattery()
    {
        var frame = new byte[ReportLength];
        frame[0] = 0x00;
        frame[1] = 0x12;
        frame[2] = 0x01;
        return frame;
    }

    public static bool TryDecodeKeyboardBattery(
        ReadOnlySpan<byte> frame, AsusKeyboardModel model,
        out bool asleep, out int level, out ChargingState charging)
    {
        asleep = false;
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != ReportLength)
            return false;

        int header = frame[0] == 0x00 ? 1 : frame[0] is 0x12 or 0xFF ? 0 : -1;
        if (header < 0)
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
        int raw = model.Layout switch
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
}

internal sealed class AsusHidAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
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

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(hid => MatchKeyboard(hid) is not null)))
        {
            if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            {
                gates.RecordRejection(endpoint, 3, $"已拦截（{blockedReason}）");
                continue;
            }
            var model = MatchKeyboard(endpoint)!;
            var tier = DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId);
            yield return new(new($"asus:{endpoint.ReceiverId}", model.Name, DeviceKind.Keyboard,
                AdaptationSource.Catalog, tier, model.Connection)
            {
                SourceLabel = model.Connection == DeviceConnection.UsbCable ? "华硕 USB 直连" : "华硕无线接收器"
            }, endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || MatchKeyboard(endpoint) is not { } model)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "华硕键盘控制集合不匹配");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        using var session = transport.OpenHid(
            endpoint with { RequiredInputReportId = 0, RequiredOutputReportId = 0 }, token);
        var request = AsusHidProtocol.EncodeKeyboardBattery();
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

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || MatchKeyboard(endpoint) is null ||
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

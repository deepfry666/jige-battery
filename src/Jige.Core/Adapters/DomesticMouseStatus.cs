namespace Jige.Core.Adapters;

internal readonly record struct MouseBatteryStatus(int? Level, ChargingState Charging, string? Name = null);

internal sealed record DomesticMouseStatus(
    DeviceCatalogEntry Model, ushort UsagePage, ushort Usage, byte? InterfaceNumber,
    byte ReportId, int MinimumLength, bool Feature = false, ushort? AlternateUsage = null)
{
    private const ushort Beken = 0x1D57;
    private const ushort Incott = 0x093A;
    private const ushort Rapoo = 0x24AE;

    private static readonly DomesticMouseStatus[] Routes =
    [
        new(Entry(Beken, 0xFA60, "ATTACK SHARK / Delux 鼠标", "1D57 Input 03",
                "HaloBattery / mouse-battery-tray / OpenMouse"),
            0x000A, 0x0000, 2, 0x03, 5),
        new(Entry(Beken, 0xFA65, "Inphic IN9 Pro", "1D57 Input 03", "HaloBattery"),
            0x000A, 0x0000, 2, 0x03, 5),
        new(Entry(Incott, 0x522C, "Incott 8K 鼠标", "Incott Input 09", "OpenMouse"),
            0xFF05, 0x0001, 3, 0x09, 8),
        new(Entry(Rapoo, 0x1413, "Rapoo VT7", "Rapoo Gen 2 Input 07", "HaloBattery"),
            0xFF00, 0x0002, null, 0x07, 9, AlternateUsage: 0x000E),
        new(Entry(Rapoo, 0x14A9, "Rapoo VT9 Pro V2", "Rapoo Gen 2 Input 07", "HaloBattery"),
            0xFF00, 0x0002, null, 0x07, 9, AlternateUsage: 0x000E),
        new(Entry(Rapoo, 0x1205, "Rapoo VT9 Pro", "Rapoo Feature 2A", "OpenMouse", "rapoo-vt9:"),
            0xFF0B, 0x0104, null, 0x2A, 10, Feature: true)
    ];

    internal static IEnumerable<DeviceCatalogEntry> Models => Routes.Select(route => route.Model);

    private static DeviceCatalogEntry Entry(ushort vid, ushort pid, string name, string protocol, string source,
        string prefix = "passive-mouse:") =>
        new(vid, pid, name, protocol, DeviceKind.Mouse, VerificationTier.DocsOnly, prefix, source);

    internal static DomesticMouseStatus? Find(ushort? vid, ushort? pid) =>
        Routes.FirstOrDefault(route => route.Model.VendorId == vid && route.Model.ProductId == pid);

    internal static bool IsFamily(ushort? vid) => vid is Beken or Incott or Rapoo;

    internal bool MatchesCollection(ushort? page, ushort? usage, byte? mi, ushort inputLength, ushort featureLength) =>
        page == UsagePage && (usage == Usage || AlternateUsage is not null && usage == AlternateUsage) &&
        (InterfaceNumber is null || mi == InterfaceNumber) &&
        (Feature ? featureLength : inputLength) >= MinimumLength;

    internal static DomesticMouseStatus? Match(HidInterface hid)
    {
        var route = Find(hid.VendorId, hid.ProductId);
        if (route is null || hid.BluetoothHid || hid.Bus == HidBus.Bluetooth ||
            !route.MatchesCollection(hid.UsagePage, hid.Usage,
                hid.InterfaceNumber, hid.InputLength ?? 0, hid.FeatureLength ?? 0))
            return null;
        var ids = route.Feature ? hid.FeatureReportIds : hid.InputReportIds;
        // 真实枚举会预检报告 ID；旧诊断报告可能没有这项元数据，开句柄时仍会复核描述符。
        return ids is null || ids.Split(',').Contains($"{route.ReportId:X2}", StringComparer.OrdinalIgnoreCase)
            ? route : null;
    }

    internal static DeviceTarget[] Detect(DeviceInventory inventory, bool feature) =>
        inventory.Hid.Select(hid => (Hid: hid, Route: Match(hid)))
            .Where(candidate => candidate.Route is not null && candidate.Route.Feature == feature)
            .GroupBy(candidate => candidate.Hid.ReceiverId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group =>
            {
                var (hid, route) = group.Single();
                var model = route!.Model;
                return new DeviceTarget(new(model.AdapterPrefix + hid.ReceiverId, model.Model, model.Kind,
                    AdaptationSource.Catalog, model.Verification)
                {
                    SourceLabel = "2.4G 无线接收器",
                    UsesPassiveBatteryReports = !feature
                }, hid, "0");
            }).ToArray();

    internal static bool IsEmptyFeatureBuffer(ReadOnlySpan<byte> frame) =>
        frame.Length > 1 && (!frame[1..].ContainsAnyExcept((byte)0) || !frame[1..].ContainsAnyExcept(byte.MaxValue));

    internal MouseBatteryStatus? Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < MinimumLength || frame[0] != ReportId)
            return null;
        if (Feature)
        {
            if (IsEmptyFeatureBuffer(frame))
                return null;
            return frame[9] <= 100 ? new(frame[9], ChargingState.Unknown) : null;
        }
        if (Model.VendorId == Beken)
            return DecodeBeken(frame);
        if (Model.VendorId == Incott)
        {
            int encoded = frame[1];
            if (encoded <= 100)
                return new(encoded, ChargingState.NotCharging);
            return encoded is >= 128 and <= 228 ? new(encoded - 128, ChargingState.Charging) : null;
        }
        return frame[7] is 1 or 2 && frame[8] <= 100
            ? new(frame[8], frame[7] == 2 ? ChargingState.Charging : ChargingState.NotCharging)
            : null;
    }

    private MouseBatteryStatus? DecodeBeken(ReadOnlySpan<byte> frame)
    {
        if (frame[2] != 0x40)
            return null;
        byte model = frame[1];
        string? name = Model.ProductId == 0xFA65
            ? model is 0x95 or 0x90 or 0x93 or 0x99 ? Model.Model : null
            : model switch
            {
                0x55 => "ATTACK SHARK X11",
                0x4E => "ATTACK SHARK X3 Max",
                0x10 => "ATTACK SHARK R1 / X6",
                0x85 => "ATTACK SHARK X6",
                0x4D => "ATTACK SHARK X3",
                0xBE => "ATTACK SHARK X11 Pro",
                0x07 => "ATTACK SHARK X11 SE",
                0x20 => "Delux M600 Pro",
                _ => null
            };
        if (name is null)
            return null;

        int value = frame[4];
        if (model is 0x10 or 0x85)
            return value is >= 1 and <= 10 ? new(value * 10, ChargingState.Unknown, name) : null;
        return frame[3] switch
        {
            0x01 when value is >= 1 and <= 100 => new(value, ChargingState.NotCharging, name),
            0x02 when model != 0x4E => new(100, ChargingState.Full, name),
            0x02 or 0x03 => new(null, ChargingState.Charging, name),
            _ => null
        };
    }

    internal void CopyDiagnosticFields(ReadOnlySpan<byte> raw, Span<byte> safe)
    {
        if (raw.IsEmpty)
            return;
        safe[0] = raw[0];
        if (Decode(raw) is null)
            return;
        if (Feature)
            safe[9] = raw[9];
        else if (Model.VendorId == Beken)
        {
            raw[1..4].CopyTo(safe[1..]);
            // 充电或充满状态帧不声明百分比。
            if (raw[1] is 0x10 or 0x85 || raw[3] == 0x01)
                safe[4] = raw[4];
        }
        else if (Model.VendorId == Incott)
            safe[1] = raw[1];
        else
            raw[7..9].CopyTo(safe[7..]);
    }
}

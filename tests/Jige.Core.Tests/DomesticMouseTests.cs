using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class DomesticMouseTests
{
    private const string CompxBattery = "0804000000000000000000000000000049";
    private const string CompxOnline = "080300000000000000000000000000004A";
    private const string CompxCidMid = "081000000000000000000000000000003D";
    private const string CompxReply = "0804000000024901106800000000000085";

    [Theory]
    [InlineData(0x3554, 0xF58A, "VXE R1 系列")]
    [InlineData(0x3554, 0xF505, "VXE / VGN 鼠标")]
    [InlineData(0x3554, 0xF58E, "VXE 鼠标")]
    [InlineData(0x373B, 0x1040, "MAD 鼠标")]
    [InlineData(0x373B, 0x1085, "VXE R1 SE+")]
    [InlineData(0x3554, 0xF5F7, "Scyrox V8")]
    [InlineData(0x3554, 0xF5F4, "Scyrox 鼠标")]
    [InlineData(0x3554, 0xF517, "Compx 方案鼠标")]
    [InlineData(0x3554, 0xFB56, "VGN F2 Master")]
    [InlineData(0x3554, 0xFB3E, "VGN Y2 Ultra")]
    [InlineData(0x3554, 0xFB52, "VGN Y2 Master")]
    [InlineData(0x3554, 0xFB54, "VGN Y2 Pro Max")]
    [InlineData(0x3554, 0xFB58, "VGN Dragonfly 3 Master")]
    [InlineData(0x3554, 0xF503, "VGN 鼠标")]
    [InlineData(0x3554, 0xF50B, "VGN 鼠标")]
    [InlineData(0x3554, 0xF50D, "Compx 方案鼠标")]
    public async Task CompxReceiverReadsOnlyItsExactCollection(int vid, int pid, string name)
    {
        var hid = Compx(vid, pid);
        var sibling = hid with { Path = "sibling", UsagePage = 0xFF01 };
        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidInput, CompxReply),
            new(DiagnosticOperation.HidOutput, CompxBattery)
        ]));
        var registry = AdapterRegistry.CreateDefault();
        var device = Detect(registry, hid, sibling);
        Assert.Equal(name, device.Target.Device.Name);
        Assert.Empty(device.Target.SiblingInputHids);
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(73, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal(name, reading.Device.Name);
        transport.AssertComplete();
    }

    [Fact]
    public async Task CompxTimeoutKeepsOneRouteAndDoesNotTrySiblingCollections()
    {
        var hid = Compx(0x3554, 0xF58A);
        var alternate = Xvi(0x3554, 0xF58A, 2) with { Path = "sibling" };
        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
            new(DiagnosticOperation.HidOutput, CompxOnline),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
            new(DiagnosticOperation.HidOutput, CompxBattery)
        ]));
        var registry = AdapterRegistry.CreateDefault();
        var device = Detect(registry, hid, alternate);
        var presence = await registry.CheckPresenceAsync(device, transport, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, presence?.Link);
        transport.AssertComplete();
    }

    [Theory]
    [InlineData(0x373E, 0x001E, 2, 0xA1)]
    [InlineData(0x373E, 0x001D, 2, 0xA1)]
    [InlineData(0x36A7, 0xA880, 3, 0xA2)]
    [InlineData(0x36A7, 0xA868, 1, 0xA2)]
    [InlineData(0x36A7, 0xA883, 4, 0xA2)]
    [InlineData(0x36A7, 0xA887, 0, 0xA2)]
    [InlineData(0x36A7, 0xA882, null, 0xA2)]
    [InlineData(0x2023, 0xF013, 2, 0xA1)]
    [InlineData(0x2023, 0xF015, 1, 0xA1)]
    public async Task XviReceiverUsesExactFeatureQuery(int vid, int pid, int? mi, int header)
    {
        var hid = Xvi(vid, pid, (byte?)mi);
        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("00000002020083", 65)),
            new(DiagnosticOperation.GetFeature, Pad($"00{header:X2}0002020083013A", 65), Argument: 0)
        ]));
        var registry = AdapterRegistry.CreateDefault();
        var device = Detect(registry, hid);
        if (pid == 0xA882)
            Assert.Equal("WLmouse 鼠标", device.Target.Device.Name);
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(58, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        transport.AssertComplete();
    }

    [Theory]
    [InlineData(0x5253, 0x1020, 21, 0x11, "ACAD")]
    [InlineData(0x5253, 0x1020, 65, 0x12, "ACAD")]
    [InlineData(0x3837, 0x100B, 21, 0x11, "C8C7")]
    [InlineData(0x3837, 0x100B, 65, 0x12, "C8C7")]
    public async Task MchoseRetriesWithASetBeforeEveryGetAndUsesCapsLength(int vid, int pid, int length, int id,
        string encodedVid)
    {
        var hid = Mchose(vid, pid, length);
        string request = Pad($"{id:X2}F9", length, 'F');
        string reply = Pad($"{id:X2}F9{encodedVid}AABB12AB34CDF6BCFE", length, 'F');
        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, request),
            new(DiagnosticOperation.GetFeature, Pad($"{id:X2}F9", length), Argument: id),
            new(DiagnosticOperation.SetFeature, request),
            new(DiagnosticOperation.GetFeature, request, Argument: id),
            new(DiagnosticOperation.SetFeature, request),
            new(DiagnosticOperation.GetFeature, Issue: ReadIssue.IoError, Argument: id),
            new(DiagnosticOperation.SetFeature, request),
            new(DiagnosticOperation.GetFeature, reply, Argument: id)
        ]));
        var registry = new AdapterRegistry(new MchoseMouseAdapter(null, TimeSpan.Zero));
        var device = Detect(registry, hid, hid with { Path = "sibling", UsagePage = 0xFF0B, Usage = 0x0104 });
        Assert.IsType<MchoseMouseAdapter>(device.Adapter);
        if (pid == 0x1020)
            Assert.Equal("MCHOSE 鼠标", device.Target.Device.Name);
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(67, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        transport.AssertComplete();
    }

    [Theory]
    [InlineData(0x3554, 0xFA09, 0xFF02)]
    [InlineData(0x3554, 0xF58A, 0xFF01)]
    [InlineData(0x3554, 0xFFFF, 0xFF02)]
    [InlineData(0x3554, 0xF524, 0xFF02)]
    [InlineData(0x3554, 0xF527, 0xFF02)]
    [InlineData(0x3554, 0xF5D5, 0xFF02)]
    [InlineData(0x3554, 0xFB43, 0xFF02)]
    [InlineData(0x3554, 0xFB44, 0xFF02)]
    [InlineData(0x3554, 0xF510, 0xFF02)]
    [InlineData(0x3554, 0xFB26, 0xFF02)]
    [InlineData(0x3554, 0xFB23, 0xFF02)]
    [InlineData(0x3554, 0xFB36, 0xFF02)]
    [InlineData(0x3554, 0xFB35, 0xFF02)]
    public Task RejectedCompxCollectionsNeverOpen(int vid, int pid, int page) =>
        Reject(Compx(vid, pid) with { UsagePage = (ushort)page }, new MouseReceiverAdapter());

    [Theory]
    [InlineData(0x36A7, 0xFFFF, 2)]
    [InlineData(0x373E, 0xFFFF, 2)]
    [InlineData(0x373E, 0x001E, 3)]
    [InlineData(0x2023, 0xF019, 2)]
    public Task RejectedXviCollectionsNeverOpen(int vid, int pid, int mi) =>
        Reject(Xvi(vid, pid, (byte)mi), new MouseReceiverAdapter());

    [Theory]
    [InlineData(0x5253, 0x0031, 2, 21)]
    [InlineData(0x5253, 0x00B0, 2, 65)]
    [InlineData(0x5253, 0xFFFF, 2, 65)]
    [InlineData(0x3837, 0x100B, 1, 65)]
    [InlineData(0x3837, 0xFFFF, 2, 65)]
    [InlineData(0x5253, 0x1020, 2, 20)]
    [InlineData(0x41E4, 0x2001, 2, 21)]
    public Task RejectedMchoseCollectionsNeverOpen(int vid, int pid, int mi, int length) =>
        Reject(Mchose(vid, pid, length) with { InterfaceNumber = (byte)mi }, new MchoseMouseAdapter());

    [Fact]
    public void DuplicateMatchingCollectionsAreRejectedForEveryNewRoute()
    {
        var registry = AdapterRegistry.CreateDefault();
        foreach (var hid in new[] { Compx(0x3554, 0xF58A), Xvi(0x36A7, 0xA880, 2), Mchose(0x5253, 0x1020, 65) })
            Assert.Empty(registry.Detect(new([hid, hid with { Path = "duplicate", InterfaceNumber = 3 }], [], [])));
    }

    [Fact]
    public void DangerousCommandsAndAlteredQueryFramesAreRejected()
    {
        foreach (byte command in new byte[] { 0x05, 0x07, 0x09, 0x0B, 0x0C, 0x0D, 0x0F })
        {
            var frame = new byte[17];
            frame[0] = 0x08;
            frame[1] = command;
            frame[^1] = (byte)(0x55 - frame[0] - command);
            Assert.False(MouseBatteryProtocol.Allows(MouseFamily.CompxOut, frame));
        }

        foreach (string command in new[] { CompxBattery, CompxOnline, CompxCidMid })
        {
            var frame = Convert.FromHexString(command);
            Assert.True(MouseBatteryProtocol.Allows(MouseFamily.CompxOut, frame));
            frame[4] = 1;
            frame[^1]--;
            Assert.False(MouseBatteryProtocol.Allows(MouseFamily.CompxOut, frame));
        }

        var xvi = Convert.FromHexString(Pad("00000002020083", 65));
        Assert.True(MouseBatteryProtocol.Allows(MouseFamily.XviNew, xvi));
        foreach (string command in new[] { "00000002010083", "0000000201008B", "00000002020183", "00000002020084", "0000000202008301" })
            Assert.False(MouseBatteryProtocol.Allows(MouseFamily.XviNew, Convert.FromHexString(Pad(command, 65))));
        Assert.False(MouseBatteryProtocol.Allows(MouseFamily.BitMouse, xvi));
        Assert.False(MouseBatteryProtocol.Allows(MouseFamily.BitMouse, Convert.FromHexString(Pad("08F9720200018301", 64))));

        foreach ((int id, int length) in new[] { (0x11, 21), (0x12, 65) })
        {
            var frame = Convert.FromHexString(Pad($"{id:X2}F9", length, 'F'));
            Assert.True(MchoseMouseProtocol.Allows(frame));
            for (int command = 0; command <= byte.MaxValue; command++)
            {
                frame[1] = (byte)(command ^ 0xFF);
                Assert.Equal(command == 0x06, MchoseMouseProtocol.Allows(frame));
            }
            frame[1] = 0xF9;
            frame[^1] = 0x00;
            Assert.False(MchoseMouseProtocol.Allows(frame));
        }
    }

    [Theory]
    [InlineData("00A30002020083013A")]
    [InlineData("00A20003020083013A")]
    [InlineData("00A20002010083013A")]
    [InlineData("00A20002020183013A")]
    [InlineData("00A20002020082013A")]
    [InlineData("00A200020200830165")]
    public void XviRejectsNonMatchingReplies(string header) =>
        Assert.False(MouseBatteryProtocol.TryXvi(Convert.FromHexString(Pad(header, 65)), MouseFamily.XviNew, out _, out _));

    [Theory]
    [InlineData("12F9ACADAABB12AB34CDF6BCFE")] // ID 不符
    [InlineData("11F8ACADAABB12AB34CDF6BCFE")] // 命令不符
    [InlineData("11F9ACADAABB12AB34CDF69AFE")] // 101%
    [InlineData("11F9")]                       // 请求回显
    [InlineData("1106", '0')]                  // 解码后的请求回显
    [InlineData("11F9", '0')]                  // 占位帧
    [InlineData("0000", '0')]                  // 全零
    public void MchoseRejectsInvalidReplies(string hex, char padding = 'F') =>
        Assert.False(MchoseMouseProtocol.TryDecode(Convert.FromHexString(Pad(hex, 21, padding)), 0x11, out _, out _));

    [Fact]
    public async Task MchoseIdlePollReturnsNoNewBattery()
    {
        var hid = Mchose(0x5253, 0x1020, 21);
        string request = Pad("11F9", 21, 'F');
        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            ..Enumerable.Range(0, 4).SelectMany(_ => new ReceiverStep[]
            {
                new(DiagnosticOperation.SetFeature, request),
                new(DiagnosticOperation.GetFeature, request, Argument: 0x11)
            })
        ]));
        var registry = new AdapterRegistry(new MchoseMouseAdapter(null, TimeSpan.Zero));
        var reading = await registry.ReadAsync(Detect(registry, hid), transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Null(reading.Percentage);
        transport.AssertComplete();
    }

    [Theory]
    [InlineData(0x3554, "Compx（VXE / VGN / Scyrox / Lamzu 等）")]
    [InlineData(0x36A7, "WLmouse")]
    [InlineData(0x373E, "Lamzu")]
    [InlineData(0x2023, "MCHOSE")]
    [InlineData(0x5253, "MCHOSE")]
    [InlineData(0x3837, "MCHOSE")]
    public void DiagnosticVendorNamesRespectSharedVid(int vid, string label) =>
        Assert.Equal(label, DiagnosticPrivacy.KnownVendorName((ushort)vid));

    [Fact]
    public async Task MchoseDiagnosticMasksIdentityAndCanReplayBattery()
    {
        var hid = Mchose(0x5253, 0x1020, 21);
        string request = Pad("11F9", 21, 'F');
        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.SetFeature, request),
            new(DiagnosticOperation.GetFeature, Pad("11F9ACADAABB12AB34CDF6FFFF", 21, 'F'), Argument: 0x11)
        ]));
        string report = await DiagnosticReport.GenerateAsync(new([hid], [], []), transport, CancellationToken.None);
        transport.AssertComplete();
        Assert.DoesNotContain("AABB12AB34CD", report);
        Assert.Contains("11F9ACADFFFFFFFFFFFFF6FFFF", report);
        var (inventory, replay, _) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault();
        var readings = await registry.ReadDevicesAsync(Assert.Single(registry.Detect(inventory)), replay, null, CancellationToken.None);
        var reading = Assert.Single(readings);
        Assert.Equal(0, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        replay.AssertComplete();
    }

    [Fact]
    public void DomesticDeviceDocumentationMatchesCatalog()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Jige.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var doc = File.ReadAllLines(Path.Combine(root.FullName, "docs", "devices.md"));
        var readme = File.ReadAllLines(Path.Combine(root.FullName, "README.md"));
        var models = MouseReceiverCatalog.DomesticModels.Select(model => DeviceCatalog.FindEntry(
            model.Receiver.VendorId, model.Receiver.ProductId)).Concat(MchoseMouseAdapter.Models).Concat(DomesticMouseStatus.Models);
        foreach (var model in models)
        {
            Assert.NotNull(model);
            Assert.Single(doc, line => line.StartsWith($"| {model.Model} | `{model.VidPid}` | {model.ProtocolFamily} | 仅按资料 |"));
            Assert.Contains(readme, line => line.Contains($"`{model.VidPid}`") && line.EndsWith("| 仅按资料 |"));
        }
    }

    private static DetectedDevice Detect(AdapterRegistry registry, params HidInterface[] endpoints)
    {
        Assert.True(AdapterRegistry.IsKnownHidReceiver(endpoints[0].VendorId, endpoints[0].ProductId));
        var device = Assert.Single(registry.Detect(new(endpoints, [], [])));
        Assert.Equal(AdaptationSource.Catalog, device.Target.Device.Source);
        Assert.Equal(VerificationTier.DocsOnly, device.Target.Device.Verification);
        return device;
    }

    private static async Task Reject(HidInterface hid, IDeviceAdapter adapter)
    {
        Assert.Empty(AdapterRegistry.CreateDefault().Detect(new([hid], [], [])));
        Assert.Null(MouseReceiverCatalog.GenericRoute(hid));
        var transport = new FakeReceiverTransport();
        var reading = await adapter.ReadAsync(new(new("mouse", "鼠标", DeviceKind.Mouse), hid, "0"),
            transport, null, CancellationToken.None);
        Assert.Null(reading.Percentage);
        Assert.Equal(0, transport.Calls);
    }

    private static HidInterface Compx(int vid, int pid) =>
        new("receiver", "primary", (ushort)vid, (ushort)pid, null, 0xFF02, 2, 17, 17, 0, 1);

    private static HidInterface Xvi(int vid, int pid, byte? mi) =>
        new("receiver", "primary", (ushort)vid, (ushort)pid, null, 0xFFFF, 0, 0, 0, 65, mi);

    private static HidInterface Mchose(int vid, int pid, int length) =>
        new("receiver", "primary", (ushort)vid, (ushort)pid, null, 0xFF01, 1, 0, 0, (ushort)length, 2);

    private static string Pad(string hex, int length, char padding = '0') => hex.PadRight(length * 2, padding);
}

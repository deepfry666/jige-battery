using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class BrandExpansionTests
{
    private static string Pad(string hex, int length) => hex.PadRight(length * 2, '0');

    [Fact]
    public async Task AsusMiceStrixGoAndOmniReceiverReadOnlyQueriesWork()
    {
        var registry = new AdapterRegistry(new AsusHidAdapter(null, TimeSpan.Zero), new AsusOmniAdapter());

        // 1) ASUS ROG Gladius III Aimpoint（百分比鼠标）
        var mouse = new HidInterface("asus-m1", "p-m1", 0x0B05, 0x1A72, "ROG Gladius III", 0xFF00, 1, 65, 65, 0, 0);
        var mTarget = Assert.Single(registry.Detect(new([mouse], [], [])));
        Assert.Equal("ROG Gladius III Aimpoint", mTarget.Target.Device.Name);
        Assert.Equal(DeviceKind.Mouse, mTarget.Target.Device.Kind);
        var mTransport = new FakeReceiverTransport((mouse.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("001207", 65)),
            new(DiagnosticOperation.HidInput, Pad("0012070000550000000001", 65))
        ]));
        var mReading = await registry.ReadAsync(mTarget, mTransport, null, CancellationToken.None);
        Assert.Equal(85, mReading.Percentage);
        Assert.False(mReading.IsEstimated);
        Assert.Equal(ChargingState.Charging, mReading.Charging);
        Assert.Equal(LinkState.Online, mReading.Link);
        mTransport.AssertComplete();

        // 2) ASUS ROG Chakram（5 档 0..4 × 25%，标为估算）
        var stepMouse = mouse with { ReceiverId = "asus-m2", Path = "p-m2", ProductId = 0x18E5 };
        var sTarget = Assert.Single(registry.Detect(new([stepMouse], [], [])));
        var sTransport = new FakeReceiverTransport((stepMouse.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("001207", 65)),
            new(DiagnosticOperation.HidInput, Pad("0012070000030000000000", 65))
        ]));
        var sReading = await registry.ReadAsync(sTarget, sTransport, null, CancellationToken.None);
        Assert.Equal(75, sReading.Percentage);
        Assert.True(sReading.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, sReading.Charging);
        sTransport.AssertComplete();

        // 3) ASUS ROG Harpe II Ace SPEEDNOVA 接收器（MI_02 FF01 Report ID 03）
        var harpe = new HidInterface("asus-h2", "p-h2", 0x0B05, 0x1AD0, "SPEEDNOVA", 0xFF01, 1, 64, 64, 0, 2);
        var hTarget = Assert.Single(registry.Detect(new([harpe], [], [])));
        var hTransport = new FakeReceiverTransport((harpe.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("031207", 64)),
            new(DiagnosticOperation.HidInput, Pad("0312070000400000000000", 64))
        ]));
        var hReading = await registry.ReadAsync(hTarget, hTransport, null, CancellationToken.None);
        Assert.Equal(64, hReading.Percentage);
        Assert.Equal(ChargingState.NotCharging, hReading.Charging);
        hTransport.AssertComplete();

        // 4) ROG Strix Go 2.4 无线耳机（Feature FF，电压 3831 mV = 0x0EF7 -> 75%，充电扣除 160 mV 抬升）
        var headset = new HidInterface("asus-hs", "p-hs", 0x0B05, 0x18D6, "ROG Strix Go 2.4", 0xFF00, 1, 0, 0, 64, 3);
        var hsTarget = Assert.Single(registry.Detect(new([headset], [], [])));
        var hsTransport = new FakeReceiverTransport((headset.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("FF0800FD0412F1035201", 64)),
            new(DiagnosticOperation.GetFeature, Pad("FF1B000000000000000300F70E", 64), Argument: 0xFF)
        ]));
        var hsReading = await registry.ReadAsync(hsTarget, hsTransport, null, CancellationToken.None);
        Assert.Equal(75, hsReading.Percentage);
        Assert.True(hsReading.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, hsReading.Charging);
        hsTransport.AssertComplete();

        // 充电 3897 mV (0x0F39) 扣除 160 mV -> 3737 mV -> 50%
        Assert.True(AsusHidProtocol.TryDecodeHeadsetBattery(
            Convert.FromHexString(Pad("FF1B000000000000000A00390F", 64)), false, out bool hsOffline, out int hsLvl, out var hsChg));
        Assert.False(hsOffline);
        Assert.Equal(50, hsLvl);
        Assert.Equal(ChargingState.Charging, hsChg);

        // 5) ASUS ROG OMNI 接收器（0B05:1ACE：配对表含 Harpe Ace Mini 1B65 + Falchion RX 1B06）
        var omniCtrl = new HidInterface("omni-1", "p-omni-c", 0x0B05, 0x1ACE, "ROG OMNI", 0xFF02, 1, 64, 64, 0, 2);
        var omniMouse = omniCtrl with { Path = "p-omni-m", UsagePage = 0xFF01 };
        var omniKb = omniCtrl with { Path = "p-omni-k", UsagePage = 0xFF00 };
        var omniTarget = Assert.Single(registry.Detect(new([omniCtrl, omniMouse, omniKb], [], [])));
        var omniTransport = new FakeReceiverTransport(
            (omniCtrl.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("01A0", 64)),
                new(DiagnosticOperation.HidInput, Pad("01A0000000651B0100061B0200", 64))
            ]),
            (omniMouse.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("031207", 64)),
                new(DiagnosticOperation.HidInput, Pad("03120700005A0000000000", 64))
            ]),
            (omniKb.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("021201", 64)),
                new(DiagnosticOperation.HidInput, Pad("021201000000080000010052", 64))
            ]));
        var omniReadings = await registry.ReadDevicesAsync(omniTarget, omniTransport, null, CancellationToken.None);
        Assert.Equal(2, omniReadings.Count);
        Assert.Equal("ROG Harpe Ace Mini", omniReadings[0].Device.Name);
        Assert.Equal(90, omniReadings[0].Percentage);
        Assert.Equal("ROG Falchion RX Low Profile", omniReadings[1].Device.Name);
        Assert.Equal(82, omniReadings[1].Percentage);
        Assert.Equal(ChargingState.Charging, omniReadings[1].Charging);
        omniTransport.AssertComplete();

        // 只读白名单必须拒绝非电池写入命令（如 12 05 DPI/灯效）
        Assert.False(AsusHidProtocol.AllowsRequest(Convert.FromHexString(Pad("00120501", 65))));
    }

    [Theory]
    [InlineData(0x3554, 0xF508, "Pulsar X2 V2 Mini", DeviceConnection.Receiver)]
    [InlineData(0x3554, 0xF507, "Pulsar X2 V2 Mini", DeviceConnection.UsbCable)]
    [InlineData(0x3710, 0x5406, "Pulsar X2 CrazyLight Mini", DeviceConnection.Receiver)]
    [InlineData(0x3710, 0x3414, "Pulsar X2 CrazyLight Mini", DeviceConnection.UsbCable)]
    [InlineData(0x3770, 0x0300, "Hitscan Hyperlight 8K", DeviceConnection.Receiver)]
    [InlineData(0x3770, 0x0200, "Hitscan Hyperlight", DeviceConnection.Receiver)]
    [InlineData(0x3770, 0x0100, "Hitscan Hyperlight", DeviceConnection.UsbCable)]
    [InlineData(0x3554, 0xF5DF, "Cougar Revenger Pro 4K", DeviceConnection.Receiver)]
    [InlineData(0x3554, 0xF5DE, "Cougar Revenger Pro 4K", DeviceConnection.UsbCable)]
    [InlineData(0x3554, 0xF5DC, "Cougar Revenger Pro", DeviceConnection.Receiver)]
    [InlineData(0x3554, 0xF55E, "Redragon M991", DeviceConnection.UsbCable)]
    [InlineData(0x1B1C, 0x2B2A, "Corsair Sabre v2 Pro Ultralight", DeviceConnection.Receiver)]
    public async Task CrossBrandCompxMiceReadBattery(int vid, int pid, string name, DeviceConnection conn)
    {
        var hid = new HidInterface("rx-cb", "p-cb", (ushort)vid, (ushort)pid, null, 0xFF02, 2, 17, 17, 0, 1);
        var registry = AdapterRegistry.CreateDefault();
        Assert.True(AdapterRegistry.IsKnownHidReceiver((ushort)vid, (ushort)pid));
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        Assert.Equal(name, target.Target.Device.Name);
        Assert.Equal(conn, target.Target.Device.Connection);
        Assert.Equal(VerificationTier.DocsOnly, target.Target.Device.Verification);

        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, "08040000000250000FAD0000000000003B"),
            new(DiagnosticOperation.HidOutput, "0804000000000000000000000000000049")
        ]));
        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(80, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        transport.AssertComplete();
    }

    [Fact]
    public async Task GloriousModelOWirelessReadsXviNewAndDetectsA4Asleep()
    {
        var hid = new HidInterface("gl-1", "p-gl", 0x258A, 0x2022, "Glorious", 0xFFFF, 0, 0, 0, 65, 1);
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        Assert.Equal("Glorious Model O Wireless", target.Target.Device.Name);

        var onlineTransport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("00000002020083", 65)),
            new(DiagnosticOperation.GetFeature, Pad("00A100020200830148", 65), Argument: 0)
        ]));
        var reading = await registry.ReadAsync(target, onlineTransport, null, CancellationToken.None);
        Assert.Equal(72, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        onlineTransport.AssertComplete();

        var asleepTransport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("00000002020083", 65)),
            new(DiagnosticOperation.GetFeature, Pad("00A400020200830000", 65), Argument: 0)
        ]));
        var asleep = await registry.ReadAsync(target, asleepTransport, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, asleep.Link);
        asleepTransport.AssertComplete();
    }

    [Fact]
    public async Task SteelSeriesHeadsetsAndMiceWorkStrictlyReadOnly()
    {
        var registry = AdapterRegistry.CreateDefault();

        // 1) Arctis Nova 7 (22A1): link=03, battery=84%, status=01 charging
        var nova7 = new HidInterface("ss-n7", "p-n7", 0x1038, 0x22A1, "Nova 7", 0xFFC0, 1, 65, 65, 0, 3);
        var n7Target = Assert.Single(registry.Detect(new([nova7], [], [])));
        var n7Transport = new FakeReceiverTransport((nova7.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("00B0", 65)),
            new(DiagnosticOperation.HidInput, Pad("00B0035401", 65))
        ]));
        var n7Reading = await registry.ReadAsync(n7Target, n7Transport, null, CancellationToken.None);
        Assert.Equal(84, n7Reading.Percentage);
        Assert.Equal(ChargingState.Charging, n7Reading.Charging);
        n7Transport.AssertComplete();

        // Nova 7 link=02 -> 明确离线，不沿用缓存旧电量
        Assert.False(SteelSeriesProtocol.DecodeB0(Convert.FromHexString("00B0025403"), SteelSeriesProtocolKind.Nova7).Online);

        // 2) Nova Pro Wireless 底座 (12E0): 06 B0 -> byte 6 = 6 (75%), byte 15 = 08 (on battery)
        var novaPro = new HidInterface("ss-np", "p-np", 0x1038, 0x12E0, "Nova Pro", 0xFFC0, 1, 64, 64, 0, 4);
        var npTarget = Assert.Single(registry.Detect(new([novaPro], [], [])));
        var npReply = new byte[64];
        npReply[0] = 0x06;
        npReply[6] = 6;
        npReply[15] = 0x08;
        var npTransport = new FakeReceiverTransport((novaPro.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("06B0", 64)),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(npReply))
        ]));
        var npReading = await registry.ReadAsync(npTarget, npTransport, null, CancellationToken.None);
        Assert.Equal(75, npReading.Percentage);
        Assert.True(npReading.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, npReading.Charging);
        npTransport.AssertComplete();

        // 3) Aerox 9 Wireless 鼠标 (1858): 00 D2 -> 00 D2 91 (bit7=charging, step 17 -> (17-1)*5 = 80%)
        var aerox = new HidInterface("ss-a9", "p-a9", 0x1038, 0x1858, "Aerox 9", 0xFFC0, 1, 65, 65, 0, 3);
        var a9Target = Assert.Single(registry.Detect(new([aerox], [], [])));
        Assert.Equal(DeviceKind.Mouse, a9Target.Target.Device.Kind);
        var a9Transport = new FakeReceiverTransport((aerox.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("00D2", 65)),
            new(DiagnosticOperation.HidInput, Pad("00D291", 65))
        ]));
        var a9Reading = await registry.ReadAsync(a9Target, a9Transport, null, CancellationToken.None);
        Assert.Equal(80, a9Reading.Percentage);
        Assert.True(a9Reading.IsEstimated);
        Assert.Equal(ChargingState.Charging, a9Reading.Charging);
        a9Transport.AssertComplete();

        // 4) 其他型号解码单元校验：Nova 5、Arctis 7+、GameBuds、Arctis 1、Arctis 9、Rival 3
        Assert.Equal(65, SteelSeriesProtocol.DecodeB0(Convert.FromHexString("B003004100"), SteelSeriesProtocolKind.Nova5).Level);
        Assert.Equal(75, SteelSeriesProtocol.DecodeB0(Convert.FromHexString("B0030301"), SteelSeriesProtocolKind.Arctis7Plus).Level);
        Assert.Equal(40, SteelSeriesProtocol.DecodeB0(Convert.FromHexString("B0000003034628"), SteelSeriesProtocolKind.GameBuds).Level);
        Assert.Equal(90, SteelSeriesProtocol.DecodeArctis1(Convert.FromHexString("0612035A")).Level);
        Assert.Equal(50, SteelSeriesProtocol.DecodeArctis9(Convert.FromHexString("AA01007F00")).Level);
        Assert.Equal(60, SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00AA3C0001")).Level);
        Assert.Equal(55, SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00370000")).Level);

        // 严禁非只读命令
        Assert.False(SteelSeriesProtocol.AllowsRequest(Convert.FromHexString(Pad("003901", 65))));
    }

    [Fact]
    public async Task HyperXHeadsetFamiliesReadBatteryAndRejectWriteCommands()
    {
        var registry = AdapterRegistry.CreateDefault();

        // 1) HyperX Cloud II Wireless (HP 03F0:0696)
        var c2 = new HidInterface("hx-c2", "p-c2", 0x03F0, 0x0696, "Cloud II", 0xFF90, 0x0303, 20, 52, 0, 3);
        var c2Target = Assert.Single(registry.Detect(new([c2], [], [])));
        var c2Transport = new FakeReceiverTransport((c2.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("06FFBB02", 52)),
            new(DiagnosticOperation.HidInput, Pad("06FFBB02000F504B", 20)),
            new(DiagnosticOperation.HidOutput, Pad("06FFBB03", 52)),
            new(DiagnosticOperation.HidInput, Pad("06FFBB0301", 20))
        ]));
        var c2Reading = await registry.ReadAsync(c2Target, c2Transport, null, CancellationToken.None);
        Assert.Equal(75, c2Reading.Percentage);
        Assert.Equal(ChargingState.Charging, c2Reading.Charging);
        c2Transport.AssertComplete();

        // 2) HyperX Cloud Alpha Wireless (03F0:098D)
        var alpha = new HidInterface("hx-a", "p-a", 0x03F0, 0x098D, "Cloud Alpha", 0xFF43, 0x0202, 64, 31, 0, 3);
        var alphaTarget = Assert.Single(registry.Detect(new([alpha], [], [])));
        var alphaTransport = new FakeReceiverTransport((alpha.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("21BB03", 31)),
            new(DiagnosticOperation.HidInput, Pad("21BB0300", 64)),
            new(DiagnosticOperation.HidOutput, Pad("21BB0B", 31)),
            new(DiagnosticOperation.HidInput, Pad("21BB0B58", 64)),
            new(DiagnosticOperation.HidOutput, Pad("21BB0C", 31)),
            new(DiagnosticOperation.HidInput, Pad("21BB0C00", 64))
        ]));
        var alphaReading = await registry.ReadAsync(alphaTarget, alphaTransport, null, CancellationToken.None);
        Assert.Equal(88, alphaReading.Percentage);
        Assert.Equal(ChargingState.NotCharging, alphaReading.Charging);
        alphaTransport.AssertComplete();

        // 3) HyperX Cloud III Wireless (03F0:05B7)
        var c3 = new HidInterface("hx-c3", "p-c3", 0x03F0, 0x05B7, "Cloud III", 0xFF13, 0x0001, 62, 62, 62, 3);
        var c3Target = Assert.Single(registry.Detect(new([c3], [], [])));
        var c3Transport = new FakeReceiverTransport((c3.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("6689", 62)),
            new(DiagnosticOperation.HidInput, Pad("66890ED730", 62)),
            new(DiagnosticOperation.HidOutput, Pad("668A", 62)),
            new(DiagnosticOperation.HidInput, Pad("668A00", 62))
        ]));
        var c3Reading = await registry.ReadAsync(c3Target, c3Transport, null, CancellationToken.None);
        Assert.Equal(48, c3Reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, c3Reading.Charging);
        c3Transport.AssertComplete();

        // 4) HyperX Cloud III S Wireless (03F0:02CC)：支持跨集合回复，严禁 SET (byte 3 = 0x00)
        var c3sOut = new HidInterface("hx-c3s", "p-c3s-out", 0x03F0, 0x02CC, "Cloud III S", 0xFF13, 1, 0, 64, 0, 3)
        {
            OutputReportIds = "0C"
        };
        var c3sIn = new HidInterface("hx-c3s", "p-c3s-in", 0x03F0, 0x02CC, "Cloud III S", 0xFF13, 2, 64, 0, 0, 3);
        var c3sTarget = Assert.Single(registry.Detect(new([c3sOut, c3sIn], [], [])));
        var c3sTransport = new FakeReceiverTransport(
            (c3sOut.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("0C0203010006", 64)),
                new(DiagnosticOperation.HidOutput, Pad("0C0203010048", 64))
            ]),
            (c3sIn.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, Pad("0C02030100065E", 64)),
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, Pad("0C020301004801", 64))
            ]));
        var c3sReading = await registry.ReadAsync(c3sTarget, c3sTransport, null, CancellationToken.None);
        Assert.Equal(94, c3sReading.Percentage);
        Assert.Equal(ChargingState.Charging, c3sReading.Charging);
        c3sTransport.AssertComplete();

        // 5) Cloud Flight 电压估算与充电判定 + Kingston Cloud II 解码
        Assert.True(HyperXHeadsetProtocol.TryDecodeCloudFlight(Convert.FromHexString(Pad("0000000F90", 20)), out bool flOff, out int? flLvl, out var flChg));
        Assert.False(flOff);
        Assert.Equal(100, flLvl);
        Assert.Equal(ChargingState.NotCharging, flChg);

        Assert.True(HyperXHeadsetProtocol.TryDecodeCloud2KingstonLevel(Convert.FromHexString("0B00BB0200000051"), out int kLvl));
        Assert.Equal(81, kLvl);

        // Cloud III S SET 命令（byte 3 = 0x00）必须被白名单拒绝
        Assert.False(HyperXHeadsetProtocol.AllowsRequest(Convert.FromHexString(Pad("0C0203000006", 64))));
    }

    [Fact]
    public async Task CorsairAdaptersWorkStrictlyReadOnlyAndBlockSoftwareMode()
    {
        var registry = AdapterRegistry.CreateDefault();

        // 1) Corsair Void Elite Wireless (0A55)
        var voidEl = new HidInterface("cs-ve", "p-ve", 0x1B1C, 0x0A55, "Void Elite", 0xFFC5, 1, 5, 2, 0, 3);
        var veTarget = Assert.Single(registry.Detect(new([voidEl], [], [])));
        var veTransport = new FakeReceiverTransport((voidEl.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, "C964"),
            new(DiagnosticOperation.HidInput, "64004EB104")
        ]));
        var veReading = await registry.ReadAsync(veTarget, veTransport, null, CancellationToken.None);
        Assert.Equal(78, veReading.Percentage);
        Assert.Equal(ChargingState.Charging, veReading.Charging);
        veTransport.AssertComplete();

        // 2) Corsair Dark Core RGB Pro SE (1B7F, Bragi 65B) & K65 PLUS Wireless (2B07)
        var dc = new HidInterface("cs-dc", "p-dc", 0x1B1C, 0x1B7F, "Dark Core", 0xFF42, 1, 65, 65, 0, 1);
        var dcTarget = Assert.Single(registry.Detect(new([dc], [], [])));
        var dcTransport = new FakeReceiverTransport((dc.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("0009020F", 65)),
            new(DiagnosticOperation.HidInput, Pad("000102002602", 65)) // 0x0226 = 550 -> 55%
        ]));
        var dcReading = await registry.ReadAsync(dcTarget, dcTransport, null, CancellationToken.None);
        Assert.Equal(55, dcReading.Percentage);
        Assert.Equal(DeviceKind.Mouse, dcReading.Device.Kind);
        dcTransport.AssertComplete();

        // 3) Corsair Virtuoso XT Wireless (0A64, Bragi bare 64B + charging prop 0x10)
        var vxt = new HidInterface("cs-vxt", "p-vxt", 0x1B1C, 0x0A64, "Virtuoso XT", 0xFF42, 1, 64, 64, 0, 3);
        var vxtTarget = Assert.Single(registry.Detect(new([vxt], [], [])));
        var vxtTransport = new FakeReceiverTransport((vxt.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("0209020F", 64)),
            new(DiagnosticOperation.HidInput, Pad("010102002A03", 64)), // 0x032A = 810 -> 81%
            new(DiagnosticOperation.HidOutput, Pad("02090210", 64)),
            new(DiagnosticOperation.HidInput, Pad("010102000100", 64))  // 1 -> Charging
        ]));
        var vxtReading = await registry.ReadAsync(vxtTarget, vxtTransport, null, CancellationToken.None);
        Assert.Equal(81, vxtReading.Percentage);
        Assert.Equal(ChargingState.Charging, vxtReading.Charging);
        vxtTransport.AssertComplete();

        // 严格只读：禁止软件模式切换（01 03 00 02）
        Assert.False(CorsairProtocol.AllowsRequest(Convert.FromHexString(Pad("020901030002", 64))));
        Assert.False(CorsairProtocol.AllowsRequest(Convert.FromHexString(Pad("00020901030002", 65))));
    }

    [Fact]
    public async Task SplitCompxSupportsEndgameGearXm2weAndDareuA950WhileBlockingUnverified4k()
    {
        var registry = AdapterRegistry.CreateDefault();

        // 1) Endgame Gear XM2we (3367:1961): FF02 Feature 08 -> FF01 Input 09 + link 03
        var xmCmd = new HidInterface("egg-1", "p-egg-cmd", 0x3367, 0x1961, "XM2we", 0xFF02, 2, 0, 0, 17, 1);
        var xmReply = new HidInterface("egg-1", "p-egg-in", 0x3367, 0x1961, "XM2we", 0xFF01, 0, 17, 0, 0, 1);
        var xmTarget = Assert.Single(registry.Detect(new([xmCmd, xmReply], [], [])));
        Assert.Equal("Endgame Gear XM2we", xmTarget.Target.Device.Name);

        var battFrame = new byte[17];
        battFrame[0] = 0x09; battFrame[1] = 0x04; battFrame[5] = 0x02; battFrame[6] = 68; battFrame[7] = 0;
        battFrame[16] = MouseBatteryProtocol.CompxChecksum(battFrame.AsSpan(0, 16));

        var linkFrame = new byte[17];
        linkFrame[0] = 0x09; linkFrame[1] = 0x03; linkFrame[5] = 0x01; linkFrame[6] = 1;
        linkFrame[16] = MouseBatteryProtocol.CompxChecksum(linkFrame.AsSpan(0, 16));

        var xmTransport = new FakeReceiverTransport(
            (xmReply.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, Convert.ToHexString(battFrame)),
                new(DiagnosticOperation.HidInput, Convert.ToHexString(linkFrame))
            ]),
            (xmCmd.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.SetFeature, "0804000000000000000000000000000049"),
                new(DiagnosticOperation.SetFeature, "080300000000000000000000000000004A")
            ]));
        var xmReading = await registry.ReadAsync(xmTarget, xmTransport, null, CancellationToken.None);
        Assert.Equal(68, xmReading.Percentage);
        Assert.True(xmReading.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, xmReading.Charging);
        xmTransport.AssertComplete();

        // 2) DAREU A950 (260D:1074)
        var dareuCmd = xmCmd with { ReceiverId = "dr-1", Path = "p-dr-cmd", VendorId = 0x260D, ProductId = 0x1074 };
        var dareuReply = xmReply with { ReceiverId = "dr-1", Path = "p-dr-in", VendorId = 0x260D, ProductId = 0x1074 };
        var drTarget = Assert.Single(registry.Detect(new([dareuCmd, dareuReply], [], [])));
        Assert.Equal("DAREU A950", drTarget.Target.Device.Name);

        var drTransport = new FakeReceiverTransport(
            (dareuReply.Path, [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidInput, "0904000000025A000000000000000000EC")]),
            (dareuCmd.Path, [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.SetFeature, "0804000000000000000000000000000049")]));
        var drReading = await registry.ReadAsync(drTarget, drTransport, null, CancellationToken.None);
        Assert.Equal(90, drReading.Percentage);
        Assert.Equal(ChargingState.NotCharging, drReading.Charging);
        drTransport.AssertComplete();

        // 3) Endgame Gear 4K (3367:1970) 登记在 DisabledPaths 中，打开句柄前跳过
        var egg4k = xmCmd with { ReceiverId = "egg-4k", Path = "p-4k", ProductId = 0x1970 };
        Assert.True(DeviceCatalog.IsDisabledPath(egg4k, out _));
        Assert.Empty(registry.Detect(new([egg4k, xmReply with { ReceiverId = "egg-4k", ProductId = 0x1970 }], [], [])));
    }

    [Fact]
    public async Task ExtendedPeripheralsAdaptersWorkAcrossAllNewBrands()
    {
        var registry = new AdapterRegistry(new ExtendedPeripheralsAdapter(null, TimeSpan.Zero));

        // 1) Pulsar X2 V3 Mini Sonix (3710:5403)
        var sonix = new HidInterface("ext-sx", "p-sx", 0x3710, 0x5403, "Pulsar", 0xFF00, 1, 0, 0, 65, 3);
        var sxTarget = Assert.Single(registry.Detect(new([sonix], [], [])));
        var sxTransport = new FakeReceiverTransport((sonix.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Convert.ToHexString(ExtendedPeripheralProtocol.EncodeRequest(ExtendedProtocolKind.PulsarSonix, 65))),
            new(DiagnosticOperation.GetFeature, Pad("0000088101000052", 65), Argument: 0)
        ]));
        var sxReading = await registry.ReadAsync(sxTarget, sxTransport, null, CancellationToken.None);
        Assert.Equal(82, sxReading.Percentage);
        sxTransport.AssertComplete();

        // 2) EWEADN S9 / H2 (089D:062F)
        var ewe = new HidInterface("ext-ewe", "p-ewe", 0x089D, 0x062F, "EWEADN", 0xFF01, 0x0010, 33, 33, 0, 1);
        var eweTarget = Assert.Single(registry.Detect(new([ewe], [], [])));
        var eweTransport = new FakeReceiverTransport((ewe.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("0030", 33)),
            new(DiagnosticOperation.HidInput, Pad("003000010B4D33303200001101014601", 33)) // M302 -> EWEADN H2, chg=1, 70%
        ]));
        var eweReading = await registry.ReadAsync(eweTarget, eweTransport, null, CancellationToken.None);
        Assert.Equal("EWEADN H2", eweReading.Device.Name);
        Assert.Equal(70, eweReading.Percentage);
        Assert.Equal(ChargingState.Charging, eweReading.Charging);
        eweTransport.AssertComplete();

        // 3) AJAZZ AJ179 V2 MAX (249A:5C2F)
        var aj = new HidInterface("ext-aj", "p-aj", 0x249A, 0x5C2F, "AJAZZ", 0xFF00, 1, 33, 33, 0, 2);
        var ajTarget = Assert.Single(registry.Detect(new([aj], [], [])));
        var ajReply = new byte[33];
        Convert.FromHexString("001000010B4E36323500001101004B01").CopyTo(ajReply, 0);
        int ajSum = 0;
        for (int i = 5; i < 32; i++) ajSum += ajReply[i];
        ajReply[32] = (byte)ajSum;
        var ajTransport = new FakeReceiverTransport((aj.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("0010", 33)),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(ajReply))
        ]));
        var ajReading = await registry.ReadAsync(ajTarget, ajTransport, null, CancellationToken.None);
        Assert.Equal(75, ajReading.Percentage);
        Assert.Equal(ChargingState.NotCharging, ajReading.Charging);
        ajTransport.AssertComplete();

        // 4) AJAZZ AJ179 Pro GearHub V5 (3151:402D)
        var gh = new HidInterface("ext-gh", "p-gh", 0x3151, 0x402D, "AJ179 Pro", 0xFFFF, 2, 0, 0, 65, 2);
        var ghTarget = Assert.Single(registry.Detect(new([gh], [], [])));
        var ghTransport = new FakeReceiverTransport((gh.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("00F7", 65)),
            new(DiagnosticOperation.GetFeature, Pad("0001005F0000", 65), Argument: 0)
        ]));
        var ghReading = await registry.ReadAsync(ghTarget, ghTransport, null, CancellationToken.None);
        Assert.Equal(95, ghReading.Percentage);
        ghTransport.AssertComplete();

        // 5) MCHOSE V3 4D (3837:1014)
        var mv3 = new HidInterface("ext-mv3", "p-mv3", 0x3837, 0x1014, "MCHOSE V3", 0xFF01, 1, 64, 64, 0, 2);
        var mv3Target = Assert.Single(registry.Detect(new([mv3], [], [])));
        var mv3Reply = new byte[64];
        mv3Reply[0] = 0x4D; mv3Reply[1] = 0x01; mv3Reply[2] = 0x01; mv3Reply[5] = 0x09; mv3Reply[6] = 0x00; mv3Reply[7] = 13;
        mv3Reply[18] = 1; mv3Reply[19] = 1; mv3Reply[20] = 88;
        byte xor = 0;
        for (int i = 2; i < 8 + 13; i++) xor ^= mv3Reply[i];
        mv3Reply[21] = xor;
        var mv3Transport = new FakeReceiverTransport((mv3.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("4D0101000009000008", 64)),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(mv3Reply))
        ]));
        var mv3Reading = await registry.ReadAsync(mv3Target, mv3Transport, null, CancellationToken.None);
        Assert.Equal(88, mv3Reading.Percentage);
        Assert.Equal(ChargingState.Charging, mv3Reading.Charging);
        mv3Transport.AssertComplete();

        // 6) AULA F87 Pro V2C 键盘 (0C45:FEFC)
        var aula = new HidInterface("ext-aula", "p-aula", 0x0C45, 0xFEFC, "AULA", 0xFF80, 1, 33, 33, 0, 1);
        var aulaTarget = Assert.Single(registry.Detect(new([aula], [], [])));
        Assert.Equal(DeviceKind.Keyboard, aulaTarget.Target.Device.Kind);
        var aulaReply = new byte[33];
        aulaReply[0] = 0x00; aulaReply[1] = 0x55; aulaReply[2] = 0x10; aulaReply[3] = 0x18; aulaReply[26] = 96;
        var aulaTransport = new FakeReceiverTransport((aula.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("00AA10", 33)),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(aulaReply))
        ]));
        var aulaReading = await registry.ReadAsync(aulaTarget, aulaTransport, null, CancellationToken.None);
        Assert.Equal(96, aulaReading.Percentage);
        aulaTransport.AssertComplete();

        // 7) Keychron Ultra-Link 8K (3434:D028)
        var kc = new HidInterface("ext-kc", "p-kc", 0x3434, 0xD028, "Keychron", 0xFF00, 1, 64, 0, 64, 4);
        var kcTarget = Assert.Single(registry.Detect(new([kc], [], [])));
        var kcReply = new byte[64];
        kcReply[0] = 0xB4; kcReply[1] = 0x06; kcReply[20] = 63;
        var kcTransport = new FakeReceiverTransport((kc.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(kcReply)),
            new(DiagnosticOperation.SetFeature, Pad("B306", 64))
        ]));
        var kcReading = await registry.ReadAsync(kcTarget, kcTransport, null, CancellationToken.None);
        Assert.Equal(63, kcReading.Percentage);
        kcTransport.AssertComplete();

        // 8) Audeze Maxwell (3329:4B19)：在线单帧读取 0CD6，且 "Dongle" 产品名直接离线不发包
        var audeze = new HidInterface("ext-au", "p-au", 0x3329, 0x4B19, "Audeze Maxwell HID", 0xFF13, 1, 62, 62, 0, 3);
        var auTarget = Assert.Single(registry.Detect(new([audeze], [], [])));
        var auTransport = new FakeReceiverTransport((audeze.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("060780055A0300D60C", 62)),
            new(DiagnosticOperation.HidInput, Pad("070080055A0400D60C00004C", 62))
        ]));
        var auReading = await registry.ReadAsync(auTarget, auTransport, null, CancellationToken.None);
        Assert.Equal(76, auReading.Percentage);
        Assert.Equal(ChargingState.NotCharging, auReading.Charging);
        auTransport.AssertComplete();

        var audezeOff = audeze with { Product = "Audeze Maxwell Dongle" };
        var auOffTarget = Assert.Single(registry.Detect(new([audezeOff], [], [])));
        var zeroTransport = new FakeReceiverTransport();
        var auOffReading = await registry.ReadAsync(auOffTarget, zeroTransport, null, CancellationToken.None);
        Assert.Equal(LinkState.Offline, auOffReading.Link);
        Assert.Equal(0, zeroTransport.Calls);
    }

    [Fact]
    public async Task NewBrandDiagnosticsAreSanitizedAndReplayable()
    {
        var nova7 = new HidInterface("ss-diag", "p-ss", 0x1038, 0x22A1, "Nova 7", 0xFFC0, 1, 65, 65, 0, 3);
        var transport = new FakeReceiverTransport((nova7.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("00B0", 65)),
            new(DiagnosticOperation.HidInput, Pad("00B0035A03DEADBEEF", 65))
        ]));
        string report = await DiagnosticReport.GenerateAsync(new([nova7], [], []), transport, CancellationToken.None);
        transport.AssertComplete();
        Assert.Contains("验证=仅按资料", report);

        var (inventory, replay, _) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(inventory));
        var reading = await registry.ReadAsync(detected, replay, null, CancellationToken.None);
        Assert.Equal(90, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        replay.AssertComplete();
    }

    [Fact]
    public void EveryNewCatalogEntryIsDocsOnlyAndKnownToHidDiscovery()
    {
        var newPrefixes = new HashSet<string>(StringComparer.Ordinal)
        {
            "asus-mouse:", "asus-headset:", "asus-omni:",
            "steelseries:", "hyperx:", "corsair:", "split-compx:", "ext:"
        };
        var expanded = DeviceCatalog.Entries.Where(entry => newPrefixes.Contains(entry.AdapterPrefix)).ToArray();
        Assert.True(expanded.Length >= 100);
        foreach (var entry in expanded)
        {
            Assert.Equal(VerificationTier.DocsOnly, entry.Verification);
            Assert.True(AdapterRegistry.IsKnownHidReceiver(entry.VendorId, entry.ProductId),
                $"新目录项 {entry.Model} ({entry.VidPid}) 未纳入 AdapterRegistry.IsKnownHidReceiver");
        }
        foreach (var (receiver, _) in MouseReceiverCatalog.BrandModels)
        {
            Assert.True(AdapterRegistry.IsKnownHidReceiver(receiver.VendorId, receiver.ProductId));
            Assert.Equal(VerificationTier.DocsOnly, DeviceCatalog.VerificationFor(receiver.VendorId, receiver.ProductId));
        }
    }
}

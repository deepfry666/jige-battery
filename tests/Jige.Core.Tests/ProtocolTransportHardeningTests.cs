using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using static Jige.Core.Tests.LogitechBatteryTests;

namespace Jige.Core.Tests;

public sealed class ProtocolTransportHardeningTests
{
    private static string Pad(string hex, int length) => hex.PadRight(length * 2, '0');

    [Fact]
    public async Task KeychronSplitsInputOnlyAndFeatureOnlyHandles()
    {
        var registry = new AdapterRegistry(new ExtendedPeripheralsAdapter(null, TimeSpan.Zero));
        var kc = new HidInterface("ext-kc-split", "p-kc-split", 0x3434, 0xD028, "Keychron", 0xFF00, 1, 64, 0, 64, 4);
        var target = Assert.Single(registry.Detect(new([kc], [], [])));

        var reply = new byte[64];
        reply[0] = 0xB4;
        reply[1] = 0x06;
        reply[20] = 77;
        var transport = new FakeReceiverTransport((kc.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(reply)),
            new(DiagnosticOperation.SetFeature, Pad("B306", 64))
        ]));

        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(77, reading.Percentage);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Collection(transport.OpenedHids,
            input =>
            {
                Assert.True(input.InputOnly);
                Assert.False(input.FeatureOnly);
                Assert.Equal((byte)0xB4, input.RequiredInputReportId);
                Assert.Null(input.RequiredFeatureReportId);
                Assert.Null(input.RequiredOutputReportId);
            },
            feature =>
            {
                Assert.True(feature.FeatureOnly);
                Assert.False(feature.InputOnly);
                Assert.Equal((byte)0xB3, feature.RequiredFeatureReportId);
                Assert.Null(feature.RequiredInputReportId);
                Assert.Null(feature.RequiredOutputReportId);
            });
        transport.AssertComplete();
    }

    [Fact]
    public async Task Cloud3FeatureOnlyEndpointSplitsFeatureAndInputHandlesAndDisposesBoth()
    {
        var hid = new HidInterface("r-c3f", "h-c3f", 0x03F0, 0x05B7, null, 0xFF13, 1, 62, 0, 65, 3);
        var replay = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("6689", 65)),
            new(DiagnosticOperation.HidInput, "66890ED740"),
            new(DiagnosticOperation.SetFeature, Pad("668A", 65)),
            new(DiagnosticOperation.HidInput, "668A01")
        ]));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));

        var reading = await registry.ReadAsync(target, replay, null, CancellationToken.None);
        Assert.Equal(64, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Collection(replay.OpenedHids,
            feature =>
            {
                Assert.True(feature.FeatureOnly);
                Assert.False(feature.InputOnly);
                Assert.Equal((byte)0x66, feature.RequiredFeatureReportId);
                Assert.Null(feature.RequiredInputReportId);
            },
            input =>
            {
                Assert.True(input.InputOnly);
                Assert.False(input.FeatureOnly);
                Assert.Equal((byte)0x66, input.RequiredInputReportId);
                Assert.Null(input.RequiredFeatureReportId);
            });
        replay.AssertComplete();
    }

    [Fact]
    public async Task MouseFeatureInputOpensSeparateInputOnlyAndFeatureOnlyHandlesAndDisposesOnFailure()
    {
        var endpoint = new HidInterface("rx-fi", "p-fi", 0x373B, 0x111A, null, 0xFF04, 1, 17, 0, 17, 1);
        var route = new MouseReceiver("Test Compx Feature", 0x373B, 0x111A, MouseFamily.CompxFeature,
            MouseEvidence.SingleSource, Enabled: true);

        var okTransport = new FakeReceiverTransport((endpoint.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, MouseAdapterTests.CompxRequest),
            new(DiagnosticOperation.HidInput, MouseAdapterTests.CapturedCompxReply)
        ]));
        using (var session = MouseReceiverAdapter.OpenQuerySession(okTransport, endpoint, route, [], CancellationToken.None))
        {
            await session.WriteFeatureAsync(Convert.FromHexString(MouseAdapterTests.CompxRequest), CancellationToken.None);
            var buf = new byte[17];
            int read = await session.ReadInputAsync(buf, CancellationToken.None);
            Assert.Equal(17, read);
            Assert.Equal(Convert.FromHexString(MouseAdapterTests.CapturedCompxReply), buf);
        }
        Assert.Collection(okTransport.OpenedHids,
            input =>
            {
                Assert.True(input.InputOnly);
                Assert.False(input.FeatureOnly);
                Assert.Equal((byte)8, input.RequiredInputReportId);
                Assert.Null(input.RequiredFeatureReportId);
            },
            feature =>
            {
                Assert.True(feature.FeatureOnly);
                Assert.False(feature.InputOnly);
                Assert.Equal((byte)8, feature.RequiredFeatureReportId);
                Assert.Null(feature.RequiredInputReportId);
            });
        okTransport.AssertComplete();

        // 若第二个 FeatureOnly 句柄打开失败，必须释放已打开的 InputOnly 句柄。
        var failTransport = new FakeReceiverTransport((endpoint.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.OpenHid, Issue: ReadIssue.AccessDenied)
        ]));
        var err = Assert.Throws<TransportException>(() =>
            MouseReceiverAdapter.OpenQuerySession(failTransport, endpoint, route, [], CancellationToken.None));
        Assert.Equal(ReadIssue.AccessDenied, err.Issue);
        failTransport.AssertComplete();
    }

    [Fact]
    public async Task AsusStrixGoRejectsEchoAndZeroMillivoltRetriesAndPreservesGenuineOffline()
    {
        // 合法离线标记仍判离线
        Assert.True(AsusHidProtocol.TryDecodeHeadsetBattery(
            Convert.FromHexString(Pad("FFFFAA", 64)), false, out bool off1, out _, out _));
        Assert.True(off1);
        Assert.True(AsusHidProtocol.TryDecodeHeadsetBattery(
            Convert.FromHexString(Pad("FF000000", 64)), false, out bool off2, out _, out _));
        Assert.True(off2);

        // 请求回显、无关报告与 0 mV 帧必须被拒绝（返回 false），而非误判离线或 10% 在线
        byte[] echo = AsusHidProtocol.EncodeHeadsetBattery(64);
        Assert.False(AsusHidProtocol.TryDecodeHeadsetBattery(echo, false, out _, out _, out _));
        Assert.False(AsusHidProtocol.TryDecodeHeadsetBattery(
            Convert.FromHexString(Pad("FF20010203", 64)), false, out _, out _, out _));
        Assert.False(AsusHidProtocol.TryDecodeHeadsetBattery(
            Convert.FromHexString(Pad("FF1B0000000000000003000000", 64)), false, out _, out _, out _));

        var registry = new AdapterRegistry(new AsusHidAdapter(null, TimeSpan.Zero));
        var hs = new HidInterface("asus-go", "p-go", 0x0B05, 0x18D6, "ROG Strix Go 2.4", 0xFF00, 1, 0, 0, 64, 3);
        var target = Assert.Single(registry.Detect(new([hs], [], [])));
        string reqHex = Convert.ToHexString(echo);

        // 第 1 次回显、第 2 次 0 mV、第 3 次有效电压 -> 重试循环正常恢复在线
        var retryRecover = new FakeReceiverTransport((hs.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, reqHex),
            new(DiagnosticOperation.GetFeature, reqHex, Argument: 0xFF),
            new(DiagnosticOperation.SetFeature, reqHex),
            new(DiagnosticOperation.GetFeature, Pad("FF1B0000000000000003000000", 64), Argument: 0xFF),
            new(DiagnosticOperation.SetFeature, reqHex),
            new(DiagnosticOperation.GetFeature, Pad("FF1B000000000000000300F70E", 64), Argument: 0xFF)
        ]));
        var recovered = await registry.ReadAsync(target, retryRecover, null, CancellationToken.None);
        Assert.Equal(LinkState.Online, recovered.Link);
        Assert.Equal(75, recovered.Percentage);
        retryRecover.AssertComplete();

        // 连续 3 次均收到请求回显 -> 回退为 Asleep / Timeout，不误报 Offline，且诊断回放结果一致
        var allEcho = new FakeReceiverTransport((hs.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, reqHex),
            new(DiagnosticOperation.GetFeature, reqHex, Argument: 0xFF),
            new(DiagnosticOperation.SetFeature, reqHex),
            new(DiagnosticOperation.GetFeature, reqHex, Argument: 0xFF),
            new(DiagnosticOperation.SetFeature, reqHex),
            new(DiagnosticOperation.GetFeature, reqHex, Argument: 0xFF)
        ]));
        string report = await DiagnosticReport.GenerateAsync(new([hs], [], []), allEcho, CancellationToken.None);
        allEcho.AssertComplete();
        Assert.Contains("AsusHs len=64 rid=FF b1=08 b2=00", report);
        var (inv, replay, _) = FakeReceiverTransport.FromReport(report);
        var replayTarget = Assert.Single(registry.Detect(inv));
        var replayReading = await registry.ReadAsync(replayTarget, replay, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, replayReading.Link);
        Assert.Equal(ReadIssue.Timeout, replayReading.Issue);
        replay.AssertComplete();
    }

    [Fact]
    public async Task HardenedDecodersRejectRequestEchoesAllZeroAndNoiseWhileAcceptingValidFrames()
    {
        // 1) Pulsar Sonix
        byte[] sonixEcho = ExtendedPeripheralProtocol.EncodeRequest(ExtendedProtocolKind.PulsarSonix, 65);
        Assert.False(ExtendedPeripheralProtocol.TryDecodePulsarSonix(sonixEcho, out _));
        Assert.False(ExtendedPeripheralProtocol.AllowsRequest(ExtendedProtocolKind.PulsarSonix, [0x00, 0x00, 0x08]));
        byte[] sonixEchoAltByte1 = (byte[])sonixEcho.Clone();
        sonixEchoAltByte1[1] = 0x01;
        Assert.False(ExtendedPeripheralProtocol.TryDecodePulsarSonix(sonixEchoAltByte1, out _));
        Assert.False(ExtendedPeripheralProtocol.TryDecodePulsarSonix(Convert.FromHexString(Pad("0000088101000000", 65)), out _));
        Assert.True(ExtendedPeripheralProtocol.TryDecodePulsarSonix(Convert.FromHexString(Pad("0000088101000052", 65)), out int sxLevel));
        Assert.Equal(82, sxLevel);
        Assert.True(ExtendedPeripheralProtocol.TryDecodePulsarSonix(Convert.FromHexString(Pad("0000088101010000", 65)), out int sxZero));
        Assert.Equal(0, sxZero);

        // 2) EWEADN
        Assert.False(ExtendedPeripheralProtocol.TryDecodeEweadn(Convert.FromHexString(Pad("0030", 33)), out _, out _, out _, out _));
        Assert.False(ExtendedPeripheralProtocol.TryDecodeEweadn(Convert.FromHexString(Pad("30", 32)), out _, out _, out _, out _));
        Assert.True(ExtendedPeripheralProtocol.TryDecodeEweadn(Convert.FromHexString(Pad("0002", 33)), out bool eweAsleep, out _, out _, out _));
        Assert.True(eweAsleep);

        // 3) SteelSeries Rival 3
        Assert.False(SteelSeriesProtocol.DecodeRival3(new byte[65]).Matched);
        Assert.False(SteelSeriesProtocol.DecodeRival3(Convert.FromHexString(Pad("00AA01", 65))).Matched);
        Assert.False(SteelSeriesProtocol.DecodeRival3(Convert.FromHexString(Pad("AA01", 64))).Matched);
        Assert.False(SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00000500")).Matched);
        Assert.Equal(60, SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00AA3C0001")).Level);
        Assert.Equal(1, SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00AA010001")).Level);
        Assert.Equal(55, SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00370000")).Level);
        var rivalZeroChg = SteelSeriesProtocol.DecodeRival3(Convert.FromHexString("00000001"));
        Assert.True(rivalZeroChg.Matched);
        Assert.Equal(0, rivalZeroChg.Level);
        Assert.Equal(ChargingState.Charging, rivalZeroChg.Charging);

        // 4) SteelSeries Nova Pro: 必须校验 06 B0 帧头且诊断脱敏保留 06 B0 可无损回放
        var badNovaPro = new byte[64];
        badNovaPro[0] = 0x06;
        badNovaPro[1] = 0x00;
        badNovaPro[6] = 6;
        badNovaPro[15] = 0x08;
        Assert.False(SteelSeriesProtocol.DecodeNovaPro(badNovaPro).Matched);
        badNovaPro[0] = 0x00;
        badNovaPro[1] = 0xB0;
        Assert.False(SteelSeriesProtocol.DecodeNovaPro(badNovaPro).Matched);
        Assert.False(SteelSeriesProtocol.DecodeNovaPro(SteelSeriesProtocol.Encode(64, 0x06, 0xB0)).Matched);

        var goodNovaPro = new byte[64];
        goodNovaPro[0] = 0x06;
        goodNovaPro[1] = 0xB0;
        goodNovaPro[6] = 6;
        goodNovaPro[10] = 0xDE;
        goodNovaPro[11] = 0xAD;
        goodNovaPro[15] = 0x08;
        var npHid = new HidInterface("ss-np-d", "p-np-d", 0x1038, 0x12E0, "Nova Pro", 0xFFC0, 1, 64, 64, 0, 4);
        byte[] sanitizedNp = DiagnosticPrivacy.HidReply(new SteelSeriesAdapter(), npHid, goodNovaPro,
            SteelSeriesProtocol.Encode(64, 0x06, 0xB0), []);
        Assert.Equal(0x06, sanitizedNp[0]);
        Assert.Equal(0xB0, sanitizedNp[1]);
        Assert.Equal(0x00, sanitizedNp[10]);
        var npDecoded = SteelSeriesProtocol.DecodeNovaPro(sanitizedNp);
        Assert.True(npDecoded.Matched);
        Assert.Equal(75, npDecoded.Level);

        // 5) SteelSeries Nova 5 / Arctis 7+ / Arctis 1: 拒绝全零载荷请求回显
        Assert.False(SteelSeriesProtocol.DecodeB0(Convert.FromHexString(Pad("00B0", 65)), SteelSeriesProtocolKind.Nova5).Matched);
        Assert.False(SteelSeriesProtocol.DecodeB0(Convert.FromHexString(Pad("B0", 64)), SteelSeriesProtocolKind.Nova5).Matched);
        Assert.False(SteelSeriesProtocol.DecodeB0(Convert.FromHexString(Pad("00B0", 65)), SteelSeriesProtocolKind.Arctis7Plus).Matched);
        Assert.False(SteelSeriesProtocol.DecodeArctis1(Convert.FromHexString(Pad("0612", 64))).Matched);
        Assert.False(SteelSeriesProtocol.DecodeB0(Convert.FromHexString("B002000000"), SteelSeriesProtocolKind.Nova5).Online);
        Assert.False(SteelSeriesProtocol.DecodeB0(Convert.FromHexString("B0010000"), SteelSeriesProtocolKind.Arctis7Plus).Online);
        Assert.False(SteelSeriesProtocol.DecodeArctis1(Convert.FromHexString("06120100")).Online);

        // 6) HyperX Cloud III S: 拒绝原样 GET 回显
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloud3SLevel(HyperXHeadsetProtocol.EncodeCloud3S(64, 0x06), out _));
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloud3SCharging(HyperXHeadsetProtocol.EncodeCloud3S(64, 0x48), out _));
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloud3SLevel(Convert.FromHexString("0C020301000600"), out _));
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloud3SCharging(Convert.FromHexString("0C020301004800"), out _));
        Assert.True(HyperXHeadsetProtocol.TryDecodeCloud3SCharging(Convert.FromHexString("0D0000000A00"), out var c3sNotChg));
        Assert.Equal(ChargingState.NotCharging, c3sNotChg);
        Assert.True(HyperXHeadsetProtocol.TryDecodeCloud3SCharging(Convert.FromHexString("0C020302004800"), out var c3sReplyNotChg));
        Assert.Equal(ChargingState.NotCharging, c3sReplyNotChg);

        // 7) HyperX Cloud Flight: 拒绝全零、0x64 事件与无头噪声；兼容 >20B 补零 Windows HID 输入长度
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloudFlight(new byte[20], out _, out _, out _));
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloudFlight(Convert.FromHexString(Pad("6401000000", 20)), out _, out _, out _));
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloudFlight(Convert.FromHexString(Pad("21BB030000", 20)), out _, out _, out _));
        Assert.False(HyperXHeadsetProtocol.TryDecodeCloudFlight(Convert.FromHexString(Pad("AA55010F90", 20)), out _, out _, out _));
        Assert.True(HyperXHeadsetProtocol.TryDecodeCloudFlight(Convert.FromHexString(Pad("21FF050000", 64)), out bool cfOff, out _, out _));
        Assert.True(cfOff);
        Assert.True(HyperXHeadsetProtocol.TryDecodeCloudFlight(Convert.FromHexString(Pad("6500000F90", 20)), out bool cf65Off, out int? cf65Lvl, out _));
        Assert.False(cf65Off);
        Assert.Equal(100, cf65Lvl);

        var cfHid = new HidInterface("hx-cf", "p-cf", 0x0951, 0x16C4, "Cloud Flight", 0xFF90, 0x0303, 64, 20, 0, 3);
        var registry = AdapterRegistry.CreateDefault();
        var cfTarget = Assert.Single(registry.Detect(new([cfHid], [], [])));
        var cfTransport = new FakeReceiverTransport((cfHid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Pad("21FF05", 20)),
            new(DiagnosticOperation.HidInput, Pad("6401000000", 64)),
            new(DiagnosticOperation.HidInput, Pad("21FF050F90", 64))
        ]));
        var cfReading = await registry.ReadAsync(cfTarget, cfTransport, null, CancellationToken.None);
        Assert.Equal(LinkState.Online, cfReading.Link);
        Assert.Equal(100, cfReading.Percentage);
        Assert.True(cfReading.IsEstimated);
        cfTransport.AssertComplete();
    }

    [Fact]
    public async Task LogitechCachedBattery1000FallsBackTo1001WhileChargingAndRecoversExactPercentOnUnplug()
    {
        var cable = new HidInterface("logi-mxv", "long", 0x046D, 0xC08A, "MX Vertical", 0xFF00, 2, 20, 20, 0, 2);
        var shortHid = cable with { Path = "short", Usage = 1, InputLength = 7, OutputLength = 7 };

        // 场景 A：启动时正在充电（0x1000 返回 00 00 01 -> 发现阶段回退 0x1001），随后拔线应立即回到精确 0x1000 百分比
        var registryA = AdapterRegistry.CreateDefault();
        var deviceA = Assert.Single(registryA.Detect(new([cable, shortHid], [], [])));
        var sessionA1 = new BatteryHidStub(request => (request[2], request[3]) switch
        {
            (0, 0x1A) => [Frame($"11FF001A0405{request[6]:X2}", 20)],
            (0, 0x0C) => [Frame("11FF000C07", 20)],
            (7, 0x0A) => [Frame("11FF070A000001", 20)],
            (0, 0x0D) => [Frame("11FF000D09", 20)],
            (9, 0x0A) => [Frame("11FF090A0F0A87", 20)], // 3850 mV -> 50% ≈
            (0, _) => [Frame($"11FF00{request[3]:X2}00", 20)],
            _ => throw new InvalidOperationException($"意外发包 {Convert.ToHexString(request)}")
        });
        var r1 = Assert.Single(await registryA.ReadDevicesAsync(deviceA,
            new BatteryTransportStub(h => h.Path == shortHid.Path ? new BatteryHidStub(_ => []) : sessionA1), null, default));
        Assert.Equal(50, r1.Percentage);
        Assert.True(r1.IsEstimated);
        Assert.Equal(ChargingState.Charging, r1.Charging);

        // 第 2 轮仍在充电：先查缓存 0x1000（索引 7），再用已缓存的 0x1001 回退索引 9（不重复 GetFeature）
        var sessionA2 = new BatteryHidStub(request => (request[2], request[3]) switch
        {
            (7, 0x0A) => [Frame("11FF070A000001", 20)],
            (9, 0x0A) => [Frame("11FF090A0F0A87", 20)],
            _ => throw new InvalidOperationException($"意外发包 {Convert.ToHexString(request)}")
        });
        var r2 = Assert.Single(await registryA.ReadDevicesAsync(deviceA,
            new BatteryTransportStub(h => h.Path == shortHid.Path ? new BatteryHidStub(_ => []) : sessionA2), null, default));
        Assert.Equal(50, r2.Percentage);
        Assert.True(r2.IsEstimated);
        Assert.Equal(ChargingState.Charging, r2.Charging);
        Assert.Equal(2, sessionA2.Writes.Count);

        // 第 3 轮停止充电：主缓存仍是 0x1000，单次查询直接返回精确 90%，不卡在 ≈ 电压估算
        var sessionA3 = new BatteryHidStub(request =>
        {
            Assert.Equal(Frame("11FF070A", 20), request);
            return [Frame("11FF070A5A3200", 20)];
        });
        var r3 = Assert.Single(await registryA.ReadDevicesAsync(deviceA,
            new BatteryTransportStub(h => h.Path == shortHid.Path ? new BatteryHidStub(_ => []) : sessionA3), null, default));
        Assert.Equal(90, r3.Percentage);
        Assert.False(r3.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, r3.Charging);
        Assert.Contains("电量 1000", r3.ProtocolSummary);
        Assert.Single(sessionA3.Writes);

        // 场景 B：启动时在放电（0x1000 返回精确 80%，尚未探测 0x1001），随后插线充电应动态回退到 0x1001，再拔线恢复精确 0x1000
        var registryB = AdapterRegistry.CreateDefault();
        var deviceB = Assert.Single(registryB.Detect(new([cable, shortHid], [], [])));
        var sessionB1 = new BatteryHidStub(request => (request[2], request[3]) switch
        {
            (0, 0x1A) => [Frame($"11FF001A0405{request[6]:X2}", 20)],
            (0, 0x0C) => [Frame("11FF000C07", 20)],
            (7, 0x0A) => [Frame("11FF070A503200", 20)],
            (0, _) => [Frame($"11FF00{request[3]:X2}00", 20)],
            _ => throw new InvalidOperationException($"意外发包 {Convert.ToHexString(request)}")
        });
        var b1 = Assert.Single(await registryB.ReadDevicesAsync(deviceB,
            new BatteryTransportStub(h => h.Path == shortHid.Path ? new BatteryHidStub(_ => []) : sessionB1), null, default));
        Assert.Equal(80, b1.Percentage);
        Assert.False(b1.IsEstimated);

        var sessionB2 = new BatteryHidStub(request => (request[2], request[3]) switch
        {
            (7, 0x0A) => [Frame("11FF070A000001", 20)],
            (0, 0x0D) => [Frame("11FF000D09", 20)],
            (9, 0x0A) => [Frame("11FF090A0F0A87", 20)],
            _ => throw new InvalidOperationException($"意外发包 {Convert.ToHexString(request)}")
        });
        var b2 = Assert.Single(await registryB.ReadDevicesAsync(deviceB,
            new BatteryTransportStub(h => h.Path == shortHid.Path ? new BatteryHidStub(_ => []) : sessionB2), null, default));
        Assert.Equal(50, b2.Percentage);
        Assert.True(b2.IsEstimated);
        Assert.Equal(ChargingState.Charging, b2.Charging);
        Assert.Contains("电量 1001", b2.ProtocolSummary);

        var sessionB3 = new BatteryHidStub(request =>
        {
            Assert.Equal(Frame("11FF070A", 20), request);
            return [Frame("11FF070A553200", 20)];
        });
        var b3 = Assert.Single(await registryB.ReadDevicesAsync(deviceB,
            new BatteryTransportStub(h => h.Path == shortHid.Path ? new BatteryHidStub(_ => []) : sessionB3), null, default));
        Assert.Equal(85, b3.Percentage);
        Assert.False(b3.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, b3.Charging);
        Assert.Contains("电量 1000", b3.ProtocolSummary);
    }

    [Fact]
    public void LogitechDetectSetsVerificationFromCatalogAndAllHardwareVerifiedHaveNotes()
    {
        var registry = AdapterRegistry.CreateDefault();
        var bolt = new HidInterface("r-bolt", "p-bolt", 0x046D, 0xC548, "Logi Bolt", 0xFF00, 2, 20, 20, 0, 2);
        var unknownRx = bolt with { ReceiverId = "r-unk", Path = "p-unk", ProductId = 0xC5FF, Product = "Unknown Receiver" };
        var detected = registry.Detect(new([bolt, unknownRx], [], [])).ToArray();
        Assert.Equal(2, detected.Length);
        Assert.Equal(AdaptationSource.Catalog, detected[0].Target.Device.Source);
        Assert.Equal(DeviceCatalog.VerificationFor(0x046D, 0xC548), detected[0].Target.Device.Verification);
        Assert.Equal(AdaptationSource.Generic, detected[1].Target.Device.Source);
        Assert.Equal(VerificationTier.DocsOnly, detected[1].Target.Device.Verification);

        var verified = DeviceCatalog.HardwareVerified.ToArray();
        Assert.Equal(4, verified.Length);
        Assert.All(verified, entry => Assert.False(string.IsNullOrWhiteSpace(entry.VerifiedNote),
            $"已实机验证条目 {entry.VidPid} 缺少 VerifiedNote"));
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(70, true)]
    public async Task MouseReceiverSiblingNoiseDoesNotExhaustPrimaryAttemptBudget(int noiseFrames, bool expectCapClose)
    {
        var primary = new HidInterface("rx-noise", "p-main", 0x373B, 0x1031, null, 0xFF02, 2, 17, 17, 0, 1);
        var sibling = primary with { Path = "p-sib", UsagePage = 0xFF05, Usage = 0, InputLength = 8, OutputLength = 0 };
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([primary, sibling], [], [])));

        var primaryReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int siblingReads = 0;
        bool siblingDisposed = false;
        byte[] replyBytes = Convert.FromHexString(MouseAdapterTests.CapturedCompxReply);

        var transport = new BatteryTransportStub(hid => hid.Path == primary.Path
            ? new GatedPrimarySession(primaryReady.Task, replyBytes)
            : new NoisySiblingSession(noiseFrames, expectCapClose,
                onRead: () => Interlocked.Increment(ref siblingReads),
                onThreshold: () => primaryReady.TrySetResult(),
                onDispose: () =>
                {
                    siblingDisposed = true;
                    if (expectCapClose)
                        primaryReady.TrySetResult();
                }));

        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(25, reading.Percentage);
        Assert.True(siblingDisposed);
        if (expectCapClose)
            Assert.Equal(64, siblingReads);
        else
            Assert.Equal(noiseFrames, siblingReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cloud3SPropagatesDisconnectInsteadOfSwallowingAndDisposesSiblings(bool disconnectOnPrimary)
    {
        var output = new HidInterface("r-c3s-d", "out", 0x03F0, 0x02CC, null, 0xFF13, 1,
            (ushort)(disconnectOnPrimary ? 6 : 0), 64, 0, 3) { OutputReportIds = "0C" };
        var input = output with { Path = "in", Usage = 2, InputLength = 6, OutputLength = 0, OutputReportIds = null };
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([output, input], [], [])));

        var transport = disconnectOnPrimary
            ? new FakeReceiverTransport(
                (output.Path,
                [
                    new(DiagnosticOperation.OpenHid),
                    new(DiagnosticOperation.HidInput, Issue: ReadIssue.Disconnected),
                    new(DiagnosticOperation.HidOutput, Pad("0C0203010006", 64))
                ]),
                (input.Path,
                [
                    new(DiagnosticOperation.OpenHid),
                    new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
                ]))
            : new FakeReceiverTransport(
                (output.Path,
                [
                    new(DiagnosticOperation.OpenHid),
                    new(DiagnosticOperation.HidOutput, Pad("0C0203010006", 64))
                ]),
                (input.Path,
                [
                    new(DiagnosticOperation.OpenHid),
                    new(DiagnosticOperation.HidInput, Issue: ReadIssue.Disconnected)
                ]));

        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Offline, reading.Link);
        Assert.Equal(ReadIssue.Disconnected, reading.Issue);
        transport.AssertComplete();
    }

    [Fact]
    public async Task Cloud3SPropagatesDisconnectDuringChargingQueryAfterReusingSiblingHandle()
    {
        var output = new HidInterface("r-c3s-d2", "out", 0x03F0, 0x02CC, null, 0xFF13, 1, 0, 64, 0, 3)
            { OutputReportIds = "0C" };
        var input = output with { Path = "in", Usage = 2, InputLength = 6, OutputLength = 0, OutputReportIds = null };
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([output, input], [], [])));

        var transport = new FakeReceiverTransport(
            (output.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("0C0203010006", 64)),
                new(DiagnosticOperation.HidOutput, Pad("0C0203010048", 64))
            ]),
            (input.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, "0D000000014B"),
                new(DiagnosticOperation.HidInput, Issue: ReadIssue.Disconnected)
            ]));

        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Offline, reading.Link);
        Assert.Equal(ReadIssue.Disconnected, reading.Issue);
        Assert.Single(transport.OpenedHids, h => h.Path == input.Path);
        transport.AssertComplete();
    }

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(1, 10, 10)]
    public async Task AsusOmniDiagnosticPrivacyPreservesByte11LowBatteryAndReplaysAccurately(
        byte coarseB6, byte exactB11, int expectedPercent)
    {
        var omniCtrl = new HidInterface("omni-low", "p-omni-c", 0x0B05, 0x1ACE, "ROG OMNI", 0xFF02, 1, 64, 64, 0, 2);
        var omniKb = omniCtrl with { Path = "p-omni-k", UsagePage = 0xFF00 };

        var kbReply = new byte[64];
        kbReply[0] = 0x02;
        kbReply[1] = 0x12;
        kbReply[2] = 0x01;
        kbReply[6] = coarseB6;
        kbReply[9] = 0x00;
        kbReply[11] = exactB11;
        kbReply[20] = 0xDE;
        kbReply[21] = 0xAD;
        kbReply[22] = 0xBE;
        kbReply[23] = 0xEF;

        var transport = new FakeReceiverTransport(
            (omniCtrl.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("01A0", 64)),
                new(DiagnosticOperation.HidInput, Pad("01A0000000061B0200", 64)) // 0x1B06 = Falchion RX (Byte11)
            ]),
            (omniKb.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Pad("021201", 64)),
                new(DiagnosticOperation.HidInput, Convert.ToHexString(kbReply))
            ]));

        string report = await DiagnosticReport.GenerateAsync(new([omniCtrl, omniKb], [], []), transport, CancellationToken.None);
        transport.AssertComplete();
        Assert.DoesNotContain("DEADBEEF", report);

        var (inventory, replay, _) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(inventory));
        var reading = Assert.Single(await registry.ReadDevicesAsync(detected, replay, null, CancellationToken.None));
        Assert.Equal("ROG Falchion RX Low Profile", reading.Device.Name);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(expectedPercent, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        replay.AssertComplete();
    }

    [Fact]
    public void AsusOmniDiagnosticPrivacyStillScrubsGenuineAsleepKeyboardFrames()
    {
        var omniKb = new HidInterface("omni-s", "p-omni-k", 0x0B05, 0x1ACE, "ROG OMNI", 0xFF00, 1, 64, 64, 0, 2);
        var asleepFrame = new byte[64];
        asleepFrame[0] = 0x02;
        asleepFrame[1] = 0xFF;
        asleepFrame[2] = 0xAA;
        asleepFrame[6] = 0x55;
        asleepFrame[9] = 0x01;
        asleepFrame[11] = 0x66;

        byte[] sanitized = DiagnosticPrivacy.HidReply(new AsusOmniAdapter(), omniKb, asleepFrame, [], []);
        Assert.Equal(0x02, sanitized[0]);
        Assert.Equal(0xFF, sanitized[1]);
        Assert.Equal(0xAA, sanitized[2]);
        Assert.Equal(0x00, sanitized[6]);
        Assert.Equal(0x00, sanitized[9]);
        Assert.Equal(0x00, sanitized[11]);
    }

    private sealed class GatedPrimarySession(Task ready, byte[] reply) : IHidSession
    {
        public ValueTask WriteOutputAsync(byte[] report, CancellationToken token) => ValueTask.CompletedTask;

        public async ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token)
        {
            await ready.WaitAsync(token).ConfigureAwait(false);
            reply.CopyTo(report, 0);
            return reply.Length;
        }

        public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class NoisySiblingSession(
        int maxNoise, bool infiniteUntilCap, Func<int> onRead, Action onThreshold, Action onDispose) : IHidSession
    {
        private int count;

        public ValueTask WriteOutputAsync(byte[] report, CancellationToken token) => throw new NotSupportedException();

        public async ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token)
        {
            int next = Interlocked.Increment(ref count);
            if (!infiniteUntilCap && next > maxNoise)
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return 0;
            }
            onRead();
            Array.Clear(report);
            report[0] = 0x09;
            report[1] = 0x1D;
            if (!infiniteUntilCap && next == maxNoise)
                onThreshold();
            return report.Length;
        }

        public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => onDispose();
    }
}

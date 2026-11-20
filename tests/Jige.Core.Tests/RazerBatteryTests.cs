using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using static Jige.Core.Tests.LogitechBatteryTests;

namespace Jige.Core.Tests;

public sealed class RazerBatteryTests
{
    private static readonly HidInterface Mouse = new("razer-1", "razer-feature", 0x1532, 0x00BF,
        "DeathAdder V4 Pro", 0xFF00, 1, 0, 0, 91, 0);

    [Fact]
    public async Task RazerFeatureReadsRecordedValueAndUsesOnlyFeatureIdZero()
    {
        var session = new BatteryHidStub(request =>
        {
            switch (request[8])
            {
                case 0x80:
                    var expected = Frame("00001F000000020780", 91);
                    expected[89] = 0x85;
                    Assert.Equal(expected, request);
                    // HaloBattery 记录了状态到载荷的 10 字节。未公布的尾部补零并按协议填 CRC。
                    var recorded = Frame("00021F00000002078000AB", 91);
                    recorded[89] = 0x2E;
                    return [recorded];
                case 0x84:
                    var charging = Frame("00021F0000000207840001", 91);
                    charging[89] = 0x80;
                    return [charging];
                default:
                    throw new InvalidOperationException("意外发包");
            }
        }, feature: true);
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([Mouse], [], [])));
        var transport = new BatteryTransportStub(_ => session);
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(67, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal(DeviceKind.Mouse, reading.Device.Kind);
        Assert.Equal((byte?)0, Assert.Single(transport.Opened).RequiredFeatureReportId);
        Assert.Equal(new byte[] { 0x80, 0x84 }, session.Writes.Select(request => request[8]));
        Assert.True(session.Closed);
    }

    [Fact]
    public async Task AaMouseDoesNotQueryChargingAndIdleStopsTheKeyboardRead()
    {
        var registry = AdapterRegistry.CreateDefault();
        var aa = Mouse with { ProductId = 0x00B8 };
        var keyboard = Mouse with { ProductId = 0x02D5, InterfaceNumber = 2 };
        var mouseSession = new BatteryHidStub(request =>
        {
            Assert.Equal(0x80, request[8]);
            var reply = Frame("00021F00000002078000FF", 91);
            reply[89] = 0x7A;
            return [reply];
        }, feature: true);
        var keyboardSession = new BatteryHidStub(request =>
        {
            Assert.Equal(0x9F, request[2]);
            Assert.Equal(0x80, request[8]);
            var reply = Frame("00049F0000000207800000", 91);
            reply[89] = 0x85;
            return [reply];
        }, feature: true);

        var mouseTarget = Assert.Single(registry.Detect(new([aa], [], [])));
        var keyTarget = Assert.Single(registry.Detect(new([keyboard], [], [])));
        var mouseReading = await registry.ReadAsync(mouseTarget, new BatteryTransportStub(_ => mouseSession), null, CancellationToken.None);
        var keyReading = await registry.ReadAsync(keyTarget, new BatteryTransportStub(_ => keyboardSession), null, CancellationToken.None);

        Assert.Equal(100, mouseReading.Percentage);
        Assert.Equal(ChargingState.Unknown, mouseReading.Charging);
        Assert.Equal(DeviceKind.Keyboard, keyReading.Device.Kind);
        Assert.Equal(LinkState.Offline, keyReading.Link);
        Assert.Null(keyReading.Percentage);
        Assert.Single(mouseSession.Writes);
        Assert.Single(keyboardSession.Writes);
    }

    [Fact]
    public void RazerFeatureRejectsWritesBadRoutesAndCorruptReplies()
    {
        var request = RazerFeatureProtocol.Encode(0x02BA, RazerFeatureQuery.Charging);
        Assert.Equal("00009F000000020784", Convert.ToHexString(request.AsSpan(0, 9)));
        Assert.All(request[9..89], value => Assert.Equal(0, value));
        Assert.Equal(0x81, request[89]);
        foreach (byte command in new byte[] { 0x01, 0x03, 0x05, 0x10 })
            Assert.Throws<ArgumentOutOfRangeException>(() => RazerFeatureProtocol.Encode(0x00BF, (RazerFeatureQuery)command));
        // 纯有线无电池鼠标 00CB 与蓝牙路径 0095 必须继续拒绝。
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerFeatureProtocol.Encode(0x00CB, RazerFeatureQuery.Battery));
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerFeatureProtocol.Encode(0x0095, RazerFeatureQuery.Battery));
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerFeatureProtocol.Encode(0x00B8, RazerFeatureQuery.Charging));

        var model = RazerFeatureProtocol.Find(0x00BF)!;
        var frame = Frame("00021F00000002078000AB", 91);
        frame[89] = 0x2E;
        Assert.True(RazerFeatureProtocol.TryDecode(frame, model, RazerFeatureQuery.Battery, out var status, out var value));
        Assert.Equal(2, status);
        Assert.Equal(0xAB, value);
        Assert.False(RazerFeatureProtocol.TryDecode(frame, model, RazerFeatureQuery.Charging, out _, out _));
        Assert.False(RazerFeatureProtocol.TryDecode(frame.AsSpan(0, 90), model, RazerFeatureQuery.Battery, out _, out _));
        frame[2] = 0x9F;
        Assert.False(RazerFeatureProtocol.TryDecode(frame, model, RazerFeatureQuery.Battery, out _, out _));
        frame[2] = 0x1F;
        frame[89] ^= 1;
        Assert.False(RazerFeatureProtocol.TryDecode(frame, model, RazerFeatureQuery.Battery, out _, out _));
    }

    [Fact]
    public void OnlyEvidencedRazerReceiversAndUniqueCollectionsAreDetected()
    {
        var registry = AdapterRegistry.CreateDefault();
        var entries = new HidInterface[]
        {
            Mouse with { ProductId = 0x0999 },
            Mouse with { ProductId = 0x00CB }, // 纯有线无电池（Basilisk V3 35K）
            Mouse with { ProductId = 0x0095 }, // 蓝牙路径（Orochi V2 BT）
            Mouse with { ProductId = 0x00BF, InterfaceNumber = 1 },
            Mouse with { ProductId = 0x00E6, InterfaceNumber = 0 },
            Mouse with { ProductId = 0x02D2, InterfaceNumber = 3 },
            Mouse with { UsagePage = 0x000C },
            Mouse with { UsagePage = 1, Usage = 1 },
            Mouse with { FeatureLength = 90 },
            Mouse with { ProductId = 0x0565, UsagePage = 0xFF13, InterfaceNumber = 3, InputLength = 64, OutputLength = 64 }
        };
        Assert.Empty(registry.Detect(new(entries, [], [])));
        Assert.Empty(registry.Detect(new([Mouse, Mouse with { Path = "ambiguous" }], [], [])));
        foreach (ushort pid in new ushort[] { 0x052E, 0x0536, 0x053A, 0x0552, 0x0555, 0x0556, 0x0557, 0x0566, 0x0583, 0x0999 })
        {
            Assert.False(AdapterRegistry.IsKnownHidReceiver(0x1532, pid));
            var headset = Mouse with { ProductId = pid, UsagePage = 0xFF14, InterfaceNumber = 3,
                InputLength = 64, OutputLength = 64 };
            Assert.Empty(registry.Detect(new([headset], [], [])));
        }
        // BlackShark V3 / V3 Pro 仅允许 MI_05 的 FF14:0001 64/64，拒绝 MI_03 或 FF13。
        foreach (ushort pid in new ushort[] { 0x057A, 0x0577 })
        {
            Assert.True(AdapterRegistry.IsKnownHidReceiver(0x1532, pid));
            var wrongMi = Mouse with { ProductId = pid, UsagePage = 0xFF14, InterfaceNumber = 3, InputLength = 64, OutputLength = 64, FeatureLength = 0 };
            var wrongPage = Mouse with { ProductId = pid, UsagePage = 0xFF13, InterfaceNumber = 5, InputLength = 62, OutputLength = 62, FeatureLength = 0 };
            Assert.Empty(registry.Detect(new([wrongMi, wrongPage], [], [])));
        }
    }

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(1, 76, true)]
    public async Task HyperSpeedReadsOnlyLinkBatteryAndCharge(int link, int? level, bool charging)
    {
        var receiver = Mouse with { ProductId = 0x0565, UsagePage = 0xFF14, InterfaceNumber = 3,
            InputLength = 64, OutputLength = 64, FeatureLength = 0 };
        var session = new BatteryHidStub(request =>
        {
            var reply = request[10] switch
            {
                0x20 => Frame($"0202{request[2]:X2}00000005008000200101{link:X2}", 64),
                0x21 => Frame($"0202{request[2]:X2}000000050080802101014C", 64),
                0x2A => Frame($"0202{request[2]:X2}000000050080802A010102", 64),
                _ => throw new InvalidOperationException("意外发包")
            };
            foreach (byte value in reply.AsSpan(1, 61))
                reply[62] ^= value;
            var wrongSequence = reply.ToArray();
            wrongSequence[2] ^= 1;
            wrongSequence[62] ^= 1;
            return [wrongSequence, reply];
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([receiver], [], [])));
        var reading = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);

        Assert.Equal(level, reading.Percentage);
        Assert.Equal(charging ? ChargingState.Charging : ChargingState.Unknown, reading.Charging);
        Assert.Equal(link == 0 ? LinkState.Offline : LinkState.Online, reading.Link);
        Assert.Equal(link == 0 ? new byte[] { 0x20 } : [0x20, 0x21, 0x2A], session.Writes.Select(request => request[10]));
        Assert.True(session.Closed);
    }

    [Fact]
    public void CapturedV3FramesKeepTheirChecksumAndDisabledModelsRejectControlCommands()
    {
        // HaloBattery #292 的 057A 抓包，除前缀和值、CRC 外其余字节为零。
        var request = RazerHeadsetProtocol.EncodeMediaTek(0x057A, RazerHeadsetQuery.Battery, 0);
        var recorded = Frame("0200600000000400008021000000", 64);
        recorded[62] = 0xC7;
        Assert.Equal(recorded, request);
        var response = Frame("0202600000000500008021010161", 64);
        response[62] = 0xA5;
        Assert.True(RazerHeadsetProtocol.TryMediaTek(response, request, 0x057A, out byte value));
        Assert.Equal(97, value);
        Assert.False(RazerHeadsetProtocol.TryMediaTek(response, request, 0x0565, out _));
        response[62] ^= 1;
        Assert.False(RazerHeadsetProtocol.TryMediaTek(response, request, 0x057A, out _));

        // OpenRazer #2838 在 0577 上记录的纯查询模板，且 0577 同时兼容字节 0 或字节 1 起算的 XOR 校验。
        Assert.Equal(recorded, RazerHeadsetProtocol.EncodeMediaTek(0x0577, RazerHeadsetQuery.Battery, 0));
        var proReply0 = Frame("0202600000000500008021010158", 64);
        proReply0[62] = RazerHeadsetProtocol.Checksum(proReply0, 0);
        Assert.True(RazerHeadsetProtocol.TryMediaTek(proReply0, request, 0x0577, out byte proVal0));
        Assert.Equal(88, proVal0);
        var proReply1 = Frame("0202600000000500808021010159", 64);
        proReply1[62] = RazerHeadsetProtocol.Checksum(proReply1, 1);
        Assert.True(RazerHeadsetProtocol.TryMediaTek(proReply1, request, 0x0577, out byte proVal1));
        Assert.Equal(89, proVal1);
        Assert.NotEqual(0xC7, RazerHeadsetProtocol.EncodeMediaTek(0x0565, RazerHeadsetQuery.Battery, 1)[62]);

        foreach (byte command in new byte[] { 0x93, 0x95, 0xAC, 0xE6, 0xE1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodeMediaTek(0x0565, (RazerHeadsetQuery)command, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodePa(0x0555, (RazerHeadsetQuery)command));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodeMediaTek(0x0577, RazerHeadsetQuery.Link, 0));
    }

    [Fact]
    public async Task BlackSharkV3ProReadsReadOnlyBatteryAndSkipsFastHeartbeat()
    {
        // 复现实机诊断报告中的 1532:0577 集合布局（MI_05 同时包含 FF14 64/64 与 FF13 62/62）。
        var ff14 = new HidInterface("rx-v3p", "p-ff14", 0x1532, 0x0577, null, 0xFF14, 1, 64, 64, 0, 5);
        var ff13 = new HidInterface("rx-v3p", "p-ff13", 0x1532, 0x0577, null, 0xFF13, 1, 62, 62, 0, 5);
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([ff14, ff13], [], [])));
        Assert.Equal("Razer BlackShark V3 Pro", target.Target.Device.Name);

        // V3 系列跳过高频心跳，不打开句柄也不发包。
        var zeroTransport = new FakeReceiverTransport();
        Assert.Null(await registry.CheckPresenceAsync(target, zeroTransport, CancellationToken.None));
        Assert.Equal(0, zeroTransport.Calls);

        // 完整轮询仅发 21 电量与 2A 充电（不发 20 链路，也不发会话控制指令）。
        var session = new BatteryHidStub(request =>
        {
            var reply = request[10] switch
            {
                0x21 => Frame("0202600000000500008021010150", 64),
                0x2A => Frame("020260000000050000802A010101", 64),
                _ => throw new InvalidOperationException("意外发包")
            };
            reply[62] = RazerHeadsetProtocol.Checksum(reply, 0);
            return [reply];
        });
        var reading = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);
        Assert.Equal(80, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(new byte[] { 0x21, 0x2A }, session.Writes.Select(w => w[10]));
    }

    [Fact]
    public async Task RazerDeathAdderV3ProDetectsStandardMousePageFeatureCollectionWithFeatureOnlyHandle()
    {
        // Windows 实机上雷蛇 90B 鼠标的 91B 特征报告常直接挂在标准鼠标集合（0001:0002，MI_00）上。
        var dav3 = new HidInterface("rx-dav3", "p-dav3", 0x1532, 0x00B7, null, 0x0001, 2, 16, 0, 91, 0);
        var session = new BatteryHidStub(request =>
        {
            var reply = request[8] == 0x80
                ? Frame("00021F00000002078000CC", 91)
                : Frame("00021F0000000207840000", 91);
            reply[89] = RazerFeatureProtocol.Checksum(reply);
            return [reply];
        }, feature: true);
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([dav3], [], [])));
        Assert.Equal("Razer DeathAdder V3 Pro", target.Target.Device.Name);

        var transport = new BatteryTransportStub(_ => session);
        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(80, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        Assert.True(Assert.Single(transport.Opened).FeatureOnly);
        Assert.Equal((byte?)0, Assert.Single(transport.Opened).RequiredFeatureReportId);

        // 诊断报告也应包含此类 0001:0002 91B 雷蛇特征集合并可完整回放。
        string report = await DiagnosticReport.GenerateAsync(new([dav3], [], []), new BatteryTransportStub(_ => session));
        Assert.Contains("|1532|00B7|0001|0002|16|0|91|00|", report);
        Assert.Contains("匹配 Razer 90B H1", report);
    }

    [Fact]
    public async Task AsusRogKeyboardAndMchoseK87ReadOnlyProtocolsWorkAndReplay()
    {
        // 复现实机诊断报告中的 0B05:1934（MI_01 FF00:0001 65/65）与 41E4:2001（FF02:0002 20/20）。
        var asus = new HidInterface("rx-asus", "p-asus", 0x0B05, 0x1934, null, 0xFF00, 1, 65, 65, 0, 1);
        var mchose = new HidInterface("rx-mchose", "p-mchose", 0x41E4, 0x2001, null, 0xFF02, 2, 20, 20, 0, 1);

        var asusReply = new byte[65];
        asusReply[0] = 0x00;
        asusReply[1] = 0x12;
        asusReply[2] = 0x01;
        asusReply[6] = 85;
        asusReply[9] = 1; // 插电充电中

        var mchoseReply = new byte[20];
        mchoseReply[0] = 0x13;
        mchoseReply[1] = 0x4A;
        mchoseReply[2] = 0x01;
        mchoseReply[4] = 0x02;
        mchoseReply[5] = 74;
        mchoseReply[6] = 0x01;
        mchoseReply[19] = MchoseKbProtocol.Checksum(mchoseReply.AsSpan(0, 19));

        var transport = new FakeReceiverTransport(
            (asus.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Convert.ToHexString(AsusHidProtocol.EncodeKeyboardBattery())),
                new(DiagnosticOperation.HidInput, Convert.ToHexString(asusReply))
            ]),
            (mchose.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidOutput, Convert.ToHexString(MchoseKbProtocol.EncodeBattery())),
                new(DiagnosticOperation.HidInput, Convert.ToHexString(mchoseReply))
            ]));

        string report = await DiagnosticReport.GenerateAsync(new([asus, mchose], [], []), transport, CancellationToken.None);
        transport.AssertComplete();
        Assert.Contains("匹配 ASUS ROG 键盘 H1", report);
        Assert.Contains("匹配 MCHOSE 键盘 H2", report);

        var (inv, replay, baud) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault(baud);
        List<BatteryReading> readings = [];
        foreach (var d in registry.Detect(inv))
            readings.AddRange(await registry.ReadDevicesAsync(d, replay, null, CancellationToken.None));
        replay.AssertComplete();

        Assert.Contains(readings, r => r.Device.Name == "ASUS ROG 无线键盘" && r.Percentage == 85 && r.Charging == ChargingState.Charging);
        Assert.Contains(readings, r => r.Device.Name == "MCHOSE K87" && r.Percentage == 74 && r.Charging == ChargingState.Unknown);
    }

    [Fact]
    public void HyperSpeedIgnoresCapturedNonBatteryReply()
    {
        // OpenRazer #2316 的 STATUS_INDICATOR_BATTERY_STATUS.pcapng 帧，来自 LED 设置。
        var report = Frame("02027800000005008000E6010100", 64);
        report[62] = 0x19;
        var request = RazerHeadsetProtocol.EncodeMediaTek(0x0565, RazerHeadsetQuery.Link, 0x18);
        Assert.False(RazerHeadsetProtocol.TryMediaTek(report, request, 0x0565, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodeMediaTek(0x0565, (RazerHeadsetQuery)0xE6, 0x18));
    }

    [Theory]
    [InlineData(0x0555, "0280080000504108000321", "02000000005049080000000021010164", 100)]
    [InlineData(0x053A, "018008504108080321", "01800E504908E881420D00040021010122", 34)]
    public void PaOfflineCodecChecksQueryAndReplyFields(int pid, string request, string response, int expected)
    {
        // 053A 为 HaloBattery #10 的实机 34% 回复，0555 按公开帧结构构造。
        var query = RazerHeadsetQuery.Battery;
        Assert.Equal(Frame(request, 64), RazerHeadsetProtocol.EncodePa((ushort)pid, query));
        var frame = Frame(response, 64);
        Assert.True(RazerHeadsetProtocol.TryPa(frame, (ushort)pid, query, out var value));
        Assert.Equal(expected, value);
        Assert.False(RazerHeadsetProtocol.TryPa(frame, (ushort)pid, RazerHeadsetQuery.Charging, out _));
    }

    [Fact]
    public void Razer90BCatalogIsUniqueConsistentAndExcludesWiredOnlyAndBluetoothPids()
    {
        var models = RazerFeatureProtocol.AllModels;
        Assert.Equal(87, models.Count);
        Assert.Equal(models.Count, models.Select(m => m.Pid).Distinct().Count());
        Assert.Equal(72, models.Count(m => m.Kind == DeviceKind.Mouse));
        Assert.Equal(15, models.Count(m => m.Kind == DeviceKind.Keyboard));

        byte[] knownTids = [0x1F, 0x3F, 0x9F, 0xFF];
        foreach (var model in models)
        {
            Assert.Contains(model.TransactionId, knownTids);
            Assert.True(AdapterRegistry.IsKnownHidReceiver(0x1532, model.Pid));
            var catalog = DeviceCatalog.FindEntry(0x1532, model.Pid);
            Assert.NotNull(catalog);
            Assert.Equal($"Razer {model.Name}", catalog!.Model);
            Assert.Equal(model.Kind, catalog.Kind);
            Assert.Equal(VerificationTier.DocsOnly, catalog.Verification);
        }

        Assert.Null(RazerFeatureProtocol.Find(0x00CB)); // Basilisk V3 35K（纯有线）
        Assert.Null(RazerFeatureProtocol.Find(0x0095)); // Orochi V2（蓝牙）
        Assert.Equal(400, RazerFeatureProtocol.Find(0x0062)!.SettleMs);
        Assert.Equal(400, RazerFeatureProtocol.Find(0x0094)!.SettleMs);
        Assert.Equal(400, RazerFeatureProtocol.Find(0x00E0)!.SettleMs);
    }

    [Theory]
    [InlineData(0x00BE, 0, 0x1F, (int)DeviceKind.Mouse, (int)DeviceConnection.UsbCable, "雷蛇 USB 直连")] // DeathAdder V4 Pro 有线
    [InlineData(0x00B3, 0, 0x1F, (int)DeviceKind.Mouse, (int)DeviceConnection.Receiver, "雷蛇无线接收器")] // HyperPolling Wireless Dongle
    [InlineData(0x007B, 0, 0xFF, (int)DeviceKind.Mouse, (int)DeviceConnection.Receiver, "雷蛇无线接收器")] // Viper Ultimate 无线（TID FF）
    [InlineData(0x007D, 0, 0x3F, (int)DeviceKind.Mouse, (int)DeviceConnection.Receiver, "雷蛇无线接收器")] // DeathAdder V2 Pro 无线（TID 3F）
    [InlineData(0x00E2, 3, 0x1F, (int)DeviceKind.Mouse, (int)DeviceConnection.UsbCable, "雷蛇 USB 直连")] // Basilisk V4 Pro 有线（MI 3）
    [InlineData(0x025A, 2, 0x3F, (int)DeviceKind.Keyboard, (int)DeviceConnection.UsbCable, "雷蛇 USB 直连")] // BlackWidow V3 Pro 有线（MI 2, TID 3F）
    [InlineData(0x025C, 2, 0x9F, (int)DeviceKind.Keyboard, (int)DeviceConnection.Receiver, "雷蛇无线接收器")] // BlackWidow V3 Pro 无线（MI 2, TID 9F）
    [InlineData(0x0290, 2, 0x9F, (int)DeviceKind.Keyboard, (int)DeviceConnection.Receiver, "雷蛇无线接收器")] // DeathStalker V2 Pro 无线（MI 2, TID 9F）
    [InlineData(0x0292, 3, 0x1F, (int)DeviceKind.Keyboard, (int)DeviceConnection.UsbCable, "雷蛇 USB 直连")] // DeathStalker V2 Pro 有线（MI 3, TID 1F）
    [InlineData(0x02CD, 3, 0x1F, (int)DeviceKind.Keyboard, (int)DeviceConnection.UsbCable, "雷蛇 USB 直连")] // Razer Joro（MI 3, TID 1F）
    public async Task Razer90BReadsExpandedMiceKeyboardsWiredAndLegacyTids(
        int pid, int iface, int tid, int kind, int connection, string sourceLabel)
    {
        var hid = Mouse with { ProductId = (ushort)pid, InterfaceNumber = (byte)iface };
        var session = new BatteryHidStub(request =>
        {
            Assert.Equal((byte)tid, request[2]);
            var reply = request[8] == 0x80
                ? Frame($"0002{tid:X2}00000002078000CC", 91) // 204/255 = 80%
                : Frame($"0002{tid:X2}0000000207840001", 91);
            reply[89] = RazerFeatureProtocol.Checksum(reply);
            return [reply];
        }, feature: true);

        var adapter = new RazerFeatureAdapter(TimeSpan.Zero, TimeSpan.Zero);
        var registry = new AdapterRegistry(adapter);
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        Assert.Equal((DeviceKind)kind, target.Target.Device.Kind);
        Assert.Equal((DeviceConnection)connection, target.Target.Device.Connection);
        Assert.Equal(sourceLabel, target.Target.Device.SourceLabel);

        var reading = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);
        Assert.Equal(80, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal(LinkState.Online, reading.Link);
    }

    [Fact]
    public async Task Razer90BRetriesForeignSynapseRepliesAndReportsBusyWhenPersistent()
    {
        var hid = Mouse with { ProductId = 0x007D }; // DeathAdder V2 Pro（TID 3F）
        var foreignLed = Frame("00023F000000020F030001", 91);
        foreignLed[89] = RazerFeatureProtocol.Checksum(foreignLed);

        // 1) 前 2 次读到 Synapse 灯效回复（0F:03），第 3 次成功拿到电量回复。
        int attempts = 0;
        var recoverSession = new BatteryHidStub(request =>
        {
            if (request[8] == 0x80)
            {
                attempts++;
                if (attempts <= 2)
                    return [foreignLed];
                var ok = Frame("00023F0000000207800099", 91); // 153/255 = 60%
                ok[89] = RazerFeatureProtocol.Checksum(ok);
                return [ok];
            }
            var chg = Frame("00023F0000000207840000", 91);
            chg[89] = RazerFeatureProtocol.Checksum(chg);
            return [chg];
        }, feature: true);

        var registry = new AdapterRegistry(new RazerFeatureAdapter(TimeSpan.Zero, TimeSpan.Zero));
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var recovered = await registry.ReadAsync(target, new BatteryTransportStub(_ => recoverSession), null, CancellationToken.None);
        Assert.Equal(3, attempts);
        Assert.Equal(60, recovered.Percentage);
        Assert.Equal(ChargingState.NotCharging, recovered.Charging);

        // 2) 持续只读到 Synapse 0F:03 回复 → 重试耗尽后按 Busy 报告（区别于状态 01 协议忙报 Timeout）。
        int persistentAttempts = 0;
        var busySession = new BatteryHidStub(_ =>
        {
            persistentAttempts++;
            return [foreignLed];
        }, feature: true);
        var busyReading = await registry.ReadAsync(target, new BatteryTransportStub(_ => busySession), null, CancellationToken.None);
        Assert.Equal(1 + RazerFeatureAdapter.MaxBusyRetries, persistentAttempts);
        Assert.Equal(ReadIssue.Busy, busyReading.Issue);
        Assert.Equal(LinkState.Busy, busyReading.Link);
    }

    [Theory]
    [InlineData("FF0F05FE12041F08050305010EC050", 80, (int)ChargingState.NotCharging, (int)LinkState.Online, true)] // 实机：放电 80%
    [InlineData("FF0F05FE12041F08050305020E081E", 30, (int)ChargingState.NotCharging, (int)LinkState.Online, true)] // 实机：低电 30%
    [InlineData("FF0F05FE12041F08050305030DA00A", 10, (int)ChargingState.NotCharging, (int)LinkState.Online, true)] // 实机：极低 10%
    [InlineData("FF0F05FE12041F0805050309108050", null, (int)ChargingState.Charging, (int)LinkState.Online, false)] // 实机：充电中隐藏跳数
    [InlineData("FF0F05FE12041F0805060506108864", 100, (int)ChargingState.Full, (int)LinkState.Online, true)] // 实机：已充满 100%
    [InlineData("FF0F05FE12041F0805030501000050", null, (int)ChargingState.Unknown, (int)LinkState.Offline, false)] // 电压为 0 → 离线
    public async Task BlackSharkV2Pro2020ReadsCapturedFeatureFrames(
        string responseHex, int? expectedPercent, int expectedCharging, int expectedLink, bool expectedEstimated)
    {
        var receiver = new HidInterface("bs2020", "bs2020-path", 0x1532, 0x0528,
            "Razer BlackShark V2 Pro", 0xFF00, 1, 0, 0, 64, 3);
        Assert.True(AdapterRegistry.IsKnownHidReceiver(0x1532, 0x0528));

        var session = new BatteryHidStub(request =>
        {
            Assert.Equal(Frame("FF0A00FD0412F10205", 64), request);
            return [Frame(responseHex, 64)];
        }, feature: true, expectedFeatureReportId: 0xFF);

        var registry = new AdapterRegistry(new RazerHeadsetAdapter(TimeSpan.Zero));
        var target = Assert.Single(registry.Detect(new([receiver], [], [])));
        Assert.Equal("Razer BlackShark V2 Pro (2020)", target.Target.Device.Name);
        Assert.Equal(DeviceKind.Headset, target.Target.Device.Kind);

        var transport = new BatteryTransportStub(_ => session);
        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal((byte?)0xFF, Assert.Single(transport.Opened).RequiredFeatureReportId);
        Assert.Equal(expectedPercent, reading.Percentage);
        Assert.Equal(expectedEstimated, reading.IsEstimated);
        Assert.Equal((ChargingState)expectedCharging, reading.Charging);
        Assert.Equal((LinkState)expectedLink, reading.Link);
        Assert.True(session.Closed);
    }

    [Fact]
    public async Task BlackSharkV2Pro2020RejectsStaleOfflineHeaderAndReplaysDiagnostics()
    {
        var receiver = new HidInterface("bs2020", "bs2020-path", 0x1532, 0x0528,
            "Razer BlackShark V2 Pro", 0xFF00, 1, 0, 0, 64, 3);
        // 实机关机过期头 FF 01 00 FE 12 04 1F 08 05 携带旧电量 0x50，绝不可复用。
        var stale = Frame("FF0100FE12041F0805050309108850", 64);
        var staleSession = new BatteryHidStub(_ => [stale, stale, stale], feature: true, expectedFeatureReportId: 0xFF);
        var registry = new AdapterRegistry(new RazerHeadsetAdapter(TimeSpan.Zero));
        var target = Assert.Single(registry.Detect(new([receiver], [], [])));
        var offline = await registry.ReadAsync(target, new BatteryTransportStub(_ => staleSession), null, CancellationToken.None);
        Assert.Equal(LinkState.Offline, offline.Link);
        Assert.Null(offline.Percentage);

        // 诊断导出与脱敏回放校验。
        var okFrame = Frame("FF0F05FE12041F08050305010EC050", 64);
        var report = await DiagnosticReport.GenerateAsync(new([receiver], [], []),
            new BatteryTransportStub(_ => new BatteryHidStub(_ => [okFrame], feature: true, expectedFeatureReportId: 0xFF)));
        var (replayInventory, replayTransport, _) = FakeReceiverTransport.FromReport(report);
        var replayTarget = Assert.Single(AdapterRegistry.CreateDefault().Detect(replayInventory));
        var replayReading = await AdapterRegistry.CreateDefault().ReadAsync(replayTarget, replayTransport, null, CancellationToken.None);
        Assert.Equal(80, replayReading.Percentage);
        Assert.Equal(ChargingState.NotCharging, replayReading.Charging);
        replayTransport.AssertComplete();
    }
}

using Jige.Core.Adapters;
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
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerFeatureProtocol.Encode(0x00B3, RazerFeatureQuery.Battery));
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
            Mouse with { ProductId = 0x00BE }, // 有线
            Mouse with { ProductId = 0x00BF, InterfaceNumber = 1 },
            Mouse with { ProductId = 0x00E6, InterfaceNumber = 0 },
            Mouse with { ProductId = 0x02D2, InterfaceNumber = 3 },
            Mouse with { UsagePage = 1 },
            Mouse with { FeatureLength = 90 },
            Mouse with { ProductId = 0x0565, UsagePage = 0xFF13, InterfaceNumber = 3, InputLength = 64, OutputLength = 64 }
        };
        Assert.Empty(registry.Detect(new(entries, [], [])));
        Assert.Empty(registry.Detect(new([Mouse, Mouse with { Path = "ambiguous" }], [], [])));
        foreach (ushort pid in new ushort[] { 0x053A, 0x0555, 0x0556, 0x0566, 0x057A, 0x0577, 0x0999 })
        {
            Assert.False(AdapterRegistry.ShouldInspectHid(0x1532, pid));
            var headset = Mouse with { ProductId = pid, UsagePage = 0xFF14, InterfaceNumber = 3,
                InputLength = 64, OutputLength = 64 };
            Assert.Empty(registry.Detect(new([headset], [], [])));
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

        // OpenRazer #2838 在 0577 上记录的纯查询模板。
        Assert.Equal(recorded, RazerHeadsetProtocol.EncodeMediaTek(0x0577, RazerHeadsetQuery.Battery, 0));
        Assert.NotEqual(0xC7, RazerHeadsetProtocol.EncodeMediaTek(0x0565, RazerHeadsetQuery.Battery, 1)[62]);

        foreach (byte command in new byte[] { 0x93, 0x95, 0xAC, 0xE6, 0xE1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodeMediaTek(0x0565, (RazerHeadsetQuery)command, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodePa(0x0555, (RazerHeadsetQuery)command));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerHeadsetProtocol.EncodeMediaTek(0x0577, RazerHeadsetQuery.Link, 0));
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
}

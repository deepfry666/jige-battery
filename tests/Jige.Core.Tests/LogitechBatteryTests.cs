using Jige.Core.Adapters;

namespace Jige.Core.Tests;

public sealed class LogitechBatteryTests
{
    private static readonly HidInterface Receiver = new("logi-1", "logi-long", 0x046D, 0xC54D,
        "USB Receiver", 0xFF00, 2, 20, 20, 0, 2);

    [Fact]
    public async Task DefaultRegistrySeparatesSlotsAndRereadsFeatureIndicesAfterReopen()
    {
        // Solaar #2961 的 PRO X 2 硬件报告，动态索引 06、94%、未充电。
        var capturedRequest = Frame("11010617", 20);
        var capturedReply = Frame("110106175E080000", 20);
        Assert.True(LogitechHidppProtocol.Matches(capturedReply, capturedRequest));
        Assert.True(LogitechHidppProtocol.Matches(Frame("11010006060005", 20), Frame("110100061004", 20)));
        var firstSession = new BatteryHidStub(request => Reply(request, mouseIndex: 6));
        var secondSession = new BatteryHidStub(request => Reply(request, mouseIndex: 12));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([Receiver], [], [])));
        var transport = new BatteryTransportStub(_ => firstSession);

        var first = await registry.ReadDevicesAsync(target, transport, null, CancellationToken.None);
        var second = await registry.ReadDevicesAsync(target, new BatteryTransportStub(_ => secondSession), null, CancellationToken.None);

        Assert.Collection(first,
            mouse =>
            {
                Assert.Equal("PRO X 2", mouse.Device.Name);
                Assert.Equal(DeviceKind.Mouse, mouse.Device.Kind);
                Assert.Equal(94, mouse.Percentage);
                Assert.Equal(ChargingState.NotCharging, mouse.Charging);
            },
            keyboard =>
            {
                Assert.Equal(DeviceKind.Keyboard, keyboard.Device.Kind);
                Assert.Equal(45, keyboard.Percentage);
                Assert.Equal(ChargingState.Charging, keyboard.Charging);
            },
            offline =>
            {
                Assert.Equal(LinkState.Offline, offline.Link);
                Assert.Equal(ReadIssue.None, offline.Issue);
                Assert.Null(offline.Percentage);
            });
        Assert.Equal(3, first.Select(item => item.Device.Id).Distinct().Count());
        Assert.Equal(first.Select(item => item.Device.Id), second.Select(item => item.Device.Id));
        Assert.Equal(94, second[0].Percentage);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, firstSession.Writes.Where(IsPing).Select(request => request[1]));
        Assert.Single(firstSession.Writes, request => request.AsSpan(1, 5).SequenceEqual(new byte[] { 1, 0, 0x0B, 0, 5 }));
        Assert.Single(firstSession.Writes, request => request.AsSpan(1, 5).SequenceEqual(new byte[] { 1, 0, 0x0A, 0x10, 4 }));
        Assert.DoesNotContain(secondSession.Writes, request => request[1] == 1 && request[2] == 6);
        Assert.Contains(secondSession.Writes, request => request[1] == 1 && request[2] == 12);
        Assert.True(firstSession.Closed && secondSession.Closed);
        Assert.Equal((byte?)0x11, Assert.Single(transport.Opened).RequiredOutputReportId);

        byte[][] Reply(byte[] request, byte mouseIndex)
        {
            var reply = (request[1], request[2], request[3]) switch
            {
                (1 or 2, 0, 0x1A) when IsPing(request) => Frame($"11{request[1]:X2}001A0402{request[6]:X2}", 20),
                (3, 0, 0x1A) => Frame("10038F001A09", 7),
                (_, 0, 0x1A) => Frame($"10{request[1]:X2}8F001A08", 7),
                (1, 0, 0x0B) => Frame("1101000B05", 20),
                (1, 5, 0x0A) => Frame("1101050A07", 20),
                (1, 5, 0x1A) => Frame("1101051A50524F20582032", 20),
                (1, 5, 0x2A) => Frame("1101052A03", 20),
                (1, 0, 0x0A) => Frame($"1101000A{mouseIndex:X2}0005", 20),
                (1, var index, 0x0A) when index == mouseIndex => Frame($"1101{index:X2}0A0F03", 20),
                (1, var index, 0x1A) when index == mouseIndex => Frame($"1101{index:X2}1A5E080000", 20),
                (2, 0, 0x0B) => Frame("1102000B07", 20),
                (2, 7, 0x0A) => Frame("1102070A04", 20),
                (2, 7, 0x1A) => Frame("1102071A47353135", 20),
                (2, 7, 0x2A) => Frame("1102072A00", 20),
                (2, 0, 0x0A) => Frame("1102000A06", 20),
                (2, 6, 0x0A) => Frame("1102060A0F01", 20), // 仅档位，无 SoC。
                (2, 0, 0x0C) => Frame("1102000C08", 20),
                (2, 8, 0x0A) => Frame("1102080A2D1401", 20),
                _ => throw new InvalidOperationException($"意外发包 {Convert.ToHexString(request)}")
            };
            // 第二槽 1000 查表前收到迟到的 1004 Root 回复，必须按 software ID 丢弃。
            return request[1] == 2 && request[2] == 0 && request[3] == 0x0C
                ? [Frame("1102000A06", 20), reply] : [reply];
        }
    }

    [Theory]
    [InlineData(0xC52B, "10FF83B5200000", "11FF83B520000040A9000002")]
    [InlineData(0xC548, "10FF83B5510000", "11FF83B55102A94012345678")]
    public async Task PairingReadDistinguishesOfflineFromAnEmptySlot(int pid, string expected, string reply)
    {
        var longHid = Receiver with { ProductId = (ushort)pid };
        var shortHid = longHid with { Path = "logi-short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var session = new BatteryHidStub(request => request[1] == 1 ? [] : [Frame($"10{request[1]:X2}8F001A08", 7)]);
        var pairing = new BatteryHidStub(request =>
        {
            Assert.Equal(expected, Convert.ToHexString(request));
            session.Enqueue(Frame(reply, 20));
            return [];
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([longHid, shortHid], [], [])));
        var readings = await registry.ReadDevicesAsync(target, new BatteryTransportStub(hid =>
            hid.Path == shortHid.Path ? pairing : session), null, CancellationToken.None);
        var offline = Assert.Single(readings);
        Assert.Equal(LinkState.Offline, offline.Link);
        Assert.Equal(DeviceKind.Mouse, offline.Device.Kind);
        Assert.Equal(ReadIssue.None, offline.Issue);
        Assert.True(session.Closed && pairing.Closed);
    }

    [Fact]
    public async Task UnknownLogitechStopsAtRootAndNeverReadsDynamicIndices()
    {
        var receiver = Receiver with { ProductId = 0xC5FF };
        var session = new BatteryHidStub(request => IsPing(request)
            ? [Frame($"1101001A0201{request[6]:X2}", 20)] : [Frame("1101000A06", 20)]);
        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new([receiver with { Product = "Logitech Wired Mouse" }], [], [])));
        var target = Assert.Single(registry.Detect(new([receiver], [], [])));

        var reading = Assert.Single(await registry.ReadDevicesAsync(target, new BatteryTransportStub(_ => session),
            null, CancellationToken.None));

        Assert.Equal(2, session.Writes.Count);
        Assert.True(IsPing(session.Writes[0]));
        Assert.Equal(Frame("1101000A1004", 20), session.Writes[1]);
        Assert.Null(reading.Percentage);
    }

    [Fact]
    public async Task VoltageFallbackMarksEstimateAndPreservesUnknownCharge()
    {
        var session = new BatteryHidStub(request => (request[1], request[2], request[3]) switch
        {
            (1, 0, 0x1A) => [Frame($"1101001A0201{request[6]:X2}", 20)],
            (_, 0, 0x1A) => [Frame($"10{request[1]:X2}8F001A08", 7)],
            (1, 0, 0x0A) => [Frame("1101000A00", 20)],
            (1, 0, 0x0B) => [Frame("1101000B00", 20)],
            (1, 0, 0x0C) => [Frame("1101000C03", 20)],
            (1, 3, 0x0A) => [Frame("1101030A000000", 20)],
            (1, 0, 0x0D) => [Frame("1101000D09", 20)],
            (1, 9, 0x0A) => [Frame("1101090A0F0A87", 20)], // 3850 mV。
            _ => throw new InvalidOperationException("意外发包")
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([Receiver], [], [])));
        var reading = Assert.Single(await registry.ReadDevicesAsync(target, new BatteryTransportStub(_ => session),
            null, CancellationToken.None));

        Assert.Equal(50, reading.Percentage);
        Assert.True(reading.IsEstimated);
        Assert.Equal(ChargingState.Unknown, reading.Charging);
    }

    [Fact]
    public async Task ReceiverRemovalPreservesAnAlreadyReadSlot()
    {
        var session = new BatteryHidStub(request => (request[1], request[2], request[3]) switch
        {
            (1, 0, 0x1A) => [Frame($"1101001A0201{request[6]:X2}", 20)],
            (1, 0, 0x0B) => [Frame("1101000B00", 20)],
            (1, 0, 0x0A) => [Frame("1101000A04", 20)],
            (1, 4, 0x0A) => [Frame("1101040A0F03", 20)],
            (1, 4, 0x1A) => [Frame("1101041A00010000", 20)],
            (2, 0, 0x1A) => throw new TransportException(ReadIssue.Disconnected, "测试拔出接收器"),
            _ => throw new InvalidOperationException("意外发包")
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([Receiver], [], [])));
        var results = await registry.ReadDevicesAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);

        Assert.Collection(results, slot => Assert.Equal(0, slot.Percentage), failure => Assert.Equal(ReadIssue.Disconnected, failure.Issue));
        Assert.True(session.Closed);
    }

    [Fact]
    public void HidppRejectsWritesAndUnrelatedReplies()
    {
        var ping = LogitechHidppProtocol.Ping(2, 0x39);
        Assert.Equal(Frame("1102001A000039", 20), ping);
        Assert.False(LogitechHidppProtocol.Matches(Frame("1102001A02013A", 20), ping));
        Assert.False(LogitechHidppProtocol.Matches(Frame("1101001A020139", 20), ping));
        Assert.False(LogitechHidppProtocol.Matches(Frame("11020010020139", 20), ping));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.Ping(0xFF, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.GetFeature(1, (HidppFeature)0x00C2));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.ReadFeature(1, HidppFeature.Unified, 3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.ReadFeature(1, HidppFeature.Battery, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.ReadFeature(1, HidppFeature.Unified, 0x80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.ReadFeature(1, HidppFeature.Unified, 0x82, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHidppProtocol.ReadPairing(1, 0xC54D));
    }

    [Theory]
    [InlineData("50230B000310000600070A5A5A00", 90, false)]
    [InlineData("50230B000310000600070A595A02", 90, true)]
    [InlineData("50230B000310000600070A646402", 100, true)]
    public async Task G325ReadsCapturedBatteryAfterItsAck(string response, int expected, bool charging)
    {
        // HeadsetControl #581 实机帧，尾部按 64 字节的记录补零。
        var endpoint = Receiver with { ProductId = 0x0B21, UsagePage = 0xFFA0, Usage = 1,
            InterfaceNumber = 3, InputLength = 64, OutputLength = 64 };
        var session = new BatteryHidStub(request =>
        {
            Assert.Equal(Frame("50230B00031A000300070A", 64), request);
            return [Frame("50230300031A", 64), Frame(response, 64)];
        });

        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([endpoint], [], [])));
        var result = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);
        Assert.Equal(expected, result.Percentage);
        Assert.Equal(charging ? ChargingState.Charging : ChargingState.NotCharging, result.Charging);
        Assert.Equal(DeviceKind.Headset, result.Device.Kind);
        Assert.True(session.Closed);
    }

    [Fact]
    public async Task CenturionUsesDiscoveredIndicesAndIgnoresAckAndForeignEvents()
    {
        var hid = Receiver with { ProductId = 0x0AF7, UsagePage = 0xFFA0, Usage = 1, InterfaceNumber = 3,
            InputLength = 64, OutputLength = 64 };

        // HaloBattery #103 的 0104 实机载荷为 4B 4B 00。桥事务头按清单构造。
        var session = new BatteryHidStub(request => Convert.ToHexString(request.AsSpan(0, 7)) switch
        {
            "51050000010003" => [Frame("5106000001030000", 64)],
            "510A0003110005" => [Frame("5103000311", 64), Frame("510C000310000700000104000000", 64)],
            "51080003110003" =>
            [
                Frame("51050003000000", 64),
                Frame("510B00031000060004024B4B00", 64),
                Frame("510B00031000060004014B4B00", 64)
            ],
            _ => throw new InvalidOperationException("意外发包")
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var result = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);
        Assert.Equal(75, result.Percentage);
        Assert.Equal(ChargingState.NotCharging, result.Charging);
        Assert.Equal(Frame("51080003110003000401", 64), session.Writes[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHeadsetProtocol.Encode(0x0B18, LogitechHeadsetQuery.LegacyBattery));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHeadsetProtocol.Encode(0x0AF7, (LogitechHeadsetQuery)0x20, 3, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHeadsetProtocol.Encode(0x0B0B, LogitechHeadsetQuery.Battery));
    }

    [Theory]
    [InlineData(0x0AF7, true, true)]
    [InlineData(0x0AF7, false, false)]
    [InlineData(0x0B18, true, false)]
    [InlineData(0x0B45, true, false)]
    public async Task LegacyFixedRequestRequiresTheExactPidAndExplicitMissingFeature(int pid, bool reply, bool legacy)
    {
        var hid = Receiver with { ProductId = (ushort)pid, UsagePage = 0xFFA0, Usage = 1, InterfaceNumber = 3,
            InputLength = 64, OutputLength = 64 };
        string prefix = pid == 0x0AF7 ? "51" : "5023";
        var session = new BatteryHidStub(request =>
        {
            var hex = Convert.ToHexString(request);
            if (hex.StartsWith(prefix + "050000010003", StringComparison.Ordinal))
                return [Frame(prefix + "06000001030000", 64)];
            if (hex.StartsWith(prefix + "0A00031100050000010104", StringComparison.Ordinal))
                return reply ? [Frame(prefix + "0C000310000700000100000000", 64)] : [];
            Assert.Equal(Frame("510800031A000300040A", 64), request);
            return [Frame("510B000310000600040A626202", 64)];
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var result = await registry.ReadAsync(target, new BatteryTransportStub(_ => session), null, CancellationToken.None);

        Assert.Equal(legacy ? 3 : 2, session.Writes.Count);
        Assert.Equal(legacy ? 98 : (int?)null, result.Percentage);
        if (legacy)
            Assert.Equal(ChargingState.Charging, result.Charging);
    }

    [Fact]
    public void AstroUsesOnlyTheGen5Get()
    {
        // HeadsetControl 的实机格式，数值字段用合成样本。05 及更短的长度不包含充电字节。
        Assert.Equal(Frame("020C0300060C", 64), LogitechHeadsetProtocol.Encode(0x0B1C, LogitechHeadsetQuery.Battery));
        var response = Frame("020C06000600515101", 64);
        Assert.True(LogitechHeadsetProtocol.TryFixedBattery(response, 0x0B1C, out int level, out var charging));
        Assert.Equal(81, level);
        Assert.Equal(ChargingState.Charging, charging);
        response[2] = 5;
        Assert.False(LogitechHeadsetProtocol.TryFixedBattery(response, 0x0B1C, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHeadsetProtocol.Encode(0x0B1C, (LogitechHeadsetQuery)0x0F));
    }

    private static bool IsPing(byte[] request) => request[2] == 0 && request[3] == 0x1A;

    internal static byte[] Frame(string hex, int length)
    {
        byte[] frame = Convert.FromHexString(hex);
        Array.Resize(ref frame, length);
        return frame;
    }
}

internal sealed class BatteryTransportStub(Func<HidInterface, IHidSession> open) : IReceiverTransport
{
    public List<HidInterface> Opened { get; } = [];

    public IHidSession OpenHid(HidInterface endpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Opened.Add(endpoint);
        return open(endpoint);
    }

    public ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken token) =>
        throw new InvalidOperationException("意外打开串口");
}

internal sealed class BatteryHidStub(Func<byte[], byte[][]> respond, bool feature = false) : IHidSession
{
    private readonly Queue<byte[]> queue = new();
    public List<byte[]> Writes { get; } = [];
    public bool Closed { get; private set; }

    public void Enqueue(byte[] frame) => queue.Enqueue(frame);

    public ValueTask WriteOutputAsync(byte[] report, CancellationToken token) =>
        feature ? throw new InvalidOperationException("意外发送 Output") : Write(report, token);

    public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) =>
        feature ? Write(report, token) : throw new InvalidOperationException("意外发送 Feature");

    private ValueTask Write(byte[] report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Writes.Add(report.ToArray());
        foreach (var frame in respond(report))
            queue.Enqueue(frame);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token)
    {
        if (feature)
            throw new InvalidOperationException("意外读取 Input");
        if (!queue.TryDequeue(out var frame))
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
        frame.CopyTo(report, 0);
        return frame.Length;
    }

    public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!feature)
            throw new InvalidOperationException("意外读取 Feature");
        Assert.Equal(0, report[0]);
        queue.Dequeue().CopyTo(report, 0);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => Closed = true;
}

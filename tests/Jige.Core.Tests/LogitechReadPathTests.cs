using Jige.Core.Adapters;
using static Jige.Core.Tests.LogitechBatteryTests;

namespace Jige.Core.Tests;

public sealed class LogitechReadPathTests
{
    private static readonly HidInterface Receiver = new("bolt", "long", 0x046D, 0xC548, "Bolt",
        0xFF00, 2, 20, 20, 0, 2);

    [Fact]
    public void InputOnlyShortCollectionCannotDisplaceTheStandardPairOrIdentifyAnUnknownCableDevice()
    {
        var shortHid = Receiver with { Path = "short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var inputOnly = shortHid with { Path = "optional", OutputLength = 0 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([Receiver, inputOnly, shortHid], [], [])));
        Assert.Equal(shortHid, device.Target.ShortHid);

        var cable = Receiver with { ProductId = 0xC09D, Product = "Logitech mouse" };
        Assert.Empty(registry.Detect(new([cable, inputOnly with { ProductId = cable.ProductId }], [], [])));
    }

    [Theory]
    [InlineData(7, ReadIssue.IoError)]
    [InlineData(7, ReadIssue.AccessDenied)]
    [InlineData(0, ReadIssue.None)]
    public async Task LongQueriesCanReceiveShortRepliesWithoutShortOutput(int outputLength, ReadIssue writeOpenIssue)
    {
        var shortHid = Receiver with { Path = "short", Usage = 1, InputLength = 7, OutputLength = (ushort)outputLength };
        var input = new BatteryHidStub(_ => throw new InvalidOperationException("只读短集合不允许发包"));
        var output = new BatteryHidStub(request =>
        {
            Assert.Equal(0x11, request[0]);
            if (request[1] > 2)
                return [Frame($"10{request[1]:X2}8F001A08", 7)];
            if (LogitechHidppProtocol.IsPing(request))
            {
                // 槽 1 的 2.0 设备通过短输入回复长 Ping；槽 2 为要求短报告发包的旧设备。
                input.Enqueue(request[1] == 1 ? Frame($"1001001A0400{request[6]:X2}", 7) : Frame("10028F001A01", 7));
                return [];
            }
            return [request[2] switch
            {
                0 when request[3] == 0x0A => Frame("1101000A06", 20),
                0 => Frame($"110100{request[3]:X2}00", 20),
                6 when request[3] == 0x0A => Frame("1101060A0003", 20),
                6 when request[3] == 0x1A => Frame("1101061A460800", 20),
                _ => throw new InvalidOperationException("意外的 HID++ 请求")
            }];
        });
        var transport = new BatteryTransportStub(hid =>
        {
            if (hid.Path == Receiver.Path)
                return output;
            if (!hid.InputOnly)
            {
                Assert.Equal(7, outputLength);
                throw new TransportException(writeOpenIssue, "短输出预检未通过");
            }
            Assert.Equal((byte)0x10, hid.RequiredInputReportId);
            Assert.Null(hid.RequiredOutputReportId);
            return input;
        });
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([Receiver, shortHid], [], [])));

        var readings = await registry.ReadDevicesAsync(detected, transport, null, CancellationToken.None);

        Assert.Contains(readings, r => r.Percentage == 70 && r.Link == LinkState.Online);
        Assert.Empty(input.Writes);
        Assert.True(input.Closed);
    }

    [Fact]
    public async Task BusyOptionalPushInterfaceKeepsLongInputListeningUntilCancellation()
    {
        var shortHid = Receiver with { Path = "short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([Receiver, shortHid], [], [])));
        var input = new BatteryHidStub(_ => throw new InvalidOperationException());
        var transport = new BatteryTransportStub(hid => hid.Path == Receiver.Path
            ? input : throw new TransportException(ReadIssue.Busy, "短集合忙"));
        using var cancellation = new CancellationTokenSource();
        var listening = registry.ListenPushAsync(detected, transport, _ => Assert.Fail(), cancellation.Token);
        Assert.Equal(2, transport.Opened.Count);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);
        Assert.True(input.Closed);
    }

    [Theory]
    [InlineData(0x0AF7, "51")]
    [InlineData(0x0B18, "5023")]
    public async Task CenturionIgnoresBridgeRequestEchoBeforeValidBridgeReply(int pid, string prefix)
    {
        var endpoint = new HidInterface("headset", "control", 0x046D, (ushort)pid, null, 0xFFA0, 1, 64, 64, 0, 3);
        var session = new BatteryHidStub(request => request[prefix.Length / 2] switch
        {
            5 => [request, Frame(prefix + "06000001030000", 64)],
            10 => [Frame(prefix + "0C000310000700000104000000", 64)],
            8 => [Frame(prefix + "0B00031000060004014B4B00", 64)],
            _ => throw new InvalidOperationException()
        });
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([endpoint], [], [])));
        var transport = new BatteryTransportStub(_ => session);

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(75, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        Assert.Equal(3, session.Writes.Count);
    }
}

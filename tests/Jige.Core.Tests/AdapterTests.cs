using Jige.Core.Adapters;

namespace Jige.Core.Tests;

public sealed class AdapterTests
{
    private static readonly HidInterface Ulx = new("ulx-1", "vendor", 0x361D, 0x0100, "UltralightX dongle",
        0xFF00, 1, 64, 64, 0, 2);
    private static readonly UsbSerialPort Sony = new("sony-1", "serial", "COM7",
        [@"USB\VID_054C&PID_0E53&REV_0100&MI_06"]);

    [Fact]
    public void DetectUsesExactInterfacesAndSeparatesIdenticalReceivers()
    {
        var registry = AdapterRegistry.CreateDefault();
        var devices = registry.Detect(new(
            [Ulx with { Path = "mouse", UsagePage = 1 }, Ulx, Ulx with { ReceiverId = "ulx-2", Path = "vendor-2" },
             Ulx with { ReceiverId = "wired", ProductId = 0x0102, Path = "vendor-generic" },
             Ulx with { ReceiverId = "bad-shape", ProductId = 0x0103, UsagePage = 0xFF01 }],
            [Sony, Sony with { ReceiverId = "sony-2", HardwareIds = [@"USB\VID_054C&PID_0E53&MI_060"] }], [])).ToArray();

        Assert.Equal(["vendor", "vendor-2", "vendor-generic", "serial"], devices.Select(device => device.Target.Endpoint.Path));
        Assert.Equal(AdaptationSource.Catalog, devices[0].Target.Device.Source);
        Assert.Equal(AdaptationSource.Generic, devices[2].Target.Device.Source);
        Assert.Equal(4, devices.Select(device => device.Target.Device.Id).Distinct().Count());
    }

    [Fact]
    public async Task AsleepStopsAfterLinkAndClosesTheCollection()
    {
        var hid = new HidStub(new() { [0xA4] = "0503240100" });
        var transport = new TransportStub { Hid = hid };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([Ulx], [], [])));

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Null(reading.Percentage);
        Assert.Equal([0xA4], hid.Writes);
        Assert.True(hid.Closed);
    }

    [Fact]
    public async Task SilentPercentageFallsBackToVoltageAndChecksCharging()
    {
        var hid = new HidStub(new() { [0xA4] = "0503240101", [0x85] = "050405026410", [0xA5] = "0503250100" });
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([Ulx], [], [])));

        var reading = await registry.ReadAsync(device, new TransportStub { Hid = hid }, null, CancellationToken.None);

        Assert.Equal(78, reading.Percentage);
        Assert.True(reading.IsEstimated);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        Assert.Equal([0xA4, 0xA6, 0x85, 0xA5], hid.Writes);
        Assert.True(hid.Closed);
    }

    [Fact]
    public async Task ValidDirectPercentageDoesNotQueryVoltage()
    {
        var hid = new HidStub(new() { [0xA4] = "0503240101", [0xA6] = "05052603646410", [0xA5] = "0503250101" });
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([Ulx], [], [])));

        var reading = await registry.ReadAsync(device, new TransportStub { Hid = hid }, null, CancellationToken.None);

        Assert.Equal(100, reading.Percentage);
        Assert.False(reading.IsEstimated);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Equal([0xA4, 0xA6, 0xA5], hid.Writes);
        Assert.True(hid.Closed);
    }

    [Fact]
    public async Task SonyPassesBaudAndClosesAfterOneFragmentedBatteryRead()
    {
        var serial = new SerialStub();
        var transport = new TransportStub { Serial = serial };
        var registry = AdapterRegistry.CreateDefault(460800);
        var device = Assert.Single(registry.Detect(new([], [Sony], [])));
        List<string> received = [];

        var reading = await registry.ReadAsync(device, transport, (direction, bytes) =>
        {
            if (direction == FrameDirection.Received)
                received.Add(Convert.ToHexString(bytes));
        }, CancellationToken.None);

        Assert.Equal(460800, transport.Baud);
        Assert.Equal(50, reading.Percentage);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal([1, 2, 4], serial.Writes);
        Assert.Equal(3, received.Count);
        Assert.Equal("04FF0B0096C314041001000032B4", received[2]);
        Assert.True(serial.Closed);
    }

    [Fact]
    public async Task BusySerialPortIsOnlyOpenedOnce()
    {
        var transport = new TransportStub { Busy = true };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([], [Sony], [])));

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(LinkState.Busy, reading.Link);
        Assert.Equal(ReadIssue.Busy, reading.Issue);
        Assert.Null(reading.Percentage);
        Assert.Equal(1, transport.SerialOpens);
    }

    [Fact]
    public async Task CancelledNativeWorkKeepsReceiverLockedUntilItActuallyFinishes()
    {
        var adapter = new BlockingAdapter();
        var registry = new AdapterRegistry(adapter);
        var target = new DeviceTarget(new("slot-1", "测试设备", DeviceKind.Mouse), Ulx, "1");
        var device = new DetectedDevice(adapter, target);
        var transport = new TransportStub();
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        var first = registry.ReadAsync(device, transport, null, firstCancellation.Token);
        await adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        firstCancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            using var secondCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            secondCancellation.CancelAfter(100);
            var second = registry.ReadAsync(device, transport, null, secondCancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Equal(1, adapter.Entrances);
        }
        finally
        {
            adapter.Release.TrySetResult();
        }

        var third = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(2, adapter.Entrances);
        Assert.Equal(50, third.Percentage);
    }

    private sealed class BlockingAdapter : IDeviceAdapter
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Entrances;

        public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory) => [];

        public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
            FrameTrace? trace, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Entrances);
            Entered.TrySetResult();
            await Release.Task;
            return new(target.Device, 50, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow);
        }
    }

    private sealed class TransportStub : IReceiverTransport
    {
        public IHidSession? Hid { get; init; }
        public ISerialSession? Serial { get; init; }
        public bool Busy { get; init; }
        public int Baud { get; private set; }
        public int SerialOpens { get; private set; }

        public IHidSession OpenHid(HidInterface endpoint, CancellationToken token) => Hid!;

        public ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken token)
        {
            SerialOpens++;
            if (Busy)
                throw new TransportException(ReadIssue.Busy, "测试串口被占用");
            Baud = baudRate;
            return Serial!;
        }
    }

    private sealed class HidStub(Dictionary<byte, string> replies) : IHidSession
    {
        private byte[]? reply;
        public List<byte> Writes { get; } = [];
        public bool Closed { get; private set; }

        public ValueTask WriteOutputAsync(byte[] report, CancellationToken token)
        {
            Writes.Add(report[2]);
            reply = replies.TryGetValue(report[2], out var hex) ? Convert.FromHexString(hex) : null;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token)
        {
            if (reply is null)
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            }
            reply.CopyTo(report, 0);
            int length = reply.Length;
            reply = null;
            return length;
        }

        public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) => throw new InvalidOperationException();

        public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token) => throw new InvalidOperationException();

        public void Dispose() => Closed = true;
    }

    private sealed class SerialStub : ISerialSession
    {
        private readonly Queue<string> chunks = new();
        public List<byte> Writes { get; } = [];
        public bool Closed { get; private set; }

        public ValueTask WriteAsync(byte[] bytes, CancellationToken token)
        {
            Writes.Add(bytes[7]);
            switch (bytes[7])
            {
                case 1:
                    chunks.Enqueue("04FF0A0096C31201100100017E");
                    break;
                case 2:
                    chunks.Enqueue("04FF090096C3140210010080");
                    break;
                case 4:
                    chunks.Enqueue("04FF");
                    chunks.Enqueue("0B0096C31404100100");
                    chunks.Enqueue("0032B4");
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask<int> ReadAsync(byte[] buffer, CancellationToken token)
        {
            if (!chunks.TryDequeue(out var chunk))
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            }
            var data = Convert.FromHexString(chunk);
            data.CopyTo(buffer, 0);
            return data.Length;
        }

        public void Dispose() => Closed = true;
    }
}

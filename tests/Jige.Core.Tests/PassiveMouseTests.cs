using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class PassiveMouseTests
{
    [Theory]
    [InlineData(0x1D57, 0xFA60, "03FF40014A", "035540014A", 74, "ATTACK SHARK X11", "03554003A0", null, ChargingState.Charging)]
    [InlineData(0x1D57, 0xFA60, "0320400063", "0320400163", 99, "Delux M600 Pro", "03204002FF", 100, ChargingState.Full)]
    [InlineData(0x1D57, 0xFA60, "034E500132", "034E400132", 50, "ATTACK SHARK X3 Max", "034E4002A0", null, ChargingState.Charging)]
    [InlineData(0x1D57, 0xFA60, "031040010B", "0310400107", 70, "ATTACK SHARK R1 / X6", "031040050A", 100, ChargingState.Unknown)]
    [InlineData(0x1D57, 0xFA65, "035540014A", "039540013C", 60, "Inphic IN9 Pro", "0399400200", 100, ChargingState.Full)]
    [InlineData(0x093A, 0x522C, "09650123456789AB", "093F0123456789AB", 63, "Incott 8K 鼠标", "09CE0123456789AB", 78, ChargingState.Charging)]
    [InlineData(0x24AE, 0x1413, "070102030405060064", "070102030405060139", 57, "Rapoo VT7", "07010203040506023A", 58, ChargingState.Charging)]
    [InlineData(0x24AE, 0x14A9, "070102030405060165", "07010203040506014A", 74, "Rapoo VT9 Pro V2", "07010203040506024B", 75, ChargingState.Charging)]
    public async Task RegisteredReceiversPushBatteryWithoutSending(int vid, int pid, string rejected, string first, int firstLevel,
        string name, string second, int? secondLevel, ChargingState secondCharging)
    {
        var registry = AdapterRegistry.CreateDefault();
        var hid = Endpoint(vid, pid);
        var device = Assert.Single(registry.Detect(new([hid], [], [])));
        Assert.Equal(PresenceProbeKind.PushListener, device.PresenceKind);
        Assert.Equal(VerificationTier.DocsOnly, device.Target.Device.Verification);
        var transport = new FakeReceiverTransport((hid.Path,
            [new(DiagnosticOperation.OpenHid), Input(rejected), Input(first), Input(second), Input("-")]));

        List<PresenceUpdate> updates = [];
        await registry.ListenPushAsync(device, transport, updates.Add, CancellationToken.None);

        Assert.Collection(updates,
            update => AssertReading(update, firstLevel, pid == 0xFA60 && name.Contains("R1") ? ChargingState.Unknown :
                ChargingState.NotCharging),
            update => AssertReading(update, secondLevel, secondCharging));
        Assert.Equal(0, transport.Writes);
        var opened = Assert.Single(transport.OpenedHids);
        Assert.True(opened.InputOnly);
        Assert.Equal(Convert.ToByte(first[..2], 16), opened.RequiredInputReportId);
        transport.AssertComplete();

        void AssertReading(PresenceUpdate update, int? level, ChargingState charging)
        {
            Assert.Equal(LinkState.Online, update.Link);
            Assert.Equal(hid.ReceiverId, update.ReceiverId);
            Assert.Equal(device.Target.Device.Id, update.DeviceId);
            Assert.NotNull(update.Battery);
            Assert.Equal(level, update.Battery.Percentage);
            Assert.Equal(charging, update.Battery.Charging);
            Assert.Equal(name, update.Battery.Device.Name);
        }
    }

    [Theory]
    [InlineData("2A112233445566778840", 64)]
    [InlineData("07112233445566778840", null)]
    [InlineData("2A112233445566778865", null)]
    public async Task OldVt9OnlyGetsFeature2A(string frame, int? percent)
    {
        var registry = AdapterRegistry.CreateDefault();
        var hid = Endpoint(0x24AE, 0x1205);
        var device = Assert.Single(registry.Detect(new([hid], [], [])));
        Assert.Equal(PresenceProbeKind.FullPollOnly, device.PresenceKind);
        var transport = new FakeReceiverTransport((hid.Path,
            [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.GetFeature, frame, Argument: 0x2A)]));

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);

        Assert.Equal(percent, reading.Percentage);
        Assert.Equal(ChargingState.Unknown, reading.Charging);
        Assert.Equal(percent is null ? ReadIssue.InvalidResponse : ReadIssue.None, reading.Issue);
        var opened = Assert.Single(transport.OpenedHids);
        Assert.True(opened.FeatureOnly);
        Assert.Equal((byte)0x2A, opened.RequiredFeatureReportId);
        Assert.Equal(0, transport.Writes);
        transport.AssertComplete();
    }

    [Fact]
    public async Task PollWindowAllowsAValidInputAndSilenceHasNoReading()
    {
        var hid = Endpoint(0x093A, 0x522C);
        // 输入脚本显式通知无帧，虚拟时钟保持不动。
        var registry = new AdapterRegistry(new PassiveMouseAdapter(clock: new ManualClock()));
        var device = Assert.Single(registry.Detect(new([hid], [], [])));
        var transport = new FakeReceiverTransport((hid.Path,
            [new(DiagnosticOperation.OpenHid), Input("09650123456789AB"), Input("09E40123456789AB"),
                new(DiagnosticOperation.OpenHid), Input("-")]));

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(100, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Null(reading.Percentage);
        Assert.Equal("等待鼠标上报电量", reading.Detail);
        Assert.Equal(0, transport.Writes);
        transport.AssertComplete();
    }

    [Fact]
    public async Task WindowDeadlineCancelsThePendingReadAndDisposesTheSession()
    {
        var clock = new ManualClock();
        var registry = new AdapterRegistry(new PassiveMouseAdapter(clock: clock));
        var hid = Endpoint(0x24AE, 0x1413);
        var device = Assert.Single(registry.Detect(new([hid], [], [])));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeReceiverTransport((hid.Path,
            [new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)]))
        {
            OnOperation = op =>
            {
                if (op == DiagnosticOperation.HidInput)
                    started.SetResult();
            }
        };

        var work = registry.ReadAsync(device, transport, null, CancellationToken.None);
        await started.Task;
        Assert.Equal(TimeSpan.FromMilliseconds(350), clock.Due);
        clock.Expire();
        var reading = await work;
        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Null(reading.Percentage);
        transport.AssertComplete();
    }

    [Fact]
    public async Task ZeroWindowReturnsWaitingWithoutOpening()
    {
        var registry = new AdapterRegistry(new PassiveMouseAdapter(TimeSpan.Zero));
        var device = Assert.Single(registry.Detect(new([Endpoint(0x1D57, 0xFA65)], [], [])));
        var transport = new FakeReceiverTransport();
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Null(reading.Percentage);
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(0x093A, 0x7777)]
    [InlineData(0x093A, 0x622C)]
    [InlineData(0x1D57, 0xFE01)]
    [InlineData(0x24AE, 0x7777)]
    [InlineData(0x24AE, 0x4613)]
    [InlineData(0x24AE, 0x4405)]
    public void UnlistedProductsAreNeverDetected(int vid, int pid)
    {
        var hid = Endpoint(vid, pid);
        Assert.False(AdapterRegistry.IsKnownHidReceiver(hid.VendorId, hid.ProductId));
        Assert.Empty(AdapterRegistry.CreateDefault().Detect(new([hid], [], [])));
    }

    [Theory]
    [InlineData(0x1D57, 0xFA60)]
    [InlineData(0x1D57, 0xFA65)]
    [InlineData(0x093A, 0x522C)]
    [InlineData(0x24AE, 0x1413)]
    [InlineData(0x24AE, 0x14A9)]
    [InlineData(0x24AE, 0x1205)]
    public async Task WrongAndAmbiguousCollectionsAreNotOpened(int vid, int pid)
    {
        var hid = Endpoint(vid, pid);
        var wrongUsage = hid with { Path = "control", UsagePage = 0xFF10, Usage = 3 };
        var wrongReport = hid with { Path = "wrong-id", InputReportIds = "11", FeatureReportIds = "11" };
        var featureOnly = hid with { Path = "feature-only", InputLength = 0, FeatureLength = 32 };
        var wrongInterface = hid with { Path = "wrong-mi", InterfaceNumber = 1 };
        var wrong = new List<HidInterface> { wrongUsage, wrongReport };
        if (pid != 0x1205)
            wrong.Add(featureOnly);
        if (vid is 0x1D57 or 0x093A)
            wrong.Add(wrongInterface);

        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new(wrong, [], [])));
        Assert.Empty(registry.Detect(new([hid, hid with { Path = "duplicate" }], [], [])));
        var device = Assert.Single(registry.Detect(new([hid, .. wrong], [], [])));
        Assert.Equal(hid, device.Target.Endpoint);

        var transport = new FakeReceiverTransport();
        foreach (var candidate in wrong)
        {
            var invalid = device with { Target = device.Target with { Endpoint = candidate } };
            await registry.ListenPushAsync(invalid, transport, _ => Assert.Fail("意外的状态更新"), CancellationToken.None);
            var reading = await registry.ReadAsync(invalid, transport, null, CancellationToken.None);
            Assert.Null(reading.Percentage);
        }
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public void RapooRequiresExactlyOneCandidateThatDeclaresInput07()
    {
        var hid = Endpoint(0x24AE, 0x1413);
        var second = hid with { Path = "other", Usage = 0x000E };
        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new([hid, second], [], [])));
        var device = Assert.Single(registry.Detect(new([hid with { InputReportIds = "08" }, second], [], [])));
        Assert.Equal(second, device.Target.Endpoint);
    }

    [Fact]
    public async Task DiagnosticReportExportsRedactedRecentFramesAndLiveWindow()
    {
        var hid = Endpoint(0x1D57, 0xFA60);
        var inventory = new DeviceInventory([hid], [], []);
        var registry = new AdapterRegistry(new PassiveMouseAdapter(clock: new ManualClock()));
        var device = Assert.Single(registry.Detect(inventory));
        var listener = new FakeReceiverTransport((hid.Path,
            [new(DiagnosticOperation.OpenHid), Input("03FE40BBCCDD"), Input("035540014BAA"), Input("-")]));
        await registry.ListenPushAsync(device, listener, _ => { }, CancellationToken.None);
        listener.AssertComplete();
        var capture = new FakeReceiverTransport((hid.Path,
            [new(DiagnosticOperation.OpenHid), Input("0355400151FF")]));

        string report = await DiagnosticReport.GenerateAsync(inventory, capture, liveRegistry: registry);

        Assert.Contains("被动帧|H1|未识别|030000000000", report);
        Assert.Contains("被动帧|H1|电量上报|035540014B00", report);
        Assert.Contains("|035540015100|1", report);
        Assert.DoesNotContain("03FE40BBCCDD", report);
        Assert.DoesNotContain("035540014BAA", report);
        Assert.Equal(0, listener.Writes + capture.Writes);
        capture.AssertComplete();

        var (replayInventory, replay, _) = FakeReceiverTransport.FromReport(report);
        var replayDevice = Assert.Single(registry.Detect(replayInventory));
        Assert.Equal(81, (await registry.ReadAsync(replayDevice, replay, null, CancellationToken.None)).Percentage);
        replay.AssertComplete();
    }

    private sealed class ManualClock : TimeProvider
    {
        private Action? elapsed;
        public TimeSpan Due { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime;
            elapsed = () => callback(state);
            return new ManualTimer();
        }

        public void Expire() => elapsed!();

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static ReceiverStep Input(string hex) => new(DiagnosticOperation.HidInput, hex);

    private static HidInterface Endpoint(int vid, int pid) => (vid, pid) switch
    {
        (0x1D57, _) => new("receiver", "battery", (ushort)vid, (ushort)pid, null, 0x000A, 0, 6, 0, 0, 2)
            { InputReportIds = "03" },
        (0x093A, _) => new("receiver", "battery", (ushort)vid, (ushort)pid, null, 0xFF05, 1, 8, 32, 9, 3)
            { InputReportIds = "09", FeatureReportIds = "09" },
        (0x24AE, 0x1205) => new("receiver", "battery", (ushort)vid, (ushort)pid, null, 0xFF0B, 0x0104, 0, 0, 10, null)
            { FeatureReportIds = "2A" },
        _ => new("receiver", "battery", (ushort)vid, (ushort)pid, null, 0xFF00, pid == 0x14A9 ? (ushort)14 : (ushort)2,
            13, 0, 0, null) { InputReportIds = "07" }
    };
}

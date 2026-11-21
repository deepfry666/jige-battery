using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class AuditHardeningCoreTests
{
    [Fact]
    public void CatalogEntriesHaveUniqueVidPidAndDoNotIntersectDisabledPaths()
    {
        HashSet<(ushort Vid, ushort Pid)> seen = [];
        foreach (var entry in DeviceCatalog.Entries)
        {
            Assert.True(seen.Add((entry.VendorId, entry.ProductId)),
                $"DeviceCatalog 存在重复条目: {entry.VendorId:X4}:{entry.ProductId:X4} ({entry.Model})");
        }

        foreach (var disabled in DeviceCatalog.DisabledPaths)
        {
            Assert.DoesNotContain((disabled.VendorId, disabled.ProductId), seen);
        }
    }

    [Fact]
    public void SafetyGateNeverEvictsInMemoryAndPersistsNewest64WithMonotonicVersion()
    {
        var initial = new Dictionary<string, string>
        {
            ["373B:9000:FF02:0002:17:17:0"] = "seeded"
        };
        var gates = new SafetyGateStore(initial);
        Assert.Equal(0, gates.Version);

        List<HidInterface> hids = [];
        for (ushort i = 1; i <= 70; i++)
        {
            var hid = new HidInterface($"rx-{i}", $"p-{i}", 0x373B, (ushort)(0x2000 + i), null, 0xFF02, 2, 17, 17, 0, 1);
            hids.Add(hid);
            Assert.True(gates.Block(hid, $"bad-{i}"));
        }

        Assert.Equal(71, gates.BlockedCount);
        Assert.Equal(70, gates.Version);
        foreach (var hid in hids)
            Assert.True(gates.IsBlocked(hid, out _));

        var snapshot = gates.Snapshot();
        Assert.Equal(SafetyGateStore.MaxBlockedEntries, snapshot.Count);
        Assert.Contains(SafetyGateStore.Signature(hids[^1]), snapshot.Keys);
        Assert.DoesNotContain(SafetyGateStore.Signature(hids[0]), snapshot.Keys);

        long versionBeforeClear = gates.Version;
        Assert.True(gates.Clear());
        Assert.Equal(versionBeforeClear + 1, gates.Version);
        Assert.False(gates.Clear());
        Assert.Equal(versionBeforeClear + 1, gates.Version);
    }

    [Fact]
    public async Task OperationDeadlineNormalizesInnerSelfCancellationToTimeout()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => OperationDeadline.RunAsync<int>(_ =>
        {
            using var inner = new CancellationTokenSource();
            inner.Cancel();
            return Task.FromCanceled<int>(inner.Token);
        }, TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task DeviceTopologyCacheInvalidateDuringScanForcesNextRescan()
    {
        var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int scans = 0;

        var cache = new DeviceTopologyCache(AdapterRegistry.CreateDefault(), async _ =>
        {
            int current = Interlocked.Increment(ref scans);
            if (current == 1)
            {
                scanStarted.TrySetResult();
                await allowFinish.Task;
            }
            return new DeviceInventory([], [], []);
        });

        var firstScan = cache.GetOrScanAsync(forceRefresh: false, CancellationToken.None);
        await scanStarted.Task;
        cache.Invalidate();
        allowFinish.TrySetResult();
        var first = await firstScan;
        Assert.True(first.Scanned);
        Assert.True(cache.IsDirty);

        var second = await cache.GetOrScanAsync(forceRefresh: false, CancellationToken.None);
        Assert.True(second.Scanned);
        Assert.Equal(2, scans);
        Assert.False(cache.IsDirty);
    }

    [Fact]
    public async Task DeviceTopologyCacheKeepsDirtyWhenScanHasWarnings()
    {
        int scans = 0;
        var cache = new DeviceTopologyCache(AdapterRegistry.CreateDefault(), _ =>
        {
            int current = Interlocked.Increment(ref scans);
            var warnings = current == 1 ? new[] { "hid-enum-warning" } : [];
            return Task.FromResult(new DeviceInventory([], [], warnings));
        });

        var first = await cache.GetOrScanAsync(forceRefresh: false, CancellationToken.None);
        Assert.True(first.Scanned);
        Assert.True(cache.IsDirty);

        var second = await cache.GetOrScanAsync(forceRefresh: false, CancellationToken.None);
        Assert.True(second.Scanned);
        Assert.Equal(2, scans);
        Assert.False(cache.IsDirty);
    }

    [Fact]
    public void BitMousePushRecognizesChecksumShiftedPushAndRejectsMalformedFrames()
    {
        var checksumPush = new byte[64];
        checksumPush[0] = 0x08;
        checksumPush[2] = 0x72;
        checksumPush[3] = 0x73;
        checksumPush[4] = 0x01;
        checksumPush[1] = MouseBatteryProtocol.BitMouseChecksum(checksumPush.AsSpan(2));
        Assert.True(MouseBatteryProtocol.IsBitMousePush(checksumPush));

        // 校验和错误或非 0x73 推送帧不得被误判为主动推送，确保非法回复仍会触发安全门拦截。
        var badChecksum = (byte[])checksumPush.Clone();
        badChecksum[1] ^= 0xFF;
        Assert.False(MouseBatteryProtocol.IsBitMousePush(badChecksum));

        var malformed = new byte[64];
        malformed[0] = 0x08;
        malformed[1] = 0x72;
        malformed[2] = 0x99;
        Assert.False(MouseBatteryProtocol.IsBitMousePush(malformed));
    }

    [Fact]
    public async Task BitMouseUnsolicitedPushFrameDoesNotBlockGenericReceiver()
    {
        var genericBitMouse = new HidInterface("rx-bit-push", "p-bit-push", 0x373B, 0x1889, null, 0xFF05, 1, 64, 64, 0, 2);
        var gates = new SafetyGateStore();
        var registry = AdapterRegistry.CreateDefault(gates: gates);
        var target = Assert.Single(registry.Detect(new([genericBitMouse], [], [])));

        var push = new byte[64];
        push[0] = 0x08;
        push[1] = 0x72;
        push[2] = 0x73;
        var req = Convert.ToHexString(MouseBatteryProtocol.Request(MouseFamily.BitMouse, charging: false));
        var transport = new FakeReceiverTransport((genericBitMouse.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, req),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(push)),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));

        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Asleep, reading.Link);
        Assert.Equal(ReadIssue.Timeout, reading.Issue);
        Assert.False(gates.IsBlocked(genericBitMouse, out _));
        transport.AssertComplete();
    }

    [Fact]
    public async Task RazerChargingDisconnectedPreservesBatteryPercentage()
    {
        var hid = new HidInterface("rx-razer", "p-razer", 0x1532, 0x00A5, "Razer Mouse", 0x0001, 0x0002, 0, 0, 91, 0)
        {
            FeatureOnly = true
        };
        var adapter = new RazerFeatureAdapter(settleDelay: TimeSpan.Zero, busyRetryDelay: TimeSpan.Zero);
        var registry = new AdapterRegistry(adapter);
        var target = Assert.Single(registry.Detect(new([hid], [], [])));

        var batteryReqBytes = RazerFeatureProtocol.Encode(0x00A5, RazerFeatureQuery.Battery);
        var batteryReq = Convert.ToHexString(batteryReqBytes);
        var chargingReq = Convert.ToHexString(RazerFeatureProtocol.Encode(0x00A5, RazerFeatureQuery.Charging));
        var batteryReply = new byte[91];
        batteryReply[1] = 0x02;
        batteryReply[2] = batteryReqBytes[2];
        batteryReply[6] = 0x02;
        batteryReply[7] = 0x07;
        batteryReply[8] = 0x80;
        batteryReply[10] = 0xCC; // ~80%
        batteryReply[89] = RazerFeatureProtocol.Checksum(batteryReply);

        var transport = new FakeReceiverTransport((hid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, batteryReq),
            new(DiagnosticOperation.GetFeature, Convert.ToHexString(batteryReply), Argument: 0),
            new(DiagnosticOperation.SetFeature, chargingReq, Issue: ReadIssue.Disconnected)
        ]));

        var reading = await registry.ReadAsync(target, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(80, reading.Percentage);
        Assert.Equal(ChargingState.Unknown, reading.Charging);
        transport.AssertComplete();
    }

    [Fact]
    public async Task AdapterRegistryIsolatesUnexpectedAndDisposedAdapterExceptions()
    {
        var hid = new HidInterface("rx-boom", "p-boom", 0x1234, 0x5678, null, 0xFF00, 1, 64, 64, 0, 0);
        var device = new DeviceIdentity("boom:1", "Boom", DeviceKind.Mouse);
        var target = new DeviceTarget(device, hid, "0");

        var registry = new AdapterRegistry();
        var disposedReading = await registry.ReadAsync(new(new ThrowingAdapter(new ObjectDisposedException("session")), target),
            new FakeReceiverTransport(), null, CancellationToken.None);
        Assert.Equal(ReadIssue.Disconnected, disposedReading.Issue);
        Assert.Equal(LinkState.Offline, disposedReading.Link);

        var unexpectedReading = await registry.ReadAsync(new(new ThrowingAdapter(new InvalidOperationException("boom")), target),
            new FakeReceiverTransport(), null, CancellationToken.None);
        Assert.Equal(ReadIssue.InvalidResponse, unexpectedReading.Issue);
        Assert.Contains("InvalidOperationException", unexpectedReading.Detail);
    }

    [Fact]
    public async Task AdapterRegistryCheckPresenceReturnsNullOnUnexpectedException()
    {
        var hid = new HidInterface("rx-hb", "p-hb", 0x1234, 0x5678, null, 0xFF00, 1, 64, 64, 0, 0);
        var device = new DeviceIdentity("hb:1", "HB", DeviceKind.Mouse);
        var target = new DeviceTarget(device, hid, "0");

        var registry = new AdapterRegistry();
        var update = await registry.CheckPresenceAsync(
            new(new ThrowingHeartbeatAdapter(new InvalidOperationException("unexpected")), target),
            new FakeReceiverTransport(), CancellationToken.None);
        Assert.Null(update);
    }

    [Fact]
    public void AdapterRegistryReadBudgetAllocatesLargerBudgetForMultiDeviceAdapters()
    {
        var multi = new LogitechHidppAdapter();
        var single = new RazerFeatureAdapter();
        Assert.Equal(TimeSpan.FromSeconds(13.5), AdapterRegistry.ReadBudget(multi));
        Assert.Equal(TimeSpan.FromSeconds(7), AdapterRegistry.ReadBudget(single));
        var hid = new HidInterface("rx-l", "p-l", 0x046D, 0xC548, null, 0xFF00, 1, 20, 20, 0, 0);
        var target = new DeviceTarget(new DeviceIdentity("l:1", "Logi", DeviceKind.Mouse), hid, "0");
        Assert.True(new DetectedDevice(multi, target).IsMultiDevice);
        Assert.False(new DetectedDevice(single, target).IsMultiDevice);
    }

    [Fact]
    public void SerialEndpointRespectsDisabledPathsViaHardwareIdsAndIgnoresTruncatedIds()
    {
        var disabled = DeviceCatalog.DisabledPaths[0];
        var serial = new UsbSerialPort("rx-serial", @"\\.\COM9", "COM9",
            [$@"USB\VID_{disabled.VendorId:X4}&PID_{disabled.ProductId:X4}&MI_06"]);
        Assert.True(DeviceCatalog.IsDisabledPath(serial, out var matched));
        Assert.Equal(disabled.Reason, matched?.Reason);

        var truncated = new UsbSerialPort("rx-trunc", @"\\.\COM10", "COM10",
            [@"USB\VID_054C&PID_0E5", @"USB\VID_054", @"USB\PID_0E53"]);
        Assert.False(DeviceCatalog.IsDisabledPath(truncated, out var none));
        Assert.Null(none);
    }

    private sealed class ThrowingAdapter(Exception exception) : IDeviceAdapter
    {
        public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory) => [];
        public ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class ThrowingHeartbeatAdapter(Exception exception) : IDeviceAdapter, IPresenceAdapter
    {
        public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;
        public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory) => [];
        public ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken) =>
            throw exception;
        public ValueTask<PresenceUpdate?> CheckPresenceAsync(DeviceTarget target, IReceiverTransport transport, CancellationToken cancellationToken) =>
            throw exception;
    }
}

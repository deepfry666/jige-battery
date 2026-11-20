using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using Jige.Core.Windows;

namespace Jige.Core.Tests;

public sealed class GenericAndPresenceTests
{
    private const string CompxBatteryReq = "0804000000000000000000000000000049";
    private const string CompxOnlineReq = "080300000000000000000000000000004A";
    private const string CompxCidMidReq = "081000000000000000000000000000003D";
    private const string CompxBatteryReply = "08040000000219000E46000000000000DA";

    private static string CompxFrame(byte cmd, params byte[] data)
    {
        var frame = new byte[17];
        frame[0] = 0x08;
        frame[1] = cmd;
        frame[5] = (byte)data.Length;
        data.CopyTo(frame, 6);
        frame[16] = MouseBatteryProtocol.CompxChecksum(frame.AsSpan(0, 16));
        return Convert.ToHexString(frame);
    }

    [Fact]
    public void A1_GenericFamilyDetectsUnlistedUlxCompxAndBitMouse()
    {
        var ulxGeneric = new HidInterface("r-ulx", "p-ulx", 0x361D, 0x0299, null, 0xFF00, 1, 64, 64, 0, 2);
        var compxGeneric = new HidInterface("r-compx", "p-compx", 0x373B, 0x1999, null, 0xFF02, 2, 17, 17, 0, 1);
        var bitMouseGeneric = new HidInterface("r-bit", "p-bit", 0x373B, 0x1888, null, 0xFF05, 1, 64, 64, 0, 2);

        var registry = AdapterRegistry.CreateDefault();
        var detected = registry.Detect(new([ulxGeneric, compxGeneric, bitMouseGeneric], [], [])).ToArray();

        Assert.Equal(3, detected.Length);
        Assert.All(detected, item => Assert.Equal(AdaptationSource.Generic, item.Target.Device.Source));
        Assert.Contains(detected, item => item.Target.Endpoint.Path == "p-ulx" && item.Target.Device.Name == "Finalmouse ULX");
        Assert.Contains(detected, item => item.Target.Endpoint.Path == "p-compx" && item.Target.Device.Name == "ATK 鼠标");
        Assert.Contains(detected, item => item.Target.Endpoint.Path == "p-bit" && item.Target.Device.Name == "ATK 鼠标");
        Assert.True(AdapterRegistry.IsKnownHidReceiver(0x361D, 0x0299));
        Assert.True(AdapterRegistry.IsKnownHidReceiver(0x373B, 0x1999));
    }

    [Fact]
    public void A2_Gate1_RejectsAmbiguousMultiFamilyAndUnmatchedWithoutSending()
    {
        var gates = new SafetyGateStore();
        var registry = AdapterRegistry.CreateDefault(gates: gates);

        // 同一物理接收器同时具备 Compx-OUT (FF02:0002 17/17) 和 BitMouse (FF05:0001 64/64) → 多家族冲突。
        var compx = new HidInterface("same-rx", "c1", 0x373B, 0x1999, null, 0xFF02, 2, 17, 17, 0, 1);
        var bitMouse = new HidInterface("same-rx", "c2", 0x373B, 0x1999, null, 0xFF05, 1, 64, 64, 0, 2);
        // 游狼未收录 PID 匹配新旧两代 XVI → 多家族冲突。
        var unlistedXvi = new HidInterface("xvi-rx", "x1", 0x33E4, 0x9999, null, 0xFFFF, 0, 0, 0, 65, 2);
        // 未知厂商或无匹配特征 → 0 个家族匹配。
        var unmatched = new HidInterface("other-rx", "o1", 0x1234, 0x5678, null, 0xFF02, 2, 17, 17, 0, 1);

        var detected = registry.Detect(new([compx, bitMouse, unlistedXvi, unmatched], [], [])).ToArray();
        Assert.Empty(detected);
        Assert.Contains(gates.RecentRejections(), item => item.Gate == 1 && item.VendorId == 0x373B);
        Assert.Contains(gates.RecentRejections(), item => item.Gate == 1 && item.VendorId == 0x33E4);
    }

    [Fact]
    public async Task A2_Gate2AndGate3_StopsOnInvalidReplyPersistsAcrossRestartAndClearsOnRedetect()
    {
        var genericCompx = new HidInterface("rx-bad", "bad-path", 0x373B, 0x1998, null, 0xFF02, 2, 17, 17, 0, 1);
        var gates = new SafetyGateStore();
        var registry = AdapterRegistry.CreateDefault(gates: gates);
        var device = Assert.Single(registry.Detect(new([genericCompx], [], [])));

        // 安全门 ②：发送首条无害查询 0x04 后收到非法回复，立即停止（不继续发 0x10），并触发安全门 ③ 记录拦截。
        var firstTransport = new FakeReceiverTransport((genericCompx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, "DEADBEEF00000000000000000000000000"),
            new(DiagnosticOperation.HidOutput, CompxBatteryReq),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));
        var reading = await registry.ReadAsync(device, firstTransport, null, CancellationToken.None);
        Assert.Equal(ReadIssue.InvalidResponse, reading.Issue);
        firstTransport.AssertComplete();

        string sig = SafetyGateStore.Signature(genericCompx);
        Assert.True(gates.IsBlocked(sig, out _));
        Assert.Equal(1, gates.BlockedCount);

        // 模拟重启：将拦截表持久化快照加载进新实例，确认重启后不再发任何命令。
        var persisted = gates.Snapshot();
        var restartedGates = new SafetyGateStore(persisted);
        var restartedRegistry = AdapterRegistry.CreateDefault(gates: restartedGates);
        Assert.Empty(restartedRegistry.Detect(new([genericCompx], [], [])));

        var zeroTransport = new FakeReceiverTransport();
        var blockedRead = await restartedRegistry.ReadAsync(device, zeroTransport, null, CancellationToken.None);
        Assert.Equal(ReadIssue.IoError, blockedRead.Issue);
        Assert.Equal(0, zeroTransport.Calls);

        // 用户点「重新检测」清除安全门拦截后，重新允许识别与查询。
        Assert.True(restartedGates.Clear());
        Assert.Single(restartedRegistry.Detect(new([genericCompx], [], [])));
    }

    [Fact]
    public async Task A2_Gate2AndGate3_UlxGenericBlocksOnMalformedLinkReply()
    {
        var genericUlx = new HidInterface("rx-ulx-bad", "ulx-bad", 0x361D, 0x0288, null, 0xFF00, 1, 64, 64, 0, 2);
        var gates = new SafetyGateStore();
        var registry = AdapterRegistry.CreateDefault(gates: gates);
        var device = Assert.Single(registry.Detect(new([genericUlx], [], [])));

        var linkReq = Convert.ToHexString(FinalmouseProtocol.Encode(UlxQuery.Link));
        var transport = new FakeReceiverTransport((genericUlx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, linkReq),
            new(DiagnosticOperation.HidInput, MouseAdapterTests.Pad("999999", 64)),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));

        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(ReadIssue.InvalidResponse, reading.Issue);
        Assert.True(gates.IsBlocked(genericUlx, out _));
        Assert.Empty(registry.Detect(new([genericUlx], [], [])));
        transport.AssertComplete();
    }

    [Fact]
    public async Task A3_SharedAtk11D9DefaultsToAtkMouseAndRefinesNameOnlyFromCidMid()
    {
        // 实机 2026-11-19 记录的 373B:11D9 集合布局。
        var compx = new HidInterface("rx-11d9", "p-ff02", 0x373B, 0x11D9, "Wireless mouse 8k dongle-L", 0xFF02, 2, 17, 17, 0, 1);
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([compx], [], [])));

        // 未通过协议读到具体型号前，绝不写死为「ATK A9 Ultimate」。
        Assert.Equal("ATK 鼠标", target.Target.Device.Name);
        Assert.DoesNotContain("A9 Ultimate", target.Target.Device.Name);

        // 当 0x10 返回 CID/MID = 1/8（OpenMouse 记录的 F1 Ultimate 2.0）时，升级显示具体型号。
        var transportWithCidMid = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, CompxBatteryReply),
            new(DiagnosticOperation.HidOutput, CompxBatteryReq),
            new(DiagnosticOperation.HidInput, CompxFrame(0x10, 1, 8)),
            new(DiagnosticOperation.HidOutput, CompxCidMidReq)
        ]));
        var identified = await registry.ReadAsync(target, transportWithCidMid, null, CancellationToken.None);
        Assert.Equal(25, identified.Percentage);
        Assert.Equal("ATK F1 Ultimate 2.0", identified.Device.Name);
        transportWithCidMid.AssertComplete();

        // 当 0x10 超时时，保留「ATK 鼠标」并正常返回电量。
        var transportNoCidMid = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, CompxBatteryReply),
            new(DiagnosticOperation.HidOutput, CompxBatteryReq),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
            new(DiagnosticOperation.HidOutput, CompxCidMidReq)
        ]));
        var fallback = await registry.ReadAsync(target, transportNoCidMid, null, CancellationToken.None);
        Assert.Equal(25, fallback.Percentage);
        Assert.Equal("ATK 鼠标", fallback.Device.Name);
        transportNoCidMid.AssertComplete();
    }

    [Fact]
    public async Task A5_AtkCompxReceivesReplyOnSiblingVendorCollectionAndLogsPerCollectionStats()
    {
        var primary = new HidInterface("rx-11d9", "p-ff02", 0x373B, 0x11D9, "Wireless mouse 8k dongle-L", 0xFF02, 2, 17, 17, 0, 1);
        var siblingFf06 = new HidInterface("rx-11d9", "p-ff06", 0x373B, 0x11D9, "Wireless mouse 8k dongle-L", 0xFF06, 2, 49, 49, 0, 1);
        var siblingFf05 = new HidInterface("rx-11d9", "p-ff05", 0x373B, 0x11D9, "Wireless mouse 8k dongle-L", 0xFF05, 0, 8, 0, 0, 1);

        var paddedReplyOnFf06 = CompxBatteryReply + new string('0', (49 - 17) * 2);
        var transport = new FakeReceiverTransport(
            (primary.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
                new(DiagnosticOperation.HidOutput, CompxBatteryReq),
                new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout),
                new(DiagnosticOperation.HidOutput, CompxCidMidReq)
            ]),
            // 仅只读监听 FF06，绝不向 FF06 发送输出报告。
            (siblingFf06.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, paddedReplyOnFf06)
            ]),
            (siblingFf05.Path,
            [
                new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
            ]));

        string report = await DiagnosticReport.GenerateAsync(new([primary, siblingFf06, siblingFf05], [], []),
            transport, CancellationToken.None);
        transport.AssertComplete();

        Assert.Contains("电量 25%", report);
        Assert.Contains("集合统计|H1|FF02:0002|", report);
        Assert.Contains("集合统计|H2|FF06:0002|发送=0|接收=1|", report);
        Assert.Contains("集合统计|H3|FF05:0000|发送=0|接收=0|超时=1|", report);
    }

    [Fact]
    public void A6_GWolvesOfficialModelTableEnablesUnambiguousModelsAndKeeps3517Disabled()
    {
        var registry = AdapterRegistry.CreateDefault();
        var htmPlus = new HidInterface("gw-1", "p1", 0x33E4, 0x3817, "G-Wolves", 0xFFFF, 0, 0, 0, 65, 2);
        var vuk = new HidInterface("gw-2", "p2", 0x33E4, 0x3917, "G-Wolves", 0xFFFF, 0, 0, 0, 65, 2);
        var fenrirAsymConflict = new HidInterface("gw-3", "p3", 0x33E4, 0x3517, "G-Wolves", 0xFFFF, 0, 0, 0, 65, 2);

        var detected = registry.Detect(new([htmPlus, vuk, fenrirAsymConflict], [], [])).ToArray();
        Assert.Equal(2, detected.Length);
        Assert.Contains(detected, item => item.Target.Device.Name == "G-Wolves HTM Plus");
        Assert.Contains(detected, item => item.Target.Device.Name == "G-Wolves VUK");
        Assert.Equal(MouseFamily.XviNew, MouseReceiverAdapter.AllowedRoute(htmPlus)?.Family);
        Assert.Equal(MouseFamily.XviOld, MouseReceiverAdapter.AllowedRoute(vuk)?.Family);
    }

    [Fact]
    public async Task B1_CompxHeartbeatUses0x03AndDetectsOffline()
    {
        var compx = new HidInterface("rx-atk", "p-atk", 0x373B, 0x11D9, null, 0xFF02, 2, 17, 17, 0, 1);
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([compx], [], [])));
        Assert.Equal(PresenceProbeKind.Heartbeat, device.PresenceKind);

        var transport = new FakeReceiverTransport((compx.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, CompxFrame(0x03, 0, 0xAA, 0xBB, 0xCC)),
            new(DiagnosticOperation.HidOutput, CompxOnlineReq)
        ]));
        var update = await registry.CheckPresenceAsync(device, transport, CancellationToken.None);
        Assert.NotNull(update);
        Assert.Equal(LinkState.Offline, update!.Link);
        transport.AssertComplete();
    }

    [Fact]
    public async Task B2_LogitechListensForPushConnectionEventsInsteadOfHeartbeat()
    {
        var longHid = new HidInterface("logi-rx", "logi-long", 0x046D, 0xC548, "Bolt", 0xFF00, 2, 20, 20, 0, 2);
        var shortHid = longHid with { Path = "logi-short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([longHid, shortHid], [], [])));

        Assert.Equal(PresenceProbeKind.PushListener, device.PresenceKind);
        // 心跳必须跳过罗技，不发任何包。
        var emptyTransport = new FakeReceiverTransport();
        Assert.Null(await registry.CheckPresenceAsync(device, emptyTransport, CancellationToken.None));
        Assert.Equal(0, emptyTransport.Calls);

        // 0x41 字节 4 的 bit6 (0x40) 置位表示断开，清零表示连接。
        List<PresenceUpdate> events = [];
        var pushTransport = new FakeReceiverTransport((shortHid.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidInput, "1001411040A940"), // 槽 1 断开 (0x40)
            new(DiagnosticOperation.HidInput, "1001411020A940"), // 槽 1 重连 (0x20, bit6=0)
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Disconnected)
        ]));
        await Assert.ThrowsAsync<TransportException>(() => registry.ListenPushAsync(device,
            new BatteryTransportStub(hid => hid.Path == shortHid.Path
                ? pushTransport.OpenHid(hid, CancellationToken.None) : new BatteryHidStub(_ => [])),
            events.Add, CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal((byte)1, events[0].Slot);
        Assert.Equal(LinkState.Offline, events[0].Link);
        Assert.Equal((byte)1, events[1].Slot);
        Assert.Equal(LinkState.Online, events[1].Link);
        pushTransport.AssertComplete();
    }

    [Fact]
    public async Task B3_SonyInzoneSkipsHeartbeatAndYieldsWhenInzoneHubIsRunning()
    {
        var sony = new UsbSerialPort("sony-rx", "COM7", "COM7", [@"USB\VID_054C&PID_0E53&REV_0100&MI_06"]);
        var registry = AdapterRegistry.CreateDefault(isInzoneHubRunning: () => true);
        var device = Assert.Single(registry.Detect(new([], [sony], [])));

        Assert.Equal(PresenceProbeKind.FullPollOnly, device.PresenceKind);
        var transport = new FakeReceiverTransport();

        // 心跳跳过 H9，不打开串口。
        Assert.Null(await registry.CheckPresenceAsync(device, transport, CancellationToken.None));
        // INZONE Hub 运行时退让，不打开串口，返回 Busy。
        var reading = await registry.ReadAsync(device, transport, null, CancellationToken.None);
        Assert.Equal(LinkState.Busy, reading.Link);
        Assert.Equal(ReadIssue.Busy, reading.Issue);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task C1_TopologyCacheAvoidsReEnumerationUntilInvalidatedAndDiffsReceivers()
    {
        var compx = new HidInterface("rx-1", "p1", 0x373B, 0x11D9, null, 0xFF02, 2, 17, 17, 0, 1);
        var ulx = new HidInterface("rx-2", "p2", 0x361D, 0x0100, null, 0xFF00, 1, 64, 64, 0, 2);
        DeviceInventory current = new([compx], [], []);
        int scans = 0;

        var cache = new DeviceTopologyCache(AdapterRegistry.CreateDefault(), _ =>
        {
            scans++;
            return Task.FromResult(current);
        });

        var first = await cache.GetOrScanAsync(false, CancellationToken.None);
        Assert.True(first.Scanned);
        Assert.Equal(1, scans);
        Assert.Empty(first.Changes); // 首次建立基线，不误报插入。

        // 无插拔事件时，连续 5 轮查询均复用缓存，不触发枚举。
        for (int i = 0; i < 5; i++)
        {
            var cached = await cache.GetOrScanAsync(false, CancellationToken.None);
            Assert.False(cached.Scanned);
            Assert.Single(cached.Detected);
        }
        Assert.Equal(1, scans);

        // 模拟新接收器插入并失效缓存。
        current = new([compx, ulx], [], []);
        cache.Invalidate();
        var afterPlug = await cache.GetOrScanAsync(false, CancellationToken.None);
        Assert.True(afterPlug.Scanned);
        Assert.Equal(2, scans);
        var inserted = Assert.Single(afterPlug.Changes);
        Assert.True(inserted.Inserted);
        Assert.Equal((ushort)0x361D, inserted.VendorId);
        Assert.Equal((ushort)0x0100, inserted.ProductId);

        // 模拟旧接收器拔出并失效缓存。
        current = new([ulx], [], []);
        cache.Invalidate();
        var afterUnplug = await cache.GetOrScanAsync(false, CancellationToken.None);
        var removed = Assert.Single(afterUnplug.Changes);
        Assert.False(removed.Inserted);
        Assert.Equal((ushort)0x373B, removed.VendorId);
        Assert.Equal((ushort)0x11D9, removed.ProductId);
    }

    [Fact]
    public async Task D2AndD3_DiagnosticContextIncludesHistoryMetricsAndSettingsWithoutLeakingSecrets()
    {
        var gates = new SafetyGateStore();
        var blockedHid = new HidInterface("secret-instance-id", @"\\?\hid#secret-path", 0x373B, 0x1999,
            "secret-computer-name", 0xFF02, 2, 17, 17, 0, 1);
        gates.Block(blockedHid, "回复格式不符合");

        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var dirtyDevice = new DeviceIdentity("mouse:secret-container-id", @"C:\Users\secret-user\my-mouse", DeviceKind.Mouse);
        var safeDevice = new DeviceIdentity("mouse:secret-container-2", "ATK 鼠标", DeviceKind.Mouse);
        var samples = Enumerable.Range(1, 7)
            .Select(i => new DiagnosticReadingSample(now.AddMinutes(-i), 50 + i, false, ChargingState.NotCharging))
            .ToArray();

        var context = new DiagnosticContext(
            DeviceHistories:
            [
                new(dirtyDevice, now.AddHours(-2), now.AddMinutes(-1), samples),
                new(safeDevice, now.AddHours(-1), now, samples.Take(2).ToArray())
            ],
            ProcessMetrics: new(32 * 1024 * 1024, 48 * 1024 * 1024, 450, 22, 28, TimeSpan.FromSeconds(1.25), TimeSpan.FromMinutes(15)),
            SettingsSummary: new("每台设备一个图标", 15, 60, 300, 8, 30, true, 2, true, gates.BlockedCount),
            Gates: gates,
            GameState: new(5, GameDetectionReason.ProcessName));

        string report = await DiagnosticReport.GenerateAsync(new([], [], []), new FakeReceiverTransport(),
            CancellationToken.None, 115200, context);

        Assert.Contains("# 安全门状态", report);
        Assert.Contains("# 设备历史（每台最多 5 次读数）", report);
        Assert.Contains("历史|Mouse|无线鼠标|", report);
        Assert.Contains("历史|Mouse|ATK 鼠标|", report);
        Assert.Contains("# 进程指标", report);
        Assert.Contains("私有内存=32.0MB|工作集=48.0MB|句柄=450|GDI=22|USER=28|累计CPU=1.25s|运行时长=15m00s", report);
        Assert.Contains("# 当前设置摘要", report);
        Assert.Contains("托盘模式=每台设备一个图标|低电量阈值=15%", report);
        Assert.Contains($"{Environment.NewLine}当前游戏判定：游戏中（通知状态=5，依据=进程名）", report);

        string desktopReport = await DiagnosticReport.GenerateAsync(new([], [], []), new FakeReceiverTransport(),
            context: context with { GameState = new(2, GameDetectionReason.None) });
        Assert.Contains("当前游戏判定：非游戏中（通知状态=2，依据=未触发）", desktopReport);

        // D3 脱敏验证：不出现任何序列号、实例 ID、用户名、计算机名或本地路径。
        Assert.DoesNotContain("secret", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HidBusClassifierDistinguishesUsbBluetoothAndInternalNonUsbDevices()
    {
        // 标准 USB 复合接收器（例如 ATK / MCHOSE）
        Assert.Equal(HidBus.Usb, HidBusClassifier.Classify(
            @"\\?\hid#vid_373b&pid_1216&mi_02&col02#7&1234",
            @"HID\VID_373B&PID_1216&MI_02&COL02\7&1234",
            [@"USB\VID_373B&PID_1216&MI_02\6&abcd", @"USB\VID_373B&PID_1216\5&ef01", @"USB\ROOT_HUB30\4&1"]));

        // 带厂商过滤驱动的 USB 外设
        Assert.Equal(HidBus.Usb, HidBusClassifier.Classify(
            @"\\?\hid#vid_1532&pid_0084&mi_01#7&1",
            @"HID\VID_1532&PID_0084&MI_01\7&1",
            [@"RZCONTROL\HID\0000", @"USB\VID_1532&PID_0084&MI_01\6&1", @"USB\VID_1532&PID_0084\5&1"]));

        // 蓝牙 HID（即使宿主机蓝牙适配器挂在内部 USB 总线上，也必须优先判为 Bluetooth）
        Assert.Equal(HidBus.Bluetooth, HidBusClassifier.Classify(
            @"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b034#8&1",
            @"HID\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02046D_PID&B034\8&1",
            [@"BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}\7&1", @"BTH\MS_BTHLE\6&1", @"USB\VID_8087&PID_0032\5&1"]));

        // 仅凭父节点识别的蓝牙 HID（例如蓝牙 SIG 厂商码）
        Assert.Equal(HidBus.Bluetooth, HidBusClassifier.Classify(
            @"\\?\hid#bt_custom#8&1",
            @"HID\BT_CUSTOM\8&1",
            [@"BTHENUM\DEV_112233445566\7&1", @"BTH\MS_BTHBRB\6&1", @"USB\VID_8087&PID_0032\5&1"]));

        // 笔记本内置 I2C-HID 触控板（例如 PixArt 093A:0255）
        Assert.Equal(HidBus.NonUsb, HidBusClassifier.Classify(
            @"\\?\hid#uniw0001&col03#5&1234",
            @"HID\UNIW0001&COL03\5&1234",
            [@"ACPI\UNIW0001\4&abcd", @"PCI\VEN_8086&DEV_51E8\3&1", @"ACPI\PNP0A08\0", @"HTREE\ROOT\0"]));

        // 虚拟 HID（哪怕节点名里带 VID_/PID_，只要父链不是 USB/蓝牙，也算 NonUsb）
        Assert.Equal(HidBus.NonUsb, HidBusClassifier.Classify(
            @"\\?\hid#vid_1234&pid_5678#1&1",
            @"HID\VID_1234&PID_5678\1&1",
            [@"ROOT\VHIDMINI\0000", @"HTREE\ROOT\0"]));

        // 异常情况：父链为空时，带 VID_/PID_ 回退为 USB，不带则判 NonUsb
        Assert.Equal(HidBus.Usb, HidBusClassifier.Classify(
            @"\\?\hid#vid_373b&pid_1216&mi_02#7&1", @"HID\VID_373B&PID_1216&MI_02\7&1", []));
        Assert.Equal(HidBus.NonUsb, HidBusClassifier.Classify(
            @"\\?\hid#uniw0001&col03#5&1", @"HID\UNIW0001&COL03\5&1", []));
    }

    [Fact]
    public void FallbackReceiverIdGroupsInternalI2cHidCollectionsWithoutMergingBluetooth()
    {
        // 同一个 I2C-HID 触控板的多个集合共享同一个 ACPI 父节点作为 ReceiverId
        string col1 = HidBusClassifier.ResolveFallbackReceiverId(
            @"HID\UNIW0001&COL01\5&1", "fb-1", [@"ACPI\UNIW0001\4&abcd", @"PCI\VEN_8086&DEV_51E8\3&1"]);
        string col3 = HidBusClassifier.ResolveFallbackReceiverId(
            @"HID\UNIW0001&COL03\5&1", "fb-3", [@"ACPI\UNIW0001\4&abcd", @"PCI\VEN_8086&DEV_51E8\3&1"]);
        Assert.Equal(@"ACPI\UNIW0001\4&abcd", col1);
        Assert.Equal(col1, col3);

        // 蓝牙节点不能折叠到宿主蓝牙 USB 控制器或 BTH 父节点上
        string bt = HidBusClassifier.ResolveFallbackReceiverId(
            @"BTHENUM\DEV_112233445566\7&1", "fb-bt", [@"BTH\MS_BTHBRB\6&1", @"USB\VID_8087&PID_0032\5&1"]);
        Assert.Equal(@"BTHENUM\DEV_112233445566\7&1", bt);
        Assert.Null(HidBusClassifier.FindUsbAncestor([@"BTHENUM\DEV_112233445566\7&1", @"USB\VID_8087&PID_0032\5&1"]));

        // 直接挂在 HTREE\ROOT\0 下时退回自身节点 ID
        string rootChild = HidBusClassifier.ResolveFallbackReceiverId(
            @"HID\VIRTUAL\1", "fb-root", [@"HTREE\ROOT\0"]);
        Assert.Equal(@"HID\VIRTUAL\1", rootChild);
    }

    [Fact]
    public async Task TopologyCacheIgnoresInternalNonUsbHidForUnknownReceiversAndPlugDiffs()
    {
        var atk = new HidInterface("rx-atk", "p-atk", 0x373B, 0x1216, null, 0xFF05, 1, 64, 64, 0, 2) { Bus = HidBus.Usb };
        var mchose = new HidInterface("rx-mchose", "p-mchose", 0x41E4, 0x2001, null, 0xFF02, 2, 20, 20, 0, 1) { Bus = HidBus.Usb };
        var touchpad = new HidInterface("acpi-touchpad", "p-tp", 0x093A, 0x0255, null, 0xFF00, 1, 0, 0, 257, null) { Bus = HidBus.NonUsb };
        var unknownUsb = new HidInterface("rx-unknown-usb", "p-unk", 0x1234, 0x5678, null, 0xFF00, 1, 64, 64, 0, 1) { Bus = HidBus.Usb };

        DeviceInventory current = new([atk, mchose, touchpad], [], []);
        var cache = new DeviceTopologyCache(AdapterRegistry.CreateDefault(), _ => Task.FromResult(current));

        var first = await cache.GetOrScanAsync(false, CancellationToken.None);
        Assert.Equal(2, first.Detected.Length);
        Assert.Equal(0, first.UnknownCount);
        Assert.Empty(first.Unknowns);

        // 内建触控板消失/重置不应触发接收器插拔事件，也不应计入未识别接收器
        current = new([atk, mchose], [], []);
        cache.Invalidate();
        var withoutTouchpad = await cache.GetOrScanAsync(false, CancellationToken.None);
        Assert.Empty(withoutTouchpad.Changes);
        Assert.Equal(0, withoutTouchpad.UnknownCount);

        // 真正未适配的外部 USB 接收器仍应被计数并记录
        current = new([atk, mchose, touchpad, unknownUsb], [], []);
        cache.Invalidate();
        var withUnknownUsb = await cache.GetOrScanAsync(false, CancellationToken.None);
        Assert.Equal(1, withUnknownUsb.UnknownCount);
        Assert.Single(withUnknownUsb.Unknowns);
        var inserted = Assert.Single(withUnknownUsb.Changes);
        Assert.True(inserted.Inserted);
        Assert.Equal((ushort)0x1234, inserted.VendorId);
    }
}

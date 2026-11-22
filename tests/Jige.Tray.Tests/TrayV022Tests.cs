using Jige.Core;
using Jige.Core.Adapters;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TrayV022Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity MouseA = new("mouse:a", "ATK 鼠标", DeviceKind.Mouse, AdaptationSource.Generic);
    private static readonly DeviceIdentity MouseB = new("ulx:b", "Finalmouse UltralightX", DeviceKind.Mouse);
    private static readonly DeviceIdentity Headset = new("inzone-h9:c", "Sony INZONE H9（一代接收器）", DeviceKind.Headset);
    private static readonly DeviceIdentity Logi = new("logitech-hidpp:rx/1/unit-1234", "PRO X SUPERLIGHT 2", DeviceKind.Mouse);

    [Fact]
    public void B1_SimulatedClockShowsNonLogitechOfflineWithin10Seconds()
    {
        long clockMs = 100_000L;
        var schedule = new PollSchedule(clockMs);
        var settings = new TraySettings { PollSeconds = 60 };
        var model = new TrayModel(settings, new());

        // t=0: 完整查询，鼠标在线 82%。
        schedule.Start(clockMs, game: false);
        model.ApplyPoll([new(MouseA, 82, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        schedule.Complete();
        Assert.Equal(LinkState.Online, model.PrimaryDevice!.Link);

        // t=8s（< 10s）：轻量心跳到期并检测到设备关机。
        long? nextHeartbeat = schedule.HeartbeatDue(clockMs, game: false, hasHeartbeatDevices: true);
        Assert.NotNull(nextHeartbeat);
        Assert.True(nextHeartbeat.Value - clockMs <= 10_000L);

        clockMs = nextHeartbeat.Value;
        schedule.StartHeartbeat(clockMs);
        var (changed, _) = model.ApplyPresence(
            [new("a", null, MouseA.Id, LinkState.Offline, ReadIssue.Timeout)],
            Now.AddSeconds(8));
        schedule.CompleteHeartbeat();

        Assert.True(changed);
        Assert.Equal(LinkState.Offline, model.PrimaryDevice!.Link);
        Assert.False(model.PrimaryDevice.Fresh);
        Assert.Equal(82, model.PrimaryDevice.Percentage);
    }

    [Fact]
    public void B2_LogitechPushPresenceUpdatesDeviceImmediatelyByReceiverAndSlot()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([new(Logi, 91, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        Assert.True(model.PrimaryDevice!.IsOnline);

        var (changed, needsRefresh) = model.ApplyPresence(
            [new("rx", 1, null, LinkState.Offline)], Now.AddSeconds(2));
        Assert.True(changed);
        Assert.False(needsRefresh);
        Assert.Equal(LinkState.Offline, model.PrimaryDevice.Link);

        var (reconnected, refreshNow) = model.ApplyPresence(
            [new("rx", 1, null, LinkState.Online)], Now.AddSeconds(5));
        Assert.True(reconnected);
        Assert.True(refreshNow);
        Assert.Equal(LinkState.Online, model.PrimaryDevice.Link);
    }

    [Fact]
    public void TrayIconPlanKeepsOnlyActiveDevicesAndRestoresTheirIdsAfterReconnect()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([
            new(MouseA, 65, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(Headset, 40, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);

        var custom = TrayIconPlan.Build(model, suppressTooltip: false);
        Assert.Equal(2, custom.Length);
        var mouseSlot = Assert.Single(custom, slot => slot.Key == MouseA.Id);
        Assert.Contains("ATK 鼠标 65%", mouseSlot.Tooltip);
        Assert.Contains(custom, slot => slot.Key == Headset.Id && slot.Tooltip.Contains("INZONE H9 40%"));

        // 取消勾选 Headset 后立即减少为 1 个图标。
        model.ApplySettings(model.ToggleTrayDevice(Headset.Id), Now);
        var afterUncheck = TrayIconPlan.Build(model, suppressTooltip: false);
        Assert.Equal(MouseA.Id, Assert.Single(afterUncheck).Key);

        model.ApplyPresence([new("a", null, MouseA.Id, LinkState.Offline)], Now.AddSeconds(8));
        var fallback = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Equal(TrayIconPlan.SingleKey, fallback.Key);
        Assert.Null(fallback.Device);
        Assert.Contains("几格", fallback.Tooltip);
        Assert.Equal(MouseA, Assert.Single(model.BuildMainPanel().OfflineRows).Device);
        Assert.True(model.CanRemove(MouseA.Id));

        model.ApplySettings(model.Settings with { TrayMode = TrayDisplayMode.LowestBattery }, Now);
        var lowest = TrayIconPlan.Build(model, suppressTooltip: false);
        var single = Assert.Single(lowest);
        Assert.Equal(TrayIconPlan.SingleKey, single.Key);
        Assert.Equal(Headset.Id, single.Device!.Device.Id);
        Assert.DoesNotContain("ATK 鼠标", single.Tooltip);

        model.ApplyPresence([new("h9", null, Headset.Id, LinkState.Asleep)], Now.AddSeconds(16));
        var sleep = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Contains("休眠中 · 上次 40%", sleep.Tooltip);
        Assert.DoesNotContain("ATK 鼠标", sleep.Tooltip);
        model.ApplyPresence([new("h9", null, Headset.Id, LinkState.Offline)], Now.AddSeconds(24));
        Assert.Equal(fallback, Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false)));

        model.ApplySettings(model.Settings with { TrayMode = TrayDisplayMode.Custom }, Now);
        Assert.Equal(fallback, Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false)));
        model.ApplyPoll([new(MouseA, 68, false, ChargingState.NotCharging, LinkState.Online, Now.AddSeconds(25))],
            false, Now.AddSeconds(25));
        var reconnected = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Equal(mouseSlot.Key, reconnected.Key);
        Assert.Equal(TrayIconPlan.StableId(mouseSlot.Key), TrayIconPlan.StableId(reconnected.Key));
    }

    [Fact]
    public void TrayUidReservationSurvivesRemovalEvenWhenStableIdsCollide()
    {
        const string first = "bolt/slot/81";
        const string second = "bolt/slot/1828";
        Assert.Equal(TrayIconPlan.StableId(first), TrayIconPlan.StableId(second));
        Dictionary<string, uint> reservations = new(StringComparer.OrdinalIgnoreCase);
        uint firstId = TrayIconPlan.ReserveId(first, reservations);
        uint secondId = TrayIconPlan.ReserveId(second, reservations);
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(1u, TrayIconPlan.ReserveId(TrayIconPlan.SingleKey, reservations));
        Assert.Equal(secondId, TrayIconPlan.ReserveId(second, reservations));
        Assert.Equal(firstId, TrayIconPlan.ReserveId(first.ToUpperInvariant(), reservations));
    }

    [Fact]
    public void B7_MainPanelAutoSortsLowestOnlineHeroCollapsesOfflineAndInsertsNewDevices()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([
            new(MouseA, 75, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(MouseB, 30, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(Headset, 90, false, ChargingState.NotCharging, LinkState.Online, Now.AddMinutes(-10))
        ], false, Now);
        // 让 Headset 离线。
        model.ApplyPresence([new("c", null, Headset.Id, LinkState.Offline)], Now.AddMinutes(1));

        var plan = model.BuildMainPanel();
        Assert.False(plan.HeroDisconnected);
        Assert.Equal(MouseB.Id, plan.Hero!.Device.Id); // 电量最低的在线设备 (30%)
        Assert.Equal([MouseA.Id], plan.OnlineRows.Select(d => d.Device.Id));
        Assert.Equal([Headset.Id], plan.OfflineRows.Select(d => d.Device.Id));
        Assert.Equal(2, plan.RowCount(offlineExpanded: false)); // MouseA + 「离线设备（1）」
        Assert.Equal(3, plan.RowCount(offlineExpanded: true));  // MouseA + 「离线设备（1）」 + Headset

        // 新设备一上线就自动排进去：Logi 18% 直接成为顶部大卡片。
        model.ApplyPoll([
            new(MouseA, 75, false, ChargingState.NotCharging, LinkState.Online, Now.AddMinutes(2)),
            new(MouseB, 30, false, ChargingState.NotCharging, LinkState.Online, Now.AddMinutes(2)),
            new(Logi, 18, false, ChargingState.NotCharging, LinkState.Online, Now.AddMinutes(2))
        ], false, Now.AddMinutes(2));

        var afterNew = model.BuildMainPanel();
        Assert.Equal(Logi.Id, afterNew.Hero!.Device.Id);
        Assert.Equal([MouseB.Id, MouseA.Id], afterNew.OnlineRows.Select(d => d.Device.Id));

        // 全部离线时，顶部大卡片显示最近一台离线设备并标明已断开。
        model.ApplyPoll([], false, Now.AddMinutes(5));
        var allOffline = model.BuildMainPanel();
        Assert.True(allOffline.HeroDisconnected);
        Assert.NotNull(allOffline.Hero);
        Assert.Equal(3, allOffline.OfflineRows.Length);
    }

    [Fact]
    public void D1AndD3_EventTrackerLogsOnlyStateChangesDeduplicatesAndRateLimitsWithoutPii()
    {
        List<string> lines = [];
        var tracker = new TrayEventTracker(lines.Add);

        // 1. 接收器插拔
        tracker.LogReceiverChanges([new(true, 0x373B, 0x11D9, 5), new(false, 0x361D, 0x0100, 1)]);
        Assert.Contains(lines, line => line.Contains("接收器插入 373B:11D9"));
        Assert.Contains(lines, line => line.Contains("接收器拔出 361D:0100"));

        // 2. 设备上线/离线/休眠仅在状态变化时记录，且不泄露路径/自定义名
        var dirtyDevice = new DeviceIdentity("mouse:secret-id", @"C:\Users\secret-name\mouse", DeviceKind.Mouse);
        tracker.LogDeviceLinkChange(dirtyDevice, LinkState.Online, LinkState.Online); // 无变化不记
        int before = lines.Count;
        tracker.LogDeviceLinkChange(dirtyDevice, LinkState.Online, LinkState.Offline);
        Assert.Equal(before + 1, lines.Count);
        Assert.Contains("无线鼠标：在线 → 离线", lines[^1]);

        // 3. 未识别接收器每个只记一次
        var unknown = new UnknownReceiverInfo("1234:5678:FF00:0001:64:64:0", 0x1234, 0x5678, 0xFF00, 1, 64, 64, 0);
        tracker.LogUnknownReceivers([unknown]);
        tracker.LogUnknownReceivers([unknown]);
        Assert.Single(lines, line => line.Contains("未识别设备 1234:5678"));

        // 4. 安全门拦截原因
        var rejection = new SafetyGateRejection("373B:1999:FF02:0002:17:17:0", 0x373B, 0x1999, 0xFF02, 2, 3, "回复格式不符合");
        tracker.LogSafetyGateRejection(rejection);
        tracker.LogSafetyGateRejection(rejection);
        Assert.Single(lines, line => line.Contains("安全门 3 拦截"));

        // 5. 读取失败按原因汇总，每小时最多一行
        long t = 1_000_000;
        tracker.RecordReadFailure(ReadIssue.Timeout, t);
        tracker.RecordReadFailure(ReadIssue.Timeout, t + 10_000);
        tracker.RecordReadFailure(ReadIssue.Busy, t + 20_000);
        Assert.Single(lines, line => line.Contains("读取失败汇总"));
        tracker.FlushReadFailuresIfDue(t + TrayEventTracker.FailureSummaryIntervalMs);
        Assert.Equal(2, lines.Count(line => line.Contains("读取失败汇总")));
        Assert.Contains("Timeout=1，Busy=1", lines[^1]);

        // 6. 进入、退出游戏仅在变化时记录
        var desktop = GameMode.Evaluate(5, ForegroundGame.None, gameSilent: false);
        var busy = GameMode.Evaluate(2, ForegroundGame.Fullscreen, gameSilent: false);
        var d3d = GameMode.Evaluate(3, ForegroundGame.None, gameSilent: false);
        tracker.LogGameMode(desktop); // 初始非游戏不记
        tracker.LogGameMode(busy);
        tracker.LogGameMode(d3d); // 仍在游戏，只记录进入时的依据
        tracker.LogGameMode(desktop);
        tracker.LogGameMode(desktop);
        Assert.Equal("进入游戏模式（通知状态=2，依据=BUSY 且前台全屏）",
            Assert.Single(lines, line => line.StartsWith("进入游戏模式", StringComparison.Ordinal)));
        Assert.Single(lines, line => line == "退出游戏模式");

        // 7. 检查更新结果
        tracker.LogUpdateCheck("已是最新版本 v0.2.2");
        Assert.Contains(lines, line => line.Contains("检查更新：已是最新版本 v0.2.2"));

        // D3：日志中绝不出现敏感路径/标识
        Assert.All(lines, line => Assert.DoesNotContain("secret", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void G5_DeviceHistoryIsBoundedToFiveReadingsAndThirtyTwoDevices()
    {
        var model = new TrayModel(new(), new());
        for (int i = 0; i < 40; i++)
        {
            var dev = new DeviceIdentity($"dev:{i:D2}", "ATK 鼠标", DeviceKind.Mouse);
            for (int r = 0; r < 8; r++)
            {
                model.ApplyPoll([new(dev, 90 - r * 5, false, ChargingState.NotCharging, LinkState.Online,
                    Now.AddMinutes(i * 10 + r))], false, Now.AddMinutes(i * 10 + r));
            }
        }

        var snapshot = model.Snapshot();
        Assert.True(snapshot.History.Count <= TrayModel.MaxHistoryDevices);
        Assert.All(snapshot.History.Values, entry => Assert.True(entry.RecentReadings.Length <= TrayModel.MaxHistorySamples));
    }

    [Fact]
    public void B5_TrayIconIdsAreStableAcrossRunsSoWindowsKeepsPromotedIcons()
    {
        Assert.Equal(1u, TrayIconPlan.StableId(TrayIconPlan.SingleKey));
        uint mouse = TrayIconPlan.StableId("atk:373B:11D9:mouse");
        Assert.Equal(mouse, TrayIconPlan.StableId("ATK:373b:11d9:MOUSE"));
        Assert.InRange(mouse, 2u, 65001u);
        Assert.NotEqual(mouse, TrayIconPlan.StableId("inzone-h9:054C:0E53"));
    }
}

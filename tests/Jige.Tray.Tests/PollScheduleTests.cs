using Jige.Core.Diagnostics;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class PollScheduleTests
{
    private const long T0 = 1_000_000L;

    [Fact]
    public void GameDelaysAutomaticWorkButHonorsAnExplicitRefresh()
    {
        var settings = new TraySettings();
        var schedule = new PollSchedule(T0);
        Assert.Equal(T0 + 300_000L, schedule.Due(settings, T0, true));
        schedule.Request();
        Assert.Equal(T0, schedule.Due(settings, T0, true));
        schedule.Start(T0, true);
        Assert.True(schedule.StartedByUser);
        schedule.Request();
        Assert.Null(schedule.Due(settings, T0, true));
        schedule.Complete();
        Assert.Equal(T0 + 300_000L, schedule.Due(settings, T0, true));
        Assert.Equal(T0 + 60_000L, schedule.Due(settings, T0, false));
    }

    [Fact]
    public void BackgroundRetryWaitsForGameToEnd()
    {
        var settings = new TraySettings { PollSeconds = 90, GamePollSeconds = 480 };
        var schedule = new PollSchedule(T0);
        long gamePoll = T0 + 480_000L;
        schedule.Request(userInitiated: false);
        Assert.Equal(gamePoll, schedule.Due(settings, T0, true));
        schedule.Start(gamePoll, true);
        Assert.False(schedule.StartedByUser);
        schedule.Complete();
        Assert.Equal(T0 + 960_000L, schedule.Due(settings, gamePoll, true));
        Assert.Equal(gamePoll + 1_000L, schedule.Due(settings, gamePoll + 1_000L, false));
    }

    [Fact]
    public void B1_HeartbeatRunsEvery8SecondsNormallyAnd30SecondsInGame()
    {
        var settings = new TraySettings { PollSeconds = 60, GamePollSeconds = 300 };
        var schedule = new PollSchedule(T0);
        schedule.Start(T0, game: false);
        schedule.Complete();

        // 平时 8 秒一次轻量心跳，电量查询仍保持 60 秒。
        Assert.Equal(T0 + 60_000L, schedule.Due(settings, T0, game: false));
        Assert.Equal(T0 + 8_000L, schedule.HeartbeatDue(T0, game: false, hasHeartbeatDevices: true));
        Assert.Null(schedule.HeartbeatDue(T0, game: false, hasHeartbeatDevices: false));

        // 游戏中心跳放慢到 30 秒。
        Assert.Equal(T0 + 30_000L, schedule.HeartbeatDue(T0, game: true, hasHeartbeatDevices: true));

        // 心跳执行一次后，下一次顺延 8 秒。
        schedule.StartHeartbeat(T0 + 8_000L);
        Assert.Null(schedule.HeartbeatDue(T0 + 8_000L, game: false, hasHeartbeatDevices: true));
        schedule.CompleteHeartbeat();
        Assert.Equal(T0 + 16_000L, schedule.HeartbeatDue(T0 + 8_000L, game: false, hasHeartbeatDevices: true));
    }

    [Fact]
    public void C2_PowerResumeTriggersImmediateRescanAndScheduleIsMonotonic()
    {
        var settings = new TraySettings { PollSeconds = 60 };
        var schedule = new PollSchedule(T0);
        Assert.True(schedule.Start(T0, game: false));
        schedule.Complete();

        // 普通轮询不强制重新枚举。
        Assert.Equal(T0 + 60_000L, schedule.Due(settings, T0 + 10_000L, game: false));
        Assert.False(schedule.Start(T0 + 60_000L, game: false));
        schedule.Complete();

        // 休眠恢复立即到期并强制重新枚举。
        schedule.NotifyPowerResume();
        long resumeTick = T0 + 65_000L;
        Assert.Equal(resumeTick, schedule.Due(settings, resumeTick, game: false));
        Assert.True(schedule.Start(resumeTick, game: false));
        schedule.Complete();
    }

    [Theory]
    [InlineData(3, (int)GameDetectionReason.D3dFullscreen, "D3D 全屏")]
    [InlineData(4, (int)GameDetectionReason.Presentation, "演示模式")]
    public void D3dAndPresentationEnableProtectionRegardlessOfSettings(int state, int reason, string label)
    {
        foreach (bool silent in new[] { false, true })
        {
            var game = GameMode.Evaluate(state, ForegroundGame.None, silent);
            Assert.True(game.Game);
            Assert.True(game.BlockPopups);
            Assert.Equal(new DiagnosticGameState(state, (GameDetectionReason)reason), game.Status);
            Assert.Equal($"通知状态={state}，依据={label}", game.Status.Describe());
        }
    }

    [Theory]
    [InlineData((int)ForegroundGame.None, false)]
    [InlineData((int)ForegroundGame.Fullscreen, true)]
    [InlineData((int)ForegroundGame.BorderlessWorkArea, false)]
    [InlineData((int)ForegroundGame.ProcessName, false)]
    public void BusyRequiresTheForegroundToCoverTheDisplay(int foreground, bool expected)
    {
        foreach (bool silent in new[] { false, true })
        {
            var game = GameMode.Evaluate(2, (ForegroundGame)foreground, silent);
            Assert.Equal(expected, game.Game);
            Assert.Equal(expected, game.BlockPopups);
            Assert.Equal(2, game.Status.NotificationState);
            Assert.Equal(expected ? GameDetectionReason.BusyFullscreen : GameDetectionReason.None, game.Status.Reason);
        }
    }

    [Theory]
    [InlineData(5, (int)ForegroundGame.None, (int)GameDetectionReason.None)]
    [InlineData(5, (int)ForegroundGame.Fullscreen, (int)GameDetectionReason.ForegroundGeometry)]
    [InlineData(5, (int)ForegroundGame.BorderlessWorkArea, (int)GameDetectionReason.ForegroundGeometry)]
    [InlineData(6, (int)ForegroundGame.ProcessName, (int)GameDetectionReason.ProcessName)]
    [InlineData(0, (int)ForegroundGame.ProcessName, (int)GameDetectionReason.ProcessName)]
    public void ForegroundGeometryAndProcessNameRequireGameSilent(int state, int foreground, int reason)
    {
        var background = GameMode.Evaluate(state, (ForegroundGame)foreground, gameSilent: false);
        var enhanced = GameMode.Evaluate(state, (ForegroundGame)foreground, gameSilent: true);
        Assert.False(background.Game);
        Assert.Equal(new DiagnosticGameState(state, GameDetectionReason.None), background.Status);
        Assert.Equal(new DiagnosticGameState(state, (GameDetectionReason)reason), enhanced.Status);
    }

    [Theory]
    [InlineData((int)ForegroundKind.Other, 1080, true, (int)ForegroundGame.Fullscreen)]
    [InlineData((int)ForegroundKind.Other, 1078, false, (int)ForegroundGame.Fullscreen)]
    [InlineData((int)ForegroundKind.Other, 1040, false, (int)ForegroundGame.BorderlessWorkArea)]
    [InlineData((int)ForegroundKind.Other, 1040, true, (int)ForegroundGame.None)]
    [InlineData((int)ForegroundKind.Other, 900, false, (int)ForegroundGame.None)]
    [InlineData((int)ForegroundKind.System, 1080, false, (int)ForegroundGame.None)]
    [InlineData((int)ForegroundKind.Own, 1080, false, (int)ForegroundGame.None)]
    public void GameWindowClassificationDistinguishesScreenAndWorkArea(int kind,
        int bottom, bool hasCaption, int expected)
    {
        var foreground = GameMode.ClassifyWindow((ForegroundKind)kind, new(-1920, 0, 0, bottom),
            new(-1920, 0, 0, 1080), new(-1920, 0, 0, 1040), hasCaption);

        Assert.Equal((ForegroundGame)expected, foreground);
        Assert.Equal((ForegroundGame)expected == ForegroundGame.Fullscreen,
            GameMode.Evaluate(2, foreground, gameSilent: false).Game);
    }

    [Fact]
    public void TargetedRefreshRunsWithoutResettingRegularFullPollSchedule()
    {
        var settings = new TraySettings { PollSeconds = 60, GamePollSeconds = 300 };
        var schedule = new PollSchedule(T0);
        schedule.Start(T0, game: false);
        schedule.Complete();

        schedule.RequestTargeted(["mouse:atk"]);
        Assert.Equal(T0 + 10_000L, schedule.Due(settings, T0 + 10_000L, game: false));
        // 游戏中不触发后台定向刷新。
        Assert.Equal(T0 + 300_000L, schedule.Due(settings, T0 + 10_000L, game: true));
        Assert.False(schedule.TryStartTargeted(settings, T0 + 10_000L, game: true, out _));

        Assert.True(schedule.TryStartTargeted(settings, T0 + 10_000L, game: false, out var ids));
        Assert.Equal(["mouse:atk"], ids!);
        Assert.True(schedule.Running);
        schedule.Complete();

        // 定向刷新不推迟原本 60 秒的全量轮询计划。
        Assert.Equal(T0 + 60_000L, schedule.Due(settings, T0 + 15_000L, game: false));

        // 到达全量轮询时刻时优先执行全量轮询而非定向刷新。
        schedule.RequestTargeted(["mouse:atk"]);
        Assert.False(schedule.TryStartTargeted(settings, T0 + 60_000L, game: false, out _));
        schedule.Start(T0 + 60_000L, game: false);
        schedule.Complete();
        Assert.Equal(T0 + 120_000L, schedule.Due(settings, T0 + 61_000L, game: false));
    }

    [Fact]
    public void BluetoothScanThrottleSkipsRepeatedEmptyScansUntilTimeoutOrInvalidation()
    {
        var throttle = new BluetoothScanThrottle();
        Assert.True(throttle.ShouldScan(T0, forceRescan: false));

        // 空结果进入 5 分钟节流窗口。
        throttle.Record(T0, deviceCount: 0, failed: false);
        Assert.False(throttle.ShouldScan(T0 + 30_000L, forceRescan: false));
        Assert.True(throttle.ShouldScan(T0 + 30_000L, forceRescan: true));
        Assert.True(throttle.ShouldScan(T0 + BluetoothScanThrottle.EmptyRescanIntervalMs, forceRescan: false));

        // 存在蓝牙设备或上次扫描失败时不节流。
        throttle.Record(T0 + 10_000L, deviceCount: 1, failed: false);
        Assert.True(throttle.ShouldScan(T0 + 20_000L, forceRescan: false));
        throttle.Record(T0 + 20_000L, deviceCount: 0, failed: true);
        Assert.True(throttle.ShouldScan(T0 + 25_000L, forceRescan: false));

        // Invalidate 立即解除节流。
        throttle.Record(T0 + 30_000L, deviceCount: 0, failed: false);
        Assert.False(throttle.ShouldScan(T0 + 40_000L, forceRescan: false));
        throttle.Invalidate();
        Assert.True(throttle.ShouldScan(T0 + 40_000L, forceRescan: false));
    }

    [Fact]
    public void ApplyPresenceDetailedSeparatesKnownWakeFromUnknownNewDeviceAndTargetedPollKeepsPeers()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        var mouse = new Jige.Core.DeviceIdentity("mouse:atk", "ATK 鼠标", Jige.Core.DeviceKind.Mouse);
        var headset = new Jige.Core.DeviceIdentity("inzone-h9:r1", "Sony INZONE H9", Jige.Core.DeviceKind.Headset);
        var model = new TrayModel(new(), new());
        model.ApplyPoll([
            new(mouse, 40, false, Jige.Core.ChargingState.NotCharging, Jige.Core.LinkState.Online, now),
            new(headset, 60, false, Jige.Core.ChargingState.NotCharging, Jige.Core.LinkState.Online, now)
        ], false, now);

        model.ApplyPresence([new("r-atk", null, mouse.Id, Jige.Core.LinkState.Asleep)], now.AddSeconds(10));
        var (uiChanged, needsFull, refreshIds) = model.ApplyPresenceDetailed(
            [new("r-atk", null, mouse.Id, Jige.Core.LinkState.Online)], now.AddSeconds(20));
        Assert.True(uiChanged);
        Assert.False(needsFull);
        Assert.Equal([mouse.Id], refreshIds);

        // 定向刷新仅更新鼠标，不会把未参与本次查询的耳机误清扫成离线。
        model.ApplyTargetedPoll([
            new(mouse, 35, false, Jige.Core.ChargingState.NotCharging, Jige.Core.LinkState.Online, now.AddSeconds(21))
        ], now.AddSeconds(21));
        var devices = model.OrderedDevices().ToDictionary(item => item.Device.Id);
        Assert.Equal(35, devices[mouse.Id].Percentage);
        Assert.Equal(Jige.Core.LinkState.Online, devices[headset.Id].Link);

        // 未登记的新设备上线时仍请求全量刷新。
        var (_, unknownFull, unknownIds) = model.ApplyPresenceDetailed(
            [new("r-new", 2, null, Jige.Core.LinkState.Online)], now.AddSeconds(30));
        Assert.True(unknownFull);
        Assert.Empty(unknownIds);
    }

    [Fact]
    public void GameModeStabilizerAbsorbsTransientExitFlickerAndExitsCleanlyAfterDebounce()
    {
        var stabilizer = new GameModeStabilizer();
        var desktop = GameMode.Evaluate(5, ForegroundGame.None, gameSilent: true);
        var busyGame = GameMode.Evaluate(2, ForegroundGame.Fullscreen, gameSilent: true);

        // 进入游戏与隐藏直读条立即生效。
        var entered = stabilizer.Update(busyGame, rawStripFullscreen: true, taskbarMode: true, nowMs: 1_000);
        Assert.True(entered.Game.Game);
        Assert.True(entered.StripFullscreen);
        Assert.Null(entered.ExitCheckDelayMs);

        // 17 ms 后瞬态闪退、再过 31 ms 回到全屏：完全吸收抖动，不退出游戏模式也不闪现直读条。
        var flickerOut = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: true, nowMs: 1_017);
        Assert.True(flickerOut.Game.Game);
        Assert.True(flickerOut.StripFullscreen);
        Assert.Equal((uint)GameModeStabilizer.ExitDebounceMs, flickerOut.ExitCheckDelayMs);

        var flickerBack = stabilizer.Update(busyGame, rawStripFullscreen: true, taskbarMode: true, nowMs: 1_048);
        Assert.True(flickerBack.Game.Game);
        Assert.True(flickerBack.StripFullscreen);
        Assert.Null(flickerBack.ExitCheckDelayMs);

        // 真正退出游戏后，持续非游戏满 400 ms 才一次性同步退出游戏模式与直读条全屏隐藏。
        var exiting = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: true, nowMs: 50_000);
        Assert.True(exiting.Game.Game);
        Assert.True(exiting.StripFullscreen);
        Assert.Equal(400u, exiting.ExitCheckDelayMs);

        var midExit = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: true, nowMs: 50_250);
        Assert.True(midExit.Game.Game);
        Assert.True(midExit.StripFullscreen);
        Assert.Equal(150u, midExit.ExitCheckDelayMs);

        var exited = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: true, nowMs: 50_400);
        Assert.False(exited.Game.Game);
        Assert.False(exited.StripFullscreen);
        Assert.Null(exited.ExitCheckDelayMs);
    }

    [Fact]
    public void GameModeStabilizerHonorsImmediateExitAndNonGameSilentFullscreenDebounce()
    {
        var stabilizer = new GameModeStabilizer();
        var desktop = GameMode.Evaluate(5, ForegroundGame.None, gameSilent: false);
        var busyGame = GameMode.Evaluate(2, ForegroundGame.Fullscreen, gameSilent: false);

        // 用户主动点击托盘或关闭游戏免打扰时，非游戏状态立即退出，不等待 400 ms 滞回。
        stabilizer.Update(busyGame, rawStripFullscreen: true, taskbarMode: true, nowMs: 1_000);
        var immediate = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: true, nowMs: 1_020, immediateExit: true);
        Assert.False(immediate.Game.Game);
        Assert.False(immediate.StripFullscreen);
        Assert.Null(immediate.ExitCheckDelayMs);

        // 关闭游戏中不打扰时，普通全屏视频的直读条隐藏同样防退出抖动；切回托盘图标模式时立即清零直读条隐藏状态。
        var videoEnter = stabilizer.Update(desktop, rawStripFullscreen: true, taskbarMode: true, nowMs: 2_000);
        Assert.False(videoEnter.Game.Game);
        Assert.True(videoEnter.StripFullscreen);
        Assert.Null(videoEnter.ExitCheckDelayMs);

        var videoFlicker = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: true, nowMs: 2_050);
        Assert.True(videoFlicker.StripFullscreen);
        Assert.Equal(400u, videoFlicker.ExitCheckDelayMs);

        var switchedToTrayIcons = stabilizer.Update(desktop, rawStripFullscreen: false, taskbarMode: false, nowMs: 2_100);
        Assert.False(switchedToTrayIcons.StripFullscreen);
        Assert.Null(switchedToTrayIcons.ExitCheckDelayMs);
    }
}

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
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void WindowsBusyD3dAndPresentationAlwaysEnableProtection(int state)
    {
        Assert.Equal(new GameMode(true, true), GameMode.Evaluate(state, false, false));
        Assert.Equal(new GameMode(true, true), GameMode.Evaluate(state, false, true));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void EnhancedProtectionExtendsOnlyToForegroundGameWindows(int state)
    {
        Assert.Equal(new GameMode(false, false), GameMode.Evaluate(state, true, false));
        Assert.Equal(new GameMode(false, false), GameMode.Evaluate(state, false, true));
        Assert.Equal(new GameMode(true, true), GameMode.Evaluate(state, true, true));
    }

    [Theory]
    [InlineData((int)ForegroundKind.Other, 1080, true, false, true)]
    [InlineData((int)ForegroundKind.Other, 1040, false, false, true)]
    [InlineData((int)ForegroundKind.Other, 1040, true, false, false)]
    [InlineData((int)ForegroundKind.Other, 900, false, false, false)]
    [InlineData((int)ForegroundKind.Other, 900, true, true, true)]
    [InlineData((int)ForegroundKind.System, 1080, false, true, false)]
    [InlineData((int)ForegroundKind.Own, 1080, false, true, false)]
    public void GameWindowClassificationHandlesBorderlessNamedGamesAndShellWindows(int kind,
        int bottom, bool hasCaption, bool knownGame, bool expected)
    {
        Assert.Equal(expected, GameMode.IsGameWindow((ForegroundKind)kind, new(-1920, 0, 0, bottom),
            new(-1920, 0, 0, 1080), new(-1920, 0, 0, 1040), hasCaption, knownGame));
    }
}

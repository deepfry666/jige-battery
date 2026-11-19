using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class PollScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GameDelaysAutomaticWorkButHonorsAnExplicitRefresh()
    {
        var settings = new TraySettings();
        var schedule = new PollSchedule(Now);
        Assert.Equal(Now.AddMinutes(5), schedule.Due(settings, Now, true));
        schedule.Request();
        Assert.Equal(Now, schedule.Due(settings, Now, true));
        schedule.Start(Now, true);
        Assert.True(schedule.StartedByUser);
        schedule.Request();
        Assert.Null(schedule.Due(settings, Now, true));
        schedule.Complete();
        Assert.Equal(Now.AddMinutes(5), schedule.Due(settings, Now, true));
        Assert.Equal(Now.AddMinutes(1), schedule.Due(settings, Now, false));
    }

    [Fact]
    public void BackgroundRetryWaitsForGameToEnd()
    {
        var settings = new TraySettings { PollSeconds = 90, GamePollSeconds = 480 };
        var schedule = new PollSchedule(Now);
        var gamePoll = Now.AddSeconds(480);
        schedule.Request(userInitiated: false);
        Assert.Equal(gamePoll, schedule.Due(settings, Now, true));
        schedule.Start(gamePoll, true);
        Assert.False(schedule.StartedByUser);
        schedule.Complete();
        Assert.Equal(Now.AddSeconds(960), schedule.Due(settings, gamePoll, true));
        Assert.Equal(gamePoll.AddSeconds(1), schedule.Due(settings, gamePoll.AddSeconds(1), false));
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

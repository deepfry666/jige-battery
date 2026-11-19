using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class PollScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ActiveGameDelaysTheFirstPollAndAnyManualRefresh()
    {
        var settings = new TraySettings();
        var schedule = new PollSchedule(Now);
        schedule.Request();
        Assert.Equal(Now.AddMinutes(5), schedule.Due(settings, Now, true));
        Assert.Equal(Now.AddSeconds(20), schedule.Due(settings, Now.AddSeconds(20), false));
        schedule.Start(Now.AddSeconds(20), false);
        schedule.Request();
        Assert.Null(schedule.Due(settings, Now.AddSeconds(20), false));
        schedule.Complete();
        Assert.Equal(Now.AddSeconds(80), schedule.Due(settings, Now.AddSeconds(21), false));
        Assert.Equal(Now.AddSeconds(320), schedule.Due(settings, Now.AddSeconds(30), true));
    }

    [Fact]
    public void AGamePollDoesNotConsumeDeferredManualRefresh()
    {
        var settings = new TraySettings { PollSeconds = 90, GamePollSeconds = 480 };
        var schedule = new PollSchedule(Now);
        var gamePoll = Now.AddSeconds(480);
        schedule.Request();
        schedule.Start(gamePoll, true);
        schedule.Complete();
        Assert.Equal(Now.AddSeconds(960), schedule.Due(settings, gamePoll, true));
        Assert.Equal(gamePoll.AddSeconds(1), schedule.Due(settings, gamePoll.AddSeconds(1), false));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void FullscreenBusyAndPresentationAlwaysProtectGame(int state, bool fullscreen)
    {
        Assert.Equal(new GameMode(true, true), GameMode.Evaluate(state, fullscreen, false));
    }

    [Fact]
    public void ConfiguredQuietTimeDefersPopupsWithoutSlowingQueries()
    {
        Assert.Equal(new GameMode(false, true), GameMode.Evaluate(6, false, true));
        Assert.Equal(new GameMode(false, false), GameMode.Evaluate(6, false, false));
        Assert.Equal(new GameMode(false, false), GameMode.Evaluate(5, false, true));
    }
}

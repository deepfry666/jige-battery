using System.Text.Json;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class AutoUpdateTests
{
    private const long Start = 1_000_000;
    private static readonly long Minute = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;
    private static readonly long Hour = (long)TimeSpan.FromHours(1).TotalMilliseconds;

    [Fact]
    public void FirstCheckWaitsThreeMinutesThenRunsDaily()
    {
        var schedule = new BackgroundUpdateSchedule(Start);
        Assert.Equal(3 * Minute, schedule.Delay(Start, enabled: true, installed: true));
        Assert.False(schedule.TryStart(Start + 3 * Minute - 1, true, true, game: false, busy: false));
        Assert.True(schedule.TryStart(Start + 3 * Minute, true, true, game: false, busy: false));

        // 运行中先按失败间隔占位，避免任务意外中断后反复立即重试。
        Assert.Equal(Start + 3 * Minute + 3 * Hour, schedule.DueMs);
        schedule.Succeeded(Start + 4 * Minute);
        Assert.Equal(Start + 4 * Minute + 24 * Hour, schedule.DueMs);
        Assert.False(schedule.TryStart(Start + 23 * Hour, true, true, false, false));
    }

    [Fact]
    public void DisabledOrPortableBuildsNeverCheckInBackground()
    {
        var schedule = new BackgroundUpdateSchedule(Start);
        long due = Start + 3 * Minute;
        Assert.Null(schedule.Delay(due, enabled: false, installed: true));
        Assert.Null(schedule.Delay(due, enabled: true, installed: false));
        Assert.False(schedule.TryStart(due, enabled: false, installed: true, game: false, busy: false));
        Assert.False(schedule.TryStart(due, enabled: true, installed: false, game: false, busy: false));
        Assert.Equal(due, schedule.DueMs);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GameOrUserUpdateDefersTenMinutes(bool game, bool busy)
    {
        var schedule = new BackgroundUpdateSchedule(Start);
        long due = Start + 3 * Minute;
        Assert.False(schedule.TryStart(due, true, true, game, busy));
        Assert.Equal(due + 10 * Minute, schedule.DueMs);
        Assert.True(schedule.TryStart(due + 10 * Minute, true, true, game: false, busy: false));
    }

    [Fact]
    public void FailureRetriesAfterThreeHoursAndEnablingChecksSoon()
    {
        var schedule = new BackgroundUpdateSchedule(Start);
        schedule.Failed(Start);
        Assert.Equal(Start + 3 * Hour, schedule.DueMs);

        schedule.Enabled(Start + Minute);
        Assert.Equal(Start + Minute + 10_000, schedule.DueMs);
        // 已经快到期时不会被推迟。
        schedule.Enabled(Start + Minute + 5_000);
        Assert.Equal(Start + Minute + 10_000, schedule.DueMs);
    }

    [Fact]
    public void AutoCheckDefaultsOnForNewAndExistingConfigs()
    {
        Assert.True(new TraySettings().AutoUpdateEnabled);
        Assert.True(new TraySettings().Normalize().AutoCheckUpdates);
        var legacy = JsonSerializer.Deserialize("""{"lowBatteryThreshold":20,"pollSeconds":60}""",
            TrayJsonContext.Default.TraySettings)!;
        Assert.True(legacy.AutoUpdateEnabled);
        Assert.True(legacy.Normalize().AutoCheckUpdates);
        Assert.Equal(20, legacy.LowBatteryThreshold);

        var off = new TraySettings { AutoCheckUpdates = false };
        string json = JsonSerializer.Serialize(off, TrayJsonContext.Default.TraySettings);
        Assert.Contains("\"autoCheckUpdates\": false", json);
        Assert.False(JsonSerializer.Deserialize(json, TrayJsonContext.Default.TraySettings)!.AutoUpdateEnabled);
        Assert.False(off.Normalize().AutoUpdateEnabled);
    }

    [Fact]
    public void ReadyStateNeedsInstalledAvailableUpdateAndSurvivesCancel()
    {
        var update = Update();
        var package = new VerifiedPackage(@"C:\Users\u\AppData\Local\Jige\updates\Jige-Setup-0.10.0-x64.exe", 3, new byte[32]);
        var found = UpdateViewState.Found("v0.9.9", isInstalled: true, update, DateTimeOffset.UnixEpoch);
        Assert.False(found.IsReady);

        var ready = found.WithReady(package);
        Assert.True(ready.IsReady);
        Assert.Equal(UpdatePhase.Available, ready.Phase);
        Assert.Same(package, ready.Ready);

        // 点「重启并升级」后进入校验，取消后回到「已就绪」。
        var verifying = ready.WithProgress(new(3, 3, Verifying: true));
        Assert.False(verifying.IsReady);
        Assert.True(verifying.BackToAvailable().IsReady);

        Assert.False((ready with { IsInstalled = false }).IsReady);
        Assert.False(UpdateViewState.Failed("v0.9.9", true, "失败", update).IsReady);
    }

    private static AvailableUpdate Update()
    {
        const string root = AvailableUpdate.Repository + "/releases/download/v0.10.0/";
        var release = new ReleaseResponse("v0.10.0", "- 更新内容", false, false,
        [
            new("Jige-Setup-0.10.0-x64.exe", 3, root + "Jige-Setup-0.10.0-x64.exe"),
            new(AvailableUpdate.ChecksumsName, 80, root + AvailableUpdate.ChecksumsName)
        ]);
        return AvailableUpdate.Select(release, new(0, 9, 9))!;
    }
}

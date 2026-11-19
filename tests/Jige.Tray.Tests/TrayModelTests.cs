using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class TrayModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity Mouse = new("ulx:1", "Finalmouse UltralightX", DeviceKind.Mouse);
    private static readonly DeviceIdentity Headset = new("inzone-h9:1", "Sony INZONE H9", DeviceKind.Headset);

    [Fact]
    public void LowEpisodeQueuesDuringGameAndResetsOnlyAboveThreshold()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(Mouse, 16)], false, Now);
        Assert.Null(model.ReadyReminder(Now, false));

        model.ApplyPoll([Reading(Mouse, 15)], false, Now);
        Assert.Null(model.ReadyReminder(Now.AddMinutes(5), true));
        Assert.Equal(Mouse, model.ReadyReminder(Now.AddMinutes(5), false)?.Device);
        model.SetReminder(Mouse.Id, null);
        model.ApplyPoll([Reading(Mouse, 9)], false, Now.AddMinutes(6));
        model.ApplyPoll([Reading(Mouse, null, LinkState.Asleep)], false, Now.AddMinutes(7));
        model.ApplyPoll([Reading(Mouse, 15)], false, Now.AddMinutes(8));
        Assert.Null(model.ReadyReminder(Now.AddMinutes(8), false));

        model.ApplyPoll([Reading(Mouse, 16)], false, Now.AddMinutes(9));
        model.ApplyPoll([Reading(Mouse, 15)], false, Now.AddMinutes(10));
        Assert.Equal(Mouse, model.ReadyReminder(Now.AddMinutes(10), false)?.Device);
    }

    [Fact]
    public void RecoveredBatteryCancelsDeferredNoticeBeforeGameEnds()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(Mouse, 10)], false, Now);
        Assert.NotNull(model.NextReminder);
        Assert.Null(model.ReadyReminder(Now, true));
        model.ApplyPoll([Reading(Mouse, 42)], false, Now.AddMinutes(5));
        Assert.Null(model.ReadyReminder(Now.AddMinutes(10), false));
        Assert.Empty(model.Snapshot().Reminders);
    }

    [Fact]
    public void PrimaryGoesFirstAndSnoozeLastsAnHourEvenAfterRestart()
    {
        var model = new TrayModel(new() { PrimaryDeviceId = "INZONE-H9:1" }, new());
        model.ApplyPoll([Reading(Mouse, 10), Reading(Headset, 14)], false, Now);
        Assert.Equal(Headset, model.ReadyReminder(Now, false)?.Device);
        model.SetReminder(Headset.Id, Now.AddHours(1));
        Assert.Equal(Mouse, model.ReadyReminder(Now, false)?.Device);
        model.SetReminder(Mouse.Id, null);

        var restarted = new TrayModel(model.Settings, model.Snapshot());
        Assert.Null(restarted.ReadyReminder(Now.AddMinutes(59), false));
        Assert.Null(restarted.ReadyReminder(Now.AddHours(1), true));
        Assert.Equal(Headset, restarted.ReadyReminder(Now.AddHours(1), false)?.Device);
        Assert.False(restarted.OrderedDevices()[0].Fresh);
    }

    [Fact]
    public void ChargeAndThresholdRulesRemainSeparate()
    {
        var model = new TrayModel(new() { LowBatteryThreshold = 22 }, new());
        model.ApplyPoll([Reading(Headset, 20, charging: ChargingState.Charging)], false, Now);
        Assert.Equal(Headset, model.ReadyReminder(Now, false)?.Device);
        Assert.Equal(BatteryTone.Charging, Presentation.Tone(model.OrderedDevices()[0]));

        model.ApplySettings(model.Settings with { LowBatteryThreshold = 18 }, Now);
        Assert.Null(model.ReadyReminder(Now, false));
        model.ApplySettings(model.Settings with { LowBatteryThreshold = 20 }, Now);
        Assert.Equal(Headset, model.ReadyReminder(Now, false)?.Device);
    }

    [Fact]
    public void OfflineBusyAndSleepKeepLatestSuccessfulEstimate()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(Mouse, 72, estimated: true), Reading(Headset, 48)], false, Now);
        var time = Now.AddMinutes(8);
        model.ApplyPoll([Reading(Mouse, null, LinkState.Asleep), Reading(Headset, null, LinkState.Busy)], false, time);
        var devices = model.OrderedDevices();
        Assert.Equal("≈72%", Presentation.Value(devices[0]));
        Assert.Equal("休眠 · 上次读数 · 8 分钟前", Presentation.Status(devices[0], time));
        Assert.Equal("接收器被占用 · 上次读数 · 8 分钟前", Presentation.Status(devices[1], time));

        model.ApplyPoll([], true, time);
        Assert.Equal(LinkState.Unknown, model.OrderedDevices()[0].Link);
        model.ApplyPoll([], false, time);
        Assert.Equal("离线 · 上次读数 · 8 分钟前", Presentation.Status(model.OrderedDevices()[0], time));
        Assert.Equal(Now, model.Snapshot().Readings[Mouse.Id].ReadAt);
    }

    [Fact]
    public void TooltipKeepsPrimaryValueAndFitsUtf16Buffer()
    {
        var settings = new TraySettings()
        {
            PrimaryDeviceId = Headset.Id,
            DeviceAliases = new()
            {
                ["INZONE-H9:1"] = string.Concat(Enumerable.Repeat("🐱", 90)),
                [Mouse.Id] = "比赛鼠标"
            }
        }.Normalize();
        var model = new TrayModel(settings, new());
        model.ApplyPoll([Reading(Mouse, 72, estimated: true), Reading(Headset, 48, charging: ChargingState.Charging)], false, Now);

        string tip = Presentation.Tooltip(model);
        string[] lines = tip.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" 48% 充电中", lines[0]);
        Assert.Equal("比赛鼠标 ≈72%", lines[1]);
        Assert.InRange(tip.Length, 1, 127);
        Assert.DoesNotContain("\uFFFD", System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(tip)));
    }

    [Fact]
    public void StateAndSettingsRoundTripThroughActualFilesAndRecoverFromBadJson()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"jige-test-{Guid.NewGuid():N}");
        try
        {
            var files = new TrayFiles(folder);
            Assert.False(files.TryLoadSettings(out var defaults));
            Assert.Equal(60, defaults.PollSeconds);
            Assert.Equal(300, defaults.GamePollSeconds);
            var settings = defaults with
            {
                PrimaryDeviceId = Mouse.Id, PollSeconds = 72, GamePollSeconds = 480, StartWithWindows = true,
                GameSilent = false, DeviceAliases = new() { ["ULX:1"] = " 我的\n 鼠标 " }
            };
            files.SaveSettings(settings);
            Assert.True(files.TryLoadSettings(out var loaded));
            Assert.Equal("我的 鼠标", loaded.DeviceAliases[Mouse.Id]);
            Assert.True(loaded.StartWithWindows);
            Assert.False(loaded.GameSilent);
            Assert.Equal(480, loaded.GamePollSeconds);

            var model = new TrayModel(loaded, files.LoadState());
            model.ApplyPoll([Reading(Mouse, 8, estimated: true)], false, Now);
            model.SetReminder(Mouse.Id, Now.AddHours(1));
            files.SaveState(model.Snapshot());
            var restored = new TrayModel(loaded, files.LoadState());
            Assert.Equal("≈8%", Presentation.Value(restored.OrderedDevices()[0]));
            Assert.Equal(Now.AddHours(1), restored.NextReminder);

            File.WriteAllText(files.StatePath + ".tmp", "{");
            Assert.Equal(Now, files.LoadState().Readings[Mouse.Id].ReadAt);
            File.WriteAllText(files.StatePath, "{");
            Assert.Empty(files.LoadState().Readings);
            File.WriteAllText(files.ConfigPath, "[]");
            Assert.False(files.TryLoadSettings(out var fallback));
            Assert.Equal(15, fallback.LowBatteryThreshold);
            Assert.Equal("[]", File.ReadAllText(files.ConfigPath));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static BatteryReading Reading(DeviceIdentity device, int? percentage, LinkState link = LinkState.Online,
        bool estimated = false, ChargingState charging = ChargingState.NotCharging) =>
        new(device, percentage, estimated, charging, link, Now);
}

using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class TrayModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity Mouse = new("ulx:1", "Finalmouse UltralightX", DeviceKind.Mouse);
    private static readonly DeviceIdentity Headset = new("inzone-h9:1", "Sony INZONE H9", DeviceKind.Headset);

    [Fact]
    public void ShowingOrBlockingAReportDoesNotAcknowledgeIt()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(Mouse, 15)], false, Now);
        Assert.Null(model.ReadyReminder(Now, false));

        model.ApplyPoll([Reading(Mouse, 14)], false, Now);
        Assert.Equal(Mouse, model.ReadyReminder(Now, false)?.Device);
        Assert.Null(model.ReadyReminder(Now.AddSeconds(10), true));
        Assert.Equal(Mouse, model.ReadyReminder(Now.AddSeconds(11), false)?.Device);
        model.SetReminder(Mouse.Id, null);
        Assert.Null(model.ReadyReminder(Now.AddSeconds(12), false));

        model.ApplyPoll([Reading(Mouse, 15)], false, Now);
        model.ApplyPoll([Reading(Mouse, 14)], false, Now);
        Assert.Equal(Mouse, model.ReadyReminder(Now, false)?.Device);
    }

    [Fact]
    public void ReminderWaitsForFreshDataAfterSleepDisconnectionAndLongGame()
    {
        var model = new TrayModel(new() { PollSeconds = 30 }, new());
        model.ApplyPoll([Reading(Mouse, 10)], false, Now);
        Assert.NotNull(model.NextReminder(Now));
        Assert.Null(model.ReadyReminder(Now, true));
        Assert.Equal(Mouse, model.ReadyReminder(Now.AddSeconds(60), false)?.Device);
        Assert.Null(model.ReadyReminder(Now.AddSeconds(61), false));
        Assert.Null(model.NextReminder(Now.AddSeconds(61)));
        Assert.True(model.ExpireReadings(Now.AddSeconds(61)));
        Assert.Equal(BatteryTone.Idle, Presentation.Tone(model.PrimaryDevice, 15));

        foreach (var link in new[] { LinkState.Asleep, LinkState.Offline })
        {
            model.ApplyPoll([Reading(Mouse, null, link)], false, Now.AddMinutes(5));
            Assert.Null(model.ReadyReminder(Now.AddMinutes(5), false));
        }

        var resumed = Now.AddMinutes(6);
        model.ApplyPoll([Reading(Mouse, 10) with { ReadAt = resumed }], false, resumed);
        Assert.Equal(Mouse, model.ReadyReminder(resumed, false)?.Device);
        model.ApplyPoll([Reading(Mouse, 42) with { ReadAt = resumed }], false, resumed);
        Assert.Null(model.ReadyReminder(resumed, false));
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
        Assert.Null(restarted.ReadyReminder(Now.AddHours(1), false));
        Assert.False(restarted.OrderedDevices()[0].Fresh);
        var current = Now.AddMinutes(59);
        restarted.ApplyPoll([Reading(Mouse, 10) with { ReadAt = current },
            Reading(Headset, 14) with { ReadAt = current }], false, current);
        Assert.Null(restarted.ReadyReminder(current, false));
        Assert.Null(restarted.ReadyReminder(Now.AddHours(1), true));
        Assert.Equal(Headset, restarted.ReadyReminder(Now.AddHours(1), false)?.Device);
    }

    [Fact]
    public void ChargingAndProtocolReportedFullCancelAnUnconfirmedWarning()
    {
        var model = new TrayModel(new() { LowBatteryThreshold = 22 }, new());
        model.ApplyPoll([Reading(Headset, 20)], false, Now);
        Assert.Equal(Headset, model.ReadyReminder(Now, false)?.Device);
        model.ApplyPoll([Reading(Headset, 20, charging: ChargingState.Charging)], false, Now);
        Assert.Null(model.ReadyReminder(Now, false));
        Assert.Equal("充电中", Presentation.Status(model.OrderedDevices()[0], Now, 22));
        model.ApplyPoll([Reading(Headset, 20, charging: ChargingState.Full)], false, Now);
        Assert.Null(model.ReadyReminder(Now, false));
        Assert.Equal("已充满", Presentation.Status(model.OrderedDevices()[0], Now, 22));
        Assert.False(Presentation.Icon(model.OrderedDevices()[0], false, 22).Underline);

        model.ApplyPoll([Reading(Headset, 20)], false, Now);
        model.ApplySettings(model.Settings with { LowBatteryThreshold = 20 }, Now);
        Assert.Null(model.ReadyReminder(Now, false));
        model.ApplySettings(model.Settings with { LowBatteryThreshold = 21 }, Now);
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
        Assert.Equal("休眠 · 上次读数 · 8 分钟前", Presentation.Status(devices[0], time, 15));
        Assert.Equal("接收器被占用 · 上次读数 · 8 分钟前", Presentation.Status(devices[1], time, 15));

        model.ApplyPoll([], true, time);
        Assert.Equal(LinkState.Unknown, model.OrderedDevices()[0].Link);
        model.ApplyPoll([], false, time);
        Assert.Equal("离线 · 上次读数 · 8 分钟前", Presentation.Status(model.OrderedDevices()[0], time, 15));
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
        Assert.Equal("另有 1 台", lines[1]);
        Assert.InRange(tip.Length, 1, 127);
        Assert.DoesNotContain("\uFFFD", System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(tip)));
    }

    [Fact]
    public void LegacyConfigAndPanelChangesSurviveRestartWithHiddenTooltipEntries()
    {
        var directory = Directory.CreateTempSubdirectory("jige-panel-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            File.WriteAllText(files.ConfigPath, """
                {
                  "primaryDeviceId": "ulx:1",
                  "lowBatteryThreshold": 22,
                  "pollSeconds": 72,
                  "gamePollSeconds": 480,
                  "gameSilent": true,
                  "startWithWindows": false,
                  "deviceAliases": { "ULX:1": "旧配置鼠标" }
                }
                """);

            Assert.True(files.TryLoadSettings(out var loaded));
            Assert.Empty(loaded.HiddenDeviceIds);
            Assert.Null(loaded.WelcomeShown);
            Assert.Equal(22, loaded.LowBatteryThreshold);
            Assert.Equal(72, loaded.PollSeconds);
            var model = new TrayModel(loaded, new());
            model.ApplyPoll([Reading(Mouse, 72), Reading(Headset, 48)], false, Now);
            Assert.Equal("旧配置鼠标", model.Name(Mouse));
            Assert.Same(loaded, model.ToggleVisibility("ULX:1"));

            var changed = model.ToggleVisibility("INZONE-H9:1").WithAlias(Headset.Id, "  我的　耳机 🐱 ");
            changed = changed with
            {
                LowBatteryThreshold = 30, PollSeconds = 120, GameSilent = false, StartWithWindows = true
            };
            files.SaveSettings(changed);
            files.SaveState(model.Snapshot());

            Assert.True(files.TryLoadSettings(out var saved));
            var restarted = new TrayModel(saved, files.LoadState());
            Assert.Equal(30, saved.LowBatteryThreshold);
            Assert.Equal(120, saved.PollSeconds);
            Assert.Equal(480, saved.GamePollSeconds);
            Assert.False(saved.GameSilent);
            Assert.True(saved.StartWithWindows);
            Assert.Equal(new[] { "INZONE-H9:1" }, saved.HiddenDeviceIds);
            Assert.Equal("我的 耳机 🐱", restarted.Name(Headset));
            Assert.Equal(Mouse, Assert.Single(restarted.PanelDevices()).Device);
            Assert.Contains("我的 耳机 🐱 48%", Presentation.Tooltip(restarted));

            var cleared = saved.WithAlias(Headset.Id, "   ");
            restarted.ApplySettings(cleared, Now);
            Assert.Equal("INZONE H9", restarted.Name(Headset));
            files.SaveSettings(cleared);
            Assert.True(files.TryLoadSettings(out var reopened));
            Assert.False(reopened.DeviceAliases.ContainsKey(Headset.Id));

            var shown = restarted.ToggleVisibility(Headset.Id);
            Assert.Empty(shown.HiddenDeviceIds);
            restarted.ApplySettings(saved.WithPrimary(Headset.Id), Now);
            Assert.Empty(restarted.Settings.HiddenDeviceIds);
            Assert.Equal(Headset, restarted.PrimaryDevice?.Device);
            Assert.Equal(new[] { Mouse, Headset },
                restarted.OrderedDevices(primaryFirst: false).Select(device => device.Device));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ConfigNormalizationRemovesDuplicateHiddenIdsAndKeepsThePinnedDeviceVisible()
    {
        var settings = new TraySettings
        {
            PrimaryDeviceId = "ULX:1", HiddenDeviceIds = ["ulx:1", "MOUSE:2", "mouse:2", ""]
        }.Normalize();
        Assert.Equal(new[] { "MOUSE:2" }, settings.HiddenDeviceIds);
    }

    [Fact]
    public void AutomaticPrimarySkipsHiddenDevicesAndCannotBeHidden()
    {
        var model = new TrayModel(new() { HiddenDeviceIds = [Mouse.Id] }, new());
        model.ApplyPoll([Reading(Mouse, 10), Reading(Headset, 50)], false, Now);
        Assert.Equal(Headset, model.PrimaryDevice?.Device);
        Assert.Same(model.Settings, model.ToggleVisibility(Headset.Id));
        Assert.Equal(Mouse, model.ReadyReminder(Now, false)?.Device);
        model.ApplySettings(model.Settings.WithPrimary(Mouse.Id), Now);
        Assert.False(model.IsHidden(Mouse.Id));
        Assert.Equal(Mouse, model.PrimaryDevice?.Device);
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
            Assert.True(defaults.StartWithWindows);
            Assert.False(defaults.WelcomeShown);
            var settings = defaults with
            {
                PrimaryDeviceId = Mouse.Id, PollSeconds = 72, GamePollSeconds = 480, StartWithWindows = true,
                GameSilent = false, WelcomeShown = true,
                GameProcessNames = ["Arena.exe", " arena ", "C:/Hidden.exe"],
                DeviceAliases = new() { ["ULX:1"] = " 我的\n 鼠标 " }
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
            Assert.Equal(Now.AddHours(1), restored.Snapshot().Reminders[Mouse.Id].Due);

            File.WriteAllText(files.StatePath + ".tmp", "{");
            Assert.Equal(Now, files.LoadState().Readings[Mouse.Id].ReadAt);
            File.WriteAllText(files.StatePath, "{");
            Assert.Empty(files.LoadState().Readings);
            File.WriteAllText(files.ConfigPath, "[]");
            List<string> entries = [];
            Assert.False(files.TryLoadSettings(out var fallback, (message, error) => entries.Add(TrayLog.Describe(message, error))));
            Assert.Equal(15, fallback.LowBatteryThreshold);
            Assert.Null(fallback.WelcomeShown);
            Assert.False(File.Exists(files.ConfigPath));
            Assert.Equal("[]", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "config.broken-*.json"))));
            Assert.Contains("JsonException", Assert.Single(entries));
            File.WriteAllText(files.ConfigPath, "{");
            Assert.False(files.TryLoadSettings(out fallback));
            Assert.Equal("{", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "config.broken-*.json"))));
            files.SaveSettings(fallback);
            Assert.True(files.TryLoadSettings(out _));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TooltipKeepsPrimaryThenVisibleLowDevicesAndCountsOmittedRows()
    {
        var list = Enumerable.Range(0, 20).Select(i => new DeviceIdentity($"dev:{i:D2}",
            $"设备 {i:D2} 的名称", DeviceKind.Mouse)).ToArray();
        var model = new TrayModel(new TraySettings { PrimaryDeviceId = list[5].Id, HiddenDeviceIds = [list[0].Id] }, new());
        model.ApplyPoll(list.Select((device, i) => Reading(device, i + 1)).ToArray(), false, Now);
        var lines = Presentation.Tooltip(model).Split('\n');
        Assert.StartsWith("设备 05 的名称 6%", lines[0]);
        Assert.StartsWith("设备 01 的名称 2%", lines[1]);
        Assert.StartsWith("设备 02 的名称 3%", lines[2]);
        Assert.DoesNotContain(lines, line => line.StartsWith("设备 00", StringComparison.Ordinal));
        Assert.Equal($"另有 {list.Length - lines.Length + 1} 台", lines[^1]);
        Assert.True(Presentation.Tooltip(model).Length <= 127);
    }

    [Fact]
    public void RemovingOfflineDeviceClearsSavedSettingsAndReadingUntilDetectedAgain()
    {
        var settings = new TraySettings { PrimaryDeviceId = Mouse.Id }.WithAlias(Mouse.Id, "旧鼠标");
        var model = new TrayModel(settings, new());
        model.ApplyPoll([Reading(Mouse, 10)], false, Now);
        model.RemoveOffline(Mouse.Id);
        Assert.NotNull(model.PrimaryDevice);
        model.ApplyPoll([], false, Now);
        Assert.True(model.CanRemove(Mouse.Id));
        model.ApplySettings(model.Settings.WithoutDevice(Mouse.Id), Now);
        model.RemoveOffline(Mouse.Id);
        var restored = new TrayModel(model.Settings, model.Snapshot());
        Assert.Empty(restored.OrderedDevices());
        Assert.Empty(restored.Snapshot().Reminders);
        Assert.Empty(restored.Settings.DeviceAliases);
        Assert.Null(restored.Settings.PrimaryDeviceId);
        restored.ApplyPoll([Reading(Mouse, null, LinkState.Offline)], false, Now);
        Assert.Empty(restored.OrderedDevices());
        restored.ApplyPoll([Reading(Mouse, 56)], false, Now);
        Assert.Equal("Finalmouse ULX", restored.Name(restored.PrimaryDevice!.Device));
    }

    [Fact]
    public void LogitechSlotReuseDoesNotInheritExistingAlias()
    {
        var old = new DeviceIdentity("logitech-hidpp:r/1", "旧设备", DeviceKind.Mouse);
        var current = old with { Id = "logitech-hidpp:r/1/unit-9876", Name = "新设备" };
        var model = new TrayModel(new TraySettings().WithAlias(old.Id, "比赛鼠标"), new());
        model.ApplyPoll([Reading(old, 30)], false, Now);
        model.ApplyPoll([Reading(current, 80)], false, Now);
        Assert.Equal("比赛鼠标", model.Name(old));
        Assert.Equal("新设备", model.Name(current));
    }

    private static BatteryReading Reading(DeviceIdentity device, int? percentage, LinkState link = LinkState.Online,
        bool estimated = false, ChargingState charging = ChargingState.NotCharging) =>
        new(device, percentage, estimated, charging, link, Now);
}

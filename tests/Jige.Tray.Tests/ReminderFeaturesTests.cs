using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class ReminderFeaturesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity Mouse = new("mouse:1", "测试鼠标", DeviceKind.Mouse);
    private static readonly DeviceIdentity Keyboard = new("keyboard:1", "测试键盘", DeviceKind.Keyboard);

    private static TrayModel NewModel(TraySettings? settings = null) =>
        new((settings ?? new()) with { PollSeconds = 60 }, new()) { TimeZone = TimeZoneInfo.Utc };

    private static void Feed(TrayModel model, DeviceIdentity device, int percent, double hours,
        ChargingState charging = ChargingState.NotCharging)
    {
        var at = T0.AddHours(hours);
        model.ApplyPoll([new(device, percent, false, charging, LinkState.Online, at)], false, at);
    }

    private static void FeedBoth(TrayModel model, int mouse, int keyboard, double hours)
    {
        var at = T0.AddHours(hours);
        model.ApplyPoll([
            new(Mouse, mouse, false, ChargingState.NotCharging, LinkState.Online, at),
            new(Keyboard, keyboard, false, ChargingState.NotCharging, LinkState.Online, at)
        ], false, at);
    }

    private static double Discharge(TrayModel model, DeviceIdentity device, int from, int to, double start, double hoursPerPercent)
    {
        double t = start, end = start + (from - to) * hoursPerPercent;
        while (t <= end + 1e-9)
        {
            int percent = from - (int)Math.Floor((t - start) / hoursPerPercent + 1e-9);
            Feed(model, device, Math.Max(to, percent), t);
            t = Math.Round(t + .1, 6);
        }
        Feed(model, device, to, end);
        return end;
    }

    private static DeviceStatus Status(TrayModel model, string id) =>
        model.OrderedDevices().Single(item => item.Device.Id == id);

    [Fact]
    public void ReminderKeysRoundTrip()
    {
        Assert.Equal((ReminderKind.LowBattery, "logitech-hidpp:a/1"), ReminderKeys.Parse("logitech-hidpp:a/1"));
        string predicted = ReminderKeys.For(ReminderKind.Predicted, Mouse.Id);
        Assert.Equal((ReminderKind.Predicted, Mouse.Id), ReminderKeys.Parse(predicted));
        Assert.Equal((ReminderKind.FullCharge, Mouse.Id), ReminderKeys.Parse(ReminderKeys.For(ReminderKind.FullCharge, Mouse.Id)));
    }

    [Fact]
    public void PerDeviceThresholdDrivesToneAndLowBatteryReminder()
    {
        var model = NewModel(new() { DeviceThresholds = new() { [Mouse.Id] = 5 } });
        FeedBoth(model, 8, 10, 0);

        Assert.Equal(5, model.ThresholdFor(Mouse.Id));
        Assert.Equal(15, model.ThresholdFor(Keyboard.Id));
        Assert.Equal(BatteryTone.Warning, Presentation.Tone(Status(model, Mouse.Id), model.ThresholdFor(Mouse.Id)));
        Assert.Equal(BatteryTone.Low, Presentation.Tone(Status(model, Keyboard.Id), model.ThresholdFor(Keyboard.Id)));
        Assert.Equal(Keyboard.Id, model.ReadyReminderKey(T0, false));

        model.SetReminder(Keyboard.Id, null);
        Assert.Null(model.ReadyReminderKey(T0, false));
        FeedBoth(model, 5, 10, .1);
        Assert.Equal(Mouse.Id, model.ReadyReminderKey(T0.AddHours(.1), false));
    }

    [Fact]
    public void DeviceThresholdCyclesAndNormalizes()
    {
        var settings = new TraySettings();
        int?[] expected = [5, 10, 15, 20, 30, null];
        foreach (int? value in expected)
        {
            settings = settings.CycleDeviceThreshold(Mouse.Id);
            Assert.Equal(value, settings.DeviceThresholds.TryGetValue(Mouse.Id, out int v) ? v : null);
        }

        var normalized = (settings with { DeviceThresholds = new() { [Mouse.Id] = 7, [" "] = 10, [Keyboard.Id] = 20 } }).Normalize();
        Assert.Equal(new Dictionary<string, int> { [Keyboard.Id] = 20 }, normalized.DeviceThresholds);
        Assert.Equal(20, normalized.ThresholdFor(Keyboard.Id.ToUpperInvariant()));
        Assert.Empty(normalized.WithoutDevice(Keyboard.Id).DeviceThresholds);
    }

    [Fact]
    public void NewSettingsRoundTripAndOldConfigKeepsGameReminderOn()
    {
        var directory = Directory.CreateTempSubdirectory("jige-reminder-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            File.WriteAllText(files.ConfigPath, "{\"lowBatteryThreshold\": 15}");
            Assert.True(files.TryLoadSettings(out var old));
            Assert.True(old.GameEntryReminderEnabled);
            Assert.False(old.FullChargeReminder);
            Assert.Empty(old.DeviceThresholds);

            files.SaveSettings((old with
            {
                GameEntryReminder = false,
                FullChargeReminder = true,
                DeviceThresholds = new() { [Mouse.Id] = 10 }
            }).Normalize());
            Assert.True(files.TryLoadSettings(out var loaded));
            Assert.False(loaded.GameEntryReminderEnabled);
            Assert.True(loaded.FullChargeReminder);
            Assert.Equal(10, loaded.ThresholdFor(Mouse.Id));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void FullChargeReminderIsOptInAndClearsWhenUnplugged()
    {
        var off = NewModel();
        Feed(off, Mouse, 100, 0, ChargingState.Full);
        Assert.Null(off.ReadyReminderKey(T0, false));

        var model = NewModel(new() { FullChargeReminder = true });
        Feed(model, Mouse, 99, 0, ChargingState.Charging);
        Assert.Null(model.ReadyReminderKey(T0, false));
        Feed(model, Mouse, 100, .1, ChargingState.Charging);
        string fullKey = ReminderKeys.For(ReminderKind.FullCharge, Mouse.Id);
        Assert.Equal(fullKey, model.ReadyReminderKey(T0.AddHours(.1), false));
        Assert.True(model.ReminderIsDue(fullKey, T0.AddHours(.1)));

        Feed(model, Mouse, 100, .2);
        Assert.Null(model.ReadyReminderKey(T0.AddHours(.2), false));

        Feed(model, Mouse, 100, .3, ChargingState.Full);
        Assert.Equal(fullKey, model.ReadyReminderKey(T0.AddHours(.3), false));
        model.ApplySettings(model.Settings with { FullChargeReminder = false }, T0.AddHours(.3));
        Assert.Null(model.ReadyReminderKey(T0.AddHours(.3), false));
    }

    /// <summary>每天在线 5 小时、每小时掉 4%，攒够 3 个活跃日后电量降到 19%：剩余 4.75 小时不够一天。</summary>
    private static (TrayModel Model, double End) HeavyUser(bool runtime = true)
    {
        var model = NewModel(new() { ShowRuntimeEstimate = runtime });
        double end = 0;
        for (int day = 0; day < 4; day++)
            end = Discharge(model, Mouse, 100 - day * 20, day == 3 ? 19 : 80 - day * 20, day * 24, .25);
        return (model, end);
    }

    [Fact]
    public void PredictedShortfallRemindsBeforeTheLowBatteryThreshold()
    {
        var (model, end) = HeavyUser();
        var now = T0.AddHours(end);
        var device = Status(model, Mouse.Id);
        Assert.Equal(19, device.Percentage);
        Assert.True(model.PredictsShortfall(device, now));
        string key = ReminderKeys.For(ReminderKind.Predicted, Mouse.Id);
        Assert.Equal(key, model.ReadyReminderKey(now, false));

        var (title, detail) = Presentation.Reminder(ReminderKind.Predicted, device, "测试鼠标", model.Runtime(device, now));
        Assert.Equal("鼠标按你的用法撑不过今天", title);
        Assert.StartsWith("测试鼠标 预计可用约", detail);

        // 「知道了」之后本轮不再提醒；充电后清除，下一轮重新计算。
        model.SetReminder(key, null);
        Assert.Null(model.ReadyReminderKey(now, false));
        Feed(model, Mouse, 60, end + .1, ChargingState.Charging);
        Assert.False(model.ReminderIsDue(key, now.AddHours(.1)));
        Assert.Null(model.ReadyReminderKey(now.AddHours(.1), false));
    }

    [Fact]
    public void PredictedReminderNeedsRuntimeEstimateEnabled()
    {
        var (model, end) = HeavyUser(runtime: false);
        Assert.False(model.PredictsShortfall(Status(model, Mouse.Id), T0.AddHours(end)));
        Assert.Null(model.ReadyReminderKey(T0.AddHours(end), false));
    }

    [Fact]
    public void GameEntryWarningFiresOncePerDischargeAndRespectsTheSwitch()
    {
        var model = NewModel();
        FeedBoth(model, 10, 60, 0);
        var first = model.TakeGameEntryWarnings(T0);
        Assert.Equal([Mouse.Id], first.Select(item => item.Device.Id));
        Assert.Empty(model.TakeGameEntryWarnings(T0.AddMinutes(1)));

        var (title, detail) = Presentation.GameEntryBanner(first[0], "测试鼠标", RuntimeEstimate.None, 0);
        Assert.Equal("测试鼠标 只剩 10%", title);
        Assert.Equal("建议先插线再开局", detail);

        // 充过电、开始新一轮放电后再次低电量，会重新提醒。
        Feed(model, Mouse, 50, .1, ChargingState.Charging);
        Feed(model, Mouse, 50, .2);
        Feed(model, Mouse, 9, .3);
        Assert.Single(model.TakeGameEntryWarnings(T0.AddHours(.3)));

        var off = NewModel(new() { GameEntryReminder = false });
        Feed(off, Mouse, 10, 0);
        Assert.Empty(off.TakeGameEntryWarnings(T0));

        var hidden = NewModel(new() { HiddenDeviceIds = [Mouse.Id] });
        Feed(hidden, Mouse, 10, 0);
        Assert.Empty(hidden.TakeGameEntryWarnings(T0));
    }

    [Fact]
    public void GameEntryBannerMentionsRuntimeAndOtherLowDevices()
    {
        var device = new DeviceStatus(Mouse, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new(Mouse, 12, false, T0), true);
        var (_, detail) = Presentation.GameEntryBanner(device, "测试鼠标", new(RuntimeKind.Ready, .66), 1);
        // 横幅只有约 246px：有其他低电量设备时省掉「建议先插线」，两行都不截断。
        Assert.Equal("预计可用约 40 分钟 · 另有 1 台电量低", detail);
        Assert.Equal("预计可用约 40 分钟，建议先插线",
            Presentation.GameEntryBanner(device, "测试鼠标", new(RuntimeKind.Ready, .66), 0).Detail);
        Assert.Equal((" 只剩 12%", "建议先插线再开局 · 另有 2 台电量低"),
            Presentation.GameEntryBannerParts(device, RuntimeEstimate.None, 2));
    }

    [Fact]
    public void NoticeSuffixKeepsTheKeyInformationSeparateFromTheName()
    {
        var device = new DeviceStatus(Mouse, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new(Mouse, 30, false, T0), true);
        Assert.Equal(("鼠标按你的用法撑不过今天", " 预计可用约 40 分钟"),
            Presentation.ReminderParts(ReminderKind.Predicted, device, new(RuntimeKind.Ready, .66)));
        Assert.Equal(" 建议找空充电", Presentation.ReminderParts(ReminderKind.Predicted, device, RuntimeEstimate.Estimating).Suffix);
        Assert.Equal(" 只剩 30%", Presentation.ReminderParts(ReminderKind.LowBattery, device, RuntimeEstimate.None).Suffix);
    }

    [Fact]
    public void HoverCardShowsTimeSinceLastCharge()
    {
        var model = NewModel();
        Feed(model, Mouse, 100, 0, ChargingState.Full);
        Assert.Equal("已充满 · 刚刚读取", HoverCardBuilder.Build(model, Mouse.Id, T0).StatusLine);

        Feed(model, Mouse, 80, 72);
        var card = HoverCardBuilder.Build(model, Mouse.Id, T0.AddHours(72));
        Assert.Equal("上次充电 3 天前", card.StatusLine);

        // 休眠时优先显示「休眠中 · N 分钟前」，不再挤进「上次充电」。
        var asleepAt = T0.AddHours(72.5);
        model.ApplyPoll([new(Mouse, null, false, ChargingState.Unknown, LinkState.Asleep, asleepAt)], false, asleepAt);
        Assert.DoesNotContain("上次充电", HoverCardBuilder.Build(model, Mouse.Id, asleepAt).StatusLine);
        Assert.Equal(T0, model.LastCharged(Mouse.Id));

        Assert.Equal("刚充过电", RuntimeEstimator.ChargedAgo(T0, T0.AddMinutes(20)));
        Assert.Equal("上次充电 5 小时前", RuntimeEstimator.ChargedAgo(T0, T0.AddHours(5.5)));
    }

    [Fact]
    public void FullChargeNoticeTexts()
    {
        var device = new DeviceStatus(Keyboard, LinkState.Online, ReadIssue.None, ChargingState.Full,
            new(Keyboard, 100, false, T0), true);
        Assert.Equal(("键盘已充满", "测试键盘 可以拔线了"),
            Presentation.Reminder(ReminderKind.FullCharge, device, "测试键盘", RuntimeEstimate.None));
    }
}

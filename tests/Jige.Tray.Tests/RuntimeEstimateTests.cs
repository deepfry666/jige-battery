using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class RuntimeEstimateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity Mouse = new("mouse:fine", "测试鼠标", DeviceKind.Mouse);
    private static readonly DeviceIdentity Headset = new("razer:coarse", "粗档耳机", DeviceKind.Headset);
    private static readonly DeviceIdentity Ulx = new("ulx:1", "Finalmouse ULX", DeviceKind.Mouse);
    private const double Poll = .1; // 6 分钟一次，低于 20 分钟的连续计时上限。

    private static TrayModel NewModel(bool enabled = true) =>
        new(new() { ShowRuntimeEstimate = enabled, PollSeconds = 60 }, new()) { TimeZone = TimeZoneInfo.Utc };

    private static bool Feed(TrayModel model, DeviceIdentity device, int percent, double hours,
        bool estimated = false, ChargingState charging = ChargingState.NotCharging, LinkState link = LinkState.Online)
    {
        var at = T0.AddHours(hours);
        return model.ApplyPoll([new(device, link == LinkState.Online ? percent : null, estimated, charging, link, at)], false, at);
    }

    /// <summary>以固定间隔轮询，从 from 掉到 to，每掉 1% 用 hoursPerPercent 在线小时。返回结束时刻。</summary>
    private static double Discharge(TrayModel model, DeviceIdentity device, int from, int to, double start,
        double hoursPerPercent)
    {
        double t = start;
        double end = start + (from - to) * hoursPerPercent;
        while (t <= end + 1e-9)
        {
            int percent = from - (int)Math.Floor((t - start) / hoursPerPercent + 1e-9);
            Feed(model, device, Math.Max(to, percent), t);
            t = Math.Round(t + Poll, 6);
        }
        return end;
    }

    private static DeviceStatus Status(TrayModel model, string id) =>
        model.OrderedDevices().Single(item => item.Device.Id == id);

    [Fact]
    public void NewDeviceShowsEstimatingWhenEnabledAndNothingWhenDisabled()
    {
        var model = NewModel();
        Feed(model, Mouse, 80, 0);
        Assert.Equal(RuntimeKind.Estimating, model.Runtime(Status(model, Mouse.Id), T0).Kind);

        var disabled = NewModel(enabled: false);
        Feed(disabled, Mouse, 80, 0);
        Assert.Equal(RuntimeKind.None, disabled.Runtime(Status(disabled, Mouse.Id), T0).Kind);
        // 关闭时后台照常学习，打开后马上能用。
        Assert.NotNull(disabled.RuntimeState(Mouse.Id)?.Segment);
    }

    [Fact]
    public void FineDeviceUsesEdgeToEdgeRateAndIgnoresTimeParkedAtFullCharge()
    {
        var model = NewModel();
        // 先在 100% 停 2 小时：起点所在台阶的停留时间不应计入速率。
        for (double t = 0; t < 2; t = Math.Round(t + Poll, 6))
            Feed(model, Mouse, 100, t);
        double end = Discharge(model, Mouse, 100, 90, 2, .7);

        var state = model.RuntimeState(Mouse.Id)!;
        Assert.Equal(1, state.MinStep);
        var (percent, hours) = (state.LearnedPercent + state.Segment!.ChunkPercent, state.LearnedHours + state.Segment.ChunkHours);
        Assert.Equal(1 / .7, percent / hours, 3);

        var raw = RuntimeEstimator.Estimate(state, Status(model, Mouse.Id), T0.AddHours(end));
        Assert.Equal(RuntimeKind.Ready, raw.Kind);
        Assert.Equal(63, raw.Hours, 1);
        var shown = model.Runtime(Status(model, Mouse.Id), T0.AddHours(end));
        Assert.Equal(65, shown.Hours);
        Assert.Equal("约 65 小时", RuntimeEstimator.Format(shown.Hours));
    }

    [Fact]
    public void SingleEdgeOrTooLittleEvidenceStaysEstimating()
    {
        var model = NewModel();
        Feed(model, Mouse, 80, 0);
        Feed(model, Mouse, 79, .1);
        Feed(model, Mouse, 78, .2);
        // 只有 1% 证据、0.1 小时，不足 3% 与 0.5 小时门槛。
        Assert.Equal(RuntimeKind.Estimating, model.Runtime(Status(model, Mouse.Id), T0.AddHours(.2)).Kind);
    }

    [Fact]
    public void SleepTimeIsNotCountedAsUsage()
    {
        var model = NewModel();
        double end = Discharge(model, Mouse, 90, 80, 0, .5);
        double before = model.RuntimeState(Mouse.Id)!.Segment!.Hours;

        Feed(model, Mouse, 80, end + .05, link: LinkState.Asleep);
        Feed(model, Mouse, 80, end + 8);
        Assert.Equal(before, model.RuntimeState(Mouse.Id)!.Segment!.Hours, 6);
    }

    [Fact]
    public void DropAcrossOfflineGapRebuildsBaselineWithoutPollutingRate()
    {
        var model = NewModel();
        double end = Discharge(model, Mouse, 90, 80, 0, .5);
        var learned = model.RuntimeState(Mouse.Id)!;

        Feed(model, Mouse, 80, end + .05, link: LinkState.Offline);
        Feed(model, Mouse, 75, end + 10);
        var after = model.RuntimeState(Mouse.Id)!;
        Assert.Equal(learned.LearnedPercent, after.LearnedPercent);
        Assert.Equal(learned.LearnedHours, after.LearnedHours);
        Assert.Null(after.Segment!.EdgePercent);
        Assert.Equal(75, after.Segment.Level);
    }

    [Fact]
    public void ShortSleepBetweenDropsKeepsTheEdgeAnchor()
    {
        var model = NewModel();
        Discharge(model, Mouse, 90, 85, 0, .5);
        Feed(model, Mouse, 85, 2.6, link: LinkState.Asleep);
        Feed(model, Mouse, 84, 2.7);
        Assert.Equal(84, model.RuntimeState(Mouse.Id)!.Segment!.EdgePercent);
    }

    [Fact]
    public void ChargingEndsTheSegmentAndUnknownChargingRiseStartsANewOne()
    {
        var model = NewModel();
        Discharge(model, Mouse, 60, 40, 0, .5);
        Feed(model, Mouse, 41, 10.1, charging: ChargingState.Charging);
        Assert.Null(model.RuntimeState(Mouse.Id)!.Segment);
        // 充电中不再显示剩余可用时间，改为充满时间（首次充电先显示估算中）。
        Assert.Equal(RuntimeEstimate.ChargeEstimating, model.Runtime(Status(model, Mouse.Id), T0.AddHours(10.1)));

        var unknown = NewModel();
        Discharge(unknown, Mouse, 60, 40, 0, .5);
        Feed(unknown, Mouse, 60, 10.1, charging: ChargingState.Unknown);
        var segment = unknown.RuntimeState(Mouse.Id)!.Segment!;
        Assert.Equal(60, segment.Level);
        Assert.Null(segment.EdgePercent);
        Assert.True(unknown.RuntimeState(Mouse.Id)!.LearnedPercent > 0);
    }

    [Fact]
    public void OnePointBounceIsIgnoredButASustainedRiseRestarts()
    {
        var model = NewModel();
        Discharge(model, Mouse, 70, 60, 0, .5);
        Feed(model, Mouse, 61, 5.1);
        Feed(model, Mouse, 60, 5.2);
        Assert.Equal(60, model.RuntimeState(Mouse.Id)!.Segment!.EdgePercent);

        Feed(model, Mouse, 61, 5.3);
        Feed(model, Mouse, 61, 5.4);
        var segment = model.RuntimeState(Mouse.Id)!.Segment!;
        Assert.Equal(61, segment.Level);
        Assert.Null(segment.EdgePercent);
    }

    [Fact]
    public void CoarseLevelsUseEdgesAndCountDownInsideTheCurrentLevel()
    {
        var model = NewModel();
        // 开机在 80 档，过很久才掉到 50（左端没有锚点，不计入速率）。
        for (double t = 0; t <= 3; t = Math.Round(t + Poll, 6))
            Feed(model, Headset, t < 3 ? 80 : 50, t);
        // 50 → 30 用了 10 小时：速率 2%/h。
        for (double t = 3.1; t <= 13; t = Math.Round(t + Poll, 6))
            Feed(model, Headset, t < 13 - 1e-9 ? 50 : 30, t);

        var state = model.RuntimeState(Headset.Id)!;
        Assert.True(RuntimeEstimator.IsCoarse(state));
        Assert.Equal(20, state.MinStep);
        Assert.Equal(20, state.LearnedPercent, 6);
        Assert.Equal(10, state.LearnedHours, 1);

        // 在 30 档又在线 5 小时：推算真实电量约 20%，剩余约 10 小时。
        for (double t = 13.1; t <= 18 + 1e-9; t = Math.Round(t + Poll, 6))
            Feed(model, Headset, 30, t);
        var estimate = RuntimeEstimator.Estimate(model.RuntimeState(Headset.Id), Status(model, Headset.Id), T0.AddHours(18));
        Assert.Equal(RuntimeKind.Ready, estimate.Kind);
        Assert.Equal(10, estimate.Hours, 0);
    }

    [Fact]
    public void VoltageEstimatesWaitForSettleAndIgnoreSmallBounces()
    {
        var model = NewModel();
        Feed(model, Ulx, 98, 0, estimated: true);
        Feed(model, Ulx, 93, .05, estimated: true);  // 拔线后 10 分钟内的松弛读数只跟踪，不形成边沿。
        Feed(model, Ulx, 71, .1, estimated: true);
        var segment = model.RuntimeState(Ulx.Id)!.Segment!;
        Assert.Null(segment.EdgePercent);
        Assert.Equal(71, segment.Level);

        Feed(model, Ulx, 70, .2, estimated: true);   // 下降 1% 不足 2%，不算边沿。
        Feed(model, Ulx, 74, .3, estimated: true);   // 回弹 <5% 忽略。
        segment = model.RuntimeState(Ulx.Id)!.Segment!;
        Assert.Equal(71, segment.Level);
        Assert.Null(segment.EdgePercent);

        Feed(model, Ulx, 69, .4, estimated: true);
        Assert.Equal(69, model.RuntimeState(Ulx.Id)!.Segment!.EdgePercent);
        Assert.Equal(0, model.RuntimeState(Ulx.Id)!.MinStep);
    }

    [Fact]
    public void FoldRejectsImpossibleRatesAndAdaptsToSustainedHabitChanges()
    {
        var state = RuntimeEstimator.Fold(new(), 10, .05, T0); // 200%/h 不合理，丢弃。
        Assert.Equal(0, state.LearnedPercent);

        state = RuntimeEstimator.Fold(new(), 100, 100, T0);     // 1%/h
        var once = RuntimeEstimator.Fold(state, 20, 20 / 3d, T0); // 3%/h 第一次：先挂起，速率不变。
        Assert.Equal(1, once.ShiftDirection);
        Assert.Equal(20, once.ShiftPercent);
        Assert.Equal(1, once.LearnedPercent / once.LearnedHours, 6);
        var twice = RuntimeEstimator.Fold(once, 20, 20 / 3d, T0); // 连续第二次同向：确认习惯变化。
        Assert.Equal(0, twice.ShiftPercent);
        Assert.Equal(65, twice.LearnedPercent, 6);
        Assert.True(twice.LearnedPercent / twice.LearnedHours > 1.6);

        // 单次异常偏离后回到正常：挂起段按半权重并入，不会把速率拉飞。
        var spike = RuntimeEstimator.Fold(state, 10, 1, T0);     // 10%/h
        Assert.Equal(100, spike.LearnedPercent);
        var settled = RuntimeEstimator.Fold(spike, 10, 10, T0);  // 回到 1%/h
        Assert.Equal(115, settled.LearnedPercent, 6);
        Assert.Equal(0, settled.ShiftPercent);
    }

    [Fact]
    public void LearnedRateIsCappedSoOldCyclesFadeOut()
    {
        var state = new SavedRuntimeEstimate();
        for (int i = 0; i < 10; i++)
            state = RuntimeEstimator.Fold(state, 20, 20, T0);  // 1%/h
        Assert.True(state.LearnedPercent <= RuntimeEstimator.MaxLearnedPercent + 20);
    }

    [Fact]
    public void StaleLearningFallsBackToEstimating()
    {
        var state = new SavedRuntimeEstimate { LearnedPercent = 50, LearnedHours = 25, MinStep = 1, LearnedAt = T0 };
        var status = new DeviceStatus(Mouse, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new(Mouse, 60, false, T0), true);
        Assert.Equal(RuntimeKind.Ready, RuntimeEstimator.Estimate(state, status, T0.AddDays(10)).Kind);
        Assert.Equal(RuntimeKind.Estimating, RuntimeEstimator.Estimate(state, status, T0.AddDays(31)).Kind);
    }

    [Fact]
    public void DaysAtYourPaceNeedThreeActiveDays()
    {
        var status = new DeviceStatus(Mouse, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new(Mouse, 60, false, T0), true);
        var learned = new SavedRuntimeEstimate { LearnedPercent = 50, LearnedHours = 25, MinStep = 1, LearnedAt = T0 };
        Assert.Null(RuntimeEstimator.Estimate(learned with { UsageDays = 2, UsageDailyHours = 2 }, status, T0).Days);

        var estimate = RuntimeEstimator.Estimate(learned with { UsageDays = 3, UsageDailyHours = 2 }, status, T0);
        Assert.Equal(30, estimate.Hours, 6);
        Assert.Equal(15, estimate.Days!.Value, 6);
        Assert.Equal("预计可用约 30 小时 · 按你的用法约 15 天", HoverCardBuilder.RuntimeLine(status, estimate));
    }

    [Fact]
    public void DailyUsageRollsOverLocalDays()
    {
        var model = NewModel();
        for (int day = 0; day < 4; day++)
            Discharge(model, Mouse, 90 - day * 5, 85 - day * 5, day * 24, .5);
        var state = model.RuntimeState(Mouse.Id)!;
        Assert.Equal(3, state.UsageDays);
        Assert.Equal(2.5, state.UsageDailyHours, 1);
    }

    [Theory]
    [InlineData(.05, "不到 10 分钟")]
    [InlineData(.66, "约 40 分钟")]
    [InlineData(6.4, "约 6 小时")]
    [InlineData(17, "约 18 小时")]
    [InlineData(63, "约 65 小时")]
    [InlineData(432, "约 430 小时")]
    [InlineData(1200, "999 小时以上")]
    public void FormatRoundsToReadableSteps(double hours, string expected) =>
        Assert.Equal(expected, RuntimeEstimator.Format(hours));

    [Fact]
    public void DisplayHoldsSmallChangesToAvoidJitter()
    {
        var model = NewModel();
        double end = Discharge(model, Mouse, 90, 70, 0, .5); // 2%/h，70% 时约 35 小时 → 显示 36
        var first = model.Runtime(Status(model, Mouse.Id), T0.AddHours(end));
        Assert.Equal(36, RuntimeEstimator.Rounded(first.Hours));

        // 下一次读数电量不变，原始估算会因停滞修正略降，但小于 8% 时显示保持不变。
        Feed(model, Mouse, 70, end + .1);
        var second = model.Runtime(Status(model, Mouse.Id), T0.AddHours(end + .1));
        Assert.Equal(first.Hours, second.Hours);
    }

    [Fact]
    public void StatusTextsAddRuntimeOnlyWhenEnabled()
    {
        var model = NewModel();
        double end = Discharge(model, Mouse, 90, 70, 0, .5);
        var device = Status(model, Mouse.Id);
        var runtime = model.Runtime(device, T0.AddHours(end));
        Assert.Equal("预计可用约 36 小时", Presentation.Status(device, 15, runtime, compact: true));
        Assert.Equal("2.4G 已连接 · 预计可用约 36 小时", Presentation.Status(device, 15, runtime, compact: false));
        Assert.Equal("可用约 36 小时", HoverCardBuilder.ListRowStatus(device, BatteryTone.Normal, runtime));
        Assert.Equal(Presentation.Status(device, 15), Presentation.Status(device, 15, RuntimeEstimate.None, compact: true));

        Assert.Equal("可用时间估算中", Presentation.Status(device, 15, RuntimeEstimate.Estimating, compact: true));
        var low = device with { Last = device.Last! with { Percentage = 10 } };
        Assert.Equal("电量低 · 约 5 小时", Presentation.Status(low, 15, new(RuntimeKind.Ready, 5), compact: true));
    }

    [Fact]
    public void SingleHoverCardGrowsOnlyWhenRuntimeLineIsShown()
    {
        var enabled = NewModel();
        Feed(enabled, Mouse, 80, 0);
        var card = HoverCardBuilder.Build(enabled, Mouse.Id, T0);
        Assert.Equal("预计可用时间估算中", card.RuntimeLine);
        Assert.Equal(HoverCardContent.SingleHeight + HoverCardContent.RuntimeLineHeight, card.Height);

        var disabled = NewModel(enabled: false);
        Feed(disabled, Mouse, 80, 0);
        var plain = HoverCardBuilder.Build(disabled, Mouse.Id, T0);
        Assert.Equal("", plain.RuntimeLine);
        Assert.Equal(HoverCardContent.SingleHeight, plain.Height);
    }

    [Fact]
    public void StateRoundTripsThroughDiskAndRestoresAsPaused()
    {
        var model = NewModel();
        Discharge(model, Mouse, 90, 80, 0, .5);
        var directory = Directory.CreateTempSubdirectory("jige-runtime-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            files.SaveState(model.Snapshot());
            var loaded = files.LoadState();
            var restored = new TrayModel(model.Settings, loaded) { TimeZone = TimeZoneInfo.Utc };
            var before = model.RuntimeState(Mouse.Id)!;
            var after = restored.RuntimeState(Mouse.Id)!;
            Assert.Equal(before.LearnedPercent, after.LearnedPercent);
            Assert.Equal(before.LearnedHours, after.LearnedHours);
            Assert.Equal(before.Segment!.EdgePercent, after.Segment!.EdgePercent);
            Assert.True(after.Segment.Paused);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void CorruptRuntimeEntriesAreDroppedAndOldStateLoads()
    {
        var state = new TrayState
        {
            Runtime = new()
            {
                ["bad"] = new() { LearnedPercent = double.NaN },
                ["ok"] = new() { LearnedPercent = 10, LearnedHours = 5 }
            }
        };
        var model = new TrayModel(new(), state);
        Assert.Null(model.RuntimeState("bad"));
        Assert.Null(model.RuntimeState("ok")); // 既不在设备列表也不在历史里，随历史一起裁剪。
        Assert.Null(new TrayModel(new(), new()).RuntimeState(Mouse.Id));
    }

    [Fact]
    public void AccumulatedOnlineTimeTriggersAPeriodicSave()
    {
        var model = NewModel();
        Feed(model, Mouse, 80, 0);
        model.Snapshot();
        bool saved = false;
        for (double t = Poll; t <= .6 && !saved; t = Math.Round(t + Poll, 6))
            saved = Feed(model, Mouse, 80, t);
        Assert.True(saved);
        model.Snapshot();
        Assert.False(Feed(model, Mouse, 80, .7));
    }

    [Fact]
    public void RemovingAnOfflineDeviceForgetsItsRuntime()
    {
        var model = NewModel();
        Feed(model, Mouse, 80, 0);
        Feed(model, Mouse, 80, .1, link: LinkState.Offline);
        model.RemoveOffline(Mouse.Id);
        Assert.Null(model.RuntimeState(Mouse.Id));
    }
}

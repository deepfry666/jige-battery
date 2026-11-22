using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class ChargeEstimateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity Mouse = new("mouse:charge", "测试鼠标", DeviceKind.Mouse);
    private const double Tick = .5 / 60; // 30 秒一次读数。

    private static TrayModel NewModel(bool enabled = true) =>
        new(new() { ShowRuntimeEstimate = enabled, PollSeconds = 60 }, new()) { TimeZone = TimeZoneInfo.Utc };

    private static void Feed(TrayModel model, int percent, double hours, ChargingState charging = ChargingState.Charging,
        LinkState link = LinkState.Online, bool estimated = false)
    {
        var at = T0.AddHours(hours);
        model.ApplyPoll([new(Mouse, link == LinkState.Online ? percent : null, estimated, charging, link, at)], false, at);
    }

    /// <summary>典型锂电池曲线：80% 前恒流每小时 60%，之后逐步放慢。</summary>
    private static double Rate(double level) => level < 80 ? 60 : level < 90 ? 30 : 15;

    private static double TrueRemaining(double level)
    {
        double hours = 0;
        for (double p = level; p < 100; p += .01)
            hours += .01 / Rate(p);
        return hours;
    }

    /// <summary>从 from 充到 to（上报整数电量），返回结束时刻和结束时的连续电量。</summary>
    private static (double End, double Level) Charge(TrayModel model, double from, double to, double start)
    {
        double level = from, t = start;
        while (level < to)
        {
            Feed(model, (int)Math.Floor(level + 1e-9), t);
            level += Rate(level) * Tick;
            t += Tick;
        }
        if (to >= 100)
            Feed(model, 100, t, ChargingState.Full);
        return (t, level);
    }

    private static DeviceStatus Status(TrayModel model) => model.OrderedDevices().Single(item => item.Device.Id == Mouse.Id);

    [Fact]
    public void FirstChargeShowsEstimatingThenANumberCloseToTheTruth()
    {
        var model = NewModel();
        Feed(model, 20, 0);
        var estimating = model.Runtime(Status(model), T0);
        Assert.Equal(new RuntimeEstimate(RuntimeKind.Estimating, Charging: true), estimating);
        Assert.Equal("充满时间估算中", HoverCardBuilder.RuntimeLine(Status(model), estimating));
        Assert.Equal("充电中", Presentation.Status(Status(model), 15, estimating, compact: false));

        var (end, level) = Charge(model, 20, 30, 0);
        var estimate = model.Runtime(Status(model), T0.AddHours(end));
        Assert.Equal(RuntimeKind.Ready, estimate.Kind);
        Assert.True(estimate.Charging);
        // 只学到 0–50 段，后段按恒流/恒压比例推算，首轮误差在 20% 以内。
        double truth = TrueRemaining(level);
        Assert.InRange(estimate.Hours, truth * .8, truth * 1.2);
    }

    [Fact]
    public void AfterOneFullChargeTheNextChargeFollowsTheSlowTail()
    {
        var model = NewModel();
        var (end, _) = Charge(model, 20, 100, 0);
        var state = model.RuntimeState(Mouse.Id)!;
        Assert.Null(state.ChargeSegment);
        Assert.All(state.ChargePercent!, value => Assert.True(value > 0));

        Feed(model, 15, end + 6, ChargingState.NotCharging);
        double start = end + 7;
        foreach (double from in new[] { 15d, 85d })
        {
            var (at, level) = Charge(model, from, from + 2, from == 15 ? start : start + 3);
            var estimate = model.Runtime(Status(model), T0.AddHours(at));
            double truth = TrueRemaining(level);
            Assert.InRange(estimate.Hours, truth * .85, truth * 1.15);
        }
    }

    [Fact]
    public void TimeAsleepOrOfflineWhileChargingIsNotLearned()
    {
        var model = NewModel();
        var (end, _) = Charge(model, 20, 30, 0);
        double learned = model.RuntimeState(Mouse.Id)!.ChargePercent!.Sum();

        Feed(model, 0, end + .1, link: LinkState.Asleep);
        Feed(model, 60, end + .5);
        Feed(model, 61, end + .51);
        Assert.Equal(learned, model.RuntimeState(Mouse.Id)!.ChargePercent!.Sum(), 6);

        Feed(model, 62, end + .52);
        Assert.Equal(learned + 1, model.RuntimeState(Mouse.Id)!.ChargePercent!.Sum(), 6);
    }

    [Fact]
    public void ChargeTimeIsShownInPanelHoverCardAndRows()
    {
        var model = NewModel();
        var (end, _) = Charge(model, 20, 40, 0);
        var device = Status(model);
        var estimate = model.Runtime(device, T0.AddHours(end));
        string toFull = ChargeEstimator.Format(estimate.Hours);
        Assert.Matches("^约 .*充满$", toFull);

        Assert.Equal($"充电中 · {toFull}", Presentation.Status(device, 15, estimate, compact: false));
        Assert.Equal(toFull, Presentation.Status(device, 15, estimate, compact: true));
        Assert.Equal($"预计{toFull}", HoverCardBuilder.RuntimeLine(device, estimate));
        Assert.Equal(toFull, HoverCardBuilder.ListRowStatus(device, BatteryTone.Charging, estimate));
    }

    [Fact]
    public void DisabledFeatureVoltageReadingsAndFullBatteryShowNoChargeTime()
    {
        var disabled = NewModel(enabled: false);
        Charge(disabled, 20, 40, 0);
        Assert.Equal(RuntimeEstimate.None, disabled.Runtime(Status(disabled), T0.AddHours(1)));
        // 关闭时后台照常学习。
        Assert.NotNull(disabled.RuntimeState(Mouse.Id)?.ChargePercent);

        var voltage = NewModel();
        Feed(voltage, 40, 0, estimated: true);
        Feed(voltage, 45, .2, estimated: true);
        Assert.Equal(RuntimeEstimate.None, voltage.Runtime(Status(voltage), T0.AddHours(.2)));
        Assert.Null(voltage.RuntimeState(Mouse.Id)?.ChargeSegment);

        var full = NewModel();
        Charge(full, 90, 99.9, 0);
        Feed(full, 100, 1);
        Assert.False(full.Runtime(Status(full), T0.AddHours(1)).Charging);
    }

    [Fact]
    public void ChargeTimeFormatting()
    {
        Assert.Equal("即将充满", ChargeEstimator.Format(1 / 60d));
        Assert.Equal("约 5 分钟充满", ChargeEstimator.Format(3 / 60d));
        Assert.Equal("约 35 分钟充满", ChargeEstimator.Format(36 / 60d));
        Assert.Equal("约 1 小时充满", ChargeEstimator.Format(58 / 60d));
        Assert.Equal("约 1 小时 20 分钟充满", ChargeEstimator.Format(1.33));
        Assert.Equal("约 3 小时充满", ChargeEstimator.Format(2.95));
        Assert.Equal("约 4 小时充满", ChargeEstimator.Format(4.2));
    }

    [Fact]
    public void ChargeEvidenceSurvivesRestartAndInvalidDataIsDropped()
    {
        var directory = Directory.CreateTempSubdirectory("jige-charge-test-");
        try
        {
            var model = NewModel();
            Charge(model, 20, 50, 0);
            var files = new TrayFiles(directory.FullName);
            files.SaveState(model.Snapshot());
            var restored = new TrayModel(model.Settings, files.LoadState()) { TimeZone = TimeZoneInfo.Utc };
            var state = restored.RuntimeState(Mouse.Id)!;
            Assert.Equal(model.RuntimeState(Mouse.Id)!.ChargePercent, state.ChargePercent);
            // 重启前后的空洞不计时：恢复的充电段一律暂停并取消锚点。
            Assert.True(state.ChargeSegment!.Paused);
            Assert.Null(state.ChargeSegment.EdgePercent);
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        var broken = new SavedRuntimeEstimate { ChargePercent = [1, 2, 3], ChargeHours = [1, 1, 1, double.NaN] };
        Assert.Null(RuntimeEstimator.Restore(broken)!.ChargePercent);
    }

    [Fact]
    public void FoldSplitsCoarseRisesAcrossBands()
    {
        var state = ChargeEstimator.Fold(new(), 70, 100, 1, T0);
        Assert.Equal([0, 10, 10, 10], state.ChargePercent!);
        Assert.Equal(1, state.ChargeHours!.Sum(), 6);
        Assert.Same(state, ChargeEstimator.Fold(state, 50, 80, 40, T0));
    }
}

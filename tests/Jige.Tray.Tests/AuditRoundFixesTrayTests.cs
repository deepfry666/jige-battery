using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jige.Core;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class AuditRoundFixesTrayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RazerCableSwitchRemainsMergedAcrossMultiplePolls()
    {
        var model = new TrayModel(new(), new());
        var wireless = new DeviceIdentity("razer-90b:rx-dongle", "Razer Viper V3 Pro", DeviceKind.Mouse,
            Connection: DeviceConnection.Receiver)
        {
            SourceLabel = "雷蛇无线接收器"
        };
        var wired = new DeviceIdentity("razer-90b:rx-cable", "Razer Viper V3 Pro", DeviceKind.Mouse,
            Connection: DeviceConnection.UsbCable)
        {
            SourceLabel = "雷蛇有线连接"
        };

        // 第 1 轮：仅无线在线
        model.ApplyPoll([new(wireless, 40, false, ChargingState.NotCharging, LinkState.Online, Now)],
            false, Now, 0);
        Assert.Single(model.OrderedDevices());

        // 第 2 轮：插上线缆，无线转休眠、有线在线 -> 合并到原 ID
        model.ApplyPoll([
            new(wireless, null, false, ChargingState.Unknown, LinkState.Asleep, Now.AddMinutes(1)),
            new(wired, 42, false, ChargingState.Charging, LinkState.Online, Now.AddMinutes(1))
        ], false, Now.AddMinutes(1));
        var merged = Assert.Single(model.OrderedDevices());
        Assert.Equal(wireless.Id, merged.Device.Id);
        Assert.Equal(42, merged.Percentage);
        Assert.Equal(ChargingState.Charging, merged.Charging);

        // 第 3 轮：继续插线轮询 -> 不得分裂成两台设备！
        model.ApplyPoll([
            new(wireless, null, false, ChargingState.Unknown, LinkState.Asleep, Now.AddMinutes(2)),
            new(wired, 50, false, ChargingState.Charging, LinkState.Online, Now.AddMinutes(2))
        ], false, Now.AddMinutes(2));
        var stillSingle = Assert.Single(model.OrderedDevices());
        Assert.Equal(wireless.Id, stillSingle.Device.Id);
        Assert.Equal(50, stillSingle.Percentage);
    }

    [Fact]
    public void HiddenBluetoothDeviceDoesNotCauseBusyReminderLoop()
    {
        var model = new TrayModel(new() { ShowBluetoothDevices = false, LowBatteryThreshold = 20 }, new());
        var bt = new DeviceIdentity("bluetooth:AABBCCDDEEFF", "BT Mouse", DeviceKind.Mouse,
            Connection: DeviceConnection.Bluetooth);

        model.ApplyPoll([new(bt, 10, false, ChargingState.NotCharging, LinkState.Online, Now)],
            false, Now, 0);

        Assert.Null(model.ReadyReminderKey(Now, false));
        Assert.Null(model.NextReminder(Now));
    }

    [Fact]
    public void PassiveMousePreservesFreshReadingAcrossFullPollAndExcludesSleepHeartbeat()
    {
        var model = new TrayModel(new(), new());
        var passive = new DeviceIdentity("mouse-passive:rx-1", "ATTACK SHARK X11", DeviceKind.Mouse)
        {
            UsesPassiveBatteryReports = true
        };

        // 推送刚刚送达在线读数
        model.ApplyPresence([new("rx-1", null, passive.Id, LinkState.Online)
        {
            Battery = new(passive, 85, false, ChargingState.NotCharging, LinkState.Online, Now)
        }], Now);
        var dev = Assert.Single(model.OrderedDevices());
        Assert.Equal(LinkState.Online, dev.Link);
        Assert.Equal(85, dev.Percentage);

        // 60 秒后完整轮询返回默认占位 Asleep（无主动查询），不应覆盖新鲜的在线状态，且不应触发 R3 休眠心跳
        model.ApplyPoll([new(passive, null, false, ChargingState.Unknown, LinkState.Asleep, Now.AddSeconds(60))],
            false, Now.AddSeconds(60), 0);
        dev = Assert.Single(model.OrderedDevices());
        Assert.Equal(LinkState.Online, dev.Link);
        Assert.Equal(85, dev.Percentage);
        Assert.False(model.HasAsleepDevices);
        Assert.Empty(model.AsleepDeviceIds());
    }

    [Fact]
    public void RuntimeEstimatorClearsEdgeOnLongGapAndRoundsIdempotently()
    {
        var dev = new DeviceIdentity("mouse-1", "Mouse", DeviceKind.Mouse);
        DeviceStatus Status(int pct, DateTimeOffset at) => new(
            dev, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new SavedBattery(dev, pct, false, at), Fresh: true);

        var gap = TimeSpan.FromMinutes(10);
        var (state, _) = RuntimeEstimator.Observe(new SavedRuntimeEstimate { LearnedPercent = 20, LearnedHours = 20 },
            Status(81, Now), Now, gap, TimeZoneInfo.Utc);
        (state, _) = RuntimeEstimator.Observe(state, Status(80, Now.AddMinutes(5)), Now.AddMinutes(5), gap, TimeZoneInfo.Utc);
        Assert.Equal(80, state.Segment?.EdgePercent);

        // 跨越 2 天后首次读数仍为 80%，长间隔必须清空旧 EdgePercent，避免后续误算跨越数天的伪速率
        (state, _) = RuntimeEstimator.Observe(state, Status(80, Now.AddHours(48)), Now.AddHours(48), gap, TimeZoneInfo.Utc);
        Assert.Null(state.Segment?.EdgePercent);

        // Rounded 在所有正小时区间（含 47–48h 边界）必须幂等
        for (int h = 1; h <= 200; h++)
        {
            double once = RuntimeEstimator.Rounded(h);
            double twice = RuntimeEstimator.Rounded(once);
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void ChargeEstimatorClampsEffectivePercentBelow100AndClosesSegmentAtFull()
    {
        var dev = new DeviceIdentity("mouse-1", "Mouse", DeviceKind.Mouse);
        DeviceStatus ChargingStatus(int pct, DateTimeOffset at) => new(
            dev, LinkState.Online, ReadIssue.None, ChargingState.Charging,
            new SavedBattery(dev, pct, false, at), Fresh: true);

        var state = new SavedRuntimeEstimate
        {
            ChargePercent = [30, 30, 10, 10],
            ChargeHours = [0.5, 0.5, 0.25, 0.5],
            ChargeLearnedAt = Now
        };
        var gap = TimeSpan.FromMinutes(10);
        (state, _) = ChargeEstimator.Observe(state, ChargingStatus(95, Now), Now, gap);
        // 即使经过足够时间，95% 充电估算的剩余时间仍不得因 effective >= 100 跳变为 0
        var estimate = ChargeEstimator.Estimate(state, ChargingStatus(95, Now.AddMinutes(30)), Now.AddMinutes(30));
        Assert.Equal(RuntimeKind.Ready, estimate.Kind);
        Assert.True(estimate.Hours > 0);

        // 达到 100% 即使固件仍报告 Charging，也必须结束当前 ChargeSegment
        (state, _) = ChargeEstimator.Observe(state, ChargingStatus(100, Now.AddMinutes(40)), Now.AddMinutes(40), gap);
        Assert.Null(state.ChargeSegment);
    }

    [Fact]
    public void PollScheduleQueuesFullPollDuringTargetedPollAndBacksOffInputCheck()
    {
        var schedule = new PollSchedule(0);
        var settings = new TraySettings { PollSeconds = 60 };
        schedule.Start(0, false);
        schedule.Complete();
        schedule.RequestTargeted(["dev-1"]);
        Assert.True(schedule.TryStartTargeted(settings, 100, false, out _));

        // 定向轮询运行期间收到完整轮询请求，必须排队而不丢弃
        schedule.Request(userInitiated: false);
        schedule.Complete();
        Assert.Equal(100, schedule.Due(settings, 100, false));

        // 退避状态下 InputCheckDelayMs 应返回退避剩余时间而非 500ms 固定高频空转
        schedule.SyncLastInputTick(10);
        Assert.True(schedule.ShouldWakeHeartbeatOnInput(20, 1_000, true, false));
        schedule.StartHeartbeat(1_000, inputWake: true);
        schedule.CompleteHeartbeat();
        long delay = schedule.InputCheckDelayMs(1_100);
        Assert.True(delay >= 1_500, $"预期进入冷却期，实际延迟 {delay} ms");
    }

    [Fact]
    public void TrayFilesCleanAliasStripsControlAndBidiCharsAndPreservesStateOnReadError()
    {
        Assert.Equal("鼠标ABC", TraySettings.CleanAlias("  鼠标\u202E\u200B\u007FABC  "));
        Assert.Equal(64, TraySettings.CleanAlias(new string('A', 100))!.Length);

        string temp = Path.Combine(Path.GetTempPath(), $"jige-state-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var files = new TrayFiles(temp);
            File.WriteAllText(files.StatePath, """{"readings":{"dev-1":{"percentage":80,"isEstimated":false,"readAt":"2026-11-22T12:00:00Z"}}}""");
            // 用独占锁模拟杀毒软件短暂占用 state.json
            using var blocker = new FileStream(files.StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var settings = new TraySettings();
            var loaded = files.LoadState(ref settings);
            Assert.Empty(loaded.Readings);

            // LoadState 因 IO 锁定失败后，SaveState 不得把原文件覆写清空
            blocker.Dispose();
            files.SaveState(new TrayState());
            string disk = File.ReadAllText(files.StatePath);
            Assert.Contains("dev-1", disk);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void AutoHideTaskbarRespectsExplicitTaskbarEdgeInPopupAndHoverPlacement()
    {
        var screen = new Bounds(0, 0, 1920, 1080);
        var work = screen; // 自动隐藏任务栏时 rcWork == rcMonitor
        var anchor = new Bounds(1800, 1060, 1840, 1080);

        Assert.Equal(TaskbarEdge.Bottom, PopupPlacement.ResolveEdge(anchor, screen, work, TaskbarEdge.Bottom));
        Assert.Equal(TaskbarEdge.Top, PopupPlacement.ResolveEdge(new(1800, 0, 1840, 20), screen, work, TaskbarEdge.Top));

        var hover = HoverCardPlacement.Place(anchor, screen, work, 240, 88, 8, taskbarEdge: TaskbarEdge.Bottom);
        Assert.True(hover.Bottom <= anchor.Top);
    }

    [Fact]
    public async Task PrereleaseCanUpgradeToSameNumberStableAndStallFallsBackToGitHub()
    {
        const string payload = "installer-bytes";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string sums = $"{hash}  Jige-Setup-0.2.19-x64.exe\n";
        string releaseJson(string baseUrl) => $$"""
        {
          "tag_name": "v0.2.19",
          "body": "- 正式版发布",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "Jige-Setup-0.2.19-x64.exe",
              "size": {{payload.Length}},
              "browser_download_url": "{{baseUrl}}/v0.2.19/Jige-Setup-0.2.19-x64.exe"
            },
            {
              "name": "SHA256SUMS.txt",
              "size": {{sums.Length}},
              "browser_download_url": "{{baseUrl}}/v0.2.19/SHA256SUMS.txt"
            }
          ]
        }
        """;

        var mirrorUrl = "https://download.imfourj.cn/jige-battery/releases";
        var githubUrl = "https://github.com/deepfry666/jige-battery/releases/download";
        var clock = new ManualTimeProvider();
        bool mirrorDownloadHit = false;
        bool githubDownloadHit = false;

        using var http = new HttpClient(new AsyncStubHandler(async (req, token) =>
        {
            string url = req.RequestUri!.AbsoluteUri;
            if (url == UpdateClient.MirrorLatest.AbsoluteUri)
                return new(HttpStatusCode.OK) { Content = new StringContent(releaseJson(mirrorUrl), Encoding.UTF8, "application/json") };
            if (url == $"{mirrorUrl}/v0.2.19/SHA256SUMS.txt")
                return new(HttpStatusCode.OK) { Content = new StringContent(sums) };
            if (url == $"{mirrorUrl}/v0.2.19/Jige-Setup-0.2.19-x64.exe")
            {
                mirrorDownloadHit = true;
                var pending = Task.Delay(Timeout.InfiniteTimeSpan, token);
                clock.Advance(TimeSpan.FromSeconds(31));
                await pending;
            }
            if (url == $"{githubUrl}/v0.2.19/SHA256SUMS.txt")
                return new(HttpStatusCode.OK) { Content = new StringContent(sums) };
            if (url == $"{githubUrl}/v0.2.19/Jige-Setup-0.2.19-x64.exe")
            {
                githubDownloadHit = true;
                return new(HttpStatusCode.OK) { Content = new StringContent(payload) };
            }
            return new(HttpStatusCode.NotFound);
        }));

        var client = new UpdateClient(http, timeProvider: clock, downloadStallTimeout: TimeSpan.FromSeconds(30));
        // 0.2.19 测试版允许升级到同号正式版 v0.2.19
        var found = await client.CheckAsync(new Version(0, 2, 19), CancellationToken.None, currentIsPrerelease: true);
        Assert.NotNull(found);
        Assert.Equal(new Version(0, 2, 19), found!.Version);

        string temp = Path.Combine(Path.GetTempPath(), $"jige-upd-fallback-{Guid.NewGuid():N}");
        try
        {
            var files = new UpdateFiles(temp);
            var pkg = await client.DownloadVerifiedAsync(found, files, progress: null, CancellationToken.None);
            Assert.True(mirrorDownloadHit);
            Assert.True(githubDownloadHit);
            Assert.True(File.Exists(pkg.Path));
            Assert.Equal(payload, await File.ReadAllTextAsync(pkg.Path));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void TaskbarStripBadgeMaintainsWcagContrastAndSideBackdropFallback()
    {
        foreach (uint bg in new uint[] { 0x101010, 0x808080, 0xF3F3F3, 0x3A6EA5, 0xC89B3C })
        {
            var backdrop = TaskbarBackdrop.Uniform(bg, sampled: true);
            var palette = TaskbarStripPalette.Create(backdrop, TaskbarStripStyle.Badges, TaskbarStripColorScheme.Auto);
            var plate = palette.BadgeSurface(BatteryTone.Low, showBolt: false);
            uint surfaceRgb = TaskbarColorMath.BlendOver(plate.Fill.Rgb, plate.Fill.Alpha, bg);
            double contrast = TaskbarColorMath.Contrast(
                TaskbarColorMath.RelativeLuminance(palette.Low.Rgb),
                TaskbarColorMath.RelativeLuminance(surfaceRgb));
            Assert.True(contrast >= 4.5, $"背景 #{bg:X6} 下徽章对比度不足：{contrast:F2}");

            var hc = TaskbarStripPalette.Create(backdrop, TaskbarStripStyle.Badges, TaskbarStripColorScheme.HighContrast);
            Assert.True(hc.IsHighContrast);
            var hcPlate = hc.BadgeSurface(BatteryTone.Low, showBolt: false);
            uint hcSurfaceRgb = TaskbarColorMath.BlendOver(hcPlate.Fill.Rgb, hcPlate.Fill.Alpha, bg);
            double hcContrast = TaskbarColorMath.Contrast(
                TaskbarColorMath.RelativeLuminance(hc.Low.Rgb),
                TaskbarColorMath.RelativeLuminance(hcSurfaceRgb));
            Assert.True(hcContrast >= 5.5, $"高对比度模式下对比度不足：{hcContrast:F2}");
        }

        var sides = TaskbarBackdropSamplePlan.ComputeSides(
            new Bounds(0, 1032, 1920, 1080),
            new Bounds(1500, 1045, 1640, 1067),
            scale: 1);
        Assert.NotNull(sides.Left);
        Assert.NotNull(sides.Right);

        // 窄屏通知区（宽度 1200、左侧 1/3 为 400）：通知区左边界 480 在 400 以右，仍应识别通知区
        var bar = new Bounds(0, 720, 1200, 768);
        var located = TaskbarControls.Locate(bar, null,
        [
            new(new(0, 720, 48, 768), "StartButton", "Start", true),
            new(new(0, 720, 1200, 768), "TaskbarFrame", "TaskbarFrame", false),
            new(new(480, 720, 1200, 768), "SystemTrayFrame", "SystemTrayFrame", false)
        ]);
        Assert.NotNull(located.NotificationArea);
        Assert.Equal(480, located.NotificationArea!.Value.Left);
    }

    [Fact]
    public void DesktopRefreshDebouncerAndTrayMenuModelBehaveConsistently()
    {
        var debouncer = new DesktopRefreshDebouncer();
        debouncer.Request(1_000, resetPopup: false);
        Assert.False(debouncer.TryConsume(1_050, isEditing: false, out _));
        debouncer.Request(1_050, resetPopup: true);
        Assert.False(debouncer.TryConsume(1_400, isEditing: true, out _));
        Assert.True(debouncer.TryConsume(1_400, isEditing: false, out bool reset));
        Assert.True(reset);
        Assert.False(debouncer.HasPending);

        var items = TrayMenuModel.Items;
        Assert.Equal(5, items.Length);
        Assert.Equal(TrayMenuCommand.Refresh, items[0].Command);
        Assert.Equal(TrayMenuCommand.Exit, items[^1].Command);
    }

    private sealed class AsyncStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object sync = new();
        private long nowMs;
        private readonly List<ManualTimer> timers = [];

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due = [];
            lock (sync)
            {
                nowMs += (long)delta.TotalMilliseconds;
                foreach (var timer in timers)
                {
                    if (timer.DueMs is long at && at <= nowMs)
                    {
                        timer.DueMs = null;
                        due.Add(timer);
                    }
                }
            }
            foreach (var timer in due)
                timer.Fire();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (sync)
                timers.Add(timer);
            return timer;
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public long? DueMs;
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.sync)
                {
                    if (disposed)
                        return false;
                    DueMs = dueTime == Timeout.InfiniteTimeSpan
                        ? null
                        : owner.nowMs + Math.Max(0, (long)dueTime.TotalMilliseconds);
                    return true;
                }
            }

            public void Fire()
            {
                if (!disposed)
                    callback(state);
            }

            public void Dispose()
            {
                lock (owner.sync)
                {
                    disposed = true;
                    DueMs = null;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}

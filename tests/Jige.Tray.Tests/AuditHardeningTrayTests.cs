using System.Net;
using System.Text;
using Jige.Core;

namespace Jige.Tray.Tests;

public sealed class AuditHardeningTrayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static string ReleaseJson(string tag, string baseUrl, long size = 5_000_000) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "body": "- 修复更新",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "Jige-Setup-{{tag[1..]}}-x64.exe",
              "size": {{size}},
              "browser_download_url": "{{baseUrl}}/{{tag}}/Jige-Setup-{{tag[1..]}}-x64.exe"
            },
            {
              "name": "SHA256SUMS.txt",
              "size": 92,
              "browser_download_url": "{{baseUrl}}/{{tag}}/SHA256SUMS.txt"
            }
          ]
        }
        """;

    [Fact]
    public async Task UpdateClientChecksFallbackWhenMirrorIsStaleAtCurrentVersion()
    {
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri == UpdateClient.MirrorLatest)
                return JsonResponse(ReleaseJson("v0.2.17", "https://download.imfourj.cn/jige-battery/releases"));
            if (req.RequestUri == UpdateClient.Latest)
                return JsonResponse(ReleaseJson("v0.2.18", "https://github.com/deepfry666/jige-battery/releases/download"));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        var client = new UpdateClient(http);
        var found = await client.CheckAsync(new Version(0, 2, 17), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(new Version(0, 2, 18), found.Version);
        Assert.Same(UpdateSource.GitHub, found.EffectiveSource);
    }

    [Fact]
    public async Task UpdateClientReturnsNullWhenMirrorIsCurrentAndGitHubUnreachable()
    {
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri == UpdateClient.MirrorLatest)
                return JsonResponse(ReleaseJson("v0.2.17", "https://download.imfourj.cn/jige-battery/releases"));
            throw new HttpRequestException("GitHub unreachable");
        });

        using var http = new HttpClient(handler);
        var client = new UpdateClient(http);
        var found = await client.CheckAsync(new Version(0, 2, 17), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task UpdateClientReturnsNullWhenMirrorIsCurrentAndGitHubHangs()
    {
        var clock = new ManualTimeProvider();
        bool mirrorRequested = false;
        bool gitHubRequested = false;
        var handler = new AsyncStubHandler(async (req, token) =>
        {
            if (req.RequestUri == UpdateClient.MirrorLatest)
            {
                mirrorRequested = true;
                return JsonResponse(ReleaseJson("v0.2.17", "https://download.imfourj.cn/jige-battery/releases"));
            }
            if (req.RequestUri == UpdateClient.Latest)
            {
                gitHubRequested = true;
                var pending = Task.Delay(Timeout.InfiniteTimeSpan, token);
                clock.Advance(UpdateClient.DefaultMirrorCheckTimeout + TimeSpan.FromMilliseconds(1));
                await pending;
            }
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new UpdateClient(http, timeProvider: clock);
        var found = await client.CheckAsync(new Version(0, 2, 17), CancellationToken.None);

        Assert.True(mirrorRequested);
        Assert.True(gitHubRequested);
        Assert.Null(found);
    }

    [Fact]
    public async Task UpdateClientCheckFollowsSameSourceRedirectAndRejectsForeignHost()
    {
        var redirectedUrl = new Uri("https://download.imfourj.cn/jige-battery/v2/latest.json");
        var okHandler = new StubHandler(req =>
        {
            if (req.RequestUri == UpdateClient.MirrorLatest)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = redirectedUrl;
                return redirect;
            }
            if (req.RequestUri == redirectedUrl)
                return JsonResponse(ReleaseJson("v0.2.18", "https://download.imfourj.cn/jige-battery/releases"));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var okHttp = new HttpClient(okHandler);
        var okClient = new UpdateClient(okHttp, [UpdateSource.Mirror]);
        var ok = await okClient.CheckAsync(new Version(0, 2, 17), CancellationToken.None);
        Assert.NotNull(ok);
        Assert.Equal(new Version(0, 2, 18), ok.Version);

        var evilHandler = new StubHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example/latest.json");
            return redirect;
        });
        using var evilHttp = new HttpClient(evilHandler);
        var evilClient = new UpdateClient(evilHttp, [UpdateSource.Mirror]);
        await Assert.ThrowsAsync<InvalidDataException>(() => evilClient.CheckAsync(new Version(0, 2, 17), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateClientFollowsApiGithubRedirectAndFallsBackOnMirrorErrors()
    {
        var relocatedApi = new Uri("https://api.github.com/repos/new-owner/jige-battery/releases/latest");
        var redirectHandler = new StubHandler(req =>
        {
            if (req.RequestUri == UpdateClient.MirrorLatest)
            {
                var evil = new HttpResponseMessage(HttpStatusCode.Found);
                evil.Headers.Location = new Uri("https://evil.example/latest.json");
                return evil;
            }
            if (req.RequestUri == UpdateClient.Latest)
            {
                var moved = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                moved.Headers.Location = relocatedApi;
                return moved;
            }
            if (req.RequestUri == relocatedApi)
                return JsonResponse(ReleaseJson("v0.2.18", "https://github.com/deepfry666/jige-battery/releases/download"));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var redirectHttp = new HttpClient(redirectHandler);
        var fromRedirect = await new UpdateClient(redirectHttp).CheckAsync(new Version(0, 2, 17), CancellationToken.None);
        Assert.NotNull(fromRedirect);
        Assert.Equal(new Version(0, 2, 18), fromRedirect.Version);
        Assert.Same(UpdateSource.GitHub, fromRedirect.EffectiveSource);

        var cryptoFailHandler = new StubHandler(req =>
        {
            if (req.RequestUri == UpdateClient.MirrorLatest)
                throw new System.Security.Cryptography.CryptographicException("tls-error");
            if (req.RequestUri == UpdateClient.Latest)
                return JsonResponse(ReleaseJson("v0.2.19", "https://github.com/deepfry666/jige-battery/releases/download"));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var cryptoHttp = new HttpClient(cryptoFailHandler);
        var fromFallback = await new UpdateClient(cryptoHttp).CheckAsync(new Version(0, 2, 17), CancellationToken.None);
        Assert.NotNull(fromFallback);
        Assert.Equal(new Version(0, 2, 19), fromFallback.Version);
        Assert.Same(UpdateSource.GitHub, fromFallback.EffectiveSource);
    }

    [Fact]
    public void UpdateViewModelExplainErrorScrubsLocalPaths()
    {
        string ioMessage = UpdateViewState.ExplainError(
            new IOException(@"The process cannot access the file 'C:\Users\alice\AppData\Local\Jige\updates\setup.exe'."));
        Assert.DoesNotContain("alice", ioMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", ioMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("更新缓存", ioMessage);

        string unknownMessage = UpdateViewState.ExplainError(new InvalidOperationException(@"C:\Users\secret\boom"));
        Assert.DoesNotContain("secret", unknownMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrayModelPartialPollPreservesUnseenDevicesAndMergesCables()
    {
        var mouseWireless = new DeviceIdentity("logitech-hidpp:rx/1/unit-AAAA", "G Pro", DeviceKind.Mouse,
            Connection: DeviceConnection.Receiver);
        var mouseCable = new DeviceIdentity("logitech-hidpp-direct:usb/unit-AAAA", "G Pro", DeviceKind.Mouse,
            Connection: DeviceConnection.UsbCable);
        var keyboard = new DeviceIdentity("logitech-hidpp:rx/2/unit-BBBB", "MX Keys", DeviceKind.Keyboard,
            Connection: DeviceConnection.Receiver);

        var model = new TrayModel(new(), new());
        model.ApplyPoll([
            new(mouseWireless, 50, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(keyboard, 80, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);

        model.ApplyPoll([
            new(mouseWireless, 51, false, ChargingState.NotCharging, LinkState.Online, Now.AddSeconds(10)),
            new(mouseCable, 55, false, ChargingState.Charging, LinkState.Online, Now.AddSeconds(10))
        ], false, Now.AddSeconds(10), completePoll: false);

        var active = model.OrderedDevices().ToArray();
        Assert.Equal(2, active.Length);
        var kb = Assert.Single(active, item => item.Device.Id == keyboard.Id);
        Assert.Equal(LinkState.Online, kb.Link);
        Assert.True(kb.Fresh);
    }

    [Fact]
    public void MultiDeviceBatchOnlyMarksPollIncompleteOnReceiverLevelTimeout()
    {
        var mouse = new DeviceIdentity("logitech-hidpp:rx/1/m", "Mouse", DeviceKind.Mouse);
        var asleepKb = new DeviceIdentity("logitech-hidpp:rx/2/k", "Keyboard", DeviceKind.Keyboard);
        var receiver = new DeviceIdentity("logitech-hidpp:rx", "Receiver", DeviceKind.Mouse);
        var otherMouse = new DeviceIdentity("razer:other", "Razer", DeviceKind.Mouse);

        // 单个已配对但休眠/关机的槽位超时（Asleep + Timeout）不代表整轮扫描不完整。
        Assert.False(PollResult.IsIncompleteMultiDeviceBatch([
            new(mouse, 80, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(asleepKb, null, false, ChargingState.Unknown, LinkState.Asleep, Now, ReadIssue.Timeout)
        ], receiver.Id));
        Assert.False(PollResult.IsIncompleteMultiDeviceBatch([
            new(asleepKb, null, false, ChargingState.Unknown, LinkState.Asleep, Now, ReadIssue.Timeout)
        ], receiver.Id));

        // 接收器级扫描超时（含先读出部分槽位后耗尽 12 秒预算）必须标记该接收器扫描不完整。
        Assert.True(PollResult.IsIncompleteMultiDeviceBatch([
            new(receiver, null, false, ChargingState.Unknown, LinkState.Unknown, Now, ReadIssue.Timeout)
        ], receiver.Id));
        Assert.True(PollResult.IsIncompleteMultiDeviceBatch([
            new(mouse, 80, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(receiver, null, false, ChargingState.Unknown, LinkState.Unknown, Now, ReadIssue.Timeout)
        ], receiver.Id));
        Assert.False(PollResult.IsIncompleteMultiDeviceBatch([], receiver.Id));

        // 局部接收器超时仅保护该接收器未扫到的槽位，不得阻止其他已拔出设备正常清扫离线。
        var model = new TrayModel(new(), new());
        model.ApplyPoll([
            new(mouse, 80, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(asleepKb, 70, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(otherMouse, 60, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);

        model.ApplyPoll([
            new(mouse, 79, false, ChargingState.NotCharging, LinkState.Online, Now.AddSeconds(30)),
            new(receiver, null, false, ChargingState.Unknown, LinkState.Unknown, Now.AddSeconds(30), ReadIssue.Timeout)
        ], false, Now.AddSeconds(30),
            incompleteReceivers: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { receiver.Id });

        var byId = model.OrderedDevices().ToDictionary(d => d.Device.Id);
        Assert.Equal(LinkState.Online, byId[asleepKb.Id].Link);
        Assert.Equal(LinkState.Offline, byId[otherMouse.Id].Link);
    }

    [Fact]
    public void TrayModelGameModeTtlAndClockRollbackExpiration()
    {
        var mouse = new DeviceIdentity("mouse:1", "Mouse", DeviceKind.Mouse);
        var settings = new TraySettings { PollSeconds = 60, GamePollSeconds = 300, LowBatteryThreshold = 20 };
        var model = new TrayModel(settings, new());
        model.ApplyPoll([new(mouse, 15, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);

        // 游戏中 200 秒内不应过早过期（GamePollSeconds=300 → TTL=600s）
        Assert.False(model.ExpireReadings(Now.AddSeconds(200), game: true));
        Assert.True(model.ReminderIsDue(mouse.Id, Now.AddSeconds(200)));

        // 轻微时钟回拨（2 分钟 <= 5 分钟容差）不抖动，大幅回拨（10 分钟）才过期
        Assert.False(model.ExpireReadings(Now.AddMinutes(-2), game: false));
        Assert.True(model.ExpireReadings(Now.AddMinutes(-10), game: false));
    }

    [Fact]
    public void TrayModelClampsFutureReadAtToNow()
    {
        var mouse = new DeviceIdentity("mouse:future", "Mouse", DeviceKind.Mouse);
        var model = new TrayModel(new(), new());
        model.ApplyPoll([new(mouse, 77, false, ChargingState.NotCharging, LinkState.Online, Now.AddMinutes(30))], false, Now);

        var active = Assert.Single(model.OrderedDevices());
        Assert.NotNull(active.Last);
        Assert.Equal(Now, active.Last.ReadAt);
        Assert.True(active.Fresh);
    }

    [Fact]
    public void TrayModelPanelHiddenDeviceStaysConsistentBetweenLowestTrayDeviceAndHoverCardList()
    {
        var hiddenMouse = new DeviceIdentity("mouse:hidden", "Hidden Mouse", DeviceKind.Mouse);
        var visibleKb = new DeviceIdentity("kb:visible", "Visible KB", DeviceKind.Keyboard);
        var settings = new TraySettings { TrayMode = TrayDisplayMode.LowestBattery, HiddenDeviceIds = [hiddenMouse.Id] };
        var model = new TrayModel(settings, new());
        model.ApplyPoll([
            new(hiddenMouse, 8, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(visibleKb, 70, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);

        // 面板隐藏（眼睛）仅作用于主面板，托盘最低电量与悬停卡片列表保持同一可见口径。
        Assert.Equal(visibleKb.Id, model.BuildMainPanel().Hero?.Device.Id);
        Assert.Equal(hiddenMouse.Id, model.LowestTrayDevice?.Device.Id);
        var hover = HoverCardBuilder.Build(model, null, Now, forceList: true);
        Assert.Equal("几格 · 2 台设备", hover.Title);
        Assert.Equal(hiddenMouse.Id, hover.Rows[0].DeviceId);
    }

    [Fact]
    public void PollScheduleRescanWhileRunningQueuesImmediateFollowUp()
    {
        const long t0 = 1_000_000L;
        var settings = new TraySettings();
        var schedule = new PollSchedule(t0);
        schedule.Start(t0, game: false);
        schedule.Request(userInitiated: true, rescan: true);
        schedule.Complete();

        Assert.Equal(t0 + 1_000L, schedule.Due(settings, t0 + 1_000L, game: false));
    }

    [Fact]
    public void TaskbarStripPaletteUsesForegroundForAsleepChargingWithoutBolt()
    {
        var palette = TaskbarStripPalette.Create(dark: true);
        Assert.Equal((palette.Foreground, palette.Foreground), palette.Ink(BatteryTone.Charging, showBolt: false));
        Assert.Equal((palette.Foreground, palette.Charging), palette.Ink(BatteryTone.Charging, showBolt: true));
    }

    [Fact]
    public void TrayFilesSaveSettingsProtectsUnreadableConfigUntilUserInitiatedSaveSucceedsAndPrunesBackups()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(folder.FullName);
            const string original = """{"pollSeconds":120}""";
            File.WriteAllText(files.ConfigPath, original);

            // 预置 4 份旧锁定备份，验证备份清理最多保留最新 3 份。
            for (int i = 1; i <= 4; i++)
                File.WriteAllText(Path.Combine(folder.FullName, $"config.locked-20200101-00000{i}-0000000.json"), $"old-{i}");

            if (OperatingSystem.IsWindows())
            {
                // 本进程独占打开以触发 TryLoadSettings 的共享冲突。
                using (File.Open(files.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.False(files.TryLoadSettings(out _));
            }
            else
            {
                File.SetUnixFileMode(files.ConfigPath, UnixFileMode.None);
                try
                {
                    if (files.TryLoadSettings(out _))
                        return; // root 账户无视权限位时跳过，避免容器环境假红。
                }
                finally
                {
                    File.SetUnixFileMode(files.ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            // 后台自动写盘不得覆盖先前不可读的配置文件
            Assert.Throws<IOException>(() => files.SaveSettings(new TraySettings { PollSeconds = 30 }, userInitiated: false));
            Assert.Equal(original, File.ReadAllText(files.ConfigPath));

            // 若用户显式保存时写盘本身失败，仍须保持保护状态（不得在 Write 成功前清除 preserveConfig）
            string blockerTmpDir = files.ConfigPath + ".tmp";
            Directory.CreateDirectory(blockerTmpDir);
            try
            {
                Assert.ThrowsAny<Exception>(() => files.SaveSettings(new TraySettings { PollSeconds = 30 }, userInitiated: true));
            }
            finally
            {
                Directory.Delete(blockerTmpDir, recursive: true);
            }
            Assert.Throws<IOException>(() => files.SaveSettings(new TraySettings { PollSeconds = 30 }, userInitiated: false));
            Assert.Equal(original, File.ReadAllText(files.ConfigPath));

            // 清理阻碍后用户显式保存成功：备份最多保留 3 份且最新一份包含原内容
            files.SaveSettings(new TraySettings { PollSeconds = 30 }, userInitiated: true);
            var backups = Directory.EnumerateFiles(folder.FullName, "config.locked-*.json")
                .OrderByDescending(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.Equal(3, backups.Length);
            Assert.Equal(original, File.ReadAllText(backups[0]));

            // 不先调用 TryLoadSettings（避免被其重置标志掩盖），直接验证用户保存成功已解除保护
            files.SaveSettings(new TraySettings { PollSeconds = 60 }, userInitiated: false);
            Assert.True(files.TryLoadSettings(out var updated));
            Assert.Equal(60, updated.PollSeconds);
        }
        finally
        {
            try { folder.Delete(recursive: true); }
            catch (IOException) { }
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class AsyncStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private long nowTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate)
            {
                timers.Add(timer);
                timer.ScheduleLocked(dueTime, nowTicks);
            }
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer>? due = null;
            lock (gate)
            {
                nowTicks += delta.Ticks;
                foreach (var timer in timers)
                {
                    if (timer.TakeIfDueLocked(nowTicks))
                        (due ??= []).Add(timer);
                }
            }
            if (due is null)
                return;
            foreach (var timer in due)
                timer.Fire();
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? dueAtTicks;
            private bool disposed;

            public void ScheduleLocked(TimeSpan dueTime, long currentTicks)
            {
                if (disposed)
                    return;
                dueAtTicks = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : currentTicks + Math.Max(0, dueTime.Ticks);
            }

            public bool TakeIfDueLocked(long currentTicks)
            {
                if (disposed || dueAtTicks is not { } due || due > currentTicks)
                    return false;
                dueAtTicks = null;
                return true;
            }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (disposed)
                        return false;
                    ScheduleLocked(dueTime, owner.nowTicks);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    if (disposed)
                        return;
                    disposed = true;
                    dueAtTicks = null;
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

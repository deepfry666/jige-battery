using System.Text.Json;
using Jige.Core;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TrayV023Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity CompxMouse = new("mouse:atk-1", "ATK 鼠标", DeviceKind.Mouse);
    private static readonly DeviceIdentity H9 = new("inzone-h9:sony-1", "Sony INZONE H9（一代接收器）", DeviceKind.Headset,
        AdaptationSource.Catalog, VerificationTier.HardwareVerified);

    [Fact]
    public void R1AndR4_IdleTimeoutEntersAsleepWithoutGrayIconOrPanelFlicker()
    {
        long clockMs = 100_000L;
        var schedule = new PollSchedule(clockMs);
        var model = new TrayModel(new() { PollSeconds = 60, LowBatteryThreshold = 15 }, new());

        // t=0: 鼠标正常在线 72%。
        schedule.Start(clockMs, game: false);
        model.ApplyPoll([new(CompxMouse, 72, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        schedule.Complete();

        var onlineIcon = Presentation.Icon(model.PrimaryDevice, darkTaskbar: true, threshold: 15);
        Assert.Equal(BatteryTone.Normal, onlineIcon.Tone);
        Assert.Equal((byte)255, onlineIcon.Ink.Alpha);

        // t=8s: 几秒没动鼠标，Compx 接收器无回复超时 → 休眠（Asleep）。
        long hbTick = schedule.HeartbeatDue(clockMs, game: false, hasHeartbeatDevices: true)!.Value;
        Assert.Equal(clockMs + 8_000L, hbTick);
        schedule.StartHeartbeat(hbTick);
        var (changed, needsRefresh) = model.ApplyPresence(
            [new("atk-1", null, CompxMouse.Id, LinkState.Asleep, ReadIssue.Timeout)],
            Now.AddSeconds(8));
        schedule.CompleteHeartbeat();

        Assert.True(changed);
        Assert.False(needsRefresh);
        var asleepDevice = model.PrimaryDevice!;
        Assert.Equal(LinkState.Asleep, asleepDevice.Link);
        Assert.Equal(72, asleepDevice.Percentage);

        // R1：图标不变灰，保留上次读数，悬停提示明确写「休眠中 · 上次 72%」。
        var asleepIcon = Presentation.Icon(asleepDevice, darkTaskbar: true, threshold: 15);
        Assert.Equal(BatteryTone.Normal, asleepIcon.Tone);
        Assert.Equal((byte)255, asleepIcon.Ink.Alpha);
        Assert.Equal(72, asleepIcon.Percentage);
        Assert.Equal("ATK 鼠标 · 休眠中 · 上次 72%", Presentation.DeviceTooltip(model, asleepDevice));

        // 面板不跳进「离线设备」，也不在顶部大卡片显示「已断开」。
        var plan = model.BuildMainPanel();
        Assert.False(plan.HeroDisconnected);
        Assert.Equal(CompxMouse.Id, plan.Hero!.Device.Id);
        Assert.Empty(plan.OfflineRows);

        // 休眠期间低电量设备也不变灰，且不弹低电量提醒。
        model.ApplyPoll([new(CompxMouse, 10, false, ChargingState.NotCharging, LinkState.Online, Now.AddSeconds(20))], false, Now.AddSeconds(20));
        model.ApplyPresence([new("atk-1", null, CompxMouse.Id, LinkState.Asleep, ReadIssue.Timeout)], Now.AddSeconds(28));
        var lowAsleepIcon = Presentation.Icon(model.PrimaryDevice, darkTaskbar: true, threshold: 15);
        Assert.Equal(BatteryTone.Low, lowAsleepIcon.Tone);
        Assert.Equal((byte)255, lowAsleepIcon.Ink.Alpha);
        Assert.Null(model.ReadyReminder(Now.AddSeconds(28), false));
    }

    [Fact]
    public void R1AndR4_PowerOffExplicitDisconnectTurnsOfflineWithin8Seconds()
    {
        long clockMs = 100_000L;
        var schedule = new PollSchedule(clockMs);
        var model = new TrayModel(new() { PollSeconds = 60 }, new());

        schedule.Start(clockMs, game: false);
        model.ApplyPoll([new(CompxMouse, 65, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        schedule.Complete();

        // 约 8 秒心跳到期，接收器通过 0x03 明确回复「鼠标未连接」（Offline）。
        long hbTick = schedule.HeartbeatDue(clockMs, game: false, hasHeartbeatDevices: true)!.Value;
        Assert.InRange(hbTick - clockMs, 5_000L, 10_000L);

        schedule.StartHeartbeat(hbTick);
        var (changed, _) = model.ApplyPresence(
            [new("atk-1", null, CompxMouse.Id, LinkState.Offline, ReadIssue.None)],
            Now.AddMilliseconds(hbTick - clockMs));
        schedule.CompleteHeartbeat();

        Assert.True(changed);
        var offlineDevice = model.PrimaryDevice!;
        Assert.Equal(LinkState.Offline, offlineDevice.Link);
        var icon = Presentation.Icon(offlineDevice, darkTaskbar: true, threshold: 15);
        Assert.Equal(BatteryTone.Idle, icon.Tone);
        Assert.Equal((byte)140, icon.Ink.Alpha);
        Assert.Equal("ATK 鼠标 · 已断开 · 上次 65%", Presentation.DeviceTooltip(model, offlineDevice));
        Assert.True(model.BuildMainPanel().HeroDisconnected);
    }

    [Fact]
    public void R1_ReceiverUnplugImmediatelyTransitionsAsleepDeviceToOffline()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([new(CompxMouse, 80, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        model.ApplyPresence([new("atk-1", null, CompxMouse.Id, LinkState.Asleep, ReadIssue.Timeout)], Now.AddSeconds(8));
        Assert.Equal(LinkState.Asleep, model.PrimaryDevice!.Link);

        // 接收器被拔出（非枚举失败且不在读取列表中）→ 立即离线。
        model.ApplyPoll([], scanFailed: false, Now.AddSeconds(12));
        Assert.Equal(LinkState.Offline, model.PrimaryDevice!.Link);
        Assert.Equal(BatteryTone.Idle, Presentation.Icon(model.PrimaryDevice, true, 15).Tone);
    }

    [Fact]
    public void R2AndR4_ContinuousSleepOverTenMinutesBecomesOfflineAndStaysOffline()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), TrayModel.SleepToOfflineTimeout);

        var model = new TrayModel(new(), new());
        List<(LinkState? From, LinkState To)> transitions = [];
        void Track(DeviceIdentity _, LinkState? from, LinkState to)
        {
            if (from != to)
                transitions.Add((from, to));
        }

        // t=0: 在线 55%。
        model.ApplyPoll([new(CompxMouse, 55, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now, onLinkChanged: Track);

        // t=8s: 进入休眠。
        var sleepStart = Now.AddSeconds(8);
        model.ApplyPresence([new("atk-1", null, CompxMouse.Id, LinkState.Asleep, ReadIssue.Timeout)], sleepStart, Track);
        Assert.Equal(LinkState.Asleep, model.PrimaryDevice!.Link);
        Assert.Equal(sleepStart + TimeSpan.FromMinutes(10), model.NextSleepTimeout(sleepStart));

        // 连续休眠 9 分 59 秒（中途仍有超时轮询/心跳）：保持休眠、图标不变灰。
        var almost = sleepStart.AddMinutes(9).AddSeconds(59);
        model.ApplyPoll([new(CompxMouse, null, false, ChargingState.Unknown, LinkState.Asleep, almost, ReadIssue.Timeout)],
            false, almost, onLinkChanged: Track);
        Assert.False(model.AdvanceSleepTimeouts(almost, Track));
        Assert.Equal(LinkState.Asleep, model.PrimaryDevice!.Link);
        Assert.Equal(BatteryTone.Normal, Presentation.Icon(model.PrimaryDevice, true, 15).Tone);

        // 达到连续休眠 10 分钟仍无任何回复：转为离线并变灰。
        var timeoutAt = sleepStart.AddMinutes(10);
        Assert.True(model.AdvanceSleepTimeouts(timeoutAt, Track));
        Assert.Equal(LinkState.Offline, model.PrimaryDevice!.Link);
        Assert.Equal(BatteryTone.Idle, Presentation.Icon(model.PrimaryDevice, true, 15).Tone);
        Assert.Equal("ATK 鼠标 · 已断开 · 上次 55%", Presentation.DeviceTooltip(model, model.PrimaryDevice));
        Assert.Contains((LinkState.Asleep, LinkState.Offline), transitions);

        // 转为离线后，后续心跳再次超时（返回 Asleep/Timeout）不得把设备拉回休眠态，防止来回闪烁。
        var (reverted, _) = model.ApplyPresence(
            [new("atk-1", null, CompxMouse.Id, LinkState.Asleep, ReadIssue.Timeout)],
            timeoutAt.AddSeconds(8), Track);
        Assert.False(reverted);
        Assert.Equal(LinkState.Offline, model.PrimaryDevice!.Link);
    }

    [Fact]
    public void R3AndR4_InputTickWakesAsleepDeviceMaxOncePerTwoSecondsWithoutGrayFlash()
    {
        long t = 500_000L;
        var schedule = new PollSchedule(t);
        var model = new TrayModel(new() { PollSeconds = 60 }, new());

        schedule.Start(t, game: false);
        model.ApplyPoll([new(CompxMouse, 88, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        schedule.Complete();

        // 设备在 t+8s 进入休眠，同步当前 GetLastInputInfo 时间戳为 1000。
        t += 8_000L;
        model.ApplyPresence([new("atk-1", null, CompxMouse.Id, LinkState.Asleep, ReadIssue.Timeout)], Now.AddSeconds(8));
        Assert.True(model.HasAsleepDevices);
        Assert.Contains(CompxMouse.Id, model.AsleepDeviceIds());
        schedule.SyncLastInputTick(1000u);

        // 没有新的键鼠输入（dwTime 不变）→ 不触发补心跳。
        Assert.False(schedule.ShouldWakeHeartbeatOnInput(1000u, t + 500, hasAsleepHeartbeatDevices: true));

        // 动一下鼠标（dwTime 变化为 1500）→ 立即触发一次补心跳。
        Assert.True(schedule.ShouldWakeHeartbeatOnInput(1500u, t + 1_000, hasAsleepHeartbeatDevices: true));
        schedule.StartHeartbeat(t + 1_000, inputWake: true);
        // 心跳运行期间不再并发触发。
        Assert.False(schedule.ShouldWakeHeartbeatOnInput(1600u, t + 1_100, hasAsleepHeartbeatDevices: true));
        schedule.CompleteHeartbeat();

        // 2 秒冷却期内持续有输入 → 限流，最多每 2 秒一次。
        Assert.False(schedule.ShouldWakeHeartbeatOnInput(2000u, t + 2_500, hasAsleepHeartbeatDevices: true));
        Assert.False(schedule.ShouldWakeHeartbeatOnInput(2400u, t + 2_999, hasAsleepHeartbeatDevices: true));

        // 满 2 秒后再次有输入 → 允许补查一次。
        Assert.True(schedule.ShouldWakeHeartbeatOnInput(3000u, t + 3_000, hasAsleepHeartbeatDevices: true));
        // 游戏中不触发 2 秒高频唤醒，保持游戏退避。
        Assert.False(schedule.ShouldWakeHeartbeatOnInput(3500u, t + 6_000, hasAsleepHeartbeatDevices: true, game: true));

        // 唤醒心跳返回 Online：图标在完整电量刷新回来之前保持非灰（PendingRefresh），不闪烁。
        var (changed, needsRefresh) = model.ApplyPresence(
            [new("atk-1", null, CompxMouse.Id, LinkState.Online, ReadIssue.None)], Now.AddSeconds(11));
        Assert.True(changed);
        Assert.True(needsRefresh);
        Assert.Equal(LinkState.Online, model.PrimaryDevice!.Link);
        Assert.True(model.PrimaryDevice.PendingRefresh);
        Assert.Equal(BatteryTone.Normal, Presentation.Icon(model.PrimaryDevice, true, 15).Tone);
        Assert.Equal((byte)255, Presentation.Icon(model.PrimaryDevice, true, 15).Ink.Alpha);

        // 随后的完整电量查询返回最新值 87%，清除 PendingRefresh 并标记 Fresh。
        model.ApplyPoll([new(CompxMouse, 87, false, ChargingState.NotCharging, LinkState.Online, Now.AddSeconds(12))],
            false, Now.AddSeconds(12));
        Assert.True(model.PrimaryDevice.Fresh);
        Assert.False(model.PrimaryDevice.PendingRefresh);
        Assert.Equal(87, model.PrimaryDevice.Percentage);
    }

    [Fact]
    public void A4_FeedbackUrlCarriesNoReportOrDeviceInfoAndStatusTracksCopy()
    {
        Assert.Equal("https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml",
            DiagnosticExport.DeviceReportIssueUrl);
        var uri = new Uri(DiagnosticExport.DeviceReportIssueUrl);
        Assert.Equal("?template=device-report.yml", uri.Query);

        var export = new DiagnosticExport(Path.Combine(Path.GetTempPath(), "diag-test.txt"));
        Assert.False(export.HasReport);
        export.PreviewOpened();
        Assert.True(export.HasReport);
        export.FeedbackCopied();
        Assert.True(export.HasReport);
        Assert.Equal("已复制报告，请在浏览器粘贴", DiagnosticExport.Label(export.Status));
        export.FeedbackFailed();
        Assert.True(export.HasReport);
        Assert.Equal("复制或打开反馈页失败", DiagnosticExport.Label(export.Status));
    }

    [Fact]
    public void B1AndB2_PanelBadgesRemainThreeTypesWhileReadingCatalogVerificationData()
    {
        // 已实机验证（由核心目录驱动）。
        Assert.Equal(AdaptationBadge.Verified, DeviceVerification.Badge(H9));
        Assert.Equal("已实机验证", DeviceVerification.Label(H9));
        var htx = new DeviceIdentity("mouse:gw", "G-Wolves HTX Ultra 8K", DeviceKind.Mouse,
            AdaptationSource.Catalog, VerificationTier.HardwareVerified);
        Assert.Equal("已实机验证", DeviceVerification.Label(htx));

        // 同家族推定与仅按资料在面板均显示「按协议适配」，用户看到仍保持三种徽标。
        var fenrirPro = new DeviceIdentity("mouse:gw2", "G-Wolves Fenrir Pro 8K", DeviceKind.Mouse,
            AdaptationSource.Catalog, VerificationTier.FamilyInferred);
        Assert.Equal(AdaptationBadge.Protocol, DeviceVerification.Badge(fenrirPro));
        Assert.Equal("按协议适配", DeviceVerification.Label(fenrirPro));

        var atkCatalog = new DeviceIdentity("mouse:atk", "ATK F1 Ultimate", DeviceKind.Mouse,
            AdaptationSource.Catalog, VerificationTier.DocsOnly);
        Assert.Equal(AdaptationBadge.Protocol, DeviceVerification.Badge(atkCatalog));

        // 通用适配继续显示「通用适配」。
        var generic = new DeviceIdentity("mouse:gen", "ATK 鼠标", DeviceKind.Mouse, AdaptationSource.Generic);
        Assert.Equal(AdaptationBadge.Generic, DeviceVerification.Badge(generic));
        Assert.Equal("通用适配", DeviceVerification.Label(generic));
    }

    [Fact]
    public void C2AndC3_PrereleaseDisplaysCommitShaAndUpdaterNeverDowngradesToLowerStable()
    {
        // C2：测试版版本号带提交号格式化。
        Assert.Equal("0.2.3", ReleaseVersion.FormatDisplay("0.2.3"));
        Assert.Equal("0.2.3 测试版（abc1234）", ReleaseVersion.FormatDisplay("0.2.3-beta.abc1234"));
        Assert.Equal("0.2.3 测试版（abc1234）", ReleaseVersion.FormatDisplay("0.2.3-beta.ABC12349999+deadbeef"));
        Assert.True(ReleaseVersion.TryParseInstalled("0.2.3-beta.abc1234", out var installed, out bool isPre, out var commit));
        Assert.Equal(new Version(0, 2, 3), installed);
        Assert.True(isPre);
        Assert.Equal("abc1234", commit);

        // C3：检查更新固定查询 releases/latest（GitHub 正式版端点，不包含 pre-release）。
        Assert.EndsWith("/releases/latest", UpdateClient.Latest.AbsoluteUri, StringComparison.Ordinal);

        // 装了 0.2.3 测试版的用户，面对 0.2.2（更低正式版）或 0.2.3（同基础版本）都不会收到降级更新提示。
        var lowerStable = MakeRelease("v0.2.2", prerelease: false);
        var sameStable = MakeRelease("v0.2.3", prerelease: false);
        var higherStable = MakeRelease("v0.2.4", prerelease: false);
        var higherBeta = MakeRelease("v0.2.9", prerelease: true);

        Assert.Null(AvailableUpdate.Select(lowerStable, installed));
        Assert.Null(AvailableUpdate.Select(sameStable, installed));
        var upgrade = Assert.IsType<AvailableUpdate>(AvailableUpdate.Select(higherStable, installed));
        Assert.Equal(new Version(0, 2, 4), upgrade.Version);
        Assert.Throws<InvalidDataException>(() => AvailableUpdate.Select(higherBeta, installed));
    }

    [Fact]
    public void D1_UnchangedBatteryAndStateSkipsWritingStateJson()
    {
        var folder = Directory.CreateTempSubdirectory("jige-d1-test-");
        try
        {
            var files = new TrayFiles(folder.FullName);
            var model = new TrayModel(new(), new());

            // 首次读到 80%：需要落盘，SaveState 返回 true。
            Assert.True(model.ApplyPoll(
                [new(CompxMouse, 80, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now));
            var firstSnapshot = model.Snapshot();
            Assert.True(files.SaveState(firstSnapshot));
            Assert.True(File.Exists(files.StatePath));

            // 再次保存完全一致的快照：写盘前比较字节一致，直接跳过写盘并返回 false。
            var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.StatePath, stamp);
            Assert.False(files.SaveState(firstSnapshot));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(files.StatePath));

            // 之后即便间隔超过 10 分钟，只要电量和状态都没变化，ApplyPoll 返回 false（不再 5 分钟强制写盘）。
            for (int m = 1; m <= 12; m++)
            {
                var nextTime = Now.AddMinutes(m);
                bool shouldSave = model.ApplyPoll(
                    [new(CompxMouse, 80, false, ChargingState.NotCharging, LinkState.Online, nextTime)], false, nextTime);
                Assert.False(shouldSave);
            }

            // 电量降到 79% 时，ApplyPoll 返回 true 且 SaveState 真正写入磁盘。
            var dropTime = Now.AddMinutes(13);
            Assert.True(model.ApplyPoll(
                [new(CompxMouse, 79, false, ChargingState.NotCharging, LinkState.Online, dropTime)], false, dropTime));
            Assert.True(files.SaveState(model.Snapshot()));
            Assert.Equal(79, files.LoadState().Readings[CompxMouse.Id].Percentage);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void D2_SettingsAnimationSkipsRedrawWhenVisualStateDoesNotChange()
    {
        var panel = new SettingsPanel(700);
        panel.Toggle(SettingRow.Threshold, 0, animate: true, selectedIndex: 1);

        // 同一毫秒再次推进：像素高度与进度无变化，不触发重绘。
        Assert.False(panel.Advance(0, scale: 1.5f));

        // 活跃动画阶段：画面明显变化，触发重绘。
        Assert.True(panel.Advance(30, scale: 1.5f));

        // 缓动曲线尾段几乎平缓（235ms → 236ms 无像素/角度变化）：跳过多余重绘。
        panel.Advance(235, scale: 1.0f);
        Assert.False(panel.Advance(236, scale: 1.0f));

        // 到达终点（250ms）：收尾帧必须重绘一次；之后动画结束不再重绘。
        Assert.True(panel.Advance(250, scale: 1.0f));
        Assert.False(panel.Animating);
        Assert.False(panel.Advance(265, scale: 1.0f));
    }

    [Fact]
    public void D3_DesktopRefreshDebouncerFiltersCoalescesAndDefersDuringInlineRename()
    {
        // 无关的 WM_SETTINGCHANGE（如环境变量/区域设置）被直接忽略。
        Assert.False(DesktopRefreshDebouncer.IsRelevantSettingChange(0, "Environment"));
        Assert.False(DesktopRefreshDebouncer.IsRelevantSettingChange(0, "intl"));
        Assert.True(DesktopRefreshDebouncer.IsRelevantSettingChange(0, "ImmersiveColorSet"));
        Assert.True(DesktopRefreshDebouncer.IsRelevantSettingChange(0, "WindowsThemeElement"));
        Assert.True(DesktopRefreshDebouncer.IsRelevantSettingChange(0x002F, null)); // SPI_SETWORKAREA
        Assert.True(DesktopRefreshDebouncer.IsRelevantSettingChange(0x1043, null)); // SPI_SETCLIENTAREAANIMATION

        // 连续收到多条主题/DPI 变化消息：防抖合并为一次重建，且保留 resetPopup = true。
        var debouncer = new DesktopRefreshDebouncer();
        debouncer.Request(1_000, resetPopup: true);
        debouncer.Request(1_040, resetPopup: false);
        debouncer.Request(1_090, resetPopup: false);
        Assert.False(debouncer.TryConsume(1_150, isEditing: false, out _));
        Assert.False(debouncer.TryConsume(1_239, isEditing: false, out _));

        // 正在行内改名时到达防抖终点：延后提交新外观，不打断/不强制提交未完成别名。
        Assert.False(debouncer.TryConsume(1_240, isEditing: true, out _));
        Assert.True(debouncer.HasPending);

        // 改名结束后立即消费一次并执行重建，随后不再重复重建。
        Assert.True(debouncer.TryConsume(1_250, isEditing: false, out bool resetPopup));
        Assert.True(resetPopup);
        Assert.False(debouncer.HasPending);
        Assert.False(debouncer.TryConsume(1_300, isEditing: false, out _));
    }

    private static ReleaseResponse MakeRelease(string tag, bool prerelease)
    {
        string version = tag.TrimStart('v');
        string package = $"Jige-Setup-{version}-x64.exe";
        string root = $"{AvailableUpdate.Repository}/releases/download/{tag}/";
        string json = $$"""
            {"tag_name":"{{tag}}","body":"更新说明","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"assets":[
                {"name":"{{package}}","size":1024,"browser_download_url":"{{root}}{{package}}"},
                {"name":"SHA256SUMS.txt","size":96,"browser_download_url":"{{root}}SHA256SUMS.txt"}
            ]}
            """;
        return JsonSerializer.Deserialize(json, ReleaseJsonContext.Default.ReleaseResponse)!;
    }

    [Fact]
    public void R3_InputWakeBacksOffWhenAsleepDeviceNeverAnswers()
    {
        long t = 1_000_000;
        var schedule = new PollSchedule(t);
        uint input = 1000;
        schedule.SyncLastInputTick(input);

        // 设备一直不回复：每次补心跳后冷却依次为 2、4、8、16、30、30 秒。
        long[] expected = [2_000, 4_000, 8_000, 16_000, 30_000, 30_000];
        Assert.True(schedule.ShouldWakeHeartbeatOnInput(++input, t, hasAsleepHeartbeatDevices: true));
        foreach (long cooldown in expected)
        {
            schedule.StartHeartbeat(t, inputWake: true);
            schedule.CompleteHeartbeat();
            Assert.Equal(cooldown, schedule.InputWakeCooldown);
            Assert.False(schedule.ShouldWakeHeartbeatOnInput(++input, t + cooldown - 1, hasAsleepHeartbeatDevices: true));
            t += cooldown;
            Assert.True(schedule.ShouldWakeHeartbeatOnInput(++input, t, hasAsleepHeartbeatDevices: true));
        }

        // 设备重新进入休眠（主循环同步输入基线）后，冷却回到 2 秒。
        schedule.SyncLastInputTick(input);
        schedule.StartHeartbeat(t, inputWake: true);
        schedule.CompleteHeartbeat();
        Assert.Equal(2_000, schedule.InputWakeCooldown);
    }
}

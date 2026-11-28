using Jige.Core;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class PanelPagesTests
{
    [Theory]
    [InlineData((int)PanelPage.Main, 0, 700, 157, 0)]
    [InlineData((int)PanelPage.Main, 1, 700, 247, 0)]
    [InlineData((int)PanelPage.Main, 30, 400, 391, 3)]
    [InlineData((int)PanelPage.Settings, 0, 800, 519, 0)]
    [InlineData((int)PanelPage.Settings, 10, 800, 519, 0)]
    [InlineData((int)PanelPage.Settings, 8, 800, 439, 0)]
    [InlineData((int)PanelPage.Devices, 0, 700, 171, 0)]
    [InlineData((int)PanelPage.Devices, 5, 700, 443, 5)]
    [InlineData((int)PanelPage.Devices, 40, 442, 388, 4)]
    [InlineData((int)PanelPage.Devices, 40, 443, 443, 5)]
    public void PageHeightIncludesChromeAndOnlyWholeRows(int page, int count, int available, int height, int rows)
    {
        Assert.Equal(new PanelSize(height, rows), PanelLayout.Size((PanelPage)page, count, available));
    }

    [Fact]
    public void DiagnosticReportFooterExpandsSettingsAndDevicesHeightsCleanly()
    {
        int extra = PanelLayout.DiagnosticFooterExtraHeight;
        Assert.Equal(PanelLayout.DiagnosticFooterHeight - PanelLayout.FooterHeight, extra);
        Assert.True(extra > 0);

        int taskbarHeight = PanelLayout.SettingsHeight(PanelLayout.SettingsMaxRowCount);
        int trayHeight = PanelLayout.SettingsHeight(PanelLayout.SettingsTrayRowCount);
        Assert.Equal(new PanelSize(taskbarHeight + extra, 0),
            PanelLayout.Size(PanelPage.Settings, PanelLayout.SettingsMaxRowCount, 800, diagnosticFooter: true));
        Assert.Equal(new PanelSize(trayHeight + extra, 0),
            PanelLayout.Size(PanelPage.Settings, PanelLayout.SettingsTrayRowCount, 800, diagnosticFooter: true));
        Assert.Equal(new PanelSize(PanelLayout.DeviceBaseHeight + extra, 0),
            PanelLayout.Size(PanelPage.Devices, 0, 700, diagnosticFooter: true));
        Assert.Equal(new PanelSize(443 + extra, 5),
            PanelLayout.Size(PanelPage.Devices, 5, 700, diagnosticFooter: true));

        var panel = new SettingsPanel(920, hasDiagnosticFooter: true);
        Assert.True(panel.HasDiagnosticFooter);
        Assert.Equal(PanelLayout.DiagnosticFooterHeight, panel.FooterHeight);
        Assert.Equal(taskbarHeight + extra, panel.Height);
        // 卡片底部与底部分割线间距在有无诊断报告时保持一致（8px）。
        Assert.Equal(taskbarHeight - PanelLayout.FooterHeight, panel.Height - panel.FooterHeight);

        panel.Toggle(SettingRow.Threshold, 0, animate: false, selectedIndex: 1);
        // 「电量提醒」展开 4 个阈值 + 2 个勾选项，可视区容纳全部 6 项。
        Assert.Equal(taskbarHeight + 6 * SettingsPanel.OptionHeight + extra, panel.Height);
        Assert.Equal(taskbarHeight + 6 * SettingsPanel.OptionHeight - PanelLayout.FooterHeight, panel.Height - panel.FooterHeight);
    }

    [Theory]
    [InlineData(0, 0, 519)]
    [InlineData(50, 0.7944, 681)]
    [InlineData(250, 1, 723)]
    public void ResizeUsesPrototypeCurveAndKeepsBottomAgainstTray(int elapsed, double expected, int expectedHeight)
    {
        double eased = PanelLayout.Ease(elapsed / 250d);
        Assert.Equal(expected, eased, 4);
        var settings = new SettingsPanel(900);
        settings.Toggle(SettingRow.Threshold, 0, animate: true, selectedIndex: 1);
        settings.Advance(elapsed);
        int height = (int)Math.Round(settings.Height);
        var tray = new Bounds(1830, 1040, 1850, 1080);
        var work = new Bounds(0, 0, 1920, 1040);
        var screen = new Bounds(0, 0, 1920, 1080);
        var popup = PopupPlacement.Place(tray, screen, work, 344, height, 12, false);
        Assert.Equal(1028, popup.Bottom);
        Assert.Equal(expectedHeight, popup.Height);
    }

    [Theory]
    [InlineData(null, 519, 48, 88, 128, 168, 218, 258, 298, 348, 388, 428)]
    [InlineData((int)SettingRow.DisplayLocation, 587, 48, 156, 196, 236, 286, 326, 366, 416, 456, 496)]
    [InlineData((int)SettingRow.TrayMode, 587, 48, 88, 196, 236, 286, 326, 366, 416, 456, 496)]
    [InlineData((int)SettingRow.TaskbarStyle, 689, 48, 88, 128, 338, 388, 428, 468, 518, 558, 598)]
    [InlineData((int)SettingRow.TaskbarColor, 723, 48, 88, 128, 168, 422, 462, 502, 552, 592, 632)]
    [InlineData((int)SettingRow.Threshold, 723, 48, 88, 128, 168, 218, 462, 502, 552, 592, 632)]
    [InlineData((int)SettingRow.PollInterval, 621, 48, 88, 128, 168, 218, 258, 298, 450, 490, 530)]
    [InlineData((int)SettingRow.Theme, 621, 48, 88, 128, 168, 218, 258, 298, 348, 490, 530)]
    public void SettingsRowsAndHitAreasFollowExpandedOptions(int? expanded, int height, params int[] tops)
    {
        var panel = new SettingsPanel(900);
        if (expanded is int expandedRow)
            Assert.True(panel.Toggle((SettingRow)expandedRow, 0, animate: false, selectedIndex: 0));

        Assert.Equal(SettingsPanel.Rows.Length, tops.Length);
        foreach (var row in SettingsPanel.Rows)
        {
            var rect = panel.RowRect(row);
            int y = tops[(int)row];
            Assert.Equal(new PanelRect(10, y, 324, 40), rect);
            Assert.True(rect.Contains(10, y));
            Assert.True(rect.Contains(333, y + 39));
            Assert.False(rect.Contains(9, y));
            Assert.False(rect.Contains(334, y));
            Assert.False(rect.Contains(10, y + 40));
        }

        if (panel.Expanded is { } active)
        {
            int top = tops[(int)active] + 40;
            for (int index = 0; index < panel.VisibleCount(active); index++)
            {
                var rect = panel.OptionHitRect(active, index);
                Assert.Equal(new PanelRect(10, top + index * 34, 324, 34), rect);
                Assert.True(rect.Contains(100, top + index * 34));
                Assert.False(rect.Contains(100, top + (index + 1) * 34));
            }
        }
        Assert.Equal(height, panel.Height);
    }

    [Fact]
    public void SettingsAreGroupedIntoThreeCardsWithoutHeaders()
    {
        var panel = new SettingsPanel(900);
        Assert.Equal([4, 3, 3], SettingsPanel.SectionSizes);
        Assert.Equal(new PanelRect(9, 47, 326, 162), panel.SectionRect(0));
        Assert.Equal(new PanelRect(9, 217, 326, 122), panel.SectionRect(1));
        Assert.Equal(new PanelRect(9, 347, 326, 122), panel.SectionRect(2));
        // 最后一组卡片底部到底栏分割线仍留 8px。
        Assert.Equal(panel.Height - panel.FooterHeight - 8, panel.SectionRect(2).Y + panel.SectionRect(2).Height);

        panel.Toggle(SettingRow.Theme, 0, animate: false, selectedIndex: 0);
        Assert.Equal(122 + 3 * 34, panel.SectionRect(2).Height);
        Assert.Equal(panel.Height - panel.FooterHeight - 8, panel.SectionRect(2).Y + panel.SectionRect(2).Height);
    }

    [Fact]
    public void TrayIconsModeHidesTaskbarStyleAndColorRowsWithoutHitOrToggle()
    {
        var panel = new SettingsPanel(900, showTaskbarRows: false);
        Assert.False(panel.ShowTaskbarRows);
        Assert.Equal(PanelLayout.SettingsTrayRowCount, panel.VisibleRowCount);
        Assert.Equal(439, panel.Height);

        Assert.False(panel.IsVisible(SettingRow.TaskbarStyle));
        Assert.False(panel.IsVisible(SettingRow.TaskbarColor));
        Assert.True(panel.IsVisible(SettingRow.DisplayLocation));
        Assert.True(panel.IsVisible(SettingRow.TrayMode));
        Assert.True(panel.IsSectionEnd(SettingRow.TrayMode));
        Assert.False(panel.IsSectionEnd(SettingRow.TaskbarColor));

        // 隐藏行无点击热区，也不可展开。
        var styleRect = panel.RowRect(SettingRow.TaskbarStyle);
        var colorRect = panel.RowRect(SettingRow.TaskbarColor);
        Assert.Equal(0, styleRect.Height);
        Assert.Equal(0, colorRect.Height);
        Assert.False(styleRect.Contains(100, styleRect.Y));
        Assert.False(colorRect.Contains(100, colorRect.Y));
        Assert.False(panel.Toggle(SettingRow.TaskbarStyle, 0, animate: false, selectedIndex: 0));
        Assert.False(panel.Toggle(SettingRow.TaskbarColor, 0, animate: false, selectedIndex: 0));
        Assert.Null(panel.Expanded);

        // 三组卡片收紧为 2 + 3 + 3 行，底部留白保持 8px。
        Assert.Equal(new PanelRect(9, 47, 326, 82), panel.SectionRect(0));
        Assert.Equal(new PanelRect(9, 137, 326, 122), panel.SectionRect(1));
        Assert.Equal(new PanelRect(9, 267, 326, 122), panel.SectionRect(2));
        Assert.Equal(panel.Height - panel.FooterHeight - 8, panel.SectionRect(2).Y + panel.SectionRect(2).Height);

        // 显示位置偏好为任务栏但因故回退托盘时，仍保持任务栏两行可见。
        var fallbackModel = new TrayModel(new() { DisplayLocation = TrayDisplayLocation.Taskbar }, new());
        Assert.Equal("已回退托盘", SettingsPanel.DisplayLocationLabel(fallbackModel.Settings.DisplayLocation, "空间不足"));
        var fallbackPanel = new SettingsPanel(900,
            showTaskbarRows: fallbackModel.Settings.DisplayLocation != TrayDisplayLocation.TrayIcons);
        Assert.True(fallbackPanel.ShowTaskbarRows);
        Assert.True(fallbackPanel.IsVisible(SettingRow.TaskbarStyle));
        Assert.True(fallbackPanel.IsVisible(SettingRow.TaskbarColor));
        Assert.Equal(40, fallbackPanel.RowRect(SettingRow.TaskbarStyle).Height);
        Assert.Equal(40, fallbackPanel.RowRect(SettingRow.TaskbarColor).Height);
    }

    [Fact]
    public void SwitchingDisplayLocationAnimatesSettingsHeightSmoothlyAndUpdatesVisibility()
    {
        var model = new TrayModel(new() { DisplayLocation = TrayDisplayLocation.Taskbar }, new());
        var panel = new SettingsPanel(900, showTaskbarRows: true);
        Assert.Equal(519, panel.Height);

        // 展开「显示位置」后切换到「托盘图标」：从展开高度平滑收拢到 8 行高度，不能跳变。
        Assert.True(panel.Toggle(SettingRow.DisplayLocation, 0, animate: false, selectedIndex: 0));
        Assert.Equal(587, panel.Height);

        Assert.True(panel.Select(new(PopupAction.DisplayLocation, (int)TrayDisplayLocation.TrayIcons),
            model, time: 100, animate: true, next => model.ApplySettings(next, DateTimeOffset.UnixEpoch)));
        Assert.Null(panel.Expanded);
        Assert.False(panel.ShowTaskbarRows);
        Assert.True(panel.Animating);
        Assert.Equal(587, panel.Height);
        Assert.Equal(0, panel.RowRect(SettingRow.TaskbarStyle).Height);
        Assert.Equal(0, panel.RowRect(SettingRow.TaskbarColor).Height);

        Assert.True(panel.Advance(150));
        Assert.InRange(panel.Height, 440, 586);
        Assert.InRange(panel.TaskbarRowsProgress, 0.01f, 0.99f);

        Assert.True(panel.Advance(350));
        Assert.False(panel.Animating);
        Assert.Equal(0, panel.TaskbarRowsProgress);
        Assert.Equal(439, panel.Height);
        Assert.Equal(panel.Height - panel.FooterHeight - 8, panel.SectionRect(2).Y + panel.SectionRect(2).Height);

        // 切回「任务栏」：平滑恢复 10 行高度与点击热区。
        Assert.True(panel.Toggle(SettingRow.DisplayLocation, 400, animate: false, selectedIndex: 1));
        Assert.Equal(507, panel.Height);
        Assert.True(panel.Select(new(PopupAction.DisplayLocation, (int)TrayDisplayLocation.Taskbar),
            model, time: 500, animate: true, next => model.ApplySettings(next, DateTimeOffset.UnixEpoch)));
        Assert.True(panel.ShowTaskbarRows);
        Assert.True(panel.Animating);
        Assert.Equal(507, panel.Height);

        Assert.True(panel.Advance(550));
        Assert.InRange(panel.Height, 508, 518);
        Assert.True(panel.Advance(750));
        Assert.False(panel.Animating);
        Assert.Equal(1, panel.TaskbarRowsProgress);
        Assert.Equal(519, panel.Height);
        Assert.Equal(40, panel.RowRect(SettingRow.TaskbarStyle).Height);
        Assert.Equal(40, panel.RowRect(SettingRow.TaskbarColor).Height);
        Assert.True(panel.Toggle(SettingRow.TaskbarStyle, 800, animate: false, selectedIndex: 0));
    }

    [Fact]
    public void SettingsHeightAdaptsToVisibleRowCountAndCompactThreshold()
    {
        Assert.Equal(519, PanelLayout.SettingsHeight(10));
        Assert.Equal(439, PanelLayout.SettingsHeight(8));
        Assert.Equal(479, PanelLayout.SettingsCompactHeight(10));
        Assert.Equal(407, PanelLayout.SettingsCompactHeight(8));
        // 任务栏模式总高度较原版 659px 至少矮 100px。
        Assert.True(659 - PanelLayout.SettingsHeight(PanelLayout.SettingsMaxRowCount) >= 100);

        int taskbarThreshold = PanelLayout.SettingsHeight(10) + PanelLayout.DiagnosticFooterExtraHeight + SettingsPanel.OptionHeight;
        int trayThreshold = PanelLayout.SettingsHeight(8) + PanelLayout.DiagnosticFooterExtraHeight + SettingsPanel.OptionHeight;
        Assert.False(PanelLayout.CompactSettings(taskbarThreshold, 10));
        Assert.True(PanelLayout.CompactSettings(taskbarThreshold - 1, 10));
        Assert.False(PanelLayout.CompactSettings(trayThreshold, 8));
        Assert.True(PanelLayout.CompactSettings(trayThreshold - 1, 8));

        // 中等高度（520px）：10 行启用紧凑行高 36（479px），8 行保持常规行高 40（439px）。
        Assert.Equal(479, PanelLayout.SettingsHeightFor(520, 10));
        Assert.Equal(439, PanelLayout.SettingsHeightFor(520, 8));
        var mediumPanel = new SettingsPanel(520, showTaskbarRows: true);
        Assert.True(mediumPanel.Compact);
        Assert.Equal(479, mediumPanel.Height);
        Assert.True(mediumPanel.SetShowTaskbarRows(false, time: 0, animate: true));
        Assert.Equal(479, mediumPanel.Height);
        Assert.True(mediumPanel.Advance(250));
        Assert.False(mediumPanel.Compact);
        Assert.Equal(439, mediumPanel.Height);
        Assert.Equal(40, mediumPanel.RowRect(SettingRow.DisplayLocation).Height);

        // 展开「显示位置」后从托盘切回任务栏（紧凑模式切换 + 展开容量变化），起始帧高度依然连续无跳变。
        var model = new TrayModel(new() { DisplayLocation = TrayDisplayLocation.TrayIcons }, new());
        Assert.True(mediumPanel.Toggle(SettingRow.DisplayLocation, 300, animate: false, selectedIndex: 1));
        float expandedHeight = mediumPanel.Height;
        Assert.Equal(507, expandedHeight);
        Assert.True(mediumPanel.Select(new(PopupAction.DisplayLocation, (int)TrayDisplayLocation.Taskbar),
            model, time: 350, animate: true, next => model.ApplySettings(next, DateTimeOffset.UnixEpoch)));
        Assert.Equal(expandedHeight, mediumPanel.Height);
        Assert.True(mediumPanel.Advance(600));
        Assert.True(mediumPanel.Compact);
        Assert.Equal(479, mediumPanel.Height);
    }

    [Fact]
    public void ShortScreensTightenRowHeightInsteadOfShrinkingThePanel()
    {
        int threshold = PanelLayout.SettingsHeight() + PanelLayout.DiagnosticFooterExtraHeight + SettingsPanel.OptionHeight;
        Assert.False(PanelLayout.CompactSettings(threshold));
        Assert.True(PanelLayout.CompactSettings(threshold - 1));
        Assert.Equal(new PanelSize(PanelLayout.SettingsCompactHeight(10), 0), PanelLayout.Size(PanelPage.Settings, 10, 490));
        Assert.Equal(new PanelSize(PanelLayout.SettingsCompactHeight(8), 0), PanelLayout.Size(PanelPage.Settings, 8, 490));

        var panel = new SettingsPanel(490);
        Assert.True(panel.Compact);
        Assert.Equal(PanelLayout.SettingsCompactRowHeight, panel.RowHeight);
        Assert.Equal(PanelLayout.SettingsCompactHeight(10), panel.Height);
        Assert.Equal(new PanelRect(9, 47, 326, 146), panel.SectionRect(0));
        Assert.Equal(new PanelRect(9, 201, 326, 110), panel.SectionRect(1));
        Assert.Equal(new PanelRect(9, 319, 326, 110), panel.SectionRect(2));
        // 最后一组卡片底部到底栏分割线同样留 8px。
        Assert.Equal(panel.Height - panel.FooterHeight - 8, panel.SectionRect(2).Y + panel.SectionRect(2).Height);
    }

    [Fact]
    public void SwitchingOptionsIsExclusiveAndKeepsHeightContinuous()
    {
        var panel = new SettingsPanel(900);
        panel.Toggle(SettingRow.Threshold, 0, animate: true, selectedIndex: 1);
        panel.Advance(50);
        float beforeSwitch = panel.Height;
        Assert.InRange(beforeSwitch, 677, 685);
        Assert.True(panel.OptionHitRect(SettingRow.Threshold, 4).Height is > 0 and < 34);

        panel.Toggle(SettingRow.PollInterval, 50, animate: true, selectedIndex: 1);
        Assert.Equal(SettingRow.PollInterval, panel.Expanded);
        Assert.Equal(beforeSwitch, panel.Height);
        Assert.Equal(0, panel.OptionHitRect(SettingRow.Threshold, 0).Height);
        panel.Advance(300);
        Assert.Equal(621, panel.Height);
        Assert.Equal(0, panel.VisibleHeight(SettingRow.Threshold));
        Assert.Equal(34, panel.OptionHitRect(SettingRow.PollInterval, 0).Height);
        Assert.False(panel.Animating);

        Assert.True(panel.Collapse(300, animate: true));
        Assert.False(panel.Collapse(301, animate: true));
        Assert.Equal(621, panel.Height);
        Assert.Equal(0, panel.OptionHitRect(SettingRow.PollInterval, 0).Height);
        panel.Advance(550);
        Assert.Equal(519, panel.Height);
    }

    [Theory]
    [InlineData((int)SettingRow.TrayMode, (int)PopupAction.TrayMode, (int)TrayDisplayMode.LowestBattery, 15, 60, (int)TrayDisplayMode.LowestBattery)]
    [InlineData((int)SettingRow.Threshold, (int)PopupAction.Threshold, 30, 30, 60, (int)TrayDisplayMode.Custom)]
    [InlineData((int)SettingRow.PollInterval, (int)PopupAction.PollInterval, 120, 15, 120, (int)TrayDisplayMode.Custom)]
    public void SelectingAnOptionSavesIt(int row, int action, int value, int threshold, int poll, int expectedMode)
    {
        var panel = new SettingsPanel(900);
        var time = DateTimeOffset.UnixEpoch;
        var model = new TrayModel(new(), new());
        var directory = Directory.CreateTempSubdirectory("jige-settings-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            var target = new PopupTarget((PopupAction)action, value);
            Assert.False(panel.Select(target, model, 0, animate: false, Save));
            Assert.False(File.Exists(files.ConfigPath));

            panel.Toggle((SettingRow)row, 0, animate: false, selectedIndex: 0);
            Assert.True(panel.Select(target, model, 1, animate: false, Save));
            bool isTrayMode = (SettingRow)row == SettingRow.TrayMode;
            Assert.Equal(isTrayMode ? SettingRow.TrayMode : (SettingRow?)null, panel.Expanded);
            Assert.Equal(isTrayMode ? 587 : 519, panel.Height);
            Assert.True(files.TryLoadSettings(out var reloaded));
            Assert.Equal(threshold, reloaded.LowBatteryThreshold);
            Assert.Equal(poll, reloaded.PollSeconds);
            Assert.Equal((TrayDisplayMode)expectedMode, reloaded.TrayMode);

            void Save(TraySettings settings)
            {
                files.SaveSettings(settings.Normalize());
                model.ApplySettings(settings, time);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void CustomTrayCheckboxesUseTheSameSavedSelectionAsDeviceManagement()
    {
        var time = DateTimeOffset.UnixEpoch;
        var mouse = new DeviceIdentity("mouse", "MX Anywhere 3S", DeviceKind.Mouse);
        var keyboard = new DeviceIdentity("keyboard", "MX Keys S", DeviceKind.Keyboard);
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.LowestBattery }, new());
        model.ApplyPoll([
            new(mouse, 52, false, ChargingState.NotCharging, LinkState.Online, time),
            new(keyboard, 83, false, ChargingState.NotCharging, LinkState.Online, time)
        ], false, time);
        model.ApplyPresence([new("", null, keyboard.Id, LinkState.Offline)], time);
        var panel = new SettingsPanel(PanelLayout.SettingsHeight() + 3 * SettingsPanel.OptionHeight + 1);
        panel.UpdateTrayChoices(model, model.OrderedForManagement());
        panel.Toggle(SettingRow.TrayMode, 0, animate: false, selectedIndex: 0);
        Assert.Equal(2, panel.Choices(SettingRow.TrayMode).Length);

        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(folder.FullName);
            Assert.True(panel.Select(new(PopupAction.TrayMode, (int)TrayDisplayMode.Custom), model, 1, false, Save));
            Assert.Equal(SettingRow.TrayMode, panel.Expanded);
            var choices = panel.Choices(SettingRow.TrayMode);
            Assert.Equal(4, choices.Length);
            Assert.Equal(new PopupTarget(PopupAction.TrayToggle, DeviceId: keyboard.Id), choices[3].Target);
            Assert.Equal("MX Keys S", choices[3].Label);
            Assert.Equal(0, panel.OptionHitRect(SettingRow.TrayMode, 3).Height);
            Assert.True(panel.Scroll(1));
            Assert.Equal(SettingsPanel.OptionHeight, panel.OptionHitRect(SettingRow.TrayMode, 3).Height);

            var fromSettings = choices[2].Target;
            Assert.Equal(new PopupTarget(PopupAction.TrayToggle, DeviceId: mouse.Id), fromSettings);
            Save(model.ToggleTrayDevice(fromSettings.DeviceId!));
            Assert.False(model.Settings.IsInTray(mouse.Id));
            Assert.Empty(model.TrayDevices());
            Assert.True(files.TryLoadSettings(out var reloaded));
            Assert.Equal(TrayDisplayMode.Custom, reloaded.TrayMode);
            Assert.False(reloaded.IsInTray(mouse.Id));

            var fromDevices = new PopupTarget(PopupAction.TrayToggle, DeviceId: mouse.Id);
            Save(model.ToggleTrayDevice(fromDevices.DeviceId!));
            Assert.True(model.Settings.IsInTray(fromSettings.DeviceId!));
            Assert.Equal(mouse, Assert.Single(model.TrayDevices()).Device);
            Assert.True(files.TryLoadSettings(out reloaded));
            Assert.True(reloaded.IsInTray(mouse.Id));

            void Save(TraySettings settings)
            {
                files.SaveSettings(settings);
                model.ApplySettings(settings, time);
                panel.UpdateTrayChoices(model, model.OrderedForManagement());
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ThemeModeResolvesAgainstSystemAndRoundTripsInSettings()
    {
        Assert.True(Presentation.ResolveDark(ThemeMode.Auto, systemDark: true));
        Assert.False(Presentation.ResolveDark(ThemeMode.Auto, systemDark: false));
        Assert.False(Presentation.ResolveDark(ThemeMode.Light, systemDark: true));
        Assert.False(Presentation.ResolveDark(ThemeMode.Light, systemDark: false));
        Assert.True(Presentation.ResolveDark(ThemeMode.Dark, systemDark: true));
        Assert.True(Presentation.ResolveDark(ThemeMode.Dark, systemDark: false));

        Assert.Equal("自动切换", Presentation.ThemeModeLabel(ThemeMode.Auto));
        Assert.Equal("明亮模式", Presentation.ThemeModeLabel(ThemeMode.Light));
        Assert.Equal("深色模式", Presentation.ThemeModeLabel(ThemeMode.Dark));

        var normalized = (new TraySettings { Theme = (ThemeMode)99 }).Normalize();
        Assert.Equal(ThemeMode.Auto, normalized.Theme);

        var directory = Directory.CreateTempSubdirectory("jige-theme-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            const string legacy = """{"trayMode":"Custom","pollSeconds":60}""";
            File.WriteAllText(files.ConfigPath, legacy);
            Assert.True(files.TryLoadSettings(out var loaded));
            Assert.Equal(ThemeMode.Auto, loaded.Theme);
            Assert.Equal(legacy, File.ReadAllText(files.ConfigPath));

            var model = new TrayModel(loaded, new());
            var panel = new SettingsPanel(700);
            Assert.Equal(3, panel.Choices(SettingRow.Theme).Length);

            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.Auto })
            {
                Assert.True(panel.Toggle(SettingRow.Theme, 0, animate: false, selectedIndex: 0));
                Assert.True(panel.Select(new(PopupAction.Theme, (int)mode), model, 1, animate: false, Save));
                Assert.Null(panel.Expanded);
                Assert.True(files.TryLoadSettings(out var persisted));
                Assert.Equal(mode, persisted.Theme);
            }

            void Save(TraySettings settings)
            {
                files.SaveSettings(settings.Normalize());
                model.ApplySettings(settings, DateTimeOffset.UnixEpoch);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BackButtonTargetsStayDistinctByValue()
    {
        PopupTarget headerBack = PopupAction.Back;
        var footerBack = new PopupTarget(PopupAction.Back, 1);
        Assert.NotEqual(headerBack, footerBack);
        Assert.Equal(PopupAction.Back, headerBack.Action);
        Assert.Equal(PopupAction.Back, footerBack.Action);
    }
}

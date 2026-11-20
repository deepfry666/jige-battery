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
    [InlineData((int)PanelPage.Settings, 40, 700, 455, 0)]
    [InlineData((int)PanelPage.Devices, 0, 700, 153, 0)]
    [InlineData((int)PanelPage.Devices, 5, 700, 425, 5)]
    [InlineData((int)PanelPage.Devices, 40, 424, 370, 4)]
    [InlineData((int)PanelPage.Devices, 40, 425, 425, 5)]
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

        Assert.Equal(new PanelSize(PanelLayout.SettingsHeight + extra, 0),
            PanelLayout.Size(PanelPage.Settings, 0, 700, diagnosticFooter: true));
        Assert.Equal(new PanelSize(PanelLayout.DeviceBaseHeight + extra, 0),
            PanelLayout.Size(PanelPage.Devices, 0, 700, diagnosticFooter: true));
        Assert.Equal(new PanelSize(425 + extra, 5),
            PanelLayout.Size(PanelPage.Devices, 5, 700, diagnosticFooter: true));

        var panel = new SettingsPanel(700, hasDiagnosticFooter: true);
        Assert.True(panel.HasDiagnosticFooter);
        Assert.Equal(PanelLayout.DiagnosticFooterHeight, panel.FooterHeight);
        Assert.Equal(PanelLayout.SettingsHeight + extra, panel.Height);
        // 卡片底部与底部分割线间距在有无诊断报告时保持一致（413 - 405 = 8px）。
        Assert.Equal(PanelLayout.SettingsHeight - PanelLayout.FooterHeight, panel.Height - panel.FooterHeight);

        panel.Toggle(SettingRow.Threshold, 0, animate: false, selectedIndex: 1);
        Assert.Equal(591 + extra, panel.Height);
        Assert.Equal(591 - PanelLayout.FooterHeight, panel.Height - panel.FooterHeight);
    }

    [Theory]
    [InlineData(0, 0, 455)]
    [InlineData(50, 0.7944, 563)]
    [InlineData(250, 1, 591)]
    public void ResizeUsesPrototypeCurveAndKeepsBottomAgainstTray(int elapsed, double expected, int expectedHeight)
    {
        double eased = PanelLayout.Ease(elapsed / 250d);
        Assert.Equal(expected, eased, 4);
        var settings = new SettingsPanel(700);
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
    [InlineData(null, 52, 96, 140, 184, 228, 455)]
    [InlineData((int)SettingRow.DisplayLocation, 52, 96, 140, 184, 228, 523)]
    [InlineData((int)SettingRow.Theme, 52, 96, 140, 184, 228, 557)]
    [InlineData((int)SettingRow.TrayMode, 52, 164, 208, 252, 296, 523)]
    [InlineData((int)SettingRow.Threshold, 52, 96, 276, 320, 364, 591)]
    [InlineData((int)SettingRow.PollInterval, 52, 96, 140, 184, 330, 557)]
    public void SettingsRowsAndHitAreasFollowExpandedOptions(int? expanded, int trayMode, int threshold,
        int game, int poll, int autostart, int height)
    {
        var panel = new SettingsPanel(700);
        if (expanded is int expandedRow)
            Assert.True(panel.Toggle((SettingRow)expandedRow, 0, animate: false, selectedIndex: 0));

        int theme = autostart + 132 + (expanded == (int)SettingRow.DisplayLocation ? 68 : 0);
        int[] tops = [trayMode, threshold, game, poll, autostart, autostart + 44, autostart + 88, theme];
        foreach (var row in SettingsPanel.Rows)
        {
            var rect = panel.RowRect(row);
            int y = tops[(int)row];
            Assert.Equal(new PanelRect(10, y, 324, 44), rect);
            Assert.True(rect.Contains(10, y));
            Assert.True(rect.Contains(333, y + 43));
            Assert.False(rect.Contains(9, y));
            Assert.False(rect.Contains(334, y));
            Assert.False(rect.Contains(10, y + 44));
        }

        if (panel.Expanded is { } active)
        {
            int top = tops[(int)active] + 44;
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
    public void SwitchingOptionsIsExclusiveAndKeepsHeightContinuous()
    {
        var panel = new SettingsPanel(700);
        panel.Toggle(SettingRow.Threshold, 0, animate: true, selectedIndex: 1);
        panel.Advance(50);
        float beforeSwitch = panel.Height;
        Assert.InRange(beforeSwitch, 559, 565);
        Assert.True(panel.OptionHitRect(SettingRow.Threshold, 3).Height is > 0 and < 34);

        panel.Toggle(SettingRow.PollInterval, 50, animate: true, selectedIndex: 1);
        Assert.Equal(SettingRow.PollInterval, panel.Expanded);
        Assert.Equal(beforeSwitch, panel.Height);
        Assert.Equal(0, panel.OptionHitRect(SettingRow.Threshold, 0).Height);
        panel.Advance(300);
        Assert.Equal(557, panel.Height);
        Assert.Equal(0, panel.VisibleHeight(SettingRow.Threshold));
        Assert.Equal(34, panel.OptionHitRect(SettingRow.PollInterval, 0).Height);
        Assert.False(panel.Animating);

        Assert.True(panel.Collapse(300, animate: true));
        Assert.False(panel.Collapse(301, animate: true));
        Assert.Equal(557, panel.Height);
        Assert.Equal(0, panel.OptionHitRect(SettingRow.PollInterval, 0).Height);
        panel.Advance(550);
        Assert.Equal(455, panel.Height);
    }

    [Theory]
    [InlineData((int)SettingRow.TrayMode, (int)PopupAction.TrayMode, (int)TrayDisplayMode.LowestBattery, 15, 60, (int)TrayDisplayMode.LowestBattery)]
    [InlineData((int)SettingRow.Threshold, (int)PopupAction.Threshold, 30, 30, 60, (int)TrayDisplayMode.Custom)]
    [InlineData((int)SettingRow.PollInterval, (int)PopupAction.PollInterval, 120, 15, 120, (int)TrayDisplayMode.Custom)]
    public void SelectingAnOptionSavesIt(int row, int action, int value, int threshold, int poll, int expectedMode)
    {
        var panel = new SettingsPanel(700);
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
            Assert.Equal(isTrayMode ? 523 : 455, panel.Height);
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
        var panel = new SettingsPanel(PanelLayout.SettingsHeight + 3 * SettingsPanel.OptionHeight + 1);
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

    [Theory]
    [InlineData("inzone-h9:receiver", "Sony INZONE H9（一代接收器）", (int)DeviceKind.Headset, (int)AdaptationSource.Catalog, "已实机验证")]
    [InlineData("inzone-h9:receiver", "Sony INZONE H9 II", (int)DeviceKind.Headset, (int)AdaptationSource.Catalog, "按协议适配")]
    [InlineData("MOUSE:some-container", "G-Wolves HTX Ultra 8K", (int)DeviceKind.Mouse, (int)AdaptationSource.Catalog, "已实机验证")]
    [InlineData("mouse:some-container", "G-Wolves HTX Mini 8K", (int)DeviceKind.Mouse, (int)AdaptationSource.Catalog, "按协议适配")]
    [InlineData("mouse:some-container", "ATK 鼠标", (int)DeviceKind.Mouse, (int)AdaptationSource.Generic, "通用适配")]
    [InlineData("ulx:some-container", "Finalmouse ULX", (int)DeviceKind.Mouse, (int)AdaptationSource.Generic, "通用适配")]
    public void A4_BadgesDistinguishVerifiedProtocolAndGenericAdaptation(string id, string name, int kind, int source, string expectedLabel)
    {
        var identity = new DeviceIdentity(id, name, (DeviceKind)kind, (AdaptationSource)source);
        Assert.Equal(expectedLabel, DeviceVerification.Label(identity));
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

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
    [InlineData((int)PanelPage.Settings, 40, 700, 323, 0)]
    [InlineData((int)PanelPage.Devices, 0, 700, 153, 0)]
    [InlineData((int)PanelPage.Devices, 5, 700, 425, 5)]
    [InlineData((int)PanelPage.Devices, 40, 424, 370, 4)]
    [InlineData((int)PanelPage.Devices, 40, 425, 425, 5)]
    public void PageHeightIncludesChromeAndOnlyWholeRows(int page, int count, int available, int height, int rows)
    {
        Assert.Equal(new PanelSize(height, rows), PanelLayout.Size((PanelPage)page, count, available));
    }

    [Theory]
    [InlineData(0, 0, 323)]
    [InlineData(50, 0.7944, 431)]
    [InlineData(250, 1, 459)]
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
    [InlineData(null, 52, 96, 140, 184, 228, 323)]
    [InlineData((int)SettingRow.Primary, 52, 266, 310, 354, 398, 493)]
    [InlineData((int)SettingRow.Threshold, 52, 96, 276, 320, 364, 459)]
    [InlineData((int)SettingRow.PollInterval, 52, 96, 140, 184, 330, 425)]
    public void SettingsRowsAndHitAreasFollowExpandedOptions(int? expanded, int primary, int threshold,
        int game, int poll, int autostart, int height)
    {
        var panel = new SettingsPanel(700);
        panel.SetDeviceCount(5);
        if (expanded is int expandedRow)
            Assert.True(panel.Toggle((SettingRow)expandedRow, 0, animate: false, selectedIndex: 0));

        int[] tops = [primary, threshold, game, poll, autostart];
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
        Assert.InRange(beforeSwitch, 427, 433);
        Assert.True(panel.OptionHitRect(SettingRow.Threshold, 3).Height is > 0 and < 34);

        panel.Toggle(SettingRow.PollInterval, 50, animate: true, selectedIndex: 1);
        Assert.Equal(SettingRow.PollInterval, panel.Expanded);
        Assert.Equal(beforeSwitch, panel.Height);
        Assert.Equal(0, panel.OptionHitRect(SettingRow.Threshold, 0).Height);
        panel.Advance(300);
        Assert.Equal(425, panel.Height);
        Assert.Equal(0, panel.VisibleHeight(SettingRow.Threshold));
        Assert.Equal(34, panel.OptionHitRect(SettingRow.PollInterval, 0).Height);
        Assert.False(panel.Animating);

        Assert.True(panel.Collapse(300, animate: true));
        Assert.False(panel.Collapse(301, animate: true));
        Assert.Equal(425, panel.Height);
        Assert.Equal(0, panel.OptionHitRect(SettingRow.PollInterval, 0).Height);
        panel.Advance(550);
        Assert.Equal(323, panel.Height);
    }

    [Fact]
    public void DeviceOverflowCanScrollToEveryChoiceWithinWorkArea()
    {
        var panel = new SettingsPanel(400);
        Assert.False(panel.Toggle(SettingRow.Primary, 0, animate: false, selectedIndex: -1));
        panel.SetDeviceCount(12);
        Assert.True(panel.Toggle(SettingRow.Primary, 0, animate: false, selectedIndex: 0));
        Assert.Equal(391, panel.Height);
        Assert.Equal(2, panel.VisibleCount(SettingRow.Primary));
        for (int index = 0; index < 10; index++)
            Assert.True(panel.Scroll(1));
        Assert.False(panel.Scroll(1));
        Assert.False(panel.OptionHitRect(SettingRow.Primary, 9).Contains(100, 96));
        Assert.True(panel.OptionHitRect(SettingRow.Primary, 11).Contains(100, 130));
        Assert.Equal(391, panel.Height);
    }

    [Theory]
    [InlineData((int)SettingRow.Primary, (int)PopupAction.PrimaryDevice, 0, "headset", 15, 60)]
    [InlineData((int)SettingRow.Threshold, (int)PopupAction.Threshold, 30, null, 30, 60)]
    [InlineData((int)SettingRow.PollInterval, (int)PopupAction.PollInterval, 120, null, 15, 120)]
    public void SelectingAnOptionSavesItAndClosesTheList(int row, int action, int value, string? id, int threshold, int poll)
    {
        var panel = new SettingsPanel(700);
        var device = new DeviceIdentity("headset", "耳机", DeviceKind.Headset);
        var time = DateTimeOffset.UnixEpoch;
        var model = new TrayModel(new() { HiddenDeviceIds = [device.Id] }, new()
        {
            Readings = new() { [device.Id] = new(device, 50, false, time) }
        });
        panel.SetDeviceCount(1);
        var directory = Directory.CreateTempSubdirectory("jige-settings-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            var target = new PopupTarget((PopupAction)action, value, id);
            Assert.False(panel.Select(target, model, 0, animate: false, Save));
            Assert.False(File.Exists(files.ConfigPath));

            panel.Toggle((SettingRow)row, 0, animate: false, selectedIndex: 0);
            Assert.True(panel.Select(target, model, 1, animate: false, Save));
            Assert.Null(panel.Expanded);
            Assert.Equal(323, panel.Height);
            Assert.True(files.TryLoadSettings(out var reloaded));
            Assert.Equal(threshold, reloaded.LowBatteryThreshold);
            Assert.Equal(poll, reloaded.PollSeconds);
            Assert.Equal(id, reloaded.PrimaryDeviceId);
            if (id is not null)
                Assert.Empty(reloaded.HiddenDeviceIds);

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

    [Theory]
    [InlineData("inzone-h9:receiver", "Sony INZONE H9（一代接收器）", (int)DeviceKind.Headset, true)]
    [InlineData("inzone-h9:receiver", "Sony INZONE H9 II", (int)DeviceKind.Headset, false)]
    [InlineData("MOUSE:some-container", "G-Wolves HTX Ultra 8K", (int)DeviceKind.Mouse, true)]
    [InlineData("mouse:some-container", "G-Wolves HTX Mini 8K", (int)DeviceKind.Mouse, false)]
    [InlineData("mouse:some-container", "G-Wolves Fenrir Pro 8K", (int)DeviceKind.Mouse, false)]
    [InlineData("ulx:some-container", "G-Wolves HTX Ultra 8K", (int)DeviceKind.Mouse, false)]
    public void VerifiedBadgeUsesOriginalAdapterIdentity(string id, string name, int kind, bool verified)
    {
        Assert.Equal(verified, DeviceVerification.IsVerified(new(id, name, (DeviceKind)kind)));
    }
}

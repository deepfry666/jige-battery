using Jige.Core;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TaskbarStripTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddMinutes(2);
    private static readonly Bounds Screen = new(0, 0, 1920, 1080);
    private static readonly Bounds Bar = new(0, 1032, 1920, 1080);
    private static readonly Bounds Tray = new(1720, 1032, 1920, 1080);

    private static readonly TaskbarStripDevice[] Example =
    [
        new(DeviceKind.Keyboard, 80, BatteryTone.Normal),
        new(DeviceKind.Mouse, 12, BatteryTone.Low),
        new(DeviceKind.Headset, 45, BatteryTone.Charging, ShowBolt: true)
    ];

    [Theory]
    [InlineData(1, 42)]
    [InlineData(2, 88)]
    [InlineData(3, 141)]
    public void StripWidthAndSegmentPositions(int count, int width)
    {
        var layout = TaskbarStripLayout.Build(Example.AsSpan(0, count));
        Assert.Equal(width, layout.Width);
        Assert.Equal(count, layout.Segments.Length);
        Assert.Equal(new IconRect(4, 5, 11, 11), layout.Segments[0].Icon);
        Assert.Equal(new IconRect(18, 6, 20, 9), layout.Segments[0].Text);
        Assert.Null(layout.Segments[0].Bolt);
        Assert.Null(layout.Segments[^1].Separator);
        if (count >= 2)
        {
            Assert.Equal(new IconRect(44, 6, 1, 10), layout.Segments[0].Separator);
            Assert.Equal(new IconRect(50, 5, 11, 11), layout.Segments[1].Icon);
            Assert.Equal(new IconRect(64, 6, 20, 9), layout.Segments[1].Text);
        }
        if (count == 3)
        {
            Assert.Equal(new IconRect(90, 6, 1, 10), layout.Segments[1].Separator);
            Assert.Equal(new IconRect(96, 5, 11, 11), layout.Segments[2].Icon);
            Assert.Equal(new IconRect(110, 7, 5, 7), layout.Segments[2].Bolt);
            Assert.Equal(new IconRect(117, 6, 20, 9), layout.Segments[2].Text);
        }
    }

    [Theory]
    [InlineData(100, "100%", 27, 49)]
    [InlineData(7, "7%", 13, 35)]
    [InlineData(0, "0%", 13, 35)]
    [InlineData(null, "--", 13, 35)]
    public void TextFitsBothExtremeAndMissingValues(int? percent, string text, int textWidth, int segmentWidth)
    {
        var device = new TaskbarStripDevice(DeviceKind.Other, percent, BatteryTone.Normal);
        var layout = TaskbarStripLayout.Build([device]);
        Assert.Equal(text, device.Text);
        Assert.Equal(textWidth, Assert.Single(layout.Segments).Text.Width);
        Assert.Equal(segmentWidth, layout.Width);
    }

    [Theory]
    [InlineData(true, 0xfff0ece4u, 0xfffce100u, 0xffff8080u, 0xff4ce07au, 0xff393e4cu)]
    [InlineData(false, 0xff211d19u, 0xff9d5d00u, 0xffb42318u, 0xff0f7b38u, 0xffd2d2d2u)]
    public void PixelsUseOnlyOpaqueStatusInkAndTransparentEmptySpace(bool dark, uint fg, uint warning, uint low,
        uint charging, uint separator)
    {
        TaskbarStripDevice[] devices =
        [
            .. Example,
            new(DeviceKind.Other, 22, BatteryTone.Warning),
            new(DeviceKind.Other, null, BatteryTone.Idle)
        ];
        var layout = TaskbarStripLayout.Build(devices);
        uint[] pixels = TaskbarStripPixels.Render(layout, dark, 1);
        Assert.Equal(new[] { 0u, fg, warning, low, charging, separator }.Order(), pixels.Distinct().Order());

        AssertInk(layout.Segments[0], fg, fg);
        AssertInk(layout.Segments[1], low, low);
        AssertInk(layout.Segments[2], fg, charging);
        AssertInk(layout.Segments[3], warning, warning);
        AssertInk(layout.Segments[4], fg, fg);
        Assert.Equal(new[] { 0u, charging }.Order(), Within(layout.Segments[2].Bolt!.Value).Distinct().Order());
        Assert.All(Within(layout.Segments[0].Separator!.Value), pixel => Assert.Equal(separator, pixel));
        Assert.All(Within(layout.Segments[1].Separator!.Value), pixel => Assert.Equal(separator, pixel));

        // Even beside the low segment, only masks and separators can paint.
        for (int y = 0; y < TaskbarStripLayout.Height; y++)
        for (int x = 0; x < layout.Width; x++)
        {
            if (!layout.Segments.Any(segment => Contains(segment.Icon, x, y) || Contains(segment.Text, x, y) ||
                segment.Bolt is { } bolt && Contains(bolt, x, y) ||
                segment.Separator is { } sep && Contains(sep, x, y)))
                Assert.Equal(0u, pixels[y * layout.Width + x]);
        }

        void AssertInk(TaskbarStripSegment segment, uint icon, uint digit)
        {
            Assert.Equal(new[] { 0u, icon }, Within(segment.Icon).Distinct().Order());
            Assert.Equal(new[] { 0u, digit }, Within(segment.Text).Distinct().Order());
        }

        IEnumerable<uint> Within(IconRect rect)
        {
            for (int y = rect.Y; y < rect.Bottom; y++)
            for (int x = rect.X; x < rect.Right; x++)
                yield return pixels[y * layout.Width + x];
        }

        static bool Contains(IconRect rect, int x, int y) =>
            x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;
    }

    [Theory]
    [InlineData(96u, 1)]
    [InlineData(120u, 1)]
    [InlineData(144u, 2)]
    [InlineData(192u, 2)]
    [InlineData(288u, 3)]
    public void IntegerDpiScalingKeepsPixelCellsSharp(uint dpi, int scale)
    {
        Assert.Equal(scale, TaskbarStripLayout.PixelScale(dpi));
        var layout = TaskbarStripLayout.Build(Example);
        var original = TaskbarStripPixels.Render(layout, true, 1);
        var actual = TaskbarStripPixels.Render(layout, true, scale);
        int width = layout.Width * scale;
        Assert.Equal(width * TaskbarStripLayout.Height * scale, actual.Length);
        for (int y = 0; y < TaskbarStripLayout.Height * scale; y++)
        for (int x = 0; x < width; x++)
            Assert.Equal(original[y / scale * layout.Width + x / scale], actual[y * width + x]);
    }

    [Theory]
    [InlineData(96u, 1575, 1045, 1716, 1067)]
    [InlineData(144u, 1432, 1034, 1714, 1078)]
    [InlineData(192u, 1430, 1034, 1712, 1078)]
    public void PositionIsCenteredAndLeftOfNotifications(uint dpi, int left, int top, int right, int bottom)
    {
        var placement = TaskbarStripPlacement.Compute(Screen, Bar, Tray, 1300, dpi, 141, visible: true, bottomEdge: true);
        Assert.Equal(TaskbarStripReason.Ready, placement.Reason);
        Assert.Equal(new Bounds(left, top, right, bottom), placement.Bounds);
    }

    [Fact]
    public void ExactWidthFitsAndOnePixelOverlapFallsBack()
    {
        Assert.Equal(TaskbarStripReason.Ready, Place(occupiedRight: 1571).Reason);
        Assert.Equal(TaskbarStripReason.NoRoom, Place(occupiedRight: 1572).Reason);
    }

    [Fact]
    public void PositionWorksOnAMonitorWithNonzeroOrigin()
    {
        var screen = new Bounds(-1920, -140, 0, 940);
        var bar = new Bounds(-1920, 892, 0, 940);
        var tray = new Bounds(-200, 900, 0, 930);
        var placed = TaskbarStripPlacement.Compute(screen, bar, tray, -500, 96, 42, true, true);
        Assert.Equal(new Bounds(-246, 905, -204, 927), placed.Bounds);
    }

    [Fact]
    public void MissingHiddenAndUnsupportedTaskbarsAreDifferentReasons()
    {
        Assert.Equal(TaskbarStripReason.MissingTaskbar, TaskbarStripPlacement.Compute(Screen, null, Tray, 900, 96, 141, true, true).Reason);
        Assert.Equal(TaskbarStripReason.MissingNotificationArea, TaskbarStripPlacement.Compute(Screen, Bar, null, 900, 96, 141, true, true).Reason);
        Assert.Equal(TaskbarStripReason.UnknownButtonBounds, TaskbarStripPlacement.Compute(Screen, Bar, Tray, null, 96, 141, true, true).Reason);
        Assert.Equal(TaskbarStripReason.UnsupportedEdge, Place(bottom: false).Reason);
        var top = new Bounds(0, 0, 1920, 48);
        Assert.Equal(TaskbarStripReason.UnsupportedEdge, TaskbarStripPlacement.Compute(Screen, top, Tray, 900, 96, 141, true, true).Reason);
        Assert.Equal(TaskbarStripReason.HiddenTaskbar, Place(visible: false).Reason);
        var sliding = new Bounds(0, 1060, 1920, 1108);
        Assert.Equal(TaskbarStripReason.HiddenTaskbar, TaskbarStripPlacement.Compute(Screen, sliding, Tray, 900, 96, 141, true, true).Reason);
        var tooShort = new Bounds(0, 1064, 1920, 1080);
        Assert.Equal(TaskbarStripReason.NoRoom, TaskbarStripPlacement.Compute(Screen, tooShort, Tray, 900, 96, 141, true, true).Reason);
    }

    [Fact]
    public void FindsXamlNotificationAreaAndActualRightmostAppButton()
    {
        TaskbarControl[] controls =
        [
            new(Bar, "TaskbarFrame", "TaskbarFrame", false),
            new(new(613, 1036, 659, 1076), "StartButton", "ToggleButton", true),
            new(new(780, 1036, 826, 1076), "", "TaskListButton", true),
            new(new(1420, 1036, 1472, 1076), "OverflowButton", "Button", true),
            new(Tray, "SystemTrayFrame", "SystemTrayFrame", false),
            new(new(1800, 1036, 1840, 1076), "ControlCenterButton", "Button", true)
        ];
        Assert.Equal(new TaskbarControls(Tray, 1472), TaskbarControls.Locate(Bar, null, controls));
        Assert.Equal(new TaskbarControls(Tray, null), TaskbarControls.Locate(Bar, Tray, controls[3..]));
        Assert.Equal(default, TaskbarControls.Locate(Bar, null, controls[..4]));
    }

    [Fact]
    public void FindsWindows11AutomationPeerTaskbarButtonsAndFullWidthFrame()
    {
        TaskbarControl[] controls =
        [
            new(Bar, "", "Taskbar.TaskbarFrameAutomationPeer", false),
            new(new(613, 1036, 659, 1076), "StartButton", "ToggleButton", true),
            new(new(663, 1036, 860, 1076), "SearchButton", "Button", false),
            new(new(864, 1036, 912, 1076), "Appid: Microsoft.Windows.Explorer", "Taskbar.TaskListButtonAutomationPeer", true),
            new(new(916, 1036, 964, 1076), "Appid: Chrome", "Taskbar.TaskListButtonAutomationPeer", true),
            new(Tray, "", "SystemTray.SystemTrayFrame", false),
            new(new(1800, 1036, 1840, 1076), "ControlCenterButton", "SystemTray.OmniButton", true)
        ];
        Assert.Equal(new TaskbarControls(Tray, 964), TaskbarControls.Locate(Bar, Tray, controls));

        // 即使当前没有固定或打开的应用按钮，只要识别到现代 TaskbarFrame 与开始按钮也能确认占用边界。
        TaskbarControl[] noApps = [controls[0], controls[1], controls[2], controls[5], controls[6]];
        Assert.Equal(new TaskbarControls(Tray, 860), TaskbarControls.Locate(Bar, Tray, noApps));

        // 仅凭开始按钮而缺少框架和任务列表时，仍保持保守回退。
        TaskbarControl[] startOnly = [controls[1], controls[5]];
        Assert.Equal(new TaskbarControls(Tray, null), TaskbarControls.Locate(Bar, Tray, startOnly));
    }

    [Fact]
    public void FindsWindows10ClassicTaskbarButtonsWithoutCountingFullWidthTaskListContainer()
    {
        var bar = new Bounds(0, 1040, 1920, 1080);
        var tray = new Bounds(1740, 1040, 1920, 1080);
        TaskbarControl[] controls =
        [
            new(new(0, 1040, 48, 1080), "", "Start", true),
            new(new(48, 1040, 392, 1080), "", "TrayDummySearchControl", false),
            new(new(48, 1040, 392, 1080), "4101", "Button", true),
            new(new(392, 1040, 440, 1080), "", "TrayButton", true),
            new(new(440, 1040, 1740, 1080), "", "ReBarWindow32", false),
            new(new(440, 1040, 1740, 1080), "", "MSTaskSwWClass", false),
            new(new(440, 1040, 1740, 1080), "", "MSTaskListWClass", false),
            new(new(440, 1040, 488, 1080), "Appid: Microsoft.Windows.Explorer", "", true),
            new(new(488, 1040, 536, 1080), "", "", true),
            new(new(1740, 1040, 1764, 1080), "1502", "Button", true)
        ];

        var located = TaskbarControls.Locate(bar, tray, controls);
        Assert.Equal(new TaskbarControls(tray, 536), located);

        var placed = TaskbarStripPlacement.Compute(Screen, bar, located.NotificationArea, located.OccupiedRight, 96, 141, true, true);
        Assert.Equal(TaskbarStripReason.Ready, placed.Reason);
        Assert.Equal(new Bounds(1595, 1049, 1736, 1071), placed.Bounds);
    }

    [Fact]
    public void Windows10AvoidsNewsAndInterestsAndRightSideDeskBands()
    {
        var bar = new Bounds(0, 1040, 1920, 1080);
        var tray = new Bounds(1740, 1040, 1920, 1080);
        // 资讯和兴趣（DynamicContent2）位于 [1580, 1740]，且右侧还有一个自定义工具栏 [1460, 1580]；
        // MSTaskListWClass 只占 [440, 1460]，直读条应紧贴在 1460 左侧且不被右侧工具栏按钮挤掉。
        TaskbarControl[] controls =
        [
            new(new(0, 1040, 48, 1080), "", "Start", true),
            new(new(440, 1040, 1460, 1080), "", "MSTaskListWClass", false),
            new(new(440, 1040, 536, 1080), "Appid: Chrome", "", true),
            new(new(1459, 1040, 1580, 1080), "", "Button", true), // 含 1px 舍入误差的右侧工具栏按钮
            new(new(1580, 1040, 1740, 1080), "", "DynamicContent2", false),
            new(new(1580, 1040, 1740, 1080), "", "Button", true)
        ];

        var located = TaskbarControls.Locate(bar, tray, controls);
        Assert.Equal(1460, located.NotificationArea?.Left);
        Assert.Equal(536, located.OccupiedRight);

        var placed = TaskbarStripPlacement.Compute(Screen, bar, located.NotificationArea, located.OccupiedRight, 96, 141, true, true);
        Assert.Equal(TaskbarStripReason.Ready, placed.Reason);
        Assert.Equal(new Bounds(1315, 1049, 1456, 1071), placed.Bounds);

        // 即使 MSTaskListWClass 宽度覆盖到 TrayNotifyWnd，DynamicContent2 仍会被识别为右侧边界。
        TaskbarControl[] overlapNews =
        [
            new(new(0, 1040, 48, 1080), "", "Start", true),
            new(new(440, 1040, 1740, 1080), "", "MSTaskListWClass", false),
            new(new(440, 1040, 536, 1080), "", "", true),
            new(new(1580, 1040, 1740, 1080), "", "DynamicContent2", false),
            new(new(1580, 1040, 1740, 1080), "", "Button", true)
        ];
        var newsLocated = TaskbarControls.Locate(bar, tray, overlapNews);
        Assert.Equal(1580, newsLocated.NotificationArea?.Left);
        Assert.Equal(536, newsLocated.OccupiedRight);
    }

    [Fact]
    public void Windows10EmptyTaskListPreservesLeftControlsAndCrowdedTaskbarFallsBack()
    {
        var bar = new Bounds(0, 1040, 1920, 1080);
        var tray = new Bounds(1740, 1040, 1920, 1080);

        // 没有任何固定或打开的应用时，仍至少从 MSTaskListWClass 左边缘开始，不遮挡搜索框与快速启动栏。
        TaskbarControl[] emptyApps =
        [
            new(new(0, 1040, 48, 1080), "", "Start", true),
            new(new(440, 1040, 1740, 1080), "", "MSTaskListWClass", false)
        ];
        Assert.Equal(new TaskbarControls(tray, 440), TaskbarControls.Locate(bar, tray, emptyApps));

        // 应用按钮挤满到右侧时自动返回 NoRoom 并回退托盘图标。
        TaskbarControl[] crowded =
        [
            new(new(0, 1040, 48, 1080), "", "Start", true),
            new(new(440, 1040, 1700, 1080), "", "MSTaskListWClass", false),
            new(new(1620, 1040, 1696, 1080), "", "", true)
        ];
        var crowdedLocated = TaskbarControls.Locate(bar, tray, crowded);
        Assert.Equal(1700, crowdedLocated.NotificationArea?.Left);
        Assert.Equal(1696, crowdedLocated.OccupiedRight);
        Assert.Equal(TaskbarStripReason.NoRoom, TaskbarStripPlacement.Compute(
            Screen, bar, crowdedLocated.NotificationArea, crowdedLocated.OccupiedRight, 96, 141, true, true).Reason);
    }

    [Theory]
    [InlineData(96u, 30, 1050, 1054, 1076)]
    [InlineData(144u, 45, 1035, 1035, 1079)]
    public void Windows10SmallTaskbarFitsAt100And150PercentDpi(uint dpi, int barHeight, int barTop, int expectedTop, int expectedBottom)
    {
        var bar = new Bounds(0, barTop, 1920, barTop + barHeight);
        var tray = new Bounds(1740, barTop, 1920, barTop + barHeight);
        var placed = TaskbarStripPlacement.Compute(Screen, bar, tray, 600, dpi, 141, true, true);
        Assert.Equal(TaskbarStripReason.Ready, placed.Reason);
        Assert.Equal(expectedTop, placed.Bounds.Top);
        Assert.Equal(expectedBottom, placed.Bounds.Bottom);
    }

    [Fact]
    public void Windows11ModernTaskbarIgnoresStrayLegacyTaskList()
    {
        TaskbarControl[] controls =
        [
            new(Bar, "", "Taskbar.TaskbarFrameAutomationPeer", false),
            new(new(613, 1036, 659, 1076), "StartButton", "ToggleButton", true),
            new(new(864, 1036, 912, 1076), "Appid: Chrome", "Taskbar.TaskListButtonAutomationPeer", true),
            new(new(440, 1036, 1720, 1076), "", "MSTaskListWClass", false),
            new(Tray, "", "SystemTray.SystemTrayFrame", false)
        ];
        Assert.Equal(new TaskbarControls(Tray, 912), TaskbarControls.Locate(Bar, Tray, controls));
    }

    [Fact]
    public void SettingsShowsFallbackStatusWhenTaskbarStripFallsBack()
    {
        var noRoom = TaskbarStripDisplay.Resolve(TrayDisplayLocation.Taskbar, Place(occupiedRight: 1700), true, false);
        Assert.True(noRoom.IsFallback);
        Assert.Equal("已回退托盘", SettingsPanel.DisplayLocationLabel(TrayDisplayLocation.Taskbar, noRoom.Description));
        Assert.Equal("任务栏（回退：通知区左侧空间不足）",
            SettingsPanel.DisplayLocationOptionLabel(SettingsPanel.DisplayLocationChoices[0], noRoom.Description));
        Assert.Equal("托盘图标",
            SettingsPanel.DisplayLocationOptionLabel(SettingsPanel.DisplayLocationChoices[1], noRoom.Description));

        var ready = TaskbarStripDisplay.Resolve(TrayDisplayLocation.Taskbar, Place(), true, false);
        Assert.False(ready.IsFallback);
        Assert.Equal("任务栏（推荐）", SettingsPanel.DisplayLocationLabel(TrayDisplayLocation.Taskbar, null));
        Assert.Equal("托盘图标", SettingsPanel.DisplayLocationLabel(TrayDisplayLocation.TrayIcons, noRoom.Description));
    }

    [Fact]
    public void ChoosesEarliestTrayControlAndDoesNotIgnoreKnownOverlappingTaskButton()
    {
        var chevron = new Bounds(1690, 1036, 1720, 1076);
        TaskbarControl[] controls =
        [
            new(new(12, 1036, 60, 1076), "StartButton", "ToggleButton", true),
            new(new(1689, 1036, 1730, 1076), "TaskListButton", "ToggleButton", true),
            new(chevron, "ShowHiddenIconsButton", "Button", true)
        ];
        var layout = TaskbarControls.Locate(Bar, Tray, controls);
        Assert.Equal(chevron, layout.NotificationArea);
        Assert.Equal(1730, layout.OccupiedRight);
        Assert.Equal(TaskbarStripReason.NoRoom, TaskbarStripPlacement.Compute(
            Screen, Bar, layout.NotificationArea, layout.OccupiedRight, 96, 42, true, true).Reason);
    }

    [Fact]
    public void TemporaryFallbackPreservesUserPreferenceAndRecovers()
    {
        var saved = new TraySettings();
        Assert.Equal(TrayDisplayLocation.Taskbar, saved.DisplayLocation);
        foreach (var reason in new[] { TaskbarStripReason.MissingTaskbar, TaskbarStripReason.MissingNotificationArea,
            TaskbarStripReason.UnknownButtonBounds, TaskbarStripReason.NoRoom, TaskbarStripReason.UnsupportedEdge, TaskbarStripReason.WindowUnavailable })
        {
            var geometry = new TaskbarStripPlacement(reason, default);
            var fallback = TaskbarStripDisplay.Resolve(saved.DisplayLocation, geometry, true, false);
            Assert.Equal(reason, fallback.Reason);
            Assert.Equal("托盘图标", fallback.Actual);
            Assert.False(fallback.ShowStrip);
            Assert.True(fallback.ShowTrayIcons);
            Assert.True(fallback.IsFallback);
            Assert.Equal(TrayDisplayLocation.Taskbar, saved.DisplayLocation);

            // 暂无设备且任务栏不可用时，同样回退到托盘图标，避免丢失入口。
            var emptyFallback = TaskbarStripDisplay.Resolve(saved.DisplayLocation, geometry, false, false);
            Assert.Equal(reason, emptyFallback.Reason);
            Assert.False(emptyFallback.ShowStrip);
            Assert.True(emptyFallback.ShowTrayIcons);
            Assert.True(emptyFallback.IsFallback);
        }

        var ready = Place();
        var shown = TaskbarStripDisplay.Resolve(saved.DisplayLocation, ready, true, false);
        Assert.True(shown.ShowStrip);
        Assert.False(shown.ShowTrayIcons);
        Assert.False(shown.IsFallback);
        Assert.Equal("任务栏", shown.Actual);
        Assert.Equal(ready.Bounds, shown.Bounds);

        // 任务栏可用但暂无可显示设备时，仍在任务栏显示紧凑占位条，不再额外生成托盘图标。
        var noDevices = TaskbarStripDisplay.Resolve(saved.DisplayLocation, ready, false, false);
        Assert.Equal(TaskbarStripReason.NoDevices, noDevices.Reason);
        Assert.True(noDevices.ShowStrip);
        Assert.False(noDevices.ShowTrayIcons);
        Assert.False(noDevices.IsFallback);
        Assert.Equal("任务栏", noDevices.Actual);
        Assert.Equal(ready.Bounds, noDevices.Bounds);

        var full = TaskbarStripDisplay.Resolve(saved.DisplayLocation, ready, true, true);
        Assert.Equal(TaskbarStripReason.Fullscreen, full.Reason);
        Assert.False(full.ShowStrip);
        Assert.False(full.ShowTrayIcons);
        Assert.Equal("任务栏", full.Actual);

        var hidden = TaskbarStripDisplay.Resolve(saved.DisplayLocation, Place(visible: false), true, false);
        Assert.Equal(TaskbarStripReason.HiddenTaskbar, hidden.Reason);
        Assert.False(hidden.ShowStrip);
        Assert.False(hidden.ShowTrayIcons);
        Assert.Equal("任务栏", hidden.Actual);

        var manual = TaskbarStripDisplay.Resolve(TrayDisplayLocation.TrayIcons, ready, true, false);
        Assert.Equal(TaskbarStripReason.UserSelectedTray, manual.Reason);
        Assert.False(manual.ShowStrip);
        Assert.True(manual.ShowTrayIcons);
        Assert.False(manual.IsFallback);
        Assert.Equal("托盘图标", manual.Actual);

        // 任意组合下，任务栏直读条与托盘图标严格互斥，绝不同时存在。
        foreach (var location in Enum.GetValues<TrayDisplayLocation>())
        foreach (var reason in Enum.GetValues<TaskbarStripReason>())
        foreach (bool haveDevices in new[] { false, true })
        foreach (bool fullscreen in new[] { false, true })
        {
            var placement = reason == TaskbarStripReason.Ready ? ready : new TaskbarStripPlacement(reason, default);
            var resolved = TaskbarStripDisplay.Resolve(location, placement, haveDevices, fullscreen);
            Assert.False(resolved.ShowStrip && resolved.ShowTrayIcons);
        }
    }

    [Fact]
    public void ActiveSelectedDevicesAndLowestUseExistingModel()
    {
        var model = new TrayModel(new TraySettings { TrayExcludedDeviceIds = ["other"] }, new());
        var mouse = Identity(DeviceKind.Mouse);
        var keyboard = Identity(DeviceKind.Keyboard);
        var headset = Identity(DeviceKind.Headset);
        var other = Identity(DeviceKind.Other);
        model.ApplyPoll([
            new(mouse, 84, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(keyboard, 7, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(headset, 47, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(other, 2, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);
        model.ApplyPresence([new("", null, headset.Id, LinkState.Offline)], Now);

        Assert.Equal(new[] { DeviceKind.Keyboard, DeviceKind.Mouse },
            TaskbarStripDevice.Build(model).Select(device => device.Kind));
        Assert.Empty(TrayIconPlan.Build(model, suppressTooltip: false, showIcons: false));
        Assert.Equal(2, TrayIconPlan.Build(model, suppressTooltip: false).Length);

        var stripCard = HoverCardBuilder.Build(model, null, Now, forceList: true);
        Assert.Equal(new[] { other.Id, keyboard.Id, mouse.Id }, stripCard.Rows.Select(row => row.DeviceId));
        model.ApplySettings(model.Settings with { TrayMode = TrayDisplayMode.LowestBattery }, Now);
        Assert.Equal(DeviceKind.Other, Assert.Single(TaskbarStripDevice.Build(model)).Kind);
        Assert.Equal(stripCard.Rows, HoverCardBuilder.Build(model, TrayIconPlan.SingleKey, Now).Rows);
    }

    [Theory]
    [InlineData(true, 0xfff0ece4u)]
    [InlineData(false, 0xff211d19u)]
    public void PlaceholderStripUsesOpaqueBatteryAndDashes(bool dark, uint fg)
    {
        var placeholder = Assert.Single(TaskbarStripDevice.WithPlaceholder([]));
        Assert.True(placeholder.IsPlaceholder);
        Assert.Equal("--", placeholder.Text);
        Assert.False(placeholder.ShowBolt);
        Assert.Equal(BatteryTone.Idle, placeholder.Tone);

        var layout = TaskbarStripLayout.Build([placeholder]);
        Assert.Equal(35, layout.Width);
        uint[] pixels = TaskbarStripPixels.Render(layout, dark, 1);
        Assert.Equal(new[] { 0u, fg }, pixels.Distinct().Order());

        var segment = Assert.Single(layout.Segments);
        Assert.Contains(fg, Enumerable.Range(segment.Icon.Y, segment.Icon.Height)
            .SelectMany(y => Enumerable.Range(segment.Icon.X, segment.Icon.Width).Select(x => pixels[y * layout.Width + x])));
        Assert.Contains(fg, Enumerable.Range(segment.Text.Y, segment.Text.Height)
            .SelectMany(y => Enumerable.Range(segment.Text.X, segment.Text.Width).Select(x => pixels[y * layout.Width + x])));
    }

    [Theory]
    [InlineData(12, 10, (int)BatteryTone.Warning)]
    [InlineData(12, 12, (int)BatteryTone.Low)]
    [InlineData(24, 5, (int)BatteryTone.Normal)]
    public void SleepKeepsLastReadingAndThresholdColor(int percent, int threshold, int expectedTone)
    {
        var device = Status(percent, ChargingState.NotCharging);
        var asleep = device with { Link = LinkState.Asleep, Fresh = false };
        var sample = TaskbarStripDevice.From(device, threshold);
        Assert.Equal((BatteryTone)expectedTone, sample.Tone);
        Assert.Equal(sample, TaskbarStripDevice.From(asleep, threshold));
    }

    [Fact]
    public void ChargingAndFullHaveBoltsAndUnreadableShowsTwoDashes()
    {
        Assert.True(TaskbarStripDevice.From(Status(12, ChargingState.Charging), 15).ShowBolt);
        Assert.True(TaskbarStripDevice.From(Status(98, ChargingState.Full), 15).ShowBolt);
        Assert.True(TaskbarStripDevice.From(Status(100, ChargingState.NotCharging), 15).ShowBolt);
        var unknown = Status(12, ChargingState.Unknown) with { Fresh = false };
        Assert.Equal("--", TaskbarStripDevice.From(unknown, 15).Text);
        Assert.False(TaskbarStripDevice.From(unknown, 15).ShowBolt);
    }

    [Fact]
    public void SleepingDeviceRetainsLastChargingInkFromRealModelWithoutShowingBolt()
    {
        var model = new TrayModel(new(), new());
        var headset = Identity(DeviceKind.Headset);
        model.ApplyPoll([new(headset, 12, false, ChargingState.Charging, LinkState.Online, Now)], false, Now);
        Assert.True(Assert.Single(TaskbarStripDevice.Build(model)).ShowBolt);

        var time = Now.AddSeconds(10);
        model.ApplyPresence([new("", null, headset.Id, LinkState.Asleep)], time);
        var sleeping = Assert.Single(TaskbarStripDevice.Build(model));
        Assert.Equal("12%", sleeping.Text);
        Assert.Equal(BatteryTone.Charging, sleeping.Tone);
        Assert.False(sleeping.ShowBolt);

        var directory = Directory.CreateTempSubdirectory("jige-strip-reading-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            files.SaveState(model.Snapshot());
            Assert.True(files.LoadState().Readings[headset.Id].WasChargingOrFull);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExistingConfigDefaultsToTaskbarAndNewSettingRoundTrips()
    {
        var directory = Directory.CreateTempSubdirectory("jige-strip-test-");
        try
        {
            var files = new TrayFiles(directory.FullName);
            const string oldConfig = """{"trayMode":"Custom","pollSeconds":60,"deviceAliases":{"mouse":"我的鼠标"}}""";
            File.WriteAllText(files.ConfigPath, oldConfig);
            Assert.True(files.TryLoadSettings(out var settings));
            Assert.Equal(TrayDisplayLocation.Taskbar, settings.DisplayLocation);
            Assert.Equal("我的鼠标", settings.DeviceAliases["mouse"]);

            var model = new TrayModel(settings, new());
            var fallback = TaskbarStripDisplay.Resolve(model.Settings.DisplayLocation, Place(occupiedRight: 1700), true, false);
            Assert.Equal(TaskbarStripReason.NoRoom, fallback.Reason);
            Assert.Equal(oldConfig, File.ReadAllText(files.ConfigPath));

            var panel = new SettingsPanel(700);
            Assert.True(panel.Toggle(SettingRow.DisplayLocation, 0, false, 0));
            Assert.True(panel.Select(new(PopupAction.DisplayLocation, (int)TrayDisplayLocation.TrayIcons),
                model, 1, false, files.SaveSettings));
            Assert.True(files.TryLoadSettings(out var persisted));
            Assert.Equal(TrayDisplayLocation.TrayIcons, persisted.DisplayLocation);
            Assert.Equal("我的鼠标", persisted.DeviceAliases["mouse"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExportTaskbarStripPngsWhenRequested()
    {
        if (Environment.GetEnvironmentVariable("JIGE_TRAY_PNG_DIR") is not { Length: > 0 } output)
            return;
        Directory.CreateDirectory(output);
        foreach (uint dpi in new[] { 96u, 192u })
        foreach (bool dark in new[] { true, false })
            TrayIconPreview.SaveTaskbarStrip(Path.Combine(output, $"taskbar-strip-{dpi * 100 / 96}-{(dark ? "dark" : "light")}.png"),
                Example, dark, dpi);
    }

    private static TaskbarStripPlacement Place(int occupiedRight = 1300, bool visible = true, bool bottom = true) =>
        TaskbarStripPlacement.Compute(Screen, Bar, Tray, occupiedRight, 96, 141, visible, bottom);

    private static DeviceIdentity Identity(DeviceKind kind) => new(kind.ToString().ToLowerInvariant(), "测试设备", kind);

    private static DeviceStatus Status(int percent, ChargingState charge)
    {
        var device = Identity(DeviceKind.Mouse);
        return new(device, LinkState.Online, ReadIssue.None, charge, new(device, percent, false, Now), true);
    }
}

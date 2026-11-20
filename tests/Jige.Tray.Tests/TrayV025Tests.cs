using Jige.Core;
using Jige.Core.Adapters;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TrayV025Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void H5AndH6_CustomModeSingleCardCoversAllStatesSourceLabelsAndLongNames()
    {
        var boltMouse = new DeviceIdentity("logitech-hidpp:bolt/1/unit-M", "MX Anywhere 3S", DeviceKind.Mouse)
        {
            SourceLabel = "罗技 Bolt 接收器"
        };
        var estMouse = new DeviceIdentity("ulx:1", "Finalmouse ULX", DeviceKind.Mouse)
        {
            SourceLabel = "Finalmouse 接收器"
        };
        var btKeyboard = new DeviceIdentity("bluetooth:kb1",
            "Logitech MX Keys S Wireless Illuminated Keyboard Ultra Long Display Name", DeviceKind.Keyboard,
            Connection: DeviceConnection.Bluetooth)
        {
            SourceLabel = "Windows 蓝牙"
        };
        var usbHeadset = new DeviceIdentity("logitech-hidpp-direct:usb1", "PRO X Headset", DeviceKind.Headset,
            Connection: DeviceConnection.UsbCable)
        {
            SourceLabel = "罗技 USB 直连"
        };
        var offlineMouse = new DeviceIdentity("mouse:off1", "已断开鼠标", DeviceKind.Mouse);

        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.Custom, LowBatteryThreshold = 15 }, new());
        model.ApplyPoll([
            new(boltMouse, 20, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(estMouse, 72, true, ChargingState.NotCharging, LinkState.Online, Now),
            new(btKeyboard, 100, false, ChargingState.Full, LinkState.Online, Now),
            new(usbHeadset, 48, false, ChargingState.Charging, LinkState.Online, Now),
            new(offlineMouse, 60, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);

        // 让 Bolt 鼠标进入休眠，离线鼠标断开。
        model.ApplyPresence([
            new("bolt", 1, boltMouse.Id, LinkState.Asleep, ReadIssue.Timeout),
            new("off1", null, offlineMouse.Id, LinkState.Offline)
        ], Now.AddMinutes(2));

        // 1. 电量偏低 + 休眠中 + 2 分钟前读取（对照 hover-card.html）
        var asleepCard = HoverCardBuilder.Build(model, boltMouse.Id, Now.AddMinutes(2));
        Assert.Equal(HoverCardKind.Single, asleepCard.Kind);
        Assert.Equal(HoverCardContent.CardWidth, 248);
        Assert.Equal(DeviceKind.Mouse, asleepCard.DeviceKind);
        Assert.Equal("MX Anywhere 3S", asleepCard.DeviceName);
        Assert.Equal("2.4G", asleepCard.ConnectionTag);
        Assert.Equal("罗技 Bolt 接收器", asleepCard.SourceLabel);
        Assert.Equal(20, asleepCard.Percentage);
        Assert.False(asleepCard.IsEstimated);
        Assert.Equal(2, asleepCard.FilledCells);
        Assert.Equal(BatteryTone.Warning, asleepCard.Tone);
        Assert.Equal("电量偏低 · 休眠中 · 2 分钟前读取", asleepCard.StatusLine);

        // 2. 电压估算（带 ≈）
        var estCard = HoverCardBuilder.Build(model, estMouse.Id, Now.AddMinutes(3));
        Assert.True(estCard.IsEstimated);
        Assert.Equal("≈72%", estCard.ValueText);
        Assert.Equal(7, estCard.FilledCells);
        Assert.Equal("电压估算 · 3 分钟前读取", estCard.StatusLine);

        // 3. 蓝牙 + 已充满 + 长名称（高度固定、由 UI 单行省略）
        var fullCard = HoverCardBuilder.Build(model, btKeyboard.Id, Now.AddSeconds(20));
        Assert.Equal("蓝牙", fullCard.ConnectionTag);
        Assert.Equal("Windows 蓝牙", fullCard.SourceLabel);
        Assert.Equal(10, fullCard.FilledCells);
        Assert.Equal(HoverCardContent.SingleHeight, fullCard.Height);
        Assert.Equal("已充满 · 刚刚读取", fullCard.StatusLine);
        Assert.Contains("Ultra Long Display Name", fullCard.DeviceName);

        // 4. USB + 充电中
        var chargingCard = HoverCardBuilder.Build(model, usbHeadset.Id, Now.AddMinutes(1));
        Assert.Equal("USB", chargingCard.ConnectionTag);
        Assert.Equal("罗技 USB 直连", chargingCard.SourceLabel);
        Assert.True(chargingCard.IsCharging);
        Assert.Equal("充电中 · 1 分钟前读取", chargingCard.StatusLine);

        // 5. 已断开设备绝不显示在悬停卡片里。
        var offlineCard = HoverCardBuilder.Build(model, offlineMouse.Id, Now.AddMinutes(2));
        Assert.Equal(HoverCardKind.Empty, offlineCard.Kind);

        // 6. H6：卡片显示期间读数更新时内容实时刷新。
        model.ApplyPoll([
            new(boltMouse, 12, false, ChargingState.NotCharging, LinkState.Online, Now.AddMinutes(4))
        ], false, Now.AddMinutes(4));
        var refreshed = HoverCardBuilder.Build(model, boltMouse.Id, Now.AddMinutes(4));
        Assert.Equal(12, refreshed.Percentage);
        Assert.Equal(BatteryTone.Low, refreshed.Tone);
        Assert.Equal("电量低 · 刚刚读取", refreshed.StatusLine);
    }

    [Fact]
    public void H5AndH8_LowestBatteryListCardSortsAscendingExcludesOfflineAndCapsAtSixRows()
    {
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.LowestBattery, LowBatteryThreshold = 15 }, new());
        var devices = new (DeviceIdentity Id, int Pct, ChargingState Charge)[]
        {
            (new("d:1", "设备 A（88%）", DeviceKind.Keyboard), 88, ChargingState.NotCharging),
            (new("d:2", "设备 B（12%）", DeviceKind.Mouse), 12, ChargingState.NotCharging),
            (new("d:3", "设备 C（休眠 25%）", DeviceKind.Mouse), 25, ChargingState.NotCharging),
            (new("d:4", "设备 D（充电 48%）", DeviceKind.Headset), 48, ChargingState.Charging),
            (new("d:5", "设备 E（100% 满电）", DeviceKind.Keyboard), 100, ChargingState.Full),
            (new("d:6", "设备 F（60%）", DeviceKind.Other), 60, ChargingState.NotCharging),
            (new("d:7", "设备 G（75%）", DeviceKind.Mouse), 75, ChargingState.NotCharging),
            (new("d:8", "设备 H（95% 超长设备名称测试字符串）", DeviceKind.Headset, Connection: DeviceConnection.Bluetooth), 95, ChargingState.NotCharging),
            (new("d:off", "已断开设备（5%）", DeviceKind.Mouse), 5, ChargingState.NotCharging)
        };

        model.ApplyPoll(devices.Select(item =>
            new BatteryReading(item.Id, item.Pct, item.Id.Id == "d:6", item.Charge, LinkState.Online, Now)).ToArray(),
            false, Now);
        model.ApplyPresence([
            new("r", null, "d:3", LinkState.Asleep, ReadIssue.Timeout),
            new("r", null, "d:off", LinkState.Offline)
        ], Now.AddSeconds(10));

        var card = HoverCardBuilder.Build(model, TrayIconPlan.SingleKey, Now.AddSeconds(10));
        Assert.Equal(HoverCardKind.List, card.Kind);
        Assert.Equal("几格 · 8 台设备", card.Title);
        Assert.Equal(8, card.TotalActiveCount);
        Assert.Equal(6, card.Rows.Length);
        Assert.Equal(2, card.OverflowCount);
        Assert.Equal("还有 2 台", card.OverflowText);

        // 按电量从低到高排列，绝不含已断开设备（5%）。
        Assert.Equal([12, 25, 48, 60, 75, 88], card.Rows.Select(r => r.Percentage!.Value));
        Assert.DoesNotContain(card.Rows, r => r.DeviceId == "d:off");

        // 检查休眠、充电、估算和 5 格小电量条。
        Assert.Equal("电量低", card.Rows[0].StatusText);
        Assert.Equal("电量偏低 · 休眠中", card.Rows[1].StatusText);
        Assert.True(card.Rows[1].IsAsleep);
        Assert.Equal("充电中", card.Rows[2].StatusText);
        Assert.True(card.Rows[2].IsCharging);
        Assert.Equal("≈60%", card.Rows[3].ValueText);
        Assert.Equal(3, card.Rows[3].FilledCells);

        // 超过 6 行时包含溢出区高度；刚好 6 台时无溢出提示。
        int heightWithOverflow = card.Height;
        Assert.Equal(HoverCardContent.ListHeaderHeight + 6 * HoverCardContent.ListRowHeight +
            HoverCardContent.ListOverflowHeight + HoverCardContent.ListBottomPadding, heightWithOverflow);

        model.ApplyPresence([
            new("r", null, "d:7", LinkState.Offline),
            new("r", null, "d:8", LinkState.Offline)
        ], Now.AddSeconds(20));
        var sixCard = HoverCardBuilder.Build(model, TrayIconPlan.SingleKey, Now.AddSeconds(20));
        Assert.Equal(6, sixCard.Rows.Length);
        Assert.Equal(0, sixCard.OverflowCount);
        Assert.Equal("", sixCard.OverflowText);
        Assert.Equal(heightWithOverflow - HoverCardContent.ListOverflowHeight, sixCard.Height);
    }

    [Fact]
    public void H5_EmptyActiveDevicesReturnsFallbackCardWithoutDisconnectedDevices()
    {
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.LowestBattery }, new());
        var mouse = new DeviceIdentity("mouse:1", "离线鼠标", DeviceKind.Mouse);
        model.ApplyPoll([new(mouse, 40, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        model.ApplyPresence([new("r", null, mouse.Id, LinkState.Offline)], Now.AddSeconds(8));

        var card = HoverCardBuilder.Build(model, TrayIconPlan.SingleKey, Now.AddSeconds(8));
        Assert.Equal(HoverCardKind.Empty, card.Kind);
        Assert.Empty(card.Rows);
        Assert.Equal(0, card.TotalActiveCount);
        Assert.Equal(HoverCardContent.EmptyHeight, card.Height);
    }

    [Fact]
    public void H3AndH8_HoverCardPlacementHandlesAllFourTaskbarEdgesAndMultiMonitorBounds()
    {
        var monitor = new Bounds(0, 0, 1920, 1080);

        // 1. 底部任务栏：水平居中对齐图标中心，贴任务栏上方留 12px 间距。
        var bottomWork = new Bounds(0, 0, 1920, 1032);
        var bottomIcon = new Bounds(1600, 1044, 1632, 1076);
        var bottom = HoverCardPlacement.Place(bottomIcon, monitor, bottomWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(bottomIcon.CenterX - 124, bottom.Left);
        Assert.Equal(bottomWork.Bottom - 126 - 12, bottom.Top);
        Assert.Equal(bottomWork.Bottom - 12, bottom.Bottom);

        // 底部任务栏靠最右角：不超出显示器右边界，留出 8px 边距。
        var farRightIcon = new Bounds(1880, 1044, 1912, 1076);
        var clampedRight = HoverCardPlacement.Place(farRightIcon, monitor, bottomWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(1920 - 248 - 8, clampedRight.Left);
        Assert.True(clampedRight.Right <= bottomWork.Right - 8);

        // 2. 顶部任务栏：在任务栏下方留 12px，水平居中对齐图标。
        var topWork = new Bounds(0, 48, 1920, 1080);
        var topIcon = new Bounds(1600, 8, 1632, 40);
        var top = HoverCardPlacement.Place(topIcon, monitor, topWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(topIcon.CenterX - 124, top.Left);
        Assert.Equal(topWork.Top + 12, top.Top);

        // 3. 左侧任务栏：在任务栏右侧留 12px，垂直居中对齐图标。
        var leftWork = new Bounds(64, 0, 1920, 1080);
        var leftIcon = new Bounds(16, 600, 48, 632);
        var left = HoverCardPlacement.Place(leftIcon, monitor, leftWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(leftWork.Left + 12, left.Left);
        Assert.Equal(leftIcon.CenterY - 63, left.Top);

        // 4. 右侧任务栏：在任务栏左侧留 12px，垂直居中对齐图标。
        var rightWork = new Bounds(0, 0, 1856, 1080);
        var rightIcon = new Bounds(1872, 600, 1904, 632);
        var right = HoverCardPlacement.Place(rightIcon, monitor, rightWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(rightWork.Right - 248 - 12, right.Left);
        Assert.Equal(rightIcon.CenterY - 63, right.Top);

        // 5. 多显示器负坐标副屏（位于主屏左侧）与高 DPI（150% -> 372×189）：严守副屏边界，不跨屏溢出到主屏。
        var leftMonitor = new Bounds(-2560, 0, 0, 1440);
        var leftMonitorWork = new Bounds(-2560, 0, 0, 1368);
        var leftMonitorIcon = new Bounds(-90, 1384, -42, 1432);
        var secondaryLeft = HoverCardPlacement.Place(leftMonitorIcon, leftMonitor, leftMonitorWork,
            width: 372, height: 189, gap: 18, edgeMargin: 12);
        Assert.True(secondaryLeft.Left >= leftMonitorWork.Left);
        Assert.True(secondaryLeft.Right <= leftMonitorWork.Right - 12);
        Assert.Equal(leftMonitorWork.Bottom - 189 - 18, secondaryLeft.Top);

        // 6. 多显示器正坐标副屏（位于主屏右侧）：严守副屏边界。
        var rightMonitor = new Bounds(1920, 0, 3840, 1080);
        var rightMonitorWork = new Bounds(1920, 0, 3840, 1032);
        var rightMonitorIcon = new Bounds(1940, 1044, 1972, 1076);
        var secondaryRight = HoverCardPlacement.Place(rightMonitorIcon, rightMonitor, rightMonitorWork,
            width: 248, height: 126, gap: 12, edgeMargin: 8);
        Assert.Equal(1928, secondaryRight.Left);
        Assert.True(secondaryRight.Left >= rightMonitorWork.Left + 8);
        Assert.True(secondaryRight.Right <= rightMonitorWork.Right);

        // 7. 托盘溢出区内的图标（位于工作区内）：贴在溢出图标正上方。
        var overflowIcon = new Bounds(1650, 880, 1682, 912);
        var overflow = HoverCardPlacement.Place(overflowIcon, monitor, bottomWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(overflowIcon.CenterX - 124, overflow.Left);
        Assert.Equal(overflowIcon.Top - 126 - 12, overflow.Top);

        // 8. 自动隐藏任务栏（work == monitor）：按离图标最近的屏幕边放置。
        var autoHideIcon = new Bounds(1500, 1056, 1524, 1080);
        var autoHide = HoverCardPlacement.Place(autoHideIcon, monitor, monitor, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(autoHideIcon.CenterX - 124, autoHide.Left);
        Assert.Equal(autoHideIcon.Top - 126 - 12, autoHide.Top);

        // 9. 上方负 Y 坐标副屏（[0, -1080, 1920, 0]）：保持在副屏内不跨入主屏。
        var topMonitor = new Bounds(0, -1080, 1920, 0);
        var topMonitorWork = new Bounds(0, -1080, 1920, -48);
        var topMonitorIcon = new Bounds(1600, -36, 1632, -4);
        var secondaryTop = HoverCardPlacement.Place(topMonitorIcon, topMonitor, topMonitorWork, 248, 126, gap: 12, edgeMargin: 8);
        Assert.Equal(topMonitorWork.Bottom - 126 - 12, secondaryTop.Top);
        Assert.True(secondaryTop.Bottom <= topMonitorWork.Bottom);
    }

    [Fact]
    public void H5_UnknownReadingShowsWaitingStatus()
    {
        var mouse = new DeviceIdentity("logitech-hidpp:bolt/1/unit-M", "MX Anywhere 3S", DeviceKind.Mouse);
        var waitingStatus = HoverCardBuilder.SingleStatusLine(
            new DeviceStatus(mouse, LinkState.Asleep, ReadIssue.Timeout, ChargingState.Unknown, null, false),
            BatteryTone.Idle, Now);
        Assert.Equal("休眠中 · 等待读取", waitingStatus);
    }

    [Fact]
    public void G4H2AndH8_HoverCardBlockedInGameOrWhenMainPanelOrMenuIsOpen()
    {
        var normal = GameMode.Evaluate(notificationState: 5, foreground: ForegroundGame.None, gameSilent: true);
        var d3dFullscreen = GameMode.Evaluate(notificationState: 3, foreground: ForegroundGame.None, gameSilent: false);
        var busy = GameMode.Evaluate(notificationState: 2, foreground: ForegroundGame.None, gameSilent: false);
        var borderlessGame = GameMode.Evaluate(notificationState: 5, foreground: ForegroundGame.BorderlessWorkArea, gameSilent: true);

        Assert.True(HoverCardPolicy.CanShow(normal, popupOpen: false, menuOpen: false, dialogOpen: false));

        // 全屏独占游戏（即使关闭 GameSilent）与游戏静默识别的游戏均不显示悬停卡片。
        Assert.False(HoverCardPolicy.CanShow(d3dFullscreen, popupOpen: false));
        Assert.True(HoverCardPolicy.CanShow(busy, popupOpen: false));
        var busyFullscreen = GameMode.Evaluate(2, ForegroundGame.Fullscreen, gameSilent: false);
        Assert.False(HoverCardPolicy.CanShow(busyFullscreen, popupOpen: false));
        Assert.False(HoverCardPolicy.CanShow(borderlessGame, popupOpen: false));

        // 主面板、右键菜单或更新窗口打开时不显示悬停卡片。
        Assert.False(HoverCardPolicy.CanShow(normal, popupOpen: true));
        Assert.False(HoverCardPolicy.CanShow(normal, popupOpen: false, menuOpen: true));
        Assert.False(HoverCardPolicy.CanShow(normal, popupOpen: false, dialogOpen: true));
    }

    [Fact]
    public void H5_LogitechBoltReceiverPopulatesExpectedSourceLabel()
    {
        var bolt = new HidInterface("bolt-1", "bolt-long", 0x046D, 0xC548, "USB Receiver",
            0xFF00, 2, 20, 20, 0, 2);
        var unifying = bolt with { ReceiverId = "uni-1", Path = "uni-long", ProductId = 0xC52B };
        var registry = AdapterRegistry.CreateDefault();
        var targets = registry.Detect(new([bolt, unifying], [], [])).ToArray();

        Assert.Contains(targets, t => t.Target.Device.SourceLabel == "罗技 Bolt 接收器");
        Assert.Contains(targets, t => t.Target.Device.SourceLabel == "罗技 Unifying 接收器");

        // 兼容旧版 state.json（无 SourceLabel）时能按 ID 和连接方式推导出友好来源。
        var legacyBolt = new DeviceIdentity("logitech-hidpp:bolt/1/unit-A", "MX Anywhere 3S", DeviceKind.Mouse);
        Assert.Equal("罗技无线接收器", Presentation.SourceLabel(legacyBolt));
        var legacyBt = new DeviceIdentity("bluetooth:abc", "MX Keys S", DeviceKind.Keyboard, Connection: DeviceConnection.Bluetooth);
        Assert.Equal("Windows 蓝牙", Presentation.SourceLabel(legacyBt));
        var legacyUsb = new DeviceIdentity("logitech-hidpp-direct:usb/unit-A", "MX Keys S", DeviceKind.Keyboard, Connection: DeviceConnection.UsbCable);
        Assert.Equal("罗技 USB 直连", Presentation.SourceLabel(legacyUsb));
    }

    [Fact]
    public void H1AndG2_ScreenReaderTooltipAndStableUidPreserved()
    {
        var mouse = new DeviceIdentity("logitech-hidpp:bolt/1/unit-M", "MX Anywhere 3S", DeviceKind.Mouse)
        {
            SourceLabel = "罗技 Bolt 接收器"
        };
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.Custom }, new());
        model.ApplyPoll([new(mouse, 64, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);

        // szTip 仍然填写，供读屏软件使用。
        var slot = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Equal("MX Anywhere 3S 64%", slot.Tooltip);

        // 离线后重连仍沿用原 uID。
        var reservations = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        uint firstUid = TrayIconPlan.ReserveId(slot.Key, reservations);
        model.ApplyPresence([new("bolt", 1, mouse.Id, LinkState.Offline)], Now.AddSeconds(5));
        Assert.Null(Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false)).Device);
        model.ApplyPoll([new(mouse, 63, false, ChargingState.NotCharging, LinkState.Online, Now.AddSeconds(10))], false, Now.AddSeconds(10));
        var reconnected = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Equal(firstUid, TrayIconPlan.ReserveId(reconnected.Key, reservations));
    }
}

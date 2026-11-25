using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class LogitechTrayTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly DeviceIdentity Mouse = new("bluetooth:mouse", "MX Anywhere 3S", DeviceKind.Mouse,
        Connection: DeviceConnection.Bluetooth);
    private static readonly DeviceIdentity Keyboard = new("bluetooth:keyboard", "MX Keys", DeviceKind.Keyboard,
        Connection: DeviceConnection.Bluetooth);

    [Fact]
    public void BluetoothSwitchDefaultsOnAndPreservesDeviceReadingsWhenHiddenOrRestarted()
    {
        var model = new TrayModel(new(), new());
        Assert.True(model.Settings.ShowBluetoothDevices);
        model.ApplyPoll([Reading(Mouse, 52), Reading(Keyboard, 83)], false, Now);
        Assert.Equal(2, model.PanelDevices().Length);
        Assert.Equal("蓝牙", Presentation.Connection(Mouse));

        var disabled = model.Settings with { ShowBluetoothDevices = false };
        model.ApplySettings(disabled, Now);
        Assert.Empty(model.PanelDevices());
        Assert.Empty(model.TrayDevices());
        Assert.Empty(model.OrderedForManagement());

        var restarted = new TrayModel(disabled, model.Snapshot());
        restarted.ApplySettings(disabled with { ShowBluetoothDevices = true }, Now);
        Assert.Equal(2, restarted.OrderedForManagement().Length);
        Assert.Equal(52, restarted.OrderedForManagement().Single(item => item.Device == Mouse).Percentage);
    }

    [Fact]
    public void BluetoothSilenceDoesNotGreyOutAnExistingDeviceAndDisconnectionKeepsTheReading()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(Mouse, 52), Reading(Keyboard, 83)], false, Now);
        model.ApplyPoll([Reading(Keyboard, 83)], false, Now.AddSeconds(30));
        var asleep = Assert.Single(model.TrayDevices(), item => item.Device == Mouse);
        Assert.Equal(LinkState.Asleep, asleep.Link);
        Assert.Equal(52, asleep.Percentage);
        Assert.Equal(BatteryTone.Normal, Presentation.Tone(asleep, 15));
        Assert.Contains("休眠中 · 上次 52%", Presentation.DeviceTooltip(model, asleep));

        model.ApplyPoll([Reading(Mouse, null, LinkState.Offline), Reading(Keyboard, 83)], false, Now.AddSeconds(40));
        Assert.DoesNotContain(model.TrayDevices(), item => item.Device == Mouse);
        var offline = Assert.Single(model.BuildMainPanel().OfflineRows, item => item.Device == Mouse);
        Assert.Equal(LinkState.Offline, offline.Link);
        Assert.Equal(52, offline.Percentage);
        Assert.Equal(LinkState.Online, Assert.Single(model.TrayDevices(), item => item.Device == Keyboard).Link);
    }

    [Fact]
    public void CableWinsForTheSameUnitWithoutReplacingItsIdentityOrMergingADifferentUnit()
    {
        var model = new TrayModel(new(), new());
        var receiver = new DeviceIdentity("logitech-hidpp:receiver/1/unit-AAAAAAAA", "Mouse", DeviceKind.Mouse);
        var different = receiver with { Id = "logitech-hidpp:receiver/2/unit-BBBBBBBB" };
        var cable = receiver with { Id = "logitech-hidpp-direct:cable/unit-AAAAAAAA", Connection = DeviceConnection.UsbCable };
        model.ApplyPoll([Reading(receiver, 31), Reading(different, 81)], false, Now);
        model.ApplyPoll([Reading(receiver, 33), Reading(cable, 45), Reading(different, 81)], false, Now.AddSeconds(5));

        var selected = Assert.Single(model.TrayDevices(), item => item.Device.Id == receiver.Id);
        Assert.Equal(DeviceConnection.UsbCable, selected.Device.Connection);
        Assert.Equal(45, selected.Percentage);
        Assert.Equal(2, model.TrayDevices().Length);

        model.ApplyPoll([Reading(receiver, 46), Reading(different, 81)], false, Now.AddSeconds(10));
        Assert.Equal(DeviceConnection.Receiver, Assert.Single(model.TrayDevices(), item => item.Device.Id == receiver.Id).Device.Connection);
        Assert.Equal(2, model.TrayDevices().Length);
    }

    [Fact]
    public void PreviouslyKnownReceiverSlotSleepsWhileReceiverIsPresentAndGoesOfflineOnRemoval()
    {
        var identity = new DeviceIdentity("logitech-hidpp:receiver/3/unit-AAAAAAAA", "Mouse", DeviceKind.Mouse);
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(identity, 63)], false, Now);
        var restarted = new TrayModel(new(), model.Snapshot());
        restarted.ApplyPoll([], false, Now.AddSeconds(5), liveTargets: new HashSet<string> { "logitech-hidpp:receiver" });
        var asleep = Assert.Single(restarted.TrayDevices());
        Assert.Equal(LinkState.Asleep, asleep.Link);
        Assert.Equal(63, asleep.Percentage);

        restarted.ApplyPoll([], false, Now.AddSeconds(6), liveTargets: new HashSet<string>());
        Assert.Empty(restarted.TrayDevices());
        var offline = Assert.Single(restarted.OrderedForManagement());
        Assert.Equal(LinkState.Offline, offline.Link);
        Assert.Equal(63, offline.Percentage);
    }

    [Fact]
    public void BoltRestartReusesMostRecentIdentityAndReadingForSilentSlot()
    {
        const string receiver = "logitech-hidpp:bolt";
        var mouse = new DeviceIdentity($"{receiver}/2/unit-mouse", "MX Anywhere 3S", DeviceKind.Mouse);
        var oldMouse = mouse with { Id = $"{receiver}/2/unit-old", Name = "旧鼠标" };
        var keyboard = new DeviceIdentity($"{receiver}/1/unit-keyboard", "MX Keys S", DeviceKind.Keyboard);
        var settings = new TraySettings().WithAlias(mouse.Id, "公司鼠标");
        var before = new TrayModel(settings, new());
        before.ApplyPoll([Reading(oldMouse, 99)], false, Now);
        before.ApplyPoll([Reading(mouse, 58) with { ReadAt = Now.AddMinutes(1) }, Reading(keyboard, 83)], false, Now.AddMinutes(1));
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(folder.FullName);
            files.SaveSettings(settings);
            files.SaveState(before.Snapshot());
            Assert.True(files.TryLoadSettings(out var restoredSettings));
            var restoredState = files.LoadState(ref restoredSettings);
            var after = new TrayModel(restoredSettings, restoredState);
            Assert.Equal(mouse, after.SavedLogitechSlots()[$"{receiver}/2"]);
            Assert.Equal(keyboard, after.SavedLogitechSlots()[$"{receiver}/1"]);

            after.ApplyPoll([Reading(keyboard, null, LinkState.Offline)], false, Now.AddMinutes(2),
                liveTargets: new HashSet<string> { receiver });
            var asleep = Assert.Single(after.TrayDevices());
            Assert.Equal(mouse, asleep.Device);
            Assert.Equal("公司鼠标", after.Name(asleep.Device));
            Assert.Equal(58, asleep.Percentage);
            Assert.Equal(LinkState.Asleep, asleep.Link);
            Assert.Equal(BatteryTone.Normal, Presentation.Tone(asleep, 15));
            Assert.Equal(2, after.BuildMainPanel().OfflineRows.Length);

            after.ApplyPoll([Reading(mouse, 57)], false, Now.AddMinutes(3), liveTargets: new HashSet<string> { receiver });
            Assert.Equal(mouse, Assert.Single(after.TrayDevices()).Device);
            Assert.Equal(3, after.OrderedForManagement().Length);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void BluetoothPropertyFallbackAndHidPushReuseTheSavedIdentity()
    {
        var hid = Mouse with
        {
            Id = "bluetooth:container",
            BluetoothContainerKey = "bluetooth:container",
            NameFromHidpp = true
        };
        var merged = hid with { Id = "bluetooth:address" };
        var first = new TrayModel(new(), new());
        first.ApplyPoll([Reading(merged, 50), Reading(Keyboard, 83)], false, Now);

        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var files = new TrayFiles(temp.FullName);
            Assert.True(files.SaveState(first.Snapshot()));
            var model = new TrayModel(new(), files.LoadState());
            var property = merged with { Name = "Windows alias", Kind = DeviceKind.Other, NameFromHidpp = false };
            model.ApplyPoll([Reading(property, 51), Reading(Keyboard, 83)], false, Now.AddSeconds(1));
            var fallback = Assert.Single(model.TrayDevices(), item => item.Device.Id == merged.Id);
            Assert.Equal(hid.Name, fallback.Device.Name);
            Assert.Equal(DeviceKind.Mouse, fallback.Device.Kind);
            Assert.Equal(51, fallback.Percentage);

            var at = Now.AddSeconds(2);
            var push = new PresenceUpdate("container", null, hid.Id, LinkState.Online)
            {
                Battery = Reading(hid, 62) with { ReadAt = at }
            };
            Assert.Equal((true, false), model.ApplyPresence([push], at));
            Assert.Equal(2, model.TrayDevices().Length);
            var mouse = Assert.Single(model.TrayDevices(), item => item.Device.Id == merged.Id);
            Assert.Equal(hid.Name, mouse.Device.Name);
            Assert.Equal(DeviceKind.Mouse, mouse.Device.Kind);
            Assert.Equal(62, mouse.Percentage);
            Assert.Equal(83, Assert.Single(model.TrayDevices(), item => item.Device == Keyboard).Percentage);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void BatteryPushRefreshesOnlyTheTargetAndDoesNotRequestAnotherPoll()
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([Reading(Mouse, 52), Reading(Keyboard, 83)], false, Now);
        var eventTime = Now.AddSeconds(5);
        var reading = Reading(Mouse, 64) with { ReadAt = eventTime };
        var update = new PresenceUpdate("", null, Mouse.Id, LinkState.Online) { Battery = reading };
        var result = model.ApplyPresence([update], eventTime);

        Assert.True(result.UiChanged);
        Assert.False(result.NeedsBatteryRefresh);
        Assert.Equal(64, Assert.Single(model.TrayDevices(), item => item.Device == Mouse).Percentage);
        var keyboard = Assert.Single(model.TrayDevices(), item => item.Device == Keyboard);
        Assert.Equal(LinkState.Online, keyboard.Link);
        Assert.Equal(83, keyboard.Percentage);
    }

    [Fact]
    public void MxVerticalSwitchBetween24GAndChargeOnlyUsbNeverShowsZeroAndRestores90Percent()
    {
        var receiver = new DeviceIdentity("logitech-hidpp:unifying/1/unit-MXVERTICAL", "MX Vertical", DeviceKind.Mouse,
            AdaptationSource.Catalog, VerificationTier.DocsOnly, DeviceConnection.Receiver);
        var cable = receiver with
        {
            Id = "logitech-hidpp-direct:usb/unit-MXVERTICAL",
            Source = AdaptationSource.Generic,
            Connection = DeviceConnection.UsbCable
        };
        var model = new TrayModel(new() { FullChargeReminder = true }, new());

        // 1) 2.4G 在线：90% 未充电。
        model.ApplyPoll([new(receiver, 90, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        var wireless = Assert.Single(model.TrayDevices());
        Assert.Equal(90, wireless.Percentage);
        Assert.Equal("90%", Presentation.Value(wireless));
        Assert.Equal("2.4G 已连接", Presentation.Status(wireless, 15));

        // 2) 插上 USB 线缆（0x1000 回复 00 00 01 -> null / Charging），即便 2.4G 首轮仍返回旧 HoldOnline 读数，USB 也立即生效且不误显示 0% 或陈旧百分比。
        var t1 = Now.AddSeconds(30);
        model.ApplyPoll([
            new(receiver, 90, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(cable, null, false, ChargingState.Charging, LinkState.Online, t1)
        ], false, t1);
        var charging = Assert.Single(model.TrayDevices());
        Assert.Equal(receiver.Id, charging.Device.Id);
        Assert.Equal(DeviceConnection.UsbCable, charging.Device.Connection);
        Assert.True(charging.ChargeStatusOnly);
        Assert.True(charging.IsCharging);
        Assert.False(charging.IsFull);
        Assert.Null(charging.Percentage);
        Assert.Equal("–", Presentation.Value(charging));
        Assert.Equal("充电中", Presentation.Status(charging, 15));
        Assert.Equal(BatteryTone.Charging, Presentation.Tone(charging, 15));
        Assert.Equal("MX Vertical · 充电中", Presentation.DeviceTooltip(model, charging));
        var stripCharging = TaskbarStripDevice.From(charging, 15);
        Assert.Equal("--", stripCharging.Text);
        Assert.Equal(BatteryTone.Charging, stripCharging.Tone);
        Assert.True(stripCharging.ShowBolt);
        var listCard = HoverCardBuilder.Build(model, null, t1, forceList: true);
        var listRow = Assert.Single(listCard.Rows);
        Assert.Equal("–", listRow.ValueText);
        Assert.Equal("充电中", listRow.StatusText);
        Assert.Equal(BatteryTone.Charging, listRow.Tone);
        Assert.True(listRow.IsCharging);
        Assert.Null(listRow.Percentage);

        model.ApplySettings(model.Settings with { TrayMode = TrayDisplayMode.Custom }, t1);
        var singleCard = HoverCardBuilder.Build(model, receiver.Id, t1);
        Assert.Equal(HoverCardKind.Single, singleCard.Kind);
        Assert.Null(singleCard.Percentage);
        Assert.Equal("–", singleCard.ValueText);
        Assert.Equal("充电中 · 电量未知", singleCard.StatusLine);
        Assert.Equal(BatteryTone.Charging, singleCard.Tone);
        Assert.True(singleCard.IsCharging);

        Assert.Equal(t1, model.UpdatedAt);
        // 历史不记录伪造的 0% / Charging。
        var historySamples = Assert.Single(model.DiagnosticHistories()).RecentReadings;
        Assert.Equal([90], historySamples.Select(s => s.Percentage));

        // 3) USB 充满（0x1000 回复 00 00 03 -> null / Full）：显示「已充满」，不编造 100%，不显示 0%。
        var t2 = Now.AddMinutes(30);
        model.ApplyPoll([
            new(receiver, null, false, ChargingState.Unknown, LinkState.Offline, t2),
            new(cable, null, false, ChargingState.Full, LinkState.Online, t2)
        ], false, t2);
        var full = Assert.Single(model.TrayDevices());
        Assert.Null(full.Percentage);
        Assert.True(full.ChargeStatusOnly);
        Assert.True(full.IsFull);
        Assert.False(full.IsCharging);
        Assert.Equal("–", Presentation.Value(full));
        Assert.Equal("已充满", Presentation.Status(full, 15));
        Assert.Equal(BatteryTone.Charging, Presentation.Tone(full, 15));
        Assert.Equal("MX Vertical · 已充满", Presentation.DeviceTooltip(model, full));
        Assert.Equal("已充满 · 电量未知", HoverCardBuilder.SingleStatusLine(full, BatteryTone.Charging, t2));
        Assert.Equal("已充满", HoverCardBuilder.ListRowStatus(full, BatteryTone.Charging));
        Assert.Equal(ReminderKeys.For(ReminderKind.FullCharge, receiver.Id), model.ReadyReminderKey(t2, false));

        // 4) 拔线切回 2.4G：恢复 90% / 2.4G 已连接，且清除充满提醒。
        var t3 = Now.AddMinutes(31);
        model.ApplyPoll([new(receiver, 90, false, ChargingState.NotCharging, LinkState.Online, t3)], false, t3);
        var back = Assert.Single(model.TrayDevices());
        Assert.Equal(receiver.Id, back.Device.Id);
        Assert.Equal(DeviceConnection.Receiver, back.Device.Connection);
        Assert.False(back.ChargeStatusOnly);
        Assert.True(back.Fresh);
        Assert.Equal(90, back.Percentage);
        Assert.Equal("90%", Presentation.Value(back));
        Assert.Equal("2.4G 已连接", Presentation.Status(back, 15));
        Assert.Null(model.ReadyReminderKey(t3, false));
    }

    [Fact]
    public void ColdStartOnChargeOnlyUsbAppearsInTrayWithoutHijackingLowerBatteryDevice()
    {
        var cable = new DeviceIdentity("logitech-hidpp-direct:usb/unit-MXVERTICAL", "MX Vertical", DeviceKind.Mouse,
            AdaptationSource.Generic, VerificationTier.DocsOnly, DeviceConnection.UsbCable);
        var other = new DeviceIdentity("mouse:other", "Other Mouse", DeviceKind.Mouse);
        var model = new TrayModel(new(), new());

        model.ApplyPoll([new(cable, null, false, ChargingState.Charging, LinkState.Online, Now)], false, Now);
        var only = Assert.Single(model.PanelDevices());
        Assert.Null(only.Percentage);
        Assert.True(only.ChargeStatusOnly);
        Assert.Equal("–", Presentation.Value(only));
        Assert.Equal("充电中", Presentation.Status(only, 15));
        Assert.Equal(BatteryTone.Charging, Presentation.Tone(only, 15));

        // 有另一台真实 25% 无线设备时，无百分比充电鼠标排在后面，不抢占最低电量主卡。
        var t1 = Now.AddSeconds(10);
        model.ApplyPoll([
            new(cable, null, false, ChargingState.Charging, LinkState.Online, t1),
            new(other, 25, false, ChargingState.NotCharging, LinkState.Online, t1)
        ], false, t1);
        var plan = model.BuildMainPanel();
        Assert.Equal(other.Id, plan.Hero?.Device.Id);
        Assert.Equal(cable.Id, Assert.Single(plan.OnlineRows).Device.Id);
    }

    [Fact]
    public void SimultaneousFresh24GAndChargeOnlyUsbKeepsRealPercentageAndAdoptsChargingState()
    {
        var receiver = new DeviceIdentity("logitech-hidpp:unifying/1/unit-MXVERTICAL", "MX Vertical", DeviceKind.Mouse);
        var cable = receiver with { Id = "logitech-hidpp-direct:usb/unit-MXVERTICAL", Connection = DeviceConnection.UsbCable };
        var model = new TrayModel(new(), new());

        model.ApplyPoll([new(receiver, 90, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        var t1 = Now.AddSeconds(30);
        model.ApplyPoll([
            new(receiver, 90, false, ChargingState.NotCharging, LinkState.Online, t1),
            new(cable, null, false, ChargingState.Charging, LinkState.Online, t1)
        ], false, t1);

        var combined = Assert.Single(model.TrayDevices());
        Assert.Equal(90, combined.Percentage);
        Assert.True(combined.Fresh);
        Assert.False(combined.ChargeStatusOnly);
        Assert.True(combined.IsCharging);
        Assert.Equal("充电中", Presentation.Status(combined, 15));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RealZeroPercentAndLowBatteryStillDisplayAndRemindNormally(int percent)
    {
        var model = new TrayModel(new(), new());
        model.ApplyPoll([new(Mouse, percent, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        var status = Assert.Single(model.TrayDevices());
        Assert.Equal(percent, status.Percentage);
        Assert.False(status.ChargeStatusOnly);
        Assert.Equal($"{percent}%", Presentation.Value(status));
        Assert.Equal(BatteryTone.Low, Presentation.Tone(status, 15));
        Assert.Equal("电量低，该充电了", Presentation.Status(status, 15));
        Assert.Equal($"{percent}%", TaskbarStripDevice.From(status, 15).Text);
        Assert.Equal(Mouse.Id, model.ReadyReminderKey(Now, false));
    }

    [Fact]
    public void ChargeStatusOnlyFullReminderDoesNotExpireOnStaleLastReadAtWhileFreshRemindersStillExpire()
    {
        var receiver = new DeviceIdentity("logitech-hidpp:unifying/1/unit-MXVERTICAL", "MX Vertical", DeviceKind.Mouse,
            AdaptationSource.Catalog, VerificationTier.DocsOnly, DeviceConnection.Receiver);
        var cable = receiver with
        {
            Id = "logitech-hidpp-direct:usb/unit-MXVERTICAL",
            Source = AdaptationSource.Generic,
            Connection = DeviceConnection.UsbCable
        };
        var freshMouse = new DeviceIdentity("mouse:fresh", "Fresh Mouse", DeviceKind.Mouse);
        var model = new TrayModel(new() { PollSeconds = 60, LowBatteryThreshold = 15, FullChargeReminder = true }, new());

        // 1) 2.4G 在 T0 记录 90%，随后 30 分钟后切换为仅状态 USB 充满（Last.ReadAt 仍停留在 30 分钟前）。
        model.ApplyPoll([new(receiver, 90, false, ChargingState.NotCharging, LinkState.Online, Now)], false, Now);
        var t2 = Now.AddMinutes(30);
        model.ApplyPoll([
            new(receiver, null, false, ChargingState.Unknown, LinkState.Offline, t2),
            new(cable, null, false, ChargingState.Full, LinkState.Online, t2),
            new(freshMouse, 10, false, ChargingState.NotCharging, LinkState.Online, t2)
        ], false, t2);

        string fullKey = ReminderKeys.For(ReminderKind.FullCharge, receiver.Id);
        Assert.Equal(freshMouse.Id, model.ReadyReminderKey(t2, false));
        Assert.True(model.ReminderIsDue(fullKey, t2));
        // ChargeStatusOnly 充满提醒不依赖陈旧 Last.ReadAt 过期，避免 WorkTimer 10ms 忙循环。
        Assert.Null(model.ReminderExpires(fullKey));

        // 普通 Fresh 低电量提醒仍按 TTL 正常过期；消除低电量后充满提醒即就绪。
        var expectedExpiry = t2.AddSeconds(120).AddTicks(1);
        Assert.Equal(expectedExpiry, model.ReminderExpires(freshMouse.Id));
        model.SetReminder(freshMouse.Id, null);
        Assert.Equal(fullKey, model.ReadyReminderKey(t2, false));

        var t3 = t2.AddMinutes(1);
        model.ApplyPoll([
            new(freshMouse, 100, false, ChargingState.Full, LinkState.Online, t3)
        ], false, t3);
        string freshFullKey = ReminderKeys.For(ReminderKind.FullCharge, freshMouse.Id);
        Assert.True(model.ReminderIsDue(freshFullKey, t3));
        Assert.Equal(t3.AddSeconds(120).AddTicks(1), model.ReminderExpires(freshFullKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChargeStatusOnlyIconsCellsHoverCardAndTaskbarMeterRenderGreenFillWithoutFakingPercentage(bool dark)
    {
        var cable = new DeviceIdentity("logitech-hidpp-direct:usb/unit-MXVERTICAL", "MX Vertical", DeviceKind.Mouse,
            AdaptationSource.Generic, VerificationTier.DocsOnly, DeviceConnection.UsbCable);
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.Custom }, new());

        // 1) ChargeStatusOnly 充电中（无百分比）：图标 50% 绿填充，保留 Percentage=null，不编造数字。
        model.ApplyPoll([new(cable, null, false, ChargingState.Charging, LinkState.Online, Now)], false, Now);
        var charging = Assert.Single(model.TrayDevices());
        var chargingIcon = Presentation.Icon(charging, dark, 15);
        Assert.Null(chargingIcon.Percentage);
        Assert.Equal(50, chargingIcon.FillPercentage);
        Assert.Equal(50, chargingIcon.EffectiveFillPercentage);
        Assert.Equal(BatteryTone.Charging, chargingIcon.Tone);
        Assert.Equal("–", Presentation.Value(charging));

        foreach (int size in new[] { 16, 24, 32 })
        {
            uint[] pixels = TrayIconPreview.Render(size, chargingIcon);
            Assert.Contains(chargingIcon.FillInk.Premultiplied, pixels);
            Assert.Contains(chargingIcon.DimInk.Premultiplied, pixels);
        }

        // 2) ChargeStatusOnly 已充满（无百分比）：图标 100% 全绿填充，主面板/悬停卡片满格，底栏格条 4/4 全亮，数字仍为 "–"/"--"。
        var t1 = Now.AddMinutes(10);
        model.ApplyPoll([new(cable, null, false, ChargingState.Full, LinkState.Online, t1)], false, t1);
        var full = Assert.Single(model.TrayDevices());
        var fullIcon = Presentation.Icon(full, dark, 15);
        Assert.Null(fullIcon.Percentage);
        Assert.Equal(100, fullIcon.FillPercentage);
        Assert.Equal(100, fullIcon.EffectiveFillPercentage);
        Assert.Equal(BatteryTone.Charging, fullIcon.Tone);
        Assert.NotEqual(chargingIcon, fullIcon);
        Assert.Equal("–", Presentation.Value(full));
        Assert.Equal(10, Presentation.FilledCells(full, 10));
        Assert.Equal(5, Presentation.FilledCells(full, 5));

        foreach (int size in new[] { 16, 24, 32 })
        {
            uint[] pixels = TrayIconPreview.Render(size, fullIcon);
            Assert.Contains(fullIcon.FillInk.Premultiplied, pixels);
            Assert.DoesNotContain(fullIcon.DimInk.Premultiplied, pixels);
        }

        var singleCard = HoverCardBuilder.Build(model, cable.Id, t1);
        Assert.Equal(10, singleCard.FilledCells);
        Assert.Null(singleCard.Percentage);
        Assert.Equal("–", singleCard.ValueText);
        Assert.False(singleCard.IsCharging);
        Assert.Equal("已充满 · 电量未知", singleCard.StatusLine);

        var listCard = HoverCardBuilder.Build(model, null, t1, forceList: true);
        var row = Assert.Single(listCard.Rows);
        Assert.Equal(5, row.FilledCells);
        Assert.Null(row.Percentage);
        Assert.Equal("–", row.ValueText);
        Assert.False(row.IsCharging);
        Assert.Equal("已充满", row.StatusText);

        var stripDevice = TaskbarStripDevice.From(full, 15);
        Assert.True(stripDevice.IsFull);
        Assert.Null(stripDevice.Percentage);
        Assert.Equal("--", stripDevice.Text);
        Assert.True(stripDevice.ShowBolt);
        Assert.Equal(BatteryTone.Charging, stripDevice.Tone);

        var palette = TaskbarStripPalette.Create(TaskbarBackdrop.FromDark(dark), TaskbarStripStyle.Meter, TaskbarStripColorScheme.Auto);
        uint expectedLit = palette.Charging.Premultiplied;
        uint expectedDim = palette.Foreground.WithAlpha(palette.UsesLightInk ? (byte)56 : (byte)48).Premultiplied;

        // 充电中（非满电、无百分比）在底栏格条样式下保持 0/4 亮格，避免误显示满格。
        var chargingStrip = TaskbarStripDevice.From(charging, 15);
        Assert.False(chargingStrip.IsFull);
        var chargingLayout = TaskbarStripLayout.Build([chargingStrip]);
        uint[] chargingMeterPixels = TaskbarStripPixels.Render(chargingLayout, palette, 1);
        var chargingSeg = Assert.Single(chargingLayout.Segments);
        int chargingBarLeft = chargingSeg.Icon.X;
        int chargingBarWidth = chargingSeg.Text.Right - chargingBarLeft;
        for (int i = 0; i < 4; i++)
        {
            int cellLeft = chargingBarLeft + i * (chargingBarWidth + 1) / 4;
            Assert.Equal(expectedDim, chargingMeterPixels[17 * chargingLayout.Width + cellLeft]);
        }

        // 已充满（无百分比）在底栏格条样式下 4/4 全亮绿色。
        var layout = TaskbarStripLayout.Build([stripDevice]);
        uint[] meterPixels = TaskbarStripPixels.Render(layout, palette, 1);
        var seg = Assert.Single(layout.Segments);
        int barLeft = seg.Icon.X;
        int barWidth = seg.Text.Right - barLeft;
        for (int i = 0; i < 4; i++)
        {
            int cellLeft = barLeft + i * (barWidth + 1) / 4;
            Assert.Equal(expectedLit, meterPixels[17 * layout.Width + cellLeft]);
            Assert.Equal(expectedLit, meterPixels[18 * layout.Width + cellLeft]);
        }
    }

    private static BatteryReading Reading(DeviceIdentity device, int? level, LinkState link = LinkState.Online) =>
        new(device, level, false, ChargingState.NotCharging, link, Now);
}

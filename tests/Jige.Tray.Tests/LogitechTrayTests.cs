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
        var offline = Assert.Single(model.TrayDevices(), item => item.Device == Mouse);
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
        var offline = Assert.Single(restarted.TrayDevices());
        Assert.Equal(LinkState.Offline, offline.Link);
        Assert.Equal(63, offline.Percentage);
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

    private static BatteryReading Reading(DeviceIdentity device, int? level, LinkState link = LinkState.Online) =>
        new(device, level, false, ChargingState.NotCharging, link, Now);
}

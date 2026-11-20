using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class PassiveMouseTrayTests
{
    [Fact]
    public void PassiveReceiverWaitsForReportsKeepsLastReadingAndRecoversFromSleep()
    {
        var now = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        var model = new TrayModel(new(), new());
        var device = new DeviceIdentity("passive-mouse:receiver", "ATTACK SHARK / Delux 鼠标", DeviceKind.Mouse)
        {
            UsesPassiveBatteryReports = true
        };
        var waiting = new BatteryReading(device, null, false, ChargingState.Unknown, LinkState.Asleep, now);

        model.ApplyPoll([waiting], false, now);
        var status = Assert.Single(model.PanelDevices());
        Assert.Equal(LinkState.Asleep, status.Link);
        Assert.Equal("--", Presentation.Value(status));
        Assert.Equal("等待鼠标上报电量", Presentation.Status(status, 15));

        var name = device with { Name = "Delux M600 Pro" };
        Push(new(name, 73, false, ChargingState.NotCharging, LinkState.Online, now), now);
        status = Assert.Single(model.PanelDevices());
        Assert.Equal(name, status.Device);
        Assert.Equal("73%", Presentation.Value(status));
        Assert.True(status.Fresh);

        var chargingAt = now.AddSeconds(1);
        Push(new(name, null, false, ChargingState.Charging, LinkState.Online, chargingAt), chargingAt);
        status = Assert.Single(model.PanelDevices());
        Assert.Equal("73%", Presentation.Value(status));
        Assert.Equal("充电中", Presentation.Status(status, 15));

        var asleepAt = now.AddMinutes(3);
        model.ApplyPoll([waiting with { ReadAt = asleepAt }], false, asleepAt);
        Assert.Null(model.NextSleepTimeout(asleepAt));
        Assert.False(model.AdvanceSleepTimeouts(asleepAt.AddHours(1)));
        model.ApplyPoll([waiting with { ReadAt = asleepAt.AddHours(1) }], false, asleepAt.AddHours(1));
        status = Assert.Single(model.PanelDevices());
        Assert.Equal(name, status.Device);
        Assert.Equal(LinkState.Asleep, status.Link);
        Assert.Equal("73%", Presentation.Value(status));
        Assert.False(status.Fresh);

        var restored = new TrayModel(new(), model.Snapshot());
        restored.ApplyPoll([waiting with { ReadAt = asleepAt }], false, asleepAt);
        Assert.Equal(73, Assert.Single(restored.PanelDevices()).Percentage);

        var wake = asleepAt.AddHours(2);
        Push(new(name, 71, false, ChargingState.NotCharging, LinkState.Online, wake), wake);
        Assert.Equal(71, Assert.Single(model.PanelDevices()).Percentage);
        model.ApplyPoll([], false, wake.AddSeconds(1));
        Assert.Equal(LinkState.Offline, Assert.Single(model.PanelDevices()).Link);

        void Push(BatteryReading battery, DateTimeOffset at)
        {
            var update = new PresenceUpdate("receiver", null, device.Id, LinkState.Online) { Battery = battery };
            Assert.Equal((true, false), model.ApplyPresence([update], at));
        }
    }
}

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

    [Fact]
    public void PassiveMouseChargingShowsChargingOnHoverCardAndGreenIconWithAndWithoutPriorReading()
    {
        var t0 = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.Custom, ShowRuntimeEstimate = true }, new());
        var device = new DeviceIdentity("passive-mouse:beken", "ATTACK SHARK X11", DeviceKind.Mouse)
        {
            UsesPassiveBatteryReports = true,
            SourceLabel = "2.4G 无线接收器"
        };

        // 1) 尚未收到任何电量百分比时先收到充电状态帧（Last == null, Fresh == false, IsCharging == true）。
        var chargingOnly = new BatteryReading(device, null, false, ChargingState.Charging, LinkState.Online, t0);
        model.ApplyPresence([new("beken", null, device.Id, LinkState.Online) { Battery = chargingOnly }], t0);
        var noLast = Assert.Single(model.TrayDevices());
        Assert.True(noLast.IsCharging);
        Assert.False(noLast.Fresh);
        Assert.Null(noLast.Percentage);

        var singleNoLast = HoverCardBuilder.Build(model, device.Id, t0);
        Assert.Equal(HoverCardKind.Single, singleNoLast.Kind);
        Assert.Null(singleNoLast.Percentage);
        Assert.Equal("--", singleNoLast.ValueText);
        Assert.True(singleNoLast.IsCharging);
        Assert.Equal(BatteryTone.Charging, singleNoLast.Tone);
        Assert.Equal("充电中 · 等待读取", singleNoLast.StatusLine);

        var listNoLast = Assert.Single(HoverCardBuilder.Build(model, null, t0, forceList: true).Rows);
        Assert.Equal("充电中", listNoLast.StatusText);
        Assert.Equal("--", listNoLast.ValueText);
        Assert.True(listNoLast.IsCharging);

        // 图标在未知百分比充电态也呈现 50% 绿色填充，不退化为全灰。
        var iconNoLast = Presentation.Icon(noLast, darkTaskbar: true, threshold: 15);
        Assert.Null(iconNoLast.Percentage);
        Assert.Equal(50, iconNoLast.EffectiveFillPercentage);
        uint[] pixels16 = TrayIconPreview.Render(16, iconNoLast);
        Assert.Contains(iconNoLast.FillInk.Premultiplied, pixels16);
        Assert.Contains(iconNoLast.DimInk.Premultiplied, pixels16);

        // 2) 已有先前读数（73%）后收到 Beken 无百分比充电帧（Fresh == false, Last != null, IsCharging == true）。
        var t1 = t0.AddMinutes(1);
        model.ApplyPresence([new("beken", null, device.Id, LinkState.Online)
        {
            Battery = new(device, 73, false, ChargingState.NotCharging, LinkState.Online, t1)
        }], t1);
        var t2 = t1.AddMinutes(2);
        model.ApplyPresence([new("beken", null, device.Id, LinkState.Online)
        {
            Battery = new(device, null, false, ChargingState.Charging, LinkState.Online, t2)
        }], t2);

        var withLast = Assert.Single(model.TrayDevices());
        Assert.True(withLast.IsCharging);
        Assert.False(withLast.Fresh);
        Assert.Equal(73, withLast.Percentage);

        var singleWithLast = HoverCardBuilder.Build(model, device.Id, t2);
        Assert.Equal(73, singleWithLast.Percentage);
        Assert.Equal("73%", singleWithLast.ValueText);
        Assert.True(singleWithLast.IsCharging);
        Assert.Equal("充电中 · 2 分钟前读取", singleWithLast.StatusLine);
        Assert.Equal("充电中 · 2 分钟前读取",
            HoverCardBuilder.SingleStatusLine(withLast, BatteryTone.Charging, t2, lastCharge: t0));

        var listWithLast = Assert.Single(HoverCardBuilder.Build(model, null, t2, forceList: true).Rows);
        Assert.Equal("充电中", listWithLast.StatusText);
        Assert.Equal("73%", listWithLast.ValueText);
        Assert.True(listWithLast.IsCharging);
        Assert.Equal("充电中", HoverCardBuilder.ListRowStatus(withLast, BatteryTone.Charging, RuntimeEstimate.Estimating));
    }
}

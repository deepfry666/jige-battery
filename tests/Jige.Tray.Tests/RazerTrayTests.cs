using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class RazerTrayTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 5, 0, 0, TimeSpan.Zero);

    private static DeviceIdentity WirelessMouse(string id = "razer-90b:dongle-1", string name = "Razer Viper V3 Pro") =>
        new(id, name, DeviceKind.Mouse, AdaptationSource.Catalog, VerificationTier.DocsOnly, DeviceConnection.Receiver)
        {
            SourceLabel = "雷蛇无线接收器"
        };

    private static DeviceIdentity WiredMouse(string id = "razer-90b:cable-1", string name = "Razer Viper V3 Pro") =>
        new(id, name, DeviceKind.Mouse, AdaptationSource.Catalog, VerificationTier.DocsOnly, DeviceConnection.UsbCable)
        {
            SourceLabel = "雷蛇 USB 直连"
        };

    [Fact]
    public void PluggingRazerUsbCableMergesWithWirelessDongleAndPreservesAliasAndTraySelection()
    {
        var dongle = WirelessMouse();
        var cable = WiredMouse();
        var settings = new TraySettings().WithAlias(dongle.Id, "主力毒蝰");
        var model = new TrayModel(settings, new());

        // 1) 平时仅无线接收器在线。
        model.ApplyPoll([new(dongle, 65, false, ChargingState.NotCharging, LinkState.Online, BaseTime)], false, BaseTime);
        var initial = Assert.Single(model.OrderedForManagement());
        Assert.Equal(dongle.Id, initial.Device.Id);
        Assert.Equal("主力毒蝰", model.Name(initial.Device));
        Assert.Equal(DeviceConnection.Receiver, initial.Device.Connection);

        // 2) 插上 USB-C 线充电：接收器返回未响应（离线），线缆返回充电中（66%），必须合并为同一设备并保留别名。
        model.ApplyPoll([
            new(dongle, null, false, ChargingState.Unknown, LinkState.Offline, BaseTime.AddMinutes(1)),
            new(cable, 66, false, ChargingState.Charging, LinkState.Online, BaseTime.AddMinutes(1))
        ], false, BaseTime.AddMinutes(1));

        var charging = Assert.Single(model.OrderedForManagement());
        Assert.Equal(dongle.Id, charging.Device.Id);
        Assert.Equal("主力毒蝰", model.Name(charging.Device));
        Assert.Equal(DeviceConnection.UsbCable, charging.Device.Connection);
        Assert.Equal(66, charging.Percentage);
        Assert.Equal(ChargingState.Charging, charging.Charging);
        Assert.Equal(LinkState.Online, charging.Link);

        // 3) 接收器心跳继续报告离线时，不得覆盖当前活跃的 USB 线缆状态。
        var (dongleChanged, _) = model.ApplyPresence(
            [new("dongle-1", null, dongle.Id, LinkState.Offline) { Connection = DeviceConnection.Receiver }],
            BaseTime.AddMinutes(1).AddSeconds(10));
        Assert.False(dongleChanged);
        Assert.Equal(LinkState.Online, Assert.Single(model.OrderedForManagement()).Link);

        // 4) 线缆心跳通过合并 ID 映射仍能正常生效。
        var (cableChanged, _) = model.ApplyPresence(
            [new("cable-1", null, cable.Id, LinkState.Offline) { Connection = DeviceConnection.UsbCable }],
            BaseTime.AddMinutes(1).AddSeconds(20));
        Assert.True(cableChanged);
        Assert.Equal(LinkState.Offline, Assert.Single(model.OrderedForManagement()).Link);

        // 5) 拔掉线缆恢复无线连接后，仍沿用同一设备 ID。
        model.ApplyPoll([new(dongle, 72, false, ChargingState.NotCharging, LinkState.Online, BaseTime.AddMinutes(2))],
            false, BaseTime.AddMinutes(2));
        var wirelessAgain = Assert.Single(model.OrderedForManagement());
        Assert.Equal(dongle.Id, wirelessAgain.Device.Id);
        Assert.Equal(DeviceConnection.Receiver, wirelessAgain.Device.Connection);
        Assert.Equal(72, wirelessAgain.Percentage);
    }

    [Fact]
    public void TwoSimultaneouslyOnlineRazerMiceOfSameModelStaySeparate()
    {
        var dongle = WirelessMouse();
        var cable = WiredMouse();
        var model = new TrayModel(new(), new());

        model.ApplyPoll([
            new(dongle, 80, false, ChargingState.NotCharging, LinkState.Online, BaseTime),
            new(cable, 45, false, ChargingState.Charging, LinkState.Online, BaseTime)
        ], false, BaseTime);

        var all = model.OrderedForManagement();
        Assert.Equal(2, all.Length);
        Assert.Contains(all, d => d.Device.Id == dongle.Id && d.Percentage == 80);
        Assert.Contains(all, d => d.Device.Id == cable.Id && d.Percentage == 45);
    }

    [Fact]
    public void HyperPollingDonglesNeverMergeWithEachOther()
    {
        var first = WirelessMouse("razer-90b:hp-1", "Razer HyperPolling Wireless Dongle");
        var second = WirelessMouse("razer-90b:hp-2", "Razer HyperPolling Wireless Dongle");
        var model = new TrayModel(new(), new());

        model.ApplyPoll([
            new(first, 90, false, ChargingState.NotCharging, LinkState.Online, BaseTime),
            new(second, 75, false, ChargingState.NotCharging, LinkState.Online, BaseTime)
        ], false, BaseTime);
        model.ApplyPoll([
            new(first, 90, false, ChargingState.NotCharging, LinkState.Online, BaseTime.AddMinutes(1)),
            new(second, null, false, ChargingState.Unknown, LinkState.Offline, BaseTime.AddMinutes(1))
        ], false, BaseTime.AddMinutes(1));

        var all = model.OrderedForManagement();
        Assert.Equal(2, all.Length);
        Assert.Contains(all, d => d.Device.Id == first.Id && d.Link == LinkState.Online);
        Assert.Contains(all, d => d.Device.Id == second.Id && d.Link == LinkState.Offline);
    }
}

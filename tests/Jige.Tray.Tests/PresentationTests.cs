using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData(31, false, (int)BatteryTone.Normal, 0x1b1b1bu)]
    [InlineData(30, false, (int)BatteryTone.Warning, 0x9d5d00u)]
    [InlineData(16, true, (int)BatteryTone.Warning, 0xfce100u)]
    [InlineData(15, true, (int)BatteryTone.Low, 0xff99a4u)]
    [InlineData(14, true, (int)BatteryTone.Low, 0xff99a4u)]
    [InlineData(0, false, (int)BatteryTone.Low, 0xc42b1cu)]
    public void TrayColorsFollowPrototype(int percent, bool dark, int tone, uint rgb)
    {
        var icon = Presentation.Icon(Device(percent), dark, 15);
        Assert.Equal((BatteryTone)tone, icon.Tone);
        Assert.Equal(new Color(rgb), icon.FillInk);
    }

    [Theory]
    [InlineData(29, 30, (int)BatteryTone.Low, "电量低，该充电了")]
    [InlineData(30, 30, (int)BatteryTone.Low, "电量低，该充电了")]
    [InlineData(31, 30, (int)BatteryTone.Warning, "电量偏低")]
    [InlineData(45, 30, (int)BatteryTone.Warning, "电量偏低")]
    [InlineData(46, 30, (int)BatteryTone.Normal, "2.4G 已连接")]
    [InlineData(12, 10, (int)BatteryTone.Warning, "电量偏低")]
    public void CustomThresholdControlsBothTrayToneAndPanelStatus(int percent, int threshold, int tone, string label)
    {
        var device = Device(percent);
        Assert.Equal((BatteryTone)tone, Presentation.Icon(device, false, threshold).Tone);
        Assert.Equal(label, Presentation.Status(device, DateTimeOffset.UnixEpoch, threshold));
    }

    [Fact]
    public void FilledCellsRoundHalfUp()
    {
        Assert.Equal(2, Presentation.FilledCells(15, 10));
        Assert.Equal(1, Presentation.FilledCells(15, 5));
    }

    [Theory]
    [InlineData(720, 1040, 0, 0, 1000, 1020, 558, 732)]
    [InlineData(0, 0, 0, 40, 1000, 1080, 0, 52)]
    [InlineData(0, 0, 40, 0, 1000, 1080, 52, 0)]
    [InlineData(960, 920, 0, 0, 960, 1080, 604, 664)]
    public void PopupHugsTaskbarEdgeAndStaysInWorkArea(int x, int y,
        int left, int top, int right, int bottom, int expectedX, int expectedY)
    {
        var icon = new Bounds(x, y, x + 20, y + 20);
        var work = new Bounds(left, top, right, bottom);
        var screen = new Bounds(0, 0, 1000, 1080);
        var result = PopupPlacement.Place(icon, screen, work, 344, 276, 12, false);
        Assert.Equal(expectedX, result.Left);
        Assert.Equal(expectedY, result.Top);
    }

    [Fact]
    public void ReminderUsesBottomRightOfNegativeCoordinateMonitor()
    {
        var work = new Bounds(-1920, 0, 0, 1040);
        Assert.Equal(new Bounds(-356, 884, -12, 1028),
            PopupPlacement.Place(default, work, work, 344, 144, 12, true));
    }

    private static DeviceStatus Device(int percent)
    {
        var identity = new DeviceIdentity("ulx:1", "Finalmouse", DeviceKind.Mouse);
        return new(identity, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new(identity, percent, false, DateTimeOffset.UnixEpoch), true);
    }
}

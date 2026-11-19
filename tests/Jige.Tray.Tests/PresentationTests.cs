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
    [InlineData(0, false, (int)BatteryTone.Low, 0xc42b1cu)]
    public void TrayColorsFollowPrototype(int percent, bool dark, int tone, uint rgb)
    {
        var icon = Presentation.Icon(Device(percent), dark);
        Assert.Equal((BatteryTone)tone, icon.Tone);
        Assert.Equal(new Color(rgb), icon.Ink);
    }

    [Fact]
    public void ChargingOverridesLowAndCachedValueUsesFadedTaskbarInk()
    {
        var charging = Device(5) with { Charging = ChargingState.Charging };
        var lit = Presentation.Icon(charging, true);
        Assert.Equal(new Color(0x6ccb5f), lit.Ink);
        Assert.True(lit.Underline);

        var stale = Presentation.Icon(charging with { Fresh = false }, true);
        Assert.Equal(new Color(0xf3f3f3, 140), stale.Ink);
        Assert.False(stale.Underline);

        Assert.True(Presentation.Icon(Device(100), false).Full);
        Assert.Null(Presentation.Icon(null, false).Percentage);
    }

    [Fact]
    public void GlyphUsesWholePixelsAndChargingUnderline()
    {
        var pixels = new uint[256];
        var icon = new IconAppearance(48, BatteryTone.Charging, new(0x0f7b0f));
        PixelDigits.Draw(pixels, icon);
        const uint green = 0xff0f7b0f;
        Assert.All(pixels, pixel => Assert.True(pixel is 0 or green));
        Assert.Equal(green, pixels[2 * 16 + 1]);
        Assert.Equal(green, pixels[2 * 16 + 10]);
        Assert.Equal(0u, pixels[5 * 16 + 8]);
        Assert.All(pixels.AsSpan(13 * 16 + 3, 10).ToArray(), pixel => Assert.Equal(green, pixel));
    }

    [Fact]
    public void FullAndUnknownUseDifferentShapes()
    {
        var full = new uint[256];
        var unknown = new uint[256];
        PixelDigits.Draw(full, new(100, BatteryTone.Normal, new(0x1b1b1b)));
        PixelDigits.Draw(unknown, new(null, BatteryTone.Idle, new(0x1b1b1b, 140)));
        Assert.Equal(0xff1b1b1bu, full[7 * 16 + 14]);
        Assert.Equal(0x8c0e0e0eu, unknown[7 * 16 + 5]);
        Assert.Equal(12, unknown.Count(pixel => pixel != 0));
        Assert.Equal(2, Presentation.FilledCells(15, 10));
        Assert.Equal(1, Presentation.FilledCells(15, 5));
    }

    [Theory]
    [InlineData(720, 1040, 0, 0, 1000, 1020, 558, 732)]
    [InlineData(0, 0, 0, 40, 1000, 1080, 0, 52)]
    [InlineData(0, 0, 40, 0, 1000, 1080, 52, 0)]
    [InlineData(960, 920, 0, 0, 960, 1080, 604, 792)]
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

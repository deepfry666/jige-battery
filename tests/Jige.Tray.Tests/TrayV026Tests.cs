using Jige.Core;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TrayV026Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(8, 1)]
    [InlineData(12, 2)]
    [InlineData(16, 2)]
    [InlineData(20, 3)]
    [InlineData(24, 3)]
    [InlineData(28, 4)]
    [InlineData(32, 4)]
    [InlineData(48, 6)]
    public void GlyphAndTrackAreWithinIconAndHaveAClearGap(int size, int barHeight)
    {
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            var layout = TrayIconLayout.Compute(size, kind, 100);
            Assert.True(layout.Glyph.IsWithin(size));
            Assert.True(layout.Track.IsWithin(size));
            Assert.False(layout.Glyph.Overlaps(layout.Track));
            Assert.True(layout.Glyph.Bottom < layout.Track.Y);
            Assert.Equal(size, layout.Track.Bottom);
            Assert.Equal(barHeight, layout.Track.Height);
            Assert.Equal(layout.Track, layout.Fill);
            if (size == 16)
                Assert.InRange(layout.Glyph.Height, 11, 12);
        }
    }

    [Fact]
    public void AllSupportedDpiSizesFitEveryDeviceGlyphAndBatteryBar()
    {
        // TrayIconManager 将系统返回的图标宽度限制在 8–256px。
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            for (int size = 8; size <= 256; size++)
            {
                var layout = TrayIconLayout.Compute(size, kind, 50);
                Assert.True(layout.Glyph.IsWithin(size));
                Assert.True(layout.Track.IsWithin(size));
                Assert.True(layout.Fill.IsWithin(size));
                Assert.True(layout.Glyph.Bottom < layout.Track.Y);
                Assert.False(layout.Glyph.Overlaps(layout.Fill));
                Assert.InRange(layout.Fill.Right, layout.Track.X, layout.Track.Right);
                if (size >= 24)
                    Assert.True(layout.Track.Height >= 3);
            }
        }
    }

    [Theory]
    [InlineData(16, 14, 0, 7, 14)]
    [InlineData(24, 20, 0, 10, 20)]
    [InlineData(32, 28, 0, 14, 28)]
    [InlineData(64, 56, 1, 28, 55)]
    public void TrackFillRoundsToNearestPixel(int size, int width, int one, int half, int ninetyNine)
    {
        foreach (var (percent, expected) in new (int?, int)[]
        {
            (null, 0), (0, 0), (1, one), (50, half), (99, ninetyNine), (100, width)
        })
        {
            var layout = TrayIconLayout.Compute(size, DeviceKind.Mouse, percent);
            Assert.Equal(width, layout.Track.Width);
            Assert.Equal(layout.Track with { Width = expected }, layout.Fill);
        }
    }

    [Theory]
    [InlineData(25, 4)]
    [InlineData(75, 11)]
    public void HalfAPixelRoundsUp(int percent, int expected) =>
        Assert.Equal(expected, TrayIconLayout.Compute(16, DeviceKind.Mouse, percent).Fill.Width);

    [Theory]
    [InlineData(false, 0xff1b1b1bu, 0x40060606u, 0xff0f7b0fu)]
    [InlineData(true, 0xfff3f3f3u, 0x403c3c3cu, 0xff6ccb5fu)]
    public void PixelGlyphsAreDistinctAndOnlyTheBarUsesBatteryColor(bool dark, uint glyphInk, uint track, uint green)
    {
        var shapes = new HashSet<string>();
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            var device = Device(kind, 48) with { Charging = ChargingState.Charging };
            var pixels = new uint[256];
            PixelTrayIcon.Draw(pixels, Presentation.Icon(device, dark, 15));

            int glyphPixels = 0;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    uint pixel = pixels[16 * y + x];
                    if (y < 12)
                    {
                        Assert.True(pixel == 0 || pixel == glyphInk);
                        if (pixel != 0)
                            glyphPixels++;
                    }
                    else if (y < 14 || x is 0 or 15)
                        Assert.Equal(0u, pixel);
                    else
                        Assert.Equal(x <= 7 ? green : track, pixel);
                }
            }
            Assert.InRange(glyphPixels, 30, 80);
            Assert.True(shapes.Add(string.Join(',', pixels.Take(16 * 12))));
        }
    }

    [Theory]
    [InlineData(100, (int)ChargingState.NotCharging)]
    [InlineData(99, (int)ChargingState.Full)]
    [InlineData(8, (int)ChargingState.Charging)]
    public void ChargedAndChargingUseChargingColor(int percent, int charging)
    {
        var device = Device(DeviceKind.Keyboard, percent) with { Charging = (ChargingState)charging };
        var icon = Presentation.Icon(device, false, 15);
        Assert.Equal(BatteryTone.Charging, icon.Tone);
        Assert.Equal(new Color(0x0f7b0f), icon.FillInk);
        Assert.Equal(new Color(0x1b1b1b), icon.GlyphInk);
    }

    [Theory]
    [InlineData(8, 0xff99a4u)]
    [InlineData(20, 0xfce100u)]
    [InlineData(80, 0xf3f3f3u)]
    public void SleepingAndWakingKeepTheirLastBatteryBar(int percent, uint rgb)
    {
        var device = Device(DeviceKind.Mouse, percent);
        var online = Presentation.Icon(device, true, 15);
        var asleep = Presentation.Icon(device with { Link = LinkState.Asleep, Fresh = false }, true, 15);
        var waking = Presentation.Icon(device with { Fresh = false, PendingRefresh = true }, true, 15);
        Assert.Equal(new Color(rgb), asleep.FillInk);
        Assert.Equal(online, asleep);
        Assert.Equal(asleep, waking);
    }

    [Fact]
    public void UnknownBatteryDrawsDeviceAndTrackWithoutFillEvenWithAnOldReading()
    {
        var staleDevice = Device(DeviceKind.Mouse, 80) with { Fresh = false, Issue = ReadIssue.Timeout };
        var icon = Presentation.Icon(staleDevice, false, 15);
        Assert.Null(icon.Percentage);
        Assert.Equal(BatteryTone.Idle, icon.Tone);

        var pixels = new uint[256];
        PixelTrayIcon.Draw(pixels, icon);
        Assert.Contains(0xff1b1b1bu, pixels.Take(16 * 12));
        for (int y = 14; y < 16; y++)
            Assert.All(pixels.AsSpan(16 * y + 1, 14).ToArray(), p => Assert.Equal(0x40060606u, p));
    }

    [Fact]
    public void BothTrayModesCarryDeviceKindAndNoActiveDeviceKeepsTheDash()
    {
        var mouse = Device(DeviceKind.Mouse, 20);
        var keyboard = Device(DeviceKind.Keyboard, 80);
        var model = new TrayModel(new() { TrayMode = TrayDisplayMode.Custom }, new());
        model.ApplyPoll([
            new(mouse.Device, 20, false, ChargingState.NotCharging, LinkState.Online, Now),
            new(keyboard.Device, 80, false, ChargingState.NotCharging, LinkState.Online, Now)
        ], false, Now);

        var custom = TrayIconPlan.Build(model, suppressTooltip: false);
        Assert.Equal(2, custom.Length);
        Assert.Contains(custom, slot => Presentation.Icon(slot.Device, true, 15).Kind == DeviceKind.Mouse);
        Assert.Contains(custom, slot => Presentation.Icon(slot.Device, true, 15).Kind == DeviceKind.Keyboard);

        model.ApplySettings(model.Settings with { TrayMode = TrayDisplayMode.LowestBattery }, Now);
        var lowest = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Equal(DeviceKind.Mouse, Presentation.Icon(lowest.Device, true, 15).Kind);

        model.ApplyPoll([], false, Now.AddMinutes(1));
        var fallback = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Null(fallback.Device);
        var pixels = new uint[256];
        PixelTrayIcon.Draw(pixels, Presentation.Icon(fallback.Device, true, 15));
        var previousDash = new uint[256];
        previousDash.AsSpan(7 * 16 + 5, 6).Fill(0x8c858585u);
        previousDash.AsSpan(8 * 16 + 5, 6).Fill(0x8c858585u);
        Assert.Equal(previousDash, pixels);
    }

    private static DeviceStatus Device(DeviceKind kind, int percent)
    {
        var identity = new DeviceIdentity($"dev:{kind}", "测试设备", kind);
        return new(identity, LinkState.Online, ReadIssue.None, ChargingState.NotCharging,
            new(identity, percent, false, Now), true);
    }
}

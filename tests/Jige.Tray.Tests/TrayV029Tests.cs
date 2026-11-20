using System.Numerics;
using Jige.Core;
using Jige.Tray;
using Jige.Tray.Windows;

namespace Jige.Tray.Tests;

public sealed class TrayV029Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(16, 10, 15, 16, 14)]
    [InlineData(20, 13, 19, 20, 18)]
    [InlineData(24, 15, 23, 24, 21)]
    [InlineData(32, 20, 30, 32, 28)]
    public void SilhouettesFillTheirBoundsWithTransparentCutouts(int size,
        int mouseWidth, int keyboardWidth, int headsetWidth, int otherWidth)
    {
        var kinds = new[] { DeviceKind.Mouse, DeviceKind.Keyboard, DeviceKind.Headset, DeviceKind.Other };
        int[] widths = [mouseWidth, keyboardWidth, headsetWidth, otherWidth];
        var shapes = new HashSet<string>();
        for (int i = 0; i < kinds.Length; i++)
        {
            var icon = Presentation.Icon(Device(kinds[i], 100), true, 15);
            uint[] pixels = TrayIconPreview.Render(size, icon);
            var solid = Enumerable.Range(0, pixels.Length).Where(index => pixels[index] != 0).ToArray();
            int left = solid.Min(index => index % size), right = solid.Max(index => index % size);
            int top = solid.Min(index => index / size), bottom = solid.Max(index => index / size);
            Assert.Equal(widths[i], right - left + 1);
            Assert.InRange(Math.Abs(left - (size - 1 - right)), 0, 1);
            Assert.InRange(Math.Abs(top - (size - 1 - bottom)), 0, 1);
            Assert.True(solid.Length > widths[i] * (bottom - top + 1) * 0.4);
            Assert.Contains(Enumerable.Range(top + 1, bottom - top - 1), y =>
            {
                var row = pixels.AsSpan(y * size + left, right - left + 1);
                int first = row.IndexOfAnyExcept(0u), last = row.LastIndexOfAnyExcept(0u);
                return last > first && row[first..last].Contains(0u);
            });
            Assert.True(shapes.Add(string.Join(',', solid)));
        }
    }

    [Fact]
    public void VectorContoursFitEverySupportedDpiSize()
    {
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            for (int size = 8; size <= 256; size++)
            {
                var layout = TrayIconLayout.Compute(size, kind, 50);
                var glyph = layout.Glyph;
                var transform = TrayDeviceShape.Transform(kind, glyph);
                foreach (var contour in TrayDeviceShape.Contours(kind))
                {
                    foreach (var point in contour)
                    {
                        var scaled = Vector2.Transform(point, transform);
                        Assert.InRange(scaled.X, 0, size);
                        Assert.InRange(scaled.Y, 0, size);
                    }
                }
                Assert.InRange(layout.Waterline, glyph.Y, glyph.Bottom);
                Assert.True(transform.M11 >= .5f);
                Assert.True(transform.M22 >= .5f);
            }
        }
    }

    [Theory]
    [InlineData(16, 16, 9, 13, 12)]
    [InlineData(20, 20, 11, 16, 15)]
    [InlineData(24, 24, 14, 20, 18)]
    [InlineData(32, 32, 18, 26, 24)]
    public void WaterlineRoundsHalfUpWithinTheSilhouette(int size,
        int mouseHeight, int keyboardHeight, int headsetHeight, int otherHeight)
    {
        var shapes = new (DeviceKind Kind, int Height)[]
        {
            (DeviceKind.Mouse, mouseHeight), (DeviceKind.Keyboard, keyboardHeight),
            (DeviceKind.Headset, headsetHeight), (DeviceKind.Other, otherHeight)
        };
        foreach (var (kind, height) in shapes)
        foreach (int? percent in new int?[] { null, 0, 1, 2, 3, 4, 24, 25, 50, 99, 100 })
        {
            int expectedRows = percent is > 0
                ? Math.Max(1, (int)Math.Round(percent.Value * height / 100d, MidpointRounding.AwayFromZero))
                : 0;
            var layout = TrayIconLayout.Compute(size, kind, percent);
            Assert.Equal(height, layout.Glyph.Height);
            Assert.Equal(expectedRows, layout.Glyph.Bottom - layout.Waterline);
        }
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [InlineData(20, false)]
    [InlineData(20, true)]
    [InlineData(24, false)]
    [InlineData(24, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public void ZeroAndUnknownAreDimAndFullColorsTheWholeSilhouette(int size, bool dark)
    {
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            var empty = Device(kind, 0);
            var missing = empty with { Last = null, Fresh = false };
            var stale = Device(kind, 80) with { Fresh = false, Issue = ReadIssue.Timeout };
            var dim = Presentation.Icon(empty, dark, 15);
            var unknown = Presentation.Icon(missing, dark, 15);
            Assert.Equal(dark ? new Color(0xffffff, 71) : new Color(0, 56), dim.DimInk);
            Assert.Null(unknown.Percentage);
            Assert.Null(Presentation.Icon(stale, dark, 15).Percentage);

            var full = Presentation.Icon(Device(kind, 100), dark, 15);
            uint[] dimPixels = TrayIconPreview.Render(size, dim);
            uint[] fullPixels = TrayIconPreview.Render(size, full);
            Assert.Equal(dimPixels, TrayIconPreview.Render(size, unknown));
            Assert.Equal(dimPixels, TrayIconPreview.Render(size, Presentation.Icon(stale, dark, 15)));
            Assert.Contains(dim.DimInk.Premultiplied, dimPixels);
            Assert.Contains(full.FillInk.Premultiplied, fullPixels);
            for (int i = 0; i < dimPixels.Length; i++)
            {
                Assert.Equal(dimPixels[i] == 0, fullPixels[i] == 0);
                Assert.Equal(dimPixels[i], (fullPixels[i] == 0 ? 0u : dim.DimInk.Premultiplied));
                Assert.Equal(fullPixels[i], (dimPixels[i] == 0 ? 0u : full.FillInk.Premultiplied));
            }
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void PartialFillColorsOnlySilhouettePixelsBelowTheWaterline(int size)
    {
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            var icon = Presentation.Icon(Device(kind, 45, ChargingState.Charging), true, 15);
            var full = TrayIconPreview.Render(size, icon with { Percentage = 100 });
            var partial = TrayIconPreview.Render(size, icon);
            var layout = TrayIconLayout.Compute(size, kind, 45);
            Assert.Contains(icon.DimInk.Premultiplied, partial);
            Assert.Contains(icon.FillInk.Premultiplied, partial);
            for (int i = 0; i < partial.Length; i++)
                Assert.Equal(i / size < layout.Waterline && full[i] != 0 ? icon.DimInk.Premultiplied : full[i], partial[i]);
        }
    }

    [Theory]
    [InlineData(80, ChargingState.NotCharging, (int)BatteryTone.Normal, 0x1b1b1bu, 0xffffffu)]
    [InlineData(20, ChargingState.NotCharging, (int)BatteryTone.Warning, 0x9d5d00u, 0xfce100u)]
    [InlineData(8, ChargingState.NotCharging, (int)BatteryTone.Low, 0xc42b1cu, 0xff99a4u)]
    [InlineData(45, ChargingState.Charging, (int)BatteryTone.Charging, 0x0f7b0fu, 0x6ccb5fu)]
    [InlineData(99, ChargingState.Full, (int)BatteryTone.Charging, 0x0f7b0fu, 0x6ccb5fu)]
    [InlineData(100, ChargingState.NotCharging, (int)BatteryTone.Charging, 0x0f7b0fu, 0x6ccb5fu)]
    public void BatteryStatesUseExistingColors(int percent, ChargingState charging, int tone,
        uint lightInk, uint darkInk)
    {
        var device = Device(DeviceKind.Keyboard, percent, charging);
        var light = Presentation.Icon(device, false, 15);
        var dark = Presentation.Icon(device, true, 15);
        Assert.Equal((BatteryTone)tone, light.Tone);
        Assert.Equal((BatteryTone)tone, dark.Tone);
        Assert.Equal(new Color(lightInk), light.FillInk);
        Assert.Equal(new Color(darkInk), dark.FillInk);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(80)]
    public void SleepingAndWakingKeepTheirLastWaterLevelAndColor(int percent)
    {
        var device = Device(DeviceKind.Mouse, percent);
        var online = Presentation.Icon(device, true, 15);
        var asleep = device with { Link = LinkState.Asleep, Fresh = false };
        var waking = device with { Fresh = false, PendingRefresh = true };
        Assert.Equal(online, Presentation.Icon(asleep, true, 15));
        Assert.Equal(online, Presentation.Icon(waking, true, 15));
        Assert.Contains($"休眠中 · 上次 {percent}%", Presentation.DeviceTooltip(new TrayModel(new(), new()), asleep));
    }

    [Fact]
    public void BothTrayModesRenderTheSameDeviceAndNoActiveDeviceKeepsTheDash()
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
        var mouseSlot = Assert.Single(custom, slot => slot.Device?.Device.Kind == DeviceKind.Mouse);
        Assert.Contains("20%", mouseSlot.Tooltip);
        var mousePixels = TrayIconPreview.Render(16, Presentation.Icon(mouseSlot.Device, true, 15));
        Assert.Contains(0xfffce100u, mousePixels);
        Assert.Contains(0x47474747u, mousePixels);

        model.ApplySettings(model.Settings with { TrayMode = TrayDisplayMode.LowestBattery }, Now);
        var lowest = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Equal(mouse.Device, lowest.Device?.Device);
        Assert.Equal(mousePixels, TrayIconPreview.Render(16, Presentation.Icon(lowest.Device, true, 15)));
        Assert.Contains("20%", lowest.Tooltip);
        Assert.Contains("80%", lowest.Tooltip);

        model.ApplyPoll([], false, Now.AddMinutes(1));
        var fallback = Assert.Single(TrayIconPlan.Build(model, suppressTooltip: false));
        Assert.Null(fallback.Device);
        var previousDash = new uint[256];
        previousDash.AsSpan(7 * 16 + 5, 6).Fill(0x8c858585u);
        previousDash.AsSpan(8 * 16 + 5, 6).Fill(0x8c858585u);
        Assert.Equal(previousDash, TrayIconPreview.Render(16, Presentation.Icon(fallback.Device, true, 15)));
    }

    [Fact]
    public void ExportPngsWhenRequested()
    {
        if (Environment.GetEnvironmentVariable("JIGE_TRAY_PNG_DIR") is not { Length: > 0 } output)
            return;

        var kinds = new[] { DeviceKind.Mouse, DeviceKind.Keyboard, DeviceKind.Headset, DeviceKind.Other };
        (int Percent, ChargingState Charging)[] states =
            [(20, ChargingState.NotCharging), (80, ChargingState.NotCharging), (45, ChargingState.Charging), (8, ChargingState.NotCharging)];
        Directory.CreateDirectory(output);
        foreach (int size in new[] { 16, 32 })
        foreach (bool dark in new[] { true, false })
        {
            IconAppearance[][] icons = states.Select(state => kinds
                .Select(kind => Presentation.Icon(Device(kind, state.Percent, state.Charging), dark, 15)).ToArray())
                .ToArray();
            TrayIconPreview.SaveSheet(Path.Combine(output, $"tray-{size}px-{(dark ? "dark" : "light")}.png"),
                size, dark ? 0x1c1c1cu : 0xeeeeeeu, icons);
        }
    }

    private static DeviceStatus Device(DeviceKind kind, int percent, ChargingState charging = ChargingState.NotCharging)
    {
        var identity = new DeviceIdentity($"dev:{kind}", "测试设备", kind);
        return new(identity, LinkState.Online, ReadIssue.None, charging, new(identity, percent, false, Now), true);
    }
}

using System.Globalization;
using Jige.Core;

namespace Jige.Tray;

internal readonly record struct TaskbarStripDevice(
    DeviceKind Kind, int? Percentage, BatteryTone Tone, bool ShowBolt = false, bool IsPlaceholder = false)
{
    public static TaskbarStripDevice Placeholder { get; } =
        new(DeviceKind.Other, null, BatteryTone.Idle, ShowBolt: false, IsPlaceholder: true);

    public string Text => Percentage is { } percent
        ? Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture) + "%"
        : "--";
    public static TaskbarStripDevice[] Build(TrayModel model)
    {
        // 自定义沿用托盘的勾选规则，并使用主面板的活跃设备排序。
        var devices = model.Settings.TrayMode == TrayDisplayMode.LowestBattery
            ? model.TrayDevices()
            : model.OrderedDevices().Where(device => device.IsActive && model.Settings.IsInTray(device.Device.Id));
        return devices.Select(device => From(device, model.ThresholdFor(device.Device.Id))).ToArray();
    }

    public static TaskbarStripDevice[] WithPlaceholder(TaskbarStripDevice[] devices) =>
        devices.Length > 0 ? devices : [Placeholder];

    public static TaskbarStripDevice From(DeviceStatus device, int threshold)
    {
        var tone = Presentation.Tone(device, threshold);
        bool showBolt = tone == BatteryTone.Charging;
        // A short sleep keeps the last reading's tone; charging without bolt renders with foreground ink.
        // Only a current charging/full observation gets a lightning bolt.
        if ((device.Link == LinkState.Asleep || device is { Link: LinkState.Online, PendingRefresh: true }) &&
            device.Last is { WasChargingOrFull: true })
            tone = BatteryTone.Charging;
        return new(device.Device.Kind, tone == BatteryTone.Idle ? null : device.Percentage, tone, showBolt);
    }
}

internal readonly record struct TaskbarStripSegment(
    TaskbarStripDevice Device, IconRect Icon, IconRect? Bolt, IconRect Text, IconRect? Separator)
{
    public int Left => Icon.X - 4;
    public int Right => Text.Right + 4;
    public int Width => Right - Left;
}

internal sealed record TaskbarStripLayout(int Width, TaskbarStripSegment[] Segments)
{
    public const int Height = 22;
    public const int GlyphWidth = 6;

    public static int PixelScale(uint dpi) => Math.Clamp((int)((dpi + 48L) / 96), 1, 8);
    public static int TextWidth(string text) => text.Length == 0 ? 0 : text.Length * (GlyphWidth + 1) - 1;

    public static TaskbarStripLayout Build(ReadOnlySpan<TaskbarStripDevice> devices)
    {
        const int padding = 4, iconSize = 11, segmentGap = 4;
        var segments = new TaskbarStripSegment[devices.Length];
        int x = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            var device = devices[i];
            var icon = new IconRect(x + padding, 5, iconSize, iconSize);
            var bolt = device.ShowBolt ? new IconRect(icon.Right + 3, 7, 5, 7) : (IconRect?)null;
            var text = new IconRect(bolt is { } flash ? flash.Right + 2 : icon.Right + 3,
                6, TextWidth(device.Text), 9);
            x = text.Right + padding;
            IconRect? separator = null;
            if (i < devices.Length - 1)
            {
                separator = new(x + segmentGap / 2, 6, 1, 10);
                x += segmentGap;
            }
            segments[i] = new(device, icon, bolt, text, separator);
        }
        return new(x, segments);
    }
}

internal static class TaskbarColorMath
{
    public static double RelativeLuminance(uint rgb)
    {
        double r = Linearize((rgb >> 16) & 255);
        double g = Linearize((rgb >> 8) & 255);
        double b = Linearize(rgb & 255);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    public static double Contrast(double firstLuminance, double secondLuminance)
    {
        double high = Math.Max(firstLuminance, secondLuminance);
        double low = Math.Min(firstLuminance, secondLuminance);
        return (high + 0.05) / (low + 0.05);
    }

    public static double WorstContrast(uint inkRgb, double minLuminance, double maxLuminance)
    {
        double ink = RelativeLuminance(inkRgb);
        if (ink >= minLuminance && ink <= maxLuminance)
            return 1.0;
        return Math.Min(Contrast(ink, minLuminance), Contrast(ink, maxLuminance));
    }

    public static double PerceivedLightness(uint rgb)
    {
        uint r = (rgb >> 16) & 255;
        uint g = (rgb >> 8) & 255;
        uint b = rgb & 255;
        // 对齐 WinUI 的 IsColorLight 权重（2R + 5G + B），让深浅切换与系统时钟保持一致。
        return (2 * r + 5 * g + b) / (8.0 * 255.0);
    }

    public static double OkLabDistance(uint firstRgb, uint secondRgb)
    {
        var (l1, a1, b1) = ToOkLab(firstRgb);
        var (l2, a2, b2) = ToOkLab(secondRgb);
        double dl = l1 - l2, da = a1 - a2, db = b1 - b2;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    public static uint BlendOver(uint foregroundRgb, byte alpha, uint backgroundRgb)
    {
        if (alpha == 255)
            return foregroundRgb & 0x00ffffffu;
        if (alpha == 0)
            return backgroundRgb & 0x00ffffffu;
        uint inverse = 255u - alpha;
        uint r = (((foregroundRgb >> 16) & 255) * alpha + ((backgroundRgb >> 16) & 255) * inverse + 127) / 255;
        uint g = (((foregroundRgb >> 8) & 255) * alpha + ((backgroundRgb >> 8) & 255) * inverse + 127) / 255;
        uint b = ((foregroundRgb & 255) * alpha + (backgroundRgb & 255) * inverse + 127) / 255;
        return (r << 16) | (g << 8) | b;
    }

    public static uint GrayFromLuminance(double luminance)
    {
        double clamped = Math.Clamp(luminance, 0.0, 1.0);
        double srgb = clamped <= 0.0031308
            ? clamped * 12.92
            : 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055;
        uint channel = (uint)Math.Clamp((int)Math.Round(srgb * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        return (channel << 16) | (channel << 8) | channel;
    }

    public static uint AbgrToRgb(uint abgr) =>
        ((abgr & 255) << 16) | (abgr & 0xff00u) | ((abgr >> 16) & 255);

    private static readonly double[] LinearTable = BuildLinearTable();

    private static double[] BuildLinearTable()
    {
        var table = new double[256];
        for (int i = 0; i < 256; i++)
        {
            double value = i / 255.0;
            table[i] = value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return table;
    }

    private static double Linearize(uint channel) => LinearTable[channel & 255];

    private static (double L, double A, double B) ToOkLab(uint rgb)
    {
        double r = Linearize((rgb >> 16) & 255);
        double g = Linearize((rgb >> 8) & 255);
        double b = Linearize(rgb & 255);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }
}

internal readonly record struct TaskbarBackdrop(uint Rgb, double MinLuminance, double MaxLuminance, bool Sampled)
{
    public const uint DefaultDarkRgb = 0x1c1f28u;
    public const uint DefaultLightRgb = 0xeeeeeeu;

    public static TaskbarBackdrop FromDark(bool dark) =>
        Uniform(dark ? DefaultDarkRgb : DefaultLightRgb, sampled: false);

    public static TaskbarBackdrop Uniform(uint rgb, bool sampled = false)
    {
        uint clean = rgb & 0x00ffffffu;
        double lum = TaskbarColorMath.RelativeLuminance(clean);
        return new(clean, lum, lum, sampled);
    }

    public static TaskbarBackdrop FromSystemTheme(bool systemUsesLightTheme, bool colorPrevalence,
        uint? taskbarAccentRgb, bool highContrast, uint? highContrastBackgroundRgb)
    {
        if (highContrast && highContrastBackgroundRgb is uint hcBg)
            return Uniform(hcBg, sampled: false);
        if (systemUsesLightTheme)
            return Uniform(DefaultLightRgb, sampled: false);
        if (colorPrevalence && taskbarAccentRgb is uint accentBg)
            return Uniform(accentBg, sampled: false);
        return Uniform(DefaultDarkRgb, sampled: false);
    }

    public static TaskbarBackdrop FromBgraSamples(ReadOnlySpan<uint> bgraPixels)
    {
        if (bgraPixels.IsEmpty)
            return FromDark(true);

        const int maxSamples = 128;
        int sampleCount = Math.Min(bgraPixels.Length, maxSamples);
        Span<double> luminances = stackalloc double[sampleCount];
        long sumR = 0, sumG = 0, sumB = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            int sourceIndex = sampleCount == bgraPixels.Length
                ? i
                : (int)((long)i * bgraPixels.Length / sampleCount);
            uint rgb = bgraPixels[sourceIndex] & 0x00ffffffu;
            sumR += (rgb >> 16) & 255;
            sumG += (rgb >> 8) & 255;
            sumB += rgb & 255;
            luminances[i] = TaskbarColorMath.RelativeLuminance(rgb);
        }

        uint meanRgb = ((uint)((sumR + sampleCount / 2) / sampleCount) << 16) |
                       ((uint)((sumG + sampleCount / 2) / sampleCount) << 8) |
                       (uint)((sumB + sampleCount / 2) / sampleCount);
        luminances.Sort();
        // 使用 P10 / P90 过滤单像素噪点，同时捕获透明任务栏后的明暗拼接壁纸。
        int trim = sampleCount / 10;
        double meanLum = TaskbarColorMath.RelativeLuminance(meanRgb);
        double minLum = Math.Min(meanLum, luminances[trim]);
        double maxLum = Math.Max(meanLum, luminances[sampleCount - 1 - trim]);
        return new(meanRgb, minLum, maxLum, Sampled: true);
    }

    public bool PrefersDarkPalette(bool? previousDark = null)
    {
        double lightness = TaskbarColorMath.PerceivedLightness(Rgb);
        return previousDark switch
        {
            true => lightness <= 0.54,
            false => lightness < 0.46,
            null => (2 * ((Rgb >> 16) & 255) + 5 * ((Rgb >> 8) & 255) + (Rgb & 255)) <= 8 * 128
        };
    }
}

internal readonly record struct TaskbarBackdropSamplePlan(Bounds? Above, Bounds? Below, Bounds? Left, Bounds? Right)
{
    public bool HasRegions => Above is not null || Below is not null || Left is not null || Right is not null;

    public static TaskbarBackdropSamplePlan Compute(Bounds bar, Bounds strip, int scale)
    {
        int s = Math.Max(1, scale);
        if (bar.Width <= 0 || bar.Height <= 0 || strip.Width <= 0 || strip.Height <= 0)
            return default;

        int leftX = Math.Max(bar.Left + s, strip.Left);
        int rightX = Math.Min(bar.Right - s, strip.Right);
        int topMargin = Math.Max(2, s * 2);
        int bottomMargin = s;
        int clearance = s;

        Bounds? above = null;
        Bounds? below = null;
        if (rightX > leftX)
        {
            int aboveTop = bar.Top + topMargin;
            int aboveBottom = strip.Top - clearance;
            if (aboveBottom > aboveTop)
            {
                int y = aboveTop + (aboveBottom - aboveTop - 1) / 2;
                above = new(leftX, y, rightX, y + 1);
            }

            int belowTop = strip.Bottom + clearance;
            int belowBottom = bar.Bottom - bottomMargin;
            if (belowBottom > belowTop)
            {
                int y = belowTop + (belowBottom - belowTop - 1) / 2;
                below = new(leftX, y, rightX, y + 1);
            }
        }

        if (above is not null || below is not null)
            return new(above, below, null, null);

        var (left, right) = ComputeSides(bar, strip, s);
        return new(null, null, left, right);
    }

    /// <summary>当上下采样条被窗口遮挡或任务栏过窄时，计算直读条左右两侧的兜底采样列。</summary>
    public static (Bounds? Left, Bounds? Right) ComputeSides(Bounds bar, Bounds strip, int scale)
    {
        int s = Math.Max(1, scale);
        if (bar.Width <= 0 || bar.Height <= 0 || strip.Width <= 0 || strip.Height <= 0)
            return default;
        int topMargin = Math.Max(2, s * 2);
        int bottomMargin = s;
        int clearance = s;
        int topY = Math.Max(bar.Top + topMargin, strip.Top);
        int bottomY = Math.Min(bar.Bottom - bottomMargin, strip.Bottom);
        if (bottomY <= topY)
            return default;

        Bounds? left = null;
        int lx = strip.Left - clearance - 1;
        if (lx >= bar.Left + s)
            left = new(lx, topY, lx + 1, bottomY);

        Bounds? right = null;
        int rx = strip.Right + clearance;
        if (rx + 1 <= bar.Right - s)
            right = new(rx, topY, rx + 1, bottomY);

        return (left, right);
    }
}

internal readonly record struct TaskbarStripPalette(
    Color Foreground,
    Color Warning,
    Color Low,
    Color Charging,
    Color Separator,
    Color? Plate = null,
    Color? PlateBorder = null,
    TaskbarStripStyle Style = TaskbarStripStyle.Auto,
    bool IsMonochrome = false,
    bool IsHighContrast = false)
{
    private const double DefaultMinContrast = 4.5;
    private const double HighContrastMinContrast = 5.5;
    private const double MinStatusDistance = 0.36;
    private const double FramelessReturnDelta = 0.2;
    private const double FramelessReturnDistance = 0.37;
    private const byte MinAutoPlateAlpha = 112;
    private const byte MinForcedDarkPlateAlpha = 144;
    private const byte MinForcedLightPlateAlpha = 160;
    private const byte MaxPlateAlpha = 224;
    private const byte PlateAlphaStep = 16;

    private readonly record struct SchemeInks(
        TaskbarStripPalette Base,
        TaskbarStripPalette Boost,
        uint ScrimRgb,
        Color Border,
        double MinContrast,
        bool Monochrome);

    private static readonly SchemeInks StandardDark = new(
        new(new(0xf0ece4), Palette.Create(true).Warning, new(0xff8080), new(0x4ce07a), new(0xffffff, 48)),
        new(new(0xffffff), new(0xffe838), new(0xff9494), new(0x62ed8d), new(0xffffff, 64)),
        0x12151cu, new(0xffffff, 30), DefaultMinContrast, false);

    private static readonly SchemeInks StandardLight = new(
        new(new(0x211d19), Palette.Create(false).Warning, new(0xb42318), new(0x0f7b38), new(0x000000, 40)),
        new(new(0x141210), new(0x763e00), new(0x9e160d), new(0x075925), new(0x000000, 52)),
        0xf6f7fau, new(0x000000, 28), DefaultMinContrast, false);

    private static readonly SchemeInks HighContrastDark = new(
        new(new(0xffffff), new(0xfff040), new(0xff8c94), new(0x52ff96), new(0xffffff, 72), IsHighContrast: true),
        new(new(0xffffff), new(0xfff566), new(0xffa3aa), new(0x78ffa8), new(0xffffff, 84), IsHighContrast: true),
        0x0b0d12u, new(0xffffff, 44), HighContrastMinContrast, false);

    private static readonly SchemeInks HighContrastLight = new(
        new(new(0x000000), new(0x703800), new(0xa30000), new(0x005922), new(0x000000, 64), IsHighContrast: true),
        new(new(0x000000), new(0x5e2e00), new(0x8c0000), new(0x004a1c), new(0x000000, 72), IsHighContrast: true),
        0xffffffu, new(0x000000, 42), HighContrastMinContrast, false);

    private static readonly SchemeInks MonochromeDark = new(
        new(new(0xf3f3f3), new(0xf3f3f3), new(0xf3f3f3), new(0xf3f3f3), new(0xffffff, 52), IsMonochrome: true),
        new(new(0xffffff), new(0xffffff), new(0xffffff), new(0xffffff), new(0xffffff, 68), IsMonochrome: true),
        0x12151cu, new(0xffffff, 30), DefaultMinContrast, true);

    private static readonly SchemeInks MonochromeLight = new(
        new(new(0x1b1b1b), new(0x1b1b1b), new(0x1b1b1b), new(0x1b1b1b), new(0x000000, 44), IsMonochrome: true),
        new(new(0x000000), new(0x000000), new(0x000000), new(0x000000), new(0x000000, 56), IsMonochrome: true),
        0xf6f7fau, new(0x000000, 28), DefaultMinContrast, true);

    private static readonly SchemeInks IceBlueDark = new(
        new(new(0xd8f1ff), new(0xffd15c), new(0xff6b8b), new(0x4ef2c8), new(0x9cd8ff, 56)),
        new(new(0xe8f7ff), new(0xffe07d), new(0xff85a0), new(0x72f7d6), new(0x9cd8ff, 72)),
        0x0d1520u, new(0x9cd8ff, 38), DefaultMinContrast, false);

    private static readonly SchemeInks IceBlueLight = new(
        new(new(0x0b4c7c), new(0x8a4b00), new(0xb81d38), new(0x007369), new(0x0b4c7c, 48)),
        new(new(0x073b61), new(0x703a00), new(0x9c122a), new(0x005951), new(0x0b4c7c, 60)),
        0xf0f7fcu, new(0x0b4c7c, 34), DefaultMinContrast, false);

    private static readonly SchemeInks SakuraDark = new(
        new(new(0xffd6e7), new(0xffe066), new(0xff7d99), new(0x7ef5b8), new(0xffc2dc, 56)),
        new(new(0xffe5f0), new(0xffea85), new(0xff94ac), new(0x96fac6), new(0xffc2dc, 72)),
        0x1b1117u, new(0xffc2dc, 40), DefaultMinContrast, false);

    private static readonly SchemeInks SakuraLight = new(
        new(new(0x8f2250), new(0x8f4e00), new(0xb81d38), new(0x0d7048), new(0x8f2250, 48)),
        new(new(0x75173f), new(0x733c00), new(0x9c122a), new(0x085938), new(0x8f2250, 60)),
        0xfdf4f8u, new(0x8f2250, 34), DefaultMinContrast, false);

    private static readonly SchemeInks AmberDark = new(
        new(new(0xffcf70), new(0xffe966), new(0xff7b6b), new(0x7ee887), new(0xffcf70, 52)),
        new(new(0xffde96), new(0xfff085), new(0xff9487), new(0x98f29f), new(0xffcf70, 68)),
        0x1c160eu, new(0xffcf70, 38), DefaultMinContrast, false);

    private static readonly SchemeInks AmberLight = new(
        new(new(0x6e3f00), new(0x8a4b00), new(0xb02215), new(0x0e6e35), new(0x6e3f00, 48)),
        new(new(0x573000), new(0x703900), new(0x94160b), new(0x085728), new(0x6e3f00, 60)),
        0xfbf7eeu, new(0x6e3f00, 34), DefaultMinContrast, false);

    private static readonly SchemeInks NeonGreenDark = new(
        new(new(0x80ff9e), new(0xfff056), new(0xff7c8a), new(0x4df5ff), new(0x80ff9e, 52)),
        new(new(0xa3ffb8), new(0xfff57a), new(0xff96a1), new(0x7af8ff), new(0x80ff9e, 68)),
        0x0e1a12u, new(0x80ff9e, 38), DefaultMinContrast, false);

    private static readonly SchemeInks NeonGreenLight = new(
        new(new(0x0b662a), new(0x8a4b00), new(0xb41e2e), new(0x006680), new(0x0b662a, 48)),
        new(new(0x065220), new(0x703900), new(0x961422), new(0x005266), new(0x0b662a, 60)),
        0xf1fbf4u, new(0x0b662a, 34), DefaultMinContrast, false);

    public bool UsesLightInk => TaskbarColorMath.RelativeLuminance(Foreground.Rgb) > 0.5;
    public bool HasPlate => Plate is not null;

    public static TaskbarStripPalette Create(bool dark) => Create(TaskbarBackdrop.FromDark(dark));

    public static TaskbarStripPalette Create(TaskbarBackdrop backdrop, TaskbarStripPalette? previous = null) =>
        Create(backdrop, TaskbarStripStyle.Auto, TaskbarStripColorScheme.Auto, previous);

    public static TaskbarStripPalette Create(TaskbarBackdrop backdrop, TaskbarStripStyle style,
        TaskbarStripColorScheme scheme, TaskbarStripPalette? previous = null)
    {
        bool dark = scheme switch
        {
            TaskbarStripColorScheme.Light => true,
            TaskbarStripColorScheme.Dark => false,
            _ => backdrop.PrefersDarkPalette(previous?.UsesLightInk)
        };
        var inks = ResolveScheme(scheme, dark);
        double minDistance = inks.Monochrome ? 0.0 : MinStatusDistance;
        bool hadSamePolarityPlate = previous is { HasPlate: true } prev &&
            prev.UsesLightInk == dark && prev.Style == style && prev.IsMonochrome == inks.Monochrome;
        double framelessContrast = hadSamePolarityPlate ? inks.MinContrast + FramelessReturnDelta : inks.MinContrast;
        double framelessDistance = inks.Monochrome ? 0.0
            : hadSamePolarityPlate ? FramelessReturnDistance : MinStatusDistance;

        if (style == TaskbarStripStyle.Frameless)
        {
            var chosenInk = Passes(inks.Base, backdrop.Rgb, backdrop.MinLuminance, backdrop.MaxLuminance,
                inks.MinContrast, minDistance)
                ? inks.Base
                : inks.Boost;
            return chosenInk with { Plate = null, PlateBorder = null, Style = style };
        }

        bool forcePlate = style is TaskbarStripStyle.Capsule or TaskbarStripStyle.Badges;
        if (!forcePlate)
        {
            if (Passes(inks.Base, backdrop.Rgb, backdrop.MinLuminance, backdrop.MaxLuminance,
                framelessContrast, framelessDistance))
                return inks.Base with { Style = style };

            if (Passes(inks.Boost, backdrop.Rgb, backdrop.MinLuminance, backdrop.MaxLuminance,
                framelessContrast, framelessDistance))
                return inks.Boost with { Style = style };
        }

        var plateInks = dark ? inks.Base : inks.Boost;
        uint minGray = TaskbarColorMath.GrayFromLuminance(backdrop.MinLuminance);
        uint maxGray = TaskbarColorMath.GrayFromLuminance(backdrop.MaxLuminance);
        byte startAlpha = forcePlate
            ? (dark ? MinForcedDarkPlateAlpha : MinForcedLightPlateAlpha)
            : MinAutoPlateAlpha;

        byte chosen = MaxPlateAlpha;
        for (int alpha = startAlpha; alpha <= MaxPlateAlpha; alpha += PlateAlphaStep)
        {
            if (PassesWithPlate(plateInks, inks.ScrimRgb, (byte)alpha, backdrop.Rgb, minGray, maxGray,
                inks.MinContrast, minDistance))
            {
                chosen = (byte)alpha;
                break;
            }
        }

        // 动态壁纸轻微波纹时保持上一档透明度，避免细微亮度抖动引发反复重绘。
        if (previous is { Plate: { } prevPlate } p && p.UsesLightInk == dark &&
            p.Style == style && p.IsMonochrome == inks.Monochrome &&
            prevPlate.Alpha >= startAlpha &&
            Math.Abs(prevPlate.Alpha - chosen) <= PlateAlphaStep &&
            PassesWithPlate(plateInks, inks.ScrimRgb, prevPlate.Alpha, backdrop.Rgb, minGray, maxGray,
                inks.MinContrast - 0.15, inks.Monochrome ? 0.0 : 0.35))
        {
            chosen = prevPlate.Alpha;
        }

        return plateInks with
        {
            Plate = new Color(inks.ScrimRgb, chosen),
            PlateBorder = inks.Border,
            Style = style
        };
    }

    /// <summary>供设置页配色下拉列表展示 3 个预览色点（主字色 / 低电色 / 充电色）。</summary>
    public static (Color Foreground, Color Low, Color Charging) PreviewSwatches(TaskbarStripColorScheme scheme, bool darkPanel)
    {
        bool dark = scheme switch
        {
            TaskbarStripColorScheme.Light => true,
            TaskbarStripColorScheme.Dark => false,
            _ => darkPanel
        };
        var p = ResolveScheme(scheme, dark).Base;
        return (p.Foreground, p.Low, p.Charging);
    }

    public (Color Icon, Color Text) Ink(BatteryTone tone, bool showBolt = true) => tone switch
    {
        BatteryTone.Warning => (Warning, Warning),
        BatteryTone.Low => (Low, Low),
        BatteryTone.Charging when !showBolt => (Foreground, Foreground),
        BatteryTone.Charging => (Foreground, Charging),
        _ => (Foreground, Foreground)
    };

    /// <summary>分块徽章样式下，按设备状态返回该徽章的底衬与边框色（纯色方案与休眠无闪电保持中性；高对比度保持中性高反差底衬）。</summary>
    public (Color Fill, Color Border) BadgeSurface(BatteryTone tone, bool showBolt)
    {
        var baseFill = Plate ?? new Color(UsesLightInk ? 0x12151cu : 0xf6f7fau, MinForcedDarkPlateAlpha);
        var baseBorder = PlateBorder ?? new Color(UsesLightInk ? 0xffffffu : 0x000000u, 30);
        if (IsMonochrome || tone is BatteryTone.Normal or BatteryTone.Idle || (tone == BatteryTone.Charging && !showBolt))
            return (baseFill, baseBorder);

        bool dark = UsesLightInk;
        Color accent = tone switch
        {
            BatteryTone.Low => Low,
            BatteryTone.Warning => Warning,
            _ => Charging
        };
        if (IsHighContrast)
            return (baseFill, accent.WithAlpha(dark ? (byte)180 : (byte)160));

        // 选用高反差低亮/高亮着色底衬，确保与状态文字叠加后的对比度仍不低于 WCAG 4.5。
        uint tint = tone switch
        {
            BatteryTone.Low => dark ? 0x260d11u : 0xfdf0f1u,
            BatteryTone.Warning => dark ? 0x241c0bu : 0xfef7e6u,
            _ => dark ? (Charging.Rgb == 0x4df5ffu || Charging.Rgb == 0x7af8ffu ? 0x0c2026u : 0x0d2116u) : 0xebf8f0u
        };
        byte fillAlpha = (byte)Math.Clamp(baseFill.Alpha + 16, dark ? 192 : 208, 240);
        return (new Color(tint, fillAlpha), accent.WithAlpha(dark ? (byte)110 : (byte)96));
    }

    private static SchemeInks ResolveScheme(TaskbarStripColorScheme scheme, bool dark) => (scheme, dark) switch
    {
        (TaskbarStripColorScheme.HighContrast, true) => HighContrastDark,
        (TaskbarStripColorScheme.HighContrast, false) => HighContrastLight,
        (TaskbarStripColorScheme.Monochrome, true) => MonochromeDark,
        (TaskbarStripColorScheme.Monochrome, false) => MonochromeLight,
        (TaskbarStripColorScheme.IceBlue, true) => IceBlueDark,
        (TaskbarStripColorScheme.IceBlue, false) => IceBlueLight,
        (TaskbarStripColorScheme.Sakura, true) => SakuraDark,
        (TaskbarStripColorScheme.Sakura, false) => SakuraLight,
        (TaskbarStripColorScheme.Amber, true) => AmberDark,
        (TaskbarStripColorScheme.Amber, false) => AmberLight,
        (TaskbarStripColorScheme.NeonGreen, true) => NeonGreenDark,
        (TaskbarStripColorScheme.NeonGreen, false) => NeonGreenLight,
        (_, true) => StandardDark,
        (_, false) => StandardLight
    };

    private static bool Passes(TaskbarStripPalette palette, uint bgRgb, double minLum, double maxLum,
        double minContrast, double minDistance)
    {
        if (TaskbarColorMath.WorstContrast(palette.Foreground.Rgb, minLum, maxLum) < minContrast ||
            TaskbarColorMath.WorstContrast(palette.Warning.Rgb, minLum, maxLum) < minContrast ||
            TaskbarColorMath.WorstContrast(palette.Low.Rgb, minLum, maxLum) < minContrast ||
            TaskbarColorMath.WorstContrast(palette.Charging.Rgb, minLum, maxLum) < minContrast)
            return false;
        if (minDistance <= 0)
            return true;
        return TaskbarColorMath.OkLabDistance(palette.Warning.Rgb, bgRgb) >= minDistance &&
               TaskbarColorMath.OkLabDistance(palette.Low.Rgb, bgRgb) >= minDistance &&
               TaskbarColorMath.OkLabDistance(palette.Charging.Rgb, bgRgb) >= minDistance;
    }

    private static bool PassesWithPlate(TaskbarStripPalette palette, uint scrimRgb, byte alpha, uint bgRgb,
        uint minGray, uint maxGray, double minContrast, double minDistance)
    {
        uint effectiveBg = TaskbarColorMath.BlendOver(scrimRgb, alpha, bgRgb);
        double bgLum = TaskbarColorMath.RelativeLuminance(effectiveBg);
        double minLum = TaskbarColorMath.RelativeLuminance(TaskbarColorMath.BlendOver(scrimRgb, alpha, minGray));
        double maxLum = TaskbarColorMath.RelativeLuminance(TaskbarColorMath.BlendOver(scrimRgb, alpha, maxGray));
        double lo = Math.Min(bgLum, Math.Min(minLum, maxLum));
        double hi = Math.Max(bgLum, Math.Max(minLum, maxLum));
        return Passes(palette, effectiveBg, lo, hi, minContrast, minDistance);
    }
}

internal enum TaskbarStripReason
{
    Ready, UserSelectedTray, MissingTaskbar, MissingNotificationArea, UnknownButtonBounds,
    UnsupportedEdge, NoRoom, NoDevices, HiddenTaskbar, Fullscreen, WindowUnavailable
}

internal readonly record struct TaskbarStripPlacement(TaskbarStripReason Reason, Bounds Bounds)
{
    public static TaskbarStripPlacement Compute(Bounds screen, Bounds? taskbar, Bounds? notification,
        int? occupiedRight, uint dpi, int width, bool visible, bool bottomEdge)
    {
        if (taskbar is not { Width: > 0, Height: > 0 } bar)
            return new(TaskbarStripReason.MissingTaskbar, default);
        const int edgeTolerance = 2;
        if (!bottomEdge || bar.Width <= bar.Height || bar.Top < screen.Top ||
            bar.Right <= screen.Left || bar.Left >= screen.Right || bar.Bottom < screen.Bottom - edgeTolerance)
            return new(TaskbarStripReason.UnsupportedEdge, default);

        int scale = TaskbarStripLayout.PixelScale(dpi);
        int height = TaskbarStripLayout.Height * scale;
        int top = bar.Top + (bar.Height - height) / 2;
        if (!visible || (bar.Height >= height && top + height > screen.Bottom))
            return new(TaskbarStripReason.HiddenTaskbar, default);
        if (notification is not { Width: > 0, Height: > 0 } tray ||
            tray.Left <= bar.Left || tray.Right > bar.Right + edgeTolerance ||
            tray.Top >= bar.Bottom || tray.Bottom <= bar.Top)
            return new(TaskbarStripReason.MissingNotificationArea, default);
        if (occupiedRight is not { } right || right <= bar.Left)
            return new(TaskbarStripReason.UnknownButtonBounds, default);

        int gap = (int)((4L * dpi + 48) / 96);
        int stripRight = Math.Min(tray.Left, screen.Right) - gap;
        int left = stripRight - width * scale;
        if (bar.Height < height || (width > 0 && left < Math.Max(right, screen.Left) + gap))
            return new(TaskbarStripReason.NoRoom, default);
        return new(TaskbarStripReason.Ready, new(left, top, stripRight, top + height));
    }
}

internal readonly record struct TaskbarStripDisplay(TaskbarStripReason Reason, Bounds Bounds)
{
    public bool ShowStrip => Reason is TaskbarStripReason.Ready or TaskbarStripReason.NoDevices &&
        Bounds.Width > 0 && Bounds.Height > 0;
    public bool IsFallback => Reason is TaskbarStripReason.MissingTaskbar or TaskbarStripReason.MissingNotificationArea or
        TaskbarStripReason.UnknownButtonBounds or TaskbarStripReason.UnsupportedEdge or
        TaskbarStripReason.NoRoom or TaskbarStripReason.WindowUnavailable;
    // 任务栏直读条与托盘图标严格二选一：仅在用户选择托盘图标或直读条临时回退时注册托盘图标。
    public bool ShowTrayIcons => Reason == TaskbarStripReason.UserSelectedTray || IsFallback;
    public string Actual => ShowTrayIcons ? "托盘图标" : "任务栏";
    public string Description => Reason switch
    {
        TaskbarStripReason.Ready => "直读条可用",
        TaskbarStripReason.UserSelectedTray => "用户选择托盘图标",
        TaskbarStripReason.MissingTaskbar => "找不到主任务栏",
        TaskbarStripReason.MissingNotificationArea => "找不到通知区",
        TaskbarStripReason.UnknownButtonBounds => "无法确认应用按钮占用范围",
        TaskbarStripReason.UnsupportedEdge => "任务栏不在屏幕底部",
        TaskbarStripReason.NoRoom => "通知区左侧空间不足",
        TaskbarStripReason.NoDevices => "暂无可显示设备",
        TaskbarStripReason.HiddenTaskbar => "任务栏已隐藏",
        TaskbarStripReason.Fullscreen => "前台全屏",
        _ => "直读条窗口不可用"
    };

    public static TaskbarStripDisplay Resolve(TrayDisplayLocation preference, TaskbarStripPlacement placement,
        bool haveDevices, bool fullscreen)
    {
        if (preference == TrayDisplayLocation.TrayIcons)
            return new(TaskbarStripReason.UserSelectedTray, default);
        if (fullscreen && placement.Reason is TaskbarStripReason.Ready or TaskbarStripReason.HiddenTaskbar)
            return new(TaskbarStripReason.Fullscreen, default);
        if (placement.Reason == TaskbarStripReason.Ready)
            return new(haveDevices ? TaskbarStripReason.Ready : TaskbarStripReason.NoDevices, placement.Bounds);
        return new(placement.Reason, placement.Bounds);
    }
}

internal readonly record struct TaskbarControl(Bounds Bounds, string Id, string ClassName, bool Button);
internal readonly record struct TaskbarControls(Bounds? NotificationArea, int? OccupiedRight)
{
    public static TaskbarControls Locate(Bounds bar, Bounds? nativeTray, ReadOnlySpan<TaskbarControl> controls)
    {
        Bounds? notification = null;
        if (nativeTray is { } native && FitsNotificationArea(native))
            notification = native;
        foreach (var control in controls)
        {
            var bounds = control.Bounds;
            bool tray = control.Id is "SystemTrayFrame" or "SystemTrayFrameGrid" or "NotificationAreaIcons" or
                "NotifyIconStack" or "ShowHiddenIconsButton" or "ControlCenterButton" or "NotificationCenterButton" ||
                control.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal) ||
                control.ClassName is "SystemTrayFrame" or "ChevronIconView" or
                    "DynamicContent1" or "DynamicContent2" or "PeopleBand";
            if (tray && FitsNotificationArea(bounds) &&
                (notification is null || bounds.Left < notification.Value.Left))
                notification = bounds;
        }

        if (notification is not { } trayBounds)
            return default;

        bool sawStart = false, sawModernTaskList = false, sawModernTaskListClass = false, sawFrame = false;
        Bounds? legacyListBounds = null;
        int taskbarFrameRight = bar.Left;
        foreach (var control in controls)
        {
            var bounds = control.Bounds;
            if (!InsideBar(bounds))
                continue;
            bool start = control.Id == "StartButton" || control.ClassName == "Start";
            bool frame = control.Id == "TaskbarFrame" ||
                control.ClassName is "TaskbarFrame" or "Taskbar.TaskbarFrame" or "Taskbar.TaskbarFrameAutomationPeer";
            sawFrame |= frame;
            if (frame && bounds.Right <= trayBounds.Left)
                taskbarFrameRight = Math.Max(taskbarFrameRight, bounds.Right);
            sawStart |= start;
            if (IsModernTaskListClass(control))
                sawModernTaskListClass = true;
            if (IsTaskListButton(control))
                sawModernTaskList = true;
            if (control.ClassName == "MSTaskListWClass" &&
                (legacyListBounds is null || bounds.Width > legacyListBounds.Value.Width))
                legacyListBounds = bounds;
        }

        // Windows 10 使用经典 Win32 任务栏：MSTaskListWClass 是任务列表容器，
        // 真正占用宽度来自容器左侧的固定控件和容器内部的各个应用按钮，而非容器自身右边界。
        // 注意 Windows 10 任务按钮同样可能带 Appid: 前缀，不能据此误判为 Windows 11 XAML 任务栏。
        bool useLegacyTaskList = legacyListBounds is not null && !sawFrame && !sawModernTaskListClass;
        var effectiveNotification = trayBounds;
        if (useLegacyTaskList && legacyListBounds is { } legacy &&
            legacy.Right > bar.Left && legacy.Right < effectiveNotification.Left)
        {
            // 开启「资讯和兴趣」时，Windows 10 会给右侧预留较宽槽位并把 DynamicContent2 靠右缩紧，
            // 导致 MSTaskListWClass.Right 与 DynamicContent2.Left 之间存在纯空白间隙；
            // 仅当该区间内实际存在其他控件（如右侧自定义工具栏、语言栏或任务栏溢出翻页按钮）时才向左收缩右边界。
            int rightObstruction = effectiveNotification.Left;
            foreach (var control in controls)
            {
                var bounds = control.Bounds;
                if (!InsideBar(bounds))
                    continue;
                if (bounds.Left >= legacy.Right - 2 && bounds.Right > legacy.Right + 2 &&
                    bounds.Left < rightObstruction)
                {
                    rightObstruction = Math.Max(legacy.Right, bounds.Left);
                }
            }
            if (rightObstruction < effectiveNotification.Left)
                effectiveNotification = effectiveNotification with { Left = rightObstruction };
        }

        int rightBoundary = effectiveNotification.Left;
        int right = useLegacyTaskList && legacyListBounds is { } legacyBounds
            ? Math.Max(bar.Left, legacyBounds.Left)
            : bar.Left;
        foreach (var control in controls)
        {
            var bounds = control.Bounds;
            if (!InsideBar(bounds))
                continue;
            bool start = control.Id == "StartButton" || control.ClassName == "Start";
            // The modern taskbar frame can span the entire HWND. Its button bounds,
            // including Start, Search, Widgets and overflow, are what consume space.
            bool taskListButton = IsTaskListButton(control);
            bool shellButton = control.Id is "SearchButton" or "TaskViewButton" or "WidgetsButton" or "OverflowButton";
            bool leftOfBoundary = useLegacyTaskList
                ? bounds.Left < rightBoundary - 2 || (bounds.Left < rightBoundary && bounds.Right <= rightBoundary + 2)
                : bounds.Left < rightBoundary;
            if (start || taskListButton || (shellButton || control.Button) && leftOfBoundary)
                right = Math.Max(right, bounds.Right);
        }

        bool sawTaskList = sawModernTaskList || useLegacyTaskList;
        int? occupied = sawTaskList ? right
            : sawFrame && right > bar.Left ? right
            : taskbarFrameRight > bar.Left ? Math.Max(right, taskbarFrameRight)
            : sawFrame ? right
            : null;
        return new(effectiveNotification, sawStart ? occupied : null);

        static bool IsModernTaskListClass(TaskbarControl control) =>
            control.ClassName is "TaskListButton" or "Taskbar.TaskListButton" or
                "Taskbar.TaskListButtonAutomationPeer" or "Taskbar.TaskListLabeledButtonPanel" ||
            control.Id == "TaskListButton";

        static bool IsTaskListButton(TaskbarControl control) =>
            IsModernTaskListClass(control) ||
            control.Id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase);

        bool InsideBar(Bounds rect) => rect.Width > 0 && rect.Height > 0 &&
            rect.Left >= bar.Left - 2 && rect.Right <= bar.Right + 2 &&
            rect.Top < bar.Bottom && rect.Bottom > bar.Top;

        bool FitsNotificationArea(Bounds rect) =>
            InsideBar(rect) && rect.Left > bar.Left + bar.Width / 3;
    }
}

/// <summary>
/// 过滤 Windows 11 任务栏 UIA 在瞬态重排期间偶发丢失应用按钮边界导致的直读条↔托盘闪烁。
/// 仅在任务栏位置、DPI 与可见状态均未变化时，对连续短暂缺失应用按钮边界给予宽限并触发快速重试。
/// </summary>
internal sealed class TaskbarControlsStabilizer
{
    public const int MaxTransientMisses = 2;
    public const long GraceMs = 1_500;

    private Bounds lastBar;
    private uint lastDpi;
    private TaskbarControls? lastValid;
    private int transientMisses;
    private long firstMissMs;

    public void Reset()
    {
        lastValid = null;
        transientMisses = 0;
        firstMissMs = 0;
    }

    public (TaskbarControls Controls, bool Retry) Apply(
        Bounds bar, uint dpi, bool visible, bool bottomEdge, TaskbarControls incoming, long nowMs)
    {
        if (!visible || !bottomEdge || incoming.NotificationArea is null)
        {
            Reset();
            return (incoming, false);
        }

        if (incoming.OccupiedRight is not null)
        {
            lastBar = bar;
            lastDpi = dpi;
            lastValid = incoming;
            transientMisses = 0;
            firstMissMs = 0;
            return (incoming, false);
        }

        if (lastValid is { } cached && lastBar == bar && lastDpi == dpi)
        {
            if (transientMisses == 0)
                firstMissMs = nowMs;
            transientMisses++;
            if (transientMisses <= MaxTransientMisses && nowMs - firstMissMs <= GraceMs)
                return (cached with { NotificationArea = incoming.NotificationArea }, true);
        }

        Reset();
        return (incoming, false);
    }
}

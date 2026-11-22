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
        // A short sleep keeps the last reading's ink, including a green reading.
        // Only a current charging/full observation gets a lightning bolt.
        if ((device.Link == LinkState.Asleep || device is { Link: LinkState.Online, PendingRefresh: true }) &&
            device.Last is { WasChargingOrFull: true } or { Percentage: 100 })
            tone = BatteryTone.Charging;
        return new(device.Device.Kind, tone == BatteryTone.Idle ? null : device.Percentage, tone, showBolt);
    }
}

internal readonly record struct TaskbarStripSegment(
    TaskbarStripDevice Device, IconRect Icon, IconRect? Bolt, IconRect Text, IconRect? Separator);

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

    private static double Linearize(uint channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

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

        // 极窄任务栏上下没有余量时，退而采样直读条左右两侧的间隙列。
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

        return new(null, null, left, right);
    }
}

internal readonly record struct TaskbarStripPalette(
    Color Foreground,
    Color Warning,
    Color Low,
    Color Charging,
    Color Separator,
    Color? Plate = null,
    Color? PlateBorder = null)
{
    private const double MinContrast = 4.5;
    private const double MinStatusDistance = 0.36;
    private const double FramelessReturnContrast = 4.7;
    private const double FramelessReturnDistance = 0.37;
    private const byte MinPlateAlpha = 112;
    private const byte MaxPlateAlpha = 224;
    private const byte PlateAlphaStep = 16;

    private static readonly TaskbarStripPalette DarkBase =
        new(new(0xf0ece4), Palette.Create(true).Warning, new(0xff8080), new(0x4ce07a), new(0xffffff, 48));
    private static readonly TaskbarStripPalette DarkBoost =
        new(new(0xffffff), new(0xffe838), new(0xff9494), new(0x62ed8d), new(0xffffff, 64));
    private static readonly TaskbarStripPalette LightBase =
        new(new(0x211d19), Palette.Create(false).Warning, new(0xb42318), new(0x0f7b38), new(0x000000, 40));
    private static readonly TaskbarStripPalette LightBoost =
        new(new(0x141210), new(0x763e00), new(0x9e160d), new(0x075925), new(0x000000, 52));

    public bool UsesLightInk => TaskbarColorMath.RelativeLuminance(Foreground.Rgb) > 0.5;
    public bool HasPlate => Plate is not null;

    public static TaskbarStripPalette Create(bool dark) => Create(TaskbarBackdrop.FromDark(dark));

    public static TaskbarStripPalette Create(TaskbarBackdrop backdrop, TaskbarStripPalette? previous = null)
    {
        bool dark = backdrop.PrefersDarkPalette(previous?.UsesLightInk);
        bool hadSamePolarityPlate = previous is { HasPlate: true } prev && prev.UsesLightInk == dark;
        double framelessContrast = hadSamePolarityPlate ? FramelessReturnContrast : MinContrast;
        double framelessDistance = hadSamePolarityPlate ? FramelessReturnDistance : MinStatusDistance;

        var basePalette = dark ? DarkBase : LightBase;
        if (Passes(basePalette, backdrop.Rgb, backdrop.MinLuminance, backdrop.MaxLuminance,
            framelessContrast, framelessDistance))
            return basePalette;

        var boostPalette = dark ? DarkBoost : LightBoost;
        if (Passes(boostPalette, backdrop.Rgb, backdrop.MinLuminance, backdrop.MaxLuminance,
            framelessContrast, framelessDistance))
            return boostPalette;

        uint scrimRgb = dark ? 0x12151cu : 0xf6f7fau;
        var inks = dark ? DarkBase : LightBoost;
        var border = dark ? new Color(0xffffff, 30) : new Color(0x000000, 28);
        uint minGray = TaskbarColorMath.GrayFromLuminance(backdrop.MinLuminance);
        uint maxGray = TaskbarColorMath.GrayFromLuminance(backdrop.MaxLuminance);

        byte chosen = MaxPlateAlpha;
        for (int alpha = MinPlateAlpha; alpha <= MaxPlateAlpha; alpha += PlateAlphaStep)
        {
            if (PassesWithPlate(inks, scrimRgb, (byte)alpha, backdrop.Rgb, minGray, maxGray,
                MinContrast, MinStatusDistance))
            {
                chosen = (byte)alpha;
                break;
            }
        }

        // 动态壁纸轻微波纹时保持上一档透明度，避免细微亮度抖动引发反复重绘。
        if (previous is { Plate: { } prevPlate } p && p.UsesLightInk == dark &&
            Math.Abs(prevPlate.Alpha - chosen) <= PlateAlphaStep &&
            PassesWithPlate(inks, scrimRgb, prevPlate.Alpha, backdrop.Rgb, minGray, maxGray, 4.35, 0.35))
        {
            chosen = prevPlate.Alpha;
        }

        return inks with { Plate = new Color(scrimRgb, chosen), PlateBorder = border };
    }

    public (Color Icon, Color Text) Ink(BatteryTone tone, bool showBolt = true) => tone switch
    {
        BatteryTone.Warning => (Warning, Warning),
        BatteryTone.Low => (Low, Low),
        BatteryTone.Charging when !showBolt => (Foreground, Foreground),
        BatteryTone.Charging => (Foreground, Charging),
        _ => (Foreground, Foreground)
    };

    private static bool Passes(TaskbarStripPalette palette, uint bgRgb, double minLum, double maxLum,
        double minContrast, double minDistance)
    {
        return TaskbarColorMath.WorstContrast(palette.Foreground.Rgb, minLum, maxLum) >= minContrast &&
               TaskbarColorMath.WorstContrast(palette.Warning.Rgb, minLum, maxLum) >= minContrast &&
               TaskbarColorMath.WorstContrast(palette.Low.Rgb, minLum, maxLum) >= minContrast &&
               TaskbarColorMath.WorstContrast(palette.Charging.Rgb, minLum, maxLum) >= minContrast &&
               TaskbarColorMath.OkLabDistance(palette.Warning.Rgb, bgRgb) >= minDistance &&
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
                control.ClassName is "SystemTrayFrame" or "ChevronIconView";
            if (tray && FitsNotificationArea(bounds) &&
                (notification is null || bounds.Left < notification.Value.Left))
                notification = bounds;
        }

        if (notification is not { } trayBounds)
            return default;

        bool sawStart = false, sawTaskList = false, sawFrame = false;
        int taskbarFrameRight = bar.Left;
        int right = bar.Left;
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
            // The modern taskbar frame can span the entire HWND. Its button bounds,
            // including Start, Search, Widgets and overflow, are what consume space.
            bool taskListButton = control.ClassName is "TaskListButton" or "Taskbar.TaskListButton" or
                "Taskbar.TaskListButtonAutomationPeer" or "Taskbar.TaskListLabeledButtonPanel" ||
                control.Id == "TaskListButton" ||
                control.Id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase);
            bool shellButton = control.Id is "SearchButton" or "TaskViewButton" or "WidgetsButton" or "OverflowButton";
            bool legacyList = control.ClassName == "MSTaskListWClass";
            sawTaskList |= taskListButton || legacyList;
            if (start || taskListButton || legacyList || (shellButton || control.Button) && bounds.Left < trayBounds.Left)
                right = Math.Max(right, bounds.Right);
        }

        int? occupied = sawTaskList ? right
            : taskbarFrameRight > bar.Left ? Math.Max(right, taskbarFrameRight)
            : sawFrame ? right
            : null;
        return new(notification, sawStart ? occupied : null);

        bool InsideBar(Bounds rect) => rect.Width > 0 && rect.Height > 0 &&
            rect.Left >= bar.Left - 2 && rect.Right <= bar.Right + 2 &&
            rect.Top < bar.Bottom && rect.Bottom > bar.Top;

        bool FitsNotificationArea(Bounds rect) =>
            InsideBar(rect) && rect.Left > bar.CenterX;
    }
}

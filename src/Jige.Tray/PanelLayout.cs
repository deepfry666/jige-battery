namespace Jige.Tray;

internal enum PanelPage { Main, Settings, Devices, Notice }

internal readonly record struct PanelRect(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

internal readonly record struct PanelSize(int Height, int Rows);
internal readonly record struct PanelOption(int Value, string Label, PanelRect Rect);

internal static class PanelLayout
{
    public const int Width = 344;
    public const int NoticeHeight = 144;
    public const int MainRowHeight = 48;
    public const int SettingsHeight = 429;
    public const int DeviceRowsTop = 99;
    public const float DeviceRowHeight = 54.25f;
    public const int DeviceBaseHeight = 153;
    public const int TransitionMilliseconds = 250;
    public const int SwitchMilliseconds = 200;
    public const int SlideDistance = 56;
    public const double EaseX1 = .1, EaseY1 = .9, EaseX2 = .2, EaseY2 = 1;
    public static readonly PanelRect Back = new(9, 11, 32, 32);
    public static readonly PanelRect GameSilent = new(287, 224, 40, 20);
    public static readonly PanelRect Autostart = new(287, 349, 40, 20);
    public static readonly PanelRect ThresholdTrack = new(153.5f, 172, 173.5f, 28);
    public static readonly PanelRect PollTrack = new(175, 301, 152, 28);
    public static ReadOnlySpan<PanelOption> ThresholdOptions => ThresholdChoices;
    public static ReadOnlySpan<PanelOption> PollOptions => PollChoices;

    private static readonly PanelOption[] ThresholdChoices =
    [
        new(10, "10%", new(155.5f, 174, 39.75f, 24)),
        new(15, "15%", new(197.25f, 174, 39.75f, 24)),
        new(20, "20%", new(239, 174, 42, 24)),
        new(30, "30%", new(283, 174, 42, 24))
    ];
    private static readonly PanelOption[] PollChoices =
    [
        new(30, "30 秒", new(177, 303, 46.4f, 24)),
        new(60, "60 秒", new(225.4f, 303, 46.4f, 24)),
        new(120, "2 分钟", new(273.8f, 303, 51.2f, 24))
    ];

    public static PanelSize Size(PanelPage page, int count, int availableHeight)
    {
        switch (page)
        {
            case PanelPage.Notice: return new(NoticeHeight, 0);
            case PanelPage.Settings: return new(SettingsHeight, 0);
            case PanelPage.Main when count == 0: return new(157, 0);
            case PanelPage.Main:
                int mainRows = Math.Min(count - 1, Math.Max(0, (availableHeight - 247) / MainRowHeight));
                return new(247 + mainRows * MainRowHeight, mainRows);
            default:
                int deviceRows = Math.Min(count, Math.Max(0, (int)((availableHeight - DeviceBaseHeight) / DeviceRowHeight)));
                return new((int)Math.Ceiling(DeviceBaseHeight + deviceRows * DeviceRowHeight), deviceRows);
        }
    }

    public static double Ease(double progress)
    {
        if (progress <= 0 || progress >= 1)
            return Math.Clamp(progress, 0, 1);
        // 按时间反解 CSS 曲线，窗口高度与合成器中的横移动画共用控制点。
        double lo = 0, hi = 1;
        for (int i = 0; i < 16; i++)
        {
            double mid = (lo + hi) / 2;
            if (Bezier(mid, EaseX1, EaseX2) < progress)
                lo = mid;
            else
                hi = mid;
        }
        return Bezier((lo + hi) / 2, EaseY1, EaseY2);
    }

    public static double Bezier(double t, double a, double b) =>
        3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
}

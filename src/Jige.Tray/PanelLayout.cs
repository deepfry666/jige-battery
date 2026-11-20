namespace Jige.Tray;

internal enum PanelPage { Main, Settings, Devices, Notice, Welcome, Updates }

internal readonly record struct PanelRect(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

internal readonly record struct PanelSize(int Height, int Rows);

internal static class PanelLayout
{
    public const int Width = 344;
    public const int NoticeHeight = 144;
    public const int MainRowHeight = 48;
    public const int FooterHeight = 42;
    public const int DiagnosticFooterHeight = 68;
    public const int DiagnosticFooterExtraHeight = DiagnosticFooterHeight - FooterHeight;
    public const int SettingsHeight = 455;
    public const int UpdatesCompactHeight = 268;
    public const int UpdatesAvailableHeight = 404;
    public const int DeviceRowsTop = 99;
    public const float DeviceRowHeight = 54.25f;
    public const int DeviceBaseHeight = 153;
    public const int TransitionMilliseconds = 250;
    public const int SwitchMilliseconds = 200;
    public const int SlideDistance = 56;
    public const double EaseX1 = .1, EaseY1 = .9, EaseX2 = .2, EaseY2 = 1;
    public static readonly PanelRect Back = new(9, 11, 32, 32);

    public static PanelSize Size(PanelPage page, int count, int availableHeight, bool diagnosticFooter = false)
    {
        int footerExtra = diagnosticFooter ? DiagnosticFooterExtraHeight : 0;
        switch (page)
        {
            case PanelPage.Notice or PanelPage.Welcome: return new(NoticeHeight, 0);
            case PanelPage.Settings: return new(SettingsHeight + footerExtra, 0);
            case PanelPage.Updates:
                int target = count > 0 ? UpdatesAvailableHeight : UpdatesCompactHeight;
                return new(Math.Min(Math.Max(UpdatesCompactHeight, availableHeight), target), 0);
            case PanelPage.Main when count == 0: return new(157, 0);
            case PanelPage.Main:
                int mainRows = Math.Min(count - 1, Math.Max(0, (availableHeight - 247) / MainRowHeight));
                return new(247 + mainRows * MainRowHeight, mainRows);
            default:
                int baseHeight = DeviceBaseHeight + footerExtra;
                int deviceRows = Math.Min(count, Math.Max(0, (int)((availableHeight - baseHeight) / DeviceRowHeight)));
                return new((int)Math.Ceiling(baseHeight + deviceRows * DeviceRowHeight), deviceRows);
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

using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.Graphics.DirectWrite.DWRITE_TEXT_ALIGNMENT;

namespace Jige.Tray.Windows;

/// <summary>
/// 自绘托盘悬停卡片窗口（H1–H6）：
/// 复用 Acrylic/圆角/阴影/DirectComposition 与系统深浅色，不抢焦点、点击穿透。
/// 收起后先隐藏保留一小段时间，指针在多个托盘图标之间来回时直接复用，不重建图形设备；闲置后由托盘释放。
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class HoverCardWindow(HWND hwnd, TrayModel model) : IDisposable
{
    public const int FadeInMs = 140;
    public const int FadeOutMs = 130;

    private Composition? composition;
    private Composition.Layer? surface;
    private HMONITOR monitor;
    private float scale;
    private bool animations, acrylic, darkTheme, shown;
    private int surfaceHeight;
    private Palette colors = null!;
    private Bounds? position;
    private HoverCardContent? lastContent;

    public HWND Handle { get; private set; } = hwnd;
    public bool Closing { get; private set; }

    // DWM 的亚克力背板不受 DirectComposition 透明度影响：淡入淡出时只有文字和底色在变淡，
    // 背板始终满不透明，看起来就是一块发灰的空板子。因此有亚克力时直接显示 / 隐藏，只在纯色背景下淡入淡出。
    private bool Fades => animations && !acrylic;

    /// <summary>显示或原地切换卡片内容；正在淡出时立即恢复不透明。</summary>
    public void Show(HoverCardContent content, RECT anchor)
    {
        bool wasClosing = Closing;
        Closing = false;
        Render(content, anchor, reset: false);
        if (!shown)
        {
            if (Fades)
                composition!.Root.FadeIn(FadeInMs / 1000.0);
            else
                composition!.Root.SetOpacity(1f);
            composition.Commit();
            PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            shown = true;
        }
        else if (wasClosing)
        {
            composition!.Root.SetOpacity(1f);
            composition.Commit();
        }
    }

    public void Update(HoverCardContent content, RECT anchor, bool reset = false)
    {
        if (!shown)
            return;
        bool wasClosing = Closing;
        Closing = false;
        Render(content, anchor, reset);
        if (wasClosing && composition is not null)
        {
            composition.Root.SetOpacity(1f);
            composition.Commit();
        }
    }

    /// <summary>开始收起；返回 true 表示正在淡出，调用方应在 <see cref="FadeOutMs"/> 后调用 <see cref="Hide"/>。</summary>
    public bool BeginClose()
    {
        if (!shown)
            return false;
        if (Closing)
            return Fades;
        Closing = true;
        if (!Fades || composition is null)
            return false;
        composition.Root.FadeOut(FadeOutMs / 1000.0);
        composition.Commit();
        return true;
    }

    /// <summary>隐藏但保留图形资源，下次悬停可立即显示；同时提示显卡驱动回收瞬态缓冲。</summary>
    public void Hide()
    {
        Closing = false;
        if (!shown)
            return;
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);
        shown = false;
        composition?.Trim();
    }

    private void Render(HoverCardContent content, RECT anchor, bool reset)
    {
        var screen = PInvoke.MonitorFromRect(in anchor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (!Desktop.TryGetMonitor(screen, out var info))
            throw new InvalidOperationException("找不到悬停卡片显示器");
        var work = new Bounds(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        var screenBounds = new Bounds(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        var iconBounds = new Bounds(anchor.left, anchor.top, anchor.right, anchor.bottom);

        int dipWidth = HoverCardContent.CardWidth;
        int dipHeight = content.Height;
        float newScale = Math.Min(Desktop.Dpi(screen) / 96f,
            Math.Min((work.Width - 16f) / dipWidth, (work.Height - 16f) / dipHeight));
        if (newScale <= 0)
            throw new InvalidOperationException("找不到悬停卡片显示器的工作区");

        bool dark = Presentation.ResolveDark(model.Settings.Theme, Desktop.DarkApps);
        bool rebuilt = reset || composition is null || monitor != screen || MathF.Abs(scale - newScale) > 0.001f || darkTheme != dark;
        if (rebuilt)
        {
            DisposeGraphics();
            monitor = screen;
            scale = newScale;
            animations = Desktop.Animations();
            darkTheme = dark;
            acrylic = Desktop.Backdrop(Handle, dark);
            colors = Desktop.PanelPalette(dark);
            composition = new(Handle, scale);
            position = null;
        }

        if (surface is null || surfaceHeight != dipHeight)
        {
            surface?.Dispose();
            surface = null;
            surfaceHeight = 0;
            surface = new(composition!, composition!.Root, 0, 0, dipWidth, dipHeight);
            surfaceHeight = dipHeight;
        }

        bool contentChanged = rebuilt || !ContentEquals(lastContent, content);
        if (contentChanged)
        {
            surface.Draw(canvas => DrawCard(canvas, content, dipWidth, dipHeight));
            composition!.Root.Clip(dipWidth, dipHeight);
            lastContent = content;
        }

        var next = HoverCardPlacement.Place(iconBounds, screenBounds, work,
            Pixels(dipWidth), Pixels(dipHeight), Pixels(12), Pixels(8), TaskbarStripShell.ReadTaskbarEdge());
        if (position != next)
        {
            if (!PInvoke.SetWindowPos(Handle, default, next.Left, next.Top, next.Width, next.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
                throw new InvalidOperationException("无法定位悬停卡片");
            position = next;
        }

        composition!.Commit();
    }

    private void DrawCard(Canvas canvas, HoverCardContent content, int width, int height)
    {
        canvas.Fill(0, 0, width, height, 8, acrylic ? colors.Flyout : colors.Solid);
        canvas.Stroke(.5f, .5f, width - 1, height - 1, 7.5f, colors.Border);

        switch (content.Kind)
        {
            case HoverCardKind.Single:
                DrawSingle(canvas, content, width);
                break;
            case HoverCardKind.List:
                DrawList(canvas, content, width);
                break;
            default:
                DrawEmpty(canvas, content, width);
                break;
        }
    }

    private void DrawSingle(Canvas canvas, HoverCardContent content, int width)
    {
        bool idle = content.Tone == BatteryTone.Idle;
        canvas.DeviceTile(content.DeviceKind, 14, 12, 32,
            idle ? colors.Chip : colors.AccentSoft,
            idle ? colors.Muted : colors.Accent);

        float textX = 56;
        float textW = width - textX - 14;
        canvas.Text(content.DeviceName, textX, 11, textW, 19, 13.5f,
            idle ? colors.Secondary : colors.Ink, 600, ellipsis: true);

        float tagW = MathF.Ceiling(canvas.Measure(content.ConnectionTag, 10.5f, 600)) + 10;
        canvas.Fill(textX, 30, tagW, 15.5f, 4, colors.Chip);
        canvas.Text(content.ConnectionTag, textX, 30, tagW, 15.5f, 10.5f, colors.Secondary, 600,
            align: DWRITE_TEXT_ALIGNMENT_CENTER);

        float srcX = textX + tagW + 6;
        float srcW = MathF.Max(0, width - 14 - srcX);
        if (srcW > 12)
            canvas.Text(content.SourceLabel, srcX, 29, srcW, 17, 11.5f, colors.Muted, ellipsis: true);

        var numInk = NumberColor(content.Tone);
        string digits = content.Percentage?.ToString() ?? "–";
        string prefix = content.IsEstimated && content.Percentage is not null ? "≈" : "";
        string suffix = content.Percentage is null ? "" : "%";
        float prefixW = prefix.Length == 0 ? 0 : canvas.Measure(prefix, 16, 500) + 1.5f;
        float digitsW = canvas.Measure(digits, 30, 600, true) + 1;
        float suffixW = suffix.Length == 0 ? 0 : canvas.Measure(suffix, 14, 600) + 1.5f;
        float totalNumW = prefixW + digitsW + suffixW;

        float midY = 53;
        if (prefixW > 0)
            canvas.Text(prefix, 14, midY + 1, prefixW, 30, 16, colors.Muted, 500);
        canvas.Text(digits, 14 + prefixW, midY, digitsW, 34, 30, numInk, 600, true);
        if (suffixW > 0)
            canvas.Text(suffix, 14 + prefixW + digitsW, midY + 6, suffixW, 28, 14, numInk, 600);

        // 10 格电量条，与主面板视觉一致。
        float cellsX = 14 + MathF.Max(62, totalNumW) + 10;
        float cellsTotalW = width - 14 - cellsX;
        const int cellCount = 10;
        const float cellGap = 3;
        float cellW = MathF.Max(4, (cellsTotalW - cellGap * (cellCount - 1)) / cellCount);
        var barInk = colors.Status(content.Tone);
        for (int i = 0; i < cellCount; i++)
        {
            Color fill = i < content.FilledCells
                ? barInk
                : i == content.FilledCells && content.IsCharging
                    ? colors.Charging.WithAlpha(140)
                    : colors.Empty;
            canvas.Fill(cellsX + i * (cellW + cellGap), 64, cellW, 14, 2.5f, fill);
        }

        // 状态行：状态圆点 + 状态说明与读取时间。
        canvas.Dot(17, 104, 3, colors.Status(content.Tone));
        canvas.Text(content.StatusLine, 29, 95, width - 43, 18, 11.5f, colors.Secondary, ellipsis: true);
        if (content.RuntimeLine.Length > 0)
            canvas.Text(content.RuntimeLine, 29, 95 + HoverCardContent.RuntimeLineHeight, width - 43, 18, 11.5f,
                colors.Muted, ellipsis: true);
    }

    private void DrawList(Canvas canvas, HoverCardContent content, int width)
    {
        canvas.Text(content.Title, 14, 10, width - 28, 16, 11, colors.Muted, 600, ellipsis: true);

        for (int i = 0; i < content.Rows.Length; i++)
        {
            var row = content.Rows[i];
            float rowTop = HoverCardContent.ListHeaderHeight + i * HoverCardContent.ListRowHeight;
            if (i > 0)
                canvas.Line(14, rowTop + .5f, width - 14, rowTop + .5f, colors.Border);

            bool idle = row.Tone == BatteryTone.Idle;
            canvas.DeviceTile(row.Kind, 14, rowTop + 9, 24,
                idle ? colors.Chip : colors.AccentSoft,
                idle ? colors.Muted : colors.Accent);

            canvas.Text(row.Name, 45, rowTop + 5, 100, 17, 12.5f,
                idle ? colors.Muted : colors.Ink, 500, ellipsis: true);

            var statusColor = row.Tone is BatteryTone.Low or BatteryTone.Warning or BatteryTone.Charging
                ? colors.Status(row.Tone) : colors.Muted;
            canvas.Text(row.StatusText, 45, rowTop + 22, 100, 15, 11, statusColor, ellipsis: true);

            // 5 格小电量条
            const int miniCount = 5;
            const float miniW = 5;
            const float miniGap = 2;
            float miniX = 150;
            float miniY = rowTop + 16.5f;
            var barInk = colors.Status(row.Tone);
            for (int c = 0; c < miniCount; c++)
            {
                Color fill = c < row.FilledCells
                    ? barInk
                    : c == row.FilledCells && row.IsCharging
                        ? colors.Charging.WithAlpha(140)
                        : colors.Empty;
                canvas.Fill(miniX + c * (miniW + miniGap), miniY, miniW, 9, 1.5f, fill);
            }

            canvas.Text(row.ValueText, 186, rowTop, 48, HoverCardContent.ListRowHeight, 13,
                NumberColor(row.Tone), 600, true, DWRITE_TEXT_ALIGNMENT_TRAILING);
        }

        if (content.OverflowCount > 0)
        {
            float footerTop = HoverCardContent.ListHeaderHeight + content.Rows.Length * HoverCardContent.ListRowHeight;
            canvas.Line(14, footerTop + .5f, width - 14, footerTop + .5f, colors.Border);
            canvas.Text(content.OverflowText, 14, footerTop + 2, width - 28, HoverCardContent.ListOverflowHeight - 2,
                11.5f, colors.Muted, align: DWRITE_TEXT_ALIGNMENT_CENTER);
        }
    }

    private void DrawEmpty(Canvas canvas, HoverCardContent content, int width)
    {
        canvas.BrandIcon(14, 22, 20);
        canvas.Text(content.Title, 42, 13, width - 56, 18, 13.5f, colors.Ink, 600, ellipsis: true);
        canvas.Text(content.StatusLine, 42, 33, width - 56, 17, 11.5f, colors.Muted, ellipsis: true);
    }

    private Color NumberColor(BatteryTone tone) => tone switch
    {
        BatteryTone.Low or BatteryTone.Warning or BatteryTone.Charging => colors.Status(tone),
        BatteryTone.Idle => colors.Muted,
        _ => colors.Ink
    };

    private static bool ContentEquals(HoverCardContent? a, HoverCardContent b)
    {
        if (a is null)
            return false;
        if (a.Kind != b.Kind || a.Title != b.Title || a.DeviceKind != b.DeviceKind ||
            a.DeviceName != b.DeviceName || a.ConnectionTag != b.ConnectionTag ||
            a.SourceLabel != b.SourceLabel || a.Percentage != b.Percentage ||
            a.IsEstimated != b.IsEstimated || a.ValueText != b.ValueText ||
            a.Tone != b.Tone || a.FilledCells != b.FilledCells ||
            a.IsCharging != b.IsCharging || a.StatusLine != b.StatusLine || a.RuntimeLine != b.RuntimeLine ||
            a.TotalActiveCount != b.TotalActiveCount || a.OverflowCount != b.OverflowCount ||
            a.Rows.Length != b.Rows.Length)
            return false;
        for (int i = 0; i < a.Rows.Length; i++)
        {
            if (a.Rows[i] != b.Rows[i])
                return false;
        }
        return true;
    }

    private int Pixels(float dip) => (int)MathF.Round(dip * scale, MidpointRounding.AwayFromZero);

    private void DisposeGraphics()
    {
        surface?.Dispose();
        surface = null;
        surfaceHeight = 0;
        composition?.Dispose();
        composition = null;
        lastContent = null;
    }

    public void Dispose()
    {
        if (Handle.IsNull)
            return;
        var window = Handle;
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_HIDE);
        DisposeGraphics();
        Handle = default;
        PInvoke.DestroyWindow(window);
    }
}

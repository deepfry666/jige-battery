using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

/// <summary>
/// 自绘托盘右键 Fluent Acrylic 菜单：
/// 复用 DirectComposition + Direct2D + 系统深浅色与强调色，支持键鼠操作、平滑渐显/渐隐与防失焦闪退。
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class TrayMenuWindow(HWND hwnd, TrayModel model) : IDisposable
{
    public const int FadeInMs = 120;
    public const int FadeOutMs = 100;

    private Composition? composition;
    private Composition.Layer? surface;
    private HMONITOR monitor;
    private float scale;
    private bool animations, acrylic, darkTheme, shown, tracking, reclaimed, reclaimPending, captured;
    private long shownAt;
    private Palette colors = null!;
    private Bounds? position;
    private RECT anchor;
    private int hoverIndex = -1;
    private int pressedIndex = -1;
    private int focusIndex = -1;
    private int downIndex = -1;

    public HWND Handle { get; private set; } = hwnd;
    public bool Closing { get; private set; }
    // 与悬停卡片保持一致：DWM 亚克力背板不受 DComp 透明度影响，仅纯色背景才淡入淡出，避免闪灰块。
    private bool Fades => animations && !acrylic;

    public void Show(RECT menuAnchor)
    {
        anchor = menuAnchor;
        Closing = false;
        Render(reset: false);
        if (!shown)
        {
            if (Fades && composition is not null)
            {
                composition.Root.FadeIn(FadeInMs / 1000.0);
                composition.Commit();
            }
            PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOW);
            shown = true;
            shownAt = Environment.TickCount64;
            PInvoke.SetForegroundWindow(Handle);
        }
    }

    public void Refresh(RECT menuAnchor, bool reset = false)
    {
        if (Closing)
            return;
        anchor = menuAnchor;
        Render(reset);
    }

    /// <summary>显示后 400 ms 内的第一次失焦视为任务栏收回前台，重新激活而不是立刻关闭。</summary>
    public bool TryReclaimFocus()
    {
        if (reclaimPending)
            return true;
        if (reclaimed || Environment.TickCount64 - shownAt > 400)
            return false;
        reclaimed = true;
        reclaimPending = true;
        PInvoke.PostMessage(Handle, 0x8010, default, default);
        return true;
    }

    public void CompleteReclaim() => reclaimPending = false;

    public bool BeginClose()
    {
        if (Closing)
            return Fades;
        Closing = true;
        if (captured)
        {
            captured = false;
            PInvoke.ReleaseCapture();
        }
        if (!shown || !Fades || composition is null)
            return false;
        composition.Root.FadeOut(FadeOutMs / 1000.0);
        composition.Commit();
        return true;
    }

    public bool UpdateCursor()
    {
        if (!PInvoke.GetCursorPos(out var point) || !PInvoke.ScreenToClient(Handle, ref point))
            return false;
        int hit = scale > 0 ? TrayMenuModel.Hit(point.X / scale, point.Y / scale) : -1;
        int cursorId = hit >= 0 ? 32649 : 32512; // IDC_HAND / IDC_ARROW
        PInvoke.SetCursor(PInvoke.LoadCursor(default(HINSTANCE), new PCWSTR((char*)cursorId)));
        return true;
    }

    public void PointerMove(LPARAM lParam)
    {
        var (x, y) = ClientPoint(lParam);
        int next = TrayMenuModel.Hit(x, y);
        if (next != hoverIndex)
        {
            hoverIndex = next;
            if (next >= 0)
                focusIndex = -1;
            RedrawAndCommit();
        }
        if (tracking)
            return;
        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
            hwndTrack = Handle
        };
        tracking = PInvoke.TrackMouseEvent(ref track);
    }

    public void PointerLeave()
    {
        tracking = false;
        downIndex = -1;
        if (hoverIndex != -1 || pressedIndex != -1)
        {
            hoverIndex = -1;
            pressedIndex = -1;
            RedrawAndCommit();
        }
    }

    public void CaptureChanged()
    {
        captured = false;
        if (downIndex != -1 || pressedIndex != -1)
        {
            downIndex = -1;
            pressedIndex = -1;
            RedrawAndCommit();
        }
    }

    public TrayMenuCommand PointerButton(LPARAM lParam, bool pressed)
    {
        var (x, y) = ClientPoint(lParam);
        int hit = TrayMenuModel.Hit(x, y);
        hoverIndex = hit;
        if (pressed)
        {
            downIndex = hit;
            pressedIndex = hit;
            focusIndex = -1;
            if (hit >= 0)
            {
                PInvoke.SetCapture(Handle);
                captured = true;
            }
            RedrawAndCommit();
            return TrayMenuCommand.None;
        }

        if (captured)
        {
            captured = false;
            PInvoke.ReleaseCapture();
        }
        int target = hit >= 0 && hit == downIndex ? hit : -1;
        downIndex = -1;
        pressedIndex = -1;
        RedrawAndCommit();
        return TrayMenuModel.CommandAt(target);
    }

    public (TrayMenuCommand Command, bool Dismiss) Key(nuint key)
    {
        switch (key)
        {
            case 0x1B: // VK_ESCAPE
                return (TrayMenuCommand.None, true);
            case 0x09: // VK_TAB
                MoveSelection(PInvoke.GetKeyState(0x10) < 0 ? -1 : 1);
                return (TrayMenuCommand.None, false);
            case 0x26: // VK_UP
                MoveSelection(-1);
                return (TrayMenuCommand.None, false);
            case 0x28: // VK_DOWN
                MoveSelection(1);
                return (TrayMenuCommand.None, false);
            case 0x24: // VK_HOME
                SelectIndex(0);
                return (TrayMenuCommand.None, false);
            case 0x23: // VK_END
                SelectIndex(TrayMenuModel.Items.Length - 1);
                return (TrayMenuCommand.None, false);
            case 0x0D or 0x20: // VK_RETURN / VK_SPACE
                int active = focusIndex >= 0 ? focusIndex : hoverIndex;
                return (TrayMenuModel.CommandAt(active), false);
            default:
                return (TrayMenuCommand.None, false);
        }
    }

    private void MoveSelection(int delta)
    {
        int current = focusIndex >= 0 ? focusIndex : hoverIndex;
        SelectIndex(TrayMenuModel.MoveSelection(current, delta));
    }

    private void SelectIndex(int index)
    {
        if (index == focusIndex && hoverIndex == -1)
            return;
        focusIndex = index;
        hoverIndex = -1;
        pressedIndex = -1;
        RedrawAndCommit();
    }

    private void Render(bool reset)
    {
        var screen = PInvoke.MonitorFromRect(in anchor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (!Desktop.TryGetMonitor(screen, out var info))
            throw new InvalidOperationException("找不到菜单显示器");
        var work = new Bounds(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        var screenBounds = new Bounds(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        var iconBounds = new Bounds(anchor.left, anchor.top, anchor.right, anchor.bottom);

        int dipWidth = TrayMenuModel.Width;
        int dipHeight = TrayMenuModel.TotalHeight;
        float newScale = Math.Min(Desktop.Dpi(screen) / 96f,
            Math.Min((work.Width - 16f) / dipWidth, (work.Height - 16f) / dipHeight));
        if (newScale <= 0)
            throw new InvalidOperationException("找不到菜单显示器的工作区");

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
            surface = new(composition, composition.Root, 0, 0, dipWidth, dipHeight);
            composition.Root.Clip(dipWidth, dipHeight);
            position = null;
        }

        Redraw();

        var next = TrayMenuModel.Place(iconBounds, screenBounds, work,
            Pixels(dipWidth), Pixels(dipHeight), Pixels(10), Pixels(8), TaskbarStripShell.ReadTaskbarEdge());
        if (position != next)
        {
            if (!PInvoke.SetWindowPos(Handle, default, next.Left, next.Top, next.Width, next.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
                throw new InvalidOperationException("无法定位托盘菜单");
            position = next;
        }

        composition!.Commit();
    }

    private void RedrawAndCommit()
    {
        if (surface is null || composition is null)
            return;
        Redraw();
        composition.Commit();
    }

    private void Redraw() => surface?.Draw(DrawMenu);

    private void DrawMenu(Canvas canvas)
    {
        int width = TrayMenuModel.Width;
        int height = TrayMenuModel.TotalHeight;
        canvas.Fill(0, 0, width, height, TrayMenuModel.CornerRadius, acrylic ? colors.Flyout : colors.Solid);
        canvas.Stroke(.5f, .5f, width - 1, height - 1, TrayMenuModel.CornerRadius - .5f, colors.Border);

        for (int i = 0; i < TrayMenuModel.Items.Length; i++)
        {
            var item = TrayMenuModel.Items[i];
            if (TrayMenuModel.SeparatorY(i) is float sepY)
                canvas.Line(12, sepY, width - 12, sepY, colors.Border);

            var rect = TrayMenuModel.ItemRect(i);
            bool hot = hoverIndex == i || focusIndex == i;
            if (hot)
                canvas.Fill(rect.X, rect.Y, rect.Width, rect.Height, TrayMenuModel.ItemRadius,
                    pressedIndex == i ? colors.Press : colors.Hover);
            if (focusIndex == i)
                canvas.Stroke(rect.X + .5f, rect.Y + .5f, rect.Width - 1, rect.Height - 1,
                    TrayMenuModel.ItemRadius - .5f, colors.Accent);

            var iconInk = hot ? colors.Ink : colors.Secondary;
            DrawItemIcon(canvas, item.Command, rect.X + 18, rect.Y + rect.Height / 2f, iconInk);
            canvas.Text(item.Label, rect.X + 35, rect.Y, rect.Width - 43, rect.Height, 13, colors.Ink);
        }
    }

    private static void DrawItemIcon(Canvas canvas, TrayMenuCommand command, float cx, float cy, Color ink)
    {
        switch (command)
        {
            case TrayMenuCommand.Refresh:
                canvas.Arc(cx, cy, 5.2f, 0, MathF.PI * 1.72f, ink, 1.35f);
                canvas.Line(cx + 4.1f, cy - 5.9f, cx + 4.1f, cy - 3.3f, ink, 1.35f);
                canvas.Line(cx + 4.1f, cy - 3.3f, cx + 1.5f, cy - 3.3f, ink, 1.35f);
                break;

            case TrayMenuCommand.Settings:
                canvas.SettingsGear(cx, cy, ink, scale: 0.92f, thickness: 1.25f);
                break;

            case TrayMenuCommand.Updates:
                // 简洁向上更新箭头 + 底部托盘托线
                canvas.Line(cx, cy - 5.4f, cx, cy + 2.6f, ink, 1.35f);
                canvas.Line(cx - 3.4f, cy - 2.0f, cx, cy - 5.4f, ink, 1.35f);
                canvas.Line(cx + 3.4f, cy - 2.0f, cx, cy - 5.4f, ink, 1.35f);
                canvas.Line(cx - 5.2f, cy + 3.2f, cx - 5.2f, cy + 5.2f, ink, 1.3f);
                canvas.Line(cx - 5.2f, cy + 5.2f, cx + 5.2f, cy + 5.2f, ink, 1.3f);
                canvas.Line(cx + 5.2f, cy + 5.2f, cx + 5.2f, cy + 3.2f, ink, 1.3f);
                break;

            case TrayMenuCommand.Diagnostics:
                // 圆角窗口框 + 简洁脉搏折线
                canvas.Stroke(cx - 6.0f, cy - 5.2f, 12.0f, 10.4f, 2.0f, ink, 1.25f);
                canvas.Line(cx - 3.8f, cy + 0.4f, cx - 1.8f, cy + 0.4f, ink, 1.2f);
                canvas.Line(cx - 1.8f, cy + 0.4f, cx - 0.5f, cy - 2.4f, ink, 1.2f);
                canvas.Line(cx - 0.5f, cy - 2.4f, cx + 0.9f, cy + 2.5f, ink, 1.2f);
                canvas.Line(cx + 0.9f, cy + 2.5f, cx + 2.2f, cy + 0.4f, ink, 1.2f);
                canvas.Line(cx + 2.2f, cy + 0.4f, cx + 3.8f, cy + 0.4f, ink, 1.2f);
                break;

            case TrayMenuCommand.Exit:
                // 简洁门框 + 向右退出箭头
                canvas.Line(cx - 5.2f, cy - 5.2f, cx - 0.6f, cy - 5.2f, ink, 1.3f);
                canvas.Line(cx - 5.2f, cy - 5.2f, cx - 5.2f, cy + 5.2f, ink, 1.3f);
                canvas.Line(cx - 5.2f, cy + 5.2f, cx - 0.6f, cy + 5.2f, ink, 1.3f);
                canvas.Line(cx - 1.4f, cy, cx + 5.4f, cy, ink, 1.35f);
                canvas.Line(cx + 2.4f, cy - 3.0f, cx + 5.4f, cy, ink, 1.35f);
                canvas.Line(cx + 2.4f, cy + 3.0f, cx + 5.4f, cy, ink, 1.35f);
                break;
        }
    }

    private (float X, float Y) ClientPoint(LPARAM value)
    {
        var point = PopupWindow.MessagePoint(value);
        return scale > 0 ? (point.X / scale, point.Y / scale) : (-1, -1);
    }

    private int Pixels(float dip) => (int)MathF.Round(dip * scale, MidpointRounding.AwayFromZero);

    private void DisposeGraphics()
    {
        surface?.Dispose();
        surface = null;
        composition?.Dispose();
        composition = null;
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

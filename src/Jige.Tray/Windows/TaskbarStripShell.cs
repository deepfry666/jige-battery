using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace Jige.Tray.Windows;

internal readonly record struct TaskbarStripGeometry(HWND Owner, Bounds Screen, Bounds Bar,
    TaskbarControls Controls, uint Dpi, bool Visible, bool BottomEdge)
{
    public TaskbarStripPlacement Place(int width) => TaskbarStripPlacement.Compute(Screen,
        Owner.IsNull ? null : Bar, Controls.NotificationArea, Controls.OccupiedRight, Dpi, width, Visible, BottomEdge);
}

[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class TaskbarStripShell
{
    public const uint StateChanged = 0, PositionChanged = 1, FullscreenChanged = 2;
    private const uint Register = 0, Remove = 1, GetState = 4, GetTaskbarPosition = 5;

    public static HWND Find()
    {
        fixed (char* name = "Shell_TrayWnd")
            return PInvoke.FindWindow(new PCWSTR(name), default(PCWSTR));
    }

    public static bool Subscribe(HWND host, uint message)
    {
        // ABM_NEW registers notifications. No ABM_SETPOS: no work area is reserved.
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = host, uCallbackMessage = message };
        return PInvoke.SHAppBarMessage(Register, ref data) != 0;
    }

    public static void Unsubscribe(HWND host)
    {
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = host };
        PInvoke.SHAppBarMessage(Remove, ref data);
    }

    public static TaskbarEdge? ReadTaskbarEdge()
    {
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA) };
        if (PInvoke.SHAppBarMessage(GetTaskbarPosition, ref data) == 0)
            return null;
        return (uint)data.uEdge switch
        {
            0 => TaskbarEdge.Left,
            1 => TaskbarEdge.Top,
            2 => TaskbarEdge.Right,
            3 => TaskbarEdge.Bottom,
            _ => null
        };
    }

    public static bool TryRead(HWND taskbar, out TaskbarStripGeometry geometry, out Bounds? nativeTray)
    {
        geometry = default;
        nativeTray = null;
        if (taskbar.IsNull || !PInvoke.GetWindowRect(taskbar, out var rect))
            return false;
        var monitor = PInvoke.MonitorFromWindow(taskbar, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (!Desktop.TryGetMonitor(monitor, out var info))
            return false;
        var screen = ToBounds(info.rcMonitor);
        if (screen.Width <= 0 || screen.Height <= 0)
            return false;

        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA) };
        // ABM_GETTASKBARPOS reports the Shell edge even while its live rectangle
        // has slid below the monitor. Unavailable Shell metadata is treated as unknown.
        bool bottom = PInvoke.SHAppBarMessage(GetTaskbarPosition, ref data) != 0
            ? (uint)data.uEdge == 3 : rect.bottom == screen.Bottom;
        uint dpi = PInvoke.GetDpiForWindow(taskbar);
        if (dpi == 0)
            dpi = Desktop.Dpi(monitor);
        bool autoHide = (PInvoke.SHAppBarMessage(GetState, ref data) & 1u) != 0;
        bool visible = PInvoke.IsWindowVisible(taskbar) &&
            (!autoHide || (rect.bottom <= screen.Bottom + 2 &&
                screen.Bottom - rect.top > (int)((4L * dpi + 48) / 96)));
        var bar = ToBounds(rect);
        nativeTray = ReadNativeRightBoundary(taskbar, bar);

        geometry = new(taskbar, screen, bar, default, dpi, visible, bottom);
        return true;
    }

    private static readonly string[] RightSideWidgetClasses = ["DynamicContent2", "DynamicContent1", "PeopleBand"];

    private static Bounds? ReadNativeRightBoundary(HWND taskbar, Bounds bar)
    {
        Bounds? boundary = ReadChildBounds(taskbar, "TrayNotifyWnd");
        if (boundary is not { } tray)
            return null;
        // Windows 10 通知区左侧可能有「资讯和兴趣」或「人脉」直接挂在 Shell_TrayWnd 下。
        int minLeft = bar.Left + bar.Width / 3;
        foreach (string className in RightSideWidgetClasses)
        {
            if (ReadChildBounds(taskbar, className) is { } widget &&
                widget.Left > minLeft && widget.Right <= bar.Right + 2 &&
                widget.Top < bar.Bottom && widget.Bottom > bar.Top &&
                widget.Left < tray.Left)
            {
                tray = tray with { Left = widget.Left };
            }
        }
        return tray;
    }

    private static Bounds? ReadChildBounds(HWND parent, string className)
    {
        HWND child;
        fixed (char* name = className)
            child = PInvoke.FindWindowEx(parent, default, new PCWSTR(name), default(PCWSTR));
        if (child.IsNull || !PInvoke.IsWindowVisible(child) || !PInvoke.GetWindowRect(child, out var rect))
            return null;
        var bounds = ToBounds(rect);
        return bounds is { Width: > 0, Height: > 0 } ? bounds : null;
    }

    public static Bounds ToBounds(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);
}

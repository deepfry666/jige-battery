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

    public static bool TryRead(HWND taskbar, out TaskbarStripGeometry geometry, out Bounds? nativeTray)
    {
        geometry = default;
        nativeTray = null;
        if (taskbar.IsNull || !PInvoke.GetWindowRect(taskbar, out var rect))
            return false;
        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var screen = ToBounds(Desktop.Monitor(monitor).rcMonitor);
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
        HWND tray;
        fixed (char* name = "TrayNotifyWnd")
            tray = PInvoke.FindWindowEx(taskbar, default, new PCWSTR(name), default(PCWSTR));
        if (!tray.IsNull && PInvoke.IsWindowVisible(tray) && PInvoke.GetWindowRect(tray, out var trayRect))
            nativeTray = ToBounds(trayRect);

        geometry = new(taskbar, screen, ToBounds(rect), default, dpi, visible, bottom);
        return true;
    }

    public static Bounds ToBounds(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);
}

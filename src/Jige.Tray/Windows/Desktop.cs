using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class Desktop
{
    private const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Jige Battery";

    public static bool DarkTaskbar => GetRegistry(ThemeKey, "SystemUsesLightTheme") is 0;
    public static bool DarkApps => GetRegistry(ThemeKey, "AppsUseLightTheme") is 0;
    private static bool Transparency => GetRegistry(ThemeKey, "EnableTransparency") is not 0;

    private static object? GetRegistry(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log(error.Message);
            return null;
        }
    }

    public static Palette PanelPalette(bool dark)
    {
        // AccentColorMenu 是 Windows 为当前菜单外观选择的强调色，注册表值采用 ABGR。
        var accent = GetRegistry(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu");
        if (accent is not int value)
            return Palette.Create(dark);
        uint abgr = unchecked((uint)value);
        uint rgb = ((abgr & 255) << 16) | (abgr & 0xff00) | ((abgr >> 16) & 255);
        return Palette.Create(dark, rgb);
    }

    public static void SetAutostart(bool enabled)
    {
        string installed = InstallPaths.InstalledExecutable(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string exe = File.Exists(installed) ? installed :
            Environment.ProcessPath ?? throw new InvalidOperationException("找不到当前程序路径");
        string? desired = InstallPaths.AutostartCommand(enabled, exe);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (!InstallPaths.ShouldRepairAutostart(key?.GetValue(RunName), desired))
            return;

        using var writable = Registry.CurrentUser.CreateSubKey(RunKey);
        if (desired is null)
            writable.DeleteValue(RunName, throwOnMissingValue: false);
        else
            writable.SetValue(RunName, desired);
    }

    public static bool Animations()
    {
        int enabled = 0;
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0,
            &enabled, 0) && enabled != 0;
    }

    public static bool Backdrop(HWND hwnd, bool dark)
    {
        int mode = dark ? 1 : 0;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &mode, sizeof(int));
        int corners = 2; // DWMWCP_ROUND
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(int));
        int backdrop = Transparency ? 3 : 1; // DWMSBT_TRANSIENTWINDOW / DWMSBT_NONE
        bool acrylic = PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
            &backdrop, sizeof(int)).Succeeded && backdrop == 3;
        int inset = acrylic ? -1 : 0;
        var margins = new MARGINS { cxLeftWidth = inset, cxRightWidth = inset, cyTopHeight = inset, cyBottomHeight = inset };
        return PInvoke.DwmExtendFrameIntoClientArea(hwnd, in margins).Succeeded && acrylic;
    }

    public static GameMode GameMode(HWND foreground, bool gameSilent)
    {
        var result = PInvoke.SHQueryUserNotificationState(out var state);
        return Jige.Tray.GameMode.Evaluate(result.Succeeded ? (int)state : 0, CoversMonitor(foreground), gameSilent);
    }

    private static bool CoversMonitor(HWND foreground)
    {
        if (foreground.IsNull || foreground == PInvoke.GetShellWindow() || foreground == PInvoke.GetDesktopWindow() ||
            !PInvoke.IsWindowVisible(foreground))
            return false;
        uint process;
        PInvoke.GetWindowThreadProcessId(foreground, &process);
        if (process == (uint)Environment.ProcessId)
            return false;

        RECT frame;
        if (PInvoke.DwmGetWindowAttribute(foreground, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            &frame, (uint)sizeof(RECT)).Failed && !PInvoke.GetWindowRect(foreground, out frame))
            return false;
        var monitor = Monitor(PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));
        var screen = monitor.rcMonitor;
        const int tolerance = 2;
        return screen.Width > 0 && frame.left <= screen.left + tolerance && frame.top <= screen.top + tolerance &&
            frame.right >= screen.right - tolerance && frame.bottom >= screen.bottom - tolerance;
    }

    public static MONITORINFO Monitor(HMONITOR monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, ref info);
        return info;
    }

    public static uint Dpi(HMONITOR monitor)
    {
        if (!monitor.IsNull &&
            PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint x, out _).Succeeded && x > 0)
            return x;
        uint system = PInvoke.GetDpiForSystem();
        return system == 0 ? 96 : system;
    }

    public static RECT TaskbarAnchor(Point? preferred = null)
    {
        if (preferred is { } point)
            return RECT.FromXYWH(point.X, point.Y, 1, 1);
        fixed (char* name = "Shell_TrayWnd")
        {
            var taskbar = PInvoke.FindWindow(new PCWSTR(name), default(PCWSTR));
            if (!taskbar.IsNull && PInvoke.GetWindowRect(taskbar, out var bounds))
            {
                var monitor = Monitor(PInvoke.MonitorFromWindow(taskbar, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));
                bool horizontal = bounds.Width >= bounds.Height;
                int x = horizontal ? monitor.rcWork.right - 1 : bounds.left + bounds.Width / 2;
                int y = horizontal ? bounds.top + bounds.Height / 2 : monitor.rcWork.bottom - 1;
                return RECT.FromXYWH(x, y, 1, 1);
            }
        }
        PInvoke.GetCursorPos(out var cursor);
        return RECT.FromXYWH(cursor.X, cursor.Y, 1, 1);
    }

    public static void Log(string message)
    {
        Debug.WriteLine(message);
        fixed (char* native = $"几格 {message}\n")
            PInvoke.OutputDebugString(new PCWSTR(native));
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
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
            Log("读取系统设置失败", error);
            return null;
        }
    }

    public static Palette PanelPalette(bool dark)
    {
        // AccentColorMenu 是 Windows 为当前系统外观选择的强调色（注册表值采用 ABGR）；
        // 当用户手动覆写了与系统相反的明暗模式时，使用该极性下的默认强调色以保证对比度。
        if (dark != DarkApps)
            return Palette.Create(dark);
        var accent = GetRegistry(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu");
        if (accent is not int value)
            return Palette.Create(dark);
        uint abgr = unchecked((uint)value);
        uint rgb = ((abgr & 255) << 16) | (abgr & 0xff00) | ((abgr >> 16) & 255);
        return Palette.Create(dark, rgb);
    }

    public static string? ReadInstalledDirectory()
    {
        if (GetRegistry(InstallPaths.UninstallRegistryKey, "InstallLocation") is string location &&
            !string.IsNullOrWhiteSpace(location))
            return location;
        if (GetRegistry(InstallPaths.UninstallRegistryKey, "Inno Setup: App Path") is string appPath &&
            !string.IsNullOrWhiteSpace(appPath))
            return appPath;
        return null;
    }

    public static void SetAutostart(bool enabled)
    {
        string installed = InstallPaths.ResolveInstalledExecutable(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ReadInstalledDirectory());
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

    public static GameMode GameMode(HWND foreground, TraySettings settings)
    {
        var result = PInvoke.SHQueryUserNotificationState(out var state);
        int notificationState = result.Succeeded ? (int)state : 0;
        bool busy = notificationState == 2;
        var foregroundGame = busy || settings.GameSilent && notificationState is not (3 or 4)
            ? IsGameWindow(foreground, settings.GameProcessNames, fullscreenOnly: busy)
            : ForegroundGame.None;
        return Jige.Tray.GameMode.Evaluate(notificationState, foregroundGame, settings.GameSilent);
    }

    public static bool IsFullscreen(HWND foreground) =>
        IsGameWindow(foreground, [], fullscreenOnly: true) == ForegroundGame.Fullscreen;

    private static ForegroundGame IsGameWindow(HWND foreground, string[] gameProcessNames, bool fullscreenOnly)
    {
        var kind = Classify(foreground, out uint processId);
        if (kind != ForegroundKind.Other || !PInvoke.IsWindowVisible(foreground))
            return ForegroundGame.None;

        if (fullscreenOnly)
        {
            var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(foreground, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            if ((exStyle & (WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE)) != 0)
                return ForegroundGame.None;
        }

        if (!fullscreenOnly && gameProcessNames.Length > 0 && ProcessMatches(processId, gameProcessNames))
            return ForegroundGame.ProcessName;
        RECT frame;
        if (PInvoke.DwmGetWindowAttribute(foreground, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            &frame, (uint)sizeof(RECT)).Failed && !PInvoke.GetWindowRect(foreground, out frame))
            return ForegroundGame.None;
        var monitor = Monitor(PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));
        const int styleIndex = -16; // GWL_STYLE
        const int caption = 0x00C00000; // WS_CAPTION
        bool hasCaption = (PInvoke.GetWindowLong(foreground, (WINDOW_LONG_PTR_INDEX)styleIndex) & caption) == caption;
        return Jige.Tray.GameMode.ClassifyWindow(kind, ToBounds(frame), ToBounds(monitor.rcMonitor),
            ToBounds(monitor.rcWork), hasCaption);
    }

    private static Bounds ToBounds(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);

    private static bool ProcessMatches(uint id, string[] names)
    {
        try
        {
            using var process = Process.GetProcessById((int)id);
            return names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            // 前台进程可能已退出，继续使用窗口几何。
            return false;
        }
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

    private static readonly TrayLog EventLog = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jige", "jige.log"));

    public static void Log(string message) => EventLog.Write(message);
    public static void Log(string message, Exception error) => Log(TrayLog.Describe(message, error));
    public static string RecentLog(int lines) => EventLog.Recent(lines);

    /// <summary>R3：查询系统最后一次键鼠输入时间（毫秒），不安装任何钩子、不读取输入内容。</summary>
    public static uint? LastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        return PInvoke.GetLastInputInfo(ref info) ? info.dwTime : null;
    }

    /// <summary>A4：仅在用户点击「复制报告并去反馈」时把文本写入系统剪贴板（CF_UNICODETEXT）。</summary>
    public static bool SetClipboardText(HWND owner, string text)
    {
        if (!PInvoke.OpenClipboard(owner))
            return false;
        try
        {
            if (!PInvoke.EmptyClipboard())
                return false;
            nuint byteCount = (nuint)((text.Length + 1) * sizeof(char));
            var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, byteCount);
            if (memory.IsNull)
                return false;
            bool transferred = false;
            try
            {
                void* locked = PInvoke.GlobalLock(memory);
                if (locked == null)
                    return false;
                try
                {
                    var destination = new Span<char>(locked, text.Length + 1);
                    text.AsSpan().CopyTo(destination);
                    destination[text.Length] = '\0';
                }
                finally
                {
                    PInvoke.GlobalUnlock(memory);
                }

                const uint cfUnicodeText = 13;
                if (PInvoke.SetClipboardData(cfUnicodeText, new HANDLE(memory.Value)).IsNull)
                    return false;
                transferred = true;
                return true;
            }
            finally
            {
                if (!transferred)
                    PInvoke.GlobalFree(memory);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    public static Jige.Core.Diagnostics.DiagnosticProcessMetrics CollectProcessMetrics(DateTimeOffset startedAt)
    {
        using var process = Process.GetCurrentProcess();
        var handle = PInvoke.GetCurrentProcess();
        int gdi = (int)PInvoke.GetGuiResources(handle, GET_GUI_RESOURCES_FLAGS.GR_GDIOBJECTS);
        int user = (int)PInvoke.GetGuiResources(handle, GET_GUI_RESOURCES_FLAGS.GR_USEROBJECTS);
        var uptime = DateTimeOffset.UtcNow - startedAt;
        return new(process.PrivateMemorySize64, process.WorkingSet64, process.HandleCount, gdi, user,
            process.TotalProcessorTime, uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime);
    }

    public static ForegroundKind Classify(HWND window, out uint process)
    {
        process = 0;
        if (window.IsNull || window == PInvoke.GetShellWindow() || window == PInvoke.GetDesktopWindow())
            return ForegroundKind.System;
        uint id = 0;
        PInvoke.GetWindowThreadProcessId(window, &id);
        process = id;
        if (process == (uint)Environment.ProcessId)
            return ForegroundKind.Own;
        string windowClass = ClassOf(window);
        bool shell = process == 0 || ShellClasses.Contains(windowClass) ||
            windowClass == "Windows.UI.Core.CoreWindow" && ProcessMatches(process, XamlShellProcesses);
        return shell ? ForegroundKind.System : ForegroundKind.Other;
    }

    private static string ClassOf(HWND window)
    {
        if (window.IsNull)
            return "无";
        Span<char> buffer = stackalloc char[128];
        fixed (char* text = buffer)
        {
            int length = PInvoke.GetClassName(window, new PWSTR(text), buffer.Length);
            return length > 0 ? new string(buffer[..length]) : "未知";
        }
    }

    // 桌面、任务栏、开始菜单、通知中心和托盘溢出区都会铺满或贴边，但不是游戏。
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "WorkerW", "Progman", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow",
        "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd"
    };

    private static readonly string[] XamlShellProcesses =
        ["ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp"];
}

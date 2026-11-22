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
using Windows.Win32.UI.Accessibility;
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

    public static bool DarkTaskbar => ReadTaskbarBackdropFallback().PrefersDarkPalette();
    public static bool DarkApps => GetRegistry(ThemeKey, "AppsUseLightTheme") is 0;
    public static bool TransparencyEnabled => Transparency;
    private static bool Transparency => GetRegistry(ThemeKey, "EnableTransparency") is not 0;

    public static TaskbarBackdrop ReadTaskbarBackdropFallback()
    {
        bool highContrast = IsHighContrast();
        uint? highContrastBg = highContrast
            ? TaskbarColorMath.AbgrToRgb(PInvoke.GetSysColor(SYS_COLOR_INDEX.COLOR_WINDOW))
            : null;
        bool lightTheme = GetRegistry(ThemeKey, "SystemUsesLightTheme") is int light && light != 0;
        bool colorPrevalence = GetRegistry(ThemeKey, "ColorPrevalence") is int prevalence && prevalence != 0;
        uint? accentRgb = colorPrevalence ? ReadTaskbarAccentRgb() : null;
        return TaskbarBackdrop.FromSystemTheme(lightTheme, colorPrevalence, accentRgb, highContrast, highContrastBg);
    }

    private static bool IsHighContrast()
    {
        var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) &&
               ((uint)hc.dwFlags & 1u) != 0; // HCF_HIGHCONTRASTON
    }

    private static uint? ReadTaskbarAccentRgb()
    {
        // Windows 10/11 在 AccentPalette 索引 5（字节 20..22，RGBA 顺序）保存 AccentDark2，用作任务栏强调底色。
        if (GetRegistry(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette") is byte[] palette &&
            palette.Length >= 24)
            return ((uint)palette[20] << 16) | ((uint)palette[21] << 8) | palette[22];
        if (GetRegistry(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu") is int menu)
            return TaskbarColorMath.AbgrToRgb(unchecked((uint)menu));
        if (GetRegistry(@"Software\Microsoft\Windows\DWM", "AccentColor") is int dwm)
            return TaskbarColorMath.AbgrToRgb(unchecked((uint)dwm));
        return null;
    }

    /// <summary>
    /// 采样任务栏直读条周围未被遮挡的真实像素，仅在内存中归约为平均色与亮度区间以适配配色，不保存或记录任何画面。
    /// </summary>
    public static bool TrySampleTaskbarBackdrop(HWND taskbar, HWND stripWindow, Bounds bar, Bounds strip,
        int scale, out TaskbarBackdrop backdrop)
    {
        backdrop = default;
        if (taskbar.IsNull || !PInvoke.IsWindowVisible(taskbar))
            return false;

        var plan = TaskbarBackdropSamplePlan.Compute(bar, strip, scale);
        if (!plan.HasRegions)
            return false;

        Span<Bounds> candidates = stackalloc Bounds[2];
        int regionCount = 0;
        if (plan.Above is { } above && IsRegionUnobstructed(above, taskbar, stripWindow))
            candidates[regionCount++] = above;
        if (plan.Below is { } below && IsRegionUnobstructed(below, taskbar, stripWindow))
            candidates[regionCount++] = below;
        if (regionCount == 0)
        {
            var (left, right) = plan.Left is not null || plan.Right is not null
                ? (plan.Left, plan.Right)
                : TaskbarBackdropSamplePlan.ComputeSides(bar, strip, scale);
            if (left is { } l && IsRegionUnobstructed(l, taskbar, stripWindow))
                candidates[regionCount++] = l;
            if (right is { } r && IsRegionUnobstructed(r, taskbar, stripWindow))
                candidates[regionCount++] = r;
        }
        if (regionCount == 0)
            return false;

        int maxWidth = 0, maxHeight = 0;
        for (int i = 0; i < regionCount; i++)
        {
            maxWidth = Math.Max(maxWidth, candidates[i].Width);
            maxHeight = Math.Max(maxHeight, candidates[i].Height);
        }
        if (maxWidth <= 0 || maxHeight <= 0 || maxWidth > 4096 || maxHeight > 4096)
            return false;

        HDC screenDc = PInvoke.GetDC(default);
        if (screenDc.IsNull)
            return false;

        try
        {
            var memoryDc = PInvoke.CreateCompatibleDC(screenDc);
            if (memoryDc.IsNull)
                return false;

            try
            {
                var bitmapInfo = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)sizeof(BITMAPINFOHEADER),
                        biWidth = maxWidth,
                        biHeight = -maxHeight,
                        biPlanes = 1,
                        biBitCount = 32
                    }
                };
                void* bits = null;
                var bitmap = PInvoke.CreateDIBSection(screenDc, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
                if (bitmap.IsNull || bits == null)
                {
                    if (!bitmap.IsNull)
                        PInvoke.DeleteObject(bitmap);
                    return false;
                }

                var previous = PInvoke.SelectObject(memoryDc, new HGDIOBJ(bitmap.Value));
                if (previous.IsNull || previous.Value == (void*)-1)
                {
                    PInvoke.DeleteObject(bitmap);
                    return false;
                }

                try
                {
                    Span<uint> samples = stackalloc uint[256];
                    int totalSamples = 0;
                    int perRegionBudget = samples.Length / regionCount;
                    var buffer = new ReadOnlySpan<uint>(bits, maxWidth * maxHeight);

                    for (int i = 0; i < regionCount; i++)
                    {
                        var region = candidates[i];
                        if (!PInvoke.BitBlt(memoryDc, 0, 0, region.Width, region.Height,
                            screenDc, region.Left, region.Top, ROP_CODE.SRCCOPY))
                            continue;
                        PInvoke.GdiFlush();

                        int pixelCount = region.Width * region.Height;
                        int take = Math.Min(pixelCount, perRegionBudget);
                        for (int s = 0; s < take; s++)
                        {
                            int index = take == pixelCount ? s : (int)((long)s * pixelCount / take);
                            int row = index / region.Width;
                            int col = index % region.Width;
                            samples[totalSamples++] = buffer[row * maxWidth + col];
                        }
                    }

                    if (totalSamples == 0)
                        return false;

                    backdrop = TaskbarBackdrop.FromBgraSamples(samples[..totalSamples]);
                    return true;
                }
                finally
                {
                    PInvoke.SelectObject(memoryDc, previous);
                    PInvoke.DeleteObject(bitmap);
                }
            }
            finally
            {
                PInvoke.DeleteDC(memoryDc);
            }
        }
        finally
        {
            PInvoke.ReleaseDC(default, screenDc);
        }
    }

    private static bool IsRegionUnobstructed(Bounds region, HWND taskbar, HWND stripWindow)
    {
        if (region.Width >= region.Height)
        {
            int y = region.Top;
            return IsSamplePointUnobstructed(region.Left, y, taskbar, stripWindow) &&
                   IsSamplePointUnobstructed(region.CenterX, y, taskbar, stripWindow) &&
                   IsSamplePointUnobstructed(region.Right - 1, y, taskbar, stripWindow);
        }

        int x = region.Left;
        return IsSamplePointUnobstructed(x, region.Top, taskbar, stripWindow) &&
               IsSamplePointUnobstructed(x, region.CenterY, taskbar, stripWindow) &&
               IsSamplePointUnobstructed(x, region.Bottom - 1, taskbar, stripWindow);
    }

    private static bool IsSamplePointUnobstructed(int x, int y, HWND taskbar, HWND stripWindow)
    {
        var hit = PInvoke.WindowFromPoint(new Point(x, y));
        if (hit.IsNull)
            return false;
        var root = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root.IsNull || (!stripWindow.IsNull && root == stripWindow))
            return false;
        if (root == taskbar)
            return true;
        // 兼容 RoundedTB 等将任务栏空白区裁剪穿透到桌面壁纸的工具，同时排除弹窗、菜单与普通应用窗口。
        return ClassOf(root) is "WorkerW" or "Progman";
    }

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

    private const string StartupApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static void SetAutostart(bool enabled, bool clearTaskManagerDisable = false)
    {
        string installed = InstallPaths.ResolveInstalledExecutable(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ReadInstalledDirectory());
        string exe = File.Exists(installed) ? installed :
            Environment.ProcessPath ?? throw new InvalidOperationException("找不到当前程序路径");
        string? desired = InstallPaths.AutostartCommand(enabled, exe);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        bool needsWrite = InstallPaths.ShouldRepairAutostart(key?.GetValue(RunName), desired);

        if (needsWrite)
        {
            using var writable = Registry.CurrentUser.CreateSubKey(RunKey);
            if (desired is null)
                writable.DeleteValue(RunName, throwOnMissingValue: false);
            else
                writable.SetValue(RunName, desired);
        }

        // 仅在用户在设置里主动重新开启自启时清除任务管理器的禁用记录，不覆盖日常启动时用户的手动禁用。
        if (enabled && clearTaskManagerDisable)
        {
            try
            {
                using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKey, writable: true);
                approved?.DeleteValue(RunName, throwOnMissingValue: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
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
        if (id == 0 || names.Length == 0)
            return false;
        // 仅按 PID 直接打开受限句柄并查询映像路径，避免 Process.GetProcessById 触发整机进程/线程快照。
        HANDLE process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        if (process.IsNull)
            return false;
        try
        {
            Span<char> buffer = stackalloc char[512];
            uint length = (uint)buffer.Length;
            fixed (char* ptr = buffer)
            {
                if (!PInvoke.QueryFullProcessImageName(process, default, new PWSTR(ptr), &length) || length == 0)
                    return false;
            }
            var imagePath = buffer[..(int)length];
            int slash = imagePath.LastIndexOfAny('\\', '/');
            var fileName = slash >= 0 ? imagePath[(slash + 1)..] : imagePath;
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^4];
            foreach (string candidate in names)
            {
                if (fileName.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    public static bool TryGetMonitor(HMONITOR monitor, out MONITORINFO info)
    {
        info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        return !monitor.IsNull && PInvoke.GetMonitorInfo(monitor, ref info) &&
            info.rcMonitor.right > info.rcMonitor.left && info.rcMonitor.bottom > info.rcMonitor.top;
    }

    public static MONITORINFO Monitor(HMONITOR monitor)
    {
        TryGetMonitor(monitor, out var info);
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
                var hmon = PInvoke.MonitorFromWindow(taskbar, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
                if (TryGetMonitor(hmon, out var monitor))
                {
                    bool horizontal = bounds.Width >= bounds.Height;
                    int x = horizontal ? monitor.rcWork.right - 1 : bounds.left + bounds.Width / 2;
                    int y = horizontal ? bounds.top + bounds.Height / 2 : monitor.rcWork.bottom - 1;
                    // 自动隐藏任务栏收起时其窗口矩形会滑出显示器边缘，夹紧在显示器内避免在上下双屏落到副屏。
                    x = Math.Clamp(x, monitor.rcMonitor.left, Math.Max(monitor.rcMonitor.left, monitor.rcMonitor.right - 1));
                    y = Math.Clamp(y, monitor.rcMonitor.top, Math.Max(monitor.rcMonitor.top, monitor.rcMonitor.bottom - 1));
                    return RECT.FromXYWH(x, y, 1, 1);
                }
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

    /// <summary>A4：仅在用户点击时把文本写入系统剪贴板（CF_UNICODETEXT）。</summary>
    public static bool SetClipboardText(HWND owner, string text)
    {
        bool opened = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PInvoke.OpenClipboard(owner))
            {
                opened = true;
                break;
            }
            Thread.Sleep(15 * (attempt + 1));
        }
        if (!opened)
            return false;
        try
        {
            if (!PInvoke.EmptyClipboard())
                return false;
            nuint byteCount = checked((nuint)(text.Length + 1) * sizeof(char));
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

    public static bool IsTaskbarWindow(HWND window) =>
        !window.IsNull && TaskbarClasses.Contains(ClassOf(window));

    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow"
    };

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

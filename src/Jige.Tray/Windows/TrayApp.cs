using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed partial class TrayApp : IDisposable
{
    private const string HostClass = "Jige.Tray.Host";
    private const string PopupClass = "Jige.Tray.Popup";
    private const string HoverCardClass = "Jige.Tray.HoverCard";
    private const string MenuClass = "Jige.Tray.Menu";
    private const uint TrayMessage = 0x8001;
    private const uint ResultMessage = 0x8002;
    private const uint ForegroundMessage = 0x8003;
    private const uint MoveMessage = 0x8004;
    private const uint DiagnosticMessage = 0x8005;
    private const uint HeartbeatMessage = 0x8006;
    private const uint PushPresenceMessage = 0x8007;
    private const uint UpdateMessage = 0x8008;
    private const uint ForegroundEvent = 0x0003;
    private const uint LocationEvent = 0x800B;
    private const nuint WorkTimer = 1, GameSettleTimer = 2, CloseTimer = 3, UpdateCleanupTimer = 4,
        IconRetryTimer = 5, DeviceChangeTimer = 6, DesktopRefreshTimer = 7, HoverCloseTimer = 8,
        MenuCloseTimer = 9;
    private static readonly Guid HidInterfaceGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private static readonly Guid ComInterfaceGuid = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");
    private static readonly TimeSpan GameRecheck = TimeSpan.FromSeconds(10);
    // 指针离开托盘图标后先停留片刻：移到相邻图标时系统会紧接着发 NIN_POPUPOPEN，卡片原地换内容，不闪不淡。
    private const uint HoverLingerMs = 200;
    // 收起后隐藏保留的时间，期间再次悬停直接复用窗口与图形设备；超时才释放。
    private const uint HoverReleaseMs = 5_000;

    private enum HoverPhase { Visible, Leaving, Fading, Parked }
    private enum TrayAction { Panel, Menu }
    private static TrayApp? current;

    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private readonly TrayFiles files = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jige"));
    private readonly TrayModel model;
    private readonly SafetyGateStore gates;
    private readonly TrayEventTracker events = new(Desktop.Log);
    private readonly DiagnosticExport diagnostics;
    private readonly PollSchedule schedule = new(Environment.TickCount64);
    private readonly DesktopRefreshDebouncer desktopRefresh = new();
    private readonly DevicePoll poll;
    private readonly ConcurrentQueue<(PollResult? Result, DetectedDevice[] PushTargets)> results = new();
    private readonly ConcurrentQueue<PresenceUpdate[]> heartbeatResults = new();
    private readonly ConcurrentQueue<PresenceUpdate> pushResults = new();
    private readonly ConcurrentQueue<Action> updateCallbacks = new();
    private readonly HINSTANCE instance;
    private readonly uint taskbarCreated;
    private readonly TrayIconManager icons;
    private HWND host;
    private HDEVNOTIFY hidNotify, comNotify;
    private HWND foreground;
    private HWINEVENTHOOK foregroundHook, locationHook;
    private uint locationProcess, locationThread;
    private bool foregroundPosted, locationPosted;
    private PopupWindow? popup;
    private HoverCardWindow? hoverCard;
    private TrayMenuWindow? menu;
    private Point menuPoint;
    private uint? hoverIconId;
    private HoverPhase hoverPhase;
    private RECT hoverAnchor;
    private DateTimeOffset? hoverTextDue;
    private GameMode game;
    private bool welcomePending;
    private bool iconRetryArmed;
    private int iconFailures;
    private bool hasHeartbeatDevices;
    private bool bluetoothRefreshPending;
    private bool hadAsleepDevices;
    private DetectedDevice[] currentPushTargets = [];
    private CancellationTokenSource? pushCancellation;
    private Task pushTask = Task.CompletedTask;
    private uint? clickedIconId;
    private DateTimeOffset? nextWelcomeAttempt;
    private DateTimeOffset? welcomeExpires;
    private readonly CancellationTokenSource diagnosticCancellation = new();
    private DateTimeOffset? nextPopupAttempt;
    private DateTimeOffset? textDue;
    private long lastTraySelect = long.MinValue / 2;
    private long lostFocusByTray = long.MinValue / 2;
    private bool darkTaskbar;
    private UpdateViewState updateState;
    private CancellationTokenSource? updateCancellation;
    private Task updateWork = Task.CompletedTask;
    private int updateOperationId;
    private bool MenuOpen => menu is not null;
    private bool UpdateActive => updateState.IsBusy;
    private int cleanupAttempts;
    private bool exitRequested;
    private string? settingsError;
    private readonly bool configLoaded;
    private bool startedInGame;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? heartbeatCancellation;
    private Task cancelWork = Task.CompletedTask;
    private Task stateWrite = Task.CompletedTask;

    private unsafe TrayApp()
    {
        instance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR));
        taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        configLoaded = files.TryLoadSettings(out var settings, Desktop.Log);
        welcomePending = settings.WelcomeShown == false;
        diagnostics = new(files.DiagnosticsPath);
        var state = files.LoadState(ref settings, Desktop.Log);
        model = new(settings, state);
        gates = new(model.InitialBlockedReceivers);
        gates.Rejected += events.LogSafetyGateRejection;
        poll = new(gates, model.SavedLogitechSlots());
        darkTaskbar = Desktop.DarkTaskbar;
        updateState = CreateInitialUpdateState();

        Register(HostClass, &HostCallback);
        Register(PopupClass, &PopupCallback);
        Register(HoverCardClass, &HoverCardCallback);
        Register(MenuClass, &MenuCallback);
        Register(StripClass, &StripCallback);
        host = MakeWindow(HostClass, 0, WS_POPUP);
        icons = new(host, TrayMessage);
        RegisterDeviceNotifications();
    }

    public static int Run(bool afterSetup)
    {
        using var singleton = new Mutex(true, @"Local\Jige.Battery.Tray", out bool first);
        if (!first)
            return 0;
        if (!afterSetup && Mutex.TryOpenExisting(@"Local\Jige.Battery.Setup", out var setup))
        {
            setup.Dispose();
            return 0;
        }

        PInvoke.SetProcessDpiAwarenessContext(new DPI_AWARENESS_CONTEXT(-4)); // PerMonitorV2
        var hr = PInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED);
        if (hr.Failed)
            return 1;

        try
        {
            using var app = new TrayApp();
            current = app;
            return app.Loop();
        }
        catch (Exception error)
        {
            Desktop.Log("托盘启动失败", error);
            return 1;
        }
        finally
        {
            current = null;
            PInvoke.CoUninitialize();
        }
    }

    private unsafe int Loop()
    {
        CleanupUpdates();
        if (configLoaded || !File.Exists(files.ConfigPath))
        {
            if (configLoaded || SaveSettings(model.Settings))
                UpdateAutostart(model.Settings.StartWithWindows);
        }
        else
            settingsError = "配置读取失败";
        foregroundHook = InstallHook(ForegroundEvent);
        SetForegroundTracking();
        ReadGameMode();
        RefreshStripGeometry();
        RefreshIcon();
        ArmTimer(GameSettleTimer, 750);
        ProcessWork();

        MSG message;
        int result;
        while ((result = PInvoke.GetMessage(&message, default, 0, 0).Value) > 0)
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
        return result < 0 ? 1 : (int)message.wParam.Value;
    }

    private unsafe void RegisterDeviceNotifications()
    {
        hidNotify = RegisterInterfaceNotification(HidInterfaceGuid);
        comNotify = RegisterInterfaceNotification(ComInterfaceGuid);
    }

    private unsafe HDEVNOTIFY RegisterInterfaceNotification(Guid classGuid)
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE_W
        {
            dbcc_size = (uint)sizeof(DEV_BROADCAST_DEVICEINTERFACE_W),
            dbcc_devicetype = 5, // DBT_DEVTYP_DEVICEINTERFACE
            dbcc_classguid = classGuid
        };
        return PInvoke.RegisterDeviceNotification((HANDLE)host.Value, &filter,
            REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    private unsafe void Register(string name, delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> procedure)
    {
        fixed (char* namePointer = name)
        {
            var data = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                hInstance = instance,
                lpszClassName = new(namePointer),
                lpfnWndProc = procedure,
                hCursor = PInvoke.LoadCursor(default(HINSTANCE), new PCWSTR((char*)32512))
            };
            if (PInvoke.RegisterClassEx(&data) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private unsafe HWND MakeWindow(string name, WINDOW_EX_STYLE extras, WINDOW_STYLE style, HWND owner = default)
    {
        fixed (char* className = name)
        fixed (char* title = "几格")
        {
            var window = PInvoke.CreateWindowEx(extras, new PCWSTR(className), new PCWSTR(title), style,
                0, 0, 0, 0, owner, default, instance, null);
            return !window.IsNull ? window : throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static unsafe HWINEVENTHOOK InstallHook(uint eventId, uint process = 0, uint thread = 0) =>
        PInvoke.SetWinEventHook(eventId, eventId, default(HMODULE), &WinEvent, process, thread, 2); // 跳过本进程，回调在消息线程派发。

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void WinEvent(HWINEVENTHOOK hook, uint kind, HWND window, int objectId, int childId,
        uint thread, uint time)
    {
        if (current is not { } app || app.host.IsNull)
            return;
        if (kind == ForegroundEvent && !app.foregroundPosted)
            app.foregroundPosted = PInvoke.PostMessage(app.host, ForegroundMessage, default, default);
        if (kind == LocationEvent && objectId == 0 && childId == 0 && window == app.foreground && !app.locationPosted)
            app.locationPosted = PInvoke.PostMessage(app.host, MoveMessage, default, default);
    }

    private unsafe void SetForegroundTracking()
    {
        foreground = PInvoke.GetForegroundWindow();
        uint process = 0, thread = 0;
        // 系统报告 BUSY 时，即使关闭「游戏中不打扰」，也需要跟踪同一前台窗口进出全屏。
        if (Desktop.Classify(foreground, out _) == ForegroundKind.Other)
            thread = PInvoke.GetWindowThreadProcessId(foreground, &process);
        if (locationProcess == process && locationThread == thread)
            return;
        if (!locationHook.IsNull)
            PInvoke.UnhookWinEvent(locationHook);
        locationProcess = process;
        locationThread = thread;
        locationHook = process == 0 ? default : InstallHook(LocationEvent, process, thread);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HostCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (current is { } app && app.host == window && app.HandleMessage(message, wParam, lParam))
                return default;
        }
        catch (Exception error)
        {
            Desktop.Log("处理托盘消息失败", error);
            current?.ArmTimer(WorkTimer, 5_000);
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT PopupCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case 0x0083: return default; // WM_NCCALCSIZE，仅保留用于 DWM 的原生框架样式。
                case 0x0084: return (LRESULT)1; // HTCLIENT
                case 0x0014: return (LRESULT)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT，图像已由独立合成树提供。
                    PAINTSTRUCT paint;
                    PInvoke.BeginPaint(window, &paint);
                    PInvoke.EndPaint(window, &paint);
                    return default;
            }
            if (current is { popup: { } active } app && active.Handle == window)
            {
                if (message == 0x0021 && active.IsNotice)
                    return (LRESULT)3; // MA_NOACTIVATE
                if (message == 0x0020 && (ushort)lParam.Value == 1 && active.UpdateCursor())
                    return (LRESULT)1; // WM_SETCURSOR / HTCLIENT
                if (message == 0x0133 && active.IsEditor((HWND)lParam.Value)) // WM_CTLCOLOREDIT
                    return active.EditColors((HDC)wParam.Value);
                if (app.HandlePopup(active, message, wParam, lParam))
                    return default;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("处理面板消息失败", error);
            current?.DestroyPopup();
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT HoverCardCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case 0x0083: return default; // WM_NCCALCSIZE，保留 DWM 原生圆角与阴影。
                case 0x0084: return new LRESULT(-1); // HTTRANSPARENT，鼠标点击穿透。
                case 0x0021: return (LRESULT)3; // MA_NOACTIVATE，永不抢焦点。
                case 0x0014: return (LRESULT)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT，图像由独立合成树提供。
                    PAINTSTRUCT paint;
                    PInvoke.BeginPaint(window, &paint);
                    PInvoke.EndPaint(window, &paint);
                    return default;
                case 0x02E0: // WM_DPICHANGED
                    if (current is { } app && !app.host.IsNull)
                        PInvoke.PostMessage(app.host, 0x02E0, default, default);
                    return default;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("处理悬停卡片消息失败", error);
            current?.DestroyHoverCard();
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT MenuCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case 0x0083: return default; // WM_NCCALCSIZE，保留 DWM 原生圆角与阴影。
                case 0x0084: return (LRESULT)1; // HTCLIENT
                case 0x0014: return (LRESULT)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT，图像由独立合成树提供。
                    PAINTSTRUCT paint;
                    PInvoke.BeginPaint(window, &paint);
                    PInvoke.EndPaint(window, &paint);
                    return default;
            }
            if (current is { menu: { } active } app && active.Handle == window)
            {
                if (message == 0x0020 && (ushort)lParam.Value == 1 && active.UpdateCursor())
                    return (LRESULT)1; // WM_SETCURSOR / HTCLIENT
                if (app.HandleMenu(active, message, wParam, lParam))
                    return default;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("处理托盘菜单消息失败", error);
            current?.DestroyMenu();
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private bool HandleMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == taskbarCreated && taskbarCreated != 0)
        {
            DestroyMenu();
            DestroyHoverCard();
            icons.Recover();
            StopIconRetry();
            RecoverStrip();
            RefreshDesktop();
            ProcessWork();
            return true;
        }

        switch (message)
        {
            case StripGeometryMessage:
                CompleteStripGeometry();
                return true;
            case StripAppBarMessage:
                HandleStripAppBar(wParam.Value);
                return true;
            case TrayMessage:
                HandleTray(wParam, lParam);
                return true;
            case ResultMessage:
                CompletePoll();
                return true;
            case HeartbeatMessage:
                CompleteHeartbeat();
                return true;
            case PushPresenceMessage:
                CompletePushPresence();
                return true;
            case DiagnosticMessage:
                CompleteDiagnostics();
                return true;
            case UpdateMessage:
                CompleteUpdateEvents();
                return true;
            case ForegroundMessage:
                foregroundPosted = false;
                SetForegroundTracking();
                RefreshStripGeometry();
                // 再验证一次，给 Explorer 和 DWM 留出更新全屏状态的时间。
                ArmTimer(GameSettleTimer, 750);
                ProcessWork();
                return true;
            case MoveMessage:
                locationPosted = false;
                if (popup?.IsNotice == true || model.Settings.DisplayLocation == TrayDisplayLocation.Taskbar)
                    ReadGameMode();
                ArmTimer(GameSettleTimer, 350);
                return true;
            case 0x0219: // WM_DEVICECHANGE（C1：设备插拔通知）
                if (wParam.Value is 0x8000 or 0x8004 or 0x0007)
                    ArmTimer(DeviceChangeTimer, 1_200);
                return true;
            case 0x0218: // WM_POWERBROADCAST（C2：睡眠/休眠恢复）
                if (wParam.Value is 0x0012 or 0x0007) // PBT_APMRESUMEAUTOMATIC / PBT_APMRESUMESUSPEND
                {
                    Desktop.Log("系统从睡眠恢复，立即重新枚举并查询");
                    poll.InvalidateTopology();
                    schedule.NotifyPowerResume();
                    RefreshStripGeometry();
                    ProcessWork();
                }
                return true;
            case 0x0113: // WM_TIMER
                PInvoke.KillTimer(host, wParam.Value);
                if (wParam.Value == UpdateCleanupTimer)
                {
                    CleanupUpdates();
                    return true;
                }
                if (wParam.Value == DeviceChangeTimer)
                {
                    poll.InvalidateTopology();
                    schedule.NotifyDeviceChange();
                    ProcessWork();
                    return true;
                }
                if (wParam.Value == DesktopRefreshTimer)
                {
                    FlushDesktopRefresh();
                    return true;
                }
                if (wParam.Value == IconRetryTimer)
                {
                    iconRetryArmed = false;
                    RefreshIcon();
                }
                if (wParam.Value == CloseTimer && popup?.Closing == true)
                    DestroyPopup();
                if (wParam.Value == HoverCloseTimer)
                {
                    AdvanceHoverCard();
                    return true;
                }
                if (wParam.Value == MenuCloseTimer)
                {
                    if (menu?.Closing == true)
                        DestroyMenu();
                    return true;
                }
                ProcessWork();
                return true;
            case 0x007E or 0x02E0: // WM_DISPLAYCHANGE / WM_DPICHANGED（D3：防抖且不中断改名）
                RefreshStripGeometry();
                ScheduleDesktopRefresh(resetPopup: false);
                return true;
            case 0x001A: // WM_SETTINGCHANGE（D3：仅响应与界面外观/工作区相关的系统设置，并加防抖合并重建）
                string? area = lParam.Value == 0 ? null : Marshal.PtrToStringUni(lParam.Value);
                if (DesktopRefreshDebouncer.IsRelevantSettingChange(wParam.Value, area))
                {
                    RefreshTaskbarTheme();
                    ScheduleDesktopRefresh(resetPopup: true);
                }
                return true;
            case 0x031A or 0x031E or 0x0320:
                // 主题、DWM 合成和强调色变化（D3：连续变化防抖合并为一次重建）。
                RefreshTaskbarTheme();
                ScheduleDesktopRefresh(resetPopup: true);
                return true;
            case 0x001E: // WM_TIMECHANGE：调度使用单调时钟，仅刷新展示时间与提醒检查。
                RenderPopup();
                RenderHoverCard();
                ProcessWork();
                return true;
            case 0x0002: // WM_DESTROY
                PInvoke.PostQuitMessage(0);
                return true;
            case 0x0010: // WM_CLOSE，安装器可请求托盘正常退出。
                exitRequested = true;
                DestroyMenu();
                DestroyHoverCard();
                DestroyPopup();
                updateCancellation?.Cancel();
                PInvoke.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    private void ScheduleDesktopRefresh(bool resetPopup)
    {
        desktopRefresh.Request(Environment.TickCount64, resetPopup);
        ArmTimer(DesktopRefreshTimer, DesktopRefreshDebouncer.DebounceMs);
    }

    private void FlushDesktopRefresh()
    {
        if (!desktopRefresh.TryConsume(Environment.TickCount64, popup?.Editing == true, out bool resetPopup))
            return;
        RefreshDesktop(resetPopup);
    }

    private void RefreshDesktop(bool resetPopup = false)
    {
        ReadGameMode();
        darkTaskbar = Desktop.DarkTaskbar;
        RefreshStripGeometry();
        RefreshIcon();
        if (popup is { } active)
        {
            // D3（#22）：正在行内改名时不强制提交未完成的别名，延后到改名结束后再重建外观。
            if (active.Editing)
            {
                ScheduleDesktopRefresh(resetPopup);
                return;
            }
            active.SetAnchor(Anchor(null));
            RenderPopup(reset: resetPopup);
        }
        if (menu is { Closing: false } activeMenu)
        {
            try
            {
                activeMenu.Refresh(Anchor(menuPoint is { X: -1, Y: -1 } ? null : menuPoint), reset: resetPopup);
            }
            catch (Exception error)
            {
                DestroyMenu();
                Desktop.Log("刷新托盘菜单失败", error);
            }
        }
        RenderHoverCard(reset: resetPopup);
        ScheduleWork(DateTimeOffset.UtcNow, Environment.TickCount64);
    }

    private RECT Anchor(Point? preferred)
    {
        if (openedFromStrip && stripWindow?.VisibleBounds is { } strip)
            return ToRect(strip);
        if (openedFromStrip && preferred is not null)
            return Desktop.TaskbarAnchor(preferred);
        return icons.TryGetRect(clickedIconId, out var bounds) ? bounds : Desktop.TaskbarAnchor(preferred);
    }

    private void RefreshIcon()
    {
        RefreshStrip();
        if (iconRetryArmed && !icons.AnyRegistered)
            return;
        // H1：不设 NIF_SHOWTIP，szTip 始终保留供读屏软件使用。
        icons.Sync(model, darkTaskbar, suppressTooltip: false, defaultOnly: stripDisplay.DefaultIconOnly);
        if (hoverIconId is uint hovered && !icons.TryGetSlotKey(hovered, out _))
            DestroyHoverCard();
        if (icons.AllRegistered)
            StopIconRetry();
        else
        {
            if (iconFailures == 0)
                Desktop.Log("托盘图标注册失败，将重试");
            uint delay = iconFailures switch { 0 => 2_000u, 1 => 5_000u, 2 => 10_000u, _ => 60_000u };
            iconFailures = Math.Min(3, iconFailures + 1);
            iconRetryArmed = true;
            ArmTimer(IconRetryTimer, delay);
        }
    }

    private void StopIconRetry()
    {
        if (iconRetryArmed)
            PInvoke.KillTimer(host, IconRetryTimer);
        iconRetryArmed = false;
        iconFailures = 0;
    }

    private void ReadGameMode()
    {
        HWND currentForeground = PInvoke.GetForegroundWindow();
        var next = Desktop.GameMode(currentForeground, model.Settings);
        bool changed = next.BlockPopups != game.BlockPopups;
        bool enteredGame = next.Game && !game.Game;
        game = next;
        events.LogGameMode(game);
        if (enteredGame && schedule.Running && !startedInGame && !schedule.StartedByUser)
            cancelWork = cancellation?.CancelAsync() ?? Task.CompletedTask;

        // 进入游戏只收起自动提示和悬停卡片，用户自己打开的面板和菜单交给正常焦点处理。
        if (game.BlockPopups)
        {
            if (hoverCard is not null)
                DestroyHoverCard();
            if (popup is { IsNotice: true })
            {
                Desktop.Log("游戏中，收起自动提示");
                DestroyPopup();
            }
        }
        bool hideStrip = model.Settings.DisplayLocation == TrayDisplayLocation.Taskbar &&
            (game.Game || !model.Settings.GameSilent && Desktop.IsFullscreen(currentForeground));
        if (changed || stripFullscreen != hideStrip)
        {
            stripFullscreen = hideStrip;
            RefreshIcon();
        }
    }

    private void ProcessWork()
    {
        ReadGameMode();
        var now = DateTimeOffset.UtcNow;
        long nowTick = Environment.TickCount64;
        bool expired = model.ExpireReadings(now);
        // R2：连续休眠超过 10 分钟仍无回复的设备转为离线显示。
        bool sleepTimedOut = model.AdvanceSleepTimeouts(now, events.LogDeviceLinkChange);
        if (expired || sleepTimedOut)
        {
            if (sleepTimedOut)
                SaveState();
            RefreshIcon();
            RenderPopup();
            RenderHoverCard();
        }

        // R3：当设备首次进入休眠态时记录 GetLastInputInfo 基线，之后仅在有新键鼠输入时给休眠设备补发心跳（最多每 2 秒一次）。
        bool hasAsleep = model.HasAsleepDevices && (hasHeartbeatDevices || model.HasAsleepLogitechOrBluetoothDevices);
        if (hasAsleep && !hadAsleepDevices)
            schedule.SyncLastInputTick(Desktop.LastInputTick());
        hadAsleepDevices = hasAsleep;

        if (schedule.Due(model.Settings, nowTick, game.Game) <= nowTick)
            StartPoll(nowTick);
        else if (schedule.HeartbeatDue(nowTick, game.Game, hasHeartbeatDevices) <= nowTick)
            StartHeartbeat(nowTick);
        else if (hasAsleep && schedule.ShouldWakeHeartbeatOnInput(Desktop.LastInputTick(), nowTick, true, game.Game))
            StartHeartbeat(nowTick, model.AsleepDeviceIds(), inputWake: true);

        events.FlushReadFailuresIfDue(nowTick);
        if (textDue <= now)
            RenderPopup();
        if (hoverTextDue <= now)
            RenderHoverCard();

        if (welcomeExpires <= now || popup?.NoticeId is { } id && !model.ReminderIsDue(id, now))
            DestroyPopup();

        if (!game.BlockPopups && !MenuOpen && popup is null)
        {
            if (welcomePending && icons.AnyRegistered && !(nextWelcomeAttempt > now))
                ShowWelcome();
            else if (!schedule.Running && !(nextPopupAttempt > now) &&
                model.ReadyReminder(now, false) is { } ready)
                ShowPopup(ready.Device.Id, null);
        }
        ScheduleWork(now, nowTick);
    }

    private void ScheduleWork(DateTimeOffset now, long nowTick)
    {
        long? dueMs = null;
        if (schedule.Due(model.Settings, nowTick, game.Game) is long pollTick)
            dueMs = Math.Max(0, pollTick - nowTick);
        if (schedule.HeartbeatDue(nowTick, game.Game, hasHeartbeatDevices) is long hbTick)
            dueMs = EarlierMs(dueMs, Math.Max(0, hbTick - nowTick));
        if ((hasHeartbeatDevices || model.HasAsleepLogitechOrBluetoothDevices) && model.HasAsleepDevices && !game.Game && !schedule.Running && !schedule.HeartbeatRunning)
            dueMs = EarlierMs(dueMs, PollSchedule.InputCheckIntervalMs);

        DateTimeOffset? wallDue = model.NextSleepTimeout(now);
        if (popup?.NoticeId is { } id)
            wallDue = Earlier(wallDue, model.ReminderExpires(id));
        if (popup is null && !MenuOpen)
        {
            if (!schedule.Running && model.NextReminder(now) is { } reminder)
            {
                // 有待办提醒时，兼顾没有伴随前台事件的系统状态变化。
                wallDue = Earlier(wallDue, game.BlockPopups
                    ? Earlier(reminder > now ? reminder : null, now + GameRecheck)
                    : nextPopupAttempt is { } retry && retry > reminder ? retry : reminder);
            }
            if (welcomePending && icons.AnyRegistered)
                wallDue = Earlier(wallDue, game.BlockPopups ? now + GameRecheck : nextWelcomeAttempt ?? now);
        }
        if (popup?.IsNotice == true)
            wallDue = Earlier(wallDue, now + GameRecheck);
        wallDue = Earlier(Earlier(Earlier(wallDue, textDue), hoverTextDue), welcomeExpires);
        if (wallDue is { } wallTime)
            dueMs = EarlierMs(dueMs, Math.Max(0, (long)Math.Ceiling((wallTime - now).TotalMilliseconds)));

        if (dueMs is { } ms)
            ArmTimer(WorkTimer, (uint)Math.Clamp(ms, 10, int.MaxValue));
        else
            PInvoke.KillTimer(host, WorkTimer);
    }

    private static DateTimeOffset? Earlier(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null || b is not null && b < a ? b : a;

    private static long? EarlierMs(long? a, long? b) =>
        a is null || b is not null && b < a ? b : a;

    private unsafe void ArmTimer(nuint id, uint milliseconds)
    {
        if (!host.IsNull)
            PInvoke.SetTimer(host, id, milliseconds, null);
    }

    private void StartPoll(long nowTick)
    {
        bool forceRescan = schedule.Start(nowTick, game.Game);
        startedInGame = game.Game;
        bool showBluetooth = model.Settings.ShowBluetoothDevices;
        // 完整轮询期间暂停罗技被动推送监听，避免同一接收器短/长句柄争抢。
        StopPushListeners();
        var stoppingPush = pushTask;
        var source = new CancellationTokenSource();
        cancellation = source;
        cancelWork = Task.CompletedTask;
        var token = source.Token;
        var destination = host;
        _ = Task.Run(async () =>
        {
            PollResult? result;
            DetectedDevice[] pushTargets = [];
            try
            {
                await stoppingPush.ConfigureAwait(false);
                (result, pushTargets) = await poll.RunAsync(forceRescan, showBluetooth, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result = null;
            }
            catch (Exception error)
            {
                Desktop.Log("查询设备失败", error);
                result = new([], true);
            }
            results.Enqueue((result, pushTargets));
            PInvoke.PostMessage(destination, ResultMessage, default, default);
        });
        RenderPopup();
    }

    private void CompletePoll()
    {
        if (!results.TryDequeue(out var item))
            return;
        var (result, pushTargets) = item;
        bool cancelled = cancellation?.IsCancellationRequested == true;
        if (cancellation is { } completed)
            _ = DisposeCancellation(completed, cancelWork);
        cancellation = null;
        schedule.Complete();
        if (cancelled && (!startedInGame || bluetoothRefreshPending))
            schedule.Request(userInitiated: bluetoothRefreshPending);
        bluetoothRefreshPending = false;

        if (result is not null && !cancelled)
        {
            long nowTick = Environment.TickCount64;
            hasHeartbeatDevices = result.HasHeartbeatDevices;
            currentPushTargets = pushTargets;
            events.LogReceiverChanges(result.ReceiverChanges);
            events.LogUnknownReceivers(result.Unknowns);
            foreach (var reading in result.Readings)
                events.RecordReadFailure(reading.Issue, nowTick);
            events.FlushReadFailuresIfDue(nowTick);

            bool shouldSave = model.ApplyPoll(result.Readings, result.ScanFailed, DateTimeOffset.UtcNow,
                result.UnknownReceivers, events.LogDeviceLinkChange, result.LiveTargets);
            if (shouldSave || gates.BlockedCount != model.InitialBlockedReceivers.Count)
                SaveState();
            RefreshIcon();
            StartPushListeners(pushTargets);
        }
        else if (currentPushTargets.Length > 0)
        {
            StartPushListeners(currentPushTargets);
        }
        RenderPopup();
        RenderHoverCard();
        ProcessWork();
    }

    private void StartHeartbeat(long nowTick, IReadOnlySet<string>? onlyDeviceIds = null, bool inputWake = false)
    {
        schedule.StartHeartbeat(nowTick, inputWake);
        bool pausedPush = inputWake && currentPushTargets.Length > 0;
        if (pausedPush)
            StopPushListeners();
        var stoppingPush = pausedPush ? pushTask : Task.CompletedTask;
        bool showBluetooth = model.Settings.ShowBluetoothDevices;
        var source = new CancellationTokenSource();
        heartbeatCancellation = source;
        var token = source.Token;
        var destination = host;
        _ = Task.Run(async () =>
        {
            PresenceUpdate[] updates;
            try
            {
                await stoppingPush.ConfigureAwait(false);
                updates = await poll.RunHeartbeatAsync(token, onlyDeviceIds, showBluetooth).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                updates = [];
            }
            catch (Exception error)
            {
                Desktop.Log("在线心跳检查失败", error);
                updates = [];
            }
            heartbeatResults.Enqueue(updates);
            PInvoke.PostMessage(destination, HeartbeatMessage, default, default);
        });
    }

    private void CompleteHeartbeat()
    {
        if (!heartbeatResults.TryDequeue(out var updates))
            return;
        heartbeatCancellation?.Dispose();
        heartbeatCancellation = null;
        schedule.CompleteHeartbeat();
        if (cancellation is null && pushCancellation is null && currentPushTargets.Length > 0)
            StartPushListeners(currentPushTargets);

        if (updates.Length > 0)
        {
            long nowTick = Environment.TickCount64;
            foreach (var update in updates)
                events.RecordReadFailure(update.Issue, nowTick);
            var (uiChanged, needsRefresh) = model.ApplyPresence(updates, DateTimeOffset.UtcNow, events.LogDeviceLinkChange);
            if (uiChanged)
            {
                if (updates.Any(update => update.Battery is not null))
                    SaveState();
                RefreshIcon();
                RenderPopup();
                RenderHoverCard();
            }
            if (needsRefresh)
                schedule.Request(userInitiated: false);
        }
        ProcessWork();
    }

    private void StartPushListeners(DetectedDevice[] targets)
    {
        StopPushListeners();
        if (targets.Length == 0 || exitRequested)
            return;
        var cts = new CancellationTokenSource();
        pushCancellation = cts;
        var token = cts.Token;
        var destination = host;
        pushTask = Task.WhenAll(targets.Select(target => Task.Run(async () =>
        {
            try
            {
                await poll.ListenPushAsync(target, update =>
                {
                    pushResults.Enqueue(update);
                    PInvoke.PostMessage(destination, PushPresenceMessage, default, default);
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (TransportException)
            {
                // 接收器拔出或暂时被占用，等待插拔通知或下轮轮询重建监听。
            }
            catch (Exception error)
            {
                Desktop.Log("罗技推送监听异常", error);
            }
        })));
    }

    private void StopPushListeners()
    {
        if (pushCancellation is not { } active)
            return;
        pushCancellation = null;
        _ = DisposeCancellation(active, active.CancelAsync());
    }

    private void CompletePushPresence()
    {
        List<PresenceUpdate> batch = [];
        while (pushResults.TryDequeue(out var update))
            batch.Add(update);
        if (batch.Count == 0)
            return;

        var (uiChanged, needsRefresh) = model.ApplyPresence(batch, DateTimeOffset.UtcNow, events.LogDeviceLinkChange);
        if (uiChanged)
        {
            if (batch.Any(update => update.Battery is not null))
                SaveState();
            RefreshIcon();
            RenderPopup();
            RenderHoverCard();
        }
        if (needsRefresh)
        {
            schedule.Request(userInitiated: false);
            ProcessWork();
        }
    }

    private static async Task DisposeCancellation(CancellationTokenSource source, Task cancelling)
    {
        try
        {
            await cancelling.ConfigureAwait(false);
        }
        finally
        {
            source.Dispose();
        }
    }

    private void SaveState()
    {
        var snapshot = model.Snapshot(gates.Snapshot());
        var previous = stateWrite;
        stateWrite = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            try
            {
                files.SaveState(snapshot);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Desktop.Log("保存电量记录失败", error);
            }
        });
    }

    private bool SaveSettings(TraySettings settings)
    {
        try
        {
            files.SaveSettings(settings);
            settingsError = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            settingsError = "设置保存失败";
            Desktop.Log("保存设置失败", error);
            return false;
        }
    }

    private bool UpdateAutostart(bool enabled)
    {
        try
        {
            Desktop.SetAutostart(enabled);
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            settingsError = "设置保存失败";
            Desktop.Log("设置开机自启失败", error);
            return false;
        }
    }

    private void ChangeSettings(TraySettings settings) => TryChangeSettings(settings);

    private bool TryChangeSettings(TraySettings settings, string? remove = null)
    {
        if (settings == model.Settings && remove is null)
            return false;
        settings = settings.Normalize();
        bool autostartChanged = settings.StartWithWindows != model.Settings.StartWithWindows;
        if (autostartChanged && !UpdateAutostart(settings.StartWithWindows))
        {
            RenderPopup();
            return false;
        }
        if (!SaveSettings(settings))
        {
            if (autostartChanged)
                UpdateAutostart(model.Settings.StartWithWindows);
            RenderPopup();
            return false;
        }

        bool gameSettingChanged = settings.GameSilent != model.Settings.GameSilent;
        bool bluetoothChanged = settings.ShowBluetoothDevices != model.Settings.ShowBluetoothDevices;
        bool displayLocationChanged = settings.DisplayLocation != model.Settings.DisplayLocation;
        bool themeChanged = settings.Theme != model.Settings.Theme;
        model.ApplySettings(settings, DateTimeOffset.UtcNow);
        if (remove is not null)
            model.RemoveOffline(remove);
        if (gameSettingChanged)
            SetForegroundTracking();
        if (displayLocationChanged)
            RefreshStripGeometry();
        if (bluetoothChanged)
        {
            if (cancellation is { } activePoll)
            {
                bluetoothRefreshPending = true;
                if (!activePoll.IsCancellationRequested)
                    cancelWork = activePoll.CancelAsync();
            }
            schedule.Request(userInitiated: true);
        }
        SaveState();
        RefreshIcon();
        RenderPopup(reset: themeChanged);
        RenderHoverCard(reset: themeChanged);
        ProcessWork();
        return true;
    }

    private void HandleTray(WPARAM wParam, LPARAM lParam)
    {
        uint kind = (ushort)lParam.Value;
        uint iconId = (ushort)(lParam.Value >> 16);
        var point = PopupWindow.MessagePoint(unchecked((nint)wParam.Value));
        switch (kind)
        {
            case 0x0406: // NIN_POPUPOPEN（H1）：用户悬停托盘图标，显示自绘悬停卡片。
                ShowHoverCard(iconId, point);
                return;
            case 0x0407: // NIN_POPUPCLOSE（H1）：鼠标离开托盘图标，收起悬停卡片。
                CloseHoverCard(iconId);
                return;
        }
        if (!icons.TryGetById(iconId, out _))
            return;
        clickedIconId = iconId;
        openedFromStrip = false;
        switch (kind)
        {
            // NOTIFYICON_VERSION_4 下一次左键单击会先后收到 WM_LBUTTONUP 和 NIN_SELECT。
            // 两者都处理会让面板刚打开就被第二条消息切换关闭（0.1.0 的"点开即闪退"），因此只认 NIN_SELECT / NIN_KEYSELECT。
            case 0x0400 or 0x0401: // NIN_SELECT、NIN_KEYSELECT
                DestroyHoverCard();
                long time = Environment.TickCount64;
                if (time - lastTraySelect < 300 || time - lostFocusByTray < 300)
                    return;
                lastTraySelect = time;
                RequestTrayAction(TrayAction.Panel, point);
                break;
            case 0x007B: // WM_CONTEXTMENU
                DestroyHoverCard();
                RequestTrayAction(TrayAction.Menu, point);
                break;
        }
    }

    private void ShowHoverCard(uint iconId, Point point)
    {
        if (!icons.TryGetSlotKey(iconId, out var slotKey) || slotKey is null)
            return;
        var anchor = icons.TryGetRect(iconId, out var rect)
            ? rect
            : point is { X: -1, Y: -1 } ? Desktop.TaskbarAnchor() : RECT.FromXYWH(point.X, point.Y, 1, 1);
        ShowHoverCard(iconId, slotKey, anchor);
    }

    private void ShowHoverCard(uint? iconId, string? slotKey, RECT anchor)
    {
        ReadGameMode();
        bool fromStrip = iconId is null;
        if ((fromStrip && stripWindow?.VisibleBounds is null) ||
            !HoverCardPolicy.CanShow(game, popup is not null, MenuOpen))
        {
            DestroyHoverCard();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var content = HoverCardBuilder.Build(model, slotKey, now, forceList: fromStrip);
        try
        {
            PInvoke.KillTimer(host, HoverCloseTimer);
            hoverIconId = iconId;
            hoverAnchor = anchor;
            if (hoverCard is null)
            {
                var extra = WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP;
                HWND window = MakeWindow(HoverCardClass, extra, WS_POPUP | WS_THICKFRAME);
                hoverCard = new HoverCardWindow(window, model);
            }
            // 已显示（含刚离开、正在淡出）时原地换内容；已隐藏时复用原窗口重新显示。
            hoverCard.Show(content, anchor);
            hoverPhase = HoverPhase.Visible;
            hoverTextDue = now.AddMinutes(1);
            ScheduleWork(now, Environment.TickCount64);
        }
        catch (Exception error)
        {
            DestroyHoverCard();
            Desktop.Log("显示悬停卡片失败", error);
        }
    }

    private void RenderHoverCard(bool reset = false)
    {
        if (hoverCard is not { } active)
            return;
        if (hoverPhase == HoverPhase.Parked)
        {
            // 隐藏保留中的卡片沿用旧主题与背板，外观变化后直接释放，下次悬停重新创建。
            if (reset)
                DestroyHoverCard();
            return;
        }
        // 直读条悬停时 hoverIconId 为空，不能再据此提前返回。
        if (hoverPhase == HoverPhase.Fading)
            return;
        string? slotKey = null;
        bool fromStrip = hoverIconId is null;
        var strip = stripWindow?.VisibleBounds;
        if (!HoverCardPolicy.CanShow(game, popup is not null, MenuOpen) ||
            (fromStrip ? strip is null : !icons.TryGetSlotKey(hoverIconId!.Value, out slotKey) || slotKey is null))
        {
            DestroyHoverCard();
            return;
        }

        var anchor = strip is { } bounds && fromStrip ? ToRect(bounds)
            : icons.TryGetRect(hoverIconId, out var rect) ? rect : hoverAnchor;
        hoverAnchor = anchor;
        var now = DateTimeOffset.UtcNow;
        var content = HoverCardBuilder.Build(model, slotKey, now, forceList: fromStrip);
        try
        {
            active.Update(content, anchor, reset);
            hoverTextDue = now.AddMinutes(1);
        }
        catch (Exception error)
        {
            DestroyHoverCard();
            Desktop.Log("刷新悬停卡片失败", error);
        }
    }

    private void CloseHoverCard(uint? iconId)
    {
        if (hoverCard is null || hoverIconId != iconId || hoverPhase != HoverPhase.Visible)
            return;
        hoverPhase = HoverPhase.Leaving;
        ArmTimer(HoverCloseTimer, HoverLingerMs);
    }

    /// <summary>悬停卡片收起的分步推进：停留 → （淡出）→ 隐藏保留 → 释放。</summary>
    private void AdvanceHoverCard()
    {
        if (!host.IsNull)
            PInvoke.KillTimer(host, HoverCloseTimer);
        if (hoverCard is not { } active)
            return;
        try
        {
            switch (hoverPhase)
            {
                case HoverPhase.Leaving:
                    if (active.BeginClose())
                    {
                        hoverPhase = HoverPhase.Fading;
                        ArmTimer(HoverCloseTimer, HoverCardWindow.FadeOutMs);
                        return;
                    }
                    ParkHoverCard(active);
                    return;
                case HoverPhase.Fading:
                    ParkHoverCard(active);
                    return;
                case HoverPhase.Parked:
                    DestroyHoverCard();
                    return;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("关闭悬停卡片失败", error);
            DestroyHoverCard();
        }
    }

    private void ParkHoverCard(HoverCardWindow active)
    {
        active.Hide();
        hoverPhase = HoverPhase.Parked;
        hoverIconId = null;
        hoverTextDue = null;
        ArmTimer(HoverCloseTimer, HoverReleaseMs);
    }

    private void DestroyHoverCard()
    {
        if (!host.IsNull)
            PInvoke.KillTimer(host, HoverCloseTimer);
        var previous = hoverCard;
        hoverCard = null;
        hoverIconId = null;
        hoverPhase = HoverPhase.Visible;
        hoverTextDue = null;
        previous?.Dispose();
    }

    private void RequestTrayAction(TrayAction action, Point point)
    {
        // 用户能点到托盘图标，说明并不在独占全屏游戏里；游戏判定只拦截几格主动弹出的提醒，不拦截用户的点击。
        ReadGameMode();
        Desktop.Log($"托盘{(action == TrayAction.Panel ? "单击" : "右键")} game={game.Game} block={game.BlockPopups}");
        ExecuteTrayAction(action, point);
        ScheduleWork(DateTimeOffset.UtcNow, Environment.TickCount64);
    }

    private void ExecuteTrayAction(TrayAction action, Point point)
    {
        if (action == TrayAction.Menu)
        {
            if (popup is { } active)
                CompleteEdit(active, save: true);
            DestroyPopup();
            DestroyMenu();
            ShowMenu(point);
        }
        else if (popup is { IsNotice: false })
        {
            DestroyMenu();
            ClosePopup();
        }
        else
        {
            DestroyMenu();
            DestroyPopup();
            ShowPopup(null, point);
        }
    }

    private void ShowWelcome()
    {
        if (!ShowPopup(null, null, PanelPage.Welcome))
        {
            nextWelcomeAttempt = DateTimeOffset.UtcNow.AddSeconds(60);
            return;
        }
        welcomePending = false;
        welcomeExpires = DateTimeOffset.UtcNow.AddSeconds(20);
        var settings = model.Settings with { WelcomeShown = true };
        SaveSettings(settings);
        model.ApplySettings(settings, DateTimeOffset.UtcNow);
    }

    private bool ShowPopup(string? noticeId, Point? point, PanelPage page = PanelPage.Main)
    {
        DestroyHoverCard();
        bool automatic = noticeId is not null || page == PanelPage.Welcome;
        try
        {
            var extra = WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP;
            if (automatic)
                extra |= WS_EX_NOACTIVATE;
            var anchor = Anchor(point);
            HWND window = MakeWindow(PopupClass, extra, WS_POPUP | WS_THICKFRAME);
            popup = new(window, model, noticeId, anchor, page);
            RenderPopup();
            ReadGameMode();
            if (popup is not { } active)
                return false;
            active.Show();
            Desktop.Log(page == PanelPage.Welcome ? "显示首次使用引导" : automatic ? "显示低电量提醒" : "显示面板");
            return true;
        }
        catch (Exception error)
        {
            DestroyPopup();
            Desktop.Log("显示提示或面板失败", error);
            if (noticeId is not null)
                nextPopupAttempt = DateTimeOffset.UtcNow.AddSeconds(60);
            return false;
        }
    }

    private void RenderPopup(bool reset = false)
    {
        if (popup is not { Closing: false } active)
            return;
        var now = DateTimeOffset.UtcNow;
        if (active.NoticeId is { } id && !model.ReminderIsDue(id, now))
        {
            DestroyPopup();
            return;
        }
        try
        {
            active.Render(now, schedule.Running, settingsError, diagnostics.Status, diagnostics.PreviewPending, updateState, reset);
            textDue = active.IsNotice ? null : now.AddMinutes(1);
        }
        catch (Exception error)
        {
            DestroyPopup();
            Desktop.Log("刷新面板失败", error);
            if (active.IsNotice)
                nextPopupAttempt = now.AddSeconds(60);
        }
    }

    private bool HandlePopup(PopupWindow active, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (active.Closing)
            return true;
        switch (message)
        {
            case 0x0006 when (ushort)wParam.Value == 0 && !active.IsNotice: // WM_ACTIVATE
            case 0x0008 when !active.IsNotice: // WM_KILLFOCUS
                if (MenuOpen || (message == 0x0008 && active.IsEditor((HWND)wParam.Value)))
                    return true;
                var nextWindow = message == 0x0006 ? (HWND)lParam.Value : (HWND)wParam.Value;
                var kind = Desktop.Classify(nextWindow.IsNull ? PInvoke.GetForegroundWindow() : nextWindow, out _);
                string category = TrayLog.Foreground(kind);
                if (kind == ForegroundKind.System && active.TryReclaimFocus())
                {
                    Desktop.Log($"面板刚显示即失焦（{category}），重新激活");
                    return true;
                }
                Desktop.Log($"面板失焦关闭（msg=0x{message:X4}，前台 {category}）");
                if (PInvoke.GetCursorPos(out var cursor) && (icons.ContainsCursor(cursor) || stripWindow?.Contains(cursor) == true))
                    lostFocusByTray = Environment.TickCount64;
                ClosePopup();
                return true;
            case 0x0010: // WM_CLOSE
                PopupCommand(active, PopupAction.Close);
                return true;
            case 0x8010: // 托盘刚收回焦点时，确认前台仍是系统窗口才重新激活。
                var foregroundNow = PInvoke.GetForegroundWindow();
                if (Desktop.Classify(foregroundNow, out _) == ForegroundKind.System)
                    PInvoke.SetForegroundWindow(active.Handle);
                else if (foregroundNow != active.Handle)
                    ClosePopup();
                return true;
            case 0x0200: active.PointerMove(lParam); return true;
            case 0x02A3: active.PointerLeave(); return true;
            case 0x0201 or 0x0202:
                if (message == 0x0201)
                    CompleteEdit(active, save: true);
                if (popup == active)
                    PopupCommand(active, active.PointerButton(lParam, message == 0x0201));
                return true;
            case 0x020A:
                CompleteEdit(active, save: true);
                if (popup == active)
                    active.Wheel(unchecked((short)(wParam.Value >> 16)));
                return true;
            case InlineEdit.CompletedMessage:
                if (active.IsEditor((HWND)lParam.Value))
                    CompleteEdit(active, save: wParam.Value != 0);
                return true;
            case 0x0113 when wParam.Value == PopupWindow.AnimationTimer: // WM_TIMER
                active.Animate();
                return true;
            case 0x0100: PopupCommand(active, active.Key(wParam.Value)); return true;
            case 0x02E0: // WM_DPICHANGED，延后到当前移动消息完成。
                PInvoke.PostMessage(host, 0x02E0, default, default);
                return true;
            default:
                return false;
        }
    }

    private void PopupCommand(PopupWindow active, PopupTarget target)
    {
        var action = target.Action;
        if (action == PopupAction.None)
            return;
        if (active.NoticeId is { } id)
        {
            if (action is PopupAction.Dismiss or PopupAction.Later)
            {
                model.SetReminder(id, action == PopupAction.Later ? DateTimeOffset.UtcNow.AddHours(1) : null);
                nextPopupAttempt = null;
                SaveState();
            }
            else
                nextPopupAttempt = DateTimeOffset.UtcNow.AddSeconds(60);
            DestroyPopup();
            ProcessWork();
            return;
        }

        if (active.IsWelcome)
        {
            DestroyPopup();
            if (action == PopupAction.TaskbarSettings)
                OpenTaskbarSettings();
            ArmTimer(WorkTimer, 50);
            return;
        }

        switch (action)
        {
            case PopupAction.Close:
                ClosePopup();
                break;
            case PopupAction.Refresh:
                active.Spin();
                schedule.Request(userInitiated: true);
                ProcessWork();
                break;
            case PopupAction.Settings:
                active.Navigate(PanelPage.Settings);
                break;
            case PopupAction.Manage:
                active.Navigate(PanelPage.Devices);
                break;
            case PopupAction.Back:
                active.Navigate(active.CurrentPage == PanelPage.Updates ? active.UpdatesReturnPage : PanelPage.Main);
                break;
            case PopupAction.ToggleOffline:
                active.ToggleOffline();
                break;
            case PopupAction.ExpandSetting:
                active.ToggleSetting((SettingRow)target.Value);
                break;
            case PopupAction.TrayMode or PopupAction.Threshold or PopupAction.PollInterval or PopupAction.DisplayLocation or PopupAction.Theme:
                active.SelectSetting(target, ChangeSettings);
                break;
            case PopupAction.GameSilent:
                ChangeSettings(model.Settings with { GameSilent = !model.Settings.GameSilent });
                break;
            case PopupAction.ShowBluetooth:
                ChangeSettings(model.Settings with { ShowBluetoothDevices = !model.Settings.ShowBluetoothDevices });
                break;
            case PopupAction.Autostart:
                ChangeSettings(model.Settings with { StartWithWindows = !model.Settings.StartWithWindows });
                break;
            case PopupAction.TrayToggle when target.DeviceId is { } toggleId:
                ChangeSettings(model.ToggleTrayDevice(toggleId));
                break;
            case PopupAction.Visibility when target.DeviceId is { } visibility:
                ChangeSettings(model.ToggleVisibility(visibility));
                break;
            case PopupAction.Rename:
                try
                {
                    active.StartEdit(target);
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException)
                {
                    settingsError = "无法打开改名输入框";
                    Desktop.Log("打开改名输入框失败", error);
                    RenderPopup();
                }
                break;
            case PopupAction.Remove when target.DeviceId is { } removed && model.CanRemove(removed):
                TryChangeSettings(model.Settings.WithoutDevice(removed), removed);
                break;
            case PopupAction.Redetect:
                RedetectDevices();
                break;
            case PopupAction.Diagnostics:
                ShowDiagnostics();
                break;
            case PopupAction.OpenDiagnostics:
                OpenDiagnostics();
                RenderPopup();
                break;
            case PopupAction.CopyAndFeedback:
                CopyAndOpenFeedback();
                break;
            case PopupAction.Updates:
                OpenUpdatesPage(active, point: null);
                break;
            case PopupAction.CheckUpdatesAgain:
                StartUpdateCheck();
                break;
            case PopupAction.InstallUpdate:
                StartUpdateInstall();
                break;
            case PopupAction.OpenReleasePage:
                OpenReleasePage();
                break;
            case PopupAction.CancelUpdate:
                CancelUpdate(active);
                break;
        }
    }

    private void RedetectDevices()
    {
        gates.Clear();
        events.ResetSafetyGateLog();
        poll.InvalidateTopology();
        SaveState();
        Desktop.Log("用户触发重新检测，已清除安全门拦截并刷新设备缓存");
        schedule.Request(userInitiated: true, rescan: true);
        ProcessWork();
    }

    private void CompleteEdit(PopupWindow active, bool save)
    {
        if (!active.Editing)
            return;
        if (active.FinishEdit(save, restoreFocus: true) is { } edit)
        {
            var settings = model.Settings.WithAlias(edit.Id, edit.Name);
            if (settings != model.Settings)
            {
                ChangeSettings(settings);
                if (desktopRefresh.HasPending)
                    FlushDesktopRefresh();
                return;
            }
        }
        if (desktopRefresh.HasPending)
            FlushDesktopRefresh();
        else
            RenderPopup();
    }

    private void ClosePopup()
    {
        if (popup is not { Closing: false } active)
            return;
        Desktop.Log("收起面板");
        CompleteEdit(active, save: true);
        if (popup != active)
            return;
        textDue = null;
        try
        {
            if (active.BeginClose())
            {
                ArmTimer(CloseTimer, 120);
                return;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("关闭面板失败", error);
        }
        DestroyPopup();
        // 等本次焦点切换或托盘事件结束后再显示待办提醒。
        ArmTimer(WorkTimer, 50);
    }

    private void DestroyPopup()
    {
        PInvoke.KillTimer(host, CloseTimer);
        var previous = popup;
        popup = null;
        textDue = null;
        welcomeExpires = null;
        previous?.Dispose();
        if (desktopRefresh.HasPending && !host.IsNull)
            ArmTimer(DesktopRefreshTimer, DesktopRefreshDebouncer.DebounceMs);
    }

    private void ShowMenu(Point point)
    {
        DestroyHoverCard();
        menuPoint = point;
        try
        {
            PInvoke.KillTimer(host, MenuCloseTimer);
            var extra = WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP;
            HWND window = MakeWindow(MenuClass, extra, WS_POPUP | WS_THICKFRAME);
            var anchor = Anchor(point is { X: -1, Y: -1 } ? null : point);
            menu = new TrayMenuWindow(window, model);
            menu.Show(anchor);
        }
        catch (Exception error)
        {
            DestroyMenu();
            Desktop.Log("显示托盘菜单失败", error);
        }
    }

    private bool HandleMenu(TrayMenuWindow active, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (active.Closing)
            return true;
        switch (message)
        {
            case 0x0006 when (ushort)wParam.Value == 0: // WM_ACTIVATE
            case 0x0008: // WM_KILLFOCUS
                var nextWindow = message == 0x0006 ? (HWND)lParam.Value : (HWND)wParam.Value;
                var kind = Desktop.Classify(nextWindow.IsNull ? PInvoke.GetForegroundWindow() : nextWindow, out _);
                if (kind == ForegroundKind.System && active.TryReclaimFocus())
                    return true;
                if (PInvoke.GetCursorPos(out var cursor) && (icons.ContainsCursor(cursor) || stripWindow?.Contains(cursor) == true))
                    lostFocusByTray = Environment.TickCount64;
                CloseMenu();
                return true;
            case 0x8010:
                var foregroundNow = PInvoke.GetForegroundWindow();
                if (Desktop.Classify(foregroundNow, out _) == ForegroundKind.System)
                    PInvoke.SetForegroundWindow(active.Handle);
                else if (foregroundNow != active.Handle)
                    CloseMenu();
                return true;
            case 0x0010: // WM_CLOSE
                CloseMenu();
                return true;
            case 0x0200: active.PointerMove(lParam); return true;
            case 0x02A3: active.PointerLeave(); return true;
            case 0x0201 or 0x0202:
                var command = active.PointerButton(lParam, message == 0x0201);
                if (command != TrayMenuCommand.None)
                    ExecuteMenuCommand(command);
                return true;
            case 0x0100: // WM_KEYDOWN
                var (keyCommand, dismiss) = active.Key(wParam.Value);
                if (dismiss)
                    CloseMenu();
                else if (keyCommand != TrayMenuCommand.None)
                    ExecuteMenuCommand(keyCommand);
                return true;
            case 0x02E0: // WM_DPICHANGED
                PInvoke.PostMessage(host, 0x02E0, default, default);
                return true;
            default:
                return false;
        }
    }

    private void ExecuteMenuCommand(TrayMenuCommand command)
    {
        var anchorPoint = menuPoint is { X: -1, Y: -1 } ? (Point?)null : menuPoint;
        DestroyMenu();
        if (exitRequested)
            return;
        switch (command)
        {
            case TrayMenuCommand.Refresh:
                schedule.Request(userInitiated: true);
                ProcessWork();
                break;
            case TrayMenuCommand.Settings:
                ShowPopup(null, anchorPoint, PanelPage.Settings);
                break;
            case TrayMenuCommand.Updates:
                OpenUpdatesPage(activePopup: null, anchorPoint);
                break;
            case TrayMenuCommand.Diagnostics:
                ShowDiagnostics();
                break;
            case TrayMenuCommand.Exit:
                PInvoke.PostQuitMessage(0);
                break;
        }
    }

    private void CloseMenu()
    {
        if (menu is not { Closing: false } active)
            return;
        try
        {
            if (active.BeginClose())
            {
                ArmTimer(MenuCloseTimer, TrayMenuWindow.FadeOutMs);
                return;
            }
        }
        catch (Exception error)
        {
            Desktop.Log("关闭托盘菜单失败", error);
        }
        DestroyMenu();
        ArmTimer(WorkTimer, 50);
    }

    private void DestroyMenu()
    {
        if (!host.IsNull)
            PInvoke.KillTimer(host, MenuCloseTimer);
        var previous = menu;
        menu = null;
        previous?.Dispose();
    }

    private static UpdateViewState CreateInitialUpdateState()
    {
        string display = ReleaseVersion.Display;
        bool isPrerelease = ReleaseVersion.IsPrerelease;
        string label = isPrerelease ? display : $"v{display}";
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        bool installed = InstallPaths.IsInstalledExecutable(
            Environment.ProcessPath, local, Desktop.ReadInstalledDirectory());
        return UpdateViewState.Idle(label, installed);
    }

    private void OpenUpdatesPage(PopupWindow? activePopup, Point? point)
    {
        DestroyHoverCard();
        bool shouldCheck = !UpdateActive &&
            !(updateState.Phase == UpdatePhase.Available &&
              updateState.CheckedAt is { } checkedAt &&
              DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromMinutes(10));
        if (shouldCheck)
            StartUpdateCheck();

        if (activePopup is not null)
            activePopup.Navigate(PanelPage.Updates);
        else
            ShowPopup(null, point, PanelPage.Updates);
        RenderPopup();
    }

    private void StartUpdateCheck()
    {
        CancelUpdateTask();
        int opId = ++updateOperationId;
        string current = ReleaseVersion.Current;
        string currentLabel = updateState.CurrentVersionLabel;
        bool installed = updateState.IsInstalled;
        if (!ReleaseVersion.TryParseInstalled(current, out var version, out _, out _))
        {
            events.LogUpdateCheck("失败（InvalidVersion）");
            updateState = UpdateViewState.Failed(currentLabel, installed, $"无法识别当前版本 {current}。");
            RenderPopup();
            return;
        }

        var source = new CancellationTokenSource();
        updateCancellation = source;
        var token = source.Token;
        updateState = UpdateViewState.Checking(currentLabel, installed);
        RenderPopup();

        updateWork = Task.Run(async () =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try
            {
                using var http = UpdateClient.CreateHttpClient();
                var client = new UpdateClient(http);
                var update = await client.CheckAsync(version, timeout.Token).ConfigureAwait(false);
                var checkedAt = DateTimeOffset.UtcNow;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    if (update is null)
                    {
                        events.LogUpdateCheck($"已是最新版本 {currentLabel}");
                        updateState = UpdateViewState.UpToDate(currentLabel, installed, checkedAt);
                    }
                    else
                    {
                        events.LogUpdateCheck($"发现新版本 v{update.Version}（当前 {currentLabel}，来源：{update.EffectiveSource.Name}）");
                        updateState = UpdateViewState.Found(currentLabel, installed, update, checkedAt);
                    }
                    RenderPopup();
                });
            }
            catch (OperationCanceledException)
            {
                bool timedOut = timeout.IsCancellationRequested && !token.IsCancellationRequested;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    if (timedOut)
                    {
                        events.LogUpdateCheck("失败（Timeout）");
                        updateState = UpdateViewState.Failed(currentLabel, installed, "请求超时，请检查网络后重试。");
                    }
                    else
                    {
                        events.LogUpdateCheck("已取消");
                        updateState = UpdateViewState.Idle(currentLabel, installed);
                    }
                    RenderPopup();
                });
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or
                HttpRequestException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                string message = UpdateViewState.ExplainError(error);
                string kind = error.GetType().Name;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    events.LogUpdateCheck($"失败（{kind}）");
                    updateState = UpdateViewState.Failed(currentLabel, installed, message);
                    RenderPopup();
                });
            }
        });
    }

    private void StartUpdateInstall()
    {
        if (UpdateActive || updateState.Available is not { } update || !updateState.IsInstalled)
            return;

        CancelUpdateTask();
        int opId = ++updateOperationId;
        var source = new CancellationTokenSource();
        updateCancellation = source;
        var token = source.Token;
        string currentLabel = updateState.CurrentVersionLabel;
        bool installed = updateState.IsInstalled;
        updateState = updateState.WithProgress(new(0, update.Installer.Size, Verifying: false));
        RenderPopup();

        long lastPostedTick = 0;
        var progress = new DirectProgress<UpdateProgress>(sample =>
        {
            long nowTick = Environment.TickCount64;
            if (!sample.Verifying && nowTick - lastPostedTick < 80 && sample.DownloadedBytes < sample.TotalBytes)
                return;
            lastPostedTick = nowTick;
            PostUpdateCallback(() =>
            {
                if (opId != updateOperationId || exitRequested || !UpdateActive)
                    return;
                updateState = updateState.WithProgress(sample);
                RenderPopup();
            });
        });

        updateWork = Task.Run(() =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                using var http = UpdateClient.CreateHttpClient();
                var client = new UpdateClient(http);
                bool launched = UpdateInstallation.Run(
                    files.Updates,
                    () => client.DownloadAsync(update, files.Updates, progress, timeout.Token).GetAwaiter().GetResult(),
                    () => exitRequested || token.IsCancellationRequested,
                    LaunchInstaller);
                if (launched)
                {
                    PostUpdateCallback(() =>
                    {
                        exitRequested = true;
                        DestroyMenu();
                        DestroyHoverCard();
                        DestroyPopup();
                        PInvoke.PostQuitMessage(0);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                bool timedOut = timeout.IsCancellationRequested && !token.IsCancellationRequested;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    if (timedOut)
                    {
                        events.LogUpdateCheck("失败（Timeout）");
                        updateState = UpdateViewState.Failed(currentLabel, installed, "下载超时，请检查网络后重试。", update);
                    }
                    else
                    {
                        events.LogUpdateCheck("已取消");
                        updateState = updateState.BackToAvailable();
                    }
                    RenderPopup();
                });
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or
                HttpRequestException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                string message = UpdateViewState.ExplainError(error);
                string kind = error.GetType().Name;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    events.LogUpdateCheck($"失败（{kind}）");
                    updateState = UpdateViewState.Failed(currentLabel, installed, message, update);
                    RenderPopup();
                });
            }
        });
    }

    private void OpenReleasePage()
    {
        if (updateState.Available is not { } update)
            return;
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(update.Page.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Desktop.Log("打开 Release 页面失败", error);
        }
    }

    private void CancelUpdate(PopupWindow activePopup)
    {
        bool wasChecking = updateState.Phase is UpdatePhase.Checking or UpdatePhase.Idle;
        CancelUpdateTask();
        events.LogUpdateCheck("已取消");
        if (wasChecking)
        {
            updateState = UpdateViewState.Idle(updateState.CurrentVersionLabel, updateState.IsInstalled);
            activePopup.Navigate(activePopup.UpdatesReturnPage);
        }
        else
        {
            updateState = updateState.BackToAvailable();
            RenderPopup();
        }
    }

    private void CancelUpdateTask()
    {
        updateOperationId++;
        if (updateCancellation is not { } active)
            return;
        updateCancellation = null;
        _ = DisposeCancellation(active, active.CancelAsync());
    }

    private void PostUpdateCallback(Action action)
    {
        updateCallbacks.Enqueue(action);
        var destination = host;
        if (!destination.IsNull)
            PInvoke.PostMessage(destination, UpdateMessage, default, default);
    }

    private void CompleteUpdateEvents()
    {
        while (updateCallbacks.TryDequeue(out var action))
            action();
    }

    private static void LaunchInstaller(string package)
    {
        var start = new ProcessStartInfo(package)
        {
            ArgumentList = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" },
            UseShellExecute = false
        };
        using var installer = Process.Start(start) ?? throw new InvalidOperationException("无法启动几格安装器。");
    }

    private sealed class DirectProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private void CleanupUpdates()
    {
        if (UpdateActive)
        {
            ArmTimer(UpdateCleanupTimer, 5_000);
            return;
        }

        try
        {
            files.Updates.Clear();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 静默安装结束时旧安装器可能仍占用自身文件，等安装器退出后再清理。
            if (++cleanupAttempts < 24)
                ArmTimer(UpdateCleanupTimer, 5_000);
            else
                Desktop.Log("清理更新目录失败", error);
        }
    }

    private void ShowDiagnostics()
    {
        var token = diagnosticCancellation.Token;
        var context = BuildDiagnosticContext();
        if (diagnostics.Start(() => GenerateDiagnosticsAsync(context, token)))
        {
            var destination = host;
            _ = diagnostics.Work!.ContinueWith(_ => PInvoke.PostMessage(destination, DiagnosticMessage, default, default),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        if (popup is null)
            ShowPopup(null, null, PanelPage.Devices);
        RenderPopup();
    }

    private DiagnosticContext BuildDiagnosticContext()
    {
        events.FlushReadFailuresIfDue(Environment.TickCount64, force: true);
        var settings = model.Settings;
        var summary = new DiagnosticSettingsSummary(
            Presentation.TrayModeLabel(settings.TrayMode),
            settings.LowBatteryThreshold,
            settings.PollSeconds,
            settings.GamePollSeconds,
            PollSchedule.HeartbeatSeconds,
            PollSchedule.GameHeartbeatSeconds,
            settings.GameSilent,
            settings.GameProcessNames.Length,
            settings.StartWithWindows,
            gates.BlockedCount,
            settings.ShowBluetoothDevices)
        {
            ActualDisplayLocation = stripDisplay.Actual,
            DisplayLocationReason = stripDisplay.Description
        };
        return new(
            DeviceHistories: model.DiagnosticHistories(),
            ProcessMetrics: Desktop.CollectProcessMetrics(startedAt),
            SettingsSummary: summary,
            Gates: gates,
            GameState: Desktop.GameMode(PInvoke.GetForegroundWindow(), settings).Status);
    }

    private async Task<bool> GenerateDiagnosticsAsync(DiagnosticContext context, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            string report = await DiagnosticReport
                .GenerateWindowsAsync(timeout.Token, context).ConfigureAwait(false);
            report += "\r\n# 请先完整检查报告，再自行决定是否复制、发送。几格不会上传。\r\n";
            string log = Desktop.RecentLog(80);
            if (log.Length > 0)
                report += "\r\n# 托盘日志（最多 80 行）\r\n" + log;
            await diagnostics.WriteAsync(report, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                Desktop.Log("诊断信息导出失败", error);
            return false;
        }
    }

    private void CompleteDiagnostics()
    {
        if (!diagnostics.Complete())
            return;
        // 先在主线程重新检查前台；游戏中只保留面板里的查看入口。
        ReadGameMode();
        if (!exitRequested && !game.BlockPopups && !MenuOpen && !UpdateActive &&
            diagnostics.Status == DiagnosticStatus.Ready)
            OpenDiagnostics();
        RenderPopup();
        ScheduleWork(DateTimeOffset.UtcNow, Environment.TickCount64);
    }

    private void OpenDiagnostics()
    {
        try
        {
            using var editor = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                ArgumentList = { files.DiagnosticsPath },
                UseShellExecute = false,
            });
            diagnostics.PreviewOpened();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            diagnostics.EditorFailed();
            Desktop.Log("打开诊断报告失败", error);
        }
    }

    /// <summary>A4 / G4：仅在用户点击时复制报告全文到剪贴板并打开无参数的设备报告 Issue 页面。</summary>
    private void CopyAndOpenFeedback()
    {
        try
        {
            if (!File.Exists(files.DiagnosticsPath))
            {
                diagnostics.FeedbackFailed();
                RenderPopup();
                return;
            }
            string report = File.ReadAllText(files.DiagnosticsPath);
            if (!Desktop.SetClipboardText(host, report))
            {
                diagnostics.FeedbackFailed();
                Desktop.Log("复制诊断报告到剪贴板失败");
                RenderPopup();
                return;
            }
            using var browser = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                DiagnosticExport.DeviceReportIssueUrl) { UseShellExecute = true });
            diagnostics.FeedbackCopied();
            Desktop.Log("已复制诊断报告并打开反馈页");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            diagnostics.FeedbackFailed();
            Desktop.Log("复制报告并打开反馈页失败", error);
        }
        RenderPopup();
    }

    private static void OpenTaskbarSettings()
    {
        try
        {
            using var settings = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:taskbar")
            {
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Desktop.Log("打开任务栏设置失败", error);
        }
    }

    public void Dispose()
    {
        StopPushListeners();
        if (!hidNotify.IsNull)
            PInvoke.UnregisterDeviceNotification(hidNotify);
        if (!comNotify.IsNull)
            PInvoke.UnregisterDeviceNotification(comNotify);
        if (!foregroundHook.IsNull)
            PInvoke.UnhookWinEvent(foregroundHook);
        if (!locationHook.IsNull)
            PInvoke.UnhookWinEvent(locationHook);
        DestroyMenu();
        DestroyHoverCard();
        DestroyPopup();
        DisposeStrip();
        icons.Dispose();
        var window = host;
        host = default;
        PInvoke.DestroyWindow(window);
        if (cancellation is { } active)
        {
            _ = DisposeCancellation(active, active.IsCancellationRequested ? cancelWork : active.CancelAsync());
            cancellation = null;
        }
        if (heartbeatCancellation is { } hb)
        {
            _ = DisposeCancellation(hb, hb.CancelAsync());
            heartbeatCancellation = null;
        }
        if (updateCancellation is { } updateCts)
        {
            updateCancellation = null;
            updateCts.Cancel();
            try { updateWork.GetAwaiter().GetResult(); }
            catch { }
            updateCts.Dispose();
        }
        diagnosticCancellation.Cancel();
        diagnostics.Work?.GetAwaiter().GetResult();
        diagnosticCancellation.Dispose();
        try { pushTask.GetAwaiter().GetResult(); }
        catch { }
        stateWrite.GetAwaiter().GetResult();
    }
}

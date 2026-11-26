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
    private const string BannerClass = "Jige.Tray.GameBanner";
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
        MenuCloseTimer = 9, AutoUpdateTimer = 10, StripRetryTimer = 11, BannerTimer = 12, StripBackdropTimer = 13,
        GameExitTimer = 14;
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
    private readonly GameModeStabilizer gameStabilizer = new();
    private readonly DesktopRefreshDebouncer desktopRefresh = new();
    private readonly DevicePoll poll;
    private readonly ConcurrentQueue<(PollResult? Result, DetectedDevice[] PushTargets, bool Targeted)> results = new();
    private readonly ConcurrentQueue<PresenceUpdate[]> heartbeatResults = new();
    private readonly ConcurrentQueue<PresenceUpdate> pushResults = new();
    private readonly ConcurrentQueue<Action> updateCallbacks = new();
    private readonly HINSTANCE instance;
    private readonly uint taskbarCreated;
    private readonly uint wakeMessage;
    private readonly TrayIconManager icons;
    private HWND host;
    private HDEVNOTIFY hidNotify, comNotify;
    private bool sessionNotifyRegistered;
    private HWND foreground;
    private HWINEVENTHOOK foregroundHook, locationHook;
    private uint locationProcess, locationThread;
    private bool foregroundPosted, locationPosted;
    private PopupWindow? popup;
    private HoverCardWindow? hoverCard;
    private GameBannerWindow? gameBanner;
    private TrayMenuWindow? menu;
    private Point? menuPoint;
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
    // 当前更新任务由后台自动检查发起时记录其编号；用户操作会换新编号，从而接管或取消后台任务。
    private int? backgroundUpdateId;
    private bool activeUpdateInstalls;
    private readonly BackgroundUpdateSchedule autoUpdate = new(Environment.TickCount64);
    private bool MenuOpen => menu is not null;
    private bool HoverCardVisible => hoverCard is not null && hoverPhase != HoverPhase.Parked;
    private bool ShouldShowWelcome =>
        welcomePending && model.Settings.DisplayLocation == TrayDisplayLocation.TrayIcons && icons.AnyRegistered;
    private bool UpdateActive => updateState.IsBusy;
    private int cleanupAttempts;
    private volatile bool exitRequested;
    private bool pendingExplicitDeviceChange;
    private bool pendingRedetect;
    private string? pendingInstallerPackage;
    private string? settingsError;
    private readonly bool configLoaded;
    private bool startedInGame;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? heartbeatCancellation;
    private Task pollTask = Task.CompletedTask;
    private Task heartbeatTask = Task.CompletedTask;
    private Task cancelWork = Task.CompletedTask;
    private Task heartbeatCancelWork = Task.CompletedTask;
    private readonly object stateWriteLock = new();
    private (TrayState State, long GateVersion)? pendingStateWrite;
    private bool stateWriteActive;
    private Task stateWrite = Task.CompletedTask;
    private long lastSavedGateVersion;

    private unsafe TrayApp()
    {
        instance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR));
        taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        wakeMessage = PInvoke.RegisterWindowMessage("Jige.Tray.Wake");
        configLoaded = files.TryLoadSettings(out var settings, Desktop.Log);
        welcomePending = settings.WelcomeShown == false;
        diagnostics = new(files.DiagnosticsPath);
        var state = files.LoadState(ref settings, Desktop.Log);
        model = new(settings, state);
        gates = new(model.InitialBlockedReceivers);
        lastSavedGateVersion = gates.Version;
        gates.Rejected += events.LogSafetyGateRejection;
        poll = new(gates, model.SavedLogitechSlots());
        InitializeTaskbarBackdrop();
        updateState = CreateInitialUpdateState();

        try
        {
            Register(HostClass, &HostCallback);
            Register(PopupClass, &PopupCallback);
            Register(HoverCardClass, &HoverCardCallback);
            Register(BannerClass, &BannerCallback);
            Register(MenuClass, &MenuCallback);
            Register(StripClass, &StripCallback);
            host = MakeWindow(HostClass, 0, WS_POPUP);
            icons = new(host, TrayMessage);
            RegisterDeviceNotifications();
            sessionNotifyRegistered = PInvoke.WTSRegisterSessionNotification(host, 0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static int Run(bool afterSetup)
    {
        if (!afterSetup)
        {
            try
            {
                if (Mutex.TryOpenExisting(@"Local\Jige.Battery.Setup", out var setup))
                {
                    setup.Dispose();
                    Desktop.Log("检测到安装或更新正在进行，本次启动已跳过");
                    return 0;
                }
            }
            catch (UnauthorizedAccessException)
            {
                Desktop.Log("检测到安装或更新互斥量（无权打开），本次启动已跳过");
                return 0;
            }
        }

        string? pendingInstaller = null;
        int exitCode;
        Mutex? singleton;
        bool first;
        try
        {
            singleton = new Mutex(true, @"Local\Jige.Battery.Tray", out first);
        }
        catch (UnauthorizedAccessException error)
        {
            Desktop.Log("无法打开单实例互斥量，跳过本次启动", error);
            if (!afterSetup)
                WakeExistingInstance();
            return 0;
        }

        using (singleton)
        {
            bool ownsMutex = first;
            if (!first)
            {
                if (!afterSetup)
                {
                    WakeExistingInstance();
                    return 0;
                }
                try
                {
                    ownsMutex = singleton.WaitOne(TimeSpan.FromSeconds(35));
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }
                if (!ownsMutex)
                {
                    Desktop.Log("更新后接管托盘失败：旧实例在 35 秒内未退出");
                    return 0;
                }
            }

            PInvoke.SetProcessDpiAwarenessContext(new DPI_AWARENESS_CONTEXT(-4)); // PerMonitorV2
            var hr = PInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED);
            if (hr.Failed)
                return 1;

            try
            {
                using var app = new TrayApp();
                current = app;
                exitCode = app.Loop();
                pendingInstaller = app.pendingInstallerPackage;
            }
            catch (Exception error)
            {
                Desktop.Log("托盘启动失败", error);
                exitCode = 1;
            }
            finally
            {
                current = null;
                PInvoke.CoUninitialize();
                if (ownsMutex)
                {
                    try { singleton.ReleaseMutex(); }
                    catch (ApplicationException) { }
                }
            }
        }

        // 先彻底释放并销毁单实例互斥量再拉起静默安装器，避免 Inno Setup 的 AppMutex 门与托盘退出产生竞态。
        if (pendingInstaller is not null)
            LaunchInstallerAfterShutdown(pendingInstaller);
        return exitCode;
    }

    private static unsafe void WakeExistingInstance()
    {
        uint wake = PInvoke.RegisterWindowMessage("Jige.Tray.Wake");
        if (wake == 0)
            return;
        var existing = PInvoke.FindWindow(HostClass, null);
        if (existing.IsNull)
            return;
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(existing, &pid);
        if (pid != 0)
            PInvoke.AllowSetForegroundWindow(pid);
        if (!PInvoke.PostMessage(existing, wake, 1u, default))
            Desktop.Log($"唤醒已有实例失败（Win32 {Marshal.GetLastPInvokeError()}）");
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
        RefreshStripGeometry();
        ReadGameMode();
        RefreshIcon();
        ArmTimer(GameSettleTimer, 750);
        ProcessWork();
        ScheduleAutoUpdate();

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
        try
        {
            if (current is not { } app || app.host.IsNull)
                return;
            if (kind == ForegroundEvent && !app.foregroundPosted)
                app.foregroundPosted = PInvoke.PostMessage(app.host, ForegroundMessage, default, default);
            if (kind == LocationEvent && objectId == 0 && childId == 0 && window == app.foreground && !app.locationPosted)
                app.locationPosted = PInvoke.PostMessage(app.host, MoveMessage, default, default);
        }
        catch
        {
            // 非托管回调绝不能向外抛出托管异常。
        }
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
            try
            {
                Desktop.Log("处理托盘消息失败", error);
                current?.ArmTimer(WorkTimer, 5_000);
            }
            catch { }
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
            try
            {
                Desktop.Log("处理面板消息失败", error);
                current?.DestroyPopup();
            }
            catch { }
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT HoverCardCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (HandlePassthroughOverlay(window, message, out var handled))
                return handled;
        }
        catch (Exception error)
        {
            try
            {
                Desktop.Log("处理悬停卡片消息失败", error);
                current?.DestroyHoverCard();
            }
            catch { }
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT BannerCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (HandlePassthroughOverlay(window, message, out var handled))
                return handled;
        }
        catch (Exception error)
        {
            try
            {
                Desktop.Log("处理进游戏横幅消息失败", error);
                current?.DestroyGameBanner();
            }
            catch { }
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private static unsafe bool HandlePassthroughOverlay(HWND window, uint message, out LRESULT result)
    {
        switch (message)
        {
            case 0x0083: result = default; return true; // WM_NCCALCSIZE，保留 DWM 原生圆角与阴影。
            case 0x0084: result = new LRESULT(-1); return true; // HTTRANSPARENT，鼠标点击穿透。
            case 0x0021: result = (LRESULT)3; return true; // MA_NOACTIVATE，永不抢焦点。
            case 0x0014: result = (LRESULT)1; return true; // WM_ERASEBKGND
            case 0x000F: // WM_PAINT，图像由独立合成树提供。
                PAINTSTRUCT paint;
                PInvoke.BeginPaint(window, &paint);
                PInvoke.EndPaint(window, &paint);
                result = default;
                return true;
            case 0x02E0: // WM_DPICHANGED
                if (current is { } app && !app.host.IsNull)
                    PInvoke.PostMessage(app.host, 0x02E0, default, default);
                result = default;
                return true;
            default:
                result = default;
                return false;
        }
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
            try
            {
                Desktop.Log("处理托盘菜单消息失败", error);
                current?.DestroyMenu();
            }
            catch { }
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
        if (wakeMessage != 0 && message == wakeMessage)
        {
            Desktop.Log("收到重复启动唤醒，显示面板");
            ReadGameMode(immediateExit: true);
            DestroyMenu();
            DestroyPopup();
            ShowPopup(null, null);
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
                if (ShouldRefreshStripOnForeground())
                    RefreshStripGeometry();
                NotifyStripBackdropTransition();
                // 再验证一次，给 Explorer 和 DWM 留出更新全屏状态的时间。
                ArmTimer(GameSettleTimer, 750);
                ProcessWork();
                return true;
            case MoveMessage:
                locationPosted = false;
                if (game.Game || popup?.IsNotice == true || model.Settings.DisplayLocation == TrayDisplayLocation.Taskbar)
                    ReadGameMode();
                NotifyStripBackdropTransition();
                ArmTimer(GameSettleTimer, 350);
                return true;
            case 0x0219: // WM_DEVICECHANGE（C1：设备插拔通知）
                if (wParam.Value is 0x8000 or 0x8004 or 0x0007)
                {
                    if (wParam.Value is 0x8000 or 0x8004)
                        pendingExplicitDeviceChange = true;
                    ArmTimer(DeviceChangeTimer, 1_200);
                }
                return true;
            case 0x0218: // WM_POWERBROADCAST（C2：睡眠/休眠恢复）
                if (wParam.Value is 0x0012 or 0x0007 or 0x0006) // PBT_APMRESUMEAUTOMATIC / RESUMESUSPEND / RESUMECRITICAL
                {
                    Desktop.Log("系统从睡眠恢复，立即重新枚举并查询");
                    poll.InvalidateTopology();
                    schedule.NotifyPowerResume();
                    stripWindow?.DisposeGraphics();
                    InvalidateStripBackdropSample();
                    RefreshStripGeometry();
                    RefreshIcon();
                    ProcessWork();
                    ScheduleAutoUpdate();
                    return true;
                }
                return false;
            case 0x02B1: // WM_WTSSESSION_CHANGE（锁屏解锁、本地重连或 RDP 切换后重建图形并失效拓扑）
                if (wParam.Value is 0x1 or 0x3 or 0x5 or 0x8) // WTS_CONSOLE_CONNECT / REMOTE_CONNECT / SESSION_LOGON / SESSION_UNLOCK
                {
                    Desktop.Log("系统会话恢复，刷新设备与图形表面");
                    poll.InvalidateTopology();
                    schedule.NotifyPowerResume();
                    stripWindow?.DisposeGraphics();
                    InvalidateStripBackdropSample();
                    RefreshStripGeometry();
                    RefreshIcon();
                    ScheduleDesktopRefresh(resetPopup: true);
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
                if (wParam.Value == AutoUpdateTimer)
                {
                    RunAutoUpdate();
                    return true;
                }
                if (wParam.Value == DeviceChangeTimer)
                {
                    bool explicitChange = pendingExplicitDeviceChange;
                    pendingExplicitDeviceChange = false;
                    poll.InvalidateTopology();
                    if (explicitChange)
                        schedule.NotifyDeviceChange();
                    else
                        schedule.NotifyBackgroundDeviceChange();
                    ProcessWork();
                    return true;
                }
                if (wParam.Value == DesktopRefreshTimer)
                {
                    FlushDesktopRefresh();
                    return true;
                }
                if (wParam.Value == StripRetryTimer)
                {
                    HandleStripRetryTimer();
                    return true;
                }
                if (wParam.Value == StripBackdropTimer)
                {
                    HandleStripBackdropTimer();
                    return true;
                }
                if (wParam.Value == IconRetryTimer)
                {
                    iconRetryArmed = false;
                    if (StripGeometryPending)
                        RefreshStripGeometry();
                    RefreshIcon();
                    ProcessWork();
                    return true;
                }
                if (wParam.Value == CloseTimer && popup?.Closing == true)
                    DestroyPopup();
                if (wParam.Value == HoverCloseTimer)
                {
                    AdvanceHoverCard();
                    return true;
                }
                if (wParam.Value == BannerTimer)
                {
                    DestroyGameBanner();
                    return true;
                }
                if (wParam.Value == MenuCloseTimer)
                {
                    if (menu?.Closing == true)
                        DestroyMenu();
                    ProcessWork();
                    return true;
                }
                ProcessWork();
                if (wParam.Value == GameSettleTimer && stripZOrderPending)
                {
                    if (stripFullscreen)
                        stripZOrderPending = false;
                    else if (popup is null && menu is null && !HoverCardVisible && stripWindow is { VisibleBounds: not null })
                    {
                        stripWindow.EnsureTopmost();
                        stripZOrderPending = false;
                    }
                }
                return true;
            case 0x007E or 0x02E0: // WM_DISPLAYCHANGE / WM_DPICHANGED（D3：防抖且不中断改名）
                InvalidateStripBackdropSample();
                RefreshStripGeometry();
                ScheduleDesktopRefresh(resetPopup: false);
                return true;
            case 0x001A: // WM_SETTINGCHANGE（D3：仅响应与界面外观/工作区相关的系统设置，并加防抖合并重建）
                string? area = wParam.Value == 0 && lParam.Value != 0 ? Marshal.PtrToStringUni(lParam.Value) : null;
                if (DesktopRefreshDebouncer.IsRelevantSettingChange(wParam.Value, area))
                {
                    RefreshTaskbarTheme();
                    ScheduleDesktopRefresh(resetPopup: true);
                }
                return true;
            case 0x0015 or 0x031A or 0x031E or 0x0320:
                // 系统配色、主题、DWM 合成和强调色变化（D3：连续变化防抖合并为一次重建）。
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
                RequestExit();
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
        RefreshTaskbarTheme(redraw: false);
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
                activeMenu.Refresh(Anchor(menuPoint), reset: resetPopup);
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
        if (icons.TryGetRect(clickedIconId, out var bounds))
            return bounds;
        return stripWindow?.VisibleBounds is { } visibleStrip
            ? ToRect(visibleStrip)
            : Desktop.TaskbarAnchor(preferred);
    }

    private void RefreshIcon()
    {
        RefreshStrip();
        if (!stripDisplay.ShowTrayIcons || StripGeometryPending && !StripGeometryTimedOut)
        {
            icons.Clear();
            StopIconRetry();
            if (hoverIconId is not null)
                DestroyHoverCard();
            if (StripGeometryPending && !StripGeometryTimedOut)
                ArmTimer(IconRetryTimer, 3_000);
            return;
        }
        if (iconRetryArmed && !icons.AnyRegistered)
            return;
        try
        {
            // H1：不设 NIF_SHOWTIP，szTip 始终保留供读屏软件使用。
            icons.Sync(model, darkTaskbar, suppressTooltip: false);
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or UnauthorizedAccessException)
        {
            Desktop.Log("同步托盘图标失败，将重试", error);
            ArmIconRetry();
            return;
        }
        if (hoverIconId is uint hovered && !icons.TryGetSlotKey(hovered, out _))
            DestroyHoverCard();
        if (icons.AllRegistered)
            StopIconRetry();
        else
            ArmIconRetry();
    }

    private void ArmIconRetry()
    {
        if (iconFailures == 0)
            Desktop.Log("托盘图标注册失败，将重试");
        uint delay = iconFailures switch { 0 => 2_000u, 1 => 5_000u, 2 => 10_000u, _ => 60_000u };
        iconFailures = Math.Min(3, iconFailures + 1);
        iconRetryArmed = true;
        ArmTimer(IconRetryTimer, delay);
    }

    private void StopIconRetry()
    {
        if (iconRetryArmed)
            PInvoke.KillTimer(host, IconRetryTimer);
        iconRetryArmed = false;
        iconFailures = 0;
    }

    private void ReadGameMode(bool immediateExit = false)
    {
        HWND currentForeground = PInvoke.GetForegroundWindow();
        var rawGame = Desktop.GameMode(currentForeground, model.Settings);
        bool taskbarMode = model.Settings.DisplayLocation == TrayDisplayLocation.Taskbar;
        bool rawHideStrip = taskbarMode &&
            (rawGame.Game || !model.Settings.GameSilent && Desktop.IsFullscreen(currentForeground));
        var stabilized = gameStabilizer.Update(rawGame, rawHideStrip, taskbarMode, Environment.TickCount64, immediateExit);
        if (stabilized.ExitCheckDelayMs is uint exitDelay)
            ArmTimer(GameExitTimer, Math.Clamp(exitDelay + 20u, 20u, (uint)GameModeStabilizer.ExitDebounceMs + 20u));
        else if (!host.IsNull)
            PInvoke.KillTimer(host, GameExitTimer);

        var next = stabilized.Game;
        bool changed = next.BlockPopups != game.BlockPopups;
        bool enteredGame = next.Game && !game.Game;
        game = next;
        events.LogGameMode(game);
        if (enteredGame)
            ShowGameEntryBanner(currentForeground);
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
        bool hideStrip = stabilized.StripFullscreen;
        if (changed || stripFullscreen != hideStrip)
        {
            bool stripStateChanged = stripFullscreen != hideStrip;
            bool exitingFullscreen = stripFullscreen && !hideStrip;
            stripFullscreen = hideStrip;
            if (stripFullscreen)
                StopStripRetryTimer();
            if (stripStateChanged)
                stripAutomation?.SetPaused(stripFullscreen);
            if (exitingFullscreen)
            {
                stripZOrderPending = true;
                ArmTimer(GameSettleTimer, 750);
                if (stripAutomation is not { Alive: true })
                    RefreshStripGeometry();
            }
            RefreshIcon();
        }
    }

    private void ProcessWork()
    {
        ReadGameMode();
        var now = DateTimeOffset.UtcNow;
        long nowTick = Environment.TickCount64;
        bool expired = model.ExpireReadings(now, game.Game);
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
            if (ShouldShowWelcome && !(nextWelcomeAttempt > now))
                ShowWelcome();
            else if (!schedule.Running && !(nextPopupAttempt > now) &&
                model.ReadyReminderKey(now, false) is { } readyKey)
                ShowPopup(readyKey, null);
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
            dueMs = EarlierMs(dueMs, schedule.InputCheckDelayMs(nowTick));

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
            if (ShouldShowWelcome)
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
        bool showBluetooth = model.Settings.ShowBluetoothDevices;
        if (schedule.TryStartTargeted(model.Settings, nowTick, game.Game, out var targetedIds))
        {
            startedInGame = game.Game;
            bool pausePush = currentPushTargets.Length > 0 && currentPushTargets.Any(target =>
                targetedIds!.Contains(target.Target.Device.Id) ||
                target.IsMultiDevice && targetedIds.Any(id =>
                    id.StartsWith($"{target.Target.Device.Id}/", StringComparison.OrdinalIgnoreCase)));
            if (pausePush)
                StopPushListeners();
            var stoppingTargetedPush = pausePush ? pushTask : Task.CompletedTask;
            var targetedSource = new CancellationTokenSource();
            cancellation = targetedSource;
            cancelWork = Task.CompletedTask;
            var targetedToken = targetedSource.Token;
            var targetedDestination = host;
            pollTask = Task.Run(async () =>
            {
                PollResult? result;
                try
                {
                    await stoppingTargetedPush.ConfigureAwait(false);
                    result = await poll.RunTargetedAsync(targetedIds!, showBluetooth, targetedToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (targetedToken.IsCancellationRequested)
                {
                    result = null;
                }
                catch (Exception error)
                {
                    Desktop.Log("定向查询设备失败", error);
                    result = new([], false, Complete: false);
                }
                results.Enqueue((result, [], true));
                PInvoke.PostMessage(targetedDestination, ResultMessage, default, default);
            });
            RenderPopup();
            return;
        }

        bool forceRescan = schedule.Start(nowTick, game.Game);
        if (schedule.StartedByUser)
            poll.ResetPollingCaches();
        startedInGame = game.Game;
        // 完整轮询期间暂停监听，由读取路径使用相同集合。
        StopPushListeners();
        var stoppingPush = pushTask;
        var source = new CancellationTokenSource();
        cancellation = source;
        cancelWork = Task.CompletedTask;
        var token = source.Token;
        var destination = host;
        pollTask = Task.Run(async () =>
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
            results.Enqueue((result, pushTargets, false));
            PInvoke.PostMessage(destination, ResultMessage, default, default);
        });
        RenderPopup();
    }

    private void CompletePoll()
    {
        if (!results.TryDequeue(out var item))
            return;
        var (result, pushTargets, targeted) = item;
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
            foreach (var reading in result.Readings)
                events.RecordReadFailure(reading.Issue, nowTick);
            events.FlushReadFailuresIfDue(nowTick);

            bool shouldSave;
            if (targeted)
            {
                shouldSave = model.ApplyTargetedPoll(result.Readings, DateTimeOffset.UtcNow, events.LogDeviceLinkChange);
                if (pushCancellation is null && currentPushTargets.Length > 0)
                    StartPushListeners(currentPushTargets);
            }
            else
            {
                hasHeartbeatDevices = result.HasHeartbeatDevices;
                currentPushTargets = pushTargets;
                events.LogReceiverChanges(result.ReceiverChanges);
                events.LogUnknownReceivers(result.Unknowns);
                shouldSave = model.ApplyPoll(result.Readings, result.ScanFailed, DateTimeOffset.UtcNow,
                    result.UnknownReceivers, events.LogDeviceLinkChange, result.LiveTargets, result.Complete,
                    result.IncompleteReceivers);
                StartPushListeners(pushTargets);
            }
            if (shouldSave || gates.Version != Volatile.Read(ref lastSavedGateVersion))
                SaveState();
            RefreshIcon();
        }
        else if (pushCancellation is null && currentPushTargets.Length > 0)
        {
            StartPushListeners(currentPushTargets);
        }
        FlushPendingRedetect();
        RenderPopup();
        RenderHoverCard();
        ProcessWork();
    }

    private void StartHeartbeat(long nowTick, IReadOnlySet<string>? onlyDeviceIds = null, bool inputWake = false)
    {
        schedule.StartHeartbeat(nowTick, inputWake);
        bool pausedPush = currentPushTargets.Length > 0 && onlyDeviceIds is not null &&
            currentPushTargets.Any(target =>
                onlyDeviceIds.Contains(target.Target.Device.Id) ||
                target.IsMultiDevice && onlyDeviceIds.Any(id =>
                    id.StartsWith($"{target.Target.Device.Id}/", StringComparison.OrdinalIgnoreCase)));
        if (pausedPush)
            StopPushListeners();
        var stoppingPush = pausedPush ? pushTask : Task.CompletedTask;
        bool showBluetooth = model.Settings.ShowBluetoothDevices;
        var source = new CancellationTokenSource();
        heartbeatCancellation = source;
        var token = source.Token;
        var destination = host;
        heartbeatTask = Task.Run(async () =>
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
        bool cancelled = heartbeatCancellation?.IsCancellationRequested == true;
        if (heartbeatCancellation is { } completedHb)
            _ = DisposeCancellation(completedHb, heartbeatCancelWork);
        heartbeatCancellation = null;
        heartbeatCancelWork = Task.CompletedTask;
        schedule.CompleteHeartbeat();
        if (cancellation is null && pushCancellation is null && currentPushTargets.Length > 0)
            StartPushListeners(currentPushTargets);

        if (!cancelled && updates.Length > 0)
        {
            long nowTick = Environment.TickCount64;
            foreach (var update in updates)
                events.RecordReadFailure(update.Issue, nowTick);
            var (uiChanged, needsFullRefresh, refreshIds) = model.ApplyPresenceDetailed(
                updates, DateTimeOffset.UtcNow, events.LogDeviceLinkChange);
            if (uiChanged)
            {
                if (updates.Any(update => update.Battery is not null))
                    SaveState();
                RefreshIcon();
                RenderPopup();
                RenderHoverCard();
            }
            if (needsFullRefresh)
                schedule.Request(userInitiated: false);
            else if (refreshIds.Length > 0)
                schedule.RequestTargeted(refreshIds);
        }
        FlushPendingRedetect();
        MaybeSaveGates();
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
                BatteryReading? lastPassiveReading = null;
                await poll.ListenPushAsync(target, update =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (update.Battery is { Device.UsesPassiveBatteryReports: true } reading)
                    {
                        // 鼠标活动时可能反复报告同一个状态，避免把重复状态送到 UI 线程。
                        if (lastPassiveReading is { } previous && previous.Device == reading.Device &&
                            previous.Percentage == reading.Percentage && previous.Charging == reading.Charging &&
                            reading.ReadAt - previous.ReadAt < TimeSpan.FromSeconds(1))
                            return;
                        lastPassiveReading = reading;
                    }
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
                Desktop.Log("设备推送监听异常", error);
            }
        })));
    }

    private void StopPushListeners()
    {
        if (pushCancellation is not { } active)
            return;
        pushCancellation = null;
        var runningPush = pushTask;
        _ = DisposeCancellation(active, Task.WhenAll(active.CancelAsync(), runningPush));
    }

    private void CompletePushPresence()
    {
        List<PresenceUpdate> batch = [];
        while (pushResults.TryDequeue(out var update))
            batch.Add(update);
        if (batch.Count == 0)
            return;

        var (uiChanged, needsFullRefresh, refreshIds) = model.ApplyPresenceDetailed(
            batch, DateTimeOffset.UtcNow, events.LogDeviceLinkChange);
        if (uiChanged)
        {
            if (batch.Any(update => update.Battery is not null))
                SaveState();
            RefreshIcon();
            RenderPopup();
            RenderHoverCard();
        }
        if (needsFullRefresh)
            schedule.Request(userInitiated: false);
        else if (refreshIds.Length > 0)
            schedule.RequestTargeted(refreshIds);
        if (uiChanged || needsFullRefresh || refreshIds.Length > 0)
            ProcessWork();
        MaybeSaveGates();
    }

    private void MaybeSaveGates()
    {
        if (gates.Version != Volatile.Read(ref lastSavedGateVersion))
            SaveState();
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
        long gateVersion = gates.Version;
        var snapshot = model.Snapshot(gates.Snapshot());
        lock (stateWriteLock)
        {
            pendingStateWrite = (snapshot, gateVersion);
            if (!stateWriteActive)
            {
                stateWriteActive = true;
                stateWrite = Task.Run(DrainStateWrites);
            }
        }
    }

    private void DrainStateWrites()
    {
        try
        {
            while (true)
            {
                (TrayState State, long GateVersion) next;
                lock (stateWriteLock)
                {
                    if (pendingStateWrite is not { } item)
                    {
                        stateWriteActive = false;
                        return;
                    }
                    next = item;
                    pendingStateWrite = null;
                }
                try
                {
                    files.SaveState(next.State);
                    Interlocked.Exchange(ref lastSavedGateVersion, next.GateVersion);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Desktop.Log("保存电量记录失败", error);
                }
                catch (Exception error)
                {
                    Desktop.Log("保存电量记录发生异常", error);
                }
            }
        }
        finally
        {
            lock (stateWriteLock)
                stateWriteActive = false;
        }
    }

    private bool SaveSettings(TraySettings settings, bool userInitiated = false)
    {
        try
        {
            files.SaveSettings(settings, userInitiated, Desktop.Log);
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

    private bool UpdateAutostart(bool enabled, bool userInitiated = false)
    {
        try
        {
            Desktop.SetAutostart(enabled, clearTaskManagerDisable: userInitiated);
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
        if (autostartChanged && !UpdateAutostart(settings.StartWithWindows, userInitiated: true))
        {
            RenderPopup();
            return false;
        }
        if (!SaveSettings(settings, userInitiated: true))
        {
            if (autostartChanged)
                UpdateAutostart(model.Settings.StartWithWindows, userInitiated: false);
            RenderPopup();
            return false;
        }

        bool gameSettingChanged = settings.GameSilent != model.Settings.GameSilent;
        bool bluetoothChanged = settings.ShowBluetoothDevices != model.Settings.ShowBluetoothDevices;
        bool displayLocationChanged = settings.DisplayLocation != model.Settings.DisplayLocation;
        bool stripAppearanceChanged = settings.TaskbarStyle != model.Settings.TaskbarStyle ||
            settings.TaskbarColor != model.Settings.TaskbarColor;
        bool themeChanged = settings.Theme != model.Settings.Theme;
        bool autoUpdateChanged = settings.AutoUpdateEnabled != model.Settings.AutoUpdateEnabled;
        model.ApplySettings(settings, DateTimeOffset.UtcNow);
        if (remove is not null)
            model.RemoveOffline(remove);
        if (gameSettingChanged)
        {
            SetForegroundTracking();
            ReadGameMode(immediateExit: true);
        }
        if (displayLocationChanged)
            RefreshStripGeometry();
        if (stripAppearanceChanged)
            ApplyStripAppearanceSettings();
        if (autoUpdateChanged)
        {
            if (settings.AutoUpdateEnabled)
                autoUpdate.Enabled(Environment.TickCount64);
            else
                CancelBackgroundUpdate();
            ScheduleAutoUpdate();
        }
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
        bool reused = hoverCard is not null;
        try
        {
            ShowHoverCardCore(iconId, anchor, content, now);
        }
        catch (Exception error) when (reused && Graphics.IsDeviceLost(error))
        {
            try
            {
                DestroyHoverCard();
                ShowHoverCardCore(iconId, anchor, content, now);
            }
            catch (Exception retryError)
            {
                DestroyHoverCard();
                Desktop.Log("显卡设备重置后显示悬停卡片仍失败", retryError);
            }
        }
        catch (Exception error)
        {
            DestroyHoverCard();
            Desktop.Log("显示悬停卡片失败", error);
        }
    }

    private void ShowHoverCardCore(uint? iconId, RECT anchor, HoverCardContent content, DateTimeOffset now)
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
        catch (Exception error) when (!reset && Graphics.IsDeviceLost(error))
        {
            try
            {
                active.Update(content, anchor, reset: true);
                hoverTextDue = now.AddMinutes(1);
            }
            catch (Exception retryError)
            {
                DestroyHoverCard();
                Desktop.Log("显卡设备重置后刷新悬停卡片仍失败", retryError);
            }
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

    /// <summary>
    /// 刚进入游戏时，若有在用设备电量低（或预计不到 1 小时），在右下角弹一条不抢焦点的小横幅，几秒后自动消失。
    /// 独占全屏游戏不叠窗口，等退出游戏后由常规低电量提醒补上。
    /// </summary>
    private void ShowGameEntryBanner(HWND foreground)
    {
        if (game.Status.Reason == GameDetectionReason.D3dFullscreen || !model.Settings.GameEntryReminderEnabled)
            return;
        var now = DateTimeOffset.UtcNow;
        var warnings = model.TakeGameEntryWarnings(now);
        if (warnings.Length == 0)
            return;
        var device = warnings[0];
        try
        {
            DestroyGameBanner();
            var extra = WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP;
            HWND window = MakeWindow(BannerClass, extra, WS_POPUP | WS_THICKFRAME);
            gameBanner = new GameBannerWindow(window, model);
            gameBanner.Show(device, model.Runtime(device, now), warnings.Length - 1, foreground);
            ArmTimer(BannerTimer, GameBannerWindow.VisibleMs);
            Desktop.Log("显示进游戏低电量横幅");
        }
        catch (Exception error)
        {
            DestroyGameBanner();
            Desktop.Log("显示进游戏低电量横幅失败", error);
        }
    }

    private void DestroyGameBanner()
    {
        if (!host.IsNull)
            PInvoke.KillTimer(host, BannerTimer);
        var previous = gameBanner;
        gameBanner = null;
        previous?.Dispose();
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
        ReadGameMode(immediateExit: true);
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
            ReadGameMode(immediateExit: !automatic);
            if (popup is not { } active)
                return false;
            active.Show();
            Desktop.Log(page == PanelPage.Welcome ? "显示首次使用引导" : automatic
                ? $"显示{(noticeId is null ? "" : ReminderKeys.Parse(noticeId).Kind switch
                {
                    ReminderKind.Predicted => "预测",
                    ReminderKind.FullCharge => "充满",
                    _ => "低电量"
                })}提醒"
                : "显示面板");
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
            active.Render(now, schedule.Running, settingsError, diagnostics.Status, diagnostics.PreviewPending, updateState,
                DisplayLocationFallbackReason, reset, darkTaskbar);
            textDue = active.IsNotice ? null : now.AddMinutes(1);
        }
        catch (Exception error) when (!reset && Graphics.IsDeviceLost(error))
        {
            try
            {
                active.Render(now, schedule.Running, settingsError, diagnostics.Status, diagnostics.PreviewPending, updateState,
                    DisplayLocationFallbackReason, reset: true, darkTaskbar);
                textDue = active.IsNotice ? null : now.AddMinutes(1);
            }
            catch (Exception retryError)
            {
                DestroyPopup();
                Desktop.Log("显卡设备重置后刷新面板仍失败", retryError);
                if (active.IsNotice)
                    nextPopupAttempt = now.AddSeconds(60);
            }
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
                var targetWindow = nextWindow.IsNull ? PInvoke.GetForegroundWindow() : nextWindow;
                var kind = Desktop.Classify(targetWindow, out _);
                string category = TrayLog.Foreground(kind);
                if (Desktop.IsTaskbarWindow(targetWindow) && active.TryReclaimFocus())
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
            case 0x8010: // 托盘刚收回焦点时，确认前台仍是任务栏窗口才重新激活，避免抢夺桌面或开始菜单焦点。
                active.CompleteReclaim();
                var foregroundNow = PInvoke.GetForegroundWindow();
                if (Desktop.IsTaskbarWindow(foregroundNow))
                    PInvoke.SetForegroundWindow(active.Handle);
                else if (foregroundNow != active.Handle)
                    ClosePopup();
                return true;
            case 0x0215: // WM_CAPTURECHANGED
                active.CaptureChanged();
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
            case PopupAction.TrayMode or PopupAction.Threshold or PopupAction.PollInterval or PopupAction.DisplayLocation
                or PopupAction.TaskbarStyle or PopupAction.TaskbarColor or PopupAction.Theme
                or PopupAction.GameEntryReminder or PopupAction.FullChargeReminder:
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
            case PopupAction.AutoUpdate:
                ChangeSettings(model.Settings with { AutoCheckUpdates = !model.Settings.AutoUpdateEnabled });
                break;
            case PopupAction.RuntimeEstimate:
                ChangeSettings(model.Settings with { ShowRuntimeEstimate = !model.Settings.ShowRuntimeEstimate });
                break;
            case PopupAction.DeviceThreshold when target.DeviceId is { } thresholdId:
                ChangeSettings(model.Settings.CycleDeviceThreshold(thresholdId));
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
                // 主面板底栏的「新版已就绪」先切到更新页，让用户看到校验与重启过程。
                if (active.CurrentPage == PanelPage.Main)
                    active.Navigate(PanelPage.Updates);
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
        if (cancellation is { IsCancellationRequested: false } activePoll)
            cancelWork = activePoll.CancelAsync();
        if (heartbeatCancellation is { IsCancellationRequested: false } activeHeartbeat)
            heartbeatCancelWork = activeHeartbeat.CancelAsync();
        if (schedule.Running || schedule.HeartbeatRunning)
        {
            // 等旧轮询/心跳真正退出后再清理安全门与拓扑缓存，避免旧任务收尾把刚清除的拦截记录写回。
            pendingRedetect = true;
            Desktop.Log("用户触发重新检测，等待当前轮询结束后清除安全门拦截并刷新设备缓存");
            return;
        }
        ExecuteRedetect();
        ProcessWork();
    }

    private void FlushPendingRedetect()
    {
        if (pendingRedetect && !schedule.Running && !schedule.HeartbeatRunning)
            ExecuteRedetect();
    }

    private void ExecuteRedetect()
    {
        pendingRedetect = false;
        gates.Clear();
        events.ResetSafetyGateLog();
        poll.InvalidateTopology();
        SaveState();
        Desktop.Log("用户触发重新检测，已清除安全门拦截并刷新设备缓存");
        schedule.Request(userInitiated: true, rescan: true);
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
        if (!MenuOpen)
            openedFromStrip = false;
        textDue = null;
        welcomeExpires = null;
        previous?.Dispose();
        if (desktopRefresh.HasPending && !host.IsNull)
            ArmTimer(DesktopRefreshTimer, DesktopRefreshDebouncer.DebounceMs);
    }

    private void ShowMenu(Point point)
    {
        DestroyHoverCard();
        Point? normalized = point is { X: -1, Y: -1 } ? null : point;
        menuPoint = normalized;
        try
        {
            PInvoke.KillTimer(host, MenuCloseTimer);
            var anchor = Anchor(normalized);
            var extra = WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP;
            HWND window = MakeWindow(MenuClass, extra, WS_POPUP | WS_THICKFRAME);
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
                var targetWindow = nextWindow.IsNull ? PInvoke.GetForegroundWindow() : nextWindow;
                if (Desktop.IsTaskbarWindow(targetWindow) && active.TryReclaimFocus())
                    return true;
                if (PInvoke.GetCursorPos(out var cursor) && (icons.ContainsCursor(cursor) || stripWindow?.Contains(cursor) == true))
                    lostFocusByTray = Environment.TickCount64;
                CloseMenu();
                return true;
            case 0x8010:
                active.CompleteReclaim();
                var foregroundNow = PInvoke.GetForegroundWindow();
                if (Desktop.IsTaskbarWindow(foregroundNow))
                    PInvoke.SetForegroundWindow(active.Handle);
                else if (foregroundNow != active.Handle)
                    CloseMenu();
                return true;
            case 0x0215: // WM_CAPTURECHANGED
                active.CaptureChanged();
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
        var anchorPoint = menuPoint;
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
                RequestExit();
                break;
        }
    }

    private void RequestExit()
    {
        exitRequested = true;
        DestroyMenu();
        DestroyHoverCard();
        DestroyPopup();
        try { updateCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        PInvoke.PostQuitMessage(0);
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
        // 后台已下载好新版时直接展示「重启并升级」，不重新检查，以免丢掉已校验的安装包。
        bool shouldCheck = !UpdateActive && !updateState.IsReady &&
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

    private void StartUpdateCheck() => StartUpdateCheck(background: false);

    /// <summary>
    /// 检查最新正式版。后台检查不切换界面状态，失败只写日志；查到新版本后自动预先下载并校验，
    /// 安装仍要等用户点「重启并升级」。
    /// </summary>
    private void StartUpdateCheck(bool background)
    {
        CancelUpdateTask();
        int opId = ++updateOperationId;
        backgroundUpdateId = background ? opId : null;
        string prefix = background ? "后台检查：" : "";
        string current = ReleaseVersion.Current;
        string currentLabel = updateState.CurrentVersionLabel;
        bool installed = updateState.IsInstalled;
        if (!ReleaseVersion.TryParseInstalled(current, out var version, out bool isPrerelease, out _))
        {
            events.LogUpdateCheck($"{prefix}失败（InvalidVersion）");
            if (background)
            {
                autoUpdate.Failed(Environment.TickCount64);
                ScheduleAutoUpdate();
                return;
            }
            updateState = UpdateViewState.Failed(currentLabel, installed, $"无法识别当前版本 {current}。");
            RenderPopup();
            return;
        }

        var source = new CancellationTokenSource();
        updateCancellation = source;
        var token = source.Token;
        if (!background)
        {
            updateState = UpdateViewState.Checking(currentLabel, installed);
            RenderPopup();
        }

        updateWork = Task.Run(async () =>
        {
            CancellationTokenSource? timeout = null;
            try
            {
                timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                using var http = UpdateClient.CreateHttpClient();
                var client = new UpdateClient(http);
                var update = await client.CheckAsync(version, timeout.Token, isPrerelease).ConfigureAwait(false);
                var checkedAt = DateTimeOffset.UtcNow;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    FinishUpdateCancellation(opId);
                    if (background)
                        autoUpdate.Succeeded(Environment.TickCount64);
                    if (update is null)
                    {
                        events.LogUpdateCheck($"{prefix}已是最新版本 {currentLabel}");
                        if (!background || updateState.Phase is UpdatePhase.Idle or UpdatePhase.UpToDate)
                        {
                            bool hadReady = updateState.Ready is not null;
                            updateState = UpdateViewState.UpToDate(currentLabel, installed, checkedAt);
                            if (hadReady)
                                CleanupUpdates();
                        }
                    }
                    else if (background && updateState.IsReady && updateState.Available?.Version == update.Version)
                    {
                        // 同一版本已经下载好，保留现有安装包，不重复下载。
                        updateState = updateState with { CheckedAt = checkedAt };
                    }
                    else
                    {
                        events.LogUpdateCheck($"{prefix}发现新版本 v{update.Version}（当前 {currentLabel}，来源：{update.EffectiveSource.Name}）");
                        updateState = UpdateViewState.Found(currentLabel, installed, update, checkedAt);
                        if (background)
                            StartUpdateDownload(install: false, background: true);
                    }
                    RenderPopup();
                    ScheduleAutoUpdate();
                });
            }
            catch (OperationCanceledException)
            {
                bool timedOut = timeout?.IsCancellationRequested == true && !token.IsCancellationRequested;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    FinishUpdateCancellation(opId);
                    if (background)
                    {
                        events.LogUpdateCheck(timedOut ? "后台检查：失败（Timeout）" : "后台检查：已取消");
                        autoUpdate.Failed(Environment.TickCount64);
                        ScheduleAutoUpdate();
                        return;
                    }
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
            catch (Exception error)
            {
                string message = UpdateViewState.ExplainError(error);
                string kind = error.GetType().Name;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    FinishUpdateCancellation(opId);
                    events.LogUpdateCheck($"{prefix}失败（{kind}）");
                    if (background)
                    {
                        autoUpdate.Failed(Environment.TickCount64);
                        ScheduleAutoUpdate();
                        return;
                    }
                    updateState = UpdateViewState.Failed(currentLabel, installed, message);
                    RenderPopup();
                });
            }
            finally
            {
                timeout?.Dispose();
            }
        });
    }

    private void StartUpdateInstall() => StartUpdateDownload(install: true, background: false);

    /// <summary>
    /// 下载并校验新版安装包。install 为 false 时只预先下载（后台），完成后进入「已就绪」；
    /// install 为 true 时优先复用已就绪的安装包（重新核对 SHA-256），否则重新下载，然后启动静默安装。
    /// </summary>
    private void StartUpdateDownload(bool install, bool background)
    {
        if (UpdateActive || updateState.Available is not { } update || !updateState.IsInstalled)
            return;
        var ready = updateState.Ready;
        if (!install && ready is not null)
            return;

        CancelUpdateTask();
        int opId = ++updateOperationId;
        backgroundUpdateId = background ? opId : null;
        activeUpdateInstalls = install;
        var source = new CancellationTokenSource();
        updateCancellation = source;
        var token = source.Token;
        string currentLabel = updateState.CurrentVersionLabel;
        bool installed = updateState.IsInstalled;
        updateState = updateState.WithProgress(ready is null
            ? new(0, update.Installer.Size, Verifying: false)
            : new(update.Installer.Size, update.Installer.Size, Verifying: true));
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

        // 安装路径失败或取消时 UpdateInstallation 会清空更新目录，已就绪的安装包随之失效。
        UpdateViewState Interrupted(UpdateViewState state) => install ? state with { Ready = null } : state;

        // 串行等待上一个可能仍在清理 updates 目录的更新任务彻底退出，避免并发操作同一目录。
        var previousWork = updateWork;
        updateWork = Task.Run(async () =>
        {
            CancellationTokenSource? timeout = null;
            try
            {
                try { await previousWork.ConfigureAwait(false); }
                catch { }
                token.ThrowIfCancellationRequested();
                timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                using var http = UpdateClient.CreateHttpClient();
                var client = new UpdateClient(http);
                if (!install)
                {
                    var package = await client.DownloadVerifiedAsync(update, files.Updates, progress, timeout.Token)
                        .ConfigureAwait(false);
                    PostUpdateCallback(() =>
                    {
                        if (opId != updateOperationId || exitRequested)
                            return;
                        FinishUpdateCancellation(opId);
                        events.LogUpdateCheck($"新版本 v{update.Version} 已下载并通过校验，等待重启升级");
                        updateState = updateState.WithReady(package);
                        if (background)
                            autoUpdate.Succeeded(Environment.TickCount64);
                        RenderPopup();
                        ScheduleAutoUpdate();
                    });
                    return;
                }

                string? preparedPackage = null;
                bool launched = await UpdateInstallation.RunAsync(
                    files.Updates,
                    async () => ready is not null && UpdateFiles.TryReverify(ready)
                        ? ready.Path
                        : await client.DownloadAsync(update, files.Updates, progress, timeout.Token).ConfigureAwait(false),
                    () => exitRequested || token.IsCancellationRequested,
                    package =>
                    {
                        if (!File.Exists(package))
                            throw new IOException("更新安装包不存在。");
                        preparedPackage = package;
                    }).ConfigureAwait(false);
                if (launched && preparedPackage is not null)
                {
                    PostUpdateCallback(() =>
                    {
                        if (opId != updateOperationId || exitRequested || token.IsCancellationRequested)
                        {
                            // 仅在没有新一轮更新活动且无有效已就绪安装包时清理目录，避免误删重入后的新下载。
                            if (!UpdateActive && updateState.Ready is null)
                                files.Updates.TryClear();
                            return;
                        }
                        FinishUpdateCancellation(opId);
                        pendingInstallerPackage = preparedPackage;
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
                bool timedOut = timeout?.IsCancellationRequested == true && !token.IsCancellationRequested;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    FinishUpdateCancellation(opId);
                    if (timedOut && background)
                    {
                        events.LogUpdateCheck("后台下载：失败（Timeout）");
                        autoUpdate.Failed(Environment.TickCount64);
                        updateState = updateState.BackToAvailable();
                        ScheduleAutoUpdate();
                    }
                    else if (timedOut)
                    {
                        events.LogUpdateCheck("失败（Timeout）");
                        updateState = UpdateViewState.Failed(currentLabel, installed, "下载超时，请检查网络后重试。", update);
                    }
                    else
                    {
                        events.LogUpdateCheck("已取消");
                        updateState = Interrupted(updateState.BackToAvailable());
                    }
                    RenderPopup();
                });
            }
            catch (Exception error)
            {
                string message = UpdateViewState.ExplainError(error);
                string kind = error.GetType().Name;
                PostUpdateCallback(() =>
                {
                    if (opId != updateOperationId || exitRequested)
                        return;
                    FinishUpdateCancellation(opId);
                    if (background)
                    {
                        // 后台下载失败不显示错误页，回到「发现新版本」，用户仍可手动下载；稍后再自动重试。
                        events.LogUpdateCheck($"后台下载：失败（{kind}）");
                        autoUpdate.Failed(Environment.TickCount64);
                        updateState = updateState.BackToAvailable();
                        ScheduleAutoUpdate();
                    }
                    else
                    {
                        events.LogUpdateCheck($"失败（{kind}）");
                        updateState = UpdateViewState.Failed(currentLabel, installed, message, update);
                    }
                    RenderPopup();
                });
            }
            finally
            {
                timeout?.Dispose();
            }
        });
    }

    private void ScheduleAutoUpdate()
    {
        if (host.IsNull || exitRequested)
            return;
        if (autoUpdate.Delay(Environment.TickCount64, model.Settings.AutoUpdateEnabled, updateState.IsInstalled) is long ms)
            ArmTimer(AutoUpdateTimer, (uint)Math.Clamp(ms, 1_000, int.MaxValue));
        else
            PInvoke.KillTimer(host, AutoUpdateTimer);
    }

    private void RunAutoUpdate()
    {
        ReadGameMode();
        if (autoUpdate.TryStart(Environment.TickCount64, model.Settings.AutoUpdateEnabled, updateState.IsInstalled,
                game.Game, UpdateActive))
            StartUpdateCheck(background: true);
        ScheduleAutoUpdate();
    }

    /// <summary>关闭「自动检查更新」时停止正在进行的后台检查或下载；已就绪的安装包保留。</summary>
    private void CancelBackgroundUpdate()
    {
        if (backgroundUpdateId != updateOperationId || updateCancellation is null)
            return;
        bool downloading = UpdateActive;
        CancelUpdateTask();
        backgroundUpdateId = null;
        if (downloading)
        {
            events.LogUpdateCheck("已关闭自动检查更新，停止后台下载");
            updateState = updateState.BackToAvailable();
        }
        RenderPopup();
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
        bool clearReady = updateState.ShouldClearReadyOnCancel(activeUpdateInstalls);
        CancelUpdateTask();
        events.LogUpdateCheck("已取消");
        if (wasChecking)
        {
            updateState = UpdateViewState.Idle(updateState.CurrentVersionLabel, updateState.IsInstalled);
            activePopup.Navigate(activePopup.UpdatesReturnPage);
        }
        else
        {
            updateState = updateState.BackToAvailable(clearReady: clearReady);
            if (clearReady)
                CleanupUpdates();
            RenderPopup();
        }
    }

    private void CancelUpdateTask()
    {
        updateOperationId++;
        activeUpdateInstalls = false;
        if (updateCancellation is not { } active)
            return;
        updateCancellation = null;
        var runningUpdate = updateWork;
        _ = DisposeCancellation(active, Task.WhenAll(active.CancelAsync(), runningUpdate));
    }

    private void FinishUpdateCancellation(int opId)
    {
        if (opId != updateOperationId)
            return;
        activeUpdateInstalls = false;
        if (updateCancellation is not { } active)
            return;
        updateCancellation = null;
        active.Dispose();
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

    private static void LaunchInstallerAfterShutdown(string package)
    {
        try
        {
            var start = new ProcessStartInfo(package)
            {
                ArgumentList = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/JIGERESTART" },
                UseShellExecute = false
            };
            // 拉起安装器后本进程必须立刻退出以释放 Jige.exe 映像锁；若安装器中途失败，由 installer/jige.iss 的
            // DeinitializeSetup 按 /JIGERESTART 拉回托盘。切勿在此 WaitForExit，否则会与静默安装器互锁导致退出码 5。
            using var installer = Process.Start(start) ?? throw new Win32Exception(0, "无法启动几格安装器。");
        }
        catch (Exception error)
        {
            Desktop.Log("启动静默安装器失败，尝试恢复托盘程序", error);
            try
            {
                if (Environment.ProcessPath is { Length: > 0 } self && File.Exists(self))
                {
                    using var restored = Process.Start(new ProcessStartInfo(self)
                    {
                        ArgumentList = { "--after-setup" },
                        UseShellExecute = false
                    });
                }
            }
            catch (Exception restoreError)
            {
                Desktop.Log("恢复托盘程序失败", restoreError);
            }
        }
    }

    private sealed class DirectProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private void CleanupUpdates()
    {
        // 后台预先下载好的安装包要留到用户点「重启并升级」。
        if (updateState.Ready is not null)
            return;
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
            DisplayLocationReason = stripDisplay.Description,
            AutoCheckUpdates = settings.AutoUpdateEnabled
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
                .GenerateWindowsAsync(timeout.Token, context, poll.Registry).ConfigureAwait(false);
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
            string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            using var editor = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = notepad,
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
            var info = new FileInfo(files.DiagnosticsPath);
            if (info.Length > 8 * 1024 * 1024)
            {
                diagnostics.FeedbackFailed();
                Desktop.Log($"诊断报告体积异常（{info.Length} 字节），已拒绝复制到剪贴板");
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
        exitRequested = true;
        if (!hidNotify.IsNull)
        {
            PInvoke.UnregisterDeviceNotification(hidNotify);
            hidNotify = default;
        }
        if (!comNotify.IsNull)
        {
            PInvoke.UnregisterDeviceNotification(comNotify);
            comNotify = default;
        }
        if (!foregroundHook.IsNull)
        {
            PInvoke.UnhookWinEvent(foregroundHook);
            foregroundHook = default;
        }
        if (!locationHook.IsNull)
        {
            PInvoke.UnhookWinEvent(locationHook);
            locationHook = default;
        }
        DestroyMenu();
        DestroyHoverCard();
        DestroyGameBanner();
        DestroyPopup();
        DisposeStrip();
        icons?.Dispose();

        // 先发出所有后台取消信号，待后台任务退出后再销毁消息窗口与 CTS，避免句柄复用投递与释放竞态。
        var pushCts = pushCancellation;
        pushCancellation = null;
        try { pushCts?.Cancel(); } catch (ObjectDisposedException) { }

        var pollCts = cancellation;
        cancellation = null;
        try { pollCts?.Cancel(); } catch (ObjectDisposedException) { }

        var hbCts = heartbeatCancellation;
        heartbeatCancellation = null;
        try { hbCts?.Cancel(); } catch (ObjectDisposedException) { }

        var updateCts = updateCancellation;
        updateCancellation = null;
        try { updateCts?.Cancel(); } catch (ObjectDisposedException) { }

        try { diagnosticCancellation.Cancel(); } catch (ObjectDisposedException) { }

        // 退出前顺带保存剩余时间估算累计的在线时长；电量没变时 SaveState 字节比较后不会重复写盘。
        if (model.HasUnsavedRuntime)
            SaveState();

        Task currentStateWrite;
        lock (stateWriteLock)
            currentStateWrite = stateWrite;

        long shutdownDeadline = Environment.TickCount64 + 3_000;
        foreach (var (name, task) in new (string Name, Task Work)[]
        {
            ("轮询", pollTask),
            ("心跳", heartbeatTask),
            ("推送", pushTask),
            ("更新", updateWork),
            ("状态写盘", currentStateWrite),
            ("诊断导出", diagnostics.Work ?? Task.CompletedTask)
        })
        {
            int remaining = (int)Math.Max(0, shutdownDeadline - Environment.TickCount64);
            try
            {
                if (!task.Wait(remaining))
                    Desktop.Log($"退出等待{name}超时，已直接退出");
            }
            catch { }
        }

        if (pollTask.IsCompleted && cancelWork.IsCompleted)
            try { pollCts?.Dispose(); } catch { }
        if (heartbeatTask.IsCompleted && heartbeatCancelWork.IsCompleted)
            try { hbCts?.Dispose(); } catch { }
        if (pushTask.IsCompleted)
            try { pushCts?.Dispose(); } catch { }
        if (updateWork.IsCompleted)
            try { updateCts?.Dispose(); } catch { }
        if (diagnostics.Work?.IsCompleted != false)
            try { diagnosticCancellation.Dispose(); } catch { }

        if (sessionNotifyRegistered && !host.IsNull)
        {
            PInvoke.WTSUnRegisterSessionNotification(host);
            sessionNotifyRegistered = false;
        }
        var window = host;
        host = default;
        if (!window.IsNull)
            PInvoke.DestroyWindow(window);
    }
}

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
internal sealed class TrayApp : IDisposable
{
    private const string HostClass = "Jige.Tray.Host";
    private const string PopupClass = "Jige.Tray.Popup";
    private const uint TrayMessage = 0x8001;
    private const uint ResultMessage = 0x8002;
    private const uint ForegroundMessage = 0x8003;
    private const uint MoveMessage = 0x8004;
    private const uint ForegroundEvent = 0x0003;
    private const uint LocationEvent = 0x800B;
    private const nuint WorkTimer = 1, GameSettleTimer = 2, CloseTimer = 3, UpdateCleanupTimer = 4;
    private static readonly TimeSpan GameRecheck = TimeSpan.FromSeconds(10);
    private enum TrayAction { Panel, Menu }
    private static TrayApp? current;

    private readonly TrayFiles files = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jige"));
    private readonly TrayModel model;
    private readonly PollSchedule schedule = new(DateTimeOffset.UtcNow);
    private readonly DevicePoll poll = new();
    private readonly ConcurrentQueue<PollResult?> results = new();
    private readonly HINSTANCE instance;
    private readonly uint taskbarCreated;
    private readonly TrayIcon icon;
    private HWND host;
    private HWND foreground;
    private HWINEVENTHOOK foregroundHook, locationHook;
    private uint locationProcess, locationThread;
    private bool foregroundPosted, locationPosted;
    private PopupWindow? popup;
    private GameMode game;
    private (TrayAction Action, Point Anchor)? pendingUi;
    private DateTimeOffset? nextPopupAttempt;
    private DateTimeOffset? textDue;
    private long lastTraySelect = long.MinValue / 2;
    private long lostFocusByTray = long.MinValue / 2;
    private bool darkTaskbar;
    private bool menuOpen;
    private UpdateDialog? updateDialog;
    private int cleanupAttempts;
    private bool exitRequested;
    private string? settingsError;
    private readonly bool configLoaded;
    private bool startedInGame;
    private CancellationTokenSource? cancellation;
    private Task cancelWork = Task.CompletedTask;
    private Task stateWrite = Task.CompletedTask;

    private unsafe TrayApp()
    {
        instance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR));
        taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        configLoaded = files.TryLoadSettings(out var settings);
        model = new(settings, files.LoadState());
        darkTaskbar = Desktop.DarkTaskbar;

        Register(HostClass, &HostCallback);
        Register(PopupClass, &PopupCallback);
        host = MakeWindow(HostClass, 0, WS_POPUP);
        icon = new(host, TrayMessage);
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
            Desktop.Log(error.Message);
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
        if (configLoaded)
            UpdateAutostart(model.Settings.StartWithWindows);
        else if (!File.Exists(files.ConfigPath))
            SaveSettings(model.Settings);
        else
            settingsError = "配置读取失败";
        foregroundHook = InstallHook(ForegroundEvent);
        SetForegroundTracking();
        ReadGameMode();
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

    private unsafe HWND MakeWindow(string name, WINDOW_EX_STYLE extras, WINDOW_STYLE style)
    {
        fixed (char* className = name)
        fixed (char* title = "几格")
        {
            var window = PInvoke.CreateWindowEx(extras, new PCWSTR(className), new PCWSTR(title), style,
                0, 0, 0, 0, default, default, instance, null);
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
        if (!foreground.IsNull && foreground != PInvoke.GetShellWindow() && foreground != PInvoke.GetDesktopWindow())
            thread = PInvoke.GetWindowThreadProcessId(foreground, &process);
        if (process == (uint)Environment.ProcessId)
            process = thread = 0;
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
            Desktop.Log(error.Message);
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
                if (app.HandlePopup(active, message, wParam, lParam))
                    return default;
            }
        }
        catch (Exception error)
        {
            Desktop.Log(error.Message);
            current?.DestroyPopup();
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private bool HandleMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == taskbarCreated && taskbarCreated != 0)
        {
            icon.Recover();
            RefreshDesktop();
            return true;
        }

        switch (message)
        {
            case TrayMessage:
                HandleTray(wParam, lParam);
                return true;
            case ResultMessage:
                CompletePoll();
                return true;
            case ForegroundMessage:
                foregroundPosted = false;
                SetForegroundTracking();
                ReadGameMode();
                // 再验证一次，给 Explorer 和 DWM 留出更新全屏状态的时间。
                ArmTimer(GameSettleTimer, 750);
                ProcessWork();
                return true;
            case MoveMessage:
                locationPosted = false;
                ArmTimer(GameSettleTimer, 350);
                return true;
            case 0x0113: // WM_TIMER
                PInvoke.KillTimer(host, wParam.Value);
                if (wParam.Value == UpdateCleanupTimer)
                {
                    CleanupUpdates();
                    return true;
                }
                if (wParam.Value == CloseTimer && popup?.Closing == true)
                    DestroyPopup();
                ProcessWork();
                return true;
            case 0x007E or 0x02E0: // WM_DISPLAYCHANGE / WM_DPICHANGED
                RefreshDesktop();
                return true;
            case 0x001A or 0x031A or 0x031E or 0x0320:
                // 系统设置、主题、DWM 合成和强调色变化。
                RefreshDesktop(resetPopup: true);
                return true;
            case 0x001E: // WM_TIMECHANGE
                ProcessWork();
                return true;
            case 0x0002: // WM_DESTROY
                PInvoke.PostQuitMessage(0);
                return true;
            case 0x0010: // WM_CLOSE，安装器可请求托盘正常退出。
                exitRequested = true;
                if (menuOpen)
                    PInvoke.EndMenu();
                updateDialog?.Close();
                if (updateDialog is null)
                    PInvoke.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    private void RefreshDesktop(bool resetPopup = false)
    {
        ReadGameMode();
        darkTaskbar = Desktop.DarkTaskbar;
        RefreshIcon();
        if (popup is { } active)
        {
            active.SetAnchor(Anchor(null));
            RenderPopup(reset: resetPopup);
        }
        ScheduleWork(DateTimeOffset.UtcNow);
    }

    private RECT Anchor(Point? preferred) => icon.TryGetRect(out var bounds) ? bounds : Desktop.TaskbarAnchor(preferred);

    private void RefreshIcon()
    {
        bool hasLocation = icon.TryGetRect(out var bounds);
        if (!hasLocation)
            bounds = Desktop.TaskbarAnchor();
        var appearance = Presentation.Icon(model.OrderedDevices().FirstOrDefault(), darkTaskbar);
        var tip = game.BlockPopups ? "" : Presentation.Tooltip(model);
        int size = IconSize(bounds);
        icon.Update(appearance, size, tip);
        // 首次注册前 Explorer 没有图标位置。注册后用实际位置校正一次 DPI。
        if (!hasLocation && icon.TryGetRect(out var registeredBounds))
            icon.Update(appearance, IconSize(registeredBounds), tip);
    }

    private static int IconSize(RECT bounds)
    {
        uint dpi = Desktop.Dpi(PInvoke.MonitorFromRect(in bounds, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));
        return Math.Clamp(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi), 8, 256);
    }

    private void ReadGameMode()
    {
        var next = Desktop.GameMode(PInvoke.GetForegroundWindow(), model.Settings.GameSilent);
        bool changed = next.BlockPopups != game.BlockPopups;
        bool enteredGame = next.Game && !game.Game;
        game = next;
        if (enteredGame && schedule.Running && !startedInGame)
            cancelWork = cancellation?.CancelAsync() ?? Task.CompletedTask;

        if (game.BlockPopups)
        {
            if (menuOpen)
                PInvoke.EndMenu();
            if (popup is not null)
                DestroyPopup();
        }
        if (changed)
            RefreshIcon();
    }

    private void ProcessWork()
    {
        ReadGameMode();
        var now = DateTimeOffset.UtcNow;
        if (schedule.Due(model.Settings, now, game.Game) <= now)
            StartPoll(now);
        if (textDue <= now)
            RenderPopup();

        if (!game.BlockPopups && !menuOpen && updateDialog is null)
        {
            if (pendingUi is { } waiting)
            {
                pendingUi = null;
                ExecuteTrayAction(waiting.Action, waiting.Anchor);
            }
            if (popup is null && !schedule.Running &&
                (nextPopupAttempt is null || nextPopupAttempt <= now) && model.ReadyReminder(now, false) is { } ready)
                ShowPopup(ready.Device.Id, null);
        }
        ScheduleWork(now);
    }

    private void ScheduleWork(DateTimeOffset now)
    {
        DateTimeOffset? due = schedule.Due(model.Settings, now, game.Game);
        if (popup is null && !menuOpen && updateDialog is null && !schedule.Running && model.NextReminder is { } reminder)
        {
            // 有待办提醒时，也处理没有伴随前台事件的系统状态变化。
            due = Earlier(due, game.BlockPopups
                ? Earlier(reminder > now ? reminder : null, now + GameRecheck)
                : nextPopupAttempt is { } retry && retry > reminder ? retry : reminder);
        }
        if (game.BlockPopups && pendingUi is not null)
            due = Earlier(due, now + GameRecheck);
        due = Earlier(due, textDue);
        if (due is { } time)
            ArmTimer(WorkTimer, (uint)Math.Clamp(Math.Ceiling((time - now).TotalMilliseconds), 10, int.MaxValue));
        else
            PInvoke.KillTimer(host, WorkTimer);
    }

    private static DateTimeOffset? Earlier(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null || b < a ? b : a;

    private unsafe void ArmTimer(nuint id, uint milliseconds)
    {
        if (!host.IsNull)
            PInvoke.SetTimer(host, id, milliseconds, null);
    }

    private void StartPoll(DateTimeOffset now)
    {
        schedule.Start(now, game.Game);
        startedInGame = game.Game;
        var source = new CancellationTokenSource();
        cancellation = source;
        cancelWork = Task.CompletedTask;
        var token = source.Token;
        var destination = host;
        _ = Task.Run(async () =>
        {
            PollResult? result;
            try
            {
                result = await poll.RunAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result = null;
            }
            catch (Exception error)
            {
                Desktop.Log(error.Message);
                result = new([], true);
            }
            results.Enqueue(result);
            PInvoke.PostMessage(destination, ResultMessage, default, default);
        });
        RenderPopup();
    }

    private void CompletePoll()
    {
        if (!results.TryDequeue(out var result))
            return;
        bool cancelled = cancellation?.IsCancellationRequested == true;
        if (cancellation is { } completed)
            _ = DisposeCancellation(completed, cancelWork);
        cancellation = null;
        schedule.Complete();
        if (cancelled && !startedInGame)
            schedule.Request();

        if (result is not null && !cancelled)
        {
            if (model.ApplyPoll(result.Readings, result.ScanFailed, DateTimeOffset.UtcNow))
                SaveState();
            RefreshIcon();
        }
        RenderPopup();
        ProcessWork();
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
        var snapshot = model.Snapshot();
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
                Desktop.Log(error.Message);
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
            Desktop.Log(error.Message);
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
            Desktop.Log(error.Message);
            return false;
        }
    }

    private void ChangeSettings(TraySettings settings, bool persist)
    {
        bool autostartChanged = settings.StartWithWindows != model.Settings.StartWithWindows;
        if (autostartChanged && !UpdateAutostart(settings.StartWithWindows))
            return;
        if (persist && !SaveSettings(settings))
        {
            if (autostartChanged)
                UpdateAutostart(model.Settings.StartWithWindows);
            return;
        }

        model.ApplySettings(settings, DateTimeOffset.UtcNow);
        SaveState();
        RefreshIcon();
        RenderPopup();
        ProcessWork();
    }

    private void HandleTray(WPARAM wParam, LPARAM lParam)
    {
        uint kind = (ushort)lParam.Value;
        if ((ushort)(lParam.Value >> 16) != 1)
            return;
        var point = PopupWindow.MessagePoint(unchecked((nint)wParam.Value));
        switch (kind)
        {
            // NOTIFYICON_VERSION_4 下一次左键单击会先后收到 WM_LBUTTONUP 和 NIN_SELECT。
            // 两者都处理会让面板刚打开就被第二条消息切换关闭（0.1.0 的"点开即闪退"），因此只认 NIN_SELECT / NIN_KEYSELECT。
            case 0x0400 or 0x0401: // NIN_SELECT、NIN_KEYSELECT
                long time = Environment.TickCount64;
                if (time - lastTraySelect < 300 || time - lostFocusByTray < 300)
                    return;
                lastTraySelect = time;
                RequestTrayAction(TrayAction.Panel, point);
                break;
            case 0x007B: // WM_CONTEXTMENU
                RequestTrayAction(TrayAction.Menu, point);
                break;
        }
    }

    private void RequestTrayAction(TrayAction action, Point point)
    {
        ReadGameMode();
        if (game.BlockPopups)
            pendingUi = (action, point);
        else
            ExecuteTrayAction(action, point);
        ScheduleWork(DateTimeOffset.UtcNow);
    }

    private void ExecuteTrayAction(TrayAction action, Point point)
    {
        if (menuOpen || updateDialog is not null)
            return;
        if (action == TrayAction.Menu)
        {
            DestroyPopup();
            ShowMenu(point);
        }
        else if (popup is { IsNotice: false })
            ClosePopup();
        else
        {
            DestroyPopup();
            ShowPopup(null, point);
        }
    }

    private void ShowPopup(string? noticeId, Point? point)
    {
        bool notice = noticeId is not null;
        try
        {
            var extra = WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP;
            if (notice)
                extra |= WS_EX_NOACTIVATE;
            var anchor = Anchor(point);
            HWND window = MakeWindow(PopupClass, extra, WS_POPUP | WS_THICKFRAME);
            popup = new(window, model, noticeId, anchor);
            RenderPopup();
            ReadGameMode();
            if (popup is { } active)
            {
                active.Show();
                if (noticeId is not null)
                {
                    model.SetReminder(noticeId, null);
                    SaveState();
                }
            }
        }
        catch (Exception error)
        {
            DestroyPopup();
            Desktop.Log(error.Message);
            if (notice)
                nextPopupAttempt = DateTimeOffset.UtcNow.AddSeconds(60);
        }
    }

    private void RenderPopup(bool reset = false)
    {
        if (popup is not { Closing: false } active)
            return;
        if (active.NoticeId is { } id && !model.IsLow(id))
        {
            DestroyPopup();
            return;
        }
        var now = DateTimeOffset.UtcNow;
        try
        {
            active.Render(now, schedule.Running, reset);
            textDue = active.IsNotice ? null : now.AddMinutes(1);
        }
        catch (Exception error)
        {
            DestroyPopup();
            Desktop.Log(error.Message);
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
                if (active.TryReclaimFocus())
                    return true;
                if (icon.TryGetRect(out var bounds) && PInvoke.GetCursorPos(out var cursor) &&
                    cursor.X >= bounds.left && cursor.X < bounds.right && cursor.Y >= bounds.top && cursor.Y < bounds.bottom)
                    lostFocusByTray = Environment.TickCount64;
                ClosePopup();
                return true;
            case 0x0010: // WM_CLOSE
                PopupCommand(active, PopupAction.Close);
                return true;
            case 0x8010: // 面板刚显示即被收回前台时，重新激活一次
                PInvoke.SetForegroundWindow(active.Handle);
                return true;
            case 0x0200: active.PointerMove(lParam); return true;
            case 0x02A3: active.PointerLeave(); return true;
            case 0x0201 or 0x0202:
                PopupCommand(active, active.PointerButton(lParam, message == 0x0201));
                return true;
            case 0x020A: active.Wheel(unchecked((short)(wParam.Value >> 16))); return true;
            case 0x0100: PopupCommand(active, active.Key(wParam.Value)); return true;
            case 0x02E0: // WM_DPICHANGED，延后到当前移动消息完成。
                PInvoke.PostMessage(host, 0x02E0, default, default);
                return true;
            default:
                return false;
        }
    }

    private void PopupCommand(PopupWindow active, PopupAction action)
    {
        if (action == PopupAction.None)
            return;
        if (active.NoticeId is { } id)
        {
            model.SetReminder(id, action == PopupAction.Later ? DateTimeOffset.UtcNow.AddHours(1) : null);
            nextPopupAttempt = null;
            SaveState();
            DestroyPopup();
            ProcessWork();
            return;
        }

        switch (action)
        {
            case PopupAction.Close:
                ClosePopup();
                break;
            case PopupAction.Refresh:
                active.Spin();
                schedule.Request();
                ProcessWork();
                break;
            case PopupAction.Settings or PopupAction.Manage:
                PInvoke.GetCursorPos(out var point);
                RequestTrayAction(TrayAction.Menu, point);
                break;
        }
    }

    private void ClosePopup()
    {
        if (popup is null)
            return;
        if (popup.Closing)
            return;
        textDue = null;
        try
        {
            if (popup.BeginClose())
            {
                ArmTimer(CloseTimer, 120);
                return;
            }
        }
        catch (Exception error)
        {
            Desktop.Log(error.Message);
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
        previous?.Dispose();
    }

    private unsafe void ShowMenu(Point point)
    {
        ReadGameMode();
        if (game.BlockPopups)
        {
            pendingUi = (TrayAction.Menu, point);
            return;
        }

        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull)
            return;
        Dictionary<int, Action> commands = [];
        menuOpen = true;
        try
        {
            if (settingsError is { } error)
                Append(menu, error, flags: MENU_ITEM_FLAGS.MF_GRAYED);
            var settings = model.Settings;
            var devices = model.OrderedDevices();
            var primary = Submenu(menu, "主设备");
            Add(primary, "自动选择", () => ChangeSettings(settings with { PrimaryDeviceId = null }, true),
                settings.PrimaryDeviceId is null);
            foreach (var device in devices)
            {
                string id = device.Device.Id;
                string name = Presentation.Shorten(model.Name(device.Device), 60);
                Add(primary, $"{name}  {Presentation.Value(device)}",
                    () => ChangeSettings(settings with { PrimaryDeviceId = id }, true),
                    string.Equals(settings.PrimaryDeviceId, id, StringComparison.OrdinalIgnoreCase));
            }

            Add(menu, "立即刷新", () => { schedule.Request(); ProcessWork(); });
            Add(menu, "开机自启", () => ChangeSettings(settings with { StartWithWindows = !settings.StartWithWindows }, true),
                settings.StartWithWindows);
            Append(menu, "", flags: MENU_ITEM_FLAGS.MF_SEPARATOR);
            Add(menu, "检查更新…", ShowUpdates);
            Add(menu, "诊断信息…", ShowDiagnostics);
            Add(menu, "编辑配置文件", OpenConfig);
            Add(menu, "重新加载配置", ReloadSettings);
            Append(menu, "", flags: MENU_ITEM_FLAGS.MF_SEPARATOR);
            Add(menu, "退出", () => PInvoke.PostQuitMessage(0));

            if (point is { X: -1, Y: -1 })
            {
                var bounds = Anchor(null);
                point = new(bounds.left, bounds.top);
            }

            PInvoke.SetForegroundWindow(host);
            int selected = PInvoke.TrackPopupMenuEx(menu, 0x0100 | 0x0002, point.X, point.Y, host, (TPMPARAMS*)null).Value;
            PInvoke.PostMessage(host, 0, default, default);
            menuOpen = false;
            if (!exitRequested && commands.TryGetValue(selected, out var invoke))
                invoke();
        }
        finally
        {
            menuOpen = false;
            PInvoke.DestroyMenu(menu);
            ArmTimer(WorkTimer, 50);
        }

        void Add(HMENU target, string label, Action run, bool check = false)
        {
            int id = commands.Count + 1;
            commands.Add(id, run);
            Append(target, label, (nuint)id, check ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
        }
    }

    private static unsafe HMENU Submenu(HMENU parent, string title)
    {
        var child = PInvoke.CreatePopupMenu();
        if (child.IsNull)
            throw new InvalidOperationException("无法创建托盘子菜单");
        try
        {
            Append(parent, title, (nuint)child.Value, MENU_ITEM_FLAGS.MF_POPUP);
            return child;
        }
        catch
        {
            PInvoke.DestroyMenu(child);
            throw;
        }
    }

    private static unsafe void Append(HMENU target, string title, nuint id = 0, MENU_ITEM_FLAGS flags = 0)
    {
        fixed (char* label = title.Replace("&", "&&", StringComparison.Ordinal))
        {
            if (!PInvoke.AppendMenu(target, flags, id, new PCWSTR(label)))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private void ShowUpdates()
    {
        var dialog = new UpdateDialog(host, files.Updates, InGameForInstall);
        updateDialog = dialog;
        try
        {
            if (dialog.Run())
                exitRequested = true;
        }
        finally
        {
            updateDialog = null;
            if (exitRequested)
                PInvoke.PostQuitMessage(0);
        }
    }

    private bool InGameForInstall() =>
        Desktop.GameMode(PInvoke.GetForegroundWindow(), model.Settings.GameSilent).Game ||
        Desktop.GameMode(foreground, model.Settings.GameSilent).Game;

    private void CleanupUpdates()
    {
        if (updateDialog is not null)
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
                Desktop.Log($"更新目录清理失败：{error.Message}");
        }
    }

    private void ReloadSettings()
    {
        if (!files.TryLoadSettings(out var settings))
        {
            settingsError = "配置读取失败";
            return;
        }
        settingsError = null;
        ChangeSettings(settings, false);
    }

    /// <summary>
    /// 用户点击后才生成诊断报告：后台枚举与查询，写入本地文本文件，再用记事本完整打开，
    /// 由用户检查后自行复制发送。诊断流程只在本机执行，内容说明见 PRIVACY.md。
    /// </summary>
    private void ShowDiagnostics()
    {
        string path = files.DiagnosticsPath;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                string report = await Jige.Core.Diagnostics.DiagnosticReport
                    .GenerateWindowsAsync(timeout.Token).ConfigureAwait(false);
                const string notice =
                    "# 这份诊断报告只保存在你的电脑上，几格不会上传。\r\n" +
                    "# 请先完整检查内容，确认无误后再自行复制，发到 GitHub Issue 或发给开发者。\r\n" +
                    "# 收集了什么、没收集什么：https://github.com/deepfry666/jige-battery/blob/main/PRIVACY.md\r\n\r\n";
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, notice + report, timeout.Token).ConfigureAwait(false);
                using var editor = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    ArgumentList = { path },
                    UseShellExecute = false,
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or OperationCanceledException or System.ComponentModel.Win32Exception)
            {
                Desktop.Log($"诊断信息生成失败：{error.Message}");
            }
        });
    }

    private unsafe void OpenConfig()
    {
        if (!File.Exists(files.ConfigPath) && !SaveSettings(model.Settings))
            return;
        fixed (char* notepad = "notepad.exe")
        fixed (char* arguments = $"\"{files.ConfigPath}\"")
        {
            var result = PInvoke.ShellExecute(host, default, new PCWSTR(notepad), new PCWSTR(arguments),
                default, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
            if ((nint)result.Value <= 32)
                Desktop.Log("无法打开配置文件");
        }
    }

    public void Dispose()
    {
        if (!foregroundHook.IsNull)
            PInvoke.UnhookWinEvent(foregroundHook);
        if (!locationHook.IsNull)
            PInvoke.UnhookWinEvent(locationHook);
        DestroyPopup();
        icon.Dispose();
        var window = host;
        host = default;
        PInvoke.DestroyWindow(window);
        if (cancellation is { } active)
        {
            _ = DisposeCancellation(active, active.IsCancellationRequested ? cancelWork : active.CancelAsync());
            cancellation = null;
        }
        stateWrite.GetAwaiter().GetResult();
    }
}

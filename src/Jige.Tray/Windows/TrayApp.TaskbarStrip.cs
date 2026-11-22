using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE;

namespace Jige.Tray.Windows;

internal sealed partial class TrayApp
{
    private const string StripClass = "Jige.Tray.TaskbarStrip";
    private const uint StripGeometryMessage = 0x8009, StripAppBarMessage = 0x800A;
    private const uint StripBackdropSettleMs = 450, StripBackdropPollMs = 3_000;
    private TaskbarStripAutomation? stripAutomation;
    private TaskbarStripWindow? stripWindow;
    private TaskbarStripGeometry stripGeometry;
    private TaskbarStripDisplay stripDisplay = new(TaskbarStripReason.HiddenTaskbar, default);
    private TaskbarBackdrop taskbarBackdrop = TaskbarBackdrop.FromDark(true);
    private TaskbarBackdrop lastFallbackBackdrop = TaskbarBackdrop.FromDark(true);
    private TaskbarStripPalette stripPalette = TaskbarStripPalette.Create(true);
    private HWND stripOwner;
    private bool stripFullscreen, openedFromStrip, appBarSubscribed, stripWindowUnavailable,
        stripGeometryPending, stripFallbackLogged, stripBackdropSampled, stripBackdropDeferSample;
    private long stripGeometryPendingSinceMs, stripBackdropDueMs;
    private int stripAutomationFailures;
    private long stripAutomationNextRetryMs;
    private bool StripGeometryPending => model.Settings.DisplayLocation == TrayDisplayLocation.Taskbar &&
        stripGeometryPending;
    private bool StripGeometryTimedOut => StripGeometryPending &&
        Environment.TickCount64 - stripGeometryPendingSinceMs >= 3_000;
    private string? DisplayLocationFallbackReason =>
        model.Settings.DisplayLocation == TrayDisplayLocation.Taskbar && stripDisplay.IsFallback &&
        (!StripGeometryPending || StripGeometryTimedOut)
            ? stripDisplay.Description : null;

    private TaskbarStripAutomation? EnsureStripAutomation(bool forceRecreate = false)
    {
        if (stripAutomation is { Alive: true } && !forceRecreate)
            return stripAutomation;

        long now = Environment.TickCount64;
        if (stripAutomation is not null)
        {
            stripAutomation.Dispose();
            stripAutomation = null;
            if (!forceRecreate)
            {
                stripAutomationFailures = Math.Min(3, stripAutomationFailures + 1);
                uint delay = stripAutomationFailures switch { 1 => 2_000u, 2 => 5_000u, _ => 15_000u };
                stripAutomationNextRetryMs = now + delay;
            }
        }
        if (!forceRecreate && now < stripAutomationNextRetryMs)
        {
            ArmTimer(StripRetryTimer, (uint)Math.Clamp(stripAutomationNextRetryMs - now, 500, 15_000));
            return null;
        }

        stripAutomation = new(host, StripGeometryMessage);
        return stripAutomation;
    }

    private void RefreshStripGeometry()
    {
        if (model.Settings.DisplayLocation == TrayDisplayLocation.TrayIcons)
        {
            StopStripTracking();
            return;
        }

        bool retryWindow = stripWindowUnavailable;
        stripWindowUnavailable = false;
        HWND owner = TaskbarStripShell.Find();
        bool ownerChanged = stripOwner != owner;
        if (ownerChanged)
        {
            stripOwner = owner;
            stripGeometry = default;
            stripBackdropSampled = false;
            DestroyStripWindow();
        }
        if (owner.IsNull)
            stripGeometryPending = false;
        else if (ownerChanged)
        {
            stripGeometryPending = true;
            stripGeometryPendingSinceMs = Environment.TickCount64;
        }
        if (!owner.IsNull && !appBarSubscribed)
            appBarSubscribed = TaskbarStripShell.Subscribe(host, StripAppBarMessage);
        if (stripAutomation is not null || !owner.IsNull)
            EnsureStripAutomation()?.Request(owner, reset: retryWindow);
    }

    private void RecoverStrip()
    {
        // Explorer owns the appbar registrations and can invalidate the overlay HWND.
        // Recreate both against the new Shell_TrayWnd, even if a handle was reused.
        appBarSubscribed = false;
        stripOwner = default;
        stripGeometry = default;
        stripGeometryPending = false;
        stripBackdropSampled = false;
        stripBackdropDeferSample = false;
        StopStripBackdropTimer();
        DestroyStripWindow();
        EnsureStripAutomation()?.Request(default, reset: true);
    }

    private void HandleStripAppBar(nuint notification)
    {
        if (model.Settings.DisplayLocation != TrayDisplayLocation.Taskbar)
            return;
        if (notification == TaskbarStripShell.FullscreenChanged)
        {
            ReadGameMode();
            RefreshStripGeometry();
            return;
        }
        if (notification is not (TaskbarStripShell.StateChanged or TaskbarStripShell.PositionChanged))
            return;

        // An owned popup is not moved by its owner. Hide it at the first Shell
        // notification that its old screen rectangle no longer fits the live bar.
        if (stripWindow?.VisibleBounds is { } bounds &&
            (!PInvoke.IsWindowVisible(stripOwner) || !PInvoke.GetWindowRect(stripOwner, out var live) ||
             bounds.Top < live.top || bounds.Bottom > live.bottom ||
             bounds.Left < live.left || bounds.Right > live.right))
        {
            stripGeometry = stripGeometry with { Visible = false };
            RefreshIcon();
        }
        RefreshStripGeometry();
    }

    private void CompleteStripGeometry()
    {
        if (stripAutomation?.Take() is not { } geometry || model.Settings.DisplayLocation != TrayDisplayLocation.Taskbar)
            return;
        if (geometry.Owner.IsNull)
            return;
        if (geometry.Owner != stripOwner)
        {
            if (!stripOwner.IsNull)
                EnsureStripAutomation()?.Request(stripOwner, reset: true);
            return;
        }
        stripAutomationFailures = 0;
        bool wasPending = stripGeometryPending;
        stripGeometryPending = false;
        var previous = stripWindow?.VisibleBounds;
        var previousReason = stripDisplay.Reason;
        stripGeometry = geometry;
        RefreshIcon();
        if (openedFromStrip && previous != stripWindow?.VisibleBounds)
            ReanchorStripPopup();
        else if (wasPending || stripDisplay.Reason != previousReason)
            RenderPopup();
        if (hoverCard is not null && hoverIconId is null)
            RenderHoverCard();
    }

    private void RefreshStrip()
    {
        var preference = model.Settings.DisplayLocation;
        if (preference == TrayDisplayLocation.TrayIcons)
        {
            ApplyStripDisplay(TaskbarStripDisplay.Resolve(preference, default, false, false));
            return;
        }
        if (StripGeometryPending)
        {
            ApplyStripDisplay(new(TaskbarStripReason.MissingTaskbar, default));
            return;
        }

        var devices = TaskbarStripDevice.Build(model);
        var rendered = TaskbarStripDevice.WithPlaceholder(devices);
        var layout = TaskbarStripLayout.Build(rendered);
        var next = TaskbarStripDisplay.Resolve(preference, stripGeometry.Place(layout.Width),
            devices.Length > 0, stripFullscreen);
        if (next.ShowStrip && stripWindowUnavailable)
            next = new(TaskbarStripReason.WindowUnavailable, default);
        if (next.ShowStrip)
        {
            try
            {
                if (stripWindow is null || stripWindow.Owner != stripGeometry.Owner || !PInvoke.IsWindow(stripWindow.Handle))
                {
                    DestroyStripWindow();
                    var extra = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP;
                    var window = MakeWindow(StripClass, extra, WS_POPUP, stripGeometry.Owner);
                    stripWindow = new(window, stripGeometry.Owner);
                }
                int scale = TaskbarStripLayout.PixelScale(stripGeometry.Dpi);
                if (!stripBackdropSampled && !stripBackdropDeferSample && !game.Game && !stripFullscreen &&
                    Desktop.TrySampleTaskbarBackdrop(stripGeometry.Owner, stripWindow.Handle, stripGeometry.Bar,
                        next.Bounds, scale, out var sampled))
                {
                    taskbarBackdrop = sampled;
                    stripBackdropSampled = true;
                    stripPalette = ResolveStripPalette(sampled);
                    darkTaskbar = sampled.PrefersDarkPalette();
                }
                stripWindow.Update(rendered, layout, next.Bounds, stripPalette, scale);
            }
            catch (Exception error) when (error is ExternalException or InvalidOperationException or UnauthorizedAccessException)
            {
                DestroyStripWindow();
                stripWindowUnavailable = true;
                next = new(TaskbarStripReason.WindowUnavailable, default);
            }
        }
        ApplyStripDisplay(next);
    }

    private void ReanchorStripPopup()
    {
        if (popup is { IsNotice: false, Editing: false, Closing: false } panel)
        {
            panel.SetAnchor(Anchor(null));
            RenderPopup();
        }
        if (menu is { Closing: false } activeMenu)
        {
            try { activeMenu.Refresh(Anchor(menuPoint)); }
            catch (Exception error)
            {
                DestroyMenu();
                Desktop.Log("刷新托盘菜单失败", error);
            }
        }
    }

    private void ApplyStripDisplay(TaskbarStripDisplay next)
    {
        var previous = stripDisplay;
        stripDisplay = next;
        if (!next.ShowStrip)
        {
            StopStripBackdropTimer();
            stripWindow?.Hide();
            if (hoverCard is not null && hoverIconId is null)
                DestroyHoverCard();
        }
        else if (!game.Game && !stripFullscreen)
        {
            bool movedOrResized = !previous.ShowStrip || previous.Bounds != next.Bounds;
            ScheduleStripBackdropSample(movedOrResized || !stripBackdropSampled
                ? StripBackdropSettleMs : StripBackdropPollMs);
        }

        if (next.IsFallback && !StripGeometryPending)
        {
            if (!stripFallbackLogged || next.Reason != previous.Reason)
            {
                stripFallbackLogged = true;
                Desktop.Log($"任务栏直读条临时回退：{next.Description}");
            }
        }
        else if (next.ShowStrip && stripFallbackLogged)
        {
            stripFallbackLogged = false;
            Desktop.Log("任务栏直读条已恢复");
        }
        else if (next.Reason == TaskbarStripReason.UserSelectedTray)
        {
            stripFallbackLogged = false;
        }
    }

    private void DestroyStripWindow()
    {
        var previous = stripWindow;
        stripWindow = null;
        previous?.Dispose();
    }

    private void StopStripTracking()
    {
        if (!host.IsNull)
            PInvoke.KillTimer(host, StripRetryTimer);
        StopStripBackdropTimer();
        stripBackdropSampled = false;
        stripBackdropDeferSample = false;
        DestroyStripWindow();
        stripAutomation?.Request(default);
        stripGeometry = default;
        stripOwner = default;
        stripGeometryPending = false;
        if (appBarSubscribed)
        {
            appBarSubscribed = false;
            TaskbarStripShell.Unsubscribe(host);
        }
    }

    private void DisposeStrip()
    {
        StopStripTracking();
        stripAutomation?.Dispose();
        stripAutomation = null;
    }

    private TaskbarStripPalette ResolveStripPalette(TaskbarBackdrop backdrop, TaskbarStripPalette? previous = null) =>
        TaskbarStripPalette.Create(backdrop, model.Settings.TaskbarStyle, model.Settings.TaskbarColor, previous);

    private void InitializeTaskbarBackdrop()
    {
        var fallback = Desktop.ReadTaskbarBackdropFallback();
        lastFallbackBackdrop = fallback;
        taskbarBackdrop = fallback;
        stripPalette = ResolveStripPalette(fallback);
        darkTaskbar = fallback.PrefersDarkPalette();
    }

    private void ApplyStripAppearanceSettings()
    {
        stripPalette = ResolveStripPalette(taskbarBackdrop);
    }

    private void RefreshTaskbarTheme(bool redraw = true)
    {
        var fallback = Desktop.ReadTaskbarBackdropFallback();
        bool fallbackChanged = fallback != lastFallbackBackdrop;
        lastFallbackBackdrop = fallback;
        if (fallbackChanged)
        {
            taskbarBackdrop = fallback;
            stripBackdropSampled = false;
            // 系统切换深浅色或强调色时先用注册表回退值立即重绘，等 Explorer/DWM 过渡动画稳定后再采样像素。
            stripBackdropDeferSample = true;
            var nextPalette = ResolveStripPalette(fallback);
            bool nextDark = fallback.PrefersDarkPalette();
            bool paletteChanged = nextPalette != stripPalette;
            bool darkChanged = nextDark != darkTaskbar;
            stripPalette = nextPalette;
            darkTaskbar = nextDark;
            if (redraw && (paletteChanged || darkChanged))
                RefreshIcon();
        }
        if (stripDisplay.ShowStrip && !game.Game && !stripFullscreen)
            ScheduleStripBackdropSample(StripBackdropSettleMs);
    }

    private void NotifyStripBackdropTransition()
    {
        if (stripDisplay.ShowStrip && !game.Game && !stripFullscreen)
            ScheduleStripBackdropSample(StripBackdropSettleMs);
    }

    private void InvalidateStripBackdropSample()
    {
        stripBackdropSampled = false;
        stripBackdropDeferSample = false;
    }

    private void ScheduleStripBackdropSample(uint delayMs)
    {
        if (host.IsNull || !stripDisplay.ShowStrip || game.Game || stripFullscreen)
            return;
        long due = Environment.TickCount64 + delayMs;
        if (stripBackdropDueMs == 0 || due < stripBackdropDueMs)
        {
            stripBackdropDueMs = due;
            ArmTimer(StripBackdropTimer, delayMs);
        }
    }

    private void StopStripBackdropTimer()
    {
        stripBackdropDueMs = 0;
        if (!host.IsNull)
            PInvoke.KillTimer(host, StripBackdropTimer);
    }

    private void HandleStripBackdropTimer()
    {
        stripBackdropDueMs = 0;
        stripBackdropDeferSample = false;
        if (!stripDisplay.ShowStrip || game.Game || stripFullscreen ||
            stripWindow is not { VisibleBounds: { } bounds } window)
            return;

        int scale = TaskbarStripLayout.PixelScale(stripGeometry.Dpi);
        if (Desktop.TrySampleTaskbarBackdrop(stripGeometry.Owner, window.Handle, stripGeometry.Bar,
            bounds, scale, out var sampled))
        {
            taskbarBackdrop = sampled;
            var nextPalette = ResolveStripPalette(sampled, stripBackdropSampled ? stripPalette : null);
            stripBackdropSampled = true;
            if (nextPalette != stripPalette)
            {
                stripPalette = nextPalette;
                darkTaskbar = sampled.PrefersDarkPalette(darkTaskbar);
                RefreshStrip();
            }
        }
        ScheduleStripBackdropSample(StripBackdropPollMs);
    }

    private static RECT ToRect(Bounds bounds) =>
        RECT.FromXYWH(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT StripCallback(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case 0x0021: return (LRESULT)3; // WM_MOUSEACTIVATE / MA_NOACTIVATE
            case 0x0014: return (LRESULT)1; // WM_ERASEBKGND
            case 0x000F: // WM_PAINT; DirectComposition owns all visible pixels.
                PAINTSTRUCT paint;
                PInvoke.BeginPaint(window, &paint);
                PInvoke.EndPaint(window, &paint);
                return default;
        }
        if (current is { stripWindow: { } strip } app && strip.Handle == window)
        {
            try
            {
                if (app.HandleStripPointer(strip, message))
                    return default;
            }
            catch (Exception error)
            {
                Desktop.Log("处理直读条交互失败", error);
            }
        }
        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private bool HandleStripPointer(TaskbarStripWindow strip, uint message)
    {
        switch (message)
        {
            case 0x0200: // WM_MOUSEMOVE
                strip.PointerMove();
                return true;
            case 0x02A1: // WM_MOUSEHOVER
                if (strip.VisibleBounds is { } bounds)
                    ShowHoverCard(null, null, ToRect(bounds));
                return true;
            case 0x02A3: // WM_MOUSELEAVE
                strip.PointerLeave();
                CloseHoverCard(null);
                return true;
            case 0x0201 or 0x0202 or 0x0204 or 0x0205:
                bool right = message is 0x0204 or 0x0205;
                if (strip.PointerButton(right, message is 0x0201 or 0x0204) && PInvoke.GetCursorPos(out var cursor))
                    ClickStrip(right, cursor);
                return true;
            case 0x02E0: // WM_DPICHANGED; the proposed rect is for a standalone popup.
                PInvoke.PostMessage(host, message, default, default);
                return true;
            default:
                return false;
        }
    }

    private void ClickStrip(bool right, Point cursor)
    {
        DestroyHoverCard();
        if (!right)
        {
            long time = Environment.TickCount64;
            if (time - lastTraySelect < 300 || time - lostFocusByTray < 300)
                return;
            lastTraySelect = time;
        }
        openedFromStrip = true;
        clickedIconId = null;
        RequestTrayAction(right ? TrayAction.Menu : TrayAction.Panel, cursor);
    }
}

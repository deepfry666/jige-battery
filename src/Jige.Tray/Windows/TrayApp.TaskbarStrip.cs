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
    private TaskbarStripAutomation? stripAutomation;
    private TaskbarStripWindow? stripWindow;
    private TaskbarStripGeometry stripGeometry;
    private TaskbarStripDisplay stripDisplay = new(TaskbarStripReason.NoDevices, default);
    private HWND stripOwner;
    private bool stripFullscreen, openedFromStrip, appBarSubscribed, stripWindowUnavailable;

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
        if (stripOwner != owner)
        {
            stripOwner = owner;
            stripGeometry = default;
            DestroyStripWindow();
        }
        if (!owner.IsNull && !appBarSubscribed)
            appBarSubscribed = TaskbarStripShell.Subscribe(host, StripAppBarMessage);
        if (stripAutomation is not null || !owner.IsNull)
        {
            stripAutomation ??= new(host, StripGeometryMessage);
            stripAutomation.Request(owner, reset: retryWindow);
        }
    }

    private void RecoverStrip()
    {
        // Explorer owns the appbar registrations and can invalidate the overlay HWND.
        // Recreate both against the new Shell_TrayWnd, even if a handle was reused.
        appBarSubscribed = false;
        stripOwner = default;
        stripGeometry = default;
        DestroyStripWindow();
        stripAutomation?.Request(default, reset: true);
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
        if (!geometry.Owner.IsNull && geometry.Owner != stripOwner)
            return;
        var previous = stripWindow?.VisibleBounds;
        stripGeometry = geometry;
        RefreshIcon();
        if (openedFromStrip && previous != stripWindow?.VisibleBounds)
            ReanchorStripPopup();
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

        var devices = TaskbarStripDevice.Build(model);
        var layout = TaskbarStripLayout.Build(devices);
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
                stripWindow.Update(devices, layout, next.Bounds, darkTaskbar, TaskbarStripLayout.PixelScale(stripGeometry.Dpi));
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
        if (!next.ShowStrip)
        {
            stripWindow?.Hide();
            if (hoverCard is not null && hoverIconId is null)
                DestroyHoverCard();
        }

        if (next.Reason != stripDisplay.Reason)
        {
            if (next.Reason is TaskbarStripReason.MissingTaskbar or TaskbarStripReason.MissingNotificationArea or
                TaskbarStripReason.UnknownButtonBounds or TaskbarStripReason.UnsupportedEdge or
                TaskbarStripReason.NoRoom or TaskbarStripReason.WindowUnavailable)
                Desktop.Log($"任务栏直读条临时回退：{next.Description}");
            else if (next.ShowStrip && !stripDisplay.DefaultIconOnly && stripDisplay.Reason != TaskbarStripReason.UserSelectedTray)
                Desktop.Log("任务栏直读条已恢复");
        }
        stripDisplay = next;
    }

    private void DestroyStripWindow()
    {
        var previous = stripWindow;
        stripWindow = null;
        previous?.Dispose();
    }

    private void StopStripTracking()
    {
        DestroyStripWindow();
        stripAutomation?.Request(default);
        stripGeometry = default;
        stripOwner = default;
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

    private void RefreshTaskbarTheme()
    {
        bool dark = Desktop.DarkTaskbar;
        if (dark != darkTaskbar)
        {
            darkTaskbar = dark;
            RefreshIcon();
        }
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

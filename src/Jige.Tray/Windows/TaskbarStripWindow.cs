using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class TaskbarStripWindow(HWND handle, HWND owner) : IDisposable
{
    private Composition? composition;
    private Composition.Layer? layer;
    private TaskbarStripDevice[] previous = [];
    private TaskbarStripPalette? previousPalette;
    private int previousScale, layerWidth;
    private Bounds? visible;
    private bool tracking, leftDown, rightDown;

    public HWND Handle => handle;
    public HWND Owner => owner;
    public Bounds? VisibleBounds => visible;

    public bool Contains(Point point) => visible is { } r &&
        point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom;

    public void DisposeGraphics()
    {
        layer?.Dispose();
        layer = null;
        composition?.Dispose();
        composition = null;
        previous = [];
        previousScale = 0;
        previousPalette = null;
        layerWidth = 0;
    }

    public void Update(TaskbarStripDevice[] devices, TaskbarStripLayout layout, Bounds bounds,
        TaskbarStripPalette palette, int scale)
    {
        if (layer is null || previousScale != scale || previousPalette != palette ||
            !previous.AsSpan().SequenceEqual(devices))
        {
            RenderWithRecovery(devices, layout, bounds, palette, scale);
        }

        if (visible != bounds)
        {
            // Owner keeps the window above Shell_TrayWnd. Keep its z-order relative to
            // other top-level windows and never activate it when showing or moving it.
            if (!PInvoke.SetWindowPos(handle, default, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER | SWP_SHOWWINDOW))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            visible = bounds;
        }
    }

    private void RenderWithRecovery(TaskbarStripDevice[] devices, TaskbarStripLayout layout, Bounds bounds,
        TaskbarStripPalette palette, int scale)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                composition ??= new(handle, 1);
                if (layer is null || layerWidth != bounds.Width || previousScale != scale)
                {
                    layer?.Dispose();
                    layer = new(composition, composition.Root, 0, 0, bounds.Width, bounds.Height);
                    layerWidth = bounds.Width;
                }
                uint[] pixels = TaskbarStripPixels.Render(layout, palette, scale);
                layer.Draw(canvas => canvas.PixelBitmap(pixels, bounds.Width, bounds.Height));
                composition.Commit();
                previous = devices;
                previousPalette = palette;
                previousScale = scale;
                return;
            }
            catch (Exception error) when (attempt == 0 && Graphics.IsDeviceLost(error) && PInvoke.IsWindow(handle))
            {
                DisposeGraphics();
            }
        }
    }

    public void Hide()
    {
        if (visible is not null)
            PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_HIDE);
        visible = null;
        PointerLeave();
    }

    public unsafe void PointerMove()
    {
        if (tracking || visible is null)
            return;

        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE | TRACKMOUSEEVENT_FLAGS.TME_HOVER,
            dwHoverTime = uint.MaxValue, // HOVER_DEFAULT
            hwndTrack = handle
        };
        tracking = PInvoke.TrackMouseEvent(ref track);
    }

    public void PointerLeave() => tracking = leftDown = rightDown = false;

    public bool PointerButton(bool right, bool down)
    {
        ref bool pressed = ref (right ? ref rightDown : ref leftDown);
        bool click = !down && pressed && visible is not null;
        pressed = down;
        return click;
    }

    public void Dispose()
    {
        Hide();
        layer?.Dispose();
        composition?.Dispose();
        if (PInvoke.IsWindow(handle))
            PInvoke.DestroyWindow(handle);
    }
}

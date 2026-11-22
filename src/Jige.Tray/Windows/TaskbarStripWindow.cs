using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS;

namespace Jige.Tray.Windows;

/// <summary>
/// 任务栏电量直读条窗口：使用轻量 GDI 分层窗口（UpdateLayeredWindow）直接上传 CPU 渲染好的预乘 BGRA 像素，
/// 不常驻硬件 D3D11/D2D/DirectWrite 设备；显示或从全屏恢复时通过 HWND_TOPMOST 明确置于任务栏上方，绝不抢焦点。
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class TaskbarStripWindow(HWND handle, HWND owner) : IDisposable
{
    private static readonly HWND HwndTopmost = new(-1);

    private TaskbarStripDevice[] previous = [];
    private bool hasSurface;
    private TaskbarStripPalette? previousPalette;
    private int previousScale, surfaceWidth, surfaceHeight;
    private Bounds? visible;
    private bool leaveTracking, hoverArmed, leftDown, rightDown;

    public HWND Handle => handle;
    public HWND Owner => owner;
    public Bounds? VisibleBounds => visible;

    public bool Contains(Point point) => visible is { } r &&
        point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom;

    public void DisposeGraphics()
    {
        hasSurface = false;
        previous = [];
        previousScale = 0;
        previousPalette = null;
        surfaceWidth = 0;
        surfaceHeight = 0;
    }

    public void Update(TaskbarStripDevice[] devices, TaskbarStripLayout layout, Bounds bounds,
        TaskbarStripPalette palette, int scale, bool ensureTopmost = false)
    {
        bool wasHidden = visible is null;
        bool needsPaint = !hasSurface || surfaceWidth != bounds.Width || surfaceHeight != bounds.Height ||
            previousScale != scale || previousPalette != palette || !previous.AsSpan().SequenceEqual(devices);

        if (needsPaint)
        {
            uint[] pixels = TaskbarStripPixels.Render(layout, palette, scale);
            TaskbarStripPixels.FillLayeredHitAlpha(pixels);
            UploadLayeredSurface(pixels, bounds);
            hasSurface = true;
            surfaceWidth = bounds.Width;
            surfaceHeight = bounds.Height;
            previous = devices;
            previousPalette = palette;
            previousScale = scale;
        }

        if (wasHidden || ensureTopmost || visible != bounds)
        {
            bool raise = wasHidden || ensureTopmost;
            var insertAfter = raise ? HwndTopmost : default;
            var flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW | (raise ? 0 : SWP_NOZORDER);
            if (!PInvoke.SetWindowPos(handle, insertAfter, bounds.Left, bounds.Top, bounds.Width, bounds.Height, flags))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            visible = bounds;
        }
    }

    /// <summary>在不抢焦点、不改变尺寸位置的前提下，确保直读条位于任务栏之上。</summary>
    public void EnsureTopmost()
    {
        if (visible is null || !PInvoke.IsWindow(handle))
            return;
        PInvoke.SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void UploadLayeredSurface(ReadOnlySpan<uint> pixels, Bounds bounds)
    {
        int width = bounds.Width;
        int height = bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var memoryDc = PInvoke.CreateCompatibleDC(default);
        if (memoryDc.IsNull)
            throw new InvalidOperationException("无法创建直读条内存 DC");

        HBITMAP bitmap = default;
        HGDIOBJ previousObject = default;
        try
        {
            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32
                }
            };
            void* bits = null;
            bitmap = PInvoke.CreateDIBSection(memoryDc, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
            if (bitmap.IsNull || bits == null)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            var destination = new Span<uint>(bits, width * height);
            int count = Math.Min(pixels.Length, destination.Length);
            pixels[..count].CopyTo(destination);
            if (count < destination.Length)
                destination[count..].Fill(TaskbarStripPixels.LayeredHitPixel);

            previousObject = PInvoke.SelectObject(memoryDc, bitmap);
            if (previousObject.IsNull || previousObject.Value == (void*)-1)
            {
                previousObject = default;
                throw new InvalidOperationException("无法选入直读条位图");
            }
            var destinationPoint = new Point(bounds.Left, bounds.Top);
            var surfaceSize = new SIZE(width, height);
            var sourcePoint = new Point(0, 0);
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0, // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1 // AC_SRC_ALPHA
            };
            if (!PInvoke.UpdateLayeredWindow(handle, default, destinationPoint, surfaceSize,
                memoryDc, sourcePoint, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            if (!previousObject.IsNull)
                PInvoke.SelectObject(memoryDc, previousObject);
            if (!bitmap.IsNull)
                PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memoryDc);
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
        if (visible is null || (leaveTracking && hoverArmed))
            return;

        var flags = (leaveTracking ? default : TRACKMOUSEEVENT_FLAGS.TME_LEAVE) |
                    (hoverArmed ? default : TRACKMOUSEEVENT_FLAGS.TME_HOVER);
        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = flags,
            dwHoverTime = uint.MaxValue, // HOVER_DEFAULT
            hwndTrack = handle
        };
        if (PInvoke.TrackMouseEvent(ref track))
        {
            leaveTracking = true;
            hoverArmed = true;
        }
    }

    public void PointerHover() => hoverArmed = false;

    public void PointerLeave() => leaveTracking = hoverArmed = leftDown = rightDown = false;

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
        if (!handle.IsNull)
        {
            var window = handle;
            handle = default;
            if (PInvoke.IsWindow(window))
                PInvoke.DestroyWindow(window);
        }
    }
}

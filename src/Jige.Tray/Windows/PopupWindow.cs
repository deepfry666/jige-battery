using System.Drawing;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class PopupWindow(HWND hwnd, TrayModel model, string? noticeId, RECT anchor) : IDisposable
{
    private Composition? composition;
    private PopupScene? scene;
    private HMONITOR monitor;
    private int height;
    private Bounds? position;
    private float scale;
    private bool tracking;
    private bool animations;
    private PopupAction down;
    private RECT anchor = anchor;

    public HWND Handle { get; private set; } = hwnd;
    public string? NoticeId { get; } = noticeId;
    public bool IsNotice => NoticeId is not null;
    public bool Closing { get; private set; }

    public void Render(DateTimeOffset now, bool running, bool reset = false)
    {
        if (Closing)
            return;

        var screen = PInvoke.MonitorFromRect(in anchor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = Desktop.Monitor(screen);
        var work = new Bounds(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        var bounds = new Bounds(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        var icon = new Bounds(anchor.left, anchor.top, anchor.right, anchor.bottom);
        int deviceCount = model.OrderedDevices().Length;
        int minHeight = IsNotice ? PopupScene.NoticeHeight : PopupScene.PanelHeight(deviceCount > 0, deviceCount > 1 ? 1 : 0);
        float newScale = Math.Min(Desktop.Dpi(screen) / 96f,
            Math.Min((work.Width - 16f) / PopupScene.Width, (work.Height - 16f) / minHeight));
        if (newScale <= 0)
            throw new InvalidOperationException("找不到弹窗显示器的工作区");
        int availableHeight = (int)(work.Height / newScale) - 24;
        int rowCount = IsNotice ? 0 : Math.Min(Math.Max(0, deviceCount - 1),
            Math.Max(deviceCount > 1 ? 1 : 0, PopupScene.MaximumRows(availableHeight)));
        int newHeight = IsNotice ? PopupScene.NoticeHeight : PopupScene.PanelHeight(deviceCount > 0, rowCount);
        var nextPosition = PopupPlacement.Place(icon, bounds, work, Pixels(PopupScene.Width, newScale),
            Pixels(newHeight, newScale), Pixels(12, newScale), IsNotice);
        if (position != nextPosition)
        {
            if (!PInvoke.SetWindowPos(Handle, default, nextPosition.Left, nextPosition.Top, nextPosition.Width,
                nextPosition.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
                throw new InvalidOperationException("无法定位弹窗");
            position = nextPosition;
        }

        if (reset || scene is null || height != newHeight || monitor != screen || scale != newScale)
        {
            scene?.Dispose();
            scene = null;
            composition?.Dispose();
            composition = null;
            monitor = screen;
            height = newHeight;
            scale = newScale;
            animations = Desktop.Animations();
            bool dark = Desktop.DarkApps;
            bool acrylic = Desktop.Backdrop(Handle, dark);
            var graphics = new Composition(Handle, scale);
            composition = graphics;
            scene = new(graphics, Desktop.PanelPalette(dark), acrylic, animations, model, NoticeId, height, rowCount);
        }
        scene.Render(now, running);
    }

    private static int Pixels(float dip, float scale) =>
        (int)MathF.Round(dip * scale, MidpointRounding.AwayFromZero);

    public void Show()
    {
        scene?.Open();
        PInvoke.ShowWindow(Handle, IsNotice ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_SHOW);
        if (!IsNotice)
            PInvoke.SetForegroundWindow(Handle);
    }

    public bool BeginClose()
    {
        if (Closing)
            return true;
        Closing = true;
        if (!animations || IsNotice)
            return false;
        scene?.Close();
        return true;
    }

    public void SetAnchor(RECT value)
    {
        anchor = value;
    }

    public void Spin() => scene?.Spin();

    public void PointerMove(LPARAM lParam)
    {
        var (x, y) = ClientPoint(lParam);
        scene?.Pointer(x, y);
        if (tracking)
            return;
        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
            hwndTrack = Handle
        };
        tracking = PInvoke.TrackMouseEvent(ref track);
    }

    public void PointerLeave()
    {
        tracking = false;
        down = PopupAction.None;
        scene?.Pointer(-1, -1);
    }

    public PopupAction PointerButton(LPARAM lParam, bool pressed)
    {
        var (x, y) = ClientPoint(lParam);
        var current = scene?.Hit(x, y) ?? PopupAction.None;
        scene?.Pointer(x, y, pressed);
        if (pressed)
        {
            down = current;
            return PopupAction.None;
        }
        var result = current == down ? current : PopupAction.None;
        down = PopupAction.None;
        return result;
    }

    public PopupAction Key(nuint key)
    {
        switch (key)
        {
            case 0x1B: return PopupAction.Close;
            case 0x09: scene?.Tab(PInvoke.GetKeyState(0x10) < 0); break;
            case 0x0D or 0x20: return scene?.Focused ?? PopupAction.None;
            case 0x26: scene?.Scroll(-1); break;
            case 0x28: scene?.Scroll(1); break;
        }
        return PopupAction.None;
    }

    public void Wheel(short delta)
    {
        if (delta != 0)
            scene?.Scroll(delta > 0 ? -1 : 1);
    }

    private (float X, float Y) ClientPoint(LPARAM value)
    {
        var point = MessagePoint(value);
        return (point.X / scale, point.Y / scale);
    }

    public static Point MessagePoint(nint value) => new(unchecked((short)value), unchecked((short)(value >> 16)));

    public void Dispose()
    {
        if (Handle.IsNull)
            return;
        // 先隐藏，随后释放合成树和窗口，避免留下旧视觉树。
        var window = Handle;
        Handle = default;
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_HIDE);
        scene?.Dispose();
        scene = null;
        composition?.Dispose();
        composition = null;
        PInvoke.DestroyWindow(window);
    }
}

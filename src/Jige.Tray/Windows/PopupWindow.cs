using System.Drawing;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class PopupWindow(HWND hwnd, TrayModel model, string? noticeId, RECT anchor, PanelPage firstPage) : IDisposable
{
    public const nuint AnimationTimer = 1;

    private Composition? composition;
    private Composition.Layer? backdrop, background;
    private PopupScene? scene, outgoing;
    private InlineEdit? editor;
    private HMONITOR monitor;
    private Bounds work, screenBounds, iconBounds;
    private Bounds? position;
    private int backgroundCapacity;
    private float scale, displayedHeight;
    private bool tracking, animations, acrylic;
    private Palette colors = null!;
    private PopupTarget down;
    private RECT anchor = anchor;
    private PanelPage page = noticeId is null ? firstPage : PanelPage.Notice;
    private int mainOffset, deviceOffset;
    private long shownAt;
    private bool reclaimed, shown;
    private DateTimeOffset now;
    private bool running;
    private string? error;
    private Transition? transition;

    public HWND Handle { get; private set; } = hwnd;
    public string? NoticeId { get; } = noticeId;
    public bool IsNotice => NoticeId is not null;
    public bool Closing { get; private set; }
    public bool Editing => editor is not null;
    public bool IsEditor(HWND child) => editor?.Handle == child;

    private readonly record struct Pose(float X, float Opacity)
    {
        public Pose Towards(Pose end, float t) => new(X + (end.X - X) * t, Opacity + (end.Opacity - Opacity) * t);
    }

    private sealed record Transition(long Started, float FromHeight, int ToHeight,
        Pose IncomingStart, Pose OutgoingStart, float Direction)
    {
        public double Progress(long ticks) => (ticks - Started) / (double)PanelLayout.TransitionMilliseconds;
        public static Pose Resting => new(0, 1);
        public Pose Exit => new(-Direction, 0);
    }

    public void Render(DateTimeOffset time, bool pollRunning, string? settingsError, bool reset = false)
    {
        if (Closing)
            return;

        now = time;
        running = pollRunning;
        error = settingsError;
        // 轮询结果先进入模型，等结束改名后再更新行顺序，避免输入框跳到另一台设备。
        if (Editing && !reset)
            return;

        var screen = PInvoke.MonitorFromRect(in anchor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = Desktop.Monitor(screen);
        work = new(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        screenBounds = new(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        iconBounds = new(anchor.left, anchor.top, anchor.right, anchor.bottom);
        int minimumHeight = IsNotice ? PanelLayout.NoticeHeight : PanelLayout.SettingsHeight;
        float newScale = Math.Min(Desktop.Dpi(screen) / 96f,
            Math.Min((work.Width - 16f) / PanelLayout.Width, work.Height / (minimumHeight + 24f)));
        if (newScale <= 0)
            throw new InvalidOperationException("找不到弹窗显示器的工作区");

        if (reset || composition is null || monitor != screen || scale != newScale)
        {
            DisposeGraphics();
            monitor = screen;
            scale = newScale;
            animations = Desktop.Animations();
            bool dark = Desktop.DarkApps;
            acrylic = Desktop.Backdrop(Handle, dark);
            colors = Desktop.PanelPalette(dark);
            composition = new(Handle, scale);
            // 背景容器先加入根节点，后续所有页面（包括转场中的两页）都在它上方。
            backdrop = new(composition, composition.Root, 0, 0, 0, 0);
        }

        var size = SizeOf(page);
        if (scene?.Page != page || scene.Size != size)
            ReplaceScene(size);
        else
            DrawScene();

        Resize(transition is null ? size.Height : displayedHeight);
        composition.Commit();
    }

    private PanelSize SizeOf(PanelPage target)
    {
        int count = target switch
        {
            PanelPage.Main => model.PanelDevices().Length,
            PanelPage.Devices => model.OrderedDevices().Length,
            _ => 0
        };
        return PanelLayout.Size(target, count, (int)(work.Height / scale) - 24);
    }

    public void Navigate(PanelPage target)
    {
        if (page == target)
            return;
        page = target;
        tracking = false;
        down = default;
        ReplaceScene(SizeOf(target));
        Resize(transition is null ? scene!.Size.Height : displayedHeight);
        composition!.Commit();
    }

    private void ReplaceScene(PanelSize size)
    {
        long time = Environment.TickCount64;
        var previous = scene;
        var oldOutgoing = outgoing;
        var oldTransition = transition;
        float progress = oldTransition is null ? 1 : (float)PanelLayout.Ease(oldTransition.Progress(time));
        var previousPose = oldTransition is null ? Transition.Resting :
            oldTransition.IncomingStart.Towards(Transition.Resting, progress);
        bool animate = shown && animations && previous is not null && !IsNotice &&
            (previous.Page != page || displayedHeight != size.Height);
        bool reversing = animate && oldOutgoing?.Page == page && oldOutgoing.Size == size;

        int offset = page == PanelPage.Main ? mainOffset : page == PanelPage.Devices ? deviceOffset : 0;
        scene = reversing ? oldOutgoing! : new(composition!, colors, animations, model, page, NoticeId, size, offset);
        if (!reversing)
            oldOutgoing?.Dispose();
        outgoing = previous;
        transition = null;
        DrawScene();

        if (!animate)
        {
            outgoing?.Dispose();
            outgoing = null;
            PInvoke.KillTimer(Handle, AnimationTimer);
            return;
        }

        bool changedPage = previous!.Page != page;
        float direction = changedPage ? (page == PanelPage.Main ? -PanelLayout.SlideDistance : PanelLayout.SlideDistance) : 0;
        var incomingPose = reversing
            ? oldTransition!.OutgoingStart.Towards(oldTransition.Exit, progress)
            : changedPage ? new(direction, 0) : Transition.Resting;
        if (changedPage)
        {
            previous.Visual.PageMotion(previousPose.X, -direction, previousPose.Opacity, 0);
            scene.Visual.PageMotion(incomingPose.X, 0, incomingPose.Opacity, 1);
        }
        else
        {
            previous.Dispose();
            outgoing = null;
        }

        transition = new(Environment.TickCount64, displayedHeight, size.Height, incomingPose, previousPose, direction);
        if (PInvoke.SetTimer(Handle, AnimationTimer, 15, null) == 0)
            FinishTransition();
    }

    public void Animate()
    {
        if (transition is not { } motion)
            return;
        double progress = motion.Progress(Environment.TickCount64);
        if (progress >= 1)
            FinishTransition();
        else
            Resize(motion.FromHeight + (motion.ToHeight - motion.FromHeight) * (float)PanelLayout.Ease(progress));
        composition!.Commit();
    }

    private void FinishTransition()
    {
        PInvoke.KillTimer(Handle, AnimationTimer);
        outgoing?.Dispose();
        outgoing = null;
        transition = null;
        scene!.Visual.ResetPageMotion();
        Resize(scene.Size.Height);
    }

    private void Resize(float height)
    {
        displayedHeight = height;
        var next = PopupPlacement.Place(iconBounds, screenBounds, work, Pixels(PanelLayout.Width),
            Pixels(height), Pixels(12), IsNotice);
        if (position == next && background is not null)
            return;

        if (background is null || backgroundCapacity < height)
        {
            background?.Dispose();
            backgroundCapacity = Math.Max((int)Math.Ceiling(height), scene!.Size.Height);
            background = new(composition!, backdrop, 0, 0, PanelLayout.Width, backgroundCapacity);
        }
        background.Draw(canvas =>
        {
            canvas.Fill(0, 0, PanelLayout.Width, height, 8, acrylic ? colors.Flyout : colors.Solid);
            canvas.Stroke(.5f, .5f, PanelLayout.Width - 1, height - 1, 7.5f, colors.Border);
        });
        composition!.Root.Clip(PanelLayout.Width, height);
        if (!PInvoke.SetWindowPos(Handle, default, next.Left, next.Top, next.Width, next.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
            throw new InvalidOperationException("无法定位弹窗");
        position = next;
    }

    private int Pixels(float dip) => (int)MathF.Round(dip * scale, MidpointRounding.AwayFromZero);

    public void Show()
    {
        scene?.Open();
        shown = true;
        PInvoke.ShowWindow(Handle, IsNotice ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_SHOW);
        shownAt = Environment.TickCount64;
        if (!IsNotice)
            PInvoke.SetForegroundWindow(Handle);
    }

    /// <summary>显示后 400 ms 内的第一次失焦视为任务栏收回前台，重新激活而不是关闭。</summary>
    public bool TryReclaimFocus()
    {
        if (IsNotice || reclaimed || Editing || Environment.TickCount64 - shownAt > 400)
            return false;
        reclaimed = true;
        PInvoke.PostMessage(Handle, 0x8010, default, default);
        return true;
    }

    public bool BeginClose()
    {
        if (Closing)
            return true;
        Closing = true;
        PInvoke.KillTimer(Handle, AnimationTimer);
        if (!animations || IsNotice)
            return false;
        scene?.Close();
        return true;
    }

    public void SetAnchor(RECT value) => anchor = value;

    public void Spin() => scene?.Spin();

    private void DrawScene()
    {
        if (scene is null)
            return;
        scene.Render(now, running, error, editor?.DeviceId);
        if (scene.Page == PanelPage.Main)
            mainOffset = scene.Offset;
        else if (scene.Page == PanelPage.Devices)
            deviceOffset = scene.Offset;
    }

    public RECT? ScreenRect(PopupTarget target)
    {
        if (scene?.TargetRect(target) is not { } box || position is not { } origin)
            return null;
        return new(origin.Left + Pixels(box.X), origin.Top + Pixels(box.Y), origin.Left + Pixels(box.X + box.Width),
            origin.Top + Pixels(box.Y + box.Height));
    }

    public void StartEdit(PopupTarget target)
    {
        if (scene?.TargetRect(target) is not { } box ||
            model.OrderedDevices().FirstOrDefault(item => item.Device.Id == target.DeviceId) is not { } device)
            return;
        int left = Pixels(box.X + 2), top = Pixels(box.Y + 2);
        var bounds = RECT.FromXYWH(left, top, Pixels(box.X + box.Width - 2) - left, Pixels(box.Y + box.Height - 2) - top);
        editor = new(Handle, device.Device.Id, model.Name(device.Device), bounds, Pixels(13.5f), colors);
        DrawScene();
        composition!.Commit();
        editor.Focus();
    }

    public (string Id, string Name)? FinishEdit(bool save, bool restoreFocus)
    {
        if (editor is not { } active)
            return null;
        editor = null;
        try
        {
            if (restoreFocus)
                active.ReturnFocus();
            return save && active.RequestedSave != false ? (active.DeviceId, active.Text()) : null;
        }
        finally
        {
            active.Dispose();
        }
    }

    public LRESULT EditColors(HDC dc) => editor?.PaintColors(dc) ?? default;

    public void PointerMove(LPARAM lParam)
    {
        if (transition is null)
        {
            var (x, y) = ClientPoint(lParam);
            scene?.Pointer(x, y);
        }
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
        down = default;
        if (transition is null)
            scene?.Pointer(-1, -1);
    }

    public PopupTarget PointerButton(LPARAM lParam, bool pressed)
    {
        if (transition is not null)
            return default;
        var (x, y) = ClientPoint(lParam);
        var current = scene?.Hit(x, y) ?? default;
        scene?.Pointer(x, y, pressed);
        if (pressed)
        {
            down = current;
            return default;
        }
        var result = current == down ? current : default;
        down = default;
        return result;
    }

    public PopupTarget Key(nuint key)
    {
        if (key == 0x1B)
            return page is PanelPage.Main or PanelPage.Notice ? PopupAction.Close : PopupAction.Back;
        if (transition is not null)
            return default;
        switch (key)
        {
            case 0x09: scene?.Tab(PInvoke.GetKeyState(0x10) < 0); break;
            case 0x0D or 0x20: return scene?.Focused ?? default;
            case 0x26: Scroll(-1); break;
            case 0x28: Scroll(1); break;
        }
        return default;
    }

    public void Wheel(short delta)
    {
        if (delta != 0)
            Scroll(delta > 0 ? -1 : 1);
    }

    private void Scroll(int step)
    {
        if (transition is not null || scene?.Scroll(step) != true)
            return;
        DrawScene();
        composition!.Commit();
    }

    private (float X, float Y) ClientPoint(LPARAM value)
    {
        var point = MessagePoint(value);
        return (point.X / scale, point.Y / scale);
    }

    public static Point MessagePoint(nint value) => new(unchecked((short)value), unchecked((short)(value >> 16)));

    private void DisposeGraphics()
    {
        transition = null;
        PInvoke.KillTimer(Handle, AnimationTimer);
        scene?.Dispose();
        scene = null;
        outgoing?.Dispose();
        outgoing = null;
        background?.Dispose();
        background = null;
        backdrop?.Dispose();
        backdrop = null;
        composition?.Dispose();
        composition = null;
    }

    public void Dispose()
    {
        if (Handle.IsNull)
            return;
        FinishEdit(save: false, restoreFocus: false);
        // 先隐藏，随后释放合成树和窗口，避免留下旧视觉树。
        var window = Handle;
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_HIDE);
        DisposeGraphics();
        Handle = default;
        PInvoke.DestroyWindow(window);
    }
}

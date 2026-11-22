using System.Globalization;
using Jige.Core;

namespace Jige.Tray;

internal readonly record struct TaskbarStripDevice(
    DeviceKind Kind, int? Percentage, BatteryTone Tone, bool ShowBolt = false, bool IsPlaceholder = false)
{
    public static TaskbarStripDevice Placeholder { get; } =
        new(DeviceKind.Other, null, BatteryTone.Idle, ShowBolt: false, IsPlaceholder: true);

    public string Text => Percentage is { } percent
        ? Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture) + "%"
        : "--";
    public static TaskbarStripDevice[] Build(TrayModel model)
    {
        // 自定义沿用托盘的勾选规则，并使用主面板的活跃设备排序。
        var devices = model.Settings.TrayMode == TrayDisplayMode.LowestBattery
            ? model.TrayDevices()
            : model.OrderedDevices().Where(device => device.IsActive && model.Settings.IsInTray(device.Device.Id));
        return devices.Select(device => From(device, model.ThresholdFor(device.Device.Id))).ToArray();
    }

    public static TaskbarStripDevice[] WithPlaceholder(TaskbarStripDevice[] devices) =>
        devices.Length > 0 ? devices : [Placeholder];

    public static TaskbarStripDevice From(DeviceStatus device, int threshold)
    {
        var tone = Presentation.Tone(device, threshold);
        bool showBolt = tone == BatteryTone.Charging;
        // A short sleep keeps the last reading's ink, including a green reading.
        // Only a current charging/full observation gets a lightning bolt.
        if ((device.Link == LinkState.Asleep || device is { Link: LinkState.Online, PendingRefresh: true }) &&
            device.Last is { WasChargingOrFull: true } or { Percentage: 100 })
            tone = BatteryTone.Charging;
        return new(device.Device.Kind, tone == BatteryTone.Idle ? null : device.Percentage, tone, showBolt);
    }
}

internal readonly record struct TaskbarStripSegment(
    TaskbarStripDevice Device, IconRect Icon, IconRect? Bolt, IconRect Text, IconRect? Separator);

internal sealed record TaskbarStripLayout(int Width, TaskbarStripSegment[] Segments)
{
    public const int Height = 22;
    public const int GlyphWidth = 6;

    public static int PixelScale(uint dpi) => Math.Clamp((int)((dpi + 48L) / 96), 1, 8);
    public static int TextWidth(string text) => text.Length == 0 ? 0 : text.Length * (GlyphWidth + 1) - 1;

    public static TaskbarStripLayout Build(ReadOnlySpan<TaskbarStripDevice> devices)
    {
        const int padding = 4, iconSize = 11, segmentGap = 4;
        var segments = new TaskbarStripSegment[devices.Length];
        int x = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            var device = devices[i];
            var icon = new IconRect(x + padding, 5, iconSize, iconSize);
            var bolt = device.ShowBolt ? new IconRect(icon.Right + 3, 7, 5, 7) : (IconRect?)null;
            var text = new IconRect(bolt is { } flash ? flash.Right + 2 : icon.Right + 3,
                6, TextWidth(device.Text), 9);
            x = text.Right + padding;
            IconRect? separator = null;
            if (i < devices.Length - 1)
            {
                separator = new(x + segmentGap / 2, 6, 1, 10);
                x += segmentGap;
            }
            segments[i] = new(device, icon, bolt, text, separator);
        }
        return new(x, segments);
    }
}

internal readonly record struct TaskbarStripPalette(Color Foreground, Color Warning, Color Low, Color Charging, Color Separator)
{
    public static TaskbarStripPalette Create(bool dark) => dark
        ? new(new(0xf0ece4), Palette.Create(true).Warning, new(0xff8080), new(0x4ce07a), new(0x393e4c))
        : new(new(0x211d19), Palette.Create(false).Warning, new(0xb42318), new(0x0f7b38), new(0xd2d2d2));

    public (Color Icon, Color Text) Ink(BatteryTone tone, bool showBolt = true) => tone switch
    {
        BatteryTone.Warning => (Warning, Warning),
        BatteryTone.Low => (Low, Low),
        BatteryTone.Charging when !showBolt => (Foreground, Foreground),
        BatteryTone.Charging => (Foreground, Charging),
        _ => (Foreground, Foreground)
    };
}

internal enum TaskbarStripReason
{
    Ready, UserSelectedTray, MissingTaskbar, MissingNotificationArea, UnknownButtonBounds,
    UnsupportedEdge, NoRoom, NoDevices, HiddenTaskbar, Fullscreen, WindowUnavailable
}

internal readonly record struct TaskbarStripPlacement(TaskbarStripReason Reason, Bounds Bounds)
{
    public static TaskbarStripPlacement Compute(Bounds screen, Bounds? taskbar, Bounds? notification,
        int? occupiedRight, uint dpi, int width, bool visible, bool bottomEdge)
    {
        if (taskbar is not { Width: > 0, Height: > 0 } bar)
            return new(TaskbarStripReason.MissingTaskbar, default);
        const int edgeTolerance = 2;
        if (!bottomEdge || bar.Width <= bar.Height || bar.Top < screen.Top ||
            bar.Right <= screen.Left || bar.Left >= screen.Right || bar.Bottom < screen.Bottom - edgeTolerance)
            return new(TaskbarStripReason.UnsupportedEdge, default);

        int scale = TaskbarStripLayout.PixelScale(dpi);
        int height = TaskbarStripLayout.Height * scale;
        int top = bar.Top + (bar.Height - height) / 2;
        if (!visible || (bar.Height >= height && top + height > screen.Bottom))
            return new(TaskbarStripReason.HiddenTaskbar, default);
        if (notification is not { Width: > 0, Height: > 0 } tray ||
            tray.Left <= bar.Left || tray.Right > bar.Right + edgeTolerance ||
            tray.Top >= bar.Bottom || tray.Bottom <= bar.Top)
            return new(TaskbarStripReason.MissingNotificationArea, default);
        if (occupiedRight is not { } right || right <= bar.Left)
            return new(TaskbarStripReason.UnknownButtonBounds, default);

        int gap = (int)((4L * dpi + 48) / 96);
        int stripRight = Math.Min(tray.Left, screen.Right) - gap;
        int left = stripRight - width * scale;
        if (bar.Height < height || (width > 0 && left < Math.Max(right, screen.Left) + gap))
            return new(TaskbarStripReason.NoRoom, default);
        return new(TaskbarStripReason.Ready, new(left, top, stripRight, top + height));
    }
}

internal readonly record struct TaskbarStripDisplay(TaskbarStripReason Reason, Bounds Bounds)
{
    public bool ShowStrip => Reason is TaskbarStripReason.Ready or TaskbarStripReason.NoDevices &&
        Bounds.Width > 0 && Bounds.Height > 0;
    public bool IsFallback => Reason is TaskbarStripReason.MissingTaskbar or TaskbarStripReason.MissingNotificationArea or
        TaskbarStripReason.UnknownButtonBounds or TaskbarStripReason.UnsupportedEdge or
        TaskbarStripReason.NoRoom or TaskbarStripReason.WindowUnavailable;
    // 任务栏直读条与托盘图标严格二选一：仅在用户选择托盘图标或直读条临时回退时注册托盘图标。
    public bool ShowTrayIcons => Reason == TaskbarStripReason.UserSelectedTray || IsFallback;
    public string Actual => ShowTrayIcons ? "托盘图标" : "任务栏";
    public string Description => Reason switch
    {
        TaskbarStripReason.Ready => "直读条可用",
        TaskbarStripReason.UserSelectedTray => "用户选择托盘图标",
        TaskbarStripReason.MissingTaskbar => "找不到主任务栏",
        TaskbarStripReason.MissingNotificationArea => "找不到通知区",
        TaskbarStripReason.UnknownButtonBounds => "无法确认应用按钮占用范围",
        TaskbarStripReason.UnsupportedEdge => "任务栏不在屏幕底部",
        TaskbarStripReason.NoRoom => "通知区左侧空间不足",
        TaskbarStripReason.NoDevices => "暂无可显示设备",
        TaskbarStripReason.HiddenTaskbar => "任务栏已隐藏",
        TaskbarStripReason.Fullscreen => "前台全屏",
        _ => "直读条窗口不可用"
    };

    public static TaskbarStripDisplay Resolve(TrayDisplayLocation preference, TaskbarStripPlacement placement,
        bool haveDevices, bool fullscreen)
    {
        if (preference == TrayDisplayLocation.TrayIcons)
            return new(TaskbarStripReason.UserSelectedTray, default);
        if (fullscreen && placement.Reason is TaskbarStripReason.Ready or TaskbarStripReason.HiddenTaskbar)
            return new(TaskbarStripReason.Fullscreen, default);
        if (placement.Reason == TaskbarStripReason.Ready)
            return new(haveDevices ? TaskbarStripReason.Ready : TaskbarStripReason.NoDevices, placement.Bounds);
        return new(placement.Reason, placement.Bounds);
    }
}

internal readonly record struct TaskbarControl(Bounds Bounds, string Id, string ClassName, bool Button);
internal readonly record struct TaskbarControls(Bounds? NotificationArea, int? OccupiedRight)
{
    public static TaskbarControls Locate(Bounds bar, Bounds? nativeTray, ReadOnlySpan<TaskbarControl> controls)
    {
        Bounds? notification = null;
        if (nativeTray is { } native && FitsNotificationArea(native))
            notification = native;
        foreach (var control in controls)
        {
            var bounds = control.Bounds;
            bool tray = control.Id is "SystemTrayFrame" or "SystemTrayFrameGrid" or "NotificationAreaIcons" or
                "NotifyIconStack" or "ShowHiddenIconsButton" or "ControlCenterButton" or "NotificationCenterButton" ||
                control.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal) ||
                control.ClassName is "SystemTrayFrame" or "ChevronIconView";
            if (tray && FitsNotificationArea(bounds) &&
                (notification is null || bounds.Left < notification.Value.Left))
                notification = bounds;
        }

        if (notification is not { } trayBounds)
            return default;

        bool sawStart = false, sawTaskList = false, sawFrame = false;
        int taskbarFrameRight = bar.Left;
        int right = bar.Left;
        foreach (var control in controls)
        {
            var bounds = control.Bounds;
            if (!InsideBar(bounds))
                continue;
            bool start = control.Id == "StartButton" || control.ClassName == "Start";
            bool frame = control.Id == "TaskbarFrame" ||
                control.ClassName is "TaskbarFrame" or "Taskbar.TaskbarFrame" or "Taskbar.TaskbarFrameAutomationPeer";
            sawFrame |= frame;
            if (frame && bounds.Right <= trayBounds.Left)
                taskbarFrameRight = Math.Max(taskbarFrameRight, bounds.Right);
            sawStart |= start;
            // The modern taskbar frame can span the entire HWND. Its button bounds,
            // including Start, Search, Widgets and overflow, are what consume space.
            bool taskListButton = control.ClassName is "TaskListButton" or "Taskbar.TaskListButton" or
                "Taskbar.TaskListButtonAutomationPeer" or "Taskbar.TaskListLabeledButtonPanel" ||
                control.Id == "TaskListButton" ||
                control.Id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase);
            bool shellButton = control.Id is "SearchButton" or "TaskViewButton" or "WidgetsButton" or "OverflowButton";
            bool legacyList = control.ClassName == "MSTaskListWClass";
            sawTaskList |= taskListButton || legacyList;
            if (start || taskListButton || legacyList || (shellButton || control.Button) && bounds.Left < trayBounds.Left)
                right = Math.Max(right, bounds.Right);
        }

        int? occupied = sawTaskList ? right
            : taskbarFrameRight > bar.Left ? Math.Max(right, taskbarFrameRight)
            : sawFrame ? right
            : null;
        return new(notification, sawStart ? occupied : null);

        bool InsideBar(Bounds rect) => rect.Width > 0 && rect.Height > 0 &&
            rect.Left >= bar.Left - 2 && rect.Right <= bar.Right + 2 &&
            rect.Top < bar.Bottom && rect.Bottom > bar.Top;

        bool FitsNotificationArea(Bounds rect) =>
            InsideBar(rect) && rect.Left > bar.CenterX;
    }
}

/// <summary>
/// 过滤 Windows 11 任务栏 UIA 在瞬态重排期间偶发丢失应用按钮边界导致的直读条↔托盘闪烁。
/// 仅在任务栏位置、DPI 与可见状态均未变化时，对连续短暂缺失应用按钮边界给予宽限并触发快速重试。
/// </summary>
internal sealed class TaskbarControlsStabilizer
{
    public const int MaxTransientMisses = 2;
    public const long GraceMs = 1_500;

    private Bounds lastBar;
    private uint lastDpi;
    private TaskbarControls? lastValid;
    private int transientMisses;
    private long firstMissMs;

    public void Reset()
    {
        lastValid = null;
        transientMisses = 0;
        firstMissMs = 0;
    }

    public (TaskbarControls Controls, bool Retry) Apply(
        Bounds bar, uint dpi, bool visible, bool bottomEdge, TaskbarControls incoming, long nowMs)
    {
        if (!visible || !bottomEdge || incoming.NotificationArea is null)
        {
            Reset();
            return (incoming, false);
        }

        if (incoming.OccupiedRight is not null)
        {
            lastBar = bar;
            lastDpi = dpi;
            lastValid = incoming;
            transientMisses = 0;
            firstMissMs = 0;
            return (incoming, false);
        }

        if (lastValid is { } cached && lastBar == bar && lastDpi == dpi)
        {
            if (transientMisses == 0)
                firstMissMs = nowMs;
            transientMisses++;
            if (transientMisses <= MaxTransientMisses && nowMs - firstMissMs <= GraceMs)
                return (cached with { NotificationArea = incoming.NotificationArea }, true);
        }

        Reset();
        return (incoming, false);
    }
}

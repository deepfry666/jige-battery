using Jige.Core;
using Jige.Tray.Windows;

namespace Jige.Tray;

internal enum HoverCardKind { Single, List, Empty }

internal sealed record HoverCardRow(
    string DeviceId,
    DeviceKind Kind,
    string Name,
    string StatusText,
    int? Percentage,
    bool IsEstimated,
    string ValueText,
    BatteryTone Tone,
    int FilledCells,
    bool IsCharging,
    bool IsAsleep);

internal sealed record HoverCardContent(
    HoverCardKind Kind,
    string Title,
    DeviceKind DeviceKind,
    string DeviceName,
    string ConnectionTag,
    string SourceLabel,
    int? Percentage,
    bool IsEstimated,
    string ValueText,
    BatteryTone Tone,
    int FilledCells,
    bool IsCharging,
    string StatusLine,
    HoverCardRow[] Rows,
    int TotalActiveCount,
    int OverflowCount,
    string RuntimeLine = "")
{
    public const int CardWidth = 248;
    public const int MaxListRows = 6;
    public const int SingleHeight = 126;
    public const int RuntimeLineHeight = 18;
    public const int EmptyHeight = 64;
    public const int ListHeaderHeight = 28;
    public const int ListRowHeight = 42;
    public const int ListOverflowHeight = 24;
    public const int ListBottomPadding = 8;

    public string OverflowText => OverflowCount > 0 ? $"还有 {OverflowCount} 台" : "";

    public int Height => Kind switch
    {
        HoverCardKind.Single => SingleHeight + (RuntimeLine.Length > 0 ? RuntimeLineHeight : 0),
        HoverCardKind.Empty => EmptyHeight,
        _ => ListHeaderHeight + Rows.Length * ListRowHeight + (OverflowCount > 0 ? ListOverflowHeight : 0) + ListBottomPadding
    };
}

/// <summary>悬停卡片显示策略（G4 / H2）：全屏游戏、主面板打开、右键菜单或更新窗口打开时不显示。</summary>
internal static class HoverCardPolicy
{
    public static bool CanShow(GameMode game, bool popupOpen, bool menuOpen = false, bool dialogOpen = false) =>
        !game.BlockPopups && !popupOpen && !menuOpen && !dialogOpen;
}

/// <summary>悬停卡片定位（H3）：贴着任务栏并对齐图标中心，支持四边任务栏、多显示器与边界夹紧。</summary>
internal static class HoverCardPlacement
{
    public static Bounds Place(Bounds icon, Bounds monitor, Bounds work, int width, int height, int gap, int edgeMargin = 8)
    {
        int top = work.Top > monitor.Top ? Math.Abs(icon.CenterY - monitor.Top) : int.MaxValue;
        int bottom = work.Bottom < monitor.Bottom ? Math.Abs(monitor.Bottom - icon.CenterY) : int.MaxValue;
        int left = work.Left > monitor.Left ? Math.Abs(icon.CenterX - monitor.Left) : int.MaxValue;
        int right = work.Right < monitor.Right ? Math.Abs(monitor.Right - icon.CenterX) : int.MaxValue;
        if (Math.Min(Math.Min(top, bottom), Math.Min(left, right)) == int.MaxValue)
        {
            top = Math.Abs(icon.CenterY - monitor.Top);
            bottom = Math.Abs(monitor.Bottom - icon.CenterY);
            left = Math.Abs(icon.CenterX - monitor.Left);
            right = Math.Abs(monitor.Right - icon.CenterX);
        }

        int nearest = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
        int x;
        int y;
        bool horizontalEdge = nearest == bottom || nearest == top;
        if (nearest == bottom)
        {
            x = icon.CenterX - width / 2;
            y = Math.Min(icon.Top, work.Bottom) - height - gap;
        }
        else if (nearest == top)
        {
            x = icon.CenterX - width / 2;
            y = Math.Max(icon.Bottom, work.Top) + gap;
        }
        else if (nearest == left)
        {
            x = Math.Max(icon.Right, work.Left) + gap;
            y = icon.CenterY - height / 2;
        }
        else
        {
            x = Math.Min(icon.Left, work.Right) - width - gap;
            y = icon.CenterY - height / 2;
        }

        if (horizontalEdge)
        {
            int marginX = Math.Clamp(edgeMargin, 0, Math.Max(0, (work.Width - width) / 2));
            int minX = work.Left + marginX;
            int maxX = Math.Max(minX, work.Right - width - marginX);
            x = Math.Clamp(x, minX, maxX);
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        }
        else
        {
            int marginY = Math.Clamp(edgeMargin, 0, Math.Max(0, (work.Height - height) / 2));
            int minY = work.Top + marginY;
            int maxY = Math.Max(minY, work.Bottom - height - marginY);
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
            y = Math.Clamp(y, minY, maxY);
        }

        return new(x, y, x + width, y + height);
    }
}

/// <summary>生成两种模式下的悬停卡片视图模型（H5 / H6）。两种模式都不包含已断开的设备。</summary>
internal static class HoverCardBuilder
{
    public static HoverCardContent Build(TrayModel model, string? slotKey, DateTimeOffset now, bool forceList = false)
    {
        bool isSingleDeviceSlot = !forceList && model.Settings.TrayMode == TrayDisplayMode.Custom &&
            !string.IsNullOrEmpty(slotKey) &&
            !StringComparer.OrdinalIgnoreCase.Equals(slotKey, TrayIconPlan.SingleKey);

        if (isSingleDeviceSlot)
        {
            var device = model.OrderedDevices().FirstOrDefault(item =>
                item.IsActive && StringComparer.OrdinalIgnoreCase.Equals(item.Device.Id, slotKey));
            if (device is null)
                return Empty();
            return BuildSingle(model, device, model.ThresholdFor(device.Device.Id), now);
        }

        if (forceList || model.Settings.TrayMode == TrayDisplayMode.LowestBattery)
        {
            var active = model.OrderedDevices()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Percentage ?? int.MaxValue)
                .ThenBy(item => item.Device.Kind)
                .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (active.Length == 0)
                return Empty();

            var rows = active.Take(HoverCardContent.MaxListRows)
                .Select(device => BuildRow(model, device, model.ThresholdFor(device.Device.Id), now))
                .ToArray();
            int overflow = Math.Max(0, active.Length - HoverCardContent.MaxListRows);
            return new(
                Kind: HoverCardKind.List,
                Title: $"几格 · {active.Length} 台设备",
                DeviceKind: default,
                DeviceName: "",
                ConnectionTag: "",
                SourceLabel: "",
                Percentage: null,
                IsEstimated: false,
                ValueText: "",
                Tone: BatteryTone.Normal,
                FilledCells: 0,
                IsCharging: false,
                StatusLine: "",
                Rows: rows,
                TotalActiveCount: active.Length,
                OverflowCount: overflow);
        }

        return Empty();
    }

    private static HoverCardContent BuildSingle(TrayModel model, DeviceStatus device, int threshold, DateTimeOffset now)
    {
        var tone = Presentation.Tone(device, threshold);
        return new(
            Kind: HoverCardKind.Single,
            Title: "",
            DeviceKind: device.Device.Kind,
            DeviceName: model.Name(device.Device),
            ConnectionTag: Presentation.Connection(device.Device),
            SourceLabel: Presentation.SourceLabel(device.Device),
            Percentage: device.Percentage,
            IsEstimated: device.Last?.IsEstimated == true,
            ValueText: Presentation.Value(device),
            Tone: tone,
            FilledCells: Presentation.FilledCells(device.Percentage, 10),
            IsCharging: device.IsCharging,
            StatusLine: SingleStatusLine(device, tone, now, model.LastCharged(device.Device.Id)),
            Rows: [],
            TotalActiveCount: 1,
            OverflowCount: 0,
            RuntimeLine: RuntimeLine(device, model.Runtime(device, now)));
    }

    private static HoverCardRow BuildRow(TrayModel model, DeviceStatus device, int threshold, DateTimeOffset now)
    {
        var tone = Presentation.Tone(device, threshold);
        return new(
            DeviceId: device.Device.Id,
            Kind: device.Device.Kind,
            Name: model.Name(device.Device),
            StatusText: ListRowStatus(device, tone, model.Runtime(device, now)),
            Percentage: device.Percentage,
            IsEstimated: device.Last?.IsEstimated == true,
            ValueText: Presentation.Value(device),
            Tone: tone,
            FilledCells: Presentation.FilledCells(device.Percentage, 5),
            IsCharging: device.IsCharging,
            IsAsleep: device.Link == LinkState.Asleep);
    }

    private static HoverCardContent Empty() => new(
        Kind: HoverCardKind.Empty,
        Title: "几格",
        DeviceKind: DeviceKind.Other,
        DeviceName: "",
        ConnectionTag: "",
        SourceLabel: "",
        Percentage: null,
        IsEstimated: false,
        ValueText: "",
        Tone: BatteryTone.Idle,
        FilledCells: 0,
        IsCharging: false,
        StatusLine: "暂无在用设备 · 点击打开面板",
        Rows: [],
        TotalActiveCount: 0,
        OverflowCount: 0);

    internal static string SingleStatusLine(DeviceStatus device, BatteryTone tone, DateTimeOffset now,
        DateTimeOffset? lastCharge = null)
    {
        bool charging = device.Fresh && (device.IsFull || device.IsCharging);
        string? charged = !charging && lastCharge is { } at && at <= now ? RuntimeEstimator.ChargedAgo(at, now) : null;
        List<string> parts = [];
        if (device.Fresh && device.IsFull)
            parts.Add("已充满");
        else if (device.Fresh && device.IsCharging)
            parts.Add("充电中");
        else if (tone == BatteryTone.Low)
            parts.Add("电量低");
        else if (tone == BatteryTone.Warning)
            parts.Add("电量偏低");
        else if (device.Link != LinkState.Asleep && charged is null)
            parts.Add(device.Last?.IsEstimated == true ? "电压估算" : "在线");

        if (device.Link == LinkState.Asleep)
            parts.Add("休眠中");
        if (charged is not null)
            parts.Add(charged);

        if (device.Last is { } last)
        {
            string age = ReadAge(last.ReadAt, now);
            // 已显示「上次充电」时，刚读到的读数不再重复写「刚刚读取」，避免单行放不下。
            if (charged is null || age != "刚刚读取")
                parts.Add(age);
        }
        else
            parts.Add("等待读取");

        return string.Join(" · ", parts);
    }

    /// <summary>单设备悬停卡片的预计可用时间行；功能关闭或不适用时为空，卡片保持原高度。</summary>
    internal static string RuntimeLine(DeviceStatus device, RuntimeEstimate runtime)
    {
        if (runtime.Kind == RuntimeKind.Estimating)
            return "预计可用时间估算中";
        if (runtime.Kind != RuntimeKind.Ready)
            return "";
        string remaining = RuntimeEstimator.Format(runtime.Hours);
        if (device.IsFull)
            return $"满电预计可用{remaining}";
        return runtime.Days is { } days
            ? $"预计可用{remaining} · 按你的用法{RuntimeEstimator.FormatDays(days)}"
            : $"预计可用{remaining}";
    }

    internal static string ListRowStatus(DeviceStatus device, BatteryTone tone, RuntimeEstimate runtime)
    {
        if (runtime.Kind == RuntimeKind.None || device.Link == LinkState.Asleep || device.Fresh && (device.IsFull || device.IsCharging))
            return ListRowStatus(device, tone);
        string? remaining = runtime.Kind == RuntimeKind.Ready ? RuntimeEstimator.Format(runtime.Hours) : null;
        return tone switch
        {
            BatteryTone.Low => remaining is null ? "电量低" : $"电量低 · {remaining}",
            BatteryTone.Warning => remaining is null ? "电量偏低" : $"电量偏低 · {remaining}",
            _ => remaining is null ? "可用时间估算中" : $"可用{remaining}"
        };
    }

    internal static string ListRowStatus(DeviceStatus device, BatteryTone tone)
    {
        if (device.Link == LinkState.Asleep)
        {
            return tone switch
            {
                BatteryTone.Low => "电量低 · 休眠中",
                BatteryTone.Warning => "电量偏低 · 休眠中",
                _ => "休眠中"
            };
        }

        if (device.Fresh && device.IsFull)
            return "已充满";
        if (device.Fresh && device.IsCharging)
            return "充电中";
        return tone switch
        {
            BatteryTone.Low => "电量低",
            BatteryTone.Warning => "电量偏低",
            _ => $"在线 · {Presentation.Connection(device.Device)}"
        };
    }

    internal static string ReadAge(DateTimeOffset readAt, DateTimeOffset now)
    {
        long minutes = Math.Max(0, (long)(now - readAt).TotalMinutes);
        return minutes == 0 ? "刚刚读取" : $"{minutes} 分钟前读取";
    }
}

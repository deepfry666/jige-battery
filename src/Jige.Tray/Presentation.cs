using Jige.Core;

namespace Jige.Tray;

internal enum BatteryTone { Normal, Warning, Low, Charging, Idle }

internal readonly record struct Color(uint Rgb, byte Alpha = 255)
{
    public Color WithAlpha(byte alpha) => new(Rgb, alpha);
    public uint Premultiplied
    {
        get
        {
            uint red = ((Rgb >> 16) & 255) * Alpha / 255;
            uint green = ((Rgb >> 8) & 255) * Alpha / 255;
            uint blue = (Rgb & 255) * Alpha / 255;
            return ((uint)Alpha << 24) | (red << 16) | (green << 8) | blue;
        }
    }
}

internal sealed record Palette(Color Ink, Color Secondary, Color Muted, Color Card, Color CardStroke, Color Chip,
    Color Flyout, Color Solid, Color Border, Color Layer, Color LayerStroke, Color Hover, Color Press,
    Color Accent, Color AccentSoft, Color Warning, Color Low, Color Charging, Color Idle, Color Empty, Color ButtonText)
{
    public static Palette Create(bool dark, uint? accent = null)
    {
        var active = new Color(accent ?? (dark ? 0x60cdffu : 0x005fb8u));
        return dark
            ? new(new(0xf3f3f3), new(0xc5c5c5), new(0xa0a0a0), new(0x262626), new(0xffffff, 20), new(0x333333),
                new(0x282828, 199), new(0x2b2b2b), new(0xffffff, 23), new(0xffffff, 13), new(0xffffff, 15),
                new(0xffffff, 15), new(0xffffff, 23), active, active.WithAlpha(36), new(0xfce100), new(0xff99a4),
                new(0x6ccb5f), new(0x8f8f8f), new(0xffffff, 31), new(0))
            : new(new(0x1b1b1b), new(0x5e5e5e), new(0x6b6b6b), new(0xffffff), new(0, 20), new(0xf0f0f0),
                new(0xf9f9f9, 204), new(0xf9f9f9), new(0, 26), new(0xffffff, 158), new(0, 13),
                new(0, 10), new(0, 18), active, active.WithAlpha(31), new(0x9d5d00), new(0xc42b1c),
                new(0x0f7b0f), new(0x8a8a8a), new(0, 23), new(0xffffff));
    }

    public Color Status(BatteryTone tone) => tone switch
    {
        BatteryTone.Warning => Warning,
        BatteryTone.Low => Low,
        BatteryTone.Charging => Charging,
        BatteryTone.Idle => Idle,
        _ => Accent
    };
}

internal readonly record struct IconAppearance(
    int? Percentage, BatteryTone Tone, Color FillInk, DeviceKind? Kind, Color DimInk);

internal static class Presentation
{
    public static BatteryTone Tone(DeviceStatus? device, int threshold) => device switch
    {
        { Fresh: true, Percentage: { } } when device.IsFull || device.IsCharging => BatteryTone.Charging,
        { Fresh: true, Percentage: { } percent } when percent < threshold => BatteryTone.Low,
        { Fresh: true, Percentage: { } percent } when percent <= threshold + 15 => BatteryTone.Warning,
        { Fresh: true, Percentage: { } } => BatteryTone.Normal,
        // R1 / R3：设备休眠（或刚从休眠唤醒等待刷新）时保留上次读数且图标不变灰，按上次读数着色（不显示充电态）。
        { Link: LinkState.Asleep, Percentage: { } percent } when percent < threshold => BatteryTone.Low,
        { Link: LinkState.Asleep, Percentage: { } percent } when percent <= threshold + 15 => BatteryTone.Warning,
        { Link: LinkState.Asleep, Percentage: { } } => BatteryTone.Normal,
        { Link: LinkState.Online, PendingRefresh: true, Percentage: { } percent } when percent < threshold => BatteryTone.Low,
        { Link: LinkState.Online, PendingRefresh: true, Percentage: { } percent } when percent <= threshold + 15 => BatteryTone.Warning,
        { Link: LinkState.Online, PendingRefresh: true, Percentage: { } } => BatteryTone.Normal,
        _ => BatteryTone.Idle
    };

    public static IconAppearance Icon(DeviceStatus? device, bool darkTaskbar, int threshold)
    {
        var palette = Palette.Create(darkTaskbar);
        var tone = Tone(device, threshold);
        var color = tone switch
        {
            BatteryTone.Idle => palette.Ink.WithAlpha(140),
            BatteryTone.Normal => darkTaskbar ? new(0xffffff) : palette.Ink,
            _ => palette.Status(tone)
        };
        int? percentage = tone == BatteryTone.Idle ? null : device?.Percentage;
        Color dim = darkTaskbar ? new(0xffffff, 71) : new(0, 56);
        return new(percentage, tone, color, device?.Device.Kind, dim);
    }

    public static int FilledCells(int? percent, int count) => ((percent ?? 0) * count + 50) / 100;

    public static string Connection(DeviceIdentity device) => device.Connection switch
    {
        DeviceConnection.Bluetooth => "蓝牙",
        DeviceConnection.UsbCable => "USB",
        _ => "2.4G"
    };

    public static string SourceLabel(DeviceIdentity device)
    {
        if (!string.IsNullOrWhiteSpace(device.SourceLabel))
            return device.SourceLabel;
        if (device.Connection == DeviceConnection.Bluetooth)
            return device.NameFromHidpp ? "罗技蓝牙" : "Windows 蓝牙";
        if (device.Connection == DeviceConnection.UsbCable)
            return device.Id.StartsWith("logitech-", StringComparison.OrdinalIgnoreCase) ? "罗技 USB 直连" : "USB 直连";
        if (device.Id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase) ||
            device.Id.StartsWith("logitech-headset:", StringComparison.OrdinalIgnoreCase))
            return "罗技无线接收器";
        if (device.Id.StartsWith("inzone-h9:", StringComparison.OrdinalIgnoreCase))
            return "Sony INZONE 接收器";
        if (device.Id.StartsWith("ulx:", StringComparison.OrdinalIgnoreCase))
            return "Finalmouse 接收器";
        if (device.Id.StartsWith("razer-", StringComparison.OrdinalIgnoreCase))
            return "雷蛇无线接收器";
        return "无线接收器";
    }

    public static string Status(DeviceStatus device, DateTimeOffset now, int threshold)
    {
        if (device.Fresh)
            return Tone(device, threshold) switch
            {
                BatteryTone.Charging => device.IsFull ? "已充满" : "充电中",
                BatteryTone.Low => "电量低，该充电了",
                BatteryTone.Warning => "电量偏低",
                _ => device.Last?.IsEstimated == true ? $"{Connection(device.Device)} 已连接 · 电量估算" : $"{Connection(device.Device)} 已连接"
            };

        string status = (device.Link, device.Issue) switch
        {
            (LinkState.Asleep, _) => "休眠",
            (LinkState.Offline, _) => "已断开",
            (LinkState.Busy, _) => "接收器被占用",
            (_, ReadIssue.AccessDenied) => "无法访问接收器",
            (_, ReadIssue.Timeout) => "查询超时",
            (_, ReadIssue.InvalidResponse) => "读数无效",
            (_, ReadIssue.IoError) => "读取失败",
            _ => "状态待确认"
        };
        return device.Last is { } last ? $"{status} · 上次读数 · {Age(last.ReadAt, now)}前" : status;
    }

    public static string Value(DeviceStatus device) => device.Last is { } last
        ? $"{(last.IsEstimated ? "≈" : "")}{last.Percentage}%" : "–";

    public static string Age(DateTimeOffset at, DateTimeOffset now) =>
        $"{Math.Max(0, (long)(now - at).TotalMinutes)} 分钟";

    public static string Updated(DateTimeOffset? at, DateTimeOffset now, bool running, bool failed) =>
        running ? "查询中…" : failed ? "设备枚举未完成" : at switch
        {
            null => "等待更新",
            { } time when (now - time).TotalMinutes < 1 => "刚刚更新",
            { } time => $"{Age(time, now)}前更新"
        };

    public static string TrayModeShortLabel(TrayDisplayMode mode) => mode switch
    {
        TrayDisplayMode.LowestBattery => "最低电量",
        _ => "自定义"
    };

    public static string TrayModeLabel(TrayDisplayMode mode) => mode switch
    {
        TrayDisplayMode.LowestBattery => "只显示电量最低的一台",
        _ => "自定义"
    };

    /// <summary>单设备独立托盘图标的悬停提示。</summary>
    public static string DeviceTooltip(TrayModel model, DeviceStatus device)
    {
        const int limit = 127;
        string name = model.Name(device.Device);
        string suffix = DeviceTooltipSuffix(device, model.Settings.LowBatteryThreshold);
        int spare = Math.Max(4, limit - suffix.Length);
        return Shorten(name, spare) + suffix;
    }

    private static string DeviceTooltipSuffix(DeviceStatus device, int threshold)
    {
        // R1：休眠状态保留上次读数，悬停明确写「休眠中 · 上次 xx%」。
        if (device.Link == LinkState.Asleep)
            return device.Last is not null ? $" · 休眠中 · 上次 {Value(device)}" : " · 休眠中";
        var tone = Tone(device, threshold);
        if (!device.IsActive)
            return device.Last is not null ? $" · 已断开 · 上次 {Value(device)}" : " · 已断开";
        if (tone == BatteryTone.Idle)
            return $" · 上次 {Value(device)}";
        string tail = tone switch
        {
            BatteryTone.Charging => device.IsFull ? " 已充满" : " 充电中",
            BatteryTone.Low => " 电量低",
            BatteryTone.Warning => " 电量偏低",
            _ => ""
        };
        return $" {Value(device)}{tail}";
    }

    /// <summary>单图标模式的活跃设备悬停摘要。</summary>
    public static string Tooltip(TrayModel model)
    {
        const int limit = 127; // Shell 的 128 个 UTF-16 字符含结尾的 NUL。
        var primary = model.LowestTrayDevice;
        var devices = model.OrderedDevices().Where(device => device.IsActive && device != primary)
            .OrderBy(device => model.IsHidden(device.Device.Id))
            .ThenByDescending(device => device.IsOnline)
            .ThenBy(device => device.Percentage ?? int.MaxValue);
        var ordered = primary is null ? devices.AsEnumerable() : devices.Prepend(primary);
        var entries = ordered.Select(device =>
            (Name: model.Name(device.Device), Value: DeviceTooltipSuffix(device, model.Settings.LowBatteryThreshold)))
            .ToArray();

        if (entries.Length == 0)
            return "几格 · 点击打开面板";

        static string Overflow(int count) => count > 0 ? $"\n另有 {count} 台" : "";
        var first = entries[0];
        int spare = Math.Max(4, limit - first.Value.Length - Overflow(entries.Length - 1).Length);
        var tip = new System.Text.StringBuilder(Shorten(first.Name, spare)).Append(first.Value);
        int shown = 1;
        while (shown < entries.Length)
        {
            var entry = entries[shown];
            int available = limit - tip.Length - 1 - entry.Value.Length - Overflow(entries.Length - shown - 1).Length;
            if (available < Math.Min(entry.Name.Length, 8))
                break;
            tip.Append('\n').Append(Shorten(entry.Name, available)).Append(entry.Value);
            shown++;
        }
        return tip.Append(Overflow(entries.Length - shown)).ToString();
    }

    public static string Shorten(string value, int max)
    {
        if (value.Length <= max)
            return value;
        int count = max - 1;
        if (count > 0 && char.IsHighSurrogate(value[count - 1]))
            count--;
        return value[..count] + "…";
    }

    public static string ReminderTitle(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "鼠标该充电了",
        DeviceKind.Headset => "耳机该充电了",
        DeviceKind.Keyboard => "键盘该充电了",
        _ => "设备该充电了"
    };

    public static string Interval(int seconds) =>
        seconds % 60 == 0 ? $"{seconds / 60} 分钟" : $"{seconds} 秒";
}

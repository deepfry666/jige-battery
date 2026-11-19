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

internal readonly record struct IconAppearance(int? Percentage, BatteryTone Tone, Color Ink)
{
    public bool Full => Percentage == 100;
    public bool Underline => Tone == BatteryTone.Charging && !Full;
}

internal static class Presentation
{
    public static BatteryTone Tone(DeviceStatus? device) => device switch
    {
        not { Fresh: true } => BatteryTone.Idle,
        { IsCharging: true } => BatteryTone.Charging,
        { Percentage: <= 15 } => BatteryTone.Low,
        { Percentage: <= 30 } => BatteryTone.Warning,
        _ => BatteryTone.Normal
    };

    public static IconAppearance Icon(DeviceStatus? device, bool darkTaskbar)
    {
        var palette = Palette.Create(darkTaskbar);
        var tone = Tone(device);
        var color = tone switch
        {
            BatteryTone.Idle => palette.Ink.WithAlpha(140),
            BatteryTone.Normal => palette.Ink,
            _ => palette.Status(tone)
        };
        return new(device?.Percentage, tone, color);
    }

    public static int FilledCells(int? percent, int count) => ((percent ?? 0) * count + 50) / 100;

    public static string Status(DeviceStatus device, DateTimeOffset now)
    {
        if (device.Fresh)
            return Tone(device) switch
            {
                BatteryTone.Charging => "充电中",
                BatteryTone.Low => "电量低，该充电了",
                _ => device.Last?.IsEstimated == true ? "2.4G 已连接 · 电压估算" : "2.4G 已连接"
            };

        string status = (device.Link, device.Issue) switch
        {
            (LinkState.Asleep, _) => "休眠",
            (LinkState.Offline, _) => "离线",
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

    public static string Tooltip(TrayModel model)
    {
        const int limit = 127;
        var entries = model.OrderedDevices().Select(device =>
        {
            string tail = Tone(device) switch
            {
                BatteryTone.Idle when device.Last is not null => "（上次）",
                BatteryTone.Charging => " 充电中",
                BatteryTone.Low => " 电量低",
                _ => ""
            };
            return (Name: model.Name(device.Device), Value: $" {Value(device)}{tail}");
        }).ToArray();

        if (entries.Length == 0)
            return "几格 · 未找到支持的设备";
        string full = string.Join('\n', entries.Select(entry => entry.Name + entry.Value));
        if (full.Length <= limit)
            return full;

        // 名称过长时先压缩名称，保留每一行的百分比和状态。
        int count = 0, minimum = 0;
        foreach (var entry in entries)
        {
            int line = Math.Min(entry.Name.Length, 8) + entry.Value.Length + 1;
            if (minimum + line > limit - 1)
                break;
            minimum += line;
            count++;
        }
        bool omitted = count < entries.Length;
        int spare = limit - minimum + 1 - (omitted ? 2 : 0);
        var lines = new string[count];
        for (int i = 0; i < count; i++)
        {
            var (name, value) = entries[i];
            int baseLength = Math.Min(name.Length, 8);
            int extra = Math.Min(name.Length - baseLength, spare / (count - i));
            lines[i] = Shorten(name, baseLength + extra) + value;
            spare -= extra;
        }
        return string.Join('\n', lines) + (omitted ? "\n…" : "");
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

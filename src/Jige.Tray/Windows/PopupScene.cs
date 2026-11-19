using System.Runtime.Versioning;
using Jige.Core;
using static Windows.Win32.Graphics.DirectWrite.DWRITE_TEXT_ALIGNMENT;

namespace Jige.Tray.Windows;

internal enum PopupAction { None, Refresh, Settings, Manage, Later, Dismiss, Close }

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class PopupScene : IDisposable
{
    private readonly record struct HitBox(PopupAction Action, float X, float Y, float Width, float Height)
    {
        public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    }

    public const int Width = 344;
    public const int NoticeHeight = 144;
    public const int RowHeight = 48;
    public const int RowTop = 202;
    private const int FullBaseHeight = 247;
    private const int EmptyHeight = 157;
    private static readonly HitBox[] PanelButtons =
    [
        new(PopupAction.Refresh, 260, 12, 32, 32),
        new(PopupAction.Settings, 300, 12, 32, 32),
        new(PopupAction.Manage, 268, 0, 64, 37)
    ];
    private static readonly HitBox[] NoticeButtons =
    [
        new(PopupAction.Close, 306, 12, 24, 24),
        new(PopupAction.Later, 14, 97, 154, 32),
        new(PopupAction.Dismiss, 176, 97, 154, 32)
    ];

    private readonly Composition composition;
    private readonly Palette colors;
    private readonly bool acrylic;
    private readonly bool animations;
    private readonly Composition.Layer frame;
    private readonly Composition.Layer? refresh;
    private readonly Composition.Layer[] rows;
    private readonly List<Composition.Layer> pulses = [];
    private readonly TrayModel model;
    private readonly string? noticeId;
    private readonly int height;
    private DeviceStatus[] devices = [];
    private DeviceStatus? notice;
    private DateTimeOffset now;
    private bool reading;
    private int offset;
    private PopupAction hover;
    private PopupAction pressed;
    private PopupAction focused;
    private int hoverRow = -1;

    public static int MaximumRows(int height) => Math.Max(0, (height - FullBaseHeight) / RowHeight);
    public static int PanelHeight(bool any, int rowCount) => any ? FullBaseHeight + rowCount * RowHeight : EmptyHeight;

    public PopupScene(Composition composition, Palette colors, bool acrylic, bool animations,
        TrayModel model, string? noticeId, int height, int rowCount)
    {
        this.composition = composition;
        this.colors = colors;
        this.acrylic = acrylic;
        this.animations = animations;
        this.model = model;
        this.noticeId = noticeId;
        this.height = height;
        rows = new Composition.Layer[rowCount];
        var root = composition.Root;
        root.Clip(Width, height);
        frame = new(composition, root, 0, 0, Width, height);
        try
        {
            if (noticeId is null)
            {
                refresh = new(composition, root, 260, 12, 32, 32);
                for (int i = 0; i < rowCount; i++)
                    rows[i] = new(composition, root, 4, RowTop + i * RowHeight, Width - 8, RowHeight);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private bool IsNotice => noticeId is not null;
    private HitBox[] Buttons => IsNotice ? NoticeButtons : PanelButtons;
    private int MaxOffset => Math.Max(0, devices.Length - 1 - rows.Length);

    private HitBox Box(HitBox item) => item.Action == PopupAction.Manage ? item with { Y = height - 37 } : item;

    public void Render(DateTimeOffset time, bool running)
    {
        devices = model.OrderedDevices();
        notice = devices.FirstOrDefault(device => string.Equals(device.Device.Id, noticeId, StringComparison.OrdinalIgnoreCase));
        now = time;
        reading = running;
        offset = Math.Min(offset, MaxOffset);
        foreach (var pulse in pulses)
            pulse.Dispose();
        pulses.Clear();

        DrawFrame();
        for (int i = 0; i < rows.Length; i++)
            DrawRow(i);
        if (refresh is not null)
            refresh.Draw(DrawRefresh);
        if (!IsNotice && devices.Length > 0 && devices[0].IsCharging)
            AddPulse(composition.Root, devices[0], 165, 120, 13, 22, 3, 10);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = devices[1 + offset + i];
            if (row.IsCharging)
                AddPulse(rows[i], row, 221, 17, 5, 14, 1.5f, 5);
        }
        composition.Commit();
    }

    public void Open()
    {
        if (!animations)
            return;
        if (IsNotice)
            composition.Root.SlideIn(Width + 24);
        else
        {
            composition.Root.Open();
            for (int i = 0; i < rows.Length; i++)
                rows[i].Open(.04 + i * .025, row: true);
        }
        composition.Commit();
    }

    public void Close()
    {
        composition.Root.Close();
        composition.Commit();
    }

    public void Spin()
    {
        if (animations && refresh is not null)
        {
            refresh.Spin(16);
            composition.Commit();
        }
    }

    public PopupAction Hit(float x, float y)
    {
        foreach (var button in Buttons)
        {
            if (Box(button).Contains(x, y))
                return button.Action;
        }
        return PopupAction.None;
    }

    public void Pointer(float x, float y, bool? down = null)
    {
        var next = Hit(x, y);
        int row = -1;
        if (!IsNotice && x >= 4 && x < Width - 4 && y >= RowTop && y < RowTop + RowHeight * rows.Length)
            row = (int)(y - RowTop) / RowHeight;
        var nextPressed = down is true ? next : down is false || x < 0 ? PopupAction.None : pressed;
        bool buttonChanged = hover != next || pressed != nextPressed;
        if (!buttonChanged && row == hoverRow)
            return;

        int previousRow = hoverRow;
        var previousHover = hover;
        hover = next;
        pressed = nextPressed;
        hoverRow = row;
        if (buttonChanged)
        {
            DrawFrame();
            if (refresh is not null && (previousHover == PopupAction.Refresh || next == PopupAction.Refresh))
                refresh.Draw(DrawRefresh);
        }
        if (previousRow != row)
        {
            if (previousRow >= 0)
                DrawRow(previousRow);
            if (row >= 0)
                DrawRow(row);
        }
        composition.Commit();
    }

    public PopupAction Focused => focused;

    public void Tab(bool reverse)
    {
        int index = Array.FindIndex(Buttons, box => box.Action == focused);
        if (index < 0 && reverse)
            index = 0;
        index = (index + Buttons.Length + (reverse ? -1 : 1)) % Buttons.Length;
        focused = Buttons[index].Action;
        DrawFrame();
        composition.Commit();
    }

    public void Scroll(int step)
    {
        int next = Math.Clamp(offset + step, 0, MaxOffset);
        if (next == offset)
            return;
        offset = next;
        Render(DateTimeOffset.Now, reading);
    }

    private void DrawFrame() => frame.Draw(canvas =>
    {
        canvas.Fill(0, 0, Width, height, 8, acrylic ? colors.Flyout : colors.Solid);
        canvas.Stroke(.5f, .5f, Width - 1, height - 1, 7.5f, colors.Border);
        if (IsNotice)
        {
            DrawNotice(canvas);
            return;
        }

        canvas.Text("几格", 16, 12, 50, 32, 14, colors.Ink, 600);
        var updated = Presentation.Updated(model.UpdatedAt, now, reading, model.ScanFailed);
        canvas.Text(updated, 68, 12, 184, 32, 12, colors.Muted, align: DWRITE_TEXT_ALIGNMENT_TRAILING);
        foreach (var button in PanelButtons)
            ButtonBackground(canvas, Box(button));
        DrawSettings(canvas);
        if (devices.Length > 0)
            DrawHero(canvas, devices[0]);
        else
            canvas.Text("未找到支持的设备", 16, 60, 312, 46, 14, colors.Secondary, align: DWRITE_TEXT_ALIGNMENT_CENTER);

        int footer = height - 37;
        canvas.Line(0, footer + .5f, Width, footer + .5f, colors.Border);
        var settings = model.Settings;
        canvas.Text($"每 {settings.PollSeconds} 秒查询 · 游戏中 {Presentation.Interval(settings.GamePollSeconds)}",
            16, footer + 1, 246, 34, 12, colors.Muted, ellipsis: true);
        canvas.Text("管理设备", 268, footer + 1, 64, 34, 12, colors.Accent, align: DWRITE_TEXT_ALIGNMENT_CENTER);

        if (MaxOffset > 0 && rows.Length > 0)
        {
            float span = rows.Length * RowHeight - 8;
            float thumb = Math.Max(10, span * rows.Length / (devices.Length - 1));
            canvas.Fill(339, RowTop + 4 + (span - thumb) * offset / MaxOffset, 2, thumb, 1, colors.Muted.WithAlpha(150));
        }
    });

    private void DrawHero(Canvas canvas, DeviceStatus device)
    {
        canvas.Fill(8, 52, 328, 144, 7, colors.Layer);
        canvas.Stroke(8.5f, 52.5f, 327, 143, 6.5f, colors.LayerStroke);
        DeviceIcon(canvas, device.Device.Kind, 23, 67, 36, colors.AccentSoft, colors.Accent);
        float nameWidth = canvas.Text(model.Name(device.Device), 69, 65, 194, 22, 14, colors.Ink, 600, ellipsis: true);
        float tagX = 69 + Math.Min(nameWidth, 194) + 6;
        canvas.Fill(tagX, 69, 34, 16, 4, colors.Chip);
        canvas.Text("2.4G", tagX, 69, 34, 16, 10.5f, colors.Secondary, 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);
        DrawPin(canvas, 70, 88);
        canvas.Text("固定在托盘", 83, 86, 190, 18, 11.5f, colors.Accent);

        var tone = Presentation.Tone(device);
        var ink = tone switch
        {
            BatteryTone.Low => colors.Low,
            BatteryTone.Idle => colors.Muted,
            _ => colors.Ink
        };
        DrawNumber(canvas, device, 23, 109, 134, 52, 46, 18, 24, ink, false);
        DrawCells(canvas, device, 165, 120, 13, 22, 3, 10);
        canvas.Dot(26, 172, 3, colors.Status(tone));
        canvas.Text(Presentation.Status(device, now), 35, 162, 284, 22, 12, colors.Secondary, ellipsis: true);
    }

    private void DrawRow(int index) => rows[index].Draw(canvas =>
    {
        var device = devices[1 + offset + index];
        var tone = Presentation.Tone(device);
        if (hoverRow == index)
            canvas.Fill(0, 0, Width - 8, RowHeight, 6, colors.Hover);
        DeviceIcon(canvas, device.Device.Kind, 12, 8, 32, colors.Chip, colors.Secondary);
        string status = Presentation.Status(device, now);
        int split = status.IndexOf(" · 上次读数", StringComparison.Ordinal);
        canvas.Text(model.Name(device.Device), 56, split < 0 ? 5 : 1, 153, 19, 13.5f,
            tone == BatteryTone.Idle ? colors.Muted : colors.Ink, ellipsis: true);
        var statusColor = tone is BatteryTone.Low or BatteryTone.Charging ? colors.Status(tone) : colors.Muted;
        if (split < 0)
            canvas.Text(status, 56, 23, 161, 19, 12, statusColor, ellipsis: true);
        else
        {
            canvas.Text(status[..split], 56, 18, 161, 15, 12, statusColor, ellipsis: true);
            canvas.Text(status[(split + 3)..], 56, 32, 161, 15, 12, statusColor, ellipsis: true);
        }

        DrawCells(canvas, device, 221, 17, 5, 14, 1.5f, 5);
        var ink = tone switch
        {
            BatteryTone.Low => colors.Low,
            BatteryTone.Idle => colors.Muted,
            _ => colors.Ink
        };
        DrawNumber(canvas, device, 265, 7, 59, 34, 20, 12, 11, ink, true);
    });

    private void DrawNumber(Canvas canvas, DeviceStatus device, float x, float y, float width, float height,
        float numberSize, float percentSize, float approxSize, Color ink, bool rightAlign)
    {
        string value = device.Percentage?.ToString() ?? "–";
        string prefix = device.Last?.IsEstimated == true ? "≈" : "";
        string suffix = device.Percentage is null ? "" : "%";
        float offsetX = rightAlign
            ? width - canvas.Measure(value, numberSize, 600, true) -
                canvas.Measure(prefix, approxSize, 500) - canvas.Measure(suffix, percentSize, 600) - 3
            : 0;

        // 各部分使用不同字级，百分号贴近数字基线，估算号略抬高。
        offsetX += canvas.Text(prefix, x + offsetX, y + (rightAlign ? 2 : 0), width, height, approxSize, colors.Muted, 500);
        offsetX += prefix.Length > 0 ? 2 : 0;
        offsetX += canvas.Text(value, x + offsetX, y, width, height, numberSize, ink, 600, true);
        canvas.Text(suffix, x + offsetX + 1, y + (rightAlign ? 3 : 8), width, height, percentSize,
            rightAlign ? colors.Muted : colors.Secondary, 600);
    }

    private void DrawCells(Canvas canvas, DeviceStatus device, float x, float y, float w, float h, float radius, int count)
    {
        int filled = Presentation.FilledCells(device.Percentage, count);
        var ink = colors.Status(Presentation.Tone(device));
        for (int i = 0; i < count; i++)
        {
            if (i == filled && device.IsCharging)
            {
                // 动画层独占下一格。系统关闭动画时保留静态充电提示。
                if (!animations)
                    canvas.Fill(x + i * (w + 3), y, w, h, radius, ink.WithAlpha(140));
                continue;
            }
            canvas.Fill(x + i * (w + 3), y, w, h, radius, i < filled ? ink : colors.Empty);
        }
    }

    private void AddPulse(Composition.Layer parent, DeviceStatus device, float x, float y, float w, float h,
        float radius, int count)
    {
        int filled = Presentation.FilledCells(device.Percentage, count);
        if (!animations || filled >= count)
            return;
        var layer = new Composition.Layer(composition, parent, x + filled * (w + 3), y, w + 1, h + 1);
        pulses.Add(layer);
        layer.Draw(canvas => canvas.Fill(0, 0, w, h, radius, colors.Charging));
        layer.Breathe();
    }

    private void DrawNotice(Canvas canvas)
    {
        canvas.Battery(14, 16, 1, colors.Low);
        canvas.Text("几格", 38, 12, 220, 24, 12, colors.Secondary);
        var close = NoticeButtons[0];
        ButtonBackground(canvas, close);
        canvas.Line(314, 20, 322, 28, colors.Secondary, 1.3f);
        canvas.Line(322, 20, 314, 28, colors.Secondary, 1.3f);
        if (notice is null)
            return;
        DeviceIcon(canvas, notice.Device.Kind, 14, 46, 36, colors.Low.WithAlpha(36), colors.Low);
        canvas.Text(Presentation.ReminderTitle(notice.Device.Kind), 62, 44, 269, 21, 14, colors.Ink, 600);
        string value = $" 只剩 {Presentation.Value(notice)}";
        float valueWidth = canvas.Measure(value, 13) + 2;
        float available = 268 - valueWidth;
        float nameWidth = canvas.Text(model.Name(notice.Device), 62, 64, available, 20, 13, colors.Secondary,
            ellipsis: true);
        canvas.Text(value, 62 + Math.Min(nameWidth, available), 64, valueWidth, 20, 13, colors.Secondary);
        DrawNoticeButton(canvas, NoticeButtons[1], "1 小时后提醒", false);
        DrawNoticeButton(canvas, NoticeButtons[2], "知道了", true);
    }

    private void DrawNoticeButton(Canvas canvas, HitBox box, string label, bool primary)
    {
        canvas.Fill(box.X, box.Y, box.Width, box.Height, 5, primary ? colors.Accent : colors.Card);
        if (!primary)
            canvas.Stroke(box.X + .5f, box.Y + .5f, box.Width - 1, box.Height - 1, 4.5f, colors.CardStroke);
        ButtonBackground(canvas, box);
        canvas.Text(label, box.X, box.Y, box.Width, box.Height, 13, primary ? colors.ButtonText : colors.Ink,
            align: DWRITE_TEXT_ALIGNMENT_CENTER);
    }

    private void ButtonBackground(Canvas canvas, HitBox box)
    {
        if (hover == box.Action)
            canvas.Fill(box.X, box.Y, box.Width, box.Height, 5, pressed == box.Action ? colors.Press : colors.Hover);
        if (focused == box.Action)
            canvas.Stroke(box.X + .5f, box.Y + .5f, box.Width - 1, box.Height - 1, 4.5f, colors.Accent);
    }

    private void DrawSettings(Canvas canvas)
    {
        var ink = hover == PopupAction.Settings ? colors.Ink : colors.Secondary;
        canvas.Ring(316, 28, 2.2f, ink, 1.3f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.PI / 4;
            float x = MathF.Cos(angle), y = MathF.Sin(angle);
            canvas.Line(316 + x * 4.9f, 28 + y * 4.9f, 316 + x * 6.5f, 28 + y * 6.5f, ink, 1.3f);
        }
    }

    private void DrawRefresh(Canvas canvas)
    {
        var ink = hover == PopupAction.Refresh ? colors.Ink : colors.Secondary;
        canvas.Arc(16, 16, 5.5f, 0, MathF.PI * 1.75f, ink, 1.4f);
        canvas.Line(20.2f, 9.8f, 20.2f, 12.4f, ink, 1.4f);
        canvas.Line(20.2f, 12.4f, 17.6f, 12.4f, ink, 1.4f);
    }

    private void DrawPin(Canvas canvas, float x, float y)
    {
        var ink = colors.Accent;
        canvas.Line(x + 6, y + 1, x + 9, y + 4, ink, 1.6f);
        canvas.Line(x + 7, y + 3, x + 4, y + 6, ink, 2.2f);
        canvas.Line(x + 2, y + 4, x + 7, y + 9, ink, 1.6f);
        canvas.Line(x + 5, y + 7, x + 1, y + 11, ink, 1);
    }

    private static void DeviceIcon(Canvas canvas, DeviceKind kind, float x, float y, float size, Color fill, Color ink)
    {
        canvas.Fill(x, y, size, size, size == 36 ? 8 : 7, fill);
        float cx = x + size / 2, cy = y + size / 2;
        switch (kind)
        {
            case DeviceKind.Mouse:
                canvas.Stroke(cx - 4.5f, cy - 7.2f, 9, 14.3f, 4.5f, ink, 1.2f);
                canvas.Line(cx, cy - 7.2f, cx, cy - 2.3f, ink, 1.2f);
                break;
            case DeviceKind.Headset:
                canvas.Arc(cx, cy, 6, MathF.PI, 2 * MathF.PI, ink, 1.2f);
                canvas.Stroke(cx - 7, cy + 1.5f, 3.4f, 5.2f, 1.2f, ink, 1.2f);
                canvas.Stroke(cx + 3.6f, cy + 1.5f, 3.4f, 5.2f, 1.2f, ink, 1.2f);
                break;
            case DeviceKind.Keyboard:
                canvas.Stroke(cx - 7, cy - 4.5f, 14, 9, 2, ink, 1.2f);
                for (int i = -2; i <= 2; i++)
                    canvas.Dot(cx + i * 2.3f, cy - 1, .5f, ink);
                canvas.Line(cx - 3, cy + 2, cx + 3, cy + 2, ink, 1.2f);
                break;
            default:
                canvas.Battery(cx - 8, cy - 8, 1, ink);
                break;
        }
    }

    public void Dispose()
    {
        foreach (var pulse in pulses)
            pulse.Dispose();
        foreach (var row in rows)
            row?.Dispose();
        refresh?.Dispose();
        frame.Dispose();
    }
}

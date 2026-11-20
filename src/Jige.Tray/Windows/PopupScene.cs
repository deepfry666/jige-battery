using System.Runtime.Versioning;
using Jige.Core;
using static Windows.Win32.Graphics.DirectWrite.DWRITE_TEXT_ALIGNMENT;

namespace Jige.Tray.Windows;

internal enum PopupAction
{
    None, Refresh, Settings, Manage, Later, Dismiss, Close, Back, ExpandSetting, TrayMode, Threshold, PollInterval,
    GameSilent, Autostart, ShowBluetooth, Diagnostics, Updates, Rename, TrayToggle, Visibility, Remove, TaskbarSettings,
    OpenDiagnostics, CopyAndFeedback, ToggleOffline, Redetect
}

internal readonly record struct PopupTarget(PopupAction Action, int Value = 0, string? DeviceId = null)
{
    public static implicit operator PopupTarget(PopupAction action) => new(action);
}

[SupportedOSPlatform("windows10.0.17763")]
internal sealed partial class PopupScene : IDisposable
{
    private readonly record struct HitBox(PopupTarget Target, PanelRect Rect)
    {
        public HitBox(PopupAction action, float x, float y, float width, float height)
            : this(action, new PanelRect(x, y, width, height)) { }

        public PopupAction Action => Target.Action;
        public float X => Rect.X;
        public float Y => Rect.Y;
        public float Width => Rect.Width;
        public float Height => Rect.Height;
    }

    private readonly record struct MainRowEntry(DeviceStatus? Device, int OfflineCount, bool OfflineExpanded)
    {
        public bool IsOfflineToggle => Device is null;
        public static MainRowEntry ForDevice(DeviceStatus device) => new(device, 0, false);
        public static MainRowEntry ForOfflineToggle(int count, bool expanded) => new(null, count, expanded);
    }

    public const int Width = PanelLayout.Width;
    private const int RowHeight = PanelLayout.MainRowHeight;
    private const int RowTop = 202;
    private static readonly HitBox[] PanelButtons =
    [
        new(PopupAction.Refresh, 260, 12, 32, 32),
        new(PopupAction.Settings, 300, 12, 32, 32),
        new(PopupAction.Manage, 268, 0, 64, 37)
    ];
    private static readonly HitBox[] NoticeButtons =
    [
        new(PopupAction.Later, 14, 97, 154, 32),
        new(PopupAction.Dismiss, 176, 97, 154, 32)
    ];

    private static readonly HitBox[] WelcomeButtons =
    [
        new(PopupAction.TaskbarSettings, 14, 97, 154, 32),
        new(PopupAction.Dismiss, 176, 97, 154, 32)
    ];

    private readonly Composition composition;
    private readonly Palette colors;
    private readonly bool animations;
    private readonly Composition.Layer frame;
    private readonly Composition.Layer? refresh;
    private readonly Composition.Layer[] rows;
    private readonly List<Composition.Layer> pulses = [];
    private readonly TrayModel model;
    private readonly string? noticeId;
    private readonly PanelSize size;
    private int height => size.Height;
    private readonly List<HitBox> mainButtons = [];
    private readonly List<HitBox> subButtons = [];
    private Composition.Layer? gameThumb;
    private Composition.Layer? autostartThumb;
    private Composition.Layer? bluetoothThumb;
    private ThumbMotion? gameMotion, autostartMotion, bluetoothMotion;
    private string? error;
    private DiagnosticStatus diagnosticStatus;
    private bool diagnosticPreviewPending;
    private bool HasDiagnosticReport => diagnosticStatus is DiagnosticStatus.Ready or DiagnosticStatus.EditorFailed
        or DiagnosticStatus.FeedbackCopied or DiagnosticStatus.FeedbackFailed;
    private int Threshold => model.Settings.LowBatteryThreshold;
    private string? editorId;
    private DeviceStatus[] devices = [];
    private MainPanelPlan mainPlan = new(null, false, [], []);
    private MainRowEntry[] mainEntries = [];
    private bool offlineExpanded;
    private DeviceStatus? notice;
    private DateTimeOffset now;
    private bool reading;
    private int offset;
    private PopupTarget hover;
    private PopupTarget pressed;
    private PopupTarget focused;
    private int hoverRow = -1;

    public PopupScene(Composition composition, Palette colors, bool animations, TrayModel model,
        PanelPage page, string? noticeId, PanelSize size, int offset, int availableHeight)
    {
        this.composition = composition;
        this.colors = colors;
        this.animations = animations;
        this.model = model;
        this.noticeId = noticeId;
        this.size = size;
        this.offset = offset;
        Page = page;
        rows = new Composition.Layer[page == PanelPage.Main ? size.Rows : 0];
        if (page == PanelPage.Settings)
            Settings = new(availableHeight);
        frame = new(composition, composition.Root, 0, 0, Width, SurfaceHeight);
        try
        {
            if (Page == PanelPage.Main)
            {
                refresh = new(composition, frame, 260, 12, 32, 32);
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = new(composition, frame, 4, RowTop + i * RowHeight, Width - 8, RowHeight);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public PanelPage Page { get; }
    public PanelSize Size => size;
    public int Offset => offset;
    public SettingsPanel? Settings { get; }
    public float DisplayHeight => Settings?.Height ?? height;
    public int SurfaceHeight => Settings?.SurfaceHeight ?? height;
    public Composition.Layer Visual => frame;
    private bool IsNotice => Page is PanelPage.Notice or PanelPage.Welcome;
    private bool IsSubPage => Page is PanelPage.Settings or PanelPage.Devices;
    private IReadOnlyList<HitBox> Buttons => Page switch
    {
        PanelPage.Notice => NoticeButtons,
        PanelPage.Welcome => WelcomeButtons,
        PanelPage.Main => mainButtons,
        _ => subButtons
    };
    private int MaxOffset => Page switch
    {
        PanelPage.Main => Math.Max(0, mainEntries.Length - size.Rows),
        PanelPage.Devices => Math.Max(0, devices.Length - size.Rows),
        _ => 0
    };

    private HitBox Box(HitBox item) => item.Action == PopupAction.Manage
        ? item with { Rect = item.Rect with { Y = height - 37 } }
        : item;

    public void Render(DateTimeOffset time, bool running, string? settingsError, string? editing,
        DiagnosticStatus diagnostics, bool previewPending, bool showOfflineExpanded)
    {
        offlineExpanded = showOfflineExpanded;
        if (Page == PanelPage.Main)
        {
            mainPlan = model.BuildMainPanel();
            List<MainRowEntry> entries = [];
            foreach (var online in mainPlan.OnlineRows)
                entries.Add(MainRowEntry.ForDevice(online));
            if (mainPlan.OfflineRows.Length > 0)
            {
                entries.Add(MainRowEntry.ForOfflineToggle(mainPlan.OfflineRows.Length, offlineExpanded));
                if (offlineExpanded)
                {
                    foreach (var off in mainPlan.OfflineRows)
                        entries.Add(MainRowEntry.ForDevice(off));
                }
            }
            mainEntries = [.. entries];
            devices = model.PanelDevices();
        }
        else
        {
            devices = model.OrderedForManagement();
            mainEntries = [];
        }

        error = settingsError;
        diagnosticStatus = diagnostics;
        diagnosticPreviewPending = previewPending;
        editorId = editing;
        notice = model.OrderedDevices().FirstOrDefault(device => string.Equals(device.Device.Id, noticeId, StringComparison.OrdinalIgnoreCase));
        now = time;
        reading = running;
        offset = Math.Min(offset, MaxOffset);
        RebuildMainButtons();

        foreach (var pulse in pulses)
            pulse.Dispose();
        pulses.Clear();

        Redraw();
        for (int i = 0; i < rows.Length; i++)
            DrawRow(i);
        if (refresh is not null)
            refresh.Draw(DrawRefresh);
        if (Page == PanelPage.Main && mainPlan.Hero is { IsCharging: true } hero)
            AddPulse(frame, hero, 172, 120, 12, 22, 3, 10);
        for (int i = 0; i < rows.Length; i++)
        {
            if (offset + i < mainEntries.Length && mainEntries[offset + i].Device is { IsCharging: true } row)
                AddPulse(rows[i], row, 217, 17, 5, 14, 1.5f, 5);
        }
    }

    private void RebuildMainButtons()
    {
        if (Page != PanelPage.Main)
            return;
        mainButtons.Clear();
        mainButtons.AddRange(PanelButtons);
        if (diagnosticPreviewPending)
            mainButtons.Add(new(PopupAction.OpenDiagnostics, 80, 12, 172, 32));
        for (int i = 0; i < rows.Length; i++)
        {
            if (offset + i < mainEntries.Length && mainEntries[offset + i].IsOfflineToggle)
                mainButtons.Add(new(PopupAction.ToggleOffline, 4, RowTop + i * RowHeight, Width - 8, RowHeight));
        }
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

    public PopupTarget Hit(float x, float y)
    {
        foreach (var button in Buttons)
        {
            if (Box(button).Rect.Contains(x, y))
                return button.Target;
        }
        return default;
    }

    public PanelRect? TargetRect(PopupTarget target)
    {
        foreach (var button in Buttons)
        {
            if (button.Target == target)
                return Box(button).Rect;
        }
        return null;
    }

    public void Pointer(float x, float y, bool? down = null)
    {
        var next = Hit(x, y);
        int row = -1;
        float top = Page == PanelPage.Devices ? PanelLayout.DeviceRowsTop : RowTop;
        float step = Page == PanelPage.Devices ? PanelLayout.DeviceRowHeight : RowHeight;
        if (Page is PanelPage.Main or PanelPage.Devices && x >= 4 && x < Width - 4 &&
            y >= top && y < top + step * size.Rows)
            row = (int)((y - top) / step);

        var nextPressed = down is true ? next : down is false || x < 0 ? default : pressed;
        bool buttonChanged = hover != next || pressed != nextPressed;
        if (!buttonChanged && row == hoverRow)
            return;

        int previousRow = hoverRow;
        var previousHover = hover;
        hover = next;
        pressed = nextPressed;
        hoverRow = row;
        if (buttonChanged || IsSubPage)
        {
            DrawFrame();
            if (refresh is not null && (previousHover.Action == PopupAction.Refresh || next.Action == PopupAction.Refresh))
                refresh.Draw(DrawRefresh);
        }
        if (Page == PanelPage.Main && previousRow != row)
        {
            if (previousRow >= 0)
                DrawRow(previousRow);
            if (row >= 0)
                DrawRow(row);
        }
        composition.Commit();
    }

    public PopupTarget Focused => focused;

    public void Tab(bool reverse)
    {
        if (Buttons.Count == 0)
            return;
        int index = -1;
        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i].Target == focused)
            {
                index = i;
                break;
            }
        }
        if (index < 0 && reverse)
            index = 0;
        index = (index + Buttons.Count + (reverse ? -1 : 1)) % Buttons.Count;
        focused = Buttons[index].Target;
        DrawFrame();
        if (Page == PanelPage.Main)
        {
            for (int i = 0; i < rows.Length; i++)
                DrawRow(i);
        }
        composition.Commit();
    }

    public bool Scroll(int step)
    {
        if (Settings is { } settings)
            return settings.Scroll(step);
        int next = Math.Clamp(offset + step, 0, MaxOffset);
        if (next == offset)
            return false;
        offset = next;
        return true;
    }

    public void Redraw()
    {
        DrawFrame();
        if (Settings is not null)
            DrawSwitchThumbs();
    }

    private void DrawFrame() => frame.Draw(canvas =>
    {
        if (IsNotice)
        {
            DrawNotice(canvas);
            return;
        }

        if (IsSubPage)
        {
            DrawSubPage(canvas);
            return;
        }

        canvas.BrandIcon(15, 18, 20);
        canvas.Text("几格", 40, 12, 38, 32, 14, colors.Ink, 600);
        var updated = diagnosticPreviewPending ? "查看诊断报告" :
            (diagnosticStatus == DiagnosticStatus.Ready ? null : DiagnosticExport.Label(diagnosticStatus)) ??
            error ?? Presentation.Updated(model.UpdatedAt, now, reading, model.ScanFailed);
        foreach (var button in Buttons)
        {
            if (button.Action != PopupAction.ToggleOffline)
                ButtonBackground(canvas, Box(button));
        }
        canvas.Text(updated, 80, 12, 172, 32, 12, diagnosticPreviewPending ? colors.Accent : colors.Muted,
            align: DWRITE_TEXT_ALIGNMENT_TRAILING, ellipsis: true);
        DrawSettings(canvas);
        if (mainPlan.Hero is { } hero)
            DrawHero(canvas, hero, mainPlan.HeroDisconnected);
        else
            canvas.Text("未找到支持的设备", 16, 60, 312, 46, 14, colors.Secondary, align: DWRITE_TEXT_ALIGNMENT_CENTER);

        // 主面板底部不再显示查询间隔文字，只保留「管理设备」（B9）。
        int footer = height - 37;
        canvas.Line(0, footer + .5f, Width, footer + .5f, colors.Border);
        canvas.Text("管理设备", 268, footer + 1, 64, 34, 12, colors.Accent, align: DWRITE_TEXT_ALIGNMENT_CENTER);

        if (MaxOffset > 0 && rows.Length > 0)
        {
            float span = rows.Length * RowHeight - 8;
            float thumb = Math.Max(10, span * rows.Length / mainEntries.Length);
            canvas.Fill(339, RowTop + 4 + (span - thumb) * offset / MaxOffset, 2, thumb, 1, colors.Muted.WithAlpha(150));
        }
    });

    private void DrawHero(Canvas canvas, DeviceStatus device, bool disconnected)
    {
        canvas.Fill(8, 52, 328, 144, 7, colors.Layer);
        canvas.Stroke(8.5f, 52.5f, 327, 143, 6.5f, colors.LayerStroke);
        DeviceIcon(canvas, device.Device.Kind, 23, 67, 36,
            disconnected ? colors.Chip : colors.AccentSoft,
            disconnected ? colors.Muted : colors.Accent);
        float nameWidth = canvas.Text(model.Name(device.Device), 69, 65, 194, 22, 14,
            disconnected ? colors.Secondary : colors.Ink, 600, ellipsis: true);
        float tagX = 69 + Math.Min(nameWidth, 194) + 6;
        canvas.Fill(tagX, 69, 34, 16, 4, colors.Chip);
        canvas.Text(Presentation.Connection(device.Device), tagX, 69, 34, 16, 10.5f, colors.Secondary, 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);

        // 顶部卡片不再显示「固定在托盘」；无在线设备时明确标明「已断开」，同时展示适配徽标（A4 / B4 / B7）。
        float metaX = 69;
        if (disconnected)
        {
            float discWidth = canvas.Text("已断开 · ", metaX, 86, 80, 18, 11.5f, colors.Muted);
            metaX += discWidth;
        }
        DrawBadge(canvas, device.Device, metaX, 87.5f, dimmed: false);

        var tone = Presentation.Tone(device, Threshold);
        var ink = tone switch
        {
            BatteryTone.Low or BatteryTone.Warning or BatteryTone.Charging => colors.Status(tone),
            BatteryTone.Idle => colors.Muted,
            _ => colors.Ink
        };
        DrawNumber(canvas, device, 23, 109, 145, 52, 44, 18, 24, ink, false);
        DrawCells(canvas, device, 172, 120, 12, 22, 3, 10);
        canvas.Dot(26, 172, 3, colors.Status(tone));
        canvas.Text(Presentation.Status(device, now, Threshold), 35, 162, 284, 22, 12,
            tone is BatteryTone.Low or BatteryTone.Warning or BatteryTone.Charging ? colors.Status(tone) : colors.Secondary,
            ellipsis: true);
    }

    private void DrawRow(int index) => rows[index].Draw(canvas =>
    {
        if (offset + index >= mainEntries.Length)
            return;
        var entry = mainEntries[offset + index];
        if (entry.IsOfflineToggle)
        {
            bool hot = hoverRow == index || hover.Action == PopupAction.ToggleOffline;
            if (hot)
                canvas.Fill(0, 0, Width - 8, RowHeight, 6, pressed.Action == PopupAction.ToggleOffline ? colors.Press : colors.Hover);
            if (focused.Action == PopupAction.ToggleOffline)
                canvas.Stroke(.5f, .5f, Width - 9, RowHeight - 1, 5.5f, colors.Accent);
            canvas.Line(12, .5f, Width - 20, .5f, colors.Border);
            canvas.Text($"离线设备（{entry.OfflineCount}）", 14, 0, 240, RowHeight, 12.5f, colors.Muted, 600);
            DrawChevron(canvas, Width - 28, RowHeight / 2f, entry.OfflineExpanded ? 1f : 0f);
            return;
        }

        var device = entry.Device!;
        var tone = Presentation.Tone(device, Threshold);
        if (hoverRow == index)
            canvas.Fill(0, 0, Width - 8, RowHeight, 6, colors.Hover);
        DeviceIcon(canvas, device.Device.Kind, 12, 8, 32, colors.Chip, colors.Secondary);
        string status = Presentation.Status(device, now, Threshold);
        int split = status.IndexOf(" · 上次读数", StringComparison.Ordinal);
        bool isGeneric = DeviceVerification.Badge(device.Device) == AdaptationBadge.Generic;
        float nameMaxWidth = isGeneric ? 98 : 153;
        float nameWidth = canvas.Text(model.Name(device.Device), 56, split < 0 ? 5 : 1, nameMaxWidth, 19, 13.5f,
            tone == BatteryTone.Idle ? colors.Muted : colors.Ink, ellipsis: true);
        if (isGeneric)
            DrawBadge(canvas, device.Device, 56 + Math.Min(nameWidth, nameMaxWidth) + 5, split < 0 ? 7 : 3, dimmed: tone == BatteryTone.Idle);
        var statusColor = tone is BatteryTone.Low or BatteryTone.Warning or BatteryTone.Charging ? colors.Status(tone) : colors.Muted;
        if (split < 0)
            canvas.Text(status, 56, 23, 161, 19, 12, statusColor, ellipsis: true);
        else
        {
            canvas.Text(status[..split], 56, 18, 161, 15, 12, statusColor, ellipsis: true);
            canvas.Text(status[(split + 3)..], 56, 32, 161, 15, 12, statusColor, ellipsis: true);
        }

        DrawCells(canvas, device, 217, 17, 5, 14, 1.5f, 5);
        var ink = tone switch
        {
            BatteryTone.Low or BatteryTone.Warning or BatteryTone.Charging => colors.Status(tone),
            BatteryTone.Idle => colors.Muted,
            _ => colors.Ink
        };
        DrawNumber(canvas, device, 260, 7, 64, 34, 20, 12, 11, ink, true);
    });

    private void DrawBadge(Canvas canvas, DeviceIdentity device, float x, float y, bool dimmed)
    {
        var badge = DeviceVerification.Badge(device);
        string label = DeviceVerification.Label(badge);
        float badgeWidth = canvas.Measure(label, 10.5f, 600) + 10;
        var (bg, fg) = badge switch
        {
            AdaptationBadge.Verified => (colors.Charging.WithAlpha(41), colors.Charging),
            AdaptationBadge.Generic => (colors.AccentSoft, colors.Accent),
            _ => (colors.Chip, colors.Secondary)
        };
        if (dimmed)
        {
            bg = bg.WithAlpha((byte)Math.Round(bg.Alpha * .45));
            fg = fg.WithAlpha((byte)Math.Round(fg.Alpha * .45));
        }
        canvas.Fill(x, y, badgeWidth, 15.5f, 4, bg);
        canvas.Text(label, x + 5, y, badgeWidth - 10, 15.5f, 10.5f, fg, 600);
    }

    private void DrawNumber(Canvas canvas, DeviceStatus device, float x, float y, float width, float height,
        float numberSize, float percentSize, float approxSize, Color ink, bool rightAlign)
    {
        string value = device.Percentage?.ToString() ?? "–";
        string prefix = device.Last?.IsEstimated == true ? "≈" : "";
        string suffix = device.Percentage is null ? "" : "%";
        float prefixWidth = prefix.Length == 0 ? 0 : canvas.Measure(prefix, approxSize, 500) + 2;
        float numberWidth = canvas.Measure(value, numberSize, 600, true) + 1;
        float suffixWidth = suffix.Length == 0 ? 0 : canvas.Measure(suffix, percentSize, 600) + 2;
        float offsetX = rightAlign ? Math.Max(0, width - prefixWidth - numberWidth - suffixWidth) : 0;

        // 给每段实际测得的宽度，避免 DirectWrite 在百分号或估算号前折行。
        if (prefixWidth > 0)
            canvas.Text(prefix, x + offsetX, y + (rightAlign ? 2 : 0), prefixWidth, height, approxSize, colors.Muted, 500);
        canvas.Text(value, x + offsetX + prefixWidth, y, numberWidth, height, numberSize, ink, 600, true);
        if (suffixWidth > 0)
            canvas.Text(suffix, x + offsetX + prefixWidth + numberWidth, y + (rightAlign ? 3 : 8),
                suffixWidth, height, percentSize, ink, 600);
    }

    private void DrawCells(Canvas canvas, DeviceStatus device, float x, float y, float w, float h, float radius, int count)
    {
        int filled = Presentation.FilledCells(device.Percentage, count);
        var ink = colors.Status(Presentation.Tone(device, Threshold));
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
        canvas.BrandIcon(14, 15, 20);
        canvas.Text("几格", 40, 12, 220, 24, 12, colors.Secondary);
        if (Page == PanelPage.Welcome)
        {
            canvas.Text("几格在这里。", 16, 40, 312, 22, 14, colors.Ink, 600);
            canvas.Text("把图标拖到时钟旁，就能一直看到电量", 16, 63, 312, 21, 13, colors.Secondary);
            DrawNoticeButton(canvas, WelcomeButtons[0], "打开任务栏设置", false);
            DrawNoticeButton(canvas, WelcomeButtons[1], "知道了", true);
            return;
        }
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
        DrawNoticeButton(canvas, NoticeButtons[0], "1 小时后提醒", false);
        DrawNoticeButton(canvas, NoticeButtons[1], "知道了", true);
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

    private void ButtonBackground(Canvas canvas, HitBox box, float radius = 5)
    {
        if (hover == box.Target)
            canvas.Fill(box.X, box.Y, box.Width, box.Height, radius, pressed == box.Target ? colors.Press : colors.Hover);
        if (focused == box.Target)
            canvas.Stroke(box.X + .5f, box.Y + .5f, box.Width - 1, box.Height - 1, radius - .5f, colors.Accent);
    }

    private void DrawSettings(Canvas canvas)
    {
        var ink = hover.Action == PopupAction.Settings ? colors.Ink : colors.Secondary;
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
        var ink = hover.Action == PopupAction.Refresh ? colors.Ink : colors.Secondary;
        canvas.Arc(16, 16, 5.5f, 0, MathF.PI * 1.75f, ink, 1.4f);
        canvas.Line(20.2f, 9.8f, 20.2f, 12.4f, ink, 1.4f);
        canvas.Line(20.2f, 12.4f, 17.6f, 12.4f, ink, 1.4f);
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
        gameThumb?.Dispose();
        autostartThumb?.Dispose();
        bluetoothThumb?.Dispose();
        frame.Dispose();
    }
}

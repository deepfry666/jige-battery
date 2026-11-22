using static Windows.Win32.Graphics.DirectWrite.DWRITE_TEXT_ALIGNMENT;

namespace Jige.Tray.Windows;

internal sealed partial class PopupScene
{
    private int DevicesFooter => height - (hasDiagnosticFooter ? PanelLayout.DiagnosticFooterHeight : PanelLayout.FooterHeight);
    private const float TrayCheckX = 261, EyeX = 301;

    private readonly record struct ThumbMotion(float From, float To, long Started)
    {
        public float At(long time) => From + (To - From) *
            (float)PanelLayout.Ease((time - Started) / (double)PanelLayout.SwitchMilliseconds);
    }

    private void DrawSubPage(Canvas canvas)
    {
        subButtons.Clear();
        if (Settings is { } settings)
        {
            SettingRow? optionRow = focused.Action switch
            {
                PopupAction.TrayMode or PopupAction.TrayToggle => SettingRow.TrayMode,
                PopupAction.DisplayLocation => SettingRow.DisplayLocation,
                PopupAction.TaskbarStyle => SettingRow.TaskbarStyle,
                PopupAction.TaskbarColor => SettingRow.TaskbarColor,
                PopupAction.Theme => SettingRow.Theme,
                PopupAction.Threshold or PopupAction.GameEntryReminder or PopupAction.FullChargeReminder => SettingRow.Threshold,
                PopupAction.PollInterval => SettingRow.PollInterval,
                _ => null
            };
            if (optionRow is { } row && row != settings.Expanded)
                focused = new(PopupAction.ExpandSetting, (int)row);
        }
        var back = new HitBox(PopupAction.Back, PanelLayout.Back);
        subButtons.Add(back);
        ButtonBackground(canvas, back);
        var arrow = hover == back.Target ? colors.Ink : colors.Secondary;
        canvas.Line(27, 22, 22, 27, arrow, 1.5f);
        canvas.Line(22, 27, 27, 32, arrow, 1.5f);
        string title = Page switch
        {
            PanelPage.Settings => "设置",
            PanelPage.Updates => "检查更新",
            _ => "管理设备"
        };
        canvas.Text(title, 45, 11, 200, 32, 14, colors.Ink, 600);

        if (Page == PanelPage.Settings)
            DrawSettingsPage(canvas);
        else if (Page == PanelPage.Updates)
            DrawUpdatesPage(canvas);
        else
            DrawDevicesPage(canvas);
        if (!subButtons.Any(button => button.Target == focused))
            focused = default;
    }

    private void DrawSettingsPage(Canvas canvas)
    {
        var layout = Settings!;
        var settings = model.Settings;
        // 三组卡片：显示 / 电量 / 通用，每组上方一个小标题。
        for (int section = 0; section < SettingsPanel.SectionTitles.Length; section++)
        {
            var card = layout.SectionRect(section);
            if (!layout.Compact)
                canvas.Text(SettingsPanel.SectionTitles[section], SettingsPanel.CardX + 8, card.Y - SettingsPanel.HeaderHeight,
                    200, SettingsPanel.HeaderHeight, 12, colors.Secondary, 600);
            canvas.Fill(card.X, card.Y, card.Width, card.Height, 7, colors.Layer);
        }
        foreach (var row in SettingsPanel.Rows)
        {
            var rect = layout.RowRect(row);
            var target = row switch
            {
                SettingRow.GameSilent => PopupAction.GameSilent,
                SettingRow.Autostart => PopupAction.Autostart,
                SettingRow.AutoUpdate => PopupAction.AutoUpdate,
                SettingRow.RuntimeEstimate => PopupAction.RuntimeEstimate,
                _ => new PopupTarget(PopupAction.ExpandSetting, (int)row)
            };
            var button = new HitBox(target, rect);
            subButtons.Add(button);
            int roundSide = SettingsPanel.IsSectionStart(row) ? -1
                : SettingsPanel.IsSectionEnd(row) && layout.Progress(row) == 0 ? 1 : 0;
            DrawSettingBackground(canvas, button, roundSide);
            if (!SettingsPanel.IsSectionStart(row))
                canvas.Line(rect.X, rect.Y + .5f, rect.X + rect.Width, rect.Y + .5f, colors.LayerStroke);

            DrawSettingIcon(canvas, row, 25, rect.Y + (rect.Height - 16) / 2);
            string name = row switch
            {
                SettingRow.TrayMode => "显示哪些设备",
                SettingRow.DisplayLocation => "显示位置",
                SettingRow.TaskbarStyle => "任务栏样式",
                SettingRow.TaskbarColor => "任务栏配色",
                SettingRow.Theme => "外观模式",
                SettingRow.Threshold => "电量提醒",
                SettingRow.GameSilent => "游戏中不打扰",
                SettingRow.PollInterval => "查询间隔",
                SettingRow.AutoUpdate => "自动检查更新",
                SettingRow.RuntimeEstimate => "预计可用时间",
                _ => "开机自启"
            };
            if (row == SettingRow.RuntimeEstimate)
            {
                // 明确告知用户：数字由本机历史读数推算，不代表实际续航。
                float topPad = MathF.Max(2, (rect.Height - 36) / 2);
                canvas.Text(name, 54, rect.Y + topPad, 200, 19, 13.5f, colors.Ink);
                canvas.Text("含充满时间 · 仅为估算，不代表实际", 54, rect.Y + topPad + 18, 200, 16, 11, colors.Muted, ellipsis: true);
            }
            else
                canvas.Text(name, 54, rect.Y, 122, rect.Height, 13.5f, colors.Ink);

            if (row is SettingRow.GameSilent or SettingRow.Autostart or SettingRow.AutoUpdate or SettingRow.RuntimeEstimate)
            {
                DrawSwitch(canvas, SwitchRect(rect), row switch
                {
                    SettingRow.GameSilent => settings.GameSilent,
                    SettingRow.Autostart => settings.StartWithWindows,
                    SettingRow.AutoUpdate => settings.AutoUpdateEnabled,
                    _ => settings.ShowRuntimeEstimate
                });
                continue;
            }

            string value = row switch
            {
                SettingRow.TrayMode => Presentation.TrayModeShortLabel(settings.TrayMode),
                SettingRow.DisplayLocation => SettingsPanel.DisplayLocationLabel(settings.DisplayLocation, displayLocationFallback),
                SettingRow.TaskbarStyle => Presentation.TaskbarStyleLabel(settings.TaskbarStyle),
                SettingRow.TaskbarColor => Presentation.TaskbarColorLabel(settings.TaskbarColor),
                SettingRow.Theme => Presentation.ThemeModeLabel(settings.Theme),
                SettingRow.Threshold => $"{settings.LowBatteryThreshold}%",
                _ => Presentation.Interval(settings.PollSeconds)
            };
            canvas.Text(value, SettingsPanel.Right - 164, rect.Y, 140, rect.Height, 13, colors.Muted,
                align: DWRITE_TEXT_ALIGNMENT_TRAILING, ellipsis: true);
            DrawChevron(canvas, SettingsPanel.Right - 6, rect.Y + rect.Height / 2, layout.Progress(row));
            DrawSettingChoices(canvas, row);
        }
        for (int section = 0; section < SettingsPanel.SectionTitles.Length; section++)
        {
            var card = layout.SectionRect(section);
            canvas.Stroke(card.X + .5f, card.Y + .5f, card.Width - 1, card.Height - 1, 6.5f, colors.LayerStroke);
        }

        float footer = layout.Height - layout.FooterHeight;
        if (HasDiagnosticReport)
        {
            DrawDiagnosticReportFooter(canvas, footer,
                DiagnosticExport.Label(diagnosticStatus) ?? "诊断已导出",
                $"几格 {ReleaseVersion.Display}",
                (PopupAction.Diagnostics, "诊断信息"),
                (PopupAction.Updates, "检查更新"));
        }
        else
        {
            canvas.Line(1, footer + .5f, Width - 1, footer + .5f, colors.Border);
            float linksLeft = DrawLinksRight(canvas, Width - 17, footer + 11, 20, 14,
                (PopupAction.Diagnostics, "诊断信息"),
                (PopupAction.Updates, "检查更新"));
            string about = DiagnosticExport.Label(diagnosticStatus) ?? error ?? $"几格 {ReleaseVersion.Display}";
            bool isError = diagnosticStatus is DiagnosticStatus.Failed or DiagnosticStatus.EditorFailed
                or DiagnosticStatus.FeedbackFailed || error is not null;
            canvas.Text(about, 17, footer + 11, MathF.Max(40, linksLeft - 29), 20, 12,
                isError ? colors.Low : colors.Muted, ellipsis: true);
        }
    }

    public void ToggleSetting(SettingRow row, long time)
    {
        var choices = Settings!.Choices(row);
        int selected = Array.FindIndex(choices, choice => IsSelected(choice.Target));
        Settings.Toggle(row, time, animations, selected);
    }

    private bool IsSelected(PopupTarget target) => target.Action switch
    {
        PopupAction.TrayMode => target.Value == (int)model.Settings.TrayMode,
        PopupAction.DisplayLocation => target.Value == (int)model.Settings.DisplayLocation,
        PopupAction.TaskbarStyle => target.Value == (int)model.Settings.TaskbarStyle,
        PopupAction.TaskbarColor => target.Value == (int)model.Settings.TaskbarColor,
        PopupAction.Theme => target.Value == (int)model.Settings.Theme,
        PopupAction.Threshold => target.Value == model.Settings.LowBatteryThreshold,
        PopupAction.PollInterval => target.Value == model.Settings.PollSeconds,
        _ => false
    };

    private void DrawSettingChoices(Canvas canvas, SettingRow row)
    {
        var layout = Settings!;
        var area = layout.OptionsRect(row);
        if (area.Height <= 0)
            return;

        var choices = layout.Choices(row);
        int offset = layout.Offset(row);
        int end = offset + layout.VisibleCount(row);
        canvas.PushClip(area);
        try
        {
            for (int index = offset; index < end; index++)
            {
                var choice = choices[index];
                var rect = layout.OptionRect(row, index);
                var hit = layout.OptionHitRect(row, index);
                if (hit.Height > 0)
                {
                    subButtons.Add(new(choice.Target, hit));
                    int optionRound = SettingsPanel.IsSectionEnd(row) && index == end - 1 ? 1 : 0;
                    DrawSettingBackground(canvas, new(choice.Target, rect), optionRound);
                }
                if (choice.Target is { Action: PopupAction.TrayToggle, DeviceId: { } id })
                {
                    canvas.Text(choice.Label, 62, rect.Y, SettingsPanel.Right - 84, rect.Height, 13, colors.Ink,
                        ellipsis: true);
                    DrawCheckbox(canvas, SettingsPanel.Right - 16, rect.Y + 9, model.Settings.IsInTray(id), true, colors.Muted);
                    continue;
                }
                if (choice.Target.Action is PopupAction.GameEntryReminder or PopupAction.FullChargeReminder)
                {
                    bool on = choice.Target.Action == PopupAction.GameEntryReminder
                        ? model.Settings.GameEntryReminderEnabled : model.Settings.FullChargeReminder;
                    canvas.Text(choice.Label, 54, rect.Y, SettingsPanel.Right - 76, rect.Height, 13, colors.Ink,
                        ellipsis: true);
                    DrawCheckbox(canvas, SettingsPanel.Right - 16, rect.Y + 9, on, true, colors.Muted);
                    continue;
                }
                bool selected = IsSelected(choice.Target);
                string label = row == SettingRow.DisplayLocation
                    ? SettingsPanel.DisplayLocationOptionLabel(choice, displayLocationFallback)
                    : choice.Label;
                bool showSwatches = choice.Target.Action == PopupAction.TaskbarColor;
                float textWidth = showSwatches ? SettingsPanel.Right - 110 : SettingsPanel.Right - 76;
                canvas.Text(label, 54, rect.Y, textWidth, rect.Height, 13, colors.Ink,
                    selected ? 600 : 400, ellipsis: true);
                if (showSwatches)
                {
                    var (fgSwatch, lowSwatch, chgSwatch) = TaskbarStripPalette.PreviewSwatches(
                        (TaskbarStripColorScheme)choice.Target.Value, darkPanel: darkTaskbar);
                    float cy = rect.Y + rect.Height / 2;
                    DrawColorDot(canvas, SettingsPanel.Right - 46, cy, fgSwatch);
                    DrawColorDot(canvas, SettingsPanel.Right - 36, cy, lowSwatch);
                    DrawColorDot(canvas, SettingsPanel.Right - 26, cy, chgSwatch);
                }
                if (selected)
                {
                    float x = SettingsPanel.Right - 16, y = rect.Y + 10;
                    canvas.Line(x + 3, y + 7.2f, x + 5.6f, y + 9.8f, colors.Accent, 1.6f);
                    canvas.Line(x + 5.6f, y + 9.8f, x + 11, y + 4.4f, colors.Accent, 1.6f);
                }
            }

            if (layout.MaxOffset(row) > 0)
            {
                float span = layout.VisibleCount(row) * SettingsPanel.OptionHeight - 8;
                float thumb = Math.Max(10, span * layout.VisibleCount(row) / choices.Length);
                canvas.Fill(area.X + area.Width - 4, area.Y + 4 + (span - thumb) * offset / layout.MaxOffset(row),
                    2, thumb, 1, colors.Muted.WithAlpha(150));
            }
        }
        finally
        {
            canvas.PopClip();
        }
    }

    private void DrawSettingBackground(Canvas canvas, HitBox button, int roundSide = 0)
    {
        if (hover == button.Target)
        {
            var fill = pressed == button.Target ? colors.Press : colors.Hover;
            if (roundSide != 0)
            {
                // 把靠卡片内部的圆角画到裁剪区外，避免半透明填充叠加。
                float y = roundSide > 0 ? button.Y - 6 : button.Y;
                canvas.PushClip(button.Rect);
                try
                {
                    canvas.Fill(button.X, y, button.Width, button.Height + 6, 6, fill);
                }
                finally
                {
                    canvas.PopClip();
                }
            }
            else
                canvas.Fill(button.X, button.Y, button.Width, button.Height, 0, fill);
        }
        if (focused == button.Target)
            canvas.Stroke(button.X + 1, button.Y + 1, button.Width - 2, button.Height - 2, roundSide != 0 ? 5 : 0, colors.Accent);
    }

    private void DrawChevron(Canvas canvas, float cx, float cy, float progress)
    {
        float angle = progress * MathF.PI / 2;
        float sin = MathF.Sin(angle), cos = MathF.Cos(angle);
        (float X, float Y) Rotate(float x, float y) => (cx + x * cos - y * sin, cy + x * sin + y * cos);
        var a = Rotate(-1.5f, -3.5f);
        var b = Rotate(2, 0);
        var c = Rotate(-1.5f, 3.5f);
        canvas.Line(a.X, a.Y, b.X, b.Y, colors.Muted, 1.4f);
        canvas.Line(b.X, b.Y, c.X, c.Y, colors.Muted, 1.4f);
    }

    private void DrawSettingIcon(Canvas canvas, SettingRow row, float x, float y)
    {
        var ink = colors.Secondary;
        void Line(float x1, float y1, float x2, float y2) => canvas.Line(x + x1, y + y1, x + x2, y + y2, ink, 1.3f);
        void Curve(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3) =>
            canvas.Cubic(x + x0, y + y0, x + x1, y + y1, x + x2, y + y2, x + x3, y + y3, ink);

        switch (row)
        {
            case SettingRow.TrayMode:
                canvas.Stroke(x + 1.5f, y + 9.5f, 13, 5, 1.2f, ink, 1.3f);
                Line(10.5f, 12, 12.5f, 12);
                break;
            case SettingRow.DisplayLocation:
                canvas.Stroke(x + 1, y + 2.5f, 14, 11, 1.4f, ink, 1.3f);
                Line(1, 10, 15, 10);
                Line(10.5f, 11.8f, 12.5f, 11.8f);
                break;
            case SettingRow.TaskbarStyle:
                // 胶囊样式图标：外框 + 内部两小格。
                canvas.Stroke(x + 1, y + 4, 14, 8, 3.2f, ink, 1.3f);
                Line(4, 8, 6.5f, 8);
                Line(9.5f, 8, 12, 8);
                break;
            case SettingRow.TaskbarColor:
                // 三点色板图标。
                canvas.Ring(x + 8, y + 4.5f, 2.2f, ink, 1.2f);
                canvas.Ring(x + 4.6f, y + 10.8f, 2.2f, ink, 1.2f);
                canvas.Dot(x + 11.4f, y + 10.8f, 2.4f, ink);
                break;
            case SettingRow.Theme:
                canvas.Ring(x + 8, y + 8, 5.8f, ink, 1.3f);
                canvas.PushClip(new PanelRect(x + 8, y + 1, 7, 14));
                try
                {
                    canvas.Dot(x + 8, y + 8, 5.8f, ink);
                }
                finally
                {
                    canvas.PopClip();
                }
                break;
            case SettingRow.Threshold:
                canvas.Arc(x + 8, y + 7, 4, MathF.PI, 2 * MathF.PI, ink, 1.3f);
                Line(4, 7, 4, 11);
                Line(12, 7, 12, 11);
                Line(4, 11, 2.8f, 12.5f);
                Line(12, 11, 13.2f, 12.5f);
                Line(2.8f, 12.5f, 13.2f, 12.5f);
                Curve(6.5f, 14, 7, 15.4f, 9, 15.4f, 9.5f, 14);
                break;
            case SettingRow.GameSilent:
                Line(4.5f, 4.5f, 11.5f, 4.5f);
                Curve(11.5f, 4.5f, 13.35f, 4.5f, 14.75f, 6.05f, 14.5f, 7.9f);
                Line(14.5f, 7.9f, 14, 11.2f);
                Curve(14, 11.2f, 13.75f, 12.6f, 12.05f, 13.1f, 11.2f, 12);
                Line(11.2f, 12, 9.8f, 10);
                Line(9.8f, 10, 6.2f, 10);
                Line(6.2f, 10, 4.8f, 12);
                Curve(4.8f, 12, 3.95f, 13.1f, 2.25f, 12.6f, 2, 11.2f);
                Line(2, 11.2f, 1.5f, 7.9f);
                Curve(1.5f, 7.9f, 1.25f, 6.05f, 2.65f, 4.5f, 4.5f, 4.5f);
                Line(5, 7, 5, 9);
                Line(4, 8, 6, 8);
                break;
            case SettingRow.RuntimeEstimate:
                // 沙漏：上下横梁与两个对顶三角。
                Line(3.5f, 1.8f, 12.5f, 1.8f);
                Line(3.5f, 14.2f, 12.5f, 14.2f);
                Line(4.6f, 1.8f, 8, 8);
                Line(11.4f, 1.8f, 8, 8);
                Line(8, 8, 4.6f, 14.2f);
                Line(8, 8, 11.4f, 14.2f);
                Line(6.2f, 12.6f, 9.8f, 12.6f);
                break;
            case SettingRow.PollInterval:
                canvas.Ring(x + 8, y + 8, 6, ink, 1.3f);
                Line(8, 4.8f, 8, 8);
                Line(8, 8, 10.2f, 9.4f);
                break;
            case SettingRow.AutoUpdate:
                // 带箭头的环形刷新符号。
                canvas.Arc(x + 8, y + 8, 5.6f, -MathF.PI * .35f, MathF.PI * 1.35f, ink, 1.3f);
                Line(10.5f, 3f, 9.3f, .3f);
                Line(10.5f, 3f, 7.6f, 3.7f);
                break;
            case SettingRow.Autostart:
                Line(8, 2, 8, 7.5f);
                canvas.Arc(x + 8, y + 8.13f, 5.2f, -.856f, MathF.PI + .856f, ink, 1.3f);
                break;
        }
    }

    private void DrawColorDot(Canvas canvas, float cx, float cy, Color swatch)
    {
        canvas.Dot(cx, cy, 3.1f, swatch);
        canvas.Ring(cx, cy, 3.1f, colors.Border, 0.8f);
    }

    private static PanelRect SwitchRect(PanelRect row) => new(SettingsPanel.Right - 40, row.Y + (row.Height - 20) / 2, 40, 20);

    private void DrawSwitch(Canvas canvas, PanelRect rect, bool enabled)
    {
        if (enabled)
            canvas.Fill(rect.X, rect.Y, rect.Width, rect.Height, 10, colors.Accent);
        canvas.Stroke(rect.X + .5f, rect.Y + .5f, rect.Width - 1, rect.Height - 1, 9.5f,
            enabled ? colors.Accent : colors.Secondary);
    }

    private void DrawSwitchThumbs()
    {
        var settings = model.Settings;
        UpdateThumb(ref gameThumb, ref gameMotion, SwitchRect(Settings!.RowRect(SettingRow.GameSilent)), settings.GameSilent);
        UpdateThumb(ref autostartThumb, ref autostartMotion, SwitchRect(Settings.RowRect(SettingRow.Autostart)), settings.StartWithWindows);
        UpdateThumb(ref autoUpdateThumb, ref autoUpdateMotion, SwitchRect(Settings.RowRect(SettingRow.AutoUpdate)), settings.AutoUpdateEnabled);
        UpdateThumb(ref runtimeThumb, ref runtimeMotion, SwitchRect(Settings.RowRect(SettingRow.RuntimeEstimate)), settings.ShowRuntimeEstimate);
    }

    private void UpdateThumb(ref Composition.Layer? thumb, ref ThumbMotion? motion, PanelRect track, bool enabled)
    {
        thumb ??= new(composition, frame, 0, 0, 12, 12);
        thumb.SetY(track.Y + 4);
        float destination = track.X + 4 + (enabled ? 20 : 0);
        if (motion?.To == destination)
            return;
        thumb.Draw(canvas => canvas.Dot(6, 6, 6, enabled ? colors.Solid : colors.Secondary));
        long time = Environment.TickCount64;
        float start = animations && motion is { } previous ? previous.At(time) : destination;
        if (start != destination)
            thumb.AnimateX(start, destination, PanelLayout.SwitchMilliseconds);
        else
            thumb.SetX(destination);
        motion = new(start, destination, time);
    }

    private void DrawDevicesPage(Canvas canvas)
    {
        string place = model.Settings.DisplayLocation == TrayDisplayLocation.TrayIcons ? "托盘" : "任务栏";
        canvas.Text($"点名称改名 · 勾选：在{place}显示 · 眼睛：在面板显示", 17, 55, 310, 18, 12, colors.Muted, ellipsis: true);
        // 「显示蓝牙设备」决定哪些设备出现，从设置页移到这里。
        var bluetooth = new HitBox(PopupAction.ShowBluetooth, new(9, 76, 326, 36));
        subButtons.Add(bluetooth);
        ButtonBackground(canvas, bluetooth, 6);
        canvas.Text("显示蓝牙设备", 17, 76, 200, 36, 13, colors.Ink);
        var track = new PanelRect(SettingsPanel.Right - 40, 84, 40, 20);
        bool showBluetooth = model.Settings.ShowBluetoothDevices;
        DrawSwitch(canvas, track, showBluetooth);
        canvas.Dot(track.X + 10 + (showBluetooth ? 20 : 0), track.Y + 10, 6, showBluetooth ? colors.Solid : colors.Secondary);
        canvas.Line(17, PanelLayout.DeviceRowsTop - 3.5f, Width - 17, PanelLayout.DeviceRowsTop - 3.5f, colors.Border);
        int visibleRows = Math.Min(size.Rows, Math.Max(0, devices.Length - offset));
        for (int i = 0; i < visibleRows; i++)
        {
            var device = devices[i + offset];
            DrawDevice(canvas, i, device);
        }
        if (MaxOffset > 0 && size.Rows > 0)
        {
            float span = size.Rows * PanelLayout.DeviceRowHeight - 8;
            float thumb = Math.Max(10, span * size.Rows / devices.Length);
            canvas.Fill(339, PanelLayout.DeviceRowsTop + 4 + (span - thumb) * offset / MaxOffset,
                2, thumb, 1, colors.Muted.WithAlpha(150));
        }

        string unidentified = model.UnknownReceivers > 0
            ? $"有 {model.UnknownReceivers} 个 USB 设备未识别" : "没看到设备？";
        string? feedback = DiagnosticExport.Label(diagnosticStatus) ?? error;
        if (HasDiagnosticReport)
        {
            DrawDiagnosticReportFooter(canvas, DevicesFooter,
                feedback ?? "诊断已导出",
                unidentified,
                (PopupAction.Redetect, "重新检测"),
                (PopupAction.Diagnostics, "导出诊断信息"));
        }
        else
        {
            canvas.Line(1, DevicesFooter + .5f, Width - 1, DevicesFooter + .5f, colors.Border);
            float linksLeft = DrawLinksRight(canvas, Width - 17, DevicesFooter + 11, 20, 14,
                (PopupAction.Redetect, "重新检测"),
                (PopupAction.Diagnostics, "导出诊断信息"));
            float labelWidth = MathF.Max(40, linksLeft - 29);
            if (model.UnknownReceivers > 0 && feedback is not null)
            {
                canvas.Text(unidentified, 17, DevicesFooter + 3, labelWidth, 18, 12, colors.Muted, ellipsis: true);
                canvas.Text(feedback, 17, DevicesFooter + 21, labelWidth, 17, 11,
                    diagnosticStatus is DiagnosticStatus.Failed or DiagnosticStatus.EditorFailed ? colors.Low : colors.Muted,
                    ellipsis: true);
            }
            else
            {
                bool isError = diagnosticStatus is DiagnosticStatus.Failed or DiagnosticStatus.EditorFailed || error is not null;
                canvas.Text(feedback ?? unidentified, 17, DevicesFooter + 11, labelWidth, 20, 12,
                    isError ? colors.Low : colors.Muted, ellipsis: true);
            }
        }
    }

    private void DrawDevice(Canvas canvas, int index, DeviceStatus device)
    {
        float y = PanelLayout.DeviceRowsTop + index * PanelLayout.DeviceRowHeight;
        string id = device.Device.Id;
        bool hidden = model.IsHidden(id);
        Color Dim(Color color) => hidden ? color.WithAlpha((byte)Math.Round(color.Alpha * .45)) : color;

        if (hoverRow == index)
            canvas.Fill(5, y, 334, PanelLayout.DeviceRowHeight, 6, colors.Hover);
        DeviceIcon(canvas, device.Device.Kind, 17, y + 11.125f, 32, Dim(colors.Chip), Dim(colors.Secondary));
        bool removable = model.CanRemove(id);
        var name = new HitBox(new PopupTarget(PopupAction.Rename, DeviceId: id),
            new(55, y + 7, removable ? 149 : 192, 22.25f));
        subButtons.Add(name);
        if (StringComparer.OrdinalIgnoreCase.Equals(editorId, id))
        {
            canvas.Fill(name.X, name.Y, name.Width, name.Height, 4, colors.Solid);
            canvas.Stroke(name.X, name.Y, name.Width, name.Height, 4, colors.Accent, 2);
        }
        else
        {
            if (hover == name.Target)
                canvas.Fill(name.X, name.Y, name.Width, name.Height, 4, Dim(colors.Chip));
            ButtonBackground(canvas, name, 4);
            canvas.Text(model.Name(device.Device), 59, name.Y, name.Width - 8, name.Height, 13.5f, Dim(colors.Ink), ellipsis: true);
        }

        float prefix = canvas.Text($"{Presentation.Connection(device.Device)} · ", 59, y + 29.25f, 70, 18, 12, Dim(colors.Muted));
        float badgeWidth = DrawBadge(canvas, device.Device, 59 + prefix, y + 31, hidden);

        float iconTop = y + 12.125f;
        // 单台设备的低电量提醒阈值：点一下在「默认 → 5% → … → 30%」之间循环。
        bool custom = model.Settings.DeviceThresholds.ContainsKey(id);
        string thresholdLabel = custom ? $"提醒 {model.ThresholdFor(id)}%" : "提醒 默认";
        float chipX = 59 + prefix + badgeWidth + 6;
        float chipWidth = canvas.Measure(thresholdLabel, 10.5f, 600) + 12;
        // 离线可移除的行右侧有「移除」按钮，统一不显示阈值标签，避免有的显示、有的因放不下而消失。
        if (!removable && chipX + chipWidth <= TrayCheckX - 4)
        {
            var chip = new HitBox(new PopupTarget(PopupAction.DeviceThreshold, DeviceId: id),
                new(chipX, y + 31, chipWidth, 15.5f));
            subButtons.Add(chip);
            canvas.Fill(chip.X, chip.Y, chip.Width, chip.Height, 4, custom ? Dim(colors.AccentSoft) : Dim(colors.Chip));
            ButtonBackground(canvas, chip, 4);
            canvas.Text(thresholdLabel, chip.X, chip.Y, chip.Width, chip.Height, 10.5f,
                custom ? Dim(colors.Accent) : Dim(colors.Secondary), 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);
        }
        if (removable)
        {
            var remove = new HitBox(new PopupTarget(PopupAction.Remove, DeviceId: id), new(216, iconTop, 34, 30));
            subButtons.Add(remove);
            ButtonBackground(canvas, remove);
            canvas.Text("移除", remove.X, remove.Y, remove.Width, remove.Height, 12, colors.Accent,
                align: DWRITE_TEXT_ALIGNMENT_CENTER);
        }

        // 与设置页的「自定义」共用托盘勾选状态。
        bool inTray = model.Settings.IsInTray(id);
        bool customMode = model.Settings.TrayMode == TrayDisplayMode.Custom;
        var trayCheck = new HitBox(new PopupTarget(PopupAction.TrayToggle, DeviceId: id), new(TrayCheckX, iconTop, 30, 30));
        subButtons.Add(trayCheck);
        ButtonBackground(canvas, trayCheck);
        DrawCheckbox(canvas, TrayCheckX + 7, iconTop + 7, inTray, customMode, IconInk(trayCheck));

        var eye = new HitBox(new PopupTarget(PopupAction.Visibility, DeviceId: id), new(EyeX, iconTop, 30, 30));
        subButtons.Add(eye);
        ButtonBackground(canvas, eye);
        DrawEye(canvas, EyeX + 15, iconTop + 15, hidden, IconInk(eye));
    }

    private void DrawCheckbox(Canvas canvas, float x, float y, bool isChecked, bool activeMode, Color borderInk)
    {
        const float boxSize = 16;
        if (isChecked)
        {
            var fill = activeMode ? colors.Accent : colors.Muted.WithAlpha(120);
            canvas.Fill(x, y, boxSize, boxSize, 4, fill);
            var tick = activeMode ? colors.ButtonText : colors.Solid;
            canvas.Line(x + 3.8f, y + 8.2f, x + 6.7f, y + 11.1f, tick, 1.6f);
            canvas.Line(x + 6.7f, y + 11.1f, x + 12.2f, y + 5.2f, tick, 1.6f);
        }
        else
        {
            var stroke = activeMode ? borderInk : colors.Muted.WithAlpha(90);
            canvas.Stroke(x + .6f, y + .6f, boxSize - 1.2f, boxSize - 1.2f, 3.6f, stroke, 1.3f);
        }
    }

    private Color IconInk(HitBox button) => hover == button.Target ? colors.Ink : colors.Muted;

    private static void DrawEye(Canvas canvas, float x, float y, bool hidden, Color ink)
    {
        canvas.Cubic(x - 6.5f, y, x - 3, y - 6, x + 3, y - 6, x + 6.5f, y, ink);
        canvas.Cubic(x - 6.5f, y, x - 3, y + 6, x + 3, y + 6, x + 6.5f, y, ink);
        if (hidden)
            canvas.Line(x - 6, y - 6, x + 6, y + 6, ink, 1.3f);
        else
            canvas.Ring(x, y, 2, ink, 1.3f);
    }

    private void DrawDiagnosticReportFooter(Canvas canvas, float footer, string reportLabel, string bottomLabel,
        params ReadOnlySpan<(PopupAction Action, string Label)> bottomLinks)
    {
        const float right = Width - 17;
        const float rowHeight = 20;
        canvas.Line(1, footer + .5f, Width - 1, footer + .5f, colors.Border);

        float row1Y = footer + 10;
        float row1LinksLeft = DrawLinksRight(canvas, right, row1Y, rowHeight, 12,
            (PopupAction.OpenDiagnostics, "查看报告"),
            (PopupAction.CopyAndFeedback, "复制报告并去反馈"));
        bool reportError = diagnosticStatus is DiagnosticStatus.Failed or DiagnosticStatus.EditorFailed
            or DiagnosticStatus.FeedbackFailed;
        canvas.Text(reportLabel, 17, row1Y, MathF.Max(40, row1LinksLeft - 25), rowHeight, 11.5f,
            reportError ? colors.Low : colors.Muted, ellipsis: true);

        float row2Y = footer + 38;
        float row2LinksLeft = DrawLinksRight(canvas, right, row2Y, rowHeight, 14, bottomLinks);
        canvas.Text(bottomLabel, 17, row2Y, MathF.Max(40, row2LinksLeft - 29), rowHeight, 12,
            colors.Muted, ellipsis: true);
    }

    private float DrawLinksRight(Canvas canvas, float right, float y, float rowHeight, float gap,
        params ReadOnlySpan<(PopupAction Action, string Label)> links)
    {
        Span<float> widths = stackalloc float[links.Length];
        float total = 0;
        for (int i = 0; i < links.Length; i++)
        {
            widths[i] = MathF.Ceiling(canvas.Measure(links[i].Label, 12)) + 2;
            total += widths[i];
        }
        if (links.Length > 1)
            total += gap * (links.Length - 1);

        float start = right - total;
        float x = start;
        for (int i = 0; i < links.Length; i++)
        {
            DrawLink(canvas, new(links[i].Action, x, y, widths[i], rowHeight), links[i].Label);
            x += widths[i] + gap;
        }
        return start;
    }

    private void DrawLink(Canvas canvas, HitBox box, string label)
    {
        subButtons.Add(box);
        if (focused == box.Target || pressed == box.Target)
            ButtonBackground(canvas, box);
        canvas.Text(label, box.X, box.Y, box.Width, box.Height, 12, colors.Accent);
        if (hover == box.Target)
            canvas.Line(box.X, box.Y + box.Height - 2.5f, box.X + box.Width, box.Y + box.Height - 2.5f, colors.Accent);
    }

    private void DrawUpdatesPage(Canvas canvas)
    {
        // 右上角版本徽章
        if (!string.IsNullOrEmpty(updateState.CurrentVersionLabel))
        {
            float pillW = MathF.Min(138, MathF.Ceiling(canvas.Measure(updateState.CurrentVersionLabel, 11, 600)) + 14);
            float pillX = Width - 12 - pillW;
            canvas.Fill(pillX, 18, pillW, 18, 4, colors.Chip);
            canvas.Text(updateState.CurrentVersionLabel, pillX + 5, 18, pillW - 10, 18, 11, colors.Muted, 600,
                align: DWRITE_TEXT_ALIGNMENT_CENTER, ellipsis: true);
        }

        if (updateState.Phase == UpdatePhase.Available && updateState.Available is { } available)
        {
            DrawUpdateAvailable(canvas, available);
            return;
        }

        updateNotesMaxScroll = 0;
        const float cardX = 12, cardY = 54, cardW = 320, cardH = 148;
        canvas.Fill(cardX, cardY, cardW, cardH, 7, colors.Layer);
        canvas.Stroke(cardX + .5f, cardY + .5f, cardW - 1, cardH - 1, 6.5f, colors.LayerStroke);

        float buttonY = height - 48;
        switch (updateState.Phase)
        {
            case UpdatePhase.UpToDate:
                canvas.Dot(172, 92, 21, colors.Charging.WithAlpha(36));
                canvas.Line(163.5f, 92.5f, 169.2f, 98.2f, colors.Charging, 2.0f);
                canvas.Line(169.2f, 98.2f, 180.8f, 86.4f, colors.Charging, 2.0f);
                canvas.Text("已是最新版本", 24, 122, 296, 22, 14.5f, colors.Ink, 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);
                canvas.Text($"当前版本 {updateState.CurrentVersionLabel}", 24, 146, 296, 20, 12.5f, colors.Secondary,
                    align: DWRITE_TEXT_ALIGNMENT_CENTER, ellipsis: true);
                canvas.Text(model.Settings.AutoUpdateEnabled ? "正式版通道 · 每天自动检查" : "正式版通道 · 仅手动检查时联网",
                    24, 169, 296, 18, 11.5f, colors.Muted,
                    align: DWRITE_TEXT_ALIGNMENT_CENTER);
                DrawActionButton(canvas, new(PopupAction.CheckUpdatesAgain, 12, buttonY, 156, 34), "重新检查", primary: false);
                DrawActionButton(canvas, new(new PopupTarget(PopupAction.Back, 1), 176, buttonY, 156, 34), "返回", primary: false);
                break;

            case UpdatePhase.Downloading or UpdatePhase.Verifying:
                var progress = updateState.Progress ?? new UpdateProgress(0, updateState.Available?.Installer.Size ?? 0,
                    updateState.Phase == UpdatePhase.Verifying);
                canvas.Dot(172, 92, 21, colors.AccentSoft);
                DrawUpdateArrowGlyph(canvas, 172, 92, colors.Accent, downward: !progress.Verifying);
                string downloadTitle = progress.Verifying
                    ? "正在校验安装包…"
                    : updateState.Available is { } target ? $"正在下载 v{target.Version}…" : "正在下载更新…";
                canvas.Text(downloadTitle, 24, 122, 296, 22, 14.5f, colors.Ink, 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);
                canvas.Fill(32, 152, 280, 6, 3, colors.Empty);
                float fraction = (float)(progress.Fraction ?? 0);
                float fillW = progress.Verifying ? 280 : MathF.Max(fraction > 0 ? 8 : 0, 280 * fraction);
                if (fillW > 0)
                    canvas.Fill(32, 152, fillW, 6, 3, progress.Verifying ? colors.Charging : colors.Accent);
                canvas.Text(progress.SummaryText, 24, 166, 296, 20, 12, colors.Muted,
                    align: DWRITE_TEXT_ALIGNMENT_CENTER, ellipsis: true);
                DrawActionButton(canvas, new(PopupAction.CancelUpdate, 12, buttonY, 320, 34), "取消下载", primary: false);
                break;

            case UpdatePhase.Error:
                canvas.Dot(172, 90, 21, colors.Low.WithAlpha(36));
                canvas.Line(172, 81, 172, 93, colors.Low, 2.0f);
                canvas.Dot(172, 98.5f, 1.3f, colors.Low);
                canvas.Text("更新未完成", 24, 118, 296, 22, 14.5f, colors.Ink, 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);
                canvas.TextWrapped(updateState.ErrorMessage ?? "检查或更新时发生错误。", 26, 144, 292, 50, 12.5f,
                    colors.Secondary, align: DWRITE_TEXT_ALIGNMENT_CENTER);
                DrawActionButton(canvas, new(new PopupTarget(PopupAction.Back, 1), 12, buttonY, 156, 34), "返回", primary: false);
                DrawActionButton(canvas, new(PopupAction.CheckUpdatesAgain, 176, buttonY, 156, 34), "重试", primary: true);
                break;

            default: // Checking / Idle
                canvas.Dot(172, 92, 21, colors.AccentSoft);
                DrawUpdateArrowGlyph(canvas, 172, 92, colors.Accent, downward: false);
                canvas.Text("正在检查更新…", 24, 122, 296, 22, 14.5f, colors.Ink, 600, align: DWRITE_TEXT_ALIGNMENT_CENTER);
                canvas.Text("正在查询最新正式版（优先国内镜像）", 24, 146, 296, 18, 12, colors.Muted,
                    align: DWRITE_TEXT_ALIGNMENT_CENTER);
                canvas.Fill(82, 174, 180, 4, 2, colors.Empty);
                if (!animations)
                    canvas.Fill(112, 174, 120, 4, 2, colors.Accent);
                DrawActionButton(canvas, new(PopupAction.CancelUpdate, 12, buttonY, 320, 34), "取消", primary: false);
                break;
        }
    }

    private void DrawUpdateAvailable(Canvas canvas, AvailableUpdate update)
    {
        // 顶部新版本概览卡片
        canvas.Fill(12, 54, 320, 56, 7, colors.Layer);
        canvas.Stroke(12.5f, 54.5f, 319, 55, 6.5f, colors.LayerStroke);
        canvas.Fill(22, 65, 34, 34, 7, colors.AccentSoft);
        DrawUpdateArrowGlyph(canvas, 39, 82, colors.Accent, downward: false);
        bool ready = updateState.IsReady;
        canvas.Text(ready ? $"新版本 v{update.Version} 已就绪" : $"发现新版本 v{update.Version}",
            66, 62, 254, 20, 14, colors.Ink, 600, ellipsis: true);
        canvas.Text(ready
                ? $"{updateState.CurrentVersionLabel} → v{update.Version} · 已下载并通过校验"
                : $"{updateState.CurrentVersionLabel} → v{update.Version} · {UpdateProgress.FormatBytes(update.Installer.Size)}",
            66, 83, 254, 18, 12, colors.Muted, ellipsis: true);

        // 更新说明卡片（支持滚轮与方向键滚动）
        float buttonY = height - 46;
        float standaloneNoticeH = updateState.IsInstalled ? 0 : 22;
        const float notesTop = 118;
        float notesCardH = MathF.Max(90, buttonY - 10 - standaloneNoticeH - notesTop);
        canvas.Fill(12, notesTop, 320, notesCardH, 7, colors.Layer);
        canvas.Stroke(12.5f, notesTop + .5f, 319, notesCardH - 1, 6.5f, colors.LayerStroke);
        canvas.Text("更新说明", 24, notesTop + 7, 200, 18, 11.5f, colors.Muted, 600);
        canvas.Line(22, notesTop + 29.5f, 322, notesTop + 29.5f, colors.LayerStroke);

        var viewport = new PanelRect(22, notesTop + 34, 298, MathF.Max(40, notesCardH - 42));
        var blocks = updateState.Notes;
        var blockHeights = new float[blocks.Length];
        float contentHeight = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            if (block.Kind == UpdateNoteKind.Heading && i > 0)
                contentHeight += 4;
            float textWidth = block.Kind == UpdateNoteKind.Bullet ? viewport.Width - 20 : viewport.Width - 8;
            int weight = block.Kind == UpdateNoteKind.Heading ? 600 : 400;
            float h = MathF.Ceiling(canvas.MeasureWrappedHeight(block.Text, textWidth, 12.5f, weight));
            blockHeights[i] = MathF.Max(18, h);
            contentHeight += blockHeights[i] + (block.Kind == UpdateNoteKind.Heading ? 4 : 5);
        }

        updateNotesMaxScroll = MathF.Max(0, contentHeight - viewport.Height);
        updateNotesScroll = Math.Clamp(updateNotesScroll, 0, updateNotesMaxScroll);

        canvas.PushClip(viewport);
        try
        {
            float y = viewport.Y - updateNotesScroll;
            for (int i = 0; i < blocks.Length; i++)
            {
                var block = blocks[i];
                if (block.Kind == UpdateNoteKind.Heading && i > 0)
                    y += 4;
                float h = blockHeights[i];
                if (y + h >= viewport.Y - 2 && y <= viewport.Y + viewport.Height + 2)
                {
                    switch (block.Kind)
                    {
                        case UpdateNoteKind.Heading:
                            canvas.TextWrapped(block.Text, viewport.X, y, viewport.Width - 8, h + 2, 12.5f, colors.Ink, 600);
                            break;
                        case UpdateNoteKind.Bullet:
                            canvas.Dot(viewport.X + 4.5f, y + 8f, 2.1f, colors.Accent);
                            canvas.TextWrapped(block.Text, viewport.X + 14, y, viewport.Width - 20, h + 2, 12.5f, colors.Secondary);
                            break;
                        default:
                            canvas.TextWrapped(block.Text, viewport.X, y, viewport.Width - 8, h + 2, 12.5f, colors.Secondary);
                            break;
                    }
                }
                y += h + (block.Kind == UpdateNoteKind.Heading ? 4 : 5);
            }
        }
        finally
        {
            canvas.PopClip();
        }

        if (updateNotesMaxScroll > 0)
        {
            float trackH = viewport.Height - 4;
            float thumbH = MathF.Max(18, trackH * viewport.Height / contentHeight);
            float thumbY = viewport.Y + 2 + (trackH - thumbH) * (updateNotesScroll / updateNotesMaxScroll);
            canvas.Fill(325, thumbY, 2.5f, thumbH, 1.25f, colors.Muted.WithAlpha(150));
        }

        if (!updateState.IsInstalled)
        {
            canvas.Text("当前运行的是独立构建，请前往 Release 获取正式安装器", 16, buttonY - 24, 312, 18, 11.5f, colors.Muted,
                align: DWRITE_TEXT_ALIGNMENT_CENTER, ellipsis: true);
            DrawActionButton(canvas, new(PopupAction.OpenReleasePage, 12, buttonY, 320, 34), "前往 Release 下载", primary: true);
        }
        else
        {
            DrawActionButton(canvas, new(PopupAction.OpenReleasePage, 12, buttonY, 134, 34), "查看 Release", primary: false);
            DrawActionButton(canvas, new(PopupAction.InstallUpdate, 154, buttonY, 178, 34),
                ready ? "重启并升级" : "下载并安装", primary: true);
        }
    }

    private void DrawActionButton(Canvas canvas, HitBox box, string label, bool primary)
    {
        subButtons.Add(box);
        canvas.Fill(box.X, box.Y, box.Width, box.Height, 5, primary ? colors.Accent : colors.Card);
        if (!primary)
            canvas.Stroke(box.X + .5f, box.Y + .5f, box.Width - 1, box.Height - 1, 4.5f, colors.CardStroke);
        ButtonBackground(canvas, box, 5);
        canvas.Text(label, box.X, box.Y, box.Width, box.Height, 13,
            primary ? colors.ButtonText : colors.Ink, primary ? 600 : 400,
            align: DWRITE_TEXT_ALIGNMENT_CENTER);
    }

    private static void DrawUpdateArrowGlyph(Canvas canvas, float cx, float cy, Color ink, bool downward)
    {
        if (downward)
        {
            canvas.Line(cx, cy - 6.5f, cx, cy + 3.2f, ink, 1.6f);
            canvas.Line(cx - 4.0f, cy - 0.8f, cx, cy + 3.2f, ink, 1.6f);
            canvas.Line(cx + 4.0f, cy - 0.8f, cx, cy + 3.2f, ink, 1.6f);
        }
        else
        {
            canvas.Line(cx, cy - 6.5f, cx, cy + 3.2f, ink, 1.6f);
            canvas.Line(cx - 4.0f, cy - 2.5f, cx, cy - 6.5f, ink, 1.6f);
            canvas.Line(cx + 4.0f, cy - 2.5f, cx, cy - 6.5f, ink, 1.6f);
        }
        canvas.Line(cx - 6.5f, cy + 4.0f, cx - 6.5f, cy + 6.5f, ink, 1.5f);
        canvas.Line(cx - 6.5f, cy + 6.5f, cx + 6.5f, cy + 6.5f, ink, 1.5f);
        canvas.Line(cx + 6.5f, cy + 6.5f, cx + 6.5f, cy + 4.0f, ink, 1.5f);
    }
}

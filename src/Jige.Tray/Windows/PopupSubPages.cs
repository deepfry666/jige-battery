using static Windows.Win32.Graphics.DirectWrite.DWRITE_TEXT_ALIGNMENT;

namespace Jige.Tray.Windows;

internal sealed partial class PopupScene
{
    private int DevicesFooter => height - 42;
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
                PopupAction.TrayMode => SettingRow.TrayMode,
                PopupAction.Threshold => SettingRow.Threshold,
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
        canvas.Text(Page == PanelPage.Settings ? "设置" : "管理设备", 45, 11, 270, 32, 14, colors.Ink, 600);

        if (Page == PanelPage.Settings)
            DrawSettingsPage(canvas);
        else
            DrawDevicesPage(canvas);
        if (!subButtons.Any(button => button.Target == focused))
            focused = default;
    }

    private void DrawSettingsPage(Canvas canvas)
    {
        var layout = Settings!;
        var settings = model.Settings;
        float cardHeight = SettingsPanel.CollapsedCardHeight + layout.ExtraHeight;
        canvas.Fill(SettingsPanel.CardX, SettingsPanel.CardY, SettingsPanel.CardWidth, cardHeight, 7, colors.Layer);
        foreach (var row in SettingsPanel.Rows)
        {
            var rect = layout.RowRect(row);
            var target = row switch
            {
                SettingRow.GameSilent => PopupAction.GameSilent,
                SettingRow.Autostart => PopupAction.Autostart,
                _ => new PopupTarget(PopupAction.ExpandSetting, (int)row)
            };
            var button = new HitBox(target, rect);
            subButtons.Add(button);
            DrawSettingBackground(canvas, button, row is SettingRow.TrayMode or SettingRow.Autostart);
            if (row != SettingRow.TrayMode)
                canvas.Line(rect.X, rect.Y + .5f, rect.X + rect.Width, rect.Y + .5f, colors.LayerStroke);

            DrawSettingIcon(canvas, row, 25, rect.Y + 14);
            string name = row switch
            {
                SettingRow.TrayMode => "托盘显示",
                SettingRow.Threshold => "低电量提醒",
                SettingRow.GameSilent => "游戏中不打扰",
                SettingRow.PollInterval => "查询间隔",
                _ => "开机自启"
            };
            canvas.Text(name, 54, rect.Y, 122, rect.Height, 13.5f, colors.Ink);

            if (row is SettingRow.GameSilent or SettingRow.Autostart)
            {
                DrawSwitch(canvas, SwitchRect(rect),
                    row == SettingRow.GameSilent ? settings.GameSilent : settings.StartWithWindows);
                continue;
            }

            string value = row switch
            {
                SettingRow.TrayMode => Presentation.TrayModeShortLabel(settings.TrayMode),
                SettingRow.Threshold => $"{settings.LowBatteryThreshold}%",
                _ => Presentation.Interval(settings.PollSeconds)
            };
            canvas.Text(value, SettingsPanel.Right - 164, rect.Y, 140, rect.Height, 13, colors.Muted,
                align: DWRITE_TEXT_ALIGNMENT_TRAILING, ellipsis: true);
            DrawChevron(canvas, SettingsPanel.Right - 6, rect.Y + 22, layout.Progress(row));
            DrawSettingChoices(canvas, row);
        }
        canvas.Stroke(SettingsPanel.CardX + .5f, SettingsPanel.CardY + .5f,
            SettingsPanel.CardWidth - 1, cardHeight - 1, 6.5f, colors.LayerStroke);

        float footer = layout.Height - 42;
        DrawAbout(canvas, footer, DiagnosticExport.Label(diagnosticStatus) ?? error ?? $"几格 {ReleaseVersion.Current}");
        DrawLink(canvas, new(PopupAction.Diagnostics, 221, footer + 10, 48, 20), "诊断信息");
        DrawLink(canvas, new(PopupAction.Updates, 279, footer + 10, 48, 20), "检查更新");
    }

    public void ToggleSetting(SettingRow row, long time)
    {
        var choices = Choices(row);
        int selected = Array.FindIndex(choices, choice => IsSelected(choice.Target));
        Settings!.Toggle(row, time, animations, selected);
    }

    private static SettingChoice[] Choices(SettingRow row) => row switch
    {
        SettingRow.TrayMode => SettingsPanel.TrayModeChoices,
        SettingRow.Threshold => SettingsPanel.ThresholdChoices,
        SettingRow.PollInterval => SettingsPanel.PollChoices,
        _ => []
    };

    private bool IsSelected(PopupTarget target) => target.Action switch
    {
        PopupAction.TrayMode => target.Value == (int)model.Settings.TrayMode,
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

        var choices = Choices(row);
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
                    DrawSettingBackground(canvas, new(choice.Target, rect));
                }
                bool selected = IsSelected(choice.Target);
                canvas.Text(choice.Label, 54, rect.Y, SettingsPanel.Right - 76, rect.Height, 13, colors.Ink,
                    selected ? 600 : 400, ellipsis: true);
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

    private void DrawSettingBackground(Canvas canvas, HitBox button, bool round = false)
    {
        if (hover == button.Target)
        {
            var fill = pressed == button.Target ? colors.Press : colors.Hover;
            if (round)
            {
                // 把靠卡片内部的圆角画到裁剪区外，避免半透明填充叠加。
                float y = button.Target.Action == PopupAction.Autostart ? button.Y - 6 : button.Y;
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
            canvas.Stroke(button.X + 1, button.Y + 1, button.Width - 2, button.Height - 2, round ? 5 : 0, colors.Accent);
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
            case SettingRow.PollInterval:
                canvas.Ring(x + 8, y + 8, 6, ink, 1.3f);
                Line(8, 4.8f, 8, 8);
                Line(8, 8, 10.2f, 9.4f);
                break;
            case SettingRow.Autostart:
                Line(8, 2, 8, 7.5f);
                canvas.Arc(x + 8, y + 8.13f, 5.2f, -.856f, MathF.PI + .856f, ink, 1.3f);
                break;
        }
    }

    private static PanelRect SwitchRect(PanelRect row) => new(SettingsPanel.Right - 40, row.Y + 12, 40, 20);

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
        canvas.Text("点名称可改名，勾选在托盘显示，眼睛控制是否出现在", 17, 59, 310, 18, 12, colors.Muted);
        canvas.Text("面板里。", 17, 77, 310, 18, 12, colors.Muted);
        for (int i = 0; i < size.Rows; i++)
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
            ? $"检测到 {model.UnknownReceivers} 个未识别接收器" : "没看到设备？";
        float footerWidth = HasDiagnosticReport ? 120 : 172;
        string? feedback = DiagnosticExport.Label(diagnosticStatus) ?? error;
        if (model.UnknownReceivers > 0 && feedback is not null)
        {
            canvas.Line(1, DevicesFooter + .5f, Width - 1, DevicesFooter + .5f, colors.Border);
            canvas.Text(unidentified, 17, DevicesFooter + 3, footerWidth, 18, 12, colors.Muted, ellipsis: true);
            canvas.Text(feedback, 17, DevicesFooter + 21, footerWidth, 17, 11,
                diagnosticStatus is DiagnosticStatus.Failed or DiagnosticStatus.EditorFailed ? colors.Low : colors.Muted,
                ellipsis: true);
        }
        else
            DrawAbout(canvas, DevicesFooter, feedback ?? unidentified, footerWidth);

        if (HasDiagnosticReport)
            DrawLink(canvas, new(PopupAction.OpenDiagnostics, 143, DevicesFooter + 9, 48, 20), "查看报告");
        DrawLink(canvas, new(PopupAction.Redetect, 199, DevicesFooter + 9, 48, 20), "重新检测");
        DrawLink(canvas, new(PopupAction.Diagnostics, 255, DevicesFooter + 9, 72, 20), "导出诊断信息");
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

        float prefix = canvas.Text("2.4G · ", 59, y + 29.25f, 70, 18, 12, Dim(colors.Muted));
        DrawBadge(canvas, device.Device, 59 + prefix, y + 31, hidden);

        float iconTop = y + 12.125f;
        if (removable)
        {
            var remove = new HitBox(new PopupTarget(PopupAction.Remove, DeviceId: id), new(216, iconTop, 34, 30));
            subButtons.Add(remove);
            ButtonBackground(canvas, remove);
            canvas.Text("移除", remove.X, remove.Y, remove.Width, remove.Height, 12, colors.Accent,
                align: DWRITE_TEXT_ALIGNMENT_CENTER);
        }

        // B4 / B5：去掉图钉，改为托盘勾选框（在「每台设备一个图标」模式下控制该设备是否显示在托盘）。
        bool inTray = model.Settings.IsInTray(id);
        bool perDeviceMode = model.Settings.TrayMode == TrayDisplayMode.PerDevice;
        var trayCheck = new HitBox(new PopupTarget(PopupAction.TrayToggle, DeviceId: id), new(TrayCheckX, iconTop, 30, 30));
        subButtons.Add(trayCheck);
        ButtonBackground(canvas, trayCheck);
        DrawCheckbox(canvas, TrayCheckX + 7, iconTop + 7, inTray, perDeviceMode, IconInk(trayCheck));

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

    private void DrawAbout(Canvas canvas, float y, string label, float width = 190)
    {
        canvas.Line(1, y + .5f, Width - 1, y + .5f, colors.Border);
        canvas.Text(label, 17, y + 10, width, 18, 12, error is null ? colors.Muted : colors.Low, ellipsis: true);
    }

    private void DrawLink(Canvas canvas, HitBox box, string label)
    {
        subButtons.Add(box);
        if (focused == box.Target || pressed == box.Target)
            ButtonBackground(canvas, box);
        canvas.Text(label, box.X, box.Y, box.Width, box.Height, 12, colors.Accent);
        if (hover == box.Target)
            canvas.Line(box.X, box.Y + 17, box.X + box.Width, box.Y + 17, colors.Accent);
    }
}

using static Windows.Win32.Graphics.DirectWrite.DWRITE_TEXT_ALIGNMENT;

namespace Jige.Tray.Windows;

internal sealed partial class PopupScene
{
    // 原型使用 1px 面板边框，页面坐标包含这 1px。
    private static readonly PanelRect[] SettingRows =
    [
        new(5, 81, 334, 48.5f),
        new(5, 161.5f, 334, 48.5f),
        new(5, 210, 334, 48.5f),
        new(5, 290.5f, 334, 48.5f),
        new(5, 339, 334, 40)
    ];

    private static readonly HitBox SettingsDiagnostics = new(PopupAction.Diagnostics, 221, 396, 48, 20);
    private static readonly HitBox SettingsUpdates = new(PopupAction.Updates, 279, 396, 48, 20);
    private int DevicesFooter => height - 42;
    private const float PinX = 261, EyeX = 301;

    private readonly record struct ThumbMotion(float From, float To, long Started)
    {
        public float At(long time) => From + (To - From) *
            (float)PanelLayout.Ease((time - Started) / (double)PanelLayout.SwitchMilliseconds);
    }

    private static int SettingRowAt(float x, float y) => Array.FindIndex(SettingRows, row => row.Contains(x, y));

    private void DrawSubPage(Canvas canvas)
    {
        subButtons.Clear();
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
        var settings = model.Settings;
        canvas.Text("托盘", 17, 59, 310, 18, 12, colors.Muted, 600);
        canvas.Text("提醒", 17, 139.5f, 310, 18, 12, colors.Muted, 600);
        canvas.Text("常规", 17, 268.5f, 310, 18, 12, colors.Muted, 600);

        SettingLabel(canvas, 0, "托盘显示", "任务栏数字显示哪台设备");
        SettingLabel(canvas, 1, "低电量提醒", "跌破阈值时提醒一次");
        SettingLabel(canvas, 2, "游戏中静默", "全屏游戏时不弹任何窗口，退出后再提醒");
        SettingLabel(canvas, 3, "查询间隔", $"游戏中自动放宽到 {Presentation.Interval(settings.GamePollSeconds)}");
        SettingLabel(canvas, 4, "开机自启");

        DrawPrimaryPicker(canvas);
        DrawOptions(canvas, PanelLayout.ThresholdTrack, PanelLayout.ThresholdOptions, PopupAction.Threshold,
            settings.LowBatteryThreshold);
        DrawSwitch(canvas, PanelLayout.GameSilent, PopupAction.GameSilent, settings.GameSilent);
        DrawOptions(canvas, PanelLayout.PollTrack, PanelLayout.PollOptions, PopupAction.PollInterval, settings.PollSeconds);
        DrawSwitch(canvas, PanelLayout.Autostart, PopupAction.Autostart, settings.StartWithWindows);

        DrawAbout(canvas, 387, error ?? $"几格 {ReleaseVersion.Current}");
        DrawLink(canvas, SettingsDiagnostics, "诊断信息");
        DrawLink(canvas, SettingsUpdates, "检查更新");
    }

    private void SettingLabel(Canvas canvas, int index, string label, string? detail = null)
    {
        var row = SettingRows[index];
        if (hoverSetting == index)
            canvas.Fill(row.X, row.Y, row.Width, row.Height, 6, colors.Hover);
        canvas.Text(label, 21, row.Y + (detail is null ? 10 : 6), 254, 20.25f, 13.5f, colors.Ink);
        if (detail is not null)
            canvas.Text(detail, 21, row.Y + 26.25f, 254, 16.2f, 12, colors.Muted);
    }

    private void DrawPrimaryPicker(Canvas canvas)
    {
        string label = primary is null ? "未选择设备" : model.Name(primary.Device);
        if (primaryPicker is null)
        {
            float width = canvas.Measure(label, 12.5f);
            foreach (var device in devices)
                width = Math.Max(width, canvas.Measure(model.Name(device.Device), 12.5f));
            width = Math.Min(150, width + 30);
            primaryPicker = new HitBox(PopupAction.PrimaryPicker, 327 - width, 90, width, 30);
        }
        var picker = primaryPicker.Value;
        canvas.Fill(picker.X, picker.Y, picker.Width, picker.Height, 6, colors.Chip);
        if (devices.Length > 0)
        {
            subButtons.Add(picker);
            ButtonBackground(canvas, picker);
        }
        var ink = devices.Length == 0 ? colors.Muted : colors.Ink;
        canvas.Text(label, picker.X + 8, picker.Y, picker.Width - 29, picker.Height, 12.5f, ink, ellipsis: true);
        canvas.Line(311, 103, 315, 107, ink, 1.1f);
        canvas.Line(315, 107, 319, 103, ink, 1.1f);
    }

    private void DrawOptions(Canvas canvas, PanelRect track, ReadOnlySpan<PanelOption> options, PopupAction action, int selected)
    {
        canvas.Fill(track.X, track.Y, track.Width, track.Height, 6, colors.Chip);
        foreach (var option in options)
        {
            var button = new HitBox(new PopupTarget(action, option.Value), option.Rect);
            subButtons.Add(button);
            if (option.Value == selected)
            {
                canvas.Fill(button.X, button.Y + 1, button.Width, button.Height, 4, new(0, 31));
                canvas.Fill(button.X, button.Y, button.Width, button.Height, 4, colors.Solid);
            }
            ButtonBackground(canvas, button, 4);
            canvas.Text(option.Label, button.X, button.Y, button.Width, button.Height, 12,
                option.Value == selected ? colors.Ink : colors.Secondary, align: DWRITE_TEXT_ALIGNMENT_CENTER);
        }
    }

    private void DrawSwitch(Canvas canvas, PanelRect rect, PopupAction action, bool enabled)
    {
        var button = new HitBox(action, rect);
        subButtons.Add(button);
        if (enabled)
            canvas.Fill(rect.X, rect.Y, rect.Width, rect.Height, 10, colors.Accent);
        ButtonBackground(canvas, button, 10);
        canvas.Stroke(rect.X + .5f, rect.Y + .5f, rect.Width - 1, rect.Height - 1, 9.5f,
            enabled || focused == button.Target ? colors.Accent : colors.Secondary);
    }

    private void DrawSwitchThumbs()
    {
        var settings = model.Settings;
        UpdateThumb(ref gameThumb, ref gameMotion, PanelLayout.GameSilent, settings.GameSilent);
        UpdateThumb(ref autostartThumb, ref autostartMotion, PanelLayout.Autostart, settings.StartWithWindows);
    }

    private void UpdateThumb(ref Composition.Layer? thumb, ref ThumbMotion? motion, PanelRect track, bool enabled)
    {
        float destination = track.X + 4 + (enabled ? 20 : 0);
        if (motion?.To == destination)
            return;
        thumb ??= new(composition, frame, 0, track.Y + 4, 12, 12);
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
        canvas.Text("点名称可以改名，图钉设为托盘显示，眼睛控制是否出现在", 17, 59, 310, 18, 12, colors.Muted);
        canvas.Text("面板里。", 17, 77, 310, 18, 12, colors.Muted);
        for (int i = 0; i < size.Rows; i++)
        {
            var device = devices[i + offset];
            DrawDevice(canvas, i, device, device == primary);
        }
        if (MaxOffset > 0 && size.Rows > 0)
        {
            float span = size.Rows * PanelLayout.DeviceRowHeight - 8;
            float thumb = Math.Max(10, span * size.Rows / devices.Length);
            canvas.Fill(339, PanelLayout.DeviceRowsTop + 4 + (span - thumb) * offset / MaxOffset,
                2, thumb, 1, colors.Muted.WithAlpha(150));
        }

        DrawAbout(canvas, DevicesFooter, error ?? "没看到你的设备？");
        DrawLink(canvas, new(PopupAction.Diagnostics, 255, DevicesFooter + 9, 72, 20), "导出诊断信息");
    }

    private void DrawDevice(Canvas canvas, int index, DeviceStatus device, bool primary)
    {
        float y = PanelLayout.DeviceRowsTop + index * PanelLayout.DeviceRowHeight;
        string id = device.Device.Id;
        bool hidden = model.IsHidden(id);
        Color Dim(Color color) => hidden ? color.WithAlpha((byte)Math.Round(color.Alpha * .45)) : color;

        if (hoverRow == index)
            canvas.Fill(5, y, 334, PanelLayout.DeviceRowHeight, 6, colors.Hover);
        DeviceIcon(canvas, device.Device.Kind, 17, y + 11.125f, 32, Dim(colors.Chip), Dim(colors.Secondary));
        var name = new HitBox(new PopupTarget(PopupAction.Rename, DeviceId: id), new(55, y + 7, 192, 22.25f));
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
            canvas.Text(model.Name(device.Device), 59, name.Y, 184, name.Height, 13.5f, Dim(colors.Ink), ellipsis: true);
        }

        bool verified = DeviceVerification.IsVerified(device.Device);
        float prefix = canvas.Text("2.4G · ", 59, y + 29.25f, 70, 18, 12, Dim(colors.Muted));
        string badge = verified ? "已实机验证" : "按协议适配";
        float badgeWidth = canvas.Measure(badge, 10.5f, 600) + 10;
        canvas.Fill(59 + prefix, y + 31, badgeWidth, 15.5f, 4,
            Dim(verified ? colors.Charging.WithAlpha(41) : colors.Chip));
        canvas.Text(badge, 64 + prefix, y + 31, badgeWidth - 10, 15.5f, 10.5f,
            Dim(verified ? colors.Charging : colors.Secondary), 600);

        float iconTop = y + 12.125f;
        var pin = new HitBox(new PopupTarget(PopupAction.Pin, DeviceId: id), new(PinX, iconTop, 30, 30));
        subButtons.Add(pin);
        ButtonBackground(canvas, pin);
        DrawPin(canvas, PinX + 9, iconTop + 9, primary ? colors.Accent : IconInk(pin), 1.2f);

        var eye = new HitBox(new PopupTarget(PopupAction.Visibility, DeviceId: id), new(EyeX, iconTop, 30, 30));
        if (!primary)
        {
            subButtons.Add(eye);
            ButtonBackground(canvas, eye);
        }
        DrawEye(canvas, EyeX + 15, iconTop + 15, hidden, primary ? colors.Muted.WithAlpha(89) : IconInk(eye));
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

    private void DrawAbout(Canvas canvas, int y, string label)
    {
        canvas.Line(1, y + .5f, Width - 1, y + .5f, colors.Border);
        canvas.Text(label, 17, y + 10, 190, 18, 12, error is null ? colors.Muted : colors.Low);
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

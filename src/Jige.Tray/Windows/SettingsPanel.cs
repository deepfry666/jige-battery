namespace Jige.Tray.Windows;

/// <summary>设置行按显示顺序排列，每 3 行一组：显示 / 电量 / 通用。</summary>
internal enum SettingRow { DisplayLocation, TrayMode, Theme, Threshold, RuntimeEstimate, PollInterval, GameSilent, Autostart, AutoUpdate }

internal readonly record struct SettingChoice(PopupTarget Target, string Label);

internal sealed class SettingsPanel(int availableHeight, bool hasDiagnosticFooter = false)
{
    public const int CardX = 9, CardWidth = PanelLayout.Width - 18;
    public const int RowHeight = 44, OptionHeight = 34, Right = CardX + CardWidth - 13;
    /// <summary>第一组小标题的顶部，以及小标题高度、组间距和每组行数。</summary>
    public const int StackTop = 47, HeaderHeight = 20, SectionGap = 6, SectionSize = 3;
    public static readonly string[] SectionTitles = ["显示", "电量", "通用"];
    private const int MaxVisibleOptions = 6;

    public static ReadOnlySpan<SettingRow> Rows =>
        [SettingRow.DisplayLocation, SettingRow.TrayMode, SettingRow.Theme,
            SettingRow.Threshold, SettingRow.RuntimeEstimate, SettingRow.PollInterval,
            SettingRow.GameSilent, SettingRow.Autostart, SettingRow.AutoUpdate];

    public static int SectionOf(SettingRow row) => (int)row / SectionSize;
    public static bool IsSectionStart(SettingRow row) => (int)row % SectionSize == 0;
    public static bool IsSectionEnd(SettingRow row) => (int)row % SectionSize == SectionSize - 1;

    public static readonly SettingChoice[] TrayModeChoices =
    [
        new(new(PopupAction.TrayMode, (int)TrayDisplayMode.LowestBattery), "只显示电量最低的一台"),
        new(new(PopupAction.TrayMode, (int)TrayDisplayMode.Custom), "自定义")
    ];

    public static readonly SettingChoice[] DisplayLocationChoices =
    [
        new(new(PopupAction.DisplayLocation, (int)TrayDisplayLocation.Taskbar), "任务栏（推荐）"),
        new(new(PopupAction.DisplayLocation, (int)TrayDisplayLocation.TrayIcons), "托盘图标")
    ];

    public static readonly SettingChoice[] ThemeChoices =
    [
        new(new(PopupAction.Theme, (int)ThemeMode.Auto), "自动切换（跟随系统）"),
        new(new(PopupAction.Theme, (int)ThemeMode.Light), "明亮模式"),
        new(new(PopupAction.Theme, (int)ThemeMode.Dark), "深色模式")
    ];

    public static string DisplayLocationLabel(TrayDisplayLocation location, string? fallbackReason = null) =>
        location == TrayDisplayLocation.TrayIcons ? DisplayLocationChoices[1].Label
        : string.IsNullOrEmpty(fallbackReason) ? DisplayLocationChoices[0].Label
        : "已回退托盘";

    public static string DisplayLocationOptionLabel(SettingChoice choice, string? fallbackReason) =>
        choice.Target.Action == PopupAction.DisplayLocation &&
        choice.Target.Value == (int)TrayDisplayLocation.Taskbar &&
        !string.IsNullOrEmpty(fallbackReason)
            ? $"任务栏（回退：{fallbackReason}）"
            : choice.Label;

    public static readonly SettingChoice[] ThresholdChoices =
    [
        new(new(PopupAction.Threshold, 10), "10%"),
        new(new(PopupAction.Threshold, 15), "15%（默认）"),
        new(new(PopupAction.Threshold, 20), "20%"),
        new(new(PopupAction.Threshold, 30), "30%")
    ];

    /// <summary>「电量提醒」展开后阈值下方的两个勾选项。</summary>
    public static readonly SettingChoice[] ReminderToggleChoices =
    [
        new(new(PopupAction.GameEntryReminder), "进游戏时也提醒一次"),
        new(new(PopupAction.FullChargeReminder), "充满时提醒拔线")
    ];

    public static readonly SettingChoice[] PollChoices = TraySettings.PollIntervals.Select(seconds =>
        new SettingChoice(new(PopupAction.PollInterval, seconds),
            Presentation.Interval(seconds) + (seconds == 60 ? "（默认）" : ""))).ToArray();

    private readonly float[] progress = new float[Rows.Length];
    private readonly float[] from = new float[Rows.Length];
    private readonly float[] renderedProgress = new float[Rows.Length];
    private readonly int[] offsets = new int[Rows.Length];
    private SettingChoice[] trayChoices = TrayModeChoices;
    private float renderedHeight = -1;
    private long started;

    public SettingRow? Expanded { get; private set; }
    public bool Animating { get; private set; }
    public bool HasDiagnosticFooter => hasDiagnosticFooter;
    private int BaseHeight => PanelLayout.SettingsHeight + (hasDiagnosticFooter ? PanelLayout.DiagnosticFooterExtraHeight : 0);
    public int FooterHeight => hasDiagnosticFooter ? PanelLayout.DiagnosticFooterHeight : PanelLayout.FooterHeight;
    private int Capacity => Math.Clamp((availableHeight - BaseHeight) / OptionHeight, 1, MaxVisibleOptions);
    public int SurfaceHeight => BaseHeight + Capacity * OptionHeight;
    public float ExtraHeight => VisibleHeight(SettingRow.TrayMode) + VisibleHeight(SettingRow.Threshold) +
        VisibleHeight(SettingRow.PollInterval) + VisibleHeight(SettingRow.DisplayLocation) +
        VisibleHeight(SettingRow.Theme);
    public float Height => BaseHeight + ExtraHeight;

    public void UpdateTrayChoices(TrayModel model, IReadOnlyList<DeviceStatus> devices)
    {
        trayChoices = model.Settings.TrayMode == TrayDisplayMode.Custom ?
            [.. TrayModeChoices, .. devices.Select(device =>
                new SettingChoice(new(PopupAction.TrayToggle, DeviceId: device.Device.Id), model.Name(device.Device)))] :
            TrayModeChoices;
        offsets[(int)SettingRow.TrayMode] = Math.Min(Offset(SettingRow.TrayMode), MaxOffset(SettingRow.TrayMode));
    }

    public SettingChoice[] Choices(SettingRow row) => row switch
    {
        SettingRow.TrayMode => trayChoices,
        SettingRow.DisplayLocation => DisplayLocationChoices,
        SettingRow.Theme => ThemeChoices,
        SettingRow.Threshold => [.. ThresholdChoices, .. ReminderToggleChoices],
        SettingRow.PollInterval => PollChoices,
        _ => []
    };

    private int Count(SettingRow row) => Choices(row).Length;

    public int VisibleCount(SettingRow row) => Math.Min(Count(row), Capacity);
    public int Offset(SettingRow row) => offsets[(int)row];
    public int MaxOffset(SettingRow row) => Count(row) - VisibleCount(row);
    public float Progress(SettingRow row) => progress[(int)row];
    public float VisibleHeight(SettingRow row) => VisibleCount(row) * OptionHeight * Progress(row);

    public PanelRect RowRect(SettingRow row)
    {
        float y = StackTop;
        for (int index = 0; ; index++)
        {
            if (index % SectionSize == 0)
                y += (index > 0 ? 1 + SectionGap : 0) + HeaderHeight + 1;
            if (index == (int)row)
                break;
            y += RowHeight + VisibleHeight((SettingRow)index);
        }
        return new(CardX + 1, y, CardWidth - 2, RowHeight);
    }

    /// <summary>一组设置的卡片外框（含展开的选项）。</summary>
    public PanelRect SectionRect(int section)
    {
        var first = RowRect((SettingRow)(section * SectionSize));
        var last = (SettingRow)(section * SectionSize + SectionSize - 1);
        float bottom = RowRect(last).Y + RowHeight + VisibleHeight(last) + 1;
        return new(CardX, first.Y - 1, CardWidth, bottom - first.Y + 1);
    }

    public PanelRect OptionsRect(SettingRow row)
    {
        var parent = RowRect(row);
        return parent with { Y = parent.Y + RowHeight, Height = VisibleHeight(row) };
    }

    public PanelRect OptionRect(SettingRow row, int index)
    {
        var area = OptionsRect(row);
        return area with { Y = area.Y + (index - Offset(row)) * OptionHeight, Height = OptionHeight };
    }

    public PanelRect OptionHitRect(SettingRow row, int index)
    {
        var option = OptionRect(row, index);
        if (Expanded != row || index < Offset(row) || index >= Count(row))
            return option with { Height = 0 };
        var area = OptionsRect(row);
        return option with { Height = Math.Clamp(area.Y + area.Height - option.Y, 0, OptionHeight) };
    }

    public bool Scroll(int step)
    {
        if (Expanded is not { } row)
            return false;
        int current = Offset(row);
        int next = Math.Clamp(current + step, 0, MaxOffset(row));
        offsets[(int)row] = next;
        return next != current;
    }

    public bool Toggle(SettingRow row, long time, bool animate, int selectedIndex)
    {
        if (Count(row) == 0)
            return false;
        if (Expanded == row)
            return Collapse(time, animate);
        offsets[(int)row] = Math.Clamp(selectedIndex - VisibleCount(row) + 1, 0, MaxOffset(row));
        SetExpanded(row, time, animate);
        return true;
    }

    public bool Collapse(long time, bool animate)
    {
        if (Expanded is null)
            return false;
        SetExpanded(null, time, animate);
        return true;
    }

    private void SetExpanded(SettingRow? row, long time, bool animate)
    {
        // 切换时从当前帧接续，旧选项退场的同时新选项展开。
        Advance(time);
        progress.CopyTo(from, 0);
        Expanded = row;
        started = time;
        Animating = animate;
        renderedHeight = -1;
        Advance(time);
    }

    /// <summary>
    /// D2（#14）：推进动画并返回画面是否发生可见变化；缓动尾段像素与箭头角度均未变化时返回 false，避免无效重绘。
    /// </summary>
    public bool Advance(long time, float scale = 1f)
    {
        bool wasAnimating = Animating;
        double elapsed = (time - started) / (double)PanelLayout.TransitionMilliseconds;
        float eased = Animating ? (float)PanelLayout.Ease(elapsed) : 1;
        Animating &= elapsed < 1;
        foreach (var row in Rows)
        {
            float target = Expanded == row ? 1 : 0;
            progress[(int)row] = from[(int)row] + (target - from[(int)row]) * eased;
        }

        bool finished = wasAnimating && !Animating;
        float nextHeight = Height;
        bool heightChanged = renderedHeight < 0 ||
            MathF.Round(nextHeight * scale) != MathF.Round(renderedHeight * scale);
        bool rowChanged = false;
        for (int i = 0; i < progress.Length; i++)
        {
            if (MathF.Abs(progress[i] - renderedProgress[i]) >= 0.005f)
            {
                rowChanged = true;
                break;
            }
        }

        if (!finished && !heightChanged && !rowChanged)
            return false;

        renderedHeight = nextHeight;
        progress.CopyTo(renderedProgress, 0);
        return true;
    }

    public bool Select(PopupTarget target, TrayModel model, long time, bool animate, Action<TraySettings> save)
    {
        var settings = model.Settings;
        TraySettings? next = (Expanded, target.Action) switch
        {
            (SettingRow.TrayMode, PopupAction.TrayMode) when TrayModeChoices.Any(choice => choice.Target == target) =>
                settings with { TrayMode = (TrayDisplayMode)target.Value },
            (SettingRow.DisplayLocation, PopupAction.DisplayLocation) when DisplayLocationChoices.Any(choice => choice.Target == target) =>
                settings with { DisplayLocation = (TrayDisplayLocation)target.Value },
            (SettingRow.Theme, PopupAction.Theme) when ThemeChoices.Any(choice => choice.Target == target) =>
                settings with { Theme = (ThemeMode)target.Value },
            (SettingRow.Threshold, PopupAction.Threshold) when ThresholdChoices.Any(choice => choice.Target == target) =>
                settings with { LowBatteryThreshold = target.Value },
            (SettingRow.PollInterval, PopupAction.PollInterval) when PollChoices.Any(choice => choice.Target == target) =>
                settings with { PollSeconds = target.Value },
            (SettingRow.Threshold, PopupAction.GameEntryReminder) =>
                settings with { GameEntryReminder = !settings.GameEntryReminderEnabled },
            (SettingRow.Threshold, PopupAction.FullChargeReminder) =>
                settings with { FullChargeReminder = !settings.FullChargeReminder },
            _ => null
        };
        if (next is null)
            return false;
        // 勾选类选项（托盘设备、两个提醒开关）保持展开，方便连续调整。
        if (target.Action is not (PopupAction.TrayMode or PopupAction.GameEntryReminder or PopupAction.FullChargeReminder))
            Collapse(time, animate);
        save(next);
        return true;
    }
}

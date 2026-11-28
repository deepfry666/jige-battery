namespace Jige.Tray.Windows;

/// <summary>设置行按显示顺序排列，分三组：显示位置与托盘/任务栏（2 或 4 行）/ 电量与轮询（3 行）/ 外观与通用（3 行）。</summary>
internal enum SettingRow
{
    DisplayLocation, TrayMode, TaskbarStyle, TaskbarColor,
    Threshold, RuntimeEstimate, PollInterval,
    Theme, GameSilent, Autostart
}

internal readonly record struct SettingChoice(PopupTarget Target, string Label);

internal sealed class SettingsPanel(int availableHeight, bool hasDiagnosticFooter = false, bool showTaskbarRows = true)
{
    public const int CardX = 9, CardWidth = PanelLayout.Width - 18;
    public const int OptionHeight = PanelLayout.SettingsOptionHeight, Right = CardX + CardWidth - 13;
    public const int StackTop = PanelLayout.SettingsStackTop, SectionGap = PanelLayout.SettingsSectionGap;
    public const int SectionCount = PanelLayout.SettingsSections;
    public static readonly int[] SectionSizes = [4, 3, 3];
    private static readonly int[] SectionStarts = [0, 4, 7];
    private const int TaskbarRowCount = PanelLayout.SettingsMaxRowCount - PanelLayout.SettingsTrayRowCount;
    private const int MaxVisibleOptions = 6;

    public static ReadOnlySpan<SettingRow> Rows =>
        [SettingRow.DisplayLocation, SettingRow.TrayMode, SettingRow.TaskbarStyle, SettingRow.TaskbarColor,
            SettingRow.Threshold, SettingRow.RuntimeEstimate, SettingRow.PollInterval,
            SettingRow.Theme, SettingRow.GameSilent, SettingRow.Autostart];

    public static bool IsTaskbarRow(SettingRow row) =>
        row is SettingRow.TaskbarStyle or SettingRow.TaskbarColor;

    public static int SectionOf(SettingRow row)
    {
        int index = (int)row;
        for (int section = SectionStarts.Length - 1; section >= 0; section--)
            if (index >= SectionStarts[section])
                return section;
        return 0;
    }

    public static bool IsSectionStart(SettingRow row) => (int)row == SectionStarts[SectionOf(row)];

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

    public static readonly SettingChoice[] TaskbarStyleChoices =
    [
        new(new(PopupAction.TaskbarStyle, (int)TaskbarStripStyle.Auto), "自动融入（推荐）"),
        new(new(PopupAction.TaskbarStyle, (int)TaskbarStripStyle.Frameless), "极简无框"),
        new(new(PopupAction.TaskbarStyle, (int)TaskbarStripStyle.Capsule), "磨砂胶囊"),
        new(new(PopupAction.TaskbarStyle, (int)TaskbarStripStyle.Badges), "分块徽章"),
        new(new(PopupAction.TaskbarStyle, (int)TaskbarStripStyle.Meter), "底栏格条")
    ];

    public static readonly SettingChoice[] TaskbarColorChoices =
    [
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.Auto), "自动适配（推荐）"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.Light), "暖白亮彩（深底）"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.Dark), "墨黑深彩（浅底）"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.HighContrast), "高对比锐利"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.Monochrome), "极简纯色"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.IceBlue), "冰蓝赛博"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.Sakura), "樱花粉"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.Amber), "复古琥珀"),
        new(new(PopupAction.TaskbarColor, (int)TaskbarStripColorScheme.NeonGreen), "荧光绿")
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

    private static readonly SettingChoice[] ThresholdRowChoices = [.. ThresholdChoices, .. ReminderToggleChoices];

    public static readonly SettingChoice[] PollChoices = TraySettings.PollIntervals.Select(seconds =>
        new SettingChoice(new(PopupAction.PollInterval, seconds),
            Presentation.Interval(seconds) + (seconds == 60 ? "（默认）" : ""))).ToArray();

    private readonly float[] progress = new float[Rows.Length];
    private readonly float[] from = new float[Rows.Length];
    private readonly float[] renderedProgress = new float[Rows.Length];
    private readonly float[] optionHeight = new float[Rows.Length];
    private readonly float[] optionHeightFrom = new float[Rows.Length];
    private readonly int[] offsets = new int[Rows.Length];
    private SettingChoice[] trayChoices = TrayModeChoices;
    private float taskbarProgress = showTaskbarRows ? 1f : 0f;
    private float taskbarFrom = showTaskbarRows ? 1f : 0f;
    private float renderedTaskbarProgress = showTaskbarRows ? 1f : 0f;
    private float rowHeightCurrent = RowHeightFor(availableHeight, showTaskbarRows);
    private float rowHeightFrom = RowHeightFor(availableHeight, showTaskbarRows);
    private float renderedRowHeight = RowHeightFor(availableHeight, showTaskbarRows);
    private float renderedHeight = -1;
    private long started;

    private static int RowHeightFor(int height, bool taskbarRows) =>
        PanelLayout.CompactSettings(height, PanelLayout.SettingsRowCount(taskbarRows))
            ? PanelLayout.SettingsCompactRowHeight : PanelLayout.SettingsRowHeight;

    public SettingRow? Expanded { get; private set; }
    public bool Animating { get; private set; }
    public bool HasDiagnosticFooter => hasDiagnosticFooter;
    public bool ShowTaskbarRows { get; private set; } = showTaskbarRows;
    public int VisibleRowCount => PanelLayout.SettingsRowCount(ShowTaskbarRows);
    /// <summary>屏幕太矮时收紧行高，见 <see cref="PanelLayout.CompactSettings"/>。</summary>
    public bool Compact => PanelLayout.CompactSettings(availableHeight, VisibleRowCount);
    public int RowHeight => Compact ? PanelLayout.SettingsCompactRowHeight : PanelLayout.SettingsRowHeight;
    public float CurrentRowHeight => rowHeightCurrent;
    public float TaskbarRowsProgress => taskbarProgress;
    public int FooterHeight => hasDiagnosticFooter ? PanelLayout.DiagnosticFooterHeight : PanelLayout.FooterHeight;
    private int BaseHeight => PanelLayout.SettingsHeightFor(availableHeight, VisibleRowCount) +
        (hasDiagnosticFooter ? PanelLayout.DiagnosticFooterExtraHeight : 0);
    private float BaseHeightFloat => PanelLayout.SettingsChromeHeight +
        (PanelLayout.SettingsTrayRowCount + TaskbarRowCount * taskbarProgress) * rowHeightCurrent +
        (hasDiagnosticFooter ? PanelLayout.DiagnosticFooterExtraHeight : 0);
    private int MaxBaseHeight => Math.Max(
        PanelLayout.SettingsHeightFor(availableHeight, PanelLayout.SettingsMaxRowCount),
        PanelLayout.SettingsHeightFor(availableHeight, PanelLayout.SettingsTrayRowCount)) +
        (hasDiagnosticFooter ? PanelLayout.DiagnosticFooterExtraHeight : 0);
    private int Capacity => Math.Clamp((availableHeight - BaseHeight) / OptionHeight, 1, MaxVisibleOptions);
    public int SurfaceHeight => MaxBaseHeight + MaxVisibleOptions * OptionHeight;
    public float ExtraHeight => VisibleHeight(SettingRow.DisplayLocation) + VisibleHeight(SettingRow.TrayMode) +
        VisibleHeight(SettingRow.TaskbarStyle) + VisibleHeight(SettingRow.TaskbarColor) +
        VisibleHeight(SettingRow.Threshold) + VisibleHeight(SettingRow.PollInterval) +
        VisibleHeight(SettingRow.Theme);
    public float Height => BaseHeightFloat + ExtraHeight;

    public bool IsVisible(SettingRow row) => !IsTaskbarRow(row) || ShowTaskbarRows;

    public bool IsSectionEnd(SettingRow row) => row switch
    {
        SettingRow.TrayMode => !ShowTaskbarRows && taskbarProgress <= 0,
        SettingRow.TaskbarColor => ShowTaskbarRows || taskbarProgress > 0,
        SettingRow.PollInterval or SettingRow.Autostart => true,
        _ => false
    };

    public void UpdateTrayChoices(TrayModel model, IReadOnlyList<DeviceStatus> devices)
    {
        trayChoices = model.Settings.TrayMode == TrayDisplayMode.Custom ?
            [.. TrayModeChoices, .. devices.Select(device =>
                new SettingChoice(new(PopupAction.TrayToggle, DeviceId: device.Device.Id), model.Name(device.Device)))] :
            TrayModeChoices;
        offsets[(int)SettingRow.TrayMode] = Math.Min(Offset(SettingRow.TrayMode), MaxOffset(SettingRow.TrayMode));
        if (!Animating && Expanded == SettingRow.TrayMode)
            optionHeight[(int)SettingRow.TrayMode] = VisibleCount(SettingRow.TrayMode) * OptionHeight;
    }

    public SettingChoice[] Choices(SettingRow row) => row switch
    {
        SettingRow.TrayMode => trayChoices,
        SettingRow.DisplayLocation => DisplayLocationChoices,
        SettingRow.TaskbarStyle => TaskbarStyleChoices,
        SettingRow.TaskbarColor => TaskbarColorChoices,
        SettingRow.Theme => ThemeChoices,
        SettingRow.Threshold => ThresholdRowChoices,
        SettingRow.PollInterval => PollChoices,
        _ => []
    };

    private int Count(SettingRow row) => Choices(row).Length;

    public int VisibleCount(SettingRow row) => Math.Min(Count(row), Capacity);
    public int Offset(SettingRow row) => offsets[(int)row];
    public int MaxOffset(SettingRow row) => Count(row) - VisibleCount(row);
    public float Progress(SettingRow row) => progress[(int)row];
    public float VisibleHeight(SettingRow row) => optionHeight[(int)row];
    private float TaskbarRowsHeight => TaskbarRowCount * rowHeightCurrent * taskbarProgress +
        VisibleHeight(SettingRow.TaskbarStyle) + VisibleHeight(SettingRow.TaskbarColor);

    /// <summary>「任务栏样式」「任务栏配色」两行在第一组卡片底部平滑展开/收起时的裁剪区域。</summary>
    public PanelRect TaskbarRowsClipRect()
    {
        float y = StackTop + 1 + rowHeightCurrent + VisibleHeight(SettingRow.DisplayLocation) +
            rowHeightCurrent + VisibleHeight(SettingRow.TrayMode);
        return new(CardX + 1, y, CardWidth - 2, TaskbarRowsHeight);
    }

    /// <summary>行内容绘制区域（始终按完整行高排版，条件隐藏行由裁剪区平滑截断，避免文字被压扁）。</summary>
    public PanelRect RowDrawRect(SettingRow row) =>
        new(CardX + 1, RowTop(row), CardWidth - 2, rowHeightCurrent);

    /// <summary>行点击与焦点区域；托盘模式下隐藏的任务栏两行高度为 0，无点击热区。</summary>
    public PanelRect RowRect(SettingRow row)
    {
        var draw = RowDrawRect(row);
        if (!IsTaskbarRow(row))
            return draw;
        if (!ShowTaskbarRows)
            return draw with { Height = 0 };
        var clip = TaskbarRowsClipRect();
        return draw with { Height = Math.Clamp(clip.Y + clip.Height - draw.Y, 0, draw.Height) };
    }

    private float RowTop(SettingRow row)
    {
        float y0 = StackTop + 1;
        if (row == SettingRow.DisplayLocation)
            return y0;
        float yTray = y0 + rowHeightCurrent + VisibleHeight(SettingRow.DisplayLocation);
        if (row == SettingRow.TrayMode)
            return yTray;
        float yStyle = yTray + rowHeightCurrent + VisibleHeight(SettingRow.TrayMode);
        if (row == SettingRow.TaskbarStyle)
            return yStyle;
        if (row == SettingRow.TaskbarColor)
            return yStyle + rowHeightCurrent + VisibleHeight(SettingRow.TaskbarStyle);

        float yThreshold = yStyle + TaskbarRowsHeight + 1 + SectionGap + 1;
        if (row == SettingRow.Threshold)
            return yThreshold;
        float yRuntime = yThreshold + rowHeightCurrent + VisibleHeight(SettingRow.Threshold);
        if (row == SettingRow.RuntimeEstimate)
            return yRuntime;
        float yPoll = yRuntime + rowHeightCurrent + VisibleHeight(SettingRow.RuntimeEstimate);
        if (row == SettingRow.PollInterval)
            return yPoll;

        float yTheme = yPoll + rowHeightCurrent + VisibleHeight(SettingRow.PollInterval) + 1 + SectionGap + 1;
        if (row == SettingRow.Theme)
            return yTheme;
        float yGame = yTheme + rowHeightCurrent + VisibleHeight(SettingRow.Theme);
        if (row == SettingRow.GameSilent)
            return yGame;
        return yGame + rowHeightCurrent + VisibleHeight(SettingRow.GameSilent);
    }

    /// <summary>一组设置的卡片外框（含展开的选项与条件可见行）。</summary>
    public PanelRect SectionRect(int section)
    {
        if (section == 0)
        {
            float content = 2 * rowHeightCurrent + VisibleHeight(SettingRow.DisplayLocation) +
                VisibleHeight(SettingRow.TrayMode) + TaskbarRowsHeight;
            return new(CardX, StackTop, CardWidth, content + 2);
        }
        if (section == 1)
        {
            float top = RowTop(SettingRow.Threshold) - 1;
            float content = 3 * rowHeightCurrent + VisibleHeight(SettingRow.Threshold) +
                VisibleHeight(SettingRow.RuntimeEstimate) + VisibleHeight(SettingRow.PollInterval);
            return new(CardX, top, CardWidth, content + 2);
        }
        float top2 = RowTop(SettingRow.Theme) - 1;
        float content2 = 3 * rowHeightCurrent + VisibleHeight(SettingRow.Theme) +
            VisibleHeight(SettingRow.GameSilent) + VisibleHeight(SettingRow.Autostart);
        return new(CardX, top2, CardWidth, content2 + 2);
    }

    public PanelRect OptionsRect(SettingRow row)
    {
        var parent = RowDrawRect(row);
        return parent with { Y = parent.Y + rowHeightCurrent, Height = VisibleHeight(row) };
    }

    public PanelRect OptionRect(SettingRow row, int index)
    {
        var area = OptionsRect(row);
        return area with { Y = area.Y + (index - Offset(row)) * OptionHeight, Height = OptionHeight };
    }

    public PanelRect OptionHitRect(SettingRow row, int index)
    {
        var option = OptionRect(row, index);
        if (!IsVisible(row) || Expanded != row || index < Offset(row) || index >= Count(row))
            return option with { Height = 0 };
        var area = OptionsRect(row);
        float bottom = area.Y + area.Height;
        if (IsTaskbarRow(row))
        {
            var clip = TaskbarRowsClipRect();
            bottom = MathF.Min(bottom, clip.Y + clip.Height);
        }
        return option with { Height = Math.Clamp(bottom - option.Y, 0, OptionHeight) };
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
        if (!IsVisible(row) || Count(row) == 0)
            return false;
        if (Expanded == row)
            return Collapse(time, animate);
        offsets[(int)row] = Math.Clamp(selectedIndex - VisibleCount(row) + 1, 0, MaxOffset(row));
        SetState(row, ShowTaskbarRows, time, animate);
        return true;
    }

    public bool Collapse(long time, bool animate)
    {
        if (Expanded is null)
            return false;
        SetState(null, ShowTaskbarRows, time, animate);
        return true;
    }

    public bool SetShowTaskbarRows(bool visible, long time, bool animate)
    {
        if (ShowTaskbarRows == visible)
            return false;
        var nextExpanded = !visible && Expanded is { } active && IsTaskbarRow(active) ? null : Expanded;
        SetState(nextExpanded, visible, time, animate);
        return true;
    }

    public void SyncTaskbarRows(bool visible)
    {
        if (ShowTaskbarRows != visible)
            SetShowTaskbarRows(visible, Environment.TickCount64, animate: false);
    }

    private void SetState(SettingRow? expanded, bool showTaskbar, long time, bool animate)
    {
        // 切换时从当前帧接续，旧选项退场、新选项展开与条件行显隐共用同一缓动曲线。
        Advance(time);
        progress.CopyTo(from, 0);
        optionHeight.CopyTo(optionHeightFrom, 0);
        taskbarFrom = taskbarProgress;
        rowHeightFrom = rowHeightCurrent;
        Expanded = expanded;
        ShowTaskbarRows = showTaskbar;
        foreach (var row in Rows)
            offsets[(int)row] = Math.Min(Offset(row), MaxOffset(row));
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
            float targetHeight = Expanded == row ? VisibleCount(row) * OptionHeight : 0;
            optionHeight[(int)row] = optionHeightFrom[(int)row] + (targetHeight - optionHeightFrom[(int)row]) * eased;
        }
        float targetTaskbar = ShowTaskbarRows ? 1 : 0;
        taskbarProgress = taskbarFrom + (targetTaskbar - taskbarFrom) * eased;
        float targetRowHeight = RowHeight;
        rowHeightCurrent = rowHeightFrom + (targetRowHeight - rowHeightFrom) * eased;

        bool finished = wasAnimating && !Animating;
        float nextHeight = Height;
        bool heightChanged = renderedHeight < 0 ||
            MathF.Round(nextHeight * scale) != MathF.Round(renderedHeight * scale);
        bool rowChanged = MathF.Abs(taskbarProgress - renderedTaskbarProgress) >= 0.005f ||
            MathF.Abs(rowHeightCurrent - renderedRowHeight) >= 0.05f;
        if (!rowChanged)
        {
            for (int i = 0; i < progress.Length; i++)
            {
                if (MathF.Abs(progress[i] - renderedProgress[i]) >= 0.005f)
                {
                    rowChanged = true;
                    break;
                }
            }
        }

        if (!finished && !heightChanged && !rowChanged)
            return false;

        renderedHeight = nextHeight;
        renderedTaskbarProgress = taskbarProgress;
        renderedRowHeight = rowHeightCurrent;
        progress.CopyTo(renderedProgress, 0);
        return true;
    }

    public bool Select(PopupTarget target, TrayModel model, long time, bool animate, Action<TraySettings> save)
    {
        if (Expanded is { } active && !IsVisible(active))
            return false;
        var settings = model.Settings;
        TraySettings? next = (Expanded, target.Action) switch
        {
            (SettingRow.TrayMode, PopupAction.TrayMode) when TrayModeChoices.Any(choice => choice.Target == target) =>
                settings with { TrayMode = (TrayDisplayMode)target.Value },
            (SettingRow.DisplayLocation, PopupAction.DisplayLocation) when DisplayLocationChoices.Any(choice => choice.Target == target) =>
                settings with { DisplayLocation = (TrayDisplayLocation)target.Value },
            (SettingRow.TaskbarStyle, PopupAction.TaskbarStyle) when TaskbarStyleChoices.Any(choice => choice.Target == target) =>
                settings with { TaskbarStyle = (TaskbarStripStyle)target.Value },
            (SettingRow.TaskbarColor, PopupAction.TaskbarColor) when TaskbarColorChoices.Any(choice => choice.Target == target) =>
                settings with { TaskbarColor = (TaskbarStripColorScheme)target.Value },
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
        if (target.Action == PopupAction.DisplayLocation)
        {
            bool nextShowTaskbar = next.DisplayLocation != TrayDisplayLocation.TrayIcons;
            SetState(expanded: null, showTaskbar: nextShowTaskbar, time, animate);
        }
        // 勾选类选项与任务栏样式/配色保持展开，方便边看任务栏效果边连续切换。
        else if (target.Action is not (PopupAction.TrayMode or PopupAction.TaskbarStyle or PopupAction.TaskbarColor
            or PopupAction.GameEntryReminder or PopupAction.FullChargeReminder))
        {
            Collapse(time, animate);
        }
        save(next);
        return true;
    }
}

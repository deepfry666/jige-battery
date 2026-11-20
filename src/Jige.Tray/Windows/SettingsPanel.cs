namespace Jige.Tray.Windows;

internal enum SettingRow { TrayMode, Threshold, GameSilent, PollInterval, Autostart, Bluetooth }

internal readonly record struct SettingChoice(PopupTarget Target, string Label);

internal sealed class SettingsPanel(int availableHeight)
{
    public const int CardX = 9, CardY = 51, CardWidth = PanelLayout.Width - 18;
    public const int RowHeight = 44, OptionHeight = 34, Right = CardX + CardWidth - 13;
    public const int CollapsedCardHeight = 6 * RowHeight + 2;
    private const int MaxVisibleOptions = 6;

    public static ReadOnlySpan<SettingRow> Rows =>
        [SettingRow.TrayMode, SettingRow.Threshold, SettingRow.GameSilent, SettingRow.PollInterval, SettingRow.Autostart, SettingRow.Bluetooth];

    public static readonly SettingChoice[] TrayModeChoices =
    [
        new(new(PopupAction.TrayMode, (int)TrayDisplayMode.LowestBattery), "只显示电量最低的一台"),
        new(new(PopupAction.TrayMode, (int)TrayDisplayMode.Custom), "自定义")
    ];

    public static readonly SettingChoice[] ThresholdChoices =
    [
        new(new(PopupAction.Threshold, 10), "10%"),
        new(new(PopupAction.Threshold, 15), "15%（默认）"),
        new(new(PopupAction.Threshold, 20), "20%"),
        new(new(PopupAction.Threshold, 30), "30%")
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
    private int Capacity => Math.Clamp((availableHeight - PanelLayout.SettingsHeight) / OptionHeight, 1, MaxVisibleOptions);
    public int SurfaceHeight => PanelLayout.SettingsHeight + Capacity * OptionHeight;
    public float ExtraHeight => VisibleHeight(SettingRow.TrayMode) + VisibleHeight(SettingRow.Threshold) +
        VisibleHeight(SettingRow.PollInterval);
    public float Height => PanelLayout.SettingsHeight + ExtraHeight;

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
        SettingRow.Threshold => ThresholdChoices,
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
        float y = CardY + 1 + (int)row * RowHeight;
        for (int index = 0; index < (int)row; index++)
            y += VisibleHeight((SettingRow)index);
        return new(CardX + 1, y, CardWidth - 2, RowHeight);
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
            (SettingRow.Threshold, PopupAction.Threshold) when ThresholdChoices.Any(choice => choice.Target == target) =>
                settings with { LowBatteryThreshold = target.Value },
            (SettingRow.PollInterval, PopupAction.PollInterval) when PollChoices.Any(choice => choice.Target == target) =>
                settings with { PollSeconds = target.Value },
            _ => null
        };
        if (next is null)
            return false;
        if (target.Action != PopupAction.TrayMode)
            Collapse(time, animate);
        save(next);
        return true;
    }
}

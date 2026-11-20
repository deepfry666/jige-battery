namespace Jige.Tray;

/// <summary>
/// 基于单调时钟（毫秒）调度完整电量查询与轻量在线心跳：
/// 修改系统时间不会打乱节奏；休眠恢复与设备插拔立即安排重新枚举与查询。
/// </summary>
internal sealed class PollSchedule(long createdMs)
{
    public const int HeartbeatSeconds = 8;
    public const int GameHeartbeatSeconds = 30;
    /// <summary>R3：键鼠输入触发的休眠设备补心跳冷却时长，最多每 2 秒一次。</summary>
    public const int InputWakeCooldownMs = 2_000;
    /// <summary>
    /// 休眠设备一直不回复时（例如协议不通，或设备确实关着），补心跳的冷却按 2、4、8、16 秒翻倍，最长 30 秒；
    /// 避免用户持续操作电脑时，每 2 秒占用一次接收器。设备重新进入休眠时从 2 秒重新开始。
    /// </summary>
    public const int InputWakeMaxCooldownMs = 30_000;
    /// <summary>R3：存在休眠设备时检查 GetLastInputInfo 的轮询步长（仅在非游戏中且有休眠设备时启用）。</summary>
    public const int InputCheckIntervalMs = 500;

    private long? lastPollStartedMs;
    private long? lastHeartbeatStartedMs;
    private uint? lastObservedInputTick;
    private long lastInputWakeMs = long.MinValue / 2;
    private int inputWakeStreak;
    private bool requested;
    private bool userRequested;
    private bool urgentRescan;
    private bool forceRescan = true;

    public bool Running { get; private set; }
    public bool HeartbeatRunning { get; private set; }
    public bool StartedByUser { get; private set; }

    public void Request(bool userInitiated = true, bool rescan = false)
    {
        if (rescan)
            forceRescan = true;
        if (Running)
            return;
        requested = true;
        userRequested |= userInitiated;
    }

    /// <summary>系统从睡眠/休眠恢复：立即重新枚举并查询一次。</summary>
    public void NotifyPowerResume()
    {
        forceRescan = true;
        urgentRescan = true;
        requested = true;
    }

    /// <summary>检测到接收器插拔：失效缓存并尽快刷新。</summary>
    public void NotifyDeviceChange()
    {
        forceRescan = true;
        urgentRescan = true;
        requested = true;
    }

    public long? Due(TraySettings settings, long nowMs, bool game)
    {
        if (Running)
            return null;
        if (userRequested || urgentRescan)
            return nowMs;
        if (game)
            return (lastPollStartedMs ?? createdMs) + settings.GamePollSeconds * 1000L;
        if (requested || lastPollStartedMs is null)
            return nowMs;
        return lastPollStartedMs.Value + settings.PollSeconds * 1000L;
    }

    /// <summary>轻量在线心跳到期时间：平时 8 秒一次，游戏中 30 秒一次。</summary>
    public long? HeartbeatDue(long nowMs, bool game, bool hasHeartbeatDevices)
    {
        if (Running || HeartbeatRunning || !hasHeartbeatDevices || lastPollStartedMs is null)
            return null;
        long anchor = lastHeartbeatStartedMs is { } hb && hb > lastPollStartedMs.Value
            ? hb : lastPollStartedMs.Value;
        long intervalMs = (game ? GameHeartbeatSeconds : HeartbeatSeconds) * 1000L;
        return anchor + intervalMs;
    }

    public bool Start(long nowMs, bool game)
    {
        Running = true;
        StartedByUser = userRequested;
        lastPollStartedMs = nowMs;
        lastHeartbeatStartedMs = nowMs;
        bool shouldRescan = forceRescan;
        forceRescan = false;
        urgentRescan = false;
        if (!game || userRequested)
            requested = false;
        userRequested = false;
        return shouldRescan;
    }

    public void Complete() => Running = false;

    public void StartHeartbeat(long nowMs, bool inputWake = false)
    {
        HeartbeatRunning = true;
        if (inputWake)
        {
            lastInputWakeMs = nowMs;
            inputWakeStreak++;
        }
        else
            lastHeartbeatStartedMs = nowMs;
    }

    public void CompleteHeartbeat() => HeartbeatRunning = false;

    /// <summary>记录当前系统最后输入时间戳作为基线，避免设备刚进入休眠就被旧输入误触发。</summary>
    public void SyncLastInputTick(uint? currentInputTick)
    {
        if (currentInputTick is uint tick)
            lastObservedInputTick = tick;
        inputWakeStreak = 0;
    }

    /// <summary>当前补心跳冷却：首次唤醒后 2 秒，此后每次翻倍，最长 30 秒。</summary>
    public long InputWakeCooldown =>
        Math.Min(InputWakeMaxCooldownMs, (long)InputWakeCooldownMs << Math.Clamp(inputWakeStreak - 1, 0, 8));

    /// <summary>
    /// R3：用户有新的键鼠输入（GetLastInputInfo 的 dwTime 发生变化）且存在休眠设备时，
    /// 立即允许补一次心跳（最多每 2 秒一次）；不安装钩子、不记录任何输入内容。
    /// 游戏中保持 30 秒退避，不对休眠中的副外设按 2 秒高频发包。
    /// </summary>
    public bool ShouldWakeHeartbeatOnInput(uint? currentInputTick, long nowMs, bool hasAsleepHeartbeatDevices, bool game = false)
    {
        if (currentInputTick is not uint tick)
            return false;
        bool inputChanged = lastObservedInputTick is uint previous && previous != tick;
        lastObservedInputTick = tick;
        if (!inputChanged || !hasAsleepHeartbeatDevices || game || Running || HeartbeatRunning)
            return false;
        return nowMs - lastInputWakeMs >= InputWakeCooldown;
    }
}

/// <summary>
/// D3（#22）：DPI、显示器或主题变化防抖与编辑保护。
/// 1) 过滤与界面外观/工作区无关的 WM_SETTINGCHANGE；
/// 2) 连续收到多条系统设置变化消息时合并为一次重建；
/// 3) 行内改名尚未结束时暂缓重建，避免强制提交未完成的别名。
/// </summary>
internal sealed class DesktopRefreshDebouncer
{
    public const int DebounceMs = 150;

    private long? dueAtMs;
    private bool pendingReset;
    private bool deferredForEdit;

    public bool HasPending => dueAtMs.HasValue || deferredForEdit;

    public static bool IsRelevantSettingChange(nuint wParam, string? area)
    {
        // SPI_SETNONCLIENTMETRICS(0x2A)、SPI_SETWORKAREA(0x2F)、SPI_SETHIGHCONTRAST(0x43)、SPI_SETCLIENTAREAANIMATION(0x1043)。
        if (wParam is 0x002A or 0x002F or 0x0043 or 0x1043)
            return true;
        if (string.IsNullOrEmpty(area))
            return wParam == 0;
        return area.Equals("ImmersiveColorSet", StringComparison.OrdinalIgnoreCase) ||
               area.Equals("WindowsThemeElement", StringComparison.OrdinalIgnoreCase) ||
               area.Equals("UserPreferences", StringComparison.OrdinalIgnoreCase);
    }

    public long Request(long nowMs, bool resetPopup)
    {
        pendingReset |= resetPopup;
        long nextDue = nowMs + DebounceMs;
        dueAtMs = nextDue;
        return nextDue;
    }

    public bool TryConsume(long nowMs, bool isEditing, out bool resetPopup)
    {
        resetPopup = false;
        if (dueAtMs is null && !deferredForEdit)
            return false;
        if (dueAtMs is long due && nowMs < due)
            return false;
        if (isEditing)
        {
            dueAtMs = null;
            deferredForEdit = true;
            return false;
        }
        dueAtMs = null;
        deferredForEdit = false;
        resetPopup = pendingReset;
        pendingReset = false;
        return true;
    }
}

internal enum ForegroundKind { System, Own, Other }

internal readonly record struct GameMode(bool Game, bool BlockPopups)
{
    public static GameMode Evaluate(int notificationState, bool foregroundGame, bool gameSilent)
    {
        bool game = notificationState is 2 or 3 or 4 || gameSilent && foregroundGame;
        return new(game, game);
    }

    public static bool IsGameWindow(ForegroundKind kind, Bounds frame, Bounds screen, Bounds work,
        bool hasCaption, bool knownGame)
    {
        return kind == ForegroundKind.Other && (knownGame || Covers(frame, screen) ||
            !hasCaption && Covers(frame, work));
    }

    private static bool Covers(Bounds frame, Bounds area)
    {
        const int tolerance = 2;
        return frame.Right > frame.Left && frame.Bottom > frame.Top &&
            area.Right > area.Left && area.Bottom > area.Top &&
            frame.Left <= area.Left + tolerance && frame.Top <= area.Top + tolerance &&
            frame.Right >= area.Right - tolerance && frame.Bottom >= area.Bottom - tolerance;
    }
}

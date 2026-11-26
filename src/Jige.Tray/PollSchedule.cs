using Jige.Core.Diagnostics;


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
    private bool runningTargeted;
    private HashSet<string>? targetedDeviceIds;

    public bool Running { get; private set; }
    public bool HeartbeatRunning { get; private set; }
    public bool StartedByUser { get; private set; }

    public void Request(bool userInitiated = true, bool rescan = false)
    {
        if (rescan)
        {
            forceRescan = true;
            urgentRescan = true;
            requested = true;
            userRequested |= userInitiated;
            targetedDeviceIds = null;
            return;
        }
        if (Running && !runningTargeted)
            return;
        requested = true;
        userRequested |= userInitiated;
        targetedDeviceIds = null;
    }

    /// <summary>安排仅补查指定唤醒设备的轻量查询，避免单台设备从休眠恢复时牵连全量设备重查。</summary>
    public void RequestTargeted(IEnumerable<string> deviceIds)
    {
        if ((Running && !runningTargeted) || requested || userRequested || urgentRescan)
            return;
        foreach (string id in deviceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            targetedDeviceIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            targetedDeviceIds.Add(id);
        }
    }

    /// <summary>系统从睡眠/休眠恢复：立即重新枚举并查询一次。</summary>
    public void NotifyPowerResume()
    {
        forceRescan = true;
        urgentRescan = true;
        requested = true;
        targetedDeviceIds = null;
    }

    /// <summary>检测到接收器插拔：失效缓存并尽快刷新。</summary>
    public void NotifyDeviceChange()
    {
        forceRescan = true;
        urgentRescan = true;
        requested = true;
        targetedDeviceIds = null;
    }

    /// <summary>通用设备节点变更（如 DBT_DEVNODES_CHANGED 0x0007）：仅失效缓存并在非游戏免打扰时尽快刷新，不打断游戏降频。</summary>
    public void NotifyBackgroundDeviceChange()
    {
        forceRescan = true;
        requested = true;
        targetedDeviceIds = null;
    }

    public long? Due(TraySettings settings, long nowMs, bool game)
    {
        if (Running || HeartbeatRunning)
            return null;
        if (userRequested || urgentRescan)
            return nowMs;
        if (game)
            return (lastPollStartedMs ?? createdMs) + settings.GamePollSeconds * 1000L;
        if (requested || targetedDeviceIds is { Count: > 0 } || lastPollStartedMs is null)
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

    /// <summary>若当前仅有定向补查待办且未到常规全量轮询时刻，则接管本次运行并返回目标设备 ID。</summary>
    public bool TryStartTargeted(TraySettings settings, long nowMs, bool game, out HashSet<string>? deviceIds)
    {
        deviceIds = null;
        if (Running || HeartbeatRunning || game || userRequested || urgentRescan || requested ||
            lastPollStartedMs is null || targetedDeviceIds is not { Count: > 0 } ||
            nowMs >= lastPollStartedMs.Value + settings.PollSeconds * 1000L)
            return false;

        Running = true;
        runningTargeted = true;
        StartedByUser = false;
        deviceIds = targetedDeviceIds;
        targetedDeviceIds = null;
        return true;
    }

    public bool Start(long nowMs, bool game)
    {
        Running = true;
        runningTargeted = false;
        StartedByUser = userRequested;
        lastPollStartedMs = nowMs;
        lastHeartbeatStartedMs = nowMs;
        bool shouldRescan = forceRescan;
        bool wasUrgent = urgentRescan;
        forceRescan = false;
        urgentRescan = false;
        targetedDeviceIds = null;
        if (!game || userRequested || wasUrgent)
            requested = false;
        userRequested = false;
        return shouldRescan;
    }

    public void Complete()
    {
        Running = false;
        runningTargeted = false;
    }

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

    /// <summary>下一次输入检测的合理等待毫秒数：冷却中直接睡到冷却结束，避免退避期间每 500ms 空转唤醒。</summary>
    public long InputCheckDelayMs(long nowMs)
    {
        long remaining = lastInputWakeMs + InputWakeCooldown - nowMs;
        return Math.Max(InputCheckIntervalMs, remaining);
    }

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
        if (!hasAsleepHeartbeatDevices || game || !inputChanged)
        {
            lastObservedInputTick = tick;
            return false;
        }
        if (Running || HeartbeatRunning || nowMs - lastInputWakeMs < InputWakeCooldown)
            return false;
        lastObservedInputTick = tick;
        return true;
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
        // SPI_SETDESKWALLPAPER(0x14)、SPI_SETNONCLIENTMETRICS(0x2A)、SPI_SETWORKAREA(0x2F)、
        // SPI_SETHIGHCONTRAST(0x43)、SPI_SETCLIENTAREAANIMATION(0x1043)。
        if (wParam is 0x0014 or 0x002A or 0x002F or 0x0043 or 0x1043)
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

internal enum ForegroundGame { None, Fullscreen, BorderlessWorkArea, ProcessName }

internal readonly record struct GameMode(DiagnosticGameState Status)
{
    public bool Game => Status.Active;
    public bool BlockPopups => Game;

    public static GameMode Evaluate(int notificationState, ForegroundGame foreground, bool gameSilent)
    {
        var reason = notificationState switch
        {
            3 => GameDetectionReason.D3dFullscreen,
            4 => GameDetectionReason.Presentation,
            2 => foreground == ForegroundGame.Fullscreen ? GameDetectionReason.BusyFullscreen : GameDetectionReason.None,
            _ when gameSilent => foreground switch
            {
                ForegroundGame.Fullscreen or ForegroundGame.BorderlessWorkArea => GameDetectionReason.ForegroundGeometry,
                ForegroundGame.ProcessName => GameDetectionReason.ProcessName,
                _ => GameDetectionReason.None
            },
            _ => GameDetectionReason.None
        };
        return new(new(notificationState, reason));
    }

    public static ForegroundGame ClassifyWindow(ForegroundKind kind, Bounds frame, Bounds screen, Bounds work,
        bool hasCaption)
    {
        if (kind != ForegroundKind.Other)
            return ForegroundGame.None;
        if (Covers(frame, screen))
            return ForegroundGame.Fullscreen;
        return !hasCaption && Covers(frame, work) ? ForegroundGame.BorderlessWorkArea : ForegroundGame.None;
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

internal readonly record struct StabilizedGameState(
    GameMode Game,
    bool StripFullscreen,
    uint? ExitCheckDelayMs);

/// <summary>
/// 过滤全屏游戏启动、切换显示模式或前景过渡瞬间（几十毫秒）产生的游戏模式/全屏退出抖动：
/// 进入游戏与隐藏直读条立即生效；自动退出需连续维持一段稳定时间，用户主动交互或切换设置则立即退出。
/// </summary>
internal sealed class GameModeStabilizer
{
    public const long ExitDebounceMs = 400;

    private GameMode effectiveGame;
    private bool effectiveStripFullscreen;
    private long? gameExitPendingSinceMs;
    private long? stripExitPendingSinceMs;

    public StabilizedGameState Update(
        GameMode rawGame, bool rawStripFullscreen, bool taskbarMode, long nowMs, bool immediateExit = false)
    {
        uint? delay = null;

        if (rawGame.Game)
        {
            effectiveGame = rawGame;
            gameExitPendingSinceMs = null;
        }
        else if (effectiveGame.Game)
        {
            if (immediateExit)
            {
                effectiveGame = rawGame;
                gameExitPendingSinceMs = null;
            }
            else
            {
                gameExitPendingSinceMs ??= nowMs;
                long elapsed = nowMs - gameExitPendingSinceMs.Value;
                if (elapsed < 0)
                {
                    gameExitPendingSinceMs = nowMs;
                    elapsed = 0;
                }
                if (elapsed >= ExitDebounceMs)
                {
                    effectiveGame = rawGame;
                    gameExitPendingSinceMs = null;
                }
                else
                {
                    delay = (uint)(ExitDebounceMs - elapsed);
                }
            }
        }
        else
        {
            effectiveGame = rawGame;
            gameExitPendingSinceMs = null;
        }

        if (!taskbarMode)
        {
            effectiveStripFullscreen = false;
            stripExitPendingSinceMs = null;
        }
        else if (rawStripFullscreen)
        {
            effectiveStripFullscreen = true;
            stripExitPendingSinceMs = null;
        }
        else if (effectiveStripFullscreen)
        {
            if (immediateExit)
            {
                effectiveStripFullscreen = false;
                stripExitPendingSinceMs = null;
            }
            else
            {
                stripExitPendingSinceMs ??= nowMs;
                long elapsed = nowMs - stripExitPendingSinceMs.Value;
                if (elapsed < 0)
                {
                    stripExitPendingSinceMs = nowMs;
                    elapsed = 0;
                }
                if (elapsed >= ExitDebounceMs)
                {
                    effectiveStripFullscreen = false;
                    stripExitPendingSinceMs = null;
                }
                else
                {
                    uint remaining = (uint)(ExitDebounceMs - elapsed);
                    delay = delay is uint existing ? Math.Min(existing, remaining) : remaining;
                }
            }
        }
        else
        {
            stripExitPendingSinceMs = null;
        }

        bool stripHidden = taskbarMode && (effectiveGame.Game || effectiveStripFullscreen);
        return new(effectiveGame, stripHidden, delay);
    }
}

/// <summary>
/// 当本机未检测到任何蓝牙设备时，对全机 PnP 蓝牙扫描（SetupDiGetClassDevsW ALLCLASSES）降频，
/// 避免每 30 秒遍历整机所有设备；有蓝牙设备、上次失败、收到插拔/恢复或超过 5 分钟兜底周期时照常扫描。
/// </summary>
internal sealed class BluetoothScanThrottle
{
    public const long EmptyRescanIntervalMs = 300_000;

    private readonly object sync = new();
    private bool hasScanned;
    private bool lastFailed;
    private int lastDeviceCount;
    private long lastScanMs;

    public bool ShouldScan(long nowMs, bool forceRescan)
    {
        lock (sync)
        {
            if (forceRescan || !hasScanned || lastFailed || lastDeviceCount > 0)
                return true;
            return nowMs - lastScanMs >= EmptyRescanIntervalMs;
        }
    }

    public void Record(long nowMs, int deviceCount, bool failed)
    {
        lock (sync)
        {
            hasScanned = true;
            lastScanMs = nowMs;
            lastDeviceCount = deviceCount;
            lastFailed = failed;
        }
    }

    public void Invalidate()
    {
        lock (sync)
            hasScanned = false;
    }
}

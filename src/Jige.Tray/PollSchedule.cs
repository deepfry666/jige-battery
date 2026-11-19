namespace Jige.Tray;

/// <summary>
/// 基于单调时钟（毫秒）调度完整电量查询与轻量在线心跳：
/// 修改系统时间不会打乱节奏；休眠恢复与设备插拔立即安排重新枚举与查询。
/// </summary>
internal sealed class PollSchedule(long createdMs)
{
    public const int HeartbeatSeconds = 8;
    public const int GameHeartbeatSeconds = 30;

    private long? lastPollStartedMs;
    private long? lastHeartbeatStartedMs;
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

    public void StartHeartbeat(long nowMs)
    {
        HeartbeatRunning = true;
        lastHeartbeatStartedMs = nowMs;
    }

    public void CompleteHeartbeat() => HeartbeatRunning = false;
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

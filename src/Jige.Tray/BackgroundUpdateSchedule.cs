namespace Jige.Tray;

/// <summary>
/// 后台自动检查更新的节奏（单调时钟毫秒，改系统时间不影响）：
/// 启动 3 分钟后查一次，之后每 24 小时一次；失败 3 小时后重试；
/// 游戏中或用户正在手动检查、下载时推迟 10 分钟。只检查和预先下载，安装始终由用户点击。
/// </summary>
internal sealed class BackgroundUpdateSchedule(long startedMs)
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(3);
    public static readonly TimeSpan Deferral = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan EnabledDelay = TimeSpan.FromSeconds(10);

    private long dueMs = startedMs + (long)InitialDelay.TotalMilliseconds;

    public long DueMs => dueMs;

    /// <summary>距离下次检查的毫秒数；未开启自动检查或不是安装版时返回 null。</summary>
    public long? Delay(long nowMs, bool enabled, bool installed) =>
        enabled && installed ? Math.Max(0, dueMs - nowMs) : null;

    /// <summary>到期且条件允许时返回 true；到期但正在游戏或已有更新任务时顺延。</summary>
    public bool TryStart(long nowMs, bool enabled, bool installed, bool game, bool busy)
    {
        if (!enabled || !installed || nowMs < dueMs)
            return false;
        if (game || busy)
        {
            dueMs = nowMs + (long)Deferral.TotalMilliseconds;
            return false;
        }
        // 先按失败间隔占位，避免任务意外中断后反复立刻重试；完成时再改为正常间隔。
        dueMs = nowMs + (long)RetryAfterFailure.TotalMilliseconds;
        return true;
    }

    public void Succeeded(long nowMs) => dueMs = nowMs + (long)Interval.TotalMilliseconds;

    public void Failed(long nowMs) => dueMs = nowMs + (long)RetryAfterFailure.TotalMilliseconds;

    /// <summary>用户刚打开自动检查：10 秒内补查一次，不必等到原定时间。</summary>
    public void Enabled(long nowMs) => dueMs = Math.Min(dueMs, nowMs + (long)EnabledDelay.TotalMilliseconds);
}

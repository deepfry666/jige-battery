using Jige.Core;

namespace Jige.Tray;

/// <summary>
/// 单台设备的剩余可用时间学习状态（持久化在 state.json）。
/// 只消费已有读数，不向设备发送任何额外字节。
/// </summary>
internal sealed record SavedRuntimeEstimate
{
    /// <summary>已学到的掉电证据（百分点，按掉电量加权并封顶衰减）。</summary>
    public double LearnedPercent { get; init; }
    /// <summary>与 <see cref="LearnedPercent"/> 对应的在线小时数。</summary>
    public double LearnedHours { get; init; }
    /// <summary>挂起中的偏离段方向（+1 更快、-1 更慢）及其证据；连续两段同向偏离才确认习惯变化。</summary>
    public int ShiftDirection { get; init; }
    public double ShiftPercent { get; init; }
    public double ShiftHours { get; init; }
    /// <summary>连续在线时观察到的最小下降步长；≥10 视为粗粒度设备。0 表示尚未观察到。</summary>
    public int MinStep { get; init; }
    public DateTimeOffset? LearnedAt { get; init; }
    public SavedRuntimeSegment? Segment { get; init; }
    public string? UsageDay { get; init; }
    public double UsageTodayHours { get; init; }
    public double UsageDailyHours { get; init; }
    public int UsageDays { get; init; }
    /// <summary>最近一次看到充电（含充满，或从电量回升推断出充过电）的时间。</summary>
    public DateTimeOffset? LastChargeAt { get; init; }
    /// <summary>充电速率证据：按 <see cref="ChargeEstimator.Bands"/> 分段的上升百分点与对应在线小时数。</summary>
    public double[]? ChargePercent { get; init; }
    public double[]? ChargeHours { get; init; }
    public DateTimeOffset? ChargeLearnedAt { get; init; }
    public SavedChargeSegment? ChargeSegment { get; init; }
}

/// <summary>当前放电段。时间口径为「设备在线时长」，休眠、离线与程序未运行期间不计时。</summary>
internal sealed record SavedRuntimeSegment
{
    /// <summary>参考电量：细粒度为最近读数，电压估算为放电段内的运行最小值。</summary>
    public int Level { get; init; }
    /// <summary>最近一次在连续计时下观察到的下降边沿；null 表示尚未锚定。</summary>
    public int? EdgePercent { get; init; }
    public double EdgeHours { get; init; }
    public double Hours { get; init; }
    public DateTimeOffset? LastOnlineAt { get; init; }
    public bool Paused { get; init; }
    public bool LongGap { get; init; }
    public bool PendingRise { get; init; }
    public DeviceConnection Connection { get; init; }
    public bool Estimated { get; init; }
    public DateTimeOffset? SettleUntil { get; init; }
    public double ChunkPercent { get; init; }
    public double ChunkHours { get; init; }
}

internal enum RuntimeKind { None, Estimating, Ready }

/// <summary>
/// 预计时间。<paramref name="Charging"/> 为 true 时 <paramref name="Hours"/> 表示距离充满的时间，
/// 否则表示剩余可用时间。
/// </summary>
internal readonly record struct RuntimeEstimate(RuntimeKind Kind, double Hours = 0, double? Days = null, bool Charging = false)
{
    public static RuntimeEstimate None => default;
    public static RuntimeEstimate Estimating => new(RuntimeKind.Estimating);
    public static RuntimeEstimate ChargeEstimating => new(RuntimeKind.Estimating, Charging: true);
}

/// <summary>
/// 剩余可用时间估算：台阶边沿 + 在线时钟 + 掉电量加权比值。
/// 细粒度与粗档设备共用一套逻辑；速率只用两次下降边沿之间的在线时长计算，消除台阶量化误差。
/// </summary>
internal static class RuntimeEstimator
{
    public const double FineChunkPercent = 5;
    public const double MaxLearnedPercent = 100;
    public const double CoarseMaxLearnedPercent = 150;
    public const int CoarseStep = 10;
    public const double MinRatePerHour = 0.05;
    public const double MaxRatePerHour = 60;
    public const double MinEvidenceHours = 0.5;
    public const double FineMinEvidencePercent = 3;
    public const double UsageSmoothing = 0.2;
    public const int MinUsageDays = 3;
    public const double ShiftRatio = 1.4;
    public static readonly TimeSpan ShortPause = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan VoltageSettle = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    public static bool IsCoarse(SavedRuntimeEstimate state) => state.MinStep >= CoarseStep;

    /// <summary>程序启动时恢复的放电段一律视为暂停：重启前后的空洞不计时。</summary>
    public static SavedRuntimeEstimate? Restore(SavedRuntimeEstimate? saved)
    {
        if (saved is null || !Finite(saved.LearnedPercent) || !Finite(saved.LearnedHours) ||
            saved.LearnedPercent < 0 || saved.LearnedHours < 0 || saved.MinStep is < 0 or > 100 ||
            !Finite(saved.UsageDailyHours) || !Finite(saved.UsageTodayHours) ||
            !Finite(saved.ShiftPercent) || !Finite(saved.ShiftHours) || saved.ShiftPercent < 0 || saved.ShiftHours < 0 ||
            saved.UsageDailyHours is < 0 or > 24 || saved.UsageTodayHours is < 0 or > 24 || saved.UsageDays < 0)
            return null;
        var segment = saved.Segment;
        if (segment is not null && (segment.Level is < 0 or > 100 || segment.EdgePercent is < 0 or > 100 ||
            !Finite(segment.Hours) || !Finite(segment.EdgeHours) || !Finite(segment.ChunkPercent) ||
            !Finite(segment.ChunkHours) || segment.Hours < 0 || segment.EdgeHours < 0 ||
            segment.EdgeHours > segment.Hours || segment.ChunkPercent < 0 || segment.ChunkHours < 0))
            segment = null;
        return ChargeEstimator.Restore(saved with { Segment = segment is null ? null : segment with { Paused = true } });
    }

    private static bool Finite(double value) => double.IsFinite(value);

    /// <summary>根据一次设备状态更新推进学习状态。返回新的状态与本次累计的在线小时数。</summary>
    public static (SavedRuntimeEstimate State, double ActiveHours) Observe(SavedRuntimeEstimate? state,
        DeviceStatus status, DateTimeOffset now, TimeSpan gapLimit, TimeZoneInfo zone)
    {
        state ??= new();
        var segment = state.Segment;

        if (status.Link != LinkState.Online)
        {
            if (segment is null)
                return (state, 0);
            return (state with
            {
                Segment = segment with { Paused = true, LongGap = segment.LongGap || status.Link == LinkState.Offline }
            }, 0);
        }

        // 在线但还没有新读数（例如刚从休眠唤醒，等待完整刷新）时不计时也不判断边沿。
        if (!status.Fresh || status.Last is not { } last)
            return (state, 0);

        if (status.Charging is ChargingState.Charging or ChargingState.Full)
            return (state with { Segment = null, LastChargeAt = now }, 0);

        int percent = last.Percentage;
        bool estimated = last.IsEstimated;
        if (segment is null || segment.Connection != status.Device.Connection || segment.Estimated != estimated)
            return (state with { Segment = Start(percent, estimated, status.Device.Connection, now) }, 0);

        double delta = 0;
        bool longGap = segment.LongGap || segment.LastOnlineAt is null;
        if (segment.LastOnlineAt is { } previousAt)
        {
            var gap = now - previousAt;
            if (gap < -TimeSpan.FromSeconds(5))
                longGap = true;
            else if (segment.Paused)
                longGap |= gap > ShortPause;
            else if (gap > gapLimit)
                longGap = true;
            else if (gap > TimeSpan.Zero)
                delta = gap.TotalHours;
        }

        double hours = segment.Hours + delta;
        var next = segment with
        {
            Hours = hours,
            LastOnlineAt = now,
            Paused = false,
            LongGap = false,
            EdgePercent = longGap ? null : segment.EdgePercent
        };
        state = AddUsage(state, delta, now, zone);

        if (estimated && next.SettleUntil is { } settle)
        {
            // 时钟回拨导致 SettleUntil 落在未来窗口外时，重新夹紧到当前起算的 VoltageSettle，避免永久冻结学习。
            if (settle - now > VoltageSettle)
            {
                settle = now + VoltageSettle;
                next = next with { SettleUntil = settle };
            }
            if (now < settle)
                return (state with { Segment = next with { Level = percent, EdgePercent = null, PendingRise = false } }, delta);
        }

        int level = segment.Level;
        if (percent < level)
        {
            int drop = level - percent;
            if (estimated && drop < 2)
                return (state with { Segment = next with { PendingRise = false } }, delta);
            if (longGap)
            {
                // 跨离线、关机或程序重启掉的电不知道花了多少在线时长，只重新建立基线，不计入速率。
                return (state with { Segment = next with { Level = percent, EdgePercent = null, PendingRise = false } }, delta);
            }

            int minStep = estimated ? state.MinStep : state.MinStep == 0 ? drop : Math.Min(state.MinStep, drop);
            state = state with { MinStep = minStep };
            double chunkPercent = next.ChunkPercent, chunkHours = next.ChunkHours;
            if (next.EdgePercent is int edge && edge > percent)
            {
                chunkPercent += edge - percent;
                chunkHours += Math.Max(0, hours - next.EdgeHours);
            }
            next = next with
            {
                Level = percent, EdgePercent = percent, EdgeHours = hours, PendingRise = false,
                ChunkPercent = chunkPercent, ChunkHours = chunkHours
            };
            double threshold = IsCoarse(state) ? double.Epsilon : FineChunkPercent;
            if (chunkPercent >= threshold && chunkPercent > 0)
            {
                state = Fold(state, chunkPercent, chunkHours, now);
                next = next with { ChunkPercent = 0, ChunkHours = 0 };
            }
            return (state with { Segment = next }, delta);
        }

        if (percent > level)
        {
            int rise = percent - level;
            bool charged = estimated ? rise >= 5 : IsCoarse(state) || rise >= 3 || segment.PendingRise;
            if (charged)
            {
                bool markChargeTime = status.Charging != ChargingState.NotCharging || rise >= 3 || IsCoarse(state);
                return (state with
                {
                    Segment = Start(percent, estimated, status.Device.Connection, now),
                    LastChargeAt = markChargeTime ? now : state.LastChargeAt
                }, delta);
            }
            return (state with { Segment = next with { PendingRise = !estimated } }, delta);
        }

        return (state with { Segment = next with { PendingRise = false } }, delta);
    }

    private static SavedRuntimeSegment Start(int percent, bool estimated, DeviceConnection connection, DateTimeOffset now) => new()
    {
        Level = percent,
        Connection = connection,
        Estimated = estimated,
        LastOnlineAt = now,
        SettleUntil = estimated ? now + VoltageSettle : null
    };

    /// <summary>
    /// 把一段掉电证据并入长期速率：封顶衰减、异常剔除与习惯突变检测。
    /// 偏离现有速率超过 1.4 倍的段先挂起；下一段同向偏离则确认习惯变化（旧证据只留 25%），
    /// 回到正常范围则把挂起段按半权重并入，单次异常不会把数字拉飞。
    /// </summary>
    internal static SavedRuntimeEstimate Fold(SavedRuntimeEstimate state, double percent, double hours, DateTimeOffset now)
    {
        if (hours <= 0 || percent <= 0)
            return state;
        double rate = percent / hours;
        if (rate is < MinRatePerHour or > MaxRatePerHour)
            return state;

        double learnedPercent = state.LearnedPercent, learnedHours = state.LearnedHours;
        if (state.LearnedAt is { } learnedAt && now - learnedAt > StaleAfter)
            (learnedPercent, learnedHours) = (0, 0);

        double addPercent = percent, addHours = hours;
        if (learnedPercent >= 20 && learnedHours > 0)
        {
            double ratio = rate / (learnedPercent / learnedHours);
            int sign = ratio > ShiftRatio ? 1 : ratio < 1 / ShiftRatio ? -1 : 0;
            bool pending = state.ShiftPercent > 0 && state.ShiftHours > 0;
            if (sign != 0 && !(pending && sign == state.ShiftDirection))
                return state with { ShiftDirection = sign, ShiftPercent = percent, ShiftHours = hours };
            if (sign != 0)
            {
                // 连续两段同向偏离（如 1K→8K 回报率、开启 RGB 或 ANC）：旧证据只留 25%，快速收敛到新功耗。
                learnedPercent *= .25;
                learnedHours *= .25;
                addPercent += state.ShiftPercent;
                addHours += state.ShiftHours;
            }
            else if (pending)
            {
                addPercent += state.ShiftPercent / 2;
                addHours += state.ShiftHours / 2;
            }
        }

        double cap = IsCoarse(state) ? CoarseMaxLearnedPercent : MaxLearnedPercent;
        if (learnedPercent > cap)
        {
            double scale = cap / learnedPercent;
            learnedPercent *= scale;
            learnedHours *= scale;
        }

        return state with
        {
            LearnedPercent = learnedPercent + addPercent,
            LearnedHours = learnedHours + addHours,
            ShiftDirection = 0,
            ShiftPercent = 0,
            ShiftHours = 0,
            LearnedAt = now
        };
    }

    private static SavedRuntimeEstimate AddUsage(SavedRuntimeEstimate state, double hours, DateTimeOffset now, TimeZoneInfo zone)
    {
        double clampedHours = Math.Clamp(hours, 0, 24);
        string day = TimeZoneInfo.ConvertTime(now, zone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (state.UsageDay == day)
            return clampedHours > 0 ? state with { UsageTodayHours = Math.Min(24, state.UsageTodayHours + clampedHours) } : state;
        if (state.UsageDay is null || state.UsageTodayHours <= 0 || string.CompareOrdinal(day, state.UsageDay) < 0)
            return state with { UsageDay = day, UsageTodayHours = clampedHours };
        double daily = state.UsageDays == 0
            ? Math.Min(24, state.UsageTodayHours)
            : Math.Min(24, state.UsageDailyHours * (1 - UsageSmoothing) + Math.Min(24, state.UsageTodayHours) * UsageSmoothing);
        return state with
        {
            UsageDay = day,
            UsageTodayHours = clampedHours,
            UsageDailyHours = daily,
            UsageDays = state.UsageDays + 1
        };
    }

    private static (double Percent, double Hours) Evidence(SavedRuntimeEstimate state, DateTimeOffset now)
    {
        double percent = state.LearnedPercent, hours = state.LearnedHours;
        if (state.LearnedAt is { } at && now - at > StaleAfter)
            (percent, hours) = (0, 0);
        if (state.Segment is { } segment)
        {
            percent += segment.ChunkPercent;
            hours += segment.ChunkHours;
        }
        return (percent, hours);
    }

    private static double? Rate(SavedRuntimeEstimate? state, DateTimeOffset now)
    {
        if (state is null)
            return null;
        var (percent, hours) = Evidence(state, now);
        double minPercent = IsCoarse(state) ? 1 : FineMinEvidencePercent;
        if (percent < minPercent || hours < MinEvidenceHours)
            return null;
        double rate = percent / hours;
        return rate is >= MinRatePerHour and <= MaxRatePerHour ? rate : null;
    }

    /// <summary>计算当前剩余在线可用时长。数据不足时返回「估算中」，充电中或已断开返回 None。</summary>
    public static RuntimeEstimate Estimate(SavedRuntimeEstimate? state, DeviceStatus status, DateTimeOffset now)
    {
        if (!status.IsActive || status.Last is not { } last || status.IsCharging)
            return RuntimeEstimate.None;
        var rate = Rate(state, now);
        if (status.IsFull)
            return rate is { } fullRate ? new(RuntimeKind.Ready, 100 / fullRate, Days(state!, 100 / fullRate)) : RuntimeEstimate.None;
        if (rate is not { } r)
            return RuntimeEstimate.Estimating;

        int percent = last.Percentage;
        if (percent <= 0)
            return new(RuntimeKind.Ready, 0);
        int step = Math.Max(1, state!.MinStep);
        bool coarse = IsCoarse(state);
        double effective = percent;
        if (state.Segment is { EdgePercent: int edge } segment && edge == percent)
        {
            double stay = Math.Max(0, segment.Hours - segment.EdgeHours);
            // 停滞修正：在当前台阶停留已超过按速率推算的时间时，把停滞证据与历史证据加权混合，避免单次短暂停留覆盖全部历史速率。
            if (stay > 0 && r * stay > step)
            {
                var (evPercent, evHours) = Evidence(state, now);
                r = Math.Clamp((evPercent + step) / (evHours + stay), MinRatePerHour, MaxRatePerHour);
            }
            effective = Math.Max(percent - step, percent - r * stay);
        }
        else if (coarse)
            effective = percent - step / 2d;
        effective = Math.Max(0, effective);

        double hours = effective / r;
        if (last.IsEstimated && percent <= 15)
            hours *= .85;
        return new(RuntimeKind.Ready, hours, Days(state, hours));
    }

    /// <summary>本机统计的日均在线时长；不足 3 个活跃日或太少时返回 null。</summary>
    public static double? DailyHours(SavedRuntimeEstimate? state) =>
        state is { UsageDays: >= MinUsageDays, UsageDailyHours: >= 1 / 6d } ? state.UsageDailyHours : null;

    private static double? Days(SavedRuntimeEstimate state, double hours)
    {
        if (state.UsageDays < MinUsageDays || state.UsageDailyHours < 1 / 6d)
            return null;
        double days = hours / state.UsageDailyHours;
        return days >= 2 ? days : null;
    }

    /// <summary>显示取整后的小时数，用于防抖比较。</summary>
    public static double Rounded(double hours)
    {
        // 在线时长由多次浮点累加得到，加一个极小量避免 34.999… 这类值落到下一档。
        hours += 1e-7;
        if (hours < 1 / 6d)
            return 0;
        if (hours < 1)
            return Math.Round(hours * 6, MidpointRounding.AwayFromZero) / 6;
        if (hours < 10)
            return Math.Round(hours, MidpointRounding.AwayFromZero);
        if (hours < 50)
            return Math.Round(hours / 2, MidpointRounding.AwayFromZero) * 2;
        if (hours < 100)
            return Math.Round(hours / 5, MidpointRounding.AwayFromZero) * 5;
        return Math.Min(999, Math.Round(hours / 10, MidpointRounding.AwayFromZero) * 10);
    }

    public static string Format(double hours)
    {
        double rounded = Rounded(hours);
        if (rounded <= 0)
            return "不到 10 分钟";
        if (rounded < 1)
            return $"约 {Math.Round(rounded * 60)} 分钟";
        if (rounded >= 999)
            return "超过 999 小时";
        return $"约 {rounded:0} 小时";
    }

    /// <summary>悬停卡片里的「上次充电」文字。</summary>
    public static string ChargedAgo(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromHours(1))
            return "刚充过电";
        if (elapsed < TimeSpan.FromDays(1))
            return $"上次充电 {(int)elapsed.TotalHours} 小时前";
        return $"上次充电 {(int)elapsed.TotalDays} 天前";
    }

    public static string FormatDays(double days) =>
        days >= 30 ? "超过 30 天" : $"约 {Math.Max(2, Math.Round(days, MidpointRounding.AwayFromZero)):0} 天";
}

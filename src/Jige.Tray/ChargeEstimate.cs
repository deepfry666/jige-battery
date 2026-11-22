using Jige.Core;

namespace Jige.Tray;

/// <summary>当前充电段。只在设备在线、明确上报「充电中」且读数不是电压估算时计时。</summary>
internal sealed record SavedChargeSegment
{
    public int Level { get; init; }
    /// <summary>最近一次在连续计时下观察到的上升边沿；null 表示尚未锚定。</summary>
    public int? EdgePercent { get; init; }
    public double EdgeHours { get; init; }
    public double Hours { get; init; }
    public DateTimeOffset? LastOnlineAt { get; init; }
    public bool Paused { get; init; }
}

/// <summary>
/// 充满预计时间：与续航估算同一思路（上升边沿到上升边沿 + 在线时钟），
/// 但锂电池后段为恒压慢充，所以按电量分段分别学习速率。
/// </summary>
internal static class ChargeEstimator
{
    /// <summary>分段边界：0–50、50–80、80–90、90–100。</summary>
    public static readonly int[] Bands = [0, 50, 80, 90, 100];
    /// <summary>某一段还没学到时，按相邻已学段的速率乘以恒流/恒压经验比例推算。</summary>
    private static readonly double[] Shape = [1, .9, .6, .35];
    public const double MinRatePerHour = 1;
    public const double MaxRatePerHour = 600;
    public const double MinBandPercent = 3;
    public const double MinBandHours = 2 / 60d;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(60);

    public static int BandOf(double percent)
    {
        for (int i = Bands.Length - 2; i > 0; i--)
            if (percent >= Bands[i])
                return i;
        return 0;
    }

    private static double Width(int band) => Bands[band + 1] - Bands[band];

    /// <summary>每段最多保留约两次充电的证据，旧数据按比例衰减，适应换线、换充电头。</summary>
    private static double Cap(int band) => Math.Max(20, 2 * Width(band));

    public static SavedRuntimeEstimate Restore(SavedRuntimeEstimate state)
    {
        bool valid = state.ChargePercent is null && state.ChargeHours is null ||
            state.ChargePercent is { Length: 4 } p && state.ChargeHours is { Length: 4 } h &&
            p.Concat(h).All(value => double.IsFinite(value) && value >= 0);
        if (!valid)
            state = state with { ChargePercent = null, ChargeHours = null, ChargeLearnedAt = null };
        var segment = state.ChargeSegment;
        if (segment is not null && (segment.Level is < 0 or > 100 || segment.EdgePercent is < 0 or > 100 ||
            !double.IsFinite(segment.Hours) || !double.IsFinite(segment.EdgeHours) || segment.Hours < 0))
            segment = null;
        return state with { ChargeSegment = segment is null ? null : segment with { Paused = true, EdgePercent = null } };
    }

    /// <summary>根据一次设备状态更新推进充电学习。返回新状态与本次累计的充电在线小时数。</summary>
    public static (SavedRuntimeEstimate State, double ActiveHours) Observe(SavedRuntimeEstimate? state,
        DeviceStatus status, DateTimeOffset now, TimeSpan gapLimit)
    {
        state ??= new();
        var segment = state.ChargeSegment;
        if (status.Link != LinkState.Online)
            return segment is null ? (state, 0) : (state with { ChargeSegment = segment with { Paused = true } }, 0);
        if (!status.Fresh || status.Last is not { } last)
            return (state, 0);
        if (last.IsEstimated || status.Charging is not (ChargingState.Charging or ChargingState.Full))
            return segment is null ? (state, 0) : (state with { ChargeSegment = null }, 0);

        int percent = last.Percentage;
        if (segment is null)
        {
            if (status.Charging == ChargingState.Full)
                return (state, 0);
            return (state with { ChargeSegment = new() { Level = percent, LastOnlineAt = now } }, 0);
        }

        double delta = 0;
        bool broken = segment.Paused || segment.LastOnlineAt is null;
        if (!broken && segment.LastOnlineAt is { } previousAt)
        {
            var gap = now - previousAt;
            if (gap > gapLimit)
                broken = true;
            else if (gap > TimeSpan.Zero)
                delta = gap.TotalHours;
        }
        double hours = segment.Hours + delta;
        var next = segment with { Hours = hours, LastOnlineAt = now, Paused = false };

        if (broken || percent < segment.Level)
        {
            // 休眠、断开或间隔过长期间仍可能在充电，这段电量花了多久不可知：只重新锚定，不计入速率。
            next = next with { Level = percent, EdgePercent = null };
        }
        else if (percent > segment.Level)
        {
            if (next.EdgePercent is int edge && edge < percent)
                state = Fold(state, edge, percent, hours - next.EdgeHours, now);
            next = next with { Level = percent, EdgePercent = percent, EdgeHours = hours };
        }

        if (status.Charging == ChargingState.Full)
            return (state with { ChargeSegment = null }, delta);
        return (state with { ChargeSegment = next }, delta);
    }

    /// <summary>把一次上升（from→to，耗时 hours）按电量重叠比例拆进各段。</summary>
    internal static SavedRuntimeEstimate Fold(SavedRuntimeEstimate state, int from, int to, double hours, DateTimeOffset now)
    {
        double span = to - from;
        if (span <= 0 || hours <= 0)
            return state;
        double rate = span / hours;
        if (rate is < MinRatePerHour or > MaxRatePerHour)
            return state;

        bool stale = state.ChargeLearnedAt is { } at && now - at > StaleAfter;
        double[] percent = !stale && state.ChargePercent is { Length: 4 } p ? [.. p] : new double[4];
        double[] spent = !stale && state.ChargeHours is { Length: 4 } h ? [.. h] : new double[4];
        for (int band = 0; band < 4; band++)
        {
            double overlap = Math.Min(to, Bands[band + 1]) - Math.Max(from, Bands[band]);
            if (overlap <= 0)
                continue;
            double cap = Cap(band);
            if (percent[band] + overlap > cap && percent[band] > 0)
            {
                double scale = Math.Max(0, cap - overlap) / percent[band];
                percent[band] *= scale;
                spent[band] *= scale;
            }
            percent[band] += overlap;
            spent[band] += hours * overlap / span;
        }
        return state with { ChargePercent = percent, ChargeHours = spent, ChargeLearnedAt = now };
    }

    private static double?[] Rates(SavedRuntimeEstimate state, DateTimeOffset now)
    {
        var rates = new double?[4];
        if (state.ChargeLearnedAt is { } at && now - at > StaleAfter ||
            state.ChargePercent is not { Length: 4 } percent || state.ChargeHours is not { Length: 4 } hours)
            return rates;
        for (int band = 0; band < 4; band++)
        {
            if (percent[band] >= Math.Min(MinBandPercent, Width(band)) && hours[band] >= MinBandHours)
                rates[band] = percent[band] / hours[band];
        }
        return rates;
    }

    private static double? RateFor(double?[] rates, int band)
    {
        if (rates[band] is { } own)
            return own;
        for (int distance = 1; distance < 4; distance++)
        {
            foreach (int other in new[] { band - distance, band + distance })
            {
                if (other is >= 0 and < 4 && rates[other] is { } near)
                    return near * Shape[band] / Shape[other];
            }
        }
        return null;
    }

    /// <summary>当前距离充满还需的在线小时数。没有在充电时返回 None，数据不足时返回「估算中」。</summary>
    public static RuntimeEstimate Estimate(SavedRuntimeEstimate? state, DeviceStatus status, DateTimeOffset now)
    {
        if (status is not { Fresh: true, Link: LinkState.Online, Charging: ChargingState.Charging, Last: { } last } ||
            last.IsEstimated)
            return RuntimeEstimate.None;
        if (last.Percentage >= 100)
            return new(RuntimeKind.Ready, 0, Charging: true);
        var rates = state is null ? new double?[4] : Rates(state, now);
        if (rates.All(rate => rate is null))
            return RuntimeEstimate.ChargeEstimating;

        int percent = last.Percentage;
        double effective = percent;
        if (state!.ChargeSegment is { EdgePercent: int edge } segment && edge == percent &&
            RateFor(rates, BandOf(percent)) is { } current)
        {
            int step = Math.Max(1, state.MinStep);
            double stay = Math.Max(0, segment.Hours - segment.EdgeHours);
            effective = Math.Min(percent + step * .9, percent + current * stay);
        }

        double remaining = 0;
        for (int band = 0; band < 4; band++)
        {
            double overlap = Bands[band + 1] - Math.Max(effective, Bands[band]);
            if (overlap <= 0)
                continue;
            remaining += overlap / RateFor(rates, band)!.Value;
        }
        return new(RuntimeKind.Ready, remaining, Charging: true);
    }

    /// <summary>显示取整：1 小时内按 5 分钟，3 小时内按 10 分钟，再往上按小时。</summary>
    public static double Rounded(double hours)
    {
        double minutes = hours * 60 + 1e-7;
        if (minutes < 2.5)
            return 0;
        if (minutes < 57.5)
            return Math.Max(5, Math.Round(minutes / 5, MidpointRounding.AwayFromZero) * 5) / 60;
        if (minutes < 175)
            return Math.Round(minutes / 10, MidpointRounding.AwayFromZero) * 10 / 60;
        return Math.Min(99, Math.Round(hours, MidpointRounding.AwayFromZero));
    }

    public static string Format(double hours)
    {
        double minutes = Math.Round(Rounded(hours) * 60);
        if (minutes <= 0)
            return "即将充满";
        if (minutes < 60)
            return $"约 {minutes:0} 分钟充满";
        int whole = (int)(minutes / 60), rest = (int)(minutes % 60);
        return rest == 0 ? $"约 {whole} 小时充满" : $"约 {whole} 小时 {rest} 分钟充满";
    }
}

using Jige.Core;
using Jige.Core.Diagnostics;

namespace Jige.Tray;

internal sealed record DeviceStatus(DeviceIdentity Device, LinkState Link, ReadIssue Issue,
    ChargingState Charging, SavedBattery? Last, bool Fresh, bool PendingRefresh = false)
{
    public int? Percentage => Last?.Percentage;
    public bool IsCharging => (Fresh || Device.UsesPassiveBatteryReports && IsOnline) && Charging == ChargingState.Charging;
    public bool IsFull => Fresh && (Charging == ChargingState.Full || Percentage == 100);
    public bool IsOnline => Link == LinkState.Online;
    /// <summary>在线或短时休眠（未超时转离线）均视为活跃设备，避免鼠标静止数秒即跳入「离线设备」闪烁（R1）。</summary>
    public bool IsActive => Link is LinkState.Online or LinkState.Asleep;
}

internal sealed record MainPanelPlan(
    DeviceStatus? Hero,
    bool HeroDisconnected,
    DeviceStatus[] OnlineRows,
    DeviceStatus[] OfflineRows)
{
    public int RowCount(bool offlineExpanded)
    {
        if (Hero is null)
            return 0;
        int extra = OnlineRows.Length;
        if (OfflineRows.Length > 0)
            extra += 1 + (offlineExpanded ? OfflineRows.Length : 0);
        return extra;
    }
}

/// <summary>三类提醒共用同一套「知道了 / 1 小时后提醒」与持久化机制，以键前缀区分。</summary>
internal enum ReminderKind { LowBattery, Predicted, FullCharge }

internal static class ReminderKeys
{
    private const string PredictedPrefix = "predict|", FullPrefix = "full|";

    public static string For(ReminderKind kind, string deviceId) => kind switch
    {
        ReminderKind.Predicted => PredictedPrefix + deviceId,
        ReminderKind.FullCharge => FullPrefix + deviceId,
        _ => deviceId
    };

    public static (ReminderKind Kind, string DeviceId) Parse(string key) =>
        key.StartsWith(PredictedPrefix, StringComparison.Ordinal) ? (ReminderKind.Predicted, key[PredictedPrefix.Length..])
        : key.StartsWith(FullPrefix, StringComparison.Ordinal) ? (ReminderKind.FullCharge, key[FullPrefix.Length..])
        : (ReminderKind.LowBattery, key);
}

internal sealed class TrayModel
{
    public const int MaxHistoryDevices = 32;
    public const int MaxHistorySamples = 5;

    /// <summary>
    /// R2：连续休眠超过 10 分钟仍无任何回复才转为离线显示。
    /// Compx 等无线鼠标静止数秒后接收器即停止应答查询（见 docs/protocols.md），短时无回复属于正常省电休眠；
    /// 10 分钟足够覆盖日常看文档、看视频等短暂停顿而不误判离线，同时在设备真正关机或带离后能及时隐藏托盘图标。
    /// </summary>
    public static readonly TimeSpan SleepToOfflineTimeout = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, DeviceStatus> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedReminder> reminders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removedOffline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedDeviceHistory> history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> blockedReceivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> asleepSince = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> mergedRazerIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedRuntimeEstimate> runtime = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> runtimeShown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> chargeShown = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>本轮放电已弹过「进游戏电量低」横幅的设备；充电或开新放电段后清除。</summary>
    private readonly HashSet<string> gameWarned = new(StringComparer.OrdinalIgnoreCase);
    private double unsavedRuntimeHours;
    private bool hasSavedOnce;

    /// <summary>剩余时间估算按本地自然日统计日均使用时长；测试可注入固定时区。</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;

    public TrayModel(TraySettings settings, TrayState saved)
    {
        Settings = settings;
        removedOffline.UnionWith((saved.RemovedOfflineDevices ?? []).Where(id => !string.IsNullOrWhiteSpace(id)));
        foreach (var (id, reading) in saved.Readings ?? [])
        {
            if (reading?.Device is not { } device || string.IsNullOrWhiteSpace(device.Name) ||
                reading.Percentage is < 0 or > 100 || !string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase))
                continue;
            devices[id] = new(device, LinkState.Unknown, ReadIssue.None, ChargingState.Unknown, reading, false);
        }

        foreach (var (key, reminder) in saved.Reminders ?? [])
        {
            if (reminder is null || string.IsNullOrEmpty(key))
                continue;
            var (kind, id) = ReminderKeys.Parse(key);
            if (devices.TryGetValue(id, out var device) &&
                (kind != ReminderKind.LowBattery || device.Percentage <= ThresholdFor(id)))
                reminders[key] = reminder;
        }

        foreach (var (id, entry) in saved.History ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(id))
                continue;
            var samples = (entry.RecentReadings ?? [])
                .Where(sample => sample is not null && sample.Percentage is >= 0 and <= 100)
                .TakeLast(MaxHistorySamples).ToArray();
            history[id] = new(entry.FirstSeen, entry.LastOnline, samples);
        }
        foreach (var (id, entry) in saved.Runtime ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id) && RuntimeEstimator.Restore(entry) is { } restored)
                runtime[id] = restored;
        }
        TrimHistory();

        foreach (var (sig, reason) in saved.BlockedReceivers ?? [])
        {
            if (!string.IsNullOrWhiteSpace(sig) && !string.IsNullOrWhiteSpace(reason))
                blockedReceivers[sig] = reason;
        }
    }

    public TraySettings Settings { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool ScanFailed { get; private set; }
    public int UnknownReceivers { get; private set; }
    public IReadOnlyDictionary<string, string> InitialBlockedReceivers => blockedReceivers;
    public IReadOnlyDictionary<string, DeviceIdentity> SavedLogitechSlots() =>
        LatestLogitechSlots().ToDictionary(pair => pair.Key, pair => pair.Value.Device, StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, DeviceStatus> LatestLogitechSlots() => devices.Values
        .Where(item => LogitechReceiverSlot(item.Device.Id) is not null && item.Last is not null)
        .OrderByDescending(item => item.Last!.ReadAt)
        .ThenByDescending(item => history.GetValueOrDefault(item.Device.Id)?.LastOnline)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .DistinctBy(item => LogitechReceiverSlot(item.Device.Id), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(item => LogitechReceiverSlot(item.Device.Id)!, StringComparer.OrdinalIgnoreCase);

    private static string? LogitechReceiverSlot(string id)
    {
        if (!id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase))
            return null;
        int end = id.LastIndexOf('/');
        return end > 0 && id.IndexOf('/') < end ? id[..end] : null;
    }

    private static readonly TimeSpan FutureClockSkew = TimeSpan.FromMinutes(5);
    private bool inGameMode;

    public bool ExpireReadings(DateTimeOffset now, bool game = false)
    {
        inGameMode = game;
        bool changed = false;
        foreach (var (id, device) in devices)
        {
            if (device is { Fresh: true, Last: { } last } && IsExpired(last, now, game))
            {
                devices[id] = device.Device.UsesPassiveBatteryReports
                    ? device with { Fresh = false, Link = LinkState.Asleep, Charging = ChargingState.Unknown }
                    : device with { Fresh = false };
                ObserveRuntime(id, devices[id], now);
                changed = true;
            }
        }
        return changed;
    }

    public bool HasAsleepDevices => devices.Values.Any(item => item.Link == LinkState.Asleep && IsShown(item.Device));

    public bool HasAsleepLogitechOrBluetoothDevices => devices.Values.Any(item =>
        item.Link == LinkState.Asleep && IsShown(item.Device) &&
        (item.Device.Connection == DeviceConnection.Bluetooth ||
            item.Device.Id.StartsWith("logitech-hidpp", StringComparison.OrdinalIgnoreCase)));

    public HashSet<string> AsleepDeviceIds() => devices.Values
        .Where(item => item.Link == LinkState.Asleep && IsShown(item.Device))
        .SelectMany(item => item.Device.BluetoothContainerKey is { } container
            ? new[] { item.Device.Id, container } : [item.Device.Id])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>R2：返回最早一台休眠设备达到 10 分钟离线阈值的时间点，供主循环安排定时器唤醒。</summary>
    public DateTimeOffset? NextSleepTimeout(DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        foreach (var (id, device) in devices)
        {
            if (device.Link != LinkState.Asleep || device.Device.UsesPassiveBatteryReports)
                continue;
            if (!asleepSince.TryGetValue(id, out var since) || now < since)
                since = now;
            var due = since + SleepToOfflineTimeout;
            if (earliest is null || due < earliest.Value)
                earliest = due;
        }
        return earliest;
    }

    /// <summary>R2：检查连续休眠是否已超过 10 分钟，超时且仍无任何回复的设备转为离线显示。</summary>
    public bool AdvanceSleepTimeouts(
        DateTimeOffset now,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null)
    {
        bool changed = false;
        foreach (var id in devices.Keys.ToArray())
        {
            var device = devices[id];
            if (device.Link != LinkState.Asleep || device.Device.UsesPassiveBatteryReports)
            {
                asleepSince.Remove(id);
                continue;
            }
            if (!asleepSince.TryGetValue(id, out var since) || now < since)
            {
                asleepSince[id] = now;
                continue;
            }
            if (now - since < SleepToOfflineTimeout)
                continue;

            asleepSince.Remove(id);
            devices[id] = device with
            {
                Link = LinkState.Offline,
                Fresh = false,
                PendingRefresh = false,
                Charging = ChargingState.Unknown
            };
            ObserveRuntime(id, devices[id], now);
            changed = true;
            onLinkChanged?.Invoke(device.Device, LinkState.Asleep, LinkState.Offline);
        }
        return changed;
    }

    public bool CanRemove(string id) => devices.TryGetValue(id, out var device) && device.Link == LinkState.Offline;

    public void RemoveOffline(string id)
    {
        if (CanRemove(id))
        {
            devices.Remove(id);
            RemoveReminders(id);
            history.Remove(id);
            runtime.Remove(id);
            runtimeShown.Remove(id);
            chargeShown.Remove(id);
            asleepSince.Remove(id);
            removedOffline.Add(id);
        }
    }

    /// <summary>主面板自动排序规则（B7 / R1）：活跃（在线或短时休眠）按电量低→高，顶部大卡片取最低活跃设备，全离线时取最近一台离线设备。</summary>
    public MainPanelPlan BuildMainPanel()
    {
        var visible = devices.Values.Where(item => IsShown(item.Device) && !IsHidden(item.Device.Id)).ToArray();
        var online = visible.Where(item => item.IsActive)
            .OrderBy(item => item.Percentage ?? int.MaxValue)
            .ThenBy(item => item.Device.Kind)
            .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var offline = visible.Where(item => !item.IsActive)
            .OrderByDescending(item => item.Last?.ReadAt ?? history.GetValueOrDefault(item.Device.Id)?.LastOnline ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Device.Kind)
            .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (online.Length > 0)
            return new(online[0], HeroDisconnected: false, online[1..], offline);
        if (offline.Length > 0)
            return new(offline[0], HeroDisconnected: true, [], offline[1..]);
        return new(null, HeroDisconnected: false, [], []);
    }

    public DeviceStatus[] OrderedDevices() => devices.Values.Where(item => IsShown(item.Device))
        .OrderByDescending(item => item.IsActive)
        .ThenBy(item => item.IsActive ? (item.Percentage ?? int.MaxValue) : 0)
        .ThenByDescending(item => item.IsActive ? DateTimeOffset.MinValue : (item.Last?.ReadAt ?? DateTimeOffset.MinValue))
        .ThenBy(item => item.Device.Kind)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public DeviceStatus[] OrderedForManagement() => devices.Values.Where(item => IsShown(item.Device))
        .OrderByDescending(item => item.IsActive)
        .ThenBy(item => item.Device.Kind)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private bool IsShown(DeviceIdentity device) => Settings.ShowBluetoothDevices || device.Connection != DeviceConnection.Bluetooth;

    public bool IsHidden(string id) => Settings.HiddenDeviceIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public DeviceStatus[] PanelDevices()
    {
        var plan = BuildMainPanel();
        if (plan.Hero is null)
            return [];
        return [plan.Hero, .. plan.OnlineRows, .. plan.OfflineRows];
    }

    /// <summary>当前最需要关注的设备（最低电量在线设备，无在线时为最近一台离线设备）。</summary>
    public DeviceStatus? PrimaryDevice => BuildMainPanel().Hero;

    public DeviceStatus? LowestTrayDevice => OrderedDevices().FirstOrDefault(item => item.IsActive);

    /// <summary>根据托盘模式返回在线和休眠中应显示图标的设备。</summary>
    public DeviceStatus[] TrayDevices()
    {
        if (Settings.TrayMode == TrayDisplayMode.LowestBattery)
            return LowestTrayDevice is { } top ? [top] : [];
        return OrderedForManagement().Where(item => item.IsActive && Settings.IsInTray(item.Device.Id)).ToArray();
    }

    public TraySettings ToggleVisibility(string id)
    {
        if (!devices.ContainsKey(id))
            return Settings;
        string[] hidden = IsHidden(id)
            ? Settings.HiddenDeviceIds.Where(item => !StringComparer.OrdinalIgnoreCase.Equals(item, id)).ToArray()
            : [.. Settings.HiddenDeviceIds, id];
        return Settings with { HiddenDeviceIds = hidden };
    }

    public TraySettings ToggleTrayDevice(string id) =>
        devices.ContainsKey(id) ? Settings.ToggleTrayDevice(id) : Settings;

    public string Name(DeviceIdentity device)
    {
        if (Settings.DeviceAliases.TryGetValue(device.Id, out var alias))
            return alias;
        if (device.Id.StartsWith("ulx:", StringComparison.OrdinalIgnoreCase))
            return "Finalmouse ULX";
        if (device.Id.StartsWith("inzone-h9:", StringComparison.OrdinalIgnoreCase))
            return "INZONE H9";
        return device.Name;
    }

    public void ApplySettings(TraySettings settings, DateTimeOffset now)
    {
        Settings = settings;
        foreach (var device in devices.Values)
        {
            if (device.Fresh)
                UpdateReminder(device, now);
            else if (device.Percentage > ThresholdFor(device.Device.Id))
                reminders.Remove(device.Device.Id);
        }
        foreach (var key in reminders.Keys.ToArray())
        {
            var kind = ReminderKeys.Parse(key).Kind;
            if (kind == ReminderKind.FullCharge && !settings.FullChargeReminder ||
                kind == ReminderKind.Predicted && !settings.ShowRuntimeEstimate)
                reminders.Remove(key);
        }
    }

    public int ThresholdFor(string id) => Settings.ThresholdFor(id);

    private void RemoveReminders(string id)
    {
        reminders.Remove(id);
        reminders.Remove(ReminderKeys.For(ReminderKind.Predicted, id));
        reminders.Remove(ReminderKeys.For(ReminderKind.FullCharge, id));
        gameWarned.Remove(id);
    }

    public bool ApplyPoll(
        IReadOnlyList<BatteryReading> readings,
        bool scanFailed,
        DateTimeOffset now,
        int unknownReceivers = 0,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null,
        IReadOnlySet<string>? liveTargets = null,
        bool completePoll = true,
        IReadOnlySet<string>? incompleteReceivers = null) =>
        ApplyReadings(readings, scanFailed, now, unknownReceivers, onLinkChanged, liveTargets,
            mergeChannels: true, sweepMissing: completePoll, incompleteReceivers: incompleteReceivers);

    /// <summary>定向刷新刚唤醒的单台或少数设备，不覆盖全量扫描状态，也不清扫未参与本次查询的设备。</summary>
    public bool ApplyTargetedPoll(
        IReadOnlyList<BatteryReading> readings,
        DateTimeOffset now,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null) =>
        ApplyReadings(readings, ScanFailed, now, UnknownReceivers, onLinkChanged, liveTargets: null,
            mergeChannels: false, sweepMissing: false);

    private bool ApplyReadings(IReadOnlyList<BatteryReading> readings, bool scanFailed, DateTimeOffset now,
        int unknownReceivers, Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged,
        IReadOnlySet<string>? liveTargets, bool mergeChannels, bool sweepMissing,
        IReadOnlySet<string>? incompleteReceivers = null)
    {
        ScanFailed = scanFailed;
        if (!scanFailed || unknownReceivers > 0)
            UnknownReceivers = unknownReceivers;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyFresh = false, meaningfulChange = false, removedChanged = false;
        if (mergeChannels)
        {
            readings = MergeLogitechCableReadings(readings, ref meaningfulChange);
            readings = MergeRazerCableReadings(readings, ref meaningfulChange);
        }
        var maxAllowedReadAt = now + TimeSpan.FromMinutes(5);
        foreach (var input in readings)
        {
            var reading = input.ReadAt > maxAllowedReadAt ? input with { ReadAt = now } : input;
            var device = reading.Device;
            if (!IsShown(device) || device.Id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase) &&
                !device.Id.Contains('/') && reading.Percentage is null)
                continue;
            if (!devices.ContainsKey(device.Id) && device.Id.EndsWith("/unknown", StringComparison.Ordinal) &&
                device.Id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase))
            {
                string prefix = device.Id[..^"unknown".Length];
                var alternatives = devices.Values.Where(item => item.Device.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToArray();
                if (alternatives is [var existing])
                {
                    device = existing.Device;
                    reading = reading with { Device = device };
                }
            }
            if (device.Connection == DeviceConnection.Bluetooth)
            {
                if (FindKnownDevice(device) is { } existing)
                {
                    var known = existing.Device;
                    bool useKnownHidppName = known.NameFromHidpp && !device.NameFromHidpp;
                    bool useKnownKind = device.Kind == DeviceKind.Other ||
                        useKnownHidppName && known.Kind != DeviceKind.Other;
                    device = device with
                    {
                        Id = known.Id,
                        Name = useKnownHidppName ? known.Name : device.Name,
                        Kind = useKnownKind ? known.Kind : device.Kind,
                        NameFromHidpp = device.NameFromHidpp || known.NameFromHidpp,
                        BluetoothContainerKey = device.BluetoothContainerKey ?? known.BluetoothContainerKey,
                        SourceLabel = device.SourceLabel ?? known.SourceLabel
                    };
                    reading = reading with { Device = device };
                }
                else if (reading.Percentage is null)
                    continue;
            }
            var previous = devices.GetValueOrDefault(device.Id);
            if (device.UsesPassiveBatteryReports && previous is not null && reading.Link != LinkState.Online)
                device = previous.Device;
            // 休眠、超时等没读到电量的回复不会带上协议细化出的型号名（如 ATK 读 CID/MID 后的具体型号），
            // 沿用上次的名称，避免同一设备在面板和日志里来回换名。
            if (previous is not null && reading.Percentage is null && reading.Link != LinkState.Online &&
                device.Name != previous.Device.Name)
            {
                device = device with { Name = previous.Device.Name };
                reading = reading with { Device = device };
            }
            if (previous is null && (reading.Percentage is null || reading.Link != LinkState.Online) &&
                !(device.UsesPassiveBatteryReports && reading.Link is LinkState.Online or LinkState.Asleep))
            {
                if (removedOffline.Contains(device.Id))
                    seen.Add(device.Id);
                continue;
            }
            seen.Add(device.Id);
            if (removedOffline.Contains(device.Id))
            {
                if (reading.Link != LinkState.Online)
                    continue;
                removedChanged |= removedOffline.Remove(device.Id);
            }
            var resolvedLink = ResolveLinkForSleep(device, previous?.Link, reading.Link, now);
            var last = previous?.Last;
            bool fresh = resolvedLink == LinkState.Online && reading.Percentage is not null;
            if (fresh)
            {
                anyFresh = true;
                var nextLast = new SavedBattery(device, reading.Percentage!.Value, reading.IsEstimated, reading.ReadAt)
                {
                    WasChargingOrFull = reading.Charging is ChargingState.Charging or ChargingState.Full
                };
                if (last is null || last.Percentage != nextLast.Percentage || last.IsEstimated != nextLast.IsEstimated ||
                    previous?.Charging != reading.Charging || previous?.Device != device || previous?.Link != resolvedLink ||
                    previous?.Fresh != true)
                    meaningfulChange = true;
                last = nextLast;
            }
            else if (previous is null || previous.Link != resolvedLink || previous.Device != device ||
                previous.Charging != reading.Charging && device.UsesPassiveBatteryReports)
            {
                meaningfulChange = true;
            }

            var status = new DeviceStatus(device, resolvedLink, reading.Issue, reading.Charging, last, fresh);
            devices[device.Id] = status;
            onLinkChanged?.Invoke(device, previous?.Link, status.Link);
            UpdateHistory(device.Id, status, now);
            ObserveRuntime(device.Id, status, now);
            if (fresh && UpdateReminder(status, now))
                meaningfulChange = true;
        }

        var missing = sweepMissing
            ? devices.Keys.Where(id => !seen.Contains(id) &&
                (incompleteReceivers is null || !incompleteReceivers.Contains(id.Split('/', 2)[0]))).ToArray()
            : [];
        var latestSlots = liveTargets is not null && missing.Length > 0 ? LatestLogitechSlots() : null;
        var seenSlots = latestSlots is { Count: > 0 } ?
            seen.Select(LogitechReceiverSlot).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        foreach (var id in missing)
        {
            var previous = devices[id];
            if (!IsShown(previous.Device))
                continue;
            // R1：接收器被拔出（非枚举失败且不在设备列表中）→ 立即离线。
            bool receiverStillPresent = id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase) &&
                liveTargets?.Contains(id.Split('/', 2)[0]) == true;
            if (receiverStillPresent && LogitechReceiverSlot(id) is { } slot)
                receiverStillPresent = seenSlots?.Contains(slot) == false && latestSlots?.GetValueOrDefault(slot) == previous;
            var missingLink = previous.Device.Connection == DeviceConnection.Bluetooth ? LinkState.Asleep :
                scanFailed ? LinkState.Unknown : receiverStillPresent ? LinkState.Asleep : LinkState.Offline;
            var nextLink = ResolveLinkForSleep(previous.Device, previous.Link, missingLink, now);
            if (previous.Link != nextLink || previous.Fresh)
                meaningfulChange = true;
            devices[id] = previous with
            {
                Link = nextLink,
                Issue = ReadIssue.None,
                Fresh = false,
                PendingRefresh = false,
                Charging = ChargingState.Unknown
            };
            ObserveRuntime(id, devices[id], now);
            onLinkChanged?.Invoke(previous.Device, previous.Link, nextLink);
        }
        if (anyFresh)
            UpdatedAt = now;
        // 移除后已消失的设备无需继续保留标记。再次识别时重新出现。
        if (sweepMissing && !scanFailed)
            removedChanged |= removedOffline.RemoveWhere(id => !seen.Contains(id)) > 0;

        TrimHistory();
        // D1（#21）：电量和状态都没有变化时不写 state.json（仅首次落盘或存在实质变更时返回 true）。
        bool shouldSave = meaningfulChange || removedChanged || (anyFresh && !hasSavedOnce) ||
            unsavedRuntimeHours >= RuntimeSaveHours;
        if (shouldSave)
            hasSavedOnce = true;
        return shouldSave;
    }

    private DeviceStatus? FindKnownDevice(DeviceIdentity device)
    {
        if (devices.TryGetValue(device.Id, out var exact))
            return exact;
        return device.Connection == DeviceConnection.Bluetooth && device.BluetoothContainerKey is { } key
            ? devices.Values.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Device.BluetoothContainerKey, key))
            : null;
    }

    // 继续沿用首次建档的 ID，保留跨接收器和线缆切换时的历史、别名和托盘设置。
    private IReadOnlyList<BatteryReading> MergeLogitechCableReadings(
        IReadOnlyList<BatteryReading> readings, ref bool changed)
    {
        var keyed = readings.Where(reading => LogitechUnit(reading.Device.Id) is not null)
            .GroupBy(reading => LogitechUnit(reading.Device.Id)!, StringComparer.OrdinalIgnoreCase);
        List<BatteryReading> merged = readings.Where(reading => LogitechUnit(reading.Device.Id) is null).ToList();
        foreach (var group in keyed)
        {
            var known = devices.Values.Where(item => StringComparer.OrdinalIgnoreCase.Equals(LogitechUnit(item.Device.Id), group.Key))
                .OrderByDescending(item => item.Last is not null).ToArray();
            var winner = group.OrderByDescending(item => item.Link == LinkState.Online && item.Percentage is not null)
                .ThenByDescending(item => item.Link == LinkState.Online)
                .ThenByDescending(item => item.Link == LinkState.Asleep)
                .ThenByDescending(item => item.Device.Connection == DeviceConnection.UsbCable)
                .First();
            string id = known.FirstOrDefault()?.Device.Id ?? winner.Device.Id;
            merged.Add(winner with { Device = winner.Device with { Id = id } });
            foreach (var duplicate in known.Skip(1))
            {
                string redundantId = duplicate.Device.Id;
                changed |= devices.Remove(redundantId);
                asleepSince.Remove(redundantId);
                RemoveReminders(redundantId);
                history.Remove(redundantId);
                MoveRuntime(redundantId, id);
            }
        }
        return merged;
    }

    private static string? LogitechUnit(string id)
    {
        if (!id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase) &&
            !id.StartsWith("logitech-hidpp-direct:", StringComparison.OrdinalIgnoreCase))
            return null;
        int suffix = id.LastIndexOf('/');
        return suffix >= 0 && id.AsSpan(suffix + 1).StartsWith("unit-", StringComparison.OrdinalIgnoreCase)
            ? id[(suffix + 1)..] : null;
    }

    // 同一型号的雷蛇 90B 设备插上 USB-C 线充电时，原无线接收器会同时在位并报告未响应（离线）。
    // 只要不是两台同型号设备同时在线，就沿用首次建档 ID 合并有线与无线路径，保留别名、历史与托盘勾选。
    private IReadOnlyList<BatteryReading> MergeRazerCableReadings(
        IReadOnlyList<BatteryReading> readings, ref bool changed)
    {
        var razerGroups = readings
            .Where(reading => IsMergeableRazer90B(reading.Device))
            .GroupBy(reading => (Name: reading.Device.Name, reading.Device.Kind),
                RazerModelKeyComparer.Instance);
        List<BatteryReading> merged = readings.Where(reading => !IsMergeableRazer90B(reading.Device)).ToList();
        foreach (var group in razerGroups)
        {
            var items = group.ToArray();
            // 两台同型号设备同时在线（例如两只 Viper V3 Pro），各自独立保留。
            if (items.Count(item => item.Link == LinkState.Online) >= 2)
            {
                foreach (var item in items)
                    mergedRazerIds.Remove(item.Device.Id);
                merged.AddRange(items);
                continue;
            }

            bool hasBothConnections = items.Any(item => item.Device.Connection == DeviceConnection.UsbCable) &&
                items.Any(item => item.Device.Connection == DeviceConnection.Receiver);
            var known = devices.Values
                .Where(item => IsMergeableRazer90B(item.Device) &&
                    item.Device.Kind == group.Key.Kind &&
                    StringComparer.OrdinalIgnoreCase.Equals(item.Device.Name, group.Key.Name))
                .OrderByDescending(item => item.Last is not null)
                .ToArray();

            bool singleSwitch = items.Length == 1 && !devices.ContainsKey(items[0].Device.Id) &&
                known.Length == 1 && known[0].Device.Connection != items[0].Device.Connection;
            if (!hasBothConnections && !singleSwitch)
            {
                merged.AddRange(items);
                continue;
            }

            var winner = items
                .OrderByDescending(item => item.Link == LinkState.Online && item.Percentage is not null)
                .ThenByDescending(item => item.Link == LinkState.Online)
                .ThenByDescending(item => item.Link == LinkState.Asleep)
                .ThenByDescending(item => item.Device.Connection == DeviceConnection.UsbCable)
                .First();
            string canonicalId = known.FirstOrDefault()?.Device.Id ?? winner.Device.Id;
            foreach (var item in items)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(item.Device.Id, canonicalId))
                    mergedRazerIds[item.Device.Id] = canonicalId;
            }
            merged.Add(winner with { Device = winner.Device with { Id = canonicalId } });
            foreach (var duplicate in known.Skip(1))
            {
                string redundantId = duplicate.Device.Id;
                changed |= devices.Remove(redundantId);
                asleepSince.Remove(redundantId);
                RemoveReminders(redundantId);
                history.Remove(redundantId);
                MoveRuntime(redundantId, canonicalId);
                mergedRazerIds[redundantId] = canonicalId;
            }
        }
        return merged;
    }

    private static bool IsMergeableRazer90B(DeviceIdentity device) =>
        device.Id.StartsWith("razer-90b:", StringComparison.OrdinalIgnoreCase) &&
        device.Connection is DeviceConnection.Receiver or DeviceConnection.UsbCable &&
        !StringComparer.OrdinalIgnoreCase.Equals(device.Name, "Razer HyperPolling Wireless Dongle");

    private sealed class RazerModelKeyComparer : IEqualityComparer<(string Name, DeviceKind Kind)>
    {
        public static readonly RazerModelKeyComparer Instance = new();
        public bool Equals((string Name, DeviceKind Kind) x, (string Name, DeviceKind Kind) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);
        public int GetHashCode((string Name, DeviceKind Kind) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name), obj.Kind);
    }

    /// <summary>应用心跳或设备推送的在线状态及电量变化（B1 / B2 / R1 / R2）。</summary>
    public (bool UiChanged, bool NeedsBatteryRefresh) ApplyPresence(
        IReadOnlyList<PresenceUpdate> updates,
        DateTimeOffset now,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null)
    {
        var (uiChanged, needsFullRefresh, refreshIds) = ApplyPresenceDetailed(updates, now, onLinkChanged);
        return (uiChanged, needsFullRefresh || refreshIds.Length > 0);
    }

    /// <summary>应用心跳或设备推送，并区分需要全量刷新（未知新设备）还是仅定向补查刚唤醒的设备。</summary>
    public (bool UiChanged, bool NeedsFullRefresh, string[] RefreshDeviceIds) ApplyPresenceDetailed(
        IReadOnlyList<PresenceUpdate> updates,
        DateTimeOffset now,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null)
    {
        bool uiChanged = false;
        bool needsFullRefresh = false;
        HashSet<string>? refreshIds = null;

        foreach (var update in updates)
        {
            if (update.Battery is { } battery)
            {
                var known = FindKnownDevice(battery.Device);
                if (known is null && LogitechUnit(battery.Device.Id) is { } unit)
                    known = devices.Values.FirstOrDefault(item =>
                        StringComparer.OrdinalIgnoreCase.Equals(LogitechUnit(item.Device.Id), unit));
                if (known is not null)
                {
                    // 当线缆和接收器同时工作时，低优先级通道的推送不能覆盖线缆读数。
                    if (known.Device.Connection != battery.Device.Connection || !IsShown(known.Device) ||
                        known.Last is { } last && last.ReadAt > battery.ReadAt)
                        continue;
                    if (battery.Device.Connection != DeviceConnection.Bluetooth && !battery.Device.UsesPassiveBatteryReports)
                        battery = battery with { Device = known.Device };
                }
                uiChanged |= ApplyReadings([battery], ScanFailed, now, UnknownReceivers, onLinkChanged, null,
                    mergeChannels: false, sweepMissing: false);
                continue;
            }
            var matches = FindMatchingDeviceIds(update);
            if (matches.Count == 0)
            {
                if (update.Link == LinkState.Online)
                    needsFullRefresh = true;
                continue;
            }

            foreach (string id in matches)
            {
                var previous = devices[id];
                if (!IsShown(previous.Device) ||
                    update.Slot is not null && previous.Device.Connection == DeviceConnection.UsbCable ||
                    update.Connection is { } conn && conn != previous.Device.Connection)
                    continue;
                var resolvedLink = ResolveLinkForSleep(previous.Device, previous.Link, update.Link, now);
                if (previous.Link == resolvedLink && previous.Issue == update.Issue)
                    continue;

                bool becameOnline = !previous.IsOnline && resolvedLink == LinkState.Online;
                bool lostLink = resolvedLink is LinkState.Offline or LinkState.Asleep;
                // R1 / R3：从休眠唤醒到在线时，保留非灰显示直至完整电量刷新返回，避免图标闪灰。
                bool pendingRefresh = becameOnline && previous.Link == LinkState.Asleep && previous.Last is not null;
                var next = previous with
                {
                    Link = resolvedLink,
                    Issue = update.Issue,
                    Fresh = lostLink ? false : previous.Fresh,
                    PendingRefresh = lostLink ? false : pendingRefresh || previous.PendingRefresh,
                    Charging = lostLink ? ChargingState.Unknown : previous.Charging
                };
                devices[id] = next;
                ObserveRuntime(id, next, now);
                uiChanged = true;
                if (becameOnline)
                {
                    refreshIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    refreshIds.Add(id);
                    UpdateHistory(id, next, now);
                }
                onLinkChanged?.Invoke(previous.Device, previous.Link, next.Link);
            }
        }
        return (uiChanged, needsFullRefresh, refreshIds is null ? [] : [.. refreshIds]);
    }

    /// <summary>
    /// R1 / R2 状态转移规则：
    /// 1) 已处于 Offline 的设备不会因超时/休眠回复退回 Asleep，必须收到明确 Online 才恢复；
    /// 2) 连续休眠达到 10 分钟仍无回复则转为 Offline。
    /// </summary>
    private LinkState ResolveLinkForSleep(DeviceIdentity device, LinkState? previousLink, LinkState incoming, DateTimeOffset now)
    {
        string id = device.Id;
        if (device.UsesPassiveBatteryReports)
            return incoming;
        if (incoming == LinkState.Online)
        {
            asleepSince.Remove(id);
            return LinkState.Online;
        }
        if (incoming == LinkState.Asleep)
        {
            if (previousLink == LinkState.Offline)
            {
                asleepSince.Remove(id);
                return LinkState.Offline;
            }
            if (!asleepSince.TryGetValue(id, out var since) || now < since)
            {
                asleepSince[id] = now;
                return LinkState.Asleep;
            }
            if (now - since >= SleepToOfflineTimeout)
            {
                asleepSince.Remove(id);
                return LinkState.Offline;
            }
            return LinkState.Asleep;
        }

        asleepSince.Remove(id);
        return incoming;
    }

    private List<string> FindMatchingDeviceIds(PresenceUpdate update)
    {
        if (update.DeviceId is { Length: > 0 } exact)
        {
            if (devices.ContainsKey(exact))
                return [exact];
            if (mergedRazerIds.TryGetValue(exact, out var canonical) && devices.ContainsKey(canonical))
                return [canonical];
            if (LogitechUnit(exact) is { } unit)
                return devices.Keys.Where(id => StringComparer.OrdinalIgnoreCase.Equals(LogitechUnit(id), unit)).ToList();
        }

        if (update.Slot is byte slot)
        {
            string key = $"logitech-hidpp:{update.ReceiverId}/{slot}";
            if (LatestLogitechSlots().TryGetValue(key, out var latest))
                return [latest.Device.Id];
        }
        return [];
    }

    /// <summary>累计在线时长达到这个值时，即使电量没变也顺带写一次 state.json，避免崩溃丢失过多学习进度。</summary>
    public const double RuntimeSaveHours = .5;

    public bool HasUnsavedRuntime => unsavedRuntimeHours > 0;

    private TimeSpan RuntimeGapLimit()
    {
        int poll = inGameMode ? Math.Max(Settings.PollSeconds, Settings.GamePollSeconds) : Settings.PollSeconds;
        return TimeSpan.FromSeconds(Math.Max(RuntimeEstimator.ShortPause.TotalSeconds, 2.5 * poll));
    }

    private void ObserveRuntime(string id, DeviceStatus status, DateTimeOffset now)
    {
        var previous = runtime.GetValueOrDefault(id);
        if (previous is null && !(status.Fresh && status.Link == LinkState.Online))
            return;
        var (charged, chargeHours) = ChargeEstimator.Observe(previous, status, now, RuntimeGapLimit());
        var (next, hours) = RuntimeEstimator.Observe(charged, status, now, RuntimeGapLimit(), TimeZone);
        runtime[id] = next;
        unsavedRuntimeHours += hours + chargeHours;
        if (next.ChargeSegment is null || previous?.ChargeSegment is null)
            chargeShown.Remove(id);
        if (next.Segment is null || previous?.Segment is null || next.Segment.Level > previous.Segment.Level)
        {
            runtimeShown.Remove(id);
            // 充过电或开了新的放电段：预测提醒和进游戏横幅都重新开始计算。
            if (previous?.Segment is not null || next.Segment is null)
            {
                reminders.Remove(ReminderKeys.For(ReminderKind.Predicted, id));
                gameWarned.Remove(id);
            }
        }
    }

    private void MoveRuntime(string from, string to)
    {
        if (runtime.Remove(from, out var moved) && !runtime.ContainsKey(to))
            runtime[to] = moved;
        runtimeShown.Remove(from);
        chargeShown.Remove(from);
    }

    /// <summary>
    /// 当前应显示的预计时间：充电中为距离充满的时间，否则为剩余可用时间。功能关闭时返回 None（后台仍在学习）。
    /// 显示值带迟滞：同一放电段内小幅回升（≤15%）或相对变化不足 8% 时保持原值，避免数字来回跳。
    /// </summary>
    public RuntimeEstimate Runtime(DeviceStatus device, DateTimeOffset now)
    {
        if (!Settings.ShowRuntimeEstimate)
            return RuntimeEstimate.None;
        string id = device.Device.Id;
        if (device.IsCharging && !device.IsFull)
        {
            runtimeShown.Remove(id);
            return ChargeTime(id, device, now);
        }
        chargeShown.Remove(id);
        var estimate = RuntimeEstimator.Estimate(runtime.GetValueOrDefault(id), device, now);
        if (estimate.Kind != RuntimeKind.Ready || device.IsFull)
        {
            runtimeShown.Remove(id);
            return estimate;
        }

        double rounded = RuntimeEstimator.Rounded(estimate.Hours);
        if (runtimeShown.TryGetValue(id, out double shown) && shown > 0)
        {
            bool smallRise = rounded > shown && rounded <= shown * 1.15;
            bool smallChange = Math.Abs(rounded - shown) < shown * .08;
            if (smallRise || smallChange)
                rounded = shown;
        }
        runtimeShown[id] = rounded;
        // 返回显示值，天数按同一比例换算，保证面板、悬停卡片看到的是同一个数。
        double? shownDays = estimate.Days is { } days && estimate.Hours > 0 ? days * rounded / estimate.Hours : null;
        return estimate with { Hours = rounded, Days = shownDays is >= 2 ? shownDays : null };
    }

    /// <summary>充满时间只允许小幅下降后才刷新；回升超过 15% 才跟上（例如换成了慢充头），避免数字抖动。</summary>
    private RuntimeEstimate ChargeTime(string id, DeviceStatus device, DateTimeOffset now)
    {
        var estimate = ChargeEstimator.Estimate(runtime.GetValueOrDefault(id), device, now);
        if (estimate.Kind != RuntimeKind.Ready)
        {
            chargeShown.Remove(id);
            return estimate;
        }
        double rounded = ChargeEstimator.Rounded(estimate.Hours);
        if (chargeShown.TryGetValue(id, out double shown) && rounded > shown && rounded <= shown * 1.15)
            rounded = shown;
        chargeShown[id] = rounded;
        return estimate with { Hours = rounded };
    }

    internal SavedRuntimeEstimate? RuntimeState(string id) => runtime.GetValueOrDefault(id);

    private void UpdateHistory(string id, DeviceStatus status, DateTimeOffset now)
    {
        bool exists = history.TryGetValue(id, out var existing);
        var firstSeen = exists ? existing!.FirstSeen : (status.Last?.ReadAt ?? now);
        var lastOnline = status.IsOnline ? now : existing?.LastOnline;
        var samples = existing?.RecentReadings ?? [];

        if (status is { Fresh: true, Last: { } last })
        {
            var lastSample = samples.Length > 0 ? samples[^1] : null;
            bool record = lastSample is null ||
                lastSample.Percentage != last.Percentage ||
                lastSample.IsEstimated != last.IsEstimated ||
                lastSample.Charging != status.Charging;
            if (record)
            {
                var nextSample = new SavedHistorySample(last.ReadAt, last.Percentage, last.IsEstimated, status.Charging);
                samples = [.. samples.TakeLast(MaxHistorySamples - 1), nextSample];
            }
        }

        history[id] = new(firstSeen, lastOnline, samples);
    }

    private void TrimHistory()
    {
        while (history.Count > MaxHistoryDevices)
        {
            var oldest = history
                .OrderBy(pair => devices.ContainsKey(pair.Key) ? 1 : 0)
                .ThenBy(pair => pair.Value.LastOnline ?? pair.Value.FirstSeen)
                .First().Key;
            history.Remove(oldest);
        }
        foreach (var id in runtime.Keys.Where(id => !devices.ContainsKey(id) && !history.ContainsKey(id)).ToArray())
        {
            runtime.Remove(id);
            runtimeShown.Remove(id);
            chargeShown.Remove(id);
        }
    }

    public DiagnosticDeviceHistory[] DiagnosticHistories()
    {
        List<DiagnosticDeviceHistory> list = [];
        foreach (var status in OrderedForManagement())
        {
            history.TryGetValue(status.Device.Id, out var entry);
            var samples = (entry?.RecentReadings ?? [])
                .TakeLast(MaxHistorySamples)
                .Select(s => new DiagnosticReadingSample(s.At, s.Percentage, s.IsEstimated, s.Charging))
                .ToArray();
            var first = entry?.FirstSeen ?? status.Last?.ReadAt ?? DateTimeOffset.UtcNow;
            var lastOnline = entry?.LastOnline ?? (status.IsOnline ? DateTimeOffset.UtcNow : status.Last?.ReadAt);
            list.Add(new(status.Device, first, lastOnline, samples));
        }
        return [.. list];
    }

    /// <summary>根据最新读数增删三类提醒；返回是否有变化（用于决定是否写盘）。</summary>
    private bool UpdateReminder(DeviceStatus device, DateTimeOffset now)
    {
        string id = device.Device.Id;
        bool changed;
        if (device.Percentage <= ThresholdFor(id) && !device.IsCharging && !device.IsFull)
            changed = reminders.TryAdd(id, new(now));
        else
            changed = reminders.Remove(id);

        string fullKey = ReminderKeys.For(ReminderKind.FullCharge, id);
        if (Settings.FullChargeReminder && IsChargedFull(device))
            changed |= reminders.TryAdd(fullKey, new(now));
        else if (!Settings.FullChargeReminder || device.Fresh && device.Charging == ChargingState.NotCharging)
            changed |= reminders.Remove(fullKey);

        string predictKey = ReminderKeys.For(ReminderKind.Predicted, id);
        if (device.IsCharging || device.IsFull || !Settings.ShowRuntimeEstimate)
            changed |= reminders.Remove(predictKey);
        else if (PredictsShortfall(device, now))
            changed |= reminders.TryAdd(predictKey, new(now));
        return changed;
    }

    private static bool IsChargedFull(DeviceStatus device) =>
        device is { Fresh: true, Link: LinkState.Online, Last: { } last } &&
        (device.Charging == ChargingState.Full || device.Charging == ChargingState.Charging && last.Percentage == 100);

    /// <summary>
    /// 预测提醒：剩余可用时长不够本机日均在线时长（按你的用法撑不过一天），且电量还没到低电量阈值。
    /// 需要打开「预计可用时间」并攒够 3 个活跃日。
    /// </summary>
    internal bool PredictsShortfall(DeviceStatus device, DateTimeOffset now)
    {
        if (!Settings.ShowRuntimeEstimate || device is not { Fresh: true, Link: LinkState.Online, Last: { } last } ||
            device.IsCharging || device.IsFull || last.Percentage <= ThresholdFor(device.Device.Id))
            return false;
        var state = runtime.GetValueOrDefault(device.Device.Id);
        if (RuntimeEstimator.DailyHours(state) is not { } daily)
            return false;
        var estimate = RuntimeEstimator.Estimate(state, device, now);
        return estimate.Kind == RuntimeKind.Ready && estimate.Hours < daily;
    }

    public DateTimeOffset? NextReminder(DateTimeOffset now) => reminders
        .Where(pair => CanRemind(pair.Key, now))
        .Select(pair => pair.Value.Due)
        .Min();

    /// <summary>当前应弹出的提醒键：低电量优先，其次预测提醒，最后充满提醒。</summary>
    public string? ReadyReminderKey(DateTimeOffset now, bool blocked)
    {
        if (blocked)
            return null;
        var ordered = OrderedDevices();
        foreach (var kind in new[] { ReminderKind.LowBattery, ReminderKind.Predicted, ReminderKind.FullCharge })
        {
            foreach (var device in ordered)
            {
                string key = ReminderKeys.For(kind, device.Device.Id);
                if (ReminderIsDue(key, now))
                    return key;
            }
        }
        return null;
    }

    public DeviceStatus? ReadyReminder(DateTimeOffset now, bool blocked) =>
        ReadyReminderKey(now, blocked) is { } key ? devices.GetValueOrDefault(ReminderKeys.Parse(key).DeviceId) : null;

    public bool ReminderIsDue(string key, DateTimeOffset now) =>
        CanRemind(key, now) && reminders.TryGetValue(key, out var reminder) && reminder.Due <= now;

    private bool CanRemind(string key, DateTimeOffset now)
    {
        var (kind, id) = ReminderKeys.Parse(key);
        if (!devices.TryGetValue(id, out var device) ||
            device is not { Fresh: true, Link: LinkState.Online, Last: { } last } || IsExpired(last, now, inGameMode))
            return false;
        return kind switch
        {
            ReminderKind.FullCharge => Settings.FullChargeReminder && IsChargedFull(device),
            ReminderKind.Predicted => Settings.ShowRuntimeEstimate && !device.IsCharging && !device.IsFull,
            _ => !device.IsCharging && !device.IsFull && last.Percentage <= ThresholdFor(id)
        };
    }

    /// <summary>
    /// 刚进入游戏时需要提醒的设备：在用、没在充电，电量不高于该设备阈值，
    /// 或打开了预计可用时间且剩余不到 1 小时。每台设备每轮放电只返回一次。
    /// </summary>
    public DeviceStatus[] TakeGameEntryWarnings(DateTimeOffset now)
    {
        if (!Settings.GameEntryReminderEnabled)
            return [];
        List<DeviceStatus> warnings = [];
        foreach (var device in OrderedDevices())
        {
            if (device is not { Fresh: true, Link: LinkState.Online, Last: { } last } || device.IsCharging ||
                device.IsFull || IsExpired(last, now, game: true) || gameWarned.Contains(device.Device.Id) ||
                IsHidden(device.Device.Id))
                continue;
            bool low = last.Percentage <= ThresholdFor(device.Device.Id);
            bool shortRuntime = Settings.ShowRuntimeEstimate &&
                RuntimeEstimator.Estimate(runtime.GetValueOrDefault(device.Device.Id), device, now) is
                    { Kind: RuntimeKind.Ready, Hours: < 1 };
            if (!low && !shortRuntime)
                continue;
            gameWarned.Add(device.Device.Id);
            warnings.Add(device);
        }
        return [.. warnings];
    }

    public DateTimeOffset? LastCharged(string id) => runtime.GetValueOrDefault(id)?.LastChargeAt;

    private TimeSpan ReadingTtl(bool game) =>
        TimeSpan.FromSeconds(2 * (game ? Math.Max(Settings.PollSeconds, Settings.GamePollSeconds) : Settings.PollSeconds));

    private bool IsExpired(SavedBattery last, DateTimeOffset now, bool game = false) =>
        now - last.ReadAt > ReadingTtl(game) || last.ReadAt - now > FutureClockSkew;

    public DateTimeOffset? ReminderExpires(string key)
    {
        if (devices.GetValueOrDefault(ReminderKeys.Parse(key).DeviceId)?.Last is not { } last)
            return null;
        // 用时间差判断读数是否过期；定时器遇到不合理的未来记录直接略过。
        var ttl = ReadingTtl(inGameMode).Add(TimeSpan.FromTicks(1));
        return last.ReadAt <= DateTimeOffset.MaxValue - ttl ? last.ReadAt + ttl : null;
    }

    public void SetReminder(string id, DateTimeOffset? due)
    {
        if (reminders.ContainsKey(id))
            reminders[id] = new(due);
    }

    public TrayState Snapshot(Dictionary<string, string>? blocked = null)
    {
        unsavedRuntimeHours = 0;
        var readings = new Dictionary<string, SavedBattery>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, device) in devices)
        {
            if (device.Last is { } last)
                readings[id] = last;
        }

        if (blocked is not null)
        {
            blockedReceivers.Clear();
            foreach (var (sig, reason) in blocked)
                blockedReceivers[sig] = reason;
        }

        return new()
        {
            Readings = readings,
            Reminders = new(reminders, StringComparer.OrdinalIgnoreCase),
            RemovedOfflineDevices = [.. removedOffline],
            History = new(history, StringComparer.OrdinalIgnoreCase),
            Runtime = new(runtime, StringComparer.OrdinalIgnoreCase),
            BlockedReceivers = new(blockedReceivers, StringComparer.OrdinalIgnoreCase)
        };
    }
}

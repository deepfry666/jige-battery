using Jige.Core;
using Jige.Core.Diagnostics;

namespace Jige.Tray;

internal sealed record DeviceStatus(DeviceIdentity Device, LinkState Link, ReadIssue Issue,
    ChargingState Charging, SavedBattery? Last, bool Fresh, bool PendingRefresh = false)
{
    public int? Percentage => Last?.Percentage;
    public bool IsCharging => Fresh && Charging == ChargingState.Charging;
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

internal sealed class TrayModel
{
    public const int MaxHistoryDevices = 32;
    public const int MaxHistorySamples = 5;

    /// <summary>
    /// R2：连续休眠超过 10 分钟仍无任何回复才转为离线显示。
    /// Compx 等无线鼠标静止数秒后接收器即停止应答查询（见 docs/protocols.md），短时无回复属于正常省电休眠；
    /// 10 分钟足够覆盖日常看文档、看视频等短暂停顿而不误判离线，同时在设备真正关机或带离后能及时转为离线变灰。
    /// </summary>
    public static readonly TimeSpan SleepToOfflineTimeout = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, DeviceStatus> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedReminder> reminders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removedOffline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedDeviceHistory> history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> blockedReceivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> asleepSince = new(StringComparer.OrdinalIgnoreCase);
    private bool hasSavedOnce;

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

        foreach (var (id, reminder) in saved.Reminders ?? [])
        {
            if (reminder is not null && devices.TryGetValue(id, out var device) &&
                device.Percentage < Settings.LowBatteryThreshold)
                reminders[id] = reminder;
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

    public bool ExpireReadings(DateTimeOffset now)
    {
        bool changed = false;
        foreach (var (id, device) in devices)
        {
            if (device is { Fresh: true, Last: { } last } && IsExpired(last, now))
            {
                devices[id] = device with { Fresh = false };
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
            if (device.Link != LinkState.Asleep)
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
            if (device.Link != LinkState.Asleep)
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
            reminders.Remove(id);
            history.Remove(id);
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

    /// <summary>根据托盘模式返回应当显示在系统托盘中的设备列表（B5/B6）。</summary>
    public DeviceStatus[] TrayDevices()
    {
        if (Settings.TrayMode == TrayDisplayMode.LowestBattery)
            return PrimaryDevice is { } top ? [top] : [];
        return OrderedForManagement().Where(item => Settings.IsInTray(item.Device.Id)).ToArray();
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
        int previous = Settings.LowBatteryThreshold;
        Settings = settings;
        if (previous == settings.LowBatteryThreshold)
            return;

        foreach (var device in devices.Values)
        {
            if (device.Percentage >= settings.LowBatteryThreshold)
                reminders.Remove(device.Device.Id);
            else if (device.Fresh)
                UpdateReminder(device, now);
        }
    }

    public bool ApplyPoll(
        IReadOnlyList<BatteryReading> readings,
        bool scanFailed,
        DateTimeOffset now,
        int unknownReceivers = 0,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null,
        IReadOnlySet<string>? liveTargets = null) =>
        ApplyReadings(readings, scanFailed, now, unknownReceivers, onLinkChanged, liveTargets, completePoll: true);

    private bool ApplyReadings(IReadOnlyList<BatteryReading> readings, bool scanFailed, DateTimeOffset now,
        int unknownReceivers, Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged,
        IReadOnlySet<string>? liveTargets, bool completePoll)
    {
        ScanFailed = scanFailed;
        if (!scanFailed || unknownReceivers > 0)
            UnknownReceivers = unknownReceivers;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyFresh = false, meaningfulChange = false, removedChanged = false;
        if (completePoll)
            readings = MergeLogitechCableReadings(readings, ref meaningfulChange);
        foreach (var input in readings)
        {
            var reading = input;
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
                        BluetoothContainerKey = device.BluetoothContainerKey ?? known.BluetoothContainerKey
                    };
                    reading = reading with { Device = device };
                }
                else if (reading.Percentage is null)
                    continue;
            }
            seen.Add(device.Id);
            if (removedOffline.Contains(device.Id))
            {
                if (reading.Link != LinkState.Online)
                    continue;
                removedChanged |= removedOffline.Remove(device.Id);
            }
            var previous = devices.GetValueOrDefault(device.Id);
            var resolvedLink = ResolveLinkForSleep(device.Id, previous?.Link, reading.Link, now);
            var last = previous?.Last;
            bool fresh = resolvedLink == LinkState.Online && reading.Percentage is not null;
            if (fresh)
            {
                anyFresh = true;
                var nextLast = new SavedBattery(device, reading.Percentage!.Value, reading.IsEstimated, reading.ReadAt);
                if (last is null || last.Percentage != nextLast.Percentage || last.IsEstimated != nextLast.IsEstimated ||
                    previous?.Charging != reading.Charging || previous?.Device != device || previous?.Link != resolvedLink ||
                    previous?.Fresh != true)
                    meaningfulChange = true;
                last = nextLast;
            }
            else if (previous is null || previous.Link != resolvedLink || previous.Device != device)
            {
                meaningfulChange = true;
            }

            var status = new DeviceStatus(device, resolvedLink, reading.Issue, reading.Charging, last, fresh);
            devices[device.Id] = status;
            onLinkChanged?.Invoke(device, previous?.Link, status.Link);
            UpdateHistory(device.Id, status, now);
            if (fresh && UpdateReminder(status, now))
                meaningfulChange = true;
        }

        foreach (var id in completePoll ? devices.Keys.Where(id => !seen.Contains(id)).ToArray() : [])
        {
            var previous = devices[id];
            if (!IsShown(previous.Device))
                continue;
            // R1：接收器被拔出（非枚举失败且不在设备列表中）→ 立即离线。
            bool receiverStillPresent = id.StartsWith("logitech-hidpp:", StringComparison.OrdinalIgnoreCase) &&
                liveTargets?.Contains(id.Split('/', 2)[0]) == true;
            var missingLink = previous.Device.Connection == DeviceConnection.Bluetooth ? LinkState.Asleep :
                scanFailed ? LinkState.Unknown : receiverStillPresent ? LinkState.Asleep : LinkState.Offline;
            var nextLink = ResolveLinkForSleep(id, previous.Link, missingLink, now);
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
            onLinkChanged?.Invoke(previous.Device, previous.Link, nextLink);
        }
        if (anyFresh)
            UpdatedAt = now;
        // 移除后已消失的设备无需继续保留标记。再次识别时重新出现。
        if (completePoll && !scanFailed)
            removedChanged |= removedOffline.RemoveWhere(id => !seen.Contains(id)) > 0;

        TrimHistory();
        // D1（#21）：电量和状态都没有变化时不写 state.json（仅首次落盘或存在实质变更时返回 true）。
        bool shouldSave = meaningfulChange || removedChanged || (anyFresh && !hasSavedOnce);
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
                reminders.Remove(redundantId);
                history.Remove(redundantId);
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

    /// <summary>应用心跳或罗技接收器推送的在线状态变化（B1 / B2 / R1 / R2）。</summary>
    public (bool UiChanged, bool NeedsBatteryRefresh) ApplyPresence(
        IReadOnlyList<PresenceUpdate> updates,
        DateTimeOffset now,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null)
    {
        bool uiChanged = false;
        bool needsRefresh = false;

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
                    if (battery.Device.Connection != DeviceConnection.Bluetooth)
                        battery = battery with { Device = known.Device };
                }
                uiChanged |= ApplyReadings([battery], ScanFailed, now, UnknownReceivers, onLinkChanged, null, completePoll: false);
                continue;
            }
            var matches = FindMatchingDeviceIds(update);
            if (matches.Count == 0)
            {
                if (update.Link == LinkState.Online)
                    needsRefresh = true;
                continue;
            }

            foreach (string id in matches)
            {
                var previous = devices[id];
                if (!IsShown(previous.Device) || update.Slot is not null && previous.Device.Connection == DeviceConnection.UsbCable)
                    continue;
                var resolvedLink = ResolveLinkForSleep(id, previous.Link, update.Link, now);
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
                uiChanged = true;
                if (becameOnline)
                {
                    needsRefresh = true;
                    UpdateHistory(id, next, now);
                }
                onLinkChanged?.Invoke(previous.Device, previous.Link, next.Link);
            }
        }
        return (uiChanged, needsRefresh);
    }

    /// <summary>
    /// R1 / R2 状态转移规则：
    /// 1) 已处于 Offline 的设备不会因超时/休眠回复退回 Asleep，必须收到明确 Online 才恢复；
    /// 2) 连续休眠达到 10 分钟仍无回复则转为 Offline。
    /// </summary>
    private LinkState ResolveLinkForSleep(string id, LinkState? previousLink, LinkState incoming, DateTimeOffset now)
    {
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
            if (LogitechUnit(exact) is { } unit)
                return devices.Keys.Where(id => StringComparer.OrdinalIgnoreCase.Equals(LogitechUnit(id), unit)).ToList();
        }

        if (update.Slot is byte slot)
        {
            string prefix = $"logitech-hidpp:{update.ReceiverId}/{slot}/";
            return devices.Keys.Where(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return [];
    }

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

    private bool UpdateReminder(DeviceStatus device, DateTimeOffset now)
    {
        if (device.Percentage < Settings.LowBatteryThreshold && !device.IsCharging && !device.IsFull)
            return reminders.TryAdd(device.Device.Id, new(now));
        return reminders.Remove(device.Device.Id);
    }

    public DateTimeOffset? NextReminder(DateTimeOffset now) => devices.Values
        .Where(device => CanRemind(device, now))
        .Select(device => reminders.GetValueOrDefault(device.Device.Id)?.Due)
        .Min();

    public DeviceStatus? ReadyReminder(DateTimeOffset now, bool blocked) => blocked ? null :
        OrderedDevices().FirstOrDefault(item => ReminderIsDue(item.Device.Id, now));

    public bool ReminderIsDue(string id, DateTimeOffset now) =>
        devices.TryGetValue(id, out var device) && CanRemind(device, now) &&
        reminders.TryGetValue(id, out var reminder) && reminder.Due <= now;

    private bool CanRemind(DeviceStatus device, DateTimeOffset now) =>
        device is { Fresh: true, Link: LinkState.Online, IsCharging: false, IsFull: false, Last: { } last } &&
        last.Percentage < Settings.LowBatteryThreshold &&
        !IsExpired(last, now);

    private bool IsExpired(SavedBattery last, DateTimeOffset now) =>
        now - last.ReadAt > TimeSpan.FromSeconds(2 * Settings.PollSeconds);

    public DateTimeOffset? ReminderExpires(string id)
    {
        if (devices.GetValueOrDefault(id)?.Last is not { } last)
            return null;
        // 用时间差判断读数是否过期；定时器遇到不合理的未来记录直接略过。
        var ttl = TimeSpan.FromSeconds(2 * Settings.PollSeconds).Add(TimeSpan.FromTicks(1));
        return last.ReadAt <= DateTimeOffset.MaxValue - ttl ? last.ReadAt + ttl : null;
    }

    public void SetReminder(string id, DateTimeOffset? due)
    {
        if (reminders.ContainsKey(id))
            reminders[id] = new(due);
    }

    public TrayState Snapshot(Dictionary<string, string>? blocked = null)
    {
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
            BlockedReceivers = new(blockedReceivers, StringComparer.OrdinalIgnoreCase)
        };
    }
}

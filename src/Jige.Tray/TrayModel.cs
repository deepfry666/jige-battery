using Jige.Core;
using Jige.Core.Diagnostics;

namespace Jige.Tray;

internal sealed record DeviceStatus(DeviceIdentity Device, LinkState Link, ReadIssue Issue,
    ChargingState Charging, SavedBattery? Last, bool Fresh)
{
    public int? Percentage => Last?.Percentage;
    public bool IsCharging => Fresh && Charging == ChargingState.Charging;
    public bool IsFull => Fresh && (Charging == ChargingState.Full || Percentage == 100);
    public bool IsOnline => Link == LinkState.Online;
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

    private readonly Dictionary<string, DeviceStatus> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedReminder> reminders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removedOffline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedDeviceHistory> history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> blockedReceivers = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? lastPersistedAt;

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

    public bool CanRemove(string id) => devices.TryGetValue(id, out var device) && device.Link == LinkState.Offline;

    public void RemoveOffline(string id)
    {
        if (CanRemove(id))
        {
            devices.Remove(id);
            reminders.Remove(id);
            history.Remove(id);
            removedOffline.Add(id);
        }
    }

    /// <summary>主面板自动排序规则（B7）：在线按电量低→高，顶部大卡片取最低在线设备，无在线时取最近一台离线设备。</summary>
    public MainPanelPlan BuildMainPanel()
    {
        var visible = devices.Values.Where(item => !IsHidden(item.Device.Id)).ToArray();
        var online = visible.Where(item => item.IsOnline)
            .OrderBy(item => item.Percentage ?? int.MaxValue)
            .ThenBy(item => item.Device.Kind)
            .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var offline = visible.Where(item => !item.IsOnline)
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

    public DeviceStatus[] OrderedDevices() => devices.Values
        .OrderByDescending(item => item.IsOnline)
        .ThenBy(item => item.IsOnline ? (item.Percentage ?? int.MaxValue) : 0)
        .ThenByDescending(item => item.IsOnline ? DateTimeOffset.MinValue : (item.Last?.ReadAt ?? DateTimeOffset.MinValue))
        .ThenBy(item => item.Device.Kind)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public DeviceStatus[] OrderedForManagement() => devices.Values
        .OrderByDescending(item => item.IsOnline)
        .ThenBy(item => item.Device.Kind)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

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
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null)
    {
        ScanFailed = scanFailed;
        if (!scanFailed || unknownReceivers > 0)
            UnknownReceivers = unknownReceivers;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyFresh = false, meaningfulChange = false, removedChanged = false;
        foreach (var reading in readings)
        {
            var device = reading.Device;
            seen.Add(device.Id);
            if (removedOffline.Contains(device.Id))
            {
                if (reading.Link != LinkState.Online)
                    continue;
                removedChanged |= removedOffline.Remove(device.Id);
            }
            var previous = devices.GetValueOrDefault(device.Id);
            var last = previous?.Last;
            bool fresh = reading is { Link: LinkState.Online, Percentage: not null };
            if (fresh)
            {
                anyFresh = true;
                var nextLast = new SavedBattery(device, reading.Percentage!.Value, reading.IsEstimated, reading.ReadAt);
                if (last is null || last.Percentage != nextLast.Percentage || last.IsEstimated != nextLast.IsEstimated ||
                    previous?.Charging != reading.Charging || previous?.Device != device)
                    meaningfulChange = true;
                last = nextLast;
            }
            else if (previous is null || previous.Link != reading.Link)
            {
                meaningfulChange = true;
            }

            var status = new DeviceStatus(device, reading.Link, reading.Issue, reading.Charging, last, fresh);
            devices[device.Id] = status;
            onLinkChanged?.Invoke(device, previous?.Link, status.Link);
            UpdateHistory(device.Id, status, now);
            if (fresh)
                UpdateReminder(status, now);
        }

        foreach (var id in devices.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            var previous = devices[id];
            var nextLink = scanFailed ? LinkState.Unknown : LinkState.Offline;
            if (previous.Link != nextLink || previous.Fresh)
                meaningfulChange = true;
            devices[id] = previous with
            {
                Link = nextLink,
                Issue = ReadIssue.None,
                Fresh = false,
                Charging = ChargingState.Unknown
            };
            onLinkChanged?.Invoke(previous.Device, previous.Link, nextLink);
        }
        if (anyFresh)
            UpdatedAt = now;
        // 移除后已消失的设备无需继续保留标记。再次识别时重新出现。
        if (!scanFailed)
            removedChanged |= removedOffline.RemoveWhere(id => !seen.Contains(id)) > 0;

        TrimHistory();
        bool periodicFlush = anyFresh && (lastPersistedAt is null || now - lastPersistedAt.Value >= TimeSpan.FromMinutes(5));
        bool shouldSave = meaningfulChange || removedChanged || periodicFlush;
        if (shouldSave)
            lastPersistedAt = now;
        return shouldSave;
    }

    /// <summary>应用心跳或罗技接收器推送的在线状态变化（B1 / B2）。</summary>
    public (bool UiChanged, bool NeedsBatteryRefresh) ApplyPresence(
        IReadOnlyList<PresenceUpdate> updates,
        DateTimeOffset now,
        Action<DeviceIdentity, LinkState?, LinkState>? onLinkChanged = null)
    {
        bool uiChanged = false;
        bool needsRefresh = false;

        foreach (var update in updates)
        {
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
                if (previous.Link == update.Link && previous.Issue == update.Issue)
                    continue;

                bool becameOnline = !previous.IsOnline && update.Link == LinkState.Online;
                bool lostLink = update.Link is LinkState.Offline or LinkState.Asleep;
                var next = previous with
                {
                    Link = update.Link,
                    Issue = update.Issue,
                    Fresh = lostLink ? false : previous.Fresh,
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

    private List<string> FindMatchingDeviceIds(PresenceUpdate update)
    {
        if (update.DeviceId is { Length: > 0 } exact)
            return devices.ContainsKey(exact) ? [exact] : [];

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
                lastSample.Charging != status.Charging ||
                last.ReadAt - lastSample.At >= TimeSpan.FromMinutes(5);
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

    private void UpdateReminder(DeviceStatus device, DateTimeOffset now)
    {
        if (device.Percentage < Settings.LowBatteryThreshold && !device.IsCharging && !device.IsFull)
            reminders.TryAdd(device.Device.Id, new(now));
        else
            reminders.Remove(device.Device.Id);
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

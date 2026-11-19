using Jige.Core;

namespace Jige.Tray;

internal sealed record DeviceStatus(DeviceIdentity Device, LinkState Link, ReadIssue Issue,
    ChargingState Charging, SavedBattery? Last, bool Fresh)
{
    public int? Percentage => Last?.Percentage;
    public bool IsCharging => Fresh && Charging == ChargingState.Charging;
    public bool IsFull => Fresh && (Charging == ChargingState.Full || Percentage == 100);
}

internal sealed class TrayModel
{
    private readonly Dictionary<string, DeviceStatus> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedReminder> reminders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removedOffline = new(StringComparer.OrdinalIgnoreCase);

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
    }

    public TraySettings Settings { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool ScanFailed { get; private set; }
    public int UnknownReceivers { get; private set; }

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
            removedOffline.Add(id);
        }
    }

    public DeviceStatus[] OrderedDevices(bool primaryFirst = true) => devices.Values
        .OrderByDescending(item => primaryFirst && StringComparer.OrdinalIgnoreCase.Equals(item.Device.Id, Settings.PrimaryDeviceId))
        .ThenBy(item => item.Device.Kind)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool IsHidden(string id) => Settings.HiddenDeviceIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public DeviceStatus[] PanelDevices() => OrderedDevices().Where(item => !IsHidden(item.Device.Id)).ToArray();

    public DeviceStatus? PrimaryDevice => PanelDevices().FirstOrDefault();

    public TraySettings ToggleVisibility(string id)
    {
        if (!devices.ContainsKey(id) || StringComparer.OrdinalIgnoreCase.Equals(id, PrimaryDevice?.Device.Id))
            return Settings;
        string[] hidden = IsHidden(id)
            ? Settings.HiddenDeviceIds.Where(item => !StringComparer.OrdinalIgnoreCase.Equals(item, id)).ToArray()
            : [.. Settings.HiddenDeviceIds, id];
        return Settings with { HiddenDeviceIds = hidden };
    }

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

    public bool ApplyPoll(IReadOnlyList<BatteryReading> readings, bool scanFailed, DateTimeOffset now, int unknownReceivers = 0)
    {
        ScanFailed = scanFailed;
        if (!scanFailed || unknownReceivers > 0)
            UnknownReceivers = unknownReceivers;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyFresh = false, removedChanged = false;
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
            var last = devices.GetValueOrDefault(device.Id)?.Last;
            bool fresh = reading is { Link: LinkState.Online, Percentage: not null };
            if (fresh)
            {
                anyFresh = true;
                last = new(device, reading.Percentage!.Value, reading.IsEstimated, reading.ReadAt);
            }
            var status = new DeviceStatus(device, reading.Link, reading.Issue, reading.Charging, last, fresh);
            devices[device.Id] = status;
            if (fresh)
                UpdateReminder(status, now);
        }

        foreach (var id in devices.Keys.Where(id => !seen.Contains(id)).ToArray())
            devices[id] = devices[id] with
            {
                Link = scanFailed ? LinkState.Unknown : LinkState.Offline,
                Issue = ReadIssue.None,
                Fresh = false,
                Charging = ChargingState.Unknown
            };
        if (anyFresh)
            UpdatedAt = now;
        // 移除后已消失的设备无需继续保留标记。再次识别时重新出现。
        if (!scanFailed)
            removedChanged |= removedOffline.RemoveWhere(id => !seen.Contains(id)) > 0;
        return anyFresh || removedChanged;
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

    public TrayState Snapshot()
    {
        var readings = new Dictionary<string, SavedBattery>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, device) in devices)
        {
            if (device.Last is { } last)
                readings[id] = last;
        }

        return new()
        {
            Readings = readings,
            Reminders = new(reminders, StringComparer.OrdinalIgnoreCase),
            RemovedOfflineDevices = [.. removedOffline]
        };
    }
}

using Jige.Core;

namespace Jige.Tray;

internal sealed record DeviceStatus(DeviceIdentity Device, LinkState Link, ReadIssue Issue,
    ChargingState Charging, SavedBattery? Last, bool Fresh)
{
    public int? Percentage => Last?.Percentage;
    public bool IsCharging => Fresh && Charging == ChargingState.Charging;
}

internal sealed class TrayModel
{
    private readonly Dictionary<string, DeviceStatus> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedReminder> reminders = new(StringComparer.OrdinalIgnoreCase);

    public TrayModel(TraySettings settings, TrayState saved)
    {
        Settings = settings;
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
                device.Percentage <= Settings.LowBatteryThreshold)
                reminders[id] = reminder;
        }
    }

    public TraySettings Settings { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool ScanFailed { get; private set; }

    public DeviceStatus[] OrderedDevices() => devices.Values
        .OrderByDescending(item => string.Equals(item.Device.Id, Settings.PrimaryDeviceId, StringComparison.OrdinalIgnoreCase))
        .ThenBy(item => item.Device.Kind)
        .ThenBy(item => item.Device.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

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
            if (device.Percentage > settings.LowBatteryThreshold)
                reminders.Remove(device.Device.Id);
            else if (device.Fresh)
                UpdateReminder(device, now);
        }
    }

    public bool ApplyPoll(IReadOnlyList<BatteryReading> readings, bool scanFailed, DateTimeOffset now)
    {
        UpdatedAt = now;
        ScanFailed = scanFailed;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyFresh = false;
        foreach (var reading in readings)
        {
            var device = reading.Device;
            seen.Add(device.Id);
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
        return anyFresh;
    }

    private void UpdateReminder(DeviceStatus device, DateTimeOffset now)
    {
        if (device.Percentage <= Settings.LowBatteryThreshold)
            reminders.TryAdd(device.Device.Id, new(now));
        else
            reminders.Remove(device.Device.Id);
    }

    public DateTimeOffset? NextReminder => reminders.Values.Min(item => item.Due);

    public DeviceStatus? ReadyReminder(DateTimeOffset now, bool blocked)
    {
        if (blocked || NextReminder is not { } due || due > now)
            return null;
        return OrderedDevices().FirstOrDefault(item =>
            reminders.TryGetValue(item.Device.Id, out var reminder) && reminder.Due <= now);
    }

    public bool IsLow(string id) => reminders.ContainsKey(id);

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
            Reminders = new(reminders, StringComparer.OrdinalIgnoreCase)
        };
    }
}

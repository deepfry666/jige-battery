using System.Runtime.Versioning;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Core.Windows;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class DevicePoll
{
    private readonly SafetyGateStore gates;
    private readonly AdapterRegistry registry;
    private readonly WindowsReceiverTransport transport = new();
    private readonly DeviceTopologyCache topology;
    private readonly BluetoothScanThrottle bluetoothThrottle = new();

    public DevicePoll(SafetyGateStore gates, IReadOnlyDictionary<string, DeviceIdentity> savedLogitechSlots)
    {
        this.gates = gates;
        registry = AdapterRegistry.CreateDefault(savedLogitechSlots, gates);
        topology = new(registry, token => WindowsDeviceDiscovery.ScanAsync(token,
            AdapterRegistry.IsKnownHidReceiver, includeUnknownHid: true));
    }

    public SafetyGateStore Gates => gates;
    public AdapterRegistry Registry => registry;

    public void InvalidateTopology()
    {
        topology.Invalidate();
        bluetoothThrottle.Invalidate();
        registry.ResetPollingCaches();
    }

    public void ResetPollingCaches()
    {
        bluetoothThrottle.Invalidate();
        registry.ResetPollingCaches();
    }

    public async Task<(PollResult Result, DetectedDevice[] PushTargets)> RunAsync(bool forceRescan, bool showBluetooth, CancellationToken token)
    {
        try
        {
            var snapshot = await topology.GetOrScanAsync(forceRescan, token).ConfigureAwait(false);
            var detected = snapshot.Detected.Where(item => showBluetooth ||
                item.Target.Device.Connection != DeviceConnection.Bluetooth).ToArray();
            IReadOnlyList<BluetoothDevice> bluetooth = [];
            bool bluetoothScanFailed = false;
            if (showBluetooth && bluetoothThrottle.ShouldScan(Environment.TickCount64, forceRescan || snapshot.Scanned))
            {
                try
                {
                    var properties = await WindowsDeviceDiscovery.ScanBluetoothAsync(token).ConfigureAwait(false);
                    bluetooth = properties.Devices;
                    bluetoothScanFailed = properties.Warnings.Count > 0;
                }
                catch (TimeoutException)
                {
                    bluetoothScanFailed = true;
                }
                bluetoothThrottle.Record(Environment.TickCount64, bluetooth.Count, bluetoothScanFailed);
            }
            List<BatteryReading> readings = [];
            var liveTargets = detected.Select(item => item.Target.Device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string>? incompleteReceivers = null;
            bool disconnected = false;
            foreach (var device in detected)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // 罗技接收器下可能有多台设备，统一走多设备读取。
                    var batch = await registry.ReadDevicesAsync(device, transport, null, token).ConfigureAwait(false);
                    readings.AddRange(batch);
                    if (device.IsMultiDevice && PollResult.IsIncompleteMultiDeviceBatch(batch, device.Target.Device.Id))
                    {
                        incompleteReceivers ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        incompleteReceivers.Add(device.Target.Device.Id);
                    }
                    if (batch.Any(item => item.Issue == ReadIssue.Disconnected))
                    {
                        disconnected = true;
                        liveTargets.Remove(device.Target.Device.Id);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Desktop.Log("读取设备电量失败", error);
                    readings.Add(new(device.Target.Device, null, false, ChargingState.Unknown,
                        LinkState.Unknown, DateTimeOffset.UtcNow, ReadIssue.IoError));
                }
            }
            if (disconnected)
                topology.Invalidate();

            bool hasHeartbeat = detected.Any(item => item.PresenceKind == PresenceProbeKind.Heartbeat);
            var pushTargets = detected.Where(item => item.PresenceKind == PresenceProbeKind.PushListener).ToArray();
            var result = new PollResult(
                BluetoothBattery.Merge(readings, bluetooth, DateTimeOffset.UtcNow),
                snapshot.Inventory.Warnings.Count > 0 || bluetoothScanFailed,
                snapshot.UnknownCount,
                snapshot.Changes,
                snapshot.Unknowns,
                hasHeartbeat,
                snapshot.Scanned,
                liveTargets,
                Complete: true,
                IncompleteReceivers: incompleteReceivers);
            return (result, pushTargets);
        }
        catch (TimeoutException)
        {
            return (new([], true, Complete: false), []);
        }
    }

    /// <summary>仅刷新刚从休眠/离线唤醒的指定设备，避免牵连其他离线设备与全机蓝牙枚举。</summary>
    public async Task<PollResult> RunTargetedAsync(
        IReadOnlySet<string> deviceIds, bool showBluetooth, CancellationToken token)
    {
        try
        {
            var snapshot = await topology.GetOrScanAsync(forceRefresh: false, token).ConfigureAwait(false);
            var targets = snapshot.Detected
                .Where(item => (showBluetooth || item.Target.Device.Connection != DeviceConnection.Bluetooth) &&
                    MatchesTarget(item, deviceIds))
                .ToArray();

            IReadOnlyList<BluetoothDevice> bluetooth = [];
            if (showBluetooth && deviceIds.Any(id => id.StartsWith("bluetooth:", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var properties = await WindowsDeviceDiscovery.ScanBluetoothAsync(token).ConfigureAwait(false);
                    bluetooth = properties.Devices;
                }
                catch (TimeoutException) { }
            }

            List<BatteryReading> readings = [];
            bool disconnected = false;
            foreach (var device in targets)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var batch = device.PresenceKind == PresenceProbeKind.PushListener && device.IsMultiDevice
                        ? await registry.RefreshSleepingLogitechAsync(device, transport, deviceIds, token).ConfigureAwait(false)
                        : await registry.ReadDevicesAsync(device, transport, null, token).ConfigureAwait(false);
                    readings.AddRange(batch);
                    if (batch.Any(item => item.Issue == ReadIssue.Disconnected))
                        disconnected = true;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Desktop.Log("定向读取设备电量失败", error);
                    readings.Add(new(device.Target.Device, null, false, ChargingState.Unknown,
                        LinkState.Unknown, DateTimeOffset.UtcNow, ReadIssue.IoError));
                }
            }
            if (disconnected)
                topology.Invalidate();

            var merged = BluetoothBattery.Merge(readings, bluetooth, DateTimeOffset.UtcNow)
                .Where(reading => reading.Device.Connection != DeviceConnection.Bluetooth ||
                    deviceIds.Contains(reading.Device.Id) ||
                    (reading.Device.BluetoothContainerKey is { } key && deviceIds.Contains(key)))
                .ToArray();
            return new PollResult(merged, ScanFailed: false, Complete: false);
        }
        catch (TimeoutException)
        {
            return new([], ScanFailed: false, Complete: false);
        }

        static bool MatchesTarget(DetectedDevice item, IReadOnlySet<string> ids)
        {
            string id = item.Target.Device.Id;
            if (ids.Contains(id))
                return true;
            if (item.IsMultiDevice)
                return ids.Any(candidate => candidate.StartsWith($"{id}/", StringComparison.OrdinalIgnoreCase));
            return item.Target.Device.BluetoothContainerKey is { } key && ids.Contains(key);
        }
    }

    /// <summary>轻量在线心跳（B1 / R3）：不重新枚举，仅对支持心跳的非罗技/非 H9 设备发状态查询；可按集合仅查休眠设备。</summary>
    public async Task<PresenceUpdate[]> RunHeartbeatAsync(
        CancellationToken token, IReadOnlySet<string>? onlyDeviceIds = null, bool showBluetooth = true)
    {
        try
        {
            var snapshot = await topology.GetOrScanAsync(forceRefresh: false, token).ConfigureAwait(false);
            List<PresenceUpdate> updates = [];
            List<BatteryReading> hidpp = [];
            bool disconnected = false;
            foreach (var device in snapshot.Detected)
            {
                token.ThrowIfCancellationRequested();
                if (device.PresenceKind == PresenceProbeKind.PushListener && onlyDeviceIds is not null &&
                    (showBluetooth || device.Target.Device.Connection != DeviceConnection.Bluetooth))
                {
                    var readings = await registry.RefreshSleepingLogitechAsync(device, transport, onlyDeviceIds, token)
                        .ConfigureAwait(false);
                    hidpp.AddRange(readings);
                    disconnected |= readings.Any(item => item.Issue == ReadIssue.Disconnected);
                }
                if (device.PresenceKind != PresenceProbeKind.Heartbeat)
                    continue;
                if (onlyDeviceIds is not null && !onlyDeviceIds.Contains(device.Target.Device.Id))
                    continue;
                var update = await registry.CheckPresenceAsync(device, transport, token).ConfigureAwait(false);
                if (update is not null)
                {
                    updates.Add(update);
                    if (update.Issue == ReadIssue.Disconnected)
                        disconnected = true;
                }
            }
            if (disconnected)
                topology.Invalidate();

            if (onlyDeviceIds is not null)
            {
                IReadOnlyList<BluetoothDevice> bluetooth = [];
                if (showBluetooth && onlyDeviceIds.Any(id => id.StartsWith("bluetooth:", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var properties = await WindowsDeviceDiscovery.ScanBluetoothAsync(token).ConfigureAwait(false);
                        bluetooth = properties.Devices;
                    }
                    catch (TimeoutException) { }
                }

                foreach (var reading in BluetoothBattery.Merge(hidpp, bluetooth, DateTimeOffset.UtcNow))
                {
                    bool isBluetooth = reading.Device.Connection == DeviceConnection.Bluetooth;
                    if (!isBluetooth || onlyDeviceIds.Contains(reading.Device.Id))
                        updates.Add(new("", null, reading.Device.Id, reading.Link, reading.Issue) { Battery = reading });
                }
            }
            return [.. updates];
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    /// <summary>被动等待设备上报连接状态或电量。</summary>
    public Task ListenPushAsync(DetectedDevice device, Action<PresenceUpdate> onUpdate, CancellationToken token) =>
        registry.ListenPushAsync(device, transport, onUpdate, token);
}

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

    public DevicePoll(SafetyGateStore gates)
    {
        this.gates = gates;
        registry = AdapterRegistry.CreateDefault(gates: gates);
        topology = new(registry, token => WindowsDeviceDiscovery.ScanAsync(token,
            AdapterRegistry.IsKnownHidReceiver, includeUnknownHid: true));
    }

    public SafetyGateStore Gates => gates;
    public void InvalidateTopology() => topology.Invalidate();

    public async Task<(PollResult Result, DetectedDevice[] PushTargets)> RunAsync(bool forceRescan, CancellationToken token)
    {
        try
        {
            var snapshot = await topology.GetOrScanAsync(forceRescan, token).ConfigureAwait(false);
            var detected = snapshot.Detected;
            List<BatteryReading> readings = [];
            bool disconnected = false;
            foreach (var device in detected)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // 罗技接收器下可能有多台设备，统一走多设备读取。
                    var batch = await registry.ReadDevicesAsync(device, transport, null, token).ConfigureAwait(false);
                    readings.AddRange(batch);
                    if (batch.Any(item => item.Issue == ReadIssue.Disconnected))
                        disconnected = true;
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
                readings,
                snapshot.Inventory.Warnings.Count > 0,
                snapshot.UnknownCount,
                snapshot.Changes,
                snapshot.Unknowns,
                hasHeartbeat,
                snapshot.Scanned);
            return (result, pushTargets);
        }
        catch (TimeoutException)
        {
            return (new([], true), []);
        }
    }

    /// <summary>轻量在线心跳（B1 / R3）：不重新枚举，仅对支持心跳的非罗技/非 H9 设备发状态查询；可按集合仅查休眠设备。</summary>
    public async Task<PresenceUpdate[]> RunHeartbeatAsync(
        CancellationToken token, IReadOnlySet<string>? onlyDeviceIds = null)
    {
        try
        {
            var snapshot = await topology.GetOrScanAsync(forceRefresh: false, token).ConfigureAwait(false);
            List<PresenceUpdate> updates = [];
            bool disconnected = false;
            foreach (var device in snapshot.Detected)
            {
                token.ThrowIfCancellationRequested();
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
            return [.. updates];
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    /// <summary>罗技接收器被动连接/断开推送监听（B2）：仅只读等待输入事件。</summary>
    public Task ListenPushAsync(DetectedDevice device, Action<PresenceUpdate> onUpdate, CancellationToken token) =>
        registry.ListenPushAsync(device, transport, onUpdate, token);
}

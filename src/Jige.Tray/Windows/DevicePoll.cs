using System.Runtime.Versioning;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Core.Windows;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class DevicePoll
{
    private readonly AdapterRegistry registry = AdapterRegistry.CreateDefault();
    private readonly WindowsReceiverTransport transport = new();

    public async Task<PollResult> RunAsync(CancellationToken token)
    {
        try
        {
            // 未收录接收器只读描述符，不读取产品字符串或打开报告通道。
            var inventory = await WindowsDeviceDiscovery.ScanAsync(token,
                AdapterRegistry.IsKnownHidReceiver, includeUnknownHid: true).ConfigureAwait(false);
            var detected = registry.Detect(inventory).ToArray();
            List<BatteryReading> readings = [];
            foreach (var device in detected)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // 罗技接收器下可能有多台设备，统一走多设备读取。
                    readings.AddRange(await registry.ReadDevicesAsync(device, transport, null, token).ConfigureAwait(false));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Desktop.Log("读取设备电量失败", error);
                    readings.Add(new(device.Target.Device, null, false, ChargingState.Unknown,
                        LinkState.Unknown, DateTimeOffset.UtcNow, ReadIssue.IoError));
                }
            }
            return new(readings, inventory.Warnings.Count > 0, PollResult.CountUnknown(inventory, detected));
        }
        catch (TimeoutException)
        {
            return new([], true);
        }
    }
}

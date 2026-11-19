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
            // 只读取已收录接收器的 HID 描述符；合并 ATK/游狼后必须与 AdapterRegistry 保持一致。
            var inventory = await WindowsDeviceDiscovery.ScanAsync(token,
                AdapterRegistry.IsKnownHidReceiver).ConfigureAwait(false);
            List<BatteryReading> readings = [];
            foreach (var device in registry.Detect(inventory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var reading = await registry.ReadAsync(device, transport, null, token).ConfigureAwait(false);
                    readings.Add(reading);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    readings.Add(new(device.Target.Device, null, false, ChargingState.Unknown,
                        LinkState.Unknown, DateTimeOffset.UtcNow, ReadIssue.IoError, error.Message));
                }
            }
            return new(readings, inventory.Warnings.Count > 0);
        }
        catch (TimeoutException)
        {
            return new([], true);
        }
    }
}

internal sealed record PollResult(IReadOnlyList<BatteryReading> Readings, bool ScanFailed);

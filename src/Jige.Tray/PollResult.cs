using Jige.Core;
using Jige.Core.Adapters;

namespace Jige.Tray;

internal sealed record PollResult(
    IReadOnlyList<BatteryReading> Readings,
    bool ScanFailed,
    int UnknownReceivers = 0,
    ReceiverChange[]? ReceiverChanges = null,
    UnknownReceiverInfo[]? Unknowns = null,
    bool HasHeartbeatDevices = false,
    bool Scanned = false,
    IReadOnlySet<string>? LiveTargets = null)
{
    public static int CountUnknown(DeviceInventory inventory, IEnumerable<DetectedDevice> detected)
    {
        var matched = detected.Select(item => item.Target.Endpoint.ReceiverId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return inventory.Hid.Where(hid => hid.UsagePage is >= 0xFF00 && !matched.Contains(hid.ReceiverId))
            .Select(hid => hid.ReceiverId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }
}

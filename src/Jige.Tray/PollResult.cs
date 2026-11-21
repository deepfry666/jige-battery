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
    IReadOnlySet<string>? LiveTargets = null,
    bool Complete = true)
{
    public static int CountUnknown(DeviceInventory inventory, IEnumerable<DetectedDevice> detected)
    {
        var matched = detected.Select(item => item.Target.Endpoint.ReceiverId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return inventory.Hid.Where(hid => DeviceTopologyCache.IsUnknownReceiverCandidate(hid) && !matched.Contains(hid.ReceiverId))
            .Select(hid => hid.ReceiverId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    /// <summary>
    /// 仅当多设备接收器发生接收器级超时失败（返回 LinkState.Unknown + ReadIssue.Timeout）时，才判定整轮扫描不完整；
    /// 单个已配对但休眠/关机的槽位超时（LinkState.Asleep + ReadIssue.Timeout）属于正常读数，不得阻止离线清扫。
    /// </summary>
    public static bool IsIncompleteMultiDeviceBatch(IReadOnlyList<BatteryReading> batch) =>
        batch.Count > 0 && batch.All(item => item.Issue == ReadIssue.Timeout && item.Link == LinkState.Unknown);
}

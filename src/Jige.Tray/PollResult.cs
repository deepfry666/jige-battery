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
    bool Complete = true,
    IReadOnlySet<string>? IncompleteReceivers = null)
{
    public static int CountUnknown(DeviceInventory inventory, IEnumerable<DetectedDevice> detected)
    {
        var matched = detected.Select(item => item.Target.Endpoint.ReceiverId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return inventory.Hid.Where(hid => DeviceTopologyCache.IsUnknownReceiverCandidate(hid) && !matched.Contains(hid.ReceiverId))
            .Select(hid => hid.ReceiverId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    /// <summary>
    /// 仅当多设备接收器产出接收器级超时标记（设备 ID 为接收器自身 + LinkState.Unknown + ReadIssue.Timeout）时，
    /// 才判定该接收器扫描未完成（含先读出部分槽位后耗尽扫描预算的情形）；
    /// 单个已配对但休眠/关机的槽位超时（带槽位后缀且 LinkState.Asleep）不视为扫描不完整。
    /// </summary>
    public static bool IsIncompleteMultiDeviceBatch(IReadOnlyList<BatteryReading> batch, string receiverDeviceId) =>
        batch.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Device.Id, receiverDeviceId) &&
            item.Issue == ReadIssue.Timeout && item.Link == LinkState.Unknown);
}

using System.Text.RegularExpressions;

namespace Jige.Core.Adapters;

/// <summary>接收器插拔变化事件（仅含 VID:PID 与接口数量，不含实例 ID 或路径）。</summary>
public sealed record ReceiverChange(bool Inserted, ushort? VendorId, ushort? ProductId, int CollectionCount);

/// <summary>未识别的厂商自定义 HID 接收器描述（仅含描述符特征，用于单次日志提示）。</summary>
public sealed record UnknownReceiverInfo(
    string Signature, ushort? VendorId, ushort? ProductId, ushort? UsagePage, ushort? Usage,
    ushort InputLength, ushort OutputLength, ushort FeatureLength);

public sealed record TopologySnapshot(
    DeviceInventory Inventory,
    DetectedDevice[] Detected,
    int UnknownCount,
    ReceiverChange[] Changes,
    UnknownReceiverInfo[] Unknowns,
    bool Scanned);

/// <summary>
/// 缓存已枚举的设备拓扑：无插拔或系统恢复事件时直接复用缓存，避免每轮查询全量枚举系统设备。
/// </summary>
public sealed partial class DeviceTopologyCache(
    AdapterRegistry registry,
    Func<CancellationToken, Task<DeviceInventory>> scan)
{
    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SerialVidPidPattern();

    private readonly SemaphoreSlim gate = new(1);
    private DeviceInventory? cachedInventory;
    private DetectedDevice[]? cachedDetected;
    private UnknownReceiverInfo[] cachedUnknowns = [];
    private int cachedUnknownCount;
    private Dictionary<string, (ushort? Vid, ushort? Pid, int Count)>? lastReceivers;
    private int generation = 1;
    private int scannedGeneration;

    public bool IsDirty => Volatile.Read(ref generation) != Volatile.Read(ref scannedGeneration);

    /// <summary>仅把外部 USB/蓝牙 HID 的厂商自定义集合计入未识别接收器，避免笔记本内置 I2C/ACPI 触控板等误报。</summary>
    public static bool IsUnknownReceiverCandidate(HidInterface hid) =>
        hid.UsagePage is >= 0xFF00 && hid.IsExternalReceiverBus;

    /// <summary>标记缓存失效（收到插拔通知、休眠唤醒、设备断开错误或用户点「重新检测」时调用）。</summary>
    public void Invalidate() => Interlocked.Increment(ref generation);

    public async Task<TopologySnapshot> GetOrScanAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int targetGeneration = Volatile.Read(ref generation);
            if (!forceRefresh && targetGeneration == Volatile.Read(ref scannedGeneration) &&
                cachedInventory is not null && cachedDetected is not null)
                return new(cachedInventory, cachedDetected, cachedUnknownCount, [], cachedUnknowns, Scanned: false);

            var inventory = await scan(cancellationToken).ConfigureAwait(false);
            var detected = registry.Detect(inventory).ToArray();
            var matchedReceivers = detected.Select(item => item.Target.Endpoint.ReceiverId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<UnknownReceiverInfo> unknowns = [];
            HashSet<string> unknownReceivers = new(StringComparer.OrdinalIgnoreCase);
            foreach (var hid in inventory.Hid)
            {
                if (!IsUnknownReceiverCandidate(hid) || matchedReceivers.Contains(hid.ReceiverId))
                    continue;
                unknownReceivers.Add(hid.ReceiverId);
                unknowns.Add(new(
                    SafetyGateStore.Signature(hid), hid.VendorId, hid.ProductId, hid.UsagePage, hid.Usage,
                    hid.InputLength ?? 0, hid.OutputLength ?? 0, hid.FeatureLength ?? 0));
            }

            var currentReceivers = CollectReceivers(inventory);
            var changes = DiffReceivers(lastReceivers, currentReceivers);
            lastReceivers = currentReceivers;

            cachedInventory = inventory;
            cachedDetected = detected;
            cachedUnknownCount = unknownReceivers.Count;
            cachedUnknowns = unknowns.DistinctBy(item => item.Signature, StringComparer.OrdinalIgnoreCase).ToArray();
            // 若本次枚举出现警告，不推进代次让下次轮询补救；枚举期间若收到新的 Invalidate()，targetGeneration 也会小于当前 generation。
            if (inventory.Warnings.Count == 0)
                Volatile.Write(ref scannedGeneration, targetGeneration);
            return new(inventory, detected, cachedUnknownCount, changes, cachedUnknowns, Scanned: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static Dictionary<string, (ushort? Vid, ushort? Pid, int Count)> CollectReceivers(DeviceInventory inventory)
    {
        var map = new Dictionary<string, (ushort? Vid, ushort? Pid, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var hid in inventory.Hid.Where(IsUnknownReceiverCandidate))
        {
            if (map.TryGetValue(hid.ReceiverId, out var existing))
                map[hid.ReceiverId] = (existing.Vid ?? hid.VendorId, existing.Pid ?? hid.ProductId, existing.Count + 1);
            else
                map[hid.ReceiverId] = (hid.VendorId, hid.ProductId, 1);
        }
        foreach (var serial in inventory.Serial)
        {
            var (vid, pid) = ParseSerialVidPid(serial.HardwareIds);
            if (vid is null)
                continue;
            if (map.TryGetValue(serial.ReceiverId, out var existing))
                map[serial.ReceiverId] = (existing.Vid ?? vid, existing.Pid ?? pid, existing.Count + 1);
            else
                map[serial.ReceiverId] = (vid, pid, 1);
        }
        return map;
    }

    private static (ushort? Vid, ushort? Pid) ParseSerialVidPid(IReadOnlyList<string> hardwareIds)
    {
        foreach (var id in hardwareIds)
        {
            var match = SerialVidPidPattern().Match(id);
            if (match.Success &&
                ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out ushort vid) &&
                ushort.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out ushort pid))
                return (vid, pid);
        }
        return (null, null);
    }

    private static ReceiverChange[] DiffReceivers(
        Dictionary<string, (ushort? Vid, ushort? Pid, int Count)>? previous,
        Dictionary<string, (ushort? Vid, ushort? Pid, int Count)> current)
    {
        // 首次启动扫描只建基线，不把开机已插着的接收器误报为"插入"。
        if (previous is null)
            return [];

        List<ReceiverChange> changes = [];
        foreach (var (id, info) in current)
        {
            if (!previous.ContainsKey(id))
                changes.Add(new(Inserted: true, info.Vid, info.Pid, info.Count));
        }
        foreach (var (id, info) in previous)
        {
            if (!current.ContainsKey(id))
                changes.Add(new(Inserted: false, info.Vid, info.Pid, info.Count));
        }
        return [.. changes];
    }
}

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

    /// <summary>
    /// 已知不带电池、只用于灯效或设置的外接 HID，不计入未识别设备。
    /// ITE IT5702 / IT5711 / IT8297（技嘉 RGB Fusion 2）、ASUS Aura 主板灯控（0B05:18F3/1939）、
    /// ASRock Polychrome（26CE:01A2），来源均见 OpenRGB 设备表。
    /// </summary>
    private static readonly HashSet<(ushort Vid, ushort Pid)> KnownNonBattery =
    [
        (0x048D, 0x5702), (0x048D, 0x5711), (0x048D, 0x8297),
        (0x0B05, 0x18F3), (0x0B05, 0x1939),
        (0x26CE, 0x01A2)
    ];

    public static bool IsKnownNonBattery(HidInterface hid) =>
        hid.VendorId is { } vid && hid.ProductId is { } pid && KnownNonBattery.Contains((vid, pid));

    /// <summary>
    /// 仅把外部 USB/蓝牙 HID 的厂商自定义集合计入未识别设备，避免笔记本内置 I2C/ACPI 触控板等误报。
    /// 有线键盘、显示器等也带厂商集合，所以计数只表示「没认出来」，不代表一定带电池。
    /// </summary>
    public static bool IsUnknownReceiverCandidate(HidInterface hid) =>
        hid.UsagePage is >= 0xFF00 && hid.IsExternalReceiverBus && !IsKnownNonBattery(hid);

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
                if (!IsUnknownReceiverCandidate(hid) || matchedReceivers.Contains(hid.ReceiverId) ||
                    DeviceCatalog.IsDisabledPath(hid, out _))
                    continue;
                unknownReceivers.Add(hid.ReceiverId);
                unknowns.Add(new(
                    SafetyGateStore.Signature(hid), hid.VendorId, hid.ProductId, hid.UsagePage, hid.Usage,
                    hid.InputLength ?? 0, hid.OutputLength ?? 0, hid.FeatureLength ?? 0));
            }

            ReceiverChange[] changes;
            if (inventory.Warnings.Count == 0)
            {
                var currentReceivers = CollectReceivers(inventory, detected);
                changes = DiffReceivers(lastReceivers, currentReceivers);
                lastReceivers = currentReceivers;
            }
            else
            {
                // 枚举不完整时不覆盖在位基线，避免误报整批接收器拔出/重新插入。
                changes = [];
            }

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

    private static Dictionary<string, (ushort? Vid, ushort? Pid, int Count)> CollectReceivers(
        DeviceInventory inventory, IReadOnlyList<DetectedDevice> detected)
    {
        var map = new Dictionary<string, (ushort? Vid, ushort? Pid, int Count)>(StringComparer.OrdinalIgnoreCase);
        var seenHidPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hid in inventory.Hid.Where(IsUnknownReceiverCandidate))
        {
            seenHidPaths.Add(hid.Path);
            if (map.TryGetValue(hid.ReceiverId, out var existing))
                map[hid.ReceiverId] = (existing.Vid ?? hid.VendorId, existing.Pid ?? hid.ProductId, existing.Count + 1);
            else
                map[hid.ReceiverId] = (hid.VendorId, hid.ProductId, 1);
        }
        // 纳入已支持的非 FF00+ HID 端点（如雷蛇 90B 0001、罗技 G535 000C、Beken 000A），确保其插拔也能被拓扑追踪。
        foreach (var item in detected)
        {
            if (item.Target.Endpoint is HidInterface hid && seenHidPaths.Add(hid.Path))
            {
                if (map.TryGetValue(hid.ReceiverId, out var existing))
                    map[hid.ReceiverId] = (existing.Vid ?? hid.VendorId, existing.Pid ?? hid.ProductId, existing.Count + 1);
                else
                    map[hid.ReceiverId] = (hid.VendorId, hid.ProductId, 1);
            }
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

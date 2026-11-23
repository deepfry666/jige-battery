using System.Globalization;

namespace Jige.Core.Windows;

public static partial class WindowsDeviceDiscovery
{
    private static readonly Native.PropertyKey BluetoothBatteryLevel = new()
    {
        Format = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), Id = 2
    };
    private static readonly Native.PropertyKey IsConnected = new()
    {
        Format = new("83DA6326-97A6-4088-9453-A1923F573B29"), Id = 15
    };
    private static readonly Native.PropertyKey FriendlyName = new()
    {
        Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14
    };
    private static readonly Native.PropertyKey DeviceDescription = new()
    {
        Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 2
    };

    private static readonly Native.PropertyKey DeviceCategory = new()
    {
        Format = new("78C34FC8-104A-4ACA-9EA4-524D52996E57"), Id = 90
    };

    // 查询 Windows 本机缓存，不发起蓝牙扫描、不打开设备连接。
    public static Task<(IReadOnlyList<BluetoothDevice> Devices, IReadOnlyList<string> Warnings)> ScanBluetoothAsync(
        CancellationToken cancellationToken) =>
        OperationDeadline.RunAsync(token => Task.FromResult(ScanBluetooth(token)), TimeSpan.FromSeconds(5), cancellationToken);

    private static readonly string[] BluetoothEnumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE"];

    private static unsafe (IReadOnlyList<BluetoothDevice>, IReadOnlyList<string>) ScanBluetooth(CancellationToken token)
    {
        const uint present = 2, allClasses = 4;
        List<string> warnings = [];
        List<BluetoothDevice> nodes = [];

        foreach (string enumerator in BluetoothEnumerators)
        {
            token.ThrowIfCancellationRequested();
            nint set = Native.GetAllClassDevs(0, enumerator, 0, present | allClasses);
            if (set == -1)
            {
                warnings.Add(WindowsErrors.Last($"枚举 Windows 蓝牙设备（{enumerator}）").Message);
                continue;
            }
            try
            {
                for (uint index = 0; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = new Native.DeviceData { Size = (uint)sizeof(Native.DeviceData) };
                    if (Native.EnumDevices(set, index, ref node) == 0)
                    {
                        AddEnumerationError(warnings, $"枚举 Windows 蓝牙设备（{enumerator}）");
                        break;
                    }

                    string instance = ReadInstanceId(node.DevInst);
                    ulong? address = ParseBluetoothAddress(instance);
                    byte? level = ReadByte(set, node, BluetoothBatteryLevel, 0x03);
                    if (address is null && level is null)
                        continue;
                    const uint hasProblem = 0x400;
                    if (level is > 100 || Native.GetDevNodeStatus(out uint status, out _, node.DevInst, 0) == 0 &&
                        (status & hasProblem) != 0)
                        level = null;

                    string container = ReadReceiverId(set, node, instance, instance);
                    bool? connected = ReadByte(set, node, IsConnected, 0x11) switch { 0 => false, 0xFF => true, _ => null };
                    var name = ReadDeviceString(set, node, FriendlyName) ?? ReadDeviceString(set, node, DeviceDescription);
                    var kind = ReadDeviceCategoryKind(set, node);
                    bool mainNode = instance.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
                        instance.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);
                    nodes.Add(new(container, name ?? "", kind, connected, level)
                    {
                        Address = address ?? FindBluetoothAddress(node.DevInst),
                        MainNode = mainNode
                    });
                }
            }
            finally
            {
                Native.DestroyDeviceInfoList(set);
            }
        }

        var byContainer = nodes.GroupBy(node => node.Container, StringComparer.OrdinalIgnoreCase)
            .Select(group => CombineBluetoothNodes(group)).ToArray();
        var bluetooth = byContainer.GroupBy(device => device.Address is ulong address ? $"bt:{address:X12}" : device.Container,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => CombineBluetoothNodes(group)).ToArray();
        return (bluetooth, warnings);
    }

    internal static BluetoothDevice CombineBluetoothNodes(IEnumerable<BluetoothDevice> nodes)
    {
        var ordered = nodes.OrderByDescending(node => node.MainNode).ThenByDescending(node => node.Connected == true).ToArray();
        var main = ordered[0];
        bool? connected = Linked(ordered.Where(node => node.MainNode)) ?? Linked(ordered);
        int? level = ordered.Where(node => node.Percentage is not null)
            .OrderByDescending(node => node.Connected == true)
            .ThenByDescending(node => node.MainNode)
            .FirstOrDefault()?.Percentage;
        return main with
        {
            Name = ordered.FirstOrDefault(node => !string.IsNullOrWhiteSpace(node.Name))?.Name ?? "蓝牙设备",
            Address = ordered.FirstOrDefault(node => node.Address.HasValue)?.Address,
            Kind = ordered.FirstOrDefault(node => node.Kind != DeviceKind.Other)?.Kind ?? DeviceKind.Other,
            Connected = connected,
            Percentage = level
        };

        static bool? Linked(IEnumerable<BluetoothDevice> candidates) =>
            candidates.Any(node => node.Connected == true) ? true :
            candidates.Any(node => node.Connected == false) ? false : null;
    }

    private static unsafe byte? ReadByte(nint set, Native.DeviceData node, Native.PropertyKey key, uint expectedType)
    {
        byte value;
        return Native.GetDeviceProperty(set, node, key, out uint type, &value, 1, out uint size, 0) != 0 &&
            type == expectedType && size == 1 ? value : null;
    }

    private static unsafe DeviceKind ReadDeviceCategoryKind(nint set, Native.DeviceData node)
    {
        char* value = stackalloc char[256];
        if (Native.GetDeviceProperty(set, node, DeviceCategory, out uint type, value, 512, out uint size, 0) == 0 ||
            (type != 0x12 && type != 0x2012) || size is < 4 or > 512 || size % 2 != 0 || value[(size / 2) - 1] != '\0')
            return DeviceKind.Other;
        var span = new ReadOnlySpan<char>(value, (int)(size / 2));
        int pos = 0;
        while (pos < span.Length)
        {
            int end = span[pos..].IndexOf('\0');
            if (end <= 0)
                break;
            var kind = BluetoothBattery.KindFromCategory(new string(span.Slice(pos, end)));
            if (kind != DeviceKind.Other)
                return kind;
            pos += end + 1;
        }
        return DeviceKind.Other;
    }

    private static unsafe string? ReadDeviceString(nint set, Native.DeviceData node, Native.PropertyKey key,
        bool allowList = false)
    {
        char* value = stackalloc char[256];
        return Native.GetDeviceProperty(set, node, key, out uint type, value, 512, out uint size, 0) != 0 &&
            (type == 0x12 || allowList && type == 0x2012) && size is >= 4 and <= 512 && size % 2 == 0 && value[(size / 2) - 1] == '\0'
            ? new string(value) : null;
    }

    internal static ulong? ParseBluetoothAddress(string instance)
    {
        int offset = instance.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ? 12 :
            instance.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase) ? 10 : -1;
        if (offset >= 0 && instance.Length > offset + 12 && instance[offset + 12] == '\\')
        {
            return ulong.TryParse(instance.AsSpan(offset, 12), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                out ulong address) && address != 0 ? address : null;
        }
        // 经典蓝牙服务节点（如 HFP 电量服务 BTHENUM\{0000111E-...}\...&<12hex>_C00000000）
        if (instance.StartsWith(@"BTHENUM\{", StringComparison.OrdinalIgnoreCase))
        {
            int amp = instance.LastIndexOf('&');
            if (amp >= 0 && amp + 13 <= instance.Length)
            {
                int tail = amp + 13;
                if (tail == instance.Length || instance[tail] is '_' or '\\')
                {
                    if (ulong.TryParse(instance.AsSpan(amp + 1, 12), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out ulong serviceAddr) && serviceAddr != 0)
                        return serviceAddr;
                }
            }
        }
        return null;
    }

    private static ulong? FindBluetoothAddress(IReadOnlyList<string> ancestors)
    {
        foreach (string ancestor in ancestors)
        {
            if (ParseBluetoothAddress(ancestor) is ulong address)
                return address;
        }
        return null;
    }

    private static ulong? FindBluetoothAddress(uint devInst)
    {
        HashSet<uint> visited = [devInst];
        while (Native.GetParent(out uint parent, devInst, 0) == 0 && visited.Add(parent))
        {
            if (ParseBluetoothAddress(ReadInstanceId(parent)) is ulong address)
                return address;
            devInst = parent;
        }
        return null;
    }
}

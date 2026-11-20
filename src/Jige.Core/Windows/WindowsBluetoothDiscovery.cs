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

    private static unsafe (IReadOnlyList<BluetoothDevice>, IReadOnlyList<string>) ScanBluetooth(CancellationToken token)
    {
        const uint present = 2, allClasses = 4;
        List<string> warnings = [];
        nint set = Native.GetAllClassDevs(0, 0, 0, present | allClasses);
        if (set == -1)
            return ([], [WindowsErrors.Last("枚举 Windows 蓝牙设备").Message]);

        List<BluetoothDevice> nodes = [];
        try
        {
            for (uint index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                var node = new Native.DeviceData { Size = (uint)sizeof(Native.DeviceData) };
                if (Native.EnumDevices(set, index, ref node) == 0)
                {
                    AddEnumerationError(warnings, "枚举 Windows 蓝牙设备");
                    break;
                }

                string instance = ReadInstanceId(node.DevInst);
                if (!(instance.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) ||
                    instance.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase) ||
                    instance.StartsWith(@"BTHLEDEVICE\", StringComparison.OrdinalIgnoreCase)))
                    continue;

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
                var category = ReadDeviceString(set, node, DeviceCategory, allowList: true);
                nodes.Add(new(container, name ?? "", BluetoothBattery.KindFromCategory(category), connected, level)
                {
                    Address = address ?? FindBluetoothAddress(node.DevInst),
                    MainNode = address.HasValue
                });
            }
        }
        finally
        {
            Native.DestroyDeviceInfoList(set);
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
        if (offset < 0 || instance.Length <= offset + 12 || instance[offset + 12] != '\\')
            return null;
        return ulong.TryParse(instance.AsSpan(offset, 12), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
            out ulong address) && address != 0 ? address : null;
    }

    private static ulong? FindBluetoothAddress(uint devInst)
    {
        while (Native.GetParent(out uint parent, devInst, 0) == 0 && parent != devInst)
        {
            if (ParseBluetoothAddress(ReadInstanceId(parent)) is ulong address)
                return address;
            devInst = parent;
        }
        return null;
    }
}

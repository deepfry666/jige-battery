using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jige.Core.Adapters;

namespace Jige.Core.Windows;

[SupportedOSPlatform("windows")]
public static partial class WindowsDeviceDiscovery
{
    private static readonly SemaphoreSlim ScanGate = new(1);
    private static readonly Guid ComInterface = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");
    private static readonly Guid PortsClass = new("4D36E978-E325-11CE-BFC1-08002BE10318");
    private static readonly Native.PropertyKey ContainerId = new()
    {
        Format = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        Id = 2
    };

    public static Task<DeviceInventory> ScanAsync(
        CancellationToken cancellationToken, Func<ushort?, ushort?, bool>? inspectHid = null, bool includeUnknownHid = false,
        bool includeBluetooth = false, bool includeReportIds = false) =>
        OperationDeadline.RunAsync(async token =>
        {
            await ScanGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var inventory = Scan(inspectHid, includeUnknownHid, includeReportIds, token);
                if (!includeBluetooth)
                    return inventory;
                var (bluetooth, warnings) = ScanBluetooth(token);
                return inventory with { Bluetooth = bluetooth, Warnings = [.. inventory.Warnings, .. warnings] };
            }
            finally
            {
                ScanGate.Release();
            }
        }, TimeSpan.FromSeconds(10), cancellationToken);

    private static unsafe DeviceInventory Scan(
        Func<ushort?, ushort?, bool>? inspectHid, bool includeUnknownHid, bool includeReportIds, CancellationToken token)
    {
        List<HidInterface> hid = [];
        List<UsbSerialPort> serial = [];
        List<string> warnings = [];
        HashSet<uint> serialNodes = [];
        Native.GetHidGuid(out var hidClass);

        ScanInterfaces(hidClass, (set, node, path) =>
        {
            var nodeId = ReadInstanceId(node.DevInst);
            var vid = ReadHexTag(path, "VID_", 4) ?? ReadHexTag(nodeId, "VID_", 4) ??
                BluetoothHidPath.UsbVendorId(path) ?? BluetoothHidPath.UsbVendorId(nodeId);
            var pid = ReadHexTag(path, "PID_", 4) ?? ReadHexTag(nodeId, "PID_", 4) ??
                BluetoothHidPath.ProductId(path) ?? BluetoothHidPath.ProductId(nodeId);
            // 三个 VID 都有无关设备，日常轮询时避免为未收录 PID 打开 HID 元数据句柄；
            // 导出诊断（includeUnknownHid）时照常列出，便于用户反馈尚未支持的设备。
            if (!includeUnknownHid && DomesticMouseStatus.IsFamily(vid))
            {
                var route = DomesticMouseStatus.Find(vid, pid);
                var mi = ReadHexTag(path, "MI_", 2) ?? ReadHexTag(nodeId, "MI_", 2);
                if (route is null || route.InterfaceNumber is byte required && mi != required)
                    return;
            }
            bool known = inspectHid is null || inspectHid(vid, pid);
            if ((known || includeUnknownHid) &&
                InspectHid(set, node, path, nodeId, vid, pid, known, includeReportIds, token) is { } endpoint)
                hid.Add(endpoint);
        }, warnings, token);

        ScanInterfaces(ComInterface, (set, node, path) =>
        {
            var port = InspectSerial(set, node, path);
            if (port is not null && serialNodes.Add(node.DevInst))
                serial.Add(port);
        }, warnings, token);

        // 较旧的 USB 串口驱动可能只注册 Ports 类设备节点。
        nint ports = Native.GetClassDevs(PortsClass, 0, 0, 0x02);
        if (ports == -1)
            warnings.Add(WindowsErrors.Last("枚举 Ports 类").Message);
        else
        {
            try
            {
                for (uint index = 0; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = new Native.DeviceData { Size = (uint)sizeof(Native.DeviceData) };
                    if (Native.EnumDevices(ports, index, ref node) == 0)
                    {
                        AddEnumerationError(warnings, "枚举串口节点");
                        break;
                    }

                    if (!serialNodes.Contains(node.DevInst) && InspectSerial(ports, node, null) is { } port)
                        serial.Add(port);
                }
            }
            finally
            {
                Native.DestroyDeviceInfoList(ports);
            }
        }

        return new(hid, serial, warnings);
    }

    private static unsafe void ScanInterfaces(Guid classId, Action<nint, Native.DeviceData, string> add,
        List<string> warnings, CancellationToken token)
    {
        nint set = Native.GetClassDevs(classId, 0, 0, 0x12);
        if (set == -1)
        {
            warnings.Add(WindowsErrors.Last($"枚举接口类 {classId}").Message);
            return;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                var info = new Native.InterfaceData { Size = (uint)sizeof(Native.InterfaceData) };
                if (Native.EnumInterfaces(set, 0, classId, index, ref info) == 0)
                {
                    AddEnumerationError(warnings, $"枚举接口类 {classId}");
                    break;
                }

                var node = new Native.DeviceData { Size = (uint)sizeof(Native.DeviceData) };
                const uint maxDetailBytes = 64 * 1024;
                if (Native.GetInterfaceDetail(set, in info, null, 0, out uint size, null) == 0 &&
                    Marshal.GetLastPInvokeError() != 122)
                {
                    warnings.Add(WindowsErrors.Last("读取设备路径长度").Message);
                    continue;
                }
                if (size < 8 || size > maxDetailBytes)
                {
                    warnings.Add(WindowsErrors.Last("读取设备路径长度").Message);
                    continue;
                }

                var buffer = new byte[size];
                fixed (byte* detail = buffer)
                {
                    // Unicode 结构的路径从偏移 4 开始，cbSize 按进程位数填写。
                    *(uint*)detail = IntPtr.Size == 8 ? 8u : 6u;
                    if (Native.GetInterfaceDetail(set, in info, detail, size, out _, &node) != 0)
                    {
                        add(set, node, new string((char*)(detail + 4)));
                        continue;
                    }
                    warnings.Add(WindowsErrors.Last("读取设备路径").Message);
                }
            }
        }
        finally
        {
            Native.DestroyDeviceInfoList(set);
        }
    }

    private static void AddEnumerationError(List<string> warnings, string operation)
    {
        int error = Marshal.GetLastPInvokeError();
        if (error != 259)
            warnings.Add(WindowsErrors.FromCode(error, operation).Message);
    }

    private static unsafe HidInterface? InspectHid(nint set, Native.DeviceData node, string path,
        string nodeId, ushort? vid, ushort? pid, bool readProduct, bool includeReportIds, CancellationToken token)
    {
        var number = ReadHexTag(path, "MI_", 2) ?? ReadHexTag(nodeId, "MI_", 2);
        byte? interfaceNumber = number.HasValue ? (byte)number.Value : null;
        string? product = null;
        try
        {
            using var handle = WindowsReceiverTransport.OpenMetadata(path);
            token.ThrowIfCancellationRequested();
            var caps = WindowsReceiverTransport.ReadCaps(handle);
            if (!readProduct && caps.UsagePage < 0xFF00)
                return null;
            var attributes = WindowsReceiverTransport.ReadAttributes(handle);
            vid = attributes.VendorId;
            pid = attributes.ProductId;
            (string? inputIds, string? outputIds, string? featureIds) = (null, null, null);
            if (DomesticMouseStatus.Find(vid, pid) is { } route)
            {
                if (!route.MatchesCollection(caps.UsagePage, caps.Usage, interfaceNumber, caps.InputLength, caps.FeatureLength))
                    return null;
                int type = route.Feature ? 2 : 0;
                ushort length = route.Feature ? caps.FeatureLength : caps.InputLength;
                if (!WindowsReceiverTransport.DeclaresReportId(handle, type, route.ReportId, length))
                    return null;
                if (route.Feature)
                    featureIds = $"{route.ReportId:X2}";
                else
                    inputIds = $"{route.ReportId:X2}";
            }
            else if (includeReportIds &&
                (caps.UsagePage >= 0xFF00 || vid == 0x1532 && caps.FeatureLength == RazerFeatureProtocol.ReportLength))
            {
                (inputIds, outputIds, featureIds) = WindowsReceiverTransport.ReadDeclaredReportIds(handle, caps);
            }
            if (readProduct && (caps.UsagePage >= 0xFF00 || vid == 0x046D && pid == 0x0AC4))
            {
                char* chars = stackalloc char[126];
                if (Native.GetProductString(handle, chars, 252) != 0 && chars[0] != '\0')
                    product = new string(chars);
            }
            token.ThrowIfCancellationRequested();
            var ancestors = ReadAncestors(node.DevInst);
            var bus = HidBusClassifier.Classify(path, nodeId, ancestors);
            var receiver = ReadReceiverId(set, node, nodeId, path, ancestors);
            return new(receiver, path, vid, pid, product, caps.UsagePage, caps.Usage,
                caps.InputLength, caps.OutputLength, caps.FeatureLength, interfaceNumber)
            {
                FirmwareVersion = attributes.Version,
                InputReportIds = inputIds,
                OutputReportIds = outputIds,
                FeatureReportIds = featureIds,
                Bus = bus,
                BluetoothHid = bus == HidBus.Bluetooth,
                BluetoothAddress = vid == 0x046D && caps is { UsagePage: 0xFF43, Usage: 0x0202 }
                    ? FindBluetoothAddress(node.DevInst) : null
            };
        }
        catch (TransportException error)
        {
            if (!readProduct)
                return null;
            var ancestors = ReadAncestors(node.DevInst);
            var bus = HidBusClassifier.Classify(path, nodeId, ancestors);
            return new(ReadReceiverId(set, node, nodeId, path, ancestors), path, vid, pid, product,
                null, null, null, null, null, interfaceNumber, error.Issue, error.Message)
            {
                Bus = bus,
                BluetoothHid = bus == HidBus.Bluetooth
            };
        }
    }

    private static UsbSerialPort? InspectSerial(nint set, Native.DeviceData node, string? path)
    {
        var ids = ReadHardwareIds(set, node);
        var nodeId = ReadInstanceId(node.DevInst);
        if (!ids.Any(IsUsb) && !IsUsb(nodeId) && FindUsbAncestor(node.DevInst) is null)
            return null;
        var port = ReadPortName(set, node);
        if (path is null && port is not null)
            path = @"\\.\" + port;
        string? error = port is null ? "设备未公布有效的 COM 号" : null;
        return new(ReadReceiverId(set, node, nodeId, path ?? nodeId), path ?? "", port, ids, error);
    }

    private static unsafe string[] ReadHardwareIds(nint set, Native.DeviceData node)
    {
        const uint hardwareId = 1;
        const uint maxPropertyBytes = 64 * 1024;
        if (Native.GetRegistryProperty(set, node, hardwareId, out _, null, 0, out uint size) == 0 &&
            Marshal.GetLastPInvokeError() != 122)
            return [];
        if (size < 2 || size > maxPropertyBytes)
            return [];
        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (Native.GetRegistryProperty(set, node, hardwareId, out uint type, data, size, out uint written) == 0 ||
                type != 7 || written > size)
                return [];
            return new string((char*)data, 0, (int)(written / 2)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private static unsafe string? ReadPortName(nint set, Native.DeviceData node)
    {
        using var key = Native.OpenDeviceKey(set, node, 1, 0, 1, 1);
        if (key.IsInvalid)
            return null;
        char* buffer = stackalloc char[32];
        uint size = 64;
        if (Native.GetRegistryValue(key, null, "PortName", 2, out _, buffer, ref size) != 0)
            return null;
        var port = new string(buffer);
        return port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && port.Length > 3 &&
            port.AsSpan(3).IndexOfAnyExceptInRange('0', '9') == -1 ? port.ToUpperInvariant() : null;
    }

    private static unsafe string ReadReceiverId(nint set, Native.DeviceData node, string nodeId, string fallback,
        IReadOnlyList<string>? ancestors = null)
    {
        Guid container;
        if (Native.GetDeviceProperty(set, node, ContainerId, out uint type, &container, 16, out uint size, 0) != 0 &&
            type == 13 && size == 16 && container != Guid.Empty &&
            container != new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF"))
            return container.ToString("D");
        return HidBusClassifier.ResolveFallbackReceiverId(nodeId, fallback, ancestors ?? ReadAncestors(node.DevInst));
    }

    private static List<string> ReadAncestors(uint devInst)
    {
        List<string> ancestors = [];
        HashSet<uint> visited = [devInst];
        while (Native.GetParent(out uint parent, devInst, 0) == 0 && visited.Add(parent))
        {
            string id = ReadInstanceId(parent);
            if (id.Length > 0)
                ancestors.Add(id);
            devInst = parent;
        }
        return ancestors;
    }

    private static string? FindUsbAncestor(uint devInst) =>
        HidBusClassifier.FindUsbAncestor(ReadAncestors(devInst));

    private static bool IsUsb(string instance) => HidBusClassifier.IsUsb(instance);

    private static unsafe string ReadInstanceId(uint devInst)
    {
        char* buffer = stackalloc char[256];
        return Native.GetDeviceId(devInst, buffer, 256, 0) == 0 ? new string(buffer) : "";
    }

    private static ushort? ReadHexTag(string text, string tag, int digits) =>
        HidBusClassifier.ReadHexTag(text, tag, digits);
}

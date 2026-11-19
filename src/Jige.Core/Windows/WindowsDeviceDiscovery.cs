using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jige.Core.Adapters;

namespace Jige.Core.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsDeviceDiscovery
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
        CancellationToken cancellationToken, Func<ushort?, ushort?, bool>? inspectHid = null, bool includeUnknownHid = false) =>
        OperationDeadline.RunAsync(async token =>
        {
            await ScanGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return Scan(inspectHid, includeUnknownHid, token);
            }
            finally
            {
                ScanGate.Release();
            }
        }, TimeSpan.FromSeconds(10), cancellationToken);

    private static unsafe DeviceInventory Scan(Func<ushort?, ushort?, bool>? inspectHid, bool includeUnknownHid, CancellationToken token)
    {
        List<HidInterface> hid = [];
        List<UsbSerialPort> serial = [];
        List<string> warnings = [];
        HashSet<uint> serialNodes = [];
        Native.GetHidGuid(out var hidClass);

        ScanInterfaces(hidClass, (set, node, path) =>
        {
            var nodeId = ReadInstanceId(node.DevInst);
            var vid = ReadHexTag(path, "VID_", 4) ?? ReadHexTag(nodeId, "VID_", 4);
            var pid = ReadHexTag(path, "PID_", 4) ?? ReadHexTag(nodeId, "PID_", 4);
            bool known = inspectHid is null || inspectHid(vid, pid);
            if ((known || includeUnknownHid) &&
                InspectHid(set, node, path, nodeId, vid, pid, known, token) is { } endpoint)
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
                Native.GetInterfaceDetail(set, in info, null, 0, out uint size, null);
                if (size < 8)
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
        string nodeId, ushort? vid, ushort? pid, bool readProduct, CancellationToken token)
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
            if (readProduct && caps.UsagePage >= 0xFF00)
            {
                char* chars = stackalloc char[126];
                if (Native.GetProductString(handle, chars, 252) != 0 && chars[0] != '\0')
                    product = new string(chars);
            }
            token.ThrowIfCancellationRequested();
            var receiver = ReadReceiverId(set, node, nodeId, path);
            return new(receiver, path, vid, pid, product, caps.UsagePage, caps.Usage,
                caps.InputLength, caps.OutputLength, caps.FeatureLength, interfaceNumber);
        }
        catch (TransportException error)
        {
            return readProduct ? new(ReadReceiverId(set, node, nodeId, path), path, vid, pid, product,
                null, null, null, null, null, interfaceNumber, error.Issue, error.Message) : null;
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
        Native.GetRegistryProperty(set, node, hardwareId, out _, null, 0, out uint size);
        if (size < 2)
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

    private static unsafe string ReadReceiverId(nint set, Native.DeviceData node, string nodeId, string fallback)
    {
        Guid container;
        if (Native.GetDeviceProperty(set, node, ContainerId, out uint type, &container, 16, out uint size, 0) != 0 &&
            type == 13 && size == 16 && container != Guid.Empty &&
            container != new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF"))
            return container.ToString("D");
        if (IsUsb(nodeId) && ReadHexTag(nodeId, "MI_", 2) is null)
            return nodeId;
        return FindUsbAncestor(node.DevInst) ?? (nodeId.Length == 0 ? fallback : nodeId);
    }

    private static string? FindUsbAncestor(uint devInst)
    {
        while (Native.GetParent(out uint parent, devInst, 0) == 0 && parent != devInst)
        {
            var id = ReadInstanceId(parent);
            if (IsUsb(id))
            {
                if (ReadHexTag(id, "MI_", 2) is null)
                    return id;
                if (Native.GetParent(out uint composite, parent, 0) == 0)
                {
                    var compositeId = ReadInstanceId(composite);
                    if (IsUsb(compositeId) && ReadHexTag(compositeId, "VID_", 4) == ReadHexTag(id, "VID_", 4) &&
                        ReadHexTag(compositeId, "PID_", 4) == ReadHexTag(id, "PID_", 4))
                        return compositeId;
                }
                return id;
            }
            devInst = parent;
        }
        return null;
    }

    private static bool IsUsb(string instance) => instance.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase);

    private static unsafe string ReadInstanceId(uint devInst)
    {
        char* buffer = stackalloc char[200];
        return Native.GetDeviceId(devInst, buffer, 200, 0) == 0 ? new string(buffer) : "";
    }

    private static ushort? ReadHexTag(string text, string tag, int digits)
    {
        int offset = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
        if (offset < 0 || text.Length < offset + tag.Length + digits)
            return null;
        return ushort.TryParse(text.AsSpan(offset + tag.Length, digits),
            System.Globalization.NumberStyles.AllowHexSpecifier, null, out ushort value) ? value : null;
    }
}

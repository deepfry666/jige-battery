using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Jige.Core.Windows;

internal static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct InterfaceData
    {
        public uint Size;
        public Guid Class;
        public uint Flags;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceData
    {
        public uint Size;
        public Guid Class;
        public uint DevInst;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidAttributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputLength;
        public ushort OutputLength;
        public ushort FeatureLength;
        private fixed ushort reserved[17];
        private fixed ushort counts[10];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dcb
    {
        public uint Length;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLimit;
        public ushort XoffLimit;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EventChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommTimeouts
    {
        public uint ReadInterval;
        public uint ReadMultiplier;
        public uint ReadConstant;
        public uint WriteMultiplier;
        public uint WriteConstant;
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    internal static partial nint GetClassDevs(in Guid classGuid, nint enumerator, nint parent, uint flags);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiDestroyDeviceInfoList")]
    internal static partial int DestroyDeviceInfoList(nint set);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInterfaces", SetLastError = true)]
    internal static partial int EnumInterfaces(nint set, nint device, in Guid classGuid, uint index, ref InterfaceData data);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInfo", SetLastError = true)]
    internal static partial int EnumDevices(nint set, uint index, ref DeviceData data);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    internal static partial int GetInterfaceDetail(nint set, in InterfaceData data, byte* detail, uint size,
        out uint requiredSize, DeviceData* device);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    internal static partial int GetRegistryProperty(nint set, in DeviceData data, uint property, out uint type,
        byte* buffer, uint size, out uint requiredSize);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    internal static partial int GetDeviceProperty(nint set, in DeviceData data, in PropertyKey property, out uint type,
        void* buffer, uint size, out uint requiredSize, uint flags);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiOpenDevRegKey", SetLastError = true)]
    internal static partial SafeRegistryHandle OpenDeviceKey(
        nint set, in DeviceData data, uint scope, uint profile, uint keyType, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "RegGetValueW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetRegistryValue(
        SafeRegistryHandle key, string? subKey, string value, uint flags, out uint type, char* data, ref uint size);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Parent")]
    internal static partial uint GetParent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW")]
    internal static partial uint GetDeviceId(uint devInst, char* buffer, uint length, uint flags);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetHidGuid")]
    internal static partial void GetHidGuid(out Guid classGuid);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetAttributes", SetLastError = true)]
    internal static partial byte GetHidAttributes(SafeFileHandle file, ref HidAttributes attributes);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetPreparsedData", SetLastError = true)]
    internal static partial byte GetPreparsedData(SafeFileHandle file, out nint data);

    [LibraryImport("hid.dll", EntryPoint = "HidD_FreePreparsedData")]
    internal static partial byte FreePreparsedData(nint data);

    [LibraryImport("hid.dll", EntryPoint = "HidP_GetCaps")]
    internal static partial int GetHidCaps(nint data, out HidCaps caps);

    [LibraryImport("hid.dll", EntryPoint = "HidP_InitializeReportForID")]
    internal static partial int InitializeReportForId(int reportType, byte id, nint data, byte* report, uint length);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetProductString", SetLastError = true)]
    internal static partial byte GetProductString(SafeFileHandle file, char* data, uint length);

    [LibraryImport("hid.dll", EntryPoint = "HidD_SetFeature", SetLastError = true)]
    internal static partial byte SetFeature(SafeFileHandle file, byte* data, uint length);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetFeature", SetLastError = true)]
    internal static partial byte GetFeature(SafeFileHandle file, byte* data, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string path, uint access, uint sharing, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int ReadFile(
        SafeFileHandle file, byte* buffer, uint count, nint transferred, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int WriteFile(
        SafeFileHandle file, byte* buffer, uint count, nint transferred, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll")]
    internal static partial int CancelIoEx(SafeFileHandle file, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int GetCommState(SafeFileHandle file, ref Dcb settings);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int SetCommState(SafeFileHandle file, in Dcb settings);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int SetCommTimeouts(SafeFileHandle file, in CommTimeouts timeouts);
}

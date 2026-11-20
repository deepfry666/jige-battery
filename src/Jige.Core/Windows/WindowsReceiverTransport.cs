using System.Runtime.Versioning;
using Jige.Core.Adapters;
using Microsoft.Win32.SafeHandles;

namespace Jige.Core.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsReceiverTransport : IReceiverTransport
{
    private const uint ReadWrite = 0xC0000000;
    private const uint ShareReadWrite = 3;
    private const uint Overlapped = 0x40000000;

    internal static SafeFileHandle OpenMetadata(string path) => Open(path, 0, ShareReadWrite, 0, false);

    public IHidSession OpenHid(HidInterface endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 仅使用 Feature 报告的集合（如雷蛇 90B 在标准鼠标/键盘页 0x0001 上的 91 字节特征通道）
        // 在 Windows 上须以零访问权限（dwDesiredAccess = 0）打开，才能避开 mouclass/kbdclass 的独占锁，
        // 且物理上无法调用 ReadFile 读取任何键鼠输入。
        uint access = endpoint.FeatureOnly && endpoint.UsagePage is < 0xFF00
            ? 0u
            : endpoint.InputOnly ? 0x80000000u : ReadWrite;
        uint flags = endpoint.FeatureOnly ? 0u : Overlapped;
        SafeFileHandle handle;
        try
        {
            handle = Open(endpoint.Path, access, ShareReadWrite, flags, false);
        }
        catch (TransportException error) when (endpoint.FeatureOnly && access != 0 && error.Issue == ReadIssue.AccessDenied)
        {
            handle = Open(endpoint.Path, 0, ShareReadWrite, 0, false);
        }
        try
        {
            var attributes = ReadAttributes(handle);
            var caps = ReadCaps(handle, endpoint);
            if (attributes.VendorId != endpoint.VendorId || attributes.ProductId != endpoint.ProductId ||
                caps.UsagePage != endpoint.UsagePage || caps.Usage != endpoint.Usage ||
                caps.InputLength != endpoint.InputLength || caps.OutputLength != endpoint.OutputLength ||
                caps.FeatureLength != endpoint.FeatureLength)
                throw new TransportException(ReadIssue.Disconnected, "HID 接口已变更，请重新枚举");
            cancellationToken.ThrowIfCancellationRequested();
            return new HidSession(handle, caps, endpoint.FeatureOnly);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = Open(endpoint.Path, ReadWrite, 0, Overlapped, true);
        try
        {
            ConfigureSerial(handle, baudRate);
            cancellationToken.ThrowIfCancellationRequested();
            return new SerialSession(new OverlappedDevice(handle));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle Open(string path, uint access, uint share, uint flags, bool isSerial)
    {
        var handle = Native.CreateFile(path, access, share, 0, 3, flags, 0);
        if (!handle.IsInvalid)
            return handle;

        var error = WindowsErrors.Last(isSerial ? "打开串口" : "打开 HID 接口", isSerial);
        handle.Dispose();
        throw error;
    }

    internal static unsafe Native.HidAttributes ReadAttributes(SafeFileHandle handle)
    {
        var attributes = new Native.HidAttributes { Size = (uint)sizeof(Native.HidAttributes) };
        if (Native.GetHidAttributes(handle, ref attributes) == 0)
            throw WindowsErrors.Last("读取 HID VID/PID");
        return attributes;
    }

    internal static Native.HidCaps ReadCaps(SafeFileHandle handle, HidInterface? endpoint = null)
    {
        if (Native.GetPreparsedData(handle, out var data) == 0)
            throw WindowsErrors.Last("读取 HID 描述符");
        try
        {
            int status = Native.GetHidCaps(data, out var caps);
            if (status != 0x00110000)
                throw new TransportException(ReadIssue.IoError, $"解析 HID 描述符失败（0x{status:X8}）", nativeCode: status);
            if (endpoint is not null)
            {
                VerifyReportId(data, 0, endpoint.RequiredInputReportId, caps.InputLength);
                VerifyReportId(data, 1, endpoint.RequiredOutputReportId, caps.OutputLength);
                VerifyReportId(data, 2, endpoint.RequiredFeatureReportId, caps.FeatureLength);
            }
            return caps;
        }
        finally
        {
            Native.FreePreparsedData(data);
        }
    }

    internal static (string? InputIds, string? OutputIds, string? FeatureIds) ReadDeclaredReportIds(
        SafeFileHandle handle, Native.HidCaps caps)
    {
        if (Native.GetPreparsedData(handle, out var data) == 0)
            return (null, null, null);
        try
        {
            return (
                ScanReportIds(data, 0, caps.InputLength),
                ScanReportIds(data, 1, caps.OutputLength),
                ScanReportIds(data, 2, caps.FeatureLength)
            );
        }
        finally
        {
            Native.FreePreparsedData(data);
        }
    }

    private static unsafe string? ScanReportIds(nint data, int reportType, ushort length)
    {
        if (length == 0)
            return null;
        // 仅在内存中按 HID 描述符校验声明过的报告 ID，不产生任何 USB 通信。
        var buffer = new byte[length];
        List<string> ids = [];
        fixed (byte* report = buffer)
        {
            for (int id = 0; id <= byte.MaxValue; id++)
            {
                if (Native.InitializeReportForId(reportType, (byte)id, data, report, length) == 0x00110000)
                {
                    ids.Add($"{id:X2}");
                    if (ids.Count >= 16)
                    {
                        ids.Add("...");
                        break;
                    }
                }
            }
        }
        return ids.Count == 0 ? null : string.Join(',', ids);
    }

    private static unsafe void VerifyReportId(nint data, int reportType, byte? id, ushort length)
    {
        if (id is not byte reportId)
            return;

        // 只在内存中初始化描述符指定的报告，验证编号时无 USB 发包。
        var buffer = new byte[length];
        fixed (byte* report = buffer)
        {
            int status = Native.InitializeReportForId(reportType, reportId, data, report, length);
            if (status != 0x00110000)
                throw new TransportException(ReadIssue.IoError,
                    $"HID 描述符未确认报告 ID {reportId:X2}（类型 {reportType}，0x{status:X8}）");
        }
    }

    private static unsafe void ConfigureSerial(SafeFileHandle handle, int baudRate)
    {
        var settings = new Native.Dcb { Length = (uint)sizeof(Native.Dcb) };
        if (Native.GetCommState(handle, ref settings) == 0)
            throw WindowsErrors.Last("读取串口参数");

        settings.BaudRate = checked((uint)baudRate);
        // 8N1、二进制模式、关闭流控，DTR 和 RTS 置位，只配置本次串口连接。
        settings.Flags = 0x1011;
        settings.ByteSize = 8;
        settings.Parity = 0;
        settings.StopBits = 0;
        settings.XonChar = 0x11;
        settings.XoffChar = 0x13;
        if (Native.SetCommState(handle, in settings) == 0)
            throw WindowsErrors.Last("设置串口通信参数");

        var timeouts = new Native.CommTimeouts
        {
            ReadInterval = uint.MaxValue,
            ReadMultiplier = uint.MaxValue,
            ReadConstant = 100,
            WriteConstant = 500
        };
        if (Native.SetCommTimeouts(handle, in timeouts) == 0)
            throw WindowsErrors.Last("设置串口超时");
    }

    private sealed class SerialSession(OverlappedDevice io) : ISerialSession
    {
        public ValueTask WriteAsync(byte[] bytes, CancellationToken token) => io.WriteAsync(bytes, token);
        public ValueTask<int> ReadAsync(byte[] buffer, CancellationToken token) => io.ReadAsync(buffer, token);
        public void Dispose() => io.Dispose();
    }

    private sealed class HidSession(SafeFileHandle handle, Native.HidCaps caps, bool featureOnly) : IHidSession
    {
        private readonly OverlappedDevice? io = featureOnly ? null : new OverlappedDevice(handle);

        public ValueTask WriteOutputAsync(byte[] report, CancellationToken token)
        {
            if (io is null)
                throw new TransportException(ReadIssue.IoError, "只读特征会话不支持写入输出报告");
            CheckLength(report, caps.OutputLength);
            return io.WriteAsync(report, token);
        }

        public async ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token)
        {
            if (io is null)
                throw new TransportException(ReadIssue.IoError, "只读特征会话不支持读取输入报告");
            CheckLength(report, caps.InputLength);
            int count = await io.ReadAsync(report, token).ConfigureAwait(false);
            if (count == 0)
                throw new TransportException(ReadIssue.Disconnected, "HID 输入通道已关闭");
            return count;
        }

        public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) => Feature(report, true, token);

        public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token) => Feature(report, false, token);

        private unsafe ValueTask Feature(byte[] report, bool write, CancellationToken token)
        {
            CheckLength(report, caps.FeatureLength);
            token.ThrowIfCancellationRequested();
            if (!write)
                report.AsSpan(1).Clear();
            fixed (byte* buffer = report)
            {
                byte success = write
                    ? Native.SetFeature(handle, buffer, (uint)report.Length)
                    : Native.GetFeature(handle, buffer, (uint)report.Length);
                if (success == 0)
                    throw WindowsErrors.Last(write ? "发送 HID 特征报告" : "接收 HID 特征报告");
            }
            token.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        private static void CheckLength(byte[] report, int expected)
        {
            if (expected == 0 || report.Length != expected)
                throw new TransportException(ReadIssue.IoError, $"报告缓冲区长度与 HID 描述符不符（期望 {expected}）");
        }

        public void Dispose()
        {
            if (io is not null)
                io.Dispose();
            else
                handle.Dispose();
        }
    }
}

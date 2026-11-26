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
            var caps = ReadOpenedCaps(handle, endpoint, attributes);
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

    /// <summary>单次读取 HID 描述符，按需检查目标国产鼠标报告 ID 或枚举全部声明报告 ID，避免重复调用 GetPreparsedData。</summary>
    internal static (Native.HidCaps Caps, string? InputIds, string? OutputIds, string? FeatureIds, bool MatchedDomesticReportId)
        ReadCapsForDiscovery(
            SafeFileHandle handle,
            DomesticMouseStatus? domesticRoute,
            byte? interfaceNumber,
            Native.HidAttributes attributes,
            bool includeUnknownHid,
            bool includeReportIds)
    {
        if (Native.GetPreparsedData(handle, out var data) == 0)
            throw WindowsErrors.Last("读取 HID 描述符");
        try
        {
            int status = Native.GetHidCaps(data, out var caps);
            if (status != 0x00110000)
                throw new TransportException(ReadIssue.IoError, $"解析 HID 描述符失败（0x{status:X8}）", nativeCode: status);

            bool domesticMatched = domesticRoute is not null &&
                domesticRoute.MatchesCollection(caps.UsagePage, caps.Usage, interfaceNumber, caps.InputLength, caps.FeatureLength);
            bool declaredDomestic = false;
            if (domesticMatched)
            {
                int type = domesticRoute!.Feature ? 2 : 0;
                ushort length = domesticRoute.Feature ? caps.FeatureLength : caps.InputLength;
                declaredDomestic = DeclaresReportId(data, type, domesticRoute.ReportId, length);
            }

            if (domesticRoute is not null && !includeUnknownHid && !includeReportIds)
            {
                if (!domesticMatched || !declaredDomestic)
                    return (caps, null, null, null, false);
                string declared = $"{domesticRoute.ReportId:X2}";
                return domesticRoute.Feature
                    ? (caps, null, null, declared, true)
                    : (caps, declared, null, null, true);
            }

            string? inputIds = null, outputIds = null, featureIds = null;
            bool inspectUlx = FinalmouseAdapter.NeedsReportIds(attributes.VendorId, attributes.ProductId, attributes.Version) &&
                caps is { UsagePage: 0xFF00, Usage: 1, FeatureLength: 0,
                    InputLength: FinalmouseProtocol.ReportLength, OutputLength: FinalmouseProtocol.ReportLength };
            bool inspectScyrox = ExtendedPeripheralProtocol.NeedsReportIds(
                attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.InputLength, caps.OutputLength);
            if (inspectUlx || inspectScyrox || includeReportIds &&
                (caps.UsagePage >= 0xFF00 || attributes.VendorId == 0x1532 && caps.FeatureLength == RazerFeatureProtocol.ReportLength ||
                 domesticRoute is not null))
            {
                inputIds = ScanReportIds(data, 0, caps.InputLength);
                outputIds = ScanReportIds(data, 1, caps.OutputLength);
                featureIds = ScanReportIds(data, 2, caps.FeatureLength);
            }
            if (outputIds is null && HyperXHeadsetProtocol.IsCloud3SOutputCandidate(attributes.VendorId, attributes.ProductId,
                caps.UsagePage, interfaceNumber, caps.OutputLength))
            {
                // 多个同形集合时，普通轮询和完整诊断必须选中同一个 0C 命令集合。
                outputIds = DeclaresReportId(data, 1, 0x0C, caps.OutputLength) ? "0C" : "";
            }
            if (featureIds is null && AsusHidProtocol.IsStrixGoFeatureCandidate(attributes.VendorId, attributes.ProductId, caps.FeatureLength))
            {
                featureIds = DeclaresReportId(data, 2, AsusHidProtocol.HeadsetReportId, caps.FeatureLength) ? "FF" : "";
            }

            if (domesticMatched)
            {
                // 明确区分「已验证未声明目标报告 ID（""）」与「旧诊断样本缺元数据（null）」，防止下游误放行匹配。
                if (domesticRoute!.Feature)
                    featureIds ??= declaredDomestic ? $"{domesticRoute.ReportId:X2}" : "";
                else
                    inputIds ??= declaredDomestic ? $"{domesticRoute.ReportId:X2}" : "";
            }

            return (caps, inputIds, outputIds, featureIds, domesticMatched && declaredDomestic);
        }
        finally
        {
            Native.FreePreparsedData(data);
        }
    }

    private static Native.HidCaps ReadOpenedCaps(SafeFileHandle handle, HidInterface endpoint, Native.HidAttributes attributes)
    {
        if (Native.GetPreparsedData(handle, out var data) == 0)
            throw WindowsErrors.Last("读取 HID 描述符");
        try
        {
            int status = Native.GetHidCaps(data, out var caps);
            if (status != 0x00110000)
                throw new TransportException(ReadIssue.IoError, $"解析 HID 描述符失败（0x{status:X8}）", nativeCode: status);
            // 先识别同一路径上被替换的接口，避免过期的报告编号把热插拔误判为永久 I/O 错误。
            if (attributes.VendorId != endpoint.VendorId || attributes.ProductId != endpoint.ProductId ||
                endpoint.FirmwareVersion is ushort firmware && attributes.Version != firmware ||
                caps.UsagePage != endpoint.UsagePage || caps.Usage != endpoint.Usage ||
                caps.InputLength != endpoint.InputLength || caps.OutputLength != endpoint.OutputLength ||
                caps.FeatureLength != endpoint.FeatureLength)
                throw new TransportException(ReadIssue.Disconnected, "HID 接口已变更，请重新枚举");

            if (endpoint is { RequiredInputReportId: 0x05, RequiredOutputReportId: 0x05, RequiredFeatureReportId: null } &&
                FinalmouseAdapter.Has0303Layout(endpoint))
            {
                if (ScanReportIds(data, 0, caps.InputLength) != endpoint.InputReportIds ||
                    ScanReportIds(data, 1, caps.OutputLength) != endpoint.OutputReportIds)
                    throw new TransportException(ReadIssue.Disconnected, "ULX 报告布局已变更，请重新枚举");
            }
            else
            {
                VerifyReportId(data, 0, endpoint.RequiredInputReportId, caps.InputLength, endpoint.InputReportIds);
                VerifyReportId(data, 1, endpoint.RequiredOutputReportId, caps.OutputLength, endpoint.OutputReportIds);
                VerifyReportId(data, 2, endpoint.RequiredFeatureReportId, caps.FeatureLength, endpoint.FeatureReportIds);
            }
            return caps;
        }
        finally
        {
            Native.FreePreparsedData(data);
        }
    }

    // 只操作同一份预解析描述符，所有编号共用一块缓冲区，不产生 USB 通信。
    private static unsafe int InitializeReport(nint data, int type, byte id, byte[] buffer)
    {
        fixed (byte* report = buffer)
            return Native.InitializeReportForId(type, id, data, report, (uint)buffer.Length);
    }

    private static string? ScanReportIds(nint data, int type, ushort length)
    {
        if (length == 0)
            return null;
        var buffer = new byte[length];
        return HidReportDescriptor.ListIds(id => InitializeReport(data, type, id, buffer));
    }

    private static bool DeclaresReportId(nint data, int type, byte id, ushort length) =>
        length > 0 && HidReportDescriptor.IsDeclared(type, id, InitializeReport(data, type, id, new byte[length]));

    private static void VerifyReportId(nint data, int type, byte? id, ushort length, string? enumeratedIds)
    {
        if (id is not byte required)
            return;
        if (length == 0)
            throw new TransportException(ReadIssue.IoError, $"HID 描述符报告长度为 0（类型 {type}，ID {required:X2}）");
        var buffer = new byte[length];
        HidReportDescriptor.Require(type, required, enumeratedIds, candidate => InitializeReport(data, type, candidate, buffer));
    }

    private static unsafe void ConfigureSerial(SafeFileHandle handle, int baudRate)
    {
        Native.SetupComm(handle, 4096, 4096);
        var settings = new Native.Dcb { Length = (uint)sizeof(Native.Dcb) };
        if (Native.GetCommState(handle, ref settings) == 0)
            throw WindowsErrors.Last("读取串口参数");

        settings.BaudRate = checked((uint)baudRate);
        // 8N1、二进制模式、关闭流控，DTR 和 RTS 置位，只配置本次串口连接。
        settings.Flags = 0x1011;
        settings.Reserved = 0;
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

        // 清空历史错误标志与驱动 RX/TX 残留字节，避免旧帧或切波特率垃圾污染首包解析。
        Native.ClearCommError(handle, out _, 0);
        Native.PurgeComm(handle, 0x000Fu);
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
            if (!featureOnly)
                throw new TransportException(ReadIssue.IoError, "特征报告需通过 FeatureOnly 会话访问");
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

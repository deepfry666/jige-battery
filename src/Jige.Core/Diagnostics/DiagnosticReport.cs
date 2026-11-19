using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Jige.Core.Adapters;
using Jige.Core.Windows;

namespace Jige.Core.Diagnostics;

/// <summary>在用户主动请求后调用，返回供完整预览的纯文本。</summary>
public static class DiagnosticReport
{
    internal const string Header = "几格 / Jige Battery 诊断报告 v1";

    [SupportedOSPlatform("windows")]
    public static async Task<string> GenerateWindowsAsync(CancellationToken cancellationToken = default)
    {
        DeviceInventory inventory;
        try
        {
            // 用零访问句柄读取全部 HID 描述符，只为找出未收录接收器的厂商集合；不打开任何报告通道。
            inventory = await WindowsDeviceDiscovery.ScanAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return CreateHeader(115200).AppendLine("枚举错误 Timeout").ToString();
        }
        return await GenerateAsync(inventory, new WindowsReceiverTransport(), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> GenerateAsync(DeviceInventory inventory, IReceiverTransport transport,
        CancellationToken cancellationToken = default, int sonyBaudRate = 115200)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = AdapterRegistry.CreateDefault(sonyBaudRate);
        var report = CreateHeader(sonyBaudRate);
        var ids = new Dictionary<ReceiverEndpoint, string>();
        var receivers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var salt = RandomNumberGenerator.GetBytes(16);

        string ReceiverAlias(ReceiverEndpoint endpoint)
        {
            if (!receivers.TryGetValue(endpoint.ReceiverId, out var id))
                receivers.Add(endpoint.ReceiverId, id = $"R{receivers.Count + 1}");
            return id;
        }

        var hid = inventory.Hid.Where(IsRelevantHid).Distinct().ToArray();
        for (int i = 0; i < hid.Length; i++)
        {
            var endpoint = hid[i];
            string id = $"H{i + 1}";
            ids.Add(endpoint, id);
            report.AppendLine(FormattableString.Invariant(
                $"HID|{id}|{ReceiverAlias(endpoint)}|{endpoint.VendorId:X4}|{endpoint.ProductId:X4}|{endpoint.UsagePage:X4}|{endpoint.Usage:X4}|{endpoint.InputLength}|{endpoint.OutputLength}|{endpoint.FeatureLength}|{endpoint.InterfaceNumber:X2}|{DiagnosticPrivacy.Product(endpoint.Product, salt)}"));
            foreach (var candidate in MouseReceiverCatalog.Candidates(endpoint.VendorId, endpoint.ProductId))
            {
                string status = candidate.Matches(endpoint) ? candidate.Enabled ? "描述符匹配" : "仅登记" : "描述符不匹配";
                report.AppendLine($"候选 {id} {candidate.Model} {FamilyName(candidate.Family)} {EvidenceName(candidate.Evidence)} {status}");
            }
        }

        int serialIndex = 0;
        foreach (var endpoint in inventory.Serial.Distinct())
        {
            var hardwareIds = endpoint.HardwareIds.Select(DiagnosticPrivacy.UsbId).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            string id = $"S{++serialIndex}";
            ids.Add(endpoint, id);
            report.AppendLine($"USB|{id}|{ReceiverAlias(endpoint)}|{string.Join(',', hardwareIds)}");
        }

        report.AppendLine(FormattableString.Invariant($"枚举警告数量 {inventory.Warnings.Count}"));
        var devices = registry.Detect(inventory).ToArray();
        foreach (var adapter in registry.Adapters)
        {
            var targets = devices.Where(device => device.Adapter == adapter).ToArray();
            string name = AdapterName(adapter);
            if (targets.Length == 0)
                report.AppendLine($"匹配 {name} 无可查询接口");
            foreach (var device in targets)
            {
                var endpoint = device.Target.Endpoint;
                if (!ids.TryGetValue(endpoint, out string? id))
                {
                    // ULX 的枚举降级路径可能引用标准集合或缺失描述符的接口。
                    report.AppendLine($"匹配 {name} 安全检查未通过，跳过查询");
                    continue;
                }

                string label = endpoint is HidInterface mouse && MouseReceiverAdapter.AllowedRoute(mouse) is { } route
                    ? FamilyName(route.Family) : name;
                report.AppendLine($"匹配 {label} {id}");
                var capture = new DiagnosticTransport(transport, endpoint);
                long started = Stopwatch.GetTimestamp();
                // 罗技接收器下可能有多台设备，统一走多设备读取，逐条输出结果。
                var readings = await registry.ReadDevicesAsync(device, capture, null, cancellationToken).ConfigureAwait(false);
                long elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var (transfers, truncated) = capture.Finish();
                WriteTransfers(report, id, transfers);
                if (truncated)
                    report.AppendLine($"截断 {id} 耗尽诊断记录上限");
                foreach (var reading in readings)
                    report.AppendLine(FormattableString.Invariant(
                        $"结果 {id} {elapsed} ms {reading.Device.Kind} 电量 {reading.Percentage?.ToString(CultureInfo.InvariantCulture) ?? "-"}% {reading.Charging} {reading.Link} {reading.Issue}"));
            }
        }
        return report.ToString();
    }

    private static StringBuilder CreateHeader(int baud)
    {
        var version = Environment.OSVersion.Version;
        string os = OperatingSystem.IsWindows() ? $"{version.Major}.{version.Minor}.{version.Build}" : "非 Windows";
        return new StringBuilder().AppendLine(Header)
            .AppendLine($"几格版本 {BuildVersion.Value}")
            .AppendLine($"Windows {os}")
            .AppendLine($"CPU 架构 {RuntimeInformation.OSArchitecture}")
            .AppendLine(FormattableString.Invariant($"SONY-BAUD|{baud}"))
            .AppendLine("HID 列为接口｜接收器｜VID｜PID｜使用页｜用法｜输入｜输出｜特征｜接口号｜产品字符串。")
            .AppendLine("USB 列为接口｜接收器｜硬件 ID。OP 列格式见 PRIVACY.md。");
    }

    // 只输出厂商自定义集合（使用页 FF00–FFFF），任何品牌都列出；标准键盘、鼠标、音量等集合一律不出现在报告里。
    private static bool IsRelevantHid(HidInterface hid) => hid.UsagePage is >= 0xFF00;

    private static void WriteTransfers(StringBuilder report, string id, DiagnosticTransfer[] transfers)
    {
        var serialData = transfers.Where(item => item.Operation == DiagnosticOperation.SerialRead)
            .SelectMany(item => item.Data).ToArray();
        var safeSerial = DiagnosticPrivacy.SerialReplies(serialData);
        Array.Clear(serialData);
        int offset = 0;
        foreach (var item in transfers)
        {
            var safe = item.Data;
            bool masked = item.Masked;
            if (item.Operation == DiagnosticOperation.SerialRead)
            {
                safe = safeSerial.AsSpan(offset, item.Data.Length).ToArray();
                offset += item.Data.Length;
                masked = !safe.AsSpan().SequenceEqual(item.Data);
                Array.Clear(item.Data);
            }
            string code = item.NativeCode?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string argument = item.Argument?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string hex = safe.Length == 0 ? "-" : Convert.ToHexString(safe);
            report.AppendLine(FormattableString.Invariant(
                $"OP|{id}|{item.Operation}|{item.Milliseconds}|{item.Issue}|{code}|{argument}|{hex}|{(masked ? 1 : 0)}"));
        }
    }

    private static string AdapterName(IDeviceAdapter adapter) => adapter switch
    {
        FinalmouseAdapter => "Finalmouse ULX",
        SonyInzoneAdapter => "Sony INZONE H9",
        _ => "ATK / G-Wolves"
    };

    private static string EvidenceName(MouseEvidence evidence) => evidence switch
    {
        MouseEvidence.MultipleSources => "多方开源实现一致",
        MouseEvidence.SingleSource => "单一来源",
        _ => "未核实"
    };

    private static string FamilyName(MouseFamily family) => family switch
    {
        MouseFamily.CompxOut => "Compx-OUT",
        MouseFamily.CompxFeature => "Compx-FEAT",
        MouseFamily.BitMouse => "BitMouse",
        MouseFamily.XviOld => "XVI-OLD",
        MouseFamily.XviNew => "XVI-NEW",
        _ => "待分流"
    };
}

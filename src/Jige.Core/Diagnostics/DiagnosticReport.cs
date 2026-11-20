using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Jige.Core.Adapters;
using Jige.Core.Windows;

namespace Jige.Core.Diagnostics;

public sealed record DiagnosticReadingSample(
    DateTimeOffset At, int Percentage, bool IsEstimated, ChargingState Charging);

public sealed record DiagnosticDeviceHistory(
    DeviceIdentity Device, DateTimeOffset FirstSeen, DateTimeOffset? LastOnline,
    IReadOnlyList<DiagnosticReadingSample> RecentReadings);

public sealed record DiagnosticProcessMetrics(
    long PrivateMemoryBytes, long WorkingSetBytes, int HandleCount, int GdiObjects, int UserObjects,
    TimeSpan TotalCpuTime, TimeSpan Uptime);

public sealed record DiagnosticSettingsSummary(
    string TrayMode, int LowBatteryThreshold, int PollSeconds, int GamePollSeconds,
    int HeartbeatSeconds, int GameHeartbeatSeconds, bool GameSilent, int CustomGameProcessCount,
    bool StartWithWindows, int BlockedReceiverCount);

public sealed record DiagnosticContext(
    IReadOnlyList<DiagnosticDeviceHistory>? DeviceHistories = null,
    DiagnosticProcessMetrics? ProcessMetrics = null,
    DiagnosticSettingsSummary? SettingsSummary = null,
    SafetyGateStore? Gates = null);

/// <summary>在用户主动请求后调用，返回供完整预览的纯文本。</summary>
public static class DiagnosticReport
{
    internal const string Header = "几格 / Jige Battery 诊断报告 v1";
    internal const string PrivacyNotice =
        "提示：这份报告可以直接附到 GitHub issue；序列号、实例 ID、用户名、计算机名和路径已经脱敏，提交前可先完整检查。";

    [SupportedOSPlatform("windows")]
    public static async Task<string> GenerateWindowsAsync(
        CancellationToken cancellationToken = default, DiagnosticContext? context = null)
    {
        DeviceInventory inventory;
        try
        {
            // 用零访问句柄读取全部 HID 描述符，只为找出未收录接收器的厂商集合；不打开任何报告通道。
            inventory = await WindowsDeviceDiscovery.ScanAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var timeoutReport = CreateHeader(115200).AppendLine("枚举错误 Timeout");
            AppendContextSections(timeoutReport, context);
            return timeoutReport.ToString();
        }
        return await GenerateAsync(inventory, new WindowsReceiverTransport(), cancellationToken, 115200, context)
            .ConfigureAwait(false);
    }

    public static async Task<string> GenerateAsync(DeviceInventory inventory, IReceiverTransport transport,
        CancellationToken cancellationToken = default, int sonyBaudRate = 115200, DiagnosticContext? context = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gates = context?.Gates;
        var registry = AdapterRegistry.CreateDefault(sonyBaudRate, gates);
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
            string product = DiagnosticPrivacy.Product(endpoint.Product, salt);
            if (LogitechHidppAdapter.UsesReceiverName(endpoint))
                product += " · HID++ Receiver"; // 回放所需的非识别性匹配标记。
            report.AppendLine(FormattableString.Invariant(
                $"HID|{id}|{ReceiverAlias(endpoint)}|{endpoint.VendorId:X4}|{endpoint.ProductId:X4}|{endpoint.UsagePage:X4}|{endpoint.Usage:X4}|{endpoint.InputLength}|{endpoint.OutputLength}|{endpoint.FeatureLength}|{endpoint.InterfaceNumber:X2}|{product}"));
            // B3：路径开关登记在禁用表中的设备，直接说明原因且不打开设备句柄。
            if (DeviceCatalog.IsDisabledPath(endpoint, out var disabled))
            {
                report.AppendLine($"状态 {id} {disabled!.Model}（{disabled.ProtocolFamily}） 已识别，暂未启用：{disabled.Reason}（出处：{disabled.Source}）");
                continue;
            }
            var candidates = MouseReceiverCatalog.Candidates(endpoint.VendorId, endpoint.ProductId).ToArray();
            string tierLabel = DeviceCatalog.TierLabel(DeviceCatalog.VerificationFor(endpoint.VendorId, endpoint.ProductId));
            foreach (var candidate in candidates)
            {
                string status = candidate.Matches(endpoint) ? candidate.Enabled ? "描述符匹配" : "仅登记" : "描述符不匹配";
                report.AppendLine($"候选 {id} {candidate.Model} {FamilyName(candidate.Family)} {EvidenceName(candidate.Evidence)} 验证={tierLabel} {status}");
            }
            if (candidates.Length == 0 && MouseReceiverCatalog.GenericRoute(endpoint) is { } generic)
                report.AppendLine($"候选 {id} {generic.Model} {FamilyName(generic.Family)} 通用适配 验证=仅按资料 描述符匹配");
            else if (FinalmouseAdapter.IsVendorCollection(endpoint) && !FinalmouseAdapter.IsCatalogPid(endpoint.ProductId))
                report.AppendLine($"候选 {id} Finalmouse ULX ULX 通用适配 验证=仅按资料 描述符匹配");
            else if (candidates.Length == 0 && DeviceCatalog.FindEntry(endpoint.VendorId, endpoint.ProductId) is { } entry)
                report.AppendLine($"候选 {id} {entry.Model} {entry.ProtocolFamily} 验证={DeviceCatalog.TierLabel(entry.Verification)}");
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
                string genericTag = device.Target.Device.Source == AdaptationSource.Generic ? "（通用适配）" : "";
                string verificationTag = $" 验证={DeviceCatalog.TierLabel(DeviceCatalog.GetVerification(device.Target.Device))}";
                if (device.Target.ShortHid is { } shortHid && ids.TryGetValue(shortHid, out var shortId))
                    report.AppendLine($"匹配 {label}{genericTag} {id}（长报告） {shortId}（短报告）{verificationTag}");
                else if (device.Target.SiblingInputHids.Count > 0)
                {
                    var siblingIds = device.Target.SiblingInputHids
                        .Select(item => ids.TryGetValue(item, out var siblingId) ? siblingId : null)
                        .OfType<string>().ToArray();
                    string siblingText = siblingIds.Length > 0 ? $" 旁路监听 {string.Join(',', siblingIds)}" : "";
                    report.AppendLine($"匹配 {label}{genericTag} {id}{siblingText}{verificationTag}");
                }
                else
                    report.AppendLine($"匹配 {label}{genericTag} {id}{verificationTag}");

                var capture = new DiagnosticTransport(transport, adapter);
                long started = Stopwatch.GetTimestamp();
                // 罗技接收器下可能有多台设备，统一走多设备读取，逐条输出结果。
                var readings = await registry.ReadDevicesAsync(device, capture, null, cancellationToken).ConfigureAwait(false);
                long elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var (transfers, truncated) = capture.Finish();
                WriteTransfers(report, ids, transfers);
                WriteCollectionSummary(report, ids, transfers);
                if (truncated)
                    report.AppendLine($"截断 {id} 耗尽诊断记录上限");
                foreach (var reading in readings)
                    report.AppendLine(FormattableString.Invariant(
                        $"结果 {id} {elapsed} ms {reading.Device.Kind} 电量 {reading.Percentage?.ToString(CultureInfo.InvariantCulture) ?? "-"}% {reading.Charging} {reading.Link} {reading.Issue}"));
            }
        }

        AppendContextSections(report, context);
        return report.ToString();
    }

    private static void WriteCollectionSummary(
        StringBuilder report, Dictionary<ReceiverEndpoint, string> ids, DiagnosticTransfer[] transfers)
    {
        var hidGroups = transfers
            .Where(item => item.Endpoint is HidInterface)
            .GroupBy(item => NormalizeEndpoint(item.Endpoint));
        foreach (var group in hidGroups)
        {
            if (group.Key is not HidInterface hid || !ids.TryGetValue(hid, out string? id))
                continue;
            int sent = group.Count(item => item.Operation is DiagnosticOperation.HidOutput or DiagnosticOperation.SetFeature &&
                item.Issue == ReadIssue.None);
            int received = group.Count(item => item.Operation is DiagnosticOperation.HidInput or DiagnosticOperation.GetFeature &&
                item.Issue == ReadIssue.None);
            int timeouts = group.Count(item => item.Issue == ReadIssue.Timeout);
            int errors = group.Count(item => item.Issue is not (ReadIssue.None or ReadIssue.Timeout));
            report.AppendLine(FormattableString.Invariant(
                $"集合统计|{id}|{hid.UsagePage:X4}:{hid.Usage:X4}|发送={sent}|接收={received}|超时={timeouts}|错误={errors}"));
        }
    }

    private static void AppendContextSections(StringBuilder report, DiagnosticContext? context)
    {
        if (context is null)
            return;

        if (context.Gates is { } gates)
        {
            var blocked = gates.Snapshot();
            var rejections = gates.RecentRejections();
            if (blocked.Count > 0 || rejections.Length > 0)
            {
                report.AppendLine();
                report.AppendLine("# 安全门状态");
                foreach (var (signature, reason) in blocked)
                    report.AppendLine($"安全门拦截|{DiagnosticPrivacy.SanitizeText(signature)}|{DiagnosticPrivacy.SanitizeText(reason)}");
                foreach (var item in rejections.TakeLast(16))
                    report.AppendLine(FormattableString.Invariant(
                        $"安全门记录|门{item.Gate}|{DiagnosticPrivacy.SanitizeText(item.Signature)}|{DiagnosticPrivacy.SanitizeText(item.Reason)}"));
            }
        }

        if (context.DeviceHistories is { Count: > 0 } histories)
        {
            report.AppendLine();
            report.AppendLine("# 设备历史（每台最多 5 次读数）");
            foreach (var item in histories)
            {
                string safeName = DiagnosticPrivacy.SafeDeviceLabel(item.Device);
                string first = item.FirstSeen.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                string last = item.LastOnline?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "-";
                var samples = item.RecentReadings.TakeLast(5).Select(sample => FormattableString.Invariant(
                    $"{sample.At.ToUniversalTime():HH:mm:ss}={(sample.IsEstimated ? "~" : "")}{sample.Percentage}%/{sample.Charging}"));
                string sampleText = string.Join(',', samples);
                if (sampleText.Length == 0)
                    sampleText = "-";
                report.AppendLine(FormattableString.Invariant(
                    $"历史|{item.Device.Kind}|{safeName}|首次={first}|最后在线={last}|读数={sampleText}"));
            }
        }

        if (context.ProcessMetrics is { } metrics)
        {
            report.AppendLine();
            report.AppendLine("# 进程指标");
            double privateMb = metrics.PrivateMemoryBytes / (1024.0 * 1024.0);
            double workingMb = metrics.WorkingSetBytes / (1024.0 * 1024.0);
            report.AppendLine(FormattableString.Invariant(
                $"进程|私有内存={privateMb:F1}MB|工作集={workingMb:F1}MB|句柄={metrics.HandleCount}|GDI={metrics.GdiObjects}|USER={metrics.UserObjects}|累计CPU={metrics.TotalCpuTime.TotalSeconds:F2}s|运行时长={FormatDuration(metrics.Uptime)}"));
        }

        if (context.SettingsSummary is { } summary)
        {
            report.AppendLine();
            report.AppendLine("# 当前设置摘要");
            report.AppendLine(FormattableString.Invariant(
                $"设置|托盘模式={DiagnosticPrivacy.SanitizeText(summary.TrayMode)}|低电量阈值={summary.LowBatteryThreshold}%|查询间隔={summary.PollSeconds}s/游戏中{summary.GamePollSeconds}s|心跳={summary.HeartbeatSeconds}s/游戏中{summary.GameHeartbeatSeconds}s|游戏中不打扰={(summary.GameSilent ? "开" : "关")}|自定义游戏进程数={summary.CustomGameProcessCount}|开机自启={(summary.StartWithWindows ? "开" : "关")}|安全门拦截数={summary.BlockedReceiverCount}"));
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;
        if (duration.TotalHours >= 1)
            return FormattableString.Invariant($"{(int)duration.TotalHours}h{duration.Minutes:D2}m{duration.Seconds:D2}s");
        if (duration.TotalMinutes >= 1)
            return FormattableString.Invariant($"{duration.Minutes}m{duration.Seconds:D2}s");
        return FormattableString.Invariant($"{duration.Seconds}s");
    }

    private static StringBuilder CreateHeader(int baud)
    {
        var version = Environment.OSVersion.Version;
        string os = OperatingSystem.IsWindows() ? $"{version.Major}.{version.Minor}.{version.Build}" : "非 Windows";
        return new StringBuilder().AppendLine(Header)
            .AppendLine(PrivacyNotice)
            .AppendLine($"几格版本 {BuildVersion.Value}")
            .AppendLine($"Windows {os}")
            .AppendLine($"CPU 架构 {RuntimeInformation.OSArchitecture}")
            .AppendLine(FormattableString.Invariant($"SONY-BAUD|{baud}"))
            .AppendLine("HID 列为接口｜接收器｜VID｜PID｜使用页｜用法｜输入｜输出｜特征｜接口号｜产品字符串。")
            .AppendLine("USB 列为接口｜接收器｜硬件 ID。OP 列格式见 PRIVACY.md。");
    }

    // 只输出厂商自定义集合（使用页 FF00–FFFF），任何品牌都列出；标准键盘、鼠标、音量等集合一律不出现在报告里。
    private static bool IsRelevantHid(HidInterface hid) => hid.UsagePage is >= 0xFF00;

    private static ReceiverEndpoint NormalizeEndpoint(ReceiverEndpoint endpoint) =>
        endpoint is HidInterface hid ? hid with
        {
            RequiredInputReportId = null,
            RequiredOutputReportId = null,
            RequiredFeatureReportId = null
        } : endpoint;

    private static void WriteTransfers(StringBuilder report, Dictionary<ReceiverEndpoint, string> ids, DiagnosticTransfer[] transfers)
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
            // 报告 ID 约束只在打开句柄时使用，归回添加约束前的枚举端点。
            var endpoint = NormalizeEndpoint(item.Endpoint);
            string id = ids[endpoint];
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
        MouseReceiverAdapter => "ATK / G-Wolves",
        LogitechHidppAdapter => "Logitech HID++",
        LogitechHeadsetAdapter => "Logitech 耳机",
        RazerFeatureAdapter => "Razer 90B",
        RazerHeadsetAdapter => "Razer 耳机",
        _ => adapter.GetType().Name
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

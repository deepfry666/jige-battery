namespace Jige.Core.Adapters;

internal sealed class FinalmouseAdapter(SafetyGateStore? gates = null) : IDeviceAdapter, IPresenceAdapter
{
    private const string CatalogName = "Finalmouse UltralightX";
    private const string GenericName = "Finalmouse ULX";
    private readonly Lock confirmLock = new();
    private readonly HashSet<string> confirmedPaths = new(StringComparer.OrdinalIgnoreCase);

    public PresenceProbeKind PresenceKind => PresenceProbeKind.Heartbeat;

    public void ResetCache()
    {
        lock (confirmLock)
            confirmedPaths.Clear();
    }

    private bool IsConfirmed(string path)
    {
        lock (confirmLock)
            return confirmedPaths.Contains(path);
    }

    private void Confirm(string path)
    {
        lock (confirmLock)
            confirmedPaths.Add(path);
    }

    public static bool IsReceiver(HidInterface hid) => hid.VendorId == 0x361D;
    public static bool IsCatalogPid(ushort? pid) => pid == 0x0100;

    internal static bool NeedsReportIds(ushort? vid, ushort? pid, ushort? firmware) =>
        vid == 0x361D && IsCatalogPid(pid) && firmware == 0x0303;

    internal static bool IsVendorCollection(HidInterface hid) => IsReceiver(hid) &&
        hid is { UsagePage: 0xFF00, Usage: 0x0001, InputLength: FinalmouseProtocol.ReportLength,
            OutputLength: FinalmouseProtocol.ReportLength, MetadataIssue: ReadIssue.None };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(IsReceiver).GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var groupHids = group.ToArray();
            var candidates = groupHids.Where(IsVendorCollection).Take(2).ToArray();
            bool catalogPid = groupHids.Any(hid => IsCatalogPid(hid.ProductId));

            if (candidates.Length == 2)
            {
                gates?.RecordRejection(candidates[0], 1, "同一接收器存在多个匹配的 ULX 厂商集合");
                if (!catalogPid)
                    continue;
                var ambiguous = candidates[0] with { MetadataError = "有多个匹配的 ULX 厂商集合", MetadataIssue = ReadIssue.IoError };
                yield return new(new($"ulx:{group.Key}", CatalogName, DeviceKind.Mouse, AdaptationSource.Catalog), ambiguous, "0");
                continue;
            }

            if (candidates.Length == 0)
            {
                // 目录 PID 保留原有描述符错误提示，便于诊断；未收录 PID 无匹配集合则不发任何命令（安全门 ①）。
                if (!catalogPid)
                    continue;
                var endpoint = groupHids.FirstOrDefault(hid => hid.MetadataError is not null) ?? groupHids[0];
                yield return new(new($"ulx:{group.Key}", CatalogName, DeviceKind.Mouse, AdaptationSource.Catalog), endpoint, "0");
                continue;
            }

            var selected = candidates[0];
            if (DeviceCatalog.IsDisabledPath(selected, out _))
                continue;
            if (gates?.IsBlocked(selected, out _) == true)
                continue;

            var source = IsCatalogPid(selected.ProductId) ? AdaptationSource.Catalog : AdaptationSource.Generic;
            string name = source == AdaptationSource.Catalog ? CatalogName : GenericName;
            var tier = source == AdaptationSource.Catalog
                ? DeviceCatalog.VerificationFor(selected.VendorId, selected.ProductId)
                : VerificationTier.DocsOnly;
            yield return new(new($"ulx:{group.Key}", name, DeviceKind.Mouse, source, tier)
            {
                SourceLabel = "Finalmouse 接收器"
            }, selected, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        if (target.Endpoint is not HidInterface endpoint)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "ULX 接口类型不匹配");
        if (endpoint.MetadataIssue != ReadIssue.None)
            return AdapterRegistry.Failure(target, endpoint.MetadataIssue, endpoint.MetadataError ?? "无法读取 HID 描述符");
        if (!IsVendorCollection(endpoint))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "未找到 FF00:0001、输入和输出均为 64 字节的 HID 集合");
        if (DeviceCatalog.IsDisabledPath(endpoint, out var disabled))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"已识别，暂未启用：{disabled!.Reason}");
        if (gates?.IsBlocked(endpoint, out var blockedReason) == true)
            return AdapterRegistry.Failure(target, ReadIssue.IoError, $"安全门已拦截：{blockedReason}");

        using var session = transport.OpenHid(QueryEndpoint(endpoint), cancellationToken);
        // 安全门 ②：第一条只发无害的 0x24 链路状态查询，回复格式正确才继续。
        var (link, sawReply) = await QueryAsync(session, UlxQuery.Link, trace, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            if (sawReply)
            {
                if (target.Device.Source == AdaptationSource.Generic && !IsConfirmed(endpoint.Path))
                    gates?.Block(endpoint, "ULX 链路状态查询回复格式不符合协议");
                return AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "链路状态回复格式不符合 ULX 协议");
            }
            // R1：超时无回复一律视为休眠，仅接收器拔出或明确断开才算离线。
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow,
                ReadIssue.Timeout, "链路状态查询超时，设备可能正在休眠");
        }
        Confirm(endpoint.Path);
        if (link[0] == 0)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow,
                Detail: "无线链路未连接，接收器无法区分休眠和关机");

        var (battery, _) = await QueryAsync(session, UlxQuery.Percentage, trace, cancellationToken).ConfigureAwait(false);
        int? level = battery is not null && battery[0] <= 100 ? battery[0] : null;
        var estimated = false;
        var malformed = battery is not null && level is null;
        if (level is null)
        {
            var (voltage, _) = await QueryAsync(session, UlxQuery.Voltage, trace, cancellationToken).ConfigureAwait(false);
            level = voltage is null ? null : FinalmouseProtocol.VoltagePercentage(voltage);
            estimated = level.HasValue;
            malformed |= voltage is not null && level is null;
        }

        var (power, _) = await QueryAsync(session, UlxQuery.Charging, trace, cancellationToken).ConfigureAwait(false);
        var charging = power?[0] switch { 0 => ChargingState.NotCharging, 1 => ChargingState.Charging, _ => ChargingState.Unknown };
        var issue = level.HasValue ? ReadIssue.None : malformed ? ReadIssue.InvalidResponse : ReadIssue.Timeout;
        var detail = issue switch
        {
            ReadIssue.InvalidResponse => "百分比或电压超出有效范围",
            ReadIssue.Timeout => "百分比和电压均未收到有效回复",
            _ when power is null => "充电状态查询超时",
            _ when charging == ChargingState.Unknown => "充电状态字节未知",
            _ => null
        };
        return new(target.Device, level, estimated, charging, LinkState.Online, DateTimeOffset.UtcNow, issue, detail);
    }

    public async ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken cancellationToken)
    {
        if (target.Endpoint is not HidInterface endpoint || !IsVendorCollection(endpoint) ||
            DeviceCatalog.IsDisabledPath(endpoint, out _) ||
            gates?.IsBlocked(endpoint, out _) == true)
            return null;

        using var session = transport.OpenHid(QueryEndpoint(endpoint), cancellationToken);
        var (link, sawReply) = await QueryAsync(session, UlxQuery.Link, null, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            if (sawReply && target.Device.Source == AdaptationSource.Generic && !IsConfirmed(endpoint.Path))
                gates?.Block(endpoint, "ULX 心跳链路回复格式不符合协议");
            // R1：ULX 查询超时算休眠（不置离线），异常回复返回 Unknown。
            return new(endpoint.ReceiverId, null, target.Device.Id,
                sawReply ? LinkState.Unknown : LinkState.Asleep,
                sawReply ? ReadIssue.InvalidResponse : ReadIssue.Timeout);
        }
        Confirm(endpoint.Path);
        return new(endpoint.ReceiverId, null, target.Device.Id, link[0] == 0 ? LinkState.Asleep : LinkState.Online);
    }

    internal static bool Has0303Layout(HidInterface endpoint) =>
        NeedsReportIds(endpoint.VendorId, endpoint.ProductId, endpoint.FirmwareVersion) &&
        endpoint is { Bus: HidBus.Usb, FeatureLength: 0, InputReportIds: "03,05", OutputReportIds: "03,05" };

    private static HidInterface QueryEndpoint(HidInterface endpoint)
    {
        // Windows 实机报告（见 docs/protocols.md）：0303 的描述符可初始化输出 05，
        // 但 ULX 只读请求在线上的首字节仍是 04。打开句柄时用描述符中的编号复核集合。
        return endpoint with
        {
            RequiredInputReportId = 0x05,
            RequiredOutputReportId = Has0303Layout(endpoint) ? (byte)0x05 : (byte)0x04
        };
    }

    private static async ValueTask<(byte[]? Payload, bool SawReply)> QueryAsync(
        IHidSession session, UlxQuery query, FrameTrace? trace, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        var token = deadline.Token;
        var request = FinalmouseProtocol.Encode(query);
        var report = new byte[FinalmouseProtocol.ReportLength];
        bool sawReply = false;
        try
        {
            await session.WriteOutputAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var count = await session.ReadInputAsync(report, token).ConfigureAwait(false);
                sawReply = true;
                trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                if (FinalmouseProtocol.TryReadPayload(report.AsSpan(0, count), query, out var payload))
                    return (payload.ToArray(), sawReply);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, sawReply);
        }
        return (null, sawReply);
    }
}

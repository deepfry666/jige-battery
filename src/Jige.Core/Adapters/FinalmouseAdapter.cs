namespace Jige.Core.Adapters;

internal sealed class FinalmouseAdapter : IDeviceAdapter
{
    private const string Name = "Finalmouse UltralightX";

    public static bool IsReceiver(HidInterface hid) => hid is { VendorId: 0x361D, ProductId: 0x0100 };

    private static bool IsVendorCollection(HidInterface hid) => IsReceiver(hid) &&
        hid is { UsagePage: 0xFF00, Usage: 0x0001, InputLength: FinalmouseProtocol.ReportLength,
            OutputLength: FinalmouseProtocol.ReportLength };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var group in inventory.Hid.Where(IsReceiver).GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.Where(IsVendorCollection).Take(2).ToArray();
            var endpoint = candidates.Length == 1 ? candidates[0] : group.First();
            if (candidates.Length == 0)
                endpoint = group.FirstOrDefault(hid => hid.MetadataError is not null) ?? endpoint;
            if (candidates.Length == 2)
                endpoint = endpoint with { MetadataError = "有多个匹配的 ULX 厂商集合", MetadataIssue = ReadIssue.IoError };
            yield return new(new($"ulx:{group.Key}", Name, DeviceKind.Mouse), endpoint, "0");
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(
        DeviceTarget target, IReceiverTransport transport, FrameTrace? trace, CancellationToken cancellationToken)
    {
        var endpoint = (HidInterface)target.Endpoint;
        if (endpoint.MetadataIssue != ReadIssue.None)
            return AdapterRegistry.Failure(target, endpoint.MetadataIssue, endpoint.MetadataError ?? "无法读取 HID 描述符");
        if (!IsVendorCollection(endpoint))
            return AdapterRegistry.Failure(target, ReadIssue.IoError, "未找到 FF00:0001、输入和输出均为 64 字节的 HID 集合");

        using var session = transport.OpenHid(endpoint, cancellationToken);
        var link = await QueryAsync(session, UlxQuery.Link, trace, cancellationToken).ConfigureAwait(false);
        if (link is null)
            return AdapterRegistry.Failure(target, ReadIssue.Timeout, "链路状态查询超时");
        if (link[0] == 0)
            return new(target.Device, null, false, ChargingState.Unknown, LinkState.Asleep, DateTimeOffset.UtcNow,
                Detail: "无线链路未连接，接收器无法区分休眠和关机");

        var battery = await QueryAsync(session, UlxQuery.Percentage, trace, cancellationToken).ConfigureAwait(false);
        int? level = battery is not null && battery[0] <= 100 ? battery[0] : null;
        var estimated = false;
        var malformed = battery is not null && level is null;
        if (level is null)
        {
            var voltage = await QueryAsync(session, UlxQuery.Voltage, trace, cancellationToken).ConfigureAwait(false);
            level = voltage is null ? null : FinalmouseProtocol.VoltagePercentage(voltage);
            estimated = level.HasValue;
            malformed |= voltage is not null && level is null;
        }

        var power = await QueryAsync(session, UlxQuery.Charging, trace, cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask<byte[]?> QueryAsync(
        IHidSession session, UlxQuery query, FrameTrace? trace, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        var token = deadline.Token;
        var request = FinalmouseProtocol.Encode(query);
        var report = new byte[FinalmouseProtocol.ReportLength];
        try
        {
            await session.WriteOutputAsync(request, token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await session.ReadInputAsync(report, token).ConfigureAwait(false);
                trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                if (FinalmouseProtocol.TryReadPayload(report.AsSpan(0, count), query, out var payload))
                    return payload.ToArray();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

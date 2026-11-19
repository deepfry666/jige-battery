using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;

namespace Jige.Core.Adapters;

internal sealed class LogitechHidppAdapter : IMultiDeviceAdapter
{
    private static int nextMarker;

    internal static bool UsesReceiverName(HidInterface hid) => !IsKnownReceiver(hid.ProductId) && IsLong(hid);

    private static bool IsKnownReceiver(ushort? pid) => pid is
        0xC539 or 0xC53A or 0xC53D or 0xC53F or 0xC541 or 0xC545 or 0xC547 or 0xC54D or
        0xC548 or 0xC52B or 0xC532;

    private static bool IsLong(HidInterface hid) => hid is
    {
        VendorId: 0x046D, ProductId: not null, UsagePage: 0xFF00, Usage: 2,
        InputLength: LogitechHidppProtocol.LongLength, OutputLength: LogitechHidppProtocol.LongLength,
        MetadataIssue: ReadIssue.None
    } && hid.ProductId is not (0x0AF7 or 0x0B18 or 0x0B21 or 0x0B45 or 0x0B1C or 0x0B0B) &&
        (IsKnownReceiver(hid.ProductId) || hid.Product is { } product &&
            (product.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
             product.Contains("dongle", StringComparison.OrdinalIgnoreCase) ||
             product.Contains("接收器", StringComparison.Ordinal)));

    private static bool IsShort(HidInterface hid) => hid is
    {
        VendorId: 0x046D, UsagePage: 0xFF00, Usage: 1,
        InputLength: LogitechHidppProtocol.ShortLength, OutputLength: LogitechHidppProtocol.ShortLength,
        MetadataIssue: ReadIssue.None
    };

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(IsLong)))
        {
            var id = $"logitech-hidpp:{endpoint.ReceiverId}";
            var name = $"Logitech HID++ 接收器 {endpoint.ProductId:X4}";
            var shortHid = HidQuery.UniqueCollections(inventory.Hid.Where(hid =>
                IsShort(hid) && hid.ProductId == endpoint.ProductId &&
                StringComparer.OrdinalIgnoreCase.Equals(hid.ReceiverId, endpoint.ReceiverId))).SingleOrDefault();
            yield return new(new(id, name, DeviceKind.Other), endpoint, "0") { ShortHid = shortHid };
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        var devices = await ReadDevicesAsync(target, transport, trace, token).ConfigureAwait(false);
        return devices.Count switch
        {
            1 => devices[0],
            0 => new(target.Device, null, false, ChargingState.Unknown, LinkState.Unknown, DateTimeOffset.UtcNow,
                Detail: "未确认接收器下有已配对的 HID++ 2.0 设备"),
            _ => new(target.Device, null, false, ChargingState.Unknown, LinkState.Online, DateTimeOffset.UtcNow,
                Detail: $"接收器下确认了 {devices.Count} 个槽位，请使用多设备读取接口")
        };
    }

    public async ValueTask<IReadOnlyList<BatteryReading>> ReadDevicesAsync(DeviceTarget target,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !IsLong(endpoint))
            return [AdapterRegistry.Failure(target, ReadIssue.IoError, "HID++ 长报告集合不匹配")];

        using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 0x11, RequiredOutputReportId = 0x11 }, token);
        bool known = IsKnownReceiver(endpoint.ProductId);
        bool hasPairingRoute = target.ShortHid is { } shortHid && IsShort(shortHid) &&
            shortHid.ProductId == endpoint.ProductId &&
            StringComparer.OrdinalIgnoreCase.Equals(shortHid.ReceiverId, endpoint.ReceiverId) &&
            endpoint.ProductId is 0xC548 or 0xC52B or 0xC532;

        List<BatteryReading> results = [];
        using var scanDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        scanDeadline.CancelAfter(TimeSpan.FromSeconds(6));
        var scanToken = scanDeadline.Token;
        IHidSession? shortSession = null;
        try
        {
            for (byte slot = 1; slot <= (known ? 6 : 1); slot++)
            {
                var marker = unchecked((byte)Interlocked.Increment(ref nextMarker));
                var ping = await QueryAsync(LogitechHidppProtocol.Ping(slot, marker)).ConfigureAwait(false);
                bool online = LogitechHidppProtocol.IsSuccess(ping) && ping![4] is >= 2 and < 0xFF;
                if (!known)
                {
                    // 未收录 PID 仅探测一次对应槽的 Root。即使应答正常也不调用动态索引。
                    var feature = online
                        ? await QueryAsync(LogitechHidppProtocol.GetFeature(slot, HidppFeature.Unified)).ConfigureAwait(false)
                        : null;
                    return [new(target.Device, null, false, ChargingState.Unknown,
                        online ? LinkState.Online : LinkState.Unknown, DateTimeOffset.UtcNow,
                        Detail: LogitechHidppProtocol.IsSuccess(feature)
                            ? $"未知 PID 的 Root 探测有回复，1004 索引 {feature![4]:X2}"
                            : "未知 PID 的只读探测未确认电量功能")];
                }

                var device = new DeviceIdentity($"{target.Device.Id}/{slot}/unknown",
                    $"Logitech 无线设备（槽 {slot}）", DeviceKind.Other);
                if (online)
                {
                    // 当前接收器句柄下，每个槽独立缓存。关闭句柄即丢弃，重新打开时重新确认索引。
                    var reader = new SlotReader(slot, QueryAsync);
                    results.Add(await reader.ReadAsync(device).ConfigureAwait(false));
                    continue;
                }

                // 无回复并不能证明槽已配对。仅用明确的不可达错误或可解析配对记录显示离线。
                bool unreachable = ping is not null && ping[2] == 0x8F && ping[5] is 0x04 or 0x09;
                if (!unreachable && ping is not null)
                    continue;
                if (hasPairingRoute)
                {
                    shortSession ??= transport.OpenHid(target.ShortHid! with { RequiredOutputReportId = 0x10 }, scanToken);
                    var request = LogitechHidppProtocol.ReadPairing(slot, endpoint.ProductId!.Value);
                    var pair = await HidQuery.OutputAsync(shortSession, session, request,
                        LogitechHidppProtocol.LongLength, reply => LogitechHidppProtocol.Matches(reply, request),
                        trace, scanToken, 200).ConfigureAwait(false);
                    if (LogitechHidppProtocol.IsSuccess(pair))
                    {
                        bool bolt = endpoint.ProductId == 0xC548;
                        ushort wpid = bolt ? BinaryPrimitives.ReadUInt16LittleEndian(pair.AsSpan(6))
                            : BinaryPrimitives.ReadUInt16BigEndian(pair.AsSpan(7));
                        if (wpid is not (0 or ushort.MaxValue))
                        {
                            var type = pair![bolt ? 5 : 11];
                            device = device with
                            {
                                Id = device.Id[..^"unknown".Length] + $"wpid-{wpid:X4}",
                                Kind = LogitechHidppProtocol.PairingKind((byte)(type & 0x0F))
                            };
                            unreachable = true;
                        }
                    }
                }
                if (unreachable)
                    results.Add(new(device, null, false, ChargingState.Unknown, LinkState.Offline, DateTimeOffset.UtcNow,
                        Detail: $"已配对槽 {slot} 当前不可达"));
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            results.Add(AdapterRegistry.Failure(target, ReadIssue.Timeout, "HID++ 接收器扫描超时"));
        }
        catch (TransportException error) when (results.Count > 0)
        {
            results.Add(AdapterRegistry.Failure(target, error.Issue, error.Message));
        }
        finally
        {
            shortSession?.Dispose();
        }
        return results;

        ValueTask<byte[]?> QueryAsync(byte[] request) => HidQuery.OutputAsync(session, session, request,
            LogitechHidppProtocol.LongLength, reply => LogitechHidppProtocol.Matches(reply, request),
            trace, scanToken, 200);
    }

    private static string IdentitySuffix(byte[]? info, DeviceIdentity device)
    {
        if (info is { Length: LogitechHidppProtocol.LongLength })
        {
            var unit = info.AsSpan(5, 4);
            if (unit.ContainsAnyExcept((byte)0) && unit.ContainsAnyExcept(byte.MaxValue))
            {
                // P1–P4 为 Unit ID，P6–P11 为型号 ID。两者结合可区分同槽换配对。
                byte[] identity = [.. unit, .. info.AsSpan(10, 6)];
                return $"unit-{Convert.ToHexString(SHA256.HashData(identity).AsSpan(0, 12))}";
            }
        }
        byte[] metadata = Encoding.UTF8.GetBytes($"{device.Kind}:{device.Name}");
        return $"model-{Convert.ToHexString(SHA256.HashData(metadata).AsSpan(0, 12))}";
    }

    private sealed class SlotReader(byte slot, Func<byte[], ValueTask<byte[]?>> query)
    {
        private readonly Dictionary<HidppFeature, byte> indices = [];

        private async ValueTask<byte[]?> ReadFeatureAsync(HidppFeature feature, byte function = 0, byte offset = 0)
        {
            if (!indices.TryGetValue(feature, out byte index))
            {
                var response = await query(LogitechHidppProtocol.GetFeature(slot, feature)).ConfigureAwait(false);
                if (!LogitechHidppProtocol.IsSuccess(response))
                    return null;
                index = response![4];
                indices.Add(feature, index);
            }
            if (!LogitechHidppProtocol.IsFeatureIndex(index))
                return null;
            var frame = await query(LogitechHidppProtocol.ReadFeature(slot, feature, index, function, offset)).ConfigureAwait(false);
            return LogitechHidppProtocol.IsSuccess(frame) ? frame : null;
        }

        public async ValueTask<BatteryReading> ReadAsync(DeviceIdentity device)
        {
            var size = await ReadFeatureAsync(HidppFeature.DeviceName).ConfigureAwait(false);
            if (size?[4] is > 0 and <= 64)
            {
                var name = new byte[size[4]];
                for (int offset = 0; offset < name.Length;)
                {
                    var chunk = await ReadFeatureAsync(HidppFeature.DeviceName, 1, (byte)offset).ConfigureAwait(false);
                    if (chunk is null)
                        break;
                    int capacity = chunk[0] == 0x10 ? 3 : 16;
                    int count = Math.Min(name.Length - offset, capacity);
                    chunk.AsSpan(4, count).CopyTo(name.AsSpan(offset));
                    offset += count;
                    if (offset == name.Length && !name.Contains((byte)0))
                        device = device with { Name = Encoding.UTF8.GetString(name) };
                }
            }

            var type = await ReadFeatureAsync(HidppFeature.DeviceName, 2).ConfigureAwait(false);
            if (type is not null)
                device = device with { Kind = LogitechHidppProtocol.NameKind(type[4]) };

            // Unit ID 只用于本机区分同型号的不同设备，不保存原始值。
            var info = await ReadFeatureAsync(HidppFeature.DeviceInfo).ConfigureAwait(false);
            string suffix = IdentitySuffix(info, device);
            device = device with { Id = device.Id[..^"unknown".Length] + suffix };

            var capabilities = await ReadFeatureAsync(HidppFeature.Unified).ConfigureAwait(false);
            if (capabilities is not null && (capabilities[5] & 0x02) != 0)
            {
                var reading = await ReadFeatureAsync(HidppFeature.Unified, 1).ConfigureAwait(false);
                if (reading is not null && reading[4] <= 100)
                    return Make(reading[4], false, (capabilities[5] & 1) != 0
                        ? LogitechHidppProtocol.Charging(reading[6]) : ChargingState.Unknown, $"HID++ 1004，状态 {reading[6]:X2}");
            }

            var legacy = await ReadFeatureAsync(HidppFeature.Battery).ConfigureAwait(false);
            if (legacy is not null && legacy[4] is > 0 and <= 100 && legacy[6] is not (5 or 6))
                return Make(legacy[4], false, LogitechHidppProtocol.Charging(legacy[6], legacy: true),
                    $"HID++ 1000，状态 {legacy[6]:X2}");

            var voltage = await ReadFeatureAsync(HidppFeature.Voltage).ConfigureAwait(false);
            if (voltage is not null)
            {
                int millivolts = BinaryPrimitives.ReadUInt16BigEndian(voltage.AsSpan(4));
                int? estimate = LogitechHidppProtocol.EstimateVoltage(millivolts);
                var charging = (voltage[6] & 0x80) == 0 ? ChargingState.NotCharging : (voltage[6] & 0x07) switch
                {
                    0 => ChargingState.Charging,
                    1 => ChargingState.Full,
                    2 => ChargingState.NotCharging,
                    _ => ChargingState.Unknown
                };
                return Make(estimate, estimate.HasValue, charging, $"HID++ 1001，{millivolts} mV，标志 {voltage[6]:X2}");
            }
            return Make(null, false, ChargingState.Unknown, "未获得有效 HID++ 电量回复");

            BatteryReading Make(int? percent, bool estimated, ChargingState state, string detail) =>
                new(device, percent, estimated, state, LinkState.Online, DateTimeOffset.UtcNow, Detail: detail);
        }
    }
}

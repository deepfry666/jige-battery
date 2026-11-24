using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Jige.Core.Diagnostics;
using Jige.Core.Windows;

namespace Jige.Core.Adapters;

internal sealed class LogitechHidppAdapter(SafetyGateStore? gates = null,
    IReadOnlyDictionary<string, DeviceIdentity>? savedSlots = null) : IMultiDeviceAdapter, IPushPresenceAdapter
{
    private static int nextMarker;
    private readonly SafetyGateStore safety = gates ?? new();
    private readonly Dictionary<string, EndpointState> endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceIdentity> startupSlots = savedSlots is null
        ? new(StringComparer.OrdinalIgnoreCase) : new(savedSlots, StringComparer.OrdinalIgnoreCase);

    private sealed class EndpointState
    {
        private readonly SlotState[] slots = Enumerable.Range(0, 7).Select(_ => new SlotState()).ToArray();
        // EndpointState is scoped to one HID path. Use the active Unit ID digest per slot
        // and evict the prior route on re-pairing; devices without Unit ID use their discovered identity.
        private readonly Dictionary<(byte Address, string Identity), BatteryQuery> batteryQueries = [];

        public SlotState Slot(byte address) => slots[address == 0xFF ? 0 : address];
        public bool Confirmed;

        public BatteryQuery? Query(byte address) => Slot(address).QueryIdentity is { } key
            ? batteryQueries.GetValueOrDefault((address, key)) : null;

        public void SetQuery(byte address, string? identity, BatteryQuery? query)
        {
            var slot = Slot(address);
            if (slot.QueryIdentity is { } previous)
                batteryQueries.Remove((address, previous));
            slot.QueryIdentity = identity;
            if (identity is not null && query is not null)
                batteryQueries.Add((address, identity), query);
        }
    }

    // 实机（MX Keys S / MX Anywhere 3S + Bolt，2026-11-20）：设备打盹时首条请求 200 ms 内收不到回复，稍后才回复。
    // HaloBattery 的 Ping 超时取 2 秒；这里首条请求给 1.5 秒，其后每条 600 ms。空槽由接收器立即回错误码，不受影响。
    internal const int WakeTimeoutMs = 1500;
    internal const int ReplyTimeoutMs = 600;

    private sealed class SlotState
    {
        public int Generation;
        public bool Disconnected;
        public bool Reconnect;
        public BatteryReading? Last;
        public int Misses;
        public DeviceIdentity? SavedDevice;
        public string? UnitDigest;
        public string? QueryIdentity;
    }

    private sealed record BatteryQuery(HidppFeature Feature, byte Index, byte Flags, byte Register = 0)
    {
        public byte[] Request(byte address) => Register != 0
            ? LogitechHidppProtocol.ReadRegister(address, Register)
            : LogitechHidppProtocol.ReadFeature(address, Feature, Index, Feature == HidppFeature.Unified ? (byte)1 : (byte)0);

        public string Label => Register != 0 ? $"寄存器 {Register:X2}" : $"{(ushort)Feature:X4}";

        public bool TryBattery(ReadOnlySpan<byte> reply, out int? level, out bool estimated,
            out ChargingState charging, out LinkState link)
        {
            level = null;
            estimated = false;
            charging = ChargingState.Unknown;
            link = LinkState.Online;
            if (Register != 0)
            {
                if (!LogitechHidppProtocol.TryRegisterBattery(reply, Register, out int percent, out estimated, out charging))
                    return false;
                level = percent;
                return true;
            }
            switch (Feature)
            {
                case HidppFeature.Unified:
                    if ((Flags & 2) != 0)
                    {
                        if (reply[4] > 100)
                            return false;
                        level = reply[4];
                    }
                    else
                    {
                        level = reply[5] switch { 8 => 100, 4 => 50, 2 => 20, 1 => 5, _ => (int?)null };
                        estimated = level.HasValue;
                        if (!estimated)
                            return false;
                    }
                    if ((Flags & 1) != 0)
                        charging = LogitechHidppProtocol.Charging(reply[6]);
                    return true;
                case HidppFeature.Battery:
                    if (reply[4] > 100)
                        return false;
                    charging = LogitechHidppProtocol.Charging(reply[6], legacy: true);
                    if (charging == ChargingState.Unknown)
                        return false;
                    if (reply[4] == 0)
                    {
                        if (charging == ChargingState.NotCharging)
                            return false;
                        // HID++ 2.0 0x1000（BATTERY_STATUS）：P0=0 表示当前无有效放电百分比（如 USB 充电/充满时返回 00 00 01 / 00 00 03），
                        // 不误报为 0%，也不编造固定百分比；保留有效的充电/充满状态。
                        level = null;
                        return true;
                    }
                    level = reply[4];
                    return true;
                case HidppFeature.Adc when (reply[6] & 1) == 0:
                    link = LinkState.Offline;
                    return true;
                case HidppFeature.Adc:
                    charging = (reply[6] & 2) != 0 ? ChargingState.Charging : ChargingState.NotCharging;
                    break;
                case HidppFeature.Voltage:
                    charging = (reply[6] & 0x80) == 0 ? ChargingState.NotCharging : (reply[6] & 7) switch
                    {
                        0 => ChargingState.Charging,
                        1 => ChargingState.Full,
                        2 => ChargingState.NotCharging,
                        _ => ChargingState.Unknown
                    };
                    break;
            }
            level = LogitechHidppProtocol.EstimateVoltage(BinaryPrimitives.ReadUInt16BigEndian(reply[4..]));
            estimated = level.HasValue;
            return estimated;
        }

        public string Summary(ReadOnlySpan<byte> reply) => Register switch
        {
            0x0D => $"{reply[4]:X2} {reply[6]:X2}",
            0x07 => $"{reply[4]:X2} {reply[5]:X2}",
            _ => Convert.ToHexString(reply.Slice(4, Feature == HidppFeature.Unified && reply[0] == 0x11 ? 4 : 3))
        };
    }

    public void ResetCache()
    {
        lock (endpoints)
            endpoints.Clear();
    }

    public PresenceProbeKind PresenceKind => PresenceProbeKind.PushListener;

    public ValueTask<PresenceUpdate?> CheckPresenceAsync(
        DeviceTarget target, IReceiverTransport transport, CancellationToken token) =>
        ValueTask.FromResult<PresenceUpdate?>(null);

    private static bool IsLong(HidInterface hid) => hid is
    {
        VendorId: 0x046D, ProductId: not null, InputLength: LogitechHidppProtocol.LongLength,
        OutputLength: LogitechHidppProtocol.LongLength, MetadataIssue: ReadIssue.None
    } && DeviceCatalog.FindDisabledPath(hid.VendorId, hid.ProductId) is null &&
        (hid is { UsagePage: 0xFF00, Usage: 2 } or { UsagePage: 0xFF43, Usage: 0x0202 } ||
            hid is { ProductId: 0x0AC4, UsagePage: 0x000C, Usage: 1 });

    private static bool IsShort(HidInterface hid) => hid is
    {
        VendorId: 0x046D, UsagePage: 0xFF00, Usage: 1,
        InputLength: LogitechHidppProtocol.ShortLength, OutputLength: 0 or LogitechHidppProtocol.ShortLength,
        MetadataIssue: ReadIssue.None
    };

    private static bool ReceiverLabel(string? product) =>
        product is not null && (product.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
            product.Contains("dongle", StringComparison.OrdinalIgnoreCase) || product.Contains("接收器", StringComparison.Ordinal));

    internal static bool UsesReceiverName(HidInterface hid) => IsLong(hid) && hid is { UsagePage: 0xFF00, Usage: 2 } &&
        DeviceCatalog.LogitechReceiverFamily(hid.ProductId) is null && ReceiverLabel(hid.Product);

    internal static bool IsBluetooth(HidInterface hid) =>
        hid.BluetoothHid || hid.Bus == HidBus.Bluetooth || hid.BluetoothAddress.HasValue || BluetoothHidPath.UsbVendorId(hid.Path) == 0x046D;

    internal static bool IsHeadset(HidInterface hid) => IsLong(hid) &&
        DeviceCatalog.LogitechHidppHeadset(hid.ProductId) is not null &&
        (hid.ProductId == 0x0AC4 ? hid is { UsagePage: 0x000C, Usage: 1 } : hid is { UsagePage: 0xFF43, Usage: 0x0202 });

    public IEnumerable<DeviceTarget> Detect(DeviceInventory inventory)
    {
        var targets = new List<DeviceTarget>();
        foreach (var endpoint in HidQuery.UniqueCollections(inventory.Hid.Where(IsLong)))
        {
            var family = DeviceCatalog.LogitechReceiverFamily(endpoint.ProductId);
            string? headset = IsHeadset(endpoint) ? DeviceCatalog.LogitechHidppHeadset(endpoint.ProductId) : null;
            bool bluetooth = IsBluetooth(endpoint) && endpoint is { UsagePage: 0xFF43, Usage: 0x0202 };
            bool usbCollection = endpoint is { UsagePage: 0xFF00, Usage: 2 } && !IsBluetooth(endpoint);
            bool receiver = usbCollection && (family is not null || ReceiverLabel(endpoint.Product));
            if (safety.IsBlocked(endpoint, out _) || (!bluetooth && !usbCollection && headset is null))
                continue;

            var shortCandidates = inventory.Hid.Where(hid => IsShort(hid) &&
                hid.ProductId == endpoint.ProductId && SameReceiver(hid, endpoint));
            var shortHid = HidQuery.UniqueCollections(shortCandidates.Where(hid =>
                hid.OutputLength == LogitechHidppProtocol.ShortLength)).SingleOrDefault();
            if (receiver && shortHid is null)
                shortHid = HidQuery.UniqueCollections(shortCandidates).SingleOrDefault();
            bool cable = usbCollection && !receiver && shortHid is not null;
            if (!bluetooth && !receiver && !cable && headset is null)
                continue;

            var connection = bluetooth ? DeviceConnection.Bluetooth : cable ? DeviceConnection.UsbCable : DeviceConnection.Receiver;
            string id = bluetooth ? BluetoothBattery.DeviceId(endpoint.ReceiverId, endpoint.BluetoothAddress) :
                receiver ? $"logitech-hidpp:{endpoint.ReceiverId}" : $"logitech-hidpp-direct:{endpoint.ReceiverId}";
            string name = receiver ? $"Logitech {family ?? "HID++"} 接收器 {endpoint.ProductId:X4}" :
                headset ?? (bluetooth ? BluetoothBattery.UnnamedHidpp : endpoint.Product ?? "Logitech 无线设备");
            string sourceLabel = receiver ? $"罗技 {family ?? "HID++"} 接收器" :
                bluetooth ? "罗技蓝牙" : cable ? "罗技 USB 直连" : "罗技无线接收器";
            var device = new DeviceIdentity(id, name, headset is null ? DeviceKind.Other : DeviceKind.Headset,
                family is null && headset is null ? AdaptationSource.Generic : AdaptationSource.Catalog, Connection: connection)
            {
                BluetoothContainerKey = bluetooth ? BluetoothBattery.DeviceId(endpoint.ReceiverId, null) : null,
                SourceLabel = sourceLabel
            };
            targets.Add(new(device, endpoint, receiver ? "0" : "255") { ShortHid = shortHid });
        }

        if (inventory.Warnings.Count == 0)
        {
            var live = targets.Select(target => target.Endpoint.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (endpoints)
            {
                foreach (string path in endpoints.Keys.Where(path => !live.Contains(path)).ToArray())
                    endpoints.Remove(path);
            }
        }
        return targets;
    }

    private static bool SameReceiver(HidInterface first, HidInterface second) =>
        StringComparer.OrdinalIgnoreCase.Equals(first.ReceiverId, second.ReceiverId);

    private EndpointState State(HidInterface endpoint, DeviceTarget target)
    {
        lock (endpoints)
        {
            if (!endpoints.TryGetValue(endpoint.Path, out var state))
            {
                endpoints.Add(endpoint.Path, state = new());
                if (target.Slot == "0")
                {
                    for (byte slot = 1; slot <= 6; slot++)
                        if (startupSlots.Remove($"{target.Device.Id}/{slot}", out var device))
                            state.Slot(slot).SavedDevice = device;
                }
            }
            return state;
        }
    }

    public async Task ListenPushAsync(DeviceTarget target, IReceiverTransport transport, Action<PresenceUpdate> onUpdate,
        CancellationToken token)
    {
        if (target.Endpoint is not HidInterface endpoint || !IsLong(endpoint) || safety.IsBlocked(endpoint, out _))
            return;
        var state = State(endpoint, target);
        if ((UsesReceiverName(endpoint) || target.Device.Source == AdaptationSource.Generic) &&
            !Volatile.Read(ref state.Confirmed))
            return;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var longListener = ListenAsync(endpoint, 0x11);
        var listeners = new List<Task> { longListener };
        if (target.ShortHid is { } shortHid)
            listeners.Add(ListenShortAsync(shortHid));

        try
        {
            await await Task.WhenAny(listeners).ConfigureAwait(false);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(listeners).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        async Task ListenShortAsync(HidInterface input)
        {
            try
            {
                await ListenAsync(input, 0x10).ConfigureAwait(false);
            }
            catch (TransportException error) when (OptionalChannelUnavailable(error))
            {
                // 短集合不可用时，保留可用的长集合推送监听，直到取消或拔出。
                await longListener.ConfigureAwait(false);
            }
        }

        async Task ListenAsync(HidInterface input, byte reportId)
        {
            using var session = transport.OpenHid(input with { RequiredInputReportId = reportId, InputOnly = true },
                lifetime.Token);
            var buffer = new byte[input.InputLength!.Value];
            while (!lifetime.IsCancellationRequested)
            {
                int count = await session.ReadInputAsync(buffer, lifetime.Token).ConfigureAwait(false);
                if (count == 0)
                    return;
                if (HandleEvent(target, state, buffer.AsSpan(0, count), allowBattery: true) is { } update)
                    onUpdate(update);
            }
        }
    }

    private static PresenceUpdate? HandleEvent(DeviceTarget target, EndpointState state, ReadOnlySpan<byte> report,
        bool allowBattery)
    {
        var endpoint = (HidInterface)target.Endpoint;
        lock (state)
        {
            if (target.Slot == "0" && LogitechHidppProtocol.TryParseConnectionEvent(report, out byte address, out bool connected))
            {
                var slot = state.Slot(address);
                slot.Generation++;
                state.SetQuery(address, null, null);
                slot.Disconnected = !connected;
                slot.Reconnect = true;
                return new(endpoint.ReceiverId, address, (slot.Last?.Device ?? slot.SavedDevice)?.Id,
                    connected ? LinkState.Online : LinkState.Offline);
            }
            if (!allowBattery || !LogitechHidppProtocol.HasReportShape(report) ||
                !LogitechHidppProtocol.IsDeviceAddress(report[1]) || (target.Slot == "0") == (report[1] == 0xFF) ||
                report[3] != 0)
                return null;

            var batterySlot = state.Slot(report[1]);
            if (batterySlot.Disconnected || batterySlot.Last is not { } last ||
                state.Query(report[1]) is not { Feature: HidppFeature.Unified or HidppFeature.Battery } query ||
                report[2] != query.Index || !query.TryBattery(report, out int? level, out bool estimated, out var charging, out var link) ||
                (level is null && charging is not (ChargingState.Charging or ChargingState.Full)))
                return null;
            var reading = Reading(last.Device, level, estimated, charging, link, last.ProtocolSummary, endpoint);
            batterySlot.Last = reading;
            return new(endpoint.ReceiverId, report[1] == 0xFF ? null : report[1], reading.Device.Id, link) { Battery = reading };
        }
    }

    public async ValueTask<BatteryReading> ReadAsync(DeviceTarget target, IReceiverTransport transport,
        FrameTrace? trace, CancellationToken token)
    {
        var devices = await ReadDevicesAsync(target, transport, trace, token).ConfigureAwait(false);
        return devices.Count == 1 ? devices[0] : AdapterRegistry.Failure(target, ReadIssue.None,
            devices.Count == 0 ? "未确认有已配对的 HID++ 设备" : $"已确认 {devices.Count} 个槽位，请使用多设备读取接口");
    }

    public ValueTask<IReadOnlyList<BatteryReading>> ReadDevicesAsync(DeviceTarget target,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token) =>
        ReadSelectedAsync(target, transport, trace, token, onlyDeviceIds: null);

    internal async ValueTask<IReadOnlyList<BatteryReading>> ReadSelectedAsync(DeviceTarget target,
        IReceiverTransport transport, FrameTrace? trace, CancellationToken token, IReadOnlySet<string>? onlyDeviceIds)
    {
        if (target.Endpoint is not HidInterface endpoint || !IsLong(endpoint) || safety.IsBlocked(endpoint, out _))
            return [AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "HID++ 集合未通过安全检查")];

        bool receiver = target.Slot == "0";
        var state = State(endpoint, target);
        var addresses = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : [0xFF];
        if (onlyDeviceIds is not null)
        {
            lock (state)
            {
                addresses = addresses.Where(address =>
                {
                    string? knownId = state.Slot(address).Last?.Device.Id;
                    if (knownId is not null && onlyDeviceIds.Contains(knownId))
                        return true;
                    if (!receiver)
                    {
                        if (onlyDeviceIds.Contains(target.Device.Id) ||
                            (target.Device.BluetoothContainerKey is { } btKey && onlyDeviceIds.Contains(btKey)))
                            return true;
                        return onlyDeviceIds.Any(id => id.StartsWith($"{target.Device.Id}/", StringComparison.OrdinalIgnoreCase));
                    }
                    return onlyDeviceIds.Any(id => id.StartsWith($"{target.Device.Id}/{address}/", StringComparison.OrdinalIgnoreCase));
                }).ToArray();
            }
            if (addresses.Length == 0)
                return [];
        }
        var results = new List<BatteryReading>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        CancellationToken scanToken = deadline.Token;

        try
        {
            using var session = transport.OpenHid(endpoint with { RequiredInputReportId = 0x11, RequiredOutputReportId = 0x11 }, scanToken);
            using var shortOutput = OpenShort(inputOnly: false);
            using var shortInputOnly = shortOutput is null ? OpenShort(inputOnly: true) : null;
            var shortSession = shortOutput ?? shortInputOnly;
            bool guarded = (UsesReceiverName(endpoint) || target.Device.Source == AdaptationSource.Generic) &&
                !Volatile.Read(ref state.Confirmed);
            bool invalidPing = false;
            var features = new Dictionary<(byte, byte), HidppFeature>();
            foreach (byte slot in addresses)
            {
                var result = await ReadSlotAsync(slot).ConfigureAwait(false);
                if (invalidPing)
                {
                    safety.Block(endpoint, "首个 HID++ Ping 应答格式不匹配");
                    Forget();
                    return [AdapterRegistry.Failure(target, ReadIssue.InvalidResponse, "首个 HID++ Ping 应答格式不匹配")];
                }
                if (result is not null)
                    results.Add(result);
            }

            async ValueTask<BatteryReading?> ReadSlotAsync(byte slot)
            {
                SlotState entry = state.Slot(slot);
                BatteryReading? previous;
                DeviceIdentity? known;
                BatteryQuery? cached;
                int generation;
                bool disconnected;
                bool reconnect;
                string? unit;
                lock (state)
                {
                    previous = entry.Last;
                    known = previous?.Device ?? entry.SavedDevice;
                    cached = state.Query(slot);
                    unit = entry.UnitDigest;
                    generation = entry.Generation;
                    disconnected = entry.Disconnected;
                    reconnect = entry.Reconnect;
                }

                var device = reconnect ? target.Device : previous?.Device ?? target.Device;
                if (device.SourceLabel is null && target.Device.SourceLabel is not null)
                    device = device with { SourceLabel = target.Device.SourceLabel };
                if (receiver && (previous is null || reconnect))
                    device = device with { Id = $"{device.Id}/{slot}/unknown", Name = $"Logitech 无线设备（槽 {slot}）" };
                string route = receiver ? $"接收器槽位 {slot}" : target.Device.Connection switch
                {
                    DeviceConnection.Bluetooth => "蓝牙 HID++",
                    DeviceConnection.UsbCable => "线缆",
                    _ => "耳机接收器"
                };

                string version = "未知";
                var indices = new Dictionary<HidppFeature, byte>();
                bool Unreachable(byte[]? reply) => receiver && LogitechHidppProtocol.IsUnreachable(reply);

                if (cached is not null)
                {
                    // 每个槽本轮的第一条请求给足唤醒时间：罗技设备打盹时，无线电要约 0.5 秒才回复。
                    var reply = await QueryAsync(cached.Request(slot), WakeTimeoutMs).ConfigureAwait(false);
                    if (ParseBattery(cached, reply, previous?.ProtocolSummary) is { } updated)
                        return Remember(updated, cached);
                    if (reply is null && HoldOnline() is { } held)
                        return held;
                    if (receiver && LogitechHidppProtocol.LegacyError(reply) == 0x08)
                        return Remember(Unavailable(true, previous?.ProtocolSummary), null);
                    if (reply is null || Unreachable(reply))
                        return Remember(Unavailable(Unreachable(reply), previous?.ProtocolSummary),
                            reply is null ? cached : null);
                    // 功能索引被设备拒绝，下轮重新 Ping 和查表。
                    return Remember(Unavailable(false, previous?.ProtocolSummary), null);
                }

                byte marker = unchecked((byte)Interlocked.Increment(ref nextMarker));
                var ping = await QueryAsync(LogitechHidppProtocol.Ping(slot, marker), WakeTimeoutMs).ConfigureAwait(false);
                byte? legacyError = LogitechHidppProtocol.LegacyError(ping);
                if (receiver && legacyError == 0x08)
                    return Remember(Unavailable(true), null);
                if (Unreachable(ping))
                    return Remember(Unavailable(true), null);
                if (ping is null)
                    return HoldOnline() ?? Remember(Unavailable(disconnected), null);

                if (legacyError == 0x01)
                {
                    version = "1.0";
                    unit = null;
                    foreach (byte register in new byte[] { 0x0D, 0x07 })
                    {
                        var legacy = new BatteryQuery(default, 0, 0, register);
                        var reply = await QueryAsync(legacy.Request(slot)).ConfigureAwait(false);
                        if (ParseBattery(legacy, reply) is { } reading)
                            return Remember(reading, legacy);
                        if (Unreachable(reply))
                            return Remember(Unavailable(true), null);
                    }
                    return Remember(Reading(device, null, false, ChargingState.Unknown, LinkState.Online, Summary(), endpoint), null);
                }
                if (!LogitechHidppProtocol.IsSuccess(ping) || ping[4] is < 2 or 0xFF)
                {
                    device = previous?.Device ?? device;
                    return previous is null && receiver ? null : Remember(Unavailable(disconnected, previous?.ProtocolSummary), null);
                }

                version = $"{ping[4]}.{ping[5]}";
                string? detectedUnit = UnitDigest(await ReadFeatureAsync(HidppFeature.DeviceInfo).ConfigureAwait(false));
                if (detectedUnit is not null || reconnect)
                    unit = detectedUnit;
                var size = await ReadFeatureAsync(HidppFeature.DeviceName).ConfigureAwait(false);
                if (size?[4] is > 0 and <= 64)
                {
                    var name = new byte[size[4]];
                    for (int offset = 0; offset < name.Length;)
                    {
                        var chunk = await ReadFeatureAsync(HidppFeature.DeviceName, 1, (byte)offset).ConfigureAwait(false);
                        if (chunk is null)
                            break;
                        int count = Math.Min(name.Length - offset, chunk[0] == 0x10 ? 3 : 16);
                        chunk.AsSpan(4, count).CopyTo(name.AsSpan(offset));
                        offset += count;
                        if (offset == name.Length && !name.Contains((byte)0))
                        {
                            string decoded = Encoding.UTF8.GetString(name).Trim();
                            if (decoded.Length > 0 && !decoded.Any(char.IsControl))
                                device = device with { Name = decoded, NameFromHidpp = device.Connection == DeviceConnection.Bluetooth };
                        }
                    }
                }
                var type = await ReadFeatureAsync(HidppFeature.DeviceName, 2).ConfigureAwait(false);
                if (type is not null && device.Kind != DeviceKind.Headset)
                    device = device with { Kind = LogitechHidppProtocol.NameKind(type[4]) };
                if (device.Connection != DeviceConnection.Bluetooth)
                {
                    string? suffix = unit ?? ((previous is null || reconnect) && receiver ? ModelDigest(device) : null);
                    if (suffix is not null)
                        device = device with { Id = receiver ? $"{target.Device.Id}/{slot}/{suffix}" : $"{target.Device.Id}/{suffix}" };
                }

                HidppFeature[] batteryFeatures = device.Kind == DeviceKind.Headset
                    ? [HidppFeature.Unified, HidppFeature.Adc, HidppFeature.Battery, HidppFeature.Voltage]
                    : [HidppFeature.Unified, HidppFeature.Battery, HidppFeature.Voltage];
                (BatteryReading Reading, BatteryQuery Query)? chargeOnlyFallback = null;
                foreach (var feature in batteryFeatures)
                {
                    byte index = await FindFeatureAsync(feature).ConfigureAwait(false);
                    if (!LogitechHidppProtocol.IsFeatureIndex(index))
                        continue;
                    byte flags = 0;
                    if (feature == HidppFeature.Unified)
                    {
                        var caps = await ReadFeatureAsync(feature).ConfigureAwait(false);
                        if (caps is null)
                            continue;
                        flags = caps[5];
                    }
                    var query = new BatteryQuery(feature, index, flags);
                    var frame = await QueryAsync(query.Request(slot)).ConfigureAwait(false);
                    if (ParseBattery(query, frame) is { } reading)
                    {
                        if (reading.Percentage is not null)
                        {
                            if (chargeOnlyFallback is { } prior && reading.Charging == ChargingState.Unknown)
                                reading = reading with { Charging = prior.Reading.Charging };
                            return Remember(reading, query);
                        }
                        chargeOnlyFallback ??= (reading, query);
                        continue;
                    }
                    if (Unreachable(frame))
                        return Remember(Unavailable(true), null);
                }
                if (chargeOnlyFallback is { } fallback)
                    return Remember(fallback.Reading, fallback.Query);
                return Remember(Reading(device, null, false, ChargingState.Unknown, LinkState.Online, Summary(), endpoint), null);

                async ValueTask<byte> FindFeatureAsync(HidppFeature feature)
                {
                    if (indices.TryGetValue(feature, out byte index))
                        return index;
                    var response = await QueryAsync(LogitechHidppProtocol.GetFeature(slot, feature)).ConfigureAwait(false);
                    index = LogitechHidppProtocol.IsSuccess(response) ? response![4] : (byte)0;
                    indices.Add(feature, index);
                    return index;
                }

                async ValueTask<byte[]?> ReadFeatureAsync(HidppFeature feature, byte function = 0, byte offset = 0)
                {
                    byte index = await FindFeatureAsync(feature).ConfigureAwait(false);
                    if (!LogitechHidppProtocol.IsFeatureIndex(index))
                        return null;
                    var frame = await QueryAsync(LogitechHidppProtocol.ReadFeature(slot, feature, index, function, offset))
                        .ConfigureAwait(false);
                    return LogitechHidppProtocol.IsSuccess(frame) ? frame : null;
                }

                string Summary() => $"{route}；HID++ {version}；功能索引 " +
                    (indices.Count == 0 ? "—" : string.Join("、", indices.Where(item => LogitechHidppProtocol.IsFeatureIndex(item.Value))
                        .Select(item => $"{(ushort)item.Key:X4}={item.Value:X2}").DefaultIfEmpty("—")));

                // 刚在线的设备只超时一次时保留在线：打字、移动时偶发一次超时不应让图标在在线和休眠之间来回跳。
                // 连续第二次没有回复才按休眠处理。
                BatteryReading? HoldOnline()
                {
                    lock (state)
                    {
                        if (previous is not { Link: LinkState.Online } || entry.Generation != generation || entry.Misses > 0)
                            return null;
                        entry.Misses = 1;
                        return previous;
                    }
                }

                BatteryReading Unavailable(bool offline, string? summary = null, DeviceIdentity? identity = null) =>
                    Reading(identity ?? known ?? device, null, false, ChargingState.Unknown,
                        offline ? LinkState.Offline : LinkState.Asleep,
                        summary ?? Summary(), endpoint, offline ? ReadIssue.None : ReadIssue.Timeout);

                BatteryReading? ParseBattery(BatteryQuery query, byte[]? reply, string? oldSummary = null)
                {
                    if (!LogitechHidppProtocol.IsSuccess(reply) ||
                        !query.TryBattery(reply, out var percent, out bool estimated, out var charging, out var link))
                        return null;
                    var summary = oldSummary?.Split("；电量 ", 2)[0] ?? Summary();
                    return Reading(device, percent, estimated, charging, link,
                        $"{summary}；电量 {query.Label}，回复载荷 {query.Summary(reply!)}", endpoint);
                }

                BatteryReading? Remember(BatteryReading reading, BatteryQuery? query)
                {
                    lock (state)
                    {
                        if (entry.Generation != generation)
                        {
                            var current = entry.Last?.Device ?? entry.SavedDevice;
                            return receiver && current is null ? null : Unavailable(entry.Disconnected, identity: current);
                        }
                        // 在线但暂时无读数时仍可输出诊断信息。只有读到过电量或明确充电状态后才记住槽位。
                        bool hasBatterySignal = reading.Percentage is not null ||
                            reading.Link == LinkState.Online && reading.Charging is ChargingState.Charging or ChargingState.Full;
                        if (receiver && !hasBatterySignal)
                        {
                            if (known is null)
                                return reading.Link == LinkState.Online ? reading : null;
                            if (!StringComparer.OrdinalIgnoreCase.Equals(reading.Device.Id, known.Id))
                            {
                                if (reading.Link != LinkState.Online)
                                    return null;
                                if (!reading.Device.Id.EndsWith("/unknown", StringComparison.Ordinal))
                                    return reading;
                                reading = reading with { Device = known };
                            }
                            // 首轮只有链路状态时保留启动时的身份，用真实读数重新确认设备。
                            if (previous is null)
                                return reading;
                        }
                        entry.Last = reading;
                        entry.SavedDevice = null;
                        entry.UnitDigest = unit;
                        state.SetQuery(slot, query is null ? null : unit ?? reading.Device.Id, query);
                        if (reading.Link == LinkState.Online)
                        {
                            entry.Disconnected = false;
                            entry.Reconnect = false;
                            entry.Misses = 0;
                        }
                        return reading;
                    }
                }
            }

            IHidSession? OpenShort(bool inputOnly)
            {
                if (target.ShortHid is not { } hid || !IsShort(hid) || hid.ProductId != endpoint.ProductId || !SameReceiver(endpoint, hid) ||
                    !inputOnly && hid.OutputLength != LogitechHidppProtocol.ShortLength)
                    return null;
                try
                {
                    return transport.OpenHid(hid with
                    {
                        InputOnly = inputOnly, RequiredInputReportId = 0x10, RequiredOutputReportId = inputOnly ? null : (byte)0x10
                    }, scanToken);
                }
                catch (TransportException error) when (OptionalChannelUnavailable(error))
                {
                    return null;
                }
            }

            async ValueTask<byte[]?> QueryAsync(byte[] request, int timeoutMs = ReplyTimeoutMs)
            {
                bool register = request[0] == 0x10;
                if (register && shortOutput is null)
                    return null;
                bool ping = LogitechHidppProtocol.IsPing(request);
                bool malformed = false;
                var response = await QueryCollectionsAsync(register ? shortOutput! : session, session, shortSession, request, frame =>
                {
                    if (LogitechHidppProtocol.Matches(frame, request))
                        return true;
                    HandleEvent(target, state, frame, allowBattery: false);
                    if (guarded && ping && !LogitechHidppProtocol.HasReportShape(frame))
                        malformed = true;
                    return false;
                }, trace is null ? null : (direction, bytes) => trace(direction, direction == FrameDirection.Sent
                    ? bytes : DiagnosticPrivacy.HidReply(this, endpoint, bytes, request, features)), scanToken, timeoutMs).ConfigureAwait(false);

                if (guarded && ping)
                {
                    byte? error = LogitechHidppProtocol.Error(response);
                    invalidPing = response is null ? malformed : error is null && response[4] is < 2 or 0xFF;
                    if (!invalidPing && response is not null)
                    {
                        Volatile.Write(ref state.Confirmed, true);
                        guarded = false;
                    }
                }
                return response;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // 无论前面是否已读出部分槽位，只要接收器扫描超时就附带接收器级超时标记，防止未扫描到的槽位被误扫离线。
            results.Add(AdapterRegistry.Failure(target, ReadIssue.Timeout, "HID++ 接收器扫描超时"));
        }
        catch (TransportException error) when (target.Device.Connection == DeviceConnection.Bluetooth &&
            error.Issue is ReadIssue.Disconnected or ReadIssue.IoError)
        {
            // Windows HID 句柄暂时不可用不能证明无线链路断开，由 Windows 蓝牙连接属性决定。
            lock (state)
            {
                var last = state.Slot(0xFF).Last;
                results.Add(Reading(last?.Device ?? target.Device, null, false, ChargingState.Unknown,
                    LinkState.Asleep, last?.ProtocolSummary, endpoint, ReadIssue.IoError));
            }
        }
        catch (TransportException error)
        {
            if (error.Issue == ReadIssue.Disconnected)
            {
                Forget();
                results.Clear();
            }
            results.Add(Reading(target.Device, null, false, ChargingState.Unknown,
                error.Issue switch
                {
                    ReadIssue.Disconnected => LinkState.Offline,
                    ReadIssue.Timeout => LinkState.Asleep,
                    ReadIssue.Busy => LinkState.Busy,
                    _ => LinkState.Unknown
                }, null, endpoint, error.Issue));
        }
        return results;

        void Forget()
        {
            lock (endpoints)
                endpoints.Remove(endpoint.Path);
        }
    }

    private static bool OptionalChannelUnavailable(TransportException error) =>
        error.Issue is ReadIssue.Busy or ReadIssue.AccessDenied or ReadIssue.IoError;

    private static async ValueTask<byte[]?> QueryCollectionsAsync(IHidSession output, IHidSession longInput,
        IHidSession? shortInput, byte[] request, Func<byte[], bool> matches, FrameTrace? trace, CancellationToken token,
        int timeoutMs = ReplyTimeoutMs)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        var work = deadline.Token;
        var reads = new List<Task<byte[]?>>(2);
        try
        {
            await output.WriteOutputAsync(request, work).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            bool preferShort = request[0] == 0x10;
            var longTask = ReadAsync(longInput, LogitechHidppProtocol.LongLength);
            reads.Add(longTask);
            Task<byte[]?>? shortTask = shortInput is not null
                ? ReadAsync(shortInput, LogitechHidppProtocol.ShortLength)
                : null;
            if (shortTask is not null)
                reads.Add(shortTask);

            var pending = new List<Task<byte[]?>>(reads);
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                try
                {
                    return await completed.ConfigureAwait(false);
                }
                catch (TransportException) when (pending.Count > 0 &&
                    ((!preferShort && completed == shortTask) || (preferShort && completed == longTask)))
                {
                    // 辅助监听通道异常时继续等待主目标通道回复，不提前中断本次读取。
                }
            }
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            await ((Task)Task.WhenAll(reads)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        async Task<byte[]?> ReadAsync(IHidSession input, int length)
        {
            var buffer = new byte[length];
            while (true)
            {
                int count = await input.ReadInputAsync(buffer, work).ConfigureAwait(false);
                work.ThrowIfCancellationRequested();
                if (count == 0)
                    throw new TransportException(ReadIssue.Disconnected, "HID 输入通道已关闭");
                var frame = buffer[..count];
                lock (reads)
                {
                    trace?.Invoke(FrameDirection.Received, frame);
                    if (matches(frame))
                        return frame;
                }
            }
        }
    }

    private static BatteryReading Reading(DeviceIdentity device, int? percent, bool estimated, ChargingState charging,
        LinkState link, string? summary, HidInterface endpoint, ReadIssue issue = ReadIssue.None) =>
        new(device, percent, estimated, charging, link, DateTimeOffset.UtcNow, issue)
        {
            ProtocolSummary = summary,
            BluetoothContainer = device.Connection == DeviceConnection.Bluetooth ? endpoint.ReceiverId : null,
            BluetoothAddress = endpoint.BluetoothAddress
        };

    private static string? UnitDigest(byte[]? info)
    {
        if (info is not { Length: LogitechHidppProtocol.LongLength } || info[0] != 0x11)
            return null;
        var unit = info.AsSpan(5, 4);
        if (!unit.ContainsAnyExcept((byte)0) || !unit.ContainsAnyExcept(byte.MaxValue))
            return null;
        byte[] identity = [.. unit, .. info.AsSpan(10, 6)];
        return $"unit-{Convert.ToHexString(SHA256.HashData(identity).AsSpan(0, 12))}";
    }

    private static string ModelDigest(DeviceIdentity device) =>
        $"model-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{device.Kind}:{device.Name}")).AsSpan(0, 12))}";
}

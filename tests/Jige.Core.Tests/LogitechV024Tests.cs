using System.Text;
using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using Jige.Core.Windows;
using static Jige.Core.Tests.LogitechBatteryTests;

namespace Jige.Core.Tests;

public sealed class LogitechV024Tests
{
    private static readonly HidInterface Nano = new("receiver", "long", 0x046D, 0xC52F,
        null, 0xFF00, 2, 20, 20, 0, 2);

    [Theory]
    [InlineData(0x0D, "564D50", 86, false, (int)ChargingState.Charging)]
    [InlineData(0x07, "0522AA", 50, true, (int)ChargingState.Full)]
    [InlineData(0x07, "0321AA", 20, true, (int)ChargingState.Charging)]
    [InlineData(0x07, "0523AA", 50, true, (int)ChargingState.Charging)]
    [InlineData(0x07, "0100AA", 5, true, (int)ChargingState.NotCharging)]
    [InlineData(0x07, "0700AA", 100, true, (int)ChargingState.NotCharging)]
    public async Task LegacyPingErrorEnablesOnlyWhitelistedBatteryRegisters(
        int register, string payload, int percentage, bool estimate, int charging)
    {
        var shortHid = Nano with { Path = "short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var shortSession = new BatteryHidStub(request => request[3] == register
            ? [Frame($"100181{register:X2}{payload}", 7)] : [Frame("10018F810D02", 7)]);
        var longSession = new BatteryHidStub(request =>
        {
            Assert.True(LogitechHidppProtocol.IsPing(request));
            shortSession.Enqueue(Frame($"10{request[1]:X2}8F001A{(request[1] == 1 ? 1 : 8):X2}", 7));
            return [];
        });
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([Nano, shortHid], [], [])));

        var reading = Assert.Single(await registry.ReadDevicesAsync(detected,
            new BatteryTransportStub(hid => hid.Path == shortHid.Path ? shortSession : longSession), null, default));

        Assert.Equal(percentage, reading.Percentage);
        Assert.Equal(estimate, reading.IsEstimated);
        Assert.Equal((ChargingState)charging, reading.Charging);
        Assert.Contains("HID++ 1.0", reading.ProtocolSummary);
        Assert.Equal(Frame("1001810D000000", 7), shortSession.Writes[0]);
        Assert.Equal(register == 0x0D ? 1 : 2, shortSession.Writes.Count);
        Assert.All(shortSession.Writes, request => Assert.Equal(0x81, request[2]));
    }

    [Fact]
    public async Task MalformedFirstPingBlocksUnknownReceiverUntilRedetect()
    {
        var gates = new SafetyGateStore();
        var receiver = Nano with { ProductId = 0xC5FF, Product = "Unknown Dongle" };
        var registry = AdapterRegistry.CreateDefault(gates: gates);
        var target = Assert.Single(registry.Detect(new([receiver], [], [])));
        var session = new BatteryHidStub(_ => [Frame("7E01001A0908", 20)]);

        var failure = Assert.Single(await registry.ReadDevicesAsync(target, new BatteryTransportStub(_ => session), null, default));
        Assert.Equal(ReadIssue.InvalidResponse, failure.Issue);
        Assert.True(LogitechHidppProtocol.IsPing(Assert.Single(session.Writes)));
        var reopened = AdapterRegistry.CreateDefault(gates: new SafetyGateStore(gates.Snapshot()));
        Assert.Empty(reopened.Detect(new([receiver], [], [])));
        Assert.Equal(3, Assert.Single(gates.RecentRejections()).Gate);
        gates.Clear();
        Assert.Single(registry.Detect(new([receiver], [], [])));
    }

    [Fact]
    public async Task CableUsesAddressFFAndShowsCharging()
    {
        var cable = Nano with { ProductId = 0xC09D, Product = "Logitech mouse" };
        var shortHid = cable with { Path = "short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var session = new BatteryHidStub(request => DirectReply(request, "USB Mouse", 3, 72));
        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new([cable], [], [])));
        var device = Assert.Single(registry.Detect(new([cable, shortHid], [], [])));
        var reading = Assert.Single(await registry.ReadDevicesAsync(device, new BatteryTransportStub(hid =>
            hid.Path == shortHid.Path ? new BatteryHidStub(_ => throw new InvalidOperationException()) : session), null, default));

        Assert.Equal(DeviceConnection.UsbCable, reading.Device.Connection);
        Assert.Equal(72, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        Assert.Contains("线缆", reading.ProtocolSummary);
        Assert.All(session.Writes, request => Assert.Equal(0xFF, request[1]));
    }

    [Theory]
    [InlineData(@"\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_Dev_VID&02046d_PID&b037_REV&0110_112233445566&Col02", 0xB037)]
    [InlineData(@"\?\HID#{00001124-0000-1000-8000-00805F9B34FB}_vid&0002046d_pid&b010&col02", 0xB010)]
    public async Task BluetoothPathWorksWithoutAnAddressFromParentNodes(string path, int pid)
    {
        Assert.Equal((ushort)0x046D, BluetoothHidPath.UsbVendorId(path));
        Assert.Equal((ushort)pid, BluetoothHidPath.ProductId(path));
        var endpoint = Nano with
        {
            ReceiverId = "container", Path = path, ProductId = (ushort)pid, UsagePage = 0xFF43, Usage = 0x0202
        };
        var registry = AdapterRegistry.CreateDefault();
        var selected = Assert.Single(registry.Detect(new([endpoint], [], [])));
        var session = new BatteryHidStub(request => DirectReply(request, "MX Anywhere", 3, 71));
        var reading = Assert.Single(await registry.ReadDevicesAsync(selected, new BatteryTransportStub(_ => session), null, default));
        Assert.Equal(DeviceConnection.Bluetooth, reading.Device.Connection);
        Assert.Equal(71, reading.Percentage);
    }

    [Theory]
    [InlineData("Input.Mouse", (int)DeviceKind.Mouse)]
    [InlineData("Input.Keyboard", (int)DeviceKind.Keyboard)]
    [InlineData("Audio.Headphones", (int)DeviceKind.Headset)]
    public void WindowsBluetoothCategoryIdentifiesPeripherals(string category, int kind) =>
        Assert.Equal((DeviceKind)kind, BluetoothBattery.KindFromCategory(category));

    [Fact]
    public async Task BleMouseAndKeyboardEachMergeWithTheirOwnWindowsDevice()
    {
        var mouse = Ble("mouse-container", 0xB037, 0xA1B2C3D4E5F6);
        var keys = Ble("hid-keys-container", 0xB378, 0xAB12CD34EF56);
        var properties = new BluetoothDevice[]
        {
            new(mouse.ReceiverId, "Mouse alias", DeviceKind.Mouse, true, 14) { Address = mouse.BluetoothAddress },
            new("windows-keys-container", "MX Keys S", DeviceKind.Keyboard, true, 32) { Address = keys.BluetoothAddress }
        };
        var inventory = new DeviceInventory([mouse, keys], [], []) { Bluetooth = properties };
        var registry = AdapterRegistry.CreateDefault();
        List<BatteryReading> hid = [];
        var transport = new BatteryTransportStub(endpoint => endpoint.ProductId == mouse.ProductId
            ? new BatteryHidStub(request => DirectReply(request, "MX Anywhere 3S", 3, 78))
            : new BatteryHidStub(request => DirectReply(request, null, 0, 61)));
        foreach (var device in registry.Detect(inventory))
            hid.AddRange(await registry.ReadDevicesAsync(device, transport, null, default));
        Assert.Equal(2, hid.Count);

        var readings = BluetoothBattery.Merge(hid, properties, DateTimeOffset.UtcNow);
        Assert.Collection(readings, first =>
        {
            Assert.Equal("MX Anywhere 3S", first.Device.Name);
            Assert.Equal(DeviceKind.Mouse, first.Device.Kind);
            Assert.Equal(78, first.Percentage);
            Assert.Equal(ChargingState.Charging, first.Charging);
            Assert.Contains("蓝牙 HID++；HID++ 4.2", first.ProtocolSummary);
        }, second =>
        {
            Assert.Equal("MX Keys S", second.Device.Name);
            Assert.Equal(DeviceKind.Keyboard, second.Device.Kind);
            Assert.Equal(61, second.Percentage);
        });
        Assert.NotEqual(readings[0].Device.Id, readings[1].Device.Id);
        Assert.All(readings, item => Assert.Equal(DeviceConnection.Bluetooth, item.Device.Connection));

        string report = await DiagnosticReport.GenerateAsync(inventory, transport);
        Assert.Contains("蓝牙电量来源|B1|Mouse|MX Anywhere 3S|电量=78%|Online|蓝牙 HID++", report);
        Assert.Contains("蓝牙电量来源|B2|Keyboard|MX Keys S|电量=61%|Online|蓝牙 HID++", report);
        Assert.DoesNotContain("A1B2C3D4E5F6", report);
        Assert.DoesNotContain("AB12CD34EF56", report);
        Assert.DoesNotContain("DEADBEEF", report);
        Assert.DoesNotContain("mouse-container", report);
        Assert.DoesNotContain("windows-keys-container", report);
    }

    [Fact]
    public async Task BleDiagnosticReportReplaysThroughTheNormalRegistry()
    {
        var mouse = Ble("private-container", 0xB037, null);
        var inventory = new DeviceInventory([mouse], [], []);
        var transport = new BatteryTransportStub(_ =>
            new BatteryHidStub(request => DirectReply(request, "MX Anywhere 3S", 3, 78)));
        var report = await DiagnosticReport.GenerateAsync(inventory, transport);
        var (replayInventory, replayTransport, _) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(replayInventory));
        var reading = Assert.Single(await registry.ReadDevicesAsync(device, replayTransport, null, default));
        Assert.Equal(78, reading.Percentage);
        Assert.Equal(DeviceConnection.Bluetooth, reading.Device.Connection);
        replayTransport.AssertComplete();
    }

    [Fact]
    public async Task UnavailableBleBatteryFallsBackToWindowsWithoutDeviceCommandsForTheProperty()
    {
        var mouse = Ble("mouse", 0xB037, 0x112233445566);
        var unknownKeyboard = new BluetoothDevice("keys-container", "Alice private-name", DeviceKind.Keyboard, true, 48);
        var properties = new BluetoothDevice[]
        {
            new(mouse.ReceiverId, "MX Anywhere 3S", DeviceKind.Mouse, true, 82) { Address = mouse.BluetoothAddress },
            unknownKeyboard
        };
        var inventory = new DeviceInventory([mouse], [], []) { Bluetooth = properties };
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(inventory));
        var transport = new BatteryTransportStub(_ => new BatteryHidStub(request => DirectReply(request, null, 0, null)));
        var hid = await registry.ReadDevicesAsync(detected, transport, null, default);
        var noBattery = Assert.Single(BluetoothBattery.Merge(hid, [], DateTimeOffset.UtcNow));
        Assert.Equal(LinkState.Asleep, noBattery.Link);
        var readings = BluetoothBattery.Merge(hid, properties, DateTimeOffset.UtcNow);
        Assert.Equal(new int?[] { 82, 48 }, readings.Select(reading => reading.Percentage));
        Assert.All(readings, reading => Assert.Contains("由 Windows 提供", reading.ProtocolSummary));
        Assert.Equal("MX Anywhere 3S", readings[0].Device.Name);
        Assert.Equal(ChargingState.Unknown, readings[0].Charging);
        Assert.Single(transport.Opened);

        string report = await DiagnosticReport.GenerateAsync(inventory, transport);
        Assert.Contains("蓝牙电量来源|B1|Mouse|MX Anywhere 3S|电量=82%|Online|蓝牙属性（由 Windows 提供）", report);
        Assert.DoesNotContain("Alice private-name", report);
        Assert.DoesNotContain("112233445566", report);
    }

    [Theory]
    [InlineData(null, null, (int)LinkState.Asleep, null)]
    [InlineData(true, null, (int)LinkState.Asleep, null)]
    [InlineData(false, 92, (int)LinkState.Offline, null)]
    [InlineData(true, 0, (int)LinkState.Online, 0)]
    public void BluetoothStateUsesExplicitWindowsDisconnection(bool? connected, int? level, int link, int? expected)
    {
        var result = Assert.Single(BluetoothBattery.Merge([], [new("container", "Keyboard", DeviceKind.Keyboard, connected, level)],
            DateTimeOffset.UnixEpoch));
        Assert.Equal((LinkState)link, result.Link);
        Assert.Equal(expected, result.Percentage);
    }

    [Theory]
    [InlineData(0x0AB5, 0xFF43, 0x0202, 0x03, (int)LinkState.Online)]
    [InlineData(0x0AC4, 0x000C, 1, 0, (int)LinkState.Offline)]
    public async Task HeadsetAdcUsesAddressFFAndDecodesConnectionAndCharging(int pid, int page, int usage, int flags, int link)
    {
        var hid = Nano with { ProductId = (ushort)pid, UsagePage = (ushort)page, Usage = (ushort)usage };
        var session = new BatteryHidStub(request => request switch
        {
            [0x11, 0xFF, 0, 0x1A, ..] => [Frame($"11FF001A0201{request[6]:X2}", 20)],
            [0x11, 0xFF, 0, 0x0F, 0x1F, 0x20, ..] => [Frame("11FF000F04", 20)],
            [0x11, 0xFF, 4, 0x0A, ..] => [Frame($"11FF040A0F0A{flags:X2}", 20)],
            [0x11, 0xFF, 0, _, ..] => [Frame($"11FF00{request[3]:X2}00", 20)],
            _ => throw new InvalidOperationException("意外发包")
        });
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var reading = Assert.Single(await registry.ReadDevicesAsync(target, new BatteryTransportStub(_ => session), null, default));
        Assert.Equal(DeviceKind.Headset, reading.Device.Kind);
        Assert.Equal((LinkState)link, reading.Link);
        Assert.Equal(flags == 0 ? null : (int?)50, reading.Percentage);
        Assert.Equal(flags != 0, reading.IsEstimated);
        Assert.Equal(flags == 0 ? ChargingState.Unknown : ChargingState.Charging, reading.Charging);
        Assert.Contains("电量 1F20", reading.ProtocolSummary);
    }

    [Theory]
    [InlineData(0x1004, "11FF060045080100")]
    [InlineData(0x1000, "11FF060045080100")]
    public async Task BatteryPushUpdatesTheDiscoveredDeviceWithoutSendingAQuery(int feature, string push)
    {
        var ble = Ble("container", 0xB037, null);
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([ble], [], [])));
        var session = new BatteryHidStub(request => DirectReply(request, "MX Anywhere 3S", 3, 78, feature == 0x1004));
        var first = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => session), null, default));

        var listener = new BatteryHidStub(_ => throw new InvalidOperationException("推送监听不应发包"));
        listener.Enqueue(Frame(push, 20));
        listener.Enqueue([]);
        List<PresenceUpdate> updates = [];
        await registry.ListenPushAsync(detected, new BatteryTransportStub(_ => listener), updates.Add, default);
        var update = Assert.Single(updates);
        Assert.Equal(first.Device.Id, update.DeviceId);
        Assert.Equal(69, update.Battery?.Percentage);
        Assert.Equal(ChargingState.Charging, update.Battery?.Charging);
        Assert.Empty(listener.Writes);
    }

    [Fact]
    public async Task BleHidpp20UnsupportedBatteryFunctionFallsBackToTheNextFeature()
    {
        var ble = Ble("container", 0xB037, null);
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([ble], [], [])));
        var session = new BatteryHidStub(request => (request[2], request[3]) switch
        {
            (6, 0x1A) => [Frame("11FFFF061A09", 20)],
            (0, 0x0C) => [Frame("11FF000C09", 20)],
            (9, 0x0A) => [Frame("11FF090A320001", 20)],
            _ => DirectReply(request, "MX Anywhere 3S", 3, 78)
        });

        var reading = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => session), null, default));
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(50, reading.Percentage);
        Assert.Contains("电量 1000", reading.ProtocolSummary);
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x08)]
    [InlineData(0x09)]
    public async Task BleHidpp20ErrorsKeepIdentityAndAllowWindowsFallback(int code)
    {
        var ble = Ble("container", 0xB037, null);
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([ble], [], [])));
        var session = new BatteryHidStub(request => DirectReply(request, "MX Anywhere 3S", 3, 78));
        var first = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => session), null, default));
        var silent = new BatteryHidStub(_ => []);
        // 刚在线的设备第一次没有回复时保留在线（防抖），连续第二次才算休眠。
        var held = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => silent), null, default));
        var asleep = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => silent), null, default));
        Assert.Equal(LinkState.Online, held.Link);
        Assert.Equal(78, held.Percentage);
        var rejected = new BatteryHidStub(request => [Frame($"11FFFF{request[2]:X2}{request[3]:X2}{code:X2}", 20)]);
        var failed = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => rejected), null, default));
        var ping = Assert.Single(await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => rejected), null, default));

        Assert.Equal(first.Device, asleep.Device);
        Assert.Equal(first.Device, failed.Device);
        Assert.Equal(first.Device, ping.Device);
        Assert.Equal(LinkState.Asleep, asleep.Link);
        Assert.Equal(LinkState.Asleep, failed.Link);
        Assert.Equal(LinkState.Asleep, ping.Link);
        Assert.All(silent.Writes, write => Assert.Equal(Frame("11FF061A", 20), write));
        Assert.Equal(2, silent.Writes.Count);

        var property = new BluetoothDevice("container", "My Mouse", DeviceKind.Other, true, 83);
        var merged = Assert.Single(BluetoothBattery.Merge([failed], [property], DateTimeOffset.UnixEpoch));
        Assert.Equal(LinkState.Online, merged.Link);
        Assert.Equal(83, merged.Percentage);
        Assert.Equal("MX Anywhere 3S", merged.Device.Name);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(1167)]
    public async Task TransientBleTransportErrorUsesWindowsLinkStateWithoutDroppingKnownIdentity(int win32)
    {
        var endpoint = Ble("container", 0xB037, null);
        var registry = AdapterRegistry.CreateDefault();
        var detected = Assert.Single(registry.Detect(new([endpoint], [], [])));
        var transport = new BatteryTransportStub(_ => new BatteryHidStub(request => DirectReply(request, "MX Anywhere 3S", 3, 78)));
        var first = Assert.Single(await registry.ReadDevicesAsync(detected, transport, null, default));
        var broken = new BatteryTransportStub(_ => new BatteryHidStub(_ => throw WindowsErrors.FromCode(win32, "读取")));
        var unavailable = Assert.Single(await registry.ReadDevicesAsync(detected, broken, null, default));

        Assert.Equal(first.Device, unavailable.Device);
        Assert.Equal(LinkState.Asleep, unavailable.Link);
        var windows = new BluetoothDevice("container", "My Mouse", DeviceKind.Other, true, 81);
        var online = Assert.Single(BluetoothBattery.Merge([unavailable], [windows], DateTimeOffset.UnixEpoch));
        Assert.Equal(LinkState.Online, online.Link);
        Assert.Equal(81, online.Percentage);
        Assert.Equal(first.Device, online.Device);
        var offline = Assert.Single(BluetoothBattery.Merge([unavailable], [windows with { Connected = false }], DateTimeOffset.UnixEpoch));
        Assert.Equal(LinkState.Offline, offline.Link);
        Assert.Null(offline.Percentage);

        var recovered = new BatteryHidStub(request => DirectReply(request, "MX Anywhere 3S", 3, 78));
        await registry.ReadDevicesAsync(detected, new BatteryTransportStub(_ => recovered), null, default);
        Assert.Equal(Frame("11FF061A", 20), Assert.Single(recovered.Writes));
    }

    [Fact]
    public async Task ReceiverReconnectInvalidatesTheCachedBatteryIndex()
    {
        var longHid = Nano;
        var shortHid = Nano with { Path = "short", Usage = 1, InputLength = 7, OutputLength = 7 };
        var registry = AdapterRegistry.CreateDefault();
        var device = Assert.Single(registry.Detect(new([longHid, shortHid], [], [])));
        var first = new BatteryHidStub(request => SlotReply(request, 6));
        await registry.ReadDevicesAsync(device, new BatteryTransportStub(hid => hid.Path == longHid.Path
            ? first : new BatteryHidStub(_ => [])), null, default);

        var shortPush = new BatteryHidStub(_ => throw new InvalidOperationException());
        shortPush.Enqueue(Frame("10014110400000", 7));
        shortPush.Enqueue(Frame("10014110000000", 7));
        shortPush.Enqueue([]);
        var events = new List<PresenceUpdate>();
        await registry.ListenPushAsync(device, new BatteryTransportStub(hid =>
            hid.Path == shortHid.Path ? shortPush : new BatteryHidStub(_ => [])), events.Add, default);

        var second = new BatteryHidStub(request => SlotReply(request, 9));
        var reading = Assert.Single(await registry.ReadDevicesAsync(device, new BatteryTransportStub(hid =>
            hid.Path == longHid.Path ? second : new BatteryHidStub(_ => [])), null, default));
        Assert.Equal(new[] { LinkState.Offline, LinkState.Online }, events.Select(update => update.Link));
        Assert.True(LogitechHidppProtocol.IsPing(second.Writes[0]));
        Assert.Contains(second.Writes, request => request[1] == 1 && request[2] == 9 && request[3] == 0x1A);
        Assert.Equal(78, reading.Percentage);

        static byte[][] SlotReply(byte[] request, byte index)
        {
            if (request[1] != 1)
                return [Frame($"10{request[1]:X2}8F001A08", 7)];
            byte[] direct = request.ToArray();
            direct[1] = 0xFF;
            if (request[2] == index)
                direct[2] = 6;
            var replies = DirectReply(direct, null, 0, 78);
            foreach (var reply in replies)
            {
                reply[1] = 1;
                if (reply[2] == 6)
                    reply[2] = index;
                if (request[2] == 0 && request[3] == 0x0A)
                    reply[4] = index;
            }
            return replies;
        }
    }

    [Fact]
    public void ProX3StaysDisabledBeforeOpeningAnyHandle()
    {
        var hid = Nano with { ProductId = 0x0B45, UsagePage = 0xFFA0, Usage = 1,
            InputLength = 64, OutputLength = 64, InterfaceNumber = 3 };
        Assert.NotNull(DeviceCatalog.FindDisabledPath(hid.VendorId, hid.ProductId));
        Assert.Empty(AdapterRegistry.CreateDefault().Detect(new([hid], [], [])));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogitechHeadsetProtocol.Encode(0x0B45, LogitechHeadsetQuery.Bridge));
    }

    private static HidInterface Ble(string container, ushort pid, ulong? address) =>
        new(container, $@"\\?\hid#VID&0002046D&PID&{pid:X4}#{container}", 0x046D, pid,
            "Bluetooth HID Device", 0xFF43, 0x0202, 20, 20, 0, null) { BluetoothAddress = address };

    private static byte[][] DirectReply(byte[] request, string? name, int kind, int? percentage, bool unified = true)
    {
        Assert.Equal(0xFF, request[1]);
        return (request[2], request[3]) switch
        {
            (0, 0x1A) => [Frame($"11FF001A0402{request[6]:X2}", 20)],
            (0, 0x0E) => [Frame("11FF000E08", 20)],
            (8, 0x0A) => [Frame("11FF080A01DEADBEEF000140A900000000", 20)],
            (0, 0x0B) => [Frame($"11FF000B{(name is null ? 0 : 7):X2}", 20)],
            (7, 0x0A) => [Frame($"11FF070A{name!.Length:X2}", 20)],
            (7, 0x1A) => [Frame($"11FF071A{Convert.ToHexString(Encoding.UTF8.GetBytes(name!))}", 20)],
            (7, 0x2A) => [Frame($"11FF072A{kind:X2}", 20)],
            (0, 0x0A) when unified && percentage is not null => [Frame("11FF000A06", 20)],
            (0, 0x0C) when !unified && percentage is not null => [Frame("11FF000C06", 20)],
            (6, 0x0A) when unified => [Frame("11FF060A0F03", 20)],
            (6, 0x0A) => [Frame($"11FF060A{percentage:X2}0001", 20)],
            (6, 0x1A) => [Frame($"11FF061A{percentage:X2}080100", 20)],
            (0, _) => [Frame($"11FF00{request[3]:X2}00", 20)],
            _ => throw new InvalidOperationException("意外发包")
        };
    }
}

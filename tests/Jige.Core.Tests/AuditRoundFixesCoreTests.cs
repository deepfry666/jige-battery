using Jige.Core.Adapters;
using Jige.Core.Diagnostics;
using Jige.Core.Windows;

namespace Jige.Core.Tests;

public sealed class AuditRoundFixesCoreTests
{
    [Theory]
    [InlineData(0x00, ChargingState.NotCharging)] // 秒
    [InlineData(0x10, ChargingState.NotCharging)] // 分
    [InlineData(0x20, ChargingState.NotCharging)] // 时
    [InlineData(0x30, ChargingState.NotCharging)] // 天
    [InlineData(0x50, ChargingState.Charging)]    // 充电 + 分
    [InlineData(0x60, ChargingState.Charging)]    // 充电 + 时
    [InlineData(0x90, ChargingState.Full)]        // 充满 + 分
    public void LogitechLegacyRegister0DParsesAllTimeUnitBits(byte statusByte, ChargingState expected)
    {
        byte[] reply = [0x10, 0x01, 0x81, 0x0D, 35, 0x02, statusByte];
        Assert.True(LogitechHidppProtocol.TryRegisterBattery(reply, 0x0D, out int percentage, out bool estimated, out var charging));
        Assert.Equal(35, percentage);
        Assert.False(estimated);
        Assert.Equal(expected, charging);
    }

    [Fact]
    public void LogitechLegacyRegister0DRejectsErrorBits()
    {
        byte[] reply = [0x10, 0x01, 0x81, 0x0D, 35, 0x02, 0xC0];
        Assert.False(LogitechHidppProtocol.TryRegisterBattery(reply, 0x0D, out _, out _, out _));
    }

    [Theory]
    [InlineData(3199, null)]
    [InlineData(3200, 0)]
    [InlineData(3450, 0)]
    [InlineData(3500, 0)]
    [InlineData(3850, 50)]
    [InlineData(4200, 100)]
    [InlineData(4300, 100)]
    [InlineData(4380, 100)]
    [InlineData(4381, null)]
    public void LogitechVoltageEstimateClampsExtremesAndRejectsOutOfRange(int millivolts, int? expected)
    {
        Assert.Equal(expected, LogitechHidppProtocol.EstimateVoltage(millivolts));
    }

    [Fact]
    public void MchoseKbRejectsRequestEchoAndAllZeroReply()
    {
        byte[] echo = MchoseKbProtocol.EncodeBattery();
        Assert.False(MchoseKbProtocol.TryDecodeBattery(echo, out _));

        byte[] zeroPayload = new byte[20];
        zeroPayload[0] = MchoseKbProtocol.ReportId;
        zeroPayload[1] = MchoseKbProtocol.Command;
        Assert.False(MchoseKbProtocol.TryDecodeBattery(zeroPayload, out _));

        byte[] valid = new byte[20];
        valid[0] = MchoseKbProtocol.ReportId;
        valid[1] = MchoseKbProtocol.Command;
        valid[5] = 78;
        valid[19] = MchoseKbProtocol.Checksum(valid.AsSpan(0, 19));
        Assert.True(MchoseKbProtocol.TryDecodeBattery(valid, out int level));
        Assert.Equal(78, level);
    }

    [Fact]
    public void BitMouseRejectsRequestEchoAndRecognizesPushFrame()
    {
        byte[] echo = MouseBatteryProtocol.Request(MouseFamily.BitMouse, charging: false);
        Assert.False(MouseBatteryProtocol.TryBitMouse(echo, BitMouseReply.Auto, charging: false, out _));

        byte[] pushNoChecksum = new byte[64];
        pushNoChecksum[0] = 8;
        pushNoChecksum[1] = 0x72;
        pushNoChecksum[2] = 0x73;
        Assert.True(MouseBatteryProtocol.IsBitMousePush(pushNoChecksum));

        byte[] pushWithChecksum = new byte[64];
        pushWithChecksum[0] = 8;
        pushWithChecksum[2] = 0x72;
        pushWithChecksum[3] = 0x73;
        pushWithChecksum[1] = MouseBatteryProtocol.BitMouseChecksum(pushWithChecksum.AsSpan(2));
        Assert.True(MouseBatteryProtocol.IsBitMousePush(pushWithChecksum));
    }

    [Fact]
    public void SafetyGateFiltersInvalidSignaturesAndRefreshesLruOnIsBlocked()
    {
        var initial = new Dictionary<string, string>
        {
            ["invalid-sig"] = "junk",
            ["373B:1000:FF02:0002:17:17:0"] = "oldest-valid"
        };
        var gates = new SafetyGateStore(initial);
        Assert.Equal(1, gates.BlockedCount);

        var oldest = new HidInterface("rx-old", "p-old", 0x373B, 0x1000, null, 0xFF02, 2, 17, 17, 0, 1);
        for (ushort i = 1; i <= 63; i++)
        {
            var hid = new HidInterface($"rx-{i}", $"p-{i}", 0x373B, (ushort)(0x2000 + i), null, 0xFF02, 2, 17, 17, 0, 1);
            gates.Block(hid, $"bad-{i}");
        }

        // 命中 IsBlocked 后应刷新最旧条目的近期热度，再新增 1 条时淘汰的是次旧条目而非刚被命中的条目。
        Assert.True(gates.IsBlocked(oldest, out _));
        var newest = new HidInterface("rx-new", "p-new", 0x373B, 0x2FFF, null, 0xFF02, 2, 17, 17, 0, 1);
        gates.Block(newest, "bad-new");

        var snapshot = gates.Snapshot();
        Assert.Equal(SafetyGateStore.MaxBlockedEntries, snapshot.Count);
        Assert.Contains(SafetyGateStore.Signature(oldest), snapshot.Keys);
        Assert.Contains(SafetyGateStore.Signature(newest), snapshot.Keys);
        Assert.DoesNotContain("373B:2001:FF02:0002:17:17:0", snapshot.Keys);
    }

    [Fact]
    public void BluetoothMergeDoesNotCollapseEmptyContainersAndDeduplicatesSharedContainer()
    {
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var r1 = new BatteryReading(
            new("logitech-hidpp:bt-1", "Mouse A", DeviceKind.Mouse, Connection: DeviceConnection.Bluetooth),
            80, false, ChargingState.NotCharging, LinkState.Online, now)
        { BluetoothContainer = "" };
        var r2 = new BatteryReading(
            new("logitech-hidpp:bt-2", "Keyboard B", DeviceKind.Keyboard, Connection: DeviceConnection.Bluetooth),
            60, false, ChargingState.NotCharging, LinkState.Online, now)
        { BluetoothContainer = "" };

        var merged = BluetoothBattery.Merge([r1, r2], [], now);
        Assert.Equal(2, merged.Count);

        var propA = new BluetoothDevice("{11111111-1111-1111-1111-111111111111}", "WH-1000XM5", DeviceKind.Headset, true, 75)
        { Address = 0xAABBCCDDEEFF };
        var propB = new BluetoothDevice("{11111111-1111-1111-1111-111111111111}", "WH-1000XM5", DeviceKind.Headset, true, null);
        var deduped = BluetoothBattery.Merge([], [propB, propA], now);
        var single = Assert.Single(deduped);
        Assert.Equal(75, single.Percentage);
    }

    [Fact]
    public void ParseBluetoothAddressSupportsClassicHfpServiceInstanceId()
    {
#pragma warning disable CA1416 // 纯字符串解析逻辑，跨平台测试可直接执行
        ulong? address = WindowsDeviceDiscovery.ParseBluetoothAddress(
            @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2014\7&12345678&0&AABBCCDDEEFF_C00000000");
#pragma warning restore CA1416
        Assert.Equal(0xAABBCCDDEEFFUL, address);
    }

    [Fact]
    public void DiagnosticPrivacySanitizesPipesAndDistinguishesMchoseIdleFromMalformed()
    {
        Assert.Equal("foo｜bar baz", DiagnosticPrivacy.SanitizeText("foo|bar\r\nbaz"));

        var hid = new HidInterface("rx-1", "p-1", 0x373B, 0x1001, null, 0xFF00, 1, 65, 65, 65, 1);
        byte[] req = MchoseMouseProtocol.Request(65);
        var adapter = new MchoseMouseAdapter();

        byte[] idle = new byte[65];
        idle[0] = 0x12;
        idle[1] = 0xF9;
        byte[] sanitizedIdle = DiagnosticPrivacy.HidReply(adapter, hid, idle, req, []);
        Assert.True(MchoseMouseProtocol.IsIdleReply(sanitizedIdle));

        byte[] malformed = new byte[65];
        malformed[0] = 0x12;
        malformed[1] = 0x55;
        malformed[2] = 0xAA;
        byte[] sanitizedMalformed = DiagnosticPrivacy.HidReply(adapter, hid, malformed, req, []);
        Assert.False(MchoseMouseProtocol.IsIdleReply(sanitizedMalformed));
        Assert.Equal(0xEE, sanitizedMalformed[1]);
    }

    [Fact]
    public void DomesticMouseMatchRejectsEmptyDeclaredReportIdsForDisambiguation()
    {
        // Rapoo VT7 (24AE:1413) 支持 Usage 0x0002 与 AlternateUsage 0x000E，需按已确认的报告 ID 消歧。
        var declared = new HidInterface("rx-1", "p-valid", 0x24AE, 0x1413, null, 0xFF00, 0x0002, 64, 64, 64, 1)
        {
            InputReportIds = "07"
        };
        var missing = new HidInterface("rx-1", "p-other", 0x24AE, 0x1413, null, 0xFF00, 0x000E, 64, 64, 64, 1)
        {
            InputReportIds = ""
        };
        Assert.NotNull(DomesticMouseStatus.Match(declared));
        Assert.Null(DomesticMouseStatus.Match(missing));
    }
}

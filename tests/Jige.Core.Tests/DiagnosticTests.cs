using System.Text;
using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class DiagnosticTests
{
    private static readonly HidInterface Compx = new("container", "mouse", 0x373B, 0x1031, "ATK F1 Ultimate",
        0xFF02, 2, 17, 17, 0, 2);
    private static readonly UsbSerialPort Sony = new("sony", "sony", "COM7", [@"USB\VID_054C&PID_0E53&REV_0100&MI_06"]);

    [Fact]
    public async Task DisplayLocationAndFallbackReasonAppearInSettingsSummary()
    {
        var settings = new DiagnosticSettingsSummary("自定义", 15, 60, 300, 8, 30, true, 0, true, 0)
        {
            ActualDisplayLocation = "托盘图标",
            DisplayLocationReason = "通知区左侧空间不足"
        };
        var report = await DiagnosticReport.GenerateAsync(new([], [], []), new FakeReceiverTransport(),
            context: new(SettingsSummary: settings));
        int summary = report.IndexOf("# 当前设置摘要", StringComparison.Ordinal);
        int location = report.IndexOf("当前显示位置：托盘图标（通知区左侧空间不足）", StringComparison.Ordinal);
        Assert.True(summary >= 0 && location > summary);
    }

    [Fact]
    public async Task ExportHidesIdentifiersAndPaddingAndCanBeReplayed()
    {
        var hid = Compx with
        {
            Path = @"\\?\hid#vid_373b&pid_1031#instance-private",
            Product = @"ATK F1 Ultimate user-private C:\Users\private\battery.txt",
            ReceiverId = "computer-private"
        };
        var serial = Sony with
        {
            Path = @"C:\private\serial-private",
            HardwareIds = [Sony.HardwareIds[0], Sony.HardwareIds[0] + @"\usb-serial-private", @"USB\OTHER-private"],
            MetadataError = "serial-error-private"
        };
        var standard = hid with { Path = "keyboard-private", UsagePage = 1, Usage = 6, ProductId = 0x1155 };
        var otherVendor = hid with { VendorId = 0x1234, Product = "unrelated-private" };
        var original = new FakeReceiverTransport(
            (hid.Path, [new(DiagnosticOperation.OpenHid),
                new(DiagnosticOperation.HidInput, MouseAdapterTests.CapturedCompxReply),
                new(DiagnosticOperation.HidOutput, MouseAdapterTests.CompxRequest)]),
            (serial.Path, [new(DiagnosticOperation.OpenSerial, Issue: ReadIssue.Busy, Code: 32, Argument: 115200)]));
        var report = await DiagnosticReport.GenerateAsync(new([hid, standard, otherVendor], [serial],
            ["warning-private"]), original, CancellationToken.None);
        original.AssertComplete();

        Assert.DoesNotContain("private", report);
        // 0.1.1 起列出所有品牌的厂商自定义集合（只含 VID/PID），产品字符串仍遮蔽；标准键盘集合依旧不出现。
        Assert.Contains("|1234|1031|FF02|", report);
        Assert.DoesNotContain("1155", report);
        Assert.DoesNotContain("COM7", report);
        Assert.DoesNotContain(MouseAdapterTests.CapturedCompxReply, report);
        Assert.Contains("|080400000002190000000000000000002E|1", report);
        Assert.Contains("|OpenSerial|", report);
        Assert.Contains("|Busy|32|115200|-|0", report);
        Assert.Contains(Sony.HardwareIds[0], report);
        Assert.Contains("已遮蔽", report);

        var readings = await Replay(report);
        Assert.Equal(ReadIssue.Busy, readings[0].Issue);
        Assert.Equal(25, readings[1].Percentage);
        Assert.Equal(ChargingState.NotCharging, readings[1].Charging);
    }

    [Fact]
    public async Task UserReportFixtureReplaysThroughRegistryWithoutPhysicalDevices()
    {
        string filename = Path.Combine(AppContext.BaseDirectory, "fixtures", "device-report.txt");
        var report = await File.ReadAllTextAsync(filename, CancellationToken.None);
        var readings = await Replay(report.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(2, readings.Count);
        Assert.Equal(ReadIssue.Busy, readings[0].Issue);
        Assert.Equal(25, readings[1].Percentage);
    }

    [Fact]
    public async Task RealInzoneH9Gen1ReportFromFirstUserReplays()
    {
        // 2026-11-19 第一位用户在 Windows 11 实机上导出的 INZONE H9 一代诊断，分段到达的串口回复原样保留。
        string filename = Path.Combine(AppContext.BaseDirectory, "fixtures", "h9-gen1-real-2026-11-19.txt");
        var report = await File.ReadAllTextAsync(filename, CancellationToken.None);
        var readings = await Replay(report.Replace("\n", "\r\n", StringComparison.Ordinal));
        var headset = Assert.Single(readings);
        Assert.Equal(50, headset.Percentage);
        Assert.Equal(ChargingState.NotCharging, headset.Charging);
        Assert.Equal(LinkState.Online, headset.Link);
    }

    [Fact]
    public async Task RealBlackSharkV3ProReportReplays()
    {
        // 2026-11-20 Windows 11（10.0.26200）实机导出的 Razer BlackShark V3 Pro 诊断报告。
        string filename = Path.Combine(AppContext.BaseDirectory, "fixtures", "blackshark-v3-pro-real-2026-11-20.txt");
        var report = await File.ReadAllTextAsync(filename, CancellationToken.None);
        var readings = await Replay(report.Replace("\n", "\r\n", StringComparison.Ordinal));
        var headset = Assert.Single(readings);
        Assert.Equal("Razer BlackShark V3 Pro", headset.Device.Name);
        Assert.Equal(96, headset.Percentage);
        Assert.Equal(ChargingState.NotCharging, headset.Charging);
        Assert.Equal(LinkState.Online, headset.Link);
    }

    [Fact]
    public async Task RealAsusRogKeyboard1934ReportReplays()
    {
        // 2026-11-20 Windows 11（10.0.26200）实机导出的 ASUS ROG 无线键盘（0B05:1934）诊断报告。
        string filename = Path.Combine(AppContext.BaseDirectory, "fixtures", "asus-rog-kb-1934-real-2026-11-20.txt");
        var report = await File.ReadAllTextAsync(filename, CancellationToken.None);
        var readings = await Replay(report.Replace("\n", "\r\n", StringComparison.Ordinal));
        var keyboard = Assert.Single(readings);
        Assert.Equal("ASUS ROG 无线键盘", keyboard.Device.Name);
        Assert.Equal(100, keyboard.Percentage);
        Assert.Equal(ChargingState.Charging, keyboard.Charging);
        Assert.Equal(LinkState.Online, keyboard.Link);
    }

    [Fact]
    public async Task SplinteredSonyStreamCanReplayWithoutExposingModelSerialOrGarbage()
    {
        const string secret = "SERIAL-PRIVATE";
        byte[] model = [.. Convert.FromHexString("04FF170096C31402100100"), .. Encoding.ASCII.GetBytes(secret), 0];
        model[^1] = unchecked((byte)model[3..^1].Sum(b => b));
        string modelHex = Convert.ToHexString(model);

        var fake = new FakeReceiverTransport((Sony.Path,
        [
            new(DiagnosticOperation.OpenSerial, Argument: 460800),
            new(DiagnosticOperation.SerialWrite, "0100FC0896C321010101007D"),
            new(DiagnosticOperation.SerialRead, "04FF0A0096C31201100100017E"),
            new(DiagnosticOperation.SerialWrite, "0100FC0896C341020101009E"),
            new(DiagnosticOperation.SerialRead, modelHex[..30]),
            new(DiagnosticOperation.SerialRead, modelHex[30..]),
            new(DiagnosticOperation.SerialWrite, "0100FC0896C34104010100A0"),
            new(DiagnosticOperation.SerialRead, "BAADF00D04FF"),
            new(DiagnosticOperation.SerialRead, "0B0096C314041001000032B4")
        ]));
        var report = await DiagnosticReport.GenerateAsync(new([], [Sony], []), fake, CancellationToken.None, 460800);
        fake.AssertComplete();

        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(secret)), report);
        Assert.DoesNotContain(modelHex[..30], report);
        Assert.DoesNotContain(modelHex[30..], report);
        Assert.DoesNotContain("BAADF00D", report);
        Assert.Contains("|0000000004FF|1", report);
        Assert.Contains("|0B0096C314041001000032B4|0", report);
        var reading = Assert.Single(await Replay(report));
        Assert.Equal(50, reading.Percentage);
    }

    [Fact]
    public async Task UnrelatedSonyEventIsMaskedWithoutChangingTimeoutIntoCorruption()
    {
        var fake = new FakeReceiverTransport((Sony.Path,
        [
            new(DiagnosticOperation.OpenSerial, Argument: 115200),
            new(DiagnosticOperation.SerialWrite, "0100FC0896C321010101007D"),
            new(DiagnosticOperation.SerialRead, "04FF0A0096C31201100100017E"),
            new(DiagnosticOperation.SerialWrite, "0100FC0896C341020101009E"),
            new(DiagnosticOperation.SerialRead, "04FF090096C3140210010080"),
            new(DiagnosticOperation.SerialWrite, "0100FC0896C34104010100A0"),
            new(DiagnosticOperation.SerialRead, "04FF0A0096C314041101008003"),
            new(DiagnosticOperation.SerialRead, Issue: ReadIssue.Timeout)
        ]));
        var report = await DiagnosticReport.GenerateAsync(new([], [Sony], []), fake, CancellationToken.None);
        fake.AssertComplete();
        Assert.Contains("Timeout", report);
        Assert.Contains("04FF0A0096C300000000000059", report);
        Assert.DoesNotContain("04FF0A0096C314041101008003", report);
        var reading = Assert.Single(await Replay(report));
        Assert.Equal(ReadIssue.Timeout, reading.Issue);
    }

    [Theory]
    [InlineData(false, ReadIssue.Timeout)]
    [InlineData(true, ReadIssue.InvalidResponse)]
    public async Task TimeoutOrCorruptReplyIsReplayable(bool corrupted, ReadIssue issue)
    {
        List<ReceiverStep> steps = [new(DiagnosticOperation.OpenHid)];
        if (corrupted)
            steps.Add(new(DiagnosticOperation.HidInput, "DEADBEAF"));
        else
            steps.Add(new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout));
        steps.Add(new(DiagnosticOperation.HidOutput, MouseAdapterTests.CompxRequest));
        if (corrupted)
            steps.Add(new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout));
        var fake = new FakeReceiverTransport((Compx.Path, steps.ToArray()));
        var report = await DiagnosticReport.GenerateAsync(new([Compx], [], []), fake, CancellationToken.None);
        fake.AssertComplete();
        Assert.DoesNotContain("DEADBEAF", report);
        var reading = Assert.Single(await Replay(report));
        Assert.Equal(issue, reading.Issue);
        Assert.Null(reading.Percentage);
    }

    [Fact]
    public async Task LogitechHidppExportKeepsRoutesAndReplaysWithoutDeviceIdentifiers()
    {
        var longHid = new HidInterface("private-receiver", "long-path", 0x046D, 0xC548, "Private Receiver Name",
            0xFF00, 2, 20, 20, 0, 2);
        var shortHid = longHid with { Path = "short-path", Usage = 1, InputLength = 7, OutputLength = 7 };
        var longSession = new BatteryHidStub(request => (request[1], request[2], request[3]) switch
        {
            (1, 0, 0x1A) => [Frame($"1101001A0402{request[6]:X2}", 20)],
            (2, 0, 0x1A) => [Frame("10028F001A04", 7)],
            (_, 0, 0x1A) => [Frame($"10{request[1]:X2}8F001A08", 7)],
            (1, 0, 0x0B) => [Frame("1101000B07", 20)],
            (1, 7, 0x0A) => [Frame("1101070A05", 20)],
            (1, 7, 0x1A) => [Frame("1101071A414C494345", 20)], // ALICE
            (1, 7, 0x2A) => [Frame("1101072A03", 20)],
            (1, 0, 0x0E) => [Frame("1101000E08", 20)],
            (1, 8, 0x0A) => [Frame("1101080A01DEADBEEF000140A900000000CAFE00", 20)],
            (1, 0, 0x0A) => [Frame("1101000A06", 20)],
            (1, 6, 0x0A) => [Frame("1101060A0F03", 20)],
            (1, 6, 0x1A) => [Frame("1101061A5E080100", 20)],
            _ => throw new InvalidOperationException("意外发包")
        });
        var shortSession = new BatteryHidStub(_ => throw new InvalidOperationException("意外短集合查询"));

        string report = await DiagnosticReport.GenerateAsync(new([longHid, shortHid], [], []),
            new BatteryTransportStub(hid => hid.Path == shortHid.Path ? shortSession : longSession));
        Assert.Contains("匹配 Logitech HID++ H1（长报告） H2（短报告）", report);
        Assert.Contains("OP|H2|OpenHid|", report);
        Assert.Contains("罗技|H1|Mouse|接收器槽位 1；HID++ 4.2", report);
        Assert.Contains("OP|H1|HidInput|", report);
        Assert.Contains("|1101080A0100000000000140A900000000000000|1", report);
        Assert.DoesNotContain("414C494345", report);
        Assert.DoesNotContain("Private Receiver Name", report);
        Assert.DoesNotContain("private-receiver", report);
        var online = Assert.Single(await Replay(report));
        Assert.Equal(94, online.Percentage);
        Assert.Equal(DeviceKind.Mouse, online.Device.Kind);
        Assert.Equal(ChargingState.Charging, online.Charging);
    }

    [Fact]
    public async Task UnknownLogitechReceiverCanReplayWithAProtectedProductName()
    {
        var hid = new HidInterface("receiver", "unknown-path", 0x046D, 0xC5FF, "私有名称 dongle",
            0xFF00, 2, 20, 20, 0, 2);
        var session = new BatteryHidStub(request => (request[1], request[2], request[3]) switch
        {
            (1, 0, 0x1A) => [Frame($"1101001A0201{request[6]:X2}", 20)],
            (_, 0, 0x1A) => [Frame($"10{request[1]:X2}8F001A08", 7)],
            (1, 0, 0x0A) => [Frame("1101000A06", 20)],
            (1, 0, _) => [Frame($"110100{request[3]:X2}00", 20)],
            (1, 6, 0x0A) => [Frame("1101060A0F03", 20)],
            (1, 6, 0x1A) => [Frame("1101061A5E080100", 20)],
            _ => throw new InvalidOperationException("意外发包")
        });
        string report = await DiagnosticReport.GenerateAsync(new([hid], [], []), new BatteryTransportStub(_ => session));
        Assert.DoesNotContain("私有名称", report);
        var reading = Assert.Single(await Replay(report));
        Assert.Equal(LinkState.Online, reading.Link);
        Assert.Equal(94, reading.Percentage);
    }

    [Fact]
    public async Task LogitechCenturionReportRetainsBridgeAndBatteryFields()
    {
        var hid = new HidInterface("receiver", "headset", 0x046D, 0x0AF7, null, 0xFFA0, 1, 64, 64, 0, 3);
        var session = new BatteryHidStub(request => request[1] switch
        {
            5 => [Frame("510600000103CAFE", 64)],
            10 => [Frame("5103000311", 64), Frame("510C00031000070000010400DEAD", 64)],
            8 => [Frame("510B00031000060004014B4B03", 64)],
            _ => throw new InvalidOperationException("意外发包")
        });
        string report = await DiagnosticReport.GenerateAsync(new([hid], [], []), new BatteryTransportStub(_ => session));
        Assert.Contains("匹配 Logitech 耳机 H1", report);
        Assert.DoesNotContain("CAFE", report);
        Assert.DoesNotContain("DEAD", report);
        var reading = Assert.Single(await Replay(report));
        Assert.Equal(75, reading.Percentage);
        Assert.Equal(ChargingState.Full, reading.Charging);
    }

    [Theory]
    [InlineData(2, ReadIssue.None, LinkState.Online)]
    // D4（#19）：雷蛇协议状态 01 为无线事务忙，重试 3 次后归为 Timeout/Unknown，不误报为 Busy（被其他程序占用）。
    [InlineData(1, ReadIssue.Timeout, LinkState.Unknown)]
    [InlineData(4, ReadIssue.None, LinkState.Offline)]
    public async Task Razer90BReportRetainsStatusAndRecalculatesItsChecksum(int status, ReadIssue issue, LinkState link)
    {
        var hid = new HidInterface("razer", "feature-path", 0x1532, 0x00C1, "Razer private", 0xFF00, 1, 0, 0, 91, 0);
        var session = new BatteryHidStub(request =>
        {
            byte[] reply = request.ToArray();
            reply[1] = request[8] == 0x80 ? (byte)status : (byte)2;
            reply[10] = request[8] == 0x80 ? (byte)128 : (byte)1;
            Convert.FromHexString("CAFEF00D").CopyTo(reply, 14);
            reply[89] = RazerFeatureProtocol.Checksum(reply);
            return [reply];
        }, feature: true);

        string report = await DiagnosticReport.GenerateAsync(new([hid], [], []), new BatteryTransportStub(_ => session));
        Assert.Contains("匹配 Razer 90B H1", report);
        Assert.DoesNotContain("CAFEF00D", report);
        var result = Assert.Single(await Replay(report));
        Assert.Equal(issue, result.Issue);
        Assert.Equal(link, result.Link);
        if (status == 2)
        {
            Assert.Equal(50, result.Percentage);
            Assert.Equal(ChargingState.Charging, result.Charging);
        }
    }

    [Fact]
    public async Task RazerHeadsetReportReplaysWithNewTransactionSequences()
    {
        var hid = new HidInterface("razer", "headset-path", 0x1532, 0x0565, "private", 0xFF14, 1, 64, 64, 0, 3);
        var session = new BatteryHidStub(request =>
        {
            byte[] reply = request.ToArray();
            reply[1] = 2;
            reply[6] = 9;
            reply[8] = 0x80;
            reply[11] = 1;
            reply[12] = 5;
            reply[13] = request[10] == 0x21 ? (byte)19 : (byte)1;
            Convert.FromHexString("BAADF00D").CopyTo(reply, 14);
            reply[62] = RazerHeadsetProtocol.Checksum(reply, 1);
            return [reply];
        });
        string report = await DiagnosticReport.GenerateAsync(new([hid], [], []), new BatteryTransportStub(_ => session));
        Assert.Contains("匹配 Razer 耳机 H1", report);
        Assert.DoesNotContain("BAADF00D", report);
        var result = Assert.Single(await Replay(report));
        Assert.Equal(19, result.Percentage);
        Assert.Equal(ChargingState.Charging, result.Charging);
    }

    [Fact]
    public async Task AdaptationSummaryExportsHardwareCluesReportIdsAndReplyHeadersWithoutPii()
    {
        // 模拟未适配的 Keychron 键盘（带标准键盘伴随集合）、华硕灯控，以及已启用但返回异常校验的 1532:0577。
        var unadaptedVendor = new HidInterface("rx-keychron-private", "path-private-1", 0x3434, 0x0101, "My Secret Keyboard",
            0xFF60, 0x0061, 32, 32, 0, 1)
        {
            FirmwareVersion = 0x0120,
            InputReportIds = "06,08",
            OutputReportIds = "08"
        };
        var unadaptedKeyboard = new HidInterface("rx-keychron-private", "path-private-kbd", 0x3434, 0x0101, "My Secret Keyboard",
            0x0001, 6, 8, 1, 0, 0);
        var asusLed = new HidInterface("rx-asus-private", "path-private-2", 0x0B05, 0x19AF, "Aura Private",
            0xFF72, 0x00A1, 65, 65, 0, 2)
        {
            FirmwareVersion = 0x0002,
            InputReportIds = "EC",
            OutputReportIds = "EC"
        };
        var v3Pro = new HidInterface("rx-v3p-private", "path-private-3", 0x1532, 0x0577, "Razer Private",
            0xFF14, 1, 64, 64, 0, 5)
        {
            FirmwareVersion = 0x0104,
            InputReportIds = "02",
            OutputReportIds = "02"
        };

        // 构造一条校验故意错误的 0577 回复（内含敏感尾字节 SECRET），验证 OP 仍全零遮蔽，且回复特征只输出帧头不泄密。
        var badReply = Frame("0202600000000500808021010150", 64);
        Encoding.ASCII.GetBytes("SECRET").CopyTo(badReply, 20);
        badReply[62] = 0xAA; // 故意错 CRC

        var transport = new FakeReceiverTransport((v3Pro.Path,
        [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.HidOutput, Convert.ToHexString(RazerHeadsetProtocol.EncodeMediaTek(0x0577, RazerHeadsetQuery.Battery, 0))),
            new(DiagnosticOperation.HidInput, Convert.ToHexString(badReply)),
            new(DiagnosticOperation.HidInput, Issue: ReadIssue.Timeout)
        ]));

        string report = await DiagnosticReport.GenerateAsync(
            new([unadaptedVendor, unadaptedKeyboard, asusLed, v3Pro], [], []), transport, CancellationToken.None);
        transport.AssertComplete();

        Assert.DoesNotContain("private", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes("SECRET")), report);

        Assert.Contains("# 适配线索摘要", report);
        Assert.Contains("适配线索|R1|3434:0101|厂商=Keychron|固件=0120|连接=USB|角色=键盘|状态=未适配厂商", report);
        Assert.Contains("报告ID|H1|FF60:0061|in=06,08|out=08|feat=-", report);
        Assert.Contains("适配线索|R2|0B05:19AF|厂商=ASUS|固件=0002|连接=USB|角色=仅厂商集合|状态=未适配厂商", report);
        Assert.Contains("适配线索|R3|1532:0577|厂商=Razer|固件=0104|连接=USB|角色=仅厂商集合|状态=已匹配适配器", report);
        Assert.Contains("回复特征|H3|RazerMT len=64 rid=02 flag=02 seq=60 flen=05 dir=80 dom=80 cmd=21 st=01 vlen=01 crc0=FAIL crc1=FAIL", report);
    }

    private static byte[] Frame(string hex, int length) => LogitechBatteryTests.Frame(hex, length);

    private static async Task<List<BatteryReading>> Replay(string report)
    {
        var (inventory, transport, baud) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault(baud);
        List<BatteryReading> readings = [];
        foreach (var device in registry.Detect(inventory))
            readings.AddRange(await registry.ReadDevicesAsync(device, transport, null, CancellationToken.None));
        transport.AssertComplete();
        return readings;
    }
}

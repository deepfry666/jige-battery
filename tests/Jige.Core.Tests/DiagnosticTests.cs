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
        Assert.DoesNotContain("1234", report);
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

    private static async Task<List<BatteryReading>> Replay(string report)
    {
        var (inventory, transport, baud) = FakeReceiverTransport.FromReport(report);
        var registry = AdapterRegistry.CreateDefault(baud);
        List<BatteryReading> readings = [];
        foreach (var device in registry.Detect(inventory))
            readings.Add(await registry.ReadAsync(device, transport, null, CancellationToken.None));
        transport.AssertComplete();
        return readings;
    }
}

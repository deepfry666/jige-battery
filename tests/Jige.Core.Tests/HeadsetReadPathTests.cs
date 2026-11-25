using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

public sealed class HeadsetReadPathTests
{
    private static string Pad(string bytes, int length) => bytes.PadRight(length * 2, '0');

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cloud3FallsBackToOneVerifiedFeatureSession(bool failedWrite)
    {
        var hid = new HidInterface("r", "h", 0x03F0, 0x05B7, null, 0xFF13, 1, 62, 62, 65, 3);
        List<ReceiverStep> steps = [];
        if (failedWrite)
        {
            steps.Add(new(DiagnosticOperation.OpenHid));
            steps.Add(new(DiagnosticOperation.HidOutput, Pad("6689", 62), ReadIssue.IoError));
        }
        else
            steps.Add(new(DiagnosticOperation.OpenHid, Issue: ReadIssue.IoError, Code: unchecked((int)0xC0110010)));
        steps.AddRange([
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("6689", 65)),
            new(DiagnosticOperation.HidInput, "66890ED730"),
            new(DiagnosticOperation.SetFeature, Pad("668A", 65)),
            new(DiagnosticOperation.HidInput, "668A00")
        ]);
        var replay = new FakeReceiverTransport((hid.Path, steps.ToArray()));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var reading = await registry.ReadAsync(target, replay, null, CancellationToken.None);

        Assert.Equal(48, reading.Percentage);
        Assert.Equal(ChargingState.NotCharging, reading.Charging);
        Assert.Collection(replay.OpenedHids,
            output =>
            {
                Assert.Equal((byte)0x66, output.RequiredOutputReportId);
                Assert.Equal((byte)0x66, output.RequiredInputReportId);
                Assert.Null(output.RequiredFeatureReportId);
                Assert.False(output.FeatureOnly);
            },
            feature =>
            {
                Assert.True(feature.FeatureOnly);
                Assert.Equal((byte)0x66, feature.RequiredFeatureReportId);
                Assert.Null(feature.RequiredInputReportId);
                Assert.Null(feature.RequiredOutputReportId);
            },
            input =>
            {
                Assert.True(input.InputOnly);
                Assert.False(input.FeatureOnly);
                Assert.Equal((byte)0x66, input.RequiredInputReportId);
                Assert.Null(input.RequiredFeatureReportId);
                Assert.Null(input.RequiredOutputReportId);
            });
        replay.AssertComplete();
    }

    [Fact]
    public async Task Cloud3ReturnsAnUnrelatedOpenErrorWithoutRetrying()
    {
        var hid = new HidInterface("r", "h", 0x03F0, 0x05B7, null, 0xFF13, 1, 62, 62, 65, 3);
        var replay = new FakeReceiverTransport((hid.Path, [
            new(DiagnosticOperation.OpenHid, Issue: ReadIssue.IoError, Code: 31)
        ]));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var reading = await registry.ReadAsync(target, replay, null, CancellationToken.None);
        Assert.Equal(ReadIssue.IoError, reading.Issue);
        Assert.Null(reading.Percentage);
        Assert.Equal(0, replay.Writes);
        replay.AssertComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cloud3SReadsSixByteNotifications(bool onSibling)
    {
        var output = new HidInterface("r", "out", 0x03F0, 0x02CC, null, 0xFF13, 1,
            (ushort)(onSibling ? 0 : 6), 64, 0, 3) { OutputReportIds = "0C" };
        var input = output with { Path = "in", Usage = 2, InputLength = 6, OutputLength = 0, OutputReportIds = null };
        ReceiverStep level = new(DiagnosticOperation.HidInput, "0D0000000130");
        ReceiverStep charge = new(DiagnosticOperation.HidInput, "0D0000000A01");
        ReceiverStep readLevel = new(DiagnosticOperation.HidOutput, Pad("0C0203010006", 64));
        ReceiverStep readCharge = new(DiagnosticOperation.HidOutput, Pad("0C0203010048", 64));
        var replay = onSibling
            ? new FakeReceiverTransport((output.Path, [new(DiagnosticOperation.OpenHid), readLevel, readCharge]),
                (input.Path, [new(DiagnosticOperation.OpenHid), level, charge]))
            : new FakeReceiverTransport((output.Path, [new(DiagnosticOperation.OpenHid), level, readLevel, charge, readCharge]));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new(onSibling ? [output, input] : [output], [], [])));
        var reading = await registry.ReadAsync(target, replay, null, CancellationToken.None);
        Assert.Equal(48, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        replay.AssertComplete();
    }

    [Fact]
    public async Task Cloud3SRespondsToASiblingWhileThePrimaryHasQueuedNotifications()
    {
        var output = new HidInterface("r", "out", 0x03F0, 0x02CC, null, 0xFF13, 1, 6, 64, 0, 3)
            { OutputReportIds = "0C" };
        var input = output with { Path = "in", Usage = 2, OutputLength = 0, OutputReportIds = null };
        ReceiverStep noise = new(DiagnosticOperation.HidInput, "0D0000000302");
        var replay = new FakeReceiverTransport(
            (output.Path, [
                new(DiagnosticOperation.OpenHid),
                noise, new(DiagnosticOperation.HidOutput, Pad("0C0203010006", 64)), noise,
                noise, new(DiagnosticOperation.HidOutput, Pad("0C0203010048", 64)), noise
            ]),
            (input.Path, [
                new(DiagnosticOperation.OpenHid), new(DiagnosticOperation.HidInput, "0D0000000130"),
                new(DiagnosticOperation.HidInput, "0D0000000A01")
            ]));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([output, input], [], [])));
        var reading = await registry.ReadAsync(target, replay, null, CancellationToken.None);
        Assert.Equal(48, reading.Percentage);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        replay.AssertComplete();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("04,05")]
    public void Cloud3SPrefersTheCollectionThatDeclaresTheCommand(string? otherIds)
    {
        var other = new HidInterface("r", "wrong", 0x03F0, 0x02CC, null, 0xFF13, 1, 6, 64, 0, 3)
            { OutputReportIds = otherIds };
        var command = other with { Path = "command", OutputReportIds = "0C" };
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([other, command], [], [])));
        Assert.Equal(command, target.Target.Endpoint);
    }

    [Fact]
    public void Cloud3SRefusesTwoEquallyEligibleOutputCollections()
    {
        var hid = new HidInterface("r", "h1", 0x03F0, 0x02CC, null, 0xFF13, 1, 64, 64, 0, 3)
            { OutputReportIds = "0C" };
        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new([hid, hid with { Path = "h2" }], [], [])));
    }

    [Fact]
    public void SteelSeriesEncoderRejectsUnlistedCommands()
    {
        Assert.Throws<TransportException>(() => SteelSeriesProtocol.Encode(64, 0x41, 0xAB));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F0,0F")]
    public void StrixGoSelectsTheCollectionDeclaringFeatureFF(string? otherIds)
    {
        var other = new HidInterface("r", "other", 0x0B05, 0x18D6, null, 0xFF00, 1, 0, 0, 96, 3)
            { FeatureReportIds = otherIds };
        var command = other with { Path = "command", UsagePage = 0x0C, FeatureReportIds = "02,FF" };
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([other, command], [], [])));
        Assert.Equal(command, target.Target.Endpoint);
        if (otherIds is not null)
            Assert.Empty(registry.Detect(new([other], [], [])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("FF")]
    public void StrixGoRefusesTwoEquallyEligibleFeatureCollections(string? ids)
    {
        var hid = new HidInterface("r", "h1", 0x0B05, 0x18D7, null, 0xFF00, 1, 0, 0, 64, 3)
            { FeatureReportIds = ids };
        var registry = AdapterRegistry.CreateDefault();
        Assert.Empty(registry.Detect(new([hid, hid with { Path = "h2" }], [], [])));
    }

    [Fact]
    public async Task StrixGoUsesTheFullDeclaredFeatureLength()
    {
        var hid = new HidInterface("r", "h", 0x0B05, 0x18D6, null, 0xFF00, 1, 0, 0, 96, 3);
        var replay = new FakeReceiverTransport((hid.Path, [
            new(DiagnosticOperation.OpenHid),
            new(DiagnosticOperation.SetFeature, Pad("FF0800FD0412F1035201", 96)),
            new(DiagnosticOperation.GetFeature, Pad("FF1B000000000000000000F70E", 96), Argument: 0xFF)
        ]));
        var registry = AdapterRegistry.CreateDefault();
        var target = Assert.Single(registry.Detect(new([hid], [], [])));
        var reading = await registry.ReadAsync(target, replay, null, CancellationToken.None);
        Assert.Equal(75, reading.Percentage);
        var corrupted = Convert.FromHexString(Pad("FF0800FD0412F1035201", 96));
        corrupted[^1] = 0x01;
        Assert.False(AsusHidProtocol.AllowsRequest(corrupted));
        replay.AssertComplete();
    }
}

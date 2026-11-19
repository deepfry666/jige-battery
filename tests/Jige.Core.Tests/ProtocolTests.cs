using Jige.Core.Adapters;

namespace Jige.Core.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData(0x24, "0402A400")]
    [InlineData(0x26, "0402A600")]
    [InlineData(0x05, "04028500")]
    [InlineData(0x25, "0402A500")]
    public void UlxOnlyEncodesFixedQueries(int command, string header)
    {
        var request = FinalmouseProtocol.Encode((UlxQuery)command);
        Assert.Equal(64, request.Length);
        Assert.Equal(header, Convert.ToHexString(request.AsSpan(0, 4)));
        Assert.All(request[4..], value => Assert.Equal(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinalmouseProtocol.Encode((UlxQuery)0x12));
    }

    [Theory]
    [InlineData("050405026410", 78)]
    [InlineData("05040502B410", 88)]
    [InlineData("050405020000", null)]
    [InlineData("05040502FFFF", null)]
    public void UlxDecodesReportedVoltage(string hex, int? expected)
    {
        // HaloBattery #284 公布了 4196 mV 帧，并记录了充电时的 4276 mV 载荷。
        Assert.True(FinalmouseProtocol.TryReadPayload(Convert.FromHexString(hex), UlxQuery.Voltage, out var payload));
        Assert.Equal(expected, FinalmouseProtocol.VoltagePercentage(payload));
    }

    [Theory]
    [InlineData("040405026410")]
    [InlineData("050424026410")]
    [InlineData("050305026410")]
    [InlineData("05040503641011")]
    [InlineData("0504050264")]
    public void UlxRejectsWrongReportCommandOrLength(string hex) =>
        Assert.False(FinalmouseProtocol.TryReadPayload(Convert.FromHexString(hex), UlxQuery.Voltage, out _));

    [Fact]
    public void SonyOnlyEncodesThreeRecordedGets()
    {
        Assert.Equal("0100FC0896C321010101007D", Convert.ToHexString(SonyProtocol.Encode(SonyQuery.Link)));
        Assert.Equal("0100FC0896C341020101009E", Convert.ToHexString(SonyProtocol.Encode(SonyQuery.Model)));
        Assert.Equal("0100FC0896C34104010100A0", Convert.ToHexString(SonyProtocol.Encode(SonyQuery.Battery)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SonyProtocol.Encode((SonyQuery)4));
    }

    private const string CapturedSonyBattery = "04FF0B0096C314041001000032B4";

    [Fact]
    public void SonyDecodesRecordedBatteryReply()
    {
        // inzone-h9-mac 协议文档的完整 H9 电量回复，50%，未充电。
        Assert.True(SonyProtocol.TryBattery(Convert.FromHexString(CapturedSonyBattery), out var level, out var charge));
        Assert.Equal(50, level);
        Assert.Equal(ChargingState.NotCharging, charge);
    }

    [Theory]
    [InlineData("04FF0B0096C314041001000032B5")]
    [InlineData("04FF0B0096C314042001000032C4")]
    [InlineData("04FF0B0096C314041002000032B5")]
    [InlineData("04FF0B0096C314041001000065E7")]
    [InlineData("04FF0B0096C314041001000232B6")]
    [InlineData("04FF0B0096C314041001000032")]
    [InlineData("04FF0B0096C314041001000032B400")]
    public void SonyRejectsWrongChecksumTypeTransactionRangeAndLength(string hex) =>
        Assert.False(SonyProtocol.TryBattery(Convert.FromHexString(hex), out _, out _));

    [Fact]
    public void SonyReassemblesSplitFramesAndSkipsCorruption()
    {
        List<(FrameDirection Direction, string Hex)> trace = [];
        var stream = new SonyEventStream((direction, bytes) => trace.Add((direction, Convert.ToHexString(bytes))));
        stream.Append(Convert.FromHexString("110404FF0B0096"));
        Assert.False(stream.TryRead(out _));
        stream.Append(Convert.FromHexString("C314041001000032B5DE04"));
        Assert.False(stream.TryRead(out _));
        var capture = Convert.FromHexString(CapturedSonyBattery);
        stream.Append(capture.AsSpan(1, 8));
        Assert.False(stream.TryRead(out _));
        stream.Append(capture.AsSpan(9));
        stream.Append(capture);
        Assert.True(stream.TryRead(out byte[] first));
        Assert.True(stream.TryRead(out byte[] second));
        Assert.Equal(capture, first);
        Assert.Equal(capture, second);
        Assert.True(stream.SawInvalidData);
        Assert.False(stream.HasPendingBytes);
        Assert.Equal(2, trace.Count(item => item == (FrameDirection.Received, CapturedSonyBattery)));
    }
}

using Jige.Core.Adapters;
using Jige.Core.Windows;

namespace Jige.Core.Tests;

public sealed class HidReportDescriptorTests
{
    private const int Declared = 0x00110000;
    private const int Absent = unchecked((int)0xC0110010);
    private const int InvalidDescriptor = unchecked((int)0xC0110001);

    [Fact]
    public void DiagnosticListIncludesIdsAfterTheFirstSixteen()
    {
        var ids = HidReportDescriptor.ListIds(id => id < 16 || id is 0x2A or 0xFF ? Declared : Absent);
        Assert.Equal("00,01,02,03,04,05,06,07,08,09,0A,0B,0C,0D,0E,0F,2A,FF", ids);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InvalidDescriptorCannotAuthorizeAnUnnumberedReport(byte errorAt)
    {
        int Probe(byte id) => id == errorAt ? InvalidDescriptor : Absent;
        Assert.Null(HidReportDescriptor.ListIds(Probe));
        var error = Assert.Throws<TransportException>(() => HidReportDescriptor.Require(2, 0, null, Probe));
        Assert.Equal(InvalidDescriptor, error.NativeCode);
    }

    [Fact]
    public void LosingAKnownIdInvalidatesTheCachedCollection()
    {
        var changed = Assert.Throws<TransportException>(() => HidReportDescriptor.Require(1, 0x0C, "03,0C", _ => Absent));
        Assert.Equal(ReadIssue.Disconnected, changed.Issue);
        var missing = Assert.Throws<TransportException>(() => HidReportDescriptor.Require(1, 0x0C, "03,05", _ => Absent));
        Assert.Equal(ReadIssue.IoError, missing.Issue);
        Assert.Equal(Absent, missing.NativeCode);
    }

    [Fact]
    public void OnlyTheUnnumberedReportMayUseAnEntirelyRawDescriptor()
    {
        Assert.Equal("00(raw)", HidReportDescriptor.ListIds(_ => Absent));
        HidReportDescriptor.Require(2, 0, null, _ => Absent);
        Assert.Equal(Absent, Assert.Throws<TransportException>(() => HidReportDescriptor.Require(2, 5, null, _ => Absent)).NativeCode);
        int Numbered(byte id) => id == 0xFF ? Declared : Absent;
        Assert.Equal(Absent, Assert.Throws<TransportException>(() => HidReportDescriptor.Require(2, 0, null, Numbered)).NativeCode);
        HidReportDescriptor.Require(2, 0xFF, null, Numbered);
    }
}

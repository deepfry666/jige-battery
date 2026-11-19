using System.Buffers.Binary;

namespace Jige.Core.Adapters;

internal enum UlxQuery : byte { Link = 0x24, Percentage = 0x26, Voltage = 0x05, Charging = 0x25 }

internal static class FinalmouseProtocol
{
    public const int ReportLength = 64;
    private static ReadOnlySpan<int> Millivolts => [3000, 3620, 3660, 3740, 3880, 4170, 4380];
    private static ReadOnlySpan<double> Percentages => [0.2, 5, 10, 25, 50, 75, 100];

    public static byte[] Encode(UlxQuery query)
    {
        var command = query switch
        {
            UlxQuery.Link => (byte)0xA4,
            UlxQuery.Percentage => (byte)0xA6,
            UlxQuery.Voltage => (byte)0x85,
            UlxQuery.Charging => (byte)0xA5,
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
        var report = new byte[ReportLength];
        report[0] = 0x04;
        report[1] = 0x02;
        report[2] = command;
        return report;
    }

    public static bool TryReadPayload(ReadOnlySpan<byte> report, UlxQuery expected, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (report.Length is < 4 or > ReportLength || report[0] != 0x05 ||
            (report[2] & 0x7F) != (byte)expected)
            return false;

        int length = expected switch
        {
            UlxQuery.Link or UlxQuery.Charging => 1,
            UlxQuery.Percentage => 3,
            UlxQuery.Voltage => 2,
            _ => -1
        };
        if (report[3] != length || report[1] != length + 2 || report.Length < 4 + length)
            return false;
        payload = report.Slice(4, length);
        return true;
    }

    public static int? VoltagePercentage(ReadOnlySpan<byte> payload)
    {
        var voltage = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (voltage is 0 or ushort.MaxValue)
            return null;
        var points = Millivolts;
        var levels = Percentages;
        if (voltage < points[0])
            return 0;
        for (int i = 1; i < points.Length; i++)
        {
            if (voltage <= points[i])
            {
                var fraction = (double)(voltage - points[i - 1]) / (points[i] - points[i - 1]);
                return (int)Math.Round(levels[i - 1] + fraction * (levels[i] - levels[i - 1]),
                    MidpointRounding.AwayFromZero);
            }
        }
        return 100;
    }
}

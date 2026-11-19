namespace Jige.Core.Adapters;

internal enum RazerHeadsetQuery : byte { Link = 0x20, Battery = 0x21, Charging = 0x2A }

internal static class RazerHeadsetProtocol
{
    public const int ReportLength = 64;

    public static byte[] EncodeMediaTek(ushort pid, RazerHeadsetQuery query, byte sequence)
    {
        if (pid is not (0x0565 or 0x057A or 0x0577) ||
            query is not (RazerHeadsetQuery.Battery or RazerHeadsetQuery.Charging) &&
            !(pid == 0x0565 && query == RazerHeadsetQuery.Link) ||
            sequence > 31 || (pid != 0x0565 && sequence != 0))
            throw new ArgumentOutOfRangeException(nameof(query));

        // V3/V3 Pro 的纯查询模板供离线测试。冷启动需要会话动作，注册表不启用这两个 PID。
        var frame = new byte[ReportLength];
        frame[0] = 2;
        frame[2] = (byte)(0x60 | sequence);
        frame[6] = 4;
        frame[9] = query == RazerHeadsetQuery.Link ? (byte)0 : (byte)0x80;
        frame[10] = (byte)query;
        frame[62] = Checksum(frame, 0);
        return frame;
    }

    public static bool TryMediaTek(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> request, ushort pid, out byte value)
    {
        value = 0;
        if (pid is not (0x0565 or 0x057A) || request.Length != ReportLength || frame.Length != ReportLength || frame[0] != 2 || frame[1] != 2 ||
            frame[2] != request[2] || frame[9] != request[9] || frame[10] != request[10] ||
            frame[11] != 1 || frame[12] is < 1 or > 49 || frame[6] != frame[12] + 4 || frame[63] != 0)
            return false;

        if (pid == 0x0565 && frame[8] != 0x80 || frame[62] != Checksum(frame, pid == 0x0565 ? 1 : 0))
            return false;
        value = frame[13];
        return true;
    }

    // PA 参考实现先切 E1 远程模式。只保留 query 编解码供离线测试，不注册适配器。
    public static byte[] EncodePa(ushort pid, RazerHeadsetQuery query)
    {
        if (query is not (RazerHeadsetQuery.Battery or RazerHeadsetQuery.Charging))
            throw new ArgumentOutOfRangeException(nameof(query));
        ReadOnlySpan<byte> header = pid switch
        {
            0x0536 or 0x0555 or 0x0556 => [2, 0x80, 8, 0, 0, 0x50, 0x41, 8, 0, 3, (byte)query],
            0x053A => [1, 0x80, 8, 0x50, 0x41, 8, 8, 3, (byte)query],
            _ => throw new ArgumentOutOfRangeException(nameof(pid))
        };
        var frame = new byte[ReportLength];
        header.CopyTo(frame);
        return frame;
    }

    public static bool TryPa(ReadOnlySpan<byte> frame, ushort pid, RazerHeadsetQuery query, out byte value)
    {
        value = 0;
        if (frame.Length != ReportLength || query is not (RazerHeadsetQuery.Battery or RazerHeadsetQuery.Charging))
            return false;
        int commandOffset;
        switch (pid)
        {
            case 0x0536 or 0x0555 or 0x0556 when frame[0] == 2:
                commandOffset = 12;
                break;
            case 0x053A when frame[0] == 1 && frame[3] == 0x50 && frame[4] == 0x49:
                commandOffset = 13;
                break;
            default:
                return false;
        }
        if (frame[commandOffset] != (byte)query || frame[commandOffset + 1] != 1 ||
            frame[commandOffset + 2] < 1 || frame[commandOffset + 2] > ReportLength - commandOffset - 3)
            return false;

        value = frame[commandOffset + 3];
        return query == RazerHeadsetQuery.Charging || value <= 100;
    }

    internal static byte Checksum(ReadOnlySpan<byte> frame, int start)
    {
        byte sum = 0;
        foreach (byte value in frame[start..62])
            sum ^= value;
        return sum;
    }
}

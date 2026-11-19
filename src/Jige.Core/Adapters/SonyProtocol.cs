using System.Runtime.InteropServices;

namespace Jige.Core.Adapters;

internal enum SonyQuery { Link, Model, Battery }

internal static class SonyProtocol
{
    public static byte[] Encode(SonyQuery query) => query switch
    {
        SonyQuery.Link => Convert.FromHexString("0100FC0896C321010101007D"),
        SonyQuery.Model => Convert.FromHexString("0100FC0896C341020101009E"),
        SonyQuery.Battery => Convert.FromHexString("0100FC0896C34104010100A0"),
        _ => throw new ArgumentOutOfRangeException(nameof(query))
    };

    public static bool IsReply(ReadOnlySpan<byte> frame, SonyQuery query)
    {
        (int address, int eventId) = query switch
        {
            SonyQuery.Link => (0x12, 0x01),
            SonyQuery.Model => (0x14, 0x02),
            SonyQuery.Battery => (0x14, 0x04),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
        return IsEvent(frame) && frame[6] == address && frame[7] == eventId &&
            frame[8] == 0x10 && frame[9] == 0x01 && frame[10] == 0x00;
    }

    public static bool TryBattery(ReadOnlySpan<byte> frame, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != 14 || !IsReply(frame, SonyQuery.Battery) || frame[11] > 1 || frame[12] > 100)
            return false;
        level = frame[12];
        charging = frame[11] == 1 ? ChargingState.Charging : ChargingState.NotCharging;
        return true;
    }

    public static bool IsEvent(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 12 || !HasEventHeader(frame) || frame[2] + 3 != frame.Length)
            return false;
        int sum = 0;
        foreach (byte value in frame[3..^1])
            sum += value;
        return (byte)sum == frame[^1];
    }

    public static bool HasEventHeader(ReadOnlySpan<byte> data) =>
        data[0] == 0x04 &&
        (data.Length < 2 || data[1] == 0xFF) &&
        (data.Length < 3 || data[2] >= 9) &&
        (data.Length < 4 || data[3] == 0x00) &&
        (data.Length < 5 || data[4] == 0x96) &&
        (data.Length < 6 || data[5] == 0xC3);
}

internal sealed class SonyEventStream(FrameTrace? trace)
{
    private readonly List<byte> buffer = new(514);

    public bool SawInvalidData { get; private set; }
    public bool HasPendingBytes => buffer.Count != 0;

    public void Append(ReadOnlySpan<byte> chunk) => buffer.AddRange(chunk);

    public bool TryRead(out byte[] frame)
    {
        frame = [];
        while (buffer.Count > 0)
        {
            var data = CollectionsMarshal.AsSpan(buffer);
            int start = 0;
            while (start < data.Length && !SonyProtocol.HasEventHeader(data[start..]))
                start++;
            if (start != 0)
            {
                SawInvalidData = true;
                trace?.Invoke(FrameDirection.Incomplete, data[..start]);
                buffer.RemoveRange(0, start);
                continue;
            }

            if (data.Length < 3 || data.Length < data[2] + 3)
                return false;
            var packet = data[..(data[2] + 3)];
            if (!SonyProtocol.IsEvent(packet))
            {
                // 长度字节也可能损坏，从下一字节重新寻找帧头。
                SawInvalidData = true;
                trace?.Invoke(FrameDirection.Incomplete, data[..1]);
                buffer.RemoveAt(0);
                continue;
            }

            trace?.Invoke(FrameDirection.Received, packet);
            frame = packet.ToArray();
            buffer.RemoveRange(0, packet.Length);
            return true;
        }
        return false;
    }

    public void FlushTrace()
    {
        while (TryRead(out _)) { }
        if (HasPendingBytes)
            trace?.Invoke(FrameDirection.Incomplete, CollectionsMarshal.AsSpan(buffer));
        buffer.Clear();
    }
}

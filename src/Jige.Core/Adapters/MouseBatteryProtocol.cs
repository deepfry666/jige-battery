namespace Jige.Core.Adapters;

internal enum CompxQuery : byte
{
    Online = 0x03,
    Battery = 0x04,
    CidMid = 0x10
}

internal static class MouseBatteryProtocol
{
    public static byte[] Request(MouseFamily family, bool charging = false) => (family, charging) switch
    {
        (MouseFamily.CompxOut or MouseFamily.CompxFeature, false) => CompxRequest(CompxQuery.Battery),
        (MouseFamily.BitMouse, false) => Padded("087D720200010701", 64),
        (MouseFamily.BitMouse, true) => Padded("0885720200010F01", 64),
        (MouseFamily.XviOld, false) => Padded("0000028F01", 65),
        (MouseFamily.XviNew, false) => Padded("00000002020083", 65),
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    // Compx 只读命令白名单：0x03 在线状态（atk-control / OpenMouse）、0x04 电量（多方一致）、0x10 CID/MID 型号识别（OpenMouse / atk-control）。
    public static byte[] CompxRequest(CompxQuery query) => query switch
    {
        CompxQuery.Online => Padded("0803", 17, 0x4A),
        CompxQuery.Battery => Padded("0804", 17, 0x49),
        CompxQuery.CidMid => Padded("0810", 17, 0x3D),
        _ => throw new ArgumentOutOfRangeException(nameof(query))
    };

    private static byte[] Padded(string prefix, int size, byte last = 0)
    {
        var frame = new byte[size];
        Convert.FromHexString(prefix).CopyTo(frame, 0);
        frame[^1] = last;
        return frame;
    }

    public static bool Allows(MouseFamily family, ReadOnlySpan<byte> request) => family switch
    {
        MouseFamily.CompxOut or MouseFamily.CompxFeature =>
            request.SequenceEqual(CompxRequest(CompxQuery.Battery)) ||
            request.SequenceEqual(CompxRequest(CompxQuery.Online)) ||
            request.SequenceEqual(CompxRequest(CompxQuery.CidMid)),
        MouseFamily.BitMouse =>
            request.SequenceEqual(Request(family)) || request.SequenceEqual(Request(family, true)),
        MouseFamily.XviOld or MouseFamily.XviNew =>
            request.SequenceEqual(Request(family)),
        _ => false
    };

    public static byte CompxChecksum(ReadOnlySpan<byte> data) => unchecked((byte)(0x55 - Sum(data)));
    public static byte BitMouseChecksum(ReadOnlySpan<byte> data) => unchecked((byte)Sum(data));

    private static int Sum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte value in data)
            sum += value;
        return sum;
    }

    public static bool TryCompx(ReadOnlySpan<byte> frame, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != 17 || !frame[..5].SequenceEqual((ReadOnlySpan<byte>)[8, 4, 0, 0, 0]) ||
            frame[5] is < 2 or > 10 || frame[6] > 100 || CompxChecksum(frame[..16]) != frame[16])
            return false;
        level = frame[6];
        charging = Charge(frame[7]);
        return true;
    }

    /// <summary>同接收器其他集合（如 FF05/FF06）可能返回更长的报告缓冲，校验前 17 字节是否含合法 Compx 电量帧。</summary>
    public static bool TryCompxFromCollection(ReadOnlySpan<byte> frame, out int level, out ChargingState charging)
    {
        if (frame.Length >= 17)
            return TryCompx(frame[..17], out level, out charging);
        level = 0;
        charging = ChargingState.Unknown;
        return false;
    }

    public static bool TryCompxOnline(ReadOnlySpan<byte> frame, out bool online)
    {
        online = false;
        if (frame.Length < 17)
            return false;
        var packet = frame[..17];
        if (!packet[..5].SequenceEqual((ReadOnlySpan<byte>)[8, 3, 0, 0, 0]) ||
            packet[5] is < 1 or > 10 || CompxChecksum(packet[..16]) != packet[16])
            return false;
        online = packet[6] == 1;
        return true;
    }

    public static bool TryCompxCidMid(ReadOnlySpan<byte> frame, out byte cid, out byte mid)
    {
        cid = 0;
        mid = 0;
        if (frame.Length < 17)
            return false;
        var packet = frame[..17];
        if (!packet[..5].SequenceEqual((ReadOnlySpan<byte>)[8, 0x10, 0, 0, 0]) ||
            packet[5] is < 2 or > 10 || CompxChecksum(packet[..16]) != packet[16])
            return false;
        cid = packet[6];
        mid = packet[7];
        return true;
    }

    public static bool TryBitMouse(ReadOnlySpan<byte> frame, BitMouseReply layout, bool charging, out byte value)
    {
        value = 0;
        if (frame.Length != 64 || frame[0] != 8)
            return false;
        int start = layout == BitMouseReply.WithChecksum ? 2 : 1;
        if (start == 2 && frame[1] != BitMouseChecksum(frame[2..]))
            return false;
        int length = frame[start + 5];
        // 实机可能报告 3A/3B 序列和目标 0。匹配命令并排除拒绝及主动推送。
        if (frame[start] != 0x72 || frame[start + 1] is 0xFF or 0x73 ||
            frame[start + 4] != (charging ? 0x0F : 0x07) ||
            length < 1 || length > frame.Length - start - 6)
            return false;
        value = frame[start + 6];
        return charging || value <= 100;
    }

    public static bool TryXvi(ReadOnlySpan<byte> frame, MouseFamily family, out int level, out ChargingState charging)
    {
        level = 0;
        charging = ChargingState.Unknown;
        if (frame.Length != 65)
            return false;
        int offset = family switch
        {
            MouseFamily.XviOld when frame[..4].SequenceEqual((ReadOnlySpan<byte>)[0, 0xA1, 2, 0x8F]) => 5,
            MouseFamily.XviNew when frame[..7].SequenceEqual((ReadOnlySpan<byte>)[0, 0xA1, 0, 2, 2, 0, 0x83]) => 7,
            _ => 0
        };
        if (offset == 0 || frame[offset + 1] > 100)
            return false;
        level = frame[offset + 1];
        charging = Charge(frame[offset]);
        return true;
    }

    public static bool IsXviAsleep(ReadOnlySpan<byte> frame, MouseFamily family) =>
        family == MouseFamily.XviOld && frame.Length == 65 &&
        frame[..4].SequenceEqual((ReadOnlySpan<byte>)[0, 2, 2, 0x8F]);

    public static ChargingState Charge(byte value) => value switch
    {
        0 => ChargingState.NotCharging,
        1 => ChargingState.Charging,
        _ => ChargingState.Unknown
    };
}

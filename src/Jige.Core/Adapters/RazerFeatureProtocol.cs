namespace Jige.Core.Adapters;

internal enum RazerFeatureQuery : byte { Battery = 0x80, Charging = 0x84 }

internal sealed record RazerFeatureModel(ushort Pid, string Name, DeviceKind Kind, byte InterfaceNumber,
    byte TransactionId, bool HasCharging);

internal static class RazerFeatureProtocol
{
    public const int ReportLength = 91;

    // 无线接收器 PID、接口与事务号来自设备清单及其 OpenRazer 和 Windows 参数表。
    private static readonly RazerFeatureModel[] Models =
    [
        new(0x009F, "Viper Mini Signature Edition", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00B0, "Cobra Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00B8, "Viper V3 HyperSpeed", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00B9, "Basilisk V3 X HyperSpeed", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00C1, "Viper V3 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00C5, "DeathAdder V3 HyperSpeed", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00CD, "Basilisk V3 Pro 35K", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00D7, "Basilisk V3 Pro 35K Phantom Green", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00BF, "DeathAdder V4 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00DB, "Cobra HyperSpeed", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00D4, "Basilisk Mobile", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00DF, "Viper V3 Pro SE", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00E6, "Viper V4 Pro", DeviceKind.Mouse, 3, 0x1F, true),
        new(0x00E3, "Basilisk V4 Pro", DeviceKind.Mouse, 3, 0x1F, true),
        new(0x02BA, "BlackWidow V4 Mini HyperSpeed", DeviceKind.Keyboard, 3, 0x9F, true),
        new(0x02D5, "BlackWidow V4 Tenkeyless HyperSpeed", DeviceKind.Keyboard, 2, 0x9F, true),
        new(0x02D2, "BlackWidow V4 Low-profile Tenkeyless HyperSpeed", DeviceKind.Keyboard, 2, 0x9F, true)
    ];

    public static RazerFeatureModel? Find(ushort? pid) => Array.Find(Models, model => model.Pid == pid);

    public static byte[] Encode(ushort pid, RazerFeatureQuery query)
    {
        var model = Find(pid) ?? throw new ArgumentOutOfRangeException(nameof(pid));
        if (query != RazerFeatureQuery.Battery && (query != RazerFeatureQuery.Charging || !model.HasCharging))
            throw new ArgumentOutOfRangeException(nameof(query));
        // 索引 0 是 Win32 的 Report ID 槽。协议体从索引 1 开始。
        var report = new byte[ReportLength];
        report[2] = model.TransactionId;
        report[6] = 2;
        report[7] = 7;
        report[8] = (byte)query;
        report[89] = Checksum(report);
        return report;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, RazerFeatureModel model, RazerFeatureQuery query,
        out byte status, out byte value)
    {
        status = value = 0;
        if (frame.Length != ReportLength || frame[0] != 0 || frame[2] != model.TransactionId ||
            frame[3] != 0 || frame[4] != 0 || frame[5] != 0 || frame[6] != 2 ||
            frame[7] != 7 || frame[8] != (byte)query || frame[90] != 0 ||
            frame[89] != Checksum(frame) || frame[1] is < 1 or > 5)
            return false;
        status = frame[1];
        if (status == 2)
            value = frame[10];
        return true;
    }

    public static int Percentage(byte value) => (value * 100 + 127) / 255;

    internal static byte Checksum(ReadOnlySpan<byte> frame)
    {
        byte crc = 0;
        foreach (byte value in frame[3..89])
            crc ^= value;
        return crc;
    }
}

namespace Jige.Core.Adapters;

internal enum RazerFeatureQuery : byte { Battery = 0x80, Charging = 0x84 }

internal sealed record RazerFeatureModel(
    ushort Pid,
    string Name,
    DeviceKind Kind,
    byte InterfaceNumber,
    byte TransactionId,
    bool HasCharging,
    DeviceConnection Connection = DeviceConnection.Receiver,
    int SettleMs = 60);

internal static class RazerFeatureProtocol
{
    public const int ReportLength = 91;

    // 无线接收器与有线充电模式 PID、接口号、事务号与等待时间依据 OpenRazer（razermouse_driver.c / razerkbd_driver.c）、
    // Razer Battery Report（MIT）与 HaloBattery（MIT）逐项核对；有意排除纯有线无电池鼠标 00CB 与蓝牙路径 0095。
    private static readonly RazerFeatureModel[] Models =
    [
        // === 鼠标（72 个 PID：无线接收器、有线充电模式与 HyperPolling 通用接收器） ===
        new(0x001F, "Naga Epic", DeviceKind.Mouse, 0, 0xFF, true, DeviceConnection.UsbCable),
        new(0x0024, "Mamba 2012", DeviceKind.Mouse, 0, 0xFF, true, DeviceConnection.UsbCable),
        new(0x0025, "Mamba 2012", DeviceKind.Mouse, 0, 0xFF, true),
        new(0x0032, "Ouroboros", DeviceKind.Mouse, 0, 0xFF, true, DeviceConnection.UsbCable),
        new(0x003E, "Naga Epic Chroma", DeviceKind.Mouse, 0, 0xFF, true, DeviceConnection.UsbCable),
        new(0x003F, "Naga Epic Chroma", DeviceKind.Mouse, 0, 0xFF, true),
        new(0x0044, "Mamba Chroma", DeviceKind.Mouse, 0, 0xFF, true, DeviceConnection.UsbCable),
        new(0x0045, "Mamba Chroma", DeviceKind.Mouse, 0, 0xFF, true),
        new(0x0059, "Lancehead", DeviceKind.Mouse, 0, 0x3F, true, DeviceConnection.UsbCable),
        new(0x005A, "Lancehead", DeviceKind.Mouse, 0, 0x3F, true),
        new(0x0062, "Atheris", DeviceKind.Mouse, 0, 0x1F, false, SettleMs: 400),
        new(0x006F, "Lancehead Wireless", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x0070, "Lancehead Wireless", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x0072, "Mamba Wireless", DeviceKind.Mouse, 0, 0x3F, true),
        new(0x0073, "Mamba Wireless", DeviceKind.Mouse, 0, 0x3F, true, DeviceConnection.UsbCable),
        new(0x0077, "Pro Click", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x007A, "Viper Ultimate", DeviceKind.Mouse, 0, 0xFF, true, DeviceConnection.UsbCable),
        new(0x007B, "Viper Ultimate", DeviceKind.Mouse, 0, 0xFF, true),
        new(0x007C, "DeathAdder V2 Pro", DeviceKind.Mouse, 0, 0x3F, true, DeviceConnection.UsbCable),
        new(0x007D, "DeathAdder V2 Pro", DeviceKind.Mouse, 0, 0x3F, true),
        new(0x0080, "Pro Click", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x0083, "Basilisk X HyperSpeed", DeviceKind.Mouse, 0, 0xFF, false),
        new(0x0086, "Basilisk Ultimate", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x0088, "Basilisk Ultimate", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x008F, "Naga Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x0090, "Naga Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x0094, "Orochi V2", DeviceKind.Mouse, 0, 0x1F, false, SettleMs: 400),
        new(0x009A, "Pro Click Mini", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x009C, "DeathAdder V2 X HyperSpeed", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x009E, "Viper Mini Signature Edition", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x009F, "Viper Mini Signature Edition", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00A5, "Viper V2 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00A6, "Viper V2 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00A7, "Naga V2 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00A8, "Naga V2 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00AA, "Basilisk V3 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00AB, "Basilisk V3 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00AF, "Cobra Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00B0, "Cobra Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00B3, "HyperPolling Wireless Dongle", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00B4, "Naga V2 HyperSpeed", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00B6, "DeathAdder V3 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00B7, "DeathAdder V3 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00B8, "Viper V3 HyperSpeed", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00B9, "Basilisk V3 X HyperSpeed", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00BE, "DeathAdder V4 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00BF, "DeathAdder V4 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00C0, "Viper V3 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00C1, "Viper V3 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00C2, "DeathAdder V3 Pro", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00C3, "DeathAdder V3 Pro", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00C4, "DeathAdder V3 HyperSpeed", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00C5, "DeathAdder V3 HyperSpeed", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00C7, "Pro Click V2 Vertical Edition", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00C8, "Pro Click V2 Vertical Edition", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00CC, "Basilisk V3 Pro 35K", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00CD, "Basilisk V3 Pro 35K", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00D0, "Pro Click V2", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00D1, "Pro Click V2", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00D3, "Basilisk Mobile", DeviceKind.Mouse, 0, 0x1F, false, DeviceConnection.UsbCable),
        new(0x00D4, "Basilisk Mobile", DeviceKind.Mouse, 0, 0x1F, false),
        new(0x00D6, "Basilisk V3 Pro 35K Phantom Green", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00D7, "Basilisk V3 Pro 35K Phantom Green", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00DA, "Cobra HyperSpeed", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00DB, "Cobra HyperSpeed", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00DE, "Viper V3 Pro SE", DeviceKind.Mouse, 0, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00DF, "Viper V3 Pro SE", DeviceKind.Mouse, 0, 0x1F, true),
        new(0x00E0, "Orochi V2 (2026)", DeviceKind.Mouse, 0, 0x1F, false, SettleMs: 400),
        new(0x00E2, "Basilisk V4 Pro", DeviceKind.Mouse, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00E3, "Basilisk V4 Pro", DeviceKind.Mouse, 3, 0x1F, true),
        new(0x00E5, "Viper V4 Pro", DeviceKind.Mouse, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x00E6, "Viper V4 Pro", DeviceKind.Mouse, 3, 0x1F, true),

        // === 键盘（15 个 PID：无线接收器与有线充电模式，MI 与 TID 逐项对齐 OpenRazer razerkbd_driver.c） ===
        new(0x0258, "BlackWidow V3 Mini HyperSpeed", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x025A, "BlackWidow V3 Pro", DeviceKind.Keyboard, 2, 0x3F, true, DeviceConnection.UsbCable),
        new(0x025C, "BlackWidow V3 Pro", DeviceKind.Keyboard, 2, 0x9F, true),
        new(0x0271, "BlackWidow V3 Mini HyperSpeed", DeviceKind.Keyboard, 3, 0x9F, true),
        new(0x0290, "DeathStalker V2 Pro", DeviceKind.Keyboard, 2, 0x9F, true),
        new(0x0292, "DeathStalker V2 Pro", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x0296, "DeathStalker V2 Pro TKL", DeviceKind.Keyboard, 2, 0x9F, true),
        new(0x0298, "DeathStalker V2 Pro TKL", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x02B9, "BlackWidow V4 Mini HyperSpeed", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x02BA, "BlackWidow V4 Mini HyperSpeed", DeviceKind.Keyboard, 3, 0x9F, true),
        new(0x02CD, "Joro", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x02D2, "BlackWidow V4 Low-profile Tenkeyless HyperSpeed", DeviceKind.Keyboard, 2, 0x9F, true),
        new(0x02D4, "BlackWidow V4 Low-profile Tenkeyless HyperSpeed", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable),
        new(0x02D5, "BlackWidow V4 Tenkeyless HyperSpeed", DeviceKind.Keyboard, 2, 0x9F, true),
        new(0x02D7, "BlackWidow V4 Tenkeyless HyperSpeed", DeviceKind.Keyboard, 3, 0x1F, true, DeviceConnection.UsbCable)
    ];

    public static IReadOnlyList<RazerFeatureModel> AllModels => Models;

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

    /// <summary>
    /// 识别合法的雷蛇 90B 回复，但命令类/命令 ID 不属于本次电量或充电查询（典型如 Synapse / 灯效软件发送的 0F:03 覆盖了特征缓冲）。
    /// 校验长度、Report ID、状态范围与 CRC，避免把乱码帧误当作其他程序占用。
    /// </summary>
    public static bool IsForeignReply(ReadOnlySpan<byte> frame, RazerFeatureModel model, RazerFeatureQuery query) =>
        frame.Length == ReportLength && frame[0] == 0 && frame[1] is >= 1 and <= 5 &&
        frame[90] == 0 && frame[89] == Checksum(frame) &&
        (frame[7] != 7 || frame[8] != (byte)query) &&
        frame[7] != 0;

    public static int Percentage(byte value) => (value * 100 + 127) / 255;

    internal static byte Checksum(ReadOnlySpan<byte> frame)
    {
        byte crc = 0;
        foreach (byte value in frame[3..89])
            crc ^= value;
        return crc;
    }
}

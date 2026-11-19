using System.Runtime.Versioning;
using System.Text;
using Jige.Core;
using Jige.Core.Adapters;
using Jige.Core.Windows;

return await Probe.RunAsync(args);

internal static class Probe
{
    private const int DefaultBaud = 115200;
    private const int AlternateBaud = 460800;
    private const string Usage = """
        几格 / Jige Battery 探针
          Jige.Probe list
          Jige.Probe read [--raw] [--sony-baud 115200|460800|both]
        Sony 默认使用 115200。按 Ctrl+C 取消。
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 1 && args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out bool list, out bool raw, out int baud))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("设备枚举和读取需要 Windows。");
            return 2;
        }

        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            return await RunWindowsAsync(list, raw, baud, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("已取消。");
            return 130;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("设备枚举超时。");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static bool TryParse(string[] args, out bool list, out bool raw, out int baud)
    {
        list = args.Length > 0 && args[0] == "list";
        raw = false;
        baud = DefaultBaud;
        if (list)
            return args.Length == 1;
        if (args.Length == 0 || args[0] != "read")
            return false;
        bool hasBaud = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--raw" when !raw:
                    raw = true;
                    break;
                case "--sony-baud" when !hasBaud && i + 1 < args.Length:
                    baud = args[++i] switch { "115200" => DefaultBaud, "460800" => AlternateBaud, "both" => 0, _ => -1 };
                    if (baud < 0)
                        return false;
                    hasBaud = true;
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunWindowsAsync(bool list, bool raw, int baud, CancellationToken token)
    {
        var inventory = await WindowsDeviceDiscovery.ScanAsync(token,
            list ? null : static (vid, pid) => vid == 0x361D && pid == 0x0100);
        foreach (var warning in inventory.Warnings)
            Console.Error.WriteLine($"枚举警告  {warning}");
        if (list)
        {
            PrintInventory(inventory);
            return inventory.Warnings.Count == 0 ? 0 : 1;
        }

        var registry = AdapterRegistry.CreateDefault(baud == 0 ? DefaultBaud : baud);
        var devices = registry.Detect(inventory).ToArray();
        if (!devices.Any(device => device.Target.Endpoint is HidInterface))
            Console.WriteLine("未找到 Finalmouse ULX 的 361D:0100 接收器。");
        if (!devices.Any(device => device.Target.Endpoint is UsbSerialPort))
            Console.WriteLine("未找到 Sony INZONE H9 一代的 054C:0E53 MI_06 串口，请检查接收器的 PC 档。");

        var transport = new WindowsReceiverTransport();
        var alternate = baud == 0 ? AdapterRegistry.CreateDefault(AlternateBaud) : null;
        var alternateSerial = alternate?.Detect(inventory)
            .Where(device => device.Target.Endpoint is UsbSerialPort)
            .ToDictionary(device => device.Target.Device.Id, StringComparer.OrdinalIgnoreCase);

        bool failed = devices.Length == 0 || inventory.Warnings.Count != 0;
        foreach (var device in devices)
        {
            bool serial = device.Target.Endpoint is UsbSerialPort;
            PrintTarget(device, serial ? (baud == 0 ? DefaultBaud : baud) : null);
            var reading = await registry.ReadAsync(device, transport, raw ? PrintFrame : null, token);
            PrintReading(reading);
            bool deviceFailed = reading.Percentage is null;
            if (alternateSerial is not null && alternateSerial.TryGetValue(device.Target.Device.Id, out var second) &&
                reading.Issue is not (ReadIssue.Busy or ReadIssue.AccessDenied or ReadIssue.Disconnected))
            {
                PrintTarget(second, AlternateBaud);
                var alternateReading = await alternate!.ReadAsync(second, transport, raw ? PrintFrame : null, token);
                PrintReading(alternateReading);
                deviceFailed &= alternateReading.Percentage is null;
            }
            failed |= deviceFailed;
        }
        return failed ? 1 : 0;
    }

    private static void PrintInventory(DeviceInventory inventory)
    {
        if (inventory.Hid.Count == 0)
            Console.WriteLine("未找到 HID 接口。");
        foreach (var device in inventory.Hid)
        {
            var mi = device.InterfaceNumber?.ToString("X2") ?? "--";
            Console.WriteLine($"HID {Hex(device.VendorId)}:{Hex(device.ProductId)}  {device.Product ?? "产品字符串不可用"}");
            Console.WriteLine($"  使用页/用法 {Hex(device.UsagePage)}:{Hex(device.Usage)}  " +
                $"输入/输出/特征 {Number(device.InputLength)}/{Number(device.OutputLength)}/{Number(device.FeatureLength)}  MI_{mi}");
            Console.WriteLine($"  {device.Path}");
            if (device.MetadataError is not null)
                Console.WriteLine($"  {device.MetadataError}");
        }

        if (inventory.Serial.Count == 0)
            Console.WriteLine("未找到 USB 串口。");
        foreach (var device in inventory.Serial)
        {
            var ids = device.HardwareIds.Count == 0 ? "硬件 ID 不可用" : string.Join(" | ", device.HardwareIds);
            Console.WriteLine($"USB 串口 {device.PortName ?? "--"}  {ids}");
            if (device.MetadataError is not null)
                Console.WriteLine($"  {device.MetadataError}");
        }
    }

    private static string Hex(ushort? value) => value?.ToString("X4") ?? "----";
    private static string Number(ushort? value) => value?.ToString() ?? "--";

    private static void PrintTarget(DetectedDevice device, int? baud)
    {
        Console.WriteLine($"{device.Target.Device.Name}  {device.Target.Device.Id}");
        if (device.Target.Endpoint is UsbSerialPort serial)
            Console.WriteLine($"  {serial.PortName ?? "COM 号未知"}  {baud} 波特");
    }

    private static void PrintReading(BatteryReading reading)
    {
        var percentage = reading.Percentage is int level ? $"{level}%" : "未知";
        var estimated = reading.IsEstimated ? "（电压估算）" : "";
        var charging = reading.Charging switch
        {
            ChargingState.Charging => "充电中",
            ChargingState.NotCharging => "未充电",
            _ => "充电状态未知"
        };
        var link = reading.Link switch
        {
            LinkState.Online => "在线",
            LinkState.Asleep => "休眠或关机",
            LinkState.Offline => "离线",
            LinkState.Busy => "被占用",
            _ => "链路未知"
        };
        Console.WriteLine($"  电量 {percentage}{estimated}  {charging}  {link}  {reading.ReadAt.ToLocalTime():O}");
        if (reading.Detail is not null)
            Console.WriteLine($"  {reading.Detail}");
    }

    private static void PrintFrame(FrameDirection direction, ReadOnlySpan<byte> bytes)
    {
        var label = direction switch
        {
            FrameDirection.Sent => "发送",
            FrameDirection.Received => "接收",
            _ => "接收片段"
        };
        Console.WriteLine($"  {label}  {BitConverter.ToString(bytes.ToArray()).Replace('-', ' ')}");
    }
}

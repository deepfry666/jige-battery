using System.Security.Cryptography;
using System.Text;

namespace Jige.Core;

/// <summary>Windows 本机蓝牙设备属性。同一设备的容器与地址只在内存中匹配。</summary>
public sealed record BluetoothDevice(string Container, string Name, DeviceKind Kind, bool? Connected, int? Percentage)
{
    internal ulong? Address { get; init; }
    internal bool MainNode { get; init; }
}

public static class BluetoothBattery
{
    internal const string UnnamedHidpp = "Logitech 蓝牙设备";

    internal static string DeviceId(string container, ulong? address)
    {
        string key = address is ulong value ? $"bt:{value:X12}" : container.ToUpperInvariant();
        return "bluetooth:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 12));
    }

    /// <summary>合并同一物理蓝牙设备，并优先采用 HID++ 的实时电量与充电状态。</summary>
    public static IReadOnlyList<BatteryReading> Merge(IReadOnlyList<BatteryReading> readings,
        IReadOnlyList<BluetoothDevice> properties, DateTimeOffset now)
    {
        List<BatteryReading> result = [];
        var used = new HashSet<int>();
        foreach (var reading in readings)
        {
            if (reading.Device.Connection != DeviceConnection.Bluetooth)
            {
                result.Add(reading);
                continue;
            }

            int match = -1;
            for (int i = 0; i < properties.Count; i++)
            {
                var device = properties[i];
                if (StringComparer.OrdinalIgnoreCase.Equals(device.Container, reading.BluetoothContainer) ||
                    reading.BluetoothAddress is ulong address && device.Address == address)
                {
                    match = i;
                    break;
                }
            }
            if (match == -1)
            {
                result.Add(reading);
                continue;
            }

            used.Add(match);
            var property = properties[match];
            var identity = reading.Device with
            {
                Id = DeviceId(property.Container, property.Address ?? reading.BluetoothAddress),
                Name = reading.Device.Name == UnnamedHidpp ? property.Name : reading.Device.Name,
                Kind = reading.Device.Kind == DeviceKind.Other ? property.Kind : reading.Device.Kind
            };
            if (reading.Link == LinkState.Online && reading.Percentage is not null)
                result.Add(reading with { Device = identity });
            else if (property.Connected == false)
                result.Add(FromProperty(property, now) with { Device = identity });
            else if (reading.Link != LinkState.Offline && property.Percentage is not null)
                result.Add(FromProperty(property, now) with { Device = identity });
            else
                result.Add(reading with { Device = identity });
        }
        for (int i = 0; i < properties.Count; i++)
        {
            if (!used.Contains(i))
                result.Add(FromProperty(properties[i], now));
        }
        return result.Where(reading => reading.Device.Connection != DeviceConnection.Bluetooth)
            .Concat(result.Where(reading => reading.Device.Connection == DeviceConnection.Bluetooth)
                .GroupBy(reading => reading.Device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(reading => reading.Percentage.HasValue)
                    .ThenByDescending(reading => reading.Link == LinkState.Offline).First()))
            .ToArray();
    }

    internal static BatteryReading FromProperty(BluetoothDevice endpoint, DateTimeOffset now)
    {
        bool valid = endpoint.Percentage is >= 0 and <= 100;
        var link = endpoint.Connected == false ? LinkState.Offline : valid ? LinkState.Online : LinkState.Asleep;
        var device = new DeviceIdentity(DeviceId(endpoint.Container, endpoint.Address), endpoint.Name, endpoint.Kind,
            AdaptationSource.Generic, Connection: DeviceConnection.Bluetooth);
        return new(device, link == LinkState.Online ? endpoint.Percentage : null, false, ChargingState.Unknown, link, now)
        {
            ProtocolSummary = $"蓝牙属性（由 Windows 提供）；HID++ —；功能索引 —；电量属性 {endpoint.Percentage?.ToString() ?? "—"}"
        };
    }
}

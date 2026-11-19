using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jige.Core.Adapters;

namespace Jige.Core.Diagnostics;

internal static partial class DiagnosticPrivacy
{
    [GeneratedRegex(@"^USB\\(VID_[A-F0-9]{4}&PID_[A-F0-9]{4})(?:&(REV_[A-F0-9]{4}))?(?:&(MI_[A-F0-9]{2}))?(?=&|\\|#|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsbIdPattern();

    // 设备字符串由固件控制。仅导出经过核对的完整通用名称，其余使用每份报告独立的指纹。
    private static readonly string[] GenericProducts =
        ["ATK ZERO", "ATK Zero", "ATK A9 Air+", "ATK A9 Ultimate", "ATK F1 Ultimate",
         "Finalmouse UltralightX", "UltralightX", "G-Wolves Receiver RS", "Receiver RS"];

    public static string Product(string? product, byte[] salt)
    {
        if (string.IsNullOrEmpty(product))
            return "-";
        var known = Array.Find(GenericProducts, name => string.Equals(name, product, StringComparison.Ordinal));
        return known ?? $"[已遮蔽 {Convert.ToHexString(HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(product)).AsSpan(0, 6))}]";
    }

    public static string? UsbId(string raw)
    {
        var match = UsbIdPattern().Match(raw);
        if (!match.Success)
            return null;
        return @"USB\" + string.Join('&', match.Groups.Values.Skip(1).Where(group => group.Success)
            .Select(group => group.Value.ToUpperInvariant()));
    }

    public static byte[] HidReply(HidInterface hid, ReadOnlySpan<byte> raw)
    {
        var result = new byte[raw.Length];
        if (FinalmouseAdapter.IsReceiver(hid))
        {
            foreach (var query in new[] { UlxQuery.Link, UlxQuery.Percentage, UlxQuery.Voltage, UlxQuery.Charging })
                if (FinalmouseProtocol.TryReadPayload(raw, query, out var payload))
                    raw[..(4 + payload.Length)].CopyTo(result);
            return result;
        }

        var route = MouseReceiverAdapter.AllowedRoute(hid);
        switch (route?.Family)
        {
            case MouseFamily.CompxOut or MouseFamily.CompxFeature:
                if (MouseBatteryProtocol.TryCompx(raw, out _, out _))
                {
                    raw[..8].CopyTo(result);
                    result[^1] = MouseBatteryProtocol.CompxChecksum(result.AsSpan(0, 16));
                }
                break;
            case MouseFamily.BitMouse:
                if (MouseBatteryProtocol.TryBitMouse(raw, route.BitReply, false, out _) ||
                    MouseBatteryProtocol.TryBitMouse(raw, route.BitReply, true, out _))
                {
                    int length = route.BitReply == BitMouseReply.WithChecksum ? 9 : 8;
                    raw[..length].CopyTo(result);
                    if (route.BitReply == BitMouseReply.WithChecksum)
                        result[1] = MouseBatteryProtocol.BitMouseChecksum(result.AsSpan(2));
                }
                break;
            case MouseFamily.XviOld or MouseFamily.XviNew:
                if (MouseBatteryProtocol.TryXvi(raw, route.Family, out _, out _) ||
                    MouseBatteryProtocol.IsXviAsleep(raw, route.Family))
                {
                    raw[..(route.Family == MouseFamily.XviOld ? 7 : 9)].CopyTo(result);
                    if (route.Family == MouseFamily.XviOld)
                        result[4] = 0;
                }
                break;
        }
        return result;
    }

    public static byte[] SerialReplies(ReadOnlySpan<byte> raw)
    {
        var result = new byte[raw.Length];
        for (int offset = 0; offset + 3 <= raw.Length; )
        {
            int length = raw[offset + 2] + 3;
            if (length > raw.Length - offset || !SonyProtocol.IsEvent(raw.Slice(offset, length)))
            {
                offset++;
                continue;
            }
            var packet = raw.Slice(offset, length);
            var output = result.AsSpan(offset, length);
            if (SonyProtocol.TryBattery(packet, out _, out _))
                packet.CopyTo(output);
            else
            {
                // 保留事件边界和已识别查询的匹配字段，清除其余数据，再重算校验。
                bool queryReply = SonyProtocol.IsReply(packet, SonyQuery.Model) ||
                    SonyProtocol.IsReply(packet, SonyQuery.Link) || SonyProtocol.IsReply(packet, SonyQuery.Battery);
                int headerLength = queryReply ? 11 : 6;
                packet[..headerLength].CopyTo(output);
                // 对格式完整但电量字段无效的回复，保留无效这一语义。
                if (SonyProtocol.IsReply(packet, SonyQuery.Battery) && length == 14)
                    output[11] = 0xFF;
                int sum = 0;
                foreach (byte value in output[3..^1])
                    sum += value;
                output[^1] = unchecked((byte)sum);
            }
            offset += length;
        }
        return result;
    }
}

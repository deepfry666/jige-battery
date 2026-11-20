using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jige.Core.Adapters;

namespace Jige.Core.Diagnostics;

public static partial class DiagnosticPrivacy
{
    [GeneratedRegex(@"^USB\\(VID_[A-F0-9]{4}&PID_[A-F0-9]{4})(?:&(REV_[A-F0-9]{4}))?(?:&(MI_[A-F0-9]{2}))?(?=&|\\|#|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsbIdPattern();

    // 设备字符串由固件控制。仅导出经过核对的完整通用名称，其余使用每份报告独立的指纹。
    private static readonly string[] GenericProducts =
        ["ATK ZERO", "ATK Zero", "ATK A9 Air+", "ATK A9 Ultimate", "ATK F1 Ultimate",
         "Finalmouse UltralightX", "UltralightX", "G-Wolves Receiver RS", "Receiver RS",
         "MX Anywhere 3S", "Logitech MX Anywhere 3S", "MX Keys", "MX Keys S", "MX Keys Mini"];

    // 固件与蓝牙别名均可包含个人信息。日志与历史仅允许完整的目录名称和固定标签。
    public static string SafeDeviceLabel(DeviceIdentity device)
    {
        if (GenericProducts.Contains(device.Name, StringComparer.Ordinal) ||
            DeviceCatalog.Entries.Any(entry => StringComparer.Ordinal.Equals(entry.Model, device.Name)) ||
            device.Name is "ATK 鼠标" or "Finalmouse ULX" or "INZONE H9")
            return device.Name;
        return device.Kind switch
        {
            DeviceKind.Mouse => "无线鼠标",
            DeviceKind.Headset => "无线耳机",
            DeviceKind.Keyboard => "无线键盘",
            _ => "无线外设"
        };
    }

    public static string SanitizeText(string? text, int maxLength = 64)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "-";
        string clean = text.ReplaceLineEndings(" ").Trim();
        if (clean.Contains('\\') || clean.Contains('/') || clean.Contains('@'))
            return "[已遮蔽]";
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    internal static string Product(string? product, byte[] salt)
    {
        if (string.IsNullOrEmpty(product))
            return "-";
        var known = Array.Find(GenericProducts, name => string.Equals(name, product, StringComparison.Ordinal));
        return known ?? $"[已遮蔽 {Convert.ToHexString(HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(product)).AsSpan(0, 6))}]";
    }

    internal static string? UsbId(string raw)
    {
        var match = UsbIdPattern().Match(raw);
        if (!match.Success)
            return null;
        return @"USB\" + string.Join('&', match.Groups.Values.Skip(1).Where(group => group.Success)
            .Select(group => group.Value.ToUpperInvariant()));
    }

    internal static byte[] HidReply(IDeviceAdapter adapter, HidInterface hid, ReadOnlySpan<byte> raw,
        ReadOnlySpan<byte> request, Dictionary<(byte Slot, byte Index), HidppFeature> features)
    {
        var result = new byte[raw.Length];
        switch (adapter)
        {
            case LogitechHidppAdapter:
                CopyHidpp(raw, request, result, features);
                return result;
            case LogitechHeadsetAdapter:
                CopyLogitechHeadset(hid.ProductId!.Value, raw, request, result);
                return result;
            case RazerFeatureAdapter:
                CopyRazerFeature(hid.ProductId, raw, request, result);
                return result;
            case RazerHeadsetAdapter:
                CopyRazerHeadset(raw, request, result);
                return result;
        }
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
                CopyCompx(raw, result);
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
            default:
                // 同接收器其他厂商集合（如 FF05、FF06）上收到的 Compx 回复也按同规则脱敏保留。
                if (adapter is MouseReceiverAdapter)
                    CopyCompx(raw, result);
                break;
        }
        return result;
    }

    private static void CopyCompx(ReadOnlySpan<byte> raw, Span<byte> result)
    {
        if (MouseBatteryProtocol.TryCompxFromCollection(raw, out _, out _))
        {
            raw[..8].CopyTo(result);
            result[16] = MouseBatteryProtocol.CompxChecksum(result[..16]);
        }
        else if (MouseBatteryProtocol.TryCompxOnline(raw, out _))
        {
            // 0x03 在线状态回复只保留状态字节 [6]，清除 [7..15] 中的 RF ID 并重算校验。
            raw[..7].CopyTo(result);
            result[16] = MouseBatteryProtocol.CompxChecksum(result[..16]);
        }
        else if (MouseBatteryProtocol.TryCompxCidMid(raw, out _, out _))
        {
            // 0x10 CID/MID 回复只保留型号字节 [6..7]，清除其余字节并重算校验。
            raw[..8].CopyTo(result);
            result[16] = MouseBatteryProtocol.CompxChecksum(result[..16]);
        }
    }

    private static void CopyHidpp(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> request, Span<byte> result,
        Dictionary<(byte Slot, byte Index), HidppFeature> features)
    {
        if (request.Length < LogitechHidppProtocol.ShortLength || !LogitechHidppProtocol.Matches(raw, request))
            return;

        int length = raw[0] == 0x10 ? LogitechHidppProtocol.ShortLength : LogitechHidppProtocol.LongLength;
        if (raw[2] is 0x8F or 0xFF)
        {
            raw[..6].CopyTo(result);
            return;
        }
        raw[..4].CopyTo(result);
        if (request[0] == 0x10)
        {
            if (request[2] == 0x81 && request[3] is 0x07 or 0x0D)
            {
                result[4] = raw[4];
                if (request[3] == 0x07)
                    result[5] = raw[5];
                else
                    result[6] = raw[6];
            }
            return;
        }
        if (request[2] == 0)
        {
            raw[4..7].CopyTo(result[4..]); // Ping 版本和标记，或 Root 的索引、类型和版本。
            if (request[3] != 0x1A && LogitechHidppProtocol.IsFeatureIndex(raw[4]))
            {
                var feature = (HidppFeature)BinaryPrimitives.ReadUInt16BigEndian(request[4..]);
                features[(request[1], raw[4])] = feature;
            }
            return;
        }

        if (!features.TryGetValue((request[1], request[2]), out var known))
            return;
        int function = request[3] >> 4;
        int payloadEnd = known switch
        {
            HidppFeature.DeviceInfo => 17,
            HidppFeature.DeviceName when function != 1 => 5, // 名称长度或设备类型。
            HidppFeature.Unified => function == 0 ? 6 : 8,
            HidppFeature.Battery or HidppFeature.Voltage or HidppFeature.Adc => 7,
            _ => 4
        };
        raw[4..Math.Min(length, payloadEnd)].CopyTo(result[4..]);
        if (known == HidppFeature.DeviceInfo)
            result[5..Math.Min(length, 9)].Clear(); // 仅保留实体数量、传输和型号字段，清除 Unit ID。
    }

    private static void CopyLogitechHeadset(ushort pid, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> request, Span<byte> result)
    {
        if (LogitechHeadsetProtocol.TryFixedBattery(raw, pid, out _, out _))
        {
            int end = pid switch { 0x0B21 => 14, 0x0B1C => 9, _ => 13 };
            raw[..end].CopyTo(result);
            return;
        }
        if (pid is not (0x0AF7 or 0x0B18) || request.Length != LogitechHeadsetProtocol.ReportLength)
            return;

        int prefix = pid == 0x0AF7 ? 1 : 2;
        var query = request[prefix] switch
        {
            5 => LogitechHeadsetQuery.Bridge,
            10 => LogitechHeadsetQuery.BatteryIndex,
            8 => LogitechHeadsetQuery.Battery,
            _ => (LogitechHeadsetQuery)(-1)
        };
        byte bridge = request[prefix + 2];
        byte index = query == LogitechHeadsetQuery.Battery ? request[prefix + 7] : (byte)0;
        if (LogitechHeadsetProtocol.TryCenturion(raw, pid, query, bridge, index, out var parameters))
        {
            int count = query == LogitechHeadsetQuery.Battery ? 3 : 1;
            int start = prefix + 2 + raw[prefix] - 1 - parameters.Length;
            raw[..(start + count)].CopyTo(result);
        }
    }

    private static void CopyRazerFeature(ushort? pid, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> request, Span<byte> result)
    {
        if (request.Length != RazerFeatureProtocol.ReportLength || RazerFeatureProtocol.Find(pid) is not { } model ||
            !RazerFeatureProtocol.TryDecode(raw, model, (RazerFeatureQuery)request[8], out byte status, out _))
            return;
        raw[..(status == 2 ? 11 : 9)].CopyTo(result);
        result[89] = RazerFeatureProtocol.Checksum(result);
    }

    private static void CopyRazerHeadset(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> request, Span<byte> result)
    {
        if (!RazerHeadsetProtocol.TryMediaTek(raw, request, 0x0565, out _))
            return;
        raw[..3].CopyTo(result);
        result[6] = raw[6];
        raw[8..14].CopyTo(result[8..]);
        result[62] = RazerHeadsetProtocol.Checksum(result, 1);
    }

    internal static byte[] SerialReplies(ReadOnlySpan<byte> raw)
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

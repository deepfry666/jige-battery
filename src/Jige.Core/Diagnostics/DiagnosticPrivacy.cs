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
                CopyRazerHeadset(hid.ProductId, raw, request, result);
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
                if (MouseBatteryProtocol.TryBitMouseLayout(raw, route.BitReply, false, out _, out bool hasChecksum) ||
                    MouseBatteryProtocol.TryBitMouseLayout(raw, route.BitReply, true, out _, out hasChecksum))
                {
                    int length = hasChecksum ? 9 : 8;
                    raw[..length].CopyTo(result);
                    if (hasChecksum)
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

    private static void CopyRazerHeadset(ushort? pid, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> request, Span<byte> result)
    {
        if (pid == RazerHeadsetProtocol.BlackShark2020Pid)
        {
            if (RazerHeadsetProtocol.TryDecodeBlackShark2020(raw, out _, out _, out _, out _))
                raw[..15].CopyTo(result);
            return;
        }
        if (pid is not ushort headsetPid ||
            !RazerHeadsetProtocol.TryMediaTek(raw, request, headsetPid, out _, out int crcStart))
            return;
        raw[..3].CopyTo(result);
        result[6] = raw[6];
        raw[8..14].CopyTo(result[8..]);
        result[62] = RazerHeadsetProtocol.Checksum(result, crcStart);
    }

    /// <summary>仅根据公开的 16 位 USB VID 返回常见外设厂商名称，不包含任何个人或设备唯一信息。</summary>
    internal static string KnownVendorName(ushort? vid) => vid switch
    {
        0x046D => "Logitech",
        0x1532 => "Razer",
        0x373B => "ATK",
        0x33E4 => "G-Wolves",
        0x361D => "Finalmouse",
        0x054C => "Sony",
        0x0B05 => "ASUS",
        0x41E4 => "MCHOSE",
        0x3554 => "Compx",
        0x093A => "PixArt",
        0x1038 => "SteelSeries",
        0x1B1C => "Corsair",
        0x03F0 or 0x0951 => "HyperX",
        0x3434 => "Keychron",
        0x31E3 => "Wooting",
        0x24AE => "Rapoo",
        0x260D => "Darmoshark",
        0x3710 => "WLMouse",
        0x320F => "Evision",
        0x25A7 => "Areson/Compx",
        0x3537 => "VGN/ATK",
        0x045E => "Microsoft",
        0x05AC => "Apple",
        _ => "未知"
    };

    /// <summary>
    /// 当已启用的只读查询收到未通过严格解码的 HID 回复时，仅提取固定协议帧头与校验结果（绝不输出载荷文本或原始字节串），
    /// 便于在无需寄送实机的情况下定位协议变体差异。
    /// </summary>
    internal static string SummarizeUnrecognizedHidReply(
        IDeviceAdapter adapter, HidInterface hid, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> request)
    {
        if (raw.Length < 8)
            return FormattableString.Invariant($"len={raw.Length} short");

        switch (adapter)
        {
            case MouseReceiverAdapter:
            {
                var route = MouseReceiverAdapter.AllowedRoute(hid);
                if (route?.Family == MouseFamily.BitMouse && raw.Length == 64)
                {
                    int magic = raw[2] == 0x72 ? 2 : raw[1] == 0x72 ? 1 : -1;
                    if (magic < 0)
                        return FormattableString.Invariant($"BitMouse len=64 rid={raw[0]:X2} magic=none");
                    bool cksum = raw[1] == MouseBatteryProtocol.BitMouseChecksum(raw[2..]);
                    return FormattableString.Invariant(
                        $"BitMouse len=64 rid={raw[0]:X2} magic@{magic} st={raw[magic + 1]:X2} tgt={raw[magic + 3]:X2} cmd={raw[magic + 4]:X2} plen={raw[magic + 5]:X2} cksum={(cksum ? "OK" : "FAIL")}");
                }
                if (route?.Family is MouseFamily.CompxOut or MouseFamily.CompxFeature && raw.Length >= 17)
                {
                    bool cksum = MouseBatteryProtocol.CompxChecksum(raw[..16]) == raw[16];
                    return FormattableString.Invariant(
                        $"Compx len={raw.Length} rid={raw[0]:X2} cmd={raw[1]:X2} plen={raw[5]:X2} cksum={(cksum ? "OK" : "FAIL")}");
                }
                return FormattableString.Invariant($"len={raw.Length} rid={raw[0]:X2}");
            }
            case RazerHeadsetAdapter when raw.Length == RazerHeadsetProtocol.ReportLength &&
                hid.ProductId is 0x0565 or 0x057A or 0x0577:
            {
                bool crc0 = raw[62] == RazerHeadsetProtocol.Checksum(raw, 0);
                bool crc1 = raw[62] == RazerHeadsetProtocol.Checksum(raw, 1);
                return FormattableString.Invariant(
                    $"RazerMT len=64 rid={raw[0]:X2} flag={raw[1]:X2} seq={raw[2]:X2} flen={raw[6]:X2} dir={raw[8]:X2} dom={raw[9]:X2} cmd={raw[10]:X2} st={raw[11]:X2} vlen={raw[12]:X2} crc0={(crc0 ? "OK" : "FAIL")} crc1={(crc1 ? "OK" : "FAIL")}");
            }
            case RazerFeatureAdapter when raw.Length == RazerFeatureProtocol.ReportLength:
            {
                bool crc = raw[89] == RazerFeatureProtocol.Checksum(raw);
                return FormattableString.Invariant(
                    $"Razer90B len=91 rid={raw[0]:X2} st={raw[1]:X2} tid={raw[2]:X2} cls={raw[7]:X2} cmd={raw[8]:X2} crc={(crc ? "OK" : "FAIL")}");
            }
            default:
                return FormattableString.Invariant($"len={raw.Length} rid={raw[0]:X2}");
        }
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

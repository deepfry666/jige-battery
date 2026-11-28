using Jige.Core;

namespace Jige.Tray;

internal enum DeviceBrand
{
    Unknown,
    Bluetooth,
    Razer,
    AsusRog,
    AsusTuf,
    GWolves,
    Logitech,
    LogitechG,
    Astro,
    SonyInzone,
    SteelSeries,
    HyperX,
    Corsair,
    Finalmouse,
    Pulsar,
    Hitscan,
    Glorious,
    EndgameGear,
    Cougar,
    Redragon,
    Audeze,
    Keychron,
    Atk,
    Vxe,
    Vgn,
    Scyrox,
    Lamzu,
    WLmouse,
    Mchose,
    Dareu,
    Ajazz,
    Eweadn,
    Aula,
    AttackShark,
    Delux,
    Inphic,
    Rapoo,
    Incott
}

/// <summary>
/// 根据设备原始标识（适配器 ID 前缀、型号名、来源标签与连接方式）解析品牌图标。
/// 刻意不读取用户自定义重命名，确保改名后仍保留正确品牌图标。
/// </summary>
internal static class DeviceBrandResolver
{
    public static DeviceBrand Resolve(DeviceIdentity device)
    {
        if (device is null)
            return DeviceBrand.Unknown;

        string id = device.Id ?? "";
        string name = device.Name ?? "";
        string source = device.SourceLabel ?? "";

        // 1. 专属适配器前缀（最高优先级，改名或通用名称也能准确识别）
        if (Starts(id, "inzone-h9:"))
            return DeviceBrand.SonyInzone;
        if (Starts(id, "razer-90b:") || Starts(id, "razer-headset:"))
            return DeviceBrand.Razer;
        if (Starts(id, "ulx:"))
            return DeviceBrand.Finalmouse;
        if (Starts(id, "corsair:"))
            return DeviceBrand.Corsair;
        if (Starts(id, "steelseries:"))
            return DeviceBrand.SteelSeries;
        if (Starts(id, "hyperx:"))
            return DeviceBrand.HyperX;
        if (Starts(id, "mchose-kb:") || Starts(id, "mchose-mouse:"))
            return DeviceBrand.Mchose;
        if (Starts(id, "rapoo-vt9:"))
            return DeviceBrand.Rapoo;
        if (Starts(id, "asus:") || Starts(id, "asus-mouse:") || Starts(id, "asus-headset:") || Starts(id, "asus-omni:"))
            return IsAsusTufOrTx(name) ? DeviceBrand.AsusTuf : DeviceBrand.AsusRog;
        if (Starts(id, "logitech-headset:") || Starts(id, "logitech-hidpp:") || Starts(id, "logitech-hidpp-direct:"))
            return ResolveLogitechLine(name, source);

        // 2. 跨品牌共享接收器在尚未识别到具体型号前保持中性兜底，不误标单一品牌
        if (Contains(name, "Compx 方案") ||
            (Contains(name, "ATTACK SHARK") && Contains(name, "Delux")) ||
            (ContainsWord(name, "VXE") && ContainsWord(name, "VGN")))
            return Fallback(device);

        // 3. 型号名 / 来源标签解析（覆盖多品牌共用适配器与 Windows 蓝牙设备）
        if (IsAsusTufOrTx(name))
            return DeviceBrand.AsusTuf;
        if (ContainsWord(name, "ROG") || ContainsWord(name, "ASUS") || Contains(name, "华硕"))
            return DeviceBrand.AsusRog;

        if (ContainsWord(name, "ASTRO"))
            return DeviceBrand.Astro;
        if (IsLogitechDevice(name, source))
            return ResolveLogitechLine(name, source);

        if (ContainsWord(name, "Sony") || ContainsWord(name, "INZONE") || Contains(name, "索尼") ||
            Contains(name, "WH-1000") || Contains(name, "WF-1000") || Contains(name, "DualSense"))
            return DeviceBrand.SonyInzone;

        if (ContainsWord(name, "Razer") || Contains(name, "雷蛇") ||
            Contains(name, "BlackShark") || Contains(name, "DeathAdder") || ContainsWord(name, "Viper") ||
            Contains(name, "Basilisk") || Contains(name, "Huntsman") || Contains(name, "BlackWidow") ||
            Contains(name, "Barracuda"))
            return DeviceBrand.Razer;

        if (Contains(name, "G-Wolves") || Contains(name, "GWolves") || Contains(name, "游狼"))
            return DeviceBrand.GWolves;
        if (Contains(name, "SteelSeries") || Contains(name, "赛睿") || ContainsWord(name, "Arctis") || ContainsWord(name, "Aerox"))
            return DeviceBrand.SteelSeries;
        if (Contains(name, "HyperX") || Contains(name, "极度未知"))
            return DeviceBrand.HyperX;
        if (ContainsWord(name, "Corsair") || Contains(name, "海盗船"))
            return DeviceBrand.Corsair;
        if (Contains(name, "Finalmouse") || Contains(name, "UltralightX"))
            return DeviceBrand.Finalmouse;
        if (ContainsWord(name, "Pulsar") || Contains(name, "脉冲星"))
            return DeviceBrand.Pulsar;
        if (ContainsWord(name, "Hitscan"))
            return DeviceBrand.Hitscan;
        if (ContainsWord(name, "Glorious"))
            return DeviceBrand.Glorious;
        if (Contains(name, "Endgame Gear") || Contains(name, "EndgameGear") || ContainsWord(name, "XM2we"))
            return DeviceBrand.EndgameGear;
        if (ContainsWord(name, "Cougar") || Contains(name, "骨伽"))
            return DeviceBrand.Cougar;
        if (ContainsWord(name, "Redragon") || Contains(name, "红龙"))
            return DeviceBrand.Redragon;
        if (ContainsWord(name, "Audeze"))
            return DeviceBrand.Audeze;
        if (ContainsWord(name, "Keychron") || Contains(name, "渴创"))
            return DeviceBrand.Keychron;

        if (ContainsWord(name, "ATK"))
            return DeviceBrand.Atk;
        if (ContainsWord(name, "VXE") || ContainsWord(name, "MAD"))
            return DeviceBrand.Vxe;
        if (ContainsWord(name, "VGN"))
            return DeviceBrand.Vgn;
        if (ContainsWord(name, "Scyrox"))
            return DeviceBrand.Scyrox;
        if (ContainsWord(name, "Lamzu") || Contains(name, "兰族"))
            return DeviceBrand.Lamzu;
        if (Contains(name, "WLmouse") || Contains(name, "WL mouse") || Contains(name, "万灵"))
            return DeviceBrand.WLmouse;
        if (ContainsWord(name, "MCHOSE") || Contains(name, "迈从"))
            return DeviceBrand.Mchose;
        if (ContainsWord(name, "DAREU") || Contains(name, "达尔优"))
            return DeviceBrand.Dareu;
        if (ContainsWord(name, "AJAZZ") || Contains(name, "黑爵"))
            return DeviceBrand.Ajazz;
        if (ContainsWord(name, "EWEADN") || Contains(name, "前行者"))
            return DeviceBrand.Eweadn;
        if (ContainsWord(name, "AULA") || Contains(name, "狼蛛"))
            return DeviceBrand.Aula;
        if (Contains(name, "ATTACK SHARK") || Contains(name, "AttackShark") || Contains(name, "攻击鲨"))
            return DeviceBrand.AttackShark;
        if (ContainsWord(name, "Delux") || Contains(name, "多彩"))
            return DeviceBrand.Delux;
        if (ContainsWord(name, "Inphic") || Contains(name, "英菲克"))
            return DeviceBrand.Inphic;
        if (ContainsWord(name, "Rapoo") || Contains(name, "雷柏"))
            return DeviceBrand.Rapoo;
        if (ContainsWord(name, "Incott") || Contains(name, "因科特"))
            return DeviceBrand.Incott;

        return Fallback(device);
    }

    private static DeviceBrand Fallback(DeviceIdentity device) =>
        device.Connection == DeviceConnection.Bluetooth || Starts(device.Id ?? "", "bt:")
            ? DeviceBrand.Bluetooth
            : DeviceBrand.Unknown;

    private static bool IsAsusTufOrTx(string name) =>
        ContainsWord(name, "TUF") || Contains(name, "TX Gaming") || Contains(name, "天选") ||
        (ContainsWord(name, "TX") && (ContainsWord(name, "ASUS") || Contains(name, "华硕")));

    private static bool IsLogitechDevice(string name, string source) =>
        Contains(source, "罗技") || ContainsWord(source, "Logitech") ||
        ContainsWord(name, "Logitech") || ContainsWord(name, "Logi") || Contains(name, "罗技") ||
        Contains(name, "MX Keys") || Contains(name, "MX Master") || Contains(name, "MX Anywhere") ||
        Contains(name, "MX Vertical") || Contains(name, "MX Ergo") || Contains(name, "MX Mechanical") ||
        Contains(name, "ERGO M575") || Contains(name, "ERGO K860") || Contains(name, "Signature M") ||
        Contains(name, "Signature K") || Contains(name, "Pebble ") || Contains(name, "LIGHTSPEED") ||
        Contains(name, "POWERPLAY") || Contains(name, "SUPERLIGHT") || Contains(name, "G PRO") ||
        ContainsWord(name, "G309") || ContainsWord(name, "G435") || ContainsWord(name, "G515") ||
        ContainsWord(name, "G522") || ContainsWord(name, "G325") || ContainsWord(name, "G705") ||
        ContainsWord(name, "G715") || ContainsWord(name, "G915");

    /// <summary>区分罗技旗下 ASTRO、Logitech G 游戏外设与 Logitech（MX / 办公 / 通用）三条产品线。</summary>
    private static DeviceBrand ResolveLogitechLine(string name, string source)
    {
        if (ContainsWord(name, "ASTRO"))
            return DeviceBrand.Astro;

        if (Contains(source, "LIGHTSPEED") || Contains(name, "LIGHTSPEED") ||
            Contains(name, "POWERPLAY") || Contains(name, "SUPERLIGHT") ||
            Contains(name, "Logitech G") || Contains(name, "G PRO") ||
            Contains(name, "PRO X") || Contains(name, "PRO 2") ||
            Contains(name, "PRO Wireless") || Contains(name, "PRO Gaming") ||
            HasLogitechGModelNumber(name))
            return DeviceBrand.LogitechG;

        return DeviceBrand.Logitech;
    }

    private static bool HasLogitechGModelNumber(string text)
    {
        ReadOnlySpan<char> span = text.AsSpan();
        for (int i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] is not ('G' or 'g'))
                continue;
            if (i > 0 && char.IsAsciiLetterOrDigit(span[i - 1]))
                continue;
            int digits = 0;
            while (i + 1 + digits < span.Length && char.IsAsciiDigit(span[i + 1 + digits]))
                digits++;
            if (digits is 3 or 4)
            {
                int after = i + 1 + digits;
                // 允许诸如 G502X、G305、G915 等，但避免误判 G10000 之类的长编码。
                if (after >= span.Length || !char.IsAsciiDigit(span[after]))
                    return true;
            }
        }
        return false;
    }

    private static bool Starts(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWord(string text, string word)
    {
        if (text.Length < word.Length || word.Length == 0)
            return false;
        int index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool leftOk = index == 0 || !char.IsAsciiLetterOrDigit(text[index - 1]);
            int end = index + word.Length;
            bool rightOk = end >= text.Length || !char.IsAsciiLetterOrDigit(text[end]);
            if (leftOk && rightOk)
                return true;
            index += word.Length;
        }
        return false;
    }
}

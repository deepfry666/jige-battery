using System.Globalization;
using Jige.Core.Adapters;

namespace Jige.Core;

/// <summary>核心库设备目录条目：集中保存已启用设备的型号、协议家族、验证状态分级与依据（B1 / B2）。</summary>
public sealed record DeviceCatalogEntry(
    ushort VendorId,
    ushort ProductId,
    string Model,
    string ProtocolFamily,
    DeviceKind Kind,
    VerificationTier Verification,
    string AdapterPrefix,
    string Source,
    string? VerifiedNote = null)
{
    public string VidPid => string.Create(CultureInfo.InvariantCulture, $"{VendorId:X4}:{ProductId:X4}");
}

/// <summary>证据不足的路径开关登记项：在打开设备句柄之前跳过，并在诊断报告里说明原因（B3）。</summary>
public sealed record DisabledDevicePath(
    ushort VendorId,
    ushort ProductId,
    string Model,
    string ProtocolFamily,
    string Reason,
    string Source)
{
    public string VidPid => string.Create(CultureInfo.InvariantCulture, $"{VendorId:X4}:{ProductId:X4}");
}

/// <summary>
/// 设备验证状态目录与路径开关（B1 / B2 / B3）。
/// 所有验证状态与禁用路径集中维护在此，界面徽标、诊断报告和文档一致性测试均以此为唯一数据源。
/// </summary>
public static class DeviceCatalog
{
    // Solaar base_usb.py: receivers with HID++ support. The 27 MHz EX100 uses a separate route.
    internal static string? LogitechReceiverFamily(ushort? pid) => pid switch
    {
        0xC548 => "Bolt",
        0xC52B or 0xC532 => "Unifying",
        0xC518 or 0xC51A or 0xC51B or 0xC521 or 0xC525 or 0xC526 or
            0xC52E or 0xC52F or 0xC531 or 0xC534 or 0xC535 or 0xC537 => "Nano",
        0xC539 or 0xC53A or 0xC53D or 0xC53F or 0xC541 or 0xC545 or 0xC547 or 0xC54D => "LIGHTSPEED",
        _ => null
    };

    internal static string? LogitechHidppHeadset(ushort? pid) => pid switch
    {
        0x0A66 => "Logitech G533",
        0x0AC4 => "Logitech G535",
        0x0A5C => "Logitech G633",
        0x0A89 => "Logitech G635",
        0x0A5B => "Logitech G933",
        0x0A87 => "Logitech G935",
        0x0AB5 or 0x0AFE or 0x0B1F => "Logitech G733",
        0x0AA7 => "Logitech G PRO",
        0x0AAA or 0x0ABA => "Logitech G PRO X",
        0x0AFB or 0x0AFC => "Logitech G PRO X 2",
        _ => null
    };

    /// <summary>当前已启用协议路径的设备目录及其三级验证状态。</summary>
    public static IReadOnlyList<DeviceCatalogEntry> Entries { get; } =
    [
        // === 已实机验证（仅在 Windows 实机上实际读到过电量） ===
        new(0x054C, 0x0E53, "Sony INZONE H9（一代接收器）", "Sony INZONE 串口", DeviceKind.Headset,
            VerificationTier.HardwareVerified, "inzone-h9:",
            "InZoneH9Tray / openinzone / inzone-h9-mac", "Windows 11 实机读到 50%（2026-11-19）"),
        new(0x33E4, 0x5617, "G-Wolves HTX Ultra 8K", "XVI-NEW", DeviceKind.Mouse,
            VerificationTier.HardwareVerified, "mouse:",
            "mouse.xyz 官方型号表（AppVer 1.0.0.51）", "Windows 11 实机读到 38%（2026-11-19）"),

        // === 同家族推定（与已实机验证设备同一协议家族，且来自同一份权威型号表） ===
        new(0x33E4, 0x3617, "G-Wolves Fenrir Pro 8K", "XVI-NEW", DeviceKind.Mouse,
            VerificationTier.FamilyInferred, "mouse:",
            "mouse.xyz 官方型号表（XVI=1, IsNewProtocol=1，与 HTX Ultra 8K 同属 XVI-NEW）"),
        new(0x33E4, 0x3817, "G-Wolves HTM Plus", "XVI-NEW", DeviceKind.Mouse,
            VerificationTier.FamilyInferred, "mouse:",
            "mouse.xyz 官方型号表（XVI=1, IsNewProtocol=1，与 HTX Ultra 8K 同属 XVI-NEW）"),
        new(0x33E4, 0x6817, "G-Wolves HSK Pro 2.0 8K", "XVI-NEW", DeviceKind.Mouse,
            VerificationTier.FamilyInferred, "mouse:",
            "mouse.xyz 官方型号表（XVI=1, IsNewProtocol=1，与 HTX Ultra 8K 同属 XVI-NEW）"),

        // === 仅按资料：Finalmouse ULX ===
        new(0x361D, 0x0100, "Finalmouse UltralightX", "ULX", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "ulx:", "HaloBattery（MIT）/ Finalmouse XPanel"),

        // === 仅按资料：ATK Compx-OUT 与 BitMouse ===
        new(0x373B, 0x101A, "ATK 鼠标", "Compx-OUT", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "OpenMouse / atk-control / HaloBattery（MIT）"),
        new(0x373B, 0x1031, "ATK F1 Ultimate", "Compx-OUT", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "OpenMouse / atk-control"),
        new(0x373B, 0x11D9, "ATK 鼠标", "Compx-OUT", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "OpenMouse / atk-control / HaloBattery（MIT）"),
        new(0x373B, 0x1278, "ATK 鼠标", "Compx-OUT", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "OpenMouse / atk-control"),
        new(0x373B, 0x1155, "ATK ZERO", "BitMouse", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "OpenMouse / atkctl"),

        // === 仅按资料：G-Wolves XVI-OLD（独立于 XVI-NEW，尚无 XVI-OLD 实机记录） ===
        new(0x33E4, 0x3917, "G-Wolves VUK", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x7913, "G-Wolves HT-S2", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x3717, "G-Wolves Fenrir Max 8K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5317, "G-Wolves HTS Ultra", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x7713, "G-Wolves HTR", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x7917, "G-Wolves HT-S2 Pro", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5817, "G-Wolves HSK Pro 8K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5807, "G-Wolves HSK Pro 4K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x2717, "G-Wolves HTX Mini 8K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5417, "G-Wolves HTS Plus 8K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5407, "G-Wolves HTS Plus 4K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5717, "G-Wolves HTX 8K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5707, "G-Wolves HTX 4K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5917, "G-Wolves HSK Plus 8K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5907, "G-Wolves HSK Plus 4K", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x7203, "G-Wolves HSK Lite", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x7717, "G-Wolves HTR Pro", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5803, "G-Wolves HSK Pro ACE", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5403, "G-Wolves HTS Plus ACE", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5703, "G-Wolves HTX ACE", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),
        new(0x33E4, 0x5903, "G-Wolves HSK Plus ACE", "XVI-OLD", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "mouse.xyz 官方型号表 / OpenMouse"),

        // === 仅按资料：Logitech HID++ 2.0 接收器 ===
        new(0x046D, 0xC539, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC53A, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC53D, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC53F, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC541, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC545, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC547, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC54D, "Logitech LIGHTSPEED 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi / OpenMouse"),
        new(0x046D, 0xC548, "Logitech Logi Bolt 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi"),
        new(0x046D, 0xC52B, "Logitech Unifying 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi"),
        new(0x046D, 0xC532, "Logitech Unifying 接收器", "Logitech HID++ 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / OpenLogi"),

        // === 仅按资料：Logitech Centurion / ASTRO 耳机 ===
        new(0x046D, 0x0AF7, "Logitech G PRO X 2 LIGHTSPEED", "Logitech Centurion", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl / HaloBattery（MIT）"),
        new(0x046D, 0x0B18, "Logitech G522 LIGHTSPEED", "Logitech Centurion", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl"),
        new(0x046D, 0x0B21, "Logitech G325 LIGHTSPEED", "Logitech Centurion", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl #581"),
        new(0x046D, 0x0B1C, "ASTRO A50 Gen 5", "ASTRO Gen 5", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl"),

        // === 仅按资料：Razer 90B Feature ===
        new(0x1532, 0x009F, "Razer Viper Mini Signature Edition", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00B0, "Razer Cobra Pro", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00B8, "Razer Viper V3 HyperSpeed", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT）"),
        new(0x1532, 0x00B9, "Razer Basilisk V3 X HyperSpeed", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00C1, "Razer Viper V3 Pro", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT）"),
        new(0x1532, 0x00C5, "Razer DeathAdder V3 HyperSpeed", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00CD, "Razer Basilisk V3 Pro 35K", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00D7, "Razer Basilisk V3 Pro 35K Phantom Green", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00BF, "Razer DeathAdder V4 Pro", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / HaloBattery（MIT）"),
        new(0x1532, 0x00DB, "Razer Cobra HyperSpeed", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00D4, "Razer Basilisk Mobile", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00DF, "Razer Viper V3 Pro SE", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x00E6, "Razer Viper V4 Pro", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / HaloBattery（MIT）"),
        new(0x1532, 0x00E3, "Razer Basilisk V4 Pro", "Razer 90B", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / HaloBattery（MIT）"),
        new(0x1532, 0x02BA, "Razer BlackWidow V4 Mini HyperSpeed", "Razer 90B", DeviceKind.Keyboard,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x02D5, "Razer BlackWidow V4 Tenkeyless HyperSpeed", "Razer 90B", DeviceKind.Keyboard,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),
        new(0x1532, 0x02D2, "Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed", "Razer 90B", DeviceKind.Keyboard,
            VerificationTier.DocsOnly, "razer-90b:", "OpenRazer / Razer Battery Report（MIT）"),

        // === 仅按资料：Razer BlackShark 64B ===
        new(0x1532, 0x0565, "Razer BlackShark V2 HyperSpeed", "Razer BlackShark 64B", DeviceKind.Headset,
            VerificationTier.DocsOnly, "razer-headset:", "OpenRazer #2316 / Synapse-for-Linux（MIT）/ V2 HyperSpeed WebHID（MIT）")
    ];

    /// <summary>
    /// 路径开关（B3）：证据不足或存在写入/断链风险的路径集中登记于此。
    /// 所有登记路径在打开设备句柄之前即被跳过，并在诊断报告中列出「已识别，暂未启用：原因」。
    /// </summary>
    public static IReadOnlyList<DisabledDevicePath> DisabledPaths { get; } =
    [
        // 雷蛇 PA 系列：查询前需发送 0xE1 切换远程模式，违背只读原则。
        new(0x1532, 0x0555, "Razer BlackShark V2 Pro 2023（PC）", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式，违背只读原则",
            "HaloBattery providers/blackshark.py（MIT）"),
        new(0x1532, 0x0556, "Razer BlackShark V2 Pro 2023（待确定 SKU）", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式且 SKU 待核实",
            "HaloBattery providers/blackshark.py（MIT）"),
        new(0x1532, 0x053A, "Razer Barracuda Pro", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式，违背只读原则",
            "HaloBattery #10 / providers/blackshark.py（MIT）"),
        new(0x1532, 0x0536, "Razer Barracuda X 2022", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式，违背只读原则",
            "HaloBattery providers/blackshark.py（MIT）"),

        // 雷蛇 BlackShark V3 系列：冷启动会话与断链风险未核实。
        new(0x1532, 0x057A, "Razer BlackShark V3", "Razer BlackShark V3 64B",
            "冷启动会话未证实可只读建立，且高频查询有重置无线链路风险",
            "HaloBattery #292（MIT）"),
        new(0x1532, 0x0577, "Razer BlackShark V3 Pro", "Razer BlackShark V3 64B",
            "冷启动会话未证实可只读建立，回复格式与不同固件变体待核实",
            "OpenRazer #2838"),

        // ATK A9 Ultra（373B:111A）：FF04 精确 Usage 与独立电量抓包待核实。
        new(0x373B, 0x111A, "ATK A9 Ultra", "Compx-FEAT",
            "FF04 接口的精确 Usage 未经实机核实，独立电量抓包待补",
            "atkctl docs/PROTOCOL.md#compx-a9-ultra-compatibility"),

        // ATK 星闪 / 带前置校验 BitMouse 候选：布局与路由未经实机核实。
        new(0x373B, 0x1216, "ATK NK NANO 接收器（A9 Plus NK / A9 3.0 NK 候选）", "BitMouse（前置校验）",
            "带前置校验位布局与星闪接收器路由未经实机核实",
            "atkctl ProtocolTests.swift / research/device-catalog.md"),
        new(0x373B, 0x10C9, "ATK A9 Plus NK 接收器（星闪）", "Compx-OUT 候选",
            "星闪接收器协议家族与接口特征未经实机核实",
            "research/device-catalog.md"),
        new(0x373B, 0x11DC, "ATK A9 Pro Max 2.0 NK", "BitMouse 候选",
            "PID 与协议通道未经实机核实",
            "research/device-catalog.md"),
        new(0x373B, 0x11DB, "ATK A9 Pro Max 2.0 NK（另一候选）", "BitMouse 候选",
            "PID 与协议通道未经实机核实",
            "research/device-catalog.md"),

        // 游狼 Receiver RS（33E4:3854）：Usage、校验式与协议家族冲突。
        new(0x33E4, 0x3854, "G-Wolves Receiver RS", "Compx-OUT / XVI-NEW 候选",
            "精确 Usage 与校验式未核实，且存在 Compx 与 XVI-NEW 候选冲突",
            "gwolves-battery-overlay docs/PROTOCOL.md#6-the-compx-family"),

        // 游狼 Fenrir Asym（33E4:3517）：新旧两代协议冲突。
        new(0x33E4, 0x3517, "G-Wolves Fenrir Asym 8K", "XVI-OLD / XVI-NEW 冲突",
            "社区存在 XVI-OLD 与 XVI-NEW 两代协议冲突记录，待实机抓包确认",
            "mouse.xyz env-models.json / OpenMouse"),

        new(0x046D, 0x0B45, "Logitech G PRO X 3 LIGHTSPEED", "Logitech Centurion",
            "当前版本未启用此路径，等待单独实机核对", "research/acceptance-0.2.4.md"),

        // ASTRO A50 X（046D:0B0B）：集合与查询未核实。
        new(0x046D, 0x0B0B, "ASTRO A50 X", "Logitech 耳机候选",
            "HID 集合与只读电量查询尚未核实",
            "docs/protocols.md / HeadsetControl")
    ];

    public static IEnumerable<DeviceCatalogEntry> HardwareVerified =>
        Entries.Where(entry => entry.Verification == VerificationTier.HardwareVerified);

    public static DeviceCatalogEntry? FindEntry(ushort? vid, ushort? pid)
    {
        if (vid is not ushort v || pid is not ushort p)
            return null;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].VendorId == v && Entries[i].ProductId == p)
                return Entries[i];
        }
        return null;
    }

    public static VerificationTier VerificationFor(ushort? vid, ushort? pid) =>
        FindEntry(vid, pid)?.Verification ?? VerificationTier.DocsOnly;

    /// <summary>
    /// 按设备身份从核心目录解析验证状态：兼容已保存的旧版 state.json（不含 verification 字段时回查目录）。
    /// </summary>
    public static VerificationTier GetVerification(DeviceIdentity device)
    {
        if (device.Source == AdaptationSource.Generic)
            return VerificationTier.DocsOnly;
        if (device.Verification != VerificationTier.DocsOnly)
            return device.Verification;
        for (int i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (device.Id.StartsWith(entry.AdapterPrefix, StringComparison.OrdinalIgnoreCase) &&
                StringComparer.OrdinalIgnoreCase.Equals(entry.Model, device.Name))
                return entry.Verification;
        }
        return VerificationTier.DocsOnly;
    }

    public static DisabledDevicePath? FindDisabledPath(ushort? vid, ushort? pid)
    {
        if (vid is not ushort v || pid is not ushort p)
            return null;
        for (int i = 0; i < DisabledPaths.Count; i++)
        {
            if (DisabledPaths[i].VendorId == v && DisabledPaths[i].ProductId == p)
                return DisabledPaths[i];
        }
        return null;
    }

    public static bool IsDisabledPath(HidInterface endpoint, out DisabledDevicePath? disabled)
    {
        disabled = FindDisabledPath(endpoint.VendorId, endpoint.ProductId);
        return disabled is not null;
    }

    public static bool IsDisabledPath(ReceiverEndpoint endpoint, out DisabledDevicePath? disabled)
    {
        if (endpoint is HidInterface hid)
            return IsDisabledPath(hid, out disabled);
        disabled = null;
        return false;
    }

    public static string TierLabel(VerificationTier tier) => tier switch
    {
        VerificationTier.HardwareVerified => "已实机验证",
        VerificationTier.FamilyInferred => "同家族推定",
        _ => "仅按资料"
    };
}

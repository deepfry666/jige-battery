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
        new(0x373B, 0x1216, "ATK NK NANO 接收器", "BitMouse", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "atkctl docs/PROTOCOL.md"),
        new(0x373B, 0x10C9, "ATK A9 Plus NK 接收器（星闪）", "Compx-OUT", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "OpenMouse captures/atk-a9-plus-nk"),
        new(0x373B, 0x11DC, "ATK A9 Pro Max 2.0 NK", "BitMouse", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "research/device-catalog.md"),
        new(0x373B, 0x11DB, "ATK A9 Pro Max 2.0 NK（另一候选）", "BitMouse", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", "research/device-catalog.md"),

        // === 仅按资料：国产鼠标第一批 ===
        ..MouseReceiverCatalog.DomesticModels.Select(model => new DeviceCatalogEntry(
            model.Receiver.VendorId, model.Receiver.ProductId!.Value, model.Receiver.Model,
            model.Receiver.Family == MouseFamily.CompxOut ? "Compx-OUT" : "XVI-NEW", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", model.Source)),
        ..MchoseMouseAdapter.Models,

        // === 仅按资料：国产鼠标第二批 ===
        ..DomesticMouseStatus.Models,

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

        // === 仅按资料：Logitech HID++ 1.0 / 2.0 接收器（含 Nano） ===
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
        new(0x046D, 0xC518, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC51A, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC51B, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC521, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC525, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC526, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC52E, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC52F, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC531, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC534, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC535, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),
        new(0x046D, 0xC537, "Logitech Nano 接收器", "Logitech HID++ 1.0 / 2.0", DeviceKind.Other,
            VerificationTier.DocsOnly, "logitech-hidpp:", "Solaar / HaloBattery（MIT）"),

        // === 仅按资料：Logitech HID++ 耳机 ===
        new(0x046D, 0x0A66, "Logitech G533", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AC4, "Logitech G535", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0A5C, "Logitech G633", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0A89, "Logitech G635", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0A5B, "Logitech G933", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0A87, "Logitech G935", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AB5, "Logitech G733", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AFE, "Logitech G733", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0B1F, "Logitech G733", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AA7, "Logitech G PRO", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AAA, "Logitech G PRO X", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0ABA, "Logitech G PRO X", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AFB, "Logitech G PRO X 2", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),
        new(0x046D, 0x0AFC, "Logitech G PRO X 2", "Logitech HID++ 耳机", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-hidpp-direct:", "HaloBattery（MIT）/ HeadsetControl"),

        // === 仅按资料：Logitech Centurion / ASTRO 耳机 ===
        new(0x046D, 0x0AF7, "Logitech G PRO X 2 LIGHTSPEED", "Logitech Centurion", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl / HaloBattery（MIT）"),
        new(0x046D, 0x0B18, "Logitech G522 LIGHTSPEED", "Logitech Centurion", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl"),
        new(0x046D, 0x0B21, "Logitech G325 LIGHTSPEED", "Logitech Centurion", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl #581"),
        new(0x046D, 0x0B1C, "ASTRO A50 Gen 5", "ASTRO Gen 5", DeviceKind.Headset,
            VerificationTier.DocsOnly, "logitech-headset:", "HeadsetControl"),

        // === 仅按资料：Razer 90B Feature（全量对齐 OpenRazer 无线鼠键及有线充电模式） ===
        .. RazerFeatureProtocol.AllModels.Select(model => new DeviceCatalogEntry(
            0x1532, model.Pid, $"Razer {model.Name}", "Razer 90B", model.Kind,
            VerificationTier.DocsOnly, "razer-90b:",
            model.Kind == DeviceKind.Keyboard
                ? "OpenRazer / HaloBattery（MIT）"
                : "OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT）")),

        // === 仅按资料 / 已实机验证：Razer BlackShark 纯只读耳机路径 ===
        new(0x1532, 0x0528, "Razer BlackShark V2 Pro (2020)", "Razer BlackShark 64B Feature", DeviceKind.Headset,
            VerificationTier.DocsOnly, "razer-headset:", "OpenRazer #1280 / RazerNariBatteryLevel / HaloBattery（MIT）"),
        new(0x1532, 0x0565, "Razer BlackShark V2 HyperSpeed", "Razer BlackShark 64B", DeviceKind.Headset,
            VerificationTier.DocsOnly, "razer-headset:", "OpenRazer #2316 / Synapse-for-Linux（MIT）/ V2 HyperSpeed WebHID（MIT）"),
        new(0x1532, 0x057A, "Razer BlackShark V3", "Razer BlackShark V3 64B", DeviceKind.Headset,
            VerificationTier.DocsOnly, "razer-headset:", "HaloBattery #292（MIT）/ OpenRazer PR 2794"),
        // 2026-11-20 Windows 11（10.0.26200）实机诊断验证：36 ms 读到 96% NotCharging Online。
        new(0x1532, 0x0577, "Razer BlackShark V3 Pro", "Razer BlackShark V3 64B", DeviceKind.Headset,
            VerificationTier.HardwareVerified, "razer-headset:", "Windows 11 实机诊断验证（2026-11-20）/ OpenRazer #2838"),

        // === 已实机验证 / 仅按资料：ASUS ROG 无线键盘（MI_01 FF00:0001 65B 命令 12 01） ===
        // 0B05:1934 于 2026-11-20 Windows 11（10.0.26200）实机诊断验证：1 ms 读到 100% Charging Online。
        .. AsusHidProtocol.AllKeyboards.Select(model => new DeviceCatalogEntry(
            AsusHidProtocol.VendorId, model.Pid, model.Name, "ASUS ROG 65B", DeviceKind.Keyboard,
            model.Pid == 0x1934 ? VerificationTier.HardwareVerified : VerificationTier.DocsOnly,
            "asus:",
            model.Pid == 0x1934 ? "Windows 11 实机诊断验证（2026-11-20）/ G-Helper" : "G-Helper / HaloBattery（MIT）")),

        // === 仅按资料：MCHOSE 迈从无线键盘（FF02:0002 20B 命令 13 4A） ===
        new(MchoseKbProtocol.VendorId, MchoseKbProtocol.K87ReceiverPid, "MCHOSE K87", "MCHOSE 20B", DeviceKind.Keyboard,
            VerificationTier.DocsOnly, "mchose-kb:", "HaloBattery mchose_kb.py（MIT）"),

        // === 仅按资料：ASUS ROG / TUF 无线鼠标、OMNI 接收器与 ROG Strix Go 2.4 耳机 ===
        .. AsusHidProtocol.AllMice.Select(model => new DeviceCatalogEntry(
            AsusHidProtocol.VendorId, model.Pid, model.Name,
            model.ReportId == 0x03 ? "ASUS ROG 64B (RID 03)" : "ASUS ROG 65B (12 07)",
            DeviceKind.Mouse, VerificationTier.DocsOnly, "asus-mouse:", "G-Helper / HaloBattery（MIT）")),
        .. AsusHidProtocol.AllHeadsets.Select(model => new DeviceCatalogEntry(
            AsusHidProtocol.VendorId, model.Pid, model.Name, "ASUS Strix Go Feature FF",
            DeviceKind.Headset, VerificationTier.DocsOnly, "asus-headset:", "G-Helper / HaloBattery（MIT）")),
        new(AsusHidProtocol.VendorId, AsusHidProtocol.OmniReceiverPid, "ASUS ROG OMNI 接收器", "ASUS ROG OMNI",
            DeviceKind.Other, VerificationTier.DocsOnly, "asus-omni:", "G-Helper / HaloBattery（MIT）"),

        // === 仅按资料：复用 Compx-OUT / XVI-NEW 的跨品牌鼠标（Pulsar / Hitscan / Cougar / Redragon / Corsair / Glorious） ===
        .. MouseReceiverCatalog.BrandModels.Select(model => new DeviceCatalogEntry(
            model.Receiver.VendorId, model.Receiver.ProductId!.Value, model.Receiver.Model,
            model.Receiver.Family == MouseFamily.CompxOut ? "Compx-OUT" : "XVI-NEW", DeviceKind.Mouse,
            VerificationTier.DocsOnly, "mouse:", model.Source)),

        // === 仅按资料：SteelSeries 赛睿无线耳机与鼠标 ===
        .. SteelSeriesProtocol.AllModels.Select(model => new DeviceCatalogEntry(
            SteelSeriesProtocol.VendorId, model.Pid, model.Name, SteelSeriesProtocol.FamilyName(model.Protocol),
            model.Kind, VerificationTier.DocsOnly, "steelseries:", "HeadsetControl / rivalcfg / alloyctl / HaloBattery（MIT）")),

        // === 仅按资料：HyperX 极度未知无线耳机 ===
        .. HyperXHeadsetProtocol.AllModels.Select(model => new DeviceCatalogEntry(
            model.VendorId, model.Pid, model.Name, HyperXHeadsetProtocol.FamilyName(model.Protocol),
            DeviceKind.Headset, VerificationTier.DocsOnly, "hyperx:", "HeadsetControl / HyperHeadset / HaloBattery（MIT）")),

        // === 仅按资料：Corsair 海盗船无线耳机、鼠标与键盘 ===
        .. CorsairProtocol.AllModels.Select(model => new DeviceCatalogEntry(
            CorsairProtocol.VendorId, model.Pid, model.Name, CorsairProtocol.FamilyName(model.Protocol),
            model.Kind, VerificationTier.DocsOnly, "corsair:", "HeadsetControl / ckb-next / OpenLinkHub / HaloBattery（MIT）")),

        // === 仅按资料：分集合 Compx（Endgame Gear XM2we / DAREU 达尔优 A950） ===
        .. SplitCompxProtocol.AllModels.Select(model => new DeviceCatalogEntry(
            model.VendorId, model.ProductId, model.Name, "Compx 分集合 (FF02→FF01)",
            DeviceKind.Mouse, VerificationTier.DocsOnly, "split-compx:", model.Source)),

        // === 仅按资料：扩展品牌协议（Pulsar Sonix / EWEADN / AJAZZ / MCHOSE V3 / AULA / Keychron / Audeze） ===
        .. ExtendedPeripheralProtocol.AllModels.Select(model => new DeviceCatalogEntry(
            model.VendorId, model.ProductId, model.Name, ExtendedPeripheralProtocol.FamilyName(model.Protocol),
            model.Kind, VerificationTier.DocsOnly, "ext:", model.Source))
    ];

    /// <summary>
    /// 路径开关（B3）：仅保留违背只读原则、存在协议家族冲突或无明确接口特征的路径。
    /// 所有登记路径在打开设备句柄之前即被跳过，并在诊断报告中列出「已识别，暂未启用：原因」。
    /// </summary>
    public static IReadOnlyList<DisabledDevicePath> DisabledPaths { get; } =
    [
        new(0x3554, 0xF524, "Zaopin 鼠标", "Compx-OUT", "电量应答存在未核实分支", "Control HUB"),
        new(0x3554, 0xF527, "Zaopin 鼠标", "Compx-OUT", "电量应答存在未核实分支", "Control HUB"),
        new(0x3554, 0xF5D5, "Compx 方案鼠标", "Compx-OUT", "Zaopin 电量应答存在未核实分支", "Control HUB"),
        new(0x3554, 0xFB43, "Attack Shark 鼠标", "Compx-OUT", "电量应答分支未核实", "Control HUB"),
        new(0x3554, 0xFB44, "Attack Shark 鼠标", "Compx-OUT", "电量应答分支未核实", "Control HUB"),
        new(0x3554, 0xF510, "Compx 方案鼠标", "Compx-OUT", "Lamzu 与 Attack Shark 共用 PID，回复分支无法区分", "Lamzu Aurora / Control HUB"),
        new(0x3554, 0xFB26, "Delux 鼠标", "Compx-OUT", "电量应答分支未核实", "Control HUB"),
        new(0x3554, 0xFB23, "Delux 鼠标", "Compx-OUT", "电量应答分支未核实", "Control HUB"),

        // 雷蛇 PA 系列：查询前需发送 0xE1 切换远程模式，违背只读原则。
        new(0x1532, 0x0555, "Razer BlackShark V2 Pro 2023（PC）", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式，违背只读原则",
            "HaloBattery providers/blackshark.py（MIT）"),
        new(0x1532, 0x0556, "Razer BlackShark V2 Pro 2023（待确定 SKU）", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式且 SKU 待核实",
            "HaloBattery providers/blackshark.py（MIT）"),
        new(0x1532, 0x0557, "Razer BlackShark V2 Pro 2023（SKU 候选）", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式且 SKU 待核实",
            "HaloBattery providers/razer.py（MIT）"),
        new(0x1532, 0x0583, "Razer BlackShark V2 Pro 2023（SKU 候选）", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式且 SKU 待核实",
            "HaloBattery providers/blackshark.py（MIT）"),
        new(0x1532, 0x053A, "Razer Barracuda Pro", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式，违背只读原则",
            "HaloBattery #10 / providers/blackshark.py（MIT）"),
        new(0x1532, 0x0536, "Razer Barracuda X 2022", "Razer PA",
            "PA 查询序列涉及 0xE1 切换远程模式，违背只读原则",
            "HaloBattery providers/blackshark.py（MIT）"),
        new(0x1532, 0x0552, "Razer Barracuda X 2022（Macronix / Airoha RACE）", "Razer Airoha RACE",
            "查询序列需切换应用模式与远程模式，违背只读原则",
            "HaloBattery providers/barracuda_x.py（MIT）"),
        new(0x1532, 0x052E, "Razer BlackShark V2 Pro (2020) 充电线", "Razer BlackShark 64B Feature",
            "充电线不提供独立电量读取，请保持无线接收器 1532:0528 插入",
            "HaloBattery providers/blackshark2020.py（MIT）"),

        // ATK A9 Ultra（373B:111A）：FF04 精确 Usage 未核实，待实机报告补全 Usage。
        new(0x373B, 0x111A, "ATK A9 Ultra", "Compx-FEAT",
            "FF04 接口的精确 Usage 未经实机核实，独立电量抓包待补",
            "atkctl docs/PROTOCOL.md#compx-a9-ultra-compatibility"),

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
            "docs/protocols.md / HeadsetControl"),

        // Endgame Gear 4K HS Dongle（3367:1970）：社区实机测试当前 4K 协议无响应，待新抓包确认。
        new(0x3367, 0x1970, "Endgame Gear 4K 接收器", "Endgame Gear 4K 候选",
            "当前 4K 接收器电量报文实机未跑通，暂保持禁用等待抓包确认",
            "HaloBattery providers/endgame_4k.py（MIT，#321/#324）")
    ];

    private static readonly System.Collections.Frozen.FrozenDictionary<(ushort Vid, ushort Pid), DeviceCatalogEntry> EntryIndex =
        BuildFirstIndex(Entries, entry => (entry.VendorId, entry.ProductId));

    private static readonly System.Collections.Frozen.FrozenDictionary<(ushort Vid, ushort Pid), DisabledDevicePath> DisabledIndex =
        BuildFirstIndex(DisabledPaths, entry => (entry.VendorId, entry.ProductId));

    private static System.Collections.Frozen.FrozenDictionary<TKey, TValue> BuildFirstIndex<TKey, TValue>(
        IReadOnlyList<TValue> items, Func<TValue, TKey> keySelector) where TKey : notnull
    {
        var map = new Dictionary<TKey, TValue>(items.Count);
        for (int i = 0; i < items.Count; i++)
            map.TryAdd(keySelector(items[i]), items[i]);
        return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(map);
    }

    public static IEnumerable<DeviceCatalogEntry> HardwareVerified =>
        Entries.Where(entry => entry.Verification == VerificationTier.HardwareVerified);

    public static DeviceCatalogEntry? FindEntry(ushort? vid, ushort? pid) =>
        vid is ushort v && pid is ushort p && EntryIndex.TryGetValue((v, p), out var entry) ? entry : null;

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

    public static DisabledDevicePath? FindDisabledPath(ushort? vid, ushort? pid) =>
        vid is ushort v && pid is ushort p && DisabledIndex.TryGetValue((v, p), out var entry) ? entry : null;

    public static bool IsDisabledPath(HidInterface endpoint, out DisabledDevicePath? disabled)
    {
        disabled = FindDisabledPath(endpoint.VendorId, endpoint.ProductId);
        return disabled is not null;
    }

    public static bool IsDisabledPath(ReceiverEndpoint endpoint, out DisabledDevicePath? disabled)
    {
        if (endpoint is HidInterface hid)
            return IsDisabledPath(hid, out disabled);
        if (endpoint is UsbSerialPort serial)
        {
            foreach (var id in serial.HardwareIds)
            {
                if (TryParseUsbVidPid(id, out ushort vid, out ushort pid) &&
                    FindDisabledPath(vid, pid) is { } match)
                {
                    disabled = match;
                    return true;
                }
            }
        }
        disabled = null;
        return false;
    }

    private static bool TryParseUsbVidPid(string hardwareId, out ushort vid, out ushort pid)
    {
        vid = 0;
        pid = 0;
        int vidIdx = hardwareId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        int pidIdx = hardwareId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        if (vidIdx < 0 || pidIdx < 0 || vidIdx + 8 > hardwareId.Length || pidIdx + 8 > hardwareId.Length)
            return false;
        return ushort.TryParse(hardwareId.AsSpan(vidIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vid) &&
               ushort.TryParse(hardwareId.AsSpan(pidIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid);
    }

    public static string TierLabel(VerificationTier tier) => tier switch
    {
        VerificationTier.HardwareVerified => "已实机验证",
        VerificationTier.FamilyInferred => "同家族推定",
        _ => "仅按资料"
    };
}

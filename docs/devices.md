# 设备支持清单与验证状态

本文与核心库 [`src/Jige.Core/DeviceCatalog.cs`](../src/Jige.Core/DeviceCatalog.cs) 保持同步，并有单元测试校验文档中标为「已实机验证」的设备与目录数据完全一致。

## 验证状态分级说明

几格将已启用的协议路径分为三级（体现在本表、[README.md](../README.md) 和诊断报告中）；主面板与设备管理页的徽标仍保持「已实机验证 / 按协议适配 / 通用适配」三种不变：

- **已实机验证**：已在 Windows 实机上实际读到过电量（目前为 Sony INZONE H9 一代、游狼 HTX Ultra 8K、Razer BlackShark V3 Pro 与 ASUS ROG 无线键盘 `0B05:1934` 四台）。
- **同家族推定**：与已实机验证的设备属于同一协议家族，且来自同一份权威型号表。
- **仅按资料**：依据公开开源实现或协议文档编写，尚未在 Windows 实机上验证过。

---

## 一、已实机验证

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 说明与出处 |
| --- | --- | --- | --- | --- |
| Sony INZONE H9（一代接收器） | `054C:0E53` | Sony INZONE 串口（`MI_06`） | 已实机验证 | Windows 11 实机读到 50%、未充电（2026-11-19）；参考 InZoneH9Tray / openinzone / inzone-h9-mac |
| G-Wolves HTX Ultra 8K | `33E4:5617` | XVI-NEW（`MI_02 / FFFF:0000`） | 已实机验证 | Windows 11 实机读到 38%（2026-11-19）；型号表来自 `mouse.xyz` 官方网页驱动 |
| Razer BlackShark V3 Pro | `1532:0577` | Razer BlackShark V3 64B（`MI_05 / FF14:0001`） | 已实机验证 | Windows 11 实机读到 96%、未充电、36 ms（2026-11-20）；参考 OpenRazer #2838 |
| ASUS ROG 无线键盘 | `0B05:1934` | ASUS ROG 65B（`MI_01 / FF00:0001`） | 已实机验证 | Windows 11 实机读到 100%、充电中、1 ms（2026-11-20）；参考 G-Helper |

## 二、同家族推定

与已实机验证的 G-Wolves HTX Ultra 8K（`33E4:5617`）同属 **XVI-NEW** 协议家族，且同样导入自 `mouse.xyz/Config/env-models.json`（`AppVer 1.0.0.51`，`XVI=1`、`IsNewProtocol=1`）：

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| G-Wolves Fenrir Pro 8K | `33E4:3617` | XVI-NEW | 同家族推定 | `mouse.xyz` 官方型号表 |
| G-Wolves HTM Plus | `33E4:3817` | XVI-NEW | 同家族推定 | `mouse.xyz` 官方型号表 |
| G-Wolves HSK Pro 2.0 8K | `33E4:6817` | XVI-NEW | 同家族推定 | `mouse.xyz` 官方型号表 |

## 三、仅按资料（已启用，待实机验证）

### Finalmouse

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Finalmouse UltralightX | `361D:0100` | ULX（`FF00:0001` 64/64） | 仅按资料 | HaloBattery（MIT）/ Finalmouse XPanel |
| 未收录的 `361D` 接收器（通用适配） | `361D:*` | ULX（三道安全门） | 仅按资料 | 接口特征唯一匹配时尝试 |

### ATK

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| ATK 鼠标（共用接收器，协议返回 CID/MID 时细化型号） | `373B:101A` | Compx-OUT（`FF02:0002` 17/17） | 仅按资料 | OpenMouse / atk-control / HaloBattery（MIT） |
| ATK F1 Ultimate | `373B:1031` | Compx-OUT | 仅按资料 | OpenMouse / atk-control |
| ATK 鼠标（8K 共用接收器，支持 CID/MID 识别如 F1 Ultimate 2.0） | `373B:11D9` | Compx-OUT | 仅按资料 | OpenMouse / atk-control / HaloBattery（MIT） |
| ATK 鼠标（共用接收器） | `373B:1278` | Compx-OUT | 仅按资料 | OpenMouse / atk-control |
| ATK ZERO | `373B:1155` | BitMouse（`FF05:0001` 64/64） | 仅按资料 | OpenMouse / atkctl |
| ATK NK NANO 接收器（A9 Plus NK / A9 3.0 NK） | `373B:1216` | BitMouse（`FF05:0001` 64/64，兼容带/不带前置校验） | 仅按资料 | atkctl `docs/PROTOCOL.md` |
| ATK A9 Plus NK 接收器（星闪） | `373B:10C9` | Compx-OUT（`FF02:0002` 17/17） | 仅按资料 | OpenMouse `captures/atk-a9-plus-nk` |
| ATK A9 Pro Max 2.0 NK | `373B:11DC` / `373B:11DB` | BitMouse（`FF05:0001` 64/64） | 仅按资料 | `research/device-catalog.md` |
| 未收录的 `373B` 接收器（通用适配） | `373B:*` | Compx-OUT / BitMouse（三道安全门） | 仅按资料 | 接口特征唯一匹配时尝试 |

### 国产无线鼠标第一批

按接收器 VID:PID 和 HID 集合精确匹配。共享接收器显示通用名称，详细证据和暂未启用的 PID 见[交付记录](../research/delivery-0.2.15.md)。

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| VXE R1 系列 | `3554:F58A` | Compx-OUT | 仅按资料 | atk-control / HaloBattery |
| VXE / VGN 鼠标 | `3554:F505` | Compx-OUT | 仅按资料 | atk-control / HaloBattery / VGN HUB |
| VXE 鼠标 | `3554:F58E` | Compx-OUT | 仅按资料 | mouse_tray 接收器记录 |
| MAD 鼠标 | `373B:1040` | Compx-OUT | 仅按资料 | ATK HUB / HaloBattery |
| VXE R1 SE+ | `373B:1085` | Compx-OUT | 仅按资料 | HaloBattery |
| Scyrox V8 | `3554:F5F7` | Compx-OUT | 仅按资料 | Scyrox S-center / HaloBattery |
| Scyrox 鼠标 | `3554:F5F4` | Compx-OUT | 仅按资料 | Scyrox S-center |
| Compx 方案鼠标 | `3554:F517` | Compx-OUT | 仅按资料 | Scyrox S-center / Control HUB / OpenMouse |
| VGN F2 Master | `3554:FB56` | Compx-OUT | 仅按资料 | VGN HUB |
| VGN Y2 Ultra | `3554:FB3E` | Compx-OUT | 仅按资料 | VGN HUB |
| VGN Y2 Master | `3554:FB52` | Compx-OUT | 仅按资料 | VGN HUB |
| VGN Y2 Pro Max | `3554:FB54` | Compx-OUT | 仅按资料 | VGN HUB |
| VGN Dragonfly 3 Master | `3554:FB58` | Compx-OUT | 仅按资料 | VGN HUB |
| VGN 鼠标 | `3554:F503` | Compx-OUT | 仅按资料 | VGN HUB |
| VGN 鼠标 | `3554:F50B` | Compx-OUT | 仅按资料 | VGN HUB |
| Compx 方案鼠标 | `3554:F50D` | Compx-OUT | 仅按资料 | Lamzu Aurora / Control HUB |
| Lamzu Maya X 8K | `373E:001E` | XVI-NEW | 仅按资料 | Lamzu Aurora / HaloBattery |
| Lamzu Maya X 1K | `373E:001D` | XVI-NEW | 仅按资料 | Lamzu Aurora |
| WLmouse Beast X Max | `36A7:A880` | XVI-NEW | 仅按资料 | WL MOUSE HUB / HaloBattery |
| WLmouse Beast X Mini Pro | `36A7:A868` | XVI-NEW | 仅按资料 | WL MOUSE HUB |
| WLmouse Beast X | `36A7:A883` | XVI-NEW | 仅按资料 | WL MOUSE HUB |
| WLmouse Beast X 接收器 | `36A7:A887` | XVI-NEW | 仅按资料 | HaloBattery |
| WLmouse 鼠标 | `36A7:A882` | XVI-NEW | 仅按资料 | WL MOUSE HUB |
| MCHOSE A5 Pro Max 1K | `2023:F013` | XVI-NEW | 仅按资料 | HaloBattery / mchose-macos |
| MCHOSE A5 Pro Max 4K | `2023:F015` | XVI-NEW | 仅按资料 | HaloBattery / mchose-macos |
| MCHOSE 鼠标 | `5253:1020` | MCHOSE 反码 Feature | 仅按资料 | mchose-linux / HaloBattery（MIT）；M7 Ultra、L7 Pro 共用接收器 |
| MCHOSE A7 V2 Ultra | `3837:100B` | MCHOSE 反码 Feature | 仅按资料 | HaloBattery #4 描述符 / mchose-linux（MIT）；候选，要求 MI_02 |

### 游狼 G-Wolves XVI-OLD

十六进制命令与 XVI-NEW 不同，且尚未在实机上验证过任意一台 XVI-OLD 型号，因此统一标为「仅按资料」：

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| G-Wolves VUK | `33E4:3917` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HT-S2 | `33E4:7913` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves Fenrir Max 8K | `33E4:3717` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTS Ultra | `33E4:5317` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTR | `33E4:7713` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HT-S2 Pro | `33E4:7917` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Pro 8K | `33E4:5817` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Pro 4K | `33E4:5807` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTX Mini 8K | `33E4:2717` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTS Plus 8K | `33E4:5417` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTS Plus 4K | `33E4:5407` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTX 8K | `33E4:5717` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTX 4K | `33E4:5707` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Plus 8K | `33E4:5917` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Plus 4K | `33E4:5907` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Lite | `33E4:7203` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTR Pro | `33E4:7717` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Pro ACE | `33E4:5803` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTS Plus ACE | `33E4:5403` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HTX ACE | `33E4:5703` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |
| G-Wolves HSK Plus ACE | `33E4:5903` | XVI-OLD | 仅按资料 | `mouse.xyz` 官方型号表 / OpenMouse |

### 罗技 HID++ 1.0 / 2.0、蓝牙与耳机

| 设备 / 接收器 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| LIGHTSPEED 接收器 | `046D:C539`、`046D:C53A`、`046D:C53D`、`046D:C53F`、`046D:C541`、`046D:C545`、`046D:C547`、`046D:C54D` | Logitech HID++ 2.0 | 仅按资料 | Solaar / OpenLogi / OpenMouse |
| Logi Bolt 接收器 | `046D:C548` | Logitech HID++ 2.0 | 仅按资料 | Solaar / OpenLogi |
| Unifying 接收器 | `046D:C52B`、`046D:C532` | Logitech HID++ 2.0 | 仅按资料 | Solaar / OpenLogi |
| Nano 接收器 | `046D:C518/C51A/C51B/C521/C525/C526/C52E/C52F/C531/C534/C535/C537` | HID++ 1.0 / 2.0 | 仅按资料 | Solaar / HaloBattery |
| 其余罗技接收器 | `046D`，接收器名称和 `FF00:0002` 同时匹配 | HID++ 1.0 / 2.0 | 仅按资料 | 通过首个 Ping 安全门后查询 |
| 罗技鼠标、键盘线缆 | 同 PID 的 `FF00:0001` / `FF00:0002` | HID++ 直连 | 仅按资料 | HaloBattery |
| 罗技蓝牙鼠标、键盘 | `046D` 蓝牙 HID，`FF43:0202` | HID++ over BLE | 仅按资料 | Linux HID++、公开协议 |
| 任意品牌蓝牙外设 | Windows 提供电量属性 | Windows 本机属性 | 仅按资料 | BatteryHub / Windows SetupAPI |
| Logitech G533 / G535 / G633 / G635 / G933 / G935 | `046D:0A66/0AC4/0A5C/0A89/0A5B/0A87` | HID++ 耳机 | 仅按资料 | HaloBattery / HeadsetControl |
| Logitech G733 / G PRO / G PRO X / G PRO X 2（HID++ 变体） | `046D:0AB5/0AFE/0B1F/0AA7/0AAA/0ABA/0AFB/0AFC` | HID++ 耳机 | 仅按资料 | HaloBattery / HeadsetControl |
| Logitech G PRO X 2 LIGHTSPEED | `046D:0AF7` | Logitech Centurion | 仅按资料 | HeadsetControl / HaloBattery（MIT） |
| Logitech G522 LIGHTSPEED | `046D:0B18` | Logitech Centurion | 仅按资料 | HeadsetControl |
| Logitech G325 LIGHTSPEED | `046D:0B21` | Logitech Centurion | 仅按资料 | HeadsetControl #581 |
| ASTRO A50 Gen 5 | `046D:0B1C` | ASTRO Gen 5 | 仅按资料 | HeadsetControl |

### Razer 90B Feature 与 BlackShark 64B

Razer 90B 全量对齐 OpenRazer（`razermouse_driver.c` / `razerkbd_driver.c`）、Razer Battery Report（MIT）与 HaloBattery（MIT），覆盖 **72 个无线/有线充电鼠标 PID** 与 **15 个无线/有线充电键盘 PID**（有意排除纯有线无电池鼠标 `1532:00CB` 与蓝牙路径 `1532:0095`，后者由 Windows 蓝牙电量属性覆盖）：

#### Razer 90B 鼠标

| 设备 | 接收器 / 有线 PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Razer Naga Epic | `1532:001F`（有线） | Razer 90B（TID `FF`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Mamba 2012 | `1532:0025` / `1532:0024` | Razer 90B（TID `FF`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Ouroboros | `1532:0032`（有线） | Razer 90B（TID `FF`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Naga Epic Chroma | `1532:003F` / `1532:003E` | Razer 90B（TID `FF`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Mamba Chroma | `1532:0045` / `1532:0044` | Razer 90B（TID `FF`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Lancehead | `1532:005A` / `1532:0059` | Razer 90B（TID `3F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Atheris | `1532:0062` | Razer 90B（TID `1F`，仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Lancehead Wireless | `1532:006F` / `1532:0070` | Razer 90B（TID `1F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Mamba Wireless | `1532:0072` / `1532:0073` | Razer 90B（TID `3F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Pro Click | `1532:0077` / `1532:0080` | Razer 90B（TID `1F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Viper Ultimate | `1532:007B` / `1532:007A` | Razer 90B（TID `FF`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer DeathAdder V2 Pro | `1532:007D` / `1532:007C` | Razer 90B（TID `3F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk X HyperSpeed | `1532:0083` | Razer 90B（TID `FF`，仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk Ultimate | `1532:0088` / `1532:0086` | Razer 90B（TID `1F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Naga Pro | `1532:0090` / `1532:008F` | Razer 90B（TID `1F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Orochi V2 / Orochi V2 (2026) | `1532:0094`、`1532:00E0` | Razer 90B（TID `1F`，仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Pro Click Mini | `1532:009A` | Razer 90B（TID `1F`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer DeathAdder V2 X HyperSpeed | `1532:009C` | Razer 90B（TID `1F`，仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Viper Mini Signature Edition | `1532:009F` / `1532:009E` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Viper V2 Pro | `1532:00A6` / `1532:00A5` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Naga V2 Pro | `1532:00A8` / `1532:00A7` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk V3 Pro | `1532:00AB` / `1532:00AA` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Cobra Pro | `1532:00B0` / `1532:00AF` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer HyperPolling Wireless Dongle | `1532:00B3` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Naga V2 HyperSpeed | `1532:00B4` | Razer 90B（仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer DeathAdder V3 Pro | `1532:00B7` / `1532:00B6`、`1532:00C3` / `1532:00C2` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Razer 90B（仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk V3 X HyperSpeed | `1532:00B9` | Razer 90B（仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer DeathAdder V4 Pro | `1532:00BF` / `1532:00BE` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Viper V3 Pro | `1532:00C1` / `1532:00C0` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer DeathAdder V3 HyperSpeed | `1532:00C5` / `1532:00C4` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Pro Click V2 Vertical Edition | `1532:00C8` / `1532:00C7` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk V3 Pro 35K | `1532:00CD` / `1532:00CC` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Pro Click V2 | `1532:00D1` / `1532:00D0` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk Mobile | `1532:00D4` / `1532:00D3` | Razer 90B（仅电量） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk V3 Pro 35K Phantom Green | `1532:00D7` / `1532:00D6` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Cobra HyperSpeed | `1532:00DB` / `1532:00DA` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Viper V3 Pro SE | `1532:00DF` / `1532:00DE` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk V4 Pro | `1532:00E3` / `1532:00E2` | Razer 90B（MI `03`） | 仅按资料 | OpenRazer #2958 / Razer Battery Report（MIT） |
| Razer Viper V4 Pro | `1532:00E6` / `1532:00E5` | Razer 90B（MI `03`） | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |

#### Razer 90B 键盘

| 设备 | 接收器 / 有线 PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Razer BlackWidow V3 Mini HyperSpeed | `1532:0271`（MI `03`/TID `9F`） / `1532:0258`（MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer BlackWidow V3 Pro | `1532:025C`（MI `02`/TID `9F`） / `1532:025A`（MI `02`/TID `3F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer DeathStalker V2 Pro | `1532:0290`（MI `02`/TID `9F`） / `1532:0292`（MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer DeathStalker V2 Pro TKL | `1532:0296`（MI `02`/TID `9F`） / `1532:0298`（MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer BlackWidow V4 Mini HyperSpeed | `1532:02BA`（MI `03`/TID `9F`） / `1532:02B9`（MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer Joro | `1532:02CD`（有线，MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer |
| Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed | `1532:02D2`（MI `02`/TID `9F`） / `1532:02D4`（MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer BlackWidow V4 Tenkeyless HyperSpeed | `1532:02D5`（MI `02`/TID `9F`） / `1532:02D7`（MI `03`/TID `1F`） | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |

#### Razer 耳机（纯只读路径）

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Razer BlackShark V2 Pro (2020) | `1532:0528` | Razer BlackShark 64B Feature（Report ID `FF`） | 仅按资料 | OpenRazer #1280 / RazerNariBatteryLevel / HaloBattery（MIT） |
| Razer BlackShark V2 HyperSpeed | `1532:0565` | Razer BlackShark 64B | 仅按资料 | OpenRazer #2316 / Synapse-for-Linux（MIT）/ V2 HyperSpeed WebHID（MIT） |
| Razer BlackShark V3 | `1532:057A` | Razer BlackShark V3 64B（纯只读 `21/2A`，无高频心跳） | 仅按资料 | HaloBattery #292（MIT）/ OpenRazer PR 2794 |

### ASUS ROG 无线键盘与 MCHOSE 迈从键盘

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| ASUS ROG 无线键盘（含 Falchion、Claymore II、Azoth / Extreme / X、Strix Scope II 96 等） | `0B05:193C/193E`、`1B04`、`196B`、`1A83/1A85`、`1B3F`、`1C24/1C25`、`1AAE/1B78` | ASUS ROG 65B（`MI_01 / FF00:0001` 命令 `12 01`） | 仅按资料 | G-Helper / HaloBattery（MIT） |
| MCHOSE K87（2.4G 接收器） | `41E4:2001` | MCHOSE 20B（`FF02:0002` 命令 `13 4A`） | 仅按资料 | HaloBattery `mchose_kb.py`（MIT） |

---

## 四、已识别但暂未启用的路径（路径开关）

以下路径因违背只读原则、存在协议冲突或尚缺接口特征，登记在 `DeviceCatalog.DisabledPaths` 中。几格会在**打开设备句柄之前**直接跳过这些路径，不向设备发送任何字节，并在诊断报告里标明「已识别，暂未启用：原因」：

| 设备 | 接收器 VID:PID | 协议家族 | 暂未启用原因 | 出处 |
| --- | --- | --- | --- | --- |
| Razer BlackShark V2 Pro 2023（PC） | `1532:0555` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式，违背只读原则 | HaloBattery `providers/blackshark.py`（MIT） |
| Razer BlackShark V2 Pro 2023（待确定 SKU） | `1532:0556`、`1532:0557`、`1532:0583` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式且 SKU 待核实 | HaloBattery `providers/blackshark.py`（MIT） |
| Razer Barracuda Pro | `1532:053A` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式，违背只读原则 | HaloBattery #10 / `providers/blackshark.py`（MIT） |
| Razer Barracuda X 2022 | `1532:0536` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式，违背只读原则 | HaloBattery `providers/blackshark.py`（MIT） |
| Razer Barracuda X 2022（Macronix / Airoha RACE） | `1532:0552` | Razer Airoha RACE | 查询序列需切换应用模式与远程模式，违背只读原则 | HaloBattery `providers/barracuda_x.py`（MIT） |
| Razer BlackShark V2 Pro (2020) 充电线 | `1532:052E` | Razer BlackShark 64B Feature | 充电线不提供独立电量读取，请保持无线接收器 `1532:0528` 插入 | HaloBattery `providers/blackshark2020.py`（MIT） |
| ATK A9 Ultra | `373B:111A` | Compx-FEAT | `FF04` 接口的精确 Usage 未经实机核实，独立电量抓包待补 | atkctl `docs/PROTOCOL.md` |
| G-Wolves Receiver RS | `33E4:3854` | Compx-OUT / XVI-NEW 候选 | 精确 Usage 与校验式未核实，且存在 Compx 与 XVI-NEW 候选冲突 | `gwolves-battery-overlay` |
| G-Wolves Fenrir Asym 8K | `33E4:3517` | XVI-OLD / XVI-NEW 冲突 | 社区存在 XVI-OLD 与 XVI-NEW 两代协议冲突记录，待实机抓包确认 | `mouse.xyz` / OpenMouse |
| Logitech G PRO X 3 LIGHTSPEED | `046D:0B45` | Logitech Centurion | 当前版本保持关闭，待单独复核 | 0.2.4 验收标准 |
| ASTRO A50 X | `046D:0B0B` | Logitech 耳机候选 | HID 集合与只读电量查询尚未核实 | `docs/protocols.md` / HeadsetControl |

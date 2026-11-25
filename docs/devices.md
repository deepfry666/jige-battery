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

### 国产无线鼠标第二批

五个 2.4 GHz 接收器在鼠标活动或电量变化时上报电量。首次收到报告前显示 `--`，之后等待上报时保留上次读数。旧款雷柏 VT9 Pro 使用只读 Feature 查询。资料与限制见[交付记录](../research/delivery-0.2.16.md)。

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| ATTACK SHARK / Delux 鼠标 | `1D57:FA60` | 1D57 Input 03 | 仅按资料 | HaloBattery / mouse-battery-tray / OpenMouse |
| Inphic IN9 Pro | `1D57:FA65` | 1D57 Input 03 | 仅按资料 | HaloBattery |
| Incott 8K 鼠标 | `093A:522C` | Incott Input 09 | 仅按资料 | OpenMouse |
| Rapoo VT7 | `24AE:1413` | Rapoo Gen 2 Input 07 | 仅按资料 | HaloBattery |
| Rapoo VT9 Pro V2 | `24AE:14A9` | Rapoo Gen 2 Input 07 | 仅按资料 | HaloBattery |
| Rapoo VT9 Pro | `24AE:1205` | Rapoo Feature 2A | 仅按资料 | OpenMouse |

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

### ASUS ROG / TUF 无线外设与 MCHOSE 迈从键盘

| 设备 | 接收器 / 有线 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| ASUS ROG 无线键盘（含 Falchion、Claymore II、Azoth / Extreme / X、Strix Scope II 96 等） | `0B05:193C/193E`、`1B04`、`196B`、`1A83/1A85`、`1B3F`、`1C24/1C25`、`1AAE/1B78` | ASUS ROG 65B（`MI_01 / FF00:0001` 命令 `12 01`） | 仅按资料 | G-Helper / HaloBattery（MIT） |
| ASUS ROG / TUF 无线鼠标（Gladius III、Chakram / X、Harpe Ace / II Ace、Keris / Aimpoint、Spatha X、Pugio II、Strix Impact II、TUF M4 / Mini、TX 系列，共 33 个 PID） | `0B05:1A72/1A70`、`1B0C/1B0A`、`197F/197D`、`1A1A/1A18`、`1A94/1A92`、`1A68/1A66`、`1979/1977`、`19F4`、`1A8D`、`1AF5/1AF3`、`1C5A`、`1C57/1C56`、`1AD0/1C69`、`18E5/18E3`、`1960/195E`、`1A59/1A57`、`1908/1906`、`1949/1947` | ASUS ROG 65B（`12 07`）/ SPEEDNOVA 64B（RID `03`） | 仅按资料 | G-Helper / HaloBattery（MIT） |
| ASUS ROG OMNI 接收器（支持已配对的 Harpe Ace / Keris II / Impact III 鼠标与 Azoth / Scope II 96 / Falchion RX 键盘） | `0B05:1ACE` | ASUS ROG OMNI（`MI_02` `FF02`/`FF01`/`FF00`） | 仅按资料 | G-Helper / HaloBattery（MIT） |
| ASUS ROG Strix Go 2.4 无线耳机 | `0B05:18D6`（无线）、`0B05:18D7`（有线） | ASUS Strix Go Feature `FF` | 仅按资料 | G-Helper / HaloBattery（MIT） |
| MCHOSE K87（2.4G 接收器） | `41E4:2001` | MCHOSE 20B（`FF02:0002` 命令 `13 4A`） | 仅按资料 | HaloBattery `mchose_kb.py`（MIT） |

### SteelSeries 赛睿（耳机与鼠标）

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Arctis Nova 7 / 7 Gen 2 / 7X / 7P / Diablo IV / WoW、Nova 5 / 5X、Nova 3P / 3X、Arctis 7+ 全系、GameBuds / X（25 个 PID） | `1038:22A1/2202/227E/2206/2258/229E/22AD/22A4/22A5/223A/22A9/227A/2232/2253/220A/22A7/2298/2269/226D/220E/2212/2216/2236/230A/2317` | SteelSeries B0（`FFC0:0001`） | 仅按资料 | HeadsetControl / HaloBattery（MIT） |
| Arctis 1 Wireless / Xbox、Arctis 7 / 7X / 7P / 7 (2018)、Arctis 9、Arctis Pro Wireless / 2019、Nova Pro Wireless / X（11 个 PID） | `1038:12B3/12B6/12D7/12D5/12AD/1260/1252/12C2/1290/12E0/12E5` | SteelSeries Arctis / Nova Pro | 仅按资料 | HeadsetControl / Linux / HaloBattery（MIT） |
| Aerox 3 / 5 / 9 Wireless（含特别版）、Rival 3 Wireless / Gen 2（9 个 PID） | `1038:1838/1878/1852/185C/1860/1858/1874/1830/1872` | SteelSeries Aerox `D2` / Rival `AA` | 仅按资料 | rivalcfg / alloyctl / HaloBattery（MIT） |

### HyperX 极度未知无线耳机

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| HyperX Cloud II Wireless（HP / Kingston 两代） | `03F0:0696/018B`、`0951:1718` | HyperX Cloud II（HP `FF90:0303` / Kingston `FF13:0001`） | 仅按资料 | HeadsetControl / HyperHeadset / HaloBattery（MIT） |
| HyperX Cloud Flight Wireless | `0951:16C4/1723` | HyperX Cloud Flight（`21 FF 05` 电压估算） | 仅按资料 | HeadsetControl / HaloBattery（MIT） |
| HyperX Cloud Alpha Wireless | `03F0:098D` | HyperX Cloud Alpha（`FF43:0202` `21 BB`） | 仅按资料 | HeadsetControl / HyperXBatteryMonitor / HaloBattery（MIT） |
| HyperX Cloud III Wireless / Cloud II Core Wireless | `03F0:05B7/0C9D/0995/0795` | HyperX Cloud III（`FF13:0001` `66 89/8A`） | 仅按资料 | HyperHeadset / HaloBattery（MIT） |
| HyperX Cloud III S Wireless | `03F0:02CC/06BE` | HyperX Cloud III S（`0C 02 03 01` 只读 GET） | 仅按资料 | HyperHeadset / HaloBattery（MIT） |

### Corsair 海盗船（耳机、鼠标与键盘）

| 设备 | 接收器 / 有线 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Corsair Void Elite Wireless | `1B1C:0A55` | Corsair Void `C9`（`FFC5:0001`） | 仅按资料 | HeadsetControl / HaloBattery（MIT） |
| Corsair HS80 RGB Wireless（USB） | `1B1C:0A69` | Corsair HS80 USB（`FF42` `02 08 02 0F`） | 仅按资料 | HS80_Control / HaloBattery（MIT） |
| Corsair Dark Core RGB Pro SE、M75 Wireless、K65 PLUS Wireless | `1B1C:1B7F`、`1B1C:1BDC`、`1B1C:2B07` | Corsair Bragi（`FF42:0001` 65B 只读 GET） | 仅按资料 | ckb-next / OpenLinkHub / HaloBattery（MIT） |
| Corsair Virtuoso RGB Wireless SE / XT（接收器与有线） | `1B1C:0A40/0A3E/0A3D/0A64/0A62` | Corsair Bragi（`FF42:0001` 64B 只读 GET） | 仅按资料 | HeadsetControl / HaloBattery（MIT） |
| Corsair Void v2 Wireless、Virtuoso Max Wireless、HS80 Max Wireless | `1B1C:2A08`、`1B1C:2A02`、`1B1C:0A97` | Corsair Void v2（`MI_04` 纯只读握手与 `0F`） | 仅按资料 | HeadsetControl / HaloBattery（MIT） |
| Corsair Sabre v2 Pro Ultralight | `1B1C:2B2A` | Compx-OUT（`FF02:0002`） | 仅按资料 | HaloBattery（MIT） |

### 更多国际与国产外设品牌（Pulsar · Hitscan · Glorious · Endgame Gear · Cougar · Redragon · Audeze · Keychron · DAREU · EWEADN · AJAZZ · AULA · MCHOSE V3）

| 设备 | 接收器 / 有线 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Pulsar X2 V2 Mini、X2 CrazyLight Mini（无线/有线） | `3554:F508/F507`、`3710:5406/3414` | Compx-OUT（`FF02:0002`） | 仅按资料 | python-pulsar-mouse-tool / HaloBattery（MIT） |
| Pulsar X2 V3 Mini（8K 无线/有线） | `3710:5403/3402` | Pulsar Sonix Feature（`MI_03` 65B） | 仅按资料 | pulsar-x3-python / HaloBattery（MIT） |
| Hitscan Hyperlight（8K / 2.4G / 有线） | `3770:0300/0200/0100` | Compx-OUT（`FF02:0002`） | 仅按资料 | hitscan-battery / HaloBattery（MIT） |
| Cougar Revenger Pro / 4K（无线/有线） | `3554:F5DF/F5DE/F5DC` | Compx-OUT（`FF02:0002`） | 仅按资料 | HaloBattery（MIT） |
| Redragon M991（有线） | `3554:F55E` | Compx-OUT（`FF02:0002`） | 仅按资料 | HaloBattery（MIT） |
| Glorious Model O Wireless | `258A:2022` | XVI-NEW（`FFFF:0000` 65B Feature） | 仅按资料 | GloriousBatteryMonitor / HaloBattery（MIT） |
| Endgame Gear XM2we | `3367:1961` | Compx 分集合（`FF02` Feature `08` → `FF01` Input `09`） | 仅按资料 | okms/xm2we-linux / HaloBattery（MIT） |
| DAREU 达尔优 A950 / 鼠标 | `260D:1074`、`260D:1084` | Compx 分集合（`FF02` Feature `08` → `FF01` Input `09`） | 仅按资料 | MousePower（MIT）/ HaloBattery（MIT） |
| EWEADN 前行者 S9 / H2（无线/有线） | `089D:062F`、`088D:062E` | EWEADN `30`（`FF01:0010` 33B） | 仅按资料 | EWEADN E HUB / HaloBattery（MIT） |
| AJAZZ 黑爵 AJ179 V2 MAX（无线/有线） | `249A:5C2F`、`248A:5D2E` | AJAZZ `10/C0`（`MI_02` 33B） | 仅按资料 | aj179-linux（MIT）/ HaloBattery（MIT） |
| AJAZZ 黑爵 AJ179 Pro | `3151:402D` | GearHub V5 Feature（`FFFF:0002` 65B `F7`） | 仅按资料 | OpenMouse 实机记录（`research/domestic-mice-webdriver.md`） |
| MCHOSE 迈从 V3 鼠标（共享接收器） | `3837:1014/1016/1018` | MCHOSE V3 `4D`（`FF01:0001` 64B） | 仅按资料 | OpenMouse（`research/domestic-mice-webdriver.md`） |
| AULA 狼蛛 F87 Pro V2C 键盘 | `0C45:FEFC` | AULA `AA 10` 键盘（`FF80:0001` 33B） | 仅按资料 | AULA HUB / HaloBattery（MIT） |
| Keychron Ultra-Link 8K / M5 / Nape Pro | `3434:D028/D048/D026` | Keychron `B3/B4`（64B Feature → Input） | 仅按资料 | keychron-battery-dkms / HaloBattery（MIT） |
| Audeze Maxwell / Maxwell 2（无线/有线） | `3329:4B19/4B18/4B1A/4B1E/4B29/4B28` | Audeze `0CD6`（`FF13:0001` 单帧只读查询） | 仅按资料 | HeadsetControl / HaloBattery（MIT） |

---

## 四、已识别但暂未启用的路径（路径开关）

以下路径因违背只读原则、存在协议冲突或尚缺接口特征，登记在 `DeviceCatalog.DisabledPaths` 中。几格会在**打开设备句柄之前**直接跳过这些路径，不向设备发送任何字节，并在诊断报告里标明「已识别，暂未启用：原因」：

| 设备 | 接收器 VID:PID | 协议家族 | 暂未启用原因 | 出处 |
| --- | --- | --- | --- | --- |
| Zaopin 鼠标 | `3554:F524`、`3554:F527` | Compx-OUT | 电量应答存在未核实分支 | Control HUB |
| Compx 方案鼠标 | `3554:F5D5` | Compx-OUT | Zaopin 电量应答存在未核实分支 | Control HUB |
| Attack Shark 鼠标 | `3554:FB43`、`3554:FB44` | Compx-OUT | 电量应答分支未核实 | Control HUB |
| Compx 方案鼠标 | `3554:F510` | Compx-OUT | Lamzu 与 Attack Shark 共用 PID，回复分支无法区分 | Lamzu Aurora / Control HUB |
| Delux 鼠标 | `3554:FB26`、`3554:FB23` | Compx-OUT | 电量应答分支未核实 | Control HUB |
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
| Endgame Gear 4K 接收器 | `3367:1970` | Endgame Gear 4K 候选 | 当前 4K 接收器电量报文实机未跑通，暂保持禁用等待抓包确认 | HaloBattery `providers/endgame_4k.py`（MIT，#321/#324） |

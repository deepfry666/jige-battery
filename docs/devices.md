# 设备支持清单与验证状态

本文与核心库 [`src/Jige.Core/DeviceCatalog.cs`](../src/Jige.Core/DeviceCatalog.cs) 保持同步，并有单元测试校验文档中标为「已实机验证」的设备与目录数据完全一致。

## 验证状态分级说明

几格将已启用的协议路径分为三级（体现在本表、[README.md](../README.md) 和诊断报告中）；主面板与设备管理页的徽标仍保持「已实机验证 / 按协议适配 / 通用适配」三种不变：

- **已实机验证**：已在 Windows 实机上实际读到过电量（目前仅 Sony INZONE H9 一代与游狼 HTX Ultra 8K 两台）。
- **同家族推定**：与已实机验证的设备属于同一协议家族，且来自同一份权威型号表。
- **仅按资料**：依据公开开源实现或协议文档编写，尚未在 Windows 实机上验证过。

---

## 一、已实机验证

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 说明与出处 |
| --- | --- | --- | --- | --- |
| Sony INZONE H9（一代接收器） | `054C:0E53` | Sony INZONE 串口（`MI_06`） | 已实机验证 | Windows 11 实机读到 50%、未充电（2026-11-19）；参考 InZoneH9Tray / openinzone / inzone-h9-mac |
| G-Wolves HTX Ultra 8K | `33E4:5617` | XVI-NEW（`MI_02 / FFFF:0000`） | 已实机验证 | Windows 11 实机读到 38%（2026-11-19）；型号表来自 `mouse.xyz` 官方网页驱动 |

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
| 未收录的 `373B` 接收器（通用适配） | `373B:*` | Compx-OUT / BitMouse（三道安全门） | 仅按资料 | 接口特征唯一匹配时尝试 |

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

| 设备 | 接收器 VID:PID | 协议家族 | 验证状态 | 来源 |
| --- | --- | --- | --- | --- |
| Razer Viper Mini Signature Edition | `1532:009F` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Cobra Pro | `1532:00B0` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer Basilisk V3 X HyperSpeed | `1532:00B9` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Viper V3 Pro | `1532:00C1` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT）/ HaloBattery（MIT） |
| Razer DeathAdder V3 HyperSpeed | `1532:00C5` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Basilisk V3 Pro 35K | `1532:00CD` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Basilisk V3 Pro 35K Phantom Green | `1532:00D7` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer DeathAdder V4 Pro | `1532:00BF` | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer Cobra HyperSpeed | `1532:00DB` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Basilisk Mobile | `1532:00D4` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Viper V3 Pro SE | `1532:00DF` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer Viper V4 Pro | `1532:00E6` | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer Basilisk V4 Pro | `1532:00E3` | Razer 90B | 仅按资料 | OpenRazer / HaloBattery（MIT） |
| Razer BlackWidow V4 Mini HyperSpeed | `1532:02BA` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer BlackWidow V4 Tenkeyless HyperSpeed | `1532:02D5` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed | `1532:02D2` | Razer 90B | 仅按资料 | OpenRazer / Razer Battery Report（MIT） |
| Razer BlackShark V2 HyperSpeed | `1532:0565` | Razer BlackShark 64B | 仅按资料 | OpenRazer #2316 / Synapse-for-Linux（MIT）/ V2 HyperSpeed WebHID（MIT） |

---

## 四、已识别但暂未启用的路径（路径开关）

以下路径因证据不足、存在协议冲突或违背只读原则，登记在 `DeviceCatalog.DisabledPaths` 中。几格会在**打开设备句柄之前**直接跳过这些路径，不向设备发送任何字节，并在诊断报告里标明「已识别，暂未启用：原因」：

| 设备 | 接收器 VID:PID | 协议家族 | 暂未启用原因 | 出处 |
| --- | --- | --- | --- | --- |
| Razer BlackShark V2 Pro 2023（PC） | `1532:0555` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式，违背只读原则 | HaloBattery `providers/blackshark.py`（MIT） |
| Razer BlackShark V2 Pro 2023（待确定 SKU） | `1532:0556` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式且 SKU 待核实 | HaloBattery `providers/blackshark.py`（MIT） |
| Razer Barracuda Pro | `1532:053A` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式，违背只读原则 | HaloBattery #10 / `providers/blackshark.py`（MIT） |
| Razer Barracuda X 2022 | `1532:0536` | Razer PA | PA 查询序列涉及 `0xE1` 切换远程模式，违背只读原则 | HaloBattery `providers/blackshark.py`（MIT） |
| Razer BlackShark V3 | `1532:057A` | Razer BlackShark V3 64B | 冷启动会话未证实可只读建立，且高频查询有重置无线链路风险 | HaloBattery #292（MIT） |
| Razer BlackShark V3 Pro | `1532:0577` | Razer BlackShark V3 64B | 冷启动会话未证实可只读建立，回复格式与不同固件变体待核实 | OpenRazer #2838 |
| ATK A9 Ultra | `373B:111A` | Compx-FEAT | `FF04` 接口的精确 Usage 未经实机核实，独立电量抓包待补 | atkctl `docs/PROTOCOL.md` |
| ATK NK NANO 接收器（A9 Plus NK / A9 3.0 NK 候选） | `373B:1216` | BitMouse（前置校验） | 带前置校验位布局与星闪接收器路由未经实机核实 | atkctl `ProtocolTests.swift` |
| ATK A9 Plus NK 接收器（星闪） | `373B:10C9` | Compx-OUT 候选 | 星闪接收器协议家族与接口特征未经实机核实 | `research/device-catalog.md` |
| ATK A9 Pro Max 2.0 NK 候选 | `373B:11DC`、`373B:11DB` | BitMouse 候选 | PID 与协议通道未经实机核实 | `research/device-catalog.md` |
| G-Wolves Receiver RS | `33E4:3854` | Compx-OUT / XVI-NEW 候选 | 精确 Usage 与校验式未核实，且存在 Compx 与 XVI-NEW 候选冲突 | `gwolves-battery-overlay` |
| G-Wolves Fenrir Asym 8K | `33E4:3517` | XVI-OLD / XVI-NEW 冲突 | 社区存在 XVI-OLD 与 XVI-NEW 两代协议冲突记录，待实机抓包确认 | `mouse.xyz` / OpenMouse |
| Logitech G PRO X 3 LIGHTSPEED | `046D:0B45` | Logitech Centurion | 当前版本保持关闭，待单独复核 | 0.2.4 验收标准 |
| ASTRO A50 X | `046D:0B0B` | Logitech 耳机候选 | HID 集合与只读电量查询尚未核实 | `docs/protocols.md` / HeadsetControl |

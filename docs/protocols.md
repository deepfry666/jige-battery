# 设备读取协议

几格已在 Windows 11 实机读到 Sony INZONE H9 一代和游狼 HTX Ultra 8K 的电量。使用 Finalmouse ULX 接收器（固件 `0303`）的用户也已确认 Windows 版 0.2.24 能读到电量。其他设备的资料来源与验证范围见各节。C# 实现依据公开协议与报文独立编写，其中 openinzone 用于核对字段。

## Finalmouse UltralightX

| 项目 | 本阶段实现 | 验证证据 |
| --- | --- | --- |
| 接口 | 厂商 `361D`（已知 `361D:0100`，其余 PID 走通用家族识别），唯一 `FF00:0001`，输入、输出报告各 64 字节，含报告 ID | 固件 `0303` 的 Windows 诊断确认了该集合和长度。未收录 PID 需通过唯一特征、`24` 链路回复校验和异常拦截三道安全门。 |
| 封帧 | Output `04`，后接 `02`、高位置位的命令、`00`、填零。Input `05`，后接长度、命令、载荷长度和数据 | 电压回包的报告 ID、长度和字段见社区实机帧 |
| `24` 链路 | 非零后继续。零表示无线链路断开，按“休眠或关机”显示并结束本轮读取。链路查询无回复时本轮也结束 | 社区记录了链路载荷 `01`。零值无法区分休眠和关机，是否唤醒设备未核实 |
| `26` 电量 | 载荷依次为百分比、低位 mV、高位 mV；接受 0–100。超时或无有效百分比时走 `05` | 有社区固件持续不回复 `26`。百分比回复仍待本项目实机确认 |
| `05` 电压 | 读取小端毫伏，按 XPanel 曲线分段线性插值并标记估算；`0000` 和 `FFFF` 无有效读数 | 社区实机帧 `05 04 05 02 64 10` 对应 4196 mV、78%。同一设备充电时记录载荷 `B4 10`，对应 4276 mV、88%。两值均有离线用例 |
| `25` 充电 | `00` 未充电，`01` 充电，其余为未知 | 社区在插拔充电线时记录了两个值。本项目未在实机验证 |

曲线数据点（mV、百分比）为 `(3000, 0.2)`、`(3620, 5)`、`(3660, 10)`、`(3740, 25)`、`(3880, 50)`、`(4170, 75)`、`(4380, 100)`。充电时端电压偏高，估算值可能随插拔线跳变。

允许的发包前缀固定为 `04 02 A4 00`、`04 02 A6 00`、`04 02 85 00`、`04 02 A5 00`。资料中的十进制 37 和 38 分别对应十六进制 `25` 和 `26`。

2026-11-23 的几格 0.2.23 Windows 诊断显示，USB 接收器 `361D:0100` 固件 `0303` 的厂商集合无 Feature 报告，Windows 能初始化的输入、输出编号均为 `03,05`。此前强制初始化输出 `04` 会在发送第一条查询前返回 `HIDP_STATUS_REPORT_DOES_NOT_EXIST`。遇到这组描述符时，普通轮询、心跳与诊断在打开接口时复核输入和输出 `05`。实际发送的只读帧仍按上述协议使用 `04`。同一用户升级到 0.2.24 后确认这只接收器恢复了电量读数。具体读数来自百分比查询还是电压估算，仍待逐帧记录确认。

- HaloBattery，MIT。提供帧格式、曲线与社区实机记录。[协议实现说明](https://github.com/HeyOkay/HaloBattery/blob/main/providers/finalmouse.py) · [实机讨论 #284](https://github.com/HeyOkay/HaloBattery/pull/284) · [测试记录](https://github.com/HeyOkay/HaloBattery/blob/main/tests/test_device_pr284_finalmouse.py) · [许可证](https://github.com/HeyOkay/HaloBattery/blob/main/LICENSE)
- XPanel 为厂商公开网页，未发现网页源码的开源许可证。厂商单独发布的 USB 权限清单采用 MIT。[XPanel](https://xpanel.finalmouse.com/) · [USB 权限清单和许可证](https://github.com/teamfinalmouse/xpanel-linux-permissions)

## Sony INZONE H9 一代

接收器必须处于 PC 档。仅匹配 USB 硬件 ID 的 `VID_054C&PID_0E53` 和明确的 `MI_06`。H7 可能使用相同接收器，本阶段以 H9 一代接收器显示，尚未据型号回复区分耳机，H7 兼容性未核实。

默认选择 InZoneH9Tray 所用的 115200；也可选 openinzone 和 inzone-h9-mac 记录的 460800。两档均为 8N1、无流控、DTR/RTS 置位。几格已用 115200 在 Windows 11 实机读到 50%、未充电，报告已纳入离线回放。实际音频与休眠表现仍待验证。

| 顺序 | 查询 | 完整白名单帧 |
| --- | --- | --- |
| 1 | 接收器链路状态 GET | `01 00 FC 08 96 C3 21 01 01 01 00 7D` |
| 2 | 耳机型号信息 GET | `01 00 FC 08 96 C3 41 02 01 01 00 9E` |
| 3 | 耳机电量 GET | `01 00 FC 08 96 C3 41 04 01 01 00 A0` |

前两帧沿用社区的初始化查询顺序，设备无回复时仍可进行电量查询。openinzone 记录的官方串口打开流程没有额外握手，也有记录指出该代链路查询不稳定。几格用有效电量回复确认在线。因串口为独占访问，H9 不参与高频在线心跳；检测到 `INZONEHub` 进程运行时主动退让，不打开串口。

事件头为 `04 FF`，第三字节加 3 是整帧长度；按流重组分段回复。校验值为从第 4 字节到校验值之前各字节的和取低 8 位。电量只接受 14 字节的 `14 04 10 01 00` 地址、事件、回复类型和事务号组合，校验保留位和 `96 C3` 键值。载荷为 0 或 1 的充电字节以及 0–100 的百分比。

社区文档的完整记录 `04 FF 0B 00 96 C3 14 04 10 01 00 00 32 B4` 对应 50%、未充电，并用于离线测试。几格实机样本见 [H9 回放记录](../tests/fixtures/h9-gen1-real-2026-11-19.txt)。充电状态的实机变化、两档波特率对同一接收器的表现、Hub 的占用错误码、拔插和取消后的句柄释放均未在实机验证。

- InZoneH9Tray，README 声明 MIT，仓库的 `LICENSE` 链接未找到对应文件。参考其 115200、三条 GET 报文和电量字段记录。[仓库](https://github.com/janit88/InZoneH9Tray) · [报文记录](https://github.com/janit88/InZoneH9Tray/blob/main/inzone_tray.py)
- openinzone，GPL-3.0。核对一代串口设置、事件类型、地址、长度及校验和。[协议文档](https://github.com/penguinwokrs/openinzone/blob/main/docs/PROTOCOL.md) · [许可证](https://github.com/penguinwokrs/openinzone/blob/main/LICENSE)
- inzone-h9-mac，MIT。提供 460800、完整的实机电量收发帧和 H9 充电字节的独立记录。[协议文档](https://github.com/jpcarranza94/inzone-h9-mac/blob/main/docs/protocol.md) · [许可证](https://github.com/jpcarranza94/inzone-h9-mac/blob/main/LICENSE)
- HaloBattery，MIT。提供社区适配记录，测试替身中的拼接回复属于合成样本。[H9 适配记录 #278](https://github.com/HeyOkay/HaloBattery/pull/278) · [许可证](https://github.com/HeyOkay/HaloBattery/blob/main/LICENSE)

## ATK 与游狼鼠标

游狼 HTX Ultra 8K 已由几格在 Windows 11 实机读到电量，其他接收器待验证。匹配表记录的是无线接收器，证据等级沿用[设备清单](../research/device-catalog.md)。每个物理接收器须匹配唯一的协议家族和命令集合。出现多个候选命令集合或跨家族冲突时，安全门 ① 跳过该接收器。对于未收录的 `373B`（ATK）与 `361D`（Finalmouse）接收器，支持按唯一接口特征通用识别并施加三道安全门：① 特征必须唯一对应单个协议家族；② 首条只发无害状态查询，回复格式校验通过后才继续；③ 格式不符立即停止并按 `VID:PID:接口特征` 持久化拦截，重启有效，点击「重新检测」清除。发送前重新检查 VID/PID、厂商使用页和用法、该通道上的报告长度，以及包括补零区在内的完整白名单请求。

共享接收器（如 `373B:11D9`、`101A`、`1278` 及通用识别的 ATK 接收器）不再按 PID 写死型号，默认显示「ATK 鼠标」，仅在协议通过 `0x10`（CID/MID）读出已登记型号（例如 OpenMouse 记录的 F1 Ultimate 2.0 `1/8`、A9 Ultimate `1/3`）时更新名称。Compx-OUT 查询期间仅对主集合发送输出报告，同时可对同一接收器的其他厂商输入集合（如 `FF05`、`FF06`）挂起只读监听以接收跨集合回复。若主集合声明输出 `08` 而未声明输入 `08`，仅当同一接收器上有唯一可用的厂商输入集合声明 `08` 时才发送查询。诊断报告按集合统计收发。

| 家族 | 白名单请求与传输 | 运行状态 | 证据等级 |
| --- | --- | --- | --- |
| Compx-OUT | 电量 `08 04 00×14 49`、在线心跳 `08 03 00×14 4A`、型号 CID/MID `08 10 00×14 3D`；17 字节 Output / Input，先挂读再写 | ATK `101A/1031/10C9/11D9/1278` 与满足三道安全门的通用 `373B` `FF02:0002` 允许查询；游狼 `3854` 的精确 Usage 未核实，仍禁止查询 | `04` 多方开源一致；`03` 来自 [atk-control](https://github.com/NasirA1991/atk-control/blob/main/atk_control/protocol.py) 与 [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/atk/hid.ts)；`10` 来自 OpenMouse、atk-control 与 [atkctl](https://github.com/cobanov/atkctl/blob/1755d8510663/docs/PROTOCOL.md) |
| Compx-FEAT | 同一 17 字节 Compx 请求，SetFeature 发，Input 收 | ATK `111A` 在 `FF04` 的精确 Usage 未核实，仍禁止查询 | 单一来源 |
| BitMouse | `08 7D 72 02 00 01 07 01 00×56` 查电量，`08 85 72 02 00 01 0F 01 00×56` 查充电，64 字节 Output / Input | ATK ZERO `1155`、NK NANO `1216`、`11DC/11DB` 与满足三道安全门的通用 `373B` `FF05:0001` 允许查询（自动兼容带/不带前置校验回复布局） | ZERO 无线路径多方开源实现一致；`1216` 来自 atkctl 实机记录 |
| XVI-OLD | `00 00 02 8F 01 00×60`，65 字节 SetFeature / GetFeature | 从游狼官方网页驱动 [`https://mouse.xyz/Config/env-models.json`](https://mouse.xyz/Config/env-models.json)（`AppVer 1.0.0.51`，`XVI=1`、`IsNewProtocol=0`）导入启用：`2717`（HTX Mini 8K）、`3717`（Fenrir Max 8K）、`3917`（VUK）、`5317`（HTS Ultra）、`5403/5407/5417`（HTS Plus）、`5703/5707/5717`（HTX）、`5803/5807/5817`（HSK Pro）、`5903/5907/5917`（HSK Plus）、`7203`（HSK Lite）、`7713/7717`（HTR / HTR Pro）、`7913/7917`（HT-S2 / Pro） | 官方网页驱动型号表 + 单一开源实现 |
| XVI-NEW | `00 00 00 02 02 00 83 00×58`，65 字节 SetFeature / GetFeature | 从 `mouse.xyz/Config/env-models.json`（`XVI=1`、`IsNewProtocol=1`）导入启用：`3617`（Fenrir Pro 8K）、`3817`（HTM Plus）、`5617`（HTX Ultra 8K）、`6817`（HSK Pro 2.0 8K）；`3517` 存在两代协议冲突记录，保持禁用 | `5617` 已实机验证，其余来自官方网页驱动型号表 |

Compx 回复核对 ID、命令、状态、地址、载荷长度及校验字节。校验为 `0x55` 减去前 16 字节之和后取低 8 位。只解析声明范围内的百分比及充电字节。游狼 Receiver RS 的公开回复尾字节与此校验式不一致，需复核完整原始帧后再考虑启用。离线测试使用 OpenMouse 的 X1 Pro Max 无线完整抓包核对 Compx 帧。Compx-FEAT 与其共享协议编解码，A9 Ultra 用同通道的真实版本回复核验校验和及电量解码对非电量命令的拒绝，独立电量抓包待补。[Compx-OUT 抓包](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/captures/atk-x1-pro-max/receiver.json) · [A9 Ultra 协议](https://github.com/cobanov/atkctl/blob/1755d8510663/docs/PROTOCOL.md#compx-a9-ultra-compatibility) · [A9 Ultra 版本回复](https://github.com/cobanov/atkctl/blob/1755d8510663/Tests/ATKKitTests/CompxTests.swift) · [Receiver RS 回复记录](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/7e11d7154f72/docs/PROTOCOL.md#6-the-compx-family)

BitMouse 请求的校验为 ID 和校验槽之后的字节和取低 8 位。回复按登记布局检查帧头、命令和长度，拒绝 `FF` 和主动推送 `73`。实测有未回显请求序列和目标的回复。带前置校验的布局单独校验，当前无自动启用的该布局接收器。BitMouse 的 `83` 会忘记配对，发送白名单只允许 `07` 和 `0F` 的两条完整帧。[BitMouse 解码](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/src/bitmouse/index.ts) · [BitMouse 实机回复](https://github.com/cobanov/atkctl/blob/1755d8510663/Tests/ATKKitTests/ProtocolTests.swift)

XVI 的 65 字节含 Windows Report ID 槽，SetFeature 后等 100 ms，再做有限次 GetFeature。两代分别校验自己的回复头和电量范围。旧协议可识别休眠状态，超时和乱码不会生成新读数。旧协议测试用实机验证实现公布的帧布局和合成百分比，新协议测试使用公开的 60% 实机记录。设备缓存可能返回旧回复，单次成功无法证明无线端仍在线。[XVI-OLD 实机验证实现](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/src/drivers/gwolves/xvi-hid.ts) · [XVI-NEW 实机记录](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/7e11d7154f72/docs/PROTOCOL.md#3-response-frame)

## 国产鼠标第一批

[设备清单](devices.md#国产无线鼠标第一批)中的 27 个接收器标为「仅按资料」。逐个匹配 VID:PID 和唯一厂商集合，打开查询句柄时再用 HID 描述符校验报告 ID。共享接收器采用通用名称。Compx-OUT 与 XVI-NEW 复用上节的完整查询帧。

| 家族 | 使用页与报告 | 查询及响应 |
| --- | --- | --- |
| Compx-OUT | `FF02:0002`，Input / Output 都是 17 字节、ID `08` | 仅查询匹配的集合。按整帧白名单发送电量 `04` 和在线 `03`。新接收器使用目录显示名，CID/MID `10` 用于已登记的 ATK 型号。回包 `[6]` 为百分比，`[7]` 为充电，`[8..9]` 为大端毫伏值。 |
| XVI-NEW | `FFFF:0000`，Feature 65 字节、ID `00`。Lamzu Maya X 要求 `MI_02` | 只发送 `00 00 00 02 02 00 83` 加 58 字节零填充。接收 `00 A1/A2 00 02 02 00 83` 头，随后是充电状态和 0–100 的百分比。BitMouse 另有白名单，其中 `83` 是忘记配对指令。 |
| MCHOSE 反码 Feature | `FF01:0001`，按 HID caps 长度选择 21 字节 ID `11` 或 65 字节 ID `12`。A7 V2 Ultra 要求 `MI_02` | 只发送只读状态查询 `06`（线上为 `11 F9 FF×19` 或 `12 F9 FF×63`）。每次 GetFeature 前发送一次 SetFeature，最多四轮。回包需同 ID、`F9` 回显和有效的机身 VID；还原反码后 `[2..5]` 为 VID/型号，`[10]` 为连接位域，`[11]` 为百分比，`[12]` 为充电。过滤占位和回显，静默时沿用已有休眠逻辑。 |

六个新 VID 仅按目录启用，同 VID 的其他接口不会参与查询。若超时，Compx 可在原集合从在线状态查询回退为电量查询，MCHOSE 可在当前 Feature 报告上重试。各接收器始终使用同一个协议。配置、配对、固件更新和非电量 Feature 指令均未加入白名单。资料来源见[调研报告](../research/domestic-mice-webdriver.md)。

## 国产鼠标第二批

[设备清单](devices.md#国产无线鼠标第二批)中的六个接收器均标为「仅按资料」。每个接收器最多使用一个声明目标报告 ID 的集合。下标从报告 ID 所在的第 0 字节开始，报告缓冲按 Windows HID caps 分配。

| 家族 | 集合与报告 | 上报及读取 |
| --- | --- | --- |
| 1D57 Input 03 | `MI_02 / 000A:0000`，Input `03` | 被动接收 `03 <型号> 40 <状态> <数值>`，校验 PID 对应的型号字节。`01` 是电量，`03` 是充电状态，`02` 在 X3 Max 表示充电，其他接受该状态的型号为充满。R1/X6 的电量以 10% 为单位，充电未知。 |
| Incott Input 09 | `MI_03 / FF05:0001`，Input `09`，样本含 ID 8 字节 | 只读输入。`[1]` 为 0–100 时是电量，128–228 时减 128 并标充电。同一集合声明的 Feature 和 Output 均不访问。 |
| Rapoo Gen 2 Input 07 | `FF00:0002` 或 `FF00:000E`，仅允许其中唯一声明 Input `07` 的集合 | 被动接收，`[7]` 为状态（1 用电、2 充电，其他丢弃），`[8]` 为 0–100 的百分比。 |
| Rapoo Feature 2A | `FF0B:0104`，Feature `2A` | 仅 GET Feature `2A`，校验回包报告 ID 后取 `[9]` 为百分比，充电未知。 |

被动监听在等待 Input 时不设定时唤醒，手动刷新和轮询使用 350 ms 的被动窗口。诊断报告包含最近收到的脱敏帧。来源、集合预检和实机验证范围见[交付记录](../research/delivery-0.2.16.md)。

## 罗技 HID++ 1.0 与 2.0

本节适配属于「仅按资料」。几格通过离线回放检查读取流程，Windows 11 实机表现待确认。

### 2.4G 接收器

匹配 VID `046D`、唯一的 `FF00:0002` 长集合（输入和输出各 20 字节，报告 ID `11`）。同 PID、同物理容器的 `FF00:0001` 短集合使用 7 字节输入报告 `10` 接收短回复。已识别接收器的短集合可以只读，HID++ 1.0 寄存器查询另需 7 字节输出报告 `10`。已收录的 PID 如下。[Solaar 接收器目录](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base_usb.py)

| 家族 | PID |
| --- | --- |
| Bolt | `C548` |
| Unifying | `C52B/C532` |
| Nano | `C518/C51A/C51B/C521/C525/C526/C52E/C52F/C531/C534/C535/C537` |
| LIGHTSPEED | `C539/C53A/C53D/C53F/C541/C545/C547/C54D` |

逐槽查询 1–6。未知 PID 需要产品描述包含 `receiver`、`dongle` 或「接收器」，且通过同一描述符检查。首次只发 Root Ping，校验成功回复的版本和事务标记，或错误回复的地址和回显。格式不符由第三道安全门记住，点「重新检测」后可重试。短、长集合同时等待回复。HID++ 1.0 的 `8F` 错误回复中，`08` 为空槽，`04/09` 为明确不可达。HID++ 2.0 的 `FF` 错误回复中，`04/08/09` 分别表示硬件错误、设备忙和功能不支持。已识别设备无回复进入休眠，保留上次读数。接收器移除、`40/41` 断连或重连清除相应缓存。缓存仅在内存中保存，用接收器接口、槽位与 Unit ID 摘要区分，未公布 Unit ID 的设备沿用当前已发现的设备身份。已找到电量功能的设备下一轮只需一条电量查询。[HaloBattery HID++ 说明](https://github.com/HeyOkay/HaloBattery/blob/main/providers/logitech.py) · [Solaar 通知解析](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/notifications.py)

### HID++ 1.0 电量寄存器

只有 Ping 回显错误 `01`（旧设备不识别 HID++ 2.0 子命令）才通过短集合读 `10 <地址> 81 <寄存器> 00 00 00`。先读 `0D`，回复 P0 是百分比，P2 高四位 `3/5/9` 分别是未充电、充电中和已充满。没有有效读数时读 `07`，P0 档位 `7/5/3/1` 分别显示约 `100/50/20/5%`。P1 按 `21/22` 掩码识别正在充电或已充满，两者同时成立时优先判为充电。寄存器 `B5` 只允许 `81` 形式。资料中的 Bolt / Unifying 完整配对记录使用 `83`，0.2.4 不主动读配对表。首次启动遇到一直静默且没有历史读数的槽位时，等它回复后才显示。[HaloBattery 旧寄存器解析](https://github.com/HeyOkay/HaloBattery/blob/main/providers/logitech.py) · [Solaar 长寄存器定义](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py)

### USB 线缆

鼠标或键盘自身 PID 下须同时具有 `FF00:0001` 和 `FF00:0002` 集合。查询地址改为 `FF`，其余沿用功能发现与电量解析。线缆和接收器可同时读到时，按 `0003` 的 Unit ID 和型号字段生成的摘要合并，优先显示有效的线缆读数。同名设备仍各自保留。Unit ID 缺失时保留各连接，避免误合并两台设备。[HaloBattery USB 直连路由](https://github.com/HeyOkay/HaloBattery/blob/main/providers/logitech.py)

### 蓝牙 HID++

Windows 经典蓝牙 HID 路径含 `VID&0002046D`，HID over GATT 路径形如 `{00001812-…}_Dev_VID&02046D_PID&…`。路径可直接标识蓝牙路由，即使父节点没有公布地址。VID 为 `046D`，长集合为 `FF43:0202`，按 `FF` 读取。产品名优先使用 `0005`，否则采用 Windows 蓝牙名称。与 Windows 蓝牙属性按同一容器或地址合并，Windows 确认设备已断开时显示离线。其他情况下优先采用有效 HID++ 电量与充电状态。HID 集合暂时不可读或显示功能不支持时，可回退到 Windows 电量。切换来源会保留已知的 HID++ 名称与类型。诊断看 `罗技|H…` 的协议细节及 `蓝牙电量来源|B…` 的最终来源。源文件中的 MX Anywhere 3S 与 MX Keys 回放使用合成数据。[HID++ 2.0 草案](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf) · [Windows HID API 路径样本](https://github.com/libusb/hidapi/discussions/593) · [Linux HID++ 驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)

### Windows 蓝牙电量属性

适用于 Windows 已提供电量的蓝牙外设。用 SetupAPI 读取 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2`。只有类型为 BYTE、长度为 1 且在 0–100 的值可用。用设备连接属性记录显式断开，主蓝牙节点优先于服务节点。Windows 公布设备类别时，用它识别鼠标、键盘或耳机。属性暂缺时保留上次读数并显示休眠。此路线不打开设备报告通道，不主动向设备发包，诊断标为「蓝牙属性（由 Windows 提供）」。设置「显示蓝牙设备」默认开启。[BatteryHub 本机属性用法](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor/blob/main/src/drivers/generic.cpp) · [Microsoft SetupDiGetDevicePropertyW](https://learn.microsoft.com/windows/win32/api/setupapi/nf-setupapi-setupdigetdevicepropertyw) · [Microsoft 设备类别属性](https://learn.microsoft.com/windows-hardware/drivers/install/devpkey-devicedisplay-category)

### HID++ 耳机

适配 G533、G535、G633/G635、G933/G935、G733、G PRO / G PRO X 和部分 PRO X 2 变体。已收录 HID++ 耳机按地址 `FF`，匹配 `FF43:0202`，G535 `0AC4` 用 `000C:0001`。优先查询 `1004`，其次查 `1F20`，P0/P1 为大端毫伏值，P2 bit0 为已连接、bit1 为充电。毫伏转换为近似百分比并显示 `≈`。仅有非白名单旧命令的耳机固件暂不适配。[HaloBattery 耳机 PID 与路由](https://github.com/HeyOkay/HaloBattery/blob/main/providers/logitech.py) · [HeadsetControl](https://github.com/Sapd/HeadsetControl) · [Linux ADC 字段](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)

### HID++ 2.0 读取白名单

长报告格式为 `11 <地址> <功能索引> <函数<<4 | 软件ID> <参数...>`，补零至 20 字节。成功回显地址、索引和函数/软件 ID。错误回复以 `10 <地址> 8F` 或 `11 <地址> FF` 起头，随后回显请求索引、函数/软件 ID、错误码。每个动态索引只从同一设备的 Root 结果取得。功能索引落在旧寄存器命令或保留范围时拒绝使用。写寄存器 `80/82`、设置、配对控制、通知设置和固件更新均不允许。下表字段来自 [HaloBattery](https://github.com/HeyOkay/HaloBattery/blob/main/providers/logitech.py) 和 [Linux HID++ 驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)。

| 功能 | 只读函数 | 回复 |
| --- | --- | --- |
| Root | f0 查表，f1 Ping | 功能索引/标志/版本，或协议主/次版本及事务标记 |
| `0003` 设备信息 | f0 | P0 实体数、P1–P4 Unit ID、P6–P11 型号字段 |
| `0005` 名称和类型 | f0 长度、f1 分段名称、f2 类型 | 长度、名称片段或类型字节 |
| `1004` Unified Battery | f0 能力、f1 状态 | P0 百分比、P1 档位、P2 充电状态、P3 外部供电 |
| `1000` Battery Status | f0 状态 | P0 百分比（`0` 表示无有效放电百分比）、P1 下一档、P2 充电状态（`0` 放电、`1/2/4` 充电、`3` 充满、`>=5` 错误） |
| `1001` Battery Voltage | f0 电压 | P0/P1 大端毫伏，P2 含充电标志 |
| `1F20` ADC Measurement | f0 ADC | P0/P1 大端毫伏，P2 含连接和充电标志 |

依 HID++ 2.0 `0x1000`（BATTERY_STATUS）规范与 [Solaar `decipher_battery_status`](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)，部分设备（例如 Logitech MX Vertical `046D:C08A` 经 USB 线缆直连时）在充电或充满状态下回复 `P0=00`（如 `00 00 01`、`00 00 03`），表示当前无有效放电百分比而非 `0%`。几格遇到此类回复时会先尝试后续电压功能 `0x1001`；若无有效百分比，则保留充电/充满状态并置百分比为空（界面显示「充电中」或「已充满」，不误显示 `0%` 也不使用固定百分比）。已发现的 `1004` 或 `1000` 电量功能可主动推送事件 `00`。监听已知设备、槽位与索引匹配的事件，直接更新读数，无需主动开启通知或追加查询。回放以公开的 PRO X SUPERLIGHT 2 字节及合成鼠标、键盘、耳机样本覆盖上述解析。[Solaar 报文记录 #2961](https://github.com/pwr-Solaar/Solaar/issues/2961)

## Logitech Centurion 与 ASTRO A50 Gen 5

| 型号 | PID、集合、白名单 | 证据 | 验证 |
| --- | --- | --- | --- |
| G PRO X 2 LIGHTSPEED | `0AF7`，MI_03、`FFA0:0001`，前缀 `51` | 多方开源实现一致 | 未在实机验证 |
| G522 LIGHTSPEED | `0B18`，MI_03、`FFA0:0001`，前缀 `50 23` | 单一来源 | 未在实机验证 |
| G325 LIGHTSPEED | `0B21`，MI_03、`FFA0:0001`，仅固定 `50 23 0B 00 03 1A 00 03 00 07 0A` 查询 | 单一来源 | 未在实机验证 |
| ASTRO A50 Gen 5 | `0B1C`，MI_08、`FF32:0074`，仅 `02 0C 03 00 06 0C` | 单一来源 | 未在实机验证 |
| ASTRO A50 X | `0B0B`，集合与查询未核实，保持禁用 | 未核实 | 未在实机验证 |

PRO X 3 `046D:0B45` 保持关闭。

以上查询的报告长度均为 64 字节，尾部补零。Centurion 桥仅允许查询接收器 Root 的 `0003`、桥内 Root 的 `0104` 及其 `f0`。查询桥索引时跳过原样返回的请求，等待有效索引。桥内帧验证长度、内部索引及事务，跳过 ACK、断链通知和不匹配的内层帧。仅在 `0AF7` 完成桥接查表且收到 `0104` 索引明确为零时，允许备用查询 `51 08 00 03 1A 00 03 00 04 0A`。无回复时结束，读失败回显阶段。EQ、睡眠设置、任意桥转发等均不允许。

G325 跳过 ACK，用实机状态帧 `50 23 0B 00 03 10 00 06 00 07 0A` 的字节 12/13 读百分比和充电状态。Gen 5 根据命令 `06` 和回复长度读取百分比及底座充电标志，拒绝其他命令及数值越界。底座可能主动推送同格式遥测，无法从 HANDLE `00` 区分推送和请求的回复。[G325 实机记录](https://github.com/Sapd/HeadsetControl/issues/581) · [PRO X 2 实机载荷](https://github.com/HeyOkay/HaloBattery/issues/103) · [HeadsetControl Gen 5 实机样式](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_astro_a50.hpp)

## Razer 90B Feature

匹配 VID `1532`、已核对的无线接收器与有线充电模式 PID（全量对齐 OpenRazer `razermouse_driver.c` / `razerkbd_driver.c`、Razer Battery Report 与 HaloBattery，共 72 个鼠标 PID + 15 个键盘 PID，含 `00B3` HyperPolling 通用无线接收器；有意排除纯有线无电池的 `00CB` 与走 Windows 蓝牙电量的 `0095`）、指定 MI、厂商 usage page 与唯一的 91 字节 Feature 集合。打开设备时由 Windows HID 描述符验证 Report ID `00`。Win32 的首字节为 Report ID，另有 90 字节协议体。只允许 `07:80` 读电量以及支持充电型号的 `07:84` 读充电。请求使用型号规定的 TID（`1F` / `3F` / `9F` / `FF`）、单帧、数据长 2、零参数以及协议体字节 2–87 的 XOR。以 SetFeature 发送该查询，等待型号规定的稳定时间（通常 60 ms，Atheris `0062` 与 Orochi V2 `0094`/`00E0` 为 400 ms）后 GetFeature 收取同一报告。回复需匹配 Report ID、事务、类、命令、长度和校验。状态 `02` 取第二个参数，电量按 0–255 四舍五入到 0–100%，充电只识别 `00/01`。电量回复状态 `04` 显示离线。状态 `01` 表示雷蛇无线协议事务正忙（不是被其他程序占用句柄），等待 80 ms 后重试，最多重试 3 次；重试仍未就绪时按查询超时（`Timeout`）结束，不报告为「接收器被占用」。若回复的校验和与状态均合法但命令类/命令 ID 不属于本次电量或充电查询（例如 Synapse / 灯效软件向同一集合发送了 `0F:03` 覆盖特征缓冲），同样短暂等待后重发最多 3 次；连续被其他命令覆盖则报告 `Busy` 以提示通道正被其他软件占用。其它 PID、命令和 TID 无主动探测。

完整 87 个已启用 PID、接口号、TID 与充电能力见 [设备支持清单](devices.md#razer-90b-feature-与-blackshark-64b) 与 `src/Jige.Core/Adapters/RazerFeatureProtocol.cs`；Basilisk V4 HyperSpeed、Naga V3 Pro、BlackWidow V4 Pro 75%、BlackWidow V4 Low-profile HyperSpeed 全尺寸因路由或电量事务缺证据，继续保持禁用。

用于回放的 DeathAdder V4 Pro 实机回复记录到数据字段 `AB`（67%）。测试按规范补齐未公开的尾部字节和 CRC。[HaloBattery 实机记录](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md#razer-deathadder-v4-pro) · [Razer Battery Report 接口表](https://github.com/xzeldon/razer-battery-report/blob/master/crates/razer-hid/src/catalog.rs) · [设备清单](../research/device-catalog.md#2-雷蛇-razer)

## Razer PA 与 BlackShark 64B

| 型号 | 发送状态与证据 | 验证 |
| --- | --- | --- |
| BlackShark V2 Pro (2020) | 启用 `0528` 的 `FF00:0001` 64 字节 Feature 报告（Report ID `FF`）。纯只读查询，无需切换远程模式；充电线 `052E` 登记在路径开关中跳过。多方开源实现一致 | 未在实机验证 |
| BlackShark V2 HyperSpeed | 启用 `0565` 的 MI_03、`FF14:0001`。多方开源实现一致 | 未在实机验证 |
| BlackShark V2 Pro 2023（PC） | `0555` 及待确定 SKU 的 `0556`/`0557`/`0583` 默认禁用。PA 查询序列涉及 `E1` 切换远程模式。单一来源 | 未在实机验证 |
| Barracuda Pro、Barracuda X 2022 | 对照型号 `053A`/`0536` 的 PA 分支与 `0552` 的 Macronix/Airoha RACE 分支默认禁用（涉及远程/应用模式切换）。单一来源 | 未在实机验证 |
| BlackShark V3、BlackShark V3 Pro | 启用 `057A`/`0577` 的 MI_05、`FF14:0001` 纯只读 `21/2A` 查询（不发冷启动会话帧，且跳过高频心跳以避免干扰无线链路）。单一来源 | 未在实机验证 |
| Barracuda X Chroma、Barracuda V2 Pro | 协议家族与电量事务未核实，保持禁用 | 未在实机验证 |

BlackShark V2 Pro (2020) `0528` 使用 `FF00:0001` 的 64 字节 Feature 报告（Report ID `FF`），固定发送只读请求 `FF 0A 00 FD 04 12 F1 02 05 00×55`，等待约 100 ms 后读取同一报告。仅接受新鲜回复头 `FF 0F 05 FE 12 04 1F 08 05`（`[11]` 为电源状态：`01/02/03` 放电、`09` 充电中、`06` 已充满；`[12..13]` 大端毫伏，为 0 判离线；`[14]` 为设备上报的粗粒度百分比 `100/80/50/30/10`，标为估算；充电中 `09` 因固件跳数暂隐去具体百分比）；无新回复头 `FF 01 00 FE 12 04 1F 08 05` 判离线且拒绝使用其过期载荷。

BlackShark V2 HyperSpeed 使用 64 字节 Input/Output、Report ID `02`。白名单为本地域 `00` 的 `20` 链路查询，以及无线域 `80` 的 `21` 电量、`2A` 充电查询。仅发送零参数帧，核对回显序号、域、命令、方向、参数长度及回复 CRC。请求 CRC 含 Report ID，回复 CRC 从字节 1 起算。维护及设置命令禁止发送。PA/PA-Barracuda 只保留 `type 03` 的 `21/2A` 离线编解码，V3 系列只保留已公开的纯查询模板及基础款的回复解码。V3 Pro 的回复和不同固件变体有待核实。代码里没有远程模式切换或冷启会话路径。

V2 Pro (2020) 单元测试使用 OpenRazer #1280 与 HaloBattery 在 Windows 实机直接抓到的放电、低电、充电中、充满与关机过期帧；V3 单元测试使用 Synapse 实机抓包。V2 HyperSpeed 的测试回放实机的非电量回复以确认过滤，并按公开字段构造可接受回复验证 CRC 与事务。PA 测试回放 053A 的实机 34% 回复，并按公开字段构造 0555 应答。[V2 Pro 2020 实机记录](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark2020.py) · [BlackShark V3 抓包 #292](https://github.com/HeyOkay/HaloBattery/issues/292) · [V3 Pro 实机记录 #2838](https://github.com/openrazer/openrazer/issues/2838) · [V2 HyperSpeed 实机抓包 #2316](https://github.com/openrazer/openrazer/issues/2316) · [V2 HyperSpeed 协议与实测](https://github.com/bbesli/Synapse-for-Linux/blob/main/docs/PROTOCOL.md) · [Barracuda Pro 实机抓包 #10](https://github.com/HeyOkay/HaloBattery/issues/10) · [PA 查询字段](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py)

## 拓展品牌纯只读协议（ASUS ROG/TUF · SteelSeries · HyperX · Corsair · 分集合 Compx · 扩展外设）

本节新增路径全部标为「仅按资料」，遵守严格只读白名单（不发任何配置、DPI、EQ、灯效、配对或模式切换命令），并在 `tests/Jige.Core.Tests/BrandExpansionTests.cs` 中附带完整离线模拟帧与诊断回放单测：

| 品牌 / 家族 | 集合与传输 | 严格只读查询白名单 | 来源 |
| --- | --- | --- | --- |
| ASUS ROG / TUF 鼠标与 OMNI / Strix Go 2.4 | 鼠标：`0B05` `MI_00` 厂商集合 64/65B（Harpe II Ace 接收器 `1AD0` 走 `MI_02 / FF01` RID `03`）；OMNI `1ACE` 走 `MI_02`（`FF02` 配对表、`FF01` 鼠标、`FF00` 键盘）；Strix Go 2.4 `18D6/18D7` 走 `>=64B` Feature `FF` | 鼠标 `00/03 12 07`；OMNI 配对表 `01 A0`、键盘 `02 12 01`；Strix Go `FF 08 00 FD 04 12 F1 03 52 01`（电压四档映射并扣除充电抬升） | G-Helper / HaloBattery（MIT） |
| SteelSeries 赛睿 | 现代耳机与鼠标走 `1038` `FFC0:0001`（优先 `MI_03`）；经典 Arctis 与 Nova Pro 底座按型号匹配指定接口与厂商页 | 现代耳机 `00 B0`；Nova Pro `06 B0`（9 档估算）；Arctis 1/7X/7P `06 12`；Arctis 7 `06 14` / `06 18`；Arctis 9 `00 20`；Arctis Pro `41 AA` / `40 AA`；Aerox `00 D2`；Rival 3 `00 AA 01` | HeadsetControl / Linux / rivalcfg / alloyctl / HaloBattery（MIT） |
| HyperX 极度未知 | Cloud II HP（`FF90:0303`）、Kingston（`FF13:0001`）、Cloud Flight（`FF53/FF90:0303`）、Cloud Alpha（`FF43:0202`）、Cloud III / II Core（`FF13:0001`，按实际发送通道校验 Output 或 Feature `66`，回复走 Input `66`）、Cloud III S（优先 `MI_03`、核对 Output `0C`，可跨集合收回复） | `06 FF BB 02/03`；Kingston 62B `... BB 02/03`；Flight `21 FF 05`；Alpha `21 BB 03/0B/0C`；Cloud III `66 89/8A`；Cloud III S `0C 02 03 01 00 06/48`（严格 GET `01`，拒绝 SET `00`） | HeadsetControl / HyperHeadset / HyperXBatteryMonitor / HaloBattery（MIT） |
| Corsair 海盗船 | Void Elite `0A55`（`FFC5:0001`）；Bragi `FF42:0001`（65B RID `00` 或 64B RID `02`）；Void v2 / Virtuoso Max / HS80 Max（`MI_04`） | Void Elite `C9 64`；Bragi / Void v2 仅允许 `CMD_GET (02)` 查询电量 `0F`、充电 `10`、只读心跳 `12` 与固件 `13`；**严禁发送软件模式切换 `01 03 00 02`** | HeadsetControl / ckb-next / OpenLinkHub / HS80_Control / HaloBattery（MIT） |
| 分集合 Compx（Endgame Gear XM2we / DAREU A950） | 同接收器 `FF02:0002`（17B Feature `08`）发请求、`FF01:0000`（17B Input `09`）收回复；Endgame Gear 4K `3367:1970` 置入 `DisabledPaths` | 仅 `08 04 … 49` 查电量与 `08 03 … 4A` 查链路（XM2we） | okms/xm2we-linux / MousePower（MIT）/ HaloBattery（MIT） |
| Pulsar Sonix / EWEADN / AJAZZ / MCHOSE V3 / AULA / Keychron / Audeze | 均按精确 `VID:PID + 接口/使用页/报告长度` 路由，不做公版 VID 泛化 | Pulsar Sonix `00 00 08 81 01 … 8A 00`；EWEADN `00 30`；AJAZZ V2 MAX `00 10`；GearHub V5 `00 F7`；MCHOSE V3 `4D 01 01 00 00 09 00 00 08`；AULA `00 AA 10`；Keychron `B3 06` → `B4 06`；Audeze 仅单帧只读 `06 07 80 05 5A 03 00 D6 0C`（不发未核实初始化序列） | HaloBattery（MIT）/ aj179-linux（MIT）/ OpenMouse |

## 在线状态、休眠与离线判定规则

### 依据

[HaloBattery（MIT）`providers/pulsar.py`](https://github.com/HeyOkay/HaloBattery/blob/main/providers/pulsar.py) 记录过 Compx 方案无线鼠标在停止移动几秒后，接收器就不再回复电量或状态查询，这属于正常的省电静默而非设备断线。如果把"查询超时"直接当成"离线"，用户只要几秒不动鼠标，托盘图标就会反复变灰显示「已断开」，一动又亮回来，造成明显闪烁。游狼（XVI-OLD / XVI-NEW）、Finalmouse ULX、ATK BitMouse 也存在类似静默或休眠期无法区分关机的情况。

### 统一判定规则

1. **明确离线**：仅在以下两种情况下立即判定为 `Offline`（托盘图标变灰，悬停显示「已断开 · 上次 xx%」）：
   - 协议明确回复无线端未连接：例如 Razer 90B 状态字节为 `0x04`、Razer BlackShark V2 HyperSpeed `0x20` 链路为 `0`、Logitech HID++ 错误 `04/09`、推送 `0x41`（bit6=1）或 `0x40`、Windows 蓝牙明确报告断开；
   - USB 接收器被物理拔出（传输层返回 `Disconnected`，或非失败枚举中接收器已消失）。
2. **查询超时一律算休眠**：Compx、游狼（含 XVI-OLD 与 XVI-NEW）、ULX、BitMouse 等协议在查询超时且没有任何回复时（以及 XVI-OLD 休眠帧、ULX `0x24=0`、Compx `0x03` 在线标志为 `0`），均归为 `Asleep`（休眠）。Compx 在线标志原先按明确离线处理，但 ATK 鼠标的实机日志显示鼠标静止十几秒就会回 `0`，动一下又回 `1`，和关机无法区分，所以改按休眠处理：
   - 保留上次有效电量读数；
   - 托盘图标与面板**不变灰**，继续按上次读数对应正常/偏低/低电量色显示，且不把设备折叠进「离线设备」；
   - 托盘悬停提示写明「休眠中 · 上次 xx%」；休眠期间不触发低电量提醒。
3. **连续休眠 10 分钟转离线**：处于 `Asleep` 状态的设备若连续超过 `TrayModel.SleepToOfflineTimeout`（10 分钟）仍没有任何回复，则自动转为 `Offline` 并变灰；一旦进入 `Offline`，后续超时或休眠帧不再将其拉回 `Asleep`，只有收到明确的 `Online` 响应才会恢复在线。
4. **动一下就刷新**：存在休眠中的可补查外设（心跳设备、罗技 HID++ 或蓝牙设备）且当前不在游戏中时，几格通过 Win32 `GetLastInputInfo` 检查系统最后一次键鼠输入时间戳是否变化（不安装键盘/鼠标钩子、不读取也不记录任何输入内容）。一旦检测到新输入，立即对处于休眠状态的设备补发一次心跳（最多每 2 秒一次），恢复在线后立即刷新完整电量。

## Windows 传输约定

`list` 用零读写权限句柄取 HID 元数据。`read` 仅对匹配的厂商集合请求读写权限。登记在路径开关表 `DeviceCatalog.DisabledPaths` 中、证据不足或存在写入风险的路径，在打开设备句柄之前就会被跳过，并在诊断报告里标注「已识别，暂未启用：原因」。罗技推送和跨集合查询的辅助输入句柄使用只读权限。同一进程按 Windows 设备容器标识串行化物理接收器的访问，每个逻辑设备可另外指定设备槽位。标准输入集合只用于枚举元数据。串口使用短连接并在每次查询结束后释放。

重叠读写取消时调用 `CancelIoEx`。同步的 SetupAPI、打开设备和 HID Feature 调用在工作线程上执行，外层有期限。驱动若迟迟未返回，调用者可先结束等待，工作线程在驱动返回后释放句柄和接收器锁。雷蛇 90B 使用 HID Feature 通道。罗技和雷蛇在打开已匹配的集合后，用 HID 描述符核对查询所需的报告 ID。[HID 报告 API 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/sending-hid-reports) · [串口句柄文档](https://learn.microsoft.com/en-us/windows/win32/devio/communications-resource-handles)

## 本阶段验收

0.2.3 在 macOS 上通过托管构建（零警告）与离线单元测试，覆盖在线/休眠/离线三态语义与 10 分钟休眠转离线、`GetLastInputInfo` 唤醒节流、三级验证目录与文档一致性、路径开关打开句柄前跳过、诊断开头脱敏提示与反馈链接、测试版版本号格式与检查更新防降级、无变化跳过写 `state.json`、设置动画增量重绘、DPI/主题变化防抖与改名保护，以及雷蛇 90B 协议忙重试。所有 Windows 实机行为（Compx/游狼休眠唤醒、高 DPI 图标复用、DPI/主题防抖、剪贴板复制、测试版滚动发布工作流）均待 Windows 11 实机确认。完整设备清单与三级验证状态见 [设备支持清单](devices.md)。

# 设备读取协议

本仓库的全部协议均**未在实机验证**。下文的社区实机记录与本项目的离线测试是两类证据。C# 实现依据公开报文和协议说明独立编写，其中 openinzone 仅用于核对协议字段。

## Finalmouse UltralightX

| 项目 | 本阶段实现 | 验证证据 |
| --- | --- | --- |
| 接口 | `361D:0100`，`FF00:0001`，输入、输出报告各 64 字节，含报告 ID | 社区报告在该接收器和厂商使用页上查询成功。本项目未在实机验证 Windows 报告长度、Usage 和不同固件的集合布局 |
| 封帧 | Output `04`，后接 `02`、高位置位的命令、`00`、填零。Input `05`，后接长度、命令、载荷长度和数据 | 社区实机帧印证电压回复的报告 ID、长度和字段。本项目未在实机验证输入及输出 |
| `24` 链路 | 非零后继续。零表示无线链路断开，按“休眠或关机”显示并结束本轮读取。链路查询无回复时本轮也结束 | 社区记录了链路载荷 `01`。零值无法区分休眠和关机，是否唤醒设备未核实 |
| `26` 电量 | 载荷依次为百分比、低位 mV、高位 mV；接受 0–100。超时或无有效百分比时走 `05` | 有社区固件持续不回复 `26`。百分比回复仍待本项目实机确认 |
| `05` 电压 | 读取小端毫伏，按 XPanel 曲线分段线性插值并标记估算；`0000` 和 `FFFF` 无有效读数 | 社区实机帧 `05 04 05 02 64 10` 对应 4196 mV、78%。同一设备充电时记录载荷 `B4 10`，对应 4276 mV、88%。两值均有离线用例 |
| `25` 充电 | `00` 未充电，`01` 充电，其余为未知 | 社区在插拔充电线时记录了两个值。本项目未在实机验证 |

曲线数据点（mV、百分比）为 `(3000, 0.2)`、`(3620, 5)`、`(3660, 10)`、`(3740, 25)`、`(3880, 50)`、`(4170, 75)`、`(4380, 100)`。充电时端电压偏高，估算值可能随插拔线跳变。

允许的发包前缀固定为 `04 02 A4 00`、`04 02 A6 00`、`04 02 85 00`、`04 02 A5 00`。资料中的十进制 37 和 38 分别对应十六进制 `25` 和 `26`。

- HaloBattery，MIT。提供帧格式、曲线与社区实机记录。[协议实现说明](https://github.com/HeyOkay/HaloBattery/blob/main/providers/finalmouse.py) · [实机讨论 #284](https://github.com/HeyOkay/HaloBattery/pull/284) · [测试记录](https://github.com/HeyOkay/HaloBattery/blob/main/tests/test_device_pr284_finalmouse.py) · [许可证](https://github.com/HeyOkay/HaloBattery/blob/main/LICENSE)
- XPanel 为厂商公开网页，未发现网页源码的开源许可证。厂商单独发布的 USB 权限清单采用 MIT。[XPanel](https://xpanel.finalmouse.com/) · [USB 权限清单和许可证](https://github.com/teamfinalmouse/xpanel-linux-permissions)

## Sony INZONE H9 一代

接收器必须处于 PC 档。仅匹配 USB 硬件 ID 的 `VID_054C&PID_0E53` 和明确的 `MI_06`。H7 可能使用相同接收器，本阶段以 H9 一代接收器显示，尚未据型号回复区分耳机，H7 兼容性未核实。

默认选择 InZoneH9Tray 所用的 115200；也可选 openinzone 和 inzone-h9-mac 记录的 460800。两档均为 8N1、无流控、DTR/RTS 置位。接收器端实际采用哪个波特率，以及打开端口会否影响音频或休眠，均未在实机验证。

| 顺序 | 查询 | 完整白名单帧 |
| --- | --- | --- |
| 1 | 接收器链路状态 GET | `01 00 FC 08 96 C3 21 01 01 01 00 7D` |
| 2 | 耳机型号信息 GET | `01 00 FC 08 96 C3 41 02 01 01 00 9E` |
| 3 | 耳机电量 GET | `01 00 FC 08 96 C3 41 04 01 01 00 A0` |

前两帧沿用社区的初始化查询顺序，设备无回复时仍可进行电量查询。openinzone 记录的官方串口打开流程没有额外握手，也有记录指出该代链路查询不稳定。几格用有效电量回复确认在线。

事件头为 `04 FF`，第三字节加 3 是整帧长度；按流重组分段回复。校验值为从第 4 字节到校验值之前各字节的和取低 8 位。电量只接受 14 字节的 `14 04 10 01 00` 地址、事件、回复类型和事务号组合，校验保留位和 `96 C3` 键值。载荷为 0 或 1 的充电字节以及 0–100 的百分比。

社区文档的完整记录 `04 FF 0B 00 96 C3 14 04 10 01 00 00 32 B4` 对应 50%、未充电，并用于离线测试。充电状态的实机变化、两档波特率对同一接收器的表现、Hub 的占用错误码、拔插和取消后的句柄释放均未在实机验证。

- InZoneH9Tray，README 声明 MIT，仓库的 `LICENSE` 链接未找到对应文件。参考其 115200、三条 GET 报文和电量字段记录。[仓库](https://github.com/janit88/InZoneH9Tray) · [报文记录](https://github.com/janit88/InZoneH9Tray/blob/main/inzone_tray.py)
- openinzone，GPL-3.0。核对一代串口设置、事件类型、地址、长度及校验和。[协议文档](https://github.com/penguinwokrs/openinzone/blob/main/docs/PROTOCOL.md) · [许可证](https://github.com/penguinwokrs/openinzone/blob/main/LICENSE)
- inzone-h9-mac，MIT。提供 460800、完整的实机电量收发帧和 H9 充电字节的独立记录。[协议文档](https://github.com/jpcarranza94/inzone-h9-mac/blob/main/docs/protocol.md) · [许可证](https://github.com/jpcarranza94/inzone-h9-mac/blob/main/LICENSE)
- HaloBattery，MIT。提供社区适配记录，测试替身中的拼接回复属于合成样本。[H9 适配记录 #278](https://github.com/HeyOkay/HaloBattery/pull/278) · [许可证](https://github.com/HeyOkay/HaloBattery/blob/main/LICENSE)

## ATK 与游狼鼠标

以下协议全部**未在实机验证**。匹配表记录的是无线接收器，证据等级沿用[设备清单](../research/device-catalog.md)。同一物理接收器只允许一个匹配的家族和一个接口，多个候选集合一律跳过。发送前重新检查 VID/PID、厂商使用页和用法、该通道上的报告长度，以及包括补零区在内的完整白名单请求。

| 家族 | 白名单请求与传输 | 运行状态 | 证据等级 |
| --- | --- | --- | --- |
| Compx-OUT | `08 04 00×14 49`，17 字节 Output / Input；先挂读再写 | ATK `101A/1031/11D9/1278` 匹配 `FF02:0002` 时允许查询。游狼 `3854` 的精确 Usage 未核实，仍禁止查询 | `11D9`、`3854` 多方开源实现一致，其余单一来源 |
| Compx-FEAT | 同一 17 字节 Compx 请求，SetFeature 发，Input 收 | ATK `111A` 在 `FF04` 的精确 Usage 未核实，仍禁止查询 | 单一来源 |
| BitMouse | `08 7D 72 02 00 01 07 01 00×56` 查电量，`08 85 72 02 00 01 0F 01 00×56` 查充电，64 字节 Output / Input | ATK ZERO `1155` 匹配 `FF05:0001` 时允许查询。星闪接收器及分流未核实条目仅登记 | ZERO 无线路径多方开源实现一致 |
| XVI-OLD | `00 00 02 8F 01 00×60`，65 字节 SetFeature / GetFeature | 启用 `33E4:2717` 的 `MI_02 / FFFF:0000`；其他候选仅登记 | 单一来源 |
| XVI-NEW | `00 00 00 02 02 00 83 00×58`，65 字节 SetFeature / GetFeature | 启用 `33E4:3617` 的 `MI_02 / FFFF:0000`；`3517` 存在两代协议证据，始终跳过 | `3617` 单一来源，`3517` 自动分流未核实 |

Compx 回复核对 ID、命令、状态、地址、载荷长度及校验字节。校验为 `0x55` 减去前 16 字节之和后取低 8 位。只解析声明范围内的百分比及充电字节。游狼 Receiver RS 的公开回复尾字节与此校验式不一致，需复核完整原始帧后再考虑启用。离线测试使用 OpenMouse 的 X1 Pro Max 无线完整抓包核对 Compx 帧。Compx-FEAT 与其共享协议编解码，A9 Ultra 用同通道的真实版本回复核验校验和及电量解码对非电量命令的拒绝，独立电量抓包待补。[Compx-OUT 抓包](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/captures/atk-x1-pro-max/receiver.json) · [A9 Ultra 协议](https://github.com/cobanov/atkctl/blob/1755d8510663/docs/PROTOCOL.md#compx-a9-ultra-compatibility) · [A9 Ultra 版本回复](https://github.com/cobanov/atkctl/blob/1755d8510663/Tests/ATKKitTests/CompxTests.swift) · [Receiver RS 回复记录](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/7e11d7154f72/docs/PROTOCOL.md#6-the-compx-family)

BitMouse 请求的校验为 ID 和校验槽之后的字节和取低 8 位。回复按登记布局检查帧头、命令和长度，拒绝 `FF` 和主动推送 `73`。实测有未回显请求序列和目标的回复。带前置校验的布局单独校验，当前无自动启用的该布局接收器。BitMouse 的 `83` 会忘记配对，发送白名单只允许 `07` 和 `0F` 的两条完整帧。[BitMouse 解码](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/src/bitmouse/index.ts) · [BitMouse 实机回复](https://github.com/cobanov/atkctl/blob/1755d8510663/Tests/ATKKitTests/ProtocolTests.swift)

XVI 的 65 字节含 Windows Report ID 槽，SetFeature 后等 100 ms，再做有限次 GetFeature。两代分别校验自己的回复头和电量范围。旧协议可识别休眠状态，超时和乱码不会生成新读数。旧协议测试用实机验证实现公布的帧布局和合成百分比，新协议测试使用公开的 60% 实机记录。设备缓存可能返回旧回复，单次成功无法证明无线端仍在线。[XVI-OLD 实机验证实现](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/src/drivers/gwolves/xvi-hid.ts) · [XVI-NEW 实机记录](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/7e11d7154f72/docs/PROTOCOL.md#3-response-frame)

## Windows 传输约定

`list` 用零读写权限句柄取 HID 元数据。`read` 仅对匹配的厂商集合请求读写权限。同一进程按 Windows 设备容器标识串行化物理接收器的访问，每个逻辑设备可另外指定设备槽位。标准输入集合只用于枚举元数据。串口使用短连接并在每次查询结束后释放。

重叠读写取消时调用 `CancelIoEx`。同步的 SetupAPI、打开设备和 HID Feature 调用在工作线程上执行，外层有期限。驱动若迟迟未返回，调用者可先结束等待，工作线程在驱动返回后释放句柄和接收器锁。HID Output/Input、Feature 和串口通道均待 Windows 实机验证。[HID 报告 API 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/sending-hid-reports) · [串口句柄文档](https://learn.microsoft.com/en-us/windows/win32/devio/communications-resource-handles)

## 本阶段验收

macOS arm64 使用 .NET SDK 10.0.404 构建通过，0 个警告、0 个错误。48 项测试覆盖公开报文、完整请求白名单、模拟设备的查询和失败路径、诊断脱敏与报告回放。探针已检查帮助、`diag` 参数和 macOS 平台提示，win-x64 托管编译通过，本机 arm64 NativeAOT 程序可启动。Windows NativeAOT 无法在 macOS 上跨系统发布。Windows 11 实机上的枚举、驱动访问、设备休眠和唤醒、官方驱动共存及 Windows 发布产物仍待验收。

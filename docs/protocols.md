# 设备读取协议

几格已在 Windows 11 实机读到 Sony INZONE H9 一代和游狼 HTX Ultra 8K 的电量。其余条目按公开协议、社区样本和离线回放适配，实机状态见各节。C# 实现依据公开协议与报文独立编写，其中 openinzone 用于核对字段。

## Finalmouse UltralightX

| 项目 | 本阶段实现 | 验证证据 |
| --- | --- | --- |
| 接口 | 厂商 `361D`（已知 `361D:0100`，其余 PID 走通用家族识别），唯一 `FF00:0001`，输入、输出报告各 64 字节，含报告 ID | 社区报告在该厂商和使用页上查询成功；未收录 PID 需通过三道安全门（唯一特征、`24` 链路回复校验、不符则持久化拦截）。本项目未在实机验证 Windows 报告长度、Usage 和不同固件的集合布局 |
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

游狼 HTX Ultra 8K 已由几格在 Windows 11 实机读到电量，其他接收器待验证。匹配表记录的是无线接收器，证据等级沿用[设备清单](../research/device-catalog.md)。同一物理接收器只允许一个匹配的家族和一个接口，多个候选集合或跨家族冲突一律由安全门 ① 跳过。对于未收录的 `373B`（ATK）与 `361D`（Finalmouse）接收器，支持按唯一接口特征通用识别并施加三道安全门：① 特征必须唯一对应单个协议家族；② 首条只发无害状态查询，回复格式校验通过后才继续；③ 格式不符立即停止并按 `VID:PID:接口特征` 持久化拦截，重启有效，点击「重新检测」清除。发送前重新检查 VID/PID、厂商使用页和用法、该通道上的报告长度，以及包括补零区在内的完整白名单请求。

共享接收器（如 `373B:11D9`、`101A`、`1278` 及通用识别的 ATK 接收器）不再按 PID 写死型号，默认显示「ATK 鼠标」，仅在协议通过 `0x10`（CID/MID）读出已登记型号（例如 OpenMouse 记录的 F1 Ultimate 2.0 `1/8`、A9 Ultimate `1/3`）时更新名称。Compx-OUT 查询期间仅对主集合发送输出报告，同时可对同一接收器的其他厂商输入集合（如 `FF05`、`FF06`）挂起只读监听以接收跨集合回复，并在诊断报告中输出每个集合的收发统计。

| 家族 | 白名单请求与传输 | 运行状态 | 证据等级 |
| --- | --- | --- | --- |
| Compx-OUT | 电量 `08 04 00×14 49`、在线心跳 `08 03 00×14 4A`、型号 CID/MID `08 10 00×14 3D`；17 字节 Output / Input，先挂读再写 | ATK `101A/1031/11D9/1278` 与满足三道安全门的通用 `373B` `FF02:0002` 允许查询；游狼 `3854` 的精确 Usage 未核实，仍禁止查询 | `04` 多方开源一致；`03` 来自 [atk-control](https://github.com/NasirA1991/atk-control/blob/main/atk_control/protocol.py) 与 [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/atk/hid.ts)；`10` 来自 OpenMouse、atk-control 与 [atkctl](https://github.com/cobanov/atkctl/blob/1755d8510663/docs/PROTOCOL.md) |
| Compx-FEAT | 同一 17 字节 Compx 请求，SetFeature 发，Input 收 | ATK `111A` 在 `FF04` 的精确 Usage 未核实，仍禁止查询 | 单一来源 |
| BitMouse | `08 7D 72 02 00 01 07 01 00×56` 查电量，`08 85 72 02 00 01 0F 01 00×56` 查充电，64 字节 Output / Input | ATK ZERO `1155` 与满足三道安全门的通用 `373B` `FF05:0001` 允许查询；星闪 `1216`（带前置校验布局）及未核实条目保持禁用 | ZERO 无线路径多方开源实现一致 |
| XVI-OLD | `00 00 02 8F 01 00×60`，65 字节 SetFeature / GetFeature | 从游狼官方网页驱动 [`https://mouse.xyz/Config/env-models.json`](https://mouse.xyz/Config/env-models.json)（`AppVer 1.0.0.51`，`XVI=1`、`IsNewProtocol=0`）导入启用：`2717`（HTX Mini 8K）、`3717`（Fenrir Max 8K）、`3917`（VUK）、`5317`（HTS Ultra）、`5403/5407/5417`（HTS Plus）、`5703/5707/5717`（HTX）、`5803/5807/5817`（HSK Pro）、`5903/5907/5917`（HSK Plus）、`7203`（HSK Lite）、`7713/7717`（HTR / HTR Pro）、`7913/7917`（HT-S2 / Pro） | 官方网页驱动型号表 + 单一开源实现 |
| XVI-NEW | `00 00 00 02 02 00 83 00×58`，65 字节 SetFeature / GetFeature | 从 `mouse.xyz/Config/env-models.json`（`XVI=1`、`IsNewProtocol=1`）导入启用：`3617`（Fenrir Pro 8K）、`3817`（HTM Plus）、`5617`（HTX Ultra 8K）、`6817`（HSK Pro 2.0 8K）；`3517` 存在两代协议冲突记录，保持禁用 | `5617` 已实机验证，其余来自官方网页驱动型号表 |

Compx 回复核对 ID、命令、状态、地址、载荷长度及校验字节。校验为 `0x55` 减去前 16 字节之和后取低 8 位。只解析声明范围内的百分比及充电字节。游狼 Receiver RS 的公开回复尾字节与此校验式不一致，需复核完整原始帧后再考虑启用。离线测试使用 OpenMouse 的 X1 Pro Max 无线完整抓包核对 Compx 帧。Compx-FEAT 与其共享协议编解码，A9 Ultra 用同通道的真实版本回复核验校验和及电量解码对非电量命令的拒绝，独立电量抓包待补。[Compx-OUT 抓包](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/captures/atk-x1-pro-max/receiver.json) · [A9 Ultra 协议](https://github.com/cobanov/atkctl/blob/1755d8510663/docs/PROTOCOL.md#compx-a9-ultra-compatibility) · [A9 Ultra 版本回复](https://github.com/cobanov/atkctl/blob/1755d8510663/Tests/ATKKitTests/CompxTests.swift) · [Receiver RS 回复记录](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/7e11d7154f72/docs/PROTOCOL.md#6-the-compx-family)

BitMouse 请求的校验为 ID 和校验槽之后的字节和取低 8 位。回复按登记布局检查帧头、命令和长度，拒绝 `FF` 和主动推送 `73`。实测有未回显请求序列和目标的回复。带前置校验的布局单独校验，当前无自动启用的该布局接收器。BitMouse 的 `83` 会忘记配对，发送白名单只允许 `07` 和 `0F` 的两条完整帧。[BitMouse 解码](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/src/bitmouse/index.ts) · [BitMouse 实机回复](https://github.com/cobanov/atkctl/blob/1755d8510663/Tests/ATKKitTests/ProtocolTests.swift)

XVI 的 65 字节含 Windows Report ID 槽，SetFeature 后等 100 ms，再做有限次 GetFeature。两代分别校验自己的回复头和电量范围。旧协议可识别休眠状态，超时和乱码不会生成新读数。旧协议测试用实机验证实现公布的帧布局和合成百分比，新协议测试使用公开的 60% 实机记录。设备缓存可能返回旧回复，单次成功无法证明无线端仍在线。[XVI-OLD 实机验证实现](https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d44/src/drivers/gwolves/xvi-hid.ts) · [XVI-NEW 实机记录](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/7e11d7154f72/docs/PROTOCOL.md#3-response-frame)

## Logitech HID++ 2.0

已知接收器均使用 VID `046D`。Lightspeed 包括 `C539/C53A/C53D/C53F/C541/C545/C547/C54D`，Bolt 为 `C548`，Unifying 为 `C52B/C532`。读取要求唯一的 `FF00:0002` 长报告集合，Input/Output 均为 20 字节、ID `11`。使用多设备读取接口时，按同一物理接收器串行查询槽 1–6，每个已确认的槽输出一条记录。

| 只读请求 | 发送约束与结果 |
| --- | --- |
| IRoot Ping `f1` | `11 槽 00 1A 00 00 标记` 后补零。匹配槽、事务及回显标记，要求主版本至少为 2 |
| IRoot GetFeature `f0` | 仅查 `0003`、`0005`、`1004`、`1000`、`1001`。各查询使用不同 software ID，迟到的查表回复无法用于其他 feature。动态索引排除 HID++ 1.0 寄存器命令范围。索引在当前物理接收器会话的对应槽内缓存，关闭句柄即失效 |
| `0003/f0` | 只读设备身份。Unit ID 与型号 ID 组合做本机散列。报告遮蔽 Unit ID。缺少有效 Unit ID 时按名称和类型区分，无法区分返回相同元数据的两台设备。[字段定义](https://openlogi.org/hidpp/features/x0003-device-information) |
| `0005/f0–2` | 读 UTF-8 名称长度、名称片段和设备种类，按类型映射键盘、鼠标。无有效名称时显示槽编号 |
| `1004/f0–1` | 先读能力。仅能力字节 bit1 确认支持 SoC 才读取百分比，接受 0–100。bit0 未标为可充电时保持充电未知 |
| `1000/f0` | 接受 1–100，并排除明确的电池无效、温度错误状态 |
| `1001/f0` | 大端毫伏，3.5–4.2 V 按单节锂电工作区间线性映射到 0–100%，标为电压估算并保留 mV。超出区间只报告 mV，外部供电时，低三位状态 1 标记已充满、3–7 保持未知 |
| HID++ 1.0 `83 B5` | 仅 Bolt/Unifying 在具备唯一短报告集合（`FF00:0001`、7 字节、Output ID `10`）且槽无应答或明确不可达时使用。向接收器地址 `FF` 读指定槽的配对表，长回复需回显选择字节 |
| HID++ 1.0 `0x41 / 0x40`（只读被动监听） | 仅挂起 Input 读取接收器主动推送的设备连接/断开事件，不发送寄存器写入命令；`0x41` 载荷字节 4 的 bit6（`0x40`）置位判定为离线，清零判定为在线，`0x40` 解配判定为离线。来源：[Solaar notifications.py](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/notifications.py)、[Linux hid-logitech-hidpp.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c) |

明确不可达或确认已配对但 Ping 无回复的槽显示“离线”。明确空槽和无法确认配对的静默槽不生成设备。未知 `046D` PID 只有产品字符串表明是接收器且匹配长报告描述符时才对槽 1 发送一次 Ping，应答有效后最多再查一次 Root `1004`，输出探测结果。地址 `FF` 仅用于允许的配对表读。寄存器写 `80/82`、配对控制、通知开关、DFU 及配置均不在白名单中。

| 清单型号 | 路由与证据 | 验证 |
| --- | --- | --- |
| PRO X SUPERLIGHT 2、G309 LIGHTSPEED、PRO X2 SUPERSTRIKE | 已知接收器。多方开源实现一致 | 未在实机验证 |
| PRO X SUPERLIGHT 2 DEX、G515 LIGHTSPEED TKL | 已知接收器。单一来源 | 未在实机验证 |
| PRO 2 LIGHTSPEED、PRO X TKL LIGHTSPEED、G915 X LIGHTSPEED、G915 X LIGHTSPEED TKL | 原配接收器或电量事务未核实。已知接收器下由槽位应答决定是否读取 | 未在实机验证 |

离线测试包含 PRO X SUPERLIGHT 2 的 Root 查表和 94% 电量实机字节，以及模拟的名称、多槽、重开换索引、配对表、后备电量和迟到回复。[Solaar 实机日志 #2961](https://github.com/pwr-Solaar/Solaar/issues/2961) · [HID++ 2.0 草案](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf) · [设备清单](../research/device-catalog.md#1-罗技-logitech-g)

## Logitech Centurion 与 ASTRO A50 Gen 5

| 型号 | PID、集合、白名单 | 证据 | 验证 |
| --- | --- | --- | --- |
| G PRO X 2 LIGHTSPEED | `0AF7`，MI_03、`FFA0:0001`，前缀 `51` | 多方开源实现一致 | 未在实机验证 |
| G522 LIGHTSPEED、G PRO X 3 LIGHTSPEED | `0B18/0B45`，MI_03、`FFA0:0001`，前缀 `50 23` | 单一来源 | 未在实机验证 |
| G325 LIGHTSPEED | `0B21`，MI_03、`FFA0:0001`，仅固定 `50 23 0B 00 03 1A 00 03 00 07 0A` 查询 | 单一来源 | 未在实机验证 |
| ASTRO A50 Gen 5 | `0B1C`，MI_08、`FF32:0074`，仅 `02 0C 03 00 06 0C` | 单一来源 | 未在实机验证 |
| ASTRO A50 X | `0B0B`，集合与查询未核实，保持禁用 | 未核实 | 未在实机验证 |

以上查询的报告长度均为 64 字节，尾部补零。Centurion 桥仅允许查询接收器 Root 的 `0003`、桥内 Root 的 `0104` 及其 `f0`。桥内帧验证长度、内部索引及事务，跳过 ACK、断链通知和不匹配的内层帧。仅在 `0AF7` 完成桥接查表且收到 `0104` 索引明确为零时，允许备用查询 `51 08 00 03 1A 00 03 00 04 0A`。无回复时结束，读失败回显阶段。EQ、睡眠设置、任意桥转发等均不允许。

G325 跳过 ACK，用实机状态帧 `50 23 0B 00 03 10 00 06 00 07 0A` 的字节 12/13 读百分比和充电状态。Gen 5 根据命令 `06` 和回复长度读取百分比及底座充电标志，拒绝其他命令及数值越界。底座可能主动推送同格式遥测，无法从 HANDLE `00` 区分推送和请求的回复。[G325 实机记录](https://github.com/Sapd/HeadsetControl/issues/581) · [PRO X 2 实机载荷](https://github.com/HeyOkay/HaloBattery/issues/103) · [HeadsetControl Gen 5 实机样式](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_astro_a50.hpp)

## Razer 90B Feature

仅匹配 VID `1532`、下表无线 PID 与 MI、厂商 usage page、唯一的 91 字节 Feature 集合。打开设备时由 Windows HID 描述符验证 Report ID `00`。Win32 的首字节为 Report ID，另有 90 字节协议体。只允许 `07:80` 读电量以及表中支持的 `07:84` 读充电。请求使用型号规定的 TID、单帧、数据长 2、零参数以及协议体字节 2–87 的 XOR。以 SetFeature 发送该查询，再等待 60 ms 后 GetFeature 收取同一报告。回复需匹配 Report ID、事务、类、命令、长度和校验。状态 `02` 取第二个参数，电量按 0–255 四舍五入到 0–100%，充电只识别 `00/01`。电量回复状态 `04` 显示离线。状态 `01` 表示雷蛇无线协议事务正忙（不是被其他程序占用句柄），等待 80 ms 后重试，最多重试 3 次；重试仍未就绪时按查询超时（`Timeout`）结束，不报告为「接收器被占用」。其它 PID、命令和 TID 无主动探测。

| 型号 | 接收器 PID / MI / TID | 充电 | 证据 | 验证 |
| --- | --- | --- | --- | --- |
| Viper Mini Signature Edition | `009F / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| Cobra Pro | `00B0 / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| Viper V3 HyperSpeed | `00B8 / 00 / 1F` | 未查询 | 多方开源实现一致 | 未在实机验证 |
| Basilisk V3 X HyperSpeed | `00B9 / 00 / 1F` | 未查询 | 单一来源 | 未在实机验证 |
| Viper V3 Pro | `00C1 / 00 / 1F` | 查询 | 多方开源实现一致 | 未在实机验证 |
| DeathAdder V3 HyperSpeed | `00C5 / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| Basilisk V3 Pro 35K | `00CD / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| Basilisk V3 Pro 35K Phantom Green | `00D7 / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| DeathAdder V4 Pro | `00BF / 00 / 1F` | 查询 | 多方开源实现一致 | 未在实机验证 |
| Cobra HyperSpeed | `00DB / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| Basilisk Mobile | `00D4 / 00 / 1F` | 未查询 | 单一来源 | 未在实机验证 |
| Viper V3 Pro SE | `00DF / 00 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| Viper V4 Pro | `00E6 / 03 / 1F` | 查询 | 多方开源实现一致 | 未在实机验证 |
| Basilisk V4 Pro | `00E3 / 03 / 1F` | 查询 | 单一来源 | 未在实机验证 |
| BlackWidow V4 Mini HyperSpeed | `02BA / 03 / 9F` | 查询 | 单一来源 | 未在实机验证 |
| BlackWidow V4 Tenkeyless HyperSpeed | `02D5 / 02 / 9F` | 查询 | 单一来源 | 未在实机验证 |
| BlackWidow V4 Low-profile Tenkeyless HyperSpeed | `02D2 / 02 / 9F` | 查询 | 单一来源 | 未在实机验证 |
| Basilisk V4 HyperSpeed、Naga V3 Pro、BlackWidow V4 Pro 75%、BlackWidow V4 Low-profile HyperSpeed 全尺寸 | 路由或电量事务缺证据，保持禁用 | — | 未核实 | 未在实机验证 |

用于回放的 DeathAdder V4 Pro 实机回复记录到数据字段 `AB`（67%）。测试按规范补齐未公开的尾部字节和 CRC。[HaloBattery 实机记录](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md#razer-deathadder-v4-pro) · [Razer Battery Report 接口表](https://github.com/xzeldon/razer-battery-report/blob/master/crates/razer-hid/src/catalog.rs) · [设备清单](../research/device-catalog.md#2-雷蛇-razer)

## Razer PA 与 BlackShark 64B

| 型号 | 发送状态与证据 | 验证 |
| --- | --- | --- |
| BlackShark V2 HyperSpeed | 启用 `0565` 的 MI_03、`FF14:0001`。多方开源实现一致 | 未在实机验证 |
| BlackShark V2 Pro 2023（PC） | `0555` 及待确定 SKU 的 `0556` 默认禁用。PA 查询序列涉及 `E1` 切换远程模式。单一来源 | 未在实机验证 |
| Barracuda Pro、Barracuda X 2022 | 对照型号 `053A/0536` 的 PA 分支默认禁用，原因同上。单一来源 | 未在实机验证 |
| BlackShark V3、BlackShark V3 Pro | `057A/0577` 保持禁用。冷启动会话未证实可只读建立，V3 曾在高频查询时重置无线链路。单一来源 | 未在实机验证 |
| Barracuda X Chroma、Barracuda V2 Pro | 协议家族与电量事务未核实，保持禁用 | 未在实机验证 |

BlackShark V2 HyperSpeed 使用 64 字节 Input/Output、Report ID `02`。白名单为本地域 `00` 的 `20` 链路查询，以及无线域 `80` 的 `21` 电量、`2A` 充电查询。仅发送零参数帧，核对回显序号、域、命令、方向、参数长度及回复 CRC。请求 CRC 含 Report ID，回复 CRC 从字节 1 起算。维护及设置命令禁止发送。PA/PA-Barracuda 只保留 `type 03` 的 `21/2A` 离线编解码，V3 系列只保留已公开的纯查询模板及基础款的回复解码。V3 Pro 的回复和不同固件变体有待核实。代码里没有远程模式切换或冷启会话路径。

V3 单元测试使用 Synapse 实机抓包。V2 HyperSpeed 的测试回放实机的非电量回复以确认过滤，并按公开字段构造可接受回复验证 CRC 与事务。PA 测试回放 053A 的实机 34% 回复，并按公开字段构造 0555 应答。[BlackShark V3 抓包 #292](https://github.com/HeyOkay/HaloBattery/issues/292) · [V3 Pro 实机记录 #2838](https://github.com/openrazer/openrazer/issues/2838) · [V2 HyperSpeed 实机抓包 #2316](https://github.com/openrazer/openrazer/issues/2316) · [V2 HyperSpeed 协议与实测](https://github.com/bbesli/Synapse-for-Linux/blob/main/docs/PROTOCOL.md) · [Barracuda Pro 实机抓包 #10](https://github.com/HeyOkay/HaloBattery/issues/10) · [PA 查询字段](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py)

## 在线状态、休眠与离线判定规则

### 依据

[HaloBattery（MIT）`providers/pulsar.py`](https://github.com/HeyOkay/HaloBattery/blob/main/providers/pulsar.py) 记录过 Compx 方案无线鼠标在停止移动几秒后，接收器就不再回复电量或状态查询，这属于正常的省电静默而非设备断线。如果把"查询超时"直接当成"离线"，用户只要几秒不动鼠标，托盘图标就会反复变灰显示「已断开」，一动又亮回来，造成明显闪烁。游狼（XVI-OLD / XVI-NEW）、Finalmouse ULX、ATK BitMouse 也存在类似静默或休眠期无法区分关机的情况。

### 统一判定规则

1. **明确离线**：仅在以下两种情况下立即判定为 `Offline`（托盘图标变灰，悬停显示「已断开 · 上次 xx%」）：
   - 协议明确回复无线端未连接：例如 Compx `0x03` 在线标志为 `0`、Razer 90B 状态字节为 `0x04`、Razer BlackShark V2 HyperSpeed `0x20` 链路为 `0`、Logitech HID++ 推送 `0x41`（bit6=1）或 `0x40`；
   - USB 接收器被物理拔出（传输层返回 `Disconnected`，或非失败枚举中接收器已消失）。
2. **查询超时一律算休眠**：Compx、游狼（含 XVI-OLD 与 XVI-NEW）、ULX、BitMouse 等协议在查询超时且没有任何回复时（以及 XVI-OLD 休眠帧、ULX `0x24=0`），均归为 `Asleep`（休眠）：
   - 保留上次有效电量读数；
   - 托盘图标与面板**不变灰**，继续按上次读数对应正常/偏低/低电量色显示，且不把设备折叠进「离线设备」；
   - 托盘悬停提示写明「休眠中 · 上次 xx%」；休眠期间不触发低电量提醒。
3. **连续休眠 10 分钟转离线**：处于 `Asleep` 状态的设备若连续超过 `TrayModel.SleepToOfflineTimeout`（10 分钟）仍没有任何回复，则自动转为 `Offline` 并变灰；一旦进入 `Offline`，后续超时或休眠帧不再将其拉回 `Asleep`，只有收到明确的 `Online` 响应才会恢复在线。
4. **动一下就刷新**：存在休眠中的心跳设备且当前不在游戏中时，几格通过 Win32 `GetLastInputInfo` 检查系统最后一次键鼠输入时间戳是否变化（不安装键盘/鼠标钩子、不读取也不记录任何输入内容）。一旦检测到新输入，立即对处于休眠状态的设备补发一次心跳（最多每 2 秒一次），恢复在线后立即刷新完整电量。

## Windows 传输约定

`list` 用零读写权限句柄取 HID 元数据。`read` 仅对匹配的厂商集合请求读写权限。登记在路径开关表 `DeviceCatalog.DisabledPaths` 中、证据不足或存在写入风险的路径，在打开设备句柄之前就会被跳过，并在诊断报告里标注「已识别，暂未启用：原因」。同一进程按 Windows 设备容器标识串行化物理接收器的访问，每个逻辑设备可另外指定设备槽位。标准输入集合只用于枚举元数据。串口使用短连接并在每次查询结束后释放。

重叠读写取消时调用 `CancelIoEx`。同步的 SetupAPI、打开设备和 HID Feature 调用在工作线程上执行，外层有期限。驱动若迟迟未返回，调用者可先结束等待，工作线程在驱动返回后释放句柄和接收器锁。雷蛇 90B 使用 HID Feature 通道。罗技和雷蛇在打开已匹配的集合后，用 HID 描述符核对查询所需的报告 ID。[HID 报告 API 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/sending-hid-reports) · [串口句柄文档](https://learn.microsoft.com/en-us/windows/win32/devio/communications-resource-handles)

## 本阶段验收

0.2.3 在 macOS 上通过托管构建（零警告）与离线单元测试，覆盖在线/休眠/离线三态语义与 10 分钟休眠转离线、`GetLastInputInfo` 唤醒节流、三级验证目录与文档一致性、路径开关打开句柄前跳过、诊断开头脱敏提示与反馈链接、测试版版本号格式与检查更新防降级、无变化跳过写 `state.json`、设置动画增量重绘、DPI/主题变化防抖与改名保护，以及雷蛇 90B 协议忙重试。所有 Windows 实机行为（Compx/游狼休眠唤醒、高 DPI 图标复用、DPI/主题防抖、剪贴板复制、测试版滚动发布工作流）均待 Windows 11 实机确认。完整设备清单与三级验证状态见 [设备支持清单](devices.md)。

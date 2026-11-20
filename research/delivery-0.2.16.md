# 0.2.16 交付记录｜国产鼠标第二批

本版新增六个 2.4 GHz 接收器，全部标为「仅按资料」。五个接收器复用托盘的推送监听，旧款雷柏 VT9 Pro 只读取 Feature `2A`。验收依据见[验收标准](acceptance-0.2.16.md)，型号上下文见[网页驱动调研 §3.3–3.5](domestic-mice-webdriver.md)和[型号索引](domestic-mice-models.json)。

## 实现

- 按 VID:PID 预筛，再检查 MI（有确定记录时）、使用页、用法、caps 长度和报告 ID。同一接收器只允许一个匹配的集合。Windows 用零访问句柄检查候选 HID 描述符，只有通过检查的集合才打开报告通道。开报告句柄时再核验描述符。1D57 Input 缓冲按 Windows caps 分配，HaloBattery 的 100 字节是应用读取缓冲上限。
- 推送以只读句柄等待 Input，空闲时阻塞于系统读取。轮询和手动刷新用可注入的 350 ms 读取窗口。沉默时显示「等待鼠标上报电量」，保留上次电量和已经细化的设备名称，首次等待显示 `--`。收到了仅表示充电的帧时，充电状态可先于新电量显示。接收器确实拔出后，托盘转为离线。
- 完整轮询先取消并等待旧推送监听收尾，再进行短窗口读取并恢复监听。诊断导出可以与监听重叠，使用独立、共享读取权限的 HID 句柄，各句柄读取各自的 Windows 输入队列。导出还附每个当前接收器最近 12 帧，按诊断规则只保留已识别帧的必要字段和无效帧的报告 ID、长度。历史帧与此次导出的实时读取分开列出。[Windows HID 输入缓冲](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getnuminputbuffers)
- Incott 实测的 Input、Feature 与 Output 声明共用 `MI_03 / FF05:0001`。该集合只以 Input 通道打开并预检 Input `09`，Feature 和 Output 均不访问。雷柏 Gen 2 的两个候选集合由目标报告 ID 消歧，多个集合都声明 `07` 时跳过。
- 已同步设备目录、协议、致谢、CHANGELOG、项目和安装器版本。C# 代码和测试帧独立编写，OpenMouse 仅用于核对协议和硬件记录。

## PID 证据与验证状态

M 表示多来源，S 表示单一来源。来源中的实机记录属于上游，几格本轮验证状态均为「仅按资料」。

| 接收器 | 路径 | 证据 | 依据与边界 |
| --- | --- | --- | --- |
| `1D57:FA60` | `MI_02 / 000A:0000` Input `03` | M | [HaloBattery inphic.py][halo-inphic] 记录 X11 `55`、X3 Max `4E`、R1/X6 `10` 的帧和状态。[mouse-battery-tray devices.py][tray] 补充已登记的 `85 / 4D / BE / 07` 等型号字节。[OpenMouse ATTACK SHARK][om-attack] 和 [Delux M600 Pro 实测][om-delux] 确认 `20` 与电量集合的位置。 |
| `1D57:FA65` | `MI_02 / 000A:0000` Input `03` | S | [HaloBattery inphic.py][halo-inphic] 记录 INPHIC HUB 的 `95 / 90 / 93 / 99` 白名单和充电实测。 |
| `093A:522C` | `MI_03 / FF05:0001` Input `09` | S | [OpenMouse Incott 实测][om-incott-doc] 与[协议记录][om-incott]给出无线集合、8 字节样本及完整充电周期，确认是主动上报。 |
| `24AE:1413` | 唯一 `FF00:0002 / 000E` Input `07` | S | [HaloBattery rapoo.py][halo-rapoo] 记录 VT7 的接收器和有线 PID、候选集合与网页驱动的百分比及状态布局。 |
| `24AE:14A9` | 唯一 `FF00:0002 / 000E` Input `07` | S | [HaloBattery rapoo.py][halo-rapoo] 将 VT9 Pro V2 列为同代接收器，单独的 Windows 集合和报告长度待核实。 |
| `24AE:1205` | `FF0B:0104` GET Feature `2A` | S | [OpenMouse Rapoo][om-rapoo] 记录旧 VT9 Pro 无线和有线样本，Windows 回包含 ID 时取 `[9]`。 |

## 暂未启用

- Delux M800 Mini 可共用 `FA60` 路由，但候选型号字节 `2B` 缺少实际电量帧证据，仍待定位。未知型号帧丢弃。
- Incott `622C`、雷柏 `4613` 与 `4405` 为有线 PID。三个共享 VID 未收录的 PID、1D57 设置通道及其他集合不纳入匹配。
- Incott `8E/01` 在完整充电周期内没有反映电量变化。雷柏旧 `BA A5 AA` 需要当前传输没有提供的 GET_REPORT(Input)，`A5` 还被旧协议用作写入操作码。

## 验证

- macOS 执行 `dotnet build Jige.slnx -p:EnableWindowsTargeting=true`，0 个警告、0 个错误。
- 连续三次执行 `dotnet test Jige.slnx`，每次 Core 282 项、Tray 258 项，共 540 项通过，0 失败、0 跳过。
- 定向测试验证逐 PID 检测、无效帧丢弃、无写入、旧 VT9 只发起 GET Feature `2A`、虚拟时钟触发超时、诊断历史脱敏与回放，以及托盘等待、保留读数、状态恢复和接收器拔出。
- 待 Windows 10/11 实机验证各接收器的 MI、集合唯一性及 HID caps、活动及充放电状态、睡眠唤醒、热插拔、诊断与监听并行、厂商驱动和游戏输入。Windows NativeAOT 包及安装器未在此轮执行。

[halo-inphic]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/inphic.py
[halo-rapoo]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/rapoo.py
[tray]: https://github.com/incconutwo/mouse-battery-tray/blob/c1c6059472dab366c700a7f7516e0dbffde68fd7/devices.py
[om-incott]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/incott/index.ts
[om-incott-doc]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/docs/incott-testing.md
[om-attack]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/attackshark/hid.ts
[om-delux]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/docs/delux-m600-pro-testing.md
[om-rapoo]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/rapoo/index.ts

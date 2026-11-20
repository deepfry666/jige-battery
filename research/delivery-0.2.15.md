# 0.2.15 交付记录｜国产鼠标第一批

本版新增 27 个 2.4 GHz 接收器 VID:PID，全部显示「仅按资料」。详细来源见[网页驱动调研](domestic-mice-webdriver.md)和[型号索引](domestic-mice-models.json)。以下沿用调研的证据记号。W 为官方网页驱动，M 为多来源一致，S 为单一来源，I 为候选推定。网页目录和上游实机记录属于不同层次的证据，均不等同于几格在 Windows 上的实测结果。

## 实现

- **Compx-OUT（16 个）**。复用 17 字节电量及在线查询。仅打开唯一匹配的 `FF02:0002` 集合，实际开句柄时核验 Input / Output 报告 ID `08`。共享接收器采用通用名称，现有 ATK CID/MID 表继续用于已有 ATK 路径。
- **XVI-NEW（9 个）**。复用 `FFFF:0000`、Feature 65 字节、ID `00`。游狼和 Lamzu Maya X 要求 `MI_02`。其他新行在未核实 MI 时以唯一匹配集合为准。检查 A1/A2 回复头与完整命令回显。
- **MCHOSE 反码 Feature（2 个）**。按实际 Feature caps 在 `FF01:0001` 上选择 ID `11`（21 B）或 `12`（65 B），每次读取前单独发送白名单帧。过滤占位帧及请求回显，校验机身 VID 和百分比，沿用休眠时保留上次读数的行为。诊断回放保留 VID、连接位域、电量及充电，遮蔽机身型号、固件版本和尾部数据。
- 首批新增 VID 只按目录识别。鼠标目录中的 `GenericRoute` 仍限定 `373B`。超时后的查询或重试沿用原协议及原集合。设备文档、致谢、版本和三份调研材料已同步。新增测试使用独立构造的模拟帧。

## PID 证据与验证状态

| 接收器 | 路径 | 证据 | 已有依据和边界 |
| --- | --- | --- | --- |
| `3554:F58A` | Compx-OUT | M | atk-control / HaloBattery 的 VXE R1 系列记录。 |
| `3554:F505` | Compx-OUT | W / M | VXE R1 与 VGN F1 共用，显示「VXE / VGN 鼠标」。 |
| `3554:F58E` | Compx-OUT | S | mouse_tray 接收器记录。 |
| `373B:1040` | Compx-OUT | W / M | ATK HUB、atk-control 及社区 MAD 记录，多型号共用。 |
| `373B:1085` | Compx-OUT | S | HaloBattery 的 VXE R1 SE+ 路由记录。 |
| `3554:F5F7` | Compx-OUT | W / M | Scyrox S-center 与 HaloBattery V8 电量记录。 |
| `3554:F5F4` | Compx-OUT | W | Scyrox 官网配置及同家族查询代码。 |
| `3554:F517` | Compx-OUT | W / M | Scyrox V8S 接收器记录与多个 Control HUB 目录，跨品牌共用。 |
| `3554:FB56` | Compx-OUT | W | VGN HUB F2 Master；独立 F2 Master+ 实现佐证帧格式。 |
| `3554:FB3E` | Compx-OUT | W | VGN HUB Y2 Ultra。 |
| `3554:FB52` | Compx-OUT | W | VGN HUB Y2 Master。 |
| `3554:FB54` | Compx-OUT | W | VGN HUB Y2 Pro Max。 |
| `3554:FB58` | Compx-OUT | W | VGN HUB Dragonfly 3 Master。 |
| `3554:F503` | Compx-OUT | W | VGN F1 共用接收器。 |
| `3554:F50B` | Compx-OUT | W | VGN F1 共用接收器。 |
| `3554:F50D` | Compx-OUT | W / M | Lamzu Aurora 与其他品牌 Control HUB 目录，沿用 Compx 回包校验，不推断具体品牌。 |
| `373E:001E` | XVI-NEW | W / M | Lamzu Aurora 8K 目录与 HaloBattery 的 A1 路径。 |
| `373E:001D` | XVI-NEW | W | Lamzu Aurora 1K 目录；MI_02 和帧布局沿用 Maya X 家族，待实机确认。 |
| `36A7:A880` | XVI-NEW | W / M | WL MOUSE HUB 与 HaloBattery Beast X Max 记录。 |
| `36A7:A868` | XVI-NEW | W | WL MOUSE HUB Beast X Mini Pro。 |
| `36A7:A883` | XVI-NEW | W | WL MOUSE HUB Beast X。 |
| `36A7:A887` | XVI-NEW | S | HaloBattery 旧 Beast X 接收器记录。 |
| `36A7:A882` | XVI-NEW | W | WL MOUSE HUB 共享 1K 接收器，显示「WLmouse 鼠标」。 |
| `2023:F013` | XVI-NEW | M | 初代 A5 Pro Max 1K，HaloBattery #149 报告与独立实现一致。 |
| `2023:F015` | XVI-NEW | M | 初代 A5 Pro Max 4K，多方记录认得该 PID，电量帧仍待该接收器实测。 |
| `5253:1020` | MCHOSE 反码 Feature | M | mchose-linux 的 L7 Pro 与 HaloBattery 的 M7 Ultra 实测，共用接收器。 |
| `3837:100B` | MCHOSE 反码 Feature | I | A7 V2 Ultra 描述符确认 `MI_02 / FF01:0001`（HaloBattery #4），电量回复待验。 |

## 暂未启用

| PID / 路径 | 证据 | 原因 |
| --- | --- | --- |
| `3554:F510` | W / I | 验收 A2 将其列为 Lamzu 候选，A3 明确禁止启用同 PID 的 Attack Shark 路径。两者不能通过已知 VID:PID 与集合特征区分，按 A3 保持禁用。 |
| `3554:F524`、`3554:F527`、`3554:F5D5` | W | Zaopin 的网页驱动含 `[10]==1` 时读取 `[11]` 的另一回包分支，适用的固件和 PID 待核实。 |
| `3554:FB43`、`3554:FB44` | W | Attack Shark Control HUB 有目录及查询线索，实机回包分支待核实。 |
| `3554:FB26`、`3554:FB23` | W / I | Delux Control HUB 模板，实机状态布局待核实。 |
| `3554:FB36`、`3554:FB35` | W | IROK 网页驱动将 `FB36` 映射为 HE6 Ultra USB，将 `FB35` 映射为 HE6 Ultra 2.4 GHz。A2 仅列 `FB36`，按其有线排除规则跳过，`FB35` 留待另行确认。 [IROK 网页驱动 JS](https://hid.irok.cn/assets/index-CRqxRK3F.js) |
| `3554:F58C/F58F/F5F6/F515/FB57/FB3D/FB53/FB55/FB59/F502/F506/F50A/F50F`、`2023:F019`、`5253:0031/00B0`、`373E:001C` | W / M / S，依型号 | 现有本批路由未实现有线充电状态与同机无线接收器合并，暂仅支持 2.4 GHz。 |
| VGN `391D`、Lamzu `37B0`、WLmouse 旧 Beast X 4K、Dareu A950/Jm `260D` | W / M / S，依型号 | 存在其他集合、帧或独立传输，保留在调研范围。 |
| 雷柏 Gen 2、`1D57`、Incott | W / M / S，依型号 | 被动 Input 推送按验收安排留给 0.2.16。 |

## 验证

基线为 Core 176 项、Tray 257 项。最终构建与回归结果见下方，完整解决方案的测试同时覆盖已有设备和托盘状态逻辑。新增测试从注册表检测入口验证逐 PID 的完整帧和回复解析，并覆盖错误集合零查询句柄、未收录 PID、重复匹配、超时、危险命令、诊断脱敏回放及目录文档一致性。

- macOS 上通过 `dotnet build Jige.slnx -p:EnableWindowsTargeting=true`，0 个警告、0 个错误。
- macOS 上通过 `dotnet test Jige.slnx`，Core 254 项、Tray 257 项，共 511 项，0 失败、0 跳过。
- 本轮没有 Windows 11 及目标接收器。仍需实机验证 Windows 枚举、实际 HID 报告 ID 和长度、MI 唯一性、共享 PID 固件的回包分支、充电与睡眠唤醒、厂商驱动同时开启、热插拔及游戏过程中输入是否保持正常。重点核实 WLmouse A2、MCHOSE A5 4K、`3837:100B` 和两种 MCHOSE Feature caps。没有运行 Windows NativeAOT 发布包。

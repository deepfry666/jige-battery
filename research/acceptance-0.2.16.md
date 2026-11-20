# 0.2.16 验收标准：国产鼠标第二批（被动上报）

背景：0.2.15 做了能主动查询的国产鼠标。这一批的鼠标**不接受电量查询**，而是由接收器在鼠标活动或电量变化时主动推送 Input 报告，几格只能「听」。调研见 [domestic-mice-webdriver.md](domestic-mice-webdriver.md) 第 3.3–3.5 节与 [domestic-mice-models.json](domestic-mice-models.json)。

现有基础：`IPushPresenceAdapter.ListenPushAsync` + `PresenceProbeKind.PushListener`（罗技接收器在用），`PresenceUpdate.Battery` 已能把电量送进托盘模型，`TrayApp.StartPushListeners` 对所有 PushListener 设备通用。本版**复用这条通道**，不另起一套。

## 硬性门槛

- G1：`dotnet build Jige.slnx -p:EnableWindowsTargeting=true` 零警告；`dotnet test Jige.slnx` 全过，测试数不少于 511。
- G2：**被动路径零发送**。1D57、Incott、雷柏 Gen 2 三条路径不调用任何 `WriteOutputAsync`/`WriteFeatureAsync`；用测试断言 fake transport 上写操作次数为 0。唯一允许的主动操作是 C3 的雷柏旧 VT9 `GET Feature 2A`（只读，不配 SET）。
- G3：**精确路由**。只认下文列出的 VID:PID + 唯一匹配的 Input 集合；`093A` 是 PixArt 公共 VID、`1D57`/`24AE` 下也有其他设备，**一律不做通用识别**。同 VID 未登记 PID、或同 PID 的其他集合：零打开（测试）。
- G4：**沉默不等于没电、不等于离线**。没收到推送时：有上次读数则保留并标「休眠/等待上报」，没有读数显示 `--`；绝不显示 0%，也不单凭沉默判离线。
- G5：**测试不依赖真实计时**（0.2.15 在慢 CI 上翻车两次的教训）：被动读取窗口、等待时长做成可注入参数，测试传 0 或用 fake 时钟。
- G6：不影响游戏：监听线程阻塞在读上，不忙等、不定时唤醒；游戏中照常只是被动接收，不增加任何发送。
- G7：许可：HaloBattery / mouse-battery-tray（MIT）可借代码并在 `docs/credits.md` 致谢；OpenMouse（AGPL）只借事实。
- G8：版本 0.2.16：`Directory.Build.props` **和 `installer/jige.iss` 的 `AppVersion`** 都要改；CHANGELOG `## [0.2.16]` 用用户能看懂的话写；README「支持的设备」表、`docs/devices.md`、`docs/protocols.md` 同步，新增条目一律「仅按资料」。

## P. 被动上报公共部分

- P1：每个被动适配器实现 `IPushPresenceAdapter`，`PresenceKind = PushListener`。`ListenPushAsync` 打开那一个 Input 集合，循环读报告，解析成功就 `onUpdate(new PresenceUpdate(...) { Battery = ... })`；解析失败的帧丢弃（诊断里可见），不报错。
- P2：`ReadAsync`（轮询、手动刷新、诊断导出时调用）只做一个**短的被动读取窗口**（默认 ≤ 400 ms，可注入）：窗口内收到有效帧就返回读数；否则返回 `LinkState.Asleep`、`Percentage = null`、说明「等待鼠标上报电量」。
- P3：推送监听与轮询同时打开同一集合时不能互相抢数据导致丢帧或报错（Windows 下每个句柄各有输入缓冲；如有冲突，让轮询路径在监听运行时直接返回监听缓存的最近读数）。交付说明里写清楚你的做法。
- P4：诊断报告：被动设备也要能导出最近收到的原始帧（已有的脱敏规则照用），便于用户反馈。

## A. 1D57 电量推送（ATTACK SHARK / Delux / 英菲克）

- A1：`1D57:FA60`（ATTACK SHARK X11 / R1 / X3 Max、Delux M800 Mini / M600 Pro 共用接收器，中性名「ATTACK SHARK / Delux 鼠标」）、`1D57:FA65`（Inphic IN9 Pro）。
- A2：集合：调研记录为 `MI_02`、usage page `000A`、usage `0000`、Input ID `03`。Input 长度以 caps 为准；按 HaloBattery `providers/inphic.py` 与 mouse-battery-tray `devices.py`（均 MIT）核对并写进交付说明。
- A3：帧 `03 <型号> 40 <状态> <数值>`。只接受**已知型号字节**（至少 X11 `55`、X3 Max `4E`、R1/X6 `10`，以及你在 MIT 来源中核实到的其他型号，含 Delux M600 Pro、Inphic IN9 Pro）。状态语义按型号：通常 `01` 百分比、`03` 充电；X3 Max 的 `02` 为充电，其他型号 `02` 视为已充满；R1/X6（`10`）数值以 10% 为单位、充电未知。未知型号字节：丢弃，不猜。
- A4：读到型号字节后可把共享接收器的显示名细化为具体型号（与 Compx CID/MID 细化同理）。
- A5：控制通道（写 DPI / 配置）不打开、不枚举。

## B. Incott Input 09

- B1：只认 `093A:522C`（接收器）。`622C` 有线本版不加。
- B2：电量在 vendor Input ID `09`（样本 8 B 含 ID）；具体 usage page/usage、MI 按开源记录确认，必须是唯一匹配的集合；`FF05` Feature 集合不打开。
- B3：`[1] <= 100` 直接为百分比、未充电；`[1] > 100` 时减 128 为百分比并标充电；结果不在 0–100 丢弃。`[2]` 是 DPI/回报率标志，忽略。
- B4：调研已证伪的「`8E/01` 主动电量查询」不得实现。

## C. 雷柏 Rapoo

- C1：Gen 2：VT7 `24AE:1413`（接收器）、VT9 Pro V2 `24AE:14A9`（接收器）。`4613` 若确认是有线就不加。
- C2：Gen 2 被动 Input ID `07`，候选集合 `FF00:0002` / `FF00:000E`，按 HaloBattery `providers/rapoo.py`（MIT）确认并要求唯一匹配。`[7]` 状态（1 电池、2 充电、0 无效→丢弃）、`[8]` 百分比。
- C3：旧 VT9 Pro 接收器 `24AE:1205`：在 `FF0B:0104` 集合上只做 `GET Feature 2A`（**不发 SET**），回包 `[9]` 百分比，充电未知（`ChargingState.Unknown`）。这是本版唯一的主动读取；同样要白名单校验 report ID。`4405` 有线不加。
- C4：旧协议的 `BA A5 AA` Output 路径**不做**（需要 GET_REPORT(Input)，传输层没有，而且 `A5` 在旧协议里同时是写操作码）。

## D. 测试

- D1：每个新 VID:PID：正确集合 → 推送若干帧 → 解析出预期百分比、充电、（1D57）细化型号；无效帧被丢弃。
- D2：被动路径写操作计数为 0（G2）；雷柏旧 VT9 只出现 GET Feature `2A`。
- D3：窗口内无帧 → Asleep + null，不是 0%（G4）；有上次读数时托盘模型保留上次读数。
- D4：未登记的 `093A:*`、`1D57:*`、`24AE:*` → 不识别；同 PID 其他集合 → 零打开。
- D5：未知 1D57 型号字节、Incott 越界值、Rapoo 状态 0 → 丢弃。
- D6：所有涉及等待的测试用注入的 0 时长，不依赖 CI 速度（G5）。

## 交付

`research/delivery-0.2.16.md`：做了什么、每个 PID 的证据等级与来源文件、P3 的做法、没做的及原因、需实机确认的点。提交到分支、开 PR，不打 tag、不发布、不合并。

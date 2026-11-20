# 0.2.15 验收标准：国产鼠标第一批

背景：2026-11-20 用户决定主动适配国产无线鼠标（不再只按用户报告逐个加）。调研见 [domestic-mice-webdriver.md](domestic-mice-webdriver.md) 与 [domestic-mice-models.json](domestic-mice-models.json)。本版做调研第 5 节的 **1A + 1B + 2A 中的 MCHOSE 反码 Feature**。被动推送类（雷柏 Gen 2、`1D57`、Incott）需要新的「被动读 Input」能力，放 0.2.16。

## 硬性门槛

- G1：`dotnet build Jige.slnx -p:EnableWindowsTargeting=true` 零警告；`dotnet test Jige.slnx` 全部通过，测试数不少于现有数量。
- G2：**只读**。只发送下文列出的完整查询帧；发送前按帧白名单整帧比对。危险命令（Compx `05/07/09/0B/0C/0D/0F`、XVI 配对/写、MCHOSE `57/67/03` 等）必须有单元测试证明无法发出。
- G3：**精确路由**。新增 VID（`3554`、`36A7`、`373E`、`2023`、`5253`、`3837`）**一律不开放通用识别**，只认目录里登记的 VID:PID + 接口特征唯一匹配。同 VID 下不匹配的集合零发送（测试覆盖）。`GenericRoute` 仍只限 `373B`。
- G4：共享接收器（同一 PID 被多款鼠标或多个品牌使用，如 `3554:F505`、`3554:F517`、`3554:F50D`、`373B:1040`）显示**中性名称**（如「VXE / MAD 鼠标」「Compx 方案鼠标」），不按 PID 写死某一零售型号。Compx 若读出 CID/MID 且在已知表里，可细化型号。
- G5：一个接收器只走一条路径；超时不轮试其他家族。
- G6：不影响游戏、不弹窗、不改现有 UI 行为。现有 ATK、游狼、Finalmouse 路由行为不变（现有测试全过）。
- G7：许可：HaloBattery/atk-control/atkctl/MousePower/mchose-linux（MIT）可借代码并在致谢页注明；**OpenMouse（AGPL）和 mouse_tray（无许可）只借事实，不复制代码、表格、测试样本**。
- G8：版本号改为 0.2.15；CHANGELOG `## [0.2.15]` 用用户能看懂的话写；README「支持的设备」表、`docs/devices.md`、`docs/protocols.md` 同步，新增条目一律标「仅按资料」。`docs/devices.md` 与目录一致性测试保持通过。

## A. Compx-OUT 复用（批次 1A）

沿用现有 Compx-OUT 编解码：`FF02:0002`，Output/Input ID `08`，17/17 B；电量 `08 04 00…49`、在线 `08 03 00…4A`、CID/MID `08 10 00…3D`；校验低 8 位求和为 `55`；回包 `[6]` 百分比、`[7]` 充电、`[8..9]` 大端 mV。

- A1：把 Compx-OUT 的厂商范围从仅 `373B` 扩展到目录里登记的 `3554` 行；`IsVendor`/`Detect` 预筛改为「VID 在已知集合 **或** VID:PID 被目录精确登记」。
- A2：新增目录行（全部 `Enabled: true`，证据按调研标注）：
  - VXE R1 系列接收器 `3554:F58A`、`3554:F505`（共享，中性名「VXE 鼠标」）；`3554:F58E` 接收器（单一来源）。
  - MAD / VXE R1 SE+：`373B:1040`（中性名「MAD 鼠标」，共享）、`373B:1085`。注意 `373B` 已在现有目录体系内，直接加行。
  - Scyrox：`3554:F5F7`（V8，多来源）、`3554:F5F4`、`3554:F517`（共享，中性名）。
  - VGN：F2 Master `3554:FB56`、Y2 Ultra `FB3E`、Y2 Master `FB52`、Y2 Pro Max `FB54`、Dragonfly 3 Master `FB58`；F1 共享接收器 `F503/F505/F50B`（中性名「VGN 鼠标」）。`391D` **不加**（同 VID 有其他集合，证据不足）。
  - IROK HE6 Ultra `3554:FB36`（先核对调研里是接收器还是有线；有线 PID 不加）。
  - Lamzu Atlantis 接收器 `3554:F50D/F510`（共享，中性名）。
  - 有线 PID（`F58C/F58F/F5F6/F515/FB57/FB3D/FB53/FB55/FB59/F502/F506/F50A/F50F` 等）：若现有架构支持「有线即充电中」的语义就加，否则本版不加，在交付说明里写明。
- A3：Zaopin `F524/F527/F5D5`、Attack Shark `FB43/FB44/F510`、Delux `FB26/FB23`：Zaopin 有「`[10]==1` 时取 `[11]`」的另一回包分支，**本版不启用**这些 PID，以禁用行或注释登记并写原因（回包分支未核实）。
- A4：Dareu A950/Jm（`260D`）本版不做。

## B. XVI-NEW 兼容 Feature 复用（批次 1B）

沿用现有 XVI-NEW：`FFFF:0000`，Feature ID 0，65 B，请求 `00 00 00 02 02 00 83 00…`，回包 `A1 … 83 <充电> <%>`。

- B1：`MouseReceiver` 增加可选的接口号要求（目前 XVI 硬编码 `MI_02`）。游狼保持 `MI_02`；新品牌行在 MI 未核实时允许任意 MI，但仍要求 `FFFF:0000` + Feature 65B 唯一匹配。
- B2：回包头允许 `A1` 和 `A2`（WLmouse 有 A2），仍须 page/target/命令回显 `02 02 00 83` 完全一致才读字段。
- B3：新增行：
  - Lamzu Maya X 8K `373E:001E`、1K `373E:001D`（`MI_02` 已知）。`37B0` **不加**。
  - WLmouse Beast X Max `36A7:A880`、Mini Pro `A868`、Beast X `A883`、旧 Beast X 接收器 `A887`、共享 1K `A882`（中性名「WLmouse 鼠标」）。旧 Beast X 4K 分支不加。
  - MCHOSE A5 Pro Max 初代 `2023:F013`（1K）、`2023:F015`（4K）。
- B4：XVI-NEW 的危险命令仍拒绝；BitMouse 的 `83`（忘记配对）在 BitMouse 路由上永远不可发——加测试。

## C. MCHOSE 反码 Feature（批次 2A 之一，新小型适配器）

- C1：M7 Ultra / L7 Pro 共享接收器 `5253:1020`；A7 V2 Ultra 接收器 `3837:100B`（候选，`MI_02`）。集合 `FF01:0001`，Feature ID `11`（21 B 含 ID）或 `12`（65 B 含 ID），按设备实际 Feature caps 选择，长度以 caps 为准。
- C2：请求：`11 F9 FF…`/`12 F9 FF…`（命令 `06`，body 全部字节 XOR `FF`）。每次 GET 前一次 SET。
- C3：回包：同 ID 且 `[1]=F9`；body 还原反码后 `[2..5]` 为 VID/型号，`[10]` 连接位域，`[11]` 百分比，`[12]` 充电。滤掉全零占位帧和请求回显；百分比不在 0–100 拒绝。
- C4：只允许命令 `06`；`57`（整块写设置）、`67`、`03` 以及任何其他命令都不可发出（测试）。
- C5：注册到 `AdapterRegistry`，与现有 `MchoseKbAdapter`（键盘 `41E4:2001`）不冲突；`DeviceCatalog` 登记能力与证据；有线 `0031/00B0` 本版不加。
- C6：代码参考 mchose-linux / HaloBattery（MIT，致谢页注明）。

## D. 测试

- D1：每个新增 VID:PID 至少一条测试：正确集合 → 发出的帧逐字节等于预期；模拟回包 → 解析出预期百分比与充电。
- D2：同 VID 不同集合（例如 `3554` 键盘 `FA09`、`FF01` 集合）→ 零打开、零发送。
- D3：未登记的 `3554:xxxx`、`36A7:xxxx`、`373E:xxxx` → 不识别。
- D4：危险命令测试见 G2、B4、C4。
- D5：诊断报告的厂商名映射加上 VXE/VGN/Scyrox/WLmouse/Lamzu/MCHOSE 的 VID（如尚无）。

## 交付

在 `research/delivery-0.2.15.md` 写：做了什么、每个 PID 的证据等级、没做的及原因、需要实机确认的点。不要打 tag、不要发布；提交到分支并开 PR。

# 几格 / Jige Battery（v0.2.0）独立评审报告

> 评审对象：`deepfry666/jige-battery`（`main` 分支，`v0.2.0`，提交 `6cf678a`）  
> 评审性质：只读代码、协议文档、交互原型、构建与发版配置全面评审  
> 总体评价：项目架构克制且有工程洁癖——单进程 NativeAOT、零外部运行时依赖、只读白名单意识清晰、DirectComposition 面板收起即释放 GPU 资源、脱敏回放测试体系扎实。但目前刚跨越“能跑起来”的早期阶段，在**核心交互一致性、诊断闭环完整性、事件驱动可靠性、竞技游戏零干扰以及设备实机覆盖**上仍有若干高杠杆问题值得优先收口。

---

## 一、问题总表（按严重度排序）

| 编号 | 问题 | 类别 | 严重度 | 依据（文件:行 / 具体现象） | 建议 | 估计工作量 |
| --- | --- | --- | --- | --- | --- | --- |
| P01 | 诊断报告对 Logitech / Razer 全线失效：适配器名误标为 `ATK / G-Wolves`，且所有 HID 回复帧被全零清空 | 安全与隐私 / 设备协议 | 高 | [DiagnosticReport.cs:155-160](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticReport.cs#L155-L160)；[DiagnosticPrivacy.cs:36-78](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticPrivacy.cs#L36-L78)；[DiagnosticTransport.cs:11,83-85](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticTransport.cs#L11) | 补全 4 个罗技/雷蛇适配器的名称映射与按协议白名单脱敏分支；为多句柄（如 `ShortHid`）按实际端点记录 `OP` 并补回放测试 | M（1–2 天） |
| P02 | 「游戏中不打扰」设置开关几乎无效，且右键菜单在静默态仍被拦截并会在退出游戏后“幽灵弹出” | 产品与体验 / 可靠性 | 高 | [PollSchedule.cs:40-44](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/PollSchedule.cs#L40-L44)；[PopupSubPages.cs:77](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupSubPages.cs#L77)；[TrayApp.cs:384-388,855-862](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L855-L862) | 删除 `ShowMenu` 中遗留的 `BlockPopups` 拦截与 `pendingUi` 队列；明确“游戏中不打扰”的真实语义（控制全屏/游戏弹窗推迟或改为仅控制提示/专注时段并写明副标题） | S（半天） |
| P03 | Finalmouse ULX 与 ATK 识别门槛过窄，且界面对未收录/已禁用接收器完全“失声” | 设备协议 / 产品与体验 | 高 | [FinalmouseAdapter.cs:7-11,17-23](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/FinalmouseAdapter.cs#L7-L23)；[MouseReceiverCatalog.cs:42-75](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/MouseReceiverCatalog.cs#L42-L75)；[TrayApp.cs:962-994](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L962-L994) | 用实机诊断核实 ULX 实际 PID/65B 报告长度与 ATK 高频接收器（如 `101B` 8K、`111A`）；设备管理页展示已检测到但未支持的厂商接收器并给诊断导出增加进度/报错反馈 | M–L（2–3 天） |
| P04 | 缺少 USB 热插拔 (`WM_DEVICECHANGE`) 与休眠唤醒 (`WM_POWERBROADCAST`) 监听，且轮询调度依赖墙上时钟 | 可靠性 | 高 | [TrayApp.cs:252-316](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L252-L316)；[NativeMethods.txt](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/NativeMethods.txt)；[PollSchedule.cs:3-35](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/PollSchedule.cs#L3-L35) | 注册 `GUID_DEVINTERFACE_HID`/串口 `WM_DEVICECHANGE`（防抖 1–2s 触发刷新）；监听 `WM_POWERBROADCAST` 唤醒后延迟 3s 刷新；调度间隔改用单调时钟 `TickCount64` | M（1–2 天） |
| P05 | 每轮全量 `SetupDi` 枚举 + 对所有集合调 `HidD_GetProductString` + 多设备串行阻塞 + `LOCATIONCHANGE` 高频唤醒主线程 | 性能与对游戏的影响 | 高 | [DevicePoll.cs:19-35](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/DevicePoll.cs#L19-L35)；[WindowsDeviceDiscovery.cs:47-48,161](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Windows/WindowsDeviceDiscovery.cs#L47-L48)；[SonyInzoneAdapter.cs:47-50](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/SonyInzoneAdapter.cs#L47-L50)；[TrayApp.cs:170-199](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L170-L199) | 配合热插拔缓存设备拓扑而非每轮重扫；仅对未知 `046D` 与诊断调用 `HidD_GetProductString`；按不同 `ReceiverId` 并行轮询；收敛 `LOCATIONCHANGE` 监听避免非游戏窗口噪声唤醒 | M（2 天） |
| P06 | 充电中仍弹“该充电了”提醒；自定义低电量阈值未同步到托盘和面板颜色；提醒弹出后进游戏即永久丢单 | 产品与体验 | 高 | [TrayModel.cs:123-129](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TrayModel.cs#L123-L129)；[Presentation.cs:58-65](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L58-L65)；[TrayApp.cs:364-368,633-637](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L633-L637) | 充电中抑制低电量提醒；`Presentation.Tone` 接收 `LowBatteryThreshold`；仅在用户点击“知道了”/关闭时才消费提醒，被游戏打断自动重新入队 | S（半天） |
| P07 | Logitech HID++ 2.0 在长报告句柄等待短报告 `0x10`（Windows TLC 路由冲突），且每轮重复查 6 槽与全部元数据 | 设备协议 / 性能 | 高 | [LogitechHidppAdapter.cs:20-23,65,79-103,107,149-151,174-205](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/LogitechHidppAdapter.cs#L65)；[LogitechBatteryTests.cs:63-64](file:///Users/jjjj/Projects/jige-battery/tests/Jige.Core.Tests/LogitechBatteryTests.cs#L63-L64) | 同时监听短/长报告句柄或按真实 Windows TLC 区分错误帧（需实机核验）；会话/设备级缓存已发现槽位、名称与 feature 索引；未知 `046D` 仅在探针/诊断中探测，不在托盘每分钟发包 | M（1–2 天） |
| P08 | 托盘日志附入诊断报告但未脱敏原始异常文字与前台窗口类名，违背 `PRIVACY.md` 承诺 | 安全与隐私 | 中 | [PRIVACY.md:21](file:///Users/jjjj/Projects/jige-battery/PRIVACY.md#L21)；[Desktop.cs:164-174](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Desktop.cs#L164-L174)；[TrayApp.cs:685,688,977-979](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L977-L979) | 日志改用结构化错误码/事件码，不记录原始异常文本与第三方前台窗口类名，或在拼入诊断报告前过滤/脱敏；同步更新 `PRIVACY.md` | S（半天） |
| P09 | 首次安装后无任何可见引导且开机自启默认关闭，Win11 折叠托盘图标导致用户以为“没启动” | 产品与体验 | 中 | [installer/jige.iss:52-53](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L52-L53)；[TraySettings.cs:15](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TraySettings.cs#L15)；[TrayApp.cs:82-92,116-140](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L116-L140) | 安装器增加“开机启动几格”任务（默认勾选）；首次启动（无旧 `config.json` 且非游戏中）自动弹出一次面板并提示将图标从 `^` 拖到任务栏 | S（半天） |
| P10 | 离线/换 USB 口后的历史设备无法删除，隐藏后仍污染悬停提示，且自定义改名无法清空还原 | 产品与体验 / 可靠性 | 中 | [TrayModel.cs:20-26,112-119](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TrayModel.cs#L20-L26)；[PopupSubPages.cs:322-369](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupSubPages.cs#L322-L369)；[Presentation.cs:123](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L123)；[TraySettings.cs:25-32](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TraySettings.cs#L25-L32) | 设备管理页为离线设备提供“移除”操作；空字符串保存视为恢复默认型号名；悬停提示与面板隐藏策略对齐或提供选项 | S–M（0.5–1 天） |
| P11 | 更新流程缺进度反馈、CDN 域名白名单过严、静默升级失败无兜底，且产物未代码签名 | 发版与更新 / 安全与隐私 | 中 | [UpdateDialog.cs:90-106,119,157-198](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/UpdateDialog.cs#L90-L106)；[Updates.cs:195](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Updates.cs#L195)；[installer/jige.iss:94-123](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L94-L123)；[release.yml](file:///Users/jjjj/Projects/jige-battery/.github/workflows/release.yml) | 给 `TaskDialog` 加下载进度条并在失败时附“查看 Release”按钮；重定向放宽至 `*.githubusercontent.com`；静默升级写安装日志并在失败时提示；后续规划签名（如 SignPath 开源免费签名）+ 独立公钥验签 | M（1–2 天） |
| P12 | 高 DPI 每次更新托盘数字均重建 WIC/D2D/DirectWrite 工厂；面板动画每 15ms 重绘并 `SetWindowPos` | 性能 / 视觉与交互 | 中 | [TrayIcon.cs:147-205](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayIcon.cs#L147-L205)；[PopupWindow.cs:176,189,197,260-266](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupWindow.cs#L260-L266)；[Graphics.cs:244-270](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Graphics.cs#L244-L270) | `TrayIcon` 复用单例 `TextFactory`/WIC/D2D 工厂或按需轻量缓存；`Canvas.BrandIcon` 缓存 `ID2D1Bitmap`；评估固定最大表面高度+仅用 DirectComposition 裁剪动画替代每 15ms `SetWindowPos` | S–M（1 天） |
| P13 | CI 与发版覆盖缺口：PR 不编译安装器、Release 缺 `Jige.Probe.exe`、安装器可能退回英文界面、`--check` 未测合成树 | 发版与更新 / 测试 | 中 | [build.yml:11-61](file:///Users/jjjj/Projects/jige-battery/.github/workflows/build.yml#L11-L61)；[release.yml:88-102](file:///Users/jjjj/Projects/jige-battery/.github/workflows/release.yml#L88-L102)；[installer/jige.iss:35-39](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L35-L39)；[NativeCheck.cs:11-44](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/NativeCheck.cs#L11-L44) | `build.yml` 增加 `ISCC` 编译检查；`release.yml` 一并打包发布 `Jige.Probe.exe` 进 Release；仓库内置简中 `.isl` 避免混语；`--check` 增加 `BrandIcon`/WARP `Composition` 冒烟 | S–M（1 天） |
| P14 | 视觉与交互打磨不足：提醒卡片品牌图标不统一、无手型/输入光标、时长只显示分钟、`≈100%` 易裁剪、主/设页间隔文案不统一 | 视觉与交互 | 中 | [PopupScene.cs:309,324,355,388,397-408,441](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L309)；[TrayApp.cs:152](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L152)；[Presentation.cs:103,109-110,183-184](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L103-L110)；[PopupSubPages.cs:94](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupSubPages.cs#L94) | 统一提醒卡片用 FourJ 标志；处理 `WM_SETCURSOR` 区分链接手型与改名 I-Beam；`Age()` 支持刚刚/分钟/小时/天；统一“60 秒”文案；微调 `≈100%` 布局宽度 | S（半天） |
| P15 | 卸载无确认直接清空 `%LOCALAPPDATA%\Jige`（含设置、别名与未发送的诊断报告） | 发版与更新 / 产品与体验 | 中 | [installer/jige.iss:55-56](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L55-L56)；[README.md:63](file:///Users/jjjj/Projects/jige-battery/README.md#L63) | 在卸载脚本 `CurUninstallStepChanged` 中弹窗询问是否清除用户设置与缓存（静默卸载保持默认行为），避免排障重装丢配置 | S（2–3 小时） |
| P16 | Razer 90B 单次 60ms 无重试且协议忙误报“接收器被占用”；游狼 `XVI-NEW` 无休眠判定易显示缓存假电量 | 设备协议 | 中 | [RazerFeatureAdapter.cs:34-39,66-71](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/RazerFeatureAdapter.cs#L34-L39)；[Presentation.cs:96](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L96)；[MouseBatteryProtocol.cs:87-89](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/MouseBatteryProtocol.cs#L87-L89) | Razer 收到状态 `01` 时退避重试 2–3 次，最终失败归为 `Timeout` 而非“被占用”；实机验证 HTX Ultra 8K 休眠/关机时的 `XVI-NEW` 回复特征以防假在线 | S（半天） |
| P17 | 竞品差距：缺“自动切显最低电量设备”、缺插 USB-C 线充识别、主面板设备行可悬停却不可点、电压 mV 被丢弃 | 产品与体验 | 低 | [TrayModel.cs:48-50](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TrayModel.cs#L48-L50)；[PopupScene.cs:361-389](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L361-L389)；各适配器仅匹配无线接收器 PID | 增加“托盘显示：自动（最低电量优先）”选项；主面板点击副设备行可快速切换托盘置顶；后续评估主流鼠标有线充电 PID 合并去重 | M–L（2–4 天） |
| P18 | `WM_SETTINGCHANGE` 未过滤参数频繁重建图形树；Win10 缺少 `Segoe UI Variable` 字体回退 | 可靠性 / 视觉与交互 | 低 | [TrayApp.cs:295-298](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L295-L298)；[Graphics.cs:81](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Graphics.cs#L81)；[InlineEdit.cs:44](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/InlineEdit.cs#L44)；[Program.cs:12](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Program.cs#L12) | 仅当 `lParam` 涉及主题/动画/DPI/高对比度时 `resetPopup: true`；检测系统版本在 Win10 回退至 `Segoe UI` | S（2–3 小时） |
| P19 | 测试缺口：Logitech/Razer 无诊断回放单测、自定义阈值与显示色联动无测试、缺 Windows 端到端冒烟 | 代码质量与测试 | 低 | [DiagnosticTests.cs](file:///Users/jjjj/Projects/jige-battery/tests/Jige.Core.Tests/DiagnosticTests.cs)；[PresentationTests.cs:8-19](file:///Users/jjjj/Projects/jige-battery/tests/Jige.Tray.Tests/PresentationTests.cs#L8-L19) | 补齐 4 类罗技/雷蛇的诊断脱敏与回放用例；补阈值联动测试；增加多屏负坐标与热插拔状态机单测 | M（1–2 天） |
| P20 | 文档与 GitHub 首页转化弱：零截图、`docs/protocols.md` 仍写“全部未实机验证”、缺设备报告 Issue 模板 | 文档与呈现 | 低 | [README.md:1-136](file:///Users/jjjj/Projects/jige-battery/README.md#L1-L136)；[docs/protocols.md:3,48,56,151](file:///Users/jjjj/Projects/jige-battery/docs/protocols.md#L3)；仓库无 `.github/ISSUE_TEMPLATE/` | README 顶部放 Win11 托盘与面板实机截图 + 下载引导，将大表与内部验收待办移出首屏；同步 `docs/protocols.md` 实机状态；新增设备适配 Issue 表单模板 | S（半天） |

---

## 二、分项详述

### 高严重度

#### P01｜诊断报告对 Logitech / Razer 全线失效
- **事实依据**：
  1. [DiagnosticReport.cs:155-160](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticReport.cs#L155-L160) 的 `AdapterName` 仅分支了 `FinalmouseAdapter` 和 `SonyInzoneAdapter`，其余全部落入 `_ => "ATK / G-Wolves"`。合并罗技/雷蛇适配器（提交 `10fa43b`）时遗漏了同步更新。
  2. [DiagnosticPrivacy.cs:36-78](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticPrivacy.cs#L36-L78) 的 `HidReply` 只对 ULX 和 `MouseReceiverAdapter` 保留白名单字节，其余 HID 设备（罗技 HID++、Centurion/Astro、雷蛇 90B、BlackShark 64B）全部返回全零数组。
  3. [DiagnosticTransport.cs:11,83-85](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticTransport.cs#L11) 只绑定主 `endpoint`，当 `LogitechHidppAdapter` 在 Bolt/Unifying 上打开 `ShortHid` 查询配对表时，其操作会混记在长报告 ID 名下，导致 `FakeReceiverTransport.FromReport` 无法按端点还原。
- **影响**：只要有罗技/雷蛇用户提交诊断报告，所有收包都是 `0000...`，且匹配行写着 `ATK / G-Wolves`，根本无法用于实机适配与回放。

#### P02｜「游戏中不打扰」开关名不副实 + 右键菜单滞留幽灵弹出
- **事实依据**：
  1. [PollSchedule.cs:40-44](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/PollSchedule.cs#L40-L44)：`game` 恒等于 `fullscreen || notificationState is 2 or 3 or 4`，`BlockPopups` 恒等于 `game || (gameSilent && notificationState == 6)`。也就是说用户在设置页关闭“游戏中不打扰”后，对所有游戏、全屏和演示都**完全不起作用**，唯一影响的是 Windows 新装系统头一小时的 `QUNS_QUIET_TIME`（6）。
  2. [TrayApp.cs:587-594](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L587-L594) 在 v0.1.2 移除了左键的游戏拦截，但 [TrayApp.cs:855-862](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L855-L862) 的 `ShowMenu` 仍残留 `if (game.BlockPopups) { pendingUi = (TrayAction.Menu, point); return; }`。双屏玩无边框游戏（或看全屏视频/开专注）时右键托盘毫无反应，几十分钟后退出全屏时 [TrayApp.cs:384-388](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L384-L388) 会突然在旧坐标弹出右键菜单。
- **建议**：立即删除 `ShowMenu` 里的 `BlockPopups` 拦截与已废弃的 `pendingUi` 机制；若产品原则是“全屏游戏永不弹窗”，则把该开关重新定义（例如控制“游戏中也放慢查询”或“全屏浏览器/视频视为勿扰”），或者让开关真实控制低电量提醒是否在游戏期推迟，避免“假开关”。

#### P03｜Finalmouse ULX 与 ATK 未识别路径（结合实机背景）
- **事实依据**：
  1. **Finalmouse ULX**：[FinalmouseAdapter.cs:7-11](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/FinalmouseAdapter.cs#L7-L11) 仅接受 `361D:0100` 且要求 `UsagePage == 0xFF00 && Usage == 0x0001 && InputLength == 64 && OutputLength == 64`。Windows 的 `HidP_GetCaps` 返回的长度包含 1 字节 Report ID，【推测】若实机固件的 Report ID `04`/`05` 载荷为 64 字节，则 Windows 报告长度为 `65`，或该批次 ULX/8K 接收器使用了其他 PID / Usage，均会被直接拒掉。且若 `361D:0100` 存在但集合不匹配，[FinalmouseAdapter.cs:18-23](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/FinalmouseAdapter.cs#L18-L23) 会把第一个非厂商集合塞进 `DeviceTarget`，导致诊断报告在 [DiagnosticReport.cs:86-90](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Diagnostics/DiagnosticReport.cs#L86-L90) 打印“安全检查未通过，跳过查询”。
  2. **ATK 鼠标**：[MouseReceiverCatalog.cs:42-75](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/MouseReceiverCatalog.cs#L42-L75) 中 30 条 ATK 型号仅启用了 5 个 PID（`101A/1031/11D9/1278/1155`），最常见的 `373B:101B`（8K 接收器）、`373B:111A`（A9 Ultra）、`1216`/`10C9` 等全为禁用或未填 PID。
  3. **未识别体验断裂**：用户插着 ULX 和 ATK 时，主面板与设备管理页完全不提示“检测到 2 个未支持的接收器”；点击“导出诊断信息”（[TrayApp.cs:962-994](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L962-L994)）立即关闭面板并在后台静默跑最多 30 秒，既无进度提示，出错也只吞进 `jige.log`。
- **建议**：让用户在实机跑一次 `诊断信息` 拿到 ULX 与 ATK 的真实 `HID|...` 描述符行；同时在设备管理页底部列出“发现未支持/待核验的厂商接收器（VID:PID）”，一键触发带反馈的诊断导出。

#### P04｜缺少热插拔与休眠唤醒监听，调度依赖墙上时钟
- **事实依据**：[TrayApp.cs:252-316](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L252-L316) 未处理 `WM_DEVICECHANGE`（`0x0219`）和 `WM_POWERBROADCAST`（`0x0218`）；[PollSchedule.cs:5,21-24](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/PollSchedule.cs#L5) 用 `DateTimeOffset` 计算下次轮询时间，收到 `WM_TIMECHANGE`（[TrayApp.cs:299-301](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L299-L301)）时若系统时钟往回校准，旧 `lastStarted` 仍在“未来”，轮询会被推迟整段回拨时长。
- **建议**：主窗口注册 HID/串口设备通知，收到热插拔后防抖 1.5s 触发一次刷新；收到 `PBT_APMRESUMEAUTOMATIC` 后延迟 3s 刷新并重检游戏状态；`PollSchedule` 内部全面切换为单调时间戳（`Environment.TickCount64`）。

#### P05｜轮询与窗口 Hook 对竞技游戏存在隐性开销
- **事实依据**：
  1. **每轮全量 PnP 枚举与多余的 USB 字符串请求**：[DevicePoll.cs:19-20](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/DevicePoll.cs#L19-L20) 每次定时轮询都调用 `WindowsDeviceDiscovery.ScanAsync`，遍历全系统 HID/COM/Ports 三套 `SetupDi` 设备表。更关键的是 [WindowsDeviceDiscovery.cs:47-48,161](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Windows/WindowsDeviceDiscovery.cs#L161) 在判断 `UsagePage` 前就会对所有匹配 VID（包含**全部** `046D` 设备，见 [AdapterRegistry.cs:26](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/AdapterRegistry.cs#L26)）的**每一个** HID 集合调用 `HidD_GetProductString`。对于 8KHz 无线鼠标接收器，这会每 60s（游戏中每 5 分钟）对包括鼠标/键盘输入集合在内的所有 TLC 发送 `IOCTL_HID_GET_PRODUCT_STRING` 控制请求，【推测】在部分主板 USB 控制器上可能引起瞬时微卡顿。
  2. **多设备串行阻塞**：[DevicePoll.cs:22-35](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/DevicePoll.cs#L22-L35) 串行 `await` 所有设备。而 [SonyInzoneAdapter.cs:47-50](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/SonyInzoneAdapter.cs#L47-L50) 每次都先盲等 `500ms` 再发丢弃结果的 `Link`/`Model` 查询，耳机未开机时单设备就阻塞 `3.4s`（实机在线也需 `890ms`，见 [h9-gen1-real-2026-11-19.txt:23](file:///Users/jjjj/Projects/jige-battery/tests/fixtures/h9-gen1-real-2026-11-19.txt#L23)）。多设备叠加下一次轮询很容易拉长到 5–8 秒。
  3. **`EVENT_OBJECT_LOCATIONCHANGE` 跨进程噪声**：[TrayApp.cs:170-199](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L170-L199) 对当前前台线程挂接 `EVENT_OBJECT_LOCATIONCHANGE`（`0x800B`），尽管回调里过滤了 `objectId == 0 && childId == 0`，但 Win32 仍会把前台线程内所有子控件/光标/滚动的 `LOCATIONCHANGE` 事件跨进程派发到托盘消息队列。
- **建议**：引入热插拔失效的设备列表缓存，常规轮询直接复用已验证端点；仅在诊断或未知 `046D` 且 `UsagePage == 0xFF00` 时读 `ProductString`；按 `ReceiverId` 并行执行各接收器查询；改用仅在前台切换后短时校验 + 低频检查替代全局线程级 `LOCATIONCHANGE` Hook（或至少在已确认全屏后取消 `locationHook` 直到前台切换）。

#### P06｜充电中误报“该充电了”与低电量阈值/颜色割裂
- **事实依据**：
  1. [TrayModel.cs:123-129](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TrayModel.cs#L123-L129) 未排除 `device.IsCharging`，用户 12% 插上充电线后，弹窗仍提示“耳机该充电了 · 只剩 12%”。
  2. [Presentation.cs:58-65](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L58-L65) 硬编码 `Percentage <= 15 => BatteryTone.Low`。用户把阈值调到 `30%` 或 `10%` 后，托盘红字、面板“电量低，该充电了”和悬停“电量低”全部与设置不一致。
  3. [TrayApp.cs:633-637](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L633-L637) 在弹窗显示瞬间即调用 `model.SetReminder(noticeId, null)`，若随后因进入游戏（[TrayApp.cs:364-368](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L364-L368)）或用户点开主面板（[TrayApp.cs:611](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L611)）销毁卡片，用户尚未确认的提醒就此丢失。
- **建议**：`UpdateReminder` 在 `device.IsCharging` 时移除/暂缓提醒；`Presentation.Tone(device, threshold)` 接收设置阈值；只有用户显式点击“知道了”或“×”时才标记本轮已读，被游戏打断则恢复 `Due = now` 等待退出后重弹。

#### P07｜Logitech HID++ 2.0 短/长报告 TLC 路由冲突与过度查询
- **事实依据**：
  1. [LogitechHidppAdapter.cs:65,107,149-151](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/LogitechHidppAdapter.cs#L65) 仅打开 `FF00:0002`（20 字节长报告，Report ID `0x11`），却在 [LogitechHidppProtocol.cs:75-76](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/LogitechHidppProtocol.cs#L75-L76) 中期待从该句柄读取 7 字节短错误帧 `10 D 8F ...`。在 Windows `hidclass.sys` 中，Report ID `0x10` 归属 `FF00:0001` TLC，【推测/待实机核实】无法从 `FF00:0002` 句柄读到，导致未配对/离线槽每次必须硬等 200ms 超时（5 个空槽合计白耗 1 秒），且 Lightspeed 离线槽无法通过 `8F 04/09` 识别。单元测试 [LogitechBatteryTests.cs:63-64](file:///Users/jjjj/Projects/jige-battery/tests/Jige.Core.Tests/LogitechBatteryTests.cs#L63-L64) 用桩在长通道直接塞 7 字节帧，掩盖了该平台差异。
  2. [LogitechHidppAdapter.cs:174-205](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/LogitechHidppAdapter.cs#L174-L205) 每 60s 轮询都重查全部 6 个槽并重读 `0005` 设备名、类型和 `1004` 能力，单只鼠标每分钟产生 7–10 次 USB 往返。
  3. [LogitechHidppAdapter.cs:20-23,84-95](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/LogitechHidppAdapter.cs#L84-L95) 对未知 `046D` 接收器在托盘常规轮询中每分钟发包并向面板注入一条永无电量的 `Logitech HID++ 接收器 XXXX` 占位行。

---

### 中严重度

#### P08｜`jige.log` 附入诊断报告存在脱敏缺口
- **依据**：[PRIVACY.md:21](file:///Users/jjjj/Projects/jige-battery/PRIVACY.md#L21) 明确声明不包含“本地路径或原始异常文字”，但 [TrayApp.cs:977-979](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L977-L979) 直接把 `jige.log` 末尾 80 行拼入 `diagnostics.txt`。而 [Desktop.cs:164-174](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Desktop.cs#L164-L174) 只替换 `%USERPROFILE%`，既会记录异常原文（可能含非用户目录路径或系统信息），也在 [TrayApp.cs:685,688](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L685) 记录了第三方前台窗口类名（`Desktop.ForegroundClass()`）。另外 [Desktop.cs:167-168](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Desktop.cs#L167-L168) 在 Release 中仍无条件调用 `OutputDebugStringW`。

#### P09｜首次安装“隐身”且默认不自启
- **依据**：Windows 11 默认将新托盘图标收进 `^` 溢出区。当前 [installer/jige.iss:52-53](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L52-L53) 装完后静默启动 `Jige.exe --after-setup`，既不弹首屏面板，也不在安装向导中提供“开机自启”选项（[TraySettings.cs:15](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TraySettings.cs#L15) 默认 `false`）。新用户装完后看不到任何窗口、也看不到任务栏图标，重启后程序还会“消失”。

#### P10｜历史设备无法删除、换口变僵尸、改名无法还原
- **依据**：[TrayModel.cs:20-26](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TrayModel.cs#L20-L26) 会把 `state.json` 里所有历史设备永久载入内存；无 USB 序列号的接收器换插 USB 口后 [WindowsDeviceDiscovery.cs:218-228](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Windows/WindowsDeviceDiscovery.cs#L218-L228) 会生成新 `ReceiverId`，旧设备只能隐藏不能删除，且隐藏后仍出现在托盘悬停提示中（[Presentation.cs:123](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L123)）。此外 [TraySettings.cs:25-32](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TraySettings.cs#L25-L32) 在空输入时直接返回原设置，导致设备一旦改过名便无法通过界面恢复默认名。

#### P11｜更新体验、CDN 韧性与代码签名风险
- **依据**：
  1. [UpdateDialog.cs:157-198](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/UpdateDialog.cs#L157-L198) 未设置 `TDF_SHOW_PROGRESS_BAR` / `TDF_SHOW_MARQUEE_PROGRESS_BAR`，下载过程无任何进度反馈；遇 GitHub API 403 限流（[UpdateDialog.cs:119](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/UpdateDialog.cs#L119)）也不提供直达 Release 网页的按钮。
  2. [Updates.cs:195](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Updates.cs#L195) 仅放行 3 个固定域名；GitHub Release CDN 历史上有过域名切换，一旦变更将导致存量客户端全部更新失败。
  3. 静默安装器若因旧进程超过 8 秒未退出（[installer/jige.iss:94-123](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L94-L123)）而失败，`/SUPPRESSMSGBOXES` 会吞掉报错且旧托盘已退出，用户无任何提示。
  4. 产物未代码签名，且 `SHA256SUMS.txt` 与 `.exe` 同源存放于 GitHub Release，无独立公钥签名防账号劫持，同时在 Win11 上会频繁触发 SmartScreen 拦阻。

#### P12｜高 DPI 图标工厂重复创建与 15ms 窗口缩放重绘
- **依据**：[TrayIcon.cs:147-205](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayIcon.cs#L147-L205) 在 150%/200% DPI（`size != 16`）下每次刷新图标都在主线程完整创建并销毁 WIC、Direct2D、DirectWrite 工厂；[Graphics.cs:244-270](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Graphics.cs#L244-L270) 每次 `DrawFrame` 都重传 `BrandIcon` 纹理；[PopupWindow.cs:176,189,197,260-266](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupWindow.cs#L260-L266) 在切页和展开设置动画期间每 15ms 重绘 D2D 表面并 `SetWindowPos` 改写 DWM Acrylic 窗口尺寸，【推测】高刷屏上易产生底部边缘微抖。

#### P13｜CI 与发版流水线盲区
- **依据**：
  1. [build.yml](file:///Users/jjjj/Projects/jige-battery/.github/workflows/build.yml) 未编译 `installer/jige.iss`，安装脚本回归只能在发正式版 tag 时暴露。
  2. [release.yml:102](file:///Users/jjjj/Projects/jige-battery/.github/workflows/release.yml#L102) 未上传 `Jige.Probe.exe`，普通用户无 GitHub 账号无法从 Actions 下载探针，与 [README.md:65-80](file:///Users/jjjj/Projects/jige-battery/README.md#L65-L80) 脱节。
  3. [installer/jige.iss:35-39](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L35-L39) 检测 `compiler:Languages\ChineseSimplified.isl`，【推测】Chocolatey 安装的官方 Inno Setup 6 默认不含非官方简中 `.isl`，CI 编出的安装器会退回英文向导 + 中文自定义弹窗的混语状态。
  4. [NativeCheck.cs:11-44](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/NativeCheck.cs#L11-L44) 仅校验 `TrayIcon.Draw`，未覆盖 `Composition`、内嵌资源 `Jige.PanelIcon` 加载与弹窗渲染。

#### P14｜视觉与交互对照 `design/ui-claude.html` 的细节瑕疵
- **依据**：
  1. **品牌不统一**：主面板标题用 FourJ 标志（[PopupScene.cs:309](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L309)），低电量提醒卡片仍画旧电池线框（[PopupScene.cs:441](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L441)）。
  2. **缺少鼠标光标反馈**：[TrayApp.cs:152](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L152) 固定使用 `IDC_ARROW` 且未处理 `WM_SETCURSOR`，悬停链接、按钮、设备名改名区均无手型或文本输入光标（对照 [ui-claude.html:122,154,203](file:///Users/jjjj/Projects/jige-battery/design/ui-claude.html#L203)）。
  3. **时间格式退化**：[Presentation.cs:109-110](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L109-L110) 永远按总分钟输出，离线两天会显示“2880 分钟前”，刚休眠会显示“0 分钟前”。
  4. **文案与数字宽度**：设置页写“60 秒”（[PopupSubPages.cs:94](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupSubPages.cs#L94)），主页底栏却由 `Presentation.Interval(60)` 输出“每 1 分钟查询”（[PopupScene.cs:324](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L324)）；`≈100%` 在 [PopupScene.cs:355,388](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L355) 的固定宽区域内存在溢入电格或裁剪 `%` 的风险。

#### P15｜卸载无确认强制清空用户数据
- **依据**：[installer/jige.iss:55-56](file:///Users/jjjj/Projects/jige-battery/installer/jige.iss#L55-L56) 直接删除 `{localappdata}\Jige`。用户在遇到故障重装或降级回滚时，自定义别名、隐藏配置及待提交的 `diagnostics.txt` 全部被静默清空。

#### P16｜Razer 90B 忙态误报与游狼 `XVI-NEW` 休眠盲区
- **依据**：
  1. [RazerFeatureAdapter.cs:34-39,66-71](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/RazerFeatureAdapter.cs#L34-L39) 发完 `SetFeature` 只等 60ms 读一次，遇到设备无线链路正忙（协议状态 `01`）不重试而直接返 `ReadIssue.Busy`，在面板上显示为误导性的“接收器被占用”（[Presentation.cs:96](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Presentation.cs#L96)）。
  2. [MouseBatteryProtocol.cs:87-89](file:///Users/jjjj/Projects/jige-battery/src/Jige.Core/Adapters/MouseBatteryProtocol.cs#L87-L89) 的 `IsXviAsleep` 仅支持 `XviOld`，已实机验证的游狼 HTX Ultra 8K（`XviNew`）无休眠检测；若接收器返回缓存旧值，面板会持续刷新为“刚刚更新”的假在线状态。

---

### 低严重度

#### P17｜面向多外设玩家的功能与竞品差距
- **依据**：竞技玩家常同时配鼠、耳、键 2–3 套无线外设，但当前托盘仅能固定展示单一主设备（[TrayModel.cs:50](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/TrayModel.cs#L50)），缺少“自动显示最低电量设备”模式；主面板非主设备行悬停高亮却点击无作用（[PopupScene.cs:361-389](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/PopupScene.cs#L361-L389)）；插 USB-C 边充边玩时所有适配器均只认无线接收器 PID，设备会掉成“离线”；电压估算机型的原始 mV 未在界面透出。

#### P18｜`WM_SETTINGCHANGE` 过度重置与 Windows 10 字体回退
- **依据**：[TrayApp.cs:295-298](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/TrayApp.cs#L295-L298) 对所有 `0x001A` 广播无条件 `resetPopup: true` 重建整套图形上下文；[Graphics.cs:81](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/Graphics.cs#L81) 与 [InlineEdit.cs:44](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Windows/InlineEdit.cs#L44) 直接写死 Win11 字体 `Segoe UI Variable`，在最低支持系统 Win10 1809（[Program.cs:12](file:///Users/jjjj/Projects/jige-battery/src/Jige.Tray/Program.cs#L12)）上缺少显式回退至 `Segoe UI`。

#### P19｜测试覆盖缺口
- **依据**：[DiagnosticTests.cs](file:///Users/jjjj/Projects/jige-battery/tests/Jige.Core.Tests/DiagnosticTests.cs) 缺失 Logitech/Razer 脱敏与回放测试（直接放过了 P01）；[PresentationTests.cs](file:///Users/jjjj/Projects/jige-battery/tests/Jige.Tray.Tests/PresentationTests.cs) 仅测默认 15% 阈值（放过了 P06）；`Jige.Tray.Windows` 缺乏端到端 Windows 冒烟自动化。

#### P20｜文档失同步与 GitHub 首页展示力不足
- **依据**：[README.md](file:///Users/jjjj/Projects/jige-battery/README.md) 缺少任何实机截图/GIF，首屏被内部协议表占满且混杂 `0.1.0` 旧示例与开发验收待办；[docs/protocols.md:3,48,56,151](file:///Users/jjjj/Projects/jige-battery/docs/protocols.md#L3) 仍称“全部未在实机验证”且漏列已实机验证的 HTX Ultra `5617`、测试总数仍停留在旧的 101 项；仓库缺少 `.github/ISSUE_TEMPLATE/` 设备报告模板。

---

## 三、最该先做的 5 件事

1. **修好诊断闭环并攻克实机未识别设备（P01 + P03 + P08）**：先补齐 Logitech/Razer 的诊断脱敏与适配器名称、清理 `jige.log` 里的原始异常/窗口类名，再在设备管理页提示“发现未支持的接收器”并给诊断导出加反馈；拿实机报告把用户的 Finalmouse ULX 和 ATK 鼠标尽快跑通并纳入回放测试。
2. **修掉最破坏信任的 3 个交互/提醒 Bug（P02 + P06）**：移除 `ShowMenu` 遗留拦截彻底消灭“右键失灵 + 退出游戏幽灵弹窗”；修正“游戏中不打扰”假开关；让充电中不再弹“该充电了”并使托盘/面板红字跟随用户自定义的低电量阈值。
3. **补齐事件驱动并压榨游戏后台开销（P04 + P05）**：接入 `WM_DEVICECHANGE` 热插拔与 `WM_POWERBROADCAST` 休眠唤醒、调度改单调时钟；缓存设备拓扑且跳过无关集合的 `HidD_GetProductString`、将多接收器改为并行轮询、收敛 `EVENT_OBJECT_LOCATIONCHANGE` 噪声，兑现“竞技游戏零感知”。
4. **补齐首次上手、设备清理与更新体验闭环（P09 + P10 + P11）**：安装器增加开机自启勾选、首次安装后自动弹出面板提示把图标拖出 Win11 溢出区；设备管理页支持删除离线僵尸设备与清空恢复默认名；更新弹窗补齐下载进度条并放宽 GitHub CDN 域名白名单。
5. **加固 CI/发版与刷新 GitHub 首页（P13 + P20）**：让 `build.yml` 编译验证 `jige.iss`、解决简中安装器语言包问题并在 `release.yml` 同发 `Jige.Probe.exe`；给 `README.md` 加上 Win11 实机截图与极简首屏引导、同步 `docs/protocols.md` 实机状态并加设备报告 Issue 模板。

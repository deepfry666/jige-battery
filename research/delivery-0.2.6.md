# 几格 0.2.6 交付与验收记录

验收依据为 [0.2.6 验收标准](acceptance-0.2.6.md)。

## G1–G5

| 项目 | 实现与证据 |
| --- | --- |
| G1 | macOS ARM64 上使用 .NET SDK 10.0.404。 `dotnet build Jige.slnx -p:EnableWindowsTargeting=true` 零警告、零错误。 `dotnet test Jige.slnx` 通过 328 项，核心 143 项、托盘 185 项，失败和跳过均为 0。 |
| G2 | 保留 STA、`--check`、DirectComposition 的 `AddVisual` 参数、稳定 uID、悬停通知和 `szTip`。 `--check` 会创建并释放 16、24、32px 的四种设备图标、未知读数图标和默认图标。对应 [Program.cs](../src/Jige.Tray/Program.cs)、[NativeCheck.cs](../src/Jige.Tray/Windows/NativeCheck.cs)、[Composition.cs](../src/Jige.Tray/Windows/Composition.cs)、[TrayIconManager.cs](../src/Jige.Tray/Windows/TrayIconManager.cs)。 |
| G3 | 本次修改集中在托盘显示、更新流程和游戏诊断。设备适配器、协议和传输层无改动。 |
| G4 | 游戏保护仍拦截自动提醒和引导，并收起悬停卡片。主动打开面板、菜单、立即刷新和确认安装沿用手动入口。对应 [TrayApp.cs](../src/Jige.Tray/Windows/TrayApp.cs)、[PollScheduleTests.cs](../tests/Jige.Tray.Tests/PollScheduleTests.cs)、[UpdateTests.cs](../tests/Jige.Tray.Tests/UpdateTests.cs)。 |
| G5 | 项目和安装器版本均为 0.2.6，已同步 [CHANGELOG](../CHANGELOG.md) 和 [README](../README.md)。 |

## 托盘图标

| 项目 | 实现与位置 |
| --- | --- |
| I1 | 两种模式共用方案 B。16px 的设备图形高 11–12px，底部电量条高 2px。24px 和 32px 的条高分别为 3px 和 4px。对应 [TrayIconLayout.cs](../src/Jige.Tray/TrayIconLayout.cs)、[TrayIcon.cs](../src/Jige.Tray/Windows/TrayIcon.cs)。 |
| I2 | 重画鼠标、键盘、耳机和通用设备。16px 使用对齐整像素的手工图案，高 DPI 使用 Direct2D 矢量。设备图形使用任务栏前景色。对应 [PixelTrayIcon.cs](../src/Jige.Tray/PixelTrayIcon.cs)、[Graphics.cs](../src/Jige.Tray/Windows/Graphics.cs)。 |
| I3 | 淡色底槽随电量填充，正常使用任务栏前景色，偏低用琥珀色，过低用红色，充电和已充满用绿色。休眠沿用最近电量，保持按电量着色。无可用读数时显示设备图形和底槽。对应 [Presentation.cs](../src/Jige.Tray/Presentation.cs)、[TrayIconLayout.cs](../src/Jige.Tray/TrayIconLayout.cs)。 |
| I4 | 已移除托盘图标的数字绘制。托盘电量百分比通过原有悬停卡片显示，`szTip` 继续供读屏软件使用。对应 [TrayIcon.cs](../src/Jige.Tray/Windows/TrayIcon.cs)、[HoverCardModel.cs](../src/Jige.Tray/HoverCardModel.cs)、[TrayApp.cs](../src/Jige.Tray/Windows/TrayApp.cs)。 |
| I5 | 无活跃设备时显示原有的「几格」短横线图标。对应 [PixelTrayIcon.cs](../src/Jige.Tray/PixelTrayIcon.cs)、[TrayIcon.cs](../src/Jige.Tray/Windows/TrayIcon.cs)。 |
| I6 | 原型的模拟托盘和图标样例已同步。Chrome 渲染核对了深浅主题和 16、24、32px 样例，原型交互检查覆盖模式切换、悬停和模拟游戏中的悬停抑制。对应 [ui-claude.html](../design/ui-claude.html)。 |
| I7 | 测试覆盖四种设备和 8–256px 支持范围的布局边界、0%、1%、50%、99%、100% 及状态颜色。填充宽度四舍五入到整像素，恰好半像素向上取整。对应 [TrayV026Tests.cs](../tests/Jige.Tray.Tests/TrayV026Tests.cs)、[PresentationTests.cs](../tests/Jige.Tray.Tests/PresentationTests.cs)。 |

## 更新安装

| 项目 | 实现与位置 |
| --- | --- |
| U1 | 移除确认安装后、下载前和启动安装器前的两处游戏拦截。校验、取消和清理仍由原流程处理。对应 [UpdateDialog.cs](../src/Jige.Tray/Windows/UpdateDialog.cs)、[Updates.cs](../src/Jige.Tray/Updates.cs)。 |
| U2 | 更新检查由用户打开更新对话框触发。只有用户确认后才下载并安装。对应 [TrayApp.cs](../src/Jige.Tray/Windows/TrayApp.cs)、[UpdateDialog.cs](../src/Jige.Tray/Windows/UpdateDialog.cs)。 |
| U3 | 用本地模拟的 Release 响应执行正式下载与 SHA-256 校验流程。测试覆盖通过校验后调用模拟安装器、损坏文件拒绝安装和关闭后清理。安装流程没有游戏状态输入。对应 [UpdateTests.cs](../tests/Jige.Tray.Tests/UpdateTests.cs)。 |

## 游戏判定与诊断

| 项目 | 实现与位置 |
| --- | --- |
| M1 | 状态 3 和 4 直接启用游戏保护。状态 2 需要可见的普通前台窗口覆盖所在显示器，系统、桌面、几格自身、工具窗口和不可激活窗口均排除。其他状态按原有「游戏中不打扰」开关应用窗口几何和进程匹配。同一前台窗口进出全屏也会重新判定。对应 [PollSchedule.cs](../src/Jige.Tray/PollSchedule.cs)、[Desktop.cs](../src/Jige.Tray/Windows/Desktop.cs)、[TrayApp.cs](../src/Jige.Tray/Windows/TrayApp.cs)。 |
| M2 | 进入游戏模式时记录通知状态数值和原因类别，包括 D3D 全屏、演示模式、BUSY 且前台全屏、前台几何、进程名匹配。日志仅输出类别和数值。对应 [TrayLog.cs](../src/Jige.Tray/TrayLog.cs)、[DiagnosticReport.cs](../src/Jige.Core/Diagnostics/DiagnosticReport.cs)。 |
| M3 | 发起诊断时读取当前判定，在「当前设置摘要」下输出游戏状态、通知状态数值及原因类别。对应 [TrayApp.cs](../src/Jige.Tray/Windows/TrayApp.cs)、[DiagnosticReport.cs](../src/Jige.Core/Diagnostics/DiagnosticReport.cs)。 |
| M4 | 测试覆盖 BUSY 前台全屏及非全屏、状态 3 和 4、开关两种取值，以及物理显示器边界、工作区边界和窗口排除。另检查日志去重和报告输出。对应 [PollScheduleTests.cs](../tests/Jige.Tray.Tests/PollScheduleTests.cs)、[TrayV022Tests.cs](../tests/Jige.Tray.Tests/TrayV022Tests.cs)、[GenericAndPresenceTests.cs](../tests/Jige.Core.Tests/GenericAndPresenceTests.cs)。 |

## 公司电脑实机确认清单

本次验收覆盖托管构建、单元测试和浏览器原型。下列 Windows 11 和 Bolt 硬件项目待确认。测试设备为经 Bolt 接收器连接的 MX Anywhere 3S 和 MX Keys S。

1. **Windows 产物。** 构建 Windows x64 NativeAOT 和安装器，核对版本 0.2.6。运行 `Jige.exe --check`，确认退出码为 0。
2. **托盘、主题和缩放。** 分别用 100%、150%、200% 缩放以及深浅任务栏，检查自定义模式的鼠标与键盘图形和电量条。切到最低电量模式，确认图标和悬停列表对应正确。若有可用的耳机或通用设备，再补查对应图形。
3. **休眠、恢复和读屏。** 让两台设备休眠再唤醒，确认上次读数的电量条可辨。拔出并插回 Bolt，核对默认图标及重连后原托盘位置。检查悬停百分比、设备名称、读屏文字和报读顺序。MX Keys S 若持续显示休眠，记录现象供独立任务处理。
4. **游戏判定。** 分别关闭和开启「游戏中不打扰」。普通窗口在前台时打开置顶覆盖层或 PixPin 选区，再让同一个普通应用窗口进入和退出全屏。分别验证 D3D 独占全屏与演示模式，检查自动提醒和悬停抑制、手动操作、轮询频率、进入游戏日志和诊断摘要。检查原因文本仅含类别和通知状态数值。
5. **主动更新。** 用已安装的测试版本和可用的新版本安装包，分别在桌面和确认进入游戏保护时点「下载并安装」。核对下载、校验、启动安装器、升级后重启以及配置与设备缓存保留。检查取消和校验失败时的下载清理。

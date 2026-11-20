# 0.2.10 任务栏直读条交付验收

## T1–T13

| 项目 | 实现与证据 |
| --- | --- |
| T1 | 设备横排。沿用方案一的 11×11 设备像素图、5×7 闪电和 6×9 字库，补通用设备图及短横线。每段之间留 4px，中间画 1px 分隔线。[布局](../src/Jige.Tray/TaskbarStrip.cs) · [像素图](../src/Jige.Tray/TaskbarStripPixels.cs) |
| T2 | 合成表面以全透明像素清空，仅写图标、数字、闪电及分隔线。测试逐像素检查留白，低电量的段宽与正常相同。[像素渲染](../src/Jige.Tray/TaskbarStripPixels.cs) · [测试](../tests/Jige.Tray.Tests/TaskbarStripTests.cs) |
| T3 | 复用 `Presentation.Tone` 和用户阈值，采用方案一的正常、低电与充电配色及现有警告色。休眠保留读数和颜色，闪电反映当前充电或满电，缺电量显示前景色 `--`。画出的像素全部不透明。[状态与配色](../src/Jige.Tray/TaskbarStrip.cs) · [上次读数](../src/Jige.Tray/TrayModel.cs) |
| T4 | 读取系统任务栏的 `SystemUsesLightTheme`。主题消息到达时颜色立即刷新，面板改名仍可继续。[主题读取](../src/Jige.Tray/Windows/Desktop.cs) · [消息处理](../src/Jige.Tray/Windows/TrayApp.cs) |
| T5 | 按最接近的整数倍复制像素，正好半倍时向上取整。100%、150%、200% 分别为 1×、2×、2×，数字有效高度为 9、18、18 个物理像素。Direct2D 以最近邻方式上传实际尺寸的位图，保留粗体笔画和清晰边缘。[缩放与布局](../src/Jige.Tray/TaskbarStrip.cs) · [上传位图](../src/Jige.Tray/Windows/Graphics.cs) |
| T6 | 自定义模式沿用托盘勾选，活跃设备按主面板电量顺序排列。最低电量模式复用原托盘选择规则。两种模式均移除断开设备。[设备选择](../src/Jige.Tray/TaskbarStrip.cs) |
| T7 | 找到主任务栏及通知区域，按按钮边界检测空位。任务栏大小、位置、显示状态和 UI Automation 结构变化走事件刷新，Explorer 重启时重新绑定。全屏通过现有游戏判定及前台几何隐藏。定位和回退经过纯函数测试，窗口行为待 Windows 11 实机确认。[探测](../src/Jige.Tray/Windows/TaskbarStripShell.cs) · [事件](../src/Jige.Tray/Windows/TaskbarStripAutomation.cs) · [窗口](../src/Jige.Tray/Windows/TaskbarStripWindow.cs) |
| T8 | 直读条范围处理左右键和系统悬停消息，复用面板、Fluent 菜单及最低电量模式的列表卡片。[交互入口](../src/Jige.Tray/Windows/TrayApp.TaskbarStrip.cs) · [悬停内容](../src/Jige.Tray/HoverCardModel.cs) |
| T9 | 显示直读条时，托盘保留一个既有「几格」默认槽位。回退及手动切换恢复 0.2.9 水位图标，图标身份继续由 `TrayIconPlan.StableId` 决定。[托盘槽位](../src/Jige.Tray/Windows/TrayIconManager.cs) |
| T10 | 新增「显示位置」，支持「任务栏（推荐）」和「托盘图标」。旧配置默认为任务栏，设置选择沿用即时保存与刷新流程。[配置](../src/Jige.Tray/TraySettings.cs) · [设置选项](../src/Jige.Tray/Windows/SettingsPanel.cs) |
| T11 | 丢失任务栏或通知区、按钮边界不明、空间不足及非底边等情况暂时使用托盘图标，定位恢复后自动显示直读条。日志仅记录回退分类，诊断摘要显示实际位置及原因。[回退决策](../src/Jige.Tray/TaskbarStrip.cs) · [诊断摘要](../src/Jige.Core/Diagnostics/DiagnosticReport.cs) |
| T12 | 覆盖 1–3 台布局、不同读数宽度、颜色透明度和缩放、按钮和通知区边界、放不下和恢复，以及旧配置加载、设置保存、诊断导出。[直读条测试](../tests/Jige.Tray.Tests/TaskbarStripTests.cs) · [诊断测试](../tests/Jige.Core.Tests/DiagnosticTests.cs) |
| T13 | 通过 `TrayIconPreview` 调用生产像素渲染器导出四张模拟任务栏背景 PNG。100% 为 185×48，200% 为 370×96，四张均已目视核对。[100% 深色](../design/previews/0.2.10/taskbar-strip-100-dark.png) · [100% 浅色](../design/previews/0.2.10/taskbar-strip-100-light.png) · [200% 深色](../design/previews/0.2.10/taskbar-strip-200-dark.png) · [200% 浅色](../design/previews/0.2.10/taskbar-strip-200-light.png) |

## T7 窗口选择

几格通过 `CreateWindowEx` 创建由 `Shell_TrayWnd` 拥有的无激活工具窗，DirectComposition 提供透明表面。所有者关系为叠放在任务栏上方提供系统的 z 序约束，并绑定 Explorer 重启时的窗口生命周期。窗口位置保持在已确认的任务栏空位内，使用 `SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER` 显示和移动。选择独立窗口以保留几格现有的 Per-Monitor V2 DPI 环境。跨进程所有者关系会关联输入队列，Explorer 恢复和菜单焦点列为实机检查项。[Win32 所有者窗口](https://learn.microsoft.com/windows/win32/winmsg/window-features) · [跨进程所有者关系](https://devblogs.microsoft.com/oldnewthing/20130412-00/?p=4683%2F) · [SetParent 的 DPI 行为](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setparent)

`FindWindow` 找主任务栏，`FindWindowEx` 的 `TrayNotifyWnd` 与 UI Automation 可访问元素提供通知区边界。可访问元素的按钮矩形确定已占用范围，遇到无法确认的 Windows 任务栏结构时按原因降级。Shell appbar 注册仅接收通知，系统工作区保持原布局。UI Automation 在无窗口的 MTA 线程上订阅结构和边界属性变化。几格在电量、颜色或像素倍率改变时更新直读条图像，位置变化只移动窗口。[Appbar 通知](https://learn.microsoft.com/windows/win32/shell/application-desktop-toolbars) · [UI Automation 事件](https://learn.microsoft.com/windows/win32/winauto/uiauto-eventsforclients) · [UI Automation 线程模型](https://learn.microsoft.com/windows/win32/winauto/uiauto-threading)

## G1–G6

- G1：macOS 运行 `dotnet build Jige.slnx -p:EnableWindowsTargeting=true`，零警告、零错误。`dotnet test Jige.slnx` 的 419 项全部通过。
- G2：核对 `[STAThread]`、`--check`、`AddVisual(v, false, null)`、稳定 uID、现有菜单和悬停视图、更新页面和安装器调用路径。
- G3：直读条消费几格已有读数，设备协议和查询路径维持原有只读规则。
- G4：通过现有前台/全屏检测和 Shell 通知隐藏直读条。直读条绘制由内容变化驱动，窗口使用无激活样式。
- G5：版本为 0.2.10，已同步 [CHANGELOG](../CHANGELOG.md) 和 [README](../README.md)。
- G6：通过公开 Win32、DirectComposition 和 UI Automation API 使用几格自身的窗口，新增代码限于读取 Shell 边界、订阅事件及呈现几格 UI。

## Windows 11 实机待验

- 核对居中与左对齐任务栏，确认通知区左侧对齐、垂直居中、上箭头和应用按钮都可操作。填满与腾出应用区域，检查自动回退和恢复。
- 打开开始菜单、通知中心及其他 Shell 弹层，检查叠放、焦点与任务栏其他区域的右键和拖拽。验证直读条左键、右键、列表悬停。
- 开关任务栏自动隐藏，检查反复滑入滑出时直读条的显示、隐藏和位置。
- 重启 Explorer，测试原生托盘入口及直读条重建。切换分辨率、主显示器、任务栏大小或底边之外的布局。
- 检查独占全屏、无边框全屏及退出后的恢复，包括关闭「游戏中不打扰」时全屏仍隐藏。
- 在 100%、150%、200% 缩放及深浅任务栏检查像素字与时钟的视觉大小，并实测切换主题。
- 在 Windows 上执行 NativeAOT 发布与启动，核对 UI Automation 提供者的数据、通知到达情况及诊断回退分类。

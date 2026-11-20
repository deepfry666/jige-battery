# 几格 0.2.4 交付与验收记录

## G1–G8

| 项目 | 结果与依据 |
| --- | --- |
| G1 构建与测试 | 在 macOS ARM64 使用 .NET 10 构建。 `dotnet build Jige.slnx -p:EnableWindowsTargeting=true` 零警告、零错误。 `dotnet test Jige.slnx` 通过 272 项，核心 141 项、托盘 131 项。 |
| G2 入口与托盘 | [Program.cs](../src/Jige.Tray/Program.cs) 保留 STA 入口和 `--check` 分支。[Composition.cs](../src/Jige.Tray/Windows/Composition.cs) 的 `AddVisual` 保留 `false, null` 参数，[TrayIconManager.cs](../src/Jige.Tray/Windows/TrayIconManager.cs) 继续用 `TrayIconPlan.StableId` 推导 uID。Windows 原生 `--check` 和托盘启动待实机。 |
| G3 只读 | [LogitechHidppProtocol.cs](../src/Jige.Core/Adapters/LogitechHidppProtocol.cs) 限制 Root、功能读取和 `81` 寄存器读。回放测试覆盖对 `80/82/83`、设置函数和未知功能的拒绝。原有 Centurion 和 ASTRO 固定查询保持其专用白名单。 |
| G4 网络与隐私 | 蓝牙属性使用本机 SetupAPI。既有更新入口继续由用户发起，新增代码无网络调用。名称、地址和日志处理见 [隐私说明](../PRIVACY.md)。 |
| G5 磁盘与缓存 | [TrayLog.cs](../src/Jige.Tray/TrayLog.cs) 的两个 1 MB 轮换文件通过回归测试。新增功能及槽位缓存只在内存中保存。诊断导出仍替换既有报告。 |
| G6 游戏窗口 | [TrayApp.cs](../src/Jige.Tray/Windows/TrayApp.cs) 的自动窗口经游戏保护，用户打开面板、设置及诊断入口沿用现有行为。Windows 11 全屏游戏中的实际表现待实机。 |
| G7 版本 | [Directory.Build.props](../Directory.Build.props) 与 [jige.iss](../installer/jige.iss) 均为 0.2.4，README 与中文更新记录已同步。Windows NativeAOT 和 Inno Setup 安装包待 Windows 构建。 |
| G8 验证等级 | 本次覆盖托管构建、假 HID 回放和托盘模型。罗技设备为「仅按资料」，新增协议未作实机声明。 |

## L1–L10

| 项目 | 实现、范围和位置 |
| --- | --- |
| L1 接收器 | 厂商集合和 PID 匹配 Bolt、Unifying、Nano、LIGHTSPEED。名称匹配的未知接收器先 Ping 经安全门，随后和已知接收器一样逐槽查 1–6。未回包又无历史记录的槽不会生成设备。见 [DeviceCatalog.cs](../src/Jige.Core/DeviceCatalog.cs)、[LogitechHidppAdapter.cs](../src/Jige.Core/Adapters/LogitechHidppAdapter.cs)。 |
| L2 HID++ 1.0 | Ping 明确报 `01` 才使用短集合读 `0D/07`。档位读数带 `≈`，2.0 设备不走寄存器后备。完整 B5 配对表在参考实现中需要 `83`，超出本版白名单，因此不再主动读表。见 [LogitechHidppProtocol.cs](../src/Jige.Core/Adapters/LogitechHidppProtocol.cs)。 |
| L3 线缆 | 同一设备 PID 的一对长短 HID++ 集合按 `FF` 读取。识别到同一 Unit ID 摘要时，线缆电量优先，同时保留原来的托盘 ID、名称别名和历史。缺失 Unit ID 时保留各连接，避免同型号设备误合并。见 [LogitechHidppAdapter.cs](../src/Jige.Core/Adapters/LogitechHidppAdapter.cs)、[TrayModel.cs](../src/Jige.Tray/TrayModel.cs)。 |
| L4 蓝牙 | `FF43:0202` 按 `FF` 读取，Windows BYTE 电量属性覆盖其他品牌。识别经典蓝牙与 BLE 的 Windows 路径，按容器或地址分别合并每台外设。采用 HID++ 电量和 `0005` 名称，缺失时使用 Windows 电量或名称。来源切换和推送复用同一设备身份。默认显示开关已接入托盘、主面板、设备管理与后台轮询。Windows 蓝牙硬件、驱动是否暴露 HID++ 和提供电量仍待实机。见 [WindowsBluetoothDiscovery.cs](../src/Jige.Core/Windows/WindowsBluetoothDiscovery.cs)、[BluetoothBattery.cs](../src/Jige.Core/BluetoothBattery.cs)、[DevicePoll.cs](../src/Jige.Tray/Windows/DevicePoll.cs)、[SettingsPanel.cs](../src/Jige.Tray/Windows/SettingsPanel.cs)。 |
| L5 耳机 | 登记型号经 `FF43:0202` 或 G535 的 `000C:0001`，优先 `1004`，再查 `1F20` 并显示估算电量和充电状态。Centurion 专用路径保留，PRO X 3 禁用。只响应非白名单旧命令的耳机固件暂未适配。见 [LogitechHidppAdapter.cs](../src/Jige.Core/Adapters/LogitechHidppAdapter.cs)、[LogitechHeadsetAdapter.cs](../src/Jige.Core/Adapters/LogitechHeadsetAdapter.cs)、[DeviceCatalog.cs](../src/Jige.Core/DeviceCatalog.cs)。 |
| L6 缓存 | 结合接收器、槽位与 Unit ID 摘要复用已发现的电量读法。没有 Unit ID 时使用已发现的设备身份。第二轮对已取得读数的设备只发电量命令。断开、重连或接收器移除后重新发现。长短集合同时接收，并取消输掉竞争的读取。见 [LogitechHidppAdapter.cs](../src/Jige.Core/Adapters/LogitechHidppAdapter.cs)、[回放测试](../tests/Jige.Core.Tests/LogitechBatteryTests.cs)。 |
| L7 推送 | 被动监听 `1004/1000` 电量事件，校验已发现的地址和索引，更新对应读数，无需写入通知设置或追加电量查询。实机是否主动广播由设备状态和固件决定。见 [LogitechHidppAdapter.cs](../src/Jige.Core/Adapters/LogitechHidppAdapter.cs)、[TrayModel.cs](../src/Jige.Tray/TrayModel.cs)。 |
| L8 在线语义 | 静默保留上次读数和颜色。接收器 `8F` 错误的 `04/09`、断开推送、接收器拔出或 Windows 明确断开进入离线。HID++ 2.0 功能错误和蓝牙 HID 句柄暂时不可用可走 Windows 电量回退。休眠保持 10 分钟规则，用户新输入触发限频补查。见 [TrayModel.cs](../src/Jige.Tray/TrayModel.cs)、[DevicePoll.cs](../src/Jige.Tray/Windows/DevicePoll.cs)、[托盘模型测试](../tests/Jige.Tray.Tests/LogitechTrayTests.cs)。 |
| L9 诊断 | `罗技\|H…` 记录连接、版本、已发现功能索引和白名单回复摘要。 `蓝牙电量来源\|B…` 记录合并后来源。报告和日志屏蔽地址、原始 Unit ID、名称中的私人内容、序列号、实例 ID 和路径。见 [DiagnosticReport.cs](../src/Jige.Core/Diagnostics/DiagnosticReport.cs)、[DiagnosticPrivacy.cs](../src/Jige.Core/Diagnostics/DiagnosticPrivacy.cs)、[回放测试](../tests/Jige.Core.Tests/LogitechV024Tests.cs)。 |
| L10 文档 | [设备读取协议](../docs/protocols.md)、[设备支持清单](../docs/devices.md)、[README](../README.md) 和 [致谢](../docs/credits.md) 已更新。标准 HID 电量兜底留待下一版。 |

## Claude 复核后修正

Claude 对首个实现提交独立复核，完整编译零警告、零错误，运行了当时的 260 项测试。针对复核发现，本地回放补测了 HID++ 2.0 功能错误和忙状态、两种蓝牙路径、Win32 31/1167、Windows 设备类别、保持型号身份及按容器合并的推送。Windows 驱动如何公开集合、连接属性及休眠错误码，仍需要实机确认。

## Windows 11 实机清单

1. **MX Anywhere 3S 与罗技蓝牙键盘同时连接。** 在 Windows「设置 → 蓝牙和设备」确认两台均已连接。打开几格「管理设备」，确认各有一条，连接均为「蓝牙」。名称优先显示 HID++ 读到的型号名称，缺失时应与 Windows 显示名称一致。关闭任意一台、重新开启或唤醒，确认另一台的设备名称、电量和状态不随之改变。
2. **分别确认蓝牙读数实际来源。** 唤醒两台外设，在「管理设备 → 导出诊断信息」导出报告。搜索 `蓝牙电量来源|B`，按名称或类型识别两行。出现「蓝牙 HID++」的行应包含 `HID++` 版本、功能索引和电量功能；再匹配相应的 `罗技|H` 行，检查 `HID|H` 集合为 `FF43:0202`，相关 `OP|H` 请求头为 `11FF`。显示「蓝牙属性（由 Windows 提供）」时，核对电量与 Windows 设置一致。每台都单独记录实际来源和读数。
3. **检查显示与状态。** 关闭「显示蓝牙设备」应立即隐藏两台，重新开启后各显示一条，重启确认开关会保存。运行期间让设备安静一段时间、移动鼠标、敲键，再主动关闭其中一台，核对休眠保留读数和颜色、明确断开后变灰、重连恢复。对可通过 HID++ 查询的设备插线充电时检查充电提示。
4. **检查 Windows 交付。** 在 Windows 构建 x64 NativeAOT 程序及 Inno Setup 安装包，确认版本 0.2.4，运行 `Jige.exe --check`，安装启动后确认托盘图标和本机诊断。全屏游戏中观察自动提醒不出现，手动点击仍可打开面板。在相同状态连续查询，检查无重复日志或状态写盘增长。
5. **其余硬件由具备设备的测试人员补验。** 使用接收器、多槽 HID++ 1.0、可双路连接的 USB 线设备及 HID++ 耳机分别检查拔插、短错误、多槽、充电、推送和估算。用户目前的蓝牙鼠标与键盘无需接收器即可完成前四项相关检查。

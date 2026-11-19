# 几格 / Jige Battery

几格是用托盘数字显示无线外设电量的 Windows 工具。当前阶段已经实现 win-x64 NativeAOT 托盘、设备面板和低电量提醒，并保留一次性命令行探针。下表设备均经 2.4 GHz USB 接收器查询，协议和托盘交互都待 Windows 实机验收。

| 支持的设备 | 接收器与识别方式 | 证据等级 | 实机验证状态 |
| --- | --- | --- | --- |
| Finalmouse UltralightX（ULX） | 2.4 GHz 接收器 `361D:0100`，厂商 HID 集合 `FF00:0001` | 单一来源 | 未在实机验证 |
| Sony INZONE H9 一代 | PC 档接收器 `054C:0E53` 的 `MI_06` USB 串口 | 多方开源实现一致 | 未在实机验证 |
| ATK Compx-OUT | `373B:101A/1031/11D9/1278`，`FF02:0002`；分别对应 ATK 1K 接收器、F1 Ultimate、A9 Ultimate、A9 Air+ | A9 Ultimate 多方开源实现一致，其余单一来源 | 未在实机验证 |
| ATK BitMouse | ATK ZERO `373B:1155`，`FF05:0001` | 多方开源实现一致 | 未在实机验证 |
| 游狼 XVI-OLD | HTX Mini 8K `33E4:2717`，`MI_02 / FFFF:0000` | 单一来源 | 未在实机验证 |
| 游狼 XVI-NEW | Fenrir Pro 8K `33E4:3617`，`MI_02 / FFFF:0000` | 单一来源 | 未在实机验证 |
| ATK Compx-FEAT、游狼 Receiver RS | A9 Ultra `373B:111A` 和 Receiver RS `33E4:3854` 已登记，待补精确 Usage | A9 Ultra 单一来源；Receiver RS 多方开源实现一致，使用项未核实 | 未在实机验证 |

其余候选型号保留在接收器表与[设备清单](research/device-catalog.md)，默认只用于诊断识别。

## 只读声明

托盘和探针共用核心库的只读适配器。适配器只发送白名单内的电量与状态查询，覆盖链路、充电、电压及 H9 的型号信息。程序使用现有 Windows 驱动，查询结束后释放设备句柄。与官方驱动共存的表现待实机验证。

协议来自公开的社区逆向资料和网页驱动的公开行为，仅用于互通和显示电量。几格与各厂商无关联，也未获厂商授权。项目采用 [MIT 许可证](LICENSE)。

## 使用前提

几格面向平时关闭官方驱动界面和后台进程、按需调节设置的玩家。官方软件正在运行时，可能抢占接收器。INZONE Hub 尤其会独占 H9 的串口，遇到占用时，两种入口都会结束该设备的本轮读取。托盘保留上次成功读数和时间。Windows 有时用同一个错误码表示独占和权限不足，探针可显示详细报错。

## 使用托盘

在 Windows 11 x64 解压 `jige-tray-win-x64`，双击 `Jige.Tray.exe`。图标若折叠在任务栏的上箭头内，可拖到通知区域。悬停查看简表，单击打开设备面板，按 Esc 或点面板外收起。右键可换主设备、刷新、设为开机自启、查看诊断信息或退出。「诊断信息…」会在本机生成一份脱敏报告并用记事本完整打开，由你检查后自行复制发送，几格不会联网或上传，详见 [PRIVACY.md](PRIVACY.md)。面板中的设置和管理设备同样打开这个菜单。面板中的 `≈` 表示电压估算。

默认每 60 秒在后台查询一次。Windows 报告忙碌、D3D 全屏或演示，或前台窗口铺满显示器时，查询间隔改为 5 分钟。游戏中提交的刷新请求会等到退出游戏，面板、菜单和低电量提醒也会推迟。面板收起后会释放窗口和图形资源。

设备第一次读到电量等于或低于阈值时会进入提醒队列，默认 15%。每轮低电量提醒一次，“1 小时后提醒”可明确延后，本轮电量回升至阈值以上后才能触发新一轮提醒。

### 配置与缓存

- `%LOCALAPPDATA%\Jige\config.json` 保存 `primaryDeviceId`、`lowBatteryThreshold`、`pollSeconds`、`gamePollSeconds`、`gameSilent`、`startWithWindows` 和 `deviceAliases`。查询间隔以秒为单位，可设为 15–86400 秒，游戏间隔至少等于平时间隔。阈值范围为 0–100。
- `gameSilent` 默认为 `true`，将 Windows 新系统首次使用静默期（`QUNS_QUIET_TIME`）也纳入弹窗推迟规则。游戏和演示状态始终启用静默和查询退避。
- `%LOCALAPPDATA%\Jige\state.json` 保存设备最近一次成功读数、时间和提醒状态。设备别名按该文件中 `readings` 的键配置，例如 `"deviceAliases": { "ulx:接收器标识": "比赛鼠标" }`。
- 右键选“编辑配置文件”，保存后选择“重新加载配置”。开机自启使用当前用户的 Windows Run 注册表项，程序移动后可从新位置启动，更新启用时登记的路径。

## 使用探针

在 Windows 11 的 PowerShell 中进入解压后的探针目录。H9 接收器须拨到 PC 档。

```powershell
.\Jige.Probe.exe list
.\Jige.Probe.exe read
.\Jige.Probe.exe read --raw
.\Jige.Probe.exe read --raw --sony-baud 460800
.\Jige.Probe.exe read --raw --sony-baud both
.\Jige.Probe.exe diag
```

- `list` 列出系统 HID 接口的描述符信息和 USB 串口的 COM 号、硬件 ID。读不到的字段显示占位符。
- `read` 对识别出的设备各查询一次。ULX 的估算值会标记“电压估算”。H9 默认使用 115200，也可独立选 460800。`both` 会分别尝试两个波特率，每次单独打开并关闭串口。遇到占用、权限错误或设备拔除时结束尝试。
- `--raw` 输出十六进制收发帧和串口残片。按 `Ctrl+C` 可取消。

## 设备诊断与隐私

主动运行 `diag` 会查询受支持的接收器，并把完整的脱敏报告显示在终端。先检查报告，再决定是否将它与设备型号、连接方式和官方读数一起粘贴到设备报告 Issue。诊断在本地执行。收集字段、脱敏方式和示例见 [PRIVACY.md](PRIVACY.md)。

## 构建与测试

在仓库根目录用 .NET 10 运行以下命令。macOS 可编译托管项目并运行协议、模拟传输、托盘逻辑与配置恢复测试。设备访问、窗口和原生发布在 Windows 上运行。

```sh
dotnet build -p:EnableWindowsTargeting=true
dotnet test
```

Windows CI 会还原依赖、执行测试、发布两个 win-x64 NativeAOT 程序并检查原生启动。CI 将探针与托盘分别上传为 `jige-probe-win-x64` 和 `jige-tray-win-x64`。托盘启动检查会生成并释放三种尺寸的原生图标。本地 Windows 发布需要安装 Visual Studio 的 C++ 桌面开发工具链。

```powershell
dotnet publish src/Jige.Probe/Jige.Probe.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/win-x64
dotnet publish src/Jige.Tray/Jige.Tray.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/tray-win-x64
```

## Windows 实机验收待办

macOS arm64 上使用 .NET 10 完成常规构建、win-x64 托管编译以及 57 项离线测试。Windows NativeAOT 发布、原生启动检查与以下实机项目仍待 Windows 环境验证。

- 核对托盘面板与提醒卡片的 DWM 亚克力、圆角、浅深色和纯色回退。
- 核对 100%、150%、200% DPI 图标清晰度、浅深色切换和混合 DPI 多屏定位。
- 测试任务栏四个方向、Explorer 重启、单实例、失焦、Esc 和开机自启。
- 检查展开、收起、逐行进场、充电呼吸的流畅度及系统关闭动画时的行为。
- 验证独占全屏、无边框全屏和退出全屏时的查询退避、静默与提醒恢复。
- 测量面板关闭、展开和长时间轮询的 CPU、内存和 GDI 句柄占用。
- 验证两台外设的实际读数、休眠、拔插、占用和官方驱动共存。

## 致谢与参考

- Finalmouse XPanel、官方 USB 权限清单以及 HaloBattery 的 ULX 报文与实机记录。[XPanel](https://xpanel.finalmouse.com/) · [USB 权限清单](https://github.com/teamfinalmouse/xpanel-linux-permissions) · [HaloBattery](https://github.com/HeyOkay/HaloBattery)
- InZoneH9Tray、openinzone 与 inzone-h9-mac 提供 H9 串口查询、帧格式及实机记录。[InZoneH9Tray](https://github.com/janit88/InZoneH9Tray) · [openinzone](https://github.com/penguinwokrs/openinzone) · [inzone-h9-mac](https://github.com/jpcarranza94/inzone-h9-mac)

协议字段、出处及各字段的验证状态见 [协议记录](docs/protocols.md)。

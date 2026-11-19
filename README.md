<p align="center"><img src="design/brand/fourj-mark.svg" width="96" alt="几格 FourJ 家族标志"></p>

# 几格 / Jige Battery

几格是用托盘数字显示无线外设电量的 Windows 工具。当前阶段已经实现 win-x64 NativeAOT 托盘、设备面板和低电量提醒，并保留一次性命令行探针。下表设备均经 2.4 GHz USB 接收器查询，协议和托盘交互都待 Windows 实机验收。

| 支持的设备 | 接收器与识别方式 | 证据等级 | 实机验证状态 |
| --- | --- | --- | --- |
| Finalmouse UltralightX（ULX） | 2.4 GHz 接收器 `361D:0100`，厂商 HID 集合 `FF00:0001` | 单一来源 | 未在实机验证 |
| Sony INZONE H9 一代 | PC 档接收器 `054C:0E53` 的 `MI_06` USB 串口 | 多方开源实现一致 | **已实机验证**（Windows 11，2026-11-19） |
| ATK Compx-OUT | `373B:101A/1031/11D9/1278`，`FF02:0002`；分别对应 ATK 1K 接收器、F1 Ultimate、A9 Ultimate、A9 Air+ | A9 Ultimate 多方开源实现一致，其余单一来源 | 未在实机验证 |
| ATK BitMouse | ATK ZERO `373B:1155`，`FF05:0001` | 多方开源实现一致 | 未在实机验证 |
| 游狼 XVI-OLD | HTX Mini 8K `33E4:2717`，`MI_02 / FFFF:0000` | 单一来源 | 未在实机验证 |
| 游狼 XVI-NEW | Fenrir Pro 8K `33E4:3617`、HTX Ultra 8K `33E4:5617`，`MI_02 / FFFF:0000` | 单一来源；HTX Ultra 依据官方网页驱动型号表 | HTX Ultra **已实机验证**（Windows 11，2026-11-19）；其余未在实机验证 |
| ATK Compx-FEAT、游狼 Receiver RS | A9 Ultra `373B:111A` 和 Receiver RS `33E4:3854` 已登记，待补精确 Usage | A9 Ultra 单一来源；Receiver RS 多方开源实现一致，使用项未核实 | 未在实机验证 |
| [Logitech HID++ 2.0](docs/protocols.md#logitech-hid-20) | 已知 Lightspeed、Bolt、Unifying 接收器逐槽查找，例如 PRO X SUPERLIGHT 2、G309、G515 | 多方开源实现一致 / 单一来源，按型号 | 未在实机验证 |
| [Logitech Centurion](docs/protocols.md#logitech-centurion-与-astro-a50-gen-5) | G PRO X 2、G522、G PRO X 3、G325 的无线接收器 | 多方开源实现一致 / 单一来源，按型号 | 未在实机验证 |
| [ASTRO A50 Gen 5](docs/protocols.md#logitech-centurion-与-astro-a50-gen-5) | 底座 `046D:0B1C` | 单一来源 | 未在实机验证 |
| [Razer 90B](docs/protocols.md#razer-90b-feature) | 14 款鼠标、3 款键盘的无线接收器，逐型号匹配 PID、接口、TID | 多方开源实现一致 / 单一来源，按型号 | 未在实机验证 |
| [Razer BlackShark V2 HyperSpeed](docs/protocols.md#razer-pa-与-blackshark-64b) | `1532:0565`，MI_03、`FF14:0001`，直接查询 | 多方开源实现一致 | 未在实机验证 |
| Razer PA、BlackShark V3 系列 | PA 的模式切换及 V3 的冷启动会话风险尚待核验，保持禁用 | 单一来源 | 未在实机验证 |

其余候选型号保留在接收器表与[设备清单](research/device-catalog.md)，默认只用于诊断识别。

## 只读声明

托盘和探针共用核心库的只读适配器。适配器只发送白名单内的电量与状态查询，覆盖链路、充电、电压及 H9 的型号信息。程序使用现有 Windows 驱动，查询结束后释放设备句柄。与官方驱动共存的表现待实机验证。

协议来自公开的社区逆向资料和网页驱动的公开行为，仅用于互通和显示电量。几格与各厂商无关联，也未获厂商授权。项目采用 [MIT 许可证](LICENSE)。

## 使用前提

几格面向平时关闭官方驱动界面和后台进程、按需调节设置的玩家。官方软件正在运行时，可能抢占接收器。INZONE Hub 尤其会独占 H9 的串口，遇到占用时，两种入口都会结束该设备的本轮读取。托盘保留上次成功读数和时间。Windows 有时用同一个错误码表示独占和权限不足，探针可显示详细报错。

## 安装与使用

1. 从 [最新正式 Release](https://github.com/deepfry666/jige-battery/releases/latest) 下载 `Jige-Setup-<版本>-x64.exe` 和同版 `SHA256SUMS.txt`。
2. 在 PowerShell 运行 `Get-FileHash .\Jige-Setup-0.1.0-x64.exe -Algorithm SHA256`，将文件名替换为下载的版本，并和清单里的散列核对。
3. 运行安装器，从开始菜单打开「几格」。几格安装到当前用户的 `%LOCALAPPDATA%\Programs\Jige`，无需管理员权限。在 Windows 的「已安装的应用」（或「应用和功能」）可查看版本和卸载。

图标若折叠在任务栏的上箭头内，可拖到通知区域。悬停查看简表，单击打开设备面板，按 Esc 或点面板外收起。右键可换主设备、刷新、设为开机自启、检查更新、查看诊断信息或退出。「诊断信息…」会在本机生成一份脱敏报告并用记事本完整打开，由你检查后自行复制发送，诊断流程不会联网或上传，详见 [PRIVACY.md](PRIVACY.md)。面板中的设置和管理设备同样打开这个菜单。面板中的 `≈` 表示电压估算。

默认每 60 秒在后台查询一次。Windows 报告忙碌、D3D 全屏或演示，或前台窗口铺满显示器时，查询间隔改为 5 分钟。游戏中提交的刷新请求会等到退出游戏，面板、菜单和低电量提醒也会推迟。面板收起后会释放窗口和图形资源。

设备第一次读到电量等于或低于阈值时会进入提醒队列，默认 15%。每轮低电量提醒一次，“1 小时后提醒”可明确延后，本轮电量回升至阈值以上后才能触发新一轮提醒。

### 检查更新

右键选「检查更新…」后，几格才访问 GitHub 查询最新正式版。窗口显示检查结果及新版本摘要。选「下载并安装」后，几格再访问 Release 资源，核对安装包大小和 SHA-256，通过后退出并交给安装器静默升级，安装完成后自动重启。游戏中暂缓安装。选「查看 Release」会用默认浏览器打开该版本页面。几格没有后台更新检查或主动弹出的更新卡片。

升级原地覆盖程序文件，保留配置和设备缓存。下载目录为 `%LOCALAPPDATA%\Jige\updates`，下载前清理旧文件，最多保留一个待装包。校验失败的文件会清除，托盘下次启动会再次清理目录。CI 独立构建可用于试用和检查版本，从 Release 安装的托盘支持直接升级。同一台电脑只需保留正式安装版。

### 配置与缓存

- `%LOCALAPPDATA%\Jige\config.json` 保存 `primaryDeviceId`、`lowBatteryThreshold`、`pollSeconds`、`gamePollSeconds`、`gameSilent`、`startWithWindows` 和 `deviceAliases`。查询间隔以秒为单位，可设为 15–86400 秒，游戏间隔至少等于平时间隔。阈值范围为 0–100。
- `gameSilent` 默认为 `true`，将 Windows 新系统首次使用静默期（`QUNS_QUIET_TIME`）也纳入弹窗推迟规则。游戏和演示状态始终启用静默和查询退避。
- `%LOCALAPPDATA%\Jige\state.json` 保存设备最近一次成功读数、时间和提醒状态。设备别名按该文件中 `readings` 的键配置，例如 `"deviceAliases": { "ulx:接收器标识": "比赛鼠标" }`。
- 右键选“编辑配置文件”，保存后选择“重新加载配置”。开机自启在当前用户的 Windows Run 注册表项中只使用一个名为 `Jige Battery` 的值。启用后启动时会纠正旧路径。
- 卸载会移除程序、开始菜单快捷方式、开机自启值及整个 `%LOCALAPPDATA%\Jige` 数据目录。需要保留设置、缓存或诊断报告时，请在卸载前备份。

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
- `read` 对识别出的设备各查询一次。ULX 和 HID++ 的电压估算会标记“电压估算”。罗技 HID++ 接收器下已确认的槽各有一条记录，已配对但不可达的槽显示“离线”。H9 默认使用 115200，也可独立选 460800。`both` 会分别尝试两个波特率，每次单独打开并关闭串口。遇到占用、权限错误或设备拔除时结束尝试。
- `--raw` 输出十六进制收发帧和串口残片。按 `Ctrl+C` 可取消。

## 设备诊断与隐私

主动运行 `diag` 会查询受支持的接收器，并把完整的脱敏报告显示在终端。先检查报告，再决定是否将它与设备型号、连接方式和官方读数一起粘贴到设备报告 Issue。诊断在本地执行。收集字段、脱敏方式和示例见 [PRIVACY.md](PRIVACY.md)。

## 构建与测试

在仓库根目录用 .NET 10 运行以下命令。macOS 可编译托管项目并运行协议、模拟传输、托盘逻辑与配置恢复测试。设备访问、窗口和原生发布在 Windows 上运行。

```sh
dotnet build -p:EnableWindowsTargeting=true
dotnet test
```

Windows CI 会还原依赖、执行测试、发布两个 win-x64 NativeAOT 程序并检查原生启动。CI 将探针与托盘分别上传为 `jige-probe-win-x64` 和 `jige-tray-win-x64`。托盘启动检查会生成并释放三种尺寸的原生图标。托盘独立产物解压后运行 `Jige.exe`。本地 Windows 发布需要安装 Visual Studio 的 C++ 桌面开发工具链。

```powershell
dotnet publish src/Jige.Probe/Jige.Probe.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/win-x64
dotnet publish src/Jige.Tray/Jige.Tray.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/tray-win-x64
```

### 发布正式版

先将通过 Windows 验收的提交合入发布分支，并在 [CHANGELOG.md](CHANGELOG.md) 添加 `## [0.1.0]` 这样的版本标题。首发用以下命令为已合入的版本添加标签并推送。

```sh
git tag -a v0.1.0 -m "几格 v0.1.0"
git push origin v0.1.0
```

发布工作流从标签注入版本，运行测试和托盘自检，用 `installer/jige.iss` 编译安装器，最后上传安装器与 `SHA256SUMS.txt` 并以对应更新记录创建正式 Release。探针由普通 Windows CI 单独构建。安装器还可在 Windows 本地复现，先按上方命令发布托盘，再运行 `ISCC.exe /DAppVersion=0.1.0 installer\jige.iss`。重新生成品牌资源可运行 `python3 scripts/generate-icons.py`，需要 Node.js、npx 和 Pillow。

## Windows 实机验收待办

当前版本已通过 macOS 上的托管构建和离线测试。以下项目需要在 Windows 上验证。

- 验证两个 NativeAOT 产物和托盘 `--check`，核对 exe 图标、文件版本和安装器资产。
- 验证非管理员首次安装、开始菜单、已安装的应用记录、从运行中卸载及数据清除。
- 从较早版本静默升级，验证退旧启新、单实例、配置保留、待装包清理及开机自启路径纠正。
- 验证正式版检查、查看 Release、断网、取消、损坏下载与游戏中阻止安装。
- 核对浅深色、DWM、DPI、混合多屏、任务栏位置、Explorer 重启、失焦和 Esc。
- 验证全屏轮询退避及提醒恢复，检查资源占用、外设真实读数、休眠、拔插与占用。

## 致谢与参考

- Finalmouse XPanel、官方 USB 权限清单以及 HaloBattery 的 ULX 报文与实机记录。[XPanel](https://xpanel.finalmouse.com/) · [USB 权限清单](https://github.com/teamfinalmouse/xpanel-linux-permissions) · [HaloBattery](https://github.com/HeyOkay/HaloBattery)
- InZoneH9Tray、openinzone 与 inzone-h9-mac 提供 H9 串口查询、帧格式及实机记录。[InZoneH9Tray](https://github.com/janit88/InZoneH9Tray) · [openinzone](https://github.com/penguinwokrs/openinzone) · [inzone-h9-mac](https://github.com/jpcarranza94/inzone-h9-mac)

- Solaar（GPL-2.0，部分文件 GPL-2.0-or-later）、OpenLogi（MIT 或 Apache-2.0）、OpenMouse mouse-protocol（AGPL-3.0）提供 HID++ 的槽、身份和电量协议资料。[Solaar](https://github.com/pwr-Solaar/Solaar) · [OpenLogi](https://github.com/AprilNEA/OpenLogi) · [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol)
- HeadsetControl（GPL-3.0）提供 Centurion、G325 和 ASTRO Gen 5 的报文与实机记录。[HeadsetControl](https://github.com/Sapd/HeadsetControl)
- OpenRazer（GPL-2.0）、Razer Battery Report（MIT）、HaloBattery（MIT）、BlackShark V2 HyperSpeed WebHID（MIT）与 Synapse-for-Linux（MIT）提供雷蛇 PID、接口、读电量与充电的报文和实机证据。[OpenRazer](https://github.com/openrazer/openrazer) · [Razer Battery Report](https://github.com/xzeldon/razer-battery-report) · [HaloBattery](https://github.com/HeyOkay/HaloBattery) · [V2 HyperSpeed WebHID](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid) · [Synapse-for-Linux](https://github.com/bbesli/Synapse-for-Linux)

几格依据公开协议字段和实机报文独立实现，保留 MIT 许可。许可出处见 [设备清单](research/device-catalog.md#6-参考实现许可证)。

协议字段、出处及各字段的验证状态见 [协议记录](docs/protocols.md)。

<p align="center"><img src="design/brand/fourj-mark.svg" width="96" alt="几格 FourJ 家族标志"></p>

# 几格 / Jige Battery

几格是用托盘图标显示无线外设电量的 Windows 工具。当前阶段已经实现 win-x64 NativeAOT 托盘、设备面板和低电量提醒，并保留一次性命令行探针。下表设备均经 2.4 GHz USB 接收器查询，实机验证状态见表。

| 支持的设备 | 接收器与识别方式 | 证据等级 | 验证状态 |
| --- | --- | --- | --- |
| Sony INZONE H9 一代 | PC 档接收器 `054C:0E53` 的 `MI_06` USB 串口（INZONE Hub 运行时自动退让） | 多方开源实现一致 | **已实机验证**（Windows 11，2026-11-19） |
| 游狼 HTX Ultra 8K（XVI-NEW） | `33E4:5617`，`MI_02 / FFFF:0000` | 依据官方网页驱动型号表 | **已实机验证**（Windows 11，2026-11-19） |
| 游狼 XVI-NEW 同家族 | Fenrir Pro 8K `33E4:3617`、HTM Plus `33E4:3817`、HSK Pro 2.0 8K `33E4:6817`，`MI_02 / FFFF:0000` | 与已验证型号同协议家族、同官方型号表 | **同家族推定**（未在实机验证） |
| Finalmouse UltralightX（ULX） | 2.4 GHz 接收器 `361D:0100`，以及同家族 `361D` + `FF00:0001`（64/64）通用识别 | 单一来源 / 通用适配 | 仅按资料 |
| ATK Compx-OUT | `373B:1031`（F1 Ultimate）、共用接收器 `373B:101A/11D9/1278`（默认「ATK 鼠标」，协议返回 CID/MID 时显示具体型号），以及同家族 `373B` + `FF02:0002`（17/17）通用识别 | 多方开源实现一致 / 单一来源 / 通用适配 | 仅按资料 |
| ATK BitMouse | ATK ZERO `373B:1155`（`FF05:0001`），以及同家族 `373B` + `FF05:0001`（64/64）通用识别 | 多方开源实现一致 / 通用适配 | 仅按资料 |
| 游狼 XVI-OLD | 从官方网页驱动型号表导入 21 款 `XVI=1` 旧协议型号（如 HTX Mini 8K `33E4:2717`、VUK `33E4:3917`、HT-S2 / HTS Plus / HSK Pro 等），`MI_02 / FFFF:0000` | 依据官方网页驱动型号表 | 仅按资料 |
| [罗技 HID++](docs/protocols.md#罗技-hid-10-与-20) | Bolt、Unifying、Nano、LIGHTSPEED 及符合识别条件的接收器；支持老款 HID++ 1.0、USB 线缆及符合条件的罗技蓝牙鼠标和键盘 | Solaar / HaloBattery / Linux | 仅按资料 |
| [Windows 蓝牙电量](docs/protocols.md#windows-蓝牙电量属性) | Windows 提供电量的各品牌蓝牙外设。支持在设置里关闭显示，与同一台设备的 HID++ 电量自动合并 | Windows 设备属性 | 仅按资料 |
| [Logitech HID++ 耳机](docs/protocols.md#hid-耳机) | G533、G535、G733、G935、G PRO X Wireless 等匹配型号；部分读数为估算 | HaloBattery / HeadsetControl / Linux | 仅按资料 |
| [Logitech Centurion](docs/protocols.md#logitech-centurion-与-astro-a50-gen-5) | G PRO X 2、G522、G325 的无线接收器 | 多方开源实现一致 / 单一来源，按型号 | 仅按资料 |
| [ASTRO A50 Gen 5](docs/protocols.md#logitech-centurion-与-astro-a50-gen-5) | 底座 `046D:0B1C` | 单一来源 | 仅按资料 |
| [Razer 90B](docs/protocols.md#razer-90b-feature) | 72 个鼠标 PID、15 个键盘 PID（对齐 OpenRazer，含无线接收器、HyperPolling 接收器与 USB-C 有线充电模式；协议忙或被其他软件临时占用时自动重试最多 3 次） | 多方开源实现一致 / 单一来源，按型号 | 仅按资料 |
| [Razer BlackShark 纯只读耳机](docs/protocols.md#razer-pa-与-blackshark-64b) | BlackShark V2 Pro (2020) `1532:0528`（`FF00:0001` 64B Feature）与 BlackShark V2 HyperSpeed `1532:0565`（MI_03、`FF14:0001`） | 多方开源实现一致 | 仅按资料 |
| 暂未启用路径（路径开关） | 雷蛇 PA（`0555/0556/0557/0583/053A/0536`）、Barracuda X Macronix（`0552`）、V2 Pro 2020 充电线（`052E`）、BlackShark V3（`057A/0577`）、ATK A9 Ultra（`373B:111A`）、ATK 星闪/前置校验候选、游狼 Receiver RS（`33E4:3854`）、Fenrir Asym（`33E4:3517`）、ASTRO A50 X（`046D:0B0B`）、罗技 PRO X 3（`046D:0B45`） | 证据不足或存在写入/断链风险，打开句柄前跳过 | 暂未启用 |

完整设备支持表、三级验证状态和暂未启用原因见 [设备支持清单](docs/devices.md)。其余候选型号保留在 [设备清单](research/device-catalog.md)。

## 界面截图

Windows 实机截图待补，预留托盘、主面板和管理设备页的位置。[截图目录](docs/images/README.md)

## 只读声明

托盘和探针共用核心库的只读适配器。适配器只发送白名单内的电量与状态查询，覆盖链路、充电、电压与设备身份信息。程序使用现有 Windows 驱动，查询结束后释放设备句柄。与官方驱动共存的表现待实机验证。

协议来自公开的社区逆向资料和网页驱动的公开行为，仅用于互通和显示电量。几格与各厂商无关联，也未获厂商授权。项目采用 [MIT 许可证](LICENSE)。

## 使用前提

几格面向平时关闭官方驱动界面和后台进程、按需调节设置的玩家。官方软件正在运行时，可能抢占接收器。INZONE Hub 会独占 H9 的串口，几格检测到 INZONE Hub 运行时会自动退让，不抢占串口；若仍遇到占用，也会结束该设备的本轮读取。托盘保留上次成功读数和时间。Windows 有时用同一个错误码表示独占和权限不足，探针可显示详细报错。

## 安装与使用

1. 从 [最新正式 Release](https://github.com/deepfry666/jige-battery/releases/latest) 下载 `Jige-Setup-<版本>-x64.exe` 和同版 `SHA256SUMS.txt`（无代理环境可通过国内镜像 [`download.imfourj.cn`](https://download.imfourj.cn/jige-battery/latest.json) 获取同版直链；如需提前试用主分支改动，可从滚动测试版 [`prerelease-build`](https://github.com/deepfry666/jige-battery/releases/tag/prerelease-build) 或国内镜像 [测试版安装包](https://download.imfourj.cn/jige-battery/prerelease/Jige-Setup-prerelease-x64.exe) 与 [`SHA256SUMS.txt`](https://download.imfourj.cn/jige-battery/prerelease/SHA256SUMS.txt) 获取，详见 [CONTRIBUTING.md](CONTRIBUTING.md)）。
2. 在 PowerShell 运行 `Get-FileHash .\Jige-Setup-0.2.6-x64.exe -Algorithm SHA256`，将文件名替换为下载的版本，并和清单里的散列核对。
3. 运行安装器，按提示选择安装位置（默认 `%LOCALAPPDATA%\Programs\Jige`，无需管理员权限），可按需勾选「创建桌面快捷方式」与首次安装的「开机时启动几格」，安装完成后从桌面或开始菜单打开「几格」。在 Windows 的「已安装的应用」（或「应用和功能」）可查看版本和卸载。

首次运行时，几格会在托盘上方提示如何把图标从上箭头拖到时钟旁。托盘图标用鼠标、键盘、耳机或通用设备的剪影区分设备。剩余电量越多，剪影从底部填上的颜色就越高，未填部分显示为暗色。充电中或已充满时填绿色，暂无读数时整个剪影显示为暗色。托盘默认采用「自定义」，每台已勾选的活跃设备各占一个图标。也可设为「只显示电量最低的一台」，图标会随当前电量最低的设备切换。把鼠标移到托盘图标上可在几格自绘的 Windows 11 风格悬停卡片中查看电量百分比（替换系统默认文字提示，同时保留读屏软件提示）：「自定义」模式下展示该设备的名称、连接方式与来源、大号百分比（`≈` 表示估算）、10 格电量条和读取时间；「只显示电量最低的一台」模式下按电量从低到高列出所有在用设备（最多 6 行，超出显示「还有 N 台」），两种模式都不显示已断开的设备，读数更新时内容实时刷新。卡片不抢焦点、点击穿透，主面板打开或全屏独占游戏期间不显示。设备休眠时保留上次读数且图标不变灰，悬停卡片写明「休眠中」。连续休眠超过 10 分钟仍无回复、设备明确断开或接收器拔出后，设备图标会从托盘移除，重连时恢复原位置。托盘没有可显示的设备时，保留一个可打开面板的「几格」图标。动一下键鼠即可唤醒并补查休眠中的设备（使用 `GetLastInputInfo`，不装钩子、不记录任何输入，最多每 2 秒一次）。

单击托盘图标打开完整面板，`≈` 表示电压估算。主面板自动排序：顶部大卡片显示电量最低的活跃设备（全离线时显示最近一台离线设备并标明已断开），其余活跃设备从低到高排列，离线设备折叠为「离线设备（N）」，点击可展开。按 Esc 从子页返回主页，再按一次收起面板，点击面板外也可收起。面板和设备页以「已实机验证」「按协议适配」「通用适配」三种徽标区分设备来源。

- 点右上角齿轮进入「设置」。展开「托盘显示」可切换模式，在「自定义」下直接勾选设备，选择会与「管理设备」页同步。设置页也可更改低电量提醒阈值（10%、15%、20%、30%）、查询间隔（30 秒、1 分钟、2 分钟）以及「游戏中不打扰」和开机自启。「显示蓝牙设备」默认开启。
- 点底部「管理设备」，点击名称后可行内改名，清空保存可恢复默认名（Enter 或失焦保存，Esc 取消）。每台设备提供「托盘显示」勾选框和「面板显示」眼睛开关；离线设备可以移除，清除其别名和缓存电量。点击「重新检测」可清空安全门拦截记录并立即重新扫描接收器。设备较多时可用滚轮翻看。
- 管理设备页会提示检测到的未识别接收器数。可从此页或托盘右键菜单导出诊断，面板会反馈导出进度和结果。报告保存在本机并用记事本完整打开，包含当前游戏判定及原因。确认后可点击面板里的「复制报告并去反馈」一键复制并打开设备报告模板（网址不带任何报告或设备信息）。游戏中导出完成时，下次开面板可点「查看诊断报告」。详见 [PRIVACY.md](PRIVACY.md)、[CONTRIBUTING.md](CONTRIBUTING.md) 与 [抓包教程](docs/capture-guide.md)。

默认每分钟查询一次完整电量，并另起轻量在线心跳（平时 8 秒一次，游戏中 30 秒一次）快速感知开关机；罗技接收器改为被动监听连接/断开推送而不发心跳；Sony INZONE H9 不做高频心跳。平时仅在收到系统插拔或休眠恢复通知时重新枚举设备，日常查询直接复用接口缓存；调度使用单调时钟，修改系统时间不影响节奏。Windows 报告 D3D 独占全屏或演示模式时，查询延长至 5 分钟，低电量提醒和首次引导会推迟。Windows 报告忙碌时，只有前台是铺满所在显示器的普通应用窗口，才启用这项保护。其他通知状态下，打开「游戏中不打扰」后，普通应用全屏、无边框窗口铺满工作区或匹配配置里的游戏进程，也会启用保护。主动点击托盘、菜单或「立即刷新」可照常操作。

设备在线、未充电且尚未充满时，几格会根据最近两个普通查询周期内低于阈值的读数提示充电。提醒被游戏或面板打断后会在有有效读数时再显示，直到点「知道了」或「1 小时后提醒」。面板和托盘以红色表示低于阈值，阈值至阈值加 15 个百分点以琥珀色表示。

### 检查更新

主动点击「检查更新」后，几格才查询最新正式版（优先访问国内镜像 `download.imfourj.cn`，不可达或失败时自动回退 GitHub `releases/latest`，均不包含测试版；装了测试版也不会被提示降级到更低的正式版）。窗口显示检查结果及新版本摘要。选「下载并安装」后，几格优先从国内镜像下载（失败时自动回退 GitHub Release），核对安装包大小和 SHA-256，通过后退出并交给安装器静默升级，安装完成后自动重启。游戏中也可主动下载并安装。选「查看 Release」会用默认浏览器打开该版本页面。几格没有后台更新检查或主动弹出的更新卡片。

升级原地覆盖程序文件，保留配置和设备缓存。下载目录为 `%LOCALAPPDATA%\Jige\updates`，下载前清理旧文件，最多保留一个待装包。校验失败的文件会清除，托盘下次启动会再次清理目录。CI 独立构建可用于试用和检查版本，从 Release 安装的托盘支持直接升级。同一台电脑只需保留正式安装版。

### 配置与缓存

- `%LOCALAPPDATA%\Jige\config.json` 保存托盘显示模式、各设备的托盘显示选择、提醒阈值、查询间隔、游戏保护、开机自启、设备别名与面板可见性（兼容旧版本配置中的已废弃字段）。配置损坏时保留最近一份 `config.broken-<时间>.json`，再恢复默认值。
- 若游戏以常规窗口运行，可在退出几格后编辑 `config.json`，将任务管理器显示的进程名加到 `"gameProcessNames": ["Game.exe"]`，再启动几格。名称匹配忽略大小写和 `.exe` 后缀，默认名单为空。
- `%LOCALAPPDATA%\Jige\state.json` 保存最近一次成功读数、时间、提醒状态、每台设备最近 5 次读数历史（最多 32 台）以及安全门拦截记录（最多 64 条）。仅在电量或状态真正变化且内容有差异时才写盘。罗技 HID++ 设备升级后按查询得到的身份识别，旧的槽位别名仍可在管理页移除。
- 开机自启默认开启，使用当前用户 Windows Run 项下的 `Jige Battery` 值。升级沿用现有设置。
- 卸载会移除程序、快捷方式与开机自启值，并询问是否同时清除 `%LOCALAPPDATA%\Jige` 下的设置、电量、日志和诊断。静默卸载默认清除该目录。

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
- `read` 对识别出的设备各查询一次。ULX 和 HID++ 的电压估算会标记“电压估算”。罗技 HID++ 接收器只列出本次查询确认有设备响应的槽位。H9 默认使用 115200，也可独立选 460800。`both` 会分别尝试两个波特率，每次单独打开并关闭串口。遇到占用、权限错误或设备拔除时结束尝试。
- `--raw` 输出原始十六进制收发帧和串口残片，可能含设备标识。需要分享时，请使用脱敏的 `diag` 报告。按 `Ctrl+C` 可取消。

## 设备诊断与隐私

主动运行 `diag` 会查询受支持的接收器，并把完整的脱敏报告显示在终端。先检查报告，再决定是否将它与设备型号、连接方式和官方读数一起粘贴到设备报告 Issue。诊断在本地执行。收集字段、脱敏方式和示例见 [PRIVACY.md](PRIVACY.md)。

## 构建与测试

在仓库根目录用 .NET 10 运行以下命令。macOS 可编译托管项目并运行协议、模拟传输、托盘逻辑与配置恢复测试。设备访问、窗口和原生发布在 Windows 上运行。

```sh
dotnet build Jige.slnx -p:EnableWindowsTargeting=true
dotnet test Jige.slnx
```

Windows CI 会还原依赖、执行测试、发布两个 win-x64 NativeAOT 程序并检查原生启动。CI 将探针与托盘分别上传为 `jige-probe-win-x64` 和 `jige-tray-win-x64`。托盘启动检查会生成并释放 16、24、32px 的四种设备图标及默认图标。托盘独立产物解压后运行 `Jige.exe`。本地 Windows 发布需要安装 Visual Studio 的 C++ 桌面开发工具链。

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
- 验证用户主动检查更新、查看 Release、断网、取消、校验失败，以及游戏中主动下载并安装。
- 核对浅深色、DWM、DPI、混合多屏、任务栏位置、Explorer 重启、失焦和 Esc。
- 核对四种设备图形和电量条在 100%／150%／200% 缩放及深浅任务栏下的清晰度。检查低电量、充电、满电、休眠和无读数的显示。
- 核对 0.2.5 自绘悬停卡片：系统默认提示不再弹出、Acrylic 与关闭透明纯色、淡入淡出与关闭动画、不抢焦点、点击穿透、主面板打开与全屏游戏时不显示、单设备/列表模式与读数实时刷新。
- 核对面板导航、系统动画开关、设置菜单锚点、设备列表滚动和中文输入法改名。
- 验证普通桌面加覆盖窗口或截图选区、普通应用全屏、D3D 独占全屏及演示模式，核对查询间隔、自动提醒与诊断原因。检查资源占用、外设读数、休眠、拔插与占用。

## 致谢与参考

- Finalmouse XPanel、官方 USB 权限清单以及 HaloBattery 的 ULX 报文与实机记录。[XPanel](https://xpanel.finalmouse.com/) · [USB 权限清单](https://github.com/teamfinalmouse/xpanel-linux-permissions) · [HaloBattery](https://github.com/HeyOkay/HaloBattery)
- InZoneH9Tray、openinzone 与 inzone-h9-mac 提供 H9 串口查询、帧格式及实机记录。[InZoneH9Tray](https://github.com/janit88/InZoneH9Tray) · [openinzone](https://github.com/penguinwokrs/openinzone) · [inzone-h9-mac](https://github.com/jpcarranza94/inzone-h9-mac)

- Solaar（GPL-2.0，部分文件 GPL-2.0-or-later）、OpenLogi（MIT 或 Apache-2.0）、OpenMouse mouse-protocol（AGPL-3.0）提供 HID++ 的槽、身份和电量协议资料。[Solaar](https://github.com/pwr-Solaar/Solaar) · [OpenLogi](https://github.com/AprilNEA/OpenLogi) · [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol)
- HeadsetControl（GPL-3.0）提供 Centurion、G325 和 ASTRO Gen 5 的报文与实机记录。[HeadsetControl](https://github.com/Sapd/HeadsetControl)
- OpenRazer（GPL-2.0）、Razer Battery Report（MIT）、HaloBattery（MIT）、BlackShark V2 HyperSpeed WebHID（MIT）与 Synapse-for-Linux（MIT）提供雷蛇 PID、接口、读电量与充电的报文和实机证据。[OpenRazer](https://github.com/openrazer/openrazer) · [Razer Battery Report](https://github.com/xzeldon/razer-battery-report) · [HaloBattery](https://github.com/HeyOkay/HaloBattery) · [V2 HyperSpeed WebHID](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid) · [Synapse-for-Linux](https://github.com/bbesli/Synapse-for-Linux)

几格依据公开协议字段和实机报文独立实现，保留 MIT 许可。许可出处见 [设备清单](research/device-catalog.md#6-参考实现许可证)。

协议字段、出处及各字段的验证状态见 [协议记录](docs/protocols.md)。

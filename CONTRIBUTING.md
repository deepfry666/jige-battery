# 贡献与设备反馈指南

欢迎帮助几格适配更多无线外设、修正读数问题或改进体验！几格的核心原则是**只读、低打扰、可验证**：我们只发有明确来源的只读查询，不猜测设备命令，也不把没在实机跑通的设备标成「已实机验证」。

> Looking for English instructions? Jump to the [English section](#english) below.

---

## 一、提交设备报告前的准备

在导出诊断报告前，请先做好下面三件事，否则接收器可能无回复或被占用：

1. **先记下官方读数，再彻底关闭官方驱动软件**：先在官方软件（如 ATK HUB、Logitech G HUB、Razer Synapse、INZONE Hub、游狼网页驱动等）里看一眼当前的电量百分比和充电状态，然后**彻底退出官方软件**（别忘了右键任务栏右下角托盘图标退出后台进程）。
2. **唤醒无线设备**：轻动一下无线鼠标、确认耳机或键盘已经开机且处于 2.4 GHz 无线连接模式（Sony INZONE H9 接收器需拨到 PC 档）。如果鼠标已经静止休眠，接收器可能不会回复查询。
3. **插好目标接收器**：确认 2.4 GHz 接收器直接插在电脑上并且系统已识别。

## 二、如何导出诊断报告

你可以通过下面任意一种方式导出本地诊断报告：

- **方法 A（推荐，在几格托盘内导出）**：
  1. 右键托盘图标点击「诊断信息…」，或在面板「管理设备」页点击「导出诊断信息」。
  2. 几格会在本机生成脱敏报告 `%LOCALAPPDATA%\Jige\diagnostics.txt`，并自动用记事本打开供你完整检查（若当时正在玩游戏，不会弹出记事本，可稍后打开面板点击「查看诊断报告」）。
  3. 确认内容没问题后，可在面板底部点击「**复制报告并去反馈**」——几格会把报告全文复制到剪贴板，再用默认浏览器打开设备报告页面（网址里**不会**携带任何报告内容或设备信息），你直接粘贴即可。
- **方法 B（使用命令行探针）**：
  在 PowerShell 中进入探针目录运行：
  ```powershell
  .\Jige.Probe.exe diag | Set-Content -Encoding utf8 diagnostics.txt
  notepad .\diagnostics.txt
  ```

诊断报告已经对序列号、Windows 设备实例 ID、用户名、计算机名、COM 号和本地路径做过脱敏，不会记录你的按键、光标或自定义设备别名。几格**不会**自动上传任何内容，详见 [PRIVACY.md](PRIVACY.md)。

## 三、如何提交 Issue

- **新设备适配 / 识别不到 / 电量或充电不准**：请使用 [设备电量报告](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml) 模板。
  - 请附上**完整**的 `diagnostics.txt`（不要只截取最后几行，开头的 `HID|` 描述符行最关键）。
  - 写明设备完整型号、连接方式、以及官方驱动与几格各自的读数。
  - **如果你知道哪个开源项目能读这台设备**（例如 [HaloBattery](https://github.com/HeyOkay/HaloBattery)、[OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol)、[OpenRazer](https://github.com/openrazer/openrazer)、[Solaar](https://github.com/pwr-Solaar/Solaar)、[HeadsetControl](https://github.com/Sapd/HeadsetControl) 或厂商网页驱动），请务必附上链接，这能大幅加快适配！
  - 如果设备尚未有公开协议资料，你可以按 [Wireshark + USBPcap 抓包教程](docs/capture-guide.md) 抓取官方软件读电量的 `.pcapng` 文件一并提供。
- **崩溃 / 界面 / 托盘 / 更新等普通问题**：请使用 [问题反馈](https://github.com/deepfry666/jige-battery/issues/new?template=bug.yml) 模板。

## 四、测试版通道与反馈方式

如果你想提前体验最新修复或协助实机验证新适配的设备，可以从测试版通道下载滚动构建：

- **下载地址**：前往 GitHub Releases 的固定预发布页面 [`prerelease-build`](https://github.com/deepfry666/jige-battery/releases/tag/prerelease-build)，下载最新的 `Jige-Setup-prerelease-x64.exe` 和 `SHA256SUMS.txt`（每次推送到 `main` 分支都会自动重建并替换旧文件）。
- **关于自动更新**：几格内置的「检查更新」**只查看正式版**，不会推送测试版；如果你安装了测试版（例如 `0.2.3 测试版（abc1234）`），点击「检查更新」时也不会提示你降级回更低的正式版（如 `v0.2.2`）。
- **反馈测试版问题**：打开几格的「设置」页底部或诊断报告开头，复制完整的版本号和 **7 位提交号**（例如 `0.2.3 测试版（abc1234）`），连同诊断报告一起写进 issue，方便定位到具体提交。

## 五、提交代码与新增协议规范

如果你打算直接提 PR 适配新设备或修复问题，请务必遵守以下红线：

1. **严格只读，绝不发送猜测的命令**：
   - 只允许发送与电量、充电、无线链路状态、设备名称/身份有关的只读查询命令，且每条发送帧都必须进入适配器的显式白名单检查。
   - **禁止**发送任何猜测的命令，禁止发送配对、忘配对、切模式、写配置、灯光、DPI 或固件更新等写入/状态修改命令。
2. **协议必须注明出处与许可证**：
   - 每条新协议或新 PID 路由都必须在 [docs/protocols.md](docs/protocols.md) 和代码注释里注明来源：公开开源实现、厂商公开网页驱动，或真实 USB 抓包记录。
   - 允许参考 MIT / Apache-2.0 项目的实现（例如 HaloBattery，MIT），并须在文档和致谢页注明出处。
   - **不得复制 GPL / AGPL 项目的代码**（例如 BatteryHub、OpenRazer、Solaar、HeadsetControl、openinzone），只能参考公开协议事实并独立实现。
3. **如实标注验证状态分级**：
   - 集中登记在 `src/Jige.Core/DeviceCatalog.cs` 并同步到 [docs/devices.md](docs/devices.md) 与 [README.md](README.md)。
   - 没在 Windows 实机上实际看到过读数的设备，一律不得标为「已实机验证」；证据不足或有副作用风险的路径必须登记进 `DeviceCatalog.DisabledPaths`（在打开设备句柄前跳过）。
4. **附带离线测试与零警告构建**：
   - 新增或修改协议必须附带离线回放或模拟传输单元测试（不依赖实机硬件）。
   - 提交前确认：
     ```sh
     dotnet build Jige.slnx -p:EnableWindowsTargeting=true
     dotnet test Jige.slnx
     ```
     必须零警告、零错误，且全部测试通过。

---

## English

Thank you for helping improve Jige Battery (几格)! Jige follows three core rules: **read-only**, **zero disturbance during gaming**, and **honest hardware verification**. We never send guessed commands and never mark a device as hardware-verified unless actual readings were observed on Windows.

### 1. Before Submitting a Device Report

1. **Note official readings, then fully close vendor software**: Check the current battery percentage and charging state in the official app (ATK HUB, G HUB, Synapse, INZONE Hub, web drivers, etc.), then **completely exit it** (including its tray background process).
2. **Wake the wireless device**: Move the mouse or turn on the headset/keyboard so it is actively connected over 2.4 GHz (for Sony INZONE H9, switch the dongle to PC mode). Sleeping mice often stop replying to queries.
3. **Keep the receiver plugged in** directly to your PC.

### 2. Exporting a Diagnostic Report

- **In Jige Tray (Recommended)**:
  1. Right-click the tray icon → **诊断信息…** (or open **管理设备** → **导出诊断信息**).
  2. Jige generates a sanitized local report at `%LOCALAPPDATA%\Jige\diagnostics.txt` and opens it in Notepad for your review (deferred while gaming).
  3. After inspecting it, click **复制报告并去反馈** in the panel. Jige copies the full report to your clipboard and opens the GitHub device-report form in your default browser — **no report text or device data is placed in the URL**.
- **Via CLI Probe**:
  ```powershell
  .\Jige.Probe.exe diag | Set-Content -Encoding utf8 diagnostics.txt
  notepad .\diagnostics.txt
  ```

Serial numbers, Windows device instance IDs, usernames, computer names, COM port numbers, and local file paths are scrubbed before export. Jige never uploads reports automatically (see [PRIVACY.md](PRIVACY.md)).

### 3. Opening an Issue

- Use the [Device Report template](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml) for new devices or inaccurate battery/charging readings. Attach the **full** `diagnostics.txt` and specify the device model, connection type, and official vs. Jige readings.
- **Know an open-source project that already reads your device?** (e.g., HaloBattery, OpenMouse, OpenRazer, Solaar, HeadsetControl, or a vendor web driver) — please include the link!
- If no public protocol documentation exists yet, see [docs/capture-guide.md](docs/capture-guide.md) to capture a `.pcapng` trace with Wireshark + USBPcap.
- For crashes, UI, tray, or updater issues, use the [Bug Report template](https://github.com/deepfry666/jige-battery/issues/new?template=bug.yml).

### 4. Beta / Pre-release Channel

- Every push to `main` updates a single rolling pre-release at [`prerelease-build`](https://github.com/deepfry666/jige-battery/releases/tag/prerelease-build) (`Jige-Setup-prerelease-x64.exe` + `SHA256SUMS.txt`). Old assets are replaced automatically.
- Jige's built-in "Check for Updates" only checks stable releases (`releases/latest`) and will never prompt beta users to downgrade to an older stable release.
- When reporting an issue on a beta build, please include the displayed version and 7-character commit hash shown in Settings or at the top of `diagnostics.txt` (e.g. `0.2.3 测试版（abc1234）`).

### 5. Protocol & Code Contribution Rules

1. **Strictly read-only — never send guessed commands.** Every outgoing packet must be explicitly whitelisted and limited to battery, charging, link status, or read-only device identity queries.
2. **Cite every protocol source and respect licenses.** Document sources in [docs/protocols.md](docs/protocols.md). MIT references (such as HaloBattery) require attribution; **never copy code from GPL/AGPL projects** (such as BatteryHub, OpenRazer, Solaar, or HeadsetControl) — only use public protocol facts with clean-room C# implementations.
3. **Maintain accurate verification tiers.** Register enabled entries and disabled paths in `src/Jige.Core/DeviceCatalog.cs` and keep [docs/devices.md](docs/devices.md) and [README.md](README.md) in sync. Paths lacking sufficient evidence or requiring mode switches must be listed in `DeviceCatalog.DisabledPaths` so they are skipped before opening any device handle.
4. **Include offline tests and build with zero warnings**:
   ```sh
   dotnet build Jige.slnx -p:EnableWindowsTargeting=true
   dotnet test Jige.slnx
   ```

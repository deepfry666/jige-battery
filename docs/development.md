# 开发与维护

面向想自己构建、排查设备或维护发布的人。普通用户看 [README](../README.md) 就够了。

## 配置与缓存

- `%LOCALAPPDATA%\Jige\config.json`：显示位置、外观模式（`theme`）、托盘模式、各设备的面板/托盘显示选择（`hiddenDeviceIds` / `trayExcludedDeviceIds`）、提醒阈值（含单台设备阈值 `deviceThresholds`、进游戏提醒 `gameEntryReminder`、充满提醒 `fullChargeReminder`）、查询间隔（含游戏间隔）、游戏保护、自动检查更新（`autoCheckUpdates`）、预计可用时间开关（`showRuntimeEstimate`）、开机自启、设备别名。配置损坏时，保留一份 `config.broken-<时间>.json`，再恢复默认值。
- 游戏以普通窗口运行时，可以先退出几格，再在 `config.json` 里加上 `"gameProcessNames": ["Game.exe"]`（进程名见任务管理器，不区分大小写，可省略 `.exe`）。
- `%LOCALAPPDATA%\Jige\state.json`：最近一次成功读数、提醒状态、每台设备最近 5 次读数，以及预计可用时间的统计摘要（`runtime`）。电量或状态有变化时才写盘，另外每累计在线半小时顺带保存一次估算进度。
- `%LOCALAPPDATA%\Jige\jige.log`：脱敏日志，只记录状态变化和原因类别。
- 开机自启使用当前用户 Run 项里的 `Jige Battery` 值。

## 命令行探针

探针 `Jige.Probe.exe` 用于排查设备，由 Windows CI 单独构建（产物 `jige-probe-win-x64`）。在 PowerShell 中运行，H9 接收器须拨到 PC 档：

```powershell
.\Jige.Probe.exe list                          # 列出 HID 接口和 USB 串口
.\Jige.Probe.exe read                          # 对识别出的设备各查询一次
.\Jige.Probe.exe read --raw                    # 同时输出原始收发帧（可能含设备标识）
.\Jige.Probe.exe read --raw --sony-baud both   # H9 分别尝试 115200 和 460800
.\Jige.Probe.exe diag                          # 输出脱敏诊断报告
```

需要分享时，请用脱敏的 `diag` 报告，不要用 `--raw` 的输出。

## 构建与测试

在仓库根目录用 .NET 10 运行。macOS 上可以编译托管项目，运行协议、模拟传输、托盘逻辑和配置测试；设备访问、窗口和原生发布需要在 Windows 上运行。

```sh
dotnet build Jige.slnx -p:EnableWindowsTargeting=true
dotnet test Jige.slnx
```

Windows 本地发布 NativeAOT 需要 Visual Studio 的 C++ 桌面开发工具：

```powershell
dotnet publish src/Jige.Probe/Jige.Probe.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/win-x64
dotnet publish src/Jige.Tray/Jige.Tray.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/tray-win-x64
```

### 发布流程

- 每次推送到 `main`，CI 都会运行测试、发布两个 NativeAOT 程序、做托盘启动自检，并更新滚动测试版 [`prerelease-build`](https://github.com/deepfry666/jige-battery/releases/tag/prerelease-build)（国内镜像：[安装包](https://download.imfourj.cn/jige-battery/prerelease/Jige-Setup-prerelease-x64.exe) · [SHA256SUMS.txt](https://download.imfourj.cn/jige-battery/prerelease/SHA256SUMS.txt)）。几格内置的检查更新只看正式版。
- 发布正式版：确认 `Directory.Build.props` 和 `installer/jige.iss` 的版本号，在 [CHANGELOG.md](../CHANGELOG.md) 写好 `## [x.y.z]`，然后打标签推送：

  ```sh
  git tag vX.Y.Z
  git push origin vX.Y.Z
  ```

  发布工作流会运行测试和托盘自检，用 `installer/jige.iss` 编译安装器，上传安装包和 `SHA256SUMS.txt`，创建 GitHub Release，并同步到国内镜像。发布后把 `main` 的版本号升到下一个。
- 安装器也可以在 Windows 本地复现：先发布托盘，再运行 `ISCC.exe /DAppVersion=x.y.z installer\jige.iss`。重新生成品牌资源：`python3 scripts/generate-icons.py`（需要 Node.js、npx 和 Pillow）。

## 待实机确认

- 任务栏直读条在 Windows 10 和 Windows 11 上的位置和层级：点开开始菜单、通知/操作中心以后是否还在；Windows 10 下的「资讯和兴趣」、小任务栏按钮、自定义工具栏避让；自动隐藏任务栏、Explorer 重启、全屏游戏进出时的表现；100%、150%、200% 缩放下与时钟的大小是否协调。
- 托盘水位图标在深浅色任务栏、不同缩放下的清晰度。
- 标为「仅按资料」的设备的实际读数。

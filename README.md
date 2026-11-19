# 几格 / Jige Battery

几格计划做成 Windows 11 托盘工具，用数字显示经 2.4 GHz USB 接收器连接的无线外设电量。第一阶段提供设备读取核心库和一次性命令行探针，托盘仍待实现。探针运行一次即退出，平时没有常驻进程或后台轮询。

| 探针适配器 | 接收器与识别方式 | 证据等级 | 实机验证状态 |
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

探针只发送白名单内的电量与状态查询，覆盖链路、充电、电压及 H9 的型号信息。设备设置、固件刷写、模式切换、配对和 bootloader 均在查询范围之外。程序使用现有 Windows 驱动，查询结束即释放设备句柄。官方驱动功能仍可照常使用，程序运行期间的占用情况见下节，实机共存尚待核实。

协议来自公开的社区逆向资料和网页驱动的公开行为，仅用于互通和显示电量。几格与各厂商无关联，也未获厂商授权。项目采用 [MIT 许可证](LICENSE)。

## 使用前提

几格面向平时关闭官方驱动界面和后台进程、按需调节设置的玩家。官方软件正在运行时，可能抢占接收器。INZONE Hub 尤其会独占 H9 的串口，探针遇到占用即显示“被占用”并结束本次读取。Windows 有时用同一个错误码表示独占和权限不足，探针会同时提示这两种可能。后续托盘版计划显示“被占用”及上一次成功读数和时间。本阶段探针每次独立运行，尚无历史缓存。

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

在仓库根目录运行以下命令。macOS 可构建托管项目并运行协议和模拟传输测试，设备枚举和读取在 Windows 上运行。

```sh
dotnet build -p:EnableWindowsTargeting=true
dotnet test
```

Windows CI 会还原依赖、执行测试、发布 win-x64 NativeAOT 程序，并把探针上传为 `jige-probe-win-x64`。本地 Windows 发布需要安装 Visual Studio 的 C++ 桌面开发工具链。

```powershell
dotnet publish src/Jige.Probe/Jige.Probe.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/win-x64
```

## 致谢与参考

- Finalmouse XPanel、官方 USB 权限清单以及 HaloBattery 的 ULX 报文与实机记录。[XPanel](https://xpanel.finalmouse.com/) · [USB 权限清单](https://github.com/teamfinalmouse/xpanel-linux-permissions) · [HaloBattery](https://github.com/HeyOkay/HaloBattery)
- InZoneH9Tray、openinzone 与 inzone-h9-mac 提供 H9 串口查询、帧格式及实机记录。[InZoneH9Tray](https://github.com/janit88/InZoneH9Tray) · [openinzone](https://github.com/penguinwokrs/openinzone) · [inzone-h9-mac](https://github.com/jpcarranza94/inzone-h9-mac)

协议字段、出处及各字段的验证状态见 [协议记录](docs/protocols.md)。

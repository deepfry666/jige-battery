# 几格 / Jige Battery

几格计划做成 Windows 11 托盘工具，用数字显示经 2.4 GHz USB 接收器连接的无线外设电量。第一阶段提供设备读取核心库和一次性命令行探针，托盘仍待实现。探针运行一次即退出，平时没有常驻进程或后台轮询。

| 探针适配器 | 识别方式 | 证据等级 | 实机验证状态 |
| --- | --- | --- | --- |
| Finalmouse UltralightX（ULX） | 2.4 GHz 接收器 `361D:0100`，厂商 HID 集合 `FF00:0001` | 单一来源 | 未在实机验证 |
| Sony INZONE H9 一代 | PC 档接收器 `054C:0E53` 的 `MI_06` USB 串口 | 多方开源实现一致 | 未在实机验证 |
| [Logitech HID++ 2.0](docs/protocols.md#logitech-hid-20) | 已知 Lightspeed、Bolt、Unifying 接收器逐槽查找，例如 PRO X SUPERLIGHT 2、G309、G515 | 多方开源实现一致 / 单一来源，按型号 | 未在实机验证 |
| [Logitech Centurion](docs/protocols.md#logitech-centurion-与-astro-a50-gen-5) | G PRO X 2、G522、G PRO X 3、G325 的无线接收器 | 多方开源实现一致 / 单一来源，按型号 | 未在实机验证 |
| [ASTRO A50 Gen 5](docs/protocols.md#logitech-centurion-与-astro-a50-gen-5) | 底座 `046D:0B1C` | 单一来源 | 未在实机验证 |
| [Razer 90B](docs/protocols.md#razer-90b-feature) | 14 款鼠标、3 款键盘的无线接收器，逐型号匹配 PID、接口、TID | 多方开源实现一致 / 单一来源，按型号 | 未在实机验证 |
| [Razer BlackShark V2 HyperSpeed](docs/protocols.md#razer-pa-与-blackshark-64b) | `1532:0565`，MI_03、`FF14:0001`，直接查询 | 多方开源实现一致 | 未在实机验证 |
| Razer PA、BlackShark V3 系列 | PA 的模式切换及 V3 的冷启动会话风险尚待核验，保持禁用 | 单一来源 | 未在实机验证 |

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
```

- `list` 列出系统 HID 接口的描述符信息和 USB 串口的 COM 号、硬件 ID。读不到的字段显示占位符。
- `read` 对识别出的设备各查询一次。ULX 和 HID++ 的电压估算会标记“电压估算”。罗技 HID++ 接收器下已确认的槽各有一条记录，已配对但不可达的槽显示“离线”。H9 默认使用 115200，也可独立选 460800。`both` 会分别尝试两个波特率，每次单独打开并关闭串口。遇到占用、权限错误或设备拔除时结束尝试。
- `--raw` 输出十六进制收发帧和串口残片。按 `Ctrl+C` 可取消。
- 反馈问题时，把 `list` 和 `read --raw` 的输出、设备型号、固件版本、耳机开关档位及官方驱动是否在运行一起交给开发者。公开分享前可将设备路径中的实例标识和原始回复里的设备序列号打码，保留 VID、PID、接口号和报文长度。

```powershell
.\Jige.Probe.exe list 2>&1 | Tee-Object -FilePath jige-list.txt
.\Jige.Probe.exe read --raw 2>&1 | Tee-Object -FilePath jige-read.txt
```

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

- Solaar（GPL-2.0，部分文件 GPL-2.0-or-later）、OpenLogi（MIT 或 Apache-2.0）、OpenMouse mouse-protocol（AGPL-3.0）提供 HID++ 的槽、身份和电量协议资料。[Solaar](https://github.com/pwr-Solaar/Solaar) · [OpenLogi](https://github.com/AprilNEA/OpenLogi) · [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol)
- HeadsetControl（GPL-3.0）提供 Centurion、G325 和 ASTRO Gen 5 的报文与实机记录。[HeadsetControl](https://github.com/Sapd/HeadsetControl)
- OpenRazer（GPL-2.0）、Razer Battery Report（MIT）、HaloBattery（MIT）、BlackShark V2 HyperSpeed WebHID（MIT）与 Synapse-for-Linux（MIT）提供雷蛇 PID、接口、读电量与充电的报文和实机证据。[OpenRazer](https://github.com/openrazer/openrazer) · [Razer Battery Report](https://github.com/xzeldon/razer-battery-report) · [HaloBattery](https://github.com/HeyOkay/HaloBattery) · [V2 HyperSpeed WebHID](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid) · [Synapse-for-Linux](https://github.com/bbesli/Synapse-for-Linux)

几格依据公开协议字段和实机报文独立实现，保留 MIT 许可。许可出处见 [设备清单](research/device-catalog.md#6-参考实现许可证)。

协议字段、出处及各字段的验证状态见 [协议记录](docs/protocols.md)。

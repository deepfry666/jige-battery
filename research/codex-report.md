# Windows 11 无线外设托盘电量调研

| 设备 | 连接方式 | 首选读取路径 | 可行性与估计 | 主要来源 |
|---|---|---|---|---|
| Finalmouse UltralightX（ULX） | 2.4 GHz，USB 线 | 接收器 `361D:0100`，HID `FF00`，Output 04 / Input 05 | 容易，0.5–1.5 天 | [HaloBattery 实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/finalmouse.py) |
| Finalmouse Starlight X（SLX） | 2.4 GHz，USB 线 | 接收器 `361D:0300`，HID `FF00`，Output 01 | 容易，1–2 天 | [厂商 USB 规则](https://github.com/teamfinalmouse/xpanel-linux-permissions/blob/main/70-finalmouse.rules)、[OpenMouse 实现](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/finalmouse/hid.ts) |
| Finalmouse Starlight-12 | 2.4 GHz，USB 线 | 先识别实际接收器，再核查是否有电量遥测 | 需逆向，2–5 天做可行性验证 | [厂商产品目录](https://finalmouse.com/pages/mice)、[接收器 FCC 档案](https://fccid.io/2AZX7-SL12D) |
| ATK X1 / F1 / A9 | 多数为 2.4 GHz、USB 线。部分 X1 V2 有蓝牙，部分 A9 为 NearLink | 按 HID 描述符和型号选 Compx 或 BitMouse，蓝牙版先查 PnP | 已验证组合容易，0.5–2 天。未知 SKU 需逆向，1–3 天 | [ATK HUB](https://hub.atk.pro/)、[atkctl](https://github.com/cobanov/atkctl)、[atk-control](https://github.com/NasirA1991/atk-control) |
| 游狼 G-Wolves | 常见电竞款为 2.4 GHz、USB 线 | 按接收器选 Feature 往返或 Compx 查询 | 已验证组合容易，0.5–1.5 天。未知 SKU 需逆向，1–3 天 | [亚太代理](https://www.g-wolves.cn/pages/g-wolves-htx-4k-wireless-mouse)、[网页驱动](https://mouse.xyz/)、[开源实现](https://github.com/maximelonguepe/gwolves-battery-overlay) |
| Sony INZONE H9 一代 | 2.4 GHz USB 接收器、蓝牙，USB-C 充电 | PC 模式 `054C:0E53` 的 COM 口。蓝牙先查 PnP | COM 容易，0.5–1 天。蓝牙 0.5 天验证，结果未核实 | [Sony 手册](https://helpguide.sony.net/mdr/295960/v1/en/index.html)、[InZoneH9Tray](https://github.com/janit88/InZoneH9Tray)、[openinzone 协议](https://github.com/penguinwokrs/openinzone/blob/main/docs/PROTOCOL.md) |

调研日期 2026-11-19。工期按一人拿到实机后移植电量适配器和做基础验证估算，不含公共 UI。下文的实测指来源作者报告的硬件结果。用户的具体 SKU、接收器、固件、Windows 版本和实际功耗均未核实。

## 1. Finalmouse

**ULX / UltralightX** 是同一系列。官方 [XPANEL](https://xpanel.finalmouse.com/) 是网页配置器，使用 WebHID 并显示电量。已知接收器为 `361D:0100`，有线设备 `361D:0102`。厂商权限文件还列出其他 ULX ID，清单本身不足以确定每个 ID 的功能。HaloBattery 报告其测试机有线接口只用于固件更新，充电期间仍经接收器查电量。其他批次的有线读数未核实。蓝牙未见官方支持证据。 [厂商权限文件](https://github.com/teamfinalmouse/xpanel-linux-permissions/blob/main/70-finalmouse.rules)、[HaloBattery 实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/finalmouse.py)

ULX 接收器在 `FF00` vendor collection 上发送 Output ID `04`，其后 63 字节，正文为长度、带高位标志的命令、载荷长度、载荷及填零。例如电量请求以 `04 02 A6 00` 开头。Input ID `05`，回复命令 `26`，载荷第 1 字节为百分比，第 2–3 字节为小端 mV。部分固件不回 `26`，可用 `05` 命令获取 mV 并按 XPANEL 曲线估算，UI 应标为估算值。命令 `24` 查链路，`25` 查充电。命令号和报告 ID 是两个字段。 [HaloBattery 实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/finalmouse.py)、[ULX 逆向记录](https://github.com/johnklucinec/ulx_reverse)

**Starlight X（SLX）** 是另一代。厂商文件明确列出普通接收器 `361D:0300`、鼠标 `361D:0310` 和独立的 bootloader ID。OpenMouse 从 XPANEL 得到 SLX 的 `FF00` 接口、Output ID `01` 和输入报告里的电量、充电状态。可参考其协议库和单独的硬件实测报告，按具体接收器核实输入帧，避免沿用 ULX 的 Output ID。 [厂商权限文件](https://github.com/teamfinalmouse/xpanel-linux-permissions/blob/main/70-finalmouse.rules)、[OpenMouse 驱动](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/finalmouse/hid.ts)、[Mouse Battery 作者实测说明](https://marketplace.elgato.com/product/mouse-battery-209a2aa9-7421-4ffe-8002-bced67409821)

**Starlight-12** 包括 Phantom、Poseidon 等旧款。FCC 档案证实有独立接收器。官方 XPANEL 的旧款支持、具体 USB VID/PID、桌面软件电量百分比和第三方查询协议均未核实。开源兼容表把 Starlight-12 和 ULX 合写但引用的是 ULX 接收器 PID，实机识别必须分开。旧固件工具只能作为识别线索。如果官方工具及抓包都没有电量读数证据，首版可交付未知状态，精确百分比的可行性留待进一步取证。 [厂商产品目录](https://finalmouse.com/pages/mice)、[FCC 档案](https://fccid.io/2AZX7-SL12D)、[OpenMouse 驱动](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/finalmouse/hid.ts)

**共存、权限、耗电。** XPANEL 以普通桌面浏览器的设备授权访问 vendor collection，原生程序通常也可用标准用户打开相同集合。Windows 具体 ACL、XPANEL 网页或桌面版是否独占、并发回复是否会串线均未核实。ULX 电量命令由接收器发给鼠标，睡眠是否被打断没有实测结论。关闭 XPANEL 建立基线，再单独跑共存和休眠实验。

## 2. ATK

**连接和官方软件。** X1、F1 常见性能款支持 2.4 GHz 接收器与 USB 线，部分 X1 V2 支持蓝牙。A9 有 2.4 GHz 与 NearLink SKU，A9 Plus NK 应按 NearLink 接收器识别。ATK HUB 网页版使用 WebHID 显示电量，厂商也提供下载入口，桌面客户端版本和实际 UI 刷新节奏未核实。用户 X1/F1/A9 的 PID 未核实，不能仅按品牌字符串或系列名称路由。 [ATK 官网型号比较](https://www.atk.store/es/blogs/noticias/atk-f1-vs-atk-x1)、[ATK HUB](https://hub.atk.pro/)、[atkctl](https://github.com/cobanov/atkctl)

- **A9 Plus NK，BitMouse。** 开源作者实测鼠标 `373B:1263` 和 NANO 接收器 `373B:1216`。`FF05:0001`，Output / Input ID 都是 `08`，后接 63 字节。按不含 ID 的偏移，0 是字节 1–62 的低 8 位和，1 是 `72`，4 是目标（经接收器查鼠标为 1），5 是命令，7 是回复载荷首字节。`07` 查百分比，`0F` 查充电。 [atkctl 协议与硬件记录](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)
- **A9 Ultra，Compx。** atkctl 实测鼠标 PID `1119`、8K 接收器 PID `111A`，此处 VID 未单独核实。`FF04`，Feature ID `08` 请求，Input ID `08` 回复，含 ID 17 字节。电量命令 `04`，整帧含校验和相加低 8 位为 `55`。另一个 A9 Ultra 实现可交叉核对帧和字段，仍以本机描述符决定传输方式。 [atkctl 协议](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)、[A9 Ultra 驱动](https://github.com/xb-bx/atk-a9-ultra-driver)
- **A9 Ultimate / X1 / F1。** atk-control 作者报告 A9 Ultimate 回应旧 `FF02` Compx 协议；其 X1 Pro Max 是识别范围，单独实测未核实。F1 按实机核对使用页和报告长度，再在 ATK HUB 对应 JS 分支对照电量命令。蓝牙版先查 Windows PnP，只有发现 BAS 才走 GATT。 [atk-control](https://github.com/NasirA1991/atk-control)、[ATK HUB](https://hub.atk.pro/)

**共存、权限、耗电。** 普通用户访问 vendor HID 是默认验证路径。ATK HUB 同时查电量、改设置或独占句柄的行为未核实。Compx 和 BitMouse 都可能由接收器向鼠标转发命令，休眠和耗电影响未核实。测试时先关 HUB，禁止切到固件、配对和设置功能，低频查询并在鼠标空闲时暂停。

## 3. 游狼 G-Wolves

亚太代理明确使用“游狼 G-Wolves”。常见无线产品有 Hati 系列 HTS Plus / HT-S2、HTX / HTX Mini、HSK Pro，以及 Fenrir、Lycan 等。常见竞赛款使用专用接收器和 USB 线，蓝牙支持按 SKU 核查，目前未核实。旧款有型号专用 Windows 驱动，HTX 4K 的官方更新日志明确提到修复电量显示。现代网页驱动用 WebHID，并可通过刷新读取电量。 [亚太代理 HTX 4K](https://www.g-wolves.cn/pages/g-wolves-htx-4k-wireless-mouse)、[HTS Plus 4K](https://www.g-wolves.cn/pages/g-wolves-hts-plus-4k-wireless-mouse)、[网页驱动](https://mouse.xyz/)

- **Feature 系列。** Fenrir / Lycan Asym 8K 已有实测接收器 `33E4:3517` 和线连 `33E4:3508`。网页分支的 Feature 查询以 `00 00 02 02 00 83` 开头，返回帧中 `A1 … 83` 后为充电状态和百分比。开源项目的 Windows 默认缓冲区为 65 字节（含 ID），HaloBattery 按自身枚举条件选 64 字节 Feature 集合。移植时按实机 `HidP_GetCaps` 和库的 ID 计长约定确定填充。 [gwolves-battery-overlay](https://github.com/maximelonguepe/gwolves-battery-overlay)、[HaloBattery 协议笔记](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md)
- **Compx 系列。** Fenrir Pro Receiver RS `33E4:3854`、线连 `33E4:3619`，作者实测用 ID `08` 的 17 字节报告，命令 `04`，可读百分比、充电和电压。即使鼠标同名，也应以接收器版本和描述符选分支。 [gwolves-battery-overlay](https://github.com/maximelonguepe/gwolves-battery-overlay)
- **较旧 WebHID 分支。** HaloBattery 从网页型号表整理出 HSK Pro ACE 接收器 `33E4:5803`，旧协议候选无线 Feature 前缀 `00 02 8F 01`，回复 `A1 02 8F …` 后带充电状态与百分比。该项目明确标记未实机核实。另一个项目收到 HSK Pro 能读的用户反馈，仍不能据此确认 ACE 或其他批次。HTX / HTS 的具体 VID/PID 和用户型号未核实。 [HaloBattery 协议笔记](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md)、[gwolves-battery-overlay](https://github.com/maximelonguepe/gwolves-battery-overlay)

**共存、权限、耗电。** 通常先以标准账户访问 vendor collection。网页和旧版 Windows 驱动是否长期占用、Feature 回复是否会被另一请求覆盖均未核实。休眠时的行为也未核实。对当前型号先比对网页手动刷新结果，再验证原生查询的间隔和空闲暂停。

## 4. Sony INZONE H9 一代

**连接和官方显示。** H9 一代可同时使用 USB 接收器和蓝牙。PC 模式接收器 `054C:0E53`，PS5 模式 `054C:0E4C` 由逆向作者记录。USB-C 线用于充电，未找到以充电线当有线音频或电量数据通道的证据。Windows 桌面客户端 INZONE Hub 显示剩余电量，机身也能在蓝牙模式播报粗略电量。Windows 设置是否为这台 H9 暴露百分比未核实。H9 II 的 PID、接口和开源协议需单独验证。 [Sony H9 手册](https://helpguide.sony.net/mdr/295960/v1/en/index.html)、[一代接收器逆向](https://github.com/jpcarranza94/inzone-h9-mac)

**2.4 GHz 优先走串口。** Windows 开源实现在 `VID_054C&PID_0E53&MI_06` 下定位 COM 口，115200 波特，打开 DTR/RTS。依次发送两个初始化帧（源文件 `INIT_1`、`INIT_2`），再发电量查询 `01 00 FC 08 96 C3 41 04 01 01 00 A0`。示例解析长度 14、以 `04 FF 0B 00 96 C3 14 04` 开头的回复，以从 0 开始的索引 12 取百分比、索引 11 的 1 表示充电。实现应校验帧和范围，处理 COM 分段到达、超时及拔插。openinzone 另有帧结构和收发记录，可供交叉验证。 [InZoneH9Tray 源码](https://github.com/janit88/InZoneH9Tray/blob/main/inzone_tray.py)、[openinzone 协议](https://github.com/penguinwokrs/openinzone/blob/main/docs/PROTOCOL.md)

**冲突和权限。** 已有 Windows 项目报告 INZONE Hub 占用同一 COM 口，建议验收时对照 Hub 完全退出和运行两种状态。端口被占用时显示上次值及时间，低频退避。管理员权限一般无法解除独占，标准用户访问已枚举 COM 口是第一条测试路径，本机 ACL 未核实。串口初始化和定期查询是否影响耳机自动关机或低功耗未核实。 [InZoneH9Tray](https://github.com/janit88/InZoneH9Tray)、[HaloBattery 协议笔记](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md)

**蓝牙。** H9 文档列出经典音频配置文件。先用正常配对连接检查经典蓝牙及 Hands-Free 关联节点上是否有电量键，记录关闭和启用麦克风时该值是否出现，并比较 Windows 设置。原生 BAS 服务在 H9 上未核实。两条路径同时可用时，优先使用新鲜的接收器值，并在设备映射确认后合并成一个条目。 [Sony H9 手册](https://helpguide.sony.net/mdr/295960/v1/en/index.html)、[Windows 蓝牙电量开源实现](https://github.com/dkamioka/bluetooth-battery-tray/blob/main/src/BluetoothBatteryTray.cs)

## 5. 通用读取与架构

- **经典蓝牙 PnP 判断基本成立，覆盖率取决于设备和 Windows。** 多个实现用 SetupAPI 或 CfgMgr32 读取 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` 的单字节百分比。枚举 `BTHENUM`、`BTHLE` 和关联服务节点，按容器或已验证蓝牙地址关联，独立判断连接与数据新鲜度。微软正式文档中的 Shell 属性 `System.Devices.BatteryLife` 使用另一组 GUID/PID，直接调用 PnP 时按实际键读取，不能按名称推定两者键值相同。读系统缓存本身不要求主动建立蓝牙连接。 [Windows 实现](https://github.com/dkamioka/bluetooth-battery-tray/blob/main/src/BluetoothBatteryTray.cs)、[微软 Shell 属性](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-batterylife)
- **GATT 是条件性补充。** 实机公开服务 `0x180F` 和 Battery Level 特征 `0x2A19` 时可用 WinRT 读单字节 0–100，设备支持通知时优先订阅。先读缓存，确有需要再尝试非缓存读取。主动读可能建立连接、触发无线活动或超时。 [Bluetooth SIG Battery Service](https://www.bluetooth.com/specifications/specs/battery-service/)、[微软 GATT 客户端](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)
- **2.4 GHz 常需厂商适配，传输以描述符为准。** HID++ 只是同类先例，Sony 一代是 COM 例外。Windows 用 SetupAPI 枚举 HID 接口、`HidP_GetCaps` 确认 TLC 和报告长度，Output/Input 用 `WriteFile` 与异步 `ReadFile`，Feature 用 `HidD_SetFeature/GetFeature`。只访问对应 vendor collection。匹配 PID、产品字符串、usage、报告长度和响应命令；单接收器串行请求，热拔插取消 I/O。HID 共享打开成功只表示句柄可用，仍需验证应用层的请求和回复不会相互干扰。 [微软 HID 发送说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/sending-hid-reports)、[微软 HID 接收说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
- **WebHID 网页驱动是有效的逆向资料。** DevTools Sources 搜 `sendReport`、`sendFeatureReport`、`receiveFeatureReport`、`inputreport`、`battery`，追踪型号表和封帧函数。WebHID 将 report ID 与载荷分开传入，Windows 原生缓冲区通常包含 ID 字节。抓包时记录版本、方向和具体 collection，防止把常规鼠标输入、启动初始化或固件维护帧当成电量查询。 [ATK HUB 与协议记录](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)、[OpenMouse WebHID 驱动](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/finalmouse/hid.ts)

**首版选型。** C# / .NET 10 + Win32 托盘 + 延迟 WPF 合适。托盘宿主先启动 Win32 消息循环和设备服务，首次点开才初始化 WPF，在同一 STA 上处理简单面板。关面板后 CLR 和已加载的 WPF 组件仍可能驻留，需分别评估首次打开前后的占用。可用一个数字图标展示用户选中的主设备，点击查看四台及状态。图标使用 DPI 适配的位图，在内容变化时更新，测试 `100` 的可读性、Explorer 重启后恢复和 Win11 溢出区。图标能否始终可见由 Windows 托盘设置决定。 [微软 .NET 支持周期](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)、[Shell_NotifyIcon 文档](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)

若面板只需要四行设备和少量操作，可先用 Win32 popup 或简单 WinForms，实测后再决定是否引入 WPF。纯 C++ 或 Rust + Win32 更适合有明确个位数 MiB 常驻预算的场合，代价是多写 UI 和设备生命周期代码。WinUI 3 适合有现代 Windows UI 需求的产品，这里会增加 Windows App SDK 依赖和启动路径，首版收益有限。 [Windows App SDK 部署说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)、[原生 Rust 托盘开源样例](https://github.com/ryanlewis/mousebatt)

**性能验收建议。** 用发布版、同一目标机和四台设备测量。建议将后续验收预算设为首次打开前私有工作集 ≤30 MiB，打开并关闭后 ≤80 MiB。CPU 用 30 分钟平均核时衡量，目标低于单个逻辑核的 0.1%，同时关注查询瞬间和游戏帧时间。这些是初始预算，当前没有实测数据。记录私有工作集、提交量、句柄数和冷启动到托盘时间，分别测空闲、游戏中、设备断开以及唤醒后。资源控制优先依赖热插拔通知、缓存和低频查询。初始间隔可取 60–120 秒，空闲暂停主动鼠标查询，手动刷新和恢复连接后允许单次查询。缓存值带时间戳，区分离线、睡眠、端口忙与未知。 [Windows Performance Recorder](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder)

## 6. 首轮验证实验

1. **先盘点设备。** Windows 设备管理器按连接查看，逐个插拔接收器，记录机身 SKU、固件、硬件 ID、接口号、产品字符串和 COM 口，USB 线和接收器分开查。蓝牙模式对 H9 用系统自带 PowerShell 的 `Get-PnpDevice` 和 `Get-PnpDeviceProperty` 查上述 GUID 属性，比较系统设置、断开及重连前后。先用现有工具即可。
2. **再确认一条基准读数。** H9 一代接收器置 PC 档，确认 `054C:0E53` 与 COM 口，记录 INZONE Hub 电量，完全退出 Hub 后用已有的开源参考工具在标准用户下验证。若工具尚未具备，先完成前一步和网页核对。鼠标优先做 ULX 或已匹配的 A9 / Fenrir，用官方网页刷新读数，记录充电和断开两个对照状态。
3. **缺协议才抓包。** 先从官方 WebHID JS 对照分支；如果目标机已具备 USBPcap / Wireshark，抓同一接收器上页面打开、单次电量刷新、充电切换、鼠标关机四段。按 USB 设备地址定位控制传输与 interrupt IN/OUT，记录请求、回复及时间。COM 路径也可在 USB 层抓收发。若工具未安装，把抓包列入后续另行安排，当前可完成设备清单和 JS 核对。 [USBPcap](https://desowin.org/usbpcap/)、[Wireshark USB 指南](https://wiki.wireshark.org/CaptureSetup/USB)
4. **确认共存与休眠。** 做官方工具单开、读数工具单开、两者同时开三组。用相同固件和休眠设置，对照首次静置入睡、持续休眠、手动唤醒、拔插、重连。同步记录最近一次有效值、超时、回复串线、端口忙和游戏输入异常。要确认额外耗电，用多小时同条件有查询／无查询对照；托盘百分比变化和 USB 接收器功耗只能作间接线索。实测发现延迟入睡或提前唤醒时，按该型号禁用空闲轮询，保留上次值。

首版落地顺序建议按 Sony H9 COM、已确认型号的 ATK/G-Wolves 或 ULX、支持标准属性的蓝牙设备推进。若 Finalmouse 是 Starlight-12，先完成接收器识别和遥测可行性验证，其他三台可独立交付。

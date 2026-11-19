# Windows 11 无线外设托盘电量工具 — 调研与可行性报告

> 调研日期：2026-11-19  
> 调研范围：第一阶段 4 台目标无线外设（Finalmouse 鼠标、ATK 鼠标、游狼 / G-Wolves 鼠标、Sony INZONE H9 耳机）的电量读取协议、Windows 系统接口、现有技术判断核实、整体技术栈评估与首步验证方案。  
> 原则：所有未在公开资料或源码中确认的报文细节、PID 或休眠行为均明确标注 **“未核实”**，不编造协议字节。

---

## 汇总表（设备 × 连接方式 × 读取路径 × 可行性 × 主要来源）

| 设备 | 连接方式 | 推荐读取路径（Windows 11） | 可行性评级（预估工作量） | 主要来源 |
| :--- | :--- | :--- | :--- | :--- |
| **Finalmouse 鼠标**<br>① **UltralightX (ULX) 系列**<br>② **Starlight-12 / Starlight Pro** | 2.4GHz 接收器 + USB-C 有线（无蓝牙） | **ULX**：USB HID Vendor Usage Page `0xFF00:0x0001`（VID `0x361D`，接收器 PID `0x0100`），查询电压命令 `0x05` 换算百分比 + `0x37` 充电状态。<br>**Starlight-12 / Pro**：无软件/无公开 USB 电量接口。 | **ULX：容易**（1–2 天）<br>**Starlight：基本不可行** | [teamfinalmouse/xpanel-linux-permissions](https://github.com/teamfinalmouse/xpanel-linux-permissions)、[krisgfx9/Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor)、[red0x0002/xpanel-custom-dpi](https://github.com/red0x0002/xpanel-custom-dpi)、[XPanel](https://xpanel.finalmouse.com) |
| **ATK 鼠标**<br>（烈空 X1 / F1、蜻蜓 A9 等） | 三模：2.4GHz（1K–8K 接收器）+ 蓝牙 + USB-C 有线（部分 A9 含星闪） | **2.4G/有线（主力）**：USB HID Vendor Usage Page `0xFF02:0x0002`，Report ID `0x08`（VID `0x373B` / Compx 方案 `0x3554`），16 字节帧 + `OP 0x04` 查电量/电压/充电状态。<br>**蓝牙（辅助）**：`DEVPKEY_Bluetooth_Battery` / BLE GATT `0x180F`（实机未核实）。 | **容易**（0.5–1 天） | [Fan4Metal/ATK_tray](https://github.com/Fan4Metal/ATK_tray)、[noosxe/atk-tool](https://github.com/noosxe/atk-tool)、[NasirA1991/atk-control](https://github.com/NasirA1991/atk-control)、[cyberphantom52/libatk-rs](https://github.com/cyberphantom52/libatk-rs)、[wushuai0802/myfinder-device-battery](https://github.com/wushuai0802/myfinder-device-battery) |
| **游狼（已确认 = G-Wolves）**<br>（HTS Plus / HTX / HSK Pro / HTR / Fenrir 等） | 2.4GHz 接收器 + USB-C 有线（无蓝牙） | **2.4G/有线**：USB HID Interface 2 的 **Feature Report**（VID `0x33E4`），先 `SET_REPORT` 写入 64 字节查询头，延迟 10–100ms 后 `GET_REPORT` 读回电量字节（分新旧两代指令头）。 | **容易 ～ 轻微需逆向**（1–2 天） | [marcp.xyz (`gwolves-battery`)](https://marcp.xyz)、[OpenMouse-Project/mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol)、[OpenMouse-Project/openmouse](https://github.com/OpenMouse-Project/openmouse)、[HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)、[mouse.xyz](https://www.mouse.xyz) |
| **Sony INZONE H9 耳机**<br>（一代 `WH-G900N`；附注 `H9 II`） | 2.4GHz USB 接收器 + 经典蓝牙（支持同时双连）；USB-C 仅充电与配置，不支持有线音频 | **一代 H9（2.4G 主力）**：**非标准 HID**！接收器（`054C:0E53`）暴露 **USB VCOM / CDC-ACM 虚拟串口（115200 baud）**，需走串口读写索尼私有帧。<br>**H9 II**：支持原生 USB HID 控制。<br>**蓝牙（备用）**：`DEVPKEY_Bluetooth_Battery`（Win11 原生上报不稳定）。 | **容易（有现成开源实现），但串口强独占冲突**（1–2 天） | [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)（PR #278 / v1.16.0）、[patyhank/linzone-hub](https://github.com/patyhank/linzone-hub)、[Sapd/HeadsetControl](https://github.com/Sapd/HeadsetControl) |

> **关键发现**：开源项目 **[HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)**（Windows 托盘外设电量工具）的生态已不同程度覆盖/讨论了本次 4 台设备中的多款（包括 Sony INZONE H9 `054C:0E53` 串口方案、G-Wolves、Finalmouse 等），极具直接参考价值。

---

## 一、对已有技术判断的核实与修正

### 1. 经典蓝牙 `DEVPKEY_Bluetooth_Battery` 与 BLE GATT `0x180F`
- **结论：基本核实成立，但有 3 个工程注意事项。**
- **核实细节**：
  - `DEVPKEY_Bluetooth_Battery` 对应 `DEVPROPKEY` 为 `FormatId = {104EA319-6EE2-4701-BD47-8DDBF425BBE5}`、`PropertyId = 2`，值类型为 `DEVPROP_TYPE_BYTE`（`0x03`，取值 `0–100`）。在 C# 中可直接通过 P/Invoke 调用 `setupapi.dll`（`SetupDiGetClassDevs` + `SetupDiGetDevicePropertyW`）或 `cfgmgr32.dll`（`CM_Get_DevNode_PropertyW`）读取，**无需管理员权限**。
  - BLE GATT Battery Service 确为 UUID `0x180F`（Battery Level Characteristic 为 `0x2A19`），Windows 11 可用 WinRT `Windows.Devices.Bluetooth.GenericAttributeProfile` 读取；同时 Windows 也会将许多 BLE 设备的 GATT 电量自动同步进 `DEVPKEY_Bluetooth_Battery`。
- **必须注意的坑**：
  1. **节点过滤**：经典蓝牙电量往往挂在 `BTHENUM` 下的 HFP（Hands-Free）子设备节点，BLE 设备挂在 `BTHLEDEVICE` 下，枚举时建议扫描 `DIGCF_PRESENT | DIGCF_ALLCLASSES` 再按节点属性过滤，不能只看音频端点。
  2. **并非所有蓝牙耳机都稳定上报**：经典蓝牙依赖 HFP 的 `AT+IPHONEACCEV` 等扩展；实测社区反馈 **Sony INZONE H9 在 Windows 11 原生蓝牙设置中经常不显示或电量卡住**，且游戏玩家通常把 INZONE H9 的 2.4G 接收器插 PC、蓝牙连手机，因此对这 4 台设备而言，**蓝牙路径只能当“兜底补充”，真正的核心全部在 2.4G 接收器上**。
  3. **BLE 轮询耗电**：若直接通过 WinRT `GattDeviceService` 主动连接读 `0x2A19`，频繁轮询会阻止低功耗外设休眠；应优先读 Windows 缓存的 PnP 属性，必要时用 GATT Notification（`ValueChanged`）订阅。

### 2. 2.4GHz 接收器私有 HID 协议与驱动冲突
- **结论：部分成立，但存在一个关键反例（INZONE H9 走串口而非 HID），且冲突机制需要细分。**
- **修正与补充**：
  1. **权限与系统拦截边界**：Windows 为防键盘记录器，会由内核驱动（`Mouclass`/`Kbdclass`）独占标准系统鼠标/键盘集合（`UsagePage 0x01, Usage 0x02/0x06`），对其用 `CreateFile` 请求 `GENERIC_READ | GENERIC_WRITE` 会返回 `ERROR_ACCESS_DENIED`。但游戏外设的控制/电量通道几乎都在 **Vendor-Defined Usage Page（`0xFF00–0xFFFF`）** 上，Windows **允许普通非管理员用户**带 `FILE_SHARE_READ | FILE_SHARE_WRITE` 共享打开。
  2. **关键反例 —— 并非都是 HID**：Sony INZONE H9 一代（`054C:0E53`）的 2.4G 接收器走的是 **USB CDC-ACM 虚拟串口（COM 口）**。Windows 上 COM 口是**绝对独占**的：只要官方 INZONE Hub 在后台挂着，第三方程序根本打不开 COM 口（会报访问被拒绝）；反之第三方工具一直占着 COM 口，INZONE Hub 也会永久转圈“未连接”。
  3. **HID 共享模式下的“协议层抢包”**：即便 HID 句柄共享打开成功，如果厂商客户端和本工具同时向同一 Vendor 端点发命令+读响应，两个程序会互相读走对方的回包导致错乱。**好消息是**：本阶段 3 只鼠标（Finalmouse、ATK、游狼）均已采用 **WebHID 网页驱动**，玩家平时打游戏根本不会一直开着驱动网页，因此鼠标侧的冲突在实际使用中**几乎为零**。

### 3. 国产鼠标 WebHID 网页驱动可从 JS 看报文
- **结论：完全核实成立，且远不止国产鼠标。**
- **补充**：4 台设备中的 **3 只鼠标全都有官方 WebHID 网页驱动**（Finalmouse 的 `xpanel.finalmouse.com`、ATK 的 `hub.atk.pro`、游狼的 `mouse.xyz`）。比起硬啃打包混淆后的前端 JS，更省时的实操手段是在 Chrome DevTools 里直接 monkey-patch `HIDDevice.prototype`（拦截 `sendReport`/`sendFeatureReport`/`receiveFeatureReport`/`inputreport`），几秒钟就能把明文十六进制报文全部打出来。

---

## 二、4 台目标设备的详细调研

### 2.1 Finalmouse 鼠标

> **首要风险提示**：Finalmouse 无线鼠标分**两大硬件代际**，能否读取电量截然不同，动手前必须先确认用户手上是哪一代。

1. **型号区分与连接方式**
   - **新一代：UltralightX（ULX，含 Cheetah / Lion / Tiger、ULX Competition、ULX Prophecy 等）**
     - 连接方式：**2.4GHz 高回报率无线接收器（最高 4K/8K）+ USB-C 有线**，**不支持蓝牙**。
     - USB VID/PID：
       - VID：`0x361D`（Finalmouse）
       - ULX 2.4G 无线接收器 PID：`0x0100`（`361D:0100`）
       - ULX 有线直连 PID：`0x0102`（`361D:0102`）
       - 官方 Linux udev 规则（`teamfinalmouse/xpanel-linux-permissions`）还涵盖 `0100–0111` 与 `0300–0312` 等变体/Bootloader 范围（各子型号精确 PID **未逐一核实**）。
   - **老一代：Starlight-12（含 Achilles / Hades / Zeus / Ares / Phantom / Pegasus / Poseidon）及 Starlight Pro（TenZ / The Last Legend）**
     - 连接方式：2.4GHz 接收器 + Micro-USB 有线，不支持蓝牙。
     - VID/PID：**未核实**（老款无线方案与 ULX 架构完全不同）。
2. **官方驱动/软件与电量显示方式**
   - **ULX 系列**：官方使用纯浏览器 **WebHID** 工具 **[XPanel](https://xpanel.finalmouse.com)**，无独立常驻 Windows 客户端。连接后在网页界面显示电池电量百分比与充电状态。
   - **Starlight-12 / Starlight Pro**：**官方明确不支持 XPanel，也没有任何官方电量显示软件**（仅提供一个独立固件升级器）。只能靠鼠标滚轮附近 LED 在低电量时闪红灯肉眼判断。
3. **Windows 第三方读取路径与报文线索**
   - **ULX 系列**：
     - 路径：打开接收器 `361D:0100`（或有线 `361D:0102`）暴露的自定义 HID 集合 **`UsagePage = 0xFF00`, `Usage = 0x0001`**。
     - 协议特征（来自 `Wireless-Device-Battery-Monitor` 等社区逆向讨论）：
       - 很多固件版本对“直接读电量百分比”指令（命令 `0x38`）**无响应**；
       - 实操需要改发**电压查询指令 `0x05`**，设备返回毫伏值（如 `4196 mV`–`4276 mV` 左右），再按类似 XPanel 的电压-电量查找表（LUT 放电曲线）换算为 0–100%；充电时电压会因充电电流虚高，属正常现象（与 XPanel 表现一致）；
       - **充电状态查询指令 `0x37`**，返回 `0x01`（充电中）或 `0x00`（未充电）。
       - **未核实项**：完整的 HID Report ID、精确包长、包头/校验字节及 XPanel 的精确 mV→% 曲线表尚未从原始抓包逐字节核实，需在第一步通过 XPanel WebHID hook 或参考项目源码确认。
   - **Starlight-12 / Pro**：未发现任何可靠公开的 USB 电量读取协议或成功开源案例（**未核实 / 基本不可行**）。
4. **开源项目与参考资料**
   - [teamfinalmouse/xpanel-linux-permissions](https://github.com/teamfinalmouse/xpanel-linux-permissions)（官方 `70-finalmouse.rules`，列出 `361d` 设备 PID 范围）
   - [red0x0002/xpanel-custom-dpi](https://github.com/red0x0002/xpanel-custom-dpi)（基于 XPanel 协议的自定义配置研究）
   - [krisgfx9/Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor)（记录了 `361D:0100` 与 `0x05`/`0x37`/`0x38` 电压查询逻辑）
   - [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)（相关设备支持与社区讨论）
5. **冲突、权限与休眠影响**
   - **冲突**：因 XPanel 是按需打开的网页，平时关闭网页时**无冲突**；仅在同时打开 XPanel 调参时可能存在命令抢包。
   - **管理员权限**：访问 `0xFF00` 自定义 HID 集合**不需要**管理员权限。
   - **休眠/耗电**：接收器在鼠标深度休眠时收到 `0x05` 指令是否返回超时/旧缓存，还是会唤醒鼠标本体，目前**未核实**；保守策略是低频轮询（≥60s）并在系统无鼠标活动时暂停查询、沿用缓存值。
6. **可行性评级与工作量**
   - **ULX 系列**：**容易**，预估 **1–2 天**（含实机抓包确认精确字节帧 + 移植电压曲线 + 休眠边界处理）。
   - **Starlight-12 / Pro**：**基本不可行**（硬件固件未对外提供软件电量接口，建议直接确认型号后决定是否放弃）。

---

### 2.2 ATK 鼠标（烈空 X1 / F1、蜻蜓 A9 等）

1. **连接方式与 USB VID/PID**
   - 品牌背景：ATK（艾泰克）与 VGN / VXE 同属一个外设技术生态，主打系列（烈空 X1、烈空 F1、蜻蜓 A9 等）普遍为 **三模连接：2.4GHz（1K/4K/8K 接收器）+ 蓝牙 + USB-C 有线**（A9 部分版本额外支持星闪 NearLink）。
   - USB VID/PID：
     - **主用 VID**：`0x373B`（ATK 品牌常用 VID，例如开源适配记录中 ATK A9 Air 接收器为 `373B:1278`）。
     - **共用方案 VID**：`0x3554`（Compx 康帕斯公版方案，部分 VGN/VXE/ATK 机型共享）。
     - 注：X1 / F1 / A9 各细分版（标准/Pro/Max/Ultimate、1K/8K 接收器、有线模式）PID 均不相同，具体到用户手上那只鼠标的 PID **未逐一核实**，需实机枚举确认。
2. **官方驱动/软件与电量显示方式**
   - 官方同时提供 **WebHID 网页驱动（[hub.atk.pro](https://hub.atk.pro) / [hub.atk.store](https://hub.atk.store)）** 与桌面客户端 **ATK V HUB**。两者均可实时显示电量百分比与充电状态（注：社区反馈旧固件存在电量卡 95%/10% 跳变 Bug，建议先通过官方驱动统一鼠标与接收器固件）。
3. **Windows 第三方读取路径与报文格式**
   - **2.4G / 有线主路径（成熟协议）**：
     - 接口：自定义 HID 集合，典型为 **`UsagePage = 0xFF02`, `Usage = 0x0002`, `Report ID = 0x08`**。
     - 已知帧结构（多个开源项目交叉印证）：
       - 16 字节固定帧：`[OP, CTX, ADDR_H, ADDR_L, LEN, DATA..., CHK]`
       - 校验和公式常见为：`CHK = (0x4D - sum(bytes[0..14])) mod 0xFF`（不同主控/固件版本的常量基准可能微调，**需以实机确认**）
       - 电量查询典型指令为 **`OP = 0x04`**，响应帧包含电量百分比、充电标志及实时电压。
     - **未核实项**：ATK 较新批次（如星闪 NearLink 或新款主控）是否沿用完全一致的 `0x08`/`0x04` 协议，需拿到实机后对照 `ATK_tray` / `atk-tool` 或抓一次 `hub.atk.pro` 确认。
   - **蓝牙备用路径**：三模切换至蓝牙办公时，理论上可由 Windows `DEVPKEY_Bluetooth_Battery` 或 BLE GATT `0x180F` 读取，但该系列蓝牙模式是否标准上报**未核实**。
4. **开源项目与参考资料（生态最丰富）**
   - [Fan4Metal/ATK_tray](https://github.com/Fan4Metal/ATK_tray)（Windows 系统托盘电量工具，Python + `hidapi`，已扩展为 `mouse_tray`，明确支持 ATK F1 Ultimate、ATK A9 Ultimate 等及 VXE/VGN）
   - [noosxe/atk-tool](https://github.com/noosxe/atk-tool)（Go 模块化库与 CLI，支持 `373b`，可直接读电量百分比、电压与有线/无线充电状态）
   - [NasirA1991/atk-control](https://github.com/NasirA1991/atk-control)（Linux 原生控制中心，支持 ATK/MAD/VXE 4K/8K 接收器与托盘电量）
   - [cyberphantom52/libatk-rs](https://github.com/cyberphantom52/libatk-rs)（Rust 协议库）
   - [wushuai0802/myfinder-device-battery](https://github.com/wushuai0802/myfinder-device-battery)（含 ATK A9 Air `373B:1278` 适配记录）
5. **冲突、权限与休眠影响**
   - **冲突**：若后台常驻桌面版 **ATK V HUB**，同时读写 `0xFF02` 接口可能引发命令抢包；建议卸载或关闭桌面版，需要改设置时用网页版 `hub.atk.pro` 即可避免常驻冲突。
   - **管理员权限**：访问 `0xFF02` **不需要**管理员权限。
   - **休眠/耗电**：ATK 鼠标默认休眠较积极（很多固件默认 1 分钟进入休眠）。休眠期间向接收器发 `OP 0x04` 往往返回离线/失败或上一次缓存值，是否会唤醒鼠标本身**未核实**，建议结合 Windows `GetLastInputInfo` 或鼠标活动检测，闲置时降低/暂停轮询。
6. **可行性评级与工作量**
   - **容易**，预估 **0.5–1 天**（4 台设备中开源资料最完备、最稳的一台，直接移植 `ATK_tray` / `atk-tool` 协议即可）。

---

### 2.3 “游狼”（已确认品牌：G-Wolves）

1. **品牌与型号消歧**
   - **“游狼” = G-Wolves**（中文官网 `g-wolves.cn`，国际官网 `g-wolves.com`），是 FPS 电竞圈知名的超轻量化无线游戏鼠标品牌（不生产耳机，也不以机械键盘为主业）。
   - 常见无线型号：
     - **HTS 系列**：HTS Plus 4K / 8K（热门抓握对称鼠）
     - **HTX 系列**：HTX 4K / 8K / Ultra
     - **HSK / HTR 系列**：HSK Pro 4K / 8K / 2.0 8K、HTR（极致轻量化指尖鼠，20–35g）
     - **近年新款**：Fenrir（Max / Asym）、VUK、Lycan、Warg 等（多采用 Nordic 主控 + PAW3950/3955 传感器 + 4K/8K 接收器）。
2. **连接方式与 USB VID/PID**
   - 连接方式：主力无线型号均为 **2.4GHz 接收器（1K/4K/8K）+ USB-C 有线**，**不支持蓝牙**。
   - USB VID/PID：
     - **VID**：`0x33E4`（G-Wolves）
     - **PID**：随型号、有线/接收器形态及固件版本变化（例如社区已记录 Fenrir Max 有线 PID `0x3708`、8K 接收器 PID `0x3717`；另据逆向者记录，部分接收器更新固件后 PID 曾从 `0x5817` 变为 `0x0017`）。因此**代码匹配时建议按 VID `0x33E4` + HID 接口特征/集合匹配，不要只硬编码单个 PID**（其他型号精确 PID **未逐一核实**）。
3. **官方驱动/软件与电量显示方式**
   - 现代 Nordic 主控无线型号统一使用 **WebHID 网页驱动**：**[mouse.xyz](https://www.mouse.xyz)** / **[mouse.wiki](https://www.mouse.wiki)** / **[mouse.fit](https://www.mouse.fit)**（仅早期老型号曾有桌面驱动）。
   - 提示：G-Wolves 官方文档明确提醒早期固件存在电量读数不准/卡死的问题，需先通过网页驱动把鼠标与接收器更新到最新固件。
4. **Windows 第三方读取路径与报文格式**
   - 路径：通过接收器 **Interface 2**（自定义 HID 接口）收发 **HID Feature Report**（在 Windows 上对应 Win32 `HidD_SetFeature` 与 `HidD_GetFeature`；**切勿**在 Windows 上用 Zadig/libusb 替换驱动，否则会破坏鼠标正常使用）。
   - 已知事务流程（来源：Marc 的 `gwolves-battery` 与 OpenMouse 协议库）：
     1. **`SET_REPORT`**（USB 控制传输 `bRequest = 0x09`, `wValue = 0x0300`，即 Feature Report ID `0`），发送 64 字节 payload；
     2. **短延迟等待**：旧固件/型号（如 HSK Pro 8K）约 **10 ms**，较新固件/型号（如 HSK Pro 2.0 8K）需约 **100 ms**；
     3. **`GET_REPORT`**（`bRequest = 0x01`）读取返回 Feature Report，电量百分比位于返回数组的特定字节偏移（已知两代中位于第 5 或第 6 字节，视协议代际而定）。
   - 已知两代请求头：
     - **旧协议**（如 HSK Pro 8K）：`0x00 0x02 0x8F 0x01 ...`（其余补零至 64 字节）
     - **新协议**（如 HSK Pro 2.0 8K 及较新固件）：`0x00 0x00 0x02 0x02 0x00 0x83 ...`
   - **未核实项**：用户实际持有的游狼具体型号可能属于上述两代之一，或最新款（如 Fenrir/VUK/HTS Ultra）有微调字节位与充电状态位，需对照 `@openmouse/protocol/gwolves` 源码并用 `mouse.xyz` 抓一次包确认。
5. **开源项目与参考资料**
   - [marcp.xyz (`gwolves-battery`)](https://marcp.xyz)（约 200 行 C 工具，详尽记录了 `33E4` 两代 Feature Report 协议与延迟差异）
   - [OpenMouse-Project/mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol) 与 [OpenMouse-Project/openmouse](https://github.com/OpenMouse-Project/openmouse)（含 `@openmouse/protocol/gwolves` 协议编解码）
   - [Universal Mouse Drivers (`umdrivers.com`)](https://umdrivers.com)（含 G-Wolves WebHID 适配信息）
   - [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)（含 `33E4` 电量支持参考）
6. **冲突、权限与休眠影响**
   - **冲突**：纯 WebHID 驱动，日常不打开 `mouse.xyz` 时**无冲突**。
   - **管理员权限**：通过 Windows 原生 `HidD_SetFeature` / `HidD_GetFeature` 访问自定义 HID 节点**不需要**管理员权限。
   - **休眠/耗电**：鼠标休眠时发 `SET_REPORT` 是否会唤醒或导致返回 `0%`/错误值**未核实**；因 `GET_REPORT` 需等待 10–100ms，务必放在独立后台线程异步执行，绝不可阻塞 UI/托盘消息循环。
7. **可行性评级与工作量**
   - **容易 ～ 轻微需逆向**，预估 **1–2 天**（若落入已知两代协议则 1 天完成；若需用 `mouse.xyz` hook 补抓新款协议多加半天）。

---

### 2.4 Sony INZONE H9 耳机

> **核心结论**：一代 **INZONE H9（`WH-G900N`）** 的 2.4G 接收器走 **USB 虚拟串口（COM 口）**，而不是 HID！这是 4 台设备中架构最特殊、冲突风险最高的一台。

1. **连接方式与 USB VID/PID**
   - **一代 Sony INZONE H9（型号 `WH-G900N`）**：
     - 连接方式：**2.4GHz USB 无线接收器（带 PC / PS5 拨动开关）+ 经典蓝牙**，支持 **2.4G 与蓝牙同时双连**（典型玩家用法：2.4G 连 PC 打游戏，蓝牙连手机接电话/听通知）。
     - **USB-C 接口限制**：耳机本体 USB-C 口**仅用于充电与配合 INZONE Hub 软件配置，不支持有线 USB 音频**（支持边充边用无线）。
     - USB VID/PID：Sony VID **`0x054C`**，INZONE H9 / H7 2.4G 接收器 PID **`0x0E53`**（`054C:0E53`）。
   - **补充区分：Sony INZONE H9 II**
     - 后续新款 H9 II 增加了更标准的 **USB HID 硬件控制通道**，已被 `Sapd/HeadsetControl` 纳入原生 HID 支持（具体 H9 II PID **未核实**）。请先确认用户手上是一代 H9（`054C:0E53`）还是 H9 II。
2. **官方驱动/软件与电量显示方式**
   - 官方 Windows 桌面软件：**Sony INZONE Hub**。
   - 主要通过 2.4G 接收器通信并在软件界面显示剩余电量、降噪/环境音模式、EQ 等；仅蓝牙连接时 INZONE Hub 功能不可用/受限。
3. **Windows 第三方读取路径**
   - **路径 A：2.4G USB 接收器（一代 H9 主力路径）**
     - 接收器 `054C:0E53` 在 PC 模式下向系统暴露一个 **USB CDC-ACM 虚拟串口（VCOM）**，波特率 **`115200`**。
     - 第三方程序（如 `HeyOkay/HaloBattery`、`patyhank/linzone-hub`）通过定位 VID=`054C` & PID=`0E53` 对应的 `COMx` 端口，以 115200 baud 收发索尼私有串口指令帧获取电量百分比与充电状态。
     - **未核实项**：本次联网检索未直接获取到串口请求/响应的完整原始十六进制字节序列，但 **已有现成开源实现可直接查阅源码**（无需从零逆向）。
   - **路径 B：蓝牙（仅当蓝牙也连在 Windows 11 PC 上时作为备用）**
     - 可尝试读取 `DEVPKEY_Bluetooth_Battery`。但社区反馈 INZONE H9 在 Windows 11 原生蓝牙面板中电量上报**并不可靠**（部分系统不显示或数值滞后），因此**不应把蓝牙当作主力方案**。
   - **路径 C（仅适用于 H9 II）**：直接参考 `Sapd/HeadsetControl` 的原生 USB HID 读写路径。
4. **开源项目与参考资料**
   - [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)（Windows 托盘电量工具，v1.16.0 / PR #278 已实装 Sony INZONE H9 `054C:0E53` 通过 115200 baud 串口读取电量与充电状态）
   - [patyhank/linzone-hub](https://github.com/patyhank/linzone-hub)（Go CLI + Wails GUI + Linux DKMS 驱动，专门支持 Sony INZONE H9/H7 `054c:0e53` 的 USB VCOM/HID 模式与电量解析）
   - [Sapd/HeadsetControl](https://github.com/Sapd/HeadsetControl)（支持新款 INZONE H5 / H9 II 的原生 HID 控制与电量读取）
5. **冲突、权限与休眠影响**
   - **强烈冲突（必须重点设计）**：
     - Windows 上 **COM 串口是严格独占的**。只要官方 **Sony INZONE Hub** 在后台运行，就会锁死该虚拟串口，本工具打开 `COMx` 会直接失败；反之如果本工具一直保持串口打开，INZONE Hub 就会卡在加载页显示“未连接”（社区已知 Arduino IDE、SimHub 等串口扫描软件会导致 INZONE Hub 失联）。
     - **工程对策**：① 建议平时彻底关闭 INZONE Hub 常驻（降噪/EQ 多数设置可由耳机按键或临时打开调整）；② 本工具绝不要长连接占用 COM 口，而是采用 **“打开 COM → 查一次电量 → 立即 Close 释放句柄”** 的短连接模式，并在遇到 `UnauthorizedAccessException`（串口被占用）时优雅降级显示“被 INZONE Hub 占用”，不疯狂重试。
   - **管理员权限**：打开普通 USB 虚拟串口 `\\.\COMx` **不需要**管理员权限。
   - **休眠/耗电与音频干扰**：耳机关机后接收器仍在，查询只会超时或返回离线，不会唤醒关机的耳机；但串口查询期间是否会给 2.4G 无线音频带来极其轻微的爆音/抖动**未核实**，建议轮询间隔设为 **60 秒** 并在第一步实机听音测试。
6. **可行性评级与工作量**
   - **容易（可直接借鉴 `HaloBattery` / `linzone-hub` 串口帧源码）**，但串口发现与冲突容错要写稳；预估 **1–2 天**。

---

## 三、整体技术方案评价

原方案：**C# / .NET 10 + Win32 托盘（`Shell_NotifyIcon`）+ 延迟加载的 WPF 面板**。

### 1. 对原方案的客观诊断（特别是“延迟加载 WPF”的陷阱）
原方案在**开发效率**与**Windows API 亲和度**（SetupAPI、HidSharp/Win32 HID、SerialPort、WinRT BLE）上非常好，但有一个必须提前看清的内存与架构真相：

1. **.NET 10 中 WPF 仍不支持 NativeAOT**：启用 `<PublishAot>true</PublishAot>` 编译 WPF 会直接报错（WPF 深度依赖运行时反射与 XAML 动态解析），因此项目只能运行在完整 CoreCLR + JIT 上。
2. **“同进程延迟加载 WPF”只能省“第一次点击前”的内存**：
   - 开机后、用户从未点开托盘前：不触发任何 WPF 类型加载，纯 CoreCLR + Win32 托盘进程私有工作集大约在 **15–25 MB**。
   - **一旦用户点开一次面板**：CLR 会加载 `PresentationFramework`、`PresentationCore`、`WindowsBase`、`wpfgfx_cor3.dll` 及 Direct3D 渲染管线，私有工作集瞬间升至 **50–90 MB**。
   - **关闭窗口后不会回落**：.NET 进程内已加载的程序集和 WPF 原生图形引擎**无法卸载**，后续即使把窗口 `Close()`，常驻内存仍会停留在 50–80 MB 级别（除非靠 `EmptyWorkingSet` 把内存页强行换出到 Standby，但那只是任务管理器数字好看，物理提交仍在）。
3. **对游戏玩家的真实体感分析**：
   - 现代游戏 PC 普遍 32GB+ 内存，**50MB RAM 本身对帧率几乎零影响**；真正影响竞技 FPS 玩家 1% Low FPS 的是：**后台 CPU 唤醒频率（Timer 精度/高频轮询）**、**USB/串口同步 I/O 阻塞** 以及 **后台隐藏窗口是否残留 GPU 渲染线程**。
   - 如果采用同进程 WPF，面板隐藏时必须确保零动画、零 `CompositionTarget.Rendering` 监听、并彻底关闭窗口（不要只 `Hide()` 一个带透明特效的窗口）。

### 2. 主流备选方案横向对比

| 方案 | 常驻内存（点击面板后） | 空闲 CPU | 启动速度 | 开发效率（含 Fluent 卡片 UI） | 评价 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **方案 A（原方案）**<br>.NET 10 + Win32 托盘 + 同进程延迟 WPF | 首次点击前 ~15–25 MB<br>点击后 **~50–90 MB**（不回落） | ~0.0% | ~300–500 ms | **最高**（XAML 写面板极快） | 务实、首版最快落地，但不满足严格意义上的“极低内存”。 |
| **方案 B1（C# 极简推荐）**<br>.NET 10 **NativeAOT** + Win32 托盘 + 纯 Win32 / Direct2D 自绘简洁 Flyout | **~5–12 MB**（始终稳定） | **0.0%** | **<50 ms** | 中（4 台设备卡片用 D2D/GDI+Win11 DWM Mica 圆角代码量不大） | **最契合“C# + 极低占用 + 游戏常驻”的理想形态**；配合 `CsWin32` 零反射、零 GC 压力、零 WPF 图形残留。 |
| **方案 B2（C# 折中推荐）**<br>**双进程架构**：NativeAOT 托盘守护进程 + 按需启动/关窗即退出的 WPF 面板 | 平时 **~6–12 MB**<br>点开时短暂 +50 MB，关闭即**彻底归零** | **0.0%** | 托盘 <50 ms<br>弹窗 ~250 ms | **高**（核心逻辑与 UI 都能用 C#） | 兼得极致常驻内存与 WPF 高开发效率，通过 Named Pipe 传 4 台设备 JSON 即可。 |
| **方案 C**<br>Rust（`tray-icon` + `windows-rs` + `hidapi`/`serialport`）或纯 C++ Win32 | **~3–8 MB** | **0.0%** | **<30 ms** | 较低（原生美观弹窗 UI 开发成本较高；**切忌配 WebView2**，单进程即吃 80–150 MB） | 极客最爱、无 GC 停顿；如果作者本就熟 Rust/C++ 可选，否则没必要为省 5MB RAM 放弃 C# 效率。 |
| **方案 D**<br>WinUI 3（Windows App SDK 1.6+ NativeAOT） | **~40–80 MB+** | ~0.0% | ~200–400 ms | 中（托盘非原生、打包部署重） | **不推荐**。虽已支持 NativeAOT，但 WASDK 运行时庞大，杀鸡用牛刀。 |

### 3. 结论与建议
- **CPU 目标（空闲趋近 0%）**：**完全现实**。核心设计是 **事件驱动（`WM_DEVICECHANGE` 监听 USB 插拔）+ 粗粒度轮询（60 秒，使用 `SetWaitableTimer` / `PeriodicTimer` 允许系统 Timer Coalescing）+ 闲置退避**。
- **内存目标**：
  - 若追求硬核指标（**<15 MB 永久常驻**）：建议从原方案升级为 **方案 B1（.NET 10 NativeAOT + Win32/D2D 简洁浮层）** 或 **方案 B2（NativeAOT 托盘常驻 + 独立 WPF 弹窗进程用完即毁）**。
  - 若接受 **50–80 MB 内存（0% CPU）** 且希望最快出 MVP：原方案 **完全可行**，可以先做通 4 台设备协议，以后再把 UI 拆出进程或换自绘。
- **动态数字托盘图标细节提醒**：
  - Win32 `Shell_NotifyIcon` 动态画数字图标（GDI `CreateCompatibleBitmap` + `DrawText` + `CreateIconIndirect`）时，**每次更新图标后务必 `DestroyIcon` 销毁旧 `HICON` 句柄**，否则会引发 GDI 句柄泄漏（Windows 单进程 GDI 上限 10,000，长期开机必崩）。
  - 适配 100% / 125% / 150% / 200% DPI 缩放（按 `GetSystemMetricsForDpi(SM_CXSMICON, dpi)` 生成对应像素尺寸图标），否则数字在 Win11 任务栏上会发糊。

---

## 四、推荐的第一步验证实验（无需写项目代码）

在敲任何一行正式项目代码之前，建议按以下 **5 步实机验证实验**（预计 1–2 小时），一次把所有未知数（尤其是 Finalmouse 型号、实际 PID、确切字节帧、INZONE H9 串口冲突与鼠标休眠行为）全部摸清：

### 实验 1：枚举 4 台设备的真实 VID/PID、HID 接口、COM 口与蓝牙属性（15 分钟）
1. 下载轻量单文件工具 **[hidapitester](https://github.com/todbot/hidapitester)**（或 Uwe Sieber 的 **USB Device Tree Viewer**），插齐 4 台设备后运行：
   ```powershell
   hidapitester.exe --list-detail
   ```
   - 核对 **Finalmouse**：如果是 `361D:0100`（或 `0102`）且出现 `usagePage=0xFF00`，说明是 **ULX 系列**（可做）；如果不是 `361D` 或老 Starlight 接收器且无 XPanel 支持，尽早止损。
   - 核对 **ATK**：记录真实 `VID`（`373B` 或 `3554`）、`PID` 与 `usagePage=0xFF02`（或实际 vendor page）。
   - 核对 **游狼（G-Wolves）**：记录 `VID=33E4` 下的实际 `PID` 与 Interface 2 的 `usagePage/usage`。
2. 在 PowerShell 定位 **Sony INZONE H9** 的虚拟串口号：
   ```powershell
   Get-PnpDevice -Class Ports -PresentOnly | Select-Object FriendlyName, InstanceId
   ```
   确认 `VID_054C&PID_0E53` 对应的 `COMx`（若为 H9 II，则在 `hidapitester` 中查看其 HID 接口）。
3. 若蓝牙连有耳机或三模鼠标，顺手验证 `DEVPKEY_Bluetooth_Battery`：
   ```powershell
   Get-PnpDevice -Class Bluetooth -PresentOnly | ForEach-Object {
     $b = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2' -ErrorAction SilentlyContinue
     if ($b.Data -ne $null) { "$($_.FriendlyName) => $($b.Data)%" }
   }
   ```

### 实验 2：用 Chrome DevTools Hook 一键抓取 3 只鼠标的 WebHID 报文（20 分钟）
无需反编译前端 JS！分别用 Chrome/Edge 打开：
- Finalmouse：`https://xpanel.finalmouse.com`
- ATK：`https://hub.atk.pro`
- 游狼（G-Wolves）：`https://www.mouse.xyz`

在点击页面“连接设备”**之前**，按 `F12` 打开 Console 粘贴一段临时拦截脚本（hook `HIDDevice.prototype.sendReport`、`sendFeatureReport`、`receiveFeatureReport` 以及 `addEventListener('inputreport', ...)` 并以 Hex 打印），然后再点连接设备：
- 观察并记录每次点击刷新/打开首页时：**发出的十六进制字节**、**收到的十六进制字节**、以及**页面当下显示的电量百分比和充电状态**。
- 拔插一次 USB-C 充电线，对比“未充电 vs 充电中”响应帧的字节差异。

### 实验 3：脱离浏览器复现 3 只鼠标的独立 HID 读取 + 休眠测试（20 分钟）
关闭浏览器网页驱动，用 `hidapitester` 直接发送实验 2 抓到的字节：
- 验证普通非管理员 PowerShell 窗口下能否稳定读回正确电量（验证权限与无需网页初始化）。
- **关键休眠实验**：把鼠标放一边静置，等它进入深度休眠（可先把 ATK/游狼休眠时间调成 1 分钟方便测试），再执行一次 `hidapitester` 查询：
  - 观察鼠标底部的传感器灯/指示灯是否亮起（判断是否误唤醒）；
  - 记录休眠时返回的是“超时 / 0x00 / 上次缓存电量 / 真实电量”。

### 实验 4：验证 Sony INZONE H9 串口帧、冲突与音质影响（25 分钟）
1. 先查看 **[HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)**（`v1.16.0` / PR #278）或 **[patyhank/linzone-hub](https://github.com/patyhank/linzone-hub)** 源码中对 `054C:0E53` 的 115200 baud 查询帧定义。
2. 如需自行抓包核对：安装 **Wireshark + USBPcap**，勾选监听 INZONE H9 接收器所在的 USB Root Hub，打开官方 **Sony INZONE Hub** 读取电量后停止抓包，过滤 `usb.transfer_type == 0x03`（Bulk）查看串口收发的实际字节。
3. 彻底退出 INZONE Hub（确认任务管理器无残留进程），用简单串口工具（如 RealTerm 或 10 行临时脚本）按“打开 `COMx` → 发查询帧 → 读回电量 → 立即关闭 `COMx`”验证：
   - 戴着耳机播放音乐/游戏时执行该操作，确认**不会引起音频爆音或短暂断流**；
   - 再打开 INZONE Hub，验证当本工具已关闭串口句柄后 INZONE Hub 能否正常连上，以及 INZONE Hub 开着时串口打开失败的错误码。

### 实验 5：直接体验现有开源工具 `HaloBattery` 作为基线对照（10 分钟）
- 可先只读研究（或按个人意愿在测试环境对比）**[HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)** 的设备驱动实现，对照自己 4 台设备在其中已经打通了多少，能大幅减少重复踩坑。

---

## 五、参考项目与资料索引

- **综合多品牌托盘参考**
  - [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery) — Windows 托盘外设电量监控（含 Sony INZONE H9 `054C:0E53` 串口支持、G-Wolves/Finalmouse 社区讨论）
  - [krisgfx9/Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor) — 含 Finalmouse ULX `361D:0100` 电压查询逻辑
  - [wushuai0802/myfinder-device-battery](https://github.com/wushuai0802/myfinder-device-battery) — 含 ATK A9 Air `373B:1278` 等国产鼠标适配
- **Finalmouse**
  - [XPanel 官方网页驱动](https://xpanel.finalmouse.com)
  - [teamfinalmouse/xpanel-linux-permissions](https://github.com/teamfinalmouse/xpanel-linux-permissions)
  - [red0x0002/xpanel-custom-dpi](https://github.com/red0x0002/xpanel-custom-dpi)
- **ATK（VGN / VXE 生态）**
  - [ATK WebHUB 官方网页驱动](https://hub.atk.pro)
  - [Fan4Metal/ATK_tray](https://github.com/Fan4Metal/ATK_tray)
  - [noosxe/atk-tool](https://github.com/noosxe/atk-tool)
  - [NasirA1991/atk-control](https://github.com/NasirA1991/atk-control)
  - [cyberphantom52/libatk-rs](https://github.com/cyberphantom52/libatk-rs)
- **游狼（G-Wolves）**
  - [G-Wolves WebHID 网页驱动](https://www.mouse.xyz)（备用域名 [mouse.wiki](https://www.mouse.wiki) / [mouse.fit](https://www.mouse.fit)）
  - [marcp.xyz (`gwolves-battery` 文档)](https://marcp.xyz)
  - [OpenMouse-Project/mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol) & [OpenMouse-Project/openmouse](https://github.com/OpenMouse-Project/openmouse)
  - [Universal Mouse Drivers](https://umdrivers.com)
- **Sony INZONE H9**
  - [patyhank/linzone-hub](https://github.com/patyhank/linzone-hub)
  - [Sapd/HeadsetControl](https://github.com/Sapd/HeadsetControl)

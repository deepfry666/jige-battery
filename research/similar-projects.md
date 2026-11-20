# 同类项目调研

调研：2026-11-20，Claude。读了两个最接近几格的 Windows 托盘项目的 README、贡献指南和部分源码。

## 项目概况

| | HaloBattery | BatteryHub |
|---|---|---|
| 仓库 | [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery) | [krisgfx9/Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor) |
| 语言、许可证 | Python（PyInstaller 打包），**MIT** | C++ / Win32，**GPL-3.0** |
| 规模 | 约 825 星，60 多个 `providers/*.py`，30 多个品牌 | 约 4600 星，30 位贡献者，`src/drivers/*.cpp` 上百个 |
| 形态 | 每台设备一个托盘图标，游戏中推迟提醒、放慢查询，诊断报告，Inno 安装器 | 任务栏时钟旁的文字小部件，内置 HeadsetControl |

几格可以借用 HaloBattery 的代码，在致谢页注明出处；BatteryHub 是 GPL，只看协议事实，不复制代码。

## 它们怎么解决"没有设备就没法适配"

1. **按协议家族组织代码。** HaloBattery 的贡献指南规定：新设备放进它所属协议家族的 provider，只有遇到新协议才新建 provider。加一款鼠标通常就是加一行 `VID:PID → 名字`。
2. **用户报告驱动适配。** 托盘菜单点「Diagnostics…」生成报告，按 `device.yml` 模板提交 issue；报告里最关键的是「All HID devices」一节（VID、PID、接口号、usage）。BatteryHub 用 `--dump` 做同样的事。
3. **协议必须有出处。** 来源只有三种：开源项目、厂商网页驱动（ATK HUB、VGN HUB、mouse.xyz 等）、用 Wireshark + USBPcap 抓官方软件的包。HaloBattery 明确写着"不发送猜测的命令"，每个 provider 顶部注明来源。
4. **每次合并加一份回放测试。** HaloBattery 有几十个 `tests/test_device_prNNN_*.py`，用假 HID 设备模拟，不需要实机。
5. **会自我描述的协议直接通用。** HaloBattery 的罗技 provider：接收器 PID 在 Solaar 的列表里，或者产品字符串含 "receiver"，就逐个槽位按 HID++ 功能发现读取（0x1004 → 0x1000 → 0x1001，耳机用 0x1F20）。所以罗技整体标 "likely"。
6. **标准字段兜底。** BatteryHub 的 `generic.cpp`：任何 HID 设备只要声明了标准电量字段（Generic Device Controls `0x06:0x20` Battery Strength，或 Battery System `0x85:0x65` Absolute State Of Charge），就直接读；跳过 UPS 和电源（usage page `0x84` / `0x85` 的电源类集合）。蓝牙设备读 Windows 保存的电量属性（DEVPKEY `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}`, 2），即"设置 → 蓝牙和设备"里显示的那个值。
7. **验证状态分级，证据不足先关掉。** HaloBattery 的设备表分 yes / likely / no 三级。发版前把没有确认的路径在打开 HID 之前就跳过，例如 INZONE H9 二代 `054c:0fa8`、罗技 PRO X 3 `046d:0b45`、Lamzu Tachi。
8. **枚举省着用。** HaloBattery 的 `hidlist.py` 用 cfgmgr32 列出 HID 路径（不打开设备），路径集合没变就用缓存，不再调用会打开所有设备的 `hid.enumerate`。几格 0.2.2 的拓扑缓存是同一思路。

## 对几格有用的具体发现

- **ATK `373B:11D9` 的型号误标有共同来源。** HaloBattery 也把它写成「ATK A9 Ultimate」，来自 python-pulsar-mouse-tool 的型号表。几格 0.2.2 已改为「ATK 鼠标」。
- **Compx `08 03` 在线查询有实机旁证。** BatteryHub 对 Cherry XTRFY（`046A:0330`，标注实机测过）先发 `08 03` 查链路、再发 `08 04` 查电量。和几格 0.2.2 的 ATK 心跳一致。
- **一个厂商下有多套协议。** ATK（`373B`）下面就有 Compx（`FF02:0002`）、BitMouse（`FF05:0001`）、8K Nano 接收器 `373B:1178` 的 Holtek GetConfigData（`FF10:0001`），键盘则走 Evision（`320F`，`FF1C:0092`）。只看 VID 不够，必须看接口特征。
- **Compx 是方案商 ODM 协议**，VID `3554` 下有 VXE、Scyrox、Cougar、Redragon 等多个品牌；Hitscan（`3770`）、Zenith（`260D:1114`）、Corsair Sabre V2 Pro（`1B1C:2B2A`）也用同一种 17 字节帧。
- **Compx 鼠标空闲几秒后接收器就不回复**，这是正常状态，不代表设备断开（HaloBattery `pulsar.py` 的说明）。几格的 ATK 超时问题复测时要考虑这一点。
- **BatteryHub 标注实测**：Finalmouse UltralightX `361D:0100`、Starlight X `361D:0300/0301`。
- **HaloBattery 选 Compx 集合的规则**：优先 `FF02:0002`；输出报告长度不是 17 的集合直接跳过（用 HidP_GetCaps 只读获取），否则写入会被 Windows 拒绝，表现和设备关机一样。

## 来源

- [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery)：`README.md`、`.github/CONTRIBUTING.md`、`docs/devices.md`、`docs/maintenance.md`、`providers/compx.py`、`providers/pulsar.py`、`providers/logitech.py`、`providers/razer.py`、`providers/hidlist.py`
- [krisgfx9/Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor)：`README.md`、`CONTRIBUTING.md`、`src/drivers/generic.cpp`
- [Korben：HaloBattery 介绍](https://korben.info/en/halobattery-track-battery-level-peripherals.html)

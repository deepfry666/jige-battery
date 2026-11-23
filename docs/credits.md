# 致谢与资料来源

几格的 C# 适配器按公开协议事实独立编写。

| 项目 | 用途 |
| --- | --- |
| [HaloBattery](https://github.com/HeyOkay/HaloBattery)（MIT） | 参考罗技接收器、线缆、耳机匹配、HID++ 旧款寄存器、电量档位与社区回放资料；参考 Finalmouse、Compx、WLmouse、Lamzu、MCHOSE、雷蛇、ASUS ROG 鼠键耳机、SteelSeries、HyperX、Corsair、Pulsar、Hitscan、Cougar、Redragon、Glorious、Endgame Gear、DAREU、EWEADN、AJAZZ、AULA、Keychron 与 Audeze 的公开只读适配记录，以及 IN9 Pro、ATTACK SHARK 与雷柏 Gen 2 的上报资料。 |
| [mchose-linux](https://github.com/alexfrih/mchose-linux)（MIT） | 核对迈从鼠标反码 Feature 的报文、报告长度和电量状态字段。 |
| [atk-control](https://github.com/NasirA1991/atk-control)（MIT） | 核对 VXE / ATK Compx 电量、在线状态及接收器记录。 |
| [mchose-macos](https://github.com/Klegus/mchose-macos)（MIT） | 交叉核对初代 MCHOSE A5 Pro Max 接收器与 Feature 查询。 |
| [MousePower](https://github.com/vamyane/MousePower)（MIT） | 参考 DAREU A950 分集合 Compx（FF02 Feature → FF01 Input）只读电量查询格式。 |
| [aj179-linux](https://github.com/johan-akn/aj179-linux)（MIT） | 交叉核对 AJAZZ AJ179 V2 MAX 接收器 0xC0 电量广播帧格式。 |
| [Solaar](https://github.com/pwr-Solaar/Solaar)（GPL） | 参考罗技接收器 PID、HID++ 版本、功能号、错误码和事件字段。 |
| [Linux HID++ 驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)（GPL） | 核对电池功能、回复与推送字段、ADC 毫伏值及连接状态。 |
| [HeadsetControl](https://github.com/Sapd/HeadsetControl)（GPL） | 参考罗技、SteelSeries Arctis/Nova、HyperX Cloud、Corsair Void/Virtuoso 与 Audeze Maxwell 无线耳机的公开只读协议字段。 |
| [BatteryHub](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor)（GPL） | 参考 Windows 蓝牙电量属性、连接属性及同物理设备去重思路。 |
| [OpenRazer](https://github.com/openrazer/openrazer) | 参考雷蛇电量与充电命令、设备清单和公开抓包事实。 |
| [G-Helper](https://github.com/seerge/g-helper)（GPL） | 参考 ASUS ROG / TUF 无线键盘、鼠标、OMNI 接收器与 Strix Go 2.4 耳机（`0B05` 厂商集合）的只读电池百分比与充电状态查询报文格式。 |
| [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol) | 参考 ATK、游狼、BitMouse、罗技、MCHOSE V3 与 AJAZZ GearHub V5 的公开协议。国产鼠标适配仅参考协议和硬件事实，C# 实现及测试帧独立编写。 |
| [mouse-battery-tray](https://github.com/incconutwo/mouse-battery-tray)（MIT） | 核对 ATTACK SHARK 接收器与上报型号字节。 |
| [mouse_tray](https://github.com/Fan4Metal/ATK_tray) | 核对 VXE 接收器 VID:PID 事实。 |
| [atkctl](https://github.com/cobanov/atkctl)、[openinzone](https://github.com/penguinwokrs/openinzone) | 核对 ATK 和 INZONE 的报文格式。 |
| 其余社区只读协议参考 | 参考 `python-pulsar-mouse-tool`、`pulsar-x3-python`、`hitscan-battery`、`okms/xm2we-linux`、`GloriousBatteryMonitor`、`rivalcfg`、`alloyctl`、`HyperHeadset`、`HyperXBatteryMonitor`、`ckb-next`、`OpenLinkHub`、`HS80_Control`、`keychron-battery-dkms` 的公开协议事实，C# 适配器全部独立净室编写。 |

协议命令与具体来源见[设备读取协议](protocols.md)。感谢提供设备报告、抓包和实机反馈的用户。

## 其他参考

- Finalmouse [XPanel](https://xpanel.finalmouse.com/)、官方 [USB 权限清单](https://github.com/teamfinalmouse/xpanel-linux-permissions) 以及 HaloBattery 提供 ULX 报文与实机记录。
- [InZoneH9Tray](https://github.com/janit88/InZoneH9Tray)、[openinzone](https://github.com/penguinwokrs/openinzone) 与 [inzone-h9-mac](https://github.com/jpcarranza94/inzone-h9-mac) 提供 H9 串口查询、帧格式及实机记录。
- Solaar（GPL-2.0，部分文件 GPL-2.0-or-later）、[OpenLogi](https://github.com/AprilNEA/OpenLogi)（MIT 或 Apache-2.0）、OpenMouse mouse-protocol（AGPL-3.0）提供 HID++ 的槽、身份和电量协议资料。
- HeadsetControl（GPL-3.0）提供 Centurion、G325 和 ASTRO Gen 5 的报文与实机记录。
- OpenRazer（GPL-2.0）、[Razer Battery Report](https://github.com/xzeldon/razer-battery-report)（MIT）、HaloBattery（MIT）、[BlackShark V2 HyperSpeed WebHID](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid)（MIT）与 [Synapse-for-Linux](https://github.com/bbesli/Synapse-for-Linux)（MIT）提供雷蛇 PID、接口、读电量与充电的报文和实机证据。

几格依据公开协议字段和实机报文独立实现，不复制 GPL 代码，保留 MIT 许可。许可出处见 [设备清单](../research/device-catalog.md#6-参考实现许可证)。

# 致谢与资料来源

几格的 C# 适配器按公开协议事实独立编写。

| 项目 | 用途 |
| --- | --- |
| [HaloBattery](https://github.com/HeyOkay/HaloBattery)（MIT） | 参考罗技接收器、线缆、耳机匹配、HID++ 旧款寄存器、电量档位与社区回放资料。也参考 Finalmouse、Compx、WLmouse、Lamzu、MCHOSE 鼠标和雷蛇适配记录，以及 IN9 Pro、ATTACK SHARK 与雷柏 Gen 2 的上报资料。 |
| [mchose-linux](https://github.com/alexfrih/mchose-linux)（MIT） | 核对迈从鼠标反码 Feature 的报文、报告长度和电量状态字段。 |
| [atk-control](https://github.com/NasirA1991/atk-control)（MIT） | 核对 VXE / ATK Compx 电量、在线状态及接收器记录。 |
| [mchose-macos](https://github.com/Klegus/mchose-macos)（MIT） | 交叉核对初代 MCHOSE A5 Pro Max 接收器与 Feature 查询。 |
| [Solaar](https://github.com/pwr-Solaar/Solaar)（GPL） | 参考罗技接收器 PID、HID++ 版本、功能号、错误码和事件字段。 |
| [Linux HID++ 驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)（GPL） | 核对电池功能、回复与推送字段、ADC 毫伏值及连接状态。 |
| [HeadsetControl](https://github.com/Sapd/HeadsetControl)（GPL） | 参考罗技无线耳机 PID、集合、Centurion 桥和 ASTRO 数据字段。 |
| [BatteryHub](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor)（GPL） | 参考 Windows 蓝牙电量属性、连接属性及同物理设备去重思路。 |
| [OpenRazer](https://github.com/openrazer/openrazer) | 参考雷蛇电量与充电命令、设备清单和公开抓包事实。 |
| [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol) | 参考 ATK、游狼、BitMouse、罗技的公开协议。国产鼠标两批仅参考协议和硬件事实，C# 实现及测试帧独立编写。 |
| [mouse-battery-tray](https://github.com/incconutwo/mouse-battery-tray)（MIT） | 核对 ATTACK SHARK 接收器与上报型号字节。 |
| [mouse_tray](https://github.com/Fan4Metal/ATK_tray) | 核对 VXE 接收器 VID:PID 事实。 |
| [atkctl](https://github.com/cobanov/atkctl)、[openinzone](https://github.com/penguinwokrs/openinzone) | 核对 ATK 和 INZONE 的报文格式。 |

协议命令与具体来源见[设备读取协议](protocols.md)。感谢提供设备报告、抓包和实机反馈的用户。

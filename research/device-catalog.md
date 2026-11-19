# 几格 / Jige Battery 设备与电量协议清单

| 家族 | USB 识别线索 | 电量请求与充电数据 | 默认启用条件 |
| --- | --- | --- | --- |
| Logitech HID++ 2.0 | `046D` 接收器；长报告通常 `FF00:0002`、ID `11`、20 B | IRoot 解析索引，再用 `1004/f1`、`1000/f0` 或 `1001/f0` | 已识别接收器与有效槽响应 |
| Logitech Centurion | `046D` 指定耳机；`FFA0:0001`、64 B Output/Input | 桥接 `0104/f0`，另有型号专属固定查询 | 完整 PID 和前缀匹配 |
| Astro A50 Gen 5 | `046D:0B1C`、`FF32:0074`、64 B Output/Input | `02 0C 03 00 06 0C`，含底座充电标志 | 仅该底座 |
| Razer 90B | `1532`、Feature ID 0、90 B payload；接口随型号 | `07:80` 原始 0–255，`07:84` 充电 | PID、接口、TID 均有证据 |
| Razer PA / PA-Barracuda | `1532` 指定耳机；64 B Output/Input；ID `02` 或 `01` | `21` 百分比、`2A` 充电；参考实现有远程模式切换 | 严格只读模式暂不主动启用 |
| Razer BlackShark 64B | V2 HyperSpeed `1532:0565`、V3 `057A`、V3 Pro `0577`；64 B Output/Input | `21` 百分比、`2A` 充电；型号间有回复校验和会话差异 | V2 HyperSpeed 可用纯查询；V3 系列冷启动保持禁用 |
| Compx-OUT | `373B` / `3554` / `33E4` 等；`FF02`，17 B ID `08` Output/Input | `04`，百分比、充电，可选 mV | 匹配具体接收器、collection 和传输 |
| Compx-FEAT | A9 Ultra 已知 `373B:111A`；`FF04`，17 B ID `08` | 与 Compx-OUT 同帧，Feature 发、Input 收 | 匹配独立描述符分支 |
| ATK BitMouse | `373B`、`FF05:0001`、64 B ID `08` Output/Input | `07` 百分比、`0F` 充电，目标 1 | 匹配已证实的接收器和响应布局 |
| 游狼 XVI-OLD / XVI-NEW | `33E4`、Report ID 0 的 64 B Feature payload，实测 `FFFF:0000` | `8F` / `83`，回复含充电与百分比 | PID 和固件分支有证据；同长度无法区分两代 |

调研截至 **2026-11-19**。主表收录 2023 年 1 月起有资料的型号、无法确认首发年的相关候选及有工程价值的星闪对照。发布年优先用厂商发布记录，带「商店」的年份仅代表官网在该年发布对应商品页，带「系列」的年份是系列上架年。早期对照设备另列。接收器与有线 ID 均指 USB 枚举值。罗技 WPID 是接收器侧的无线产品 ID。除另有标注，偏移从 0 起算，包含报告 ID。十六进制模板中的 `00×N` 代表补 N 个零字节。报告所用的实机证据来自来源作者，本次未连接外设。

**证据等级。**「多方开源实现一致」要求该型号与接收器的电量路径有交叉依据，其中至少一方有对应实机读数或抓包。「单一来源」包括目前只有一个独立实现或实机记录，以及多项目引用同一抓包的情况。「未核实」表示主表中的接收器 PID、选定协议、请求/响应或适配所需分流有缺口，包含来源冲突。主表行按型号计数，一行内的接收器版本不重复计数。未核实比例以该证据栏计，充电线 PID 或年份单项缺失仍会在对应单元格标出。

## 1. 罗技 Logitech G

### HID++ 2.0 与接收器

- **接收器 USB VID 为 `046D`。** Solaar 的 LIGHTSPEED 表为 `C539/C53A/C53D/C53F/C541/C545/C547/C54D`。Bolt 为 `C548`，Unifying 为 `C52B/C532`。三种均可作为 HID++ 路由候选，配对生态各自独立。Solaar 把 `C52E/C531/C534/C535/C537` 列为 Nano。主表的 Logitech G 型号未找到通过 Bolt 或 Unifying 配对的证据。Lightspeed 多设备支持取决于型号和接收器固件。 [Solaar 接收器表](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base_usb.py)、[Lightspeed 实机讨论](https://github.com/AprilNEA/OpenLogi/issues/512)
- **帧。** 长报告为 `11 D I FS P0…P15`，共 20 B。短报告为 `10 D I FS P0 P1 P2`，共 7 B。通常长集合 `FF00:0002`，短集合 `FF00:0001`。`D` 是槽位，`I` 是动态 feature index，`FS` 高半字节是 function index，低半字节是非零 software ID。下文取软件 ID `0A`。按匹配的 HID Output/Input 路径收发，协议层无额外校验和。普通回复回显 `D/I/FS`，HID++ 2.0 错误常为 `11 D FF <原 I> <原 FS> <error> …`，1.0 错误使用 `8F`。主动事件通常带 software ID 0。 [Logitech HID++ 2.0 草案](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf)、[Linux HID++](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)、[HaloBattery Windows 读取](https://github.com/HeyOkay/HaloBattery/blob/main/providers/logitech.py)
- **槽位。** 接收器控制地址是 `FF`，USB 直连外设常用 `FF`。接收器转发时从槽 1 检查到已知容量，通用上限 6，Bolt/Unifying 可用 1–6。先发 IRoot Ping `11 D 00 1A 00 00 MM 00×13`，`MM` 是每次变更的标记。回复参数前 3 字节为主版本、次版本、原标记。部分实现走等价 7 B 短 Ping，以对应描述符为准。空槽或超时继续下一槽，1.0 错误 `08` 表示未知槽，`04/09` 可为设备不可达。用配对表区分已配对离线和未配对。某些 G 接收器还会在 `FF` 回应自己的 Root，只有确认外设身份并完成对应电量查询的条目才显示为外设。按接收器物理实例与槽位缓存，拔插、换配对或固件变化后重查。 [Solaar Ping](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base.py)、[OpenMouse 索引与路由](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/logitech/hidpp.ts)
- **可选配对表读取。** 已知支持时使用 HID++ 1.0 长寄存器读操作 `10 FF 83 B5 SS 00 00`，Unifying 的 `SS=0x20+槽位-1`，Bolt 的 `SS=0x50+槽位`，长回复回显 `FF 83 B5 SS`。Lightspeed 的寄存器支持随固件变化，缺表时逐槽 Ping。设备身份可用 `0005` 函数 0/1/2 读取名称长度、名称片段、设备种类，并用 `0003/f0` 的 Unit ID 区分实例。 [Solaar 接收器解析](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/receiver.py)、[OpenMouse 配对表格式](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/logitech/receiver-pairing.ts)
- **IRoot GetFeature。** 查 `1004` 的完整模板为 `11 D 00 0A 10 04 00×14`，`1000`、`1001` 分别替换低字节 `00`、`01`。回复 `11 D 00 0A I T V …`，`I` 非零为索引，`T` 为类型标志，`V` 为版本；索引 0 表示该查询 feature 不存在。 [Logitech HID++ 2.0 草案](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf)、[Solaar feature 查找](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)

| Feature | 电量请求（长报告） | 参数区响应及充电 |
| --- | --- | --- |
| `1004` UNIFIED_BATTERY | 能力 `11 D I 0A 00×16`；状态 `11 D I 1A 00×16` | 能力参数 `P0` 为支持的档位位图，`P1` bit0 可充电、bit1 支持 SoC。状态 `P0` 百分比，`P1` 档位（`01/02/04/08` 为极低/低/良好/满），`P2` 充电状态，`P3` 外部供电状态（独立于是否正在充电）。有 SoC 能力才用百分比。 |
| `1000` BATTERY_STATUS | `11 D I 0A 00×16` | `P0` 当前放电百分比，`P1` 下一离散通知阈值，`P2` 充电状态。Solaar 将 `P0=0` 视为无有效百分比。 |
| `1001` BATTERY_VOLTAGE | `11 D I 0A 00×16` | `P0:P1` 大端 mV；`P2` bit7 表示外部供电。供电时低位子状态 `0` 充电中、`1` 充满、`2` 停止充电。OpenLogi 取低三位，Solaar 取低两位，建议将原始低三位 3–7 保留为未知，防止折叠成正常状态。bit3/bit4 区分快充/慢充，Solaar 另用 bit5 标记极低。电压映射随电池曲线和负载而变，显示估算值或 mV。 |
| `1F20` ADC_MEASUREMENT | `11 D I 0A 00×16` | 耳机旧系列参考；`P0:P1` 大端 mV，`P2` 典型 `01` 放电、`03` 充电、`07` 充满；无有效链路、未知标志或填充电压不显示新读数。 |

`1000` 状态值按枚举解释。0 放电、1 充电、2 接近充满、3 已充满、4 慢充、5 电池无效、6 温度错误。`1004` 对 0、1、2、3 可分别显示放电、充电、充电、已满。其余状态保留原始值，尤其 `04` 在 Solaar/OpenLogi 解释为慢充，Linux 驱动解释为错误。优先使用 `1004` 的有效 SoC，其次 `1000`，最后按设备能力用 `1001`。电压类与等级类读数应标明精度。 [Linux HID++ 电源解析](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c)、[Solaar 电源解析](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)、[OpenLogi 1004](https://openlogi.org/hidpp/features/x1004-unified-battery)、[OpenLogi 1001](https://openlogi.org/hidpp/features/x1001-battery-voltage)

### 耳机与底座

**Centurion。** 64 B Output/Input，无已见应用层校验。G PRO X 2 前缀 `51`，G522 和 PRO X 3 前缀 `50 23`。前缀后为长度和 flags，长度计入 flags，单帧查询 flags 0。先用接收器 Root 查桥索引 `B`，模板 `<前缀> 05 00 00 01 00 03`；再发 `<前缀> 0A 00 B 11 00 05 00 00 01 01 04`，向耳机 Root 查 `0104` 索引 `I`；电量请求为 `<前缀> 08 00 B 11 00 03 00 I 01`。每条补零到 64 B。桥外层 `B 11` 发单段，接收方向的桥事件也用高半字节 `1`。等内层应答并校验桥索引、分段长度、内层 `00 I 01`，拒绝断链事件、空 ACK 和内层错误。内层返回参数 `P0` 为 0–100，`P2` 为 0 放电、1/2 充电、3 已满。G PRO X 2 的旧固件备用请求 `51 08 00 03 1A 00 03 00 04 0A` 补零至 64 B，回复 `[0]=51 [1]=0B [8]=04` 后以 `[10]` 为百分比、`[12]=02` 为充电，此请求只在 `0AF7` 经正确握手确认旧固件缺少 `0104` 时考虑。 [HeadsetControl Centurion](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/protocols/logitech_centurion_protocol.hpp)、[G PRO X 2 旧分支](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_gpro_x2_lightspeed.hpp)、[Solaar 状态](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/centurion.py)、[HaloBattery 实机](https://github.com/HeyOkay/HaloBattery/issues/103)

**G325 的固定帧。** `0B21`、`FFA0:0001`（MI_03），64 B Output/Input。单台实测的查询为 `50 23 0B 00 03 1A 00 03 00 07 0A 00×53`，先返回短 ACK，再以 `50 23 0B 00 03 10 00 06 00 07 0A` 开头报告电量状态。`[11]` 为原始读数，`[12]` 为 G HUB 所示百分比，`[13]=00/02` 对应未充电/充电，观测有 100% 且仍处于充电状态。仅将固定查询绑定到这个 PID。 [HeadsetControl 社区实机记录](https://github.com/Sapd/HeadsetControl/issues/581)

**Astro A50 Gen 5。** 底座 `FF32:0074`（MI_08），64 B Output/Input，无已见应用层校验。`02 0C 03 00 06 0C 00×58` 查询。帧 `02 0C LEN 00 CMD HANDLE …`，长度计从 `[3]` 起，回复以 `CMD=06` 匹配，观测回复 HANDLE `00`。`[6]` 百分比，`[8]` 在底座充电标志。A50 X 的底座实机枚举为 `046D:0B0B`，电量协议有 Centurion 线索，该 PID 的报告前缀和电量事务仍未核实。 [HeadsetControl Gen 5](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_astro_a50.hpp)、[HaloBattery 耳机笔记](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md#astro-a50-gen-5-logitech-046d0b1c)、[A50 X 原始枚举](https://www.reddit.com/r/Fedora/comments/1vg1z7q/how_i_got_logitech_astro_a50_x_gamevoice_audio/)

### 型号

| 型号 | 年 | 种类 | 接收器 / 有线 | 家族与电量路径（含充电字段） | 可信度和来源 |
| --- | --- | --- | --- | --- | --- |
| PRO X SUPERLIGHT 2 | 2023 | 鼠标 | `046D:C54D` / `046D:C09B` | HID++，`1004`，WPID `40A9` | 多方开源实现一致。[实机 dump](https://github.com/pwr-Solaar/Solaar/blob/master/docs/devices/PRO%20X%202%2040A9.text)、[OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/logitech/hidpp.ts)、[发布](https://news.logitech.com/press-releases/news-details/2023/Logitech-G-Takes-Esports-Performance-to-New-Levels-With-New-PRO-Series-Gear/default.aspx) |
| PRO X SUPERLIGHT 2 DEX | 2024 | 鼠标 | `046D:C54D` / 未核实 | HID++ Root 查电量 feature，实机 feature 未核实 | 单一来源。[接收器实机](https://github.com/AprilNEA/OpenLogi/issues/512)、[发布](https://news.logitech.com/press-releases/news-details/2024/Logitech-G-Launches-the-Next-Generation-of-PRO-Series-Mice-and-Keyboard-Designed-With-and-For-the-Worlds-Best-Esports-Athletes/default.aspx) |
| PRO 2 LIGHTSPEED | 2024 | 鼠标 | 未核实 / `046D:C09A`（型号表） | HID++ 候选，WPID `40A8`，电量功能未核实 | 未核实。[型号 ID](https://openlogi.org/hidpp/model-ids)、[发布](https://news.logitech.com/press-releases/news-details/2024/Logitech-G-Launches-the-Next-Generation-of-PRO-Series-Mice-and-Keyboard-Designed-With-and-For-the-Worlds-Best-Esports-Athletes/default.aspx) |
| G309 LIGHTSPEED | 2024 | 鼠标 | `046D:C547` / 无 USB 数据线模式 | HID++，WPID `40B1`，Root 查候选 feature；AA 供电和 POWERPLAY 配置应按能力呈现充电状态 | 多方开源实现一致。[Solaar dump](https://github.com/pwr-Solaar/Solaar/issues/2998)、[OpenLogi 实机](https://github.com/AprilNEA/OpenLogi/issues/877)、[发布](https://news.logitech.com/press-releases/news-details/2024/Logitech-G-Launches-LIGHTSPEED-Gaming-Mouse-for-All-Players/default.aspx) |
| PRO X2 SUPERSTRIKE | 2026 | 鼠标 | `046D:C54D` / `046D:C0A8` | HID++，WPID `40BD`，`1004` | 多方开源实现一致。[实机 feature](https://github.com/pwr-Solaar/Solaar/blob/master/docs/devices/PRO%20X%202%20Superstrike%2040BD.txt)、[接收器实机](https://github.com/OpenMouse-Project/openmouse/issues/1362)、[2025 预告](https://news.logitech.com/press-releases/news-details/2025/Logitech-G-Drops-a-Wide-Array-of-New-Products-and-Innovations-at-Logitech-G-PLAY-2025/default.aspx) |
| PRO X TKL LIGHTSPEED | 2023 | 键盘 | 未核实 / `046D:C352`（型号表） | HID++ 候选，WPID `40B0`，电量功能未核实 | 未核实。[型号 ID](https://openlogi.org/hidpp/model-ids)、[发布](https://news.logitech.com/press-releases/news-details/2023/Logitech-G-Takes-Esports-Performance-to-New-Levels-With-New-PRO-Series-Gear/default.aspx) |
| G515 LIGHTSPEED TKL | 2024 | 键盘 | `046D:C547` / `046D:C355` | HID++ `1004`，WPID `40B4` | 单一来源。[接收器 dump](https://github.com/pwr-Solaar/Solaar/issues/2791)、[电量与有线 ID](https://github.com/pwr-Solaar/Solaar/blob/master/docs/devices/G515%20LS%20TKL%2040B4.text)、[发布](https://news.logitech.com/press-releases/news-details/2024/Introducing-Logitechs-G515--A-Next-Generation-Low-Profile-Keyboard-for-High-Performance-Gaming/default.aspx) |
| G915 X LIGHTSPEED | 2024 | 键盘 | `046D:C545`（实机替换接收器） / `046D:C356` | HID++，WPID `40B5`，Solaar 观测 `1004`；原配接收器 PID 未核实 | 未核实（原配）。[电量 dump](https://github.com/pwr-Solaar/Solaar/issues/3328)、[替换接收器](https://github.com/AprilNEA/OpenLogi/issues/512)、[发布](https://news.logitech.com/press-releases/news-details/2024/Logitech-G-Introduces-the-G915-X-Series---The-Most-Advanced-Low-Profile-Gaming-Keyboard-Ever-Built/default.aspx) |
| G915 X LIGHTSPEED TKL | 2024 | 键盘 | `046D:C547`（实机枚举） / `046D:C357`（型号表） | HID++ 候选，WPID `40B6`，电量事务未核实 | 未核实。[型号 ID](https://openlogi.org/hidpp/model-ids)、[发布](https://news.logitech.com/press-releases/news-details/2024/Logitech-G-Introduces-the-G915-X-Series---The-Most-Advanced-Low-Profile-Gaming-Keyboard-Ever-Built/default.aspx)、[实机档案](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/5001) |
| G PRO X 2 LIGHTSPEED | 2023 | 耳机 | `046D:0AF7` / 未核实 | Centurion `51`，`0104/f0` 或已证实的旧帧 | 多方开源实现一致。[HeadsetControl](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_gpro_x2_lightspeed.hpp)、[实机电量](https://github.com/HeyOkay/HaloBattery/issues/103)、[发布年份](https://news.logitech.com/press-releases/news-details/2023/Logitech-G-Takes-Esports-Performance-to-New-Levels-With-New-PRO-Series-Gear/default.aspx) |
| G522 LIGHTSPEED | 2025 | 耳机 | `046D:0B18` / 未核实 | Centurion `50 23`，`0104/f0` | 单一来源。[HeadsetControl](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_g522_lightspeed.hpp)、[发布](https://news.logitech.com/press-releases/news-details/2025/Level-Up-Your-Play-With-the-New-Logitech-G522-Gaming-Headset/default.aspx) |
| G PRO X 3 LIGHTSPEED | 2026 | 耳机 | `046D:0B45` / 未核实 | Centurion `50 23`，`0104/f0`；旧 `51` 实测无应答 | 单一来源。[HeadsetControl](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_gpro_x3_lightspeed.hpp)、[旧帧失败记录](https://github.com/HeyOkay/HaloBattery/issues/343)、[发布](https://news.logitech.com/press-releases/news-details/2026/From-New-PRO-Gear-to-the-Iconic-Yeti-2-Mic---Logitech-G-Unveils-Fifteen-New-Products-at-Logitech-G-PLAY-2026/default.aspx) |
| G325 LIGHTSPEED | 2026 | 耳机 | `046D:0B21` / 未核实 | Centurion 固定帧分支，`07 0A` 查询，百分比与充电取 `[12..13]` | 单一来源。[实机](https://github.com/Sapd/HeadsetControl/issues/581)、[发布](https://news.logitech.com/press-releases/news-details/2026/Affordable-Excellence-Logitech-G325-LIGHTSPEED-Delivers-Comfort-Style-and-Game-Ready-Audio/default.aspx) |
| ASTRO A50 X | 2023 | 耳机 | 底座 `046D:0B0B`（单台枚举） / 未核实 | Centurion 线索；该 PID 的准确集合、请求帧和充电字段未核实 | 未核实。[原始实机枚举](https://www.reddit.com/r/Fedora/comments/1vg1z7q/how_i_got_logitech_astro_a50_x_gamevoice_audio/)、[发布](https://news.logitech.com/press-releases/news-details/2023/Logitech-G-Launches-New-Flagship-Console-Wireless-Gaming-Headset---the-ASTRO-A50-X/default.aspx)、[底座协议辨识](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md#astro-a50-gen-5-logitech-046d0b1c) |
| ASTRO A50 Gen 5 | 2024 | 耳机 | 底座 `046D:0B1C` / 未核实 | Astro A50 Gen 5，`02 0C … 06` 查询 | 单一来源。[HeadsetControl 抓包实现](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_astro_a50.hpp)、[发布](https://news.logitech.com/press-releases/news-details/2024/Logitech-G-Unveils-ASTRO-A50-Gen-5-Now-Featuring-PLAYSYNC-AUDIO/default.aspx) |

**只读白名单。** HID++ 2.0 限 Root `f0/f1`、身份 `0003/f0` 与 `0005/f0–2`、上表电量函数；接收器可选仅 `83 B5` 的指定槽位读取。HID++ 1.0 旧设备若独立支持，电量只开放 `81 0D`、`81 07`。禁止寄存器写入 `80/82`（含打开通知、`B2/C0/C1` 配对控制）、DFU、板载配置和灯效。Centurion 仅允许接收器 Root 查 `0003`、桥内 Root 查 `0104` 和桥内 `0104/f0`，另有上面设备专属旧电量查询及 G325 固定查询。桥的 `f1` 是通道封装，必须验证内层；禁止任意转发及睡眠、EQ、侧音、灯效设置。Gen 5 仅允许 `CMD 06/HANDLE 0C`，禁止音量设置、底座 LED、复位及升级。 [Solaar 寄存器定义](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10_constants.py)、[HeadsetControl Centurion](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/protocols/logitech_centurion_protocol.hpp)、[Gen 5 命令](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_astro_a50.hpp)

## 2. 雷蛇 Razer

### 90 字节 Feature 家族

`R[]` 是 90 B 协议体。一些接口以无编号 Feature 报告对外，Win32 传 91 B，`[0]=00` 是 HID API 的 report ID 槽，协议体从下一字节起。USB 原始 control-transfer 实现直接传 90 B，`wIndex` 选择具体接口。用描述符确认 Feature 报告和对应接口，同一接收器可有多个 collection。常见鼠标走接口 0，键盘以具体 `razer_get_report_params()` 为准。 [OpenRazer 帧与 CRC](https://github.com/openrazer/openrazer/blob/master/driver/razercommon.c)、[鼠标驱动](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[键盘驱动](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)、[Windows 实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/razer.py)

| 协议体偏移 | 字段 | 电量查询 |
| --- | --- | --- |
| `0` | 状态 | 请求 `00`；回复 `01` 忙、`02` 成功、`03` 失败、`04` 超时、`05` 不支持 |
| `1` | Transaction ID | 按 PID 与供电路径选表内 TID |
| `2–3` | 剩余包数 | `00 00`，单帧 |
| `4` | 协议类型 | `00` |
| `5` | 数据长 | `02` |
| `6–7` | 类 / 命令 | `07 80` 电量，`07 84` 充电 |
| `8–87` | 参数区 | 请求补零，回复 `R[9]`（arg[1]）为值 |
| `88` | 校验 | `R[2]` 到 `R[87]` 逐字节 XOR |
| `89` | 保留 | `00` |

**十六进制模板（含 Win32 Report ID）。** 电量 `00 00 TT 00 00 00 02 07 80 00×80 85 00`，充电 `00 00 TT 00 00 00 02 07 84 00×80 81 00`。同路径先 SetFeature，再 GetFeature。校验事务、类、命令、长度和 CRC，只有状态 `02` 才取值。将电量四舍五入到 `100×R[9]/255`，充电常为 `00/01`，命令不支持、超时或意外值显示未知。AA 机型通常只读 `80`。旧型号 TID 示例为 Mamba Wireless `0x3F`、Mamba 2012 `0xFF`；新款鼠标多为 `0x1F`，部分键盘接收器为 `0x9F`。协议描述符无法推出 TID。 [OpenRazer 查询定义](https://github.com/openrazer/openrazer/blob/master/driver/razerchromacommon.c)、[HaloBattery 电量解析](https://github.com/HeyOkay/HaloBattery/blob/main/providers/razer.py)、[Windows 鼠标型号表](https://github.com/xzeldon/razer-battery-report#supported-mice)

### 耳机协议

- **PA，BlackShark V2 Pro 2023。** `FF00` 为已知厂商 page；另有驱动把 `FF14` 列作候选，缺实机证明时不主动发送。64 B Output/Input。查询 `02 80 08 00 00 50 41 08 00 03 CC 00×53`，`CC=21` 百分比、`2A` 充电。帧含类型 `[9]=03`、命令 `[10]`、标志 `[11]`、参数长度 `[12]`，无已见应用层校验。回复 `[12]=CC [13]=01 [14]=长度 [15]=值`，至少 1 字节，充电响应非零为充电。参考实现需要先以 `E1` 切远程模式并在结束后切回；这是接收器状态变更，严格只读策略禁止。仅在已证实无需发送 `E1` 的状态下允许发送查询，或等待已验证的被动读法。PC、PlayStation 和 Xbox SKU 分开登记。 [HaloBattery 2023 PA 帧](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py)、[OpenRazer PR 2862](https://github.com/openrazer/openrazer/pull/2862)、[HeadsetControl PS 版](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/razer_blackshark_v2_pro_ps.hpp)
- **PA-Barracuda，Barracuda Pro（2022 对照）。** `1532:053A`，`FF00:0000`。64 B Output/Input，查询 `01 80 08 50 41 08 08 03 CC 00×55`，`CC=21/2A`。回复 `[3:4]=50 49`，`[13]` 回显命令、`[14]=01` 应答或 `02` 通知、`[15]` 长度、`[16]` 百分比或充电（非零）。无已见应用层校验；同样受 `E1` 的限制。Barracuda X (2022) 至少见到 `0536` 的 PA 分支与 `0552` Macronix 分支；后者的协议单独处理。两者均不作为 Barracuda X Chroma 或 Barracuda V2 Pro 的匹配依据。 [Barracuda Pro 实机实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/barracuda.py)、[Barracuda X 分批说明](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md#razer-barracuda-x-2022-24-ghz)
- **BlackShark V3。** 基础款 `1532:057A`，`FF14:0001`（MI_05），64 B Output/Input，`[0]=02 [2]=60 [6]=04 [9]=80 [10]=命令`，末段 `[62]=XOR([0]…[61])`。电量查询 `02 00 60 00 00 00 04 00 00 80 21 00×51 C7 00`，换 `2A` 和 `CC` 查充电。回复 `[1]=02`，`[10]` 同命令、`[11]=01` 应答（`02` 为推送）、`[12]` 参数数，`[13]` 百分比或充电 `0/1`，`FF` 链路未就绪。参考实现抓到额外冷启动会话帧，且高频查询出现重置无线链路。只读策略仅许可验证过的被动推送及已就绪会话的两条查询。 [HaloBattery V3 抓包与实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark_v3.py)、[OpenRazer PR 2794](https://github.com/openrazer/openrazer/pull/2794)

- **BlackShark V2 HyperSpeed 的 MediaTek 分支。** 与 V3 共用 64 B 外形，`0565` 实测控制集合为 `FF14:0001`（MI_03）。`[2]` 为 `60` 加 5 位序号，回复回显序号。`[8]=00` 请求、`80` 回复，`[9]` 寻址无线 `80`、有线 `00`。序号为 0 时无线请求使用上面的 `C7/CC` 模板。有线 domain 为 0 时，同序号电量尾部为 `47 00`，充电为 `4C 00`。序号改变后重算 CRC。回复 `[13]` 取百分比或充电（0 或正值），实测回复 CRC 异或 `[1]…[61]`，请求 CRC 含 `[0]`。可先读本地 domain `00` 的链路 `20`。实测可直接查询，无需 PA 的远程模式开关。 [WebHID 逆向](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid#mediatek-protocol-specification)、[独立实测与 CRC](https://github.com/bbesli/Synapse-for-Linux/blob/main/docs/PROTOCOL.md)
- **BlackShark V3 Pro 的会话差异。** `0577` 的 Windows 实机记录使用同样的 `21/2A` 请求和数值偏移。该接收器需要额外启动并维持无线会话，独立的纯查询冷启动路径未核实。仅考虑已验证的被动读取。完整 TLC usage、有线 PID 和回复 CRC 细节未核实。 [OpenRazer 社区实机](https://github.com/openrazer/openrazer/issues/2838)

### 型号

Razer 表中四位 PID 均用 VID `1532`。90B 的 TID 写作「接收器 / 有线」，响应与充电依上文。

| 型号 | 年 | 种类 | 接收器 PID / 有线 PID | 家族与电量路径（含充电字段） | 可信度和来源 |
| --- | --- | --- | --- | --- | --- |
| Viper Mini Signature Edition | 2023 | 鼠标 | `009F / 009E` | 90B，`1F/1F` | 单一来源。[OpenRazer PID 和路径](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.h)、[90B 实现](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c) |
| Cobra Pro | 2023 | 鼠标 | `00B0 / 00AF` | 90B，`1F/1F` | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[2023 官方跨型号公告](https://www.razer.com/newsroom/product-news/razer-mice-with-8000hz-wireless-polling) |
| Viper V3 HyperSpeed | 2023 | 鼠标 | `00B8` / 无 USB 数据线模式 | 90B，`1F`，仅电量 | 多方开源实现一致。[OpenMouse 验证表](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/razer/devices.ts)、[Windows 适配](https://github.com/xzeldon/razer-battery-report#supported-mice)、[2023 官方公告](https://www.razer.com/newsroom/product-news/razer-mice-with-8000hz-wireless-polling) |
| Basilisk V3 X HyperSpeed | 2023 | 鼠标 | `00B9` / 无 USB 数据线模式 | 90B，`1F`，仅电量 | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[Windows 摘录](https://github.com/xzeldon/razer-battery-report#supported-mice) |
| Viper V3 Pro | 2024 | 鼠标 | `00C1 / 00C0` | 90B，`1F/1F` | 多方开源实现一致。[OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/razer/devices.ts)、[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c) |
| DeathAdder V3 HyperSpeed | 2024 | 鼠标 | `00C5 / 00C4` | 90B，`1F/1F` | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[官方代际](https://www.razer.com/pc/gaming-mice/deathadder-line) |
| Basilisk V3 Pro 35K | 2024 | 鼠标 | `00CD / 00CC` | 90B，`1F/1F` | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[Razer 手册](https://dl.razerzone.com/master-guides/RazerSynapse4/BASILISKV3PRO35K-00000214-en.pdf) |
| Basilisk V3 Pro 35K Phantom Green | 2025 | 鼠标 | `00D7 / 00D6` | 90B，`1F/1F` | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[Razer 产品线](https://www.razer.com/pc/gaming-mice) |
| DeathAdder V4 Pro | 2025 | 鼠标 | `00BF / 00BE` | 90B，`1F/1F`，实测原始 `AB` → 67% | 多方开源实现一致。[HaloBattery 实机](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md#razer-deathadder-v4-pro)、[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[发布](https://www.razer.com/newsroom/product-news/razer-unveils-the-ultra-lightweight-deathadder-v4-pro) |
| Cobra HyperSpeed | 2025 | 鼠标 | `00DB / 00DA` | 90B，`1F/1F`，MI_00 | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[Windows 适配表](https://github.com/xzeldon/razer-battery-report#supported-mice) |
| Basilisk Mobile | 2025 | 鼠标 | `00D4 / 00D3` | 90B，`1F/1F`，参考实现仅查电量 | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)、[Windows 适配表](https://github.com/xzeldon/razer-battery-report#supported-mice) |
| Viper V3 Pro SE | 未核实 | 鼠标 | `00DF / 00DE` | 90B，`1F/1F`，与原 Pro 的接收器 PID 不同 | 单一来源。[驱动 PR 2818](https://github.com/openrazer/openrazer/pull/2818)、[OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/razer/devices.ts) |
| Viper V4 Pro | 2026 | 鼠标 | `00E6 / 00E5` | 90B，`1F/1F`，MI_03；其它控制命令有单独的代际差异 | 多方开源实现一致。[HaloBattery 实机](https://github.com/HeyOkay/HaloBattery/issues/333)、[Windows 支持表](https://github.com/xzeldon/razer-battery-report#supported-mice)、[Razer 官方](https://www.razer.com/newsroom/product-news/viper-v4-pro-arrives-in-esports-green) |
| Basilisk V4 Pro | 2026 | 鼠标 | `00E3 / 00E2` | 90B，`1F/1F`，MI_03 | 单一来源。[Windows 参数表](https://github.com/xzeldon/razer-battery-report/blob/master/crates/razer-hid/src/catalog.rs)、[实机记录](https://github.com/openrazer/openrazer/issues/2958)、[发布](https://www.razer.com/newsroom/product-news/basilisk-v4-line) |
| Basilisk V4 HyperSpeed | 2026 | 鼠标 | 未核实 / 未核实 | 未核实 | 未核实。[发布](https://www.razer.com/newsroom/product-news/basilisk-v4-line) |
| Naga V3 Pro | 2026 | 鼠标 | 未核实 / 未核实 | 未核实 | 未核实。[发布](https://www.razer.com/newsroom/product-news/razer-unveils-the-naga-v3-pro) |
| BlackWidow V4 Pro 75% | 2024 | 键盘 | `02B4`（候选） / `02B3` | 电量 TID、接口、能力未核实，不能按有线灯效路径启用 | 未核实。[uncoil 实机有线与接收器候选](https://uncoil.justinthevoid.com/docs/devices/) |
| BlackWidow V4 Mini HyperSpeed | 2024 | 键盘 | `02BA / 02B9` | 90B，`9F/1F`，两者 MI_03 | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)、[官方 2024 固件](https://mysupport.razer.com/app/answers/detail/a_id/14782/) |
| BlackWidow V4 Tenkeyless HyperSpeed | 2025 | 键盘 | `02D5 / 02D7` | 90B，`9F/1F`，MI_02 / MI_03 | 单一来源。[OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)、[发布](https://press.razer.com/product-news/razer-expands-its-custom-gaming-keyboard-lineup-with-the-blackwidow-v4-tenkeyless-hyperspeed/) |
| BlackWidow V4 Low-profile Tenkeyless HyperSpeed | 2025 | 键盘 | `02D2 / 02D4` | 90B，`9F/1F`，MI_02 / MI_03 | 单一来源。[OpenRazer 读电量](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)、[发布](https://www.razer.com/newsroom/product-news/razer-unveils-the-blackwidow-v4-low-profile-line) |
| BlackWidow V4 Low-profile HyperSpeed（全尺寸） | 2025 | 键盘 | 未核实 / 未核实 | 未核实 | 未核实。[发布](https://www.razer.com/newsroom/product-news/razer-unveils-the-blackwidow-v4-low-profile-line) |
| BlackShark V2 Pro (2023，PC) | 2023 | 耳机 | `0555` / 未核实；`0556` 也见 PA 实现，具体 SKU 未核实 | PA，`21/2A`，有远程会话限制 | 单一来源。[抓包衍生实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py)、[Razer 型号](https://mysupport.razer.com/app/answers/detail/a_id/20519/) |
| BlackShark V2 HyperSpeed | 2023 | 耳机 | `0565 / 056E`；`0566` 为候选接收器 | BlackShark 64B MediaTek 分支，`21/2A` | 多方开源实现一致。[WebHID 实机](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid#mediatek-protocol-specification)、[独立实测](https://github.com/bbesli/Synapse-for-Linux/blob/main/docs/PROTOCOL.md)、[有线枚举](https://github.com/openrazer/openrazer/issues/2316) |
| BlackShark V3 | 2025 | 耳机 | `057A` / 未核实 | BlackShark V3 专用 64 B，`21/2A` | 单一来源。[HaloBattery 实机](https://github.com/HeyOkay/HaloBattery/issues/292)、[系列发布](https://www.razer.com/newsroom/product-news/razer-launches-blackshark-v3-pro) |
| BlackShark V3 Pro | 2025 | 耳机 | `0577` / 未核实 | BlackShark 64B `21/2A`，实测需会话启动与唤醒 | 单一来源。[实机和帧](https://github.com/openrazer/openrazer/issues/2838)、[发布](https://www.razer.com/newsroom/product-news/razer-launches-blackshark-v3-pro) |
| Barracuda X Chroma | 2024 | 耳机 | 未核实 / 未核实 | PA 家族适用性、电量及充电字段未核实 | 未核实。[发布](https://www.razer.com/newsroom/product-news/introducing-the-barracuda-x-chroma) |
| Barracuda V2 Pro | 2026 | 耳机 | 未核实 / 未核实 | 未核实 | 未核实。[发布](https://www.razer.com/newsroom/product-news/barracuda-v2-pro) |

**型号核验说明。** `00C2/00C3` 在 OpenRazer 和 Windows 参考表是 DeathAdder V3 Pro 的另一对 ID，Viper V3 HyperSpeed 接收器用 `00B8`。`00CB` 是有线 Basilisk V3 35K，带 Pro 的无线版用 `00CD`。通用 HyperPolling `00B3` 与 Mouse Dock Pro `00A4` 用配对身份或显示接收器名称。其 PID 不代表内侧鼠标型号。 [OpenRazer PID 定义](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.h)、[Windows 交叉表](https://github.com/xzeldon/razer-battery-report#supported-mice)

**早期对照。** DeathAdder V3 Pro（2022 基础款）`00B7/00B6` 的 90B `1F` 已有实机记录。BlackShark V2 Pro (2020) `0528` 用 64 B Feature ID `FF`，与 2023 PA 分开。Barracuda Pro、Barracuda X (2022) 也按各自分支。 [DeathAdder 官方首发](https://www.razer.com/newsroom/product-news/the-legend-returns-razer-unveils-the-ultra-lightweight-deathadder-v3-pro)、[OpenMouse DeathAdder 实机](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/razer-testing.md)、[HaloBattery 耳机协议](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md)

**只读白名单。** 90B 只开放 `07:80` 和已证实可读的 `07:84`，锁定长度、参数、TID 与接口；禁止设置类命令，如 `07:01` 低电量阈值、`07:03` 睡眠时间、`03:10` 底座充电灯效、`04:05` DPI，以及配对、固件命令。PA/PA-Barracuda 限上述 `type 03` 的 `21/2A`；BlackShark 64B 限已知 domain、零参数的 `21/2A`，V2 HyperSpeed 可选本地 `20` 链路读。禁止 `93/95` EQ、`AC` 睡眠设置、`E6` 灯效及 `FF13` 维护通道。PA 的 `E1` 模式切换、V3 冷启控制以及固件更新命令全部排除出严格只读白名单。 [OpenRazer 命令实现](https://github.com/openrazer/openrazer/blob/master/driver/razerchromacommon.c)、[PA 原始实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py)、[V3 会话说明](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark_v3.py)

## 3. ATK

### Compx（适用于本节与后面的游狼）

**公共帧。** 17 B（含 report ID），`[0]=08 [1]=命令 [2]=状态 [3..4]=大端地址 [5]=有效载荷长度 [6..15]=数据 [16]=校验`。校验 `[16]=(0x55−sum([0]…[15])) & 0xFF`。跟 WebHID 拆开 ID 时，载荷校验目标是 `0x4D`，取低 8 位即模 256。电量 `08 04 00×14 49`。可选在线状态 `08 03 00×14 4A`，仅用于已证实支持的接收器。`04` 回复匹配 ID、命令、状态 `00`、地址与校验。百分比 `[6]`，`[7]` 已确认的组合中 0 未充电、1 充电或插线状态。可选 `[8..9]` 大端 mV，只在已知机型有该字段且值有效时使用。部分实机回复中 `[5]=02` 而后面仍出现看似电压的字节，百分比和充电有长度保障，mV 应在已取证的固件分支解析，避免把历史残留当新电压。 [atk-control 帧](https://github.com/NasirA1991/atk-control/blob/main/atk_control/protocol.py)、[ATK X1 Pro Max 抓包](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/captures/atk-x1-pro-max/receiver.json)、[atkctl 帧](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/CompxPacket.swift)、[游狼 Compx 实机](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md#6-the-compx-family)

| 传输 | Collection | 发送 / 接收 | 注意 |
| --- | --- | --- | --- |
| Compx-OUT | ATK 多见 `FF02:0002`，游狼 Compx 已知 `FF02`，精确 usage 依描述符 | ID `08` Output / ID `08` Input，均为 17 B | 先挂读再发；多个同接口 collection 时按报告类型和 ID 筛选 |
| Compx-FEAT | A9 Ultra `FF04`，具体 usage、MI 以实机描述符确认 | ID `08` Feature / ID `08` Input，均为 17 B | 用 SetFeature 发，读中断 Input；读 GetFeature 无法代替 Input |

### BitMouse（ATK HUB 的驱动名）

ATK HUB 把 `FF05:0001` 家族称为 BitMouse。PixArt PAW3395/3950 是传感器名称，Compx 和 BitMouse 型号均可用同类传感器。没有资料证明独立「PixArt 电量协议」可用作自动路由。前缀 `08` 后 63 B，Output/Input。以下 `P` 指 **不含 report ID** 的 63 B 缓冲。

- 请求布局 `P[0]=checksum, P[1]=72, P[2]=paramLen, P[3]=seq, P[4]=target, P[5]=cmd, P[6]=cmdLen, P[7…]=payload`。校验 `P[0]=sum(P[1…62]) & 0xFF`，查询序列 `00`，经接收器转发给鼠标 `target=01`。完整电量请求 `08 7D 72 02 00 01 07 01 00×56`；充电 `08 85 72 02 00 01 0F 01 00×56`。直连目标为 0，校验各减 1。 [atkctl 协议](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)、[OpenMouse BitMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/bitmouse/index.ts)
- **回复按两种观测布局处理。** OpenMouse 与 atk-control 实现接受 `P[0]=72 P[1]=状态 P[2]=序列 P[3]=目标 P[4]=命令 P[5]=长度 P[6…]=数据`，回复省略校验字节。atkctl 记录的布局在前面有一字节，`P[1]=72 P[2]=状态 P[5]=命令 P[6]=长度 P[7…]=数据`。先依据首部和该 PID 已验证的布局定位，再校验命令、状态和长度；`FF` 拒绝，观察到的 `73` 为主动事件，不作为请求回复。只取声明长度内字段，避免读取帧尾残留。在有校验的已证实分支检查校验。 [OpenMouse BitMouse 编解码](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/bitmouse/index.ts)、[atk-control 解码](https://github.com/NasirA1991/atk-control/blob/main/atk_control/protocol.py)、[atkctl](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)
- `07` 数据首字节为 0–100%。`0F` 数据首字节常见 0/1；atk-control 将 `3B` 也视为充电，应按该机型实测启用。它在部分设备上还读取 `07` 的第 1–2 个附加字节为小端 mV，需要回包长度与机型证据。 `1216` 是可共享的 NANO 接收器，支持时以读链路 `81`、读身份 `89` 区分设备。 [atk-control 状态读取](https://github.com/NasirA1991/atk-control/blob/main/atk_control/protocol.py)、[atkctl 命令表](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)

### Compx 设备

ATK 表的四位 PID 均用 VID `373B`，有注明其它 VID 时按注明识别。已知共享 `101A` 和 `101B` 分别是 1K 和 8K 接收器产品，不能分配给该系列的每一个 SKU。表内只有来源指明的组合才绑定型号。未核实的候选行仍适用「零主动探测」规则。

| 型号 | 年 | 种类 | 接收器 PID / 有线 PID | 家族与电量路径（含充电字段） | 可信度和来源 |
| --- | --- | --- | --- | --- | --- |
| X1 Pro | 2024（系列） | 鼠标 | `101A` 1K / 未核实 | Compx-OUT `04` | 单一来源。[电量实机](https://github.com/HeyOkay/HaloBattery/issues/230)、[型号表](https://www.atk.store/products/atk-blazing-sky-x1-series-wireless-mouse) |
| X1 Pro Max | 2024（系列） | 鼠标 | 未核实 / `1017`（型号表） | Compx-OUT `04`；无线抓包回复 `19 00` 为 25% / 未充电，但抓包缺 USB ID | 未核实。[协议抓包](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/captures/atk-x1-pro-max/receiver.json)、[ID 候选](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-x1-series-wireless-mouse) |
| X1 Ultra | 2024（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/43，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-x1-series-wireless-mouse) |
| X1 Ultimate | 2024（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/46，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-x1-series-wireless-mouse) |
| F1 Pro | 2024（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/36，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-f1-series-wireless-mouse) |
| F1 Pro Max | 2024（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/37，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-f1-series-wireless-mouse) |
| F1 Ultimate | 2024 | 鼠标 | `1031 / 102E` | Compx-OUT `04`，CID/MID 2/42；线连标志是否严格表示正在充电未核实 | 单一来源。[ATK_tray 设备表与电量实现](https://github.com/Fan4Metal/ATK_tray/blob/master/models.py)、[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[2024 厂商公告](https://www.dreamnews.jp/press/0000299338/items/88a763) |
| F1 Ultimate 2.0 | 2025（V2 系列） | 鼠标 | 未核实 / 未核实 | Compx-OUT 候选，CID/MID 1/8；接收器与线连 USB ID 未核实 | 未核实。[OpenMouse ATK 驱动](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/atk/hid.ts)、[F1 V2 官网](https://www.atk.store/products/atk-blazing-sky-f1-v2-series-esports-wireless-mouse) |
| F1 Ultra | 2024（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/45，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[F1 家族](https://www.atk.store/blogs/news/atk-f1-vs-atk-x1) |
| A9（基础版） | 2025（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/99，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-dragonfly-a9-series-lightweight-wireless-mouse) |
| A9 Pro Max | 2025（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/85；与 2.0 NK 分开 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-dragonfly-a9-series-lightweight-wireless-mouse) |
| A9 Ultra | 2025（系列） | 鼠标 | `111A` 8K；另有 `101A/101B` 出现在独立实现 / `1119` | 主配对 `111A/1119` 为 Compx-FEAT `04`；`101A/101B` 的同名替换组合需各自确认 collection 与电量 | 单一来源（`111A` 无线分支）。[atkctl 硬件记录](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)、[VID 与目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift)、[独立实现的其它接收器](https://github.com/xb-bx/atk-a9-ultra-driver/blob/master/main.odin) |
| A9 Ultimate | 2025（商店） | 鼠标 | `11D9` 8K / `11B6` | Compx-OUT `04`，固件 3.03，CID/MID 1/3 | 多方开源实现一致。[ATK_tray](https://github.com/Fan4Metal/ATK_tray/blob/master/models.py)、[实机记录](https://github.com/NasirA1991/atk-control/blob/main/docs/PROTOCOL.md)、[官网](https://www.atk.store/products/atk-dragonfly-a9-ultimate-lightweight-wireless-mouse) |
| A9 Air+ | 未核实 | 鼠标 | `1278` / 未核实 | Compx-OUT `FF02:0002`，17 B 电量实机；充电单独实测未核实 | 单一来源。[实测及 A9 Air 命名说明](https://github.com/wushuai0802/myfinder-device-battery) |
| U2 Pro Max | 2025（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/100，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-u2-series-wireless-mouse) |
| U2 Ultra | 2025（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/66，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-u2-series-wireless-mouse) |
| U2 Ultimate | 2025（系列） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/65，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-u2-series-wireless-mouse) |
| U2 Mini | 2025（商店） | 鼠标 | 未核实 / 未核实 | Compx 候选，CID/MID 2/64，电量路径未核实 | 未核实。[型号识别表](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[官网](https://www.atk.store/products/atk-blazing-sky-u2-mini-superlight-wireless-gaming-mouse) |

### BitMouse 与待分流型号

| 型号 | 年 | 种类 | 接收器 PID / 有线 PID | 家族与电量路径（含充电字段） | 可信度和来源 |
| --- | --- | --- | --- | --- | --- |
| ATK ZERO | 2026（商店） | 鼠标 | `1155 / 1154` | BitMouse `07/0F`，回复无前置校验字节分支；有线电量在不同实现中可靠性不一致 | 多方开源实现一致（无线）。[OpenMouse 实机](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/bitmouse/index.ts)、[ATK_tray](https://github.com/Fan4Metal/ATK_tray/blob/master/models.py)、[官网](https://www.atk.store/products/atk-blazing-sky-zero-ultralight-wireless-gaming-mouse) |
| A9 Plus NK（星闪对照） | 2025（A9 系列） | 鼠标 | `1216` / `1263` 实测；另有接收器 `10C9` 与目录线连 `1224/1300` | `1216` 为 BitMouse `07/0F`，`10C9` 固件 1.20/1.30 抓包为 Compx-OUT `04`。两者均为星闪接收器 | 单一来源（各分支）。[atkctl 1216 实机](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)、[OpenMouse 10C9 抓包](https://github.com/OpenMouse-Project/mouse-protocol/tree/main/captures/atk-a9-plus-nk) |
| A9 Pro Max 2.0 NK（星闪对照） | 未核实 | 鼠标 | `11DC` 候选 / `1264`，另见 `11DB/1301` | BitMouse 候选 `07/0F`，精确接收器绑定有待核实 | 未核实。[atk-control](https://github.com/NasirA1991/atk-control/blob/main/atk_control/devices.py)、[atkctl 型号目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift) |
| A9 3.0 NK（星闪对照） | 未核实 | 鼠标 | `1216` 候选 / `1305` | BitMouse 候选；无线电量未成功核实 | 未核实。[识别目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift)、[读取失败记录](https://github.com/wushuai0802/myfinder-device-battery) |
| A9 Ultimate+ | 2025（系列） | 鼠标 | 未核实 / `1287/12F1`（目录） | BitMouse 候选；具体无线部件与帧未核实 | 未核实。[atkctl 目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift)、[官网](https://www.atk.store/products/atk-dragonfly-a9-ultimate-lightweight-wireless-mouse) |
| X1 V2 Ultimate 2.0+ | 2025（V2 系列） | 鼠标 | 未核实 / `12D9`（目录） | BitMouse 候选；电量路径未核实 | 未核实。[atkctl 目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift)、[官网](https://www.atk.store/products/atk-blazing-sky-x1-v2-series-esports-wireless-mouse) |
| F1 V3 Ultimate+ | 2026（系列） | 鼠标 | 未核实 / `1304/1308`（目录） | BitMouse 候选；电量路径未核实 | 未核实。[atkctl 目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift)、[官网](https://www.atk.store/products/atk-blazing-sky-f1-v3-series-esports-wireless-mouse) |
| F1 V3 Ultra Max | 2026（系列） | 鼠标 | 未核实 / `1307`（目录） | BitMouse 候选；电量路径未核实 | 未核实。[atkctl 目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift)、[官网](https://www.atk.store/products/atk-blazing-sky-f1-v3-series-esports-wireless-mouse) |
| U2 V2 Ultimate | 2025（V2 系列） | 鼠标 | 未核实 / 未核实 | 分流、电量及充电均未核实 | 未核实。[官网](https://www.atk.store/products/atk-blazing-sky-u2-v2-superlight-wireless-gaming-mouse) |
| U2 Plus NK（星闪对照） | 未核实 | 鼠标 | 未核实 / `12E3`（目录） | BitMouse 候选；电量路径未核实 | 未核实。[atkctl 目录](https://github.com/cobanov/atkctl/blob/main/Sources/ATKKit/Catalog.swift) |

官方商店目录可辅助区分系列和型号，但新配色与配件会复用老链接。 [ATK 官网发布目录数据](https://www.atk.store/products.json?limit=250&page=1)

**只读白名单。** 两种 Compx 传输允许 `04`，仅在已核实分支允许 `03` 在线、`10` 身份、`12/1D` 固件版本查询。EEPROM `08`、配置 `0E` 属读命令，但无需为电量开放。禁止 `07` EEPROM 写入、`0F` 切配置以及配对、DFU。BitMouse 默认只开放电量 `07`、充电 `0F`，可选 `81` 链路、`1A` 连接类型、`1C/88` 版本、经过查证的 `4A/89` 纯读参数组合。禁止 `01/02/03` 等设置、`13` 重置、`82/83` 配对/忘记配对，以及 `FFB1/FFB3`、Report ID `09` 的已知 ATK 更新路径。唯有明确电量证据的其它厂商描述符分支才单独定义其 ID。 [atkctl 命令表](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)、[atk-control](https://github.com/NasirA1991/atk-control/blob/main/atk_control/protocol.py)

## 4. 游狼 G-Wolves

### XVI Feature 家族

两代通常走同一个形状的 HID Feature collection，已知实机为 `MI_02 / FFFF:0000`，Report ID `00`，64 B payload。Win32 缓冲区 65 B，WebHID 将 ID 和 payload 分开。枚举时以报告 ID、类型、实际报告长度和 collection 为准。公开实现未发现这两条电量查询有应用层校验。下面是包含 Win32 report ID 的模板。

| 分支 | 电量与充电合并请求 | 完整回复匹配与数据 |
| --- | --- | --- |
| XVI-OLD | 无线 `00 00 02 8F 01 00×60`；有线把 `01` 改为 `00` | `00 A1 02 8F XX CC BB …`，`[5]=CC` 充电，`[6]=BB` 百分比 |
| XVI-NEW | 无线与相应有线鼠标 `00 00 00 02 02 00 83 00×58` | `00 A1 00 02 02 00 83 CC BB …`，`[7]=CC` 充电，`[8]=BB` 百分比 |

`CC` 已知 `00/01` 为放电/充电，其它保留为未知。百分比应验证 0–100。先 SetFeature，等设备准备，再 GetFeature，实测新协议约 100 ms，旧协议具体等待以型号为准。一次请求后可以在同一短截止时间内读有限次，回包须符合已知格式。失效头、超时、休眠或拔插后不更新电量；Feature 缓存无事务号，连续读取同一内容无法证明无线端仍在线。不要把报告 ID `00` 与 payload 起始的 `00` 合并。 [游狼 WebHID 驱动](https://mouse.xyz/)、[overlay 实机与帧](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md)、[HaloBattery 型号映射与旧帧](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[OpenMouse 旧分支](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/gwolves/xvi-hid.ts)、[新分支](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/gwolves/xvi-new-hid.ts)

### 按协议家族登记

游狼表四位 PID 均用 VID `33E4`。目录里同一共享接收器可服务多个鼠标；带不同后缀的线连 ID 按硬件版本登记。`3854` 上以实机的 **17 B Compx-OUT** 为一条可用分支。HaloBattery 从网页表将这个 PID 列为 **65 B XVI-NEW** 候选，但未实测该接收器；仅凭这个 PID 不发送 XVI 帧。对具体设备行，存在独立证据的 Compx 组合可按对应 collection 识别。 [OpenMouse 机型矩阵](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[overlay 接收器实机](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md)、[HaloBattery 型号表](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)

| 型号 | 年 | 种类 | 接收器 PID / 有线 PID | 家族与电量路径（含充电字段） | 可信度和来源 |
| --- | --- | --- | --- | --- | --- |
| HTX Ultra / HTXU | 2025（商店） | 鼠标 | `3854 / 5618`；另有目录 `3854 / 5619`、`5617 / 5608` | `3854/5618` 实机 Compx-OUT `04`；`5617/5608` 为 XVI-NEW 候选 | 单一来源（`3854/5618`）。[硬件与候选表](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/htx-ultra-8k-wireless-mouse) |
| Fenrir Pro | 2025（商店） | 鼠标 | Receiver RS `3854 / 3619`；独立 `3617 / 3608` | RS 为 Compx-OUT `04`，独立 8K 为 XVI-NEW `83` | 多方开源实现一致（RS 的 ID、描述符及帧）。[RS 电量实测](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md#6-the-compx-family)、[OpenMouse 两分支](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/g-wolves-fenrir-pro-8k-wireless-mouse) |
| Lycan | 2025（商店） | 鼠标 | `3854 / 4718,4719`（目录） | Compx-OUT 候选，按 `3854` 实机分流，具体鼠标身份与无线电量未核实 | 未核实。[OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/lycan-8k-wireless-mouse) |
| HTS Plus Pro 8K | 2026（商店） | 鼠标 | `3854 / 5219`（目录） | Compx-OUT 候选，电量实测未核实 | 未核实。[机型矩阵](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/hts-plus-pro-8k-wireless-gaming-mouse-28g) |
| Warg | 2026（商店） | 鼠标 | `3854 / 4219`（目录） | Compx-OUT 候选，电量实测未核实 | 未核实。[机型矩阵](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/g-wolves-warg-8k-wireless-gaming-mouse) |
| HTX Mini Asym | 2026（商店） | 鼠标 | `3854 / 2619`（目录） | Compx-OUT 候选，电量实测未核实 | 未核实。[机型矩阵](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/htx-mini-asym) |
| Fenrir Asym 8K | 2025（商店） | 鼠标 | `3517 / 3508`；另有目录 `3854 / 3519` | `3517/3508` 存在 XVI-OLD `8F` 与 XVI-NEW `83` 两条实机实现；`3854/3519` 为 Compx 候选 | 未核实（自动分流）。[overlay 新帧实测](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md)、[OpenMouse 旧帧实机](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/g-wolves-fenris-asym-8k-wireless-mouse) |
| Fenrir Max 8K | 2025（商店） | 鼠标 | `3717 / 3708` | XVI-OLD `8F` 候选 | 单一来源。[网页表衍生映射](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/g-wolves-fenris-max-8k-wireless-mouse) |
| HTX Mini 8K | 2024（商店） | 鼠标 | `2717 / 2708`；另有目录 `3854 / 2719` | `2717` 实机 XVI-OLD `8F`；后者为 Compx-OUT 候选 | 单一来源（`2717` 实证）。[机型矩阵](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/htx-mini-8k-wireless-gaming-mouse-1) |
| HTS Plus 8K | 2024（8K 商品页） | 鼠标 | `5417 / 5408`（目录）；同名另见 `3854 / 5418,5419` | XVI-OLD `8F`；共享 8K 组合另列 Compx-OUT 候选 | 单一来源（`5417` 网页分支）。[协议映射](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/hts-plus-hts-8k-wireless) |
| HTS Ultra | 2024（商店） | 鼠标 | `5317 / 5308` | XVI-OLD `8F` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/hts-ultra-8k-wireless-gaming-mouse-29g-usb-type-c-port) |
| HT-S2 | 2024（正式商店页） | 鼠标 | `7913 / 7904` | XVI-OLD `8F` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/g-wolves-ht-s2-8k) |
| HT-S2 Pro | 2024（商店） | 鼠标 | `7917 / 7908` | XVI-OLD `8F` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/g-wolves-ht-s2-pro-8k-wireless-mouse) |
| HTR | 2024（商店） | 鼠标 | `7713 / 7704` | XVI-OLD `8F` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/g-wolves-htr-8k-wireless) |
| HTR Pro | 2024（商店） | 鼠标 | `7717 / 7708` | XVI-OLD `8F` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/g-wolves-htr-pro-8k-wireless) |
| HSK Plus Lite | 2023（商店） | 鼠标 | `7203 / 7204`（驱动称 HSK Lite） | XVI-OLD 候选；商品名与驱动名的实机绑定未核实 | 未核实。[型号表](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/docs/gwolves-devices.md)、[官网](https://shop.g-wolves.com/products/g-wolves-hsk-plus-hsk-lite-wireless-gaming-mouse) |
| HSK Plus ACE-2 | 2023（商店） | 鼠标 | 未核实 / 未核实 | 网页的 HSK Plus ACE 为 `5903/5904`，商品后缀 ACE-2 的绑定未核实 | 未核实。[网页型号表](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网目录](https://shop.g-wolves.com/products.json?limit=250&page=1) |
| HSK Pro 2.0 8K | 未核实（商店链接复用自旧版） | 鼠标 | `6817 / 6808` | XVI-NEW `83` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[当前商品标题沿用旧 4K URL](https://shop.g-wolves.com/products/g-wolves-hsk-pro-4k) |
| HTM Plus | 2025（商店） | 鼠标 | `3817 / 3808` | XVI-NEW `83` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/htm-plus-8k-wireless-mouse) |
| VUK | 2025（商店） | 鼠标 | `3917 / 3908` | XVI-OLD `8F` 候选 | 单一来源。[网页分支](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)、[官网](https://shop.g-wolves.com/products/vuk-8k-wireless-mouse-35g) |

**HTX 与 HSK 的早期版本。** 初代 HTX ACE、HTX 4K、HSK Pro ACE、HTS Plus ACE 的官方商店页早至 2022 年，不纳入近三年主表。网页驱动列出 XVI-OLD 候选接收器 / 有线组合 HTX ACE `5703/5704`，HTX 4K `5707/5708`，HSK Pro ACE `5803/5804`，HTS Plus ACE `5403/5404`。HTX 和 HSK Pro 的 8K 接收器目录分别是 `5717` 和 `5817`，确切 SKU 首发年份与具体固件电量实测未核实。 [厂商商品目录](https://shop.g-wolves.com/products.json?limit=250&page=1)、[网页驱动衍生映射](https://github.com/HeyOkay/HaloBattery/blob/main/providers/gwolves.py)

**只读白名单。** XVI-OLD 仅允许上表 `8F` 的完整头与目标；XVI-NEW 仅允许 `83` 的完整头与目标。禁用 `B0` 进 bootloader、固件擦写和任何设置命令，高位为 1 不足以判断只读。游狼 Compx 仅允许对应 17 B 路径的 `04`，其它读身份或链路命令需先按这个接收器证实。禁止 EEPROM 写、配对、灯效、复位、模式切换。 [overlay 命令定义与风险](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md)、[OpenMouse Compx 使用](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/src/drivers/gwolves/hid.ts)

## 5. Windows 11 识别与只读探测

1. **枚举阶段保持零发送。** 记录 USB 父实例、容器 ID、硬件 ID、产品字符串、MI、TLC usage page/usage、Input/Output/Feature 最大长度，以及各报告类型真实的 report ID 与长度。用 `HidD_GetPreparsedData / HidP_GetCaps` 配合描述符能力获取。Win32 caps 长度包括 ID 槽且给的是该类报告最大长度。区分报告类型、ID 和长度，排除标准鼠标、键盘输入集合及维护集合。白名单匹配可使用 `VID:PID + MI + usage/page + 方向/报告 ID/长度`，跨 VID 的公版 Compx 只作为族线索。 [微软 HID CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps)、[微软发送 HID 报告](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/sending-hid-reports)、[微软接收 HID 报告](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
2. **已知 PID 自动选择路径。** 先用 PID 识别互斥候选，再用 collection 限到唯一匹配。ATK 同接口可同时有 `FF02/FF04/FF05`，游狼有共享 `3854`，Razer 新键盘可能有多个大小相同的集合。共享接收器标为接收器或用已证实的只读型号查询确认，禁止把一个应答广播给可能配对的所有产品名。复合设备按物理接收器串行，区分有线和无线实例再考虑去重。
3. **未知 PID 先被动识别。** 优先记录设备元数据、被动 report 或厂商网页对应的只读刷新抓包。经证据确认同一协议家族且 collection 唯一后，可对该接收器做 **一次受控白名单查询**。例为可证明是 HID++ 的 Logitech `046D` 标准报告组合，可用一次 IRoot Ping，再按真实应答发现 feature。私有通道只有描述符相似时不主动发送，尤其是有着相同 Feature 长度的 XVI 两代与 Razer 耳机；先补厂商路由或抓包证据。无响应、校验、长度、状态或事务不符即停止，不轮试 protocol、collection、TID 或命令号。调试配置也不开放任意报文入口。
4. **失败和并发。** 设置有限截止时间，按物理接收器单请求在途，热拔插立即取消 I/O 并废弃槽位缓存。已匹配的 replies 与主动推送分开；遇到厂商程序占用时退避。只显示最后有效读数与采集时间，离线、休眠、充电未知和真实 0% 分开。少量轮询也可能唤醒无线链路，首轮 Windows 11 实机验收应核对充电前后、睡眠、拔插、两个设备共用接收器以及厂商工具同时打开。

同一个命令字跨家族可能有完全不同的作用。例如游狼 XVI-NEW 的 `83` 是电量查询，ATK BitMouse 的 `83` 会忘记配对。白名单应验证完整帧与路由。 [游狼协议](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md)、[ATK BitMouse 命令表](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)

## 6. 参考实现许可证

本清单归纳公开协议事实。几格的 MIT 实现应独立编写收发、校验、状态机与测试。引用 GPL/AGPL 项目的协议来源，避免复制代码、曲线表、设备数据库或测试实现。分支变动时以实际使用文件的许可声明为准。

| 项目 | 许可证 / 证据 |
| --- | --- |
| Solaar | 仓库 GPL-2.0；本清单引用的 Python 文件多注明 GPL-2.0-or-later。[许可证](https://github.com/pwr-Solaar/Solaar/blob/master/LICENSE.txt)、[Centurion 源文件声明](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/centurion.py) |
| Linux（hid-logitech-hidpp） | Linux 仓库 GPL-2.0-only 总体许可，具体文件依 SPDX/文件头。[COPYING](https://github.com/torvalds/linux/blob/master/COPYING)、[驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c) |
| HeadsetControl | GPL-3.0。[许可证](https://github.com/Sapd/HeadsetControl/blob/master/license) |
| OpenRazer | 仓库 GPL-2.0；具体驱动查文件头。[仓库](https://github.com/openrazer/openrazer)、[公共协议源码](https://github.com/openrazer/openrazer/blob/master/driver/razercommon.c) |
| HaloBattery | MIT。[许可证](https://github.com/HeyOkay/HaloBattery/blob/main/LICENSE) |
| BlackShark V2 HyperSpeed WebHID / Synapse-for-Linux | MIT。[WebHID 许可](https://github.com/justik13/razer-blackshark-v2-hyperspeed-webhid/blob/main/LICENSE)、[Synapse-for-Linux 许可](https://github.com/bbesli/Synapse-for-Linux/blob/main/LICENSE) |
| Razer Battery Report | MIT。[许可证](https://github.com/xzeldon/razer-battery-report/blob/master/LICENSE) |
| atkctl / atk-control | MIT。[atkctl](https://github.com/cobanov/atkctl/blob/main/LICENSE)、[atk-control](https://github.com/NasirA1991/atk-control/blob/main/LICENSE) |
| atk-a9-ultra-driver | GPL-2.0。[许可证](https://github.com/xb-bx/atk-a9-ultra-driver/blob/master/LICENSE) |
| ATK_tray | 所引用仓库未核实到许可证文件，仅作为协议与型号线索。[仓库](https://github.com/Fan4Metal/ATK_tray) |
| myfinder-device-battery | AGPL-3.0-only，见项目说明。[仓库](https://github.com/wushuai0802/myfinder-device-battery) |
| gwolves-battery-overlay | MIT。[许可证](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/LICENSE) |
| OpenMouse mouse-protocol | AGPL-3.0。[许可证](https://github.com/OpenMouse-Project/mouse-protocol/blob/main/LICENSE) |
| OpenLogi | 工作区代码 MIT 或 Apache-2.0，设计资产另有声明。[Cargo 清单](https://github.com/AprilNEA/OpenLogi/blob/master/Cargo.toml)、[协议参考](https://openlogi.org/hidpp/features/x1004-unified-battery) |
| uncoil | GPL-3.0-or-later。[Cargo 清单](https://github.com/justinthevoid/uncoil/blob/main/Cargo.toml)、[设备文档](https://uncoil.justinthevoid.com/docs/devices/) |
| ATK HUB / mouse.xyz / Logitech HID++ 草案 | 厂商资料。WebHID bundle 未核实有允许复用的开源许可证。Logitech 草案有其自身声明。[ATK HUB](https://hub.atk.pro/)、[游狼网页驱动](https://mouse.xyz/)、[HID++ 草案](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf) |

本次读取的主要源码快照可按提交定位。[Solaar](https://github.com/pwr-Solaar/Solaar/tree/e7304c4c451c)、[OpenRazer](https://github.com/openrazer/openrazer/tree/ab2452847d12)、[HeadsetControl](https://github.com/Sapd/HeadsetControl/tree/163c6edf744c)、[HaloBattery](https://github.com/HeyOkay/HaloBattery/tree/73e229cbee2e)、[atkctl](https://github.com/cobanov/atkctl/tree/1755d8510663)、[atk-control](https://github.com/NasirA1991/atk-control/tree/4822253b0b02)、[OpenMouse mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol/tree/93c4cc601d44)、[gwolves-battery-overlay](https://github.com/maximelonguepe/gwolves-battery-overlay/tree/7e11d7154f72)、[Razer Battery Report](https://github.com/xzeldon/razer-battery-report/tree/c3055e808085)

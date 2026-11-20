# 国产无线鼠标网页驱动与只读电量协议调研

调研日期 2026-11-20。仓库基线 `16f67a5`。范围以 2.4 GHz USB 接收器为主，有线 ID 用于区分充电、共享接收器和可替换路径。[型号索引](domestic-mice-models.json) 汇总 **17 个品牌、235 个唯一 VID:PID、271 条品牌/型号/连接映射**，其中接收器 115 个、有线 121 个，有一个 VID:PID 两种模式共用。数字包含候选过滤项，不能当作支持数或市场份额。

可用证据归纳为 **13 组读包协议（含各组内的传输变体）**。其中 Compx 和 XVI-NEW 兼容帧都有可复用路径。Compx 同时包含不同的 HID 传输变体。另列 BitMouse 共享接收器和几组只有路由证据的候选。最适合先做的是 VXE R1 及 MAD R、VGN 早期蜻蜓和已取证的 Scyrox 接收器。首批按已知接收器精确路由，尤其避免把整个 `3554` 直接打开。 [现有鼠标目录](../src/Jige.Core/Adapters/MouseReceiverCatalog.cs) · [现有协议](../src/Jige.Core/Adapters/MouseBatteryProtocol.cs) · [冲突实例](device-catalog.md#5-windows-11-识别与只读探测)

## 1. 方法与读表约定

- **网页驱动 JS 直接可见（W）**指在官网入口、入口加载的 JS 或其静态 JSON 中找到相应事实。**多个开源实现一致（M）**指有可交叉核对的实现或独立记录。派生实现明确注明，证据按共同原始来源处理。**单一来源（S）**包含一个开源实现、单方抓包及仅有官方支持页的入口。**推测（I）**表示有家族线索但尚缺交叉验证。字段缺失写「未核实」。
- 用 curl 下载 HTML、JS 和目录 JSON 后做文本检索和 JSON 解析。追踪 `requestDevice`、`sendReport`、`sendFeatureReport`、`receiveFeatureReport`、`inputreport`、型号表、查询函数和应答分支。第三方 JS 与固件均未执行，也没有打开任何 HID 设备。开源仓库静态阅读快照见末尾。Win32 的集合、响应长度与睡眠行为仍需实际设备核实。
- 从本节起，字节索引均从 0 开始，写成 **Win32 带 ID 的缓冲区**。ID 0 也占首字节。`00…` 表示向指定长度补 0。某些开源库剥掉了 ID，表中标注了换算前提。Windows caps 的长度含 ID，通常是该类型的最大长度。若来源只给了有意义的帧或 JS 载荷长度，仍须核实 Windows caps。`MI_xx` 仅在有描述符或实机记录时给出。网页对象的 `interface:1` 和浏览器过滤条件不足以证明 Windows 的 MI。 [WebHID](https://wicg.github.io/webhid/) · [HIDP_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps) · [Windows 接收 HID 报告](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
- 型号 JSON 记录可确认连接类别的公开事实，含 WLmouse 和 Lamzu 两个目录的常规有线/1K/4–8K 条目、四套 Control HUB 的鼠标过滤清单及 VGN 主包中的鼠标路由。排除键盘、空 PID、独立 bootloader 字段和维护接口。某些机型的应用与维护身份复用同一 PID，仍需检查集合。共享 PID 保留多行，未知零售型号标为「型号未核实」。JSON 的 `family` 是证据索引，带 `candidate` 的行不能直接进入查询白名单。其余厂商仅导出已辨认的子集。

## 2. 品牌与网页驱动入口

| 品牌／系列 | 网页入口与官方出处 | 桌面产品或协议证据边界 |
| --- | --- | --- |
| VXE（R1 蜻蜓、MAD R） | [ATK HUB](https://hub.atk.pro/) · [官方网页版说明](https://www.atk.store/blogs/news/atk-v-hub-web-version-instructions) | W／M。前一代 R1 Compx、部分后续共享 NK 接收器走 BitMouse。 |
| VGN（蜻蜓 F1/F2/Y2/Dragonfly 3，雷鸣系列） | [VGN HUB](https://drive.vgnlab.com.cn/) · [官方 V HUB](https://download.vgnlab.com.cn/) · [官方品牌 FAQ](https://vgnlab.com/pages/faq) | W。现包鼠标路由包括 Compx、`yongjiaxin`、`vgn-ldMs`、`vgn-king`。雷鸣的营销名与 VID:PID 映射未核实。 |
| MAD Catz（R.A.T. DWS、M.O.J.O. 等） | [官方驱动目录](https://www.madcatz.com/En/Support/Downloads/) · [R.A.T. DWS 官方页](https://www.madcatz.com/en/Product/Detail/rat-dws) | S。公开入口核实到软件下载，鼠标 WebHID 与报文未核实。VXE 的 MAD R 属于上方 VXE 产品线。 |
| Rapoo 雷柏 | [RAPOO HUB 国内](https://hub1.rapoo.cn/) · [国际版](https://hub.rapoo.com/home) · [官方下载](https://shop.rapoo.com/pages/software-download) | W／S。VT7、VT9 Pro、VT9 Pro V2 需分代。 |
| DAREU 达尔优 | [All in one WEB](https://driver.dareu.com/) · [官方国内入口](https://cn.dareu.com/drivers/#driver-entry-web-driver) · [UNI HUB](https://unihub.dareu.com/) | S／M。A950 家族有分集合 Feature 和单集合 Output 两种走法。 |
| AULA 狼蛛 | [AULA HUB 网页](https://hub.aulacn.com/home) · [国内官网入口](https://www.aulacn.com/index/index/webdriver) · [品牌下载页](https://www.aulagaming.com/pages/download) | S。SC380 Pro 有可查协议。SC680/SC800/SC900 的鼠标路由待查。HaloBattery 的另一个 AULA 命令 `AA 10` 用在键盘路径。 [AULA 键盘实现][ha-aula] |
| AJAZZ 黑爵 | [官方下载](https://www.a-jazz.com/en/h-col-159.html) · [AJ179 系列驱动](https://ajazzstore.com/blogs/software/ajazz-aj179-series-driver) | S。[NJ07/NJ08 的 NACODEX 公版面板](https://nacodex.yjx2012.com/) 已取到包；其与黑爵主站的直接互链未核实。AJ179 V2 MAX 有硬件报告。 [网页 JS][js-nj] |
| MCHOSE 迈从 | [M HUB WEB](https://www.mchose.com.cn/) · [官方支持页](https://www.maicong.cn/support/hub/) · [M HUB 使用指南](https://support.mchose.store/hc/en-us/articles/49078340935700-Getting-Started-with-MCHOSE-M-HUB) | S／M。至少四种鼠标电量路径，详见第 3 节。 |
| Darmoshark 达摩鲨 | [官方链接的网页驱动](https://darmoshark.keybord.net.cn/) · [本土官网](https://www.darmoshark.cn/) · [国际鼠标下载](https://darmoshark.com/pages/mouse) | W。与机械师共享 Keybord WebHID 桥接代码；M3/M5/M2 等鼠标的命令分支未核实。 |
| Incott | [Incott Mouse Driver](https://incott.net/mouse/) | S。网页的发布身份和当前包这次未能复核。OpenMouse 保存零售设备实测记录；具体 G 系列零售名与接收器型号映射未核实。 |
| Scyrox | [S-center](https://www.scyrox.net/) · [品牌下载页](https://scyrox.com/pages/download) | W／M。Compx Control HUB 同源模板。 |
| WLmouse 浪 | [WL MOUSE HUB](https://gm.wlmouse.gg/) · [品牌下载页](https://www.wlmouse.com/pages/download) | W／M。`36A7` 多个型号用 XVI-NEW 兼容 Feature；旧 4K 变体要核对。 |
| Lamzu 兰族 | [Aurora 网页](https://www.lamzu.net/) · [品牌下载页](https://lamzu.com/pages/download) | W／M。Maya X 用 XVI-NEW 兼容 Feature，旧 Atlantis 用 Compx。 |
| Zaopin | [Control HUB](https://controlhub.top/zaopin/) · [页面配置标示的品牌网址](https://www.zaopinworld.com/) | W。模板配置明确使用 Zaopin 品牌。Z1、Z2、ZPW 等零售型号逐个映射未核实。 |
| ATTACK SHARK | [Control HUB](https://controlhub.top/AttackShark/) · [XValley 网页](https://www.xvalleyinno.top/AttackShark/#/project/items) · [官方逐型号入口](https://attackshark.com/pages/driver-download) | W／M。并存 Compx、`1D57` 主动上报、GearHub `25A7` 和 `373E` Feature 候选。 |
| IROK 艾石头 | [说明书所列网页驱动](https://driver.irok.cn/) · [说明书镜像](https://manuals.plus/m/3d32cc5f3dcdcaa1e6245417b74774009083d060f8f91289af06faf65768a70d.pdf) · [官方站](https://www.irok.cn/) | W。官网脚本实际链接 [IROK HID](https://hid.irok.cn/)。现包注明 HE3 SE 鼠标，并含 HE6 Ultra 与 Compx 分支，见第 4 节。 [集团官网 JS][js-irok-home] · [IROK HID JS][js-irok] |
| MACHENIKE 机械师 | [官方网页驱动中心](https://www.machenike.com/webdriver/) | W。与达摩鲨共用 Keybord 桥接，鼠标路由未核实。 |
| HECATE 漫步者 | [漫步者下载与支持](https://cnweb.edifier.com/driver.html) · [HECATE Download Center](https://hecategaming.com/en-global/pages/download-center) · [G5M Pro 官方说明书](https://cn-oss.edifier.com/Uploads/images/2024/09/06/2024090610422017255905402864.pdf) | S。G3M/G5M 的安装式软件线索可见，官方鼠标 WebHID URL、报文与型号表未核实。 |
| EWEADN 前行者 | [E HUB](https://hub.eweadn.cn/) · [官方驱动页](https://www.eweadn.cn/driver) | W／S。通用入口含 Compx 模板。已取证的 S9 是独立 `zhanli-receiver` 路由。 |
| Hyeku 黑峡谷 | 官方鼠标网页入口未核实。 [存档下载索引](https://drivers.mydrivers.com/s-30-2386/h0-0-0-0-15-1.htm) | S。下载索引不足以确认网页驱动。 |
| Waizowl | [Owlnest 网页驱动](https://owlnest.waizowl.com/) · [品牌网页驱动页](https://waizowl.com/zh-hans/pages/w-hub) | W。包中有 OGM 系列多条设备路由。至少两类集合/通知解析。 |
| Beyond | 品牌全名、官网及鼠标 WebHID URL 未核实。相近名称的搜索结果无法明确归属。 | 未核实。没有用卓威驱动替代这一项。 |
| Delux 多彩 | [DELUX HUB WEB](https://www.controlhub.top/delux/) · [官方服务页](https://deluxworld.com/en/service?page=1&stoken=9019070fe1ad2e164cdb2add277ae943) | W／S。新款 Control HUB 与较早的 `1D57` M800 Mini/M600 Pro 共存。 |
| RAWM 仁魔 | [官方 RAWM HUB 网页](https://www.rawmtech.com/hub.html) | W／S。引入 MiracleTek CDN 库，`1915` JSON 状态协议有开源复核。 |
| Inphic 英菲克 | [INPHIC HUB](https://hub.inphic.cn/) | S。IN9 Pro 的开源硬件记录与 ATTACK SHARK `1D57` 被动上报同族。 [HaloBattery][ha-inphic] |
| Ninjutso | [NinjaForce](https://ninjaforce.co/) · [品牌下载页](https://ninjutso.com/pages/download) | S。Sora 家族 Feature 实现已找到，作为后续候选。 [OpenMouse 驱动][om-ninj] |
| GravaStar | [网页驱动](https://hub.gravastar1.com/gravastar/device) | W／S。入口过滤含鼠标与键盘，Mercury 型号应按独立协议取证；开源另有 Compx 复用分支。 [网页 JS](https://hub.gravastar1.com/gravastar/assets/index-BWI4_t-Z.js) · [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol/tree/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/gravastar) |

本次 curl 的失败点。Incott 带与不带 `www` 均遭遇 LibreSSL 握手错误，改用 [OpenMouse 实机记录][om-incott-doc]。IROK 的证书不匹配主机名，改用官方主站链接的 `hid.irok.cn`，可以访问。AULA Hub 返回 403，国内 AULA 下载站也发生 TLS 握手错误，采用 [品牌国际下载页](https://www.aulagaming.com/pages/download) 和 [OpenMouse][om-aula-hid]。前行者旧桌面站 TLS 握手失败，现有 E HUB 可访问。DAREU 旧国内 `allinoneweb/index.html` 返回 404，官方国际入口可访问。EWEADN 当前 `/pq.json` 请求返回 E HUB HTML，仍可通过 [S9 独立页面包][js-ewe-s9] 核对电量请求和字段。Zaopin 品牌站证书链未通过本机信任校验，Control HUB 的配置可访问。Waizowl 包中的通用 `data.json` 路径在站点根下返回 404，使用内联过滤与路由。以上是此次访问结果。

## 3. 已取得读包证据的 13 组协议

### 3.1 Compx report 8（含 Control HUB、VGN、Dareu Jm）

| 分支／覆盖 | 路由与只读协议 | 证据和复用要求 |
| --- | --- | --- |
| **Compx-OUT 基线** | `FF02:0002`，Output 与 Input ID `08`，通常 17/17。电量 `08 04 00…49`、在线 `08 03 00…4A`、CID/MID `08 10 00…3D`。全部帧校验低 8 位求和为 `55`。应答电量 `[6]`、充电 `[7]`、大端 mV `[8..9]`，直接用百分比，在线值 `[6]==1`，CID/MID 位于 `[6..7]`。异步 `0A` 与查询应答分开。 | W／M。多来源的充电字段也称 cable/power，组合 `[7]` 的实际含义按固件验证。源码通用 ATK 路由已存在，厂商匹配限于 `373B`，已知路由还需静态目录分类。 [VGN JS][js-vgn] · [Scyrox JS][js-scyrox] · [atk-control][atk-proto] · [HaloBattery][ha-pulsar] |
| **VXE R1／MAD R** | `3554:F58A/F505` 接收器在 R1 Pro 上有电量证据，`F58A` 与 `F58C` 有 R1 Pro Max 无线／有线证据。`373B:1085` 为另一 R1 SE+ 接收器，`3554:F58E/F58F` 为 mouse_tray 报告的接收器／有线。MAD R `373B:104D/103F`，MAD R Major Plus `1040/104C`，MAD R+ 实测 `1040`。 | M（`F58A/F505/1040`），其他见来源。接收器 `1040` 和 `F505` 有多型号共用证据。用通用接收器名直至经过品牌作用域内身份确认。mouse_tray 无明确许可，仅核对事实。 [atk-control 实测][atk-proto] · [HaloBattery][ha-pulsar] · [mouse_tray 型号][mouse-tray] · [ATK HUB JS][js-atk] |
| **VGN F1/F2/Y2/部分 Dragonfly 3** | 官网 `vgn-f1-pm`、`vgn-y2` 为 `FF02:0002`、Report 8；例如 F2 Master `3554:FB56/FB57`，Y2 Ultra `FB3E/FB3D`，Y2 Master `FB52/FB53`，Y2 Pro Max `FB54/FB55`，Dragonfly 3 Master `FB58/FB59` 及 `391D:1A05/1005`。F1 共享接收器 `F503/F505/F50B`，部分有线 `F502/F506/F50A`。 | W。F2 Master+ 有独立实现，确认 `03/04`、校验及 16 字节 WebHID body。其他网页目录行由 JS 报文复核后再配对 Windows caps；以 `391D` 为例，同 VID 另有 `FF01:0010` 与 `FFA0:0002`。 [VGN JS][js-vgn] · [F2 HID][om-vgn-hid] · [F2 编解码][om-vgn] |
| **Scyrox** | `3554` 的接收器 `F5F4/F5F7/F517`，有线 `F5F6/F515`。网页 `BatteryLevel:4`，应答 body `[5..8]` 为电量、充电、mV。V8 `F5F7/F5F6` 有开源实机记录，V8S `F517/F515` 有 OpenMouse 描述，需按该接收器实际分支验收。 | W／M。可复用 Compx-OUT。`F517` 跨品牌同 VID:PID，身份和型号不能由 PID 单独决定。 [配置][cfg-scyrox] · [JS][js-scyrox] · [HaloBattery][ha-pulsar] · [Scyrox 实现][om-scyrox] |
| **Zaopin、ATTACK SHARK、Delux 的 Control HUB** | 三份 `cfg.json` 均列 `3554` 鼠标 PID，接收器分别为 `F524/F50D/F527/F5D5/F517`、`FB43/FB44/F517/F50D/F510`、`FB26/FB23`。全部有线条目见 JSON。Zaopin 和 Attack 的 JS 有 `03/04` 与 report 8 读写；Zaopin 电量应答另有 body `[9]==1` 时取 `[10]` 的分支，即 Win32 `[10]==1` 时取 `[11]`，不能默认统一取 `[6]`。 | W。网页设备过滤是覆盖证据，实机回复分支、型号、MI 与 caps 未核实。Delux 包可看到 `sendReport`，同一状态布局待对照实际读包；暂列模板候选。 [Zaopin 配置][cfg-zaopin] · [Zaopin JS][js-zaopin] · [Attack 配置][cfg-attack] · [Attack JS][js-attack] · [Delux 配置][cfg-delux] · [Delux JS][js-delux] |
| **Lamzu Atlantis 与其他 Compx 品牌** | Atlantis `3554:F50D/F510` 接收器候选，`F50F` 有线有实测，`MI_01 / FF02:0002`。HaloBattery 另记录 Thorn 接收器 `3554` 的报文。相同帧在 Redragon、Hitscan、Pulsar 等已有开源硬件记录，见其 PID 表；不受鼠标机身传感器品牌限制。 | W／M。可共用帧编解码，按各品牌集合和传输归类。 `3554:F50D` 在三个网页目录出现，`3554:F5D5` 在 Zaopin 与 Redragon 资料出现。 [Aurora 型号][cfg-lamzu] · [Atlantis 实现][om-atlantis] · [HaloBattery Lamzu][ha-lamzu] · [HaloBattery Compx][ha-pulsar] |
| **Dareu A950 Feature → Input** | `260D:1074/1084`。用 `FF02:0002` 的 17B Feature、ID `08` 发送相同的 `08 04 00…49`。同物理设备另一集合 `FF01:0000` 的 17B Input 回 `09 04 00 …`，电量 `[6]`、充电 `[7]!=0`，校验同上。状态 `01` 只 ACK。在线单独查询未核实。 | S（HaloBattery 派生自 MousePower）。`FF04` 的 Feature ID `06` 返回固定 100，不能使用。现有 Compx-FEAT 期待 `FF04` 与另一套响应匹配，故只共用请求生成和 checksum。 [MousePower 源文件][mp-dareu] · [HaloBattery][ha-dareu] |
| **Dareu Jm Output → Input** | A950 PRO Mg TM265 接收器 `260D:1114` 实测，TM271F 有线候选 `1117`。`FF02:0002`，收发 ID `08`、17B，旁有 `FF05:0000` 服务集合。电量请求同 `08 04 00…49`，百分比 `[6]`、原始状态 `[7]`，在线命令 `03`。校验低字节同 Compx。 | S。可复用请求。现有解析要求回包头、长度、校验、充电枚举，先核对 Jm 实际应答和状态语义。有线未实测。 [OpenMouse Jm][om-dareu] · [Jm HID][om-dareu-hid] |

**非白名单命令。** Control HUB 的 `05` 进入配对，`07` 写 flash，`09` 清除设置，`0F` 设置当前配置。Zaopin 的表还明确含 `0B/0C` 写 VID/PID 与描述符、`0D` 进入 USB 更新模式。VGN 表也有写 flash、清设置、改描述符。Dareu Jm 有 `07/0F`，其 `260D:1074/1084` 专有配对/重置/DFU 字节未核实。只支持已验证路由的完整 `03/04/10` 帧，尤其 `10` 必须结合协议命名空间。`3554:FA09` 某键盘也用类似集合；它不是鼠标白名单成员。 [Zaopin JS][js-zaopin] · [Scyrox JS][js-scyrox] · [VGN JS][js-vgn] · [Jm 命令表][om-dareu] · [HaloBattery Compx][ha-pulsar]

### 3.2 XVI-NEW 兼容 Feature（WLmouse、Lamzu、MCHOSE A5 初代）

| 品牌／型号 | HID 与帧 | 复用判断与证据 |
| --- | --- | --- |
| **WLmouse Beast X／Max／Mini Pro** | 当前官方目录 `36A7:A880/A881`（Max），`A868/A869`（Mini Pro），`A883/A884`（X），旧 Beast X 接收器另有 `A887`。共用 1K `A882`；HUAN、YING、Sword X 等完整应用态 PID 见 JSON。已读协议取 `FFFF`，常用 usage `0000`，具体 MI 逐机型核实。Feature 0，65B，请求 `00 00 00 02 02 00 83 00…`；应答 `00 A1/A2 00 02 02 00 83 <充电> <%>`，不做电压换算，无应用层校验见诸实现。部分型号另有 Input `03 00 <%> <充电>`。 | W／M。可共用请求，严格处理已确认的 A1 或 A2 头，现有只收 A1。旧版 Beast X 4K 有独立 OpenMouse 分支，别因同 VID 提前合并。独立在线命令未核实。 [WLmouse 模型][cfg-wl] · [网页 JS][js-wl] · [HaloBattery][ha-wl] · [mouse-battery-tray][wltray] · [旧 4K 分支][om-wl4k] |
| **Lamzu Maya X** | 8K `373E:001E`，有线 `001C`，官方另列 1K `001D`。`MI_02 / FFFF:0000`，Feature 0、65B，已见 A1 应答，字段同上。Aurora 模型表同时列 `37B0` 的 Tachi、Maya、Inca、Paro、Thorn V2、Orcus 和 54H20/LM20 分支。 | W／M。A1 路径等同现有 XVI-NEW 读帧。先做 Maya X `001E`，`001C` 单独验收，`37B0` 不能按长相整体启用。HaloBattery 把 Tachi `37B0:000C` 放在待验证列表。 [Aurora 配置][cfg-lamzu] · [JS][js-lamzu] · [HaloBattery][ha-lamzu] · [Tachi 状态][ha-lamzu-sw] |
| **MCHOSE A5 Pro Max 初代** | `2023:F013/F015`（1K/4K）、`F019` 有线。`FFFF`、Feature 0、65B，同 `83` 请求，A1 后 page 与命令相符才读 `[7]/[8]`。1K 有 HaloBattery 实测，第三方 A5 codec 还记录有线已验证，4K 待验。MI 与具体 usage 未核实。 | M（帧来源可交叉），实机证据目前集中在 1K。若描述符与 A1 头完全吻合，可复用现有解析，其余单独验证。 [HaloBattery][ha-mchose] · [社区 A5 Web 驱动](https://github.com/z750sasr/mchose-a5-pro-max-web-driver) · [独立 macOS 实现](https://github.com/Klegus/mchose-macos) · [实机 #149](https://github.com/HeyOkay/HaloBattery/issues/149) · [OpenMouse A5][om-a5] |

**非白名单命令。** 只允许 page `00`、target `02` 的电量查询。WLmouse 独立配对目标为 `01`，其 `8B` 是读取已配对产品，首批无需加入。Lamzu 网页还显示另一条 report-8 传输的 `05` 进入配对。Feature 配对、写配置、恢复和 DFU 的具体字节按代次未全核实，保持拒绝。**BitMouse 的 `83` 是忘记配对**，XVI-OLD 电量用 `8F`，ULX 的 Output `04` / Input `05` 也另有封帧。 [WLmouse HID][om-wl-hid] · [Lamzu JS][js-lamzu] · [atkctl][atkctl-proto] · [现有协议记录](../docs/protocols.md)

### 3.3–3.13 新读路径

| 组 | VID:PID、Win32 HID 与只读请求 | 回包、排除命令和证据 |
| --- | --- | --- |
| **3.3 Rapoo Gen 2／旧 VT9** | Gen 2 VT7 `24AE:1413/4613`、VT9 Pro V2 `14A9` 用 **被动 Input** ID `07`，常见候选 `FF00:0002/000E`，具体 MI、精确集合与 caps 按型号核实。旧 VT9 Pro `24AE:1205/4405` 可先在 `FF0B:0104` 对 `2A` 做 GET Feature，无 Set。开放的另一条旧协议在 `FF00:000E` 发送 32B Output `BA A5 AA 00…`，用 GET_REPORT(Input) 取 ID `BA`；不能替换为普通 `ReadFile`。 | Gen 2 `[7]` 为状态（1 电池、2 充电、0 无效），`[8]` 百分比。旧 Feature `2A` 回包 `[9]` 百分比，充电未知；旧 `BA` 回包在去 ID 后 `[1]` 为电源标记 1/2、`[2]` 为百分比，按含 ID 即 `[2]/[3]`。被动新协议沉默可表示睡眠。旧协议写入 opcode `A5`（连接字段也可同值），恢复/配对/DFU 未核实。S（两个代际各有自己的资料）。 [Rapoo 网页 JS][js-rapoo] · [HaloBattery Gen 2][ha-rapoo] · [OpenMouse 旧 VT9][om-rapoo] |
| **3.4 1D57 电量推送** | `1D57:FA60`（ATTACK SHARK X11/R1/X3 Max、Delux M800 Mini/M600 Pro 共用）、`FA65`（Inphic IN9 Pro）。优先核实 `MI_02 / 000A:0000`，Input ID `03`，**零发送**。Input caps 未核实，来源的 100B 是接收缓冲上限。 | `03 <型号> 40 <状态> <数值>`；X11 型号 `55`、X3 Max `4E`，通常状态 `01` 为百分比，`03` 充电，`02` 在 X3 Max 表示充电，部分其他型号为已充满。R1/X6 的 `10` 型号字段，数值以 10% 为单位，充电未知。Delux M600 Pro 需用其独立型号判据。控制通道另有写 DPI/配置，首版无需打开或枚举命令。M。 [HaloBattery][ha-inphic] · [OpenMouse Attack/Delux][om-attack] · [mouse-battery-tray][wltray] |
| **3.5 Incott Input 09** | `093A:522C` 接收器／`622C` 有线，`FF05` 用于 Feature，电量通过同设备 vendor Input ID `09`；捕获样本为 8B 含 ID，实际 Input caps、usage、MI 仍待核实。**零发送**。 | `[1]<=100` 为直接百分比，`[1]>100` 时减 128 得百分比并标充电，拒绝不在 0–100 的结果。`[2]` 为 DPI/回报率标志。报告随活动推送。`8E/01` 的所谓主动电量已经实测证伪。DPI 配置 `02/03`，更广的写操作在低位 opcode 区间；配对、DFU 未核实。S，含官方配置工具会话与硬件样本。 [实现][om-incott] · [实测][om-incott-doc] |
| **3.6 EWEADN 展力 30** | S9/M156 `089D:062F` 接收器，`088D:062E` 有线候选。`FF01:0010`、Output ID `00`、33B，`00 30 00…00`，尾字节为 `sum([5..31]) & FF`。Input 有效长度按描述符确认。 | Win32 ID 0 槽归一后，`[1]=30 [2]=00`，身份 `[5..8]` 为 ASCII，充电 `[13]`、百分比 `[14]`，链路 `[15]`，`[1]=02` 空响应是睡眠样本。另有异步 `00 D1 01 <%> …` 候选。无需电压换算，响应 checksum、配对与 DFU 字节未核实。JS 的 `09` 可写入或重置按键。W／S，设备信息和 checksum 有当前 S9 页面 JS 与开源硬件样本。 [HaloBattery 源文件][ha-ewe] · [S9 网页 JS][js-ewe-s9] · [现 E HUB JS][js-ewe] |
| **3.7 MCHOSE 反码 Feature** | M7 Ultra、L7 Pro 共享 `5253:1020`，有线分别 `0031/00B0`；A7 V2 Ultra 接收器候选 `3837:100B`。`FF01:0001`（A7 V2 例为 `MI_02`），Feature ID `11` 或 `12`，逻辑帧含 ID 分别 21/65B；Win32 caps 以设备为准。请求 `11 F9 FF…` 或 `12 F9 FF…`（命令 `06`，所有 body 字节 XOR FF），每次取回复前对应一次请求。 | 回包同 ID 与 `[1]=F9`，还原反码后，原帧 `[2..5]` 是 VID／型号，`[10]` 是连接位域、`[11]` 百分比、`[12]` 充电值。零值轮询占位帧和请求回显必须滤掉。无独立帧校验。加密前命令 `57` 为整块写设置，`67` 是配置读取。`03` 是 bond 状态读取，首版只需 `06`，恢复/配对写/DFU 子命令未核实。M（M7/L7），A7 V2 为候选。 [mchose-linux][mchose-proto] · [HaloBattery][ha-mchose] |
| **3.8 MCHOSE V3 4D** | `3837:1014/1016/1018` 共享接收器，鼠标应用态候选 `4026–402A` 与 `4030–4038` 的具体子集见 JSON。A7 V3 Ultra+ 有接收器实测。与 V2 同为 `FF01:0001`，但改用 64B Output/Input ID `4D`。查询 `4D 01 01 00 00 09 00 00 08 00…`，命令 `0900` 小端；checksum 对 `[2]` 到 `[7+数据长度]` XOR，校验字节紧随数据。 | 正常响应 payload 起点 `[8]`，VID、PID、链路 `[18]`、充电 `[19]`、百分比 `[20]`；响应应匹配命令和数据长度。拒绝 `0101/0102/0103/0104/0109` 写配置和 `0105` 恢复；配对/DFU 未核实。S（OpenMouse，从网页提取并有零售机样本）。 [V3 codec][om-mchose-v3] · [V3 HID][om-mchose-v3-hid] |
| **3.9 永佳鑫／YJX G7 与 AJAZZ F0** | G7 `A8A5:2255` 两模式同 ID，`FF01` vendor，65B Output 0，`00 55 30 A5 0B 2E 01 01 01 00…`。AJAZZ NJ07/NJ08 及 MC 系列候选 `A8A4` 有线、`A8A5` 接收器，PID `2157/2158/2167/2168/2177/2178`，`FF01:0010`，64B Output/Input ID `F0`，`F0 30 01 00…`。 | G7 Input payload 以 `AA 30` 开头、payload offset 8/9 为百分比/充电；若 caps 为无编号 Input，则 Win32 `00 AA 30 …` 的 `[9]/[10]`。Input ID/长度与校验待实机确认。NJ07/08 回包 `[1]=30`、`[8]` 电量、`[9]` 充电；拒绝 `09` 按键写、`0F` 配置写。G7 的逻辑 `30` 与 AJAZZ 的 `30` 帧格式不同。VGN 的 `391D` `yongjiaxin` 是另一未核实变体，不能共享查询。G7 为 S，NJ 为 W／S。NJ 网页的链路命令是 `ED`，有请求/通知两种回包路径，首版可仅取电量。 [G7 硬件记录][ha-mchose] · [NJ 网页 JS][js-nj] · [NJ 实测][om-ajazz-nj] · [AJAZZ codec][om-ajazz] · [AJAZZ HID][om-ajazz-hid] · [VGN JS][js-vgn] |
| **3.10 AJAZZ AJ179 V2 MAX** | 接收器 `249A:5C2F`，有线 `248A:5D2E`。`MI_02`，Input/Output 33B 含 ID，Output ID 0，`00 10 00…` 查询设备信息。实际 vendor usage 未确证。 `3151:402D` 是 **AJ179 Pro 与 AJ159 Pro 线索共享**的接收器 ID，路径见 3.13；`3151:5031` 的 Kysona Uranus 待验证。 | 对无编号 Win32 Input，`[1..4]=10 00 01 0B`，`[14]` 电量，`[15]==00` 充电，`[32]=sum([5..31]) & FF`。另有被动 `00 C0 01 <%>` 候选，独立充电未知。旧 AJ159 套 Compx 存在假说，HaloBattery 明确关闭两个候选；避免把 GearHub 与 Compx 叠加到同一端点。配置写、重置、配对、DFU 未核实。S，有实机样本。 [HaloBattery 源码][ha-ajazz] |
| **3.11 RAWM MiracleTek JSON** | `1915:232A/232B/2335/2336` 共享接收器，Blade、ML01、ES21、ER21 系列有线完整表见 JSON。`FF00:0001`，Input/Output ID 0，65B；连接渠道 0 的接收器查询为 `00 C0 8D 01 0D 03 00 00 <8B 秒时间戳 LE> 00…`，有线去掉 `C0` 后补 0。 | Input 数据剥掉 ID 后按长度与通道重组，识别 `FF FF FF FF` 后的 JSON 类型及长度，解 `battery`、`chr`、`wired`，未见以电压换算为前提。查询无 CRC；部分**写**配置有 CRC，其协议 `03 … 15/21` 为参数/休眠写入，恢复/配对/DFU 未核实。W（网页库引用）／S（具体编解码）。不能与共享 VID `1915` 的 Waizowl 并为一路。 [RAWM 官方页](https://www.rawmtech.com/hub.html) · [MiracleTek JS][js-rawm] · [JSON codec][om-rawm] · [HID][om-rawm-hid] |

| **3.12 AULA SC380 Pro** | `258A:0138` 接收器，`FF02:0002` 上 Feature ID `09`，520B。电量 `09 0C 09 01 01 00 01 00 00…`，链路 `09 09 07 00 01 00 01 00 00…`。首部 `[1]` 为 body 中其余字节低 8 位和。 | 匹配命令、参数、长度，`[8]` 是百分比或链路值（1 在线）；充电未知。回复设置块的 checksum 有验证，短电量回复的 checksum 未核实。写 settings 用 `04`，与 Compx 电量 `04` 冲突。MI、有线 PID、配对/DFU 字节未核实。S。 [AULA codec][om-aula] · [HID 读电量][om-aula-hid] |
| **3.13 GearHub V5 接收器状态** | AJAZZ AJ179 Pro `3151:402D` 接收器，`4026` 有线，`FFFF:0002`，Feature 0、65B。按接收器 GET_STATUS `00 F7 00…` 后 GET Feature 0，只读状态命令无 checksum。 | 含 ID `[3]` 电量（实现接受 1–100），`[5]==0` 表示鼠标链路在线，`[1]` 可读。充电未知。有线没有接收器状态命令的证据。控制栈 `F6` 会切换目标，`50` 写键位，应排除。S，OpenMouse 有 Windows 11 硬件回包；同 PID 的 AJ159 Pro 待核实。 [OpenMouse 实测][om-aj179] · [GearHub codec][om-gear] · [GearHub HID][om-gear-hid] |

各组未注明的 MI、独立链路查询、回包校验细节或危险命令均未核实。报告列出的危险字节用于排除和测试白名单，发布路径只接受精确路由内的查询帧。接收器插着、链路在线、睡眠时能读到上次值、充电中是四项不同状态。

## 4. 白牌模板与仍需取证的路径

| 观察 | 证据等级与可采结论 |
| --- | --- |
| **Control HUB / Compx 的覆盖最宽** | W。Scyrox、Zaopin、Attack Shark 新款确有相同 `cfg.json` 结构、命令枚举与 report-8 发送，Delux 用相似目录和平台。VGN 和 ATK HUB 各自有实现，也能复用相同帧语义。**共享 VID:PID 可能是公版接收器，也可能只是网页模板沿用了默认过滤项**。同平台的品牌名必须辅以型号/固件证据，首批保留中性接收器名。 [Scyrox][cfg-scyrox] · [Zaopin][cfg-zaopin] · [Attack][cfg-attack] · [Delux][cfg-delux] · [VGN][js-vgn] |
| **BitMouse 与 ATK HUB** | M。VXE R1 S 有 NK NANO `373B:1216` 使用者报告，尚缺该鼠标对应的电量验证。共享接收器有 `FF05:0001`、64B、Output/Input `08`，查询命令 `07/0F`，含 ID 的请求 `08 7D 72 02 00 01 07 01 00…` / `08 85 72 02 00 01 0F 01 00…`，checksum `[1]=sum([2..63]) & FF`。首个数据字节在带校验回包 `[8]`，省略校验布局 `[7]`。`82` 配对、`83` 忘记配对；`81` 的链路查询未纳入几格现有白名单。保留现有 `1216` 的专用路径状态与复核要求。 [atkctl][atkctl-proto] · [HaloBattery][ha-bit] · [VXE R1 S 记录](https://github.com/HeyOkay/HaloBattery/pull/347) · [几格目录](../src/Jige.Core/Adapters/MouseReceiverCatalog.cs) |
| **达摩鲨、机械师同 Keybord 桥接** | W。两站共享完全相同文件名的框架分块，页面模块都调用 `sendReport(0,…)`，桥接层构造 `FF00:0002` 对象，过滤中含 `19F5` 的 `FF00:0001` 及标准集合。现阶段证据只支持共享软件模板。过滤混有键盘和维护路径，桥接常量 `interface:1` 也只是前端对象字段。鼠标 VID:PID 归属、Windows TLC、收发长度、只读命令和危险字未核实。 [达摩鲨页面 JS][js-darmo] · [机械师页面 JS][js-mach] |
| **Waizowl 至少两条路由** | W。JS 的 `1915` 许可列表有 `3301/3302/072C/0728/0727/07A3/0726/07A2/07A0/07A1/07A5/3303`。例如 `0727` 是 OGM 1K、`0728` 为 OGM 8K，均有 `FF0A:0001` 分支，Falcon R v2 部分路径为 `FFC1:0001`，还有明显的更新传输。包中 `parseStatus` 有 body `[4]` 电量、`[3]` 充电和 `[2]` 链路的分支，也见其他 offset 的解析器。未把这些解析器稳定绑定到具体鼠标的 Input ID、长度或安全 query，故暂不导入 JSON。 [Owlnest JS][js-waiz] |
| **IROK 复用 Compx 的证据** | W。主包的 HE6 Ultra 过滤为 `3554:FB36/FB35` 与 `FF02:0002`，存在 report-8 电量 `04`、在线 `03`、百分比、充电、大端 mV 解析；另有不同封帧的电量分支。还需核实具体 PID 的有线/接收器方向、实际模板路由和 Windows caps。危险字表含 `05/07/09/0D/0F` 配对／写 flash／重置／更新／配置。 [IROK HID JS][js-irok] |
| **VGN 新代路由** | W。`yongjiaxin` 有 `391D:1A01/1A03/1A09/1A14/1A16` 等接收器及配套有线 ID，`FF01:0010`、ID 0。`vgn-ldMs` 用 `FFA0:0002`、ID `A0`；`vgn-king` 接收器 `9981:7002/372E:10AD`，`FF10:0010`、ID `20`。只读帧与危险字待独立复核。JSON 记录有类型和方向的目录项。 [VGN JS][js-vgn] |
| **Attack Shark 另有 GearHub 或 XVI 样式** | S。OpenMouse 把 `25A7` 归 GearHub（ID 0）、把部分 `373E` R 系列归 `FFFF` Feature。`373E` 与 Lamzu 复用 VID，硬件产品表和读包不足以安全新增跨品牌路径。 [OpenMouse 源文件][om-attack] · [官方 XValley JS](https://www.xvalleyinno.top/AttackShark/static/js/index-1e70e4b0.js) |
| **Ninjutso、GravaStar** | S。NinjaForce Sora 的 OpenMouse 代码区分 5/6/3 报告，当前 Feature 路径有充电命令 `11` 和电量 `12`（十六进制）。旧代和 V3 应分开，不把有线、无线 ID 和通用读法直接抄入生产目录。GravaStar 有多个 VID 与键鼠类型，本轮未穷尽其接收器与电量证据。 [Ninjutso codec][om-ninj-codec] · [Ninjutso HID][om-ninj] · [GravaStar 网页 JS](https://hub.gravastar1.com/gravastar/assets/index-BWI4_t-Z.js) |
| **Nordic、PixArt、Beken、Holtek、Telink 等芯片名** | W／I。模板配置里能看到具体型号使用的芯片标记，Zaopin 目录甚至为鼠标与不同接收器分别列出 Nordic 与其他芯片。芯片供应商与 USB 协议不一一对应。已知 VGN 和 Lamzu 都存在同品牌异协议，且同一应用可配置多个栈。Beken/Holtek/Telink 的具体只读鼠标协议族未核实。 [Zaopin 配置][cfg-zaopin] · [VGN JS][js-vgn] · [Aurora 配置][cfg-lamzu] |

## 5. 分批实施优先级

覆盖量采用可确认的接收器数量、跨品牌共用范围和公开实机反馈作代理。没有逐品牌装机量。优先级把已有路由的改动成本与误发风险同时计入，不把网页目录行直接视为覆盖用户。

| 批次 | 覆盖 × 证据 × 成本 | 文件与验收重点 | 预计风险 |
| --- | --- | --- | --- |
| **1A，已有 Compx-OUT** | **高 × 强 × 低**。先做 VXE R1 `3554:F58A/F505`、MAD 8K `373B:1040`、R1 SE+ `373B:1085`、Scyrox V8 `3554:F5F7`。加 VGN F2 `FB56` 和有独立依据的 F1 接收器时，逐设备核对回复头、校验、状态。按批次接收器先行。 | 改 [MouseReceiverCatalog.cs](../src/Jige.Core/Adapters/MouseReceiverCatalog.cs)、[DeviceCatalog.cs](../src/Jige.Core/DeviceCatalog.cs) 及设备/协议文档。`Detect` 当前通过 `IsVendor` 预筛，可把精确目录成员纳入并继续让 `GenericRoute` 专属于已有路由。 [MouseReceiverAdapter.cs](../src/Jige.Core/Adapters/MouseReceiverAdapter.cs)。复用 17B 请求和解析。用 [MouseAdapterTests.cs](../tests/Jige.Core.Tests/MouseAdapterTests.cs) 覆盖预期开句柄和应发送帧，以及同 VID 的不匹配集合零发送。 | **低到中**。共享 PID 要用中性名称。现有 CID/MID 只内置 ATK 名称表，不能默认给 VXE/VGN/Scyrox 命名或把新设备标为 ATK。首批可将身份功能保持关闭，链路 `03` 按接收器实证启用。 |
| **1B，已有 XVI-NEW 兼容 A1** | **中高 × 强 × 低**。先对 Lamzu Maya X `373E:001E` 与 WLmouse Beast X Max `36A7:A880` 取 Windows 描述符及实际回包。MCHOSE A5 Pro Max `2023:F013` 路由确认后可同批。 | 同上，按每个型号提供 MI／TLC／Feature 65B 证据。只收现有 A1 格式可复用全部查询和解析，有 A2 证据时再单独增加解析变体。WLmouse 的被动上报另外排期。 | **中**。型号目录有旧版、共用接收器及与维护同 PID 的设备，禁止按 VID 泛化。无有效 Feature 回包时保留上次有效值。 |
| **2A，强证据新协议** | **中高 × 强 × 中**。先 MCHOSE 反码 Feature（L7 Pro/M7 Ultra）及 Rapoo Gen 2。并行取证 ATTACK SHARK/Delux/Inphic 的 `1D57`、Incott 被动路径。 | 各新增小型适配器和协议解码；在 [AdapterRegistry.cs](../src/Jige.Core/Adapters/AdapterRegistry.cs) 注册，在 `DeviceCatalog.cs` 加只读能力与证据，更新协议资料，补对应的模拟帧与 Windows 测试。被动路径复用读报告的传输能力，验证在现有轮询预算内怎样保存最后一帧。 | **中**。旧帧、回显、共享有线/无线实例、静置时没有主动包。只靠沉默不显示 0% 或强判离线。厂商驱动占用要退避。 |
| **2B，强路由证据但成本稍高** | **中 × 中高 × 中**。EWEADN S9、Dareu A950 分集合、MCHOSE V3、AJAZZ GearHub 先从有样本的接收器开始。 | EWEADN/MCHOSE V3/GearHub 新独立查询及解析，注册表和设备目录同步。Dareu 复用 Compx checksum 和请求，新增独立响应形态及同物理设备双集合匹配，必要时改 [WindowsReceiverTransport.cs](../src/Jige.Core/Windows/WindowsReceiverTransport.cs) 或其抽象。 | **中到高**。跨集合误配、相同 VID:PID 的其他设备、0 ID 槽与读回长度差异。确认出厂设置、配对及维护 opcode 在全部正常入口都无法发送。 |
| **3，扩大覆盖和取证** | **潜在高 × 待核实 × 中高**。Zaopin／Attack Shark／Delux Control HUB 其余 PID、VGN 新代、AULA、AJAZZ 其他分支、Waizowl、达摩鲨、机械师等。 | 先补诊断记录和协议资料。对候选收集父节点、VID:PID、MI、TLC、报告 ID／长度和一次厂商电量刷新回包。达标后才新增目录项或适配器。 | **高**。跨品牌通用 VID、相似 report ID、模糊过滤与维护接口。尤其 `3554` 下至少存在键盘和多个鼠标变体。 |

实现时按证据建立 **VID:PID → 协议与连接类别 → 唯一 HID 集合 → 完整只读帧** 的链条。适配器在发现和开句柄前排除歧义与禁用路径，[SafetyGate.cs](../src/Jige.Core/Adapters/SafetyGate.cs) 保存失败路径的拦截记录。发包处继续使用协议帧白名单。一个已知接收器只选一条可信路径，不应因超时轮试其他家族。复用集中在编解码、受控轮询和状态语义；设备目录只保存已证实的路由、共享身份和禁用例外。Win11 实机验收覆盖接收器插拔、睡眠唤醒、充放电、同时连接有线和接收器、第三方工具占用及并联接收器。不读取 HID 鼠标或键盘标准输入集合。 [MouseReceiverAdapter.cs](../src/Jige.Core/Adapters/MouseReceiverAdapter.cs) · [SafetyGate.cs](../src/Jige.Core/Adapters/SafetyGate.cs) · [既有协议安全说明](../docs/protocols.md)

## 6. 许可与可追溯性

| 参考实现 | 许可及取证边界 |
| --- | --- |
| HaloBattery | MIT，快照 `73e229c`。Provider 标有上游归属时保留原始来源；不能把同源移植当作两份独立实机记录。 [LICENSE][lic-halo] |
| OpenMouse mouse-protocol | AGPL-3.0，快照 `93c4cc6`。引用协议事实和硬件记录，MIT 实现独立编写，避免复制源码、生成的设备库、配置表或测试样本。 [LICENSE][lic-om] |
| atk-control / atkctl | 均 MIT，快照 `4822253` / `1755d85`。 [atk-control LICENSE][lic-atk] · [atkctl LICENSE][lic-atkctl] |
| mouse_tray（Fan4Metal/ATK_tray） | 快照 `7a5d9e3` 未见明确许可文件，仅引用事实，代码不移植。 [仓库快照](https://github.com/Fan4Metal/ATK_tray/tree/7a5d9e3b1aefeae60945140419cbbf9df9f83c30) |
| MousePower / mchose-linux / mouse-battery-tray | 均 MIT，快照 `81ad75a` / `75204cc` / `c1c6059`。 [MousePower LICENSE][lic-mp] · [mchose-linux LICENSE][lic-mchose] · [mouse-battery-tray LICENSE][lic-wltray] |
| A5 两个社区实现及其他上游 | mchose-macos 为 MIT；独立 A5 网页项目的 GitHub license 接口返回 404，许可证未核实。 [mchose-macos LICENSE](https://github.com/Klegus/mchose-macos/blob/main/LICENSE) · [HaloBattery A5 说明](https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/docs/protocols.md#mchose-a5-pro-max) |
| 厂商网页 | 按公开资料记录协议、ID 和字段。第三方 JS、图片、固件、配置资产留在临时抓取目录，不随项目分发。 |

本次交付为静态调研和型号索引。结论标注的实机证据来自上游，不代表本轮已连接这些外设。未执行任何查询、设置写入、配对、DFU 或 Windows 程序测试。

[atk-proto]: https://github.com/NasirA1991/atk-control/blob/4822253b0b02036f727c130bddf4d6db156e880b/docs/PROTOCOL.md
[atkctl-proto]: https://github.com/cobanov/atkctl/blob/1755d85106630cacc2f6c77a843b5819c4e07c37/docs/PROTOCOL.md
[cfg-attack]: https://controlhub.top/AttackShark/cfg.json
[cfg-delux]: https://www.controlhub.top/delux/cfg.json
[cfg-lamzu]: https://www.lamzu.net/Config/env-models.json
[cfg-scyrox]: https://www.scyrox.net/cfg.json
[cfg-wl]: https://gm.wlmouse.gg/Config/env-models.json
[cfg-zaopin]: https://controlhub.top/zaopin/cfg.json
[ha-ajazz]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/ajazz.py
[ha-aula]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/aula.py
[ha-bit]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/atk_bitmouse.py
[ha-dareu]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/dareu.py
[ha-ewe]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/eweadn.py
[ha-inphic]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/inphic.py
[ha-lamzu]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/lamzu.py
[ha-lamzu-sw]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/lamzu_sw.py
[ha-mchose]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/mchose.py
[ha-pulsar]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/pulsar.py
[ha-rapoo]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/rapoo.py
[ha-wl]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/providers/wlmouse.py
[js-atk]: https://bpcdn.atkgear.com/hub-v3/production/3.2.33/static/index-B7o6gL18.js
[js-attack]: https://controlhub.top/AttackShark/assets/index-D8Ia9d0l.js
[js-darmo]: https://darmoshark.keybord.net.cn/_next/static/chunks/app/page-6fbb50d011542ec3.js
[js-delux]: https://www.controlhub.top/delux/js/app.695ab334.js
[js-ewe]: https://hub.eweadn.cn/assets/index-DYNLTg1s.js
[js-ewe-s9]: https://hub.eweadn.cn/pq/web/_next/static/chunks/app/page-1ea75b68e269350f.js
[js-lamzu]: https://www.lamzu.net/static/js/index-DG9BUtoG.js
[js-mach]: https://www.machenike.com/webdriver/_next/static/chunks/app/page-9dc461ffc7d330ef.js
[js-rapoo]: https://hub1.rapoo.cn/assets/index-DyyXqaOq.js
[js-rawm]: https://hub.miracletek.net/hub/js/library.min.js?v=202606012357
[js-scyrox]: https://www.scyrox.net/assets/index-DUfu8aky.js
[js-vgn]: https://drive.vgnlab.com.cn/assets/index-Cw5Cr2W_.js
[js-waiz]: https://owlnest.waizowl.com/assets/js/index-BRQYaWvg.js
[js-wl]: https://gm.wlmouse.gg/static/js/index-DRmc0iTz.js
[js-zaopin]: https://controlhub.top/zaopin/assets/index-D3s0OBtp.js
[lic-atk]: https://github.com/NasirA1991/atk-control/blob/4822253b0b02036f727c130bddf4d6db156e880b/LICENSE
[lic-atkctl]: https://github.com/cobanov/atkctl/blob/1755d85106630cacc2f6c77a843b5819c4e07c37/LICENSE
[lic-halo]: https://github.com/HeyOkay/HaloBattery/blob/73e229cbee2e97fad659fcd96c78afdfa78488fb/LICENSE
[lic-mchose]: https://github.com/alexfrih/mchose-linux/blob/75204ccc3bd83883330176d1eefda211dd449576/LICENSE
[lic-mp]: https://github.com/vamyane/MousePower/blob/81ad75a84112eac9c33058e7f6de2c7bcfd7919e/LICENSE
[lic-om]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/LICENSE
[lic-wltray]: https://github.com/incconutwo/mouse-battery-tray/blob/c1c6059472dab366c700a7f7516e0dbffde68fd7/LICENSE
[mchose-proto]: https://github.com/alexfrih/mchose-linux/blob/75204ccc3bd83883330176d1eefda211dd449576/PROTOCOL.md
[mouse-tray]: https://github.com/Fan4Metal/ATK_tray/blob/7a5d9e3b1aefeae60945140419cbbf9df9f83c30/models.py
[mp-dareu]: https://github.com/vamyane/MousePower/blob/81ad75a84112eac9c33058e7f6de2c7bcfd7919e/src/mousepower/core/providers/dareu_compx.py
[om-aj179]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/docs/ajazz-aj179-pro.md
[om-ajazz]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/ajazz/index.ts
[om-ajazz-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/ajazz/hid.ts
[om-ajazz-nj]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/docs/ajazz-nj07.md
[om-atlantis]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/lamzu/atlantis.ts
[om-attack]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/attackshark/hid.ts
[om-aula]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/aula/index.ts
[om-aula-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/aula/hid.ts
[om-dareu]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/dareu/index.ts
[om-dareu-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/dareu/hid.ts
[om-gear]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/gearhub/index.ts
[om-gear-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/gearhub/hid.ts
[om-incott]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/incott/index.ts
[om-incott-doc]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/docs/incott-testing.md
[om-mchose-v3]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/mchose/v3.ts
[om-mchose-v3-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/mchose/v3-hid.ts
[om-ninj]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/ninjutso/hid.ts
[om-ninj-codec]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/ninjutso/index.ts
[om-rapoo]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/rapoo/index.ts
[om-rawm]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/rawm/index.ts
[om-rawm-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/rawm/hid.ts
[om-scyrox]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/scyrox/hid.ts
[om-vgn]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/vgn/index.ts
[om-vgn-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/vgn/hid.ts
[om-wl-hid]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/drivers/wlmouse/hid.ts
[om-wl4k]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/wlmouse/beast-x-4k.ts
[wltray]: https://github.com/incconutwo/mouse-battery-tray/blob/c1c6059472dab366c700a7f7516e0dbffde68fd7/devices.py
[js-irok-home]: https://www.irok.cn/index.1795140666211.js
[js-irok]: https://hid.irok.cn/assets/index-CRqxRK3F.js
[js-nj]: https://nacodex.yjx2012.com/_next/static/chunks/515-103c69dad660d3a3.js
[om-a5]: https://github.com/OpenMouse-Project/mouse-protocol/blob/93c4cc601d448ea481eb59d7b785eec75ae16ec2/src/mchose/a5-gen1.ts

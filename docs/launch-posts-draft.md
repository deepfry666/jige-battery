# 几格（Jige Battery）v0.2.27 多平台社区推广帖文案包（草稿）

> **使用说明与安全提醒**
> - 本文档为**待人工审阅的发布草稿**，仅保存在当前分支，未向任何外部平台发布。
> - 本轮推广核心目标：为 `v0.2.27` 招募第一批真实 Windows 用户，收集**实机确认反馈**与**脱敏 `diagnostics.txt` 诊断样本**，驱动 `v0.2.28` 将「仅按资料」设备升级为「已实机验证」并补充新外设适配。
> - 文中所有型号、架构、体积、协议、验证等级与链接均核对自 `README.md`、`docs/devices.md`、`CHANGELOG.md`、`PRIVACY.md`、`CONTRIBUTING.md` 与 `.github/ISSUE_TEMPLATE/device-report.yml`，**禁止在二次改写时夸大实机验证范围或虚构性能数据**。

---

## 核心事实速查（二次修改请以此为准）

| 项目 | 准确信息 |
| --- | --- |
| 当前版本 | `v0.2.27`（下一阶段目标：`v0.2.28`） |
| 官网与在线演示 | `https://jige.imfourj.cn` |
| 下载与安装指南 | `https://jige.imfourj.cn/download.html` |
| 国内镜像直链 | `https://download.imfourj.cn/jige-battery/releases/v0.2.27/Jige-Setup-0.2.27-x64.exe` |
| SHA-256 校验文件 | `https://download.imfourj.cn/jige-battery/releases/v0.2.27/SHA256SUMS.txt` |
| GitHub 仓库 | `https://github.com/deepfry666/jige-battery` |
| GitHub Release | `https://github.com/deepfry666/jige-battery/releases/latest` |
| 设备支持清单 | `https://github.com/deepfry666/jige-battery/blob/main/docs/devices.md` |
| 提交设备报告模板 | `https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml` |
| 反馈与贡献指南 | `https://github.com/deepfry666/jige-battery/blob/main/CONTRIBUTING.md`（英文：`#english`） |
| 隐私与脱敏说明 | `https://github.com/deepfry666/jige-battery/blob/main/PRIVACY.md` |
| 技术栈与体积 | C# / .NET 10 NativeAOT 单进程，安装包约 ~5 MB，免安装运行库，免管理员权限 |
| 系统支持 | Windows 10（1809+）/ Windows 11 x64 |
| 开源协议 | MIT（公开协议事实净室独立实现，不复制 GPL/AGPL 代码） |
| 已实机验证（4 台） | Sony INZONE H9（一代）、游狼 G-Wolves HTX Ultra 8K、Razer BlackShark V3 Pro、ASUS ROG 无线键盘（`0B05:1934`）；另罗技 MX Keys S / MX Anywhere 3S 日常在用 |
| 重要诚实披露项 | ① 安装包暂无代码签名，可能触发浏览器提醒和 Windows SmartScreen；② 除上述 4 台实机验证与 3 台同家族推定外，其余品牌型号均为按公开协议资料实现，待实机验证；③ 当前主面板/设置页/安装器界面为简体中文（任务栏直读条本身为图标 + 百分比数字） |

---

## 一、V2EX（节点：分享创造）

### 备选标题（三选一）

1. **「几格」：不开雷蛇/罗技等厂商驱动，在 Windows 任务栏直接看无线鼠键耳机电量（.NET 10 NativeAOT / ~5 MB / MIT 开源）**
2. **写了个约 5 MB 的 Windows 小工具「几格」，关掉厂商臃肿驱动也能在任务栏看多品牌无线外设电量**
3. **开源了一个纯只读的 Windows 任务栏无线外设电量监控（.NET 10 NativeAOT，征集各品牌接收器实机反馈）**

### 正文（可直接复制）

桌面上的无线外设一多，看电量就成了一件很割裂的事。

我自己平时鼠标、键盘、耳机混着不同品牌用：DPI、回报率和侧键调好之后，厂商驱动（Synapse、G HUB、INZONE Hub 之类）其实完全没必要常驻后台；可一旦关掉驱动，就不知道还剩多少电，经常打着游戏或者开着会鼠标突然没电。装两三套厂商驱动只为看个百分比，又觉得太重。

于是用业余时间写了 **几格（Jige Battery）**，刚发到 `v0.2.27`，MIT 开源，拿出来分享一下，也顺便向 V 友征集一批真实设备的诊断样本，用来推进下一个版本的实机验证和适配。

- 官网 & 在线演示：https://jige.imfourj.cn
- GitHub 仓库：https://github.com/deepfry666/jige-battery
- 国内镜像下载（免代理）：https://jige.imfourj.cn/download.html
- 完整设备清单与验证状态：https://github.com/deepfry666/jige-battery/blob/main/docs/devices.md

（这里插图：`docs/images/win11-panel.png` —— 任务栏直读条与展开主面板）

#### 技术实现与几个刻意守住的边界

1. **C# / .NET 10 NativeAOT 单进程，约 5 MB**
   - 不需要装 .NET 运行库，不需要管理员权限，默认装在 `%LOCALAPPDATA%\Programs\Jige`。
   - 主面板与悬停卡片用 Win32 + CsWin32 + Direct2D 自绘；任务栏直读条在 `0.2.19` 专门去掉了常驻 D3D/D2D 硬件资源，改用轻量 GDI 分层置顶窗口 + `GC.ConserveMemory`，降低常驻内存。
   - Windows 10 和 11 默认都支持通知区左侧的「任务栏直读条」（通过 UIA 几何避让应用按钮与 Win10 资讯和兴趣，空间不足或任务栏不在底部时自动回退托盘水位剪影图标），内置 5 种样式和 9 套配色，自适应深浅色与半透明任务栏。

2. **严格只读，绝不猜测命令**
   - 直接通过 HID / USB 串口与 2.4 GHz 接收器通信（蓝牙外设走 Windows 本机设备属性，罗技蓝牙走 HID++ over BLE 并自动去重）。
   - 所有发往接收器的查询帧都在白名单内，**只查电量、充电和链路状态**，绝不碰 DPI、回报率、RGB、宏、配对或固件；查询结束就释放句柄，检测到官方软件（如 INZONE Hub）占用接收器时主动退让。
   - 对已知需要切远程/软件模式才能读电量的型号（比如 Razer PA / Airoha RACE 耳机），或者存在协议分支冲突的接收器，统一登记在 `DeviceCatalog.DisabledPaths`，**打开设备句柄之前直接跳过，不发任何字节**。
   - 所有 C# 适配器均参考公开协议事实净室独立编写，不复制 GPL/AGPL 代码，并在 `docs/protocols.md` 和 `docs/credits.md` 标明了出处。

3. **游戏防打扰 & 零遥测**
   - 独占全屏游戏自动隐藏直读条、暂停弹窗、不抢焦点，轮询自动降频到 5 分钟（退出全屏带 400 ms 稳定滞回防闪现）；休眠鼠标唤醒只调 `GetLastInputInfo` 读最后输入时间戳，不装键鼠钩子。
   - 无账号、无埋点统计。联网仅用于检查正式版更新（默认每天一次，国内镜像优先并自动回退 GitHub，校验大小和 SHA-256，点了才升级，设置里可完全关闭）。

#### 目前支持哪些设备（如实说验证状态）

仓库里把设备分成「已实机验证 / 同家族推定 / 仅按资料」三级，不把没测过的设备标成已验证：

- **已实机验证（4 台）**：Sony INZONE H9 一代、游狼 G-Wolves HTX Ultra 8K、Razer BlackShark V3 Pro、ASUS ROG 无线键盘（`0B05:1934`）；另外罗技 MX Keys S / MX Anywhere 3S 日常在用。
- **同家族推定**：G-Wolves Fenrir Pro 8K / HTM Plus / HSK Pro 2.0 8K。
- **按公开协议资料已启用（急需实机验证）**：
  - **大厂**：雷蛇（72 个鼠标 PID + 15 个键盘 PID + BlackShark V2 Pro 2020 / V2 HyperSpeed / V3 耳机）、罗技（Bolt / Unifying / Nano / LIGHTSPEED、有线与蓝牙 HID++ 及 G533/G535/G733/G PRO X/G PRO X 2/G522/G325/ASTRO A50 Gen 5 等耳机）、ASUS ROG / TUF（Falchion / Azoth / Scope II 96 键盘、33 个鼠标 PID、OMNI 接收器、Strix Go 2.4）、赛睿 SteelSeries（Arctis Nova / Arctis / GameBuds 耳机、Aerox / Rival 鼠标）、HyperX（Cloud II / Flight / Alpha / III / III S Wireless）、海盗船 Corsair（Void / HS80 / Virtuoso / Dark Core / M75 / Sabre v2 Pro / K65 PLUS）。
  - **轻量化与国产无线外设**：Finalmouse ULX、游狼 21 款旧协议型号、ATK（F1 Ultimate / ZERO / A9 Plus NK 星闪 / A9 Pro Max 2.0 NK 等，支持 `373B` 通用适配）、VXE（R1 / MAD）、VGN（蜻蜓 F1 / F2 Master / Y2 / Dragonfly 3 Master）、Scyrox（V8 及 `0.2.27` 新增的 `395A` 新代 V6S / V8S / V12 系列）、Lamzu Maya X、WLmouse Beast X、Pulsar X2、Hitscan Hyperlight、Endgame Gear XM2we、迈从 MCHOSE（A5 Pro Max / M7 Ultra / L7 Pro / A7 V2 Ultra / V3 / K87）、雷柏（VT7 / VT9 Pro / VT9 Pro V2）、达尔优 A950、黑爵 AJ179、前行者、狼蛛 F87 Pro V2C、ATTACK SHARK、Delux、英菲克、Incott 8K、Keychron、Audeze Maxwell 等，以及 Windows 能读电量的各类蓝牙设备。

#### 两个提前说明的不足

1. **暂无代码签名**：个人项目还没买代码签名证书，下载或首次安装时浏览器 / Windows SmartScreen 可能会拦一下，点「更多信息 → 仍要运行」即可。安装包附了 `SHA256SUMS.txt`，也可以直接看 GitHub Actions 构建或本地 `dotnet` 自己编。
2. **手头硬件有限，急缺实机样本**：很多型号是我按开源资料和协议文档写完、通过离线单元测试的，但在 Windows 实机上可能遇到固件版本差异或接口选错。

#### 欢迎帮我测一下 / 扔一份脱敏诊断报告

如果你的外设正好在清单里（不管**读准了**还是**没认出来 / 读数不对**），都非常欢迎给我一条反馈：

1. 先在官方驱动里看一眼当前电量，然后**彻底退出官方驱动**（含右下角托盘进程）；
2. 动一下鼠标 / 打开耳机确保处于唤醒状态；
3. 在几格右键点「诊断信息…」（或在管理设备页点「导出诊断信息」），它会在本地生成 `diagnostics.txt` 并先用记事本打开给你检查——**序列号、设备实例 ID、用户名、计算机名、COM 号和本地路径都已自动脱敏**，几格不会自动上传任何东西；
4. 检查没问题后，点面板底部的「**复制报告并去反馈**」，直接贴到这个模板里就行：https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml

哪怕只是回帖报一声「XX 型号在 Win11 上读数准/不准」，或者对交互、架构有什么建议和吐槽，我都会逐条看，感谢大家！

### V2EX 社区防踩雷要点
- **节点**：必选「分享创造（`/go/create`）」，不要发到「程序员」或「酷工作」。
- **配图**：V2EX 不支持直接上传图片，提前将 `docs/images/win11-panel.png`（及可选的 `win11-taskbar.png`）传至稳定图床（如 GitHub Raw 或国内图床），正文直接贴图片 URL 即可自动渲染。
- **语气与互动**：不要用营销感叹号或求 Star 话术；V 友非常看重代码签名/杀软误报、内存占用细节、是否开源可自编译，首帖主动把未签名和验证状态讲透反而最容易拿好感。

---

## 二、小众软件 / 少数派

### 备选标题（三选一）

1. **关掉雷蛇、罗技的臃肿驱动，把无线鼠标和耳机的电量直接放到 Windows 任务栏：开源小工具「几格」**
2. **桌面上好几个牌子的无线外设？用这个 5 MB 开源小工具，在任务栏一眼看全剩余电量**
3. **不想再为看电量常驻厂商全家桶？试试轻量开源的 Windows 任务栏外设电量条「几格」**

### 正文（场景化体验稿，可直接复制）

如果你也在用无线鼠标、无线键盘或 2.4 GHz 无线耳机，大概率遇到过这种纠结：

刚买回来时，装上官方驱动调好 DPI、回报率和快捷键，之后就恨不得把驱动彻底关掉——毕竟像雷蛇 Synapse、罗技 G HUB、索尼 INZONE Hub 这些配套软件，常驻后台既占内存，偶尔还会在开机或打游戏时弹个更新通知。可真把驱动关了，问题就来了：**根本不知道设备还剩多少电**。打团打到一半鼠标突然断电，或者桌面混用着罗技键盘、国产 8K 鼠标和索尼耳机，为了看电量得同时开两三个客户端，实在太折腾。

最近我在 Windows 上整理并开源了一款专门解决这个痛点的小工具——**几格（Jige Battery）**（取自「还剩几格电」）。它的定位很简单：**让你彻底关掉厂商驱动，也能在任务栏一眼看清无线外设的电量。**

（这里插图：`docs/images/win11-panel.png` —— 任务栏直读条与展开面板）

#### 它怎么做到不开驱动也能看电量？

几格直接通过 2.4 GHz USB 接收器和 Windows 蓝牙接口，向设备发送**白名单内的只读查询命令**，只问「还剩多少电、有没有在充电、在不在线」，拿到数字就释放设备。

它有几条很克制的底线：
- **纯只读，不动设置**：绝不改你的 DPI、回报率、RGB 灯光、宏或固件，也不会破坏原本的配对状态；偶尔你打开厂商驱动改设置时，几格也会主动退让。
- **极轻、免依赖、零遥测**：用 C# .NET 10 NativeAOT 原生编译，安装包只有约 **5 MB**，单个进程，不用装任何运行库，也不用管理员权限；没有账号系统，不上传设备信息（联网只用来检查正式版更新，还可以在设置里关掉）。
- **完全免费、MIT 开源**：代码全公开，协议细节和隐私说明都写在仓库里。

#### 日常使用最舒服的几个细节

**1. 直接融进任务栏，不用每次点开托盘找**

在 Windows 10 和 Windows 11 上，几格默认直接在通知区左侧显示一排**任务栏直读条**，图标和百分比数字和系统时钟一样大，扫一眼就能知道鼠标、耳机、键盘各自还剩多少电。

- **颜色一目了然**：低于提醒阈值变红，略高于阈值变黄，插上线充电时变绿并带闪电图标。
- **5 种样式 + 9 套配色**：内置自动融入、极简无框、磨砂胶囊、分块徽章、底栏格条，会自动感知你的深浅色主题、彩色任务栏和半透明背景。
- **也支持经典托盘图标**：如果你任务栏偏窄，或者更喜欢传统风格，可以在「设置 → 显示位置」切换成设备剪影图标——图标本身就是水位电量计，电量越多填得越高（空间不足时也会自动临时回退，不会和系统图标打架）。

（这里插图：并排或依次放 `docs/images/win11-taskbar.png` 与 `docs/images/win10-tray.png`）

**2. 真正做到「打游戏不打扰」**

很多小工具平时挺好，一进游戏右下角弹个窗就把焦点抢走了。几格对游戏场景做了专门处理：
- 检测到独占全屏游戏时，**自动隐藏任务栏直读条、不弹窗、不抢焦点**，后台轮询自动放慢到 5 分钟一次；切回桌面后立刻恢复显示，并把错过的低电量提醒补给你。
- 如果你习惯开无边框窗口玩游戏，刚进游戏时有设备电量偏低，它还支持在右下角弹一条 **4 秒自动消失、可点穿、不抢焦点** 的小横幅，提醒你这局打完记得插线（独占全屏不会弹，也可在设置里关闭）。

**3. 够用但不花哨的电量管理**

- 点一下任务栏直读条展开主面板，电量最低的设备自动排在最上面，断开的设备收进底部的「离线设备」。
- 支持给每台设备自定义中文别名、单独设低电量提醒阈值（5%～30%，比如键盘设 5%、鼠标设 15%）、决定哪些设备显示在任务栏上。
- 如果想大概心里有数，还可以在设置里手动开启「预计可用时间」（默认关闭）：它只靠本机已有的掉电和分段充电记录来估算「还能用多久 / 够用约几天 / 约多久充满」，不额外向设备发查询，也不制造虚假精确感。

#### 支持哪些品牌？

目前通过 2.4 GHz 接收器和 Windows 蓝牙，已经覆盖了市面上大部分常见无线外设品牌：
- **国际主流**：雷蛇 Razer（72 个鼠标 PID + 15 个键盘 PID + BlackShark 耳机）、罗技 Logitech（Bolt / Unifying / LIGHTSPEED / 蓝牙及多款 G 系列耳机）、华硕 ROG / TUF（Falchion / Azoth / Harpe Ace / Gladius III / OMNI 接收器等）、赛睿 SteelSeries、HyperX 极度未知、海盗船 Corsair、Sony INZONE H9 一代、Finalmouse ULX、Audeze Maxwell、Keychron。
- **国产与轻量化外设**：游狼 G-Wolves、ATK、VXE、VGN、Scyrox（最新 `v0.2.27` 刚加上新代 V6S / V8S / V12 系列）、Lamzu、WLmouse、Pulsar、Hitscan、迈从 MCHOSE、雷柏 Rapoo、达尔优、黑爵、前行者、狼蛛、ATTACK SHARK、Delux、英菲克、Incott 等。
- **蓝牙外设**：只要 Windows 系统本身能读到电量的蓝牙鼠键耳机都能直接显示。

> **说句实在话**：因为手头不可能把几十个牌子的外设都买齐，目前除了 **Sony INZONE H9 一代、游狼 HTX Ultra 8K、雷蛇 BlackShark V3 Pro、华硕 ROG 无线键盘**（以及日常在用的罗技 MX Keys S / MX Anywhere 3S）经过了实机验证之外，其余大部分型号都是根据开源社区和公开协议资料适配的，在软件里也会诚实标注验证状态。

#### 三步上手

1. **下载安装**：前往官网下载页 [https://jige.imfourj.cn/download.html](https://jige.imfourj.cn/download.html)（提供免代理的国内镜像直链，也可去 [GitHub Release](https://github.com/deepfry666/jige-battery/releases/latest) 下载 `v0.2.27`）。
2. **通过系统提示**：由于开源个人项目暂无代码签名证书，下载或首次运行时浏览器 / Windows SmartScreen 可能会提示「已保护你的电脑」，点击「更多信息 → 仍要运行」即可（下载页附了详细的图文步骤和 SHA-256 校验方法）。
3. **关闭官方驱动体验**：彻底退出原来的厂商驱动（别忘了右下角托盘里的后台进程），动一下无线鼠标或打开耳机，几格就会自动把电量显示在任务栏上。

#### 你的设备没认出来，或者读数有偏差？

这正是发这篇分享最想请大家帮忙的地方！如果你试完发现某款鼠标/键盘/耳机没显示，或者电量百分比和官方驱动对不上，只需要 30 秒就能帮它加上适配：

1. 先记下官方驱动里显示的电量，再彻底关掉官方驱动；
2. 唤醒设备后，在几格右键点「**诊断信息…**」（或在管理设备页点「导出诊断信息」）；
3. 几格会在本机生成 `diagnostics.txt` 并先用记事本打开给你过目（**序列号、电脑名、用户名、路径都已自动去掉**，不会自动上传）；
4. 确认无误后，点面板底部的「**复制报告并去反馈**」，直接粘贴提交 GitHub Issue 即可（[设备报告模板](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml)）。

- 官网与交互演示：https://jige.imfourj.cn
- GitHub 开源地址：https://github.com/deepfry666/jige-battery
- 完整支持清单：https://github.com/deepfry666/jige-battery/blob/main/docs/devices.md

欢迎大家试用反馈，如果顺手报一下你的设备型号和实测结果，也能帮到后面同型号的用户。

### 两个平台的微调与防踩雷建议
- **小众软件（投稿 / 发现频道 `meta.appinn.net`）**：
  - 节奏要紧凑，建议在开头第一屏就放上 `docs/images/win11-panel.png` + 官网演示链接（小众软件读者极喜欢「点开就能看效果」的在线演示）。
  - 结尾直接放清晰的一行链接汇总（官网 / 国内镜像下载 / GitHub），并在发现频道明确标注「自荐 / 开源免费」。
- **少数派（Matrix 专栏 / 社区投稿）**：
  - 更看重「场景共鸣 + 清晰的二级标题 + 高清配图」，建议补拍一张真实 Windows 11 桌面角落截图（带真实深浅色任务栏效果），配图下方加简短图注。
  - 若投 Matrix 首页推荐，适当弱化「求提交 Issue」的篇幅，把它收在文末「写在最后：邀请你一起完善设备库」一节，整体读起来更像一篇高质量效率工具评测/分享。

---

## 三、Reddit `r/MouseReview`（English）

### Title Options (Pick One)

1. **I built Jige Battery — a tiny (~5 MB), strictly read-only, open-source Windows taskbar battery monitor for 2.4 GHz & Bluetooth mice/keyboards/headsets (no vendor bloatware needed)**
2. **Tired of keeping Synapse / G HUB / web drivers open just to check mouse battery? I made a ~5 MB read-only taskbar monitor (MIT open source, looking for hardware testers)**
3. **[Open Source] Jige Battery v0.2.27 — read-only Windows taskbar battery monitor covering G-Wolves, Finalmouse ULX, Razer, Logitech, Lamzu, WLmouse, Pulsar, ATK/VXE/Scyrox & more**

### Post Body (Ready to Paste)

If you rotate between multiple wireless mice (or mix brands across mouse, keyboard, and headset), checking battery levels on Windows is surprisingly annoying. You dial in your DPI, polling rate, and lift-off distance once, close Synapse / G HUB / vendor hubs to kill background bloat, and then you're flying blind until your mouse dies mid-match—or you have to keep opening web drivers just to see a percentage.

I built **Jige Battery** (`v0.2.27`, MIT licensed) to fix this for myself and just shipped a major batch of peripheral protocol support. I'm sharing it here because I only own a handful of mice myself and would really appreciate real-world tests and diagnostic reports from folks running other models.

- **GitHub & Source:** https://github.com/deepfry666/jige-battery
- **Latest Release (v0.2.27):** https://github.com/deepfry666/jige-battery/releases/latest
- **Full Supported Device List & Verification Status:** https://github.com/deepfry666/jige-battery/blob/main/docs/devices.md
- **Website & Interactive UI Demo (Chinese):** https://jige.imfourj.cn

#### Why it's built specifically for gaming setups

- **Strictly Read-Only (no bricks, no messed-up mouse settings):** Every packet sent to a 2.4 GHz USB receiver or serial port goes through an explicit per-adapter whitelist limited to battery, charging, link status, and read-only device identity. It **never** touches DPI, polling rate, LOD, RGB, macros, pairing, or firmware, and closes device handles right after querying. If a device's known battery protocol requires switching modes (e.g., certain Razer PA headsets) or has unverified packet branches (e.g., Endgame Gear 4K dongle `3367:1970`, ATK A9 Ultra), Jige explicitly skips it *before even opening a handle*.
- **Zero Disturbance in Games:** Automatically hides the taskbar strip, pauses popups, and slows background polling to once every 5 minutes in exclusive fullscreen games (with 400 ms exit hysteresis so alt-tabbing doesn't flicker). When waking sleeping mice for a re-check, it only checks Windows' `GetLastInputInfo` timestamp—**no keyboard or mouse hooks**. Optional click-through, non-focus-stealing low-battery banner when launching borderless games.
- **Tiny Native Binary:** Single-process C# / .NET 10 NativeAOT (~5 MB installer), no runtime required, no admin privileges, zero telemetry.
- **Taskbar Strip + Tray Fallback:** Displays device icons + percentages right next to your Windows 10/11 system tray clock (5 styles, 9 color themes, auto-adapts to dark/light/acrylic taskbars), or switch to silhouette fill-level tray icons.

#### Supported Mice & Peripherals (Being 100% Honest About Verification)

I strictly separate **hardware-verified** devices from **protocol-implemented** ones in the catalog—I never mark something verified until a real Windows reading is confirmed:

- **Hardware-Verified on Windows 11:** G-Wolves HTX Ultra 8K, Sony INZONE H9 (Gen 1), Razer BlackShark V3 Pro, ASUS ROG wireless keyboard (`0B05:1934`) — plus Logitech MX Keys S & MX Anywhere 3S in daily use on Win10.
- **Same-Family Inferred:** G-Wolves Fenrir Pro 8K, HTM Plus, HSK Pro 2.0 8K (same XVI-NEW table/protocol).
- **Implemented from Public Protocol Docs / Open-Source Specs (Seeking Real-World Verification!):**
  - **Enthusiast / Lightweight Wireless Mice:** Finalmouse UltralightX (ULX + generic `361D` matching), 21 older G-Wolves `XVI-OLD` models, **Lamzu** Maya X (1K/8K), **WLmouse** Beast X series, **Pulsar** X2 V2 / V3 / CrazyLight, **Hitscan** Hyperlight, **Endgame Gear** XM2we, **Glorious** Model O Wireless, **Scyrox** V8 + newly added `395A` series (V6S / V8S / V12 in `v0.2.27`), **ATK** (F1 Ultimate, ZERO, A9 Plus NK, A9 Pro Max 2.0 NK + generic `373B`), **VXE** R1 / MAD, **VGN** Dragonfly F1 / F2 Master / Y2 / Dragonfly 3 Master, **MCHOSE** (A5 Pro Max, M7 Ultra, L7 Pro, A7 V2 Ultra, V3), **Rapoo** VT7 / VT9 Pro / V2, **Incott** 8K, **ATTACK SHARK**, **Delux**, **DAREU**, **AJAZZ**, **EWEADN**, **Keychron** Ultra-Link 8K / M5 / Nape Pro.
  - **Mainstream Gaming Brands:** **Razer** (72 mouse PIDs incl. Viper V2/V3/V4 Pro, DeathAdder V2/V3/V4 Pro, Basilisk, Orochi V2, HyperPolling dongle + 15 keyboards + BlackShark V2 Pro 2020 / V2 HyperSpeed / V3), **Logitech** (LIGHTSPEED, Bolt, Unifying, Nano, wired/BLE HID++, plus G533/G535/G733/G PRO X/G PRO X 2/ASTRO A50 Gen 5 headsets), **ASUS ROG / TUF** (33 mouse PIDs incl. Harpe Ace / Gladius III / Keris, OMNI dongle, Azoth/Falchion/Scope II 96 keyboards), **SteelSeries** (Aerox 3/5/9, Rival 3, Arctis/Nova headsets), **Corsair** (Sabre v2 Pro, M75, Dark Core, Void/HS80/Virtuoso, K65 PLUS), **HyperX** (Cloud II / Flight / Alpha / III / III S Wireless), **Audeze** Maxwell, plus any standard Windows Bluetooth battery device.

#### Two Heads-Up Before You Try It

1. **Panel & Installer UI is currently Simplified Chinese:** The taskbar strip itself is just clean icons + `%` numbers (e.g. `[mouse] 12%`), so day-to-day use is language-agnostic, while the flyout panel, settings, and installer are currently in Chinese. Full English instructions for setup and reporting devices are in [`CONTRIBUTING.md#english`](https://github.com/deepfry666/jige-battery/blob/main/CONTRIBUTING.md#english). If enough English-speaking users find this useful, adding an English UI toggle is high on my list.
2. **Unsigned Installer (SmartScreen Warning):** I haven't purchased an EV/code-signing certificate for this free MIT hobby project yet, so Windows SmartScreen will show a warning on first run (*More info → Run anyway*). `SHA256SUMS.txt` is published with every release, builds are produced by GitHub Actions, and you can also build from source with `dotnet`.

#### Want Your Mouse Verified or Added for `v0.2.28`?

If you test it on your mouse—whether it **works out of the box** (so I can promote it to Hardware-Verified!) or **doesn't show up / reads wrong**—you can export a local sanitized diagnostic report in ~30 seconds:

1. Check your current battery % in the official app/web driver, then **fully exit the vendor software** (including tray background processes).
2. Move your mouse to wake it from sleep.
3. Right-click the Jige taskbar strip/tray icon → click **诊断信息…** (Diagnostics). It saves a sanitized `diagnostics.txt` locally and opens it in Notepad for you to inspect first—serial numbers, Windows device instance IDs, usernames, computer names, COM port numbers, and file paths are all scrubbed, and nothing is ever auto-uploaded.
4. Click **复制报告并去反馈** (Copy report & open feedback) in the panel, or paste it into the [Device Report template](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml).

Happy to answer any questions about the HID protocols, safety checks, or specific mice!

### `r/MouseReview` Community Notes & Pitfalls
- **Flair**: Use `Software` / `Discussion` / `Misc` (check current subreddit flair list before posting; avoid `Review`).
- **Media**: If posting as an Image/Gallery post + top-level explanation comment, attach `docs/images/win11-taskbar.png` FIRST (universal icons + `%`, no Chinese text barrier!) and `docs/images/win11-panel.png` second. Or post as a Text post with direct image links.
- **Anti-backlash**: `r/MouseReview` users are famously sensitive to anything that might interfere with polling rate, introduce input lag, or brick 8K dongles. Keep emphasizing: **no input hooks**, **5-minute polling / hidden strip in fullscreen games**, **strictly read-only whitelist**, and **MIT open source**.

---

## 四、Reddit `r/windows` 与 `r/opensource`（English）

> 下面提供两个独立成稿版本：**4A 面向 `r/windows`**（突出 Windows 10/11 任务栏原生体验、轻量免依赖、替代臃肿驱动），**4B 面向 `r/opensource`**（突出 MIT 净室实现、.NET 10 NativeAOT 架构、隐私脱敏回放测试与社区协作）。建议**不要同一天刷屏**，先发一个再隔 2–3 天发另一个。

### 4A. Reddit `r/windows` 版本

#### Title Options
1. **Jige Battery — a ~5 MB native Windows 10/11 taskbar battery strip for wireless mice, keyboards & headsets without keeping vendor bloatware running (MIT Open Source)**
2. **I made an open-source, ~5 MB .NET 10 NativeAOT app that puts wireless peripheral battery percentages directly on the Windows 10/11 taskbar**

#### Post Body (Ready to Paste)

Windows still doesn't give you a clean way to see battery levels for 2.4 GHz wireless peripherals on the taskbar without leaving heavy vendor suites (Synapse, G HUB, iCUE, INZONE Hub, Armoury Crate, etc.) running in the background.

I built **Jige Battery** (`v0.2.27`, MIT licensed) to keep all of those closed while showing wireless mouse, keyboard, and headset battery levels right next to the Windows 10/11 system tray clock.

- **GitHub:** https://github.com/deepfry666/jige-battery
- **Latest Release (`v0.2.27`):** https://github.com/deepfry666/jige-battery/releases/latest
- **Supported Devices:** https://github.com/deepfry666/jige-battery/blob/main/docs/devices.md

#### Highlights

- **Native Windows 10 & 11 Taskbar Integration:** Renders a lightweight taskbar strip right next to the notification area (automatically avoiding app buttons, Win10 News & Interests, and right-side toolbars via UIA geometry checks), with **5 visual styles** and **9 color themes** that adapt to light/dark/accent/acrylic taskbars. Falls back to bottom-up fill silhouette tray icons whenever space is tight or the taskbar isn't on the bottom edge (or if you prefer classic tray icons).
- **Tiny & Native (.NET 10 NativeAOT):** ~5 MB installer, single process, no .NET runtime install needed, no admin rights required (installs per-user to `%LOCALAPPDATA%\Programs\Jige`). The taskbar strip uses lightweight GDI layered rendering with low-memory GC settings rather than keeping permanent D3D/D2D GPU contexts alive.
- **Strictly Read-Only & Zero Telemetry:** Talks directly to 2.4 GHz USB receivers (plus native Windows Bluetooth battery properties) using whitelisted read-only battery/status queries only. Never changes device settings or firmware, backs off when official software is open, and never phones home except optional daily GitHub/mirror update checks (SHA-256 verified, user-clicked install, toggleable in settings).
- **Quiet During Fullscreen Games:** Auto-hides the strip, suppresses popups, and drops polling to every 5 minutes in exclusive fullscreen apps.
- **Wide Brand Coverage:** Supports 2.4 GHz receivers across Logitech (Bolt/Unifying/LIGHTSPEED/headsets), Razer (72 mouse PIDs, 15 keyboards, BlackShark headsets), ASUS ROG/TUF, SteelSeries, HyperX, Corsair, Sony INZONE H9, G-Wolves, Finalmouse ULX, Lamzu, WLmouse, Pulsar, Hitscan, ATK/VXE/VGN/Scyrox, MCHOSE, Keychron, Audeze Maxwell, plus standard Windows Bluetooth battery devices.

#### Full Transparency Notes

- **SmartScreen Warning:** The installer is currently unsigned (personal free project), so Windows SmartScreen will prompt on first run. `SHA256SUMS.txt` is included in every release, and all source code / GitHub Actions build workflows are public if you prefer building locally.
- **UI Language:** The taskbar strip itself is just icons + percentages, while the flyout panel, settings, and installer are currently in Simplified Chinese (English setup & feedback guide is in [`CONTRIBUTING.md#english`](https://github.com/deepfry666/jige-battery/blob/main/CONTRIBUTING.md#english)). Happy to prioritize English UI localization if folks here want it!
- **Hardware Verification Help Wanted:** Because I only have a few physical devices on hand (4 hardware-verified on Win11 + 2 Logitech MX daily drivers), most protocol adapters are implemented from open-source specs and marked accordingly. If you try it and want your device promoted to verified—or added in `v0.2.28`—you can export a locally scrubbed `diagnostics.txt` inside the app (opens in Notepad for your review first, zero auto-upload) and paste it into a [Device Report](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml).

---

### 4B. Reddit `r/opensource` 版本

#### Title Options
1. **Jige Battery (MIT) — a clean-room C# .NET 10 NativeAOT Windows taskbar battery monitor for 2.4 GHz & Bluetooth peripherals (read-only, zero telemetry)**
2. **Open-sourced my ~5 MB read-only Windows taskbar battery monitor for multi-brand wireless peripherals (C# .NET 10 NativeAOT, MIT)**

#### Post Body (Ready to Paste)

Hi everyone — I've been building **Jige Battery** (`v0.2.27`), a free, MIT-licensed Windows 10/11 utility that displays battery levels for 2.4 GHz wireless and Bluetooth mice, keyboards, and headsets directly on the taskbar so you don't need to keep heavy proprietary vendor suites running.

- **Repo:** https://github.com/deepfry666/jige-battery
- **Release (`v0.2.27`):** https://github.com/deepfry666/jige-battery/releases/latest
- **Protocol Docs & Credits:** [`docs/protocols.md`](https://github.com/deepfry666/jige-battery/blob/main/docs/protocols.md) · [`docs/credits.md`](https://github.com/deepfry666/jige-battery/blob/main/docs/credits.md)
- **Privacy & Scrubbing Spec:** [`PRIVACY.md`](https://github.com/deepfry666/jige-battery/blob/main/PRIVACY.md)

#### Engineering & Open-Source Choices Worth Sharing

- **C# / .NET 10 NativeAOT (~5 MB installer):** Zero runtime dependencies, single process, no admin elevation, Win32/CsWin32 interop with zero-reflection JSON serialization and AOT/trim analyzers enabled.
- **Clean-Room MIT Implementations:** All peripheral adapters are written independently in C# from public protocol specifications, web driver model tables, and MIT-licensed community references (with full source attribution in `docs/credits.md` and `docs/protocols.md`; zero code copied from GPL/AGPL projects).
- **Strict Read-Only Guardrails:** Every outgoing HID/serial packet must pass an explicit per-adapter read-only whitelist. Devices whose known battery query requires switching modes (like certain Razer PA/RACE headsets) or have unconfirmed reply branches are listed in `DeviceCatalog.DisabledPaths` and skipped *before opening a device handle*.
- **Privacy-First Replayable Diagnostics:** To debug or add new hardware without asking users for raw USB dumps containing serial numbers, Jige exports a locally scrubbed `diagnostics.txt` (stripping serials, Windows instance IDs, usernames, computer names, COM ports, and local paths, masking unknown payload bytes) and opens it in Notepad for review first. Approved reports can be fed straight into our offline `FakeReceiverTransport` replay unit tests (`dotnet test Jige.slnx` runs cleanly without physical hardware).
- **Zero Telemetry:** No analytics, no accounts. Network access is strictly limited to optional update checks (SHA-256 verified, user-confirmed install, fully toggleable off).

#### Honest Caveats & Looking for Contributors / Testers

- **UI Localization:** The taskbar strip is just icons + `%`, while the flyout panel and installer are currently Simplified Chinese only (with English documentation in `CONTRIBUTING.md#english`). PRs or feedback toward English UI localization are very welcome.
- **Unsigned Installer:** Triggers Windows SmartScreen on first run; `SHA256SUMS.txt` and transparent GitHub Actions workflows are provided, or build straight from source.
- **Hardware Verification Drive (`v0.2.28`):** Only 4 devices are currently marked Hardware-Verified on Win11 (+ 2 Logitech MX devices in daily use), while dozens of models across Razer, Logitech, ASUS ROG, SteelSeries, HyperX, Corsair, Finalmouse, G-Wolves, Lamzu, WLmouse, Pulsar, Scyrox, ATK/VXE/VGN, MCHOSE, Keychron, and Audeze are implemented from public docs. Real-world `diagnostics.txt` reports (even just confirming an existing device works!) are the #1 thing that helps right now: [Device Report Template](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml).

### `r/windows` & `r/opensource` Community Notes & Pitfalls
- **`r/windows` 规则检查（发帖前必看！）**：`r/windows` 对自我推广 / 软件展示有严格日期或频率限制（例如仅允许在特定日子或须遵循 `Self-Promotion` / `Software` 版规，且账号需有正常发帖记录）。发布前请打开 `r/windows` 侧边栏 Rules 确认当周允许发开源工具的时段和 Flair，避免被删帖或封禁。
- **`r/opensource` 规则检查**：强制要求正文直链开源仓库和明确许可证（MIT），切忌只放官网链接或商业营销口吻；强调 `PRIVACY.md`、`docs/credits.md` 的净室合规与离线回放测试架构，非常契合社区胃口。

---

## 五、发布前检查清单（Pre-Publish Checklist）

### 1. 配图与视觉素材建议

仓库已有素材清点如下：

| 仓库路径 | 分辨率 | 画面内容 | 推荐使用平台与顺序 |
| --- | --- | --- | --- |
| `docs/images/win11-panel.png` | `1188×1030` | Win11 深色任务栏直读条（鼠标 12% 红 / 耳机充电 45% 绿 / 键盘 80%）+ 展开的主面板（ATK 鼠标、MX Anywhere 3S、INZONE H9、Finalmouse ULX、MX Keys S） | **V2EX 主图**、**小众软件/少数派首图**、**Reddit 第二张图** |
| `docs/images/win11-taskbar.png` | `896×336` | Win11 任务栏直读条特写（无中文文字，纯图标 + 百分比 + 充电闪电） | **Reddit 首图（强烈推荐，零语言门槛）**、**小众软件/少数派细节图** |
| `docs/images/win10-tray.png` | `896×336` | Win10 传统托盘水位剪影图标特写（鼠标/耳机/键盘由底向上填色） | **小众软件/少数派对比图**、**Reddit `r/windows` 附图** |
| `docs/images/social-preview.png` | `1280×640` | GitHub 社交分享卡片（左文右图，MIT / Win10&11 / ~5 MB / Read-Only） | **少数派封面图参考**、社交平台转发卡片 |
| `site/assets/og-card.png` | `1200×630` | 官网 Open Graph 分享卡片（外链分享时自动抓取） | 确认 `https://jige.imfourj.cn` 分享预览正常显示 |
| `design/previews/0.2.10/taskbar-strip-*.png` | `185×48` / `370×96` | 程序导出的浅/深色任务栏直读条渲染小图 | 可选作对比小图 |

> **⚠️ 非常值得在发布前补拍的 2 个真实素材（强烈建议）**：
> 1. **诚实说明现有图源**：根据 `docs/images/README.md`，目前 `docs/images/win11-*.png` 与 `win10-tray.png` 是从官网演示区截取的真实渲染像素/UI 定稿图，而非真实 Windows 桌面截图。
> 2. **建议补拍**：在真实 Windows 11/10 机器上拍 **1 张真实桌面右下角截图**（如果能顺带同框截一张任务管理器里 `Jige.exe` 的内存与 0% CPU 占用就更有说服力），再录一段 **8～12 秒的短 GIF/MP4**（展示：任务栏直读条 → 鼠标悬停看卡片 → 点击展开面板 → 切换直读条样式或外观模式）。这在 V2EX、少数派和 Reddit 上能直接把点击率和信任度拉高一个档次。

### 2. 发布前技术与链接自检

- [ ] **打开官网实测**：访问 `https://jige.imfourj.cn` 和 `https://jige.imfourj.cn/download.html`，确认显示版本号为 `v0.2.27`，在线演示区交互无报错。
- [ ] **国内镜像下载直链实测**：实际点击下载 `https://download.imfourj.cn/jige-battery/releases/v0.2.27/Jige-Setup-0.2.27-x64.exe` 与 `SHA256SUMS.txt`，确认 HTTP 200、下载速度正常、哈希值一致。
- [ ] **GitHub Release 实测**：确认 `https://github.com/deepfry666/jige-battery/releases/latest` 指向 `v0.2.27`，包含安装包与 `SHA256SUMS.txt`。
- [ ] **设备报告模板实测**：打开 `https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml`，确认表单正常渲染、说明无过时版本号（注：模板 placeholder 示例里写的是 `0.2.26` 或 `0.2.27 测试版`，不影响使用，后续顺手可更新）。
- [ ] **GitHub 仓库首页体检**：确认 README 顶部徽章、图片、`English Overview` 折叠块及 `docs/devices.md` 链接均无 404。
- [ ] **准备接收 Issue 的响应节奏**：发帖后前 24 小时尽量高频看 GitHub Issues 和帖子评论，第一批愿意导出 `diagnostics.txt` 的热心用户若能在几小时内得到回复（哪怕只是确认收到、说明排进 `0.2.28`），口碑传播效果最好。

### 3. 建议发布节奏与时间窗口

建议**分 3 波错峰发布**，用第一波中文极客反馈兜底（万一发现共性小 Bug 或 SmartScreen 说明不够清楚，可以先热修再上更大流量池）：

| 波次 | 平台 | 建议时间 | 目的 |
| --- | --- | --- | --- |
| **第 1 波（冷启动试水）** | **V2EX（分享创造）** | 工作日 **上午 10:00–11:30** 或 **下午 14:30–16:00**（北京时间） | 开发者和外设极客密度高，最愿意提 GitHub Issue 和导 `diagnostics.txt`，能快速检验文档与下载链路 |
| **第 2 波（中文实用圈）** | **小众软件** → **少数派** | V2EX 发帖后 **2–3 天**；工作日 **10:00–12:00** 或 **20:00–22:00**（北京时间） | 覆盖更广泛的普通 Windows / 办公 / 游戏用户，积累真实品牌覆盖面（罗技/雷蛇/国产鼠标等） |
| **第 3 波（海外外设/开源圈）** | **Reddit `r/MouseReview`** → 隔 2–3 天发 **`r/windows`** 或 **`r/opensource`** | **美国东部时间（EST）上午 9:00–11:30**（即北京时间 **22:00–00:30**；周二至周四效果通常最佳；发 `r/windows` 前先确认该版块当日规则） | `r/MouseReview` 拥有大量小众/高端鼠标用户（Finalmouse、G-Wolves、Lamzu、WLmouse、Pulsar、Hitscan、Scyrox 等），是最稀缺的实机诊断样本来源 |

---

### 4. 高频评论回复话术模板

#### 场景 A：用户说「我的 XX 鼠标没识别 / 电量不准」（引导导出 `diagnostics.txt`）

**中文版（V2EX / 小众软件 / 少数派）：**
> 感谢实测！这款目前在仓库里是「仅按资料」/ 尚未收录，麻烦帮我导一份脱敏诊断，基本就能定位并加进 `0.2.28`：
> 1. 先在官方驱动里记一下当前电量和充电状态，然后**彻底退出官方驱动**（包括右下角托盘里的后台进程）；
> 2. 动一下鼠标 / 确认设备处于唤醒连接状态；
> 3. 在几格右键点「**诊断信息…**」，它会在本机生成 `diagnostics.txt` 并用记事本打开给你先看（序列号、用户名、电脑名和本地路径都已自动脱敏，不会自动上传）；
> 4. 检查没问题后点面板底部的「**复制报告并去反馈**」，直接贴进 Issue 就行（不方便上 GitHub 的话，把脱敏后的文本发我或者贴在帖子里也行）。

**English（Reddit）：**
> Thanks for testing it! That model is currently either protocol-only or not yet in the catalog. If you have 30 seconds, a scrubbed diagnostic report will let me fix/add it for `v0.2.28`:
> 1. Note the current battery % in the official app/web driver, then **completely exit the vendor app** (including its tray process).
> 2. Move the mouse / wake the device so the 2.4 GHz link is active.
> 3. Right-click Jige's taskbar strip/tray icon → click **诊断信息…** (Diagnostics). It generates a local `diagnostics.txt` and opens it in Notepad so you can inspect it first—serials, device instance IDs, usernames, computer names, and paths are all scrubbed, and nothing is ever auto-uploaded.
> 4. Click **复制报告并去反馈** (or paste it into the [Device Report template](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml)) along with your exact model and the official battery reading. Appreciate the help!

#### 场景 B：用户说「测了 XX 型号，读数和官方一致，能用！」（引导留下实机升级依据）

**中文版：**
> 太好了，谢谢反馈！因为我对「已实机验证」比较较真，希望每台标成已验证的设备都有真实的 `diagnostics.txt` 回放样本留档（后续改协议回归测试也能用上）。如果你方便的话，顺手导一份脱敏后的 `diagnostics.txt` 贴到 [设备报告](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml)，我就能把它正式升级成「已实机验证」写进 `0.2.28` 清单里！

**English：**
> Awesome to hear it works out of the box! I keep a strict rule of only promoting a model to "Hardware-Verified" once I have a real sanitized `diagnostics.txt` fixture for offline regression tests. If you don't mind dropping a quick [Device Report](https://github.com/deepfry666/jige-battery/issues/new?template=device-report.yml) with the scrubbed `diagnostics.txt`, I'll upgrade it to Verified in `v0.2.28`!

#### 场景 C：用户担心「Windows SmartScreen 蓝屏拦截 / 杀毒软件误报」

**中文版：**
> 理解你的顾虑！目前弹 SmartScreen 是因为这是个人免费的开源项目，还没买每年几千块的代码签名证书，新发布且下载量小的 `.exe` 都会被 Microsoft Defender 标成「未知发布者」。
> 你有几种更放心的验证方式：
> 1. 仓库完全 MIT 开源，Release 安装包是由 GitHub Actions 公开工作流直接构建出来的，并附了 `SHA256SUMS.txt`；
> 2. 下载后可以在 PowerShell 跑 `Get-FileHash .\Jige-Setup-0.2.27-x64.exe -Algorithm SHA256` 核对；
> 3. 如果你本机有 .NET 10 SDK，也可以直接 clone 源码自己 `dotnet build` 编译运行。

**English：**
> Totally fair concern! The SmartScreen prompt happens because this is a free solo open-source project and I haven't paid for a code-signing certificate yet, so low-volume newly released `.exe` installers get flagged as "Unknown publisher". Every release is built transparently via GitHub Actions with a matching `SHA256SUMS.txt`, the full C# source is MIT-licensed right in the repo, and you can also verify on VirusTotal or build it from source yourself with the .NET 10 SDK.

#### 场景 D：用户问「和 HaloBattery / BatteryHub / Solaar / G-Helper 有什么区别？」

**中文版：**
> 非常感谢提到这些优秀的开源项目——几格在协议调研阶段也参考了 HaloBattery、Solaar、OpenRazer、HeadsetControl、G-Helper、OpenMouse 等项目的公开协议资料（在 `docs/credits.md` 里都逐一致谢了）。
> 几格的差异主要在定位上：
> - **面向 Windows 原生任务栏体验**：用 C# .NET 10 NativeAOT 编译成约 5 MB 单进程，免装 Python/运行时环境，默认直接做进 Win10/11 任务栏直读条；
> - **极端保守的只读与游戏防打扰策略**：所有发包走显式白名单，涉及切模式的协议默认在打开句柄前跳过，全屏游戏自动隐藏并降频轮询；
> - **严格区分验证状态**：绝不把没在 Windows 实机验证过的型号标成已验证。
> 如果你需要改 DPI/灯效/键位，G-Helper 或 Solaar（Linux）功能更全；几格只专注把「不开驱动、在 Windows 任务栏安静看电量」这一件事做好。

**English：**
> Huge respect to those projects—HaloBattery, Solaar, OpenRazer, HeadsetControl, G-Helper, and OpenMouse were invaluable protocol references (all credited in `docs/credits.md`, while Jige's C# adapters are clean-room MIT implementations).
> Jige's focus is intentionally narrower:
> - **Native Windows 10/11 taskbar strip out of the box** in a single ~5 MB .NET 10 NativeAOT binary (no Python/runtime setup needed);
> - **Strictly read-only & game-quiet** (whitelisted queries only, mode-switching paths disabled before opening handles, fullscreen auto-hide + 5-min polling);
> - **Transparent verification tiers** so you know what's hardware-verified vs. protocol-implemented.
> If you want full device configuration/RGB/fan control, tools like G-Helper or Solaar are great—Jige strictly sticks to quiet, read-only taskbar battery monitoring.

#### 场景 E：Reddit 用户问「会有英文界面吗？/ 有 Linux 或 macOS 版本吗？」

**English：**
> - **English UI:** Yes, it's definitely on my radar! Right now the taskbar strip itself is just icons + `%` numbers so it's usable as-is, and `CONTRIBUTING.md` has full English instructions for exporting diagnostics. If there's real interest from English-speaking users here, I'll move full English UI localization near the top of the roadmap.
> - **Linux / macOS:** Jige is built specifically around Win32/Direct2D/Windows taskbar UIA, so it's Windows 10/11 only. On Linux, Solaar / OpenRazer / HeadsetControl / UPower already do a great job!

<p align="center"><img src="design/brand/fourj-mark.svg" width="88" alt="几格标志"></p>

<h1 align="center">几格 · Jige Battery</h1>

<p align="center"><b>不开厂商驱动，也能在 Windows 任务栏上看到无线鼠标、键盘和耳机的电量。</b></p>

<p align="center">
  <a href="https://jige.imfourj.cn">官网</a> ·
  <a href="https://jige.imfourj.cn/download.html">下载</a> ·
  <a href="CHANGELOG.md">更新记录</a> ·
  <a href="docs/devices.md">支持的设备</a>
</p>

<p align="center">免费 · MIT 开源 · 约 5 MB · Windows 10 / 11（64 位）</p>

<p align="center"><img src="docs/images/win11-panel.png" width="560" alt="Windows 11 上的几格：任务栏时钟左边显示鼠标 12%、耳机充电中 45%、键盘 80%，上方是展开的几格面板"></p>

## 它解决什么问题

在雷蛇、罗技或者各家鼠标驱动里调好灵敏度和回报率以后，很多人会把驱动关掉，免得占后台、影响游戏。可这样一来就看不到电量了，经常是打到一半鼠标突然没电。同时用几个牌子的话，为了看个电量还得开一堆软件。

几格直接和 USB 接收器通信，只发查电量的指令，所以厂商驱动可以一直关着。一个小工具就能同时看多个品牌的设备。

## 在 Windows 11 和 Windows 10 上的样子

| Windows 11 · 任务栏直读条 | Windows 10 · 托盘图标 |
| --- | --- |
| <img src="docs/images/win11-taskbar.png" width="400" alt="Windows 11 任务栏：时钟左边显示鼠标 12% 红色、耳机充电中 45% 绿色、键盘 80%"> | <img src="docs/images/win10-tray.png" width="400" alt="Windows 10 托盘：鼠标、耳机、键盘三个图标，按电量从底部往上填色"> |
| 时钟左边横排显示每台设备的图标和电量，字和时钟一样大。 | 每台设备一个图标，设备剪影就是电量计，电量越多填得越高。 |

两边的颜色规则一样：低于提醒阈值变红，略高于阈值变黄，充电中变绿并带一个闪电。

## 功能

- **一眼看电量**：任务栏常驻显示，鼠标移上去看所有设备的名称、状态和读取时间。
- **主面板**：单击打开，电量最低的设备排在最上面，断开的设备收进「离线设备」。
- **低电量提醒**：设备没在充电、连续两次读数都不高于阈值时才提醒，可以选「1 小时后提醒」。
- **不打扰游戏**：全屏游戏时不弹窗、不抢焦点，查询放慢到 5 分钟一次，提醒推迟到回到桌面。
- **自动更新**：每天检查一次新版本，后台下载并核对 SHA-256，面板底部提示「新版已就绪」，点一下才升级。可以在设置里关掉。
- **轻量**：原生编译，单个进程，不需要安装 .NET 运行库，也不需要管理员权限。

## 下载安装

| 渠道 | 链接 |
| --- | --- |
| **国内镜像**（不用代理） | [Jige-Setup-0.2.17-x64.exe](https://download.imfourj.cn/jige-battery/releases/v0.2.17/Jige-Setup-0.2.17-x64.exe) · [SHA256SUMS.txt](https://download.imfourj.cn/jige-battery/releases/v0.2.17/SHA256SUMS.txt) |
| GitHub Release | [最新正式版](https://github.com/deepfry666/jige-battery/releases/latest) |

两个渠道的安装包完全相同。运行安装器即可，默认装到 `%LOCALAPPDATA%\Programs\Jige`，可以勾选开机启动。安装包暂时没有代码签名，Windows 可能提示「已保护你的电脑」，点「更多信息 → 仍要运行」就行，详细步骤见 [下载与安装指南](https://jige.imfourj.cn/download.html)。

想核对文件，可以在 PowerShell 里运行下面的命令，结果应和 `SHA256SUMS.txt` 一致：

```powershell
Get-FileHash .\Jige-Setup-0.2.17-x64.exe -Algorithm SHA256
```

卸载：在 Windows「已安装的应用」里卸载，卸载时可以选择是否一起清除设置和记录。

## 支持的设备

通过 2.4 GHz USB 接收器读取，也支持 Windows 能读到电量的蓝牙设备。

| 品牌 | 覆盖范围 | 实机验证 |
| --- | --- | --- |
| Sony INZONE | INZONE H9 一代 | ✅ |
| 游狼 G-Wolves | HTX Ultra 8K，以及同协议的 Fenrir Pro 8K、HTM Plus、HSK Pro 2.0 8K 和 21 款旧协议型号 | ✅ HTX Ultra 8K |
| 雷蛇 Razer | 72 款鼠标、15 款键盘，BlackShark V3 Pro / V3 / V2 HyperSpeed / V2 Pro (2020) 耳机 | ✅ BlackShark V3 Pro |
| ASUS ROG | Falchion、Azoth、Claymore II、Strix Scope II 96 等无线键盘 | ✅ |
| 罗技 Logitech | Bolt、Unifying、LIGHTSPEED 等接收器，G533 / G535 / G733 / G PRO X 等耳机 | MX Keys S、MX Anywhere 3S 日常在用 |
| ATK | F1 Ultimate、ATK ZERO，以及同家族接收器 | 按协议资料 |
| VXE · VGN · Scyrox | VXE R1 系列、MAD，VGN 蜻蜓 F1 / F2 Master / Y2 / Dragonfly 3 Master，Scyrox V8 等 | 按协议资料 |
| Lamzu · WLmouse | Lamzu Maya X，WLmouse Beast X 系列 | 按协议资料 |
| 迈从 MCHOSE | A5 Pro Max 初代、M7 Ultra、L7 Pro、A7 V2 Ultra 鼠标，K87 键盘 | 按协议资料 |
| ATTACK SHARK · Delux · 英菲克 | ATTACK SHARK 鼠标、Delux 鼠标、Inphic IN9 Pro | 按协议资料 |
| 雷柏 Rapoo · Incott | VT7、VT9 Pro、VT9 Pro V2，Incott 8K 鼠标 | 按协议资料 |
| Finalmouse | UltralightX（ULX） | 按协议资料 |

「按协议资料」表示依据公开的协议资料实现，还没有在实机上确认。完整型号、接收器 ID 和验证状态见 [设备支持清单](docs/devices.md)。

**你的设备不在表里，或者读数不对？** 在几格里导出诊断报告，按 [反馈指南](CONTRIBUTING.md) 提交，就能帮它加上适配。

## 只读与隐私

- **只读**：只查询电量、充电和连接状态，不改任何设备设置，查询结束就释放设备。厂商软件正在占用接收器时，几格会主动退让，保留上次的读数。
- **不上传数据**：没有账号，没有遥测。联网只用来检查更新，请求里不带任何设备或个人信息。
- **诊断报告由你决定**：只在你点「导出诊断信息」时生成，先完整打开给你看，已去掉序列号、电脑名、用户名和路径。

具体收集了哪些字段，见 [隐私说明](PRIVACY.md)。

几格与各设备厂商无关，也未获得厂商授权。协议来自公开的社区资料和网页驱动的公开行为，只用于显示电量。

## 参与和开发

- 设备反馈与提交代码：[CONTRIBUTING.md](CONTRIBUTING.md)
- 构建、命令行探针、配置文件和发布流程：[docs/development.md](docs/development.md)
- 协议细节和出处：[docs/protocols.md](docs/protocols.md)
- 致谢与参考项目：[docs/credits.md](docs/credits.md)

几格依据公开协议字段和实机报文独立实现，不复制 GPL 代码，以 [MIT 许可证](LICENSE) 发布。

---
name: 几格 / Jige Battery
description: Warm paper-ceramic editorial product site with an anodized obsidian hardware showcase.
colors:
  paper: "#f4f1ea"
  surface: "#fcfbf8"
  ink: "#191715"
  ink-secondary: "#5b554e"
  ink-tertiary: "#7e776f"
  hairline: "rgba(25, 23, 21, 0.10)"
  hairline-strong: "rgba(25, 23, 21, 0.18)"
  obsidian: "#111010"
  obsidian-raised: "#1a1918"
  accent: "#d45226"
  verified: "#1e7a46"
typography:
  display:
    fontFamily: "Segoe UI Variable Display, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, sans-serif"
    fontSize: "clamp(38px, 5.2vw, 68px)"
    fontWeight: 650
    lineHeight: 1.12
    letterSpacing: "-0.025em"
  headline:
    fontFamily: "Segoe UI Variable Display, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, sans-serif"
    fontSize: "clamp(26px, 3.4vw, 40px)"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "-0.02em"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, Noto Sans CJK SC, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.7
    fontFeature: "tnum"
  mono:
    fontFamily: "Cascadia Mono, Consolas, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontFeature: "tnum"
---

# Design System: 几格 / Jige Battery

## Overview

**Creative North Star: "宣纸陶瓷 × 黑曜石硬件橱窗"（Paper Ceramic & Obsidian Showcase）**

官网以浅色宣纸陶瓷底色（`#f4f1ea`）与深墨正文（`#191715`）建立安静、易读的杂志编辑版式，在首屏交互演示与底部收尾区域嵌入深黑阳极氧化硬件橱窗（`#111010`），让真实的 Windows 任务栏直读条与托盘图标像素图成为视觉焦点。

**Key Characteristics:**
- 浅色主画布（`color-scheme: light`，`theme-color: #f4f1ea`），深色仅用于硬件演示橱窗与收尾下载卡。
- 品牌与导航使用 `assets/app-icon.svg`，页脚创作者署名使用 `assets/fourj-mark.svg`（与 `PRODUCT.md` 品牌归属规范一致）。
- 仅使用系统字体栈与等宽数字（`tnum`），零外部 CDN、零第三方字体、零分析脚本。
- 严格 CSP 兼容：所有样式与脚本均自托管于 `site/styles.css` 与 `site/app.js`，禁止内联 `<script>` 或 `<style>`。
- 动效克制且遵守 `prefers-reduced-motion`：页面采用轻量 `.reveal` 滚动渐现与 `<canvas>` 精密机械表盘弧线背景（指针静止时自动降频省电，页面隐藏时暂停）。

## Colors

- **Paper Ground (`#f4f1ea`)**：页面主底色与 `theme-color`。
- **Ceramic Surface (`#fcfbf8`)**：表格、问答与原理编辑卡片的微抬升表面。
- **Obsidian Showcase (`#111010`)**：首屏交互舞台与底部收尾下载卡，还原 Windows 11 深色任务栏环境。
- **Vermilion Accent (`#d45226`)**：机械表盘游标与局部视觉点缀。
- **Verified Green (`#1e7a46`)**：仅用于「已实机验证」标签，区分实机验证与按资料适配。
- **Battery State Colors**：电量红/黄/绿仅出现在产品直读条像素图与交互演示面板内部，不外溢为站点装饰色。

## Product Imagery & Provenance

- 任务栏直读条（`strip-win11-{1,2,3}x.png`）与托盘图标（`tray-{mouse,headset,keyboard}-{1,2,3}x.png`）由几格真实渲染路径按 1x / 2x / 3x 导出。
- 下载指南页的操作示意图（`guide-edge.svg`、`guide-chrome.svg`、`guide-inapp.svg`、`guide-smartscreen.svg`）均标注「操作示意图」，与真实桌面截图明确区分。

## Do's and Don'ts

### Do:
- **Do** 保持全部资源自托管，确保国内访问免代理且满足严格 CSP。
- **Do** 在静态 HTML 中保留当前最新正式版回退版本号与校验命令，并在运行期通过同源 `latest.json` 安全更新（严格校验 HTTPS、主机白名单与文件名）。
- **Do** 尊重 `prefers-reduced-motion`，并在页面不可见或指针空闲时降低背景动画开销。

### Don't:
- **Don't** 引入任何第三方字体、CDN、统计代码或内联脚本。
- **Don't** 在设备清单中夸大未实机验证设备的验证等级。

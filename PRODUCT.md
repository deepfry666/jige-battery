# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

The product itself is a Windows desktop app (C# / .NET 10 NativeAOT, Direct2D UI). This record covers its public web surfaces, starting with the official site https://jige.imfourj.cn (static HTML/CSS/JS in `site/`).

## Users

- **Primary: players who use 2.4 GHz wireless gaming peripherals** (G-Wolves 游狼, ATK, Finalmouse ULX, Razer, Sony INZONE headsets). They tune the device once in the vendor software, then close it to avoid background processes and latency, and lose sight of battery level. They hear about 几格 from friends or chat, open the site, and want to download it. Most do not write code; many browse from China without a proxy.
- **Secondary: office users** with Logitech MX-class keyboards and mice who want battery on the taskbar.

## Product Purpose

几格 shows the battery of wireless mice, keyboards and headsets on the Windows taskbar, so you know "还剩几格电" at a glance, and reminds you to charge — without keeping vendor drivers running. Site success: a visitor understands that in a few seconds and downloads the installer from the domestic mirror.

## Positioning

The one thing visitors must remember: **不开厂商驱动也能看电量.** 几格 talks to the USB receivers directly with read-only, whitelisted battery queries, so the vendor app can stay closed. One small tool covers many brands at once.

## Operating Context

- Windows 10 1809+ / Windows 11, x64. Taskbar strip is the default on both Windows 10 and Windows 11 (automatically avoiding Windows 10 News & Interests / right-side toolbars, and falling back to per-device tray icons whose silhouettes fill bottom-up when the taskbar is not on the bottom edge or space/UIA is unavailable). Hover card, main panel, settings, low-battery reminder.
- Never disturbs games: no popups while a fullscreen game runs; polling slows to 5 minutes.
- Auto-checks updates daily by default (can be turned off in Settings; installation is always user-clicked): domestic mirror first, GitHub fallback, size + SHA-256 verified for download integrity and release consistency.
- Downloads: domestic mirror `download.imfourj.cn` (version metadata at `/latest.json`, proxied same-origin on the site) and GitHub Releases.

## Capabilities and Constraints

- Read-only: only battery/status queries; never changes device settings; no telemetry; network traffic is strictly limited to release update checks (daily by default when enabled, or manual) and user-approved downloads; diagnostics generated only on demand and shown before sharing.
- Device coverage and verification levels are authoritative in README.md / docs/devices.md. Hardware-verified ("已实机验证") entries currently cover Sony INZONE H9 gen 1, G-Wolves HTX Ultra 8K, Razer BlackShark V3 Pro (`1532:0577`), and ASUS ROG wireless keyboard (`0B05:1934`); Logitech MX Keys S / MX Anywhere 3S are in daily use on Windows 10; everything else is "同家族推定" or "仅按资料".
- Installer is not code-signed; Windows SmartScreen may warn.
- Site constraints: static files on nginx; strict CSP (`script-src 'self'`, `style-src 'self'`, no inline scripts/styles); no third-party resources (no Google Fonts/CDNs/analytics) because visitors are in China; Chinese copy.

## Brand Commitments

- Name: 几格 (from "还剩几格电"), English "Jige Battery". Part of the user's FourJ family — keep the FourJ mark (`design/brand/fourj-mark.svg`) in the footer. The site's visual style is otherwise free (user, 2026-11-20).
- Copy: 简体中文, short plain sentences for non-programmers, no hype.
- Images of the product must be the real interface: the UI prototype `design/ui-claude.html` (the app is drawn from it) or real screenshots. Never draw a fake UI for the site.

## Evidence on Hand

- UI prototype `design/ui-claude.html`: main panel, hover cards, tray icon states (water-level silhouettes), spec notes.
- Tray icon preview: Windows 10 tray with mouse, headset & keyboard icons (`docs/images/win10-tray.png`).
- Program-exported taskbar strip renders: `design/previews/0.2.10/`.
- No testimonials, user counts, press, or benchmarks exist — do not invent any.

## Product Principles

1. Glanceable truth: show the battery number honestly; `≈` marks estimates.
2. Stay out of the way: never interrupt games; quiet by default.
3. Read-only and private: nothing leaves the PC unless the user chooses.
4. Honest coverage: say exactly which devices are verified.

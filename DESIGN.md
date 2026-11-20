---
name: 几格 / Jige Battery
description: Night-desk dark product site where the real Windows interface is the proof.
colors:
  ground: "#08090a"
  raised: "#0f1012"
  hairline: "rgba(255, 255, 255, 0.08)"
  hairline-strong: "rgba(255, 255, 255, 0.14)"
  text-primary: "#f7f8f8"
  text-secondary: "#a1a6ae"
  text-tertiary: "#7d828a"
  lit-white: "#ffffff"
  accent: "#5ec4ff"
  verified: "#6fd28a"
typography:
  display:
    fontFamily: "Segoe UI Variable Display, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, sans-serif"
    fontSize: "clamp(40px, 5.6vw, 72px)"
    fontWeight: 650
    lineHeight: 1.12
    letterSpacing: "-0.025em"
  headline:
    fontFamily: "Segoe UI Variable Display, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, sans-serif"
    fontSize: "clamp(28px, 3.6vw, 44px)"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "-0.02em"
  title:
    fontFamily: "Segoe UI Variable Display, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, sans-serif"
    fontSize: "clamp(22px, 2.4vw, 28px)"
    fontWeight: 650
    lineHeight: 1.3
    letterSpacing: "-0.02em"
  lead:
    fontFamily: "Segoe UI Variable Text, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, Noto Sans CJK SC, sans-serif"
    fontSize: "17px"
    fontWeight: 400
    lineHeight: 1.8
    fontFeature: "tnum"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, Noto Sans CJK SC, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.7
    fontFeature: "tnum"
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, Noto Sans CJK SC, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.4
    fontFeature: "tnum"
rounded:
  focus: "4px"
  sm: "8px"
  control: "10px"
  frame: "12px"
  pill: "999px"
spacing:
  gutter: "clamp(16px, 4vw, 32px)"
  row: "20px"
  gap: "24px"
  section: "clamp(80px, 11vw, 136px)"
  wrap: "1040px"
  wrap-wide: "1160px"
components:
  button-primary:
    backgroundColor: "{colors.text-primary}"
    textColor: "{colors.ground}"
    rounded: "{rounded.control}"
    padding: "0 20px"
    height: "44px"
  button-primary-hover:
    backgroundColor: "{colors.lit-white}"
    textColor: "{colors.ground}"
  button-ghost:
    backgroundColor: "rgba(255, 255, 255, 0.02)"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.control}"
    padding: "0 20px"
    height: "44px"
  button-ghost-hover:
    backgroundColor: "rgba(255, 255, 255, 0.06)"
  button-small:
    backgroundColor: "{colors.text-primary}"
    textColor: "{colors.ground}"
    rounded: "{rounded.sm}"
    padding: "0 14px"
    height: "34px"
  tag:
    textColor: "{colors.text-tertiary}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "4px 12px"
  tag-verified:
    backgroundColor: "rgba(111, 210, 138, 0.07)"
    textColor: "{colors.verified}"
    rounded: "{rounded.pill}"
    padding: "4px 12px"
  nav:
    backgroundColor: "rgba(8, 9, 10, 0.86)"
    height: "60px"
  screenshot-frame:
    backgroundColor: "{colors.raised}"
    rounded: "{rounded.frame}"
---

# Design System: 几格 / Jige Battery

## Overview

**Creative North Star: "The Night Desk"**

A dark desk at night with one lit object on it: the real 几格 interface. The site is the category-standard product page played straight at Linear's level of craft: one promise, one white download button, the real panel as the hero, and everything else quiet enough that the product's own battery colors are the only color that registers. The world is near-black and tonal, drawn with white hairlines instead of boxes, set in the visitor's own system CJK sans, and almost entirely still.

Density is calm and readable for non-programmers browsing in Chinese: generous section breathing room, short plain lines, lists that read as rows of facts rather than marketing tiles. Proof is never illustrated. Every image of the product is a crop of the UI prototype the app is drawn from, or a real Windows screenshot, and is captioned as such.

**Key Characteristics:**
- Dark-only; no light theme, `color-scheme: dark`.
- Tonal layering (ground, raised) plus 8% / 14% white hairlines; no card grids.
- One cool accent, reserved for links, focus and selection.
- Battery state color (yellow, red, green) lives only inside product imagery.
- System font stacks only; no hosted or third-party resources of any kind.
- One authored motion: the hero screenshot settles in once.

## Colors

Near-black neutrals with cool white text and a single sky-blue accent borrowed from 几格's own battery cells.

### Primary
- **Battery-Cell Blue** (accent): links ("完整设备清单", FAQ links), the 2px focus ring, and text selection at 30% alpha. Never a fill, never a button, never a heading color.

### Tertiary
- **Verified Green** (verified): one job only, the 已实机验证 tag (text, 35% border, 7% fill). It marks honest verification status, not battery state.

### Neutral
- **Night Ground** (ground): page background, nav at 86% (96% once scrolled), text on white buttons, `theme-color`.
- **Raised Slate** (raised): the inside of screenshot frames, the one raised surface.
- **Hairline** (hairline, white 8%): section top borders, list row dividers, nav border once scrolled, frame outline.
- **Strong Hairline** (hairline-strong, white 14%): ghost button border, tag border, tray-tile outline.
- **Desk-Lamp White** (text-primary): headlines, list leads, primary button fill.
- **Lit White** (lit-white): hover state of the primary button and FAQ summary; brighter than text-primary on purpose.
- **Soft Graphite** (text-secondary): sublines, leads, list descriptions, nav links at rest.
- **Dim Graphite** (text-tertiary): captions, meta line, footer, neutral tags, chevrons and list markers.

### Named Rules
**The One Accent Rule.** Battery-Cell Blue appears only on links, focus and selection. If it is filling an area or coloring a heading, it is wrong.

**The Color Lives in the Product Rule.** Yellow, red and green battery states appear only inside product screenshots. Site chrome never re-draws them as badges, charts or decoration. The only non-neutral chrome color besides the accent is Verified Green on the verification tag.

## Typography

**Display Font:** Segoe UI Variable Display (with Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei)
**Body Font:** Segoe UI Variable Text (with Segoe UI, -apple-system, PingFang SC, Microsoft YaHei UI, Microsoft YaHei, Noto Sans CJK SC)

**Character:** The visitor's own OS sans, so the site speaks the same voice as the Windows taskbar it describes. Headlines are large, heavy (650) and slightly tight; body text is loose (1.7 to 1.8) for comfortable Chinese reading. Numerals are tabular everywhere.

### Hierarchy
- **Display** (650, clamp 40 to 72px, 1.12, -0.025em): the hero promise only.
- **Headline** (650, clamp 28 to 44px, 1.2, -0.02em): section titles; the closing download headline steps up to clamp 32 to 56px.
- **Title** (650, clamp 22 to 28px, 1.3): sub-section heads inside a section.
- **Lead** (400, 17px, 1.8, max 36em): section introductions in Soft Graphite; the hero subline is clamp 16 to 18.5px, max 30em.
- **Body** (400, 16px, 1.7): list leads at 600 weight; list descriptions and FAQ answers at 15.5px in Soft Graphite.
- **Label** (400, 12.5 to 13.5px): captions, meta line, tags, footer, in Dim Graphite.

### Named Rules
**The System Voice Rule.** No hosted fonts, no webfont files, no CDN. Type comes from the visitor's OS stack; Microsoft YaHei UI is the expected Windows CJK face.

**The Unbroken Phrase Rule.** Chinese sublines are split into phrase spans that don't break internally, and headings use balanced wrapping, so a line never ends on an orphaned character.

## Layout

Centered single column at 1040px (wide variant 1160px for the hero, nav and image galleries), with fluid gutters (clamp 16 to 32px). The hero is a two-column grid (copy 1fr, framed screenshot 1.08fr) whose screenshot ends above the 900px fold at 1440. Content sections use a 5:7 split (heading and lead left, rows right) or a full-width head followed by a gallery. Sections are separated by a hairline top border and clamp 80 to 136px of vertical padding; the closer gets more (clamp 96 to 160px).

Breakpoints: at 960px every split and the hero collapse to one column and the tray grid drops from 4 to 2 columns. At 720px nav links hide (wordmark and small 下载 remain), definition rows stack, hero buttons go full width, and the hero screenshot becomes a 5:8 portrait crop anchored on the panel so its text stays near native size.

**The Native Size Rule.** Product screenshots are shown at native size or larger (2x WebP sources), never shrunk until their text is unreadable. On narrow screens crop, don't scale down.

## Elevation & Depth

Depth is tonal first: near-black ground, a raised slate inside frames, and hairlines to separate. Real shadows are reserved for two things, product screenshots (which float like lit objects on the desk) and the primary button (a soft white glow, as if lit from within). Everything else is flat.

### Shadow Vocabulary
- **Frame float** (`box-shadow: 0 0 0 1px rgba(255,255,255,0.08), 0 30px 80px -20px rgba(0,0,0,0.8), 0 12px 32px -12px rgba(0,0,0,0.6)`): the hero screenshot frame.
- **Lit top edge** (1px masked border, `linear-gradient(180deg, rgba(255,255,255,0.24), rgba(255,255,255,0.04) 30%, transparent 60%)`): the frame's top edge catching light.
- **Card float** (`box-shadow: 0 0 0 1px rgba(255,255,255,0.08), 0 18px 40px -20px rgba(0,0,0,0.8)`): hover-card screenshots.
- **Outline only** (`box-shadow: 0 0 0 1px rgba(255,255,255,0.14)`): tray-icon tiles and the real screenshot.
- **Button glow** (`box-shadow: 0 1px 0 rgba(255,255,255,0.4) inset, 0 6px 20px -6px rgba(255,255,255,0.25)`; hover `0 1px 0 rgba(255,255,255,0.5) inset, 0 10px 28px -8px rgba(255,255,255,0.35)`): primary button only.

### Named Rules
**The Only Lit Object Rule.** Only product screenshots float and catch light. Text blocks, lists and sections never get shadows or raised panels.

## Shapes

Soft, small radii that echo Windows 11: 12px on screenshot frames, 10px on buttons and screenshot tiles, 8px on the small nav button and the real-screenshot crop, 4px on the focus ring, full pills only for status tags. Lists have no corners at all; they are open rows drawn with hairlines top and bottom. The wordmark is four rounded battery cells, the last one dimmed.

## Components

### Buttons
Solid, quiet, confident; at most one white button per group.
- **Shape:** gently rounded (10px), 44px tall, 15px / 600 text, optional 16px stroke SVG icon.
- **Primary:** Desk-Lamp White fill with Night Ground text and the button glow; hover brightens to Lit White and the glow widens.
- **Ghost:** transparent (2% white) with a strong hairline border; hover to 6% white and a 22% border.
- **Small:** 34px, 8px radius, white; the nav download.
- **Press:** all buttons drop 1px on `:active`. Transitions 0.2s on the shared ease-out curve.

### Chips (status tags)
- **Style:** 12.5px pill, strong-hairline border, Dim Graphite text (按资料适配).
- **Verified:** Verified Green text with a 35% green border and 7% green fill (已实机验证). Used only for real-device verification.

### Lists (hairline rows)
The signature content container in place of cards.
- **Definition rows:** 9em lead column (600) plus Soft Graphite description, 22px vertical padding, hairline above and below.
- **Device rows:** brand (11em, 600), models (Soft Graphite, 15px), right-aligned status tag; on mobile the models wrap to their own line.
- **Changelog rows:** 6px rounded-square marker in Dim Graphite, 18px padding.
- **FAQ:** native details/summary rows, 600 summary, a CSS-drawn chevron in Dim Graphite that rotates on open (0.3s).

### Screenshot Frame
- **Corner Style:** 12px, clipped.
- **Background:** Raised Slate.
- **Shadow Strategy:** frame float plus lit top edge (see Elevation).
- **Caption:** a 12.5px Dim Graphite figcaption stating provenance: 界面原型 / 演示数据 for prototype crops, 实机截图 for real captures.
- **Motion:** the hero frame alone settles in once: from `translateY(28px) scale(0.985)`, 6px blur, 40% opacity to rest over 1.2s on `cubic-bezier(0.16, 1, 0.3, 1)`, 0.15s delay, only under `prefers-reduced-motion: no-preference`.

### Navigation
- **Style:** sticky 60px bar, Night Ground at 86%; after 8px of scroll a hairline appears and opacity rises to 96%. No backdrop blur.
- **Content:** wordmark left, 14px Soft Graphite links that turn white on hover, small white 下载 at the right.
- **Mobile:** links hide below 720px.

### Links
Inline links inherit text color with a 30% white underline that goes solid on hover; standalone "more" links use Battery-Cell Blue with a 14px arrow that nudges 2px right on hover.

## Do's and Don'ts

### Do:
- **Do** keep the page dark-only on Night Ground with Raised Slate as the only surface step.
- **Do** separate content with 8% white hairlines and open rows; reach for a list before a card.
- **Do** use Battery-Cell Blue only for links, the focus ring and selection.
- **Do** show the product only as the real interface: crops of design/ui-claude.html rendered at 2x, or real Windows screenshots, each captioned 界面原型 / 演示数据 or 实机截图, each raster carrying a provenance sidecar JSON.
- **Do** frame product screenshots with the 12px frame, float shadow and lit top edge.
- **Do** use system CJK font stacks and tabular numerals.
- **Do** keep the hero settle as the single authored motion; state transitions stay at 0.2 to 0.3s and respect reduced motion.
- **Do** keep every script and style in self-hosted files so the strict CSP holds (`script-src 'self'`, `style-src 'self'`).

### Don't:
- **Don't** draw or mock up UI, icons-as-product or fake panels; if it isn't a crop of the prototype or a real screenshot, it doesn't ship.
- **Don't** use battery yellow, red or green in site chrome, badges or illustrations.
- **Don't** build equal icon-card rows or feature-tile grids.
- **Don't** load hosted fonts, CDNs, analytics, embeds or any third-party resource; visitors are in China.
- **Don't** write inline `<script>`, inline `style` attributes or `<style>` blocks.
- **Don't** add a second animated element, scroll-triggered reveals or parallax.
- **Don't** shrink screenshots below native size to fit a layout; crop instead.

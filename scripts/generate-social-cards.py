#!/usr/bin/env python3
"""生成官网 Open Graph / Twitter Card 大图（1200x630）与 GitHub Social Preview（1280x640）。

使用仓库真实素材（assets/jige.png、docs/images/win11-panel.png）与官网宣纸陶瓷 × 黑曜石视觉体系。
依赖：Python 3 + Pillow。
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]

# 官网视觉色板（与 site/styles.css 保持一致）
PAPER = (244, 241, 234)       # #f4f1ea
PAPER_CARD = (251, 249, 245)  # #fbf9f5
INK = (25, 23, 21)            # #191715
INK_2 = (93, 88, 80)          # #5d5850
INK_3 = (140, 134, 124)       # #8c867c
ACCENT = (212, 82, 38)        # #d45226
MONO_BG = (19, 19, 18)        # #131312
MONO_BAR = (25, 25, 23)       # #191917
LINE = (25, 23, 21, 30)
LINE_STRONG = (25, 23, 21, 52)


def load_font(size: int, bold: bool = False, mono: bool = False) -> ImageFont.FreeTypeFont:
    candidates: list[tuple[str, int]] = []
    if mono:
        candidates = [
            ("/System/Library/Fonts/SFNSMono.ttf", 0),
            ("/System/Library/Fonts/Menlo.ttc", 1 if bold else 0),
            ("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", 0),
        ]
    elif bold:
        candidates = [
            ("/System/Library/Fonts/Hiragino Sans GB.ttc", 2),
            ("/System/Library/Fonts/STHeiti Medium.ttc", 0),
            ("/System/Library/Fonts/PingFang.ttc", 0),
        ]
    else:
        candidates = [
            ("/System/Library/Fonts/Hiragino Sans GB.ttc", 0),
            ("/System/Library/Fonts/STHeiti Light.ttc", 0),
            ("/System/Library/Fonts/PingFang.ttc", 0),
        ]

    for path, index in candidates:
        if Path(path).exists():
            return ImageFont.truetype(path, size=size, index=index)
    raise FileNotFoundError("找不到可用的中文字体或等宽字体。")


def draw_background(width: int, height: int) -> Image.Image:
    # 2x 超采样绘制暖宣纸底色 + 柔和日光光晕 + 精密同心圆弧（呼应官网背景）
    scale = 2
    sw, sh = width * scale, height * scale
    canvas = Image.new("RGBA", (sw, sh), PAPER + (255,))

    # 暖白日光柔晕（右上）与极淡朱橙暖晕（左下）
    glow = Image.new("RGBA", (sw, sh), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse(
        (int(sw * 0.44), int(-sh * 0.36), int(sw * 1.15), int(sh * 0.72)),
        fill=(255, 255, 255, 180),
    )
    gd.ellipse(
        (int(-sw * 0.18), int(sh * 0.38), int(sw * 0.42), int(sh * 1.25)),
        fill=(212, 82, 38, 22),
    )
    glow = glow.filter(ImageFilter.GaussianBlur(radius=90 * scale))
    canvas = Image.alpha_composite(canvas, glow)

    # 同心机械表盘圆弧 + 刻度游标（避开底部文字与分割线）
    dial = Image.new("RGBA", (sw, sh), (0, 0, 0, 0))
    dd = ImageDraw.Draw(dial)
    cx, cy = int(sw * 0.46), int(sh * 0.24)
    radii = [int(sh * 0.44), int(sh * 0.68), int(sh * 0.96)]
    for i, r in enumerate(radii):
        bbox = (cx - r, cy - r, cx + r, cy + r)
        alpha = 20 if i == 1 else 13
        dd.arc(bbox, start=18 + i * 35, end=310 + i * 35, fill=(25, 23, 21, alpha), width=2)
        if i == 1:
            for k in range(28):
                ang = math.radians(53 + (k / 28) * 292)
                inner = r - (18 if k % 7 == 0 else 9)
                dd.line(
                    (
                        cx + math.cos(ang) * inner,
                        cy + math.sin(ang) * inner,
                        cx + math.cos(ang) * r,
                        cy + math.sin(ang) * r,
                    ),
                    fill=(25, 23, 21, 22),
                    width=2,
                )
            # 游标点放在中环右上方留白区，避免压线
            ma = math.radians(340)
            mx, my = cx + math.cos(ma) * r, cy + math.sin(ma) * r
            dd.ellipse((mx - 7, my - 7, mx + 7, my + 7), fill=ACCENT + (155,))

    canvas = Image.alpha_composite(canvas, dial)
    return canvas.resize((width, height), Image.Resampling.LANCZOS)


def round_mask(size: tuple[int, int], radius: int) -> Image.Image:
    scale = 2
    w, h = size[0] * scale, size[1] * scale
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), radius=radius * scale, fill=255)
    return m.resize(size, Image.Resampling.LANCZOS)


def build_showcase_panel(panel_path: Path, frame_w: int, frame_h: int) -> Image.Image:
    """将真实 Windows 11 面板截图装入深黑阳极氧化橱窗框，确保完整显示顶部标题栏到底部任务栏直读条。"""
    with Image.open(panel_path) as raw:
        src = raw.convert("RGB")

    bar_h = 34
    inner_w = frame_w
    inner_h = frame_h - bar_h

    # 按完整高度 1030 等比裁剪右侧区域，确保主面板顶部「几格」和底部任务栏直读条都 100% 完整无裁切
    sw, sh = src.size
    crop_w = min(sw, int(round(sh * (inner_w / inner_h))))
    crop_x0 = max(0, sw - crop_w)
    cropped = src.crop((crop_x0, 0, sw, sh))
    fitted = cropped.resize((inner_w, inner_h), Image.Resampling.LANCZOS)

    frame = Image.new("RGBA", (frame_w, frame_h), MONO_BG + (255,))
    draw = ImageDraw.Draw(frame)

    # 顶部硬件橱窗状态栏
    draw.rectangle((0, 0, frame_w, bar_h), fill=MONO_BAR + (255,))
    draw.line((0, bar_h - 1, frame_w, bar_h - 1), fill=(246, 243, 238, 26), width=1)
    draw.ellipse((16, 13, 24, 21), fill=ACCENT + (255,))
    f_bar = load_font(12, mono=True)
    draw.text((34, 10), "WINDOWS 10 & 11  /  TASKBAR STRIP", font=f_bar, fill=(165, 160, 150, 255))

    frame.paste(fitted, (0, bar_h))

    mask = round_mask((frame_w, frame_h), radius=18)
    out = Image.new("RGBA", (frame_w, frame_h), (0, 0, 0, 0))
    out.paste(frame, (0, 0), mask)

    border = Image.new("RGBA", (frame_w, frame_h), (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle(
        (0, 0, frame_w - 1, frame_h - 1),
        radius=18,
        outline=(255, 255, 255, 38),
        width=1,
    )
    return Image.alpha_composite(out, border)


def draw_pill(
    overlay_draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    text: str,
    font: ImageFont.FreeTypeFont,
    dark: bool = False,
) -> int:
    bbox = overlay_draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pad_x, pad_y = 14, 7
    w = tw + pad_x * 2
    h = th + pad_y * 2 + 2
    fill = INK + (255,) if dark else PAPER_CARD + (242,)
    outline = INK + (255,) if dark else LINE_STRONG
    fg = PAPER_CARD + (255,) if dark else INK_2 + (255,)
    overlay_draw.rounded_rectangle((x, y, x + w, y + h), radius=9, fill=fill, outline=outline, width=1)
    overlay_draw.text((x + pad_x, y + pad_y - bbox[1] + 1), text, font=font, fill=fg)
    return w


def render_card(
    output_path: Path,
    width: int,
    height: int,
    kicker: str,
    subtitle_cn: str,
    subtitle_en: str | None,
    pills: list[tuple[str, bool]],
    footer_url: str,
) -> None:
    img = draw_background(width, height)
    # 使用独立 RGBA 透明层绘制文字、细线与胶囊，确保半透明描边精确抗锯齿混合
    ui = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(ui)

    margin_x = 64 if width >= 1260 else 58
    top_y = 52 if height >= 640 else 48

    # 右侧真实面板橱窗尺寸（比例贴近面板，保证主面板饱满、左侧文字呼吸感充足）
    panel_w = 456 if width >= 1260 else 438
    panel_h = height - top_y * 2
    panel_x = width - margin_x - panel_w
    panel_y = top_y

    # 1. 品牌图标 + 名称 + Kicker
    icon_size = 52
    with Image.open(ROOT / "assets" / "jige.png") as raw_icon:
        icon = raw_icon.convert("RGBA").resize((icon_size, icon_size), Image.Resampling.LANCZOS)
    img.alpha_composite(icon, (margin_x, top_y))

    f_brand = load_font(22, bold=True)
    f_kicker = load_font(13, mono=True)
    draw.text((margin_x + icon_size + 16, top_y + 4), "几格 · Jige Battery", font=f_brand, fill=INK + (255,))
    ky = top_y + 34
    draw.ellipse((margin_x + icon_size + 16, ky + 4, margin_x + icon_size + 23, ky + 11), fill=ACCENT + (255,))
    draw.text((margin_x + icon_size + 31, ky), kicker, font=f_kicker, fill=INK_2 + (255,))

    # 2. 主标题（杂志编辑感大字 + 朱橙句号）
    f_h1 = load_font(58 if width >= 1260 else 56, bold=True)
    h1_y = top_y + 92
    line1 = "不开厂商驱动，"
    line2 = "也能看电量"
    draw.text((margin_x, h1_y), line1, font=f_h1, fill=INK + (255,))
    l2_y = h1_y + 74
    draw.text((margin_x, l2_y), line2, font=f_h1, fill=INK + (255,))
    l2_bbox = draw.textbbox((margin_x, l2_y), line2, font=f_h1)
    draw.text((l2_bbox[2] + 2, l2_y), "。", font=f_h1, fill=ACCENT + (255,))

    # 3. 副标题（中文 + 英文一行）
    f_sub = load_font(21, bold=False)
    sub_y = l2_y + 88
    draw.text((margin_x, sub_y), subtitle_cn, font=f_sub, fill=INK_2 + (255,))

    if subtitle_en:
        f_en = load_font(15, bold=False)
        draw.text((margin_x, sub_y + 38), subtitle_en, font=f_en, fill=INK_3 + (255,))

    # 4. 卖点标签行
    f_pill = load_font(15, bold=True)
    px = margin_x
    pill_y = height - top_y - 96
    for text, is_dark in pills:
        w = draw_pill(draw, px, pill_y, text, f_pill, dark=is_dark)
        px += w + 10

    # 5. 底部细分割线 + 网址印记
    foot_y = height - top_y - 32
    draw.line((margin_x, foot_y - 16, panel_x - 36, foot_y - 16), fill=LINE, width=1)
    f_url = load_font(15, mono=True)
    draw.text((margin_x, foot_y), footer_url, font=f_url, fill=INK_3 + (255,))

    img = Image.alpha_composite(img, ui)

    # 6. 右侧真实 Windows 11 面板与任务栏直读条展示橱窗（带柔和硬件投影）
    panel = build_showcase_panel(ROOT / "docs" / "images" / "win11-panel.png", panel_w, panel_h)
    shadow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        (panel_x + 4, panel_y + 18, panel_x + panel_w + 4, panel_y + panel_h + 18),
        radius=20,
        fill=(25, 22, 18, 84),
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(radius=24))
    img = Image.alpha_composite(img, shadow)
    img.alpha_composite(panel, (panel_x, panel_y))

    # 极细外框线收口
    edge = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rectangle((0, 0, width - 1, height - 1), outline=(25, 23, 21, 20), width=1)
    img = Image.alpha_composite(img, edge)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").save(output_path, format="PNG", optimize=True)
    print(f"Generated {output_path.relative_to(ROOT)} ({width}x{height})")


def main() -> None:
    # 1) 官网 Open Graph / Twitter Card 大图（1200x630）
    render_card(
        output_path=ROOT / "site" / "assets" / "og-card.png",
        width=1200,
        height=630,
        kicker="WINDOWS 10 & 11  /  MIT OPEN SOURCE",
        subtitle_cn="无线鼠标、键盘和耳机电量，安静放回任务栏。",
        subtitle_en="Razer · Logitech · G-Wolves · ATK · ROG · VXE · INZONE & Bluetooth",
        pills=[
            ("约 5 MB 原生编译", True),
            ("纯只读不改设置", False),
            ("全屏游戏零打扰", False),
            ("免费开源", False),
        ],
        footer_url="jige.imfourj.cn",
    )

    # 2) GitHub Repository Social Preview（1280x640）
    render_card(
        output_path=ROOT / "docs" / "images" / "social-preview.png",
        width=1280,
        height=640,
        kicker="OPEN SOURCE  /  C# .NET 10 NATIVEAOT",
        subtitle_cn="无线外设电量直显 Windows 任务栏，无需厂商驱动。",
        subtitle_en="Read-only taskbar battery monitor for wireless mice, keyboards & headsets.",
        pills=[
            ("MIT License", True),
            ("Win10 / Win11", False),
            ("~5 MB Single App", False),
            ("Read-Only", False),
        ],
        footer_url="github.com/deepfry666/jige-battery  ·  jige.imfourj.cn",
    )


if __name__ == "__main__":
    main()

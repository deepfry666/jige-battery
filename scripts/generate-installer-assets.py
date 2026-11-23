#!/usr/bin/env python3
"""生成 Inno Setup 现代浅色/深色向导位图（多 DPI PNG）。需要 Node.js、npx 和 Pillow。"""

from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

from PIL import Image


def resolve_npx() -> str:
    npx = shutil.which("npx.cmd") or shutil.which("npx")
    if not npx:
        raise RuntimeError("未找到 npx，请先安装 Node.js")
    return npx


def extract_mark_path(svg_path: Path) -> str:
    tree = ET.parse(svg_path)
    for elem in tree.getroot().iter():
        if elem.tag.endswith("path") and elem.attrib.get("d"):
            return elem.attrib["d"]
    raise RuntimeError(f"未能从 {svg_path} 提取标志路径")


ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "assets" / "installer"
MARK_PATH = extract_mark_path(ROOT / "design" / "brand" / "fourj-mark.svg")


def sidebar_svg(dark: bool) -> str:
    if dark:
        bg_top = "#242321"
        bg_bottom = "#171615"
        edge = "#33302c"
        glow = "#ffffff"
        glow_opacity = "0.045"
        grid_color = "#ffffff"
        grid_opacity = "0.04"
        tile_fill = "#2d2a26"
        tile_stroke = "#ffffff"
        tile_stroke_opacity = "0.14"
        tile_shadow = "#000000"
        tile_shadow_opacity = "0.42"
        mark_fill = "#f3efe8"
    else:
        bg_top = "#f7f4ef"
        bg_bottom = "#ece7de"
        edge = "#ddd7cc"
        glow = "#ffffff"
        glow_opacity = "0.55"
        grid_color = "#211d19"
        grid_opacity = "0.045"
        tile_fill = "#fcfbf8"
        tile_stroke = "#211d19"
        tile_stroke_opacity = "0.11"
        tile_shadow = "#211d19"
        tile_shadow_opacity = "0.10"
        mark_fill = "#211d19"

    # Subtle architectural dot pattern in the lower portion
    dots = []
    for row in range(6):
        y = 224 + row * 12
        for col in range(9):
            x = 22 + col * 15
            fade = max(0.25, 1.0 - abs(col - 4) * 0.16) * (0.35 + row * 0.12)
            dots.append(
                f'<circle cx="{x}" cy="{y}" r="1.0" fill="{grid_color}" '
                f'fill-opacity="{float(grid_opacity) * fade:.4f}"/>'
            )
    dots_markup = "\n    ".join(dots)

    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="656" height="1256" viewBox="0 0 164 314">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="{bg_top}"/>
      <stop offset="100%" stop-color="{bg_bottom}"/>
    </linearGradient>
    <radialGradient id="glow" cx="50%" cy="42%" r="55%">
      <stop offset="0%" stop-color="{glow}" stop-opacity="{glow_opacity}"/>
      <stop offset="100%" stop-color="{glow}" stop-opacity="0"/>
    </radialGradient>
    <filter id="cardShadow" x="-25%" y="-25%" width="150%" height="160%">
      <feDropShadow dx="0" dy="6" stdDeviation="8" flood-color="{tile_shadow}" flood-opacity="{tile_shadow_opacity}"/>
    </filter>
  </defs>

  <!-- Base background -->
  <rect width="164" height="314" fill="url(#bg)"/>
  <rect width="164" height="314" fill="url(#glow)"/>

  <!-- Subtle architectural dot field -->
  <g>
    {dots_markup}
  </g>

  <!-- App Icon Tile (optically centered) -->
  <g filter="url(#cardShadow)">
    <rect x="38" y="96" width="88" height="88" rx="22" fill="{tile_fill}"/>
    <rect x="38.5" y="96.5" width="87" height="87" rx="21.5" fill="none" stroke="{tile_stroke}" stroke-opacity="{tile_stroke_opacity}"/>
  </g>
  <g transform="translate(49.0 110.2) scale(0.0822)">
    <path d="{MARK_PATH}" fill="{mark_fill}" fill-rule="evenodd"/>
  </g>

  <!-- Right hairline divider -->
  <rect x="163" y="0" width="1" height="314" fill="{edge}"/>
</svg>
"""


def small_icon_svg(dark: bool) -> str:
    if dark:
        tile_fill = "#2d2a26"
        tile_stroke = "#ffffff"
        tile_stroke_opacity = "0.16"
        mark_fill = "#f3efe8"
    else:
        tile_fill = "#f6f3ee"
        tile_stroke = "#211d19"
        tile_stroke_opacity = "0.12"
        mark_fill = "#211d19"

    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="232" height="232" viewBox="0 0 116 116">
  <rect x="8" y="8" width="100" height="100" rx="24" fill="{tile_fill}"/>
  <rect x="8.6" y="8.6" width="98.8" height="98.8" rx="23.4" fill="none" stroke="{tile_stroke}" stroke-opacity="{tile_stroke_opacity}" stroke-width="1.2"/>
  <g transform="translate(20.5 23.5) scale(0.0935)">
    <path d="{MARK_PATH}" fill="{mark_fill}" fill-rule="evenodd"/>
  </g>
</svg>
"""


def render_svg_variants(svg_text: str, targets: list[tuple[str, int, int]]) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        tmp_path = Path(tmp)
        svg_file = tmp_path / "source.svg"
        svg_file.write_text(svg_text, encoding="utf-8")

        hi_png = tmp_path / "hi.png"
        subprocess.run(
            [
                resolve_npx(), "-y", "@resvg/resvg-js-cli@2.6.2-beta.1",
                str(svg_file),
                str(hi_png),
            ],
            check=True,
        )
        with Image.open(hi_png) as img:
            rgba = img.convert("RGBA")
            for filename, width, height in targets:
                resized = rgba.resize((width, height), Image.Resampling.LANCZOS)
                out_file = OUT_DIR / filename
                tmp_file = out_file.with_suffix(".png.tmp")
                resized.save(tmp_file, format="PNG", optimize=True)
                os.replace(tmp_file, out_file)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    sidebar_sizes = [
        ("100", 164, 314),
        ("150", 246, 471),
        ("200", 328, 628),
    ]
    small_sizes = [
        ("100", 58, 58),
        ("150", 87, 87),
        ("200", 116, 116),
    ]

    for theme, dark in [("light", False), ("dark", True)]:
        render_svg_variants(
            sidebar_svg(dark),
            [(f"wizard-{theme}-{dpi}.png", w, h) for dpi, w, h in sidebar_sizes],
        )
        render_svg_variants(
            small_icon_svg(dark),
            [(f"wizard-small-{theme}-{dpi}.png", w, h) for dpi, w, h in small_sizes],
        )

    print(f"Generated installer assets in {OUT_DIR}")


if __name__ == "__main__":
    main()

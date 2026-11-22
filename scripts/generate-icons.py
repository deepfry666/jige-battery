#!/usr/bin/env python3
"""从品牌 SVG 重新生成 Windows 图标和面板位图。需要 Node.js、npx 和 Pillow。"""

from pathlib import Path
import os
import shutil
import subprocess
import tempfile

from PIL import Image


def resolve_npx() -> str:
    npx = shutil.which("npx.cmd") or shutil.which("npx")
    if not npx:
        raise RuntimeError("未找到 npx，请先安装 Node.js")
    return npx


def atomic_write(target: Path, data: bytes) -> None:
    tmp = target.with_suffix(target.suffix + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, target)


root = Path(__file__).resolve().parents[1]
assets = root / "assets"
sizes = [(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 256)]

assets.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory() as temporary:
    tmp_dir = Path(temporary)
    rendered = tmp_dir / "app-icon.png"
    subprocess.run(
        [
            resolve_npx(), "-y", "@resvg/resvg-js-cli@2.6.2-beta.1",
            str(root / "design/brand/app-icon.svg"), str(rendered),
        ],
        check=True,
    )
    with Image.open(rendered) as image:
        icon = image.convert("RGBA")

    png_tmp = assets / "jige.png.tmp"
    icon.save(png_tmp, format="PNG")
    os.replace(png_tmp, assets / "jige.png")

    ico_tmp = assets / "jige.ico.tmp"
    icon.save(ico_tmp, format="ICO", sizes=sizes)
    os.replace(ico_tmp, assets / "jige.ico")

    # Direct2D 使用预乘 Alpha 的 BGRA 像素。复用同一图标，确保浅色和深色面板中都可辨识。
    panel = icon.resize((64, 64), Image.Resampling.LANCZOS).convert("RGBa")
    raw = panel.tobytes("raw", "BGRa")
    if len(raw) != 64 * 64 * 4:
        raise RuntimeError(f"面板位图字节长度异常：{len(raw)}（期望 16384）")
    atomic_write(assets / "jige-panel.bgra", raw)

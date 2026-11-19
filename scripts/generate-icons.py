#!/usr/bin/env python3
"""从品牌 SVG 重新生成 Windows 图标和面板位图。需要 Node.js、npx 和 Pillow。"""

from pathlib import Path
import subprocess
import tempfile

from PIL import Image


root = Path(__file__).resolve().parents[1]
assets = root / "assets"
sizes = [(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 256)]

assets.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory() as temporary:
    rendered = Path(temporary) / "app-icon.png"
    subprocess.run(
        [
            "npx", "-y", "@resvg/resvg-js-cli@2.6.2-beta.1",
            str(root / "design/brand/app-icon.svg"), str(rendered),
        ],
        check=True,
    )
    with Image.open(rendered) as image:
        icon = image.convert("RGBA")

icon.save(assets / "jige.png")
icon.save(assets / "jige.ico", sizes=sizes)

# Direct2D 使用预乘 Alpha 的 BGRA 像素。复用同一图标，确保浅色和深色面板中都可辨识。
panel = icon.resize((64, 64), Image.Resampling.LANCZOS).convert("RGBa")
(assets / "jige-panel.bgra").write_bytes(panel.tobytes("raw", "BGRa"))

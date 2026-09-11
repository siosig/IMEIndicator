#!/usr/bin/env python
"""013-ime-corner-image: Gemini で生成した背景画像を 512x512 の角丸透過 PNG に後処理する。

生成 AI の出力は不透明前提のため、バッジがキャンバス全体を占める構図で生成し、
本スクリプトで四隅だけを透過させる（クロマキー除去は行わない。research.md R-8）。

処理:
  1. RGBA 化
  2. 中央正方形にクロップ
  3. 512x512 に LANCZOS リサイズ
  4. 角丸（半径 96px）の L マスクを既存アルファと乗算して四隅を透過
  5. PNG（optimize）で保存

実行例（PowerShell）:
  python tools\\postprocess_background_image.py <生成PNG> src\\cpp\\resources\\ime-on-background.png [半径px]

半径（省略時 96）は「生成画像側の角丸半径（512px 換算）より数 px 大きい値」にする。
マスクが生成画像の角丸の内側で切れれば、元画像の角の外側（白）とアンチエイリアスの縁が残らない。

要件: Windows 側 Python 3 + Pillow（未導入なら `python -m pip install pillow`）。
"""

from __future__ import annotations

import sys
from pathlib import Path

SIZE = 512
RADIUS = 96


def main(argv: list[str]) -> int:
    if len(argv) not in (3, 4):
        print("usage: postprocess_background_image.py <input.png> <output.png> [radius]", file=sys.stderr)
        return 2

    src = Path(argv[1])
    dst = Path(argv[2])
    radius = RADIUS
    if len(argv) == 4:
        try:
            radius = int(argv[3])
        except ValueError:
            print(f"error: radius must be an integer: {argv[3]}", file=sys.stderr)
            return 2
        if not 0 <= radius <= SIZE // 2:
            print(f"error: radius out of range 0..{SIZE // 2}: {radius}", file=sys.stderr)
            return 2
    if not src.is_file():
        print(f"error: input not found: {src}", file=sys.stderr)
        return 2

    try:
        from PIL import Image, ImageChops, ImageDraw
    except ImportError:
        print("error: Pillow が必要です: python -m pip install pillow", file=sys.stderr)
        return 3

    image = Image.open(src).convert("RGBA")
    width, height = image.size
    side = min(width, height)
    left = (width - side) // 2
    top = (height - side) // 2
    image = image.crop((left, top, left + side, top + side)).resize((SIZE, SIZE), Image.LANCZOS)

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=radius, fill=255)
    image.putalpha(ImageChops.multiply(image.getchannel("A"), mask))

    dst.parent.mkdir(parents=True, exist_ok=True)
    image.save(dst, "PNG", optimize=True)

    corner_alpha = image.getpixel((0, 0))[3]
    center_alpha = image.getpixel((SIZE // 2, SIZE // 2))[3]
    print(f"wrote {dst} ({SIZE}x{SIZE}, radius={radius}, corner alpha={corner_alpha}, center alpha={center_alpha})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

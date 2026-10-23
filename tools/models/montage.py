# -*- coding: utf-8 -*-
"""把 Blender 渲的四视角拼成一张（用系统 Python 的 Pillow 跑，别在 Blender 里跑）。

用法：
    python tools/models/montage.py nailong
"""
import os
import sys

from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MODELS = os.path.join(REPO, ".workbuddy-ai", "models")
VIEWS = ["front", "q34", "side", "back"]


def main():
    ch = sys.argv[1]
    paths = [os.path.join(MODELS, f"views_{ch}_{v}.png") for v in VIEWS]
    paths = [p for p in paths if os.path.isfile(p)]
    if not paths:
        raise SystemExit(f"找不到 {ch} 的视角图")
    ims = [Image.open(p).convert("RGB") for p in paths]
    w, h = sum(i.width for i in ims), max(i.height for i in ims)
    canvas = Image.new("RGB", (w, h), (45, 45, 48))
    x = 0
    d = ImageDraw.Draw(canvas)
    for im, lab in zip(ims, [os.path.basename(p)[:-4].split("_")[-1] for p in paths]):
        canvas.paste(im, (x, 0))
        d.rectangle([x, 0, x + 78, 20], fill=(30, 30, 32))
        d.text((x + 6, 5), lab, fill=(255, 226, 80))
        x += im.width
    dst = os.path.join(MODELS, f"views_{ch}.png")
    canvas.save(dst)
    print(dst, canvas.size)


if __name__ == "__main__":
    main()

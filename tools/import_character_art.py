# -*- coding: utf-8 -*-
"""把外部的角色立绘处理成项目规范格式，放进 Assets/Art/Characters/。

处理三件事：
  1. alpha 归一化 —— 有些图导出时整体透明度被压到 252/253，贴到暗背景上会透
  2. 裁掉四周全透明的空白边 —— 这样"按高度铺满"时各状态的角色大小一致
  3. 按 docs/03-素材规格.md 的命名规则落盘

用法：python tools/import_character_art.py
换素材时改下面的 JOBS 再重跑即可，幂等。
"""

import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Art", "Characters")

# (源图, 目标名不带扩展名)
JOBS = [
    (r"D:/QQmm/retouch_2026101220575682.png", "char_nailong_jumpscare"),
    (r"D:/QQmm/retouch_2026101220523820.png", "char_nailong_blackout"),
    (r"D:/QQmm/com.ss.android.ugc.aweme_20261012204701_edit_3695.png", "char_nailong_idle"),
    (r"D:/QQmm/retouch_2026101220485848.png", "char_nailong_stare"),

    # 噜噜（水豚）—— 2026-10-12 绿子提供
    (r"D:/QQmm/retouch_2026101222275753.png", "char_lulu_idle"),
    (r"D:/QQmm/com.ss.android.ugc.aweme_20261012222321_edit_3750.png", "char_lulu_stare"),
    (r"D:/QQmm/retouch_2026101222295201.png", "char_lulu_peek"),
]


def normalize_alpha(image):
    """把 alpha 峰值拉到 255。峰值已经是 255 就原样返回。"""
    arr = np.asarray(image).astype(np.float32)
    alpha = arr[:, :, 3]
    peak = float(alpha.max())

    if peak <= 0.0 or peak >= 255.0:
        return image

    arr[:, :, 3] = np.clip(alpha * (255.0 / peak), 0.0, 255.0)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def trim_transparent(image, threshold=8):
    """裁到不透明内容的边界。"""
    alpha = np.asarray(image)[:, :, 3]
    ys, xs = np.where(alpha > threshold)

    if ys.size == 0:
        return image

    return image.crop((int(xs.min()), int(ys.min()),
                       int(xs.max()) + 1, int(ys.max()) + 1))


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    for src, name in JOBS:
        if not os.path.isfile(src):
            print("跳过（找不到源文件）:", src)
            continue

        original = Image.open(src).convert("RGBA")
        image = trim_transparent(normalize_alpha(original))

        path = os.path.join(OUT_DIR, name + ".png")
        image.save(path)

        print("%-28s %s -> %s" % (
            name, "%dx%d" % original.size, "%dx%d" % image.size))


if __name__ == "__main__":
    main()

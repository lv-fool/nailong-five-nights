#!/usr/bin/env python3
"""生成占位背景图（开发阶段用）。

绿子画好真素材后，同名覆盖 Assets/Art/Backgrounds/ 即可，代码不用改。
注意：背景图**不含门片** —— 门是单独的叠层，见 docs/03-素材规格.md。

运行：
    C:/Users/DELL/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe tools/gen_placeholder_art.py
"""

import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1920, 1080
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Art", "Backgrounds")
DOOR_OUT = os.path.join(ROOT, "Assets", "Art", "UI")


def radial(w, h, cx, cy, rx, ry, power=2.0):
    """径向权重：中心 1，边缘 0。"""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    nx = (xs - cx) / max(1.0, rx)
    ny = (ys - cy) / max(1.0, ry)
    d = np.sqrt(nx * nx + ny * ny)
    return np.clip(1.0 - d, 0.0, 1.0) ** power


def vignette(w, h, strength=0.9, power=2.4):
    """暗角：中间 1，四角 1-strength。"""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    nx = (xs / w - 0.5) * 2.0
    ny = (ys / h - 0.5) * 2.0
    d = np.sqrt(nx * nx + ny * ny) / 1.41421356
    return np.clip(1.0 - strength * (d ** power), 0.0, 1.0)


def draw_desk(draw):
    """正前方视角的办公桌（占位用的简单透视块）。"""
    draw.polygon([(90, 1080), (1830, 1080), (1520, 812), (400, 812)], fill=(31, 32, 40))
    draw.line([(400, 812), (1520, 812)], fill=(78, 81, 95), width=5)
    draw.line([(432, 828), (1488, 828)], fill=(46, 48, 58), width=3)


def build_room(seed, opening, glow_center, glow_radius, glow_color,
               wall, floor, ceil, accent, desk=False):
    rng = np.random.default_rng(seed)
    img = Image.new("RGB", (W, H), wall)
    draw = ImageDraw.Draw(img)

    # 天花板 / 地板
    draw.polygon([(0, 0), (W, 0), (W, int(H * 0.20)), (0, int(H * 0.24))], fill=ceil)
    draw.polygon([(0, H), (W, H), (W, int(H * 0.70)), (0, int(H * 0.66))], fill=floor)

    # 地板透视线，往消失点收敛
    vanish_x, vanish_y = W * 0.5, H * 0.68
    for i in range(1, 10):
        x = int(W * i / 10)
        draw.line([(x, H), (int(vanish_x + (x - vanish_x) * 0.28), int(vanish_y))],
                  fill=accent, width=2)

    # 墙脚线与墙顶线
    draw.line([(0, int(H * 0.66)), (W, int(H * 0.70))], fill=accent, width=3)
    draw.line([(0, int(H * 0.24)), (W, int(H * 0.20))], fill=accent, width=3)

    # 门洞 —— 只留一个暗开口，门片由叠层负责
    x0, y0, x1, y1 = opening
    draw.rectangle([x0, y0, x1, y1], fill=(6, 7, 9))
    draw.rectangle([x0, y0, x1, y1], outline=(58, 61, 72), width=7)

    if desk:
        draw_desk(draw)

    img = img.filter(ImageFilter.GaussianBlur(1.4))
    arr = np.asarray(img).astype(np.float32)

    # 光晕
    glow = radial(W, H, glow_center[0], glow_center[1], glow_radius, glow_radius, 2.2)
    for channel in range(3):
        arr[:, :, channel] += glow * glow_color[channel]

    # 门洞保持暗 —— 门是关着的，光只在门框周围
    arr[y0:y1, x0:x1] *= 0.42

    # 噪点（恐怖氛围的关键之一）
    arr += rng.normal(0.0, 7.0, (H, W, 1)).astype(np.float32)

    # 暗角
    arr *= vignette(W, H, 0.90, 2.4)[:, :, None]

    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB")


def build_door(opening, closed):
    """叠层门片。全屏透明画布，只在门洞位置画内容，这样代码里直接整屏对齐。"""
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    if not closed:
        return img

    x0, y0, x1, y1 = opening
    draw = ImageDraw.Draw(img)

    # 门体
    draw.rectangle([x0, y0, x1, y1], fill=(56, 58, 66, 255))

    # 卷帘横条
    step = 24
    for y in range(y0 + 4, y1, step):
        draw.line([(x0, y), (x1, y)], fill=(42, 44, 52, 255), width=5)
        draw.line([(x0, y + 5), (x1, y + 5)], fill=(74, 77, 87, 255), width=2)

    # 左右导轨
    draw.rectangle([x0 - 10, y0 - 10, x0 + 12, y1 + 10], fill=(36, 38, 45, 255))
    draw.rectangle([x1 - 12, y0 - 10, x1 + 10, y1 + 10], fill=(36, 38, 45, 255))

    # 门底沿
    draw.rectangle([x0, y1 - 12, x1, y1], fill=(28, 30, 36, 255))

    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(DOOR_OUT, exist_ok=True)

    # 文件名, seed, 门洞(左,上,右,下), 光心, 光半径, 光色, 墙色, 地色, 顶色, 线条色, 是否有桌子
    jobs = [
        ("bg_office_front.png", 11,
         (760, 250, 1160, 760), (960, 480), 660, (32, 35, 45),
         (20, 22, 29), (37, 38, 46), (12, 13, 18), (52, 55, 66), True),
        ("bg_office_left_01.png", 22,
         (150, 300, 560, 830), (420, 520), 580, (28, 33, 45),
         (18, 20, 27), (34, 36, 45), (11, 12, 17), (48, 52, 64), False),
        ("bg_office_right_01.png", 33,
         (1360, 300, 1770, 830), (1500, 520), 580, (30, 32, 41),
         (18, 20, 27), (34, 36, 45), (11, 12, 17), (48, 52, 64), False),
    ]

    for name, seed, opening, center, radius, glow, wall, floor, ceil, accent, desk in jobs:
        image = build_room(seed, opening, center, radius, glow, wall, floor, ceil, accent, desk)
        path = os.path.join(OUT, name)
        image.save(path)
        print("wrote", os.path.relpath(path, ROOT))

    # 门片 —— 位置要对齐上面左右两张背景的门洞
    door_jobs = [
        ("ui_door_left_closed.png", (150, 300, 560, 830), True),
        ("ui_door_left_open.png", (150, 300, 560, 830), False),
        ("ui_door_right_closed.png", (1360, 300, 1770, 830), True),
        ("ui_door_right_open.png", (1360, 300, 1770, 830), False),
    ]

    for name, opening, closed in door_jobs:
        path = os.path.join(DOOR_OUT, name)
        build_door(opening, closed).save(path)
        print("wrote", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()

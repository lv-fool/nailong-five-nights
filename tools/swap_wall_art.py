#!/usr/bin/env python3
"""把千问 AI 的办公室背景里、后墙/侧墙上的海报和儿童画，换成我们之前程序渲染的
（用我们自己角色的）版本。

做法：复用 gen_placeholder_art.py 里的「海报 / 儿童画」生成函数得到干净的画（带砖底 /
纸底，已经是我们自己的角色），再用同一套相机参数把每张画按它在墙上的 3D 位置
透视投到屏幕上，贴回千问的图里对应的位置。

因为程序版和千问版是同一个 FNAF 1 办公室、同一个机位，墙和画在屏幕上的位置基本
重合，所以直接按程序算出来的位置贴回去就行。绿子看了若觉得某张画位置偏了，
调下面 DECALS 里的 u/v（或脚本里的偏移）即可。

运行：
    C:/Users/DELL/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe \
        tools/swap_wall_art.py
"""

import math
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, os.path.join(ROOT, "tools"))
import gen_placeholder_art as G  # noqa: E402

OUT = G.OUT
W, H = G.W, G.H


def to_pil(arr):
    return Image.fromarray((np.clip(arr, 0.0, 1.0) * 255).astype(np.uint8), "RGB")


def plane_of(axis, side_max):
    if axis == 2:
        return G.ROOM_MAX[2] if side_max else G.ROOM_MIN[2]
    if axis == 0:
        return G.ROOM_MIN[0] if not side_max else G.ROOM_MAX[0]
    return G.ROOM_MIN[1] if not side_max else G.ROOM_MAX[1]


def world_pt(axis, plane, u, v):
    if axis == 0:
        return (plane, v, u)
    if axis == 1:
        return (u, plane, v)
    return (u, v, plane)


def perspective_coeffs(src_pts, dst_pts):
    """解出 dst->src 的 8 个透视系数（PIL 的 Image.PERSPECTIVE 要的就是这个）。"""
    A = []
    B = []
    for (x, y), (xp, yp) in zip(dst_pts, src_pts):
        A.append([x, y, 1, 0, 0, 0, -x * xp, -y * xp])
        B.append(xp)
        A.append([0, 0, 0, x, y, 1, -x * yp, -y * yp])
        B.append(yp)
    sol, _, _, _ = np.linalg.lstsq(np.array(A, np.float64), np.array(B, np.float64),
                                   rcond=None)
    return [float(v) for v in sol]


def build_sprites(tex):
    sprites = [G.load_character(code) for code in G.CAST]
    celebrate = to_pil(G.make_celebrate_poster(tex["paper"], sprites))
    have = [s for s in sprites if s is not None]
    draws = []
    for i, seed in enumerate((3, 5, 7, 11, 13, 17, 19, 23)):
        draws.append(to_pil(G.make_drawing(tex["paper"], seed,
                                            have[i % len(have)] if have else None)))
    return celebrate, draws


def main():
    print("生成我们自己的海报 / 儿童画 …")
    tex = G.load_textures()
    celebrate, draws = build_sprites(tex)

    # (axis, side_max, u0, v0, u1, v1, sprite) —— 抄自 gen_placeholder_art.build_decals
    DECALS = [
        (2, True, -2.54, 1.02, -1.37, 2.48, celebrate),
        (2, True, -2.54, 0.62, -2.08, 1.08, draws[0]),
        (2, True, -2.00, 0.66, -1.54, 1.12, draws[1]),
        (2, True, -0.32, 2.24, 0.04, 2.60, draws[2]),
        (2, True, 1.12, 1.70, 1.62, 2.20, draws[3]),
        (2, True, 1.68, 1.58, 2.18, 2.08, draws[4]),
        (2, True, 1.14, 1.14, 1.64, 1.64, draws[5]),
        (2, True, 1.70, 1.06, 2.20, 1.56, draws[6]),
        (2, True, 2.24, 1.46, 2.58, 1.80, draws[7]),
        (0, False, -1.12, 1.30, -0.56, 1.86, draws[0]),
        (0, False, -0.48, 1.46, -0.02, 1.92, draws[2]),
        (0, True, -1.12, 1.30, -0.56, 1.86, draws[1]),
        (0, True, -0.48, 1.46, -0.02, 1.92, draws[3]),
    ]

    for name, yaw, _seed in G.VIEWS:
        src_path = os.path.join(OUT, "bg_office_%s.png" % name)
        if not os.path.exists(src_path):
            print("  ! 跳过（没有 %s）" % name)
            continue
        base = Image.open(src_path).convert("RGB")
        printed = False

        for axis, side_max, u0, v0, u1, v1, sprite in DECALS:
            plane = plane_of(axis, side_max)
            # 画的四个角：(u,v) -> 屏幕；sprite 角对应 (du,dv)
            # du=(u-u0)/(u1-u0), dv=(v1-v)/(v1-v0)：dv=0 在 v=v1(顶)，
            # dv=1 在 v=v0(底)
            corners = [
                ((u0, v1), (0, 0)),
                ((u1, v1), (sprite.width, 0)),
                ((u0, v0), (0, sprite.height)),
                ((u1, v0), (sprite.width, sprite.height)),
            ]
            dst = []
            valid = True
            for (u, v), _s in corners:
                wp = world_pt(axis, plane, u, v)
                sx, sy = G.project(wp, G.CAM, yaw)
                if G.depth_of(wp, G.CAM, yaw) <= 0.05:
                    valid = False
                    break
                dst.append((sx, sy))
            if not valid:
                continue
            src_pts = [c[1] for c in corners]

            coeffs = perspective_coeffs(src_pts, dst)
            warped = sprite.transform((W, H), Image.PERSPECTIVE, coeffs,
                                      Image.BICUBIC)
            white = Image.new("L", sprite.size, 255)
            wmask = white.transform((W, H), Image.PERSPECTIVE, coeffs, Image.BICUBIC)
            wmask = wmask.point(lambda p: 255 if p > 128 else 0)

            base.paste(warped, (0, 0), wmask)

            if not printed:
                print("  贴 %s 的画 …" % name)
                printed = True

        base.save(src_path)
        print("  wrote", os.path.relpath(src_path, ROOT))

    print("完成。千问的办公室保留，墙上的画换成我们自己的角色了。")


if __name__ == "__main__":
    main()

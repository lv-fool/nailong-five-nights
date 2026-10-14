#!/usr/bin/env python3
"""渲染办公室的**圆柱全景条**，给「真正的镜头转动」当背景。

为什么不是一堆视角图交叉淡化：转头时的观感取决于画面里有多少**真实的角度**
变化。视角图方案的 `left_01..09` 是第 10 帧复制来的，中间的转头其实没有内容
变化，看着就是两张图在淡入淡出。

全景条记的是「从固定眼点出发、每个方向看到什么」：列 = 偏航角，行 = 斜率。
Unity 里把它贴到一根圆柱的内壁、相机放到圆柱中轴上绕竖直轴转 —— 相机是真在转，
而且因为这是按**方向**记录的，透视跟真 3D 一模一样（近的东西转过去就是比远的
划过多），不存在「圆柱没视差」的问题。转头是连续的，不是一格格跳的。

约定（必须跟 Assets/Scripts/UI/OfficeRoom.cs 里的常量一一对应）：
  · θ = 0 是正前方；正角朝右（+x），负角朝左；角 = atan2(x, z)
  · 行从上到下：p = Δy/Δ水平距离 由 +SLOPE 降到 -SLOPE
  · 相机放在轴心 (0, EYE_Y, EYE_Z)，镜头 yaw = θ 时前方就是 (sinθ, 0, cosθ)
     —— 跟 look_rays 的约定同源，所以方向天然对得上

运行：
    C:/Users/DELL/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe tools/render_pano.py
"""

import math
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_placeholder_art as G

# ---------------------------------------------------------------- 全景参数
# ⚠️ 这几个数要和 OfficeRoom.cs 里的一致，改一处必须改另一处。
YAW_MIN = -145.0          # 度。相机满转 ±90 + 85° 视场 + 富余
YAW_MAX = 145.0
SLOPE = 0.72              # |p| 上限。可见区只到 0.515，多留一截，防止露圆柱边
PANO_W = 5120             # 280° / 5120 ≈ 17.7 px/度；可见 85° 拿到 ~1500 px
PANO_H = 1296

SEED = 900                # 噪点等随机量的种子

RES_DIR = os.path.join(G.ROOT, "Assets", "Resources")
OUT = os.path.join(RES_DIR, "office_pano.png")


def cyl_rays(yaw_min, yaw_max, p_min, p_max, w, h):
    """全景条每个像素一条单位射线：列定偏航角，行定斜率。"""
    y0 = math.radians(yaw_min)
    y1 = math.radians(yaw_max)
    thetas = y0 + (np.arange(w, dtype=np.float32) + 0.5) * (y1 - y0) / w
    slopes = p_max - (np.arange(h, dtype=np.float32) + 0.5) * (p_max - p_min) / h

    th, sl = np.meshgrid(thetas, slopes)               # → (h, w)
    d = np.stack([np.sin(th), sl, np.cos(th)], axis=-1)
    n = np.sqrt((d * d).sum(axis=-1, keepdims=True))
    return (d / n).astype(np.float32)


def make_projector(yaw_min, yaw_max, p_min, p_max, w, h):
    """世界点 → 全景条像素。方向是 (θ, p)，θ = atan2(x, z)（绕相机眼点算）。"""
    o = np.asarray(G.CAM, np.float64)

    def proj(pt):
        d = np.asarray(pt, np.float64) - o
        theta = math.degrees(math.atan2(d[0], d[2]))
        p = d[1] / max(1e-6, math.hypot(d[0], d[2]))
        return ((theta - yaw_min) / (yaw_max - yaw_min) * w,
                (p_max - p) / (p_max - p_min) * h)

    return proj


def main():
    os.makedirs(RES_DIR, exist_ok=True)

    print("读贴图 …")
    tex = G.load_textures()
    G.VENT_TEX = G.make_vent_texture(tex["metal"])
    G.DOOR_TEX = G.make_door_texture(tex["metal"])
    G.build_decals(tex)

    print("渲全景 %dx%d（%.0f°..%.0f°，±%.2f 斜率）…"
          % (PANO_W, PANO_H, YAW_MIN, YAW_MAX, SLOPE))
    dirs = cyl_rays(YAW_MIN, YAW_MAX, -SLOPE, SLOPE, PANO_W, PANO_H)
    proj = make_projector(YAW_MIN, YAW_MAX, -SLOPE, SLOPE, PANO_W, PANO_H)

    # vig=False：全景条的边缘不是画面边缘，暗角 / 角部蛛网不能照单幅画面那套加
    img = G.render_dirs(dirs, tex, SEED, 0.64, 1.0, PANO_W, PANO_H, proj,
                        vig=False)
    img.save(OUT, optimize=True)
    print("  wrote", os.path.relpath(OUT, G.ROOT), img.size)


if __name__ == "__main__":
    main()

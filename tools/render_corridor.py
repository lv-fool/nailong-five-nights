"""门洞外的走廊 —— 开灯时门洞里看到的那张画。

和办公室全景 / 监控同一套光线投射渲染器、同一套手绘贴图，
所以门口那条走廊和房间是同一个世界。

几何**直接复用** gen_placeholder_art 里的 LEFT_CORR / RIGHT_CORR ——
全景里那两块本来就画了走廊，只是房间那盏吊灯照不进去，一直是黑的。
这里给走廊单独点一盏灯，渲出来的就是「开灯之后看见的样子」。

⚠️ 只渲开灯这一版。关灯时门洞里就是黑的，不需要贴图（代码里直接隐藏）。

视角约定：门洞 1.2 × 2.05 米，玩家眼点离门 2.6 米 ——
水平 / 垂直张角由这两个数算出来，贴回门洞时透视才对得上，
不能随手写个 60°。

用法：
    python tools/render_corridor.py

产出：
    Assets/Resources/Corridor/corridor_left.png
    Assets/Resources/Corridor/corridor_right.png
"""

import os
import sys
import math

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_placeholder_art as gpa

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Corridor")

# 门洞尺寸 / 眼点到门的距离（见 PrototypeScene.DoorLightWidth / Height）
DOOR_W = 1.2
DOOR_H = 2.05
EYE_DIST = 2.6

# 门洞在眼里的张角（水平 26°）—— 但**不能照这个渲**：
# 走廊宽 2 米、长 4.4 米，26° 的锥体在尽头正好是 2 米宽，
# 两侧墙一根线都进不了画面，渲出来像正对着一堵墙。
# 放到 34° 才有纵深 —— 贴回门洞时透视略有夸张，但看着才像走廊。
FOV_H = 34.0

# 图的比例必须等于 1.2 : 2.05（≈ 0.585）
W = 468
H = 800

# 走廊灯：暖白，从天花板往下打
LIGHT_COLOR = np.array([1.00, 0.88, 0.72], np.float32)
LIGHT_RADIUS = 4.6

# 走廊比房间亮一档 —— 开灯就是为了「看清外面」，暗了等于白开
DARK = 0.95


def corridor_boxes(sign):
    """sign = -1 左门 / +1 右门。几何照抄全景里的那两只盒子。"""
    bmin, bmax = gpa.LEFT_CORR if sign < 0 else gpa.RIGHT_CORR
    return [{
        "bmin": bmin,
        "bmax": bmax,
        "default": ("wall", gpa.WALL_TINT, 2.0, 0.14),
        "band": True,
    }]


def render_corridor(sign):
    # ⚠️ 相机必须落在走廊盒子**内部**（盒子 x 边界是 ±2.60），
    # 摆在 2.598 等于贴在盒子外壁上，射线只打到那面墙 —— 渲出来一片平灰。
    # 往里挪 1 厘米，看到的就是走廊内壁。
    cam = (sign * (2.6 + 0.01), 1.025, -1.2)
    yaw = math.radians(90.0 * sign)

    gpa.CAM = cam
    gpa.BOXES = corridor_boxes(sign)
    gpa.HOLES = []
    gpa.W = W
    gpa.H = H
    gpa.FOV_H = FOV_H

    # 灯摆在走廊中段，稍微偏向门口 —— 门框边缘也能沾到一点光
    gpa.LIGHT_POS = np.array([sign * 4.2, 1.95, -1.2], np.float32)
    gpa.LIGHT_RADIUS = LIGHT_RADIUS
    gpa.LIGHT_COLOR = LIGHT_COLOR

    # 走廊墙上不挂画（那是监控里的走廊，门口这条不需要）
    gpa.DECALS = []
    gpa.POSTER_GAIN = 0.68

    tex = gpa.load_textures(512)
    dirs = gpa.look_rays(cam, yaw)
    proj = lambda pt: gpa.project(pt, cam, yaw)

    return gpa.render_dirs(dirs, tex, seed=0x51A5 if sign < 0 else 0x51A6,
                           dark=DARK, exposure=1.0, w=W, h=H, proj=proj,
                           vig=True, props=False)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    for sign, name in ((-1, "corridor_left"), (1, "corridor_right")):
        img = render_corridor(sign)
        path = os.path.join(OUT_DIR, name + ".png")
        img.convert("RGB").save(path)
        print("rendered %s -> %s  (%dx%d, FOV_H=%.1f)" % (name, path, W, H, FOV_H))


if __name__ == "__main__":
    main()

"""门洞外的走廊 —— 开灯时门洞里看到的那张画。

和办公室全景 / 监控同一套光线投射渲染器、同一套手绘贴图，
所以门口那条走廊和房间是同一个世界。

几何不是照抄全景的 LEFT_CORR / RIGHT_CORR —— 那两块是直通的长方体，
正面看只有一堵贴脸的平墙。这里自己拼了一个 **T 字路口**：
2 米宽的直段走 2.4 米，尽头豁开成 6 米宽的横向通道，对面墙上有门、
旁边有柜子和纸箱。只渲开灯这一版，关灯时门洞里就是黑的（代码里隐藏）。

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

# 门洞在眼里的张角是水平 26° —— 但**不能照这个渲**：
# 走廊 2 米宽，26° 的锥体在 2.4 米处只有 1.1 米宽，两侧墙一根线都进不了画面，
# 渲出来像正对着一堵墙。46° 才够看清两侧、也才看得见尽头的左拐口
# （贴回门洞时透视略夸张，但看着才像一条能走人的走廊）。
FOV_H = 46.0

# 图的比例必须等于 1.2 : 2.05（≈ 0.585）
W = 468
H = 800

# 走廊灯：暖白，从天花板往下打
LIGHT_COLOR = np.array([1.00, 0.88, 0.72], np.float32)
LIGHT_RADIUS = 4.6

# 走廊比房间亮一档 —— 开灯就是为了「看清外面」，暗了等于白开
DARK = 0.95


# 装饰配色（和监控里那套转角一致）
DOOR_TINT = np.array([0.26, 0.22, 0.20], np.float32)
CARTON_TINT = np.array([0.46, 0.36, 0.24], np.float32)

# 走廊几何（米，depth 从门口往里量）
CZ = -1.2               # 走廊中心 z（相机站这条线上）
CORRIDOR_HALF = 1.0     # 直段半宽 → 走廊 2 米
CORRIDOR_LEN = 5.4      # 直段多长（够长才有纵深）
TURN_LEN = 6.6          # 路口对面墙离门口多远
TURN_DEPTH = 9.0        # 右拐通道伸到多深
TURN_LEFT = -5.0        # 路口向左侧放开到哪（z）
TURN_RIGHT = 3.0        # 路口向右侧放开到哪（z）
BRANCH_Z0 = 0.5         # 右拐通道的 z 范围
BRANCH_Z1 = 2.5


def corridor_boxes(sign):
    """sign = -1 左门 / +1 右门。

    走廊直走 5.4 米，尽头是路口；路口**正前方封死，右手边拐进去**，
    拐进去那条通道再往深处延伸 2.4 米。

    照 FNAF 1 门厅外：门外不是一堵贴脸的平墙，是一条能走人、还能拐弯的路。

    ⚠️ 两个数不能随便改：
      · 直段必须**够长**（5.4 米）—— 开在 2.6 米的话正面视野只有一米多宽，
        右拐口全在画面外，看起来还是一堵墙
      · 右拐口必须在**相机视野内**（z ∈ [0.5, 2.5]，相对相机偏 1.7~3.7 米，
        6.6 米远处对应 14°~29°，水平半角 23° 刚好罩住入口那一段）
    """

    def x_at(depth):
        return sign * (2.6 + depth)

    def part(d0, d1, y0, y1, z0, z1, default=None, band=False):
        xs = sorted((x_at(d0), x_at(d1)))
        zs = sorted((z0, z1))
        return {
            "bmin": (xs[0], y0, zs[0]),
            "bmax": (xs[1], y1, zs[1]),
            "default": default if default else ("wall", gpa.WALL_TINT, 2.0, 0.14),
            "band": band,
        }

    return [
        # 边界：走廊 + 路口 + 右拐通道，全包进来
        part(0.0, TURN_DEPTH, 0.0, 2.2, TURN_LEFT, TURN_RIGHT, band=True),

        # 直段两侧的墙 —— 中间只留 2 米。
        # ⚠️ **左墙必须提前断开**（4.0 米就结束）：断得晚了，左拐口会被这面墙
        # 整个挡住，从相机根本看不见 —— 试过做到 5.4 米，渲出来还是一堵平墙。
        part(0.0, 4.0, 0.0, 2.2, TURN_LEFT, CZ - CORRIDOR_HALF),
        part(0.0, CORRIDOR_LEN, 0.0, 2.2, CZ + CORRIDOR_HALF, TURN_RIGHT),

        # 路口对面：右半边封死，左半边通到深处（那里就是「拐过去」的通道）
        part(TURN_LEN, TURN_DEPTH, 0.0, 2.2, CZ, TURN_RIGHT),

        # 左拐通道尽头墙上的门
        part(TURN_DEPTH - 0.06, TURN_DEPTH, 0.0, 2.05,
             TURN_LEFT + 0.6, TURN_LEFT + 1.8, ("metal", DOOR_TINT, 1.4, 0.10)),

        # 路口右侧地上的两只纸箱（别让封死的那半边空着）
        part(5.8, 6.4, 0.0, 0.62, CZ + 0.6, CZ + 1.3,
             ("paper", CARTON_TINT, 1.1, 0.14)),
    ]


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

    # 灯摆在转角通道里 —— 门口到转角这段是暗的，转角那头亮，纵深才出来
    gpa.LIGHT_POS = np.array([sign * 5.2, 1.95, -1.2], np.float32)
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

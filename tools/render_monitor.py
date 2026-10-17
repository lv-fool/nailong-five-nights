"""监控房间的程序化渲染 —— 和办公室同一套渲染器、同一套手绘贴图。

为什么不用 AI 出图：监控要跟办公室「同一个世界」。办公室是光线投射渲出来的
（几何由程序算、贴图只出纹理），监控要是 AI 画，两边的墙线、地板格、光位
对不上，玩家一眼就出戏。

布局照 **FNAF 1 官方平面图**（跟小地图那套一致）：
  1A 舞台 / 1B 餐厅 / 1C 海盗湾 / 2A 西走廊 / 2B 西走廊转角 / 3 储藏室 /
  4A 东走廊 / 4B 东走廊转角 / 5 后台 / 6 厨房（黑屏）/ 7 厕所

坐标约定（跟 gen_placeholder_art 一致）：单位米，y 朝上，yaw=0 朝 +z。
监控相机是**壁挂摄像头**，比人眼高（2.2~2.6m），水平 FOV 收窄到 60~70°
（监控镜头比人眼窄，画面才有「被拍下来」的感觉）。

用法：
    python tools/render_monitor.py            # 渲全部已定义的机位
    python tools/render_monitor.py 1a 2a      # 只渲指定的
"""

import os
import sys
import math

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_placeholder_art as gpa

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# ⚠️ 预览目录，不直接覆盖 Assets/Art/Monitoring/（那里是绿子给的 AI 图）。
# 确认风格 OK 之后，把确认过的机位拷进 Assets 再走「接入素材」。
OUT_DIR = os.path.join(ROOT, ".workbuddy-ai", "style", "mon_render")

# 配色沿用办公室那一套，保证同一世界
FLOOR = gpa.FLOOR_TINT
WALL = gpa.WALL_TINT
CEIL = gpa.CEIL_TINT
BAND = gpa.BAND_TINT

CURTAIN = np.array([0.46, 0.15, 0.18], np.float32)      # 舞台暗红幕布
CURTAIN_DARK = np.array([0.30, 0.10, 0.13], np.float32)
STAGE_TINT = np.array([0.28, 0.24, 0.22], np.float32)   # 舞台台面（深木色）
PLUSH_TINT = np.array([0.52, 0.44, 0.34], np.float32)   # 玩偶占位（米黄）


def box(bmin, bmax, default, faces=None, band=False):
    b = {"bmin": bmin, "bmax": bmax, "default": default}
    if faces:
        b["faces"] = faces
    if band:
        b["band"] = True
    return b


# ================================================================ 1A 舞台
# 主厅北侧。房间 8m 宽、6m 深、3m 高；舞台台面抬高 0.45m，
# 后墙挂暗红幕布，台上有三只玩偶的位置（这一版先留空，只看空间和光）。
def plush(cx, z0, z1, height, width, tint):
    """一只玩偶的占位：身体 + 头 + 两只耳朵（真素材等绿子给）。"""
    base = 0.45                      # 站在舞台台面上
    body_h = height * 0.60
    head_w = width * 0.80
    head_h = height * 0.28

    parts = [
        # 身体
        box((cx - width * 0.5, base, z0), (cx + width * 0.5, base + body_h, z1),
            ("wall", tint, 1.4, 0.10)),
        # 头
        box((cx - head_w * 0.5, base + body_h, z0 + 0.04),
            (cx + head_w * 0.5, base + body_h + head_h, z1),
            ("wall", tint, 1.4, 0.10)),
    ]

    # 两只耳朵（挂在头顶两侧）
    ear_w = width * 0.20
    ear_h = height * 0.16
    top = base + body_h + head_h - ear_h * 0.55
    for side in (-1.0, 1.0):
        ex = cx + side * head_w * 0.30
        parts.append(box(
            (ex - ear_w * 0.5, top, z0 + 0.06),
            (ex + ear_w * 0.5, top + ear_h, z1 - 0.02),
            ("wall", tint, 1.4, 0.10)))

    return parts


def boxes_1a():
    return [
        # 房间（加深到 7m —— 相机要退得够远才看得见整个舞台）
        box((-4.0, 0.0, -3.0), (4.0, 3.0, 4.0),
            ("wall", WALL, 2.2, 0.12), band=True),
        # 舞台台面（抬高的平台，深木色 —— 不要棋盘，那是观众席地面）
        box((-3.6, 0.0, -3.0), (3.6, 0.45, -1.55),
            ("wall", STAGE_TINT, 1.6, 0.12)),
        # 后墙幕布（贴在舞台后面的一层）
        box((-3.6, 0.45, -3.0), (3.6, 2.75, -2.92),
            ("wall", CURTAIN, 1.1, 0.18)),
    ]


# ================================================================ 2A 西走廊
# 走廊 2.4m 宽、9m 长、2.4m 高，尽头是转角（这一版渲到转角墙为止）。
def boxes_2a():
    return [
        # 窄长走廊：2m 宽、12m 长、2.4m 高（FNAF 的走廊就是这种又窄又长的）
        box((-1.0, 0.0, -6.0), (1.0, 2.4, 6.0),
            ("wall", WALL, 2.0, 0.14), band=True),
    ]


# ================================================================ 玩偶贴片
# 监控里的玩偶就是 2D 贴片（FNAF 也是这么做的）—— 直接拿角色立绘按投影
# 贴到画面上，比用盒子拼人形自然得多，也省得等新素材。
def paste_billboard(img, cam, yaw, sprite, world_x, world_y, world_z, world_h,
                    dim=0.82):
    """把一张带 alpha 的立绘按透视贴到画面上。脚底落在 (x, y, z)。"""
    if sprite is None:
        return img

    # 脚底 / 头顶两点投影，得到画面上的高度
    sx_b, sy_b = gpa.project((world_x, world_y, world_z), cam, yaw)
    sx_t, sy_t = gpa.project((world_x, world_y + world_h, world_z), cam, yaw)

    h_px = abs(sy_b - sy_t)
    if h_px < 2.0:
        return img

    w_px = h_px * sprite.width / sprite.height
    w_px = max(w_px, 2.0)

    art = sprite.convert("RGBA").resize((int(round(w_px)), int(round(h_px))),
                                        Image.LANCZOS)

    # 按监控的暗度压一下，别让贴片比环境亮
    arr = np.asarray(art).astype(np.float32)
    arr[..., :3] *= dim
    art = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")

    left = int(round(sx_b - w_px * 0.5))
    top = int(round(sy_b - h_px))

    img.paste(art, (left, top), art)
    return img


# ================================================================ 墙上的画
def decals_hall(tex):
    """走廊两侧的儿童画 —— FNAF 走廊的标志性元素。

    画的是我们的角色（插画版）：「MY FUN DAY!」+ 蜡笔小像，
    和办公室后墙那批画同一套生成器，风格不会打架。

    坐标约定（见 wall_uv）：axis=0 的墙，u = z、v = y。
    画框一律正方形（0.9×0.9），否则 512×512 的方图会被拉变形。
    """
    paper = tex["paper"]
    codes = ["nailong", "daishu", "niulai", "lulu"]
    out = []

    for i, z in enumerate((2.4, 1.2, 0.0)):
        for side, base in ((False, 101), (True, 201)):
            code = codes[(i * 2 + (1 if side else 0)) % len(codes)]
            dec = gpa.make_drawing(paper, base + i, gpa.load_character(code))
            out.append((0, side, z, 0.85, z + 0.9, 1.75, dec))

    return out


# ================================================================ 机位表
# name: (盒子工厂, 相机位置, 偏航角(度), 视场角, 灯位置, 灯半径, 灯色, 暗度)
#   yaw = 0 朝 +z；90 朝 +x（右）；180 朝 -z；-90 朝 -x（左）
SHOTS = {
    "1a": (boxes_1a, (0.0, 1.95, 3.30), 180.0, 74.0,
           (0.0, 2.75, -1.70), 6.0, (1.00, 0.86, 0.70), 0.78),
    "2a": (boxes_2a, (0.0, 2.05, 4.80), 180.0, 58.0,
           (0.0, 2.25, 2.20), 5.0, (0.92, 0.94, 1.00), 0.72),
}


def render_shot(name):
    if name not in SHOTS:
        raise KeyError("没有这个机位: " + name)

    boxes, cam, yaw_deg, fov, light, radius, color, dark = SHOTS[name]

    gpa.CAM = cam
    gpa.BOXES = boxes()
    gpa.HOLES = []
    gpa.FOV_H = fov
    gpa.LIGHT_POS = np.array(light, np.float32)
    gpa.LIGHT_RADIUS = radius
    gpa.LIGHT_COLOR = np.array(color, np.float32)

    tex = gpa.load_textures(512)
    yaw = math.radians(yaw_deg)

    # 走廊两侧挂儿童画；舞台不挂（舞台上就是幕布 + 玩偶）
    gpa.DECALS = decals_hall(tex) if name.startswith("2") or name.startswith("4") else []

    # 画别比墙亮太多（监控画面里纸不会发白光）
    gpa.POSTER_GAIN = 0.52

    dirs = gpa.look_rays(cam, yaw)
    proj = lambda pt: gpa.project(pt, cam, yaw)

    img = gpa.render_dirs(dirs, tex, seed=hash(name) & 0xFFFF, dark=dark,
                          exposure=1.0, w=gpa.W, h=gpa.H, proj=proj,
                          vig=True, props=False)

    # 舞台上的三只玩偶：直接贴角色立绘（billboard）
    if name == "1a":
        img = paste_billboard(img, cam, yaw, gpa.load_character("daishu"),
                              -2.30, 0.45, -2.55, 1.70)
        img = paste_billboard(img, cam, yaw, gpa.load_character("nailong"),
                              0.0, 0.45, -2.55, 1.95)
        img = paste_billboard(img, cam, yaw, gpa.load_character("niulai"),
                              2.30, 0.45, -2.55, 1.85)

    return img


def main():
    args = [a.lower() for a in sys.argv[1:]]
    names = args if args else list(SHOTS.keys())

    os.makedirs(OUT_DIR, exist_ok=True)

    for name in names:
        img = render_shot(name)
        path = os.path.join(OUT_DIR, "mon_%s.png" % name)
        img.save(path)
        print("渲好 %s → %s" % (name, path))


if __name__ == "__main__":
    main()

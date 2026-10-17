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
COVE_CURTAIN = np.array([0.34, 0.19, 0.42], np.float32)  # 海盗湾紫幕布
CHAIR_TINT = np.array([0.40, 0.28, 0.24], np.float32)    # 派对椅
DESK_TOP_TINT = gpa.DESK_TOP_TINT                        # 桌面麻点台面
CABINET_TINT = gpa.CABINET_TINT                          # 金属柜 / 架子
FRAME_TINT = gpa.FRAME_TINT                              # 镜面 / 亮金属
DOOR_TINT = np.array([0.26, 0.22, 0.20], np.float32)     # 转角那扇门
MIRROR_TINT = np.array([0.62, 0.66, 0.74], np.float32)   # 镜子（比墙略亮，别过曝）


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
        # 台口两侧的音箱（舞台常驻装饰；角色由代码动态叠加，不画进背景）
        box((-3.55, 0.45, -2.60), (-2.95, 1.10, -2.05),
            ("metal", CABINET_TINT, 0.8, 0.10)),
        box((2.95, 0.45, -2.60), (3.55, 1.10, -2.05),
            ("metal", CABINET_TINT, 0.8, 0.10)),
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


# ================================================================ 1B 餐厅
# 主厅。长桌横在中间，两侧各三把椅子，墙上贴儿童画。
def boxes_1b():
    parts = [
        box((-6.0, 0.0, -5.0), (6.0, 3.2, 5.0),
            ("wall", WALL, 2.4, 0.12), band=True),
        # 长桌
        box((-2.6, 0.0, -0.55), (2.6, 0.76, 0.55),
            ("wall", DESK_TOP_TINT, 1.4, 0.10)),
    ]

    # 两侧椅子（座 + 靠背），简化的派对椅
    for i, x in enumerate((-1.8, 0.0, 1.8)):
        for side in (-1.0, 1.0):
            z0 = 0.55 if side > 0 else -0.55 - 0.5
            parts.append(box((x - 0.24, 0.0, z0), (x + 0.24, 0.45, z0 + 0.5),
                             ("wall", CHAIR_TINT, 1.2, 0.08)))
            zb = z0 + (0.42 if side > 0 else 0.0)
            parts.append(box((x - 0.24, 0.45, zb), (x + 0.24, 1.05, zb + 0.08),
                             ("wall", CHAIR_TINT, 1.2, 0.08)))

    return parts


# ================================================================ 1C 海盗湾
# 小房间，紫色幕布 + 「OUT OF ORDER」告示。噜噜以前蹲这儿。
def boxes_1c():
    return [
        box((-3.2, 0.0, -3.0), (3.2, 3.0, 3.0),
            ("wall", WALL, 2.2, 0.12), band=True),
        # 紫色幕布（占满后墙）
        box((-2.6, 0.0, -3.0), (2.6, 2.7, -2.9),
            ("wall", COVE_CURTAIN, 1.1, 0.16)),
        # 台座（幕布前面那级台阶）
        box((-2.6, 0.0, -2.9), (2.6, 0.35, -2.2),
            ("wall", STAGE_TINT, 1.5, 0.10)),
    ]


# ================================================================ 2B / 4B 转角
# 走廊尽头的转角 —— 通向办公室那两扇门。墙上有海报。
def boxes_corner(sign=-1):
    """sign = -1 西侧 / +1 东侧。转角房间 3m×3m，尽头墙上有一扇门。"""
    return [
        box((-1.5, 0.0, -1.5), (1.5, 2.4, 1.5),
            ("wall", WALL, 2.0, 0.14), band=True),
        # 通往办公室的那扇门（深色门板，贴在尽头墙上）
        box((-0.55, 0.0, -1.50), (0.55, 2.05, -1.45),
            ("metal", DOOR_TINT, 1.4, 0.10)),
    ]


# ================================================================ 3 储藏室
# 小隔间：拖把、水桶、几层架子（都放在远离相机的那头）。
def boxes_3():
    return [
        box((-1.4, 0.0, -2.2), (1.4, 2.4, 2.2),
            ("wall", WALL, 1.8, 0.16), band=True),
        # 拖把桶
        box((-1.05, 0.0, -1.55), (-0.45, 0.55, -0.95),
            ("metal", CABINET_TINT, 0.8, 0.12)),
        # 拖把杆
        box((-0.82, 0.55, -1.32), (-0.68, 1.85, -1.18),
            ("metal", CABINET_TINT, 0.6, 0.10)),
        # 架子
        box((0.30, 0.0, -2.05), (1.25, 1.70, -1.30),
            ("metal", CABINET_TINT, 1.0, 0.14)),
    ]


# ================================================================ 5 后台
# 员工后台：架子上一排玩偶头套、工作台上散着零件。
def boxes_5():
    return [
        box((-2.6, 0.0, -2.6), (2.6, 2.8, 2.6),
            ("wall", WALL, 2.0, 0.14), band=True),
        # 靠后墙的架子
        box((-2.2, 0.0, -2.45), (2.2, 1.85, -1.65),
            ("metal", CABINET_TINT, 1.0, 0.14)),
        # 工作台
        box((-2.0, 0.0, -0.90), (2.0, 0.85, 0.50),
            ("wall", DESK_TOP_TINT, 1.4, 0.10)),
    ]


# ================================================================ 7 厕所
# 洗手池 + 镜子 + 两间隔间。
def boxes_7():
    return [
        box((-2.2, 0.0, -2.4), (2.2, 2.6, 2.4),
            ("wall", WALL, 1.9, 0.14), band=True),
        # 洗手池台（贴着后墙）
        box((-1.6, 0.0, -2.30), (1.6, 0.85, -1.75),
            ("metal", CABINET_TINT, 0.9, 0.10)),
        # 镜子（一块略亮的薄板，在池子上方）
        box((-1.5, 1.00, -2.34), (1.5, 2.05, -2.31),
            ("metal", MIRROR_TINT, 1.2, 0.04)),
        # 隔间挡板（从后墙往相机方向伸）
        box((0.55, 0.0, -2.35), (0.63, 2.05, -0.40),
            ("metal", CABINET_TINT, 0.8, 0.10)),
        box((-2.15, 0.0, -2.35), (-2.07, 2.05, -0.40),
            ("metal", CABINET_TINT, 0.8, 0.10)),
    ]


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


def decals_big_room(tex, name):
    """大房间两侧墙上的画（餐厅 / 后台）。u = z、v = y，跟走廊一个约定。"""
    paper = tex["paper"]
    codes = ["nailong", "daishu", "niulai", "lulu"]
    out = []

    zs = (-2.2, -0.4, 1.4) if name == "1b" else (0.8,)

    for i, z in enumerate(zs):
        for side in (False, True):
            code = codes[(i * 2 + (1 if side else 0)) % len(codes)]
            dec = gpa.make_drawing(paper, 300 + i * 2 + (1 if side else 0),
                                   gpa.load_character(code))
            out.append((0, side, z, 0.95, z + 0.95, 1.90, dec))

    return out


# ================================================================ 机位表
# name: (盒子工厂, 相机位置, 偏航角(度), 视场角, 灯位置, 灯半径, 灯色, 暗度)
#   yaw = 0 朝 +z；90 朝 +x（右）；180 朝 -z；-90 朝 -x（左）
SHOTS = {
    # 主厅 / 舞台
    "1a": (boxes_1a, (0.0, 1.95, 3.30), 180.0, 74.0,
           (0.0, 2.75, -1.70), 6.0, (1.00, 0.86, 0.70), 0.78),
    "1b": (boxes_1b, (0.0, 1.85, 4.70), 180.0, 78.0,
           (0.0, 3.05, 0.60), 7.0, (1.00, 0.90, 0.76), 0.80),
    "1c": (boxes_1c, (0.0, 1.75, 2.70), 180.0, 74.0,
           (0.0, 2.60, -0.80), 4.5, (0.96, 0.86, 1.00), 0.76),

    # 走廊
    "2a": (boxes_2a, (0.0, 1.90, 4.80), 180.0, 62.0,
           (0.0, 2.25, 2.20), 5.0, (0.92, 0.94, 1.00), 0.72),
    "4a": (boxes_2a, (0.0, 1.90, -4.80), 0.0, 62.0,
           (0.0, 2.25, -2.20), 5.0, (0.92, 0.94, 1.00), 0.72),

    # 转角（通向办公室那两扇门）
    "2b": (boxes_corner, (-0.50, 1.80, 1.10), 200.0, 74.0,
           (0.0, 2.30, -0.40), 3.4, (0.94, 0.96, 1.00), 0.74),
    "4b": (boxes_corner, (0.50, 1.80, 1.10), 160.0, 74.0,
           (0.0, 2.30, -0.40), 3.4, (0.94, 0.96, 1.00), 0.74),

    # 小房间
    "3": (boxes_3, (0.0, 1.80, 1.85), 180.0, 72.0,
          (0.0, 2.25, 0.20), 3.0, (0.92, 0.94, 1.00), 0.70),
    "5": (boxes_5, (0.0, 1.90, 2.15), 180.0, 78.0,
          (0.0, 2.65, 0.20), 4.6, (1.00, 0.92, 0.80), 0.76),
    "7": (boxes_7, (0.0, 1.80, 1.95), 180.0, 74.0,
          (0.0, 2.45, 0.20), 3.6, (0.92, 0.96, 1.00), 0.74),
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

    # 墙上的画：走廊两侧挂儿童画；别的房间先空着（看效果再补）
    if name in ("2a", "4a"):
        gpa.DECALS = decals_hall(tex)
    elif name in ("1b", "5"):
        gpa.DECALS = decals_big_room(tex, name)
    else:
        gpa.DECALS = []

    # 画别比墙亮太多（监控画面里纸不会发白光）
    gpa.POSTER_GAIN = 0.52

    dirs = gpa.look_rays(cam, yaw)
    proj = lambda pt: gpa.project(pt, cam, yaw)

    img = gpa.render_dirs(dirs, tex, seed=hash(name) & 0xFFFF, dark=dark,
                          exposure=1.0, w=gpa.W, h=gpa.H, proj=proj,
                          vig=True, props=False)

    # ⚠️ 舞台**不放静态角色** —— 监控里的敌人是运行时按位置叠上去的
    # （见 PrototypeScene.ApplyMonitorCharacter）。背景再画一遍就重复了。
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

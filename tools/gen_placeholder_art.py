#!/usr/bin/env python3
"""生成占位背景图（CC0 贴图 + 光线投射渲染）。

构图照 FNAF 1 的办公室：一间小房间，正前方是通向大厅的开口，左右两侧墙上
各一扇门；镜头坐在办公桌后面，只能左右转头。

做法不是「画方块拼贴」，而是**光线投射**：给每个像素射一条线，跟房间的
盒子求交，拿到命中点的三维坐标，再按平面坐标去取贴图。透视、遮挡、光照
全是算出来的 —— 所以改房间尺寸、换贴图只要重跑一次，不用手调像素。

贴图来自 ambientCG（CC0，可商用、无需署名），放在 tools/cc0/。
缺贴图也能跑，会退回纯色。

运行：
    C:/Users/DELL/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe tools/gen_placeholder_art.py
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1920, 1080
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX_DIR = os.path.join(ROOT, "tools", "cc0")
OUT = os.path.join(ROOT, "Assets", "Art", "Backgrounds")
DOOR_OUT = os.path.join(ROOT, "Assets", "Art", "UI")

# ---------------------------------------------------------------- 相机 / 房间
# 单位是米，y 朝上。玩家坐在原点附近朝 **+z** 看（yaw = 0 就是正前方），
# 面朝 +z 时右手边是 +x —— 所以「左墙」是 x 小的那面，跟直觉一致。
FOV_H = 75.0                    # 水平视场角（度）—— 广角才有原版那种「挤在小屋里」
CAM = (0.0, 1.30, -1.20)        # 坐姿眼高

ROOM_MIN = (-2.60, 0.00, -4.60)
ROOM_MAX = (2.60, 2.60, 3.00)

# 办公桌：在玩家正前方，桌沿横贯画面、向观众展开
DESK_MIN = (-1.60, 0.00, -0.60)
DESK_MAX = (1.60, 0.78, 1.80)

# 门洞外的走廊（射线穿过门洞后继续往里打，打到这几只盒子的内壁）
FRONT_CORR = ((-1.70, 0.0, 3.00), (1.70, 2.40, 8.00))
LEFT_CORR = ((-7.00, 0.0, -3.60), (-2.60, 2.20, -1.60))
RIGHT_CORR = ((2.60, 0.0, -3.60), (7.00, 2.20, -1.60))

# 墙上的洞。u/v 是那面墙的平面坐标（见 wall_uv）。
#   axis 0 = x 墙，u = z；axis 1 = y 面，u = x；axis 2 = z 墙，u = x
#
# 左右两扇门故意放在**玩家侧后方**（z 比相机还小）—— 转头 -90° 时屏幕右边
# 对应 +z，门在身后才会落在画面左边。原版也是这个位置：得扭头才看得见。
FRONT_HOLE = dict(axis=2, side=True, plane=3.00, u0=-1.10, u1=1.10,
                  v0=0.00, v1=2.10, corr=FRONT_CORR)
LEFT_HOLE = dict(axis=0, side=False, plane=-2.60, u0=-3.20, u1=-2.00,
                 v0=0.00, v1=2.05, corr=LEFT_CORR)
RIGHT_HOLE = dict(axis=0, side=True, plane=2.60, u0=-3.20, u1=-2.00,
                  v0=0.00, v1=2.05, corr=RIGHT_CORR)

HOLES = [FRONT_HOLE, LEFT_HOLE, RIGHT_HOLE]
FRAME_W = 0.11                  # 门框宽度（米）

# 天花板灯 —— 摆在玩家前上方，正好落在正面视角能看见的那条天花板上
# （相机在 1.3 米高、天花板 2.6 米，只有 3 米开外的天花板才进画面）
LIGHT_POS = np.array([0.0, 2.45, 2.40], np.float32)
LIGHT_COLOR = np.array([1.00, 0.93, 0.80], np.float32)
LIGHT_RADIUS = 4.50

# ---------------------------------------------------------------- 配色
FLOOR_TINT = np.array([0.40, 0.42, 0.50], np.float32)     # 黑白地砖压成冷灰
WALL_TINT = np.array([0.42, 0.45, 0.56], np.float32)
CEIL_TINT = np.array([0.30, 0.31, 0.38], np.float32)
CORR_TINT = np.array([0.32, 0.34, 0.42], np.float32)      # 走廊里更暗更冷
DESK_TINT = np.array([0.22, 0.23, 0.27], np.float32)
FRAME_TINT = np.array([1.70, 1.76, 1.92], np.float32)
AMBIENT = 0.15
CORR_GAIN = 0.35               # 走廊整体再压一档 —— 办公室的灯不该照亮走廊
POSTER_GAIN = 0.62             # 墙上的画别比墙还亮

TEX_FILES = {
    "floor": "floor_tiles.jpg",
    "wall": "wall_plaster.jpg",
    "concrete": "concrete.jpg",
    "metal": "metal.jpg",
    "paper": "paper.jpg",
}


# ================================================================ 数学小工具

def look_rays(cam, yaw):
    """每个像素一条单位射线。相机空间 +x 右 / +y 上 / +z 前。"""
    tan_h = math.tan(math.radians(FOV_H) * 0.5)
    tan_v = tan_h * H / W

    xs = (np.arange(W, dtype=np.float32) + 0.5) / W * 2.0 - 1.0
    ys = (np.arange(H, dtype=np.float32) + 0.5) / H * 2.0 - 1.0
    px, py = np.meshgrid(xs, ys)

    dx, dy, dz = px * tan_h, -py * tan_v, np.ones_like(px)

    cy, sy = math.cos(yaw), math.sin(yaw)
    wx = dx * cy + dz * sy
    wz = -dx * sy + dz * cy
    wy = dy

    n = np.sqrt(wx * wx + wy * wy + wz * wz)
    return np.stack([wx / n, wy / n, wz / n], axis=-1).astype(np.float32)


def project(pt, cam, yaw):
    """世界点 → 屏幕像素坐标。"""
    d = np.asarray(pt, np.float64) - np.asarray(cam, np.float64)
    cy, sy = math.cos(yaw), math.sin(yaw)
    x = d[0] * cy - d[2] * sy
    z = d[0] * sy + d[2] * cy
    y = d[1]

    tan_h = math.tan(math.radians(FOV_H) * 0.5)
    tan_v = tan_h * H / W
    sx = (x / (z * tan_h)) * 0.5 + 0.5
    sy_ = 0.5 - (y / (z * tan_v)) * 0.5
    return sx * W, sy_ * H


def box_t(o, d, bmin, bmax):
    """射线与盒子的进出参数。返回 (t_near, ax_near, t_far, ax_far)。

    ax 是命中面所在的轴：0=x / 1=y / 2=z。
    相机在盒子里时（房间）看 t_far 那个面；在盒子外时（桌子）看 t_near。

    **必须先在原始值上判 tn <= tf**：射线擦着某个轴的板过去时，那个轴给出的是
    空区间（lo = hi = -inf），可 tn 照样会返回一个看着挺正常的数 —— 不判就会
    把「根本没打中」当成打中了。
    """
    bmin = np.asarray(bmin, np.float32)
    bmax = np.asarray(bmax, np.float32)

    with np.errstate(divide="ignore", invalid="ignore"):
        inv = 1.0 / d
        t0 = (bmin - o) * inv
        t1 = (bmax - o) * inv

    lo = np.minimum(t0, t1)
    hi = np.maximum(t0, t1)
    tn_raw = lo.max(axis=-1)
    axn = lo.argmax(axis=-1)
    tf_raw = hi.min(axis=-1)
    axf = hi.argmin(axis=-1)

    valid = tn_raw <= tf_raw
    tn = np.where(valid & (tn_raw > 0.0), tn_raw, -np.inf).astype(np.float32)
    tf = np.where(valid & (tf_raw > 0.0), tf_raw, np.inf).astype(np.float32)
    return tn, axn, tf, axf


def wall_uv(axis, p):
    """命中点 → 那面墙的平面坐标 (u, v)。axis 必须是单个整数。"""
    if axis == 0:
        return p[..., 2], p[..., 1]
    if axis == 1:
        return p[..., 0], p[..., 2]
    return p[..., 0], p[..., 1]


def wall_uv_ax(ax, p):
    """逐像素版本 —— ax 是每个像素各自的命中轴。"""
    u = np.zeros(p.shape[:2], np.float32)
    v = np.zeros(p.shape[:2], np.float32)
    for a in range(3):
        m = (ax == a)
        if not m.any():
            continue
        uu, vv = wall_uv(a, p)
        u[m] = uu[m]
        v[m] = vv[m]
    return u, v


def world_pt(axis, plane, u, v):
    """平面坐标 → 世界点。"""
    if axis == 0:
        return (plane, v, u)
    if axis == 1:
        return (u, plane, v)
    return (u, v, plane)


# ================================================================ 贴图

def load_textures(size=512):
    cache = {}
    for key, fname in TEX_FILES.items():
        path = os.path.join(TEX_DIR, fname)
        if os.path.exists(path):
            im = Image.open(path).convert("RGB").resize((size, size), Image.LANCZOS)
            cache[key] = np.asarray(im).astype(np.float32) / 255.0
        else:
            print("  ! 缺贴图 %s —— 用纯色代替" % fname)
            cache[key] = np.full((size, size, 3), 0.35, np.float32)
    return cache


def texel(tex, u, v):
    """按贴图坐标取样（自动平铺）。u/v 单位是「几块贴图」。"""
    h, w = tex.shape[:2]
    xi = np.clip((np.mod(u, 1.0) * w).astype(np.int32), 0, w - 1)
    yi = np.clip((np.mod(v, 1.0) * h).astype(np.int32), 0, h - 1)
    return tex[yi, xi]


def make_poster_texture(tex, seed):
    """墙上的儿童画：纸 + 蜡笔涂鸦。"""
    rng = np.random.default_rng(seed)
    img = Image.new("RGB", (512, 512), (228, 222, 205))
    d = ImageDraw.Draw(img)
    ink = (176, 74, 62)
    blue = (78, 108, 156)
    gold = (188, 152, 62)
    green = (96, 138, 92)

    d.ellipse([150, 84, 362, 296], outline=ink, width=15)
    d.ellipse([204, 152, 236, 184], fill=ink)
    d.ellipse([276, 152, 308, 184], fill=ink)
    d.arc([212, 196, 300, 258], 20, 160, fill=ink, width=13)

    d.line([(256, 296), (256, 416)], fill=blue, width=15)
    d.line([(256, 336), (170, 392)], fill=blue, width=13)
    d.line([(256, 336), (342, 392)], fill=blue, width=13)
    d.line([(256, 416), (204, 484)], fill=blue, width=13)
    d.line([(256, 416), (308, 484)], fill=blue, width=13)

    for i, col in enumerate((gold, green, gold, green)):
        pts = [(int(rng.integers(24, 488)), int(rng.integers(24, 488))) for _ in range(4)]
        d.line(pts, fill=col, width=9, joint="curve")

    arr = np.asarray(img).astype(np.float32) / 255.0

    # 叠一层纸纹，别让画看着像塑料
    paper = np.asarray(
        Image.fromarray((tex * 255).astype(np.uint8)).resize((512, 512), Image.LANCZOS)
    ).astype(np.float32) / 255.0
    lum = paper.mean(axis=2, keepdims=True)
    arr = np.clip(arr * (1.0 + (lum - lum.mean()) * 1.6), 0.0, 1.0)
    return arr.astype(np.float32)


def make_vent_texture(tex):
    """通风口：金属底 + 横百叶。"""
    arr = tex.copy()
    img = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for i in range(1, 6):
        y = 512 * i / 6.0
        d.line([(10, y), (502, y)], fill=(24, 26, 32), width=16)
        d.line([(10, y + 9), (502, y + 9)], fill=(120, 126, 140), width=4)
    d.rectangle([0, 0, 511, 511], outline=(140, 146, 160), width=10)
    return np.asarray(img).astype(np.float32) / 255.0


def make_grime(seed=7, size=256, blur=5):
    """脏污 / 斑驳的噪声场，取值大约 -1..1。

    墙和地板的贴图本身太平（明暗差不到 3%），不叠点脏东西，整面墙就是
    一块死板的色块 —— 恐怖片最忌讳这个。
    """
    rng = np.random.default_rng(seed)
    a = rng.random((size, size)).astype(np.float32)
    im = Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(blur))
    a = np.asarray(im).astype(np.float32) / 255.0
    return (a - a.mean()) / (a.std() + 1e-6)


GRIME = make_grime()


def make_door_texture(tex):
    """卷帘门：金属底 + 横条。"""
    img = Image.fromarray((np.clip(tex, 0, 1) * 255).astype(np.uint8))
    img = img.resize((256, 512), Image.LANCZOS)
    d = ImageDraw.Draw(img)
    for y in range(0, 512, 26):
        d.line([(0, y), (256, y)], fill=(30, 32, 38), width=7)
        d.line([(0, y + 8), (256, y + 8)], fill=(104, 110, 124), width=3)
    d.rectangle([0, 0, 255, 511], outline=(22, 24, 30), width=10)
    arr = np.asarray(img).astype(np.float32) / 255.0
    return arr * np.array([0.72, 0.75, 0.84], np.float32)


# ================================================================ 渲染

def light_at(p, normal):
    """点光 + 环境光。normal 必须朝外（朝相机来的那一侧）。"""
    d = LIGHT_POS - p
    dist2 = (d * d).sum(axis=-1)
    dist = np.sqrt(dist2) + 1e-6
    nd = ((d / dist[..., None]) * normal).sum(axis=-1)
    diff = np.clip(nd, 0.0, 1.0)
    atten = 1.0 / (1.0 + dist2 / (LIGHT_RADIUS * LIGHT_RADIUS))
    gain = AMBIENT + 2.4 * atten * diff
    return gain[..., None] * LIGHT_COLOR


def draw_props(col_img, mask_img, cam, yaw):
    """桌上的风扇和纸杯。

    这两件东西几何太碎，用光线投射不划算 —— 投影出屏幕位置和尺度，
    再用 2D 画。光照按底座那点算，颜色才跟房间对得上。
    """
    d = ImageDraw.Draw(col_img)
    mk = ImageDraw.Draw(mask_img)

    def visible(base):
        """在相机背后就别画了 —— 投影会翻到画面另一头去。"""
        v = np.asarray(base, np.float64) - np.asarray(cam, np.float64)
        return (v[0] * math.sin(yaw) + v[2] * math.cos(yaw)) > 0.05

    def scale_at(base, metres):
        """metres 米在这个距离上折合多少像素。"""
        _, y0 = project(base, cam, yaw)
        _, y1 = project((base[0], base[1] + metres, base[2]), cam, yaw)
        return abs(y0 - y1)

    def shade(base, tint):
        p = np.array([base], np.float32)
        n = np.array([[0.0, 1.0, 0.0]], np.float32)
        g = light_at(p, n)[0]
        return tuple(int(np.clip(v * 255.0, 0, 255)) for v in np.clip(tint * g, 0, 1))

    def ink(fn, colour):
        fn(d, colour)
        fn(mk, 255)

    # ---- 风扇
    base = (-1.10, 0.78, 0.90)
    if visible(base):
        bx, by = project(base, cam, yaw)
        r = scale_at(base, 0.21)
        hx, hy = project((base[0], base[1] + 0.46, base[2]), cam, yaw)
        body = shade(base, np.array([0.20, 0.22, 0.28], np.float32))
        edge = shade(base, np.array([0.42, 0.45, 0.55], np.float32))

        ink(lambda i, c: i.polygon(
            [(bx - r * 0.50, by), (bx + r * 0.50, by),
             (bx + r * 0.28, by - r * 0.22), (bx - r * 0.28, by - r * 0.22)], fill=c), body)
        ink(lambda i, c: i.rectangle(
            [bx - r * 0.10, min(hy, by - r * 0.18), bx + r * 0.10, max(hy, by - r * 0.18)],
            fill=c), body)
        ink(lambda i, c: i.ellipse([hx - r, hy - r, hx + r, hy + r],
                                   outline=c, width=7), edge)
        ink(lambda i, c: i.ellipse(
            [hx - r * 0.70, hy - r * 0.70, hx + r * 0.70, hy + r * 0.70],
            outline=c, width=3), body)
        for k in range(6):
            a = math.pi * k / 3.0
            ink(lambda i, c, a=a: i.line(
                [(hx, hy), (hx + math.cos(a) * r, hy + math.sin(a) * r)],
                fill=c, width=4), body)
        ink(lambda i, c: i.ellipse(
            [hx - r * 0.17, hy - r * 0.17, hx + r * 0.17, hy + r * 0.17], fill=c), edge)

    # ---- 纸杯
    cbase = (1.15, 0.78, 1.30)
    if visible(cbase):
        cx, cy = project(cbase, cam, yaw)
        hw = scale_at(cbase, 0.055)
        cup = shade(cbase, np.array([0.30, 0.31, 0.37], np.float32))
        ink(lambda i, c: i.polygon(
            [(cx - hw, cy), (cx + hw, cy),
             (cx + hw * 0.80, cy - hw * 2.1), (cx - hw * 0.80, cy - hw * 2.1)], fill=c), cup)


def surface(tex, key, tint, u, v, tile, gain, grime=0.0, grime_tile=3.0):
    c = texel(tex[key], u / tile, v / tile) * tint
    if grime > 0.0:
        g = texel(GRIME[..., None], u / grime_tile, v / grime_tile)[..., 0]
        c = c * (1.0 + g[..., None] * grime)
    return c * gain


def render(cam, yaw, tex, seed, dark=0.70, exposure=1.05):
    """渲一张视角图。dark 是整体压暗，用于把房间压到恐怖片的亮度。"""
    dirs = look_rays(cam, yaw)
    o = np.array(cam, np.float32)

    tn_room, _, tf_room, ax_room = box_t(o, dirs, ROOM_MIN, ROOM_MAX)
    tn_desk, ax_desk, tf_desk, _ = box_t(o, dirs, DESK_MIN, DESK_MAX)

    # 光有 t_near 不够 —— 打不中的时候 t_near 是 -inf，得确认它真的有效
    use_desk = np.isfinite(tn_desk) & (tn_desk < tf_room)
    t = np.where(use_desk, tn_desk, tf_room)
    ax = np.where(use_desk, ax_desk, ax_room).astype(np.int32)

    p = o + dirs * t[..., None]

    bmin = np.where(use_desk[..., None], np.array(DESK_MIN, np.float32),
                    np.array(ROOM_MIN, np.float32))
    bmax = np.where(use_desk[..., None], np.array(DESK_MAX, np.float32),
                    np.array(ROOM_MAX, np.float32))
    pv = np.take_along_axis(p, ax[..., None], axis=-1)[..., 0]
    vmin = np.take_along_axis(bmin, ax[..., None], axis=-1)[..., 0]
    vmax = np.take_along_axis(bmax, ax[..., None], axis=-1)[..., 0]
    on_max = np.abs(pv - vmax) < np.abs(pv - vmin)

    color = np.zeros((H, W, 3), np.float32)
    normal = np.zeros((H, W, 3), np.float32)

    # 六个面逐面填。朝内的法线 = 相机所在的那一侧。
    #          轴  是否 max 侧   贴图        色调        平铺  脏污
    faces = [
        (0, False, "wall", WALL_TINT, 2.0, 0.06),      # 左墙 x = min
        (0, True, "wall", WALL_TINT, 2.0, 0.06),       # 右墙 x = max
        (1, False, "floor", FLOOR_TINT, 0.80, 0.05),   # 地板 y = min（黑白地砖）
        (1, True, "concrete", CEIL_TINT, 2.4, 0.05),   # 天花板
        (2, False, "wall", WALL_TINT, 2.0, 0.06),      # 后墙 z = min
        (2, True, "wall", WALL_TINT, 2.0, 0.06),       # 前墙 z = max
    ]

    for axis, side_max, key, tint, tile, grime in faces:
        m = (~use_desk) & (ax == axis) & (on_max == side_max)
        if not m.any():
            continue
        u, v = wall_uv(axis, p)
        # 房间内壁的法线一律朝房间里（朝相机这一侧）：
        # 左墙朝 +x、右墙朝 -x、地板朝 +y、天花板朝 -y、前墙朝 +z、后墙朝 -z
        n = np.zeros((H, W, 3), np.float32)
        n[..., axis] = -1.0 if side_max else 1.0

        gain = light_at(p, n)
        col = surface(tex, key, tint, u, v, tile, gain, grime)
        color[m] = col[m]
        normal[m] = n[m]

    # 桌子
    m = use_desk
    if m.any():
        u, v = wall_uv_ax(ax, p)
        n = np.zeros((H, W, 3), np.float32)
        n[..., 0] = np.where(on_max, 1.0, -1.0) * (ax == 0)
        n[..., 1] = np.where(on_max, 1.0, -1.0) * (ax == 1)
        n[..., 2] = np.where(on_max, 1.0, -1.0) * (ax == 2)
        gain = light_at(p, n)
        col = surface(tex, "concrete", DESK_TINT, u, v, 1.4, gain, 0.05)
        color[m] = col[m]
        normal[m] = n[m]

    # ---- 门洞、门框、贴画：都在墙上，逐面处理
    hole_mask = np.zeros((H, W), bool)
    for hole in HOLES:
        m = (~use_desk) & (ax == hole["axis"]) & (on_max == hole["side"])
        if not m.any():
            continue
        u, v = wall_uv(hole["axis"], p)

        inside = m & (u > hole["u0"]) & (u < hole["u1"]) & (v > hole["v0"]) & (v < hole["v1"])
        hole_mask |= inside

        ring = (m & (u > hole["u0"] - FRAME_W) & (u < hole["u1"] + FRAME_W)
                & (v > hole["v0"] - FRAME_W) & (v < hole["v1"] + FRAME_W)) & (~inside)
        if ring.any():
            n = normal.copy()
            gain = light_at(p, n)
            col = surface(tex, "metal", FRAME_TINT, u, v, 0.55, gain)
            color[ring] = col[ring]
            # 门框往房间里凸一点，靠自阴影把厚度做出来
            color[ring] *= 1.18

    # ---- 门洞之外：把射线放进走廊再打一次
    if hole_mask.any():
        for hole in HOLES:
            wall = (ax == hole["axis"]) & (on_max == hole["side"]) & (~use_desk)
            if not wall.any():
                continue
            u, v = wall_uv(hole["axis"], p)
            sel = wall & (u > hole["u0"]) & (u < hole["u1"]) \
                       & (v > hole["v0"]) & (v < hole["v1"])
            if not sel.any():
                continue

            cmin, cmax = hole["corr"]
            # 从墙面往外挪一点点，免得又打回自己
            o2 = p + dirs * 0.02
            tn2, _, t2, ax2 = box_t(o2, dirs, cmin, cmax)
            hit = np.isfinite(t2)
            t2 = np.where(hit, t2, 0.0)
            p2 = o2 + dirs * t2[..., None]

            n2 = np.zeros((H, W, 3), np.float32)
            for a in range(3):
                mm = (ax2 == a)
                if not mm.any():
                    continue
                pv2 = p2[..., a]
                lo = mm & (np.abs(pv2 - cmin[a]) < np.abs(pv2 - cmax[a]))
                n2[lo, a] = 1.0
                n2[mm & (~lo), a] = -1.0

            gain = light_at(p2, n2) * CORR_GAIN
            u2, v2 = wall_uv_ax(ax2, p2)
            col = surface(tex, "wall", CORR_TINT, u2, v2, 2.0, gain, 0.06)
            col = np.where(hit[..., None], col, 0.0)
            color[sel] = col[sel]

    # ---- 墙上的画 / 通风口
    decals = [
        # 前墙（u = x, v = y）—— 门洞两边各挂两张儿童画，门楣上方一个通风口
        (2, True, -2.30, 1.30, -1.72, 2.06, "poster", 3),
        (2, True, -1.58, 1.46, -1.26, 1.94, "poster", 5),
        (2, True, 1.26, 1.46, 1.58, 1.94, "poster", 7),
        (2, True, 1.72, 1.30, 2.30, 2.06, "poster", 11),
        (2, True, -0.34, 2.20, 0.34, 2.48, "vent", 0),
        # 左墙（u = z, v = y）—— 门在靠后的角落，画和通风口挂在剩下的墙上
        (0, False, -1.00, 1.36, -0.40, 2.02, "poster", 13),
        (0, False, 0.10, 1.44, 0.70, 1.88, "vent", 0),
        # 右墙
        (0, True, -1.00, 1.36, -0.40, 2.02, "poster", 19),
        (0, True, 0.10, 1.44, 0.70, 1.88, "vent", 0),
        # 天花板灯
        (1, True, -0.60, 2.05, 0.60, 2.75, "lamp", 0),
    ]

    for axis, side_max, u0, v0, u1, v1, kind, dseed in decals:
        m = (~use_desk) & (ax == axis) & (on_max == side_max)
        if not m.any():
            continue
        u, v = wall_uv(axis, p)
        sel = m & (u > u0) & (u < u1) & (v > v0) & (v < v1)
        if not sel.any():
            continue

        if kind == "poster":
            dec = poster(dseed)
            du = (u - u0) / (u1 - u0)
            dv = (v1 - v) / (v1 - v0)        # 贴图 v 轴朝下，得翻过来
        elif kind == "vent":
            dec = VENT_TEX
            du = (u - u0) / (u1 - u0)
            dv = (v1 - v) / (v1 - v0)
        else:                                        # 灯：自发光
            color[sel] = np.array([0.95, 0.90, 0.76], np.float32)
            continue

        gain = light_at(p, normal) * POSTER_GAIN
        col = texel(dec, du, dv) * gain
        # 纸边压一道暗线，看着才像贴在墙上
        edge = ((du < 0.02) | (du > 0.98) | (dv < 0.02) | (dv > 0.98))[..., None]
        col = np.where(edge, col * 0.45, col)
        color[sel] = col[sel]

    # ---- 桌上的道具（投影成 2D 画上去）
    prop_img = Image.new("RGB", (W, H), (0, 0, 0))
    prop_mask = Image.new("L", (W, H), 0)
    draw_props(prop_img, prop_mask, cam, yaw)
    pm = (np.asarray(prop_mask).astype(np.float32) / 255.0)[..., None]
    pc = np.asarray(prop_img).astype(np.float32) / 255.0
    color = color * (1.0 - pm) + pc * pm

    color = post(color, seed, dark, exposure)
    img = Image.fromarray((color * 255).astype(np.uint8), "RGB")
    return img.filter(ImageFilter.GaussianBlur(0.6))


def post(color, seed, dark, exposure):
    """统一的收尾：曝光 → 色调映射 → 噪点 → 暗角 → 暗部偏冷。

    背景和门片都得走这一套 —— 不然门贴上去会像另一张图。
    """
    color = color * (dark * exposure)
    color = color / (1.0 + color) * 1.9

    rng = np.random.default_rng(seed)
    color = color + rng.normal(0.0, 0.016, (H, W, 1)).astype(np.float32)

    ys = (np.arange(H, dtype=np.float32) + 0.5) / H * 2.0 - 1.0
    xs = (np.arange(W, dtype=np.float32) + 0.5) / W * 2.0 - 1.0
    vx, vy = np.meshgrid(xs, ys)
    d = np.sqrt(vx * vx + vy * vy) / 1.41421356
    color = color * np.clip(1.0 - 0.66 * (d ** 2.1), 0.0, 1.0)[..., None]

    # 暗部偏冷 —— 冷房间 + 暖灯，是原版那股味
    lum = color.mean(axis=2, keepdims=True)
    shadow = np.clip(1.0 - lum * 2.4, 0.0, 1.0)
    color = color * (1.0 - shadow * 0.20) * np.array([0.90, 0.95, 1.12], np.float32)

    return np.clip(color, 0.0, 1.0)


# ================================================================ 门片

def build_door(hole, cam, yaw, tex, closed, seed, dark=0.70, exposure=1.05):
    """叠层门片。整屏透明画布，门只画在门洞那个四边形里。

    门是**跟背景同一条管线渲出来的** —— 同样的光线投射、同样的光照和后期。
    直接贴一张不受光的贴图上去，门会比墙亮一大截，一眼假。
    """
    canvas = np.zeros((H, W, 4), np.float32)
    if not closed:
        return Image.fromarray((canvas * 255).astype(np.uint8), "RGBA")

    dirs = look_rays(cam, yaw)
    o = np.array(cam, np.float32)

    tn_room, _, tf_room, ax_room = box_t(o, dirs, ROOM_MIN, ROOM_MAX)
    tn_desk, ax_desk, _, _ = box_t(o, dirs, DESK_MIN, DESK_MAX)
    use_desk = np.isfinite(tn_desk) & (tn_desk < tf_room)

    t = np.where(use_desk, tn_desk, tf_room)
    ax = np.where(use_desk, ax_desk, ax_room).astype(np.int32)
    p = o + dirs * t[..., None]

    bmin = np.where(use_desk[..., None], np.array(DESK_MIN, np.float32),
                    np.array(ROOM_MIN, np.float32))
    bmax = np.where(use_desk[..., None], np.array(DESK_MAX, np.float32),
                    np.array(ROOM_MAX, np.float32))
    pv = np.take_along_axis(p, ax[..., None], axis=-1)[..., 0]
    vmin = np.take_along_axis(bmin, ax[..., None], axis=-1)[..., 0]
    vmax = np.take_along_axis(bmax, ax[..., None], axis=-1)[..., 0]
    on_max = np.abs(pv - vmax) < np.abs(pv - vmin)

    wall = (~use_desk) & (ax == hole["axis"]) & (on_max == hole["side"])
    u, v = wall_uv(hole["axis"], p)
    sel = wall & (u > hole["u0"]) & (u < hole["u1"]) & (v > hole["v0"]) & (v < hole["v1"])
    if not sel.any():
        return Image.fromarray((canvas * 255).astype(np.uint8), "RGBA")

    n = np.zeros((H, W, 3), np.float32)
    n[..., hole["axis"]] = -1.0 if hole["side"] else 1.0
    gain = light_at(p, n)

    du = (u - hole["u0"]) / (hole["u1"] - hole["u0"])
    dv = (hole["v1"] - v) / (hole["v1"] - hole["v0"])   # 贴图 v 轴朝下，得翻过来
    col = texel(DOOR_TEX, du, dv) * gain
    col = post(col, seed, dark, exposure)

    canvas[sel, :3] = col[sel]
    canvas[sel, 3] = 1.0
    return Image.fromarray((np.clip(canvas, 0, 1) * 255).astype(np.uint8), "RGBA")


# ================================================================ 主流程

# 三个视角。yaw = 0 是正前方（+z），左右各转 90°。
VIEWS = [
    ("front", 0.0, 11),
    ("left", -math.pi / 2, 22),
    ("right", math.pi / 2, 33),
]

VENT_TEX = None
DOOR_TEX = None
POSTERS = {}
_TEX = {}


def poster(seed):
    """儿童画按 seed 缓存 —— 同一个 seed 每次渲都是同一张。"""
    if seed not in POSTERS:
        POSTERS[seed] = make_poster_texture(_TEX["paper"], seed)
    return POSTERS[seed]


def main():
    global VENT_TEX, DOOR_TEX, _TEX

    os.makedirs(OUT, exist_ok=True)
    os.makedirs(DOOR_OUT, exist_ok=True)

    print("读贴图 …")
    _TEX = load_textures()
    tex = _TEX
    VENT_TEX = make_vent_texture(tex["metal"])
    DOOR_TEX = make_door_texture(tex["metal"])

    print("渲背景 …")
    for name, yaw, seed in VIEWS:
        img = render(CAM, yaw, tex, seed=seed)
        path = os.path.join(OUT, "bg_office_%s%s.png"
                            % (name, "" if name == "front" else "_01"))
        img.save(path)
        print("  wrote", os.path.relpath(path, ROOT))

    print("渲门片 …")
    door_jobs = [
        ("ui_door_left_closed.png", LEFT_HOLE, -math.pi / 2, True, 44),
        ("ui_door_left_open.png", LEFT_HOLE, -math.pi / 2, False, 44),
        ("ui_door_right_closed.png", RIGHT_HOLE, math.pi / 2, True, 55),
        ("ui_door_right_open.png", RIGHT_HOLE, math.pi / 2, False, 55),
    ]
    for name, hole, yaw, closed, seed in door_jobs:
        path = os.path.join(DOOR_OUT, name)
        build_door(hole, CAM, yaw, tex, closed, seed).save(path)
        print("  wrote", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""生成占位背景图（CC0 贴图 + 光线投射渲染）。

**构图严格照 FNAF 1 的办公室**（不是自己发挥的）：
  · 正前方是一面后墙，墙上挂着「CELEBRATE!」海报和一墙儿童画（每张顶上写着
    MY FUN DAY!），天花板垂下一盏红罩吊灯，是屋里唯一的光源
  · 桌子抵着后墙，桌面上摆着两台 CRT 显示器、一台大风扇、纸杯、揉皱的纸团、
    一只音箱；桌子正面是两格抽屉
  · 地板是黑白棋盘砖，**墙脚也有一条同样花色的棋盘墙裙**，再往上是灰墙
  · 天花板两个角挂着蛛网
  · 左右两扇门开在侧墙上，门外是走廊

做法不是「画方块拼贴」，而是**光线投射**：给每个像素射一条线，跟场景里所有
盒子求交，命中面的三维坐标决定取哪块贴图。透视、遮挡、光照全是算出来的 ——
改尺寸、换贴图只要重跑一次，不用手调像素。

贴图来自 ambientCG（CC0，可商用、无需署名），放在 tools/cc0/。

运行：
    C:/Users/DELL/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe tools/gen_placeholder_art.py
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1080
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX_DIR = os.path.join(ROOT, "tools", "cc0")
CHAR_DIR = os.path.join(ROOT, "Assets", "Art", "Characters")
OUT = os.path.join(ROOT, "Assets", "Art", "Backgrounds")
DOOR_OUT = os.path.join(ROOT, "Assets", "Art", "UI")

# 墙上那张「CELEBRATE!」海报和儿童画里站的是谁 —— 用绿子画好的立绘，
# 不另找素材：现成的就是最准的，还不用担心版权。
CAST = ("niulai", "nailong", "lulu", "daishu")

# ---------------------------------------------------------------- 相机 / 房间
# 单位是米，y 朝上。玩家朝 **+z** 看（yaw = 0 就是正前方）；面朝 +z 时右手边
# 是 +x，所以「左墙」是 x 小的那面，跟直觉一致。
FOV_H = 85.0                    # 水平视场角 —— 原版就是广角，才塞得下整间屋
CAM = (0.0, 1.30, -1.20)        # 坐姿眼高

ROOM_MIN = (-2.60, 0.00, -4.60)
ROOM_MAX = (2.60, 2.60, 3.00)

# 办公桌**抵着后墙**（原版就是这么摆的），所以镜头离桌子有 2.8 米 ——
# 这个距离是照原版反推的：只有离得够远，桌面才是窄窄一条、桌子正面
# （两格抽屉那一面）才进得了画面。凑近了只能看见一块桌面，不像原版。
DESK_MIN = (-2.00, 0.00, 1.60)
DESK_MAX = (2.00, 0.80, 2.60)

# 门洞外的走廊（射线穿过门洞后继续往里打，打到这几只盒子的内壁）
FRONT_CORR = ((-1.70, 0.0, 3.00), (1.70, 2.40, 8.00))
LEFT_CORR = ((-7.00, 0.0, -3.60), (-2.60, 2.20, -1.60))
RIGHT_CORR = ((2.60, 0.0, -3.60), (7.00, 2.20, -1.60))

# 墙上的洞。u/v 是那面墙的平面坐标（见 wall_uv）。
#   axis 0 = x 墙，u = z；axis 1 = y 面，u = x；axis 2 = z 墙，u = x
#
# 两扇门放在**玩家侧后方**（门的 z 比相机 z 小）：转头 -90° 时屏幕右边对应 +z，
# 门在身后才会落在画面左边。原版的门也在你侧后方 —— 得扭头才看得见。
FRONT_HOLE = dict(axis=2, side=True, plane=3.00, u0=-0.88, u1=0.88,
                  v0=0.00, v1=2.10, corr=FRONT_CORR)
LEFT_HOLE = dict(axis=0, side=False, plane=-2.60, u0=-3.20, u1=-2.00,
                 v0=0.00, v1=2.05, corr=LEFT_CORR)
RIGHT_HOLE = dict(axis=0, side=True, plane=2.60, u0=-3.20, u1=-2.00,
                  v0=0.00, v1=2.05, corr=RIGHT_CORR)

HOLES = [FRONT_HOLE, LEFT_HOLE, RIGHT_HOLE]
FRAME_W = 0.11                  # 门框宽度（米）
BAND_H = 0.62                   # 墙脚棋盘墙裙的高度（米）

# 吊灯：从天花板垂下来，是屋里唯一的光源
LAMP = (0.0, 2.42, 1.80)        # 灯罩中心
LIGHT_POS = np.array([0.0, 2.26, 1.80], np.float32)
LIGHT_COLOR = np.array([1.00, 0.86, 0.68], np.float32)
LIGHT_RADIUS = 3.80

# ---------------------------------------------------------------- 配色
FLOOR_TINT = np.array([0.40, 0.42, 0.50], np.float32)     # 黑白棋盘砖压成冷灰
BAND_TINT = np.array([0.62, 0.62, 0.68], np.float32)      # 墙脚墙裙，比地砖亮一点
WALL_TINT = np.array([0.40, 0.42, 0.50], np.float32)
CEIL_TINT = np.array([0.26, 0.27, 0.33], np.float32)
CORR_TINT = np.array([0.32, 0.34, 0.42], np.float32)      # 走廊里更暗更冷
DESK_TINT = np.array([0.24, 0.25, 0.29], np.float32)
DESK_TOP_TINT = np.array([0.34, 0.34, 0.38], np.float32)  # 桌面是浅色麻点台面
FRAME_TINT = np.array([1.70, 1.76, 1.92], np.float32)
CABINET_TINT = np.array([0.46, 0.47, 0.52], np.float32)   # 桌下的机柜 / 音箱
MONITOR_TINT = np.array([0.44, 0.45, 0.52], np.float32)
AMBIENT = 0.18
CORR_GAIN = 0.70               # 走廊整体再压一档 —— 办公室的灯不该照亮走廊
POSTER_GAIN = 0.80             # 墙上的画别比墙还亮

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


def depth_of(pt, cam, yaw):
    d = np.asarray(pt, np.float64) - np.asarray(cam, np.float64)
    return d[0] * math.sin(yaw) + d[2] * math.cos(yaw)


def box_t(o, d, bmin, bmax):
    """射线与盒子的进出参数。返回 (t_near, ax_near, t_far, ax_far)。

    ax 是命中面所在的轴：0=x / 1=y / 2=z。
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
    """按贴图坐标取样（自动平铺）。u/v 单位是「几块贴图」。

    ⚠️ **v 轴朝下** —— v = 0 取的是图像顶行。所以贴具体图案（海报、门片）
    时要写成 (v1 - v) / (v1 - v0)，否则上下颠倒。
    """
    h, w = tex.shape[:2]
    xi = np.clip((np.mod(u, 1.0) * w).astype(np.int32), 0, w - 1)
    yi = np.clip((np.mod(v, 1.0) * h).astype(np.int32), 0, h - 1)
    return tex[yi, xi]


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


def big_text(size, text):
    """把 PIL 自带点阵字体放大成「大字」。

    自带字体只有 11 像素高，直接画太小；用 NEAREST 放大之后是方块字，
    当占位正好 —— 而且不牵扯任何字体授权。
    """
    font = ImageFont.load_default()
    tmp = Image.new("L", (size * 3, size), 0)
    ImageDraw.Draw(tmp).text((4, 4), text, fill=255, font=font)
    bbox = tmp.getbbox()
    if bbox is None:
        return Image.new("L", (1, 1), 0)
    tmp = tmp.crop(bbox)
    return tmp.resize((size, max(1, int(size * tmp.size[1] / tmp.size[0]))), Image.NEAREST)


def paper_grain(paper, size, amount):
    """纸纹：拿 paper 贴图的亮度做一个跟目标图同尺寸的调制层。"""
    w, h = size
    lum = np.asarray(
        Image.fromarray((np.clip(paper, 0, 1) * 255).astype(np.uint8))
        .resize((w, h), Image.LANCZOS)
    ).astype(np.float32) / 255.0
    lum = lum.mean(axis=2, keepdims=True)
    return 1.0 + (lum - lum.mean()) * amount


def load_character(code, pose="idle"):
    """读一张角色立绘（带 alpha）。缺了就返回 None —— 调用方退回旧的色块画法。"""
    path = os.path.join(CHAR_DIR, "char_%s_%s.png" % (code, pose))

    if not os.path.exists(path):
        print("  ! 缺立绘 char_%s_%s.png —— 海报里用色块代替" % (code, pose))
        return None

    return Image.open(path).convert("RGBA")


def tint_sprite(sprite, gain, tint):
    """给立绘整体压暗 + 染一点环境色，免得像直接贴上去的。"""
    arr = np.asarray(sprite).astype(np.float32) / 255.0
    arr[:, :, :3] = np.clip(arr[:, :, :3] * gain * np.asarray(tint, np.float32), 0, 1)
    return Image.fromarray((arr * 255).astype(np.uint8), "RGBA")


def make_celebrate_poster(paper, cast):
    """后墙上的「CELEBRATE!」海报 —— 用我们自己的吉祥物拼。

    占位版：砖底 + 一排等高、底对齐的角色 + 顶上一行大字。
    绿子画真的时覆盖同名文件即可。
    """
    im = Image.new("RGB", (512, 640), (74, 44, 40))
    d = ImageDraw.Draw(im)

    # 砖墙底
    for row in range(0, 640, 34):
        d.line([(0, row), (512, row)], fill=(52, 30, 28), width=3)
        for col in range((row // 34) % 2 * 46, 512, 92):
            d.line([(col, row), (col, row + 34)], fill=(52, 30, 28), width=3)

    # 顶上那行 CELEBRATE!
    t = big_text(300, "CELEBRATE!")
    band = Image.new("RGB", (512, 110), (60, 34, 30))
    band.paste(Image.new("RGB", t.size, (246, 214, 96)),
               ((512 - t.size[0]) // 2, max(0, (110 - t.size[1]) // 2)), t)
    im.paste(band, (0, 20))

    # 舞台
    stage = 540
    d.rectangle([0, stage, 512, 640], fill=(46, 28, 26))
    d.line([(0, stage), (512, stage)], fill=(92, 58, 50), width=4)

    # 角色：**按高度对齐**，不是按原始像素 —— 它们本来大小差很多，
    # 直接贴会有的顶天有的只剩一条。统一缩到同一个身高、底边对齐，
    # 看上去才像「并排站在台上」。
    usable = [s for s in cast if s is not None]
    if usable:
        body_h = 340
        slot = 512 // len(usable)

        for i, spr in enumerate(usable):
            scale = body_h / float(spr.height)
            w = max(1, int(spr.width * scale))
            s = tint_sprite(spr.resize((w, body_h), Image.LANCZOS), 0.88,
                            (1.0, 0.94, 0.86))
            cx = int(slot * (i + 0.5))
            im.paste(s, (cx - w // 2, stage - body_h + 26), s)
    else:
        # 没立绘就退回三个色块
        for cx, body in ((120, (108, 74, 58)), (256, (96, 66, 118)), (392, (186, 166, 96))):
            d.ellipse([cx - 54, stage - 200, cx + 54, stage - 92], fill=body)
            d.ellipse([cx - 60, stage - 110, cx + 60, stage - 4], fill=body)

    arr = np.asarray(im).astype(np.float32) / 255.0
    return np.clip(arr * paper_grain(paper, (512, 640), 1.2), 0, 1).astype(np.float32)


def crayon_sprite(sprite, size):
    """把一张立绘压成「小孩拿蜡笔画着照着画的」——

    降到几十像素、压成几档颜色、再用 NEAREST 放大，边缘就带上方块笔触；
    最后描一圈抖动的深色轮廓。小孩画的吉祥物差不多就是这个意思。

    ⚠️ **按「身高」统一，不是按外框塞满** —— 四个角色原始宽高比差很多
    （袋鼠又矮又宽、牛来又高又窄），按外框塞满的话每张画里的大小都不一样，
    一排贴出来像四个人画的。统一身高才像同一个小孩画的同一套。
    """
    box_w, box_h = size
    small = 46

    scale = small / float(max(sprite.width, sprite.height))
    s = sprite.resize((max(1, int(sprite.width * scale)),
                       max(1, int(sprite.height * scale))), Image.LANCZOS)

    # 先合到纸色上，再降色阶
    flat = Image.new("RGB", s.size, (198, 192, 176))
    flat.paste(s, (0, 0), s)
    flat = flat.quantize(colors=5, method=Image.MEDIANCUT).convert("RGB")

    # 轮廓：alpha 的边缘
    alpha = np.asarray(s.split()[3]).astype(np.int16)
    edge = np.zeros_like(alpha, dtype=bool)
    edge[1:, :] |= np.abs(alpha[1:, :] - alpha[:-1, :]) > 40
    edge[:, 1:] |= np.abs(alpha[:, 1:] - alpha[:, :-1]) > 40

    arr = np.asarray(flat).astype(np.float32)
    arr[edge] = np.array([58, 44, 40], np.float32)

    # 统一身高；宽得放不下时再按宽度缩回来
    scale = box_h / float(arr.shape[0])
    w = max(1, int(arr.shape[1] * scale))
    if w > box_w:
        scale = box_w / float(arr.shape[1])
        w = box_w
    h = max(1, int(arr.shape[0] * scale))

    big = Image.fromarray(arr.astype(np.uint8), "RGB").resize((w, h), Image.NEAREST)

    # 抖一圈手绘感的轮廓
    out = big.copy()
    dd = ImageDraw.Draw(out)
    pts = [(0, int(h * 0.72)), (int(w * 0.30), int(h * 0.94)),
           (int(w * 0.68), int(h * 0.90)), (w - 1, int(h * 0.74))]
    dd.line(pts, fill=(150, 96, 78), width=max(2, w // 90))
    return out


def make_drawing(paper, seed, sprite=None):
    """儿童画 —— 原版墙上贴了一整墙，每张顶上写着 MY FUN DAY!"""
    rng = np.random.default_rng(seed)
    im = Image.new("RGB", (512, 512), (198, 192, 176))
    d = ImageDraw.Draw(im)
    ink = (176, 74, 62)
    blue = (78, 108, 156)
    gold = (188, 152, 62)
    green = (96, 138, 92)

    t = big_text(300, "MY FUN DAY!")
    im.paste(Image.new("RGB", t.size, (196, 78, 62)),
             ((512 - t.size[0]) // 2, 12), t)

    if sprite is not None:
        # 画的就是我们的吉祥物 —— 压成蜡笔版本，贴在纸中间
        art = crayon_sprite(sprite, (360, 360))
        im.paste(art, (76, 128))
    else:
        d.ellipse([150, 150, 362, 362], outline=ink, width=15)
        d.ellipse([204, 218, 236, 250], fill=ink)
        d.ellipse([276, 218, 308, 250], fill=ink)
        d.arc([212, 262, 300, 324], 20, 160, fill=ink, width=13)

    # 底下几笔随手的涂鸦
    for col in (gold, green, blue):
        pts = [(int(rng.integers(24, 488)), int(rng.integers(440, 496))) for _ in range(4)]
        d.line(pts, fill=col, width=9, joint="curve")

    arr = np.asarray(im).astype(np.float32) / 255.0
    return np.clip(arr * paper_grain(paper, (512, 512), 1.6), 0, 1).astype(np.float32)


def make_vent_texture(tex):
    """通风口：金属底 + 横百叶。"""
    img = Image.fromarray((np.clip(tex, 0, 1) * 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for i in range(1, 6):
        y = 512 * i / 6.0
        d.line([(10, y), (502, y)], fill=(24, 26, 32), width=16)
        d.line([(10, y + 9), (502, y + 9)], fill=(120, 126, 140), width=4)
    d.rectangle([0, 0, 511, 511], outline=(140, 146, 160), width=10)
    return np.asarray(img).astype(np.float32) / 255.0


def make_door_texture(tex):
    """卷帘门：金属底 + 横条。"""
    img = Image.fromarray((np.clip(tex, 0, 1) * 255).astype(np.uint8))
    img = img.resize((256, 512), Image.LANCZOS)
    d = ImageDraw.Draw(img)
    for y in range(0, 512, 26):
        d.line([(0, y), (256, y)], fill=(30, 32, 38), width=7)
        d.line([(0, y + 8), (256, y + 8)], fill=(104, 110, 124), width=3)
    d.rectangle([0, 0, 255, 511], outline=(22, 24, 30), width=10)
    return np.asarray(img).astype(np.float32) / 255.0 * np.array([0.72, 0.75, 0.84], np.float32)


# ================================================================ 场景盒子

# 每只盒子：范围 + 每个面用什么贴图。faces 里没列到的面走 default。
# 相机在盒子内部（房间）时看的是「离开面」，在外部（桌子、桌上的设备）时看「进入面」。
BOXES = [
    dict(
        bmin=ROOM_MIN, bmax=ROOM_MAX,
        default=("wall", WALL_TINT, 2.0, 0.06),
        faces={
            (1, False): ("floor", FLOOR_TINT, 0.80, 0.05),    # 地板：黑白棋盘砖
            (1, True): ("concrete", CEIL_TINT, 2.4, 0.05),    # 天花板
        },
        band=True,                                            # 墙脚带棋盘墙裙
    ),
    # 办公桌
    dict(bmin=DESK_MIN, bmax=DESK_MAX,
         default=("concrete", DESK_TINT, 1.4, 0.05),
         faces={(1, True): ("concrete", DESK_TOP_TINT, 1.1, 0.05)}),
    # 桌上的两台 CRT 显示器
    dict(bmin=(-1.10, 0.80, 1.95), bmax=(-0.55, 1.30, 2.40),
         default=("metal", MONITOR_TINT, 0.7, 0.04)),
    dict(bmin=(-0.48, 0.80, 2.00), bmax=(-0.06, 1.22, 2.36),
         default=("metal", MONITOR_TINT, 0.7, 0.04)),
    # 右边的音箱
    dict(bmin=(1.18, 0.80, 1.95), bmax=(1.72, 1.48, 2.42),
         default=("metal", CABINET_TINT, 0.9, 0.04)),
    # 风扇的机身（网罩和扇叶另外用 2D 画）
    dict(bmin=(0.30, 0.80, 1.95), bmax=(0.62, 1.06, 2.30),
         default=("metal", MONITOR_TINT, 0.6, 0.04)),
]


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


def surface(tex, key, tint, u, v, tile, gain, grime=0.0, grime_tile=3.0):
    c = texel(tex[key], u / tile, v / tile) * tint
    if grime > 0.0:
        g = texel(GRIME[..., None], u / grime_tile, v / grime_tile)[..., 0]
        c = c * (1.0 + g[..., None] * grime)
    return c * gain


# ================================================================ 渲染

def render(cam, yaw, tex, seed, dark=0.64, exposure=1.0):
    """渲一张针孔视角图（W×H）。"""
    return render_dirs(look_rays(cam, yaw), tex, seed, dark, exposure,
                       W, H, lambda pt: project(pt, cam, yaw))


def render_dirs(dirs, tex, seed, dark, exposure, w, h, proj, vig=True):
    """按**调用方给的方向数组**渲染。

    这是原来 `render()` 的身体，只是把「针孔射线 + 透视投影」换成了参数。
    方向由调用方造（`look_rays` 针孔 / `cyl_rays` 圆柱全景），于是同一套
    场景、同一套光照和贴图，既能出视角图也能出全景条 —— 两者天然对齐，
    不存在「两批图风格不一样」的问题。

    `vig` 同时控制噪点、暗角和扁平贴图的角部蛛网（这些是**单幅画面**才有的
    效果，全景条的边缘不是画面边缘，不能照加）。

    ⚠️ **所有着色都只对命中的像素做**（`p[m]` 这种花式索引），不要整屏算 ——
    整屏算的话每个面都要在上百万像素上跑一遍贴图采样，慢十倍不止。
    """
    o = np.array(CAM, np.float32)

    # ---- 所有盒子一起求交，取最近的
    t = np.full((h, w), np.inf, np.float32)
    ax = np.zeros((h, w), np.int32)
    which = np.zeros((h, w), np.int32)

    for bi, box in enumerate(BOXES):
        tn, axn, tf, axf = box_t(o, dirs, box["bmin"], box["bmax"])
        inside = ~np.isfinite(tn)            # tn = -inf ⇒ 相机在盒子里
        tb = np.where(inside, tf, tn)
        ab = np.where(inside, axf, axn)

        hit = np.isfinite(tb) & (tb < t)
        if not hit.any():
            continue

        t = np.where(hit, tb, t)
        ax = np.where(hit, ab, ax)
        which = np.where(hit, bi, which)

    p = o + dirs * t[..., None]
    u, v = wall_uv_ax(ax, p)

    bmin_all = np.array([b["bmin"] for b in BOXES], np.float32)
    bmax_all = np.array([b["bmax"] for b in BOXES], np.float32)
    pv = np.take_along_axis(p, ax[..., None], axis=-1)[..., 0]
    lo = np.take_along_axis(bmin_all[which], ax[..., None], axis=-1)[..., 0]
    hi = np.take_along_axis(bmax_all[which], ax[..., None], axis=-1)[..., 0]
    on_max = np.abs(pv - hi) < np.abs(pv - lo)

    color = np.zeros((h, w, 3), np.float32)

    # ---- 逐盒子、逐面着色
    for bi, box in enumerate(BOXES):
        room = (bi == 0)
        for axis in range(3):
            for side_max in (False, True):
                m = (which == bi) & (ax == axis) & (on_max == side_max)
                if not m.any():
                    continue

                key, tint, tile, grime = box.get("faces", {}).get(
                    (axis, side_max), box["default"])

                # 法线：房间内壁朝屋里，外部物体朝外
                nm = np.zeros((int(m.sum()), 3), np.float32)
                nm[:, axis] = (-1.0 if side_max else 1.0) if room \
                    else (1.0 if side_max else -1.0)

                gain = light_at(p[m], nm)
                col = surface(tex, key, tint, u[m], v[m], tile, gain, grime)

                # 墙脚的棋盘墙裙（原版两墙都有这么一条）
                if box.get("band") and axis != 1 and not side_max:
                    band = m & (v < BAND_H)
                    if band.any():
                        bcol = surface(tex, "floor", BAND_TINT, u[band], v[band],
                                       0.30, light_at(p[band], nm[:int(band.sum())]),
                                       0.05)
                        col[band[m]] = bcol

                color[m] = col

    # ---- 门洞之外：把射线放进走廊再打一次
    for hole in HOLES:
        wall = (which == 0) & (ax == hole["axis"]) & (on_max == hole["side"])
        if not wall.any():
            continue
        sel = wall & (u > hole["u0"]) & (u < hole["u1"]) \
                   & (v > hole["v0"]) & (v < hole["v1"])
        if not sel.any():
            continue

        cmin, cmax = hole["corr"]
        o2 = p + dirs * 0.02
        _, _, t2, ax2 = box_t(o2, dirs, cmin, cmax)
        hit = np.isfinite(t2)
        p2 = o2 + dirs * np.where(hit, t2, 0.0)[..., None]

        n2 = np.zeros((h, w, 3), np.float32)
        for a in range(3):
            mm = (ax2 == a)
            if not mm.any():
                continue
            pv2 = p2[..., a]
            low = mm & (np.abs(pv2 - cmin[a]) < np.abs(pv2 - cmax[a]))
            n2[low, a] = 1.0
            n2[mm & (~low), a] = -1.0

        u2, v2 = wall_uv_ax(ax2, p2)
        gain = light_at(p2[sel], n2[sel]) * CORR_GAIN
        col = surface(tex, "wall", CORR_TINT, u2[sel], v2[sel], 2.0, gain, 0.06)
        color[sel] = np.where(hit[sel][:, None], col, 0.0)

    # ---- 门框
    for hole in HOLES:
        wall = (which == 0) & (ax == hole["axis"]) & (on_max == hole["side"])
        if not wall.any():
            continue
        inside = wall & (u > hole["u0"]) & (u < hole["u1"]) \
                      & (v > hole["v0"]) & (v < hole["v1"])
        ring = wall & (u > hole["u0"] - FRAME_W) & (u < hole["u1"] + FRAME_W) \
                    & (v > hole["v0"] - FRAME_W) & (v < hole["v1"] + FRAME_W) & (~inside)
        if ring.any():
            nm = np.zeros((int(ring.sum()), 3), np.float32)
            nm[:, hole["axis"]] = -1.0 if hole["side"] else 1.0
            gain = light_at(p[ring], nm) * 1.18
            color[ring] = surface(tex, "metal", FRAME_TINT, u[ring], v[ring],
                                  0.55, gain)

    # ---- 墙上的海报 / 儿童画
    for axis, side_max, u0, v0, u1, v1, dec in DECALS:
        m = (which == 0) & (ax == axis) & (on_max == side_max)
        if not m.any():
            continue
        sel = m & (u > u0) & (u < u1) & (v > v0) & (v < v1)
        if not sel.any():
            continue

        # 保险：**画一律不许压到门洞上**（含门框那一圈）。
        # 位置我已经排开了，这条是防止以后调坐标时又糊上去。
        for hole in HOLES:
            if hole["axis"] != axis or hole["side"] != side_max:
                continue
            blocked = (u > hole["u0"] - FRAME_W) & (u < hole["u1"] + FRAME_W) \
                      & (v > hole["v0"] - FRAME_W) & (v < hole["v1"] + FRAME_W)
            sel = sel & (~blocked)

        if not sel.any():
            continue

        nm = np.zeros((int(sel.sum()), 3), np.float32)
        nm[:, axis] = -1.0 if side_max else 1.0

        du = (u[sel] - u0) / (u1 - u0)
        dv = (v1 - v[sel]) / (v1 - v0)       # 贴图 v 轴朝下，得翻过来
        gain = light_at(p[sel], nm) * POSTER_GAIN
        col = texel(dec, du, dv) * gain
        edge = ((du < 0.015) | (du > 0.985) | (dv < 0.015) | (dv > 0.985))[:, None]
        color[sel] = np.where(edge, col * 0.42, col)

    # ---- 桌面上的零碎 + 吊灯 + 蛛网（投影成 2D 画上去）
    prop_img = Image.new("RGB", (w, h), (0, 0, 0))
    prop_mask = Image.new("L", (w, h), 0)
    draw_props(prop_img, prop_mask, CAM, 0.0, proj=proj, draw_flat=vig)
    pm = (np.asarray(prop_mask).astype(np.float32) / 255.0)[..., None]
    pc = np.asarray(prop_img).astype(np.float32) / 255.0
    color = color * (1.0 - pm) + pc * pm

    color = post(color, seed, dark, exposure, w, h, vig=vig)
    return Image.fromarray((color * 255).astype(np.uint8), "RGB") \
        .filter(ImageFilter.GaussianBlur(0.6))


def post(color, seed, dark, exposure, w, h, vig=True):
    """统一的收尾：曝光 → 色调映射 → 噪点 → 暗角 → 暗部偏冷。

    背景和门片都得走这一套 —— 不然门贴上去会像另一张图。
    `vig=False` 时只保留噪点/偏冷，不加暗角 —— 全景条的边缘不是画面边缘。
    """
    color = color * (dark * exposure)
    color = color / (1.0 + color) * 1.9

    rng = np.random.default_rng(seed)
    color = color + rng.normal(0.0, 0.016, (h, w, 1)).astype(np.float32)

    if vig:
        ys = (np.arange(h, dtype=np.float32) + 0.5) / h * 2.0 - 1.0
        xs = (np.arange(w, dtype=np.float32) + 0.5) / w * 2.0 - 1.0
        vx, vy = np.meshgrid(xs, ys)
        d = np.sqrt(vx * vx + vy * vy) / 1.41421356
        color = color * np.clip(1.0 - 0.62 * (d ** 2.1), 0.0, 1.0)[..., None]

    # 暗部偏冷 —— 冷房间 + 暖灯，是原版那股味
    lum = color.mean(axis=2, keepdims=True)
    shadow = np.clip(1.0 - lum * 2.4, 0.0, 1.0)
    color = color * (1.0 - shadow * 0.20) * np.array([0.92, 0.96, 1.10], np.float32)

    return np.clip(color, 0.0, 1.0)


# ================================================================ 2D 道具

def draw_props(col_img, mask_img, cam, yaw, proj=None, draw_flat=True):
    """投影出屏幕位置再 2D 画的零碎：扇叶网罩、纸杯、纸团、抽屉、吊灯、蛛网。

    这些东西几何太碎，用光线投射不划算。光照按底座那点算，颜色才跟房间对得上。

    `proj` 是投影函数（默认透视 `project`）。全景条把圆柱投影传进来，桌面零碎
    才会跟着一起贴到条子上。`draw_flat` 关掉只在**单幅画面**里才成立的东西 ——
    角部蛛网挂在画面角落，而全景条的边缘不是画面边缘。
    """
    d = ImageDraw.Draw(col_img)
    mk = ImageDraw.Draw(mask_img)
    if proj is None:
        proj = lambda pt: project(pt, cam, yaw)

    def visible(base):
        return depth_of(base, cam, yaw) > 0.05

    def scale_at(base, metres):
        _, y0 = proj(base)
        _, y1 = proj((base[0], base[1] + metres, base[2]))
        return abs(y0 - y1)

    def shade(base, tint, up=True):
        p = np.array([base], np.float32)
        n = np.array([[0.0, 1.0, 0.0] if up else [0.0, 0.0, -1.0]], np.float32)
        # 补一点环境补光 —— 桌子正面朝观众，点光打不到它，纯按点光算会是死黑
        g = light_at(p, n)[0] + 0.34
        return tuple(int(np.clip(v * 255.0, 0, 255)) for v in np.clip(tint * g, 0, 1))

    def ink(fn, colour):
        fn(d, colour)
        fn(mk, 255)

    # ---- 风扇：网罩 + 扇叶（机身是盒子）
    fan = (0.46, 1.00, 2.12)
    if visible(fan):
        fx, fy = proj(fan)
        r = scale_at(fan, 0.28)
        edge = shade(fan, np.array([0.40, 0.43, 0.52], np.float32))
        dark = shade(fan, np.array([0.10, 0.11, 0.14], np.float32))

        ink(lambda i, c: i.ellipse([fx - r, fy - r, fx + r, fy + r], outline=c, width=8), edge)
        ink(lambda i, c: i.ellipse([fx - r * 0.72, fy - r * 0.72,
                                    fx + r * 0.72, fy + r * 0.72], outline=c, width=4), dark)
        for k in range(8):
            a = math.pi * k / 4.0
            ink(lambda i, c, a=a: i.line(
                [(fx, fy), (fx + math.cos(a) * r, fy + math.sin(a) * r)],
                fill=c, width=3), dark)
        ink(lambda i, c: i.ellipse([fx - r * 0.18, fy - r * 0.18,
                                    fx + r * 0.18, fy + r * 0.18], fill=c), edge)

    # ---- 纸杯（原版那个红白条纹的）
    cup = (-0.45, 0.80, 1.78)
    if visible(cup):
        cx, cy = proj(cup)
        hw = scale_at(cup, 0.055)
        body = shade(cup, np.array([0.52, 0.50, 0.48], np.float32))
        ink(lambda i, c: i.polygon(
            [(cx - hw, cy), (cx + hw, cy),
             (cx + hw * 0.82, cy - hw * 2.2), (cx - hw * 0.82, cy - hw * 2.2)], fill=c), body)

    # ---- 两团揉皱的纸
    for base, hw in (((-0.72, 0.80, 1.76), 0.055), ((0.86, 0.80, 1.84), 0.050)):
        if not visible(base):
            continue
        px, py = proj(base)
        s = scale_at(base, hw)
        pale = shade(base, np.array([0.86, 0.86, 0.84], np.float32))
        ink(lambda i, c, px=px, py=py, s=s: i.polygon(
            [(px - s, py), (px - s * 0.4, py - s * 0.9), (px + s * 0.6, py - s * 0.8),
             (px + s, py + s * 0.1), (px + s * 0.3, py + s * 0.6),
             (px - s * 0.7, py + s * 0.5)], fill=c), pale)

    # ---- 桌子正面：两格抽屉 + 拉手
    front = (0.0, 0.42, 1.60)
    if visible(front):
        ax0, ay0 = proj((-1.35, 0.72, 1.60))
        ax1, ay1 = proj((0.45, 0.10, 1.60))
        box = [min(ax0, ax1), min(ay0, ay1), max(ax0, ax1), max(ay0, ay1)]
        cab = shade(front, np.array([0.26, 0.27, 0.31], np.float32), up=False)
        ink(lambda i, c: i.rectangle(box, fill=c), cab)

        edge = shade(front, np.array([0.66, 0.67, 0.72], np.float32), up=False)
        handle = shade(front, np.array([0.88, 0.89, 0.94], np.float32), up=False)
        for k in range(2):
            y0 = box[1] + (box[3] - box[1]) * (0.06 + k * 0.48)
            y1 = box[1] + (box[3] - box[1]) * (0.46 + k * 0.48)
            ink(lambda i, c, y0=y0, y1=y1: i.rectangle(
                [box[0] + 8, y0, box[2] - 8, y1], outline=c, width=4), edge)
            my = (y0 + y1) * 0.5
            ink(lambda i, c, my=my: i.rectangle(
                [box[0] + 34, my - 5, box[2] - 34, my + 5], fill=c), handle)

    # ---- 吊灯：一根吊杆 + 红罩 + 发亮的灯泡
    if visible(LAMP):
        lx, ly = proj(LAMP)
        r = scale_at(LAMP, 0.30)
        top_y = proj((LAMP[0], 2.60, LAMP[2]))[1]
        rod = shade(LAMP, np.array([0.34, 0.34, 0.38], np.float32), up=False)
        ink(lambda i, c: i.rectangle([lx - r * 0.06, top_y, lx + r * 0.06, ly - r * 0.2],
                                     fill=c), rod)
        ink(lambda i, c: i.ellipse([lx - r, ly - r * 0.85, lx + r, ly + r * 0.85],
                                   fill=c), (168, 62, 44))
        ink(lambda i, c: i.ellipse([lx - r * 0.34, ly - r * 0.30,
                                    lx + r * 0.34, ly + r * 0.34], fill=c), (252, 238, 198))

    # ---- 天花板两个角的蛛网（原版画面里就有；只有单幅画面才画）
    if draw_flat:
        for sx, flip in ((0.0, 1.0), (1.0, -1.0)):
            cxp, cyp = sx * W, 0.0
            for i in range(1, 6):
                rr = i * W * 0.038
                bb = [min(cxp, cxp + flip * rr), cyp,
                      max(cxp, cxp + flip * rr), cyp + rr]
                mk.arc(bb, 0, 90 if flip > 0 else 180, fill=64)
                d.arc(bb, 0, 90 if flip > 0 else 180, fill=(148, 150, 158))
            for k in range(5):
                a = math.radians(k * 22.5)
                x1 = cxp + flip * math.cos(a) * W * 0.21
                y1 = cyp + math.sin(a) * H * 0.38
                mk.line([(cxp, cyp), (x1, y1)], fill=64, width=2)
                d.line([(cxp, cyp), (x1, y1)], fill=(138, 140, 148), width=2)


# ================================================================ 门片

def build_door(hole, cam, yaw, tex, closed, seed, dark=0.64, exposure=1.0):
    """叠层门片。整屏透明画布，门只画在门洞那个四边形里。

    门是**跟背景同一条管线渲出来的** —— 同样的光线投射、同样的光照和后期。
    直接贴一张不受光的贴图上去，门会比墙亮一大截，一眼假。
    """
    canvas = np.zeros((H, W, 4), np.float32)
    if not closed:
        return Image.fromarray((canvas * 255).astype(np.uint8), "RGBA")

    dirs = look_rays(cam, yaw)
    o = np.array(cam, np.float32)

    _, _, t, axf = box_t(o, dirs, ROOM_MIN, ROOM_MAX)     # 相机在屋里 → 看离开面
    p = o + dirs * t[..., None]

    on_max = np.abs(p[..., hole["axis"]] - hole["plane"]) < 1e-3
    wall = (axf == hole["axis"]) & (on_max == hole["side"])
    u, v = wall_uv(hole["axis"], p)
    sel = wall & (u > hole["u0"]) & (u < hole["u1"]) & (v > hole["v0"]) & (v < hole["v1"])

    # 桌子挡在门前就看不见门
    tn_d, _, _, _ = box_t(o, dirs, DESK_MIN, DESK_MAX)
    sel = sel & ~(np.isfinite(tn_d) & (tn_d < t))

    if not sel.any():
        return Image.fromarray((canvas * 255).astype(np.uint8), "RGBA")

    n = np.zeros((H, W, 3), np.float32)
    n[..., hole["axis"]] = -1.0 if hole["side"] else 1.0
    gain = light_at(p, n)

    du = (u - hole["u0"]) / (hole["u1"] - hole["u0"])
    dv = (hole["v1"] - v) / (hole["v1"] - hole["v0"])
    col = post(texel(DOOR_TEX, du, dv) * gain, seed, dark, exposure)

    canvas[sel, :3] = col[sel]
    canvas[sel, 3] = 1.0
    return Image.fromarray((np.clip(canvas, 0, 1) * 255).astype(np.uint8), "RGBA")


# ================================================================ 主流程

# 转头帧数 —— 每侧这么多帧走完 90°。帧数越多转得越顺（代码在相邻两帧之间
# 交叉淡化），但每帧都是一张 1920×1080 的图，别贪多。
TURN_FRAMES = 10

VIEWS = [("front", 0.0, 11)]
for _i in range(1, TURN_FRAMES + 1):
    VIEWS.append(("left_%02d" % _i, -math.pi / 2 * _i / TURN_FRAMES, 100 + _i))
for _i in range(1, TURN_FRAMES + 1):
    VIEWS.append(("right_%02d" % _i, math.pi / 2 * _i / TURN_FRAMES, 200 + _i))

VENT_TEX = None
DOOR_TEX = None
DECALS = []
_TEX = {}


def build_decals(tex):
    """墙上的海报与儿童画 —— 位置照原版：海报偏左，儿童画挤在它右边。

    ⚠️ **后墙中间是正门（前墙门洞 u ∈ [-1.1, 1.1]、v ∈ [0, 2.1]），画一律不许压上去。**
    原版后墙是整面实墙、想怎么贴怎么贴；我们这里正中间开了门给噜噜走，
    再照原版那样把画铺满中间，画就糊在门洞上了（门洞那块已经被走廊覆盖）。
    所以后墙的画只挂门洞**左边、右边和门楣上方**。
    """
    global DECALS
    paper = tex["paper"]

    sprites = [load_character(code) for code in CAST]
    celebrate = make_celebrate_poster(paper, sprites)

    have = [s for s in sprites if s is not None]
    draws = []
    for i, s in enumerate((3, 5, 7, 11, 13, 17, 19, 23)):
        draws.append(make_drawing(paper, s, have[i % len(have)] if have else None))

    DECALS = [
        # 后墙（u = x, v = y）：门洞左边挂海报，右边挂一墙儿童画，门楣上一个
        #
        # ⚠️ **儿童画的框一律是正方形**（宽高相等）。
        # 画的内容是 512×512 的方图，框不是方的话会被拉变形，
        # 同一张画里的角色就一会儿胖一会儿瘦 —— 一排贴出来比例全乱。
        # 海报是 512×640，框按 0.8 的宽高比来。
        (2, True, -2.54, 1.02, -1.37, 2.48, celebrate),
        (2, True, -2.54, 0.62, -2.08, 1.08, draws[0]),
        (2, True, -2.00, 0.66, -1.54, 1.12, draws[1]),
        (2, True, -0.32, 2.24, 0.04, 2.60, draws[2]),
        (2, True, 1.12, 1.70, 1.62, 2.20, draws[3]),
        (2, True, 1.68, 1.58, 2.18, 2.08, draws[4]),
        (2, True, 1.14, 1.14, 1.64, 1.64, draws[5]),
        (2, True, 1.70, 1.06, 2.20, 1.56, draws[6]),
        (2, True, 2.24, 1.46, 2.58, 1.80, draws[7]),
        # 左墙（u = z, v = y）—— 左门在 u ∈ [-3.2, -2.0]，画挂在它前面那截墙上
        (0, False, -1.12, 1.30, -0.56, 1.86, draws[0]),
        (0, False, -0.48, 1.46, -0.02, 1.92, draws[2]),
        # 右墙
        (0, True, -1.12, 1.30, -0.56, 1.86, draws[1]),
        (0, True, -0.48, 1.46, -0.02, 1.92, draws[3]),
    ]


def main():
    global VENT_TEX, DOOR_TEX, _TEX

    os.makedirs(OUT, exist_ok=True)
    os.makedirs(DOOR_OUT, exist_ok=True)

    print("读贴图 …")
    _TEX = load_textures()
    tex = _TEX
    VENT_TEX = make_vent_texture(tex["metal"])
    DOOR_TEX = make_door_texture(tex["metal"])
    build_decals(tex)

    # 背景不再一张张渲了 —— 背景改成「全景条 + 旋转相机」（tools/render_pano.py），
    # 转头是相机真在转，不是若干张图交叉淡化。这里只留门片。
    print("渲门片（%d 张）…" % (TURN_FRAMES * 2))
    for i in range(1, TURN_FRAMES + 1):
        path = os.path.join(DOOR_OUT, "ui_door_left_closed_%02d.png" % i)
        build_door(LEFT_HOLE, CAM, -math.pi / 2 * i / TURN_FRAMES, tex, True,
                   300 + i).save(path)
    for i in range(1, TURN_FRAMES + 1):
        path = os.path.join(DOOR_OUT, "ui_door_right_closed_%02d.png" % i)
        build_door(RIGHT_HOLE, CAM, math.pi / 2 * i / TURN_FRAMES, tex, True,
                   400 + i).save(path)
    print("  wrote %d 张门片" % (TURN_FRAMES * 2))

    # 门开着时整层透明。旧命名保留成空图，免得老引用报错。
    blank = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for name in ("ui_door_left_open.png", "ui_door_right_open.png",
                 "ui_door_left_closed.png", "ui_door_right_closed.png"):
        blank.save(os.path.join(DOOR_OUT, name))


if __name__ == "__main__":
    main()

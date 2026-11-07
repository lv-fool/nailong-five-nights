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

# ⚠️ 预览目录，不直接覆盖 Assets/Art/Monitoring/（那里是项目作者给的 AI 图）。
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
CARTON_TINT = np.array([0.46, 0.36, 0.24], np.float32)   # 纸箱


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
    """一只玩偶的占位：身体 + 头 + 两只耳朵（真素材等项目作者给）。"""
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
    parts = [
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

    # ── 2026-11-07 加的场景细节：舞台不能只是一块空台面 ──

    # 台沿的一圈踢脚（让台面有厚度，不然像贴在地上的一块色板）
    parts.append(box((-3.6, 0.00, -1.60), (3.6, 0.12, -1.52),
                     ("metal", CABINET_TINT, 0.9, 0.10)))

    # 两侧的幕布竖边（把幕布和墙分开，舞台才有"框"）
    for sx in (-1.0, 1.0):
        x0 = 3.42 * sx
        x1 = 3.60 * sx
        lo, hi = min(x0, x1), max(x0, x1)
        parts.append(box((lo, 0.45, -2.95), (hi, 2.75, -1.55),
                         ("wall", CURTAIN_DARK, 1.2, 0.16)))

    # 舞台上方的顶幕（横着一条深红，压住画面上沿）
    parts.append(box((-3.6, 2.55, -2.98), (3.6, 2.85, -1.80),
                     ("wall", CURTAIN_DARK, 1.0, 0.20)))

    # 舞台灯（顶部中央一盏，带一点自发光感 —— 用亮色小方块表示）
    parts.append(box((-0.42, 2.88, -2.70), (0.42, 3.00, -2.30),
                     ("metal", np.array([0.80, 0.74, 0.60], np.float32), 2.2, 0.0)))
    # 灯罩（比灯暗一档，做出体积）
    parts.append(box((-0.55, 2.72, -2.76), (0.55, 2.90, -2.24),
                     ("metal", CABINET_TINT, 1.0, 0.08)))

    # 台前地面：一块深色地毯（区分观众区和过道）
    parts.append(box((-3.2, 0.00, -1.50), (3.2, 0.02, 0.90),
                     ("wall", np.array([0.22, 0.16, 0.15], np.float32), 1.3, 0.14)))

    # ── 观众席：**不放椅子、不摆近景道具** ──
    #
    # 试过四版：椅子 z=2.3 / 1.0 / 0.9（亮度 0.52→0.82→自发光 2.4）、
    # 隔离柱 z=-1.2。全部失败。
    #
    # ⚠️ **原因是几何，不是参数**：相机在 (0,1.95,3.30) 眼高 1.95m、FOV 74°，
    # 实测投影（画面 1920×1080）：
    #     z=2.0 → 地面 y=2010   （远在画面下方）
    #     z=1.0 → 地面 y=1371   （还是画面外）
    #     z=0.0 → 地面 y=1119   （刚出界）
    #     z=-1.0→ 地面 y= 984   ← 从这里开始才进画面
    # 也就是说**舞台前面那片地面根本拍不到**，往前放什么都在画框外。
    #
    # 结论：这个机位的纵深只能靠舞台自身（幕布 / 灯光 / 台面）撑，
    # 不能靠前景道具。FNAF 原版 CAM 1A 也是这么处理的。
    # 要加东西就往 **z ≤ -1.0** 的可见区里加 —— 见下面那几条。

    # ── 可见区内的道具（z ≤ -1.0）──
    #
    # ⚠️ **x 的符号和屏幕方向相反**：相机 yaw=180°（朝 -z 看），
    # 实测 x=-3 → 屏幕 x=1619（右侧），x=+3 → 屏幕 x=301（左侧）。
    # 写坐标时按"世界 x"来想，别看屏幕。

    # 功放机柜（世界 x 负 = 画面右）
    parts.append(box((-3.50, 0.45, -2.95), (-3.05, 1.45, -2.40),
                     ("metal", CABINET_TINT, 0.9, 0.10)))
    parts.append(box((-3.46, 1.20, -2.42), (-3.09, 1.32, -2.38),
                     ("metal", np.array([0.30, 0.78, 0.36], np.float32), 2.6, 0.0)))

    # 一摞纸箱（世界 x 正 = 画面左）
    parts.append(box((3.05, 0.45, -2.90), (3.55, 1.05, -2.35),
                     ("paper", CARTON_TINT, 1.5, 0.10)))
    parts.append(box((3.12, 1.05, -2.80), (3.48, 1.45, -2.42),
                     ("paper", CARTON_TINT, 1.6, 0.10)))

    # 台面中间：一块深色地垫（划出"表演区"，让空台面有分区）
    parts.append(box((-1.30, 0.45, -2.85), (1.30, 0.47, -1.70),
                     ("wall", np.array([0.16, 0.11, 0.12], np.float32), 1.4, 0.12)))

    # 左右墙上方各一盏壁灯（暖光小方块，增加"有人维护"的感觉）
    for sx in (-1.0, 1.0):
        x0 = 3.90 * sx
        x1 = 3.98 * sx
        lo, hi = min(x0, x1), max(x0, x1)
        parts.append(box((lo, 2.10, -0.30), (hi, 2.42, 0.10),
                         ("metal", np.array([0.72, 0.66, 0.50], np.float32), 1.8, 0.05)))

    return parts


# ================================================================ 2A 西走廊
# 走廊 2.4m 宽、9m 长、2.4m 高，尽头是转角（这一版渲到转角墙为止）。
def boxes_2a():
    parts = [
        # 窄长走廊：2m 宽、12m 长、2.4m 高（FNAF 的走廊就是这种又窄又长的）
        box((-1.0, 0.0, -6.0), (1.0, 2.4, 6.0),
            ("wall", WALL, 2.0, 0.14), band=True),
    ]

    # ── 2026-11-07 加的场景细节 ──
    #
    # ⚠️ 走廊只有 2m 宽，往里塞东西会挡住通行视觉，
    # 所以只加**贴墙的**：踢脚线、天花板灯、地脚通风口。

    # 两侧踢脚线（一条深色窄带，让墙和地面分开）
    for sx in (-1.0, 1.0):
        x0, x1 = sorted((-0.98 * sx, -1.00 * sx))
        parts.append(box((x0, 0.00, -6.0), (x1, 0.12, 6.0),
                         ("metal", np.array([0.20, 0.18, 0.16], np.float32), 1.0, 0.10)))

    # 天花板灯（每隔 3m 一盏，暖白 — 走廊的光源感受）
    for z in (-4.5, -1.5, 1.5, 4.5):
        parts.append(box((-0.22, 2.34, z - 0.30), (0.22, 2.40, z + 0.30),
                         ("metal", np.array([0.86, 0.82, 0.68], np.float32), 2.2, 0.0)))

    # 墙上的通风口（两侧各两个，贴高处）
    for sx in (-1.0, 1.0):
        x0, x1 = sorted((-0.96 * sx, -1.00 * sx))
        for z in (-2.2, 2.2):
            parts.append(box((x0, 1.55, z - 0.28), (x1, 1.92, z + 0.28),
                             ("metal", np.array([0.26, 0.24, 0.22], np.float32), 1.2, 0.08)))

    return parts


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
        # 长桌（往房间深处挪 —— 贴着镜头摆会被透视压扁成一条边）
        box((-2.6, 0.0, -2.05), (2.6, 0.76, -0.95),
            ("wall", DESK_TOP_TINT, 1.4, 0.10)),
    ]

    # 两侧椅子（座 + 靠背），简化的派对椅
    for x in (-1.8, 0.0, 1.8):
        for side in (-1.0, 1.0):
            z0 = -0.95 if side > 0 else -2.55
            parts.append(box((x - 0.24, 0.0, z0), (x + 0.24, 0.45, z0 + 0.5),
                             ("wall", CHAIR_TINT, 1.2, 0.08)))
            zb = z0 + (0.42 if side > 0 else 0.0)
            parts.append(box((x - 0.24, 0.45, zb), (x + 0.24, 1.05, zb + 0.08),
                             ("wall", CHAIR_TINT, 1.2, 0.08)))

    # ── 2026-11-07 加的场景细节 ──

    # 长桌上的派对用品（FNAF 餐厅的标志：纸盘、杯子、彩旗）
    PLATE = np.array([0.72, 0.70, 0.62], np.float32)
    CUP = np.array([0.78, 0.30, 0.28], np.float32)
    for x in (-2.0, -0.9, 0.4, 1.6):
        parts.append(box((x - 0.12, 0.76, -1.45), (x + 0.12, 0.80, -1.25),
                         ("paper", PLATE, 1.8, 0.04)))
    for x in (-1.5, 0.0, 1.2):
        parts.append(box((x - 0.05, 0.76, -1.30), (x + 0.05, 0.92, -1.20),
                         ("paper", CUP, 1.9, 0.04)))

    # 墙上垂下来的彩旗（三角旗，三种颜色交替 —— 单一红看着像一排砖）
    FLAG_COLORS = (
        np.array([0.66, 0.22, 0.20], np.float32),   # 红
        np.array([0.28, 0.42, 0.62], np.float32),   # 蓝
        np.array([0.68, 0.58, 0.24], np.float32),   # 黄
    )
    for i, x in enumerate(range(-4, 5)):
        fx = x * 0.85
        # 高低错落一点，别在一条直线上
        top = 2.34 + (0.06 if i % 2 else 0.0)
        parts.append(box((fx - 0.26, top, -4.96), (fx + 0.26, top + 0.26, -4.90),
                         ("paper", FLAG_COLORS[i % 3], 1.7, 0.05)))
    # 挂旗的那条细绳
    parts.append(box((-3.60, 2.58, -4.97), (3.60, 2.62, -4.93),
                     ("metal", np.array([0.34, 0.30, 0.26], np.float32), 1.4, 0.06)))

    # 房间四角的立柱（撑起天花板，让 6m 宽的大厅不显得悬空）
    for x in (-5.6, 5.6):
        parts.append(box((x - 0.18, 0.0, -4.6), (x + 0.18, 3.2, -4.24),
                         ("wall", BAND, 1.6, 0.10)))

    # 靠后墙的一排储物柜（FNAF 餐厅常见的金属柜）
    for i, x in enumerate((-4.6, -3.9)):
        parts.append(box((x - 0.30, 0.0, -4.85), (x + 0.30, 1.75, -4.45),
                         ("metal", CABINET_TINT, 1.1, 0.10)))

    return parts


# ================================================================ 1C 海盗湾
# 小房间，紫色幕布 + 「OUT OF ORDER」告示。噜噜以前蹲这儿。
def boxes_1c():
    parts = [
        box((-3.2, 0.0, -3.0), (3.2, 3.0, 3.0),
            ("wall", WALL, 2.2, 0.12), band=True),
        # 紫色幕布（占满后墙）
        box((-2.6, 0.0, -3.0), (2.6, 2.7, -2.9),
            ("wall", COVE_CURTAIN, 1.1, 0.16)),
        # 台座（幕布前面那级台阶）
        box((-2.6, 0.0, -2.9), (2.6, 0.35, -2.2),
            ("wall", STAGE_TINT, 1.5, 0.10)),
    ]

    # ── 2026-11-07 加的场景细节 ──
    #
    # 海盗湾（Pirate Cove）在 FNAF 里靠三样东西认出来：
    #   紫色幕布 / 「OUT OF ORDER」告示 / 幕布前的帘杆 + 提示牌。
    # 只有幕布太素了，补后两样。

    # 幕布上沿的帘杆（横杆 + 两端球头，让幕布"挂"起来而不是贴上去）
    parts.append(box((-2.75, 2.72, -2.96), (2.75, 2.86, -2.84),
                     ("metal", np.array([0.46, 0.38, 0.30], np.float32), 1.6, 0.08)))
    for sx in (-1.0, 1.0):
        x0, x1 = sorted((2.70 * sx, 2.90 * sx))
        parts.append(box((x0, 2.64, -2.98), (x1, 2.94, -2.82),
                         ("metal", np.array([0.52, 0.44, 0.34], np.float32), 1.8, 0.06)))

    # 「OUT OF ORDER」告示牌：
    # ⚠️ **牌面不在这里画 3D 方块** —— 文字靠 DECALS 贴在后墙上（见 render_shot）。
    # 第一版既画了 3D 纸板、又挂了 DECAL，两个叠在同一位置，
    # 3D 纸板把贴图整个挡住，看着就是一块空白的黄板（踩过）。
    # 这里什么都不画，牌面完全交给 DECAL。

    # 台座上的道具（散落的玩具箱，暗示"有东西在里面"）
    parts.append(box((-2.10, 0.35, -2.75), (-1.30, 0.85, -2.35),
                     ("paper", CARTON_TINT, 1.4, 0.10)))
    parts.append(box((1.35, 0.35, -2.70), (2.05, 0.78, -2.32),
                     ("paper", CARTON_TINT, 1.5, 0.10)))

    # 两侧墙的壁灯（暗黄，海盗湾的"廉价主题照明"）
    for sx in (-1.0, 1.0):
        x0, x1 = sorted((3.10 * sx, 2.92 * sx))
        parts.append(box((x0, 1.90, -0.40), (x1, 2.20, 0.10),
                         ("metal", np.array([0.70, 0.58, 0.34], np.float32), 2.0, 0.04)))

    return parts


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
        # 墙角的柜子（别让转角只有一堵空墙）
        box((-1.45, 0.0, -1.45), (-0.85, 1.30, -0.95),
            ("metal", CABINET_TINT, 1.0, 0.12)),
        # 地上的纸箱
        box((0.85, 0.0, -1.40), (1.40, 0.62, -0.85),
            ("paper", CARTON_TINT, 1.1, 0.14)),
    ]


# ================================================================ 3 储藏室
# 小隔间：拖把、水桶、几层架子（都放在远离相机的那头）。
def boxes_3():
    # ⚠️ 储藏室的柜子原本用 CABINET_TINT，和墙（WALL）几乎同色，
    # 渲出来整个人糊进背景里 —— 看着就是"一间空屋子"（用户反馈）。
    # 这里全部换成**暖木色**，靠色相区分，不靠亮度。
    SHELF_WOOD = np.array([0.52, 0.38, 0.26], np.float32)
    BUCKET = np.array([0.34, 0.40, 0.46], np.float32)

    # ⚠️ 构图约束（实测投影，相机 (0,1.80,1.85) FOV 72°、画面 1080 高）：
    #     地面 z=-2.0 → 屏幕 y=1158（已经在画面外了）
    # 也就是**地面基本拍不到**，只能看到后墙 y≈0.5 以上的部分。
    # 所以道具一律**靠后墙、往上放**，别摆在地上 —— 摆了也看不见。
    parts = [
        box((-1.4, 0.0, -2.2), (1.4, 2.4, 2.2),
            ("wall", WALL, 1.8, 0.16), band=True),
        # 架子：**背板 + 两侧立板**，不是一整块实心方块。
        # ⚠️ 第一版做成实心 box，结果隔板和瓶子全埋在里面看不见（踩过）。
        # 现在只留 6cm 厚的背板，前面敞开，隔板和瓶罐才露得出来。
        box((-1.25, 0.15, -2.15), (-0.20, 2.00, -2.09),
            ("wall", SHELF_WOOD, 1.9, 0.06)),                 # 背板
        box((-1.25, 0.15, -2.09), (-1.19, 2.00, -1.55),
            ("wall", SHELF_WOOD, 1.95, 0.05)),                # 左侧板
        box((-0.26, 0.15, -2.09), (-0.20, 2.00, -1.55),
            ("wall", SHELF_WOOD, 1.95, 0.05)),                # 右侧板
        # 右侧的窄柜
        box((0.55, 0.15, -2.15), (1.25, 1.75, -1.60),
            ("wall", SHELF_WOOD, 1.85, 0.06)),
        # 右柜顶上的纸箱
        box((0.62, 1.75, -2.10), (1.15, 2.15, -1.72),
            ("paper", CARTON_TINT, 1.8, 0.06)),
    ]

    # ── 2026-11-07 加的细节 ──

    # 三层隔板（横跨两块侧板之间，前面到 -1.60 —— 让开口有深度）
    for yy in (0.55, 1.05, 1.55):
        parts.append(box((-1.19, yy, -2.09), (-0.26, yy + 0.06, -1.60),
                         ("wall", np.array([0.68, 0.54, 0.38], np.float32), 2.2, 0.02)))
    # 每层上的瓶罐（颜色区分，别全一样）
    JAR_COLORS = (
        np.array([0.34, 0.50, 0.38], np.float32),   # 绿
        np.array([0.52, 0.36, 0.24], np.float32),   # 棕
        np.array([0.40, 0.44, 0.56], np.float32),   # 蓝
    )
    for row, yy in enumerate((0.61, 1.11, 1.61)):
        for i, bx in enumerate((-1.00, -0.72, -0.44)):
            h = 0.24 + (i * 0.05)
            # 放在隔板前缘（z 更靠近相机），否则被上层隔板挡住
            parts.append(box((bx - 0.10, yy, -1.92), (bx + 0.10, yy + h, -1.64),
                             ("metal", JAR_COLORS[(row + i) % 3], 2.1, 0.02)))

    # 后墙的挂钩 + 挂着的抹布（储藏室该有的生活感，挂在架子右侧空墙）
    for i, bx in enumerate((-0.05, 0.25)):
        parts.append(box((bx - 0.04, 1.55, -2.18), (bx + 0.04, 1.68, -2.12),
                         ("metal", np.array([0.50, 0.46, 0.38], np.float32), 2.0, 0.05)))
        parts.append(box((bx - 0.15, 1.15, -2.17), (bx + 0.15, 1.55, -2.10),
                         ("paper", np.array([0.48, 0.44, 0.40], np.float32), 1.8, 0.05)))

    return parts


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


# ================================================================ 告示牌贴图
def make_sign_text(lines, size=(512, 320), bg=(0.92, 0.86, 0.64),
                   fg=(0.78, 0.10, 0.08), border=True):
    """生成一块带文字的告示牌贴图（DECALS 用）。

    为什么不用 gpa.make_drawing：那个是「儿童蜡笔画」生成器，
    画不出规整的印刷字。告示牌要的是「印刷体 + 旧纸」，
    直接 PIL 画字 + 加噪点最省事。

    返回 float32 [0,1] 的 RGB 数组（DECALS 接受这种格式）。
    """
    from PIL import Image as _Im, ImageDraw as _Dr, ImageFont as _Ft
    import numpy as _np

    w, h = size
    img = _Im.new("RGB", (w, h), tuple(int(c * 255) for c in bg))
    d = _Dr.Draw(img)

    # 边框（旧告示牌那圈红/黑边）
    if border:
        bw = max(3, w // 60)
        d.rectangle([0, 0, w - 1, h - 1], outline=tuple(int(c * 255) for c in fg), width=bw)
        d.rectangle([bw * 2, bw * 2, w - 1 - bw * 2, h - 1 - bw * 2],
                    outline=tuple(int(c * 255) for c in fg), width=max(2, bw // 2))

    # 文字：按行数自动选字号
    n = max(1, len(lines))
    fs = int(h / (n * 1.9 + 0.6))
    font = None
    for path in ("C:/Windows/Fonts/impact.ttf", "C:/Windows/Fonts/arialbd.ttf"):
        try:
            font = _Ft.truetype(path, fs)
            break
        except Exception:
            continue
    if font is None:
        font = _Ft.load_default()

    total = n * fs * 1.35
    y = (h - total) / 2
    for ln in lines:
        try:
            tw = d.textlength(ln, font=font)
        except Exception:
            tw = len(ln) * fs * 0.6
        d.text(((w - tw) / 2, y), ln, font=font, fill=tuple(int(c * 255) for c in fg))
        y += fs * 1.35

    # ⚠️ **水平翻转**：DECAL 的 u 轴方向和贴图 x 轴相反，
    # 不翻的话文字是镜像的（实测「OUT OF ORDER」印成反字，踩过）。
    # 只在这里翻，不去动通用 DECAL 逻辑 —— 那样走廊的儿童画会跟着反。
    img = img.transpose(_Im.FLIP_LEFT_RIGHT)

    arr = _np.asarray(img).astype(_np.float32) / 255.0

    # 做旧：加噪点 + 边缘压暗（和新贴图风格一致）
    rng = _np.random.default_rng(4242)
    arr += rng.normal(0.0, 0.018, arr.shape)
    yy, xx = _np.mgrid[0:h, 0:w]
    edge = _np.minimum(_np.minimum(xx, w - 1 - xx), _np.minimum(yy, h - 1 - yy))
    fade = _np.clip(edge / (min(w, h) * 0.10), 0, 1) * 0.25 + 0.75
    arr *= fade[:, :, None]

    return _np.clip(arr, 0, 1).astype(_np.float32)


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
    elif name == "1c":
        # 海盗湾的「OUT OF ORDER」告示牌 —— FNAF 里这个机位就是靠它认出来的。
        #
        # ⚠️ 要贴在**幕布表面**（z=-2.9），不是房间后墙（z=-3.0）。
        # DECAL 只对「射线打到的那个盒子」生效 —— 挂后墙的话，
        # 射线先撞到前面的幕布（盒子 1，z 从 -3.0 到 -2.9），
        # 命中的是幕布，告示牌一个像素都画不出来（踩过）。
        #
        # 所以这里传的是**幕布的盒子**：axis=0（z 方向的墙）、max 侧、u=z=-2.9。
        sign = make_sign_text(["OUT OF", "ORDER"])
        # 8 元组：盒子索引 1 = 幕布（见 boxes_1c）
        #
        # ⚠️ **axis 是「面朝哪个轴」不是「盒子排第几个」**：
        # 幕布的面法线朝 z，所以 axis = 2（不是 0）。
        # 用 0 的话一个像素都命中不了（踩过 —— 实测盒子 1 的命中点 ax 全是 2）。
        # side_max=True：朝相机那面是 z=-2.9 = 盒子的 bmax 侧。
        gpa.DECALS = [(1, 2, True, -1.15, 0.55, 1.15, 2.05, sign)]
    else:
        gpa.DECALS = []

    # 画别比墙亮太多，但也不能暗到看不见
    gpa.POSTER_GAIN = 0.68

    dirs = gpa.look_rays(cam, yaw)
    proj = lambda pt: gpa.project(pt, cam, yaw)

    img = gpa.render_dirs(dirs, tex, seed=hash(name) & 0xFFFF, dark=dark,
                          exposure=1.0, w=gpa.W, h=gpa.H, proj=proj,
                          vig=True, props=False)

    # ⚠️ 舞台**不放静态角色** —— 监控里的敌人是运行时按位置叠上去的
    # （见 PrototypeScene.ApplyMonitorCharacter）。背景再画一遍就重复了。
    # 但**后台架子上的玩偶头套是道具**，不是活敌人，可以画进背景。
    if name == "5":
        for x, code in ((-1.45, "nailong"), (0.0, "daishu"), (1.45, "niulai")):
            img = paste_billboard(img, cam, yaw,
                                  gpa.load_character(code, "stare"),
                                  x, 0.98, -1.62, 0.80, dim=0.68)

    return img


def main():
    args = [a.lower() for a in sys.argv[1:]]
    names = args if args else list(SHOTS.keys())

    os.makedirs(OUT_DIR, exist_ok=True)

    for name in names:
        img = render_shot(name)
        path = os.path.join(OUT_DIR, "mon_%s.png" % name)
        img.save(path)
        print("rendered %s -> %s" % (name, path))


if __name__ == "__main__":
    main()

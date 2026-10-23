# -*- coding: utf-8 -*-
"""牛来（niulai）：金黄色毛的瘦高牛形人，大块淡紫口鼻，深色牛角外卷，手脚下垂也是淡紫。

参考图：Assets/Art/Characters/char_niulai_{idle,stare}.png（aspect 0.49，是四个里最瘦高的）

结构：
    身体一块（metaball，含角 —— 角要「从头上长出来」而不是插上去，所以放在同一个融球里）；
    口鼻 / 手 / 蹄 是独立零件；眼睛是独立小球。

运行：
    .tools/blender/blender.exe -b --python tools/models/build_niulai.py
    python tools/models/montage.py niulai
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blib as B                                    # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

C_FUR = "#EFC242"       # 金黄色毛
C_MUZZLE = "#C9B0C4"    # 淡紫口鼻／手脚
C_HORN = "#6E5E4A"      # 深色牛角
C_EYE = "#2A2118"       # 深棕眼

HEIGHT_M = 1.80
P = {
    "torso": (0.0, 0.0, 0.560), "torso_r": (0.190, 0.150, 0.250),
    "hips":  (0.0, 0.0, 0.380), "hips_r":  (0.180, 0.145, 0.145),
    "chest": (0.0, 0.0, 0.720), "chest_r": (0.205, 0.155, 0.105),
    "head":  (0.0, -0.020, 0.845), "head_r": (0.172, 0.160, 0.145),
    # 角：两段，先往斜上外、再往外翘
    "horn_a": (0.098, -0.030, 0.945), "horn_b": (0.180, -0.015, 1.008),
    "horn_c": (0.202, 0.022, 1.040),
    # 耳朵
    "ear": (0.178, -0.010, 0.878), "ear_r": (0.070, 0.040, 0.044),
    # 手臂（长、下垂）
    "arm_a": (0.185, 0.000, 0.700), "arm_b": (0.248, 0.010, 0.545),
    "arm_c": (0.256, 0.020, 0.395),
    # 手
    "hand": (0.258, 0.024, 0.358), "hand_r": (0.046, 0.042, 0.058),
    # 腿
    "leg_x": 0.098, "leg_top_z": 0.360, "leg_bot_z": 0.086,
    # 蹄
    "foot": (0.102, -0.040, 0.052), "foot_r": (0.082, 0.110, 0.052),
    # 口鼻
    "muzzle": (0.0, -0.140, 0.782), "muzzle_r": (0.120, 0.086, 0.064),
    # 眼睛
    "eye_x": 0.073, "eye_z": 0.878, "eye_r": 0.028,
    "face_tris": 13000,
}


def build_body():
    m_fur = B.mat("niulai_fur", C_FUR, rough=0.60, spec=0.22)
    o, mb = B.mb_new("niulai_mb", resolution=0.006)

    for key, rkey in (("torso", "torso_r"), ("hips", "hips_r"),
                      ("chest", "chest_r"), ("head", "head_r")):
        B.mb_ell(mb, P[key], 1.0, P[rkey], 3.0)

    for sx in (-1, 1):

        a2 = (sx * P["arm_a"][0], P["arm_a"][1], P["arm_a"][2])
        b2 = (sx * P["arm_b"][0], P["arm_b"][1], P["arm_b"][2])
        c2 = (sx * P["arm_c"][0], P["arm_c"][1], P["arm_c"][2])
        B.mb_chain(mb, a2, b2, 0.078, 0.054, n=10, stiffness=2.2)
        B.mb_chain(mb, b2, c2, 0.052, 0.040, n=8, stiffness=2.2)

        top = (sx * P["leg_x"], 0.0, P["leg_top_z"])
        bot = (sx * P["leg_x"], 0.0, P["leg_bot_z"])
        B.mb_chain(mb, top, bot, 0.094, 0.070, n=10, stiffness=2.2)

    o = B.mb_bake(o, "niulai_body")
    B.remesh(o, voxel=0.007, smooth=6, smooth_factor=0.5)
    B.decimate(o, target_tris=P["face_tris"])
    o.data.materials.append(m_fur)
    return o


def build_horns():
    """角单独一个零件：放在身体融球里会跟着长成毛色，而且被头吸粗。"""
    m = B.mat("niulai_horn", C_HORN, rough=0.34, spec=0.40)
    o, mb = B.mb_new("niulai_horn_mb", resolution=0.004)
    for sx in (-1, 1):
        a = (sx * 0.082, -0.045, 0.918)
        b = (sx * 0.150, -0.028, 0.986)
        c = (sx * 0.202, 0.012, 1.012)
        B.mb_chain(mb, a, b, 0.048, 0.030, n=10, stiffness=2.4)
        B.mb_chain(mb, b, c, 0.030, 0.012, n=8, stiffness=2.4)
    o = B.mb_bake(o, "niulai_horns")
    # 角很细，remesh/decimate 会把尖端弄出破洞；融球出来的网格本来就够用
    B.shade_smooth(o)
    o.data.materials.append(m)
    return o


def build_face(body):
    m_muzzle = B.mat("niulai_muzzle", C_MUZZLE, rough=0.46, spec=0.30)
    m_horn = B.mat("niulai_horn", C_HORN, rough=0.34, spec=0.40)
    m_eye = B.mat("niulai_eye", C_EYE, rough=0.10, spec=0.70)
    m_fur_ear = B.mat("niulai_fur", C_FUR, rough=0.60, spec=0.22)

    parts = [B.ellipsoid("muzzle", P["muzzle"], P["muzzle_r"], m_muzzle,
                         segs=40, rings=24)]

    # 耳：横向支出去的小片
    for sx in (-1, 1):
        c = (sx * P["ear"][0], P["ear"][1], P["ear"][2])
        parts.append(B.ellipsoid(f"ear_{sx}", c, P["ear_r"], m_fur_ear, segs=24, rings=14))

    # 手 + 蹄（淡紫）
    for sx in (-1, 1):
        h = (sx * P["hand"][0], P["hand"][1], P["hand"][2])
        parts.append(B.ellipsoid(f"hand_{sx}", h, P["hand_r"], m_muzzle,
                                 segs=28, rings=16))
        f = (sx * P["foot"][0], P["foot"][1], P["foot"][2])
        parts.append(B.ellipsoid(f"foot_{sx}", f, P["foot_r"], m_muzzle,
                                 segs=28, rings=16))

    r = P["eye_r"]
    for sx in (-1, 1):
        loc, nor = B.face_point(body, sx * P["eye_x"], P["eye_z"],
                                warn="[niulai] 眼睛射线未命中")
        parts.append(B.add_sphere(f"eye_{sx}", loc - nor * (r * 0.35),
                                  r, m_eye, 24, 12))
    return parts


def build():
    B.reset()
    body = build_body()
    parts = [body, build_horns()] + build_face(body)
    B.normalize(parts, HEIGHT_M)
    B.report(parts, "niulai")
    return parts


def main():
    parts = build()
    merged = B.join_in_place(parts, "niulai")
    B.flip_180_z([merged])
    B.report([merged], "niulai-merged")

    out_dir = os.path.join(REPO, "Assets", "Art", "Models", "niulai")
    fbx = os.path.join(out_dir, "niulai.fbx")
    B.export_fbx(fbx, [merged])
    print("FBX ->", fbx)

    B.setup_scene_render(res=(560, 760), samples=28)
    pv = os.path.join(REPO, ".workbuddy-ai", "models", "views_niulai.png")
    os.makedirs(os.path.dirname(pv), exist_ok=True)
    B.render_views([merged], pv, margin=1.10)
    print("预览 ->", pv)


if __name__ == "__main__":
    main()

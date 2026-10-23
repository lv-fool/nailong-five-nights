# -*- coding: utf-8 -*-
"""噜噜（lulu）：黄身、巨大的橘色口鼻块、头顶一颗小南瓜 + 绿梗、橘色短裤、小圆耳、半眯眼。

参考图：Assets/Art/Characters/char_lulu_{idle,stare,peek,angry,startled}.png

结构：
    身体一块（metaball），口鼻 / 短裤 / 南瓜 / 耳朵 / 眼睛都是独立零件。
    短裤单独用一小坨 metaball 烤成「圆桶」压在胯上 —— 参考图里短裤就是
    挂在肚子下面的一圈，肚子从上面垂下来盖住裤腰，正好是这个形状。

运行：
    .tools/blender/blender.exe -b --python tools/models/build_lulu.py
    python tools/models/montage.py lulu
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blib as B                                    # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

C_BODY = "#F0D071"      # 身体黄（比奶龙淡一点）
C_MUZZLE = "#F79A16"    # 橘色口鼻
C_SHORTS = "#C4794A"    # 锈色短裤
C_PUMPKIN = "#F08A12"   # 头顶小南瓜
C_STEM = "#6F8C31"      # 南瓜梗
C_EYE = "#3E3018"       # 眼（近黑）

HEIGHT_M = 1.70
P = {
    # 身／胸／头：一个胖梨，头直接长在肩上
    "body":  (0.0, 0.000, 0.355), "body_r":  (0.330, 0.265, 0.290),
    "chest": (0.0, -0.010, 0.555), "chest_r": (0.305, 0.250, 0.205),
    "head":  (0.0, -0.020, 0.690), "head_r":  (0.280, 0.240, 0.195),
    "neck":  (0.0, 0.000, 0.610), "neck_r":  (0.275, 0.220, 0.120),
    # 短腿、短手
    "leg_x": 0.115, "leg_top_z": 0.140, "leg_bot_z": 0.052,
    "arm_a": (0.278, 0.010, 0.440), "arm_b": (0.312, 0.020, 0.305),
    # 口鼻：一大块橘色，压在脸的下半部
    "muzzle": (0.0, -0.128, 0.628), "muzzle_r": (0.228, 0.142, 0.152),
    # 短裤：三颗椭球叠成圆桶
    "shorts_z": (0.112, 0.170, 0.228),
    "shorts_r": ((0.288, 0.242, 0.105), (0.300, 0.252, 0.105), (0.284, 0.240, 0.105)),
    # 耳朵
    "ear": (0.235, -0.015, 0.738), "ear_r": (0.054, 0.060, 0.068),
    # 头顶南瓜 + 梗
    "pumpkin": (0.0, -0.020, 0.885), "pumpkin_r": 0.092,
    "stem_a": (0.0, -0.020, 0.960), "stem_b": (0.0, -0.010, 0.994),
    # 眼睛
    "eye_x": 0.098, "eye_z": 0.800, "eye_r": 0.036,
    "face_tris": 12000,
}


def build_body():
    m_body = B.mat("lulu_body", C_BODY, rough=0.46, spec=0.28)
    o, mb = B.mb_new("lulu_mb", resolution=0.006)

    for key, rkey in (("body", "body_r"), ("chest", "chest_r"),
                      ("head", "head_r"), ("neck", "neck_r")):
        B.mb_ell(mb, P[key], 1.0, P[rkey], 3.0)

    for sx in (-1, 1):
        a = (sx * P["arm_a"][0], P["arm_a"][1], P["arm_a"][2])
        b = (sx * P["arm_b"][0], P["arm_b"][1], P["arm_b"][2])
        B.mb_chain(mb, a, b, 0.084, 0.064, n=8, stiffness=2.2)

    for sx in (-1, 1):
        top = (sx * P["leg_x"], 0.010, P["leg_top_z"])
        bot = (sx * P["leg_x"], 0.010, P["leg_bot_z"])
        B.mb_chain(mb, top, bot, 0.094, 0.076, n=6, stiffness=2.2)

    o = B.mb_bake(o, "lulu_body")
    B.remesh(o, voxel=0.007, smooth=6, smooth_factor=0.5)
    B.decimate(o, target_tris=P["face_tris"])
    o.data.materials.append(m_body)
    return o


def build_shorts():
    """短裤：三颗椭球叠出来的一段圆桶，单独烤成一个物体。"""
    m = B.mat("lulu_shorts", C_SHORTS, rough=0.44, spec=0.26)
    o, mb = B.mb_new("lulu_shorts_mb", resolution=0.006)
    for z, r in zip(P["shorts_z"], P["shorts_r"]):
        B.mb_ell(mb, (0.0, 0.005, z), 1.0, r, 3.0)
    o = B.mb_bake(o, "lulu_shorts")
    B.remesh(o, voxel=0.007, smooth=6, smooth_factor=0.5)
    B.decimate(o, target_tris=3500)
    o.data.materials.append(m)
    return o


def build_face(body):
    m_muzzle = B.mat("lulu_muzzle", C_MUZZLE, rough=0.42, spec=0.30)
    m_ear = B.mat("lulu_body", C_BODY, rough=0.46, spec=0.28)
    m_eye = B.mat("lulu_eye", C_EYE, rough=0.10, spec=0.70)
    m_pumpkin = B.mat("lulu_pumpkin", C_PUMPKIN, rough=0.36, spec=0.35)
    m_stem = B.mat("lulu_stem", C_STEM, rough=0.50, spec=0.20)

    parts = [B.ellipsoid("muzzle", P["muzzle"], P["muzzle_r"], m_muzzle,
                         segs=40, rings=24)]

    for sx in (-1, 1):
        c = (sx * P["ear"][0], P["ear"][1], P["ear"][2])
        parts.append(B.ellipsoid(f"ear_{sx}", c, P["ear_r"], m_ear, segs=24, rings=14))

    parts.append(B.add_sphere("pumpkin", P["pumpkin"], P["pumpkin_r"], m_pumpkin, 32, 16))
    parts.append(B.add_arc_tube("stem", [P["stem_a"], P["stem_b"]], 0.013, m_stem))

    r = P["eye_r"]
    for sx in (-1, 1):
        loc, nor = B.face_point(body, sx * P["eye_x"], P["eye_z"],
                                warn="[lulu] 眼睛射线未命中")
        cen = loc - nor * (r * 0.55)
        parts.append(B.add_sphere(f"eye_{sx}", cen + nor * (r * 0.35),
                                  r, m_eye, 24, 12))
    return parts


def build():
    B.reset()
    body = build_body()
    parts = [body, build_shorts()] + build_face(body)
    B.normalize(parts, HEIGHT_M)
    B.report(parts, "lulu")
    return parts


def main():
    parts = build()
    merged = B.join_in_place(parts, "lulu")
    B.flip_180_z([merged])
    B.report([merged], "lulu-merged")

    out_dir = os.path.join(REPO, "Assets", "Art", "Models", "lulu")
    fbx = os.path.join(out_dir, "lulu.fbx")
    B.export_fbx(fbx, [merged])
    print("FBX ->", fbx)

    B.setup_scene_render(res=(560, 720), samples=28)
    pv = os.path.join(REPO, ".workbuddy-ai", "models", "views_lulu.png")
    os.makedirs(os.path.dirname(pv), exist_ok=True)
    B.render_views([merged], pv, margin=1.10)
    print("预览 ->", pv)


if __name__ == "__main__":
    main()

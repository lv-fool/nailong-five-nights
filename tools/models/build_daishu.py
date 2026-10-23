# -*- coding: utf-8 -*-
"""袋鼠（daishu）：其实是一只黄色四足小狗 —— 长耳朵向后上支，深棕大鼻头，尾巴翘起。

参考图：Assets/Art/Characters/char_daishu_{idle,stare}.png
名字叫袋鼠，但立绘是狗，按图建。

结构同奶龙：身体一块（metaball 烤成一个网格），鼻头/眼睛是独立零件。
    四足动物的「一条腿」在 metaball 里就是一串球，只要腿根埋进躯干就不会掉件。

运行：
    .tools/blender/blender.exe -b --python tools/models/build_daishu.py
    python tools/models/montage.py daishu
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blib as B                                    # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

C_BODY = "#F5CC3C"      # 亮黄
C_NOSE = "#8A3E17"      # 深棕鼻头
C_SCLERA = "#FBFBF2"    # 眼白
C_PUPIL = "#3A2A18"     # 瞳孔（参考图是暖棕）

HEIGHT_M = 1.30         # 四足，故意矮一截（两个门高 2.05m，够高了）
P = {
    # 躯干：一根横躺的胶囊，肩在 -Y、臀在 +Y
    "torso":  (0.0, 0.045, 0.400), "torso_r":  (0.165, 0.245, 0.155),
    "chest":  (0.0, -0.145, 0.415), "chest_r": (0.165, 0.145, 0.158),
    "hips":   (0.0, 0.225, 0.405), "hips_r":  (0.155, 0.135, 0.148),
    # 脖子 + 头
    "neck_a": (0.0, -0.200, 0.505), "neck_b": (0.0, -0.248, 0.600),
    "head":   (0.0, -0.262, 0.640), "head_r": (0.132, 0.142, 0.120),
    "snout":  (0.0, -0.352, 0.606), "snout_r": (0.070, 0.082, 0.060),
    # 耳朵：两节，先向后上、再往外翘
    "ear_a": (0.052, -0.232, 0.722), "ear_b": (0.074, -0.196, 0.818),
    "ear_c": (0.082, -0.158, 0.868),
    # 四条腿
    "leg_x": 0.085,
    "leg_front_y": -0.170, "leg_back_y": 0.220,
    "leg_top_z": 0.255, "leg_bot_z": 0.086,
    # 尾巴
    "tail_a": (0.0, 0.300, 0.455), "tail_b": (0.0, 0.372, 0.620),
    # 鼻子（椭球，独立零件）
    "nose": (0.0, -0.372, 0.608), "nose_r": (0.046, 0.044, 0.038),
    # 眼睛
    "eye_x": 0.057, "eye_z": 0.688, "eye_r": 0.038,
    "face_tris": 11000,
    # 脚掌（椭球，同色，补出「有脚」的轮廓）
    "feet_r": (0.086, 0.104, 0.050), "feet_z": 0.050,
}


def build_body():
    m_body = B.mat("daishu_body", C_BODY, rough=0.44, spec=0.30)
    o, mb = B.mb_new("daishu_mb", resolution=0.006)

    for key, rkey in (("torso", "torso_r"), ("chest", "chest_r"), ("hips", "hips_r"),
                      ("head", "head_r"), ("snout", "snout_r")):
        B.mb_ell(mb, P[key], 1.0, P[rkey], 3.0)

    B.mb_chain(mb, P["neck_a"], P["neck_b"], 0.090, 0.082, n=8, stiffness=2.4)

    for sx in (-1, 1):
        a = (sx * P["ear_a"][0], P["ear_a"][1], P["ear_a"][2])
        b = (sx * P["ear_b"][0], P["ear_b"][1], P["ear_b"][2])
        c = (sx * P["ear_c"][0], P["ear_c"][1], P["ear_c"][2])
        B.mb_chain(mb, a, b, 0.048, 0.032, n=10, stiffness=2.2)
        B.mb_chain(mb, b, c, 0.032, 0.024, n=8, stiffness=2.2)

    for sx in (-1, 1):
        for y in (P["leg_front_y"], P["leg_back_y"]):
            top = (sx * P["leg_x"], y, P["leg_top_z"])
            bot = (sx * P["leg_x"], y, P["leg_bot_z"])
            B.mb_chain(mb, top, bot, 0.096, 0.070, n=12, stiffness=2.2)

    B.mb_chain(mb, P["tail_a"], P["tail_b"], 0.062, 0.030, n=12, stiffness=2.2)

    o = B.mb_bake(o, "daishu_body")
    B.remesh(o, voxel=0.007, smooth=6, smooth_factor=0.5)
    B.decimate(o, target_tris=P["face_tris"])
    o.data.materials.append(m_body)
    return o


def build_face(body):
    m_nose = B.mat("daishu_nose", C_NOSE, rough=0.36, spec=0.40)
    m_body = B.mat("daishu_body", C_BODY, rough=0.44, spec=0.30)
    m_sclera = B.mat("daishu_sclera", C_SCLERA, rough=0.10, spec=0.70)
    m_pupil = B.mat("daishu_pupil", C_PUPIL, rough=0.10, spec=0.70)
    parts = [B.ellipsoid("nose", P["nose"], P["nose_r"], m_nose, segs=28, rings=16)]

    for sx in (-1, 1):
        for y in (P["leg_front_y"], P["leg_back_y"]):
            parts.append(B.ellipsoid(f"feet_{sx}_{y}", (sx * P["leg_x"], y - 0.012, P["feet_z"]),
                                     P["feet_r"], m_body, segs=24, rings=14))

    r = P["eye_r"]
    for sx in (-1, 1):
        loc, nor = B.face_point(body, sx * P["eye_x"], P["eye_z"],
                                warn="[daishu] 眼睛射线未命中")
        cen = loc - nor * (r * 0.32)
        parts.append(B.add_sphere(f"eye_{sx}_sclera", cen, r, m_sclera, 26, 14))
        parts.append(B.add_sphere(f"eye_{sx}_pupil", cen + nor * (r * 0.55),
                                  r * 0.58, m_pupil, 20, 10))
    return parts


def build():
    B.reset()
    body = build_body()
    parts = [body] + build_face(body)
    B.normalize(parts, HEIGHT_M)
    B.report(parts, "daishu")
    return parts


def main():
    parts = build()
    merged = B.join_in_place(parts, "daishu")
    B.flip_180_z([merged])
    B.report([merged], "daishu-merged")

    out_dir = os.path.join(REPO, "Assets", "Art", "Models", "daishu")
    fbx = os.path.join(out_dir, "daishu.fbx")
    B.export_fbx(fbx, [merged])
    print("FBX ->", fbx)

    B.setup_scene_render(res=(560, 720), samples=28)
    pv = os.path.join(REPO, ".workbuddy-ai", "models", "views_daishu.png")
    os.makedirs(os.path.dirname(pv), exist_ok=True)
    B.render_views([merged], pv, margin=1.10)
    print("预览 ->", pv)


if __name__ == "__main__":
    main()

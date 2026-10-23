# -*- coding: utf-8 -*-
"""奶龙（nailong）：黄色圆滚一团，白肚皮，深橄榄色手套／靴子，大青绿眼睛。

参考图：Assets/Art/Characters/char_nailong_{idle,stare}.png
比例由 tools/models/ref_analysis.py 量过（半宽/身高 已经记在 P 里）。

结构上有一条重要约定：
    身体（黄）一块；**手套、靴子、白肚皮都是独立零件**，叠在身体上。
    因为按面刷材质再怎么磨边界，交界永远是三角形锯齿；独立零件的轮廓线
    本身就是光滑曲线，看着才像立绘。

运行：
    .tools/blender/blender.exe -b --python tools/models/build_nailong.py
    python tools/models/montage.py nailong          # 拼四视角图（Pillow 在系统 Python 里）
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blib as B                                    # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# ---- 配色（从参考图 k-means 取样后按感知提亮：k-means 给的是含阴影的均值）----
C_BODY = "#EFCE4A"      # 身体黄
C_BELLY = "#F7F1DC"     # 肚皮奶白
C_PAW = "#6B6144"       # 手／脚的深橄榄
C_SCLERA = "#EDEEE2"    # 眼白（偏灰白）
C_IRIS = "#8FBFA4"      # 青绿虹膜
C_PUPIL = "#11110D"     # 瞳孔近黑
C_MOUTH = "#5A4A2E"     # 嘴线

# ---- 形体参数（单位：身高 = 1.0，最后统一缩到 HEIGHT_M）----
HEIGHT_M = 1.75
P = {
    # 头是一顶压在肩上的圆帽：宽、略扁，靠 neck 补过渡，不能是插上去的球
    "head_z": 0.760, "head_rx": 0.240, "head_ry": 0.206, "head_rz": 0.172,
    "neck_z": 0.655, "neck_rx": 0.200, "neck_ry": 0.175, "neck_rz": 0.110,
    # 身子：从胸口到胯接近一个桶，最宽在肚子
    "body_z": 0.480, "body_rx": 0.312, "body_ry": 0.260, "body_rz": 0.242,
    "hip_z": 0.280, "hip_rx": 0.302, "hip_ry": 0.250, "hip_rz": 0.210,
    # 手臂：起点埋在身子里，避免肩膀出现接缝；手腕收细，手套会盖住
    "arm_x": 0.186, "arm_z": 0.572,
    "elbow": (0.296, -0.016, 0.462),
    "wrist": (0.314, -0.026, 0.380),
    # 手套
    "hand": (0.326, -0.028, 0.340), "hand_r": (0.070, 0.058, 0.055),
    # 腿
    "leg_x": 0.150, "leg_top_z": 0.212, "leg_bot_z": 0.074,
    # 靴子
    "foot": (0.192, -0.042, 0.052), "foot_r": (0.084, 0.112, 0.052),
    # 眼睛：x / z / 半径（y 由射线决定）
    "eye_x": 0.098, "eye_z": 0.808, "eye_r": 0.046,
    "mouth_z": 0.730,
    # 白肚皮：正面椭圆块的中心与半径（x, z）
    "belly_c": (0.0, 0.400), "belly_r": (0.215, 0.215),
    "face_tris": 12000,
}


def build_body():
    m_body = B.mat("nailong_body", C_BODY, rough=0.46, spec=0.30)
    o, mb = B.mb_new("nailong_mb", resolution=0.008)

    B.mb_ell(mb, (0, 0, P["head_z"]), 1.0,
             (P["head_rx"], P["head_ry"], P["head_rz"]), 3.2)
    B.mb_ell(mb, (0, 0, P["neck_z"]), 1.0,
             (P["neck_rx"], P["neck_ry"], P["neck_rz"]), 3.0)
    B.mb_ell(mb, (0, 0, P["body_z"]), 1.0,
             (P["body_rx"], P["body_ry"], P["body_rz"]), 2.8)
    B.mb_ell(mb, (0, 0, P["hip_z"]), 1.0,
             (P["hip_rx"], P["hip_ry"], P["hip_rz"]), 2.8)

    # 手臂：起点埋在身子里，避免肩膀出现接缝；手腕收细，手套会盖住
    for sx in (-1, 1):
        sh = (sx * P["arm_x"], 0.0, P["arm_z"])
        el = (sx * P["elbow"][0], P["elbow"][1], P["elbow"][2])
        wr = (sx * P["wrist"][0], P["wrist"][1], P["wrist"][2])
        B.mb_chain(mb, sh, el, 0.100, 0.084, n=6, stiffness=2.2)
        B.mb_chain(mb, el, wr, 0.084, 0.062, n=5, stiffness=2.2)

    # 腿：下端收细，靴子会盖住
    for sx in (-1, 1):
        top = (sx * P["leg_x"], 0.0, P["leg_top_z"])
        bot = (sx * P["foot"][0], 0.0, P["leg_bot_z"])
        B.mb_chain(mb, top, bot, 0.112, 0.072, n=5, stiffness=2.2)

    o = B.mb_bake(o, "nailong_body")
    B.remesh(o, voxel=0.009, smooth=5, smooth_factor=0.5)
    B.decimate(o, target_tris=P["face_tris"])
    o.data.materials.append(m_body)
    return o


def build_paws():
    """手套 ×2 + 靴子 ×2，合成一个零件。

    这里刻意不用 metaball：小尺度的元件在 6 mm 体素下要么缩成一团、要么
    各糊各的（上一版手指就掉成了独立的小片）。手套靴子本来就是「套在
    肢体末端的整块」，四个干净的椭球各自独立、合到一个物体里最稳。
    """
    m_paw = B.mat("nailong_paw", C_PAW, rough=0.38, spec=0.35)
    objs = []
    for sx in (-1, 1):
        hx, hy, hz = sx * P["hand"][0], P["hand"][1], P["hand"][2]
        objs.append(B.ellipsoid(f"hand_{sx}", (hx, hy, hz), P["hand_r"], m_paw,
                                segs=32, rings=20))
        fx, fy, fz = sx * P["foot"][0], P["foot"][1], P["foot"][2]
        objs.append(B.ellipsoid(f"foot_{sx}", (fx, fy, fz), P["foot_r"], m_paw,
                                segs=32, rings=20))
    o = B.join_in_place(objs, "nailong_paws")
    return o


def build_face(body):
    m_sclera = B.mat("nailong_sclera", C_SCLERA, rough=0.10, spec=0.70)
    m_iris = B.mat("nailong_iris", C_IRIS, rough=0.08, spec=0.80)
    m_pupil = B.mat("nailong_pupil", C_PUPIL, rough=0.06, spec=0.85)
    m_mouth = B.mat("nailong_mouth", C_MOUTH, rough=0.45)
    parts = []

    for sx in (-1, 1):
        loc, nor = B.face_point(body, sx * P["eye_x"], P["eye_z"],
                                warn="[nailong] 眼睛射线未命中")
        r = P["eye_r"]
        cen = loc - nor * (r * 0.55)
        parts.append(B.add_sphere(f"eye_{sx}_sclera", cen, r, m_sclera, 28, 14))
        parts.append(B.add_sphere(f"eye_{sx}_iris", cen + nor * (r * 0.60),
                                  r * 0.70, m_iris, 24, 12))
        parts.append(B.add_sphere(f"eye_{sx}_pupil", cen + nor * (r * 0.90),
                                  r * 0.36, m_pupil, 20, 10))

    pts = []
    for i in range(13):
        t = (i / 12.0 - 0.5) * 2.0
        x = t * 0.070
        z = P["mouth_z"] - 0.012 * (1.0 - t * t)          # 中间略低 = 微笑
        loc, nor = B.face_point(body, x, z, warn="[nailong] 嘴射线未命中")
        pts.append(loc + nor * 0.004)
    parts.append(B.add_arc_tube("mouth", pts, 0.0052, m_mouth))
    return parts


def build():
    B.reset()
    body = build_body()
    m_belly = B.mat("nailong_belly", C_BELLY, rough=0.50, spec=0.28)
    belly = B.ellipse_patch(body, P["belly_c"], P["belly_r"], m_belly,
                            "nailong_belly_patch", rings=12, segs=72,
                            offset=0.0045)
    paws = build_paws()
    parts = [body, belly, paws] + build_face(body)

    B.normalize(parts, HEIGHT_M)
    B.report(parts, "nailong")
    return parts


def main():
    parts = build()
    # 导成**一个**网格：多物体导 FBX 后 Unity 侧的轴转换会把各子物体弄得不一样
    # （实测包围盒 Y/Z 都变成 1.92，说明有的子物体被转了、有的没转）。
    # 单个物体只带一个变换，进 Unity 不会走形；材质仍是多槽，外观不变。
    merged = B.join_in_place(parts, "nailong")
    B.flip_180_z([merged])
    B.report([merged], "nailong-merged")

    out_dir = os.path.join(REPO, "Assets", "Art", "Models", "nailong")
    fbx = os.path.join(out_dir, "nailong.fbx")
    B.export_fbx(fbx, [merged])
    print("FBX ->", fbx)

    B.setup_scene_render(res=(560, 720), samples=28)
    pv = os.path.join(REPO, ".workbuddy-ai", "models", "views_nailong.png")
    os.makedirs(os.path.dirname(pv), exist_ok=True)
    B.render_views([merged], pv, margin=1.10)
    print("预览 ->", pv, "(用 montage.py 拼图)")


if __name__ == "__main__":
    main()

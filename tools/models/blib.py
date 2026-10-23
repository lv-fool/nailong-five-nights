# -*- coding: utf-8 -*-
"""程序化建卡通角色的公共库（Blender bpy，headless 运行）。

设计取向
--------
角色都是「圆鼓鼓的一坨」——参考图里奶龙/牛来/袋鼠/噜噜的共同点是没有硬边、
肢体和身子是平滑过渡的。对这类形体，**metaball（融球）是最省事也最还原的做法**：
几个椭球 + 一串球当肢体，一 convert 就自动长出平滑的过渡，不用手动补面。

流程（每个角色脚本都走这一套）：
    reset() -> mb_new() 加元素 -> mb_bake() 变网格
    -> remesh() 统一面密度 -> decimate() 压到游戏面数 -> shade_smooth()
    -> assign_faces() 按位置劈材质（肚子那块白的、爪子那些深的）
    -> 眼睛/嘴做独立小球体 -> normalize() 缩到真实米数、脚底贴 z=0
    -> render_views() 出 4 视角预览自己看 -> export_fbx()

坐标约定
--------
建的时候角色面朝 **-Y**（Blender 正视图方向），+Z 向上，X 左右。
导出 FBX 用 Unity 约定（Forward=-Z / Up=Y），进 Unity 后物体旋转为零、面朝 +Z。
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# 颜色
# --------------------------------------------------------------------------


def _s2l(c):
    """sRGB 0..1 -> 线性。Blender 的 Base Color 是线性空间。"""
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex2lin(h):
    h = h.lstrip("#")
    r, g, b = (int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    return (_s2l(r), _s2l(g), _s2l(b), 1.0)


# --------------------------------------------------------------------------
# 场景 / 材质
# --------------------------------------------------------------------------


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0


def mat(name, color, rough=0.5, spec=0.25, sheen=0.0):
    """建一个 Principled 材质。color 可用 '#RRGGBB' 或 (r,g,b)。"""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    rgba = hex2lin(color) if isinstance(color, str) else (color[0], color[1], color[2], 1.0)
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = rough
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = spec
    if sheen and "Sheen Weight" in bsdf.inputs:
        bsdf.inputs["Sheen Weight"].default_value = sheen
    m.diffuse_color = rgba          # 供 viewport / Unity 备份用
    return m


# --------------------------------------------------------------------------
# metaball
# --------------------------------------------------------------------------


def mb_new(name="MB", resolution=0.014, threshold=0.6):
    mb = bpy.data.metaballs.new(name)
    mb.resolution = resolution
    mb.render_resolution = resolution
    mb.threshold = threshold
    o = bpy.data.objects.new(name, mb)
    bpy.context.collection.objects.link(o)
    return o, mb


def mb_ell(mb, co, radius, size=(1.0, 1.0, 1.0), stiffness=2.0, negative=False):
    el = mb.elements.new(type="ELLIPSOID")
    el.co = Vector(co)
    el.radius = radius
    el.size_x, el.size_y, el.size_z = size
    el.stiffness = stiffness
    el.use_negative = negative
    return el


def mb_ball(mb, co, radius, stiffness=2.0, negative=False):
    el = mb.elements.new(type="BALL")
    el.co = Vector(co)
    el.radius = radius
    el.stiffness = stiffness
    el.use_negative = negative
    return el


def mb_chain(mb, p0, p1, r0, r1, n=7, stiffness=2.0):
    """从 p0 到 p1 排一串球当肢体，半径线性插值。"""
    p0, p1 = Vector(p0), Vector(p1)
    for i in range(n):
        t = i / (n - 1.0) if n > 1 else 0.0
        mb_ball(mb, p0.lerp(p1, t), r0 + (r1 - r0) * t, stiffness)


def mb_bake(obj, name):
    """把 metaball 转成真网格。"""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


# --------------------------------------------------------------------------
# 后处理
# --------------------------------------------------------------------------


def remesh(o, voxel=0.012, smooth=8, smooth_factor=0.5):
    m = o.modifiers.new("RM", "REMESH")
    m.mode = "VOXEL"
    m.voxel_size = voxel
    m.use_smooth_shade = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)
    if smooth:
        s = o.modifiers.new("SM", "SMOOTH")
        s.factor = smooth_factor
        s.iterations = smooth
        bpy.ops.object.modifier_apply(modifier=s.name)
    shade_smooth(o)
    return o


def decimate(o, target_tris=None, ratio=None):
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if ratio is None:
        if not target_tris or tris <= target_tris:
            return o
        ratio = target_tris / float(tris)
    m = o.modifiers.new("DC", "DECIMATE")
    m.ratio = max(0.02, min(1.0, ratio))
    m.use_collapse_triangulate = False
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)
    shade_smooth(o)
    return o


def shade_smooth(o):
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    try:
        bpy.ops.object.shade_smooth()
    except RuntimeError:
        for p in o.data.polygons:
            p.use_smooth = True
    o.select_set(False)
    return o


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def surface_project(o, p):
    """把一个点吸到网格表面上，返回 (表面点, 法线)。

    metaball 建出来的曲面比元素参数「胀」出去一圈，硬按坐标摆零件会悬空，
    所以眼睛、嘴这类贴脸的东西一律先投影再放。
    """
    ok, loc, nor, _ = o.closest_point_on_mesh(Vector(p))
    if not ok:
        return Vector(p), Vector((0, -1, 0))
    return loc, nor


def surface_ray(o, x, z, y_from=-3.0):
    """从 (x, y_from, z) 沿 +Y 打一条射线，取到脸面上的点。

    比 closest_point_on_mesh 稳：后者在凹处（下巴、腋下）会把点吸到附近别的面上，
    结果眼睛扎堆、嘴线挤成一小段。射线是「从正前方直着撞过来」，落点一定是正面。
    只接受朝前的面（nor.y < 0），打到背面就当作没打中。
    """
    ok, loc, nor, _ = o.ray_cast(Vector((x, y_from, z)),
                                 Vector((0.0, 1.0, 0.0)))
    if ok and nor.y < -0.05:
        return loc, nor
    return None, None


def face_point(o, x, z, warn=None):
    """先射线，打不中就退回最近的表面点。"""
    loc, nor = surface_ray(o, x, z)
    if loc is not None:
        return loc, nor
    if warn:
        print(warn, (x, z))
    return surface_project(o, (x, -1.0, z))


def smooth_material_boundary(o, passes=6, lock=None):
    """材质分界做邻居多数投票，把锯齿边磨平。

    直接按多边形中心判材质，边界会沿三角形台阶走；跑几轮多数投票就顺了。
    lock: 可选的 mat_index 列表，这些材质的面不参与投票（当硬边界用）。
    """
    me = o.data
    n = len(me.polygons)
    edge_faces = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            edge_faces.setdefault(ek, []).append(p.index)
    nb = [[] for _ in range(n)]
    for fl in edge_faces.values():
        for a in fl:
            for b in fl:
                if a != b:
                    nb[a].append(b)
    idx = [p.material_index for p in me.polygons]
    locked = set(lock or [])
    for _ in range(passes):
        new = list(idx)
        for i in range(n):
            if idx[i] in locked or not nb[i]:
                continue
            cnt = {}
            for j in nb[i]:
                cnt[idx[j]] = cnt.get(idx[j], 0) + 1
            own = idx[i]
            best = max(cnt.items(), key=lambda kv: (kv[1], -kv[0]))[0]
            if cnt.get(best, 0) > cnt.get(own, 0):
                new[i] = best
        idx = new
    for p, m in zip(me.polygons, idx):
        p.material_index = m
    return o


def assign_faces(o, slots):
    """slots: [(material, predicate(center, normal) -> bool), ...]，靠前的优先。

    材质按顺序 append 到 o.data.materials，索引即顺序。
    """
    o.data.materials.clear()
    for m, _ in slots:
        o.data.materials.append(m)
    for p in o.data.polygons:
        c, n = p.center, p.normal
        for i, (_, pred) in enumerate(slots):
            if pred(c, n):
                p.material_index = i
                break
    return o


# --------------------------------------------------------------------------
# 零件：球、眼睛、嘴
# --------------------------------------------------------------------------


def add_sphere(name, co, radius, material, segs=32, rings=16, scale=(1, 1, 1), smooth=True):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=radius,
                                         location=co)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    if smooth:
        shade_smooth(o)
    o.data.materials.append(material)
    return o


def add_eye(name, center, radius, look_dir, mat_white, mat_iris, mat_pupil,
            iris_r=0.62, pupil_r=0.30, bulge=0.0):
    """眼球 = 眼白球 + 虹膜球 + 瞳孔球，一层叠一层，只有正面看得见。

    look_dir 是眼球朝向（单位向量），虹膜/瞳孔沿它前移。
    """
    d = Vector(look_dir).normalized()
    c = Vector(center)
    parts = []
    parts.append(add_sphere(f"{name}_sclera", c, radius, mat_white, 32, 16))
    parts.append(add_sphere(f"{name}_iris", c + d * (radius * iris_r * 0.92),
                            radius * iris_r, mat_iris, 28, 14))
    parts.append(add_sphere(f"{name}_pupil", c + d * (radius * (iris_r * 0.92 + pupil_r * 0.55)),
                            radius * pupil_r, mat_pupil, 24, 12))
    return parts


def ellipse_patch(target, center, radii, material, name="patch",
                  rings=10, segs=64, offset=0.002):
    """在 target 的正面贴一块椭圆形的薄壳（肚皮、口鼻色块用）。

    按面判材质再怎么投票，边界都会沿三角形走成锯齿；而参数化的椭圆网格
    边界天生是光滑曲线，所以这一类色块一律做成独立薄片，不去切主体。
    """
    cx, cz = center
    rx, rz = radii
    # 射线沿 +Y 打，落点的 x/z 恒等于射线的 x/z，所以整块薄片就是一张高度场
    # y(x, z)，只需要记 y —— 不会出现顶点重合导致的退化面。
    #
    # 打在「轮廓线附近」的射线会打空（那里的面根本不朝前）。以前是拿邻居插值
    # 糊过去，结果要么戳出黄点、要么沉进身体里开天窗。正确做法是让椭圆本身
    # 缩到「所有射线都打得中」为止。
    scale = 1.0
    for _attempt in range(10):
        ys = []
        misses = 0
        for j in range(rings + 1):
            t = (j / rings) ** 0.85              # 外圈密一点，边界更圆
            row = []
            for i in range(segs):
                a = 2.0 * math.pi * i / segs
                loc, _ = surface_ray(target,
                                     cx + rx * scale * t * math.cos(a),
                                     cz + rz * scale * t * math.sin(a))
                if loc is None:
                    misses += 1
                    row.append(None)
                else:
                    row.append(loc.y)
            ys.append(row)
        if misses == 0:
            break
        scale *= 0.96
    if misses:
        print(f"[patch] {name}: 缩到 {scale:.2f} 仍剩 {misses} 条打空，按邻居插值补")
        for _ in range(rings + 1):
            for j in range(rings + 1):
                for i in range(segs):
                    if ys[j][i] is not None:
                        continue
                    nb = []
                    if j > 0:
                        nb.append(ys[j - 1][i])
                    if j < rings:
                        nb.append(ys[j + 1][i])
                    nb.append(ys[j][(i - 1) % segs])
                    nb.append(ys[j][(i + 1) % segs])
                    nb = [v for v in nb if v is not None]
                    if nb:
                        ys[j][i] = sum(nb) / len(nb)
    else:
        print(f"[patch] {name}: 椭圆缩到 {scale:.2f}，全部射线命中")
    rx *= scale
    rz *= scale
    valid = [v for r in ys for v in r if v is not None]
    fallback = sum(valid) / len(valid) if valid else 0.0
    ys = [[fallback if v is None else v for v in r] for r in ys]

    # 薄片不是一个等厚的壳：越靠边越往里沉（t=1 时沉到表面下 6mm），
    # 这样边缘藏进主体里，看不到「贴了一块饼」的硬边，也不会 z-fighting。
    def _off(t):
        # 边缘往里沉（t=1 时沉到表面下 2mm），让薄片的边界藏进主体里，
        # 不然斜着看会看到一圈「贴在身上的饼边」。
        u = (t - 0.70) / 0.30
        if u <= 0.0:
            return offset
        if u >= 1.0:
            return -0.002
        s = u * u * (3.0 - 2.0 * u)
        return offset - (offset + 0.002) * s

    verts = [Vector((cx, ys[0][0] - offset, cz))]
    for j in range(1, rings + 1):
        t = (j / rings) ** 0.85
        d = _off(t)
        for i in range(segs):
            a = 2.0 * math.pi * i / segs
            verts.append(Vector((cx + rx * t * math.cos(a),
                                 ys[j][i] - d,
                                 cz + rz * t * math.sin(a))))

    faces = []
    for i in range(segs):
        faces.append((0, 1 + i, 1 + (i + 1) % segs))
    for j in range(rings - 1):
        b0 = 1 + j * segs
        b1 = 1 + (j + 1) * segs
        for i in range(segs):
            i2 = (i + 1) % segs
            faces.append((b0 + i, b1 + i, b1 + i2, b0 + i2))

    n = (verts[1] - verts[0]).cross(verts[2] - verts[0])
    if n.y > 0:                                  # 法线要朝前（-Y）
        faces = [tuple(reversed(f)) for f in faces]

    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material)
    shade_smooth(ob)
    return ob


def ellipsoid(name, center, radii, material, segs=32, rings=20):
    """一个椭球（手套／靴子这类叠在肢体上的零件）。"""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings,
                                         radius=1.0, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = radii
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    shade_smooth(o)
    o.data.materials.append(material)
    return o


def join_in_place(objs, name):
    """合并一批物体，保留世界坐标（先 apply scale 再 join）。"""
    bpy.ops.object.select_all(action="DESELECT")   # 别把场景里别的物体一起 apply
    for o in objs:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return join(objs, name)


def add_arc_tube(name, points, radius, material, res=6):
    """沿点列生成一根管子（用来做嘴线、眉毛）。"""
    cu = bpy.data.curves.new(name, type="CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 2
    cu.resolution_u = res
    sp = cu.splines.new("POLY")
    sp.points.add(len(points) - 1)
    for i, p in enumerate(points):
        sp.points[i].co = (p[0], p[1], p[2], 1.0)
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.materials.append(material)
    shade_smooth(o)
    return o


# --------------------------------------------------------------------------
# 归一化：缩到真实米数、脚底贴 z=0、X/Y 居中
# --------------------------------------------------------------------------


def world_bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def normalize(objs, target_height, center_x=True, ground=True):
    """按整体高度缩放到 target_height 米，脚底落在 z=0。"""
    bpy.context.view_layer.update()
    lo, hi = world_bbox(objs)
    h = hi.z - lo.z
    s = target_height / h if h > 1e-9 else 1.0
    cx = (lo.x + hi.x) * 0.5
    cy = (lo.y + hi.y) * 0.5
    for o in objs:
        o.scale = Vector((o.scale[i] * s for i in range(3)))
        o.location = Vector((
            (o.location.x - (cx if center_x else 0.0)) * s,
            (o.location.y - cy) * s,
            (o.location.z - (lo.z if ground else 0.0)) * s,
        ))
    bpy.context.view_layer.update()
    # 应用到网格数据，避免 Unity 里留一堆缩放
    for o in objs:
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()
    return s


# --------------------------------------------------------------------------
# 渲染预览
# --------------------------------------------------------------------------


def setup_scene_render(res=(620, 760), samples=32, bg=0.62):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 4
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"

    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (bg, bg, bg, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.30
    sc.world = world

    def lamp(name, co, energy, size, rot):
        d = bpy.data.lights.new(name, type="AREA")
        d.energy = energy
        d.size = size
        o = bpy.data.objects.new(name, d)
        o.location = co
        o.rotation_euler = rot
        bpy.context.collection.objects.link(o)
        return o

    lamp("Key", (2.6, -3.4, 3.2), 300, 3.0, (math.radians(52), 0, math.radians(38)))
    lamp("Fill", (-3.4, -2.6, 1.9), 120, 3.5, (math.radians(72), 0, math.radians(-52)))
    lamp("Rim", (0.0, 3.6, 2.6), 160, 2.5, (math.radians(-52), 0, 0))

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    return cam


def _look_at(cam, target):
    d = target - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_views(objs, out_path, views=None, margin=1.12):
    """正交 4 视角渲染，返回生成的文件列表。"""
    views = views or [("front", 90), ("q34", 140), ("side", 0), ("back", -90)]
    lo, hi = world_bbox(objs)
    center = (lo + hi) * 0.5
    height = hi.z - lo.z
    width = max(hi.x - lo.x, hi.y - lo.y)
    span = max(height, width) * margin
    cam = bpy.context.scene.camera
    cam.data.ortho_scale = span
    dist = span * 4.0
    out = []
    sc = bpy.context.scene
    for name, deg in views:
        a = math.radians(deg)
        cam.location = center + Vector((math.cos(a) * dist, math.sin(a) * dist, 0.0))
        _look_at(cam, center)
        sc.render.filepath = out_path.replace(".png", f"_{name}.png")
        bpy.ops.render.render(write_still=True)
        out.append(sc.render.filepath)
    return out


def montage(paths, dst, labels=None):
    """把几张预览横向拼成一张，方便一次看完。"""
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        return None
    ims = [Image.open(p).convert("RGB") for p in paths]
    w = sum(i.width for i in ims)
    h = max(i.height for i in ims)
    canvas = Image.new("RGB", (w, h), (40, 40, 40))
    x = 0
    for i in ims:
        canvas.paste(i, (x, 0))
        x += i.width
    if labels:
        d = ImageDraw.Draw(canvas)
        x = 0
        for i, lab in zip(ims, labels):
            d.text((x + 8, 8), lab, fill=(255, 255, 0))
            x += i.width
    canvas.save(dst)
    return dst


# --------------------------------------------------------------------------
# 导出
# --------------------------------------------------------------------------


def export_fbx(path, objs):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        use_custom_props=False,
        path_mode="AUTO",
        embed_textures=False,
    )
    return path


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def report(objs, title=""):
    lo, hi = world_bbox(objs)
    tris = sum(tri_count(o) for o in objs if o.type == "MESH")
    print(f"[{title}] 尺寸 {hi.x-lo.x:.3f} x {hi.y-lo.y:.3f} x {hi.z-lo.z:.3f} m, "
          f"三角面 {tris}, 物体 {len(objs)}")
    return tris

def flip_180_z(objs):
    """模型在 Blender 里朝 -Y（= Unity 的 -Z）。游戏里希望物体朝 +Z（Unity forward），
    所以导出前统一绕世界 Z 轴转 180°。
    直接改网格数据（new_world = R @ old_world），headless 下不用碰 ops。"""
    R = Matrix.Rotation(math.pi, 4, "Z")
    for o in objs:
        mw = o.matrix_world
        o.data.transform(mw.inverted() @ R @ mw)
        o.data.update()
    bpy.context.view_layer.update()

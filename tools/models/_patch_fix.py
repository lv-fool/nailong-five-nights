"""把 blib.ellipse_patch 的采样段换成「打不中就缩小椭圆重试」，一次性脚本。"""
import io, os, sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
path = os.path.join(REPO, "tools", "models", "blib.py")
src = io.open(path, encoding="utf-8").read()

start_marker = "    cx, cz = center\n    rx, rz = radii\n"
end_marker = "    ys = [[fallback if v is None else v for v in r] for r in ys]\n"

i = src.index(start_marker)
j = src.index(end_marker) + len(end_marker)

new = '''    cx, cz = center
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
'''

src = src[:i] + new + src[j:]
io.open(path, "w", encoding="utf-8", newline="\n").write(src)
print("patched", path)

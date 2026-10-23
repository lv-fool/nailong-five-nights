# -*- coding: utf-8 -*-
"""把导出的 FBX 重新导回 Blender，核对朝向与尺寸是否与导出前一致。

Blender 自己的 FBX 导入器和 Unity 的读取约定接近（都是 Y-up 的 FBX），
用它能在不启动 Unity 的情况下快速验证「导出没把模型转翻 / 缩错」。

运行：
    .tools/blender/blender.exe -b --python tools/models/check_fbx_roundtrip.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy                                          # noqa: E402
import blib as B                                    # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def main():
    for ch in ("nailong", "niulai", "daishu", "lulu"):
        fbx = os.path.join(REPO, "Assets", "Art", "Models", ch, ch + ".fbx")
        if not os.path.exists(fbx):
            print(f"[{ch}] 缺 FBX")
            continue
        B.reset()
        bpy.ops.import_scene.fbx(filepath=fbx)
        objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        if not objs:
            print(f"[{ch}] 导回后没有网格")
            continue
        lo, hi = B.world_bbox(objs)
        size = hi - lo
        roots = [o for o in bpy.context.scene.objects if o.parent is None]
        print(f"[{ch}] 网格 {len(objs)} 个，世界包围盒 "
              f"{size.x:.3f} x {size.y:.3f} x {size.z:.3f} m，"
              f"底面 z={lo.z:.3f}")
        for r in roots:
            print(f"    根 {r.name} loc={tuple(round(v, 3) for v in r.location)} "
                  f"rot={tuple(round(v, 1) for v in r.rotation_euler)} "
                  f"scale={tuple(round(v, 3) for v in r.scale)}")


if __name__ == "__main__":
    main()

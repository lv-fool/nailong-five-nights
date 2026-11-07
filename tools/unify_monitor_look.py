# -*- coding: utf-8 -*-
"""监控画质统一：把底图和角色都压到「同一台劣质摄像头」的质感。

## 为什么

用户 2026-11-07 反馈「画风有点草率」。根因不是素材差，是**精度不匹配**：
  · 角色立绘  高精度 3D 渲染，体积光影、边缘干净
  · 监控底图  低模盒子 + 程序噪声贴图，纯平几何

两边放在一起，角色像贴纸浮在上面。FNAF 原版也是低模，但全套都在同一个
渲染器里出，所以看着一致。

## 做法

不改素材内容，只加统一后期，让两边的「脏」变成同一种脏：
  1. 降到 720p 再放回 1080p   —— 消掉高清细节，两边都变糊
  2. 扫描线                    —— CRT 感
  3. 色差（RGB 错位）          —— 廉价镜头
  4. 块状压缩痕                —— 视频流压缩
  5. 噪点 + 暗角               —— 监控夜视
  6. 轻微偏青                  —— 老摄像头白平衡

先出样张对比，确认风格再决定接不接进 Unity。
"""
import os
import sys
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, ".workbuddy-ai", "style", "unify")

# ── 强度参数（改这些）──
DOWNSCALE     = 720      # 中间分辨率：越小越糊
SCANLINE      = 0.16     # 扫描线强度 0~1
CHROMA        = 2.0      # RGB 错位像素
BLOCK         = 8        # 压缩块尺寸（0 = 关）
NOISE         = 5.0      # 噪点标准差
VIGNETTE      = 0.42     # 暗角
CYAN_SHIFT    = (0.94, 1.00, 1.06)   # 偏青：R 降 B 升


def to_monitor_look(img, seed=0):
    """把一张图压成监控质感。输入/输出都是 RGBA。"""
    w, h = img.size
    rgb = img.convert("RGB")

    # 1) 降分辨率再放回 —— 这步最关键，先糊掉高清感
    small = rgb.resize((DOWNSCALE, int(DOWNSCALE * h / w)), Image.LANCZOS)
    rgb = small.resize((w, h), Image.BICUBIC)

    arr = np.asarray(rgb).astype(np.float32)
    rng = np.random.default_rng(seed)

    # 2) 块状压缩痕：每 BLOCK x BLOCK 块内取均值再混回去
    if BLOCK > 1:
        small_h = max(1, h // BLOCK)
        small_w = max(1, w // BLOCK)
        blk = rgb.resize((small_w, small_h), Image.BOX).resize((w, h), Image.NEAREST)
        arr = arr * 0.55 + np.asarray(blk).astype(np.float32) * 0.45

    # 3) 色差：R 左移、B 右移
    if CHROMA > 0:
        sh = int(round(CHROMA))
        if sh > 0:
            arr[:, sh:, 0] = arr[:, :-sh, 0]       # R 往右挪
            arr[:, :-sh, 2] = arr[:, sh:, 2]       # B 往左挪

    # 4) 偏青（老摄像头白平衡）
    arr *= np.array(CYAN_SHIFT, dtype=np.float32)

    # 5) 扫描线
    if SCANLINE > 0:
        ys = np.arange(h, dtype=np.float32)
        line = 1.0 - SCANLINE * (0.5 + 0.5 * np.cos(ys * np.pi))   # 隔行
        arr *= line[:, None, None]

    # 6) 噪点
    if NOISE > 0:
        arr += rng.normal(0.0, NOISE, arr.shape)

    # 7) 暗角
    if VIGNETTE > 0:
        yy, xx = np.mgrid[0:h, 0:w]
        nx = (xx - (w - 1) / 2.0) / (w / 2.0)
        ny = (yy - (h - 1) / 2.0) / (h / 2.0)
        d = np.sqrt(nx * nx + ny * ny) / np.sqrt(2.0)
        arr *= (1.0 - VIGNETTE * np.clip(d, 0, 1) ** 1.5)[:, :, None]

    arr = np.clip(arr, 0, 255).astype(np.uint8)
    out = Image.fromarray(arr, "RGB")

    # 保留原图的 alpha（角色立绘有透明背景）
    if img.mode == "RGBA":
        a = img.getchannel("A")
        out = out.convert("RGBA")
        out.putalpha(a)

    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    targets = [
        ("Assets/Art/Monitoring/mon_1a.png", "shot_1a"),
        ("Assets/Art/Monitoring/mon_7.png",  "shot_7"),
        ("Assets/Art/Characters/char_nailong_idle.png", "char_nailong"),
        ("Assets/Art/Characters/char_daishu_idle.png", "char_daishu"),
    ]
    for rel, tag in targets:
        p = os.path.join(ROOT, rel)
        if not os.path.exists(p):
            print("missing: " + rel)
            continue
        src = Image.open(p)
        before = src.convert("RGB")
        after = to_monitor_look(src if src.mode == "RGBA" else src.convert("RGB"),
                                seed=hash(tag) & 0xFFFF)
        after.convert("RGB").save(os.path.join(OUT, tag + "_after.png"))
        before.save(os.path.join(OUT, tag + "_before.png"))
        print("  %-16s %s -> done" % (tag, src.size))
    print("output: " + OUT)


if __name__ == "__main__":
    main()

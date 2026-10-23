# -*- coding: utf-8 -*-
"""从 Assets/Art/Characters/ 的角色立绘里量出「配色 + 剪影比例」。

用途：建模时椭球半径、颜色都不靠眼睛估，直接读参考图。

输出：
  .workbuddy-ai/models/ref_<char>.json   —— 调色板 + 每 5% 高度的半宽
  控制台摘要

用法：
  python tools/models/ref_analysis.py            # 全部四个
  python tools/models/ref_analysis.py nailong
"""
import json
import os
import sys

import numpy as np
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ART = os.path.join(REPO, "Assets", "Art", "Characters")
OUT = os.path.join(REPO, ".workbuddy-ai", "models")

CHARS = ["nailong", "niulai", "daishu", "lulu"]
VIEWS = ["idle", "stare"]


def load_rgba(path):
    im = Image.open(path)
    if im.mode != "RGBA":
        im = im.convert("RGBA")
    return np.asarray(im).astype(np.float32)


def foreground_mask(rgba):
    """前景 = 不透明 且 不是纯白背景。"""
    a = rgba[..., 3]
    rgb = rgba[..., :3]
    has_alpha = a.min() < 250
    opaque = (a > 8) if has_alpha else np.ones_like(a, dtype=bool)
    # 背景可能是纯白 / 纯黑（奶包那批是黑底），统一剔除「接近纯白」
    near_white = (rgb.min(axis=-1) > 244)
    return opaque & (~near_white)


def kmeans(px, k=7, iters=30, seed=3):
    """px: (N,3) 0..255。返回 [(weight, (r,g,b)), ...] 按权重降序。"""
    rng = np.random.default_rng(seed)
    if len(px) > 40000:
        idx = rng.choice(len(px), 40000, replace=False)
        px = px[idx]
    c = px[rng.choice(len(px), k, replace=False)]
    for _ in range(iters):
        d = ((px[:, None, :] - c[None, :, :]) ** 2).sum(-1)
        lab = d.argmin(1)
        for i in range(k):
            m = lab == i
            if m.any():
                c[i] = px[m].mean(0)
    d = ((px[:, None, :] - c[None, :, :]) ** 2).sum(-1)
    lab = d.argmin(1)
    out = []
    for i in range(k):
        m = lab == i
        if m.sum() < 0.01 * len(px):
            continue
        out.append((float(m.mean()), tuple(int(v) for v in c[i])))
    out.sort(key=lambda t: -t[0])
    return out


def hexs(rgb):
    return "#%02X%02X%02X" % rgb


def analyse(path):
    rgba = load_rgba(path)
    h, w = rgba.shape[:2]
    mask = foreground_mask(rgba)
    ys, xs = np.nonzero(mask)
    if len(ys) == 0:
        return None
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    bh, bw = y1 - y0, x1 - x0

    # 每 5% 高度一条带：半宽（相对人物总高的比例）、中心偏移
    bands = []
    for i in range(20):
        ya, yb = y0 + bh * i / 20.0, y0 + bh * (i + 1) / 20.0
        sub = mask[int(ya):max(int(ya) + 1, int(yb)), x0:x1]
        cols = np.nonzero(sub.any(0))[0]
        if len(cols) == 0:
            bands.append({"t": round(i / 20.0, 3), "half_w": 0.0, "cx": 0.0})
            continue
        half = (cols.max() - cols.min() + 1) / 2.0 / bh   # 以身高为 1 归一
        cx = ((cols.min() + cols.max() + 1) / 2.0 + x0 - (x0 + bw / 2.0)) / bh
        bands.append({"t": round(i / 20.0, 3), "half_w": round(float(half), 4),
                      "cx": round(float(cx), 4)})

    px = rgba[..., :3][mask]
    pal = [{"w": round(wt, 4), "rgb": rgb, "hex": hexs(rgb)} for wt, rgb in kmeans(px)]

    # 关键行采样：眼睛一行、胸腹一行、脚一行（按人物自身高度百分比）
    def sample_row(t):
        y = int(y0 + bh * t)
        row = rgba[y, x0:x1]                      # (W,4)
        m = foreground_mask(row[None, :, :])[0]   # (W,)
        if not m.any():
            return []
        c = kmeans(row[m][:, :3], k=4)
        return [{"w": round(a, 3), "hex": hexs(b)} for a, b in c]

    return {
        "src": os.path.relpath(path, REPO).replace("\\", "/"),
        "image_px": [w, h],
        "bbox": {"x": int(x0), "y": int(y0), "w": int(bw), "h": int(bh)},
        "aspect_wh": round(bw / bh, 3),
        "palette": pal,
        "bands_5pct": bands,
        "rows": {"25pct": sample_row(0.25), "50pct": sample_row(0.50),
                 "85pct": sample_row(0.85)},
    }


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else None
    os.makedirs(OUT, exist_ok=True)
    for ch in CHARS:
        if only and ch != only:
            continue
        for v in VIEWS:
            p = os.path.join(ART, f"char_{ch}_{v}.png")
            if not os.path.isfile(p):
                continue
            data = analyse(p)
            if not data:
                print(f"[{ch}_{v}] 找不到前景")
                continue
            dst = os.path.join(OUT, f"ref_{ch}_{v}.json")
            with open(dst, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=1)
            print(f"\n=== {ch}_{v} ===  bbox {data['bbox']}  aspect(w/h)={data['aspect_wh']}")
            print("  主色:")
            for e in data["palette"][:6]:
                print(f"    {e['w']*100:5.1f}%  {e['hex']}")
            top6 = " ".join(f"{b['t']:.2f}:{b['half_w']:.3f}" for b in data["bands_5pct"][:10])
            bot6 = " ".join(f"{b['t']:.2f}:{b['half_w']:.3f}" for b in data["bands_5pct"][10:])
            print("  半宽(上):", top6)
            print("  半宽(下):", bot6)
            print(f"  -> {os.path.relpath(dst, REPO)}")


if __name__ == "__main__":
    main()

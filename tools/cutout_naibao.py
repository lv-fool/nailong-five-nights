"""把 奶包/ 下的纯黑底 JPG 抠成透明 PNG，输出到 Assets/Resources/Naibao/。

背景是纯黑（0,0,0），所以做法很简单：从四条边灌水，把「连得上的黑」判成背景。
眼睛、发缝这些**不连到边缘**的黑会被保住 —— 这比按亮度一刀切安全得多
（07d616 那张的手机边框就是深色的，一刀切会把边框啃掉）。

边缘做 1px 羽化，羽化带里的颜色用「向外扩散」的亮色填充，避免留一圈黑边。

重跑：python tools/cutout_naibao.py
"""

import os
import glob
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "奶包")
DST = os.path.join(ROOT, "Assets", "Resources", "Naibao")

# 原图 → 输出名（名字按 AGENTS 的 char_<角色>_<姿势> 约定）
MAPPING = {
    "07d616f9bee5b96ed10c39a702d43b49_720.jpg": "char_naibao_peek",     # 手机屏里探身，皱眉嫌弃
    "755c7d0a3a9bc52a75126de9f91f86db_720.jpg": "char_naibao_guide",    # 微笑，手做「请」
    "c3a27f4b42f94c0f1994c6cf71c4260f.jpg": "char_naibao_huffy",        # 抬头叉腰撇嘴
    "437cd7e77cbe479c09be897e6ff79f51.jpg": "char_naibao_smug",         # 斜眼坏笑，叉腰
    "1cbe522cb436dd8c2c8ee6274d124657.jpg": "char_naibao_bored",        # 半睁眼垮脸
}

# 背景判定阈值：最大通道值不超过它就是「黑」。
# 取 10 而不是 0，是为了吃掉 JPG 压缩在纯黑区留下的噪点。
BLACK = 10


def background_alpha(rgb: np.ndarray) -> np.ndarray:
    """返回 alpha（0~255）：从边缘灌水，只把连通的黑色判成背景。"""
    lum = rgb.max(axis=2)
    # ⚠️ 必须 .copy()：Image.fromarray 出来的图在 Pillow 12 里是只读的，
    # ImageDraw.floodfill 遇到只读图会**静默什么都不做**（踩过一次，白抠了五张）。
    mask = Image.fromarray(np.where(lum <= BLACK, 255, 0).astype(np.uint8)).copy()

    w, h = mask.size
    seeds = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1),
             (w // 2, 0), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)]

    for seed in seeds:
        if mask.getpixel(seed) == 255:
            ImageDraw.floodfill(mask, seed, 128, thresh=0)

    arr = np.asarray(mask)
    return np.where(arr == 128, 0, 255).astype(np.uint8)


def bleed_edge_color(rgb: np.ndarray, alpha: np.ndarray, radius: int = 3) -> np.ndarray:
    """羽化带里的颜色往外扩一圈亮色，压掉黑边。

    做法：把整张图做一次 MaxFilter（相当于取邻域最亮的颜色），
    只在「半透明」的像素上用它替换原色 —— 内部实心像素不动。
    """
    if radius <= 0:
        return rgb

    img = Image.fromarray(rgb)
    spread = img.filter(ImageFilter.MaxFilter(radius * 2 + 1))
    spread_arr = np.asarray(spread)

    band = (alpha > 0) & (alpha < 250)
    if not band.any():
        return rgb

    out = rgb.copy()
    out[band] = spread_arr[band]
    return out


def process(path: str, name: str) -> None:
    img = Image.open(path).convert("RGB")
    rgb = np.asarray(img)

    alpha = background_alpha(rgb)
    rgb = bleed_edge_color(rgb, alpha)

    soft = Image.fromarray(alpha).filter(ImageFilter.GaussianBlur(1.1))
    alpha = np.asarray(soft).astype(np.uint8)

    # 羽化会把极淡的值扩散到整张图（于是 bbox 永远是整幅），这里把接近 0 的清掉
    alpha = np.where(alpha < 12, 0, alpha).astype(np.uint8)

    rgba = np.dstack([rgb, alpha])
    out_img = Image.fromarray(rgba, mode="RGBA")

    box = out_img.getbbox()
    if box is not None:
        pad = 4
        box = (max(0, box[0] - pad), max(0, box[1] - pad),
               min(out_img.width, box[2] + pad), min(out_img.height, box[3] + pad))
        out_img = out_img.crop(box)

    out_path = os.path.join(DST, name + ".png")
    out_img.save(out_path)

    coverage = float((alpha > 128).mean())
    print(f"{name:22s} {out_img.size[0]:4d}x{out_img.size[1]:4d}  覆盖 {coverage:.2%}")


def main() -> None:
    os.makedirs(DST, exist_ok=True)

    for src_name, out_name in MAPPING.items():
        path = os.path.join(SRC, src_name)
        if not os.path.exists(path):
            print(f"跳过（找不到）：{src_name}")
            continue
        process(path, out_name)

    # 预览用的合成图：深色背景上摆放，人眼能直接看边缘有没有黑边
    preview = Image.new("RGB", (1400, 900), (46, 48, 52))
    x = 10
    for _, out_name in MAPPING.items():
        p = os.path.join(DST, out_name + ".png")
        if not os.path.exists(p):
            continue
        sprite = Image.open(p)
        sprite.thumbnail((260, 860))
        preview.paste(sprite, (x, 20), sprite)
        x += 270
    os.makedirs(os.path.join(ROOT, ".setup"), exist_ok=True)
    preview.save(os.path.join(ROOT, ".setup", "naibao_cutout_preview.png"))
    print("预览：.setup/naibao_cutout_preview.png")


if __name__ == "__main__":
    main()

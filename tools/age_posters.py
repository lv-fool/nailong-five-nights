# -*- coding: utf-8 -*-
"""把海报做旧 —— 不改分辨率、不改宽高比。

## 迭代记录（都是踩出来的）

v1  无脑降饱和到 0.32 → 奶龙变灰白认不出来。角色是唯一识别点，毁了就白做。
v2  按色相加掩膜保护，但色相带太窄（H 0.10~0.18），只护住一小块，整体仍偏灰；
    克里姆特那种整幅金色的画还被误判成 32% 角色色。
v3  色相带放宽到 0.08~0.20、掩膜做闭运算 + 羽化；
    命中率 > 15% 判定为误判、整个关掉保护。
v4  亮度：第一版处理完均值从 65~126 掉到 31~55（暗角吃掉的面积远超预期），
    海报贴在灰墙上会糊成一片。改成按「原图均值」自动反推提亮系数。
"""
import os
from PIL import Image, ImageEnhance, ImageFilter
import numpy as np

# ⚠️ **必须从原始备份读，往资源目录写**。
# 第一版写成了 SRC == DST 原地覆盖，结果调一次参数就再压一遍 ——
# 跑了几轮之后胶片越来越灰，还以为是参数没调对（实际是反复做旧叠加）。
SRC = '.workbuddy-ai/poster_backup_original'
DST = 'Assets/Resources/Posters'

SAT_BG       = 0.34
SAT_CHAR     = 0.95
CONTRAST     = 1.06
WARM         = (1.04, 1.00, 0.94)
WHITE_CAP    = 0.97
NOISE        = 6.0
VIGNETTE     = 0.38
EDGE_YELLOW  = 0.32

# 提亮目标：处理完之后均值要达到原图的这个比例。
# 1.0 = 持平。略低一点点（0.92）保留做旧感，但别暗到看不清。
TARGET_MEAN_RATIO = 0.92

CHAR_H_LO, CHAR_H_HI = 0.08, 0.20
CHAR_S_LO = 0.30
CHAR_V_LO = 0.30

# ─────────────────────────────────────────────────────────────────────
# 监控后期（v5 追加，2026-11-07）
#
# 用户反馈「画风有点草率」。实测截图发现根因不是角色，是**海报**：
# 海报是 2048×1024 的高清 AI 图，饱和度拉满，而底图是程序渲的低模。
# 贴纸用的还是 UI/Default 材质，**一点劣化都没有** —— 像两块发光画布贴在灰墙上。
#
# 这里把监控画面那套后期烤进贴图。数值必须和 MonitorCRT.shader 对齐：
#   · 色偏 0.82 / 1.08 / 1.04
#   · 降饱和 0.34
#   · 降对比 ×0.78
#   · 扫描线 0.72~1.00
#   · 整体压暗 ×0.90
# 改了 shader 记得回来同步这里，不然海报又和底图分成两层。
# ─────────────────────────────────────────────────────────────────────
MONITOR_LOOK   = True     # 关了就是纯做旧（v4 行为）
MON_SAT        = 0.40     # 降饱和量（shader 是 0.34，海报本来就更艳，多压一点）
MON_CONTRAST   = 0.80     # 降对比（shader 0.78）
MON_SCAN       = 0.26     # 扫描线深度
# ⚠️ 间距按**目标显示尺寸**算，不按贴图尺寸 ——
# 海报贴到监控里只有约 300~600 像素宽（原图 1920），缩 3~6 倍。
# 所以这里 12px 一条，缩完差不多 2~4px，正好是底图那种横纹。
# 第一版写的 3px，缩图之后彻底看不见（踩过）。
MON_SCAN_LINES = 12
MON_NOISE      = 5.0      # 噪点
MON_TINT       = (0.86, 1.06, 1.03)   # 色偏：压红提绿蓝

MASK_SANITY = 0.15


def raw_mask(arr):
    rgb = arr / 255.0
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    delta = mx - mn + 1e-6

    h = np.zeros_like(mx)
    m = (mx == r)
    h[m] = ((g[m] - b[m]) / delta[m]) % 6
    m2 = (mx == g) & ~m
    h[m2] = (b[m2] - r[m2]) / delta[m2] + 2
    m3 = ~m & ~m2
    h[m3] = (r[m3] - g[m3]) / delta[m3] + 4
    h = h / 6.0

    s = np.where(mx > 1e-6, delta / (mx + 1e-6), 0.0)
    v = mx

    hue_ok = (h >= CHAR_H_LO) & (h <= CHAR_H_HI)
    return (hue_ok & (s >= CHAR_S_LO) & (v >= CHAR_V_LO)).astype(np.float32)


def build_char_mask(arr, shape):
    m = raw_mask(arr)
    ratio = float(m.mean())

    if ratio > MASK_SANITY or ratio <= 0.0002:
        return np.zeros(shape, dtype=np.float32), ratio, False

    m_img = Image.fromarray((m * 255).astype(np.uint8), 'L')
    m_img = m_img.filter(ImageFilter.MaxFilter(9))
    m_img = m_img.filter(ImageFilter.MinFilter(7))
    m_img = m_img.filter(ImageFilter.MaxFilter(5))
    m_img = m_img.filter(ImageFilter.GaussianBlur(radius=6))

    out = np.asarray(m_img).astype(np.float32) / 255.0
    return np.clip(out, 0, 1), ratio, True


def process(path, out_path):
    im = Image.open(path).convert('RGB')
    arr0 = np.asarray(im).astype(np.float32)
    h, w = arr0.shape[:2]
    mean0 = float(arr0.mean())

    w_char, ratio, used = build_char_mask(arr0, (h, w))
    w_char3 = w_char[:, :, None]

    # 降饱和：背景重、角色轻
    sat_bg = np.asarray(ImageEnhance.Color(im).enhance(SAT_BG)).astype(np.float32)
    sat_char = np.asarray(ImageEnhance.Color(im).enhance(SAT_CHAR)).astype(np.float32)
    arr = sat_bg * (1 - w_char3) + sat_char * w_char3

    im = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGB')
    im = ImageEnhance.Contrast(im).enhance(CONTRAST)
    arr = np.asarray(im).astype(np.float32)

    # 暖黄
    arr = np.clip(arr * np.array(WARM, dtype=np.float32), 0, 255)

    # 噪点
    rng = np.random.default_rng(20261105)
    arr += rng.normal(0.0, NOISE, arr.shape)

    # 暗角
    if VIGNETTE > 0:
        yy, xx = np.mgrid[0:h, 0:w]
        nx = (xx - (w - 1) / 2.0) / (w / 2.0)
        ny = (yy - (h - 1) / 2.0) / (h / 2.0)
        d = np.sqrt(nx * nx + ny * ny) / np.sqrt(2.0)
        arr *= (1.0 - VIGNETTE * np.clip(d, 0, 1) ** 1.6)[:, :, None]

    # 边缘泛黄
    if EDGE_YELLOW > 0:
        yy, xx = np.mgrid[0:h, 0:w]
        ex = np.minimum(xx, w - 1 - xx) / (w / 2.0)
        ey = np.minimum(yy, h - 1 - yy) / (h / 2.0)
        e = 1.0 - np.clip(np.minimum(ex, ey) / 0.35, 0, 1)
        a = (EDGE_YELLOW * e)[:, :, None]
        tint = np.array([1.12, 1.03, 0.80], dtype=np.float32)
        arr = arr * (1 - a) + (arr * tint) * a

    # ★ 自动提亮：按当前均值反推系数，做到「处理完 = 原图 × TARGET_MEAN_RATIO」
    #   硬编码系数在不同明暗的图上表现天差地别 —— 暗画更暗、亮画刚好。
    cur = float(np.clip(arr, 0, 255).mean())
    if cur > 1e-3:
        gain = (mean0 * TARGET_MEAN_RATIO) / cur
        gain = float(np.clip(gain, 0.5, 4.0))
        arr = arr * gain

    # ★★ 监控后期 —— 让海报和底图/敌人处在同一个「监控画面」里
    if MONITOR_LOOK:
        arr = np.clip(arr, 0, 255)

        # 降饱和
        lum = arr[:, :, 0] * 0.299 + arr[:, :, 1] * 0.587 + arr[:, :, 2] * 0.114
        for ch in range(3):
            arr[:, :, ch] = arr[:, :, ch] * (1 - MON_SAT) + lum * MON_SAT

        # 降对比（往 128 靠）
        arr = (arr - 128.0) * MON_CONTRAST + 128.0

        # 色偏
        arr *= np.array(MON_TINT, dtype=np.float32)

        # 扫描线：整张图一起压，和底图那层横纹对得上
        if MON_SCAN > 0 and MON_SCAN_LINES > 0:
            ys = np.arange(h, dtype=np.float32)
            phase = (ys % MON_SCAN_LINES) / float(MON_SCAN_LINES)
            arr *= (1.0 - MON_SCAN * phase)[:, None, None]

        # 噪点
        if MON_NOISE > 0:
            arr += rng.normal(0.0, MON_NOISE, arr.shape)

        # 整体压暗（老监控屏不会那么透亮）
        arr *= 0.92

    arr = np.clip(arr, 0, 255) * WHITE_CAP
    arr = np.clip(arr, 0, 255).astype(np.uint8)

    out = Image.fromarray(arr, 'RGB')
    out = out.filter(ImageFilter.GaussianBlur(radius=0.35))
    out.save(out_path, 'JPEG', quality=84, subsampling=2, optimize=True)

    return out.size, ratio, used, mean0, float(np.asarray(out).mean())


def main():
    files = sorted(f for f in os.listdir(SRC) if f.lower().endswith('.jpg'))
    print('共 %d 张' % len(files))
    for f in files:
        size, ratio, used, m0, m1 = process(
            os.path.join(SRC, f), os.path.join(DST, f))
        print('  %-16s %sx%-5s 命中%5.2f%% %s  亮度 %5.1f -> %5.1f' % (
            f, size[0], size[1], ratio * 100, '保护' if used else '不保护', m0, m1))
    print('完成')


if __name__ == '__main__':
    main()

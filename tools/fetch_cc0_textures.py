#!/usr/bin/env python3
"""下载 tools/cc0/ 里那几张 CC0 贴图。

来源 ambientCG（<https://ambientcg.com>），许可 CC0 1.0 —— 可商用、无需署名。
包里东西很多（颜色 / 法线 / 粗糙度 / 置换 …），这里**只要 `*_Color.jpg`**。

⚠️ `acg-media.struffelproductions.com` 对**不带 User-Agent** 的请求返回 403，
所以下面所有请求都显式加了 UA 头。（curl 默认带，所以用 curl 试的时候看不出来。）

运行：
    python tools/fetch_cc0_textures.py
"""

import io
import os
import urllib.request
import zipfile

UA = {"User-Agent": "Mozilla/5.0"}

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tools", "cc0")

# ambientCG 资产 ID → 本地文件名
JOBS = {
    "Tiles074": "floor_tiles.jpg",             # 地板：黑白棋盘砖
    "PaintedPlaster017": "wall_plaster.jpg",   # 墙面
    "Concrete046": "concrete.jpg",             # 天花板 / 办公桌
    "Metal027": "metal.jpg",                   # 门框 / 卷帘门 / 通风口
    "Plaster001": "paper.jpg",                 # 儿童画的纸底
}


def fetch(asset_id, name):
    url = "https://ambientcg.com/get?file=%s_1K-JPG.zip" % asset_id
    raw = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180).read()
    zf = zipfile.ZipFile(io.BytesIO(raw))
    color = [n for n in zf.namelist() if "Color" in n and n.lower().endswith(".jpg")]
    if not color:
        raise RuntimeError("%s 里没找到 Color 图，实际内容：%s" % (asset_id, zf.namelist()))

    path = os.path.join(OUT, name)
    with open(path, "wb") as fh:
        fh.write(zf.read(color[0]))
    print("  %-16s ← %s (%s, %d KB)"
          % (name, asset_id, color[0], os.path.getsize(path) // 1024))


def main():
    os.makedirs(OUT, exist_ok=True)
    print("下载 CC0 贴图到 tools/cc0/ …")
    for asset_id, name in JOBS.items():
        fetch(asset_id, name)
    print("完成。")


if __name__ == "__main__":
    main()

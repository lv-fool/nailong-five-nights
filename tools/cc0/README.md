# CC0 贴图源（第三方素材）

这些不是本项目原创素材，是从 **ambientCG**（<https://ambientcg.com>）下载的贴图。
许可 **CC0 1.0（公共领域）** —— 可商用、可修改、**无需署名**。
放一份原图在这里是为了让 `tools/gen_placeholder_art.py` 可复现：换台机器、换个时间
重跑，渲出来的背景是一样的。

| 文件 | ambientCG 资产 | 用途 |
|---|---|---|
| `floor_tiles.jpg` | [Tiles074](https://ambientcg.com/view?id=Tiles074) | 地板（黑白棋盘砖） |
| `wall_plaster.jpg` | [PaintedPlaster017](https://ambientcg.com/view?id=PaintedPlaster017) | 墙面 |
| `concrete.jpg` | [Concrete046](https://ambientcg.com/view?id=Concrete046) | 天花板 / 办公桌 |
| `metal.jpg` | [Metal027](https://ambientcg.com/view?id=Metal027) | 门框 / 卷帘门 / 通风口 |
| `paper.jpg` | [Plaster001](https://ambientcg.com/view?id=Plaster001) | 墙上儿童画的纸底 |

都是 1K-JPG 包里的 `*_Color.jpg`（只取颜色图，法线 / 粗糙度那几张没要）。

## 重新下载

```bash
python tools/fetch_cc0_textures.py
```

（脚本用 `urllib`，**必须带 User-Agent** —— `acg-media.*` 对没有 UA 的请求返回 403。）

## 想换贴图

改上面那张表对应的文件名，或者直接覆盖同名文件，然后重跑：

```bash
C:/Users/DELL/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe tools/gen_placeholder_art.py
```

贴图本身是**可平铺**的（ambientCG 的图都是无缝的），所以随便换别的资产也不会出接缝。

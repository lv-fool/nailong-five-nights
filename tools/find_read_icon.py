# -*- coding: utf-8 -*-
"""在豆包聊天页的截图里，找出最新那条回复下面的「朗读」图标坐标。

为什么需要它：图标那一排的位置随消息高度浮动，靠猜坐标点空了好几次。
这里用像素找：最后一行文字 → 它下方 30~45 px 的那排小图标 → 取第 2 个（朗读）。

用法：
    python tools/find_read_icon.py <截图.png>

输出：`x y`（窗口本地像素，可直接给 click 用）；找不到就打印 not-found 并返回 1。
"""

import sys

import numpy as np
from PIL import Image


def dark_mask(img):
    a = np.asarray(img.convert("L"), dtype=np.int16)
    return a < 140


def main():
    if len(sys.argv) < 2:
        print("用法: find_read_icon.py <截图.png>")
        return 2

    img = Image.open(sys.argv[1])
    mask = dark_mask(img)
    h, w = mask.shape

    # 只关心聊天区（避开左侧栏和输入框）
    x0, x1 = 480, min(w - 60, 1300)
    band = mask[:, x0:x1]

    counts = band.sum(axis=1)

    # 最后一段「有字」的行（输入框在 y>790，避开）
    limit = min(h, 790)
    rows = [y for y in range(200, limit) if counts[y] > 3]
    if not rows:
        print("not-found: 没找到文字行")
        return 1

    last = rows[-1]
    # 往上找这一行文字的起点，得到它的高度
    top = last
    while top - 1 >= 200 and counts[top - 1] > 3:
        top -= 1

    # 图标排一般在文字底部往下 25~50 px
    icon_y = None
    for y in range(last + 12, min(last + 60, limit)):
        row = mask[y, x0:x1]
        # 图标排的特征：暗像素不多，但分成好几小簇
        cols = np.where(row)[0]
        if cols.size < 6 or cols.size > 90:
            continue
        clusters = 1
        for i in range(1, cols.size):
            if cols[i] - cols[i - 1] > 8:
                clusters += 1
        if clusters >= 4:
            icon_y = y
            break

    if icon_y is None:
        print("not-found: 文字底部 %d 下面没找到图标排" % last)
        return 1

    # 在这一排里找列簇，取第 2 个（第 1 个是复制）
    cols = np.where(mask[icon_y, x0:x1])[0]
    groups = []
    cur = [cols[0]]
    for i in range(1, cols.size):
        if cols[i] - cols[i - 1] > 8:
            groups.append(cur)
            cur = [cols[i]]
        else:
            cur.append(cols[i])
    groups.append(cur)

    if len(groups) < 2:
        print("not-found: 这排只有 %d 个图形" % len(groups))
        return 1

    second = groups[1]
    cx = x0 + int(round((second[0] + second[-1]) / 2.0))

    print("%d %d" % (cx, icon_y))
    return 0


if __name__ == "__main__":
    sys.exit(main())

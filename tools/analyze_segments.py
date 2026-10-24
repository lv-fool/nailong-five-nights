"""分析一段录音里「哪里有人在说话」，用来判断豆包到底开口了没有。

用法: python tools/analyze_segments.py <wav> [阈值系数]
"""

import sys
import wave

import numpy as np


def load(path):
    with wave.open(path) as f:
        n = f.getnframes()
        rate = f.getframerate()
        ch = f.getnchannels()
        raw = f.readframes(n)
    data = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    if ch > 1:
        data = data.reshape(-1, ch).mean(axis=1)
    return data, rate


def segments(data, rate, rel=0.08, min_len=0.15):
    win = max(1, int(rate * 0.02))
    trimmed = data[: len(data) // win * win]
    env = np.sqrt(np.mean(trimmed.reshape(-1, win) ** 2, axis=1))
    thr = max(0.015, float(env.max()) * rel)
    active = env > thr
    out = []
    start = None
    for i, a in enumerate(active):
        if a and start is None:
            start = i
        elif not a and start is not None:
            if (i - start) * 0.02 >= min_len:
                out.append((start * 0.02, i * 0.02))
            start = None
    if start is not None:
        out.append((start * 0.02, len(active) * 0.02))
    return out, thr, float(env.max())


def main():
    path = sys.argv[1]
    rel = float(sys.argv[2]) if len(sys.argv) > 2 else 0.08
    data, rate = load(path)
    segs, thr, peak = segments(data, rate, rel)
    print(f"文件: {path}")
    print(f"总长 {len(data) / rate:.2f}s  峰值 {peak:.3f}  阈值 {thr:.3f}")
    print(f"有声段 {len(segs)} 个，合计 {sum(e - s for s, e in segs):.2f}s")
    for s, e in segs[:20]:
        print(f"  {s:7.2f} - {e:7.2f}  ({e - s:.2f}s)")


if __name__ == "__main__":
    main()

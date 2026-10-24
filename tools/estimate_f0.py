"""粗估一段人声的基频，用来判断录音里到底是人声还是别的东西。

原理：取有声段里若干帧，做自相关，找 70~350Hz 范围内的最强周期。
女声一般 180~260Hz，男声 90~150Hz。
"""

import sys
import wave

import numpy as np


def load(path):
    with wave.open(path) as f:
        rate = f.getframerate()
        ch = f.getnchannels()
        raw = f.readframes(f.getnframes())
    d = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    if ch > 1:
        d = d.reshape(-1, ch).mean(axis=1)
    return d, rate


def f0_track(data, rate, frame=0.04, hop=0.02, lo=70, hi=350):
    n = int(frame * rate)
    h = int(hop * rate)
    lo_lag = int(rate / hi)
    hi_lag = int(rate / lo)
    out = []
    for start in range(0, len(data) - n, h):
        seg = data[start:start + n]
        if np.sqrt(np.mean(seg ** 2)) < 0.02:
            continue
        seg = seg - seg.mean()
        ac = np.correlate(seg, seg, mode="full")[n - 1:]
        if ac[0] <= 0:
            continue
        window = ac[lo_lag:hi_lag]
        if window.size == 0:
            continue
        lag = lo_lag + int(np.argmax(window))
        out.append(rate / lag)
    return np.array(out)


def main():
    path = sys.argv[1]
    data, rate = load(path)
    track = f0_track(data, rate)
    if track.size == 0:
        print("没找到明显的周期性 — 可能是噪声或静音")
        return
    print(f"{path}")
    print(f"  有声帧 {track.size}")
    print(f"  基频中位数 {np.median(track):.0f} Hz   10%~90% 区间 "
          f"{np.percentile(track, 10):.0f} ~ {np.percentile(track, 90):.0f} Hz")


if __name__ == "__main__":
    main()

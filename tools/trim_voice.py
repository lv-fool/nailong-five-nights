"""把录到的长 wav 里「真正有人在说话」的那一段剪出来，做归一化，存成单声道 24k wav。

用法:
    python tools/trim_voice.py <输入.wav> <输出.wav> [前置留白=0.25] [后置留白=0.35]

Unity 侧用 wav 也能直接读，所以先不转 ogg（要转的话等装了 ffmpeg 再批量做）。
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


def find_span(data, rate, rel=0.06):
    win = max(1, int(rate * 0.02))
    trimmed = data[: len(data) // win * win]
    env = np.sqrt(np.mean(trimmed.reshape(-1, win) ** 2, axis=1))
    thr = max(0.012, float(env.max()) * rel)
    idx = np.where(env > thr)[0]
    if idx.size == 0:
        return 0, len(data)
    return int(idx[0] * win), int((idx[-1] + 1) * win)


def resample_to(data, rate, target=24000):
    if rate == target:
        return data
    n = int(len(data) * target / rate)
    src = np.linspace(0, len(data) - 1, n)
    return np.interp(src, np.arange(len(data)), data).astype(np.float32)


def write(path, data, rate=24000):
    peak = float(np.max(np.abs(data))) if data.size else 0.0
    gain = 0.89 / peak if peak > 1e-4 else 1.0
    pcm = np.clip(data * gain, -1.0, 1.0)
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes((pcm * 32767).astype(np.int16).tobytes())
    return gain, len(data) / rate


def main():
    src, dst = sys.argv[1], sys.argv[2]
    pre = float(sys.argv[3]) if len(sys.argv) > 3 else 0.25
    post = float(sys.argv[4]) if len(sys.argv) > 4 else 0.35

    data, rate = load(src)
    start, end = find_span(data, rate)
    start = max(0, start - int(pre * rate))
    end = min(len(data), end + int(post * rate))
    cut = resample_to(data[start:end], rate)
    gain, dur = write(dst, cut)
    print(f"{src} -> {dst}  有效 {dur:.2f}s  增益 x{gain:.2f}")


if __name__ == "__main__":
    main()

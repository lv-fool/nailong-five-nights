"""把一次「连着念好几行」的录音切成一句一个文件。

豆包网页的「朗读」会把整条消息一起念，句子之间有明显停顿；于是可以一次录 5 句，
再按停顿切开。这里只做机械活：找有声段 → 合并被阈值切碎的碎片 → 数量对不上就报错。

用法：

    python tools/split_voice.py <录音.wav> <输出目录> <id1> <id2> ... [--gap 0.32]

数量对不上会返回 2 并且不写任何文件（宁可不写，也别把错位的音配上去）。
"""

import os
import sys
import wave

import numpy as np


def load_mono(path):
    with wave.open(path, "rb") as f:
        frames = f.readframes(f.getnframes())
        ch = f.getnchannels()
        rate = f.getframerate()
    data = np.frombuffer(frames, dtype=np.int16).astype(np.float32) / 32768.0
    if ch > 1:
        data = data.reshape(-1, ch).mean(axis=1)
    return data, rate


def resample(data, src_rate, dst_rate=24000):
    if src_rate == dst_rate:
        return data
    n = int(round(len(data) * dst_rate / float(src_rate)))
    x_old = np.linspace(0.0, 1.0, len(data), endpoint=False)
    x_new = np.linspace(0.0, 1.0, n, endpoint=False)
    return np.interp(x_new, x_old, data).astype(np.float32)


def find_segments(data, rate, gap=0.32, hop=0.01, win=0.02):
    """按能量找有声段，再把间隔小于 gap 的段合并。"""
    hop_n = max(1, int(rate * hop))
    win_n = max(hop_n, int(rate * win))

    frames = 1 + max(0, (len(data) - win_n)) // hop_n
    energy = np.zeros(frames, dtype=np.float32)
    for i in range(frames):
        chunk = data[i * hop_n: i * hop_n + win_n]
        if chunk.size:
            energy[i] = float(np.sqrt(np.mean(np.square(chunk))))

    peak = float(energy.max()) if energy.size else 0.0
    if peak <= 0:
        return []

    # 阈值取「峰值的 4%」和「噪声底 3 倍」里较大的那个 —— 纯 4% 会把气声切碎
    noise = float(np.percentile(energy, 20))
    thr = max(peak * 0.04, noise * 3.0, 0.0015)

    voiced = energy >= thr
    spans = []
    start = None
    for i, v in enumerate(voiced):
        if v and start is None:
            start = i
        elif not v and start is not None:
            spans.append([start * hop, i * hop])
            start = None
    if start is not None:
        spans.append([start * hop, len(voiced) * hop])

    # 去掉太短的毛刺，再把挨得近的合并
    spans = [s for s in spans if s[1] - s[0] >= 0.10]
    merged = []
    for s in spans:
        if merged and s[0] - merged[-1][1] < gap:
            merged[-1][1] = s[1]
        else:
            merged.append(list(s))

    return merged


def save(path, data, rate, pre=0.20, post=0.28):
    a = max(0, int((data[0]) * rate) - int(pre * rate))
    b = min(len(data[1]), int((data[1]) * rate) + int(post * rate))
    return a, b


def main(argv):
    if len(argv) < 4:
        print("用法: split_voice.py <录音.wav> <输出目录> <id...> [--gap 0.32]")
        return 2

    src, outdir = argv[1], argv[2]
    rest = argv[3:]
    gap = 0.32
    if "--gap" in rest:
        i = rest.index("--gap")
        gap = float(rest[i + 1])
        rest = rest[:i] + rest[i + 2:]
    ids = rest

    raw, rate = load_mono(src)
    spans = find_segments(raw, rate, gap=gap)

    print("找到 %d 段，期望 %d 段" % (len(spans), len(ids)))
    for k, s in enumerate(spans):
        print("  %2d  %6.2f - %6.2f  (%.2fs)" % (k + 1, s[0], s[1], s[1] - s[0]))

    if len(spans) != len(ids):
        print("数量对不上，没有写任何文件。要么重录，要么把 --gap 调大/调小。")
        return 2

    if not os.path.isdir(outdir):
        os.makedirs(outdir)

    for (a, b), name in zip(spans, ids):
        s0 = max(0, int(a * rate) - int(0.20 * rate))
        s1 = min(len(raw), int(b * rate) + int(0.28 * rate))
        piece = raw[s0:s1]
        piece = resample(piece, rate)

        peak = float(np.max(np.abs(piece))) if piece.size else 0.0
        if peak > 0:
            piece = piece * (0.89 / peak)
        pcm = np.clip(piece, -1.0, 1.0)
        data16 = (pcm * 32767).astype(np.int16)

        path = os.path.join(outdir, name + ".wav")
        with wave.open(path, "wb") as f:
            f.setnchannels(1)
            f.setsampwidth(2)
            f.setframerate(24000)
            f.writeframes(data16.tobytes())

        print("  -> %-22s %5.2fs" % (name + ".wav", len(data16) / 24000.0))

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

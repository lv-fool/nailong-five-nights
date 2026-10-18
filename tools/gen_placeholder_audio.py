#!/usr/bin/env python3
"""合成占位环境音（开发阶段用）。

项目作者有真素材后，同名覆盖 Assets/Audio/Ambient/ 即可，代码不用改。
全部是 44.1kHz / 16-bit / 单声道 WAV，并且做了首尾交叉淡化，能无缝循环。

运行：
    python tools/gen_placeholder_audio.py
"""

import os
import wave

import numpy as np

SR = 44100
DURATION = 30.0
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Audio", "Ambient")


def save_wav(path, samples):
    pcm = (np.clip(samples, -1.0, 1.0) * 32767.0).astype(np.int16)
    with wave.open(path, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SR)
        handle.writeframes(pcm.tobytes())


def lowpass(x, k):
    """移动平均低通。用累积和实现，比卷积快。"""
    cumsum = np.cumsum(np.insert(x, 0, 0.0))
    filtered = (cumsum[k:] - cumsum[:-k]) / k
    return np.concatenate([np.full(k - 1, filtered[0]), filtered])[:len(x)]


def seamless(sig, fade_seconds=0.75):
    """把尾巴叠回开头，得到一个能首尾相接的循环。"""
    n = min(int(fade_seconds * SR), len(sig) // 4)
    out = sig[:-n].copy()
    ramp = np.linspace(0.0, 1.0, n)
    out[:n] = sig[:n] * ramp + sig[-n:] * (1.0 - ramp)
    return out


def normalize(sig, peak):
    return sig / (np.max(np.abs(sig)) + 1e-9) * peak


def fan():
    """办公室风扇：低通白噪声 + 缓慢的转速起伏。"""
    rng = np.random.default_rng(7)
    n = int(SR * DURATION)
    body = lowpass(rng.normal(0.0, 1.0, n), 60)

    t = np.arange(n) / SR
    # 频率取 30 秒的整数个周期，保证循环点不跳
    wobble = 1.0 + 0.10 * np.sin(2 * np.pi * 0.7 * t) + 0.04 * np.sin(2 * np.pi * 2.3 * t)

    return seamless(normalize(body * wobble, 0.35))


def hum():
    """电流嗡鸣：50Hz 基频加谐波，再掺一点嘶声。"""
    n = int(SR * DURATION)
    t = np.arange(n) / SR

    tone = (np.sin(2 * np.pi * 50 * t) * 0.60
            + np.sin(2 * np.pi * 100 * t) * 0.25
            + np.sin(2 * np.pi * 150 * t) * 0.12)

    rng = np.random.default_rng(13)
    tone += lowpass(rng.normal(0.0, 1.0, n), 200) * 0.15

    return seamless(normalize(tone, 0.22))


def distant_metal():
    """远处金属声：低频隆隆 + 几次遥远的撞击。"""
    n = int(SR * DURATION)
    rng = np.random.default_rng(29)

    sig = lowpass(rng.normal(0.0, 1.0, n), 400) * 0.5

    # 撞击放在中段，避开首尾，循环才干净
    length = int(SR * 1.2)
    decay = np.exp(-np.linspace(0.0, 6.0, length))
    strike_t = np.arange(length) / SR
    strike = (np.sin(2 * np.pi * 180 * strike_t)
              + 0.5 * np.sin(2 * np.pi * 340 * strike_t)) * decay

    for position in (0.28, 0.55, 0.79):
        start = int(position * n)
        sig[start:start + length] += strike * 0.25

    return seamless(normalize(sig, 0.30))


def main():
    os.makedirs(OUT, exist_ok=True)

    tracks = [
        ("amb_fan_loop.wav", fan()),
        ("amb_electric_hum.wav", hum()),
        ("amb_distant_metal.wav", distant_metal()),
    ]

    for name, samples in tracks:
        path = os.path.join(OUT, name)
        save_wav(path, samples)
        print("wrote", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()

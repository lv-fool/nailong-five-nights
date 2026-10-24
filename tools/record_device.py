# -*- coding: utf-8 -*-
"""录指定输出设备的环回（默认设备可能不是真正在出声的那一路）。

用法：
    python tools/record_device.py <输出.wav> <秒数> [设备名片段]

不给设备名就用 Windows 默认输出。给了就按名字片段匹配第一台。
"""

import sys
import wave

import numpy as np
import soundcard as sc


def pick_speaker(fragment):
    speakers = sc.all_speakers()
    if not fragment:
        return sc.default_speaker()
    for s in speakers:
        if fragment.lower() in s.name.lower():
            return s
    raise SystemExit("没找到设备：" + fragment + "（可选：" + ", ".join(s.name for s in speakers) + "）")


def main():
    out = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
    fragment = sys.argv[3] if len(sys.argv) > 3 else ""

    rate = 48000
    speaker = pick_speaker(fragment)
    mic = sc.get_microphone(id=str(speaker.name), include_loopback=True)

    frames = int(rate * seconds)
    with mic.recorder(samplerate=rate, channels=2) as rec:
        data = rec.record(numframes=frames)

    peak = float(np.max(np.abs(data))) if data.size else 0.0
    pcm = np.clip(data, -1.0, 1.0)
    pcm16 = (pcm * 32767).astype(np.int16)

    with wave.open(out, "wb") as f:
        f.setnchannels(2)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes(pcm16.tobytes())

    rms = float(np.sqrt(np.mean(np.square(data)))) if data.size else 0.0
    print("设备=%s  峰值=%.3f  RMS=%.4f  -> %s" % (speaker.name, peak, rms, out))


if __name__ == "__main__":
    main()

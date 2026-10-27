"""用 WASAPI loopback 把「系统正在播放的声音」录成 wav。

和 OBS 里的「桌面音频」是同一路信号，区别是这里能精确控制起止、直接得到 wav，
不用再从 mkv 里抽音轨。用法：

    python tools/record_loopback.py <输出.wav> <秒数>

录完会打印时长、峰值和有效音量（RMS），方便判断这一条到底有没有声音。
"""

import sys
import wave

import numpy as np
import soundcard as sc


def record(path, seconds, rate=48000):
    speaker = sc.default_speaker()
    mic = sc.get_microphone(id=str(speaker.name), include_loopback=True)

    frames = int(rate * seconds)
    with mic.recorder(samplerate=rate, channels=2) as rec:
        data = rec.record(numframes=frames)

    # 转成 16 位整数，统一削一下爆音
    peak = float(np.max(np.abs(data))) if data.size else 0.0
    pcm = np.clip(data, -1.0, 1.0)
    pcm16 = (pcm * 32767).astype(np.int16)

    with wave.open(path, "wb") as f:
        f.setnchannels(2)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes(pcm16.tobytes())

    rms = float(np.sqrt(np.mean(np.square(data)))) if data.size else 0.0
    print(f"写出 {path}  {seconds:.2f}s  峰值={peak:.3f}  RMS={rms:.4f}")
    return peak, rms


if __name__ == "__main__":
    out = sys.argv[1]
    secs = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
    record(out, secs)

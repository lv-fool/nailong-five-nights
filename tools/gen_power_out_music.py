"""生成断电音乐：八音盒音色的托雷多进行曲主题。

为什么是这首：FNAF 1 断电后 Freddy 出场时播的就是 Toreador March
（比才《卡门》1875，公有领域）。原版用的是 Sound Ideas 音效库的
"1905 Regina Music Box" 采样（有版权），所以这里**自己合成**一段八音盒，
音色与旋律都是原创录音，可随包分发。

旋律按 F 大调、BPM 120 写。改旋律直接改下面的 MELODY / BASS 表。

用法：
    python tools/gen_power_out_music.py
输出：
    Assets/Audio/SFX/sfx_power_out_music.wav
"""
import os
import struct
import wave

import numpy as np

SAMPLE_RATE = 44100
BPM = 120.0
BEAT_SECONDS = 60.0 / BPM

# 八音盒的梳齿谐波：基频 + 一串逐渐变弱的泛音，
# 高次泛音轻微失谐，出来才是「金属梳齿」而不是电子琴。
HARMONICS = [
    (1.0, 1.000),
    (2.0, 0.42),
    (3.0, 0.26),
    (4.0, 0.15),
    (5.0, 0.095),
    (6.0, 0.065),
    (8.0, 0.035),
]

# 主旋律（MIDI 音高, 拍数）。1 = F4。
# "Toréador, en garde! Toréador! Toréador!"
MELODY = [
    (65, 1), (65, 1), (67, 1), (69, 1),      # 小节 1  Tor-é-a-dor
    (65, 1), (69, 1), (67, 1), (65, 1),      # 小节 2  en gar-de
    (64, 2), (65, 2),                        # 小节 3  落回主音
    (65, 1), (65, 1), (67, 1), (69, 1),      # 小节 4  反复
    (65, 1), (69, 1), (67, 1), (65, 1),      # 小节 5
    (64, 2), (65, 2),                        # 小节 6
]

# 低音伴奏（MIDI 音高, 拍数）—— 每拍一下，根音与五音交替
BASS = [
    (41, 1), (48, 1), (41, 1), (48, 1),
    (48, 1), (43, 1), (48, 1), (43, 1),
    (41, 1), (48, 1), (41, 1), (48, 1),
    (41, 1), (48, 1), (41, 1), (48, 1),
    (48, 1), (43, 1), (48, 1), (43, 1),
    (41, 1), (48, 1), (41, 1), (48, 1),
]

# 尾巴留出来让最后一个音自然衰减
TAIL_SECONDS = 2.0


def midi_to_freq(midi):
    return 440.0 * (2.0 ** ((midi - 69) / 12.0))


def decay_tau(freq):
    """音越高衰减越快 —— 音乐盒就是这个脾气。"""
    return 1.9 * (220.0 / freq) ** 0.55


def render_note(start_sample, midi, seconds, amplitude, buffer):
    freq = midi_to_freq(midi)
    tau = decay_tau(freq)
    total = seconds + tau * 2.6
    count = int(total * SAMPLE_RATE)

    t = np.arange(count) / SAMPLE_RATE
    env = np.exp(-t / tau)

    # 4ms 起振，避免爆音
    attack = 0.004
    ramp = np.clip(t / attack, 0.0, 1.0)
    env = env * ramp

    wave_data = np.zeros(count)
    for multiple, weight in HARMONICS:
        # 高次泛音轻微失谐，出来更「金属」
        detune = 1.0 + (multiple - 1.0) * 0.0016
        f = freq * multiple * detune
        if f > SAMPLE_RATE * 0.45:
            continue
        wave_data += weight * np.sin(2.0 * np.pi * f * t)

    wave_data *= amplitude * env

    end = start_sample + count
    if end > len(buffer):
        wave_data = wave_data[:len(buffer) - start_sample]
        end = len(buffer)

    buffer[start_sample:end] += wave_data


def render_line(notes, amplitude, buffer):
    pos = 0.0
    for midi, beats in notes:
        seconds = beats * BEAT_SECONDS
        render_note(int(round(pos * SAMPLE_RATE)), midi, seconds, amplitude, buffer)
        pos += seconds
    return pos


def main():
    melody_seconds = sum(b for _, b in MELODY) * BEAT_SECONDS
    bass_seconds = sum(b for _, b in BASS) * BEAT_SECONDS
    length = max(melody_seconds, bass_seconds) + TAIL_SECONDS

    buffer = np.zeros(int(length * SAMPLE_RATE))

    render_line(BASS, 0.34, buffer)
    render_line(MELODY, 1.0, buffer)

    # 软限幅，防止叠加处削顶
    peak = np.max(np.abs(buffer))
    if peak > 0:
        buffer = np.tanh(buffer / peak * 1.6) / np.tanh(1.6)

    # 整体淡出，免得结尾「咔」一声
    fade = int(0.25 * SAMPLE_RATE)
    buffer[-fade:] *= np.linspace(1.0, 0.0, fade)

    samples = np.clip(buffer, -1.0, 1.0)
    pcm = (samples * 32767.0).astype('<i2')

    out_dir = os.path.join('Assets', 'Audio', 'SFX')
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, 'sfx_power_out_music.wav')

    with wave.open(out_path, 'wb') as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SAMPLE_RATE)
        f.writeframes(pcm.tobytes())

    print('已生成 %s' % out_path)
    print('旋律 %d 拍 / %.2f 秒，低音 %d 拍 / %.2f 秒'
          % (sum(b for _, b in MELODY), melody_seconds,
             sum(b for _, b in BASS), bass_seconds))
    print('文件总长 %.2f 秒（含 %.1f 秒衰减尾巴）' % (length, TAIL_SECONDS))
    print('建议 BlackoutSequence 的音乐阶段用 %.2f 秒' % (length - 0.5))


if __name__ == '__main__':
    main()

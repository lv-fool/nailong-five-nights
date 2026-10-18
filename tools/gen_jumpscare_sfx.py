"""跳杀音效说明（**素材已换成用户提供的 mp3，这个脚本不再生成音效**）。

现在的跳杀音效是用户给的 `跳杀.mp3`（MP3 / 256kbps / 2.04 秒），
放在 `Assets/Resources/Audio/sfx_jumpscare.mp3`。

为什么用 mp3 + Resources，而不是 StreamingAssets + 自己解析：
  · Unity **原生支持 mp3 导入**，`Resources.Load<AudioClip>` 直接拿到 AudioClip
  · 之前试过把 WAV 放 StreamingAssets、手写 WavReader 解析 —— 换成 mp3 后没必要了，
    手写解码器只会随算法变复杂而变脆
  · 代价是 Resources 里的资源会被全量打进包体，但这条只有 64 KB，无所谓

⚠️ **换音效只要覆盖这个文件**（保持文件名 `sfx_jumpscare`，扩展名 mp3/wav 都行），
   生成这类合成音效的脚本保留在这里只作参考 —— 万一以后要回头做原创版本。

原始合成脚本的说明（历史）：
  音色方向是「尖锐、非人声、短促冲击 + 长尾尖叫」，
  和断电音乐一个原则：原版素材有版权，所以自己做。
  但现在是用户提供的素材，不再需要它来生成。

要重新生成一条合成音效时：
    把下面的代码恢复出来跑 `python tools/gen_jumpscare_sfx.py`
"""

# 下面是留档的合成实现（当前不执行）。要用就把 main() 打开并改回输出路径。
#
# import os
# import wave
# import numpy as np
#
# SAMPLE_RATE = 44100
# DURATION = 3.0
# OUT_PATH = os.path.join("Assets", "Resources", "Audio", "sfx_jumpscare.wav")
# ...
#
# 完整实现见 git 历史：该文件在 2026-10-18 之前是 3 秒长尾版本，
# 用 FM 锯齿 + 低频冲击 + 带通噪声三层叠加。


def main():
    print("跳杀音效已改用用户提供的 mp3：")
    print("  Assets/Resources/Audio/sfx_jumpscare.mp3")
    print("")
    print("换素材：直接覆盖同名文件即可，不用跑这个脚本。")
    print("（合成实现留档在本文件末尾注释里）")


if __name__ == "__main__":
    main()

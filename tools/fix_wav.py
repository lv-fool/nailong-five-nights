"""把「头信息坏掉」的 wav 重新封成标准 PCM wav。

火山引擎 TTS 返回的 wav 里 data 块的长度字段是错的（`wave` 会读成几万秒），
Unity 导入时直接报 `Unspecified error during import of AudioClip`。
这里按 RIFF 块表把真正的 data 字节抠出来，再用正确的长度重新写一个头。

    python tools/fix_wav.py <文件> [<文件> ...]
    python tools/fix_wav.py --dir Assets/Resources/Voice
"""

import io
import os
import struct
import sys
import wave


def read_chunks(data):
    """返回 {chunk_id: payload}，按 RIFF 块表走，不信任长度之外的任何字段。"""
    chunks = {}
    if len(data) < 12 or data[0:4] != b"RIFF" or data[8:12] != b"WAVE":
        return None

    pos = 12
    while pos + 8 <= len(data):
        chunk_id = data[pos:pos + 4]
        size = struct.unpack("<I", data[pos + 4:pos + 8])[0]
        body = pos + 8
        end = body + size
        if end > len(data):
            size = len(data) - body
            end = len(data)
        chunks.setdefault(chunk_id, data[body:end])
        pos = end + (size & 1)  # 块按偶数字节对齐
    return chunks


def fix_one(path):
    with open(path, "rb") as f:
        raw = f.read()

    chunks = read_chunks(raw)
    if not chunks or b"data" not in chunks:
        return "跳过（不是 RIFF/WAVE 或没有 data 块）"

    fmt = chunks.get(b"fmt ")
    if fmt is None or len(fmt) < 16:
        return "跳过（没有 fmt 块）"

    # fmt 块布局：0-1 编码 2-3 声道 4-7 采样率 8-11 字节率 12-13 块对齐 14-15 位深
    channels = struct.unpack("<H", fmt[2:4])[0]
    rate = struct.unpack("<I", fmt[4:8])[0]
    bits = struct.unpack("<H", fmt[14:16])[0]
    if channels not in (1, 2) or rate not in (8000, 16000, 22050, 24000, 32000, 44100, 48000):
        return "跳过（fmt 不可信：%d Hz / %d 声道）" % (rate, channels)
    if bits != 16:
        return "跳过（不是 16 bit，实际 %d）" % bits

    pcm = chunks[b"data"]
    if len(pcm) % (channels * 2) != 0:
        pcm = pcm[:len(pcm) - (len(pcm) % (channels * 2))]

    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm)

    out = buf.getvalue()
    if out == raw:
        return "无需修改"

    with open(path, "wb") as f:
        f.write(out)
    return "已重封（%d Hz / %d 声道 / %.2f 秒）" % (rate, channels, len(pcm) / 2.0 / channels / rate)


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    if "--dir" in argv:
        folder = args[0] if args else os.path.join("Assets", "Resources", "Voice")
        args = [os.path.join(folder, n) for n in sorted(os.listdir(folder)) if n.endswith(".wav")]

    if not args:
        print(__doc__)
        return 1

    for path in args:
        print(os.path.basename(path), "->", fix_one(path))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

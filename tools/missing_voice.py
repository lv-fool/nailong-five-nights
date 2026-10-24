"""挑出「还没有真声」的台词，生成只含它们的 json，交给 tts_volc.py 合成。

    python tools/missing_voice.py            # 只打印
    python tools/missing_voice.py --write    # 写 .setup/voice/tts_lines.json
"""

import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANIFEST = os.path.join(ROOT, "tools", "voice_lines.json")
VOICE_DIR = os.path.join(ROOT, "Assets", "Resources", "Voice")
OUT = os.path.join(ROOT, ".setup", "voice", "tts_lines.json")


def main(argv):
    with io.open(MANIFEST, encoding="utf-8") as f:
        lines = json.load(f)

    recorded = set()
    if os.path.isdir(VOICE_DIR):
        for name in os.listdir(VOICE_DIR):
            if name.endswith(".wav"):
                recorded.add(os.path.splitext(name)[0])

    missing = {k: v for k, v in lines.items() if not k.startswith("_") and k not in recorded}
    print("已录真声 %d 句，缺 %d 句：%s" % (len(recorded), len(missing), " ".join(missing)))

    if "--write" in argv:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        with io.open(OUT, "w", encoding="utf-8") as f:
            json.dump(missing, f, ensure_ascii=False, indent=2)
        print("已写", OUT)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

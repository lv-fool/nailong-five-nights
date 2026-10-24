"""用火山引擎「豆包语音」的 V3 单向流式接口批量合成台词。

鉴权走新版控制台 API Key（`speech/new/setting/apikeys`），只要一个 X-Api-Key，
不需要老的 appid / token。

    python tools/tts_volc.py --text "你好" --out out.mp3
    python tools/tts_volc.py --lines tools/voice_lines.json --outdir .setup/voice/tts

`--lines` 支持两种写法：一个字符串数组，或者 {"名字": "台词"} 的字典（推荐，
名字直接当文件名）。
"""

import argparse
import base64
import json
import os
import sys
import time
import uuid

import urllib.request

ENDPOINT = "https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse"
RESOURCE_ID = "seed-tts-2.0"
DEFAULT_SPEAKER = "zh_female_vv_uranus_bigtts"   # 控制台里叫「Vivi 2.0」


def api_key():
    key = os.environ.get("DOUBAO_TTS_API_KEY", "").strip()
    if not key:
        raise SystemExit("需要环境变量 DOUBAO_TTS_API_KEY（控制台 → API Key）")
    return key


def build_body(text, speaker, fmt, sample_rate, speech_rate):
    audio_params = {
        "format": fmt,
        "speech_rate": speech_rate,
        "loudness_rate": 0,
    }
    if fmt in ("mp3", "ogg_opus"):
        audio_params["bit_rate"] = 64000
    additions = {
        "post_process": {"pitch": 0},
        "disable_markdown_filter": True,
        "enable_latex_tn": False,
    }
    return {
        "user": {"uid": "nailong_tts"},
        "req_params": {
            "text": text,
            "speaker": speaker,
            "sample_rate": sample_rate,
            "audio_params": audio_params,
            "additions": json.dumps(additions),
        },
    }


def synthesize(text, out_path, speaker=DEFAULT_SPEAKER, fmt="mp3",
               sample_rate=24000, speech_rate=0, timeout=90):
    body = json.dumps(build_body(text, speaker, fmt, sample_rate, speech_rate)).encode("utf-8")
    req = urllib.request.Request(ENDPOINT, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("X-Api-Key", api_key())
    req.add_header("X-Api-Resource-Id", RESOURCE_ID)
    req.add_header("X-Api-Request-Id", str(uuid.uuid4()))

    chunks = []
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        for raw in resp:
            line = raw.decode("utf-8", "ignore").strip()
            if not line.startswith("data:"):
                continue
            payload = line[5:].strip()
            if payload in ("", "[DONE]"):
                continue
            try:
                obj = json.loads(payload)
            except json.JSONDecodeError:
                continue
            code = obj.get("code", 0)
            if code not in (0, 20000000, 20000001, 20000002):
                raise RuntimeError(f"{code}: {obj.get('message', '')}")
            data = obj.get("data")
            if data:
                chunks.append(base64.b64decode(data))

    if not chunks:
        raise RuntimeError("接口没返回音频数据")

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "wb") as f:
        for c in chunks:
            f.write(c)
    return os.path.getsize(out_path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--text")
    ap.add_argument("--out")
    ap.add_argument("--lines", help="json: [文本] 或 {名字: 文本}")
    ap.add_argument("--outdir", default=".setup/voice/tts")
    ap.add_argument("--speaker", default=DEFAULT_SPEAKER)
    ap.add_argument("--format", default="mp3")
    ap.add_argument("--sample-rate", type=int, default=24000)
    ap.add_argument("--speech-rate", type=int, default=0)
    ap.add_argument("--skip-existing", action="store_true")
    args = ap.parse_args()

    if args.lines:
        with open(args.lines, encoding="utf-8") as f:
            data = json.load(f)
        items = data.items() if isinstance(data, dict) else [(str(i), t) for i, t in enumerate(data)]
        ok = 0
        for name, text in items:
            out = os.path.join(args.outdir, f"{name}.{args.format}")
            if args.skip_existing and os.path.exists(out) and os.path.getsize(out) > 0:
                print(f"跳过 {name}")
                continue
            t0 = time.time()
            try:
                size = synthesize(text, out, args.speaker, args.format,
                                  args.sample_rate, args.speech_rate)
                ok += 1
                print(f"OK  {name:24s} {size/1024:7.1f} KB  {time.time()-t0:.1f}s  {text[:18]}")
            except Exception as e:  # noqa: BLE001
                print(f"ERR {name:24s} {e}")
        print(f"完成 {ok} 条")
        return

    if not args.text:
        raise SystemExit("要么给 --text，要么给 --lines")
    out = args.out or os.path.join(args.outdir, "test." + args.format)
    size = synthesize(args.text, out, args.speaker, args.format,
                      args.sample_rate, args.speech_rate)
    print(f"写出 {out}  {size/1024:.1f} KB")


if __name__ == "__main__":
    main()

"""用火山引擎「声音复刻 2.0」训练一个自定义音色。

流程（官方 v3 接口，见 https://www.volcengine.com/docs/6561/2534906）：
    1. POST /api/v3/tts/voice_clone          —— 上传参考音频、开始训练
    2. POST /api/v3/tts/voice_clone/status   —— 轮询训练状态（2/4 就可用）
    3. 拿 speaker_id 去 tts_volc.py 合成

    python tools/voice_clone.py --audio .setup/voice/ref_doubao.wav --speaker-id S_naibao01

计费提醒：文档写明「首次正式合成」会按音色槽位收费，所以这里只训练、试听，
不替任何人生成成片；真要批量合成由人明确同意后再跑 tts_volc.py。
"""

import argparse
import base64
import json
import os
import sys
import time
import urllib.request
import uuid

BASE = "https://openspeech.bytedance.com/api/v3/tts"
CLONE = BASE + "/voice_clone"
STATUS = BASE + "/voice_clone/status"

KEY_FILE = os.path.join(".setup", "voice", "volc_tts_key.txt")


def api_key(explicit):
    if explicit:
        return explicit
    env = os.environ.get("DOUBAO_TTS_API_KEY")
    if env:
        return env.strip()
    if os.path.exists(KEY_FILE):
        # 这个文件是 KEY=value 的多行清单（记事本存的，常带 BOM）
        with open(KEY_FILE, "r", encoding="utf-8-sig") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                if "=" in line:
                    name, value = line.split("=", 1)
                    if name.strip().upper() in ("DOUBAO_TTS_API_KEY", "API_KEY", "KEY"):
                        return value.strip().strip('"').strip("'")
                elif len(line) >= 32:
                    return line
        raise SystemExit("key 文件里没找到 API Key：" + KEY_FILE)
    raise SystemExit("没有 API Key：传 --key，或设 DOUBAO_TTS_API_KEY，或写进 " + KEY_FILE)


def post(url, key, payload, timeout=120, resource=""):
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("X-Api-Key", key)
    req.add_header("X-Api-Request-Id", str(uuid.uuid4()))
    if resource:
        req.add_header("X-Api-Resource-Id", resource)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as e:
        text = e.read().decode("utf-8", "replace")
        raise SystemExit("HTTP %s: %s" % (e.code, text))


def audio_payload(path):
    with open(path, "rb") as f:
        raw = f.read()
    ext = os.path.splitext(path)[1].lower().lstrip(".")
    if ext not in ("wav", "mp3", "ogg", "m4a", "aac"):
        raise SystemExit("音频格式不支持：" + ext)
    if len(raw) > 10 * 1024 * 1024:
        raise SystemExit("参考音频超过 10 MB")
    return {"data": base64.b64encode(raw).decode("ascii"), "format": ext}


def main(argv):
    p = argparse.ArgumentParser()
    p.add_argument("--audio", required=True, help="参考音频（建议 10~30 秒，wav/mp3/m4a）")
    p.add_argument("--speaker-id", required=True, help="自定义音色 id，例如 S_naibao01")
    p.add_argument("--text", default="", help="参考音频里念的原文（可选，能提高对齐质量）")
    p.add_argument("--demo-text", default="我是奶包，这屋里最靠谱的那个。先看监控吧。",
                   help="试听文本，4~300 字")
    p.add_argument("--denoise", action="store_true", help="对录音做降噪（嘈杂样本才开）")
    p.add_argument("--key", default="", help="API Key，默认读环境变量或 .setup 里的文件")
    p.add_argument("--wait", type=float, default=180.0, help="最多等多少秒训练完成")
    p.add_argument("--poll", type=float, default=5.0, help="轮询间隔秒")
    args = p.parse_args(argv[1:])

    key = api_key(args.key)

    payload = {
        "speaker_id": args.speaker_id,
        "audio": audio_payload(args.audio),
        "language": 0,
        "extra_params": {
            "demo_text": args.demo_text,
            "enable_audio_denoise": bool(args.denoise),
        },
    }
    if args.text:
        payload["text"] = args.text

    print("[1/2] 上传参考音频并开始训练：%s -> %s" % (args.audio, args.speaker_id))
    res = post(CLONE, key, payload)
    print("     ", json.dumps(res, ensure_ascii=False))

    status = res.get("status")
    if status not in (2, 4):
        deadline = time.time() + args.wait
        print("[2/2] 轮询训练状态（最多 %d 秒）" % int(args.wait))
        while time.time() < deadline:
            time.sleep(args.poll)
            st = post(STATUS, key, {"speaker_id": args.speaker_id})
            status = st.get("status")
            print("      status=%s message=%s" % (status, st.get("message")))
            if status in (2, 4):
                res = st
                break
            if status == 3:
                raise SystemExit("训练失败：" + json.dumps(st, ensure_ascii=False))

    if status not in (2, 4):
        raise SystemExit("超时仍未训练好，稍后用 status 接口再查：" + args.speaker_id)

    demo = res.get("demo_audio")
    print("[OK] 音色可用：%s（status=%s）" % (args.speaker_id, status))
    if demo:
        print("     试听（1 小时内有效）：%s" % demo)
    print("     合成：python tools/tts_volc.py --speaker %s --text \"你好\" --out out.mp3" % args.speaker_id)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

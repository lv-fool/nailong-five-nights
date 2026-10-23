# -*- coding: utf-8 -*-
"""下载并解压便携版 Blender 到 <repo>/.tools/blender/。

用法:
    python tools/setup_blender.py            # 自动挑最新 4.5 LTS，失败退 4.2 LTS
    python tools/setup_blender.py 4.2.23     # 指定版本

不写注册表、不要管理员权限，解压即用。
"""
import io
import os
import re
import sys
import time
import shutil
import zipfile
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(REPO, ".tools")
DEST = os.path.join(TOOLS, "blender")
DL = os.path.join(TOOLS, "dl")

MIRRORS = [
    "https://mirrors.aliyun.com/blender/release/",
    "https://download.blender.org/release/",
]

UA = {"User-Agent": "Mozilla/5.0 setup_blender"}


def fetch(url, timeout=30):
    req = urllib.request.Request(url, headers=UA)
    return urllib.request.urlopen(req, timeout=timeout).read()


def list_versions(base):
    html = fetch(base).decode("utf-8", "ignore")
    return sorted(set(re.findall(r'href="(Blender\d+\.\d+)/"', html)))


def list_zips(base, verdir):
    html = fetch(base + verdir + "/").decode("utf-8", "ignore")
    zips = re.findall(r'href="(blender-([\d.]+)-windows-x64\.zip)"', html)
    return sorted(zips, key=lambda t: [int(x) for x in t[1].split(".")])


def pick(version=None):
    """返回 (mirror, verdir, filename)。"""
    for base in MIRRORS:
        try:
            vers = list_versions(base)
        except Exception as e:
            print(f"  [skip] {base} -> {e}")
            continue
        if version:
            verdir = "Blender" + ".".join(version.split(".")[:2])
            cands = [v for v in vers if v.split(".")[0] == verdir.split(".")[0] and v == verdir]
            zips = list_zips(base, verdir)
            for fn, v in zips:
                if v == version:
                    return base, verdir, fn
            raise SystemExit(f"版本 {version} 在 {verdir} 里找不到")
        # 优先 4.5，其次 4.2
        for want in ("Blender4.5", "Blender4.2"):
            if want not in vers:
                continue
            zips = list_zips(base, want)
            if zips:
                return base, want, zips[-1][0]
    raise SystemExit("所有镜像都拿不到版本列表")


def download(url, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".part"
    start = time.time()
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        total = int(r.headers.get("Content-Length") or 0)
        done = 0
        with open(tmp, "wb") as f:
            while True:
                chunk = r.read(1 << 20)
                if not chunk:
                    break
                f.write(chunk)
                done += len(chunk)
                if total:
                    pct = done * 100.0 / total
                    rate = done / max(1e-6, time.time() - start) / 1e6
                    print(f"\r  {pct:5.1f}%  {done/1e6:7.1f}/{total/1e6:.1f} MB  {rate:.1f} MB/s",
                          end="", flush=True)
    print()
    os.replace(tmp, path)
    return path


def extract(zpath):
    if os.path.isdir(DEST):
        print(f"  清掉旧目录 {DEST}")
        shutil.rmtree(DEST, ignore_errors=True)
    os.makedirs(TOOLS, exist_ok=True)
    print("  解压中…")
    with zipfile.ZipFile(zpath) as z:
        names = z.namelist()
        root = names[0].split("/")[0]
        z.extractall(TOOLS)
    inner = os.path.join(TOOLS, root)
    os.replace(inner, DEST)
    return DEST


def main():
    version = sys.argv[1] if len(sys.argv) > 1 else None
    exe = os.path.join(DEST, "blender.exe")
    if os.path.isfile(exe):
        print(f"已存在: {exe}")
        return
    print("查找可用版本…")
    base, verdir, fn = pick(version)
    url = f"{base}{verdir}/{fn}"
    print(f"  选中 {fn}\n  {url}")
    zpath = os.path.join(DL, fn)
    if os.path.isfile(zpath):
        print(f"  已有安装包 {zpath}，跳过下载")
    else:
        download(url, zpath)
    extract(zpath)
    print(f"完成: {exe}")


if __name__ == "__main__":
    main()

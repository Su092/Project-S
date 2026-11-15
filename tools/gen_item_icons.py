"""
Project S — 批量并发生成配件图标。

为什么不用 apimart_image.py 的 `--manifest` 串行模式：
它一次只跑一条（提交 → 等完成 → 下载），25 张要串 25 轮轮询。
这里复用同一个脚本里的**提交/轮询/下载**函数（DNS 反污染、重试、体积校验全都不重写），
只是把条目丢进线程池并发跑。反污染逻辑必须照抄脚本里的实现，不能自己另写一份。

用法：
  python tools\\gen_item_icons.py              # 只补缺失的
  python tools\\gen_item_icons.py --force      # 全部重生成
  python tools\\gen_item_icons.py --workers 6
"""

import argparse
import json
import os
import sys
import threading
import traceback
from concurrent.futures import ThreadPoolExecutor, as_completed

HERE = os.path.dirname(os.path.abspath(__file__))


def _find_hub(start):
    """向上找含 `.cursor/skills` 的目录（= 枢纽根）。

    为什么不用「向上数 N 级」：工程目录结构变过（早期在枢纽内、后来挪到
    `<HUB>/Project/<Game>/unity`），写死层数换机或换布局就会断。
    按特征目录找，只要还在枢纽下就一定能找到。
    """
    d = start
    for _ in range(8):
        if os.path.isdir(os.path.join(d, ".cursor", "skills")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    raise SystemExit("找不到枢纽根（含 .cursor/skills 的目录），起点：" + start)


HUB = _find_hub(HERE)
SKILL = os.path.join(HUB, ".cursor", "skills", "apimart-image", "scripts")
sys.path.insert(0, SKILL)

import apimart_image as api   # noqa: E402  复用提交/轮询/下载与反污染 DNS

# 未处理资产统一放工程根的 _Inbox/（不入库，见 _Inbox/README.md）
PROJ = os.path.dirname(HERE)
RAW = os.path.join(PROJ, "_Inbox", "ui-ai-raw")
_lock = threading.Lock()


def find_manifest():
    candidates = [
        os.path.join(HUB, "tools", "apimart-assets.json"),
        os.path.join(HERE, "..", "tools", "apimart-assets.json"),
    ]
    for c in candidates:
        c = os.path.abspath(c)
        if os.path.isfile(c):
            return c
    raise SystemExit("找不到 apimart-assets.json，候选：\n  " + "\n  ".join(candidates))

def run(key, item):
    out = item["out"]
    # `out` 在清单里是相对路径（形如 `_Inbox/ui-ai-raw/item_std.png`），
    # 而 apimart_image.download() 会把相对路径拼到**枢纽根 REPO** 上，
    # 于是产物落到 <HUB>/_Inbox 而不是本工程的 _Inbox（实测 39 张全跑偏）。
    # 这里先按「工程根」解析成绝对路径再交给 download —— 绝对路径不会被它改写。
    if not os.path.isabs(out):
        out = os.path.abspath(os.path.join(PROJ, out))
    print("[%s] submit -> %s" % (item["id"], out), flush=True)
    tid = api.submit(key, item["prompt"], item.get("size"), item.get("resolution"),
                     item.get("image_urls"), item.get("nsfw_check", False))
    print("[%s] task=%s" % (item["id"], tid), flush=True)
    url = api.poll(key, tid, timeout_s=900, interval=6)
    api.download(url, out)
    return item["id"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--workers", type=int, default=5)
    ap.add_argument("--only", help="逗号分隔的 id（不带 s-ui-item- 前缀也可）")
    args = ap.parse_args()

    key = api.find_key()
    if not key:
        print("缺少密钥 tools/.apimart.key", file=sys.stderr)
        return 2

    with open(find_manifest(), "r", encoding="utf-8") as f:
        m = json.load(f)

    items = [a for a in m["assets"] if a["id"].startswith("s-ui-item-")]
    if args.only:
        want = set()
        for s in args.only.split(","):
            s = s.strip()
            if not s:
                continue
            want.add(s if s.startswith("s-ui-item-") else "s-ui-item-" + s)
        items = [a for a in items if a["id"] in want]

    os.makedirs(RAW, exist_ok=True)
    todo = []
    for a in items:
        # 与 run() 用同一套解析，否则「判断是否存在」看的是枢纽目录，
        # 会永远认为缺图并重复生成（实测过一次：39 张跑了两轮）。
        out = a["out"]
        if not os.path.isabs(out):
            out = os.path.abspath(os.path.join(PROJ, out))
        if os.path.isfile(out) and os.path.getsize(out) > 10000 and not args.force:
            continue
        todo.append(a)

    print("待生成 %d / %d 张" % (len(todo), len(items)))
    if not todo:
        return 0

    ok, bad = [], []
    with ThreadPoolExecutor(max_workers=max(1, args.workers)) as ex:
        futs = {ex.submit(run, key, a): a for a in todo}
        for fut in as_completed(futs):
            a = futs[fut]
            try:
                ok.append(fut.result())
                print("  OK %s" % a["id"], flush=True)
            except Exception as e:
                bad.append((a["id"], "%s: %s" % (type(e).__name__, e)))
                print("  FAIL %s :: %s" % (a["id"], e), flush=True)
                traceback.print_exc()

    print("\nRESULT 成功 %d / 失败 %d" % (len(ok), len(bad)))
    for i, e in bad:
        print("  -", i, e)
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())

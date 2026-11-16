"""审计 UI Image 的「真实像素尺寸 vs 贴图尺寸 vs 9-slice 边距」。

上一版按贴图尺寸判断安全区，结论是错的：贴图 512x96 看着"可拉伸区 464x48 很宽"，
但控件实际只有 360x14 —— 上下边距 24+24=48 早就超过 14 了，Unity 会把整块边距压扁，
圆角塌成死平、描边糊成一条。判断必须用控件真实像素。

本脚本沿层级把 anchor 拉伸解析成绝对像素，再逐个核。

用法：python tools/audit_ui_slices.py
退出码：0 无问题，1 有问题
"""
import os
import re
import sys

PROJ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PF = os.path.join(PROJ, "Assets", "_Game", "Res", "UIPrefabs")
UI = os.path.join(PROJ, "Assets", "_Game", "Res", "Texture", "UI")

# 与 SAssetPostprocessor.BorderFor 保持一致（left, bottom, right, top）
# 这些值按控件真实像素倒推，不是按贴图尺寸 —— 详见 BorderFor 的注释。
BORDERS = {
    "btn_primary": (16, 14, 16, 14),
    "btn_primary_hover": (16, 14, 16, 14),
    "btn_danger": (16, 14, 16, 14),
    "bar_bg": (3, 3, 3, 3),
    "slot_empty": (18, 18, 18, 18),
    "slot_filled": (6, 6, 6, 6),
    "tag_starter": (1, 1, 1, 1),
    "tag_common": (1, 1, 1, 1),
    "tag_rare": (1, 1, 1, 1),
    "tag_epic": (1, 1, 1, 1),
    "panel": (20, 20, 20, 20),
    # ---- 老素材（预处理直接产出最终图）----
    "card_frame": (10, 10, 10, 10),
    "card_inner": (8, 8, 8, 8),
    "banner": (24, 24, 24, 24),
    "bar_frame": (16, 16, 16, 16),
}

# 参考分辨率（art-style.md §4：1600x900，CanvasScaler Match 0.5）
REF_W, REF_H = 1600.0, 900.0


def guid_map():
    """guid → 素材名。递归扫描：素材已按功能分类到 Sprites/<Category>/。"""
    out = {}
    for root, _dirs, files in os.walk(UI):
        for f in files:
            if not f.endswith(".png.meta"):
                continue
            with open(os.path.join(root, f), encoding="utf-8", errors="replace") as fh:
                m = re.search(r"guid: ([0-9a-f]{32})", fh.read(400))
            if m:
                out[m.group(1)] = f[: -len(".png.meta")]
    return out


def png_size(name):
    # 素材已按功能分类到 Sprites/<Category>/，按名递归找，不假设扁平目录。
    for root, _dirs, files in os.walk(UI):
        p = os.path.join(root, name + ".png")
        if os.path.isfile(p):
            with open(p, "rb") as f:
                head = f.read(26)
            return (int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big"))
    return None


def parse(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        s = f.read()
    docs = re.findall(r"--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=\n--- |\Z)", s, re.S)

    names, rt, imgs = {}, {}, {}
    rt2go = {}   # RectTransform fileID -> GameObject fileID
    for cls, fid, body in docs:
        if cls == "1":
            m = re.search(r"m_Name: (.*)", body)
            if m:
                names[fid] = m.group(1).strip()
        elif cls == "224":
            g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            if not g:
                continue
            gofid = g.group(1)
            rt2go[fid] = gofid

            def pair(key):
                mm = re.search(key + r": \{x: ([-\d.eE]+), y: ([-\d.eE]+)\}", body)
                return (float(mm.group(1)), float(mm.group(2))) if mm else None

            fr = re.search(r"m_Father: \{fileID: (-?\d+)\}", body)
            rt[gofid] = {
                "amin": pair("m_AnchorMin"), "amax": pair("m_AnchorMax"),
                "size": pair("m_SizeDelta"), "pos": pair("m_AnchoredPosition"),
                "pivot": pair("m_Pivot"),
                "father_rt": fr.group(1) if fr else None,
            }
        elif cls == "114":
            g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            sp = re.search(r"m_Sprite: \{fileID: -?\d+, guid: ([0-9a-f]{32})", body)
            ty = re.search(r"m_Type: (\d+)", body)
            if g and sp:
                # 同一节点可能挂多张 Image（阴影层、发光层），只取真的带 sprite 的那张。
                # 之前把无 sprite 的那张覆盖上去，导致按钮从审计结果里整体消失。
                imgs.setdefault(g.group(1), {
                    "guid": sp.group(1),
                    "type": int(ty.group(1)) if ty else None,
                })

    # 把 father_rt 翻译成 father_go
    for gofid, d in rt.items():
        frt = d.pop("father_rt", None)
        d["father_go"] = rt2go.get(frt) if frt else None
    return names, rt, imgs


def resolve_sizes(rt):
    """把 anchor 拉伸解析成绝对像素。父级用 sizeDelta+锚点递推，根按参考分辨率。"""
    cache = {}

    def size_of(gofid, depth=0):
        if gofid in cache:
            return cache[gofid]
        d = rt.get(gofid)
        if d is None or depth > 24:
            return (REF_W, REF_H)

        pgo = d.get("father_go")
        if pgo and pgo in rt:
            pw, ph = size_of(pgo, depth + 1)
        else:
            pw, ph = REF_W, REF_H

        amin, amax, size = d["amin"], d["amax"], d["size"]
        if not amin or not amax or not size:
            cache[gofid] = (size[0] if size else pw, size[1] if size else ph)
            return cache[gofid]

        w = (amax[0] - amin[0]) * pw + size[0]
        h = (amax[1] - amin[1]) * ph + size[1]
        cache[gofid] = (w, h)
        return cache[gofid]

    return size_of


def link_fathers(names, rt, imgs):
    return rt


def main():
    gm = guid_map()
    rows = []
    for f in sorted(os.listdir(PF)):
        if not f.endswith(".prefab"):
            continue
        names, rt, imgs = parse(os.path.join(PF, f))
        # 建立 GameObject → 父 GameObject（从 Transform 的 m_Father 拿不到名字，
        # 这里用 RectTransform 的 m_Father 直接指向父 RectTransform 的 GameObject）
        size_of = resolve_sizes(rt)
        for gofid, im in imgs.items():
            if gofid not in rt:
                continue
            sprite = gm.get(im["guid"])
            if not sprite:
                continue
            w, h = size_of(gofid)
            rows.append({
                "prefab": f[:-7], "node": names.get(gofid, "?"),
                "sprite": sprite, "w": w, "h": h, "type": im["type"],
            })

    def tyname(t):
        return {0: "simple", 1: "sliced", 2: "tiled", 3: "filled"}.get(t, str(t))

    print("%-13s %-20s %-18s %-14s %-12s %s" %
          ("PREFAB", "NODE", "SPRITE", "REAL PX", "TEXTURE", "TYPE"))
    print("-" * 104)

    problems = []
    seen = set()
    for r in rows:
        key = (r["prefab"], r["node"], r["sprite"], round(r["w"]), round(r["h"]))
        if key in seen:
            continue
        seen.add(key)
        sz = png_size(r["sprite"])
        tex = "%dx%d" % sz if sz else "?"
        rect = "%gx%g" % (round(r["w"]), round(r["h"]))
        print("%-13s %-20s %-18s %-14s %-12s %s" %
              (r["prefab"], r["node"][:20], r["sprite"], rect, tex, tyname(r["type"])))

        b = BORDERS.get(r["sprite"])
        if not (b and r["type"] == 1):
            continue
        l, bo, rr, t = b
        need_h, need_v = l + rr, bo + t
        w, h = r["w"], r["h"]
        flag = None
        if need_h >= w or need_v >= h:
            flag = "边距(%d,%d) 吃光控件 %gx%g -> 圆角/描边被压扁" % (need_h, need_v, w, h)
        elif (w - need_h) < 8 or (h - need_v) < 8:
            flag = "可拉伸区仅 %gx%g，圆角几乎相连" % (w - need_h, h - need_v)
        if flag:
            problems.append((r["prefab"], r["node"], r["sprite"], rect, str(b), flag))

        # 长宽比严重不符：贴图是横向药丸却当方形/竖向用
        if sz and min(sz) > 0 and min(w, h) > 0:
            ta = sz[0] / sz[1]
            ca = w / h
            if (ta > 2.5 and ca < 1.6) or (ta < 0.4 and ca > 0.6):
                problems.append((r["prefab"], r["node"], r["sprite"], rect,
                                 "%dx%d" % sz,
                                 "长宽比不符：贴图 %.1f:1，控件 %.1f:1（用错图）" % (ta, ca)))

    print("\n===== 需要修（按控件真实像素判定）=====")
    if not problems:
        print("无")
    for p in problems:
        print("  %-13s %-20s %-18s rect=%-12s %s" % (p[0], p[1][:20], p[2], p[3], p[5]))

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

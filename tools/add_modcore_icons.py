"""
向 tools/apimart-assets.json 追加大批配件图标条目（MODCORE 重写后的新 id）。

为什么用脚本而不是手写 39 段 JSON：
每条提示词的「前缀 + 主体描述 + 后缀约束」是同一套模板，
手抄一遍就是 39 个复制粘贴机会，抄错一个 id 就要等 39 张图跑完才发现。

用法：
  python tools/add_modcore_icons.py
"""

import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
# 清单是**工作区级**的（tools/apimart-assets.json 在枢纽 <HUB>/tools 下），不在游戏工程的 tools 里。
# 这个脚本自身放在工程 tools/ 下，所以要用两级向上找。
MANIFEST = os.path.abspath(os.path.join(
    HERE, "..", "..", "..", "..", "tools", "apimart-assets.json"))

SUFFIX = (
    " Single centered abstract emblem symbol, thick bold rounded line art, "
    "uniform stroke weight, monochrome bone-white glyph on a pure solid black background, "
    "high contrast, minimalist industrial sci-fi iconography, clean silhouette readable at small size, "
    "generous empty margin around the shape, perfectly symmetric where applicable, "
    "no circle, no ring, no badge plate, no frame, no border, no text, no letters, no numbers, "
    "no watermark, no shadow, no gradient, no background texture, flat 2D vector-like, "
    "the entire background is pure black."
)

# (id, 中文名, 主体描述) —— 主体描述要让模型画出「一眼能认出功能」的形状
ITEMS = [
    # ---- 子弹配件 17 ----
    ("std", "标准弹", "a single standard rifle cartridge, straight side view, pointed tip up, simple raised rim at the base"),
    ("light", "轻型弹", "a slim short lightweight bullet, tapered nose, two small speed streaks trailing behind it"),
    ("pellet", "霰弹", "a tight cluster of seven small round pellets, three in the middle four around"),
    ("ap", "穿甲弹", "a long needle-nosed armour piercing dart with an arrowhead tip and three stabiliser fins"),
    ("laser", "激光束", "a concentrated laser beam drawn as a straight shaft with a bright core and three short refraction ticks"),
    ("grenade", "榴弹", "a plump teardrop shaped grenade with a rounded body, a tail fin assembly and a short fuse cap"),
    ("bouncer", "弹跳弹", "a round ball with a zigzag ricochet path arrow bouncing off a right angle corner"),
    ("missile", "追踪飞弹", "a small guided missile with four tail fins, three flat arrow fins"),
    ("mine", "感应地雷", "a flat disc landmine seen from a low angle with four short antenna spikes"),
    ("shock", "电网陷阱", "a flat disc emitter with four lightning bolts arcing outward from its rim"),
    ("chain", "连锁闪电", "a jagged lightning bolt splitting into three branches between two opposing sharp tips"),
    ("saw", "回旋锯", "a circular saw blade with twelve teeth and a circular return arrow sweeping around it"),
    ("flame", "火焰喷射", "a three tongued flame jet spreading forward from a short wide nozzle"),
    ("hole", "奇点", "a small black disc with concentric gravity rings collapsing inward and four motion arcs"),
    ("trigger", "触发弹", "a bullet with a small burst starburst at its nose and a curved recoil arc behind it"),
    ("timer", "定时触发弹", "a bullet with a small clock face mounted at its base and a single short tick mark"),
    ("expire", "消散触发弹", "a bullet dissolving into particles, the nose breaking up into four scattered fragments"),

    # ---- 衍生配件 22 ----
    ("dmg", "伤害增幅", "a bold upward pointing thick arrow with a sharp chevron head"),
    ("heavy", "重弹头", "a heavy fat bullet with a thick blunt nose and a dense inner core ring"),
    ("speed", "加速", "three horizontal speed lines with two forward chevrons stacked"),
    ("pierce", "穿透", "a spear point thrusting straight through three stacked flat plates"),
    ("explode", "爆裂", "an eight point starburst explosion with a small core circle"),
    ("homing", "追踪", "a curved arrow bending towards a small target diamond"),
    ("bounce", "弹跳", "a ball with a zigzag path arrow bouncing off two parallel flat surfaces"),
    ("burn", "燃烧", "a single tall flame with a hollow teardrop core"),
    ("frost", "冰霜", "a six pointed snowflake with hollow triangular arms"),
    ("giant", "巨大化", "a small square beside a much larger square, with four outward arrows between them"),
    ("crit", "暴击", "a sharp four point spark star with a small solid diamond at its centre"),
    ("scatter", "三重弹幕", "three diverging arrows spreading out from a single point"),
    ("thrift", "节能回路", "a downward pointing arrow beside a small battery cell with a minus notch"),

    # ---- 枪械衍生件 9 ----
    ("g_mag", "扩容弹匣", "a tall rectangular magazine with a double outward arrow on both sides"),
    ("g_reload", "快速装填", "a circular reload arrow curling around a single cartridge"),
    ("g_rapid", "速射机构", "a gear with a lightning bolt cut through its centre"),
    ("g_stable", "稳定器", "three parallel vertical fins converging into a narrow base plus two inward pointing arrows"),
    ("g_recycle", "弹药回收", "a cartridge with a circular return arrow wrapping back into its own casing"),
    ("g_parallel", "并联扳机", "two parallel trigger mechanisms side by side joined by a horizontal bar"),
    ("g_leech", "汲能模块", "a hexagon module with two arrows pointing inward toward a small core dot"),
    ("g_double", "双重施放", "two identical arrows side by side pointing up, with a double tick mark above them"),
    ("g_triple", "三重施放", "three identical arrows side by side pointing up, with a triple tick mark above them"),
]


def main():
    with open(MANIFEST, "r", encoding="utf-8") as f:
        data = json.load(f)

    # 清单顶层结构兼容：可能是 {"assets": [...]} 或直接 [...]
    if isinstance(data, dict):
        key = "assets" if "assets" in data else list(data.keys())[0]
        arr = data[key]
    else:
        key = None
        arr = data

    existing = {a.get("id") for a in arr if isinstance(a, dict)}

    added = 0
    for item_id, cn_name, shape in ITEMS:
        eid = "s-ui-item-" + item_id
        if eid in existing:
            continue
        arr.append({
            "id": eid,
            "out": "_Inbox/ui-ai-raw/item_%s.png" % item_id,
            "size": "1:1",
            "resolution": "1k",
            "no_global_style": True,
            "prompt": (
                "Game UI inventory icon for a weapon attachment called '%s'. %s.%s"
                % (cn_name, shape, SUFFIX)
            ),
        })
        added += 1

    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print("added %d entries, total now %d" % (added, len(arr)))


if __name__ == "__main__":
    main()

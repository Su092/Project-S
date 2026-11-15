"""
Project S — 为每个配件登记 APIMart 图标条目（一次性脚本，条目并入全局清单）。

为什么写进 `tools/apimart-assets.json` 而不是散落成命令行：
那是本工作区唯一的素材清单，生成记录、提示词、落点都在一处，
后续重新生成某一件只要 `--only s-ui-item-b_slug`。

图标口径（与 preprocess_ui_assets.py 的 glyph 模式绑定，必须遵守）：
  · 纯黑背景 + 浅色线稿 —— 预处理用「亮度当 alpha」抠图，灰底会抠出不干净的边。
  · 单一居中的图形，不画圆框、不画底板、不写字 —— 圆形底板与描边由合成器用几何画，
    这样 25 张图标的圆角、描边宽度、留白才能完全一致（AI 画的圈每次都不一样）。
  · 粗线、少细节 —— 游戏内只显示 28~112px，细线在这个尺寸下会糊成一团。

用法：python tools\\gen_item_icon_prompts.py
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def find_manifest():
    """找到本工作区唯一的素材清单 tools/apimart-assets.json。

    注意：本工程（E:\\S）**不在**工作区（E:\\Unity Development）目录里面，
    所以不能靠相对层级推算，必须沿工作区根目录找。
    """
    candidates = []
    d = HERE
    for _ in range(6):
        candidates.append(os.path.join(d, "tools", "apimart-assets.json"))
        d = os.path.dirname(d)
    # 枢纽根（<HUB>/tools）—— 相对推算到不了，因为工程挂在 <HUB>/Project/<Game>/unity 下，
    # 而清单在 <HUB>/tools。这里从工程根再向上三级拿到同样位置，不写死盘符。
    candidates.append(os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", "tools",
                                                   "apimart-assets.json")))

    for c in candidates:
        if os.path.isfile(c):
            return c
    raise SystemExit("找不到 tools/apimart-assets.json，候选：\n  " + "\n  ".join(candidates))


MANIFEST = find_manifest()
# 未处理资产统一放工程根的 _Inbox/（不入库，见 _Inbox/README.md）
PROJ = os.path.dirname(HERE)
RAW = os.path.join(PROJ, "_Inbox", "ui-ai-raw")

# 共用画风后缀：所有配件图标必须是同一套笔触，否则并排显示像两个画师画的
STYLE = (
    "Single centered abstract emblem symbol, thick bold rounded line art, uniform stroke weight, "
    "monochrome bone-white glyph on a pure solid black background, high contrast, minimalist industrial "
    "sci-fi iconography, clean silhouette readable at small size, generous empty margin around the shape, "
    "perfectly symmetric where applicable, no circle, no ring, no badge plate, no frame, no border, "
    "no text, no letters, no numbers, no watermark, no shadow, no gradient, no background texture, "
    "flat 2D vector-like, the entire background is pure black."
)

# (id, 中文名, 英文主体描述)
ITEMS = [
    # ---- 子弹配件（10）----
    ("b_slug", "制式弹", "a single standard rifle cartridge, straight side view, pointed tip up, "
                         "simple raised rim at the base"),
    ("b_heavy", "重芯弹", "a heavy fat bullet with a thick blunt nose and a dense inner core ring, "
                          "side view, clearly bulkier than a normal round"),
    ("b_pellet", "霰粒", "a tight cluster of seven small round pellets, three in the middle four around"),
    ("b_pierce", "穿甲钉", "a long narrow armour-piercing dart spike with sharp conical tip and tiny tail fins, "
                           "pointing straight up"),
    ("b_ricochet", "跳弹", "a bullet deflecting off a hard surface, a curved bounce arrow arcing away from the "
                           "impact point and a small spark at contact"),
    ("b_plasma", "电浆团", "a compact sphere of electric plasma with three jagged lightning arcs discharging "
                           "outward and a bright core"),
    ("b_incend", "燃素弹", "a bullet wrapped in a stylised flame with three curling tongues of fire "
                           "trailing behind it"),
    ("b_cryo", "冻结核", "a hexagonal ice crystal bullet with sharp faceted edges, three small frost shards "
                         "breaking off, cold sharp geometry"),
    ("b_null", "空蚀弹", "a hollow bullet with an empty void core, its outline partially eaten away by "
                         "erosion, a ragged hole punched through the middle"),
    ("b_lance", "光矛", "an elongated energy spearhead made of a sharp beam of light, tapered to a needle point, "
                        "two short energy fins at the base"),

    # ---- 数值向衍生件（8）----
    ("m_dmg_up", "校准套件", "a precision calibration module, a gear meshing with a small tuning wrench, "
                              "a short upward arrow cut into the gear face"),
    ("m_dmg_flat", "增压衬片", "a rectangular pressure shim plate seen face on with a bold upward arrow embossed "
                               "on it and two bolt holes at the corners"),
    ("m_crit_up", "激光准星", "a sniper scope reticle with a tiny laser dot at its exact centre and four short "
                              "crosshair ticks, no circle housing"),
    ("m_critdmg", "中空改造", "a hollow-point bullet cut in cross section, a deep cavity at the nose and a "
                              "starburst of petals opening outward"),
    ("m_spread_down", "稳定器", "a stabiliser fin assembly, three parallel fins converging into a narrow point, "
                                "two tight inward-pointing arrows compressing them together"),
    ("m_life_up", "延时引信", "a mechanical fuse timer, a clock dial with a single bold hand and a small ignition "
                              "cap on top"),
    ("m_ammo_down", "弹链优化", "two interlocking ammunition belt links forming a chain segment, an arrow "
                                "threading through them pointing forward"),
    ("m_speed_up", "加速器", "three stacked forward chevrons accelerating into a bullet-shaped tip with two speed "
                             "lines trailing behind"),

    # ---- 行为向衍生件（4）----
    ("m_split", "量子分裂", "one solid orb splitting into three smaller orbs, thin divergence lines fanning out "
                            "from a single origin point"),
    ("m_pierce", "穿透射击", "a sharp arrow passing clean through a thick plate, the exit hole visible on the far "
                             "side, small fragments shedding at the entry"),
    ("m_homing", "制导", "a guidance antenna with a parabolic dish and two concentric radar arcs locking onto a "
                         "small target dot"),
    ("m_blast", "爆裂装药", "a radial explosion burst with eight uneven jagged spikes and a compact dense core, "
                            "three small debris chips flying outward"),

    # ---- 枪械向衍生件（3）----
    ("m_mag", "弹匣扩容", "an extended straight box magazine with visible rounds stacked inside, a bold plus sign "
                          "embossed near the base"),
    ("m_rate", "高速击发", "a lightning bolt passing through a small trigger lever, two short motion lines "
                           "indicating speed at the top"),
    ("m_recoil", "后坐抑制", "a coiled recoil damper spring mounted on a short guide rod with a heavy end cap, "
                             "two small arrows pulling inward on the spring"),
]


def main():
    with open(MANIFEST, "r", encoding="utf-8") as f:
        m = json.load(f)

    existing = {a["id"] for a in m["assets"]}
    added = 0
    for item_id, cn, subject in ITEMS:
        aid = "s-ui-item-" + item_id
        if aid in existing:
            continue
        m["assets"].append({
            "id": aid,
            "out": os.path.join(RAW, "item_" + item_id + ".png").replace("\\", "/"),
            "size": "1:1",
            "resolution": "1k",          # 方形图标（成品）：按约定走 1k
            "no_global_style": True,     # 本批共用 STYLE，不叠加清单的全局 style
            "prompt": ("Game UI inventory icon for a weapon attachment called '%s'. %s. %s"
                       % (cn, subject, STYLE)),
        })
        added += 1

    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(m, f, ensure_ascii=False, indent=2)

    print("已登记 %d 条（清单共 %d 条）" % (added, len(m["assets"])))


if __name__ == "__main__":
    main()

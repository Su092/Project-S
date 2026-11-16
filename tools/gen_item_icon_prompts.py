"""
Project S — 为每个配件登记 APIMart 图标条目（一次性脚本，条目并入全局清单）。

为什么写进 `tools/apimart-assets.json` 而不是散落成命令行：
那是本工作区唯一的素材清单，生成记录、提示词、落点都在一处，
后续重新生成某一件只要 `--only s-ui-item-std`。

图标口径（与 preprocess_ui_assets.py 的 glyph 模式绑定，必须遵守）：
  · 纯黑背景 + 浅色线稿 —— 预处理用「亮度当 alpha」抠图，灰底会抠出不干净的边。
  · 单一居中的图形，不画圆框、不画底板、不写字 —— 圆形底板与描边由合成器用几何画，
    这样 39 张图标的圆角、描边宽度、留白才能完全一致（AI 画的圈每次都不一样）。
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
# (id, 中文名, 英文主体描述)
# 与 sdata.json 的 bullets / modifiers / gunModifiers 一一对应（17 + 13 + 9 = 39）。
# 旧的 25 件（b_slug / m_split 等）在 MODCORE 对齐时作废，不要再加回来。
ITEMS = [
    # ---- 子弹配件（17）----
    ('std', '标准弹', 'a single standard rifle cartridge, straight side view, pointed tip up, simple raised rim at the base'),
    ('light', '轻型弹', 'a slim lightweight bullet with a short tapered nose and two thin speed lines trailing behind it'),
    ('pellet', '霰弹', 'a tight cluster of six small round pellets, three in the middle three around'),
    ('ap', '穿甲弹', 'a long narrow armour-piercing dart spike with sharp conical tip and tiny tail fins, pointing straight up'),
    ('laser', '激光束', 'an elongated energy spearhead made of a sharp beam of light, tapered to a needle point, two short energy fins at the base'),
    ('grenade', '榴弹', 'a short fat oval grenade shell with a blunt rounded nose and a ribbed waist band'),
    ('bouncer', '弹跳弹', 'a bullet deflecting off a hard surface, a curved bounce arrow arcing away from the impact point and a small spark at contact'),
    ('missile', '追踪飞弹', 'a small guided missile seen from the side with two tail fins and a curved homing trail arcing toward a target dot'),
    ('mine', '感应地雷', 'a squat disc shaped proximity mine with three stubby sensor prongs on top and a thin antenna'),
    ('shock', '电网陷阱', 'an expanding circular electric field with four jagged lightning arcs branching outward from a bright core'),
    ('chain', '连锁闪电', 'a jagged lightning bolt forking into three branches, each ending in a small node'),
    ('saw', '回旋锯', 'a circular saw blade with coarse angular teeth and a curved return arrow wrapping around it'),
    ('flame', '火焰喷射', 'three curling tongues of flame bursting forward from a short nozzle, the tongues layered and tapering'),
    ('hole', '奇点', 'a dark gravity well, concentric rings spiralling inward to an empty void centre'),
    ('trigger', '触发弹', 'a bullet with a small radial burst at its nose, four short impact spikes fanning out from the tip'),
    ('timer', '定时触发弹', 'a mechanical fuse timer, a clock dial with a single bold hand and a small ignition cap on top'),
    ('expire', '消散触发弹', 'a bullet dissolving into small square fragments, the outline breaking apart and scattering outward'),

    # ---- 作用于子弹的衍生件（13）----
    ('dmg', '伤害增幅', 'a precision calibration module, a gear meshing with a small tuning wrench, a short upward arrow cut into the gear face'),
    ('heavy', '重弹头', 'a heavy fat bullet with a thick blunt nose and a dense inner core ring, side view, clearly bulkier than a normal round'),
    ('speed', '加速', 'three stacked forward chevrons accelerating into a bullet-shaped tip with two speed lines trailing behind'),
    ('pierce', '穿透', 'a sharp arrow passing clean through a thick plate, the exit hole visible on the far side, small fragments shedding at the entry'),
    ('explode', '爆裂', 'a radial explosion burst with eight uneven jagged spikes and a compact dense core, three small debris chips flying outward'),
    ('homing', '追踪', 'a guidance antenna with a parabolic dish and two concentric radar arcs locking onto a small target dot'),
    ('bounce', '弹跳', 'a ball striking a hard angled wall and rebounding, a long curved arrow tracing the incoming and outgoing path'),
    ('burn', '燃烧', 'a single flame with a bright inner core and three outer tongues curling upward'),
    ('frost', '冰霜', 'a hexagonal ice crystal with sharp faceted edges and three small frost shards breaking off, cold sharp geometry'),
    ('giant', '巨大化', 'a small bullet outline beside a much larger expanded silhouette of the same shape, four short outward arrows at the corners'),
    ('crit', '暴击', 'a sniper scope reticle with a tiny laser dot at its exact centre and four short crosshair ticks, no circle housing'),
    ('scatter', '三重弹幕', 'one bullet dividing into three diverging bullets with a small spread fan between them'),
    ('thrift', '节能回路', 'a circular energy loop with a short break in the ring and a small downward arrow at the gap'),

    # ---- 枪械向衍生件（9）----
    ('g_mag', '扩容弹匣', 'an extended straight box magazine with visible rounds stacked inside, a bold plus sign embossed near the base'),
    ('g_reload', '快速装填', 'a box magazine dropping out and a fresh one sliding up, a curved reloading arrow between them'),
    ('g_rapid', '速射机构', 'a lightning bolt passing through a small trigger lever, two short motion lines indicating speed at the top'),
    ('g_stable', '稳定器', 'a stabiliser fin assembly, three parallel fins converging into a narrow point, two tight inward-pointing arrows compressing them together'),
    ('g_recycle', '弹药回收', 'a circular return arrow wrapping around a single bullet, the arrow head closing back onto the casing'),
    ('g_parallel', '并联扳机', 'two parallel bullets side by side linked by a short bracket, both sharing one common trigger bar behind them'),
    ('g_leech', '汲能模块', 'a small core cell absorbing energy, three curved arrows sweeping inward toward a glowing centre'),
    ('g_double', '双重施放', 'two identical bullets stacked in parallel with a bold number two implied by two short stacked bars beside them'),
    ('g_triple', '三重施放', 'three identical bullets fanned out in a tight triangle with three short stacked bars beside them'),
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

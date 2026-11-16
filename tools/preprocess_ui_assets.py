"""
Project S — UI 素材前期处理（APIMart 原图 → 可进引擎的源素材）

流程分三段，每段职责单一，不许跳步：

    APIMart 出图            本脚本                 合成器
  _Inbox/ui-ai-raw/  ──►  _Inbox/ui-ai-src/  ──►  Assets/Resources/UI/
   （4~8MB、RGB、           （抠图完成、尺寸             （精确几何 + 9-slice
     黑底、无 alpha）          规范、质量已过门）              + 圆角裁剪）
                                                                │
                                                          最后才接入引擎

为什么必须单独一段（踩过的坑）：
  1. 图标是「黑底 + 白 glyph」，直接进引擎会变成黑方块。抠图要在**进引擎前**做，
     不能指望 Unity 的 alphaIsTransparency 自动救 —— 它不会从亮度反推 alpha。
  2. 黑底抠图会留黑边：半透明像素的 RGB 仍是近黑，叠在深色 HUD 上就是一圈脏边。
     必须做颜色反解（unpremultiply），把 RGB 拉回真正的白色。
  3. 2048² 的 PNG 有 8MB，而 UI 里最大只采样到 512px。不先降尺寸，
     仓库会被几十张无用大图占满，导入也慢。
  4. 质量门要在这里拦：覆盖率、留白比例、黑底残留、圆角外是否透明。
     进引擎后再发现，只能靠肉眼看截图，回归成本高一个数量级。

用法（工程根目录）：
  python tools\\preprocess_ui_assets.py              # 处理全部
  python tools\\preprocess_ui_assets.py --only icon  # 只处理某一类
  python tools\\preprocess_ui_assets.py --list       # 看有哪些类
  python tools\\preprocess_ui_assets.py --force      # 源未变也重做

退出码：0 全部通过；1 有素材被质量门拦下（此时**不要**继续跑合成器）
"""

import argparse
import hashlib
import json
import os
import shutil
import sys

from PIL import Image, ImageDraw, ImageFilter

try:
    import numpy as np
except ImportError:      # pragma: no cover
    np = None

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)
# 未处理资产统一放工程根的 _Inbox/（不入库，见 _Inbox/README.md）
INBOX = os.path.join(PROJ, "_Inbox")
RAW = os.path.join(INBOX, "ui-ai-raw")      # APIMart 原始下载（未处理）
SRC = os.path.join(INBOX, "ui-ai-src")      # 处理完、可合成的源素材
REPORT = os.path.join(HERE, "ui-preprocess.json")
# 早期直接落进工程的老素材（panel / banner / 图标等）。它们没有合成步骤 ——
# 预处理就是它们的最后一道工序，所以产物直接写进 Assets。
DEST_UI = os.path.join(os.path.dirname(HERE), "Assets", "Resources", "UI")

# ---------------- 素材分类（与 Assets/_Game/View/UiAssets.cs 的 Catalog 一致）----------------
# final 素材写进 Assets 时必须落到对应分类子目录，不能再写到 UI/ 根下 ——
# 根目录是「未分类」的暂存，一旦素材分类完成，根下只允许留材质等非 sprite 资产。
SPRITES_ROOT = os.path.join(DEST_UI, "Sprites")
CAT_DIRS = {
    "Common": os.path.join(SPRITES_ROOT, "Common"),
    "Frame":  os.path.join(SPRITES_ROOT, "Frame"),
    "Widget": os.path.join(SPRITES_ROOT, "Widget"),
    "Tag":    os.path.join(SPRITES_ROOT, "Tag"),
    "Icon":   os.path.join(SPRITES_ROOT, "Icon"),
    "Items":  os.path.join(SPRITES_ROOT, "Icon", "Items"),
}

CATEGORY = {}
for _n in ("panel", "banner", "vignette", "vignette_damage", "divider"):
    CATEGORY[_n] = "Common"
for _n in ("card_frame", "card_inner", "bar_frame"):
    CATEGORY[_n] = "Frame"
for _n in ("btn_primary", "btn_primary_hover", "btn_danger", "slot_empty", "slot_filled",
           "bar_bg", "bar_fill_hp", "bar_fill_xp", "bar_fill_boss", "bar_fill_threat",
           "crosshair", "hit_marker", "keycap"):
    CATEGORY[_n] = "Widget"
for _n in ("tag_starter", "tag_common", "tag_rare", "tag_epic"):
    CATEGORY[_n] = "Tag"
for _n in ("icon_bullet", "icon_whole", "icon_after", "icon_gun",
           "icon_frame", "icon_lock", "icon_check", "icon_arrow"):
    CATEGORY[_n] = "Icon"


def cat_dir(name):
    """素材名 → 工程内目标目录。未登记就抛错，防止素材又落回扁平根目录。"""
    # 配件图标（item_*）统一归 Items，按前缀判断，与 C# 的动态取图对应
    if name.startswith("item_"):
        return CAT_DIRS["Items"]
    d = CATEGORY.get(name)
    if d is None:
        raise KeyError("素材未登记分类: %s（见 UiAssets.Catalog）" % name)
    return CAT_DIRS[d]


# ---------------------------------------------------------------- 规格表
# mode:
#   "material" —— 不透明材质，只做尺寸规范化（可选去色/对比）
#   "glyph"    —— 黑底白字，做抠图 + 颜色反解 + 包围盒归一
#   "plate"    —— 暗色底板/边框：AI 把几何和**纯黑背景**一起烤进了 RGB，无 alpha。
#                 直接进引擎时 9-slice 取到的角切片全是黑边，画面上就是四角一团黑。
#                 这里按亮度找剪影 → 填内部孔洞 → 用剪影当 alpha → 反预乘去黑边。
#   "icon"     —— 已带 alpha 的彩色图标：只做包围盒归一 + 缩尺寸，不动颜色。
#   "mask"     —— 亮度即遮罩（晕影）：RGB 归白，alpha 取亮度。
#
# size:    处理后的边长；int 表示正方形，tuple 表示 (宽, 高)
# trim:    glyph / icon 专用，按包围盒裁掉多余留白后重新居中
# final:   该素材没有合成阶段，预处理产物就是最终图（直接写进 Assets/Resources/UI）
SPECS = {
    "plate_dark":  dict(mode="material", size=1024, sat=0.0),
    "plate_light": dict(mode="material", size=1024, sat=0.0),
    "energy":      dict(mode="material", size=512, sat=0.0),
    # 卡片内衬：极暗、低对比，专门给卡片内部当文字底。
    # 它不像 plate_dark 那样有可见的拉丝颗粒 —— 那是刻意的：文字压在上面时，
    # 任何"看得见的纹理"都会让 13~15px 的小字读起来发毛。
    "card_inner":  dict(mode="material", size=512, sat=0.0, min_sigma=1.5),

    # 包围盒长边占画布的比例。三个图标归一化后都落在 0.80，
    # 这是 trim + margin_ratio 的直接结果，不是巧合；用同一区间校验，
    # 不要给单个图标放宽 —— 那会让一组图标的视觉大小失去一致性。
    "icon_lock":   dict(mode="glyph", size=256, trim=True, max_dev=(0.70, 0.90)),
    "icon_check":  dict(mode="glyph", size=256, trim=True, max_dev=(0.70, 0.90)),
    "icon_arrow":  dict(mode="glyph", size=256, trim=True, max_dev=(0.70, 0.90)),

    # ---- 老素材（原先绕过流水线，直接由 APIMart 原图落进工程）----    # 目标尺寸按「最大控件」倒推，不是按原图。原图 2048² 存着，实际最大只采样到
    # 720px（panel）—— 多出来的像素只占仓库和显存，画面上一个像素都看不到。
    #
    # panel：最大控件 UnlockPanel 720×720。512 已足够（9-slice 中间区是拉伸的，
    #   纹理细节不靠尺寸）；最小控件 VolumePanel 380×228。内容比例 1.03，接近正方。
    "panel":       dict(mode="plate", edge=512, final=True),
    # card_frame：最大控件 OfferCard 300×440，最小 ItemCard 150×84。
    #   ⚠ 内容比例 0.773（竖构图），**不能**硬缩成正方形 —— 那会把圆角横向拉宽。
    "card_frame":  dict(mode="plate", edge=256, final=True),
    # banner：HUD BannerPlate 720×88，内容比例 6.1:1（极扁横条）。
    "banner":      dict(mode="plate", edge=512, final=True),
    # bar_frame：暂无预制体引用（只被自检点名），内容比例 10.6:1。
    "bar_frame":   dict(mode="plate", edge=512, final=True),
    # crosshair 已改由 gen_ui_assets_ai.py 用精确几何绘制（尺寸 16~130px，
    # AI 纹理细节看不见，而对称与中心间隙必须像素精确）。见该脚本 make_crosshair。
    # keycap：无预制体引用，按 24~64 的按键帽规格归一。
    #   原图是带投影的立体键帽，alpha>8 的包围盒已含投影，按 64 归一即可。
    "keycap":      dict(mode="icon", size=(64, 64), trim=True, final=True, pad_ratio=0.07),
    # 4 个作用域图标：控件 56×36（preserveAspect），按 128 归一够清晰。
    # 它们本身是彩色图（菜单里直接 Simple + tint），所以不能走 glyph 的「RGB 全白」，
    # 只能归一包围盒 —— 否则枪/子弹的形状会被染成同一片纯色。
    "icon_bullet": dict(mode="icon", size=(128, 128), trim=True, final=True, pad_ratio=0.04),
    "icon_whole":  dict(mode="icon", size=(128, 128), trim=True, final=True, pad_ratio=0.04),
    "icon_after":  dict(mode="icon", size=(128, 128), trim=True, final=True, pad_ratio=0.04),
    "icon_gun":    dict(mode="icon", size=(128, 128), trim=True, final=True, pad_ratio=0.04),
    # vignette：全屏 1600×900 的外缘光晕（不是径向暗角：中心是纯黑，亮度集中在四条边带）。
    # 原图为「纯黑底 + 冷灰光环」且无 alpha，当 Simple 铺满全屏会把整块画面压黑；
    # 必须转成 alpha 遮罩（RGB 归白），颜色与强度交给 HudView 逐帧控制。
    "vignette":    dict(mode="mask", size=(1024, 576), final=True, max_alpha=255),
}

# ---- 配件图标（25 张，与 sdata.json 的 10 子弹 + 15 衍生一一对应）----
# 游戏内显示尺寸：卡片角标 28px、链区卡片 44px、悬浮详情 64px、HUD 链条 40px。
# 归一化到 128：留 3 倍采样余量，够 64px 用，又不会让仓库里躺一堆无用大图。
#
# 为什么走 glyph 而不是 icon：AI 出的是「纯黑底 + 浅色线稿」，没有 alpha。
# glyph 模式会「亮度当 alpha」抠图 + 把 RGB 反解成纯白，之后由代码染色 ——
# 25 张图因此可以共用一套配色规则（稀有度色），不用为每张单独调色。
#
# 圆形底板、外环、稀有度色环全部由 gen_ui_assets_ai.py 用几何画：
# AI 画的圆每次半径都不一样，一圈里 25 个图标放在一起立刻看得出歪。
ITEM_IDS = [
    # ---- 子弹配件（17）----
    "std", "light", "pellet", "ap", "laser", "grenade", "bouncer", "missile",
    "mine", "shock", "chain", "saw", "flame", "hole", "trigger", "timer", "expire",
    # ---- 作用于子弹的衍生件（13）----
    "dmg", "heavy", "speed", "pierce", "explode", "homing", "bounce",
    "burn", "frost", "giant", "crit", "scatter", "thrift",
    # ---- 枪械向衍生件（9）----
    "g_mag", "g_reload", "g_rapid", "g_stable", "g_recycle",
    "g_parallel", "g_leech", "g_double", "g_triple",
]

for _id in ITEM_IDS:
    # 字形占比放得比状态图标宽（0.62~0.94）：配件图标种类多，有的本来就很扁/很细
    # （穿甲钉、光矛），卡在 0.80 会让它们在圆形底板上显得比别的图标小一圈。
    #
    # pedestal：能量类有辉光底座的先减掉再抠（见 normalize_glyph 里的说明）。
    SPECS["item_" + _id] = dict(mode="glyph", size=128, trim=True,
                                max_dev=(0.62, 0.94),
                                pedestal=24 if _id in ("laser", "hole") else 0)

GROUPS = {
    "plate": ["plate_dark", "plate_light"],
    "energy": ["energy"],
    "card": ["card_inner"],
    "icon": ["icon_lock", "icon_check", "icon_arrow"],
    "panel": ["panel", "card_frame", "banner", "bar_frame"],
    "glyph_icon": ["keycap", "icon_bullet", "icon_whole", "icon_after", "icon_gun"],
    "fx": ["vignette"],
    "items": ["item_" + s for s in ITEM_IDS],
}

# 合成器（gen_ui_assets_ai.py）产出的 21 张，由它自己管，不算"绕过流水线"。
MANAGED_BY_COMPOSITOR = {
    "btn_primary", "btn_primary_hover", "btn_danger",
    "bar_bg", "bar_fill_hp", "bar_fill_xp", "bar_fill_boss", "bar_fill_threat",
    "slot_empty", "slot_filled",
    "tag_starter", "tag_common", "tag_rare", "tag_epic",
    "icon_frame", "icon_lock", "icon_check", "icon_arrow",
    "divider", "hit_marker", "vignette_damage",
    # 准星由合成器精确绘制（AI 出图在 16~130px 下看不出细节，对称性也没法保证）
    "crosshair",
}

# 质量门阈值
MIN_ALPHA_COVERAGE = 0.02      # 低于此值视为空图
BG_RESIDUE_MAX = 6.0           # glyph 画布四角平均亮度上限（黑底应接近 0）
EDGE_ALPHA_MAX = 12            # glyph 最外一圈 alpha 上限（不该碰到边框）


# ---------------------------------------------------------------- 工具

def clamp(v):
    return 0 if v < 0 else (255 if v > 255 else int(v))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_rgb(path):
    return Image.open(path).convert("RGB")


def desaturate(img):
    """按亮度重组 RGB，去掉 AI 出图常带的暖偏。"""
    g = img.convert("L")
    return Image.merge("RGB", (g, g, g))


def normalize_material(name, spec, src_path, dst_path):
    """不透明材质：降尺寸 + （可选）去色。"""
    im = load_rgb(src_path)
    src_size = im.size
    if spec.get("sat", 1.0) < 1.0:
        im = desaturate(im)
    s = spec["size"]
    im = im.resize((s, s), Image.LANCZOS)
    im.save(dst_path, optimize=True)
    return {
        "mode": "material",
        "src_size": src_size,
        "dst_size": (s, s),
        "has_alpha": False,
        "coverage": 1.0,
    }


def normalize_glyph(name, spec, src_path, dst_path):
    """黑底白字 glyph：抠图 → 颜色反解 → 包围盒归一 → 居中。

    抠图做法：取亮度当 alpha。
      黑底(0) → 全透明，白字(255) → 不透明，抗锯齿灰边 → 半透明。
    这比"按颜色阈值抠"好：边缘不会出现锯齿硬切。

    颜色反解（关键）：黑底上的抗锯齿像素是 "白字 × alpha"，其 RGB 已被拉低。
    若直接保留，半透明像素的 RGB 仍是近黑 —— 叠到深色 HUD 上就是一圈脏边。
    正确做法是把 RGB 统一写成纯白，只让 alpha 承担透明度：
      observed = white * alpha  ⇒  alpha = observed，color = white
    """
    im = load_rgb(src_path)
    src_size = im.size
    lum = im.convert("L")

    # 背景底座扣除：能量类图标（电浆、分裂）会带一圈微弱辉光，
    # 整块底被抬亮 10~20 级，「亮度当 alpha」就会把辉光一起抠进来 ——
    # 表现是图标外一圈灰雾、四角残留超标。先减掉底座再取 alpha，辉光就归零。
    # 门槛 24 是实测值：辉光峰值约 17，而线稿主体都在 200 以上，减掉不影响字形。
    pedestal = spec.get("pedestal", 0)
    if pedestal:
        lum = lum.point(lambda v: 0 if v <= pedestal else int((v - pedestal) * 255 / (255 - pedestal)))

    # 阈值化取包围盒（判"哪里有字"），用低阈值避免裁掉虚化的抗锯齿边
    bbox = lum.point(lambda v: 255 if v > 18 else 0).getbbox()
    if bbox is None:
        raise RuntimeError("整张图都是黑的，没有任何字形")

    trimmed = lum.crop(bbox)

    # 归一：把裁出的字形放进正方形并留统一边距，保证一组图标视觉大小一致
    s = spec["size"]
    margin_ratio = 0.10
    inner = int(s * (1 - margin_ratio * 2))
    gw, gh = trimmed.size
    scale = min(inner / gw, inner / gh)
    nw, nh = max(1, int(round(gw * scale))), max(1, int(round(gh * scale)))
    trimmed = trimmed.resize((nw, nh), Image.LANCZOS)

    alpha = Image.new("L", (s, s), 0)
    alpha.paste(trimmed, ((s - nw) // 2, (s - nh) // 2))

    # 轻微 gamma 提亮：AI 字形边缘偏软，压一下让轮廓更硬（art-style 要求硬边）
    alpha = alpha.point(lambda v: clamp(255 * (v / 255.0) ** 0.82))

    out = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    out.putalpha(alpha)                      # RGB 全白 + alpha 遮罩 = 正确反解
    out.save(dst_path, optimize=True)

    a = out.getchannel("A")
    hist = a.histogram()
    coverage = sum(hist[16:]) / (s * s)
    bbox_ratio = max(nw, nh) / s

    return {
        "mode": "glyph",
        "src_size": src_size,
        "dst_size": (s, s),
        "has_alpha": True,
        "coverage": coverage,
        "glyph_bbox_ratio": bbox_ratio,
        "trim_bbox": bbox,
    }


# ---------------------------------------------------------------- 老素材三模式

def _np_rgb(path):
    """读 RGB 为 float32 数组。老素材有的体积很大，用 numpy 避免逐像素循环。"""
    im = Image.open(path).convert("RGB")
    return im, np.asarray(im).astype(np.float32)


def _fill_holes(mask):
    """填掉剪影内部的孔洞。

    为什么要填：底板内部有极暗的凹槽/纹理（card_frame 内部 87% 像素接近纯黑），
    按亮度取剪影会在板面上打出一堆洞。alpha 一有洞，画面上就是透底的麻点。
    做法：从画布四角向内泛洪「外部背景」，剩下的未触达区就是内部孔洞。

    ⚠ Image.fromarray 出来的图是**只读**的，ImageDraw.floodfill 在只读图上
    会静默失败（不写任何像素、不抛异常），结果整张图都被当成孔洞填满。
    必须用 frombytes/ascontiguous 造一份可写副本。
    """
    h, w = mask.shape
    bg = np.where(mask, 0, 255).astype(np.uint8)
    bg = Image.frombytes("L", (w, h), bg.tobytes())
    for seed in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        ImageDraw.floodfill(bg, seed, 128, thresh=0)
    # 泛洪触达的 128 = 外部背景；没触达的 255 = 内部孔洞
    holes = np.asarray(bg) == 255
    return np.asarray(mask) | holes


def _silhouette(path, thresh=6):
    """按亮度取剪影。返回 (bool mask, 说明)。"""
    im, a = _np_rgb(path)
    lum = a.max(axis=2)
    mask = lum > thresh
    if mask.mean() < 0.005:
        raise RuntimeError("几乎找不到内容（亮度 >%d 的像素不足 0.5%%）" % thresh)
    return im, mask


def _corner_radius(mask):
    """估圆角半径：从四角沿对角线走，找到第一个进入实体的距离。

    9-slice 的边距必须 ≥ 圆角半径，否则角上的弧被切开、拉成椭圆。
    这里给出的是**归一化后**能复用的像素值，供 SAssetPostprocessor 与合成器对齐。
    """
    h, w = mask.shape
    diag = min(h, w) // 2 - 1
    best = 0
    for (sx, sy) in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        ox = 0 if sx > 0 else w - 1
        oy = 0 if sy > 0 else h - 1
        for d in range(diag):
            x, y = ox + sx * d, oy + sy * d
            if not (0 <= x < w and 0 <= y < h):
                break
            if mask[y, x]:
                break
        # 沿对角线走 d 步 ≈ 半径 r 的弧：d ≈ r*(1 - 1/√2) ⇒ r ≈ d/(1-0.7071)
        best = max(best, d / (1 - 0.7071))
    return int(round(best))


def normalize_plate(name, spec, src_path, dst_path):
    """暗色底板：剪影抠图 → 填孔 → 裁到内容框 → 反预乘去黑边 → 规范尺寸。

    关键点（踩过的坑）：
      · AI 出的底板是「暗板 + 纯黑背景」混在一起，没有 alpha。直接进引擎，
        9-slice 的四个角切片取到的就是纯黑背景，画面上四角是一团黑方块。
      · 剪影内部不能按亮度再抠一次 —— 板面本身很暗（panel 内部均值只有 20），
        再抠会打出一堆洞，alpha 变成筛子。
      · 必须裁到内容框：原图四周有 30~270px 的纯黑留白，不裁的话 9-slice 的
        角切片区域落在留白上，圆角直接消失。
      · 边缘像素是「板色 × alpha + 黑 × (1-alpha)」，已被拉黑。
        不反解就叠在亮背景上会留一圈黑描边（和 glyph 同一个坑）。
    """
    im, mask = _silhouette(src_path)
    mask = _fill_holes(mask)

    ys, xs = np.nonzero(mask)
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1
    im = im.crop((x0, y0, x1, y1))
    mask = mask[y0:y1, x0:x1]

    # 轻微收缩 1px：AI 边缘有一圈很淡的光晕，收掉避免"发虚的外框"
    m = Image.fromarray((mask * 255).astype(np.uint8), mode="L").filter(ImageFilter.MinFilter(3))
    alpha = m.filter(ImageFilter.GaussianBlur(0.5))

    rgb = np.asarray(im.convert("RGB")).astype(np.float32)
    aa = np.asarray(alpha).astype(np.float32) / 255.0
    # 反预乘：observed = color * a  ⇒  color = observed / a
    # 只在 a > 0.25 时做除法 —— 更小的 a 上除法会把噪声放大成亮边，
    # 而那些像素本来就几乎不可见，保持原值不会看出问题。
    denom = np.maximum(aa, 0.25)[..., None]
    fixed = np.clip(rgb / denom, 0, 255)
    out = np.where(aa[..., None] > 0.25, fixed, rgb)

    # ⚠ 不要用 Image.fromarray(...).putalpha()：fromarray 的数组是只读视图，
    # putalpha 会抛 "image is readonly"。直接拼成 RGBA 数组最稳。
    a8 = np.asarray(alpha).astype(np.float32)
    rgba = np.concatenate([out, a8[..., None]], axis=2)
    img = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), mode="RGBA")

    # 按内容比例缩放，只约束长边。硬缩成正方形会把圆角横向/纵向拉变形
    # （card_frame 是 0.77 竖构图、bar_frame 是 10.6:1 扁条，拉成 1:1 会毁掉弧）。
    edge = spec["edge"]
    cw, ch = img.size
    if cw >= ch:
        tw, th = edge, max(1, int(round(edge * ch / cw)))
    else:
        tw, th = max(1, int(round(edge * cw / ch))), edge
    src_size = img.size
    img = img.resize((tw, th), Image.LANCZOS)
    img.save(dst_path, optimize=True)

    out_alpha = np.asarray(img)[..., 3]
    radius = _corner_radius(out_alpha > 128)
    return {
        "mode": "plate",
        "src_size": src_size,
        "dst_size": (tw, th),
        "has_alpha": True,
        "coverage": float((out_alpha > 16).mean()),
        "corner_radius": radius,
        "aspect": tw / float(th),
    }


def normalize_icon(name, spec, src_path, dst_path):
    """已带 alpha 的彩色图标：包围盒归一 + 缩尺寸，**不改颜色**。

    与 glyph 的差别（很重要）：glyph 是黑底白字，可以把 RGB 统一写成纯白；
    这里原图是彩色且带 alpha（枪、子弹、准星），RGB 必须原样保留，
    否则菜单里 `Simple + tint` 出来就是一片色块，看不出枪和子弹。
    """
    im = Image.open(src_path).convert("RGBA")
    src_size = im.size
    a = im.getchannel("A")
    bbox = a.point(lambda v: 255 if v > 8 else 0).getbbox()
    if bbox is None:
        raise RuntimeError("整张图都是透明的，没有内容")

    trimmed = im.crop(bbox)
    tw, th = spec["size"] if isinstance(spec["size"], tuple) else (spec["size"],) * 2
    pad = int(min(tw, th) * spec.get("pad_ratio", 0.0))
    inner_w, inner_h = tw - pad * 2, th - pad * 2

    gw, gh = trimmed.size
    scale = min(inner_w / gw, inner_h / gh)
    nw, nh = max(1, int(round(gw * scale))), max(1, int(round(gh * scale)))
    trimmed = trimmed.resize((nw, nh), Image.LANCZOS)

    out = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    out.paste(trimmed, ((tw - nw) // 2, (th - nh) // 2))
    out.save(dst_path, optimize=True)

    cov = (np.asarray(out)[..., 3] > 16).mean()
    return {
        "mode": "icon",
        "src_size": src_size,
        "dst_size": (tw, th),
        "has_alpha": True,
        "coverage": float(cov),
        "glyph_bbox_ratio": max(nw, nh) / max(tw, th),
        "trim_bbox": bbox,
    }


def normalize_mask(name, spec, src_path, dst_path):
    """亮度遮罩（晕影）：RGB 归白，alpha 取亮度。

    原图是「纯黑底 + 暗红光晕」且没有 alpha。当 Simple 铺满全屏时，
    黑底会把整块画面压暗 —— 这是它现在的问题。遮罩化之后由代码控制颜色与强度
    （HudView 已经在逐帧调 alpha），底图只负责"哪里浓、哪里淡"。
    """
    im, a = _np_rgb(src_path)
    src_size = im.size
    lum = a.max(axis=2)
    peak = float(lum.max())
    if peak < 8:
        raise RuntimeError("整张图几乎全黑，没有晕影可提")

    alpha = np.clip(lum / peak * spec.get("max_alpha", 255), 0, 255).astype(np.uint8)
    tw, th = spec["size"] if isinstance(spec["size"], tuple) else (spec["size"],) * 2
    out = Image.new("RGBA", (tw, th), (255, 255, 255, 0))
    out.putalpha(Image.fromarray(alpha, mode="L").resize((tw, th), Image.LANCZOS))
    out.save(dst_path, optimize=True)

    cov = (np.asarray(out)[..., 3] > 16).mean()
    return {
        "mode": "mask",
        "src_size": src_size,
        "dst_size": (tw, th),
        "has_alpha": True,
        "coverage": float(cov),
        "peak_lum": peak,
    }


# ---------------------------------------------------------------- 质量门
def measure_background_residue(img, probe=None):
    """量画布四角的平均亮度。glyph 抠干净后应接近 0（全透明）。

    ⚠ probe 必须按画布比例取，不能写死像素：字形归一化后只留 10% 边距，
    128px 的配件图标用固定 24px 探测，「角」就已经压在内容上了 ——
    那会把一张干净的图标误判成「黑底没抠干净」（踩过：两张纯黑底的图被判残留 14）。
    """
    w, h = img.size
    if probe is None:
        probe = max(4, int(round(min(w, h) * 0.07)))
    a = img.getchannel("A")
    vals = []
    for (x0, y0) in ((0, 0), (w - probe, 0), (0, h - probe), (w - probe, h - probe)):
        for x in range(x0, x0 + probe, 2):
            for y in range(y0, y0 + probe, 2):
                vals.append(a.getpixel((x, y)))
    return sum(vals) / len(vals)


def measure_edge_alpha(img, band=2):
    """量最外一圈的 alpha 峰值。glyph 不该碰到画布边缘（会被切）。"""
    a = img.getchannel("A")
    w, h = img.size
    best = 0
    for x in range(w):
        for y in list(range(band)) + list(range(h - band, h)):
            best = max(best, a.getpixel((x, y)))
    for y in range(h):
        for x in list(range(band)) + list(range(w - band, w)):
            best = max(best, a.getpixel((x, y)))
    return best


def quality_gate(name, spec, out_path, info):
    """返回 (ok, [问题...], [指标...])。"""
    problems, metrics = [], []
    img = Image.open(out_path).convert("RGBA")

    cov = info.get("coverage", 0)
    metrics.append("覆盖=%.1f%%" % (cov * 100))
    if cov < MIN_ALPHA_COVERAGE:
        problems.append("几乎是空图（覆盖 %.2f%%）" % (cov * 100))

    if spec["mode"] == "material":
        a = img.getchannel("A").histogram()
        if sum(a[:255]) > 0:
            problems.append("材质图不该有透明像素（%d 个）" % sum(a[:255]))
        # 纯色检测：方差过低说明 AI 给了张纯色/严重糊的图
        g = img.convert("L")
        hist = g.histogram()
        total = sum(hist)
        mean = sum(i * v for i, v in enumerate(hist)) / total
        var = sum((i - mean) ** 2 * v for i, v in enumerate(hist)) / total
        metrics.append("亮度σ=%.1f" % (var ** 0.5))
        # min_sigma 可按用途放宽。默认 3.0 用来挡"AI 交了张纯色图"，
        # 但 card_inner 这类**文字底衬**的设计目标就是尽量平坦 ——
        # 纹理越明显，压在它上面的 13~15px 小字越读不清。
        # 它要的是"均匀且有极细颗粒"，不是"有可见纹理"，所以门槛放到 1.5。
        min_sigma = spec.get("min_sigma", 3.0)
        if var ** 0.5 < min_sigma:
            problems.append("材质几乎没有纹理（σ=%.1f < %.1f）" % (var ** 0.5, min_sigma))
        return (not problems), problems, metrics

    if spec["mode"] == "plate":
        # 底板：四个**角**必须透明，否则说明黑底没抠掉、9-slice 角上会带黑块。
        # ⚠ 判据不能是"边缘不透明" —— 底板被裁到内容框后，边带本来就紧贴板边，
        # 边中不透明才是对的。真正体现"抠掉了黑底"的是圆角外那一点点三角区。
        w, h = img.size
        a = np.asarray(img)[..., 3]
        probe = max(4, int(min(w, h) * 0.02))
        corners = [a[0, 0], a[0, -1], a[-1, 0], a[-1, -1]]
        cmean = float(np.mean(corners))
        metrics.append("四角α=%.0f" % cmean)
        if cmean > 40:
            problems.append("四角不透明（α=%.0f）：黑底没抠掉，9-slice 角上会带黑块" % cmean)

        # 板面不能有洞：内部必须几乎全不透明（孔洞填充是否生效）
        inner = a[int(h * 0.2):int(h * 0.8), int(w * 0.2):int(w * 0.8)]
        hole = float((inner < 200).mean())
        metrics.append("内部空洞=%.2f%%" % (hole * 100))
        if hole > 0.02:
            problems.append("板面有 %.1f%% 的洞（剪影填充失败）" % (hole * 100))

        r = info.get("corner_radius", 0)
        metrics.append("圆角r=%d" % r)
        if r <= 0:
            problems.append("量不到圆角半径，9-slice 边距无法确定")
        return (not problems), problems, metrics

    if spec["mode"] == "mask":
        # 遮罩（晕影）：中心必须透明（否则会压暗画面正中），
        # 边缘环带必须有强度（否则等于没画）。
        # ⚠ 判据必须落在**光环所在的位置**。这张素材是「贴着四边的细光带」
        # （实测亮度峰在离边约 3% 处），不是那种从边到心渐隐的厚晕影。
        # 之前取到 12~18% 的位置，量到的全是已经衰减到 1 的像素，误判成"没强度"。
        w, h = img.size
        a = np.asarray(img)[..., 3]
        center = float(a[int(h * 0.35):int(h * 0.65), int(w * 0.35):int(w * 0.65)].mean())
        band = max(2, int(round(min(w, h) * 0.03)))
        lo, hi = band, band * 3
        ring = np.r_[
            a[lo:hi, :].ravel(), a[-hi:-lo, :].ravel(),
            a[:, lo:hi].ravel(), a[:, -hi:-lo].ravel(),
        ]
        edge = float(np.percentile(ring, 95))
        metrics.append("中心α=%.0f 边环α=%.0f" % (center, edge))
        if center > 40:
            problems.append("中心不透明（α=%.0f）：会把画面正中压暗" % center)
        if edge < 60:
            problems.append("边环太弱（α=%.0f）：晕影没有强度" % edge)
        return (not problems), problems, metrics

    if spec["mode"] == "icon":
        # 彩色图标：不能是纯白（那说明又被当成 glyph 处理过，形状会糊成一片）、
        # 包围盒必须留边（碰到画布边缘会被切）。
        px = np.asarray(img)[..., :3]
        al = np.asarray(img)[..., 3]
        solid = al > 200
        if solid.any():
            rgb = px[solid]
            colorful = float((rgb.max(axis=1) - rgb.min(axis=1)).mean())
            metrics.append("彩度=%.0f" % colorful)
            if colorful < 2.0:
                problems.append("图标变成纯灰白了（彩度 %.1f）—— 彩色图标不能走 glyph 反解"
                                % colorful)
        edge = measure_edge_alpha(img)
        metrics.append("边缘alpha=%d" % edge)
        if edge > 8:
            problems.append("图标碰到画布边缘（alpha=%d），缩放后会被切" % edge)
        return (not problems), problems, metrics

    # ---- glyph（原有分支）----
    residue = measure_background_residue(img)
    metrics.append("四角残留=%.1f" % residue)
    if residue > BG_RESIDUE_MAX:
        problems.append("黑底没抠干净（四角残留 %.1f）" % residue)

    edge = measure_edge_alpha(img)
    metrics.append("边缘alpha=%d" % edge)
    if edge > EDGE_ALPHA_MAX:
        problems.append("字形碰到画布边缘（alpha=%d），会被切" % edge)

    ratio = info.get("glyph_bbox_ratio", 0)
    lo, hi = spec.get("max_dev", (0, 1))
    metrics.append("字形占比=%.2f" % ratio)
    if not (lo <= ratio <= hi):
        problems.append("字形占比 %.2f 超出 [%.2f, %.2f]" % (ratio, lo, hi))

    # 颜色反解校验：不透明像素必须是纯白（否则会带黑边）
    px = img.load()
    w, h = img.size
    bad = 0
    for y in range(0, h, 5):
        for x in range(0, w, 5):
            r, g, b, a = px[x, y]
            if a > 200 and (r < 248 or g < 248 or b < 248):
                bad += 1
    if bad > 0:
        problems.append("有 %d 个不透明像素不是纯白，颜色反解没生效" % bad)

    return (not problems), problems, metrics


# ---------------------------------------------------------------- 主流程

def process(name, force=False):
    spec = SPECS[name]
    src_path = os.path.join(RAW, name + ".png")
    # final 素材没有合成阶段：预处理产物就是最终图，直接写进 Assets 的对应分类目录。
    # 其余素材（材质/字形）写 _Inbox/ui-ai-src，留给 gen_ui_assets_ai.py 合成。
    dst_dir = cat_dir(name) if spec.get("final") else SRC
    dst_path = os.path.join(dst_dir, name + ".png")
    os.makedirs(dst_dir, exist_ok=True)

    if not os.path.isfile(src_path):
        return name, False, ["缺少原图：" + src_path], []

    src_hash = sha256(src_path)
    if not force and os.path.isfile(dst_path):
        prev = _prev_report().get(name)
        if prev and prev.get("src_sha256") == src_hash:
            # 回执里必须带上过门结论，否则合成器会认为"没处理过"
            return name, True, [], ["未变化，跳过"]

    mode = spec["mode"]
    # 先写到临时文件，质量门过了才搬到正式位置。
    # 为什么：final 素材的目标路径就是 Assets/Resources/UI，
    # 若先写再校验，一张没通过的图会留在工程里被 Unity 导进包（踩过）。
    tmp_path = dst_path + ".tmp.png"
    if mode == "material":
        info = normalize_material(name, spec, src_path, tmp_path)
    elif mode == "plate":
        info = normalize_plate(name, spec, src_path, tmp_path)
    elif mode == "icon":
        info = normalize_icon(name, spec, src_path, tmp_path)
    elif mode == "mask":
        info = normalize_mask(name, spec, src_path, tmp_path)
    else:
        info = normalize_glyph(name, spec, src_path, tmp_path)

    ok, problems, metrics = quality_gate(name, spec, tmp_path, info)
    if ok:
        shutil.move(tmp_path, dst_path)
    else:
        # 保留 .tmp.png 便于排查，但不进入工程
        try:
            os.remove(tmp_path)
        except OSError:
            pass

    info["src_sha256"] = src_hash
    info["ok"] = ok
    info["problems"] = problems
    info["metrics"] = metrics
    return name, ok, problems, metrics


_report_cache = None


def _prev_report():
    global _report_cache
    if _report_cache is not None:
        return _report_cache
    try:
        with open(REPORT, "r", encoding="utf-8") as f:
            _report_cache = json.load(f)
    except Exception:
        _report_cache = {}
    return _report_cache


def audit_legacy():
    """列出 Assets/Resources/UI 里**不在本流水线内**的贴图。

    这些是早期直接由 APIMart 原图落进工程的，绕过了抠图与尺寸规范化。
    典型问题：2048² 存着，实际最大只用到 720px（浪费 8 倍像素）；
    或从没做过质量门检查。
    """
    assets_ui = os.path.join(os.path.dirname(HERE), "Assets", "Resources", "UI")
    if not os.path.isdir(assets_ui):
        return
    managed = set(SPECS.keys())
    print("\n===== 绕过流水线的贴图（建议迁移）=====")
    print("%-14s %-12s %-9s %s" % ("贴图", "尺寸", "体积", "说明"))
    print("-" * 72)
    rows = 0
    # 递归扫描：素材已按功能分类到 Sprites/<Category>/，只在根目录 listdir 会全部漏掉。
    for root, _dirs, files in os.walk(assets_ui):
        for f in sorted(files):
            if not f.endswith(".png"):
                continue
            name = f[:-4]
            # 合成器产出的 21 张由 gen_ui_assets_ai.py 管，不算绕过
            if name in MANAGED_BY_COMPOSITOR:
                continue
            if name in managed:
                continue
            p = os.path.join(root, f)
            rel = os.path.relpath(p, assets_ui).replace("\\", "/")
            try:
                im = Image.open(p)
                kb = os.path.getsize(p) / 1024
            except Exception:
                continue
            note = []
            if max(im.size) >= 1024:
                note.append("尺寸偏大")
            if im.mode != "RGBA":
                note.append("无 alpha")
            print("%-14s %-12s %6.0fKB  %s  [%s]"
                  % (name, "%dx%d" % im.size, kb,
                     "、".join(note) or "形态正常，仅未纳入流水线", rel))
            rows += 1
    if rows == 0:
        print("无")
    else:
        print("\n迁移做法：把原图放进 _Inbox/ui-ai-raw/，在 SPECS 里加一条，再重跑本脚本。")
        print("⚠ 若要做降尺寸，必须同步重算 9-slice 边距 —— 边距是贴图像素，")
        print("   缩放贴图会让同样的边距在画面上放大，圆角观感随之改变。")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="逗号分隔：plate,energy,icon")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--force", action="store_true", help="源未变也重做")
    ap.add_argument("--audit-legacy", action="store_true", help="列出绕过流水线的贴图")
    args = ap.parse_args()

    if args.list:
        for k, v in GROUPS.items():
            print("%-8s %s" % (k, ", ".join(v)))
        return 0

    if args.audit_legacy:
        audit_legacy()
        return 0

    os.makedirs(SRC, exist_ok=True)

    want = []
    if args.only:
        for g in args.only.split(","):
            g = g.strip()
            if g not in GROUPS:
                print("未知类别：%s" % g, file=sys.stderr)
                return 2
            want.extend(GROUPS[g])
    else:
        for v in GROUPS.values():
            want.extend(v)

    print("UI 素材前期处理  raw=%s  ->  src=%s\n" % (RAW, SRC))
    print("%-14s %-12s %-34s %s" % ("素材", "结果", "指标", "问题"))
    print("-" * 96)

    report = _prev_report()
    bad = []
    for name in want:
        try:
            name, ok, problems, metrics = process(name, args.force)
            flag = "OK" if ok else "FAIL"
            if not ok:
                bad.append(name)
            print("%-14s %-12s %-34s %s"
                  % (name, flag, "; ".join(metrics)[:34] if metrics else "-",
                     "; ".join(problems)))
            if ok and metrics and metrics != ["未变化，跳过"]:
                report[name] = dict(SPECS[name])
                # 轻量记录，够判断"是否处理过且源未变"
                try:
                    report[name]["src_sha256"] = sha256(os.path.join(RAW, name + ".png"))
                except Exception:
                    pass
        except Exception as e:
            print("%-14s %-12s %s" % (name, "FAIL", "%s: %s" % (type(e).__name__, e)))
            bad.append(name)

    with open(REPORT, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    total = len(want)
    print("\nRESULT 前期处理 %d/%d 通过" % (total - len(bad), total))

    # 同组一致性：逐张判只能发现"单张离群"。真正要看的是**视觉重量**是否一致 ——
    # 长边占比对这三个图标恒等于 0.80（归一化的直接结果，测不出差异），
    # 而墨量（alpha 总和）才反映"看起来多大"。
    # 反例：箭头和锁的长边都是 204，但箭头细、锁粗，只比长边会误判为一致。
    glyph_ink = {}
    for name in want:
        p = os.path.join(SRC, name + ".png")
        if not os.path.isfile(p) or SPECS[name]["mode"] != "glyph":
            continue
        # 配件图标不参与「同组墨量」判据：它们是 25 种**不同物体**（弹丸 vs 天线），
        # 墨量天然差几倍，强行拉平只会让穿甲钉被放大成一根柱子。
        # 这条判据原本是给「锁定/对勾/箭头」这三个同语义状态图标用的。
        if name.startswith("item_"):
            continue
        a = Image.open(p).convert("RGBA").getchannel("A")
        glyph_ink[name] = sum(a.histogram()[i] * i for i in range(256)) / 255.0

    if len(glyph_ink) >= 2:
        lo_n, lo_v = min(glyph_ink.items(), key=lambda kv: kv[1])
        hi_n, hi_v = max(glyph_ink.items(), key=lambda kv: kv[1])
        ratio = hi_v / max(1.0, lo_v)
        print("同组墨量比 %.2f（%s %.0f : %s %.0f）"
              % (ratio, hi_n, hi_v, lo_n, lo_v))
        if ratio > 1.9:
            print("⚠ 同组图标视觉重量相差超过 1.9 倍，并排显示会一大一小")
            if "icon-consistency" not in bad:
                bad.append("icon-consistency")

    if bad:
        print("被拦下：" + ", ".join(bad))
        print("⚠ 先修好这些素材，不要继续跑 gen_ui_assets_ai.py —— 半成品素材会污染预制体。")
        return 1
    print("下一步：python tools\\gen_ui_assets_ai.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())

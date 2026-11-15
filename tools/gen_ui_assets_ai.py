"""
Project S — UI 素材合成器（AI 材质 + 精确几何）

三段流程，本脚本是第三段：

    APIMart 出图            前期处理                本脚本
  _Inbox/ui-ai-raw/  ──►  _Inbox/ui-ai-src/  ──►  Assets/Resources/UI/
  （RGB、黑底、4~8MB）      （抠图、尺寸、质量门）      （几何 + 9-slice）

分工（这是本脚本存在的理由）：
  · APIMart 负责**材质**：金属拉丝的微观起伏、能量条的细丝、图标的字形。
    这些是"手感"，程序化画不出来，AI 画得好。
  · 前期处理负责**抠图与规范化**：黑底转 alpha、颜色反解去黑边、降尺寸。
  · 本脚本负责**几何**：圆角半径、描边宽度、9-slice 安全区。
    这些必须像素级确定 —— 模型生成的圆角半径每次都不一样，
    而 9-slice 拉伸对半径极敏感（半径 > 边距就会把圆角拉成椭圆）。

所以不要"让 AI 直接画按钮"。按钮里模型还会顺手画出假文字和假图标，
而且边距对不上，进 Unity 一拉伸就变形。

前置条件：输入必须是 tools/preprocess_ui_assets.py 处理过的素材。
直接喂 APIMart 原图会得到黑底方块（原图没有 alpha），所以这里先校验。

输入：_Inbox/ui-ai-src/*.png（预处理产物，已抠图）
输出：Assets/Resources/UI/*.png（覆盖同名文件，.meta 保留原 GUID，引用不断）

用法（工程根目录）：
  python tools/preprocess_ui_assets.py              # 第一步：抠图/规范化
  python tools/gen_ui_assets_ai.py                  # 第二步：合成
  python tools/gen_ui_assets_ai.py --only btn,bar   # 只合成某一类
  python tools/gen_ui_assets_ai.py --list           # 看有哪些类
"""

import argparse
import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)
# 未处理资产统一放工程根的 _Inbox/（不入库，见 _Inbox/README.md）
INBOX = os.path.join(PROJ, "_Inbox")
SRC = os.path.join(INBOX, "ui-ai-src")
REPORT = os.path.join(HERE, "ui-preprocess.json")
OUT = os.path.join(PROJ, "Assets", "Resources", "UI")

# ---------------- 调色板（art-style.md §2/§4，与 panel.png 一致）----------------

EDGE = (86, 96, 112, 255)
CYAN = (47, 143, 216, 255)
CYAN_HI = (120, 214, 255, 255)
AMBER = (232, 163, 61, 255)
RED = (224, 71, 76, 255)
WHITE = (240, 244, 250, 255)

# 9-slice 边距必须与 SAssetPostprocessor.BorderFor 完全一致。
# 两边一旦不同步，圆角会被拉成椭圆或描边变粗 —— 这类问题只在拉伸时出现，
# 在 1:1 预览里看不出来，所以这里直接写死并在生成时按真实控件尺寸校验。
#
# 关键：这些数值是按**控件真实像素**倒推的，不是按贴图尺寸。
# 例：HUD 的 XP 条控件只有 360x14，若边距给 24，上下 24+24=48 会吃光 14 的高度，
# Unity 钳制后圆角变直角、描边糊成一条。贴图 512x96 再大也救不了。
BORDERS = {
    "btn_primary": (16, 14, 16, 14),        # 最小控件 108x40（背包 BuyButton）
    "btn_primary_hover": (16, 14, 16, 14),
    "btn_danger": (16, 14, 16, 14),
    "bar_bg": (3, 3, 3, 3),                 # 最小控件 360x14（HUD 的 XP 条）
    "slot_empty": (18, 18, 18, 18),         # 最小控件 60x84（ChainRow.Tail）
    "slot_filled": (6, 6, 6, 6),            # 最小控件 20x20（Menu.BadgeBack）
    "tag_starter": (1, 1, 1, 1),            # 最小控件 6x84（ItemCard.Tag 细竖条）
    "tag_common": (1, 1, 1, 1),
    "tag_rare": (1, 1, 1, 1),
    "tag_epic": (1, 1, 1, 1),
    "panel": (20, 20, 20, 20),              # 最小控件 380x228（VolumePanel）
    # ---- 老素材：预处理直接产出最终图，边距同样必须在两边对齐 ----
    "card_frame": (10, 10, 10, 10),         # 最小控件 150x84（ItemCard），圆角 6
    # 卡片内衬：最小控件是 ItemCard 内缩 4 后的 142x76，圆角 5 ⇒ 边距 8 够包住弧
    "card_inner": (8, 8, 8, 8),
    "banner": (24, 24, 24, 24),             # 最小控件 720x88，圆角 22（硬下限）
    "bar_frame": (16, 16, 16, 16),          # 无引用；圆角 15，服务 >=40 高控件
}

# 每个素材在 prefab 里的最小控件尺寸（宽,高）。改 prefab 布局时要同步改这里。
MIN_RECT = {
    "btn_primary": (108, 40),
    "btn_primary_hover": (108, 40),
    "btn_danger": (340, 70),
    "bar_bg": (360, 14),
    "slot_empty": (60, 84),
    "slot_filled": (20, 20),
    "tag_starter": (272, 16),
    "tag_common": (272, 16),
    "tag_rare": (272, 16),
    "tag_epic": (272, 16),
    "panel": (380, 228),
    "card_frame": (150, 84),
    "card_inner": (142, 76),
    "banner": (720, 88),
    "bar_frame": (60, 40),
}

# 圆角半径。硬规则：radius <= min(border_lr, border_tb)，
# 否则 9-slice 会把圆角切开，角上出现一小块被拉平的直角。
RADIUS = {
    "btn": 12,
    "bar": 3,
    "slot_empty": 18,
    "slot_filled": 6,
    "tag": 1,
    "panel": 12,
}


# ---------------- 材质工具 ----------------

_cache = {}
_report = None


def load_report():
    """读取预处理回执。缺失说明没跑过前期处理 —— 直接拒绝，不要静默降级。"""
    global _report
    if _report is not None:
        return _report
    try:
        with open(REPORT, "r", encoding="utf-8") as f:
            _report = json.load(f)
    except Exception:
        _report = {}
    return _report


def src(name):
    if name in _cache:
        return _cache[name]
    p = os.path.join(SRC, name + ".png")
    if not os.path.isfile(p):
        raise SystemExit(
            "缺少预处理素材：%s\n"
            "先跑：python tools\\preprocess_ui_assets.py\n"
            "（不要直接把 APIMart 原图拷进来 —— 原图是黑底无 alpha，会变成黑方块）" % p)

    rep = load_report()
    if name not in rep:
        raise SystemExit(
            "素材 %s 没有预处理回执（tools/ui-preprocess.json 里没有记录）。\n"
            "先跑：python tools\\preprocess_ui_assets.py" % name)
    if rep[name].get("ok") is False:
        raise SystemExit(
            "素材 %s 没通过前期处理的质量门，不能进引擎。\n"
            "先跑：python tools\\preprocess_ui_assets.py  看清问题再修" % name)

    im = Image.open(p).convert("RGBA")
    _cache[name] = im
    return im


def material(name, w, h, sat=0.0, bright=1.0, contrast=1.0):
    """把 AI 材质裁成目标尺寸并做轻度调色。

    sat=0 表示去色：AI 出的金属常带一点暖偏，这里统一拉到中性，
    避免按钮比面板"黄"一档（视觉上像两套资产）。
    """
    im = src(name).resize((w, h), Image.LANCZOS, box=None)
    px = im.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            # 去色：按亮度重组
            if sat < 1.0:
                lum = 0.299 * r + 0.587 * g + 0.114 * b
                r = r * sat + lum * (1 - sat)
                g = g * sat + lum * (1 - sat)
                b = b * sat + lum * (1 - sat)
            # 对比：围绕中灰 128 展开
            if contrast != 1.0:
                r = 128 + (r - 128) * contrast
                g = 128 + (g - 128) * contrast
                b = 128 + (b - 128) * contrast
            r *= bright
            g *= bright
            b *= bright
            px[x, y] = (clamp(r), clamp(g), clamp(b), a)
    return im


def clamp(v):
    return 0 if v < 0 else (255 if v > 255 else int(v))


def rounded_mask(w, h, r):
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, w - 1, h - 1], radius=r, fill=255)
    return m


def apply_mask(img, mask):
    img.putalpha(mask)
    return img


def inner_glow(img, box, radius, color, width, blur):
    """内发光细线：先模糊一层宽的，再压一条细亮线。

    为什么不用普通描边：普通描边在深底上读起来是"硬边框"，
    而 HUD 需要的是"边缘发光的嵌槽"。模糊层负责辉光，细线负责锐度。
    """
    w, h = img.size
    halo = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(halo).rounded_rectangle(box, radius=radius,
                                           outline=color[:3] + (150,), width=width)
    halo = halo.filter(ImageFilter.GaussianBlur(blur))
    img.alpha_composite(halo)

    sharp = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(sharp).rounded_rectangle(box, radius=radius,
                                            outline=color[:3] + (255,),
                                            width=max(1, width // 2))
    img.alpha_composite(sharp)


def hard_line(img, box, radius, color, width):
    ImageDraw.Draw(img).rounded_rectangle(box, radius=radius, outline=color, width=width)


def corner_ticks(img, size, length, inset, color, width=3):
    """四角 L 角标 —— 本作 UI 的识别符（面板、槽位都用它）。"""
    d = ImageDraw.Draw(img)
    for (cx, cy, sx, sy) in ((inset, inset, 1, 1), (size - inset, inset, -1, 1),
                             (inset, size - inset, 1, -1),
                             (size - inset, size - inset, -1, -1)):
        d.line([(cx, cy), (cx + length * sx, cy)], fill=color, width=width)
        d.line([(cx, cy), (cx, cy + length * sy)], fill=color, width=width)


def scanline_shade(img, top=0.86, bottom=1.10):
    """上暗下亮的纵向明度带：给平面材质一点体积感，避免整块死平。"""
    w, h = img.size
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        k = top + (bottom - top) * t
        for x in range(w):
            r, g, b, a = px[x, y]
            px[x, y] = (clamp(r * k), clamp(g * k), clamp(b * k), a)


def cut_corners(img, pad, cut):
    """菱形切角：斜切四角（本作图标底座的形状语言）。返回切后的 RGBA。"""
    w, h = img.size
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    d.polygon([
        (pad + cut, pad), (w - pad - cut, pad),
        (w - pad, pad + cut), (w - pad, h - pad - cut),
        (w - pad - cut, h - pad), (pad + cut, h - pad),
        (pad, h - pad - cut), (pad, pad + cut),
    ], fill=255)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(img, (0, 0), mask)
    return out


def glyph(name, size, inset_ratio=0.0):
    """把预处理好的 glyph 缩放到目标尺寸。

    ⚠ 抠图和颜色反解已经在 preprocess_ui_assets.py 做完了，这里**不要再抠一遍**。
    重复抠图会二次衰减灰边的 alpha（把抗锯齿边缘再压一次），轮廓变细、发脆。
    这里只负责缩放：预处理产物已是「纯白 RGB + alpha 遮罩」的正确形态。
    """
    im = src(name).convert("RGBA")
    if im.size != (size, size):
        im = im.resize((size, size), Image.LANCZOS)
    if inset_ratio > 0:
        pad = int(size * inset_ratio)
        inner = im.resize((size - pad * 2, size - pad * 2), Image.LANCZOS)
        out = Image.new("RGBA", (size, size), (255, 255, 255, 0))
        out.paste(inner, (pad, pad), inner)
        return out
    return im


# ---------------- 各件素材 ----------------

def make_btn(name, accent, light=False):
    """按钮：AI 金属板 + 圆角 + 内发光边 + 左侧强调条。

    几何与 BorderFor 绑定：圆角必须 ≤ 上下边距(14)，否则下拉时圆角被切。
    取 12：既保住圆角观感，又让 108x40 的 BuyButton 还有 12px 可拉伸区。
    强调条起点 10 已在左右边距 16 之内。
    """
    w, h = 512, 160
    r = RADIUS["btn"]

    base = material("plate_light" if light else "plate_dark", w, h,
                    sat=0.0,
                    bright=1.06 if light else 0.96,
                    contrast=1.06 if light else 1.0)
    scanline_shade(base, 0.90 if not light else 0.98, 1.06)

    img = apply_mask(base, rounded_mask(w, h, r))

    # 外框：1px 暗边压住轮廓（贴图边缘在 9-slice 下最容易糊）
    hard_line(img, [1, 1, w - 2, h - 2], r, EDGE, 2)
    # 内发光边：内缩要在边距内，否则被拉成一条贯穿的亮线
    inner_glow(img, [5, 5, w - 6, h - 6], 9,
               CYAN_HI if light else accent, 2, 3)
    # 左侧强调条：可交互标记
    bar = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(bar).rounded_rectangle([10, 14, 18, h - 15], radius=4,
                                          fill=CYAN_HI if light else accent)
    img.alpha_composite(bar.filter(ImageFilter.GaussianBlur(0.5)))

    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_bar_bg(name):
    """条底槽：深色内凹圆槽 + 细边。

    ⚠ 这张图同时服务 14 / 26 / 30 高的横条和 16x320 的竖向威胁条。
    9-slice 的上下边距只有 6，所以圆角必须 ≤6 —— 取 5。
    如果照着贴图高度(96)画半径 48 的药丸，14 高的控件里根本放不下，
    Unity 会先把边距钳到 7 再把圆角拉成直角，结果就是两端发糊。
    竖向条靠 6px 边距切出的圆角近似直角，这在 16px 宽的细条上反而正确。
    """
    w, h = 512, 96
    r = RADIUS["bar"]

    base = material("plate_dark", w, h, sat=0.0, bright=0.72, contrast=1.05)
    img = apply_mask(base, rounded_mask(w, h, r))

    # 内凹：上半压暗，模拟光的入射方向（留 4px 边，别压到圆角上）
    shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(shade).rounded_rectangle([4, 4, w - 5, h // 2], radius=4,
                                            fill=(6, 8, 12, 110))
    img.alpha_composite(shade.filter(ImageFilter.GaussianBlur(3)))

    hard_line(img, [1, 1, w - 2, h - 2], r, EDGE, 2)
    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_bar_fill(name, color):
    """条填充：AI 能量条（中性灰白）→ 乘上稀有度/资源色相。

    ⚠ 用的是 Image.Type.Filled，不走 9-slice，但圆角仍要小：
    控件高度只有 8~24（XP 条仅 8px），半径按 96 画会让细条两端出现白色尖角。
    取 5，与底槽一致，两者叠放时边缘才对得齐。
    """
    w, h = 512, 96
    r = RADIUS["bar"]

    tex = material("energy", w, h, sat=0.0, bright=1.0, contrast=1.12)
    # 乘色：把中性灰白能量染成目标色，保留亮度起伏
    px = tex.load()
    tr, tg, tb = color[0] / 255.0, color[1] / 255.0, color[2] / 255.0
    for y in range(h):
        for x in range(w):
            r0, g0, b0, a0 = px[x, y]
            # 以亮度为权重上色，避免暗部被染成脏色
            lum = (0.299 * r0 + 0.587 * g0 + 0.114 * b0) / 255.0
            k = 0.35 + 0.85 * lum
            # 注意：这里不能写死 alpha=255，否则后面 putalpha 的圆角遮罩会被整块覆盖，
            # 结果圆角变成不透明黑角（曾整批条出现 100% 不透明）。
            px[x, y] = (clamp(255 * tr * k), clamp(255 * tg * k), clamp(255 * tb * k), a0)

    img = apply_mask(tex, rounded_mask(w, h, r))

    # 顶部高光：让条"有厚度"。必须留在圆角安全区(6px)内，否则下采样后会溢出
    hi = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(hi).rounded_rectangle([6, 3, w - 7, max(5, h // 3)],
                                         radius=3, fill=(255, 255, 255, 40))
    img.alpha_composite(hi.filter(ImageFilter.GaussianBlur(2)))

    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_slot(name, filled):
    """配件槽：方形凹槽 + 四角橙标。

    ⚠ 两种用法尺寸差很大：
      · slot_empty → ChainRow.Tail 60x84（大，能看全几何）
      · slot_filled → Menu.BadgeBack 20x20（小到只能当色块）
    边距 18 是按 60 宽倒推的；20x20 那张会被钳到接近 10，
    所以圆角和角标都必须画得足够小，缩小后才不会糊成一团。
    """
    s = 256
    r = RADIUS["slot_empty"] if not filled else RADIUS["slot_filled"]

    base = material("plate_light" if filled else "plate_dark", s, s,
                    sat=0.0,
                    bright=0.88 if filled else 0.62,
                    contrast=1.04)
    img = apply_mask(base, rounded_mask(s, s, r))

    hard_line(img, [2, 2, s - 3, s - 3], r, CYAN if filled else EDGE, 3)

    # 内圈细线：空槽淡、占用态更明显
    inner_glow(img, [10, 10, s - 11, s - 11], 9, CYAN, 2, 2)

    # 四角 L 角标：本作槽位的识别符。长度同步收到 20，避免小尺寸下粘连
    corner_ticks(img, s, 20, 12, AMBER, 3)

    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_rarity_tag(name, color):
    """稀有度标签条：左端方块 + 横条，底色淡染稀有度色。

    ⚠ 最小控件是 OfferCard.TagTop 272x16。边距 8 ⇒ 圆角必须 ≤8，取 6。
    左端方块的左边界要 >8（否则拉伸时被切一半），取 10。
    """
    w, h = 256, 64
    r = RADIUS["tag"]

    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    # 底：淡染（alpha 很低，避免抢文字）
    ImageDraw.Draw(img).rounded_rectangle([0, 0, w - 1, h - 1], radius=r,
                                          fill=color[:3] + (34,))
    hard_line(img, [0, 0, w - 1, h - 1], r, color[:3] + (170,), 2)
    # 左端方块：放在左边距(8)之外一点，避免被 9-slice 拉到中间
    ImageDraw.Draw(img).rounded_rectangle([10, h // 2 - 7, 24, h // 2 + 7],
                                          radius=3, fill=color)
    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_divider(name):
    """分隔线：两端淡出的青色细线。"""
    w, h = 256, 16
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    for x in range(w):
        t = abs(x - w / 2) / (w / 2)
        a = int(170 * (1 - t * t))
        for y in range(h):
            # 中线最亮，上下渐隐
            dy = abs(y - h / 2) / (h / 2)
            aa = int(a * max(0.0, 1.0 - dy * 1.6))
            px[x, y] = CYAN[:3] + (aa,)
    img = img.filter(ImageFilter.GaussianBlur(0.7))
    ImageDraw.Draw(img).line([(w // 4, h // 2), (w - w // 4, h // 2)],
                             fill=CYAN_HI[:3] + (210,), width=1)
    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_crosshair(name):
    """准星：四段等长刻度 + 中心留空 + 一个极小的中心点。

    为什么由本脚本画、不让 AI 出图（重要）：
      准星在屏幕上只有 16~130px，AI 画的"金属拉丝、倒角细节"在这个尺寸下
      一个像素都看不见，纯属浪费；而它真正依赖的三件事必须像素精确 ——
      **对称**、**中心留空的比例**、**臂长与间隙之比**。
      AI 每次给的臂长/间隙都不一样，缩放后要么糊成一团、要么中心空洞过大。

    尺寸取舍（这次踩的坑）：
      上一版素材内容只占画布 88%，控件基准 16px ⇒ 实际可见约 14px，
      四条臂几乎连在一起，看不出是准星。现在：
        · 画布基准放大到 256（控件最大 130，留足采样余量）
        · 内容占满画布（不留外圈空白），臂长与间隙按 128 基准的视觉比例重算
        · 中心间隙显式保证 >= 12% 画布，缩放后仍是清晰的"十字"而不是"十字块"

    几何（以 256 为基准，全部按比例，缩放后不变形）：
      臂长 78、间隙 18、线宽 10 ⇒ 整体跨度 2*(18+78) = 192，占画布 75%。
      留 25% 外圈空白是故意的：控件只会放大不会裁切，但留白能让
      preserveAspect 在非正方控件下不贴边。
    """
    s = 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = s // 2

    arm, gap, thick = 78, 18, 10

    # 四条臂：从中心间隙外沿向外延伸
    for (sx, sy) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        if sx:
            x0, y0 = c + gap * sx, c - thick // 2
            x1, y1 = c + (gap + arm) * sx, c + thick // 2
        else:
            x0, y0 = c - thick // 2, c + gap * sy
            x1, y1 = c + thick // 2, c + (gap + arm) * sy
        d.rectangle([min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)], fill=WHITE)

    # 中心点：2px 视觉锚点。不能省 —— 没有它瞄准时没有参照物
    d.rectangle([c - 2, c - 2, c + 1, c + 1], fill=WHITE)

    # 极轻的暗描边：亮背景（雪地/白墙）上白准星会"消失"，
    # 一圈半透明深色让它始终可读。只在外侧扩 1px，不改变白线粗细。
    halo = img.filter(ImageFilter.MaxFilter(3))
    dark = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    dark.paste((6, 9, 13, 150), (0, 0), halo.getchannel("A"))
    dark.alpha_composite(img)
    img = dark

    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_card_inner(name):
    """卡片内衬：暗色材质 + 圆角 + 内缩细边。

    它替换的是预制体里 `Pic("Inner", ..., 纯色)` 那块纯色底。
    纯色底的问题不是"难看"，而是**和卡片框的材质不是同一套**：
    框是 AI 金属、内部是 0.06/0.08/0.11 的死平色，两者贴在一起会露出接缝感。

    几何：内衬要缩进框内 4px，圆角必须 <= 框圆角(10) - 内缩(4) = 6，取 5。
    9-slice 用 8：内衬最小控件是 ItemCard 的 142×76（150×84 内缩 4），
    8+8=16 远小于该尺寸，中间区 126×60 足够拉伸。
    """
    w = h = 256
    r = 5

    base = material("card_inner", w, h, sat=0.0, bright=1.0, contrast=1.0)
    img = apply_mask(base, rounded_mask(w, h, r))

    # 极淡的内阴影压边：让内衬看起来是"凹进去"的，而不是浮在框上
    shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(shade).rounded_rectangle([0, 0, w - 1, h - 1], radius=r,
                                            outline=(0, 0, 0, 90), width=3)
    img.alpha_composite(shade.filter(ImageFilter.GaussianBlur(3)))

    # 1px 冷色细边：和框的描边同色系，衔接才不突兀
    hard_line(img, [1, 1, w - 2, h - 2], r, EDGE, 1)

    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_hit_marker(name):
    """命中标记：四段斜线（X 形），中点留空不遮敌。"""
    s = 128
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = s // 2
    L, gap, t = 28, 9, 4
    for sx, sy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        d.line([(c + gap * sx, c + gap * sy), (c + (gap + L) * sx, c + (gap + L) * sy)],
               fill=WHITE, width=t)
    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_vignette(name):
    """受击红晕：径向渐隐。α 上限 255，实际透明度由代码按帧压到 0.35。"""
    w, h = 1024, 576
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    cx, cy = w / 2, h / 2
    maxd = math.hypot(cx, cy)
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - cx, y - cy) / maxd
            a = 0 if d < 0.52 else int(255 * ((d - 0.52) / 0.48) ** 1.8)
            px[x, y] = (255, 46, 42, min(255, a))
    img.save(os.path.join(OUT, name + ".png"))
    return img


def make_icon(name, src_name, tint, size=128):
    """状态图标：AI glyph 取亮度当 alpha，再染成目标色。"""
    g = glyph(src_name, size)
    px = g.load()
    for y in range(size):
        for x in range(size):
            r, gg, b, a = px[x, y]
            if a == 0:
                continue
            # 保留 glyph 的抗锯齿 alpha，颜色换成 tint
            px[x, y] = (tint[0], tint[1], tint[2], a)
    g.save(os.path.join(OUT, name + ".png"))
    return g


def make_icon_frame(name):
    """图标底座：菱形切角框（形状语言），AI 金属做底。"""
    s = 256
    pad, cut = 18, 30

    base = material("plate_dark", s, s, sat=0.0, bright=0.70, contrast=1.05)
    img = cut_corners(base, pad, cut)

    d = ImageDraw.Draw(img)
    pts = [
        (pad + cut, pad), (s - pad - cut, pad),
        (s - pad, pad + cut), (s - pad, s - pad - cut),
        (s - pad - cut, s - pad), (pad + cut, s - pad),
        (pad, s - pad - cut), (pad, pad + cut),
    ]
    d.line(pts + [pts[0]], fill=CYAN, width=3, joint="curve")
    img.save(os.path.join(OUT, name + ".png"))
    return img


# ---------------- 配件圆形图标 ----------------

# 25 个配件的 id → (中文名, 字形源图)。与 sdata.json 一一对应，
# 缺任何一个，"所有配件都有图标"这条就不成立（自检会点名）。
ITEM_ICONS = [
    ("b_slug", "item_b_slug"), ("b_heavy", "item_b_heavy"), ("b_pellet", "item_b_pellet"),
    ("b_pierce", "item_b_pierce"), ("b_ricochet", "item_b_ricochet"),
    ("b_plasma", "item_b_plasma"),
    ("b_incend", "item_b_incend"), ("b_cryo", "item_b_cryo"), ("b_null", "item_b_null"),
    ("b_lance", "item_b_lance"),
    ("m_dmg_up", "item_m_dmg_up"), ("m_dmg_flat", "item_m_dmg_flat"),
    ("m_crit_up", "item_m_crit_up"), ("m_critdmg", "item_m_critdmg"),
    ("m_spread_down", "item_m_spread_down"),
    ("m_life_up", "item_m_life_up"), ("m_ammo_down", "item_m_ammo_down"),
    ("m_speed_up", "item_m_speed_up"),
    ("m_split", "item_m_split"), ("m_pierce", "item_m_pierce"),
    ("m_homing", "item_m_homing"), ("m_blast", "item_m_blast"),
    ("m_mag", "item_m_mag"), ("m_rate", "item_m_rate"), ("m_recoil", "item_m_recoil"),
]


def make_item_icon(name, glyph_src_name):
    """配件圆形图标：AI 线稿 glyph + 几何圆底板 + 双层圆环。

    为什么圆环必须由本脚本画（这次明确要"圆形图标"）：
      · 圆是唯一能在 28~64px 下保持识别度的底板形状，但它对**同心度**极其敏感 ——
        半径差 1px、圆心偏 1px，一排图标并排看就明显歪。
      · AI 每张都自己画一个圈，25 张必然是 25 个不同的半径与描边粗细。
      · 圆底板还要承载"层数"语义（外环暗、内环亮、中心偏上留出图标），这是几何，不是材质。

    几何（基准 256，缩放后比例不变）：
      外圆 r=122（留 6px 安全边，避免 Simple 模式下缩略图被采样到边缘）
      暗色金属底 → 内凹渐变 → 外环 2px 亮环 → 内环 1px 细环（r=104，形成"嵌槽"）
      glyph 占内圆 76%：留出呼吸空间，同时保证 28px 时还能看出形状。
    """
    s = 256
    cx = cy = s // 2
    r_out = 122
    r_in = 104

    # 底：AI 金属（去色），再压成圆形
    base = material("plate_dark", s, s, sat=0.0, bright=0.62, contrast=1.04)
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out], fill=255)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(base, (0, 0), mask)

    # 内凹：上半压暗 + 下半微亮，给圆形一点球面感（不是平面色块）
    shade = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(shade).ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out // 3],
                                  fill=(4, 6, 10, 120))
    shade = shade.filter(ImageFilter.GaussianBlur(10))
    shade.putalpha(Image.composite(shade.getchannel("A"), Image.new("L", (s, s), 0), mask))
    img.alpha_composite(shade)

    d = ImageDraw.Draw(img)

    # 外环：2px 冷色亮环，撑住轮廓（深色 HUD 上也要看得见）
    d.ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out], outline=EDGE, width=2)

    # 内环：1px 细环，形成"嵌槽"，让 glyph 看起来嵌在底板里而不是浮着
    d.ellipse([cx - r_in, cy - r_in, cx + r_in, cy + r_in], outline=(86, 96, 112, 200), width=1)

    # 顶部弧光：4 段短弧拼出上缘高光，只覆盖上 1/3，否则整圈发亮像发光体
    arc = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(arc).arc([cx - r_out + 3, cy - r_out + 3, cx + r_out - 3, cy + r_out - 3],
                            start=200, end=340, fill=CYAN_HI[:3] + (120,), width=2)
    img.alpha_composite(arc.filter(ImageFilter.GaussianBlur(0.6)))

    # glyph：从预处理产物取（已是「纯白 RGB + alpha」），染色为 bone-white
    g = glyph(glyph_src_name, 128)
    # 图标占内圆 76%（内圆直径 208 → glyph 边长约 158，这里以 128 为画布居中放置）
    gw = int(r_in * 2 * 0.76)
    g = g.resize((gw, gw), Image.LANCZOS)
    glyph_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    glyph_layer.paste(g, (cx - gw // 2, cy - gw // 2), g)

    # 染色：偏暖的白，与 HUD 文字一致；alpha 保留抗锯齿
    px = glyph_layer.load()
    for y in range(s):
        for x in range(s):
            r0, g0, b0, a0 = px[x, y]
            if a0 == 0:
                continue
            px[x, y] = (244, 246, 250, a0)

    img.alpha_composite(glyph_layer)
    img.save(os.path.join(OUT, name + ".png"))
    return img


# ---------------- 主流程 ----------------

GROUPS = {
    "btn": ["btn_primary", "btn_primary_hover", "btn_danger"],
    "bar": ["bar_bg", "bar_fill_hp", "bar_fill_xp", "bar_fill_boss", "bar_fill_threat"],
    "slot": ["slot_empty", "slot_filled"],
    "tag": ["tag_starter", "tag_common", "tag_rare", "tag_epic"],
    "icon": ["icon_frame", "icon_lock", "icon_check", "icon_arrow"],
    "misc": ["divider", "hit_marker", "vignette_damage", "crosshair"],
    "card": ["card_inner"],
    # 25 个配件圆形图标：每件一张，名字统一为 item_<id>
    "items": ["item_" + i for i, _ in ITEM_ICONS],
}


def build(name):
    if name == "btn_primary":
        return make_btn(name, CYAN, light=False)
    if name == "btn_primary_hover":
        return make_btn(name, CYAN_HI, light=True)
    if name == "btn_danger":
        return make_btn(name, RED, light=False)
    if name == "bar_bg":
        return make_bar_bg(name)
    if name == "bar_fill_hp":
        return make_bar_fill(name, RED)
    if name == "bar_fill_xp":
        return make_bar_fill(name, CYAN)
    if name == "bar_fill_boss":
        return make_bar_fill(name, (199, 107, 242))
    if name == "bar_fill_threat":
        return make_bar_fill(name, AMBER)
    if name == "slot_empty":
        return make_slot(name, filled=False)
    if name == "slot_filled":
        return make_slot(name, filled=True)
    if name == "tag_starter":
        return make_rarity_tag(name, (115, 122, 130))
    if name == "tag_common":
        return make_rarity_tag(name, (107, 142, 107))
    if name == "tag_rare":
        return make_rarity_tag(name, (90, 166, 240))
    if name == "tag_epic":
        return make_rarity_tag(name, (199, 107, 242))
    if name == "icon_frame":
        return make_icon_frame(name)
    if name == "icon_lock":
        return make_icon(name, "icon_lock", (150, 158, 172), 128)
    if name == "icon_check":
        return make_icon(name, "icon_check", (120, 220, 140), 128)
    if name == "icon_arrow":
        return make_icon(name, "icon_arrow", (120, 200, 250), 128)
    if name == "divider":
        return make_divider(name)
    if name == "hit_marker":
        return make_hit_marker(name)
    if name == "vignette_damage":
        return make_vignette(name)
    if name == "crosshair":
        return make_crosshair(name)
    if name == "card_inner":
        return make_card_inner(name)
    for item_id, src_name in ITEM_ICONS:
        if name == "item_" + item_id:
            return make_item_icon(name, src_name)
    raise KeyError(name)


def border_safe(img, name):
    """校验：9-slice 边距在**真实控件尺寸**下是否安全。

    判据用 MIN_RECT（prefab 里的最小控件），不用贴图尺寸。
    贴图 512x96 只能说明"画布够大"，控件只有 360x14 时边距照样吃光高度。
    """
    b = BORDERS.get(name)
    if not b:
        return True, "无需边距"
    l, bo, r, t = b
    mw, mh = MIN_RECT.get(name, (0, 0))

    # 边距是贴图像素，1:1 映射到 canvas 单位，所以直接和控件尺寸比
    if (l + r) >= mw or (bo + t) >= mh:
        return False, "边距(%d,%d) ≥ 最小控件 %gx%g，圆角会被压扁" % (l + r, bo + t, mw, mh)

    slack_w, slack_h = mw - (l + r), mh - (bo + t)
    if slack_w < 8 or slack_h < 8:
        return False, "最小控件 %gx%g 的可拉伸区仅 %gx%g" % (mw, mh, slack_w, slack_h)

    return True, "边距=%s 最小控件=%gx%g 可拉伸区=%gx%g" % (b, mw, mh, slack_w, slack_h)


def coverage(img):
    a = img.getchannel("A")
    hist = a.histogram()
    return sum(hist[16:]) / max(1, img.width * img.height)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="逗号分隔的类别：btn,bar,slot,tag,icon,misc")
    ap.add_argument("--list", action="store_true")
    args = ap.parse_args()

    if args.list:
        for k, v in GROUPS.items():
            print("%-6s %s" % (k, ", ".join(v)))
        return 0

    if not os.path.isdir(SRC):
        print("缺少 AI 材质目录：%s" % SRC, file=sys.stderr)
        return 2

    os.makedirs(OUT, exist_ok=True)

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

    ok = warn = 0
    for name in want:
        try:
            img = build(name)
            cov = coverage(img)
            safe, info = border_safe(img, name)
            flag = "OK"
            if cov < 0.01:
                flag = "WARN 几乎是空图"
                warn += 1
            elif not safe:
                flag = "WARN 边距不安全"
                warn += 1
            elif cov > 0.999 and name.startswith(("bar_", "slot_", "tag_", "btn_")):
                # 条/槽/标签/按钮本来就是整块填充的矩形，全不透明是正确形态。
                # 只有图标、晕影、分隔线这类"挖空"素材才应该出现透明区。
                pass
            print("  %-20s %4dx%-4d cover=%5.1f%%  %-28s %s"
                  % (name, img.width, img.height, cov * 100, info, flag))
            ok += 1
        except Exception as e:
            print("  %-20s FAIL %s: %s" % (name, type(e).__name__, e))
            warn += 1

    print("\nRESULT %d/%d 已生成（AI 材质合成），警告=%d" % (ok, len(want), warn))
    print("OUT " + OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""把 S 工程的 UI 素材条目追加进 tools/apimart-assets.json（幂等）。"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)
# 枢纽根：工程在 <HUB>/Project/<Game>/unity，从工程根向上三级即 <HUB>。
# 不写死盘符 —— 换机后盘符会变；需要覆盖时仍可用 APIMART_MANIFEST。
HUB = os.path.abspath(os.path.join(PROJ, "..", "..", ".."))
MANIFEST = os.environ.get(
    "APIMART_MANIFEST",
    os.path.join(HUB, "tools", "apimart-assets.json"),
)
# APIMart 原图落在 _Inbox/ui-ai-raw（未处理），预处理后的产物才进 _Inbox/ui-ai-src。
# 两个目录必须分开：混在一起会让人分不清"这张到底抠过图没有"。
SRC = os.path.join(PROJ, "_Inbox", "ui-ai-raw")

STYLE = ("Cold industrial sci-fi first-person-shooter UI art. Hard edges, matte surfaces, "
         "very low saturation, restrained contrast, no gradients across the whole frame, "
         "no vignetting, no text, no logos, no watermark, no UI widgets, no frame, no border.")

ASSETS = [
    {
        "id": "s-ui-plate-dark",
        "out": SRC + r"\plate_dark.png",
        "size": "1:1",
        "resolution": "2k",
        "prompt": (
            "Seamless tileable texture of DARK BRUSHED GUNMETAL PLATE used as the background of a "
            "military HUD panel. Base tone near-black blue-grey, roughly #151A21 to #2A313B. "
            "Fine horizontal brushed grain running across the frame, faint machining marks, "
            "a few hairline micro-scratches, occasional tiny pitting and cold-weld specks, "
            "sparse very subtle darker smudges. MATTE, no specular hotspots, no reflections. "
            "Completely flat even lighting across the whole frame. "
            "The whole frame is one continuous sheet of metal: no border, no rounded corners, "
            "no bevel edge, no highlight rim, no vignette, no shadow, no object, no screws, "
            "no rivets, no text, no numbers, no watermark, no logo. "
            "Perfectly tileable, uniform detail density, sharp focus, top-down flat 2D texture map."
        ),
    },
    {
        "id": "s-ui-plate-light",
        "out": SRC + r"\plate_light.png",
        "size": "1:1",
        "resolution": "2k",
        "prompt": (
            "Seamless tileable texture of LIGHTER BRUSHED GUNMETAL PLATE, the raised hover state of a "
            "dark mechanical HUD button. Base tone mid-grey blue, roughly #3A424E to #4A5464, "
            "clearly brighter than a near-black plate but still desaturated and cold. "
            "Fine horizontal brushed grain, subtle machining striations, a few hairline scratches, "
            "sparse faint fingerprint smudges. SATIN finish, low sheen, no mirror reflection, "
            "no bright specular streak. Completely flat even lighting across the whole frame. "
            "The whole frame is one continuous sheet of metal: no border, no rounded corners, "
            "no bevel edge, no highlight rim, no vignette, no shadow, no object, no screws, "
            "no rivets, no text, no numbers, no watermark, no logo. "
            "Perfectly tileable, uniform detail density, sharp focus, top-down flat 2D texture map."
        ),
    },
    {
        "id": "s-ui-energy",
        "out": SRC + r"\energy.png",
        "size": "1:1",
        "resolution": "2k",
        "prompt": (
            "Seamless horizontal texture for the FILL of a sci-fi HUD progress bar. "
            "A desaturated near-white energy sheen: bright neutral core running horizontally through "
            "the vertical middle third of the frame, smoothly falling off to darker grey toward the "
            "top and bottom edges. Fine horizontal energy filaments and thin parallel light streaks "
            "along the core, a couple of slightly brighter pulse bands. "
            "Colour is strictly NEUTRAL grey-white (it will be tinted in engine), no hue. "
            "Completely flat even lighting, no vignette, no glow bloom outside the frame, "
            "no border, no rounded corners, no text, no numbers, no arrows, no watermark, no logo. "
            "Perfectly tileable horizontally, sharp focus, flat 2D texture map."
        ),
    },
    {
        "id": "s-ui-icon-lock",
        "out": SRC + r"\icon_lock.png",
        "size": "1:1",
        "resolution": "1k",
        "prompt": (
            "Single simple UI GLYPH of a PADLOCK, locked state, drawn as flat white line art. "
            "Shackle arch over a rectangular body with a keyhole. Thick even stroke weight, "
            "hard geometric edges, minimal detail, no perspective, no shading, no gradient, "
            "no glow, no outline, no colour. Centred, occupying about 70 percent of the frame, "
            "generous empty margin around it. Pure solid black background, nothing else in frame. "
            "Pure white glyph on pure black, high contrast, crisp anti-aliased edges, "
            "no text, no watermark, no logo."
        ),
    },
    {
        "id": "s-ui-icon-check",
        "out": SRC + r"\icon_check.png",
        "size": "1:1",
        "resolution": "1k",
        "prompt": (
            "Single simple UI GLYPH of a CHECKMARK, drawn as flat white line art. "
            "A bold angular tick: short down-stroke then long up-stroke, mitred ends, "
            "thick even stroke weight, hard geometric edges, no perspective, no shading, "
            "no gradient, no glow, no outline, no colour. Centred, occupying about 65 percent "
            "of the frame, generous empty margin around it. Pure solid black background. "
            "Pure white glyph on pure black, high contrast, crisp anti-aliased edges, "
            "no text, no watermark, no logo."
        ),
    },
    {
        "id": "s-ui-icon-arrow",
        "out": SRC + r"\icon_arrow.png",
        "size": "1:1",
        "resolution": "1k",
        "prompt": (
            "Single simple UI GLYPH of a RIGHT-POINTING CHEVRON ARROW, drawn as flat white line art. "
            "Two thick straight strokes meeting at a sharp apex on the right, mitred ends, "
            "hard geometric edges, no perspective, no shading, no gradient, no glow, no outline, "
            "no colour. Centred, occupying about 60 percent of the frame, generous empty margin. "
            "Pure solid black background. Pure white glyph on pure black, high contrast, "
            "crisp anti-aliased edges, no text, no watermark, no logo."
        ),
    },
]


def main():
    with open(MANIFEST, "r", encoding="utf-8") as f:
        m = json.load(f)

    have = {a["id"] for a in m["assets"]}
    added = 0
    for a in ASSETS:
        if a["id"] in have:
            # 幂等：更新已有条目
            for i, old in enumerate(m["assets"]):
                if old["id"] == a["id"]:
                    m["assets"][i] = a
            print("update  " + a["id"])
        else:
            a2 = dict(a)
            a2["prompt"] = a2["prompt"] + " " + STYLE
            m["assets"].append(a2)
            added += 1
            print("add     " + a["id"])

    os.makedirs(SRC, exist_ok=True)
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(m, f, ensure_ascii=False, indent=2)

    print("added=%d, total assets=%d" % (added, len(m["assets"])))


if __name__ == "__main__":
    main()

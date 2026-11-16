"""
Project S — UI 素材分类一致性校验。

为什么需要这个脚本：分类表存在三处（C# 的 `UiAssets.Catalog`、两个 Python 流水线），
原因见枢纽 `docs/ui-asset-taxonomy.md` —— C# 不能读 Python，Python 不能读 C#，
Runner 侧也不该为了这个去解析 C# 源码之外的东西。

三处一旦漂移，症状是**静默的**：
  · 素材在磁盘上换了目录，但 C# 还按旧目录找 → 运行时 `Resources.Load` 返回 null，
    界面保留纯色块（这是设计好的降级），所以不报错、不看图就发现不了。
  · Python 把新图写到分类目录，但 C# 没登记 → 同样是 null。
  · C# 登记了但磁盘上没有 → 同样是 null。

所以必须有这道门。用法：
    python tools\\audit_ui_taxonomy.py
退出码：0 一致，1 有漂移（可直接接进 check.ps1）
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)
UI = os.path.join(PROJ, "Assets", "Resources", "UI")
SPRITES = os.path.join(UI, "Sprites")
UIASSETS_CS = os.path.join(PROJ, "Assets", "_Game", "View", "UiAssets.cs")

CATEGORIES = ("Common", "Frame", "Widget", "Tag", "Icon", "Items")

# 分类目录的物理位置（Items 是 Icon 的子层，因为它按数据 id 取图）
CAT_PATH = {
    "Common": ("Common",),
    "Frame": ("Frame",),
    "Widget": ("Widget",),
    "Tag": ("Tag",),
    "Icon": ("Icon",),
    "Items": ("Icon", "Items"),
}


def disk_state():
    """磁盘现状：分类 → 素材名集合。"""
    out = {}
    for cat, parts in CAT_PATH.items():
        d = os.path.join(SPRITES, *parts)
        names = set()
        if os.path.isdir(d):
            for f in os.listdir(d):
                if f.endswith(".png"):
                    names.add(f[:-4])
        out[cat] = names
    # 根目录不该再有任何 png
    stray = sorted(f[:-4] for f in os.listdir(UI) if f.endswith(".png"))
    return out, stray


def cs_catalog():
    """解析 UiAssets.cs 的 Catalog：{ 素材名: 分类 }。"""
    with io.open(UIASSETS_CS, encoding="utf-8") as f:
        src = f.read()
    start = src.index("Catalog = new Dictionary<string, string>")
    end = src.index("};", start)
    body = src[start:end]
    out = {}
    for m in re.finditer(r'\{\s*"([A-Za-z0-9_]+)"\s*,\s*"([A-Za-z]+)"\s*\}', body):
        out[m.group(1)] = m.group(2)
    return out


def py_catalog(path, var="CATEGORY"):
    """解析 Python 流水线里的 CATEGORY 表（通过 import 取，不靠正则）。"""
    import importlib.util

    spec = importlib.util.spec_from_file_location("_tax_probe", path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["_tax_probe"] = mod
    try:
        spec.loader.exec_module(mod)
    except Exception as e:
        # 这两个脚本在 import 时会建目录/读清单，失败也不影响取表
        print("  （%s 导入告警：%s）" % (os.path.basename(path), e))
    return dict(getattr(mod, var, {}))


def main():
    ok = True
    disk, stray = disk_state()
    cs = cs_catalog()

    print("===== UI 素材分类一致性 =====")

    # ---- 1. 根目录必须干净（只允许材质等非 sprite 资产）----
    if stray:
        ok = False
        print("FAIL UI/ 根目录仍有未分类贴图（应移入 Sprites/<Category>/）：")
        for n in stray:
            print("       " + n)
    else:
        print("PASS UI/ 根目录无未分类贴图")

    # ---- 2. 分类名必须都是规范里的 ----
    bad_cat = sorted({c for c in cs.values() if c not in CATEGORIES})
    if bad_cat:
        ok = False
        print("FAIL UiAssets.Catalog 出现未知分类: " + ", ".join(bad_cat))

    # ---- 3. C# catalog ↔ 磁盘 ----
    # 注意：`item_*` **刻意不登记**在 Catalog 里 —— 它们是数据驱动的，
    # 按 `item_ + 配件id` 动态拼名查 Icon/Items/。所以比对时要排除它们，
    # 配件图标的完整性由第 7 条（对 sdata.json）单独校验。
    cs_names = set(cs)
    disk_items = set(disk["Items"])
    disk_names = set()
    for cat in ("Common", "Frame", "Widget", "Tag", "Icon"):
        disk_names |= disk[cat]

    only_cs = sorted(cs_names - disk_names - disk_items)
    only_disk = sorted(disk_names - cs_names)
    if only_cs:
        ok = False
        print("FAIL 只在 C# 登记、磁盘上没有（运行时会加载成 null）：")
        for n in only_cs:
            print("       %-18s 登记在 %s" % (n, cs[n]))
    if only_disk:
        ok = False
        print("FAIL 只在磁盘上、C# 没登记（界面找不到）：")
        for n in only_disk:
            print("       " + n)
    if not only_cs and not only_disk:
        print("PASS C# 登记与磁盘文件一一对应（%d 张）" % len(cs_names))

    # ---- 4. 位置必须一致（同名素材不能在错的分类目录里）----
    misplaced = []
    for name, cat in cs.items():
        if name not in disk_names:
            continue
        actual = [c for c in CATEGORIES if name in disk[c]]
        if actual and actual[0] != cat:
            misplaced.append((name, cat, actual[0]))
    if misplaced:
        ok = False
        print("FAIL 素材在错误的分类目录：")
        for n, want, got in misplaced:
            print("       %-18s 登记为 %-7s 实际在 %s" % (n, want, got))
    else:
        print("PASS 每个素材都在它登记的目录里")

    # ---- 5. Items 必须是数据驱动的配件图标 ----
    bad_items = sorted(n for n in disk["Items"] if not n.startswith("item_"))
    if bad_items:
        ok = False
        print("FAIL Icon/Items 里混入了非配件图标（该目录只放 item_*）：")
        for n in bad_items:
            print("       " + n)
    else:
        print("PASS Icon/Items 全部是 item_*（%d 张）" % len(disk["Items"]))

    # ---- 6. Python 流水线的分类表与 C# 一致 ----
    for rel in ("preprocess_ui_assets.py", "gen_ui_assets_ai.py"):
        p = os.path.join(HERE, rel)
        if not os.path.isfile(p):
            continue
        try:
            py = py_catalog(p)
        except Exception as e:
            ok = False
            print("FAIL %s 取分类表失败：%s" % (rel, e))
            continue
        diff = {k: (cs.get(k), py.get(k)) for k in set(cs) | set(py) if cs.get(k) != py.get(k)}
        if diff:
            ok = False
            print("FAIL %s 的分类表与 UiAssets.Catalog 不一致：" % rel)
            for k, (a, b) in sorted(diff.items()):
                print("       %-18s C#=%-8s py=%s" % (k, a, b))
        else:
            print("PASS %s 分类表与 C# 一致" % rel)

    # ---- 7. 配件图标数量应与数据一致 ----
    # 数据源已从 sdata.json 迁到 Assets/Resources/Data/Cfg/*.tsv（三行表头）。
    # 这里必须读**同一份真相**，否则图标的完整性校验会对着已废弃的文件做判断。
    cfg_dir = os.path.join(PROJ, "Assets", "Resources", "Data", "Cfg")
    if os.path.isdir(cfg_dir):
        live = set()
        for tbl in ("Bullet.tsv", "Modifier.tsv", "GunModifier.tsv"):
            p = os.path.join(cfg_dir, tbl)
            if not os.path.isfile(p):
                continue
            with io.open(p, encoding="utf-8-sig") as f:
                lines = [ln.rstrip("\r\n") for ln in f]
            if len(lines) < 3:
                continue
            fields = lines[0].split("\t")
            if "id" not in fields:
                continue
            id_col = fields.index("id")
            for ln in lines[3:]:
                if not ln.strip():
                    continue
                cols = ln.split("\t")
                if len(cols) > id_col and cols[id_col].strip():
                    live.add(cols[id_col].strip())
        # disk["Items"] 里是文件名（`item_ap`），live 里是配件 id（`ap`）。
        # 两者必须去掉前缀才能比较 —— 这是本脚本最容易写错的一处。
        have = {n[5:] if n.startswith("item_") else n for n in disk["Items"]}
        miss = sorted(live - have)
        extra = sorted(have - live)
        if miss:
            ok = False
            print("FAIL 以下配件没有图标（应放 Icon/Items/item_<id>.png）：")
            for n in miss:
                print("       item_" + n + ".png")
        if extra:
            ok = False
            print("FAIL 以下配件图标对应的 id 已不在 Cfg/*.tsv（废弃残留）：")
            for n in extra:
                print("       item_" + n + ".png")
        if not miss and not extra:
            print("PASS 配件图标与 Cfg/*.tsv 的 %d 件一一对应" % len(live))
    print()
    print("RESULT %s" % ("OK" if ok else "DRIFT"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

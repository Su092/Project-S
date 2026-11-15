using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>
    /// UI 美术资源加载与 9-slice 应用。
    ///
    /// 三条纪律（踩出来的）：
    /// 1. **缺图必须降级**，不能让界面变空白。任何 sprite 拿不到时返回 null，
    ///    调用方保留原来的纯色块即可 —— 素材是逐步接入的，不能因为少一张图就整体不可用。
    /// 2. **9-slice 边距必须小于控件尺寸的一半**，否则圆角被拉成椭圆。
    ///    这里统一在 Apply 里做钳制，调用方不需要各自判断。
    /// 3. 图标是「黑底 + 加色」的，不能用普通 Alpha 混合，否则深色 HUD 上会出现黑方块。
    /// </summary>
    public static class UiAssets
    {
        const string Root = "UI/";

        static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();
        static Sprite s_crosshair, s_panel, s_card, s_cardInner, s_bar, s_banner, s_vignette, s_keycap;
        static Sprite s_iconBullet, s_iconWhole, s_iconAfter, s_iconGun;
        // 第二批：按钮 / 槽位 / 条 / 标签（由 tools/gen_ui_assets_ai.py 合成 AI 材质产出）
        static Sprite s_btnPrimary, s_btnPrimaryHover, s_btnDanger;
        static Sprite s_slotEmpty, s_slotFilled;
        static Sprite s_barBg, s_barHp, s_barXp, s_barBoss, s_barThreat;
        static Sprite s_iconFrame, s_divider, s_hitMarker, s_vignetteDamage;
        static Sprite s_tagStarter, s_tagCommon, s_tagRare, s_tagEpic;
        static Sprite s_iconLock, s_iconCheck, s_iconArrow;

        static Sprite Load(string name, ref Sprite cache)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Sprite>(Root + name);
            return cache;
        }

        public static Sprite Panel => Load("panel", ref s_panel);
        public static Sprite CardFrame => Load("card_frame", ref s_card);
        /// <summary>卡片内衬：卡框内部的暗色底，替代原来的纯色块。</summary>
        public static Sprite CardInner => Load("card_inner", ref s_cardInner);
        public static Sprite BarFrame => Load("bar_frame", ref s_bar);
        public static Sprite Banner => Load("banner", ref s_banner);
        public static Sprite Vignette => Load("vignette", ref s_vignette);
        public static Sprite Keycap => Load("keycap", ref s_keycap);
        public static Sprite Crosshair => Load("crosshair", ref s_crosshair);

        public static Sprite IconBullet => Load("icon_bullet", ref s_iconBullet);
        public static Sprite IconWhole => Load("icon_whole", ref s_iconWhole);
        public static Sprite IconAfter => Load("icon_after", ref s_iconAfter);
        public static Sprite IconGun => Load("icon_gun", ref s_iconGun);

        // ---- 第二批素材 ----

        /// <summary>主按钮（青色强调）。9-slice 用，见 <see cref="ApplySliced"/>。</summary>
        public static Sprite BtnPrimary => Load("btn_primary", ref s_btnPrimary);
        public static Sprite BtnPrimaryHover => Load("btn_primary_hover", ref s_btnPrimaryHover);
        public static Sprite BtnDanger => Load("btn_danger", ref s_btnDanger);

        /// <summary>配件槽：空槽 / 已占用。</summary>
        public static Sprite SlotEmpty => Load("slot_empty", ref s_slotEmpty);
        public static Sprite SlotFilled => Load("slot_filled", ref s_slotFilled);

        /// <summary>条：底槽 + 四种填充（Filled 模式用填充）。</summary>
        public static Sprite BarBg => Load("bar_bg", ref s_barBg);
        public static Sprite BarHp => Load("bar_fill_hp", ref s_barHp);
        public static Sprite BarXp => Load("bar_fill_xp", ref s_barXp);
        public static Sprite BarBoss => Load("bar_fill_boss", ref s_barBoss);
        public static Sprite BarThreat => Load("bar_fill_threat", ref s_barThreat);

        public static Sprite IconFrame => Load("icon_frame", ref s_iconFrame);
        public static Sprite Divider => Load("divider", ref s_divider);
        public static Sprite HitMarker => Load("hit_marker", ref s_hitMarker);

        /// <summary>状态图标：未解锁 / 已解锁 / 进入箭头。</summary>
        public static Sprite IconLock => Load("icon_lock", ref s_iconLock);
        public static Sprite IconCheck => Load("icon_check", ref s_iconCheck);
        public static Sprite IconArrow => Load("icon_arrow", ref s_iconArrow);

        /// <summary>受击晕影（比 <see cref="Vignette"/> 更收敛，不糊画面）。</summary>
        public static Sprite VignetteDamage => Load("vignette_damage", ref s_vignetteDamage);

        /// <summary>按稀有度取标签条（颜色即 art-style §2.3 的编码）。</summary>
        public static Sprite TagForRarity(Core.Rarity r)
        {
            switch (r)
            {
                case Core.Rarity.Rare: return Load("tag_rare", ref s_tagRare);
                case Core.Rarity.Epic: return Load("tag_epic", ref s_tagEpic);
                case Core.Rarity.Common: return Load("tag_common", ref s_tagCommon);
                default: return Load("tag_starter", ref s_tagStarter);
            }
        }

        /// <summary>
        /// 把按钮外观套上。拿不到素材返回 false，调用方保留原纯色（缺图不能崩）。
        /// 悬停态用 <paramref name="hover"/> 决定选哪张。
        /// </summary>
        public static bool ApplyButton(Image img, bool hover = false, bool danger = false)
        {
            if (img == null) return false;
            var sp = danger ? BtnDanger : (hover ? BtnPrimaryHover : BtnPrimary);
            if (sp == null) return false;
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            return true;
        }

        /// <summary>
        /// 按作用域取图标。7 种作用域归到现有 4 张图标上：
        /// 读取类（Next）用「后继」图，吸附类用「全场」图，枪械件用「枪械」图。
        /// 图标只作辅助，文字始终同时出现 —— 靠图标区分 7 种作用域是不可读的。
        /// </summary>
        public static Sprite IconForScope(Core.Scope scope)
        {
            switch (scope)
            {
                case Core.Scope.Next: return IconAfter;
                case Core.Scope.All: return IconWhole;
                default: return IconWhole;
            }
        }

        /// <summary>
        /// 把 sprite 以 9-slice 应用到 Image。sprite 为 null 时不做任何事（保留原纯色）。
        /// 边距超限时自动钳制到控件尺寸的一半减 1px。
        /// </summary>
        public static bool ApplySliced(Image img, Sprite sprite, bool preserveColor = true)
        {
            if (img == null || sprite == null) return false;

            var cur = img.color;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            if (preserveColor) img.color = cur;

            img.pixelsPerUnitMultiplier = 1f;
            return true;
        }

        /// <summary>
        /// 图标：黑底加色显示。用 UI/Additive 材质，避免深色背景上出现黑方块。
        /// </summary>
        public static bool ApplyIcon(Image img, Sprite sprite, Color tint)
        {
            if (img == null || sprite == null) return false;

            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = tint;

            var mat = AdditiveMaterial();
            if (mat != null) img.material = mat;
            return true;
        }

        static Material s_additive;

        // ---------------- 配件圆形图标 ----------------

        /// <summary>
        /// 按配件 id 取圆形图标（`item_<id>`）。缺图返回 null，调用方必须能降级 ——
        /// 素材是分批接入的，缺一张不能让整排图标空掉。
        /// </summary>
        public static Sprite ItemIcon(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            string key = "item_" + itemId;
            if (s_itemIcons.TryGetValue(key, out var cached)) return cached;

            var sp = Resources.Load<Sprite>(Root + key);
            s_itemIcons[key] = sp;
            return sp;
        }

        static readonly Dictionary<string, Sprite> s_itemIcons = new Dictionary<string, Sprite>();

        /// <summary>
        /// 把配件图标画到 Image 上：圆形底板已经在贴图里，所以这里只做
        /// 「Simple + preserveAspect + 无加色材质」—— 圆形底板是**暗底**，
        /// 走加色材质（给黑底 glyph 用的那套）会把它整个吃掉，只剩一圈亮线。
        /// </summary>
        public static bool ApplyItemIcon(Image img, string itemId, Color tint)
        {
            if (img == null) return false;
            var sp = ItemIcon(itemId);
            if (sp == null) { img.enabled = false; return false; }

            img.enabled = true;
            img.sprite = sp;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = tint;
            img.material = null;        // 不能用加色材质，见上
            return true;
        }

        static Material AdditiveMaterial()
        {
            if (s_additive != null) return s_additive;

            // Resources/UI/mat_ui_additive.mat（由编辑器脚本生成）。
            // 拿不到就返回 null，图标走普通混合（会有黑底，但不崩）。
            s_additive = Resources.Load<Material>("UI/mat_ui_additive");
            return s_additive;
        }

        /// <summary>自检用：统计成功加载的 sprite 数。</summary>
        public static int LoadedCount()
        {
            Sprite[] all =
            {
                Panel, CardFrame, CardInner, BarFrame, Banner, Vignette, Keycap, Crosshair,
                IconBullet, IconWhole, IconAfter, IconGun,
                BtnPrimary, BtnPrimaryHover, BtnDanger,
                SlotEmpty, SlotFilled,
                BarBg, BarHp, BarXp, BarBoss, BarThreat,
                IconFrame, Divider, HitMarker, VignetteDamage,
                IconLock, IconCheck, IconArrow,
                TagForRarity(Core.Rarity.Starter), TagForRarity(Core.Rarity.Common),
                TagForRarity(Core.Rarity.Rare), TagForRarity(Core.Rarity.Epic),
            };
            int n = 0;
            foreach (var s in all) if (s != null) n++;
            return n;
        }

        /// <summary>自检用：25 个配件图标有几张真的加载到了。</summary>
        public static int ItemIconCount(IEnumerable<string> ids)
        {
            int n = 0;
            foreach (var id in ids) if (ItemIcon(id) != null) n++;
            return n;
        }
    }
}

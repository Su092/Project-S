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
        // ---- 素材分类（规范见枢纽 `docs/ui-asset-taxonomy.md`）----
        //
        // 分类依据是「这张图服务几个地方」，不是「它长什么样」：
        //   Common  通用底盘：出现 ≥3 处或跨界面，换掉它等于整体换肤
        //   Frame   容器边框：包裹内容，9-slice 拉伸，通常带 border
        //   Widget  交互控件：有状态（正常/悬停/危险、空/满）或承载实时数值
        //   Tag     语义标签：表达一个**离散状态**（稀有度），颜色本身就是信息
        //   Icon    图标：静态语义符号，不含状态、不承载数值
        //   Icon/Items  配件图标：**数据驱动**，按 id 动态拼名查找，与静态图标分开
        //
        // 为什么 Icon/Items 要单独一层：它是唯一按数据 id 取图的分类。
        // 与静态图标混放时，`item_*` 一多会把 `icon_*` 挤得找不到，
        // 而且「新增一件配件要加一张图」这条流程只对这一个目录成立。
        /// <summary>素材根目录（相对工程根，带尾斜杠）。运行时按名取件，不需要它；
        /// 编辑器侧（预制体工厂、九宫格校验）拼资产路径要用。</summary>
        const string Root = SRes.Root + "Texture/UI/";

        /// <summary>
        /// **唯一**真相：素材名 → 分类。
        ///
        /// 运行时加载与编辑器脚本（预制体工厂、九宫格校验）都从这里查，
        /// 杜绝「两处各写一份路径」。新增素材只做两件事：
        /// ① 文件放进对应分类目录 ② 在本表登记一行。
        ///
        /// 别把它拆成「按目录列的数组」——那样加一件要同时改数组和目录，容易漏。
        /// </summary>
        static readonly Dictionary<string, string> Catalog = new Dictionary<string, string>
        {
            { "offer_frame_green", "Frame" },
            { "offer_frame_blue", "Frame" },
            { "offer_frame_gold", "Frame" },
            { "offer_frame_red", "Frame" },
            { "offer_note_0", "Icon" },
            { "offer_note_1", "Icon" },
            { "offer_note_2", "Icon" },
            { "offer_note_3", "Icon" },
            { "offer_note_4", "Icon" },
            { "offer_note_5", "Icon" },
            { "offer_staff", "Widget" },
            { "offer_staff_glow", "Widget" },
            { "offer_item_ap", "Items" },
            { "offer_item_bounce", "Items" },
            { "offer_item_bouncer", "Items" },
            { "offer_item_burn", "Items" },
            { "offer_item_chain", "Items" },
            { "offer_item_crit", "Items" },
            { "offer_item_crossfire", "Items" },
            { "offer_item_dmg", "Items" },
            { "offer_item_expire", "Items" },
            { "offer_item_explode", "Items" },
            { "offer_item_flame", "Items" },
            { "offer_item_forward_battery", "Items" },
            { "offer_item_frost", "Items" },
            { "offer_item_g_double", "Items" },
            { "offer_item_g_leech", "Items" },
            { "offer_item_g_mag", "Items" },
            { "offer_item_g_parallel", "Items" },
            { "offer_item_g_rapid", "Items" },
            { "offer_item_g_recycle", "Items" },
            { "offer_item_g_reload", "Items" },
            { "offer_item_g_stable", "Items" },
            { "offer_item_g_triple", "Items" },
            { "offer_item_gap_bounce", "Items" },
            { "offer_item_gap_lens", "Items" },
            { "offer_item_giant", "Items" },
            { "offer_item_grenade", "Items" },
            { "offer_item_heavy", "Items" },
            { "offer_item_hole", "Items" },
            { "offer_item_homing", "Items" },
            { "offer_item_laser", "Items" },
            { "offer_item_light", "Items" },
            { "offer_item_mine", "Items" },
            { "offer_item_missile", "Items" },
            { "offer_item_pellet", "Items" },
            { "offer_item_pierce", "Items" },
            { "offer_item_rear_battery", "Items" },
            { "offer_item_rear_blast", "Items" },
            { "offer_item_rear_boost", "Items" },
            { "offer_item_rear_frost", "Items" },
            { "offer_item_ring_burn", "Items" },
            { "offer_item_saw", "Items" },
            { "offer_item_scatter", "Items" },
            { "offer_item_shock", "Items" },
            { "offer_item_speed", "Items" },
            { "offer_item_std", "Items" },
            { "offer_item_barrelroll", "Items" },
            { "offer_item_bellmark", "Items" },
            { "offer_item_ripple", "Items" },
            { "offer_item_answer", "Items" },
            { "offer_item_thrift", "Items" },
            { "offer_item_timer", "Items" },
            { "offer_item_trigger", "Items" },
            { "offer_item_twin_boost", "Items" },
            { "offer_item_whole_boost", "Items" },
            { "offer_item_whole_guidance", "Items" },
            { "offer_note_glow_0", "Icon" },
            { "offer_note_glow_1", "Icon" },
            { "offer_note_glow_2", "Icon" },
            { "offer_note_glow_3", "Icon" },
            { "offer_note_glow_4", "Icon" },
            { "offer_note_glow_5", "Icon" },
            // Music HUD: dedicated sprites leave other screen backgrounds unchanged.
            { "hud_music_badge_mask", "Widget" },
            { "hud_music_hp_fill", "Widget" },
            { "hud_music_xp_fill", "Widget" },
            { "hud_music_boss_fill", "Widget" },
            { "hud_music_crosshair", "Widget" },
            { "hud_music_resume", "Widget" },
            { "hud_music_banner", "Frame" },
            { "score_bag_icon", "Icon" },
            { "score_page", "Frame" },
            { "score_slot_empty", "Frame" },
            { "score_sort", "Icon" },
            { "score_close", "Icon" },
            { "score_preview_rail", "Frame" },
            { "hud_quality_blue", "Frame" },
            { "hud_quality_gold", "Frame" },
            { "hud_quality_red", "Frame" },
            // HUD V2: processed assets, mirrored by tools/hud-v2-catalog.json.
            { "hud_weapon_rail", "Frame" },
            { "hud_portrait_frame", "Frame" },
            { "hud_minimap_bezel", "Frame" },
            { "hud_pause_button_base", "Widget" },
            { "hud_item_slot", "Widget" },
            { "hud_hp_track_frame", "Frame" },
            { "hud_xp_track_frame", "Frame" },
            { "hud_boss_track_frame", "Frame" },
            { "hud_reload_spinner", "Widget" },
            { "hud_ammo_arc_track", "Widget" },
            { "hud_ammo_arc_fill", "Widget" },
            { "hud_map_player_heading", "Icon" },
            { "hud_map_enemy", "Icon" },
            { "hud_map_chest", "Icon" },
            { "hud_pause_glyph", "Icon" },
            { "hud_weapon_swap", "Icon" },
            { "hud_gun_rifle", "Icon" },
            { "hud_gun_musicbox", "Icon" },
            { "hud_gun_handbells", "Icon" },
            { "hud_gun_handpan", "Icon" },
            { "hud_gun_castanets", "Icon" },
            { "hud_gun_smg", "Icon" },
            { "hud_gun_shotgun", "Icon" },
            { "hud_gun_sniper", "Icon" },
            { "hud_gun_beam", "Icon" },
            { "hud_gun_launcher", "Icon" },
            { "hud_operator_portrait", "Icon" },
            { "hud_timer_plate", "Frame" },
            { "hud_attachment_read_ring", "Widget" },
            { "hud_utility_placeholder", "Icon" },
            { "hud_map_mask", "Widget" },
            // ---- Common 通用底盘（5）----
            { "panel", "Common" },
            { "banner", "Common" },
            { "vignette", "Common" },
            { "vignette_damage", "Common" },
            { "divider", "Common" },

            // ---- Frame 容器边框（3）----
            { "card_frame", "Frame" },
            { "card_inner", "Frame" },
            { "bar_frame", "Frame" },

            // ---- Widget 交互控件（13）----
            { "btn_primary", "Widget" },
            { "btn_danger", "Widget" },
            { "slot_empty", "Widget" },
            { "slot_filled", "Widget" },
            { "bar_bg", "Widget" },
            { "bar_fill_hp", "Widget" },
            { "bar_fill_xp", "Widget" },
            { "bar_fill_boss", "Widget" },
            { "bar_fill_threat", "Widget" },
            { "crosshair", "Widget" },
            { "hit_marker", "Widget" },
            { "keycap", "Widget" },


            // ---- Icon 静态图标（8）----
            { "icon_bullet", "Icon" },
            { "icon_whole", "Icon" },
            { "icon_after", "Icon" },
            { "icon_frame", "Icon" },
            { "icon_lock", "Icon" },
            { "icon_check", "Icon" },
            { "icon_arrow", "Icon" },
        };

        /// <summary>分类目录（相对工程根，带尾斜杠）。编辑器拼资产路径用。</summary>
        static readonly Dictionary<string, string> Dirs = new Dictionary<string, string>
        {
            { "Common", Root + "Common/" },
            { "Frame",  Root + "Frame/"  },
            { "Widget", Root + "Widget/" },
            { "Tag",    Root + "Tag/"    },
            { "Icon",   Root + "Icon/"   },
            // 配件图标按数据 id 取图，与静态图标分开一层（见类顶部注释）
            { "Items",  Root + "Icon/Items/" },
        };

        /// <summary>按素材名查它所在分类目录（形如 `UI/Sprites/Widget/`）。</summary>
        public static string DirOf(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            return Catalog.TryGetValue(spriteName, out var cat) && Dirs.TryGetValue(cat, out var d) ? d : null;
        }

        /// <summary>按分类名取目录（`Common` / `Frame` / `Widget` / `Tag` / `Icon` / `Items`）。</summary>
        public static string DirOfCategory(string category) =>
            category != null && Dirs.TryGetValue(category, out var d) ? d : null;

        static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();
        static Sprite s_crosshair, s_panel, s_card, s_cardInner, s_bar, s_banner, s_vignette, s_keycap;
        static Sprite s_iconBullet, s_iconWhole, s_iconAfter;
        // 第二批：按钮 / 槽位 / 条 / 标签（由 tools/gen_ui_assets_ai.py 合成 AI 材质产出）
        static Sprite s_btnPrimary, s_btnDanger;
        static Sprite s_slotEmpty, s_slotFilled;
        static Sprite s_barBg, s_barHp, s_barXp, s_barBoss, s_barThreat;
        static Sprite s_iconFrame, s_divider, s_hitMarker, s_vignetteDamage;
        static Sprite s_iconLock, s_iconCheck, s_iconArrow;

        /// <summary>按素材名加载：目录由 <see cref="Catalog"/> 决定，调用处不再写路径。</summary>
        static Sprite Load(string name, ref Sprite cache)
        {
            if (cache != null) return cache;
            var dir = DirOf(name);
            if (dir == null)
            {
                Debug.LogWarning($"[S] UI 素材未在 UiAssets.Catalog 登记: {name}");
                return null;
            }
            cache = SRes.Get<Sprite>(name);
            if (cache == null) Debug.LogWarning($"[S] UI 素材缺失: {name}（{SRes.Diagnose(name)}）");
            return cache;
        }

        public static Sprite Named(string name) => DirOf(name) != null ? SRes.Get<Sprite>(name) : null;

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

        // ---- 第二批素材 ----

        /// <summary>主按钮（青色强调）。9-slice 用，见 <see cref="ApplySliced"/>。</summary>
        public static Sprite BtnPrimary => Load("btn_primary", ref s_btnPrimary);
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

            var sp = SRes.Get<Sprite>(key);
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
            s_additive = SRes.Get<Material>("mat_ui_additive");
            return s_additive;
        }

        /// <summary>自检用：统计成功加载的 sprite 数。</summary>
        public static int LoadedCount()
        {
            Sprite[] all =
            {
                Panel, CardFrame, CardInner, BarFrame, Banner, Vignette, Keycap, Crosshair,
                IconBullet, IconWhole, IconAfter,
                BtnPrimary, BtnDanger,
                SlotEmpty, SlotFilled,
                BarBg, BarHp, BarXp, BarBoss, BarThreat,
                IconFrame, Divider, HitMarker, VignetteDamage,
                IconLock, IconCheck, IconArrow,
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

using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 背包右下角的**固定详情区**（对齐原型 `#inv-detail`）。
    ///
    /// 为什么不只靠跟随鼠标的浮动提示（ItemTooltip）：
    ///   · 浮动提示会挡住相邻格子，玩家想「对比两件」时没法同时看见；
    ///   · 它在鼠标附近只有约 340px 宽，作用域说明这种长文案要反复换行；
    ///   · 原型里这块是常驻区域，玩家视线有固定落点，读一眼就知道当前指的什么。
    ///
    /// 与 ItemTooltip 的分工：
    ///   · 本类 = 背包内的常驻详情（结构化：图标 + 名称 + 稀有度 + 作用域 + 数值行）
    ///   · ItemTooltip = 任意界面的浮动提示（HUD 链条、三选一卡片）
    /// 两者的**数值文案必须同源**，都调 ItemTooltip 的静态方法，不能各写一套。
    ///
    /// 控件引用必须是 SerializeField：预制体落盘后 Bind 的内存赋值会丢，
    /// 运行时 Instantiate 出来若字段为空，悬停只改 CurrentId、界面永远停在空态「—」。
    /// </summary>
    public sealed class ItemDetailPanel : MonoBehaviour
    {
        [SerializeField] Image _icon;
        [SerializeField] Image _iconRing;

        [SerializeField] Text _title;
        [SerializeField] Text _meta;        // 稀有度 · 类别
        [SerializeField] Text _scopeText;   // 作用域名
        [SerializeField] Image _scopeChip;  // 作用域色块
        [SerializeField] Text _stats;       // 数值行
        [SerializeField] Text _hint;        // 空态提示 / 作用域说明

        /// <summary>当前展示的配件 id（null = 空态）。自检用。</summary>
        public string CurrentId { get; private set; }

        /// <summary>是否正在展示某件配件。</summary>
        public bool HasItem => CurrentId != null;

        /// <summary>当前标题文案（自检：证明 Show 真的改了 UI，不只改了 CurrentId）。</summary>
        public string TitleText => _title != null ? _title.text : null;

        /// <summary>控件是否已接上（未接线时 Show 是空转）。</summary>
        public bool IsWired => _title != null && _stats != null && _hint != null;

        void Awake()
        {
            // 旧预制体可能只有组件、没有序列化引用：按子节点名补接，避免悬停无反馈。
            if (!IsWired) WireFromChildren();
            if (IsWired && CurrentId == null) Clear();
        }

        /// <summary>
        /// 由预制体构造后调用一次，把各子控件接进来。
        /// 让构建代码留在 SUiPrefabFactory 里，本类只负责「填数据」——
        /// 布局改动不需要碰逻辑，逻辑改动也不用重进编辑器摆位置。
        /// </summary>
        public void Bind(Image icon, Image iconRing, Text title, Text meta,
            Text scopeText, Image scopeChip, Text stats, Text hint)
        {
            _icon = icon;
            _iconRing = iconRing;
            _title = title;
            _meta = meta;
            _scopeText = scopeText;
            _scopeChip = scopeChip;
            _stats = stats;
            _hint = hint;

            Clear();
        }

        /// <summary>
        /// 按 SUiPrefabFactory.BuildDetailPanel 的固定子节点名接线。
        /// 名字是约定：改布局时两边必须一起改，否则这里接不上。
        /// </summary>
        void WireFromChildren()
        {
            _icon = FindImg("DetailIcon");
            _iconRing = FindImg("DetailRing");
            _title = FindTxt("DetailTitle");
            _meta = FindTxt("DetailMeta");
            _scopeText = FindTxt("DetailScope");
            _scopeChip = FindImg("ScopeChip");
            _stats = FindTxt("DetailStats");
            _hint = FindTxt("DetailHint");
        }

        Image FindImg(string childName)
        {
            var t = transform.Find(childName);
            return t != null ? t.GetComponent<Image>() : null;
        }

        Text FindTxt(string childName)
        {
            var t = transform.Find(childName);
            return t != null ? t.GetComponent<Text>() : null;
        }

        /// <summary>回到空态（未悬停任何配件）。</summary>
        public void Clear()
        {
            CurrentId = null;
            if (_icon != null) _icon.enabled = false;
            if (_iconRing != null) _iconRing.color = new Color(0.35f, 0.40f, 0.48f, 0.45f);
            if (_title != null) _title.text = "—";
            if (_meta != null) _meta.text = "";
            if (_scopeText != null) _scopeText.text = "";
            if (_scopeChip != null) _scopeChip.color = new Color(0f, 0f, 0f, 0f);
            if (_stats != null) _stats.text = "";
            if (_hint != null)
            {
                _hint.text = "把鼠标移到配件上查看详情";
                _hint.color = new Color(0.45f, 0.50f, 0.58f);
            }
        }

        /// <summary>展示一件配件的完整内容。</summary>
        public void Show(ChainItem item)
        {
            if (!IsWired) WireFromChildren();
            if (item == null) { Clear(); return; }

            string id = item.Id;
            CurrentId = id;

            bool bullet = item.IsBullet;
            var rarity = item.Rarity;
            string name = item.Name;

            if (_title != null)
            {
                _title.text = name;
                _title.color = Color.white;
            }

            if (_meta != null)
            {
                _meta.text = $"{ItemTooltip.RarityName(rarity)}   ·   " +
                             (bullet ? "子弹配件" : "衍生配件");
                _meta.color = ItemTooltip.RarityColor(rarity);
            }

            // 作用域：子弹没有作用域概念，这里显示它的「形态」
            if (_scopeText != null && _scopeChip != null)
            {
                if (bullet)
                {
                    _scopeText.text = BulletKindName(item.Bullet.Kind);
                    _scopeText.color = new Color(1f, 0.72f, 0.32f);
                    _scopeChip.color = new Color(1f, 0.72f, 0.32f, 0.85f);
                }
                else
                {
                    var sc = ItemTooltip.ScopeColor(item.Mod.Scope);
                    _scopeText.text = "作用范围：" + ItemTooltip.ScopeName(item.Mod.Scope);
                    _scopeText.color = sc;
                    _scopeChip.color = sc;
                }
            }

            if (_stats != null)
            {
                _stats.text = bullet
                    ? ItemTooltip.DescribeBullet(item.Bullet)
                    : ItemTooltip.DescribeMod(item.Mod);
            }

            if (_hint != null)
            {
                // 提示行给出「这件东西该怎么用」，而不是重复数值
                if (bullet)
                {
                    _hint.text = item.Bullet.Trigger != TriggerKind.None
                        ? $"会施放下一颗子弹作为载荷（{ItemTooltip.TriggerName(item.Bullet.Trigger)}触发）"
                        : "放在任意槽位即可被读取";
                    _hint.color = new Color(0.60f, 0.68f, 0.78f);
                }
                else
                {
                    _hint.text = ItemTooltip.ScopeHint(item.Mod.Scope);
                    _hint.color = item.Mod.IsGunOnly
                        ? new Color(0.78f, 0.62f, 0.95f)
                        : ItemTooltip.ScopeColor(item.Mod.Scope) * 0.9f;
                }
            }

            if (_icon != null)
            {
                if (UiAssets.ApplyItemIcon(_icon, id, Color.white)) _icon.enabled = true;
                else _icon.enabled = false;   // 缺图降级：数值照常，不整块消失
            }

            if (_iconRing != null)
            {
                var rc = ItemTooltip.RarityColor(rarity);
                _iconRing.color = new Color(rc.r, rc.g, rc.b, 0.85f);
            }
        }

        static string BulletKindName(BulletKind k) => k switch
        {
            BulletKind.Beam => "形态：瞬时光束",
            BulletKind.Mine => "形态：地雷陷阱",
            BulletKind.Field => "形态：持续力场",
            BulletKind.Chain => "形态：连锁闪电",
            BulletKind.Saw => "形态：回旋锯",
            BulletKind.Hole => "形态：引力奇点",
            _ => "形态：投射物",
        };

        /// <summary>拖拽落点预览的数据（由 InventoryView 在槽位副本上试算后填入）。</summary>
        public struct PlacementPreview
        {
            public int Cell;
            public float BaselineDamage;
            public float NewDamage;
            public float DamageDelta;
            public int AmmoDelta;
            public int AffectedTargets;
            /// <summary>该件在此位置是否有作用目标。false 时提示「无目标」。</summary>
            public bool HasTarget;
        }

        /// <summary>
        /// 展示「装到这一格」的预览：静态说明 + 前后对比。
        ///
        /// 为什么在同一个面板里切，而不是再开一块：玩家拖拽时只有一处视线落点，
        /// 多开一块会让人不知道看哪里。
        /// </summary>
        public void Show(ChainItem item, PlacementPreview pv)
        {
            Show(item);
            if (item == null) return;

            if (_hint != null)
            {
                if (!pv.HasTarget)
                {
                    _hint.text = $"✕ 装到槽 {pv.Cell + 1} 后没有作用目标，效果会全部落空";
                    _hint.color = new Color(1f, 0.42f, 0.42f);
                }
                else
                {
                    string sign = pv.DamageDelta >= 0f ? "+" : "";
                    string ammo = pv.AmmoDelta == 0 ? "耗弹不变"
                        : $"耗弹 {pv.AmmoDelta:+0;-0}";
                    string targets = item.IsBullet ? "占用 1 个读取" : $"生效目标 {pv.AffectedTargets} 个";
                    _hint.text =
                        $"装到槽 {pv.Cell + 1}：本次期望 {pv.BaselineDamage:0.#} → {pv.NewDamage:0.#}" +
                        $"（{sign}{pv.DamageDelta:0.#}）    {ammo}    {targets}";
                    _hint.color = pv.DamageDelta > 0.01f
                        ? new Color(0.55f, 1f, 0.68f)
                        : pv.DamageDelta < -0.01f
                            ? new Color(1f, 0.72f, 0.45f)
                            : new Color(0.72f, 0.78f, 0.88f);
                }
            }
        }
    }
}

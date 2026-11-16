using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 配件悬浮详情。悬停在配件上时显示名称、稀有度、作用域与完整数值。
    ///
    /// 定位：
    ///   · ShowUnder → 钉在指定控件正下方（背包/槽位悬停）
    ///   · Show → 跟随鼠标（HUD / 三选一 / 拖拽落点预览）
    ///
    /// 为什么挂在同一套 Canvas 上、只用 overrideSorting：
    /// 以前另起一个带 CanvasScaler 的嵌套 Overlay，坐标被缩放出错，
    /// 浮窗会飘到屏幕中央；文字区参数符号也曾写反，矩形倒过来 → 有底板无字。
    /// </summary>
    public sealed class ItemTooltip : MonoBehaviour
    {
        static ItemTooltip s_instance;

        RectTransform _layer;   // 铺满父 Canvas，作为定位参考
        RectTransform _root;    // 实际浮窗面板
        Image _back;
        Image _icon;
        Image _iconRing;
        Text _title;
        Text _rarity;
        Text _scope;
        Text _body;
        Text _footer;

        string _currentId;
        bool _suppressed;
        RectTransform _anchor;
        string _footerOverride;

        const float Width = 340f;
        const float Height = 220f;
        const float UnderGap = 10f;

        public static ItemTooltip Instance => s_instance;

        public bool Visible => _root != null && _root.gameObject.activeSelf;
        public string CurrentId => _currentId;
        public string TitleText => _title != null ? _title.text : null;

        public static ItemTooltip Ensure(Transform parent)
        {
            if (s_instance != null) return s_instance;

            // 挂在已有 Canvas 下，只 override 排序 —— 不要再套一层 CanvasScaler
            var go = new GameObject("ItemTooltip", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var layer = (RectTransform)go.transform;
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 90;
            go.AddComponent<GraphicRaycaster>();

            var tip = go.AddComponent<ItemTooltip>();
            tip._layer = layer;
            tip.Build();
            s_instance = tip;
            return tip;
        }

        void Build()
        {
            _root = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(_layer, false);
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.sizeDelta = new Vector2(Width, Height);

            _back = _root.gameObject.AddComponent<Image>();
            var panel = UiAssets.Panel;
            if (panel != null)
            {
                _back.sprite = panel;
                _back.type = Image.Type.Sliced;
                _back.pixelsPerUnitMultiplier = 1f;
                _back.color = new Color(1f, 1f, 1f, 0.98f);
            }
            else _back.color = new Color(0.06f, 0.08f, 0.11f, 0.97f);
            _back.raycastTarget = false;

            _iconRing = Pic("IconRing", new Vector2(14f, -14f), new Vector2(56f, 56f));
            _iconRing.sprite = UiAssets.IconFrame;
            _iconRing.color = new Color(0.55f, 0.62f, 0.72f, 0.55f);
            _iconRing.preserveAspect = true;

            _icon = Pic("Icon", new Vector2(18f, -18f), new Vector2(48f, 48f));

            // 正数边距：left/right/top/height 都是「向内」的像素，不再传负数
            _title = Txt("Title", top: 14f, left: 78f, right: 14f, height: 26f,
                20, TextAnchor.MiddleLeft, Color.white);
            _rarity = Txt("Rarity", top: 40f, left: 78f, right: 14f, height: 20f,
                13, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.9f));
            _scope = Txt("Scope", top: 60f, left: 78f, right: 14f, height: 20f,
                13, TextAnchor.MiddleLeft, new Color(0.7f, 0.78f, 0.9f));
            _body = Txt("Body", top: 88f, left: 14f, right: 14f, height: 88f,
                14, TextAnchor.UpperLeft, new Color(0.88f, 0.92f, 1f));
            _footer = Txt("Footer", top: 182f, left: 14f, right: 14f, height: 30f,
                12, TextAnchor.UpperLeft, new Color(0.55f, 0.62f, 0.72f));

            _root.gameObject.SetActive(false);
        }

        Image Pic(string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        /// <summary>
        /// top/left/right/height 全部是相对面板的正数内边距/高度。
        /// offsetMin = (left, -(top+height))；offsetMax = (-right, -top)。
        /// </summary>
        Text Txt(string name, float top, float left, float right, float height,
            int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -(top + height));
            rt.offsetMax = new Vector2(-right, -top);

            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public void Suppress(bool on)
        {
            _suppressed = on;
            if (on) Hide();
        }

        public void ShowUnder(ChainItem item, RectTransform under, string footerOverride = null)
        {
            _anchor = under;
            _footerOverride = footerOverride;
            Apply(item);
        }

        public void Show(ChainItem item, string footerOverride = null)
        {
            _anchor = null;
            _footerOverride = footerOverride;
            Apply(item);
        }

        int _currentLevel;
        void Apply(ChainItem item)
        {
            if (item == null || _suppressed) { Hide(); return; }

            // 每次显示都重绑字体：Build 时若字体尚未导入完成，会静默变成空白字
            EnsureFonts();

            string id = item.IsBullet ? item.Bullet.Id : item.Mod.Id;
            bool same = id == _currentId && _currentLevel == item.Level && Visible;
            _currentLevel = item.Level;
            _currentId = id;

            if (!same)
            {
                bool bullet = item.IsBullet;
                Rarity rar = bullet ? item.Bullet.Rarity : item.Mod.Rarity;
                string name = item.Name;

                _title.text = name;
                _rarity.text = "Lv." + item.Level + "   ·   " + RarityName(rar) + (bullet ? "   ·   子弹配件" : "   ·   衍生配件");
                _rarity.color = RarityColor(rar);

                if (bullet)
                {
                    _scope.text = BulletKindName(item.Bullet.Kind);
                    _scope.color = new Color(1f, 0.72f, 0.32f);
                    _body.text = DescribeBullet(item.EffectiveBullet);
                }
                else
                {
                    _scope.text = "作用范围：" + ScopeName(item.Mod.Scope);
                    _scope.color = ScopeColor(item.Mod.Scope);
                    _body.text = DescribeMod(item.EffectiveMod);
                }

                _icon.enabled = true;
                if (!UiAssets.ApplyItemIcon(_icon, id, Color.white))
                    _icon.enabled = false;

                var ringCol = RarityColor(rar);
                if (_iconRing != null)
                    _iconRing.color = new Color(ringCol.r, ringCol.g, ringCol.b, 0.85f);
            }

            _footer.text = !string.IsNullOrEmpty(_footerOverride)
                ? _footerOverride
                : (item.IsBullet
                    ? "放在任意槽位即可被读取"
                    : ScopeHint(item.Mod.Scope));
            if (string.IsNullOrEmpty(_footerOverride))
                _footer.text += item.Level < ItemFusion.MaxLevel ? "\n同种同级拖到一起合成升级（最高 Lv.5）" : "\n已满级 Lv.5";

            _root.gameObject.SetActive(true);
            Place();
        }

        void EnsureFonts()
        {
            var font = UiKit.Font;
            if (_title != null) _title.font = font;
            if (_rarity != null) _rarity.font = font;
            if (_scope != null) _scope.font = font;
            if (_body != null) _body.font = font;
            if (_footer != null) _footer.font = font;
        }

        public void Hide()
        {
            _currentId = null;
            _anchor = null;
            _footerOverride = null;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (Visible) Place();
        }

        void Place()
        {
            if (_anchor != null) PlaceUnderAnchor();
            else FollowMouse();
        }

        /// <summary>
        /// 用同一 Canvas 空间的 InverseTransformPoint 定位，避免嵌套 Overlay 的屏幕换算偏差。
        /// </summary>
        void PlaceUnderAnchor()
        {
            if (_layer == null || _anchor == null || !_anchor.gameObject.activeInHierarchy)
            {
                FollowMouse();
                return;
            }

            var corners = new Vector3[4];
            _anchor.GetWorldCorners(corners);
            Vector3 localBottom = _layer.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
            Vector3 localTop = _layer.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);

            _root.pivot = new Vector2(0.5f, 1f);
            float x = localBottom.x;
            float y = localBottom.y - UnderGap;

            var bounds = _layer.rect;
            // 下方放不下 → 翻到配件上方（枢轴在顶边，上方时 y = 顶部 + 高度 + 间距）
            if (y - Height < bounds.yMin + 8f)
                y = localTop.y + UnderGap + Height;

            x = Mathf.Clamp(x, bounds.xMin + Width * 0.5f + 8f, bounds.xMax - Width * 0.5f - 8f);
            y = Mathf.Clamp(y, bounds.yMin + Height + 8f, bounds.yMax - 8f);
            _root.anchoredPosition = new Vector2(x, y);
        }

        void FollowMouse()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || _layer == null) return;

            Vector2 screen = mouse.position.ReadValue();
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out local))
                return;

            _root.pivot = new Vector2(0f, 1f);
            float x = local.x + 18f;
            float y = local.y - 14f;

            var bounds = _layer.rect;
            if (x + Width > bounds.xMax - 8f) x = local.x - Width - 18f;
            if (y - Height < bounds.yMin + 8f) y = local.y + Height + 14f;

            _root.anchoredPosition = new Vector2(
                Mathf.Clamp(x, bounds.xMin + 8f, bounds.xMax - Width - 8f),
                Mathf.Clamp(y, bounds.yMin + Height + 8f, bounds.yMax - 8f));
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

        // ---------------- 文案 ----------------

        public static string RarityName(Rarity r) => r switch
        {
            Rarity.Epic => "史诗",
            Rarity.Rare => "稀有",
            Rarity.Starter => "起始",
            _ => "常见",
        };

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.Epic => new Color(0.78f, 0.42f, 0.95f),
            Rarity.Rare => new Color(0.35f, 0.65f, 0.95f),
            Rarity.Starter => new Color(0.45f, 0.48f, 0.52f),
            _ => new Color(0.42f, 0.52f, 0.42f),
        };

        public static string ScopeName(Scope s) => s switch
        {
            Scope.Next => "后一格",
            Scope.Prev => "前一格",
            Scope.Adj => "相邻",
            Scope.N2 => "后2格",
            Scope.N3 => "后3格",
            Scope.P2 => "前2格",
            Scope.P3 => "前3格",
            Scope.Adj2 => "前后各2格",
            Scope.Skip => "隔一格",
            _ => "全枪",
        };

        public static Color ScopeColor(Scope s) => s switch
        {
            Scope.Next => new Color(0.31f, 0.76f, 1f),
            Scope.Prev => new Color(0.24f, 0.96f, 0.78f),
            Scope.Adj => new Color(0.36f, 1f, 0.55f),
            Scope.N2 => new Color(0.85f, 1f, 0.36f),
            Scope.N3 => new Color(0.85f, 1f, 0.36f),
            Scope.P2 => new Color(1f, 0.54f, 0.85f),
            Scope.P3 => new Color(1f, 0.42f, 0.65f),
            Scope.Adj2 => new Color(0.3f, 1f, 0.85f),
            Scope.Skip => new Color(1f, 0.72f, 0.3f),
            _ => new Color(0.78f, 0.49f, 1f),
        };

        public static string ScopeHint(Scope s) => s switch
        {
            Scope.Next => "仅作用于槽位编号 +1 的子弹；不跨空格、不环绕",
            Scope.Prev => "仅作用于槽位编号 -1 的子弹；不跨空格、不环绕",
            Scope.Adj => "被动：作用于左右各一格内的子弹",
            Scope.N2 => "被动：作用于后 2 格内的子弹",
            Scope.N3 => "被动：作用于后 3 格内的子弹",
            Scope.P2 => "被动：作用于前 2 格内的子弹",
            Scope.P3 => "被动：作用于前 3 格内的子弹，不环绕",
            Scope.Adj2 => "被动：作用于前后各 2 格内的子弹，不环绕",
            Scope.Skip => "只作用于编号 −2、+2 的子弹，跳过紧邻格，不环绕",
            _ => "被动：作用于整把枪，不占读取次数",
        };

        public static string DescribeBullet(BulletDef b)
        {
            var lines = new System.Collections.Generic.List<string>
            {
                $"伤害 <b>{b.Damage:0.#}</b>    耗弹 {b.Cost}    数量 {b.Count}",
                $"速度 {b.Speed:0}    存在 {b.Lifetime:0.##}s    暴击 {b.Crit * 100f:0}% × {b.CritMul:0.#}",
            };

            if (b.Spread > 0f) lines.Add($"散射 {b.Spread:0.#}°");

            var feat = new System.Collections.Generic.List<string>();
            AddKind(feat, b.Kind);
            if (b.Pierce > 0) feat.Add($"穿透 {b.Pierce}");
            if (b.Bounce > 0) feat.Add($"反弹 {b.Bounce}");
            if (b.Homing > 0f) feat.Add("追踪");
            if (b.Gravity > 0f) feat.Add("抛射");
            if (b.ExplodeR > 0f) feat.Add($"爆炸 {b.ExplodeR:0.#}m / {b.ExplodeDmg:0} 伤");
            if (b.Burn > 0f) feat.Add($"燃烧 {b.Burn:0}/s");
            if (b.Slow > 0f) feat.Add($"减速 {b.Slow * 100f:0}%");
            if (b.Radius > 0f) feat.Add($"范围 {b.Radius:0.#}m");
            if (b.Duration > 0f) feat.Add($"持续 {b.Duration:0}s");
            if (b.ChainCount > 0) feat.Add($"连锁 {b.ChainCount} 次");
            if (b.Trigger != TriggerKind.None)
                feat.Add($"<b>{TriggerName(b.Trigger)}触发</b>：施放下一颗子弹");
            if (feat.Count > 0) lines.Add(string.Join("    ", feat));

            return string.Join("\n", lines);
        }

        static void AddKind(System.Collections.Generic.List<string> feat, BulletKind k)
        {
            switch (k)
            {
                case BulletKind.Beam: feat.Add("瞬时命中"); break;
                case BulletKind.Mine: feat.Add("地雷"); break;
                case BulletKind.Field: feat.Add("力场"); break;
                case BulletKind.Chain: feat.Add("连锁"); break;
                case BulletKind.Saw: feat.Add("回旋"); break;
                case BulletKind.Hole: feat.Add("奇点"); break;
            }
        }

        public static string TriggerName(TriggerKind t) => t switch
        {
            TriggerKind.Hit => "命中",
            TriggerKind.Timer => "定时",
            TriggerKind.Expire => "消散",
            _ => "",
        };

        public static string DescribeMod(ModifierDef m)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (m.DamageMulAdd != 0f) parts.Add($"伤害 {(m.DamageMulAdd > 0 ? "+" : "")}{m.DamageMulAdd * 100f:0}%");
            if (m.SizeMulAdd != 0f) parts.Add($"体积 {(m.SizeMulAdd > 0 ? "+" : "")}{m.SizeMulAdd * 100f:0}%");
            if (m.SpeedMulAdd != 0f) parts.Add($"速度 {(m.SpeedMulAdd > 0 ? "+" : "")}{m.SpeedMulAdd * 100f:0}%");
            if (m.RangeMulAdd != 0f) parts.Add($"射程 +{m.RangeMulAdd * 100f:0}%");
            if (m.LifeMulAdd != 0f) parts.Add($"存在 +{m.LifeMulAdd * 100f:0}%");
            if (m.KnockMulAdd != 0f) parts.Add($"击退 +{m.KnockMulAdd * 100f:0}%");
            if (m.CountMulAdd != 0f) parts.Add($"数量 ×{1f + m.CountMulAdd:0.##}");
            if (m.RadiusMulAdd != 0f) parts.Add($"范围 +{m.RadiusMulAdd * 100f:0}%");
            if (m.PiercePer != 0) parts.Add($"穿透 +{m.PiercePer}");
            if (m.BouncePer != 0) parts.Add($"反弹 +{m.BouncePer}");
            if (m.HomingPer != 0f) parts.Add("追踪");
            if (m.BurnPer != 0f) parts.Add($"燃烧 +{m.BurnPer:0}/s");
            if (m.SlowPer != 0f) parts.Add($"减速 {m.SlowPer * 100f:0}%");
            if (m.CritPer != 0f) parts.Add($"暴击 +{m.CritPer * 100f:0}%");
            if (m.SpreadPer != 0f) parts.Add($"散射 +{m.SpreadPer:0.#}°");
            if (m.ExplodeRBase != 0f) parts.Add($"命中爆炸 {m.ExplodeRBase:0.#}m");
            if (m.ExplodeRAdd != 0f) parts.Add($"爆炸范围 +{m.ExplodeRAdd * 100f:0}%");
            if (m.AmmoReduce != 0) parts.Add($"耗弹 −{m.AmmoReduce}");

            if (m.GunMagMul != 1f) parts.Add($"弹匣 ×{m.GunMagMul:0.##}");
            if (m.GunReloadMul != 1f) parts.Add($"换弹 ×{m.GunReloadMul:0.##}");
            if (m.GunDelayMul != 1f) parts.Add($"射速 ×{1f / m.GunDelayMul:0.##}");
            if (m.GunSpreadMul != 1f) parts.Add($"枪散射 ×{m.GunSpreadMul:0.##}");
            if (m.GunRecycleAdd != 0f) parts.Add($"免耗弹 {m.GunRecycleAdd * 100f:0}%");
            if (m.GunCastAdd != 0) parts.Add($"读取 +{m.GunCastAdd}");
            if (m.GunLeech != 0f) parts.Add($"击杀回血 {m.GunLeech:0}");
            if (m.Multicast != 0) parts.Add($"额外读取 {m.Multicast}");

            string note = "\n" + ScopeHint(m.Scope);
            return string.Join("    ", parts) + note;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }
    }
}

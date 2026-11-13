using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 配件悬浮详情。悬停在任意配件图标上时显示名称、稀有度、作用域与完整数值。
    ///
    /// 为什么做成一个「单例挂在 Overlay Canvas 上」的组件，而不是每张卡片自带一个面板：
    ///   1. 详情面板比图标大得多，跟着图标走会互相遮挡，也放不进背包格；
    ///   2. 25 张卡片各带一个面板 = 25 个隐藏的 Text 层级，每次刷新都要重建；
    ///   3. 详情必须画在**最上层**（背包面板之上），只有独立 Canvas 能保证。
    ///
    /// 交互契约：
    ///   · 悬停 → 显示；移出 → 隐藏。
    ///   · 拖拽中不显示（拖拽时鼠标位置由卡片跟随，详情会闪）。
    ///   · 面板跟随鼠标但**自动避让屏幕边缘**，不能跑出画面。
    /// </summary>
    public sealed class ItemTooltip : MonoBehaviour
    {
        static ItemTooltip s_instance;

        Canvas _canvas;
        RectTransform _root;
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

        const float Width = 340f;

        public static ItemTooltip Instance => s_instance;

        /// <summary>详情面板是否正在显示（自检用）。</summary>
        public bool Visible => _root != null && _root.gameObject.activeSelf;

        /// <summary>当前展示的配件 id（自检用）。</summary>
        public string CurrentId => _currentId;

        public static ItemTooltip Ensure(Transform parent)
        {
            if (s_instance != null) return s_instance;

            var go = new GameObject("ItemTooltipCanvas");
            go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;          // 高于背包(20)与三选一，低于主菜单

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            var tip = go.AddComponent<ItemTooltip>();
            tip.Build(go.transform);
            s_instance = tip;
            return tip;
        }

        void Build(Transform parent)
        {
            _canvas = GetComponent<Canvas>();

            _root = (RectTransform)new GameObject("Panel", typeof(RectTransform)).transform;
            _root.SetParent(parent, false);
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.sizeDelta = new Vector2(Width, 230f);

            // 底板：复用 panel 素材的 9-slice，和背包/菜单是同一套材质
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
            _back.raycastTarget = false;    // 详情不能吃鼠标，否则会打断悬停

            // 圆形图标（左上）
            _iconRing = Pic("IconRing", new Vector2(14f, -14f), new Vector2(64f, 64f));
            _iconRing.sprite = UiAssets.IconFrame;
            _iconRing.color = new Color(0.55f, 0.62f, 0.72f, 0.55f);
            _iconRing.preserveAspect = true;

            _icon = Pic("Icon", new Vector2(20f, -20f), new Vector2(52f, 52f));

            _title = Txt("Title", 17f, -76f, -20f, -40f, 20, TextAnchor.MiddleLeft, Color.white);
            _rarity = Txt("Rarity", 14f, -96f, -20f, -78f, 13, TextAnchor.MiddleLeft,
                new Color(0.75f, 0.8f, 0.9f));
            _scope = Txt("Scope", 15f, -96f, -140f, -60f, 13, TextAnchor.MiddleRight,
                new Color(0.7f, 0.78f, 0.9f));
            _body = Txt("Body", 22f, -200f, -16f, -112f, 14, TextAnchor.UpperLeft,
                new Color(0.88f, 0.92f, 1f));
            _footer = Txt("Footer", 8f, -230f, -20f, -202f, 12, TextAnchor.MiddleLeft,
                new Color(0.55f, 0.62f, 0.72f));

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
            return img;
        }

        Text Txt(string name, float top, float left, float right, float height,
            int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);

            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>拖拽开始时抑制显示，避免详情跟着拖拽中的卡片闪。</summary>
        public void Suppress(bool on)
        {
            _suppressed = on;
            if (on) Hide();
        }

        public void Show(ChainItem item)
        {
            if (item == null || _suppressed) { Hide(); return; }
            string id = item.IsBullet ? item.Bullet.Id : item.Mod.Id;
            if (id == _currentId && Visible) { Follow(); return; }
            _currentId = id;

            bool bullet = item.IsBullet;
            Rarity rar = bullet ? item.Bullet.Rarity : item.Mod.Rarity;
            string name = bullet ? item.Bullet.Name : item.Mod.Name;

            _title.text = name;
            _rarity.text = RarityName(rar) + (bullet ? "   ·   子弹配件" : "   ·   衍生配件");
            _rarity.color = RarityColor(rar);

            if (bullet)
            {
                _scope.text = "";
                _body.text = DescribeBullet(item.Bullet);
            }
            else
            {
                _scope.text = ScopeName(item.Mod.Scope);
                _scope.color = ScopeColor(item.Mod.Scope);
                _body.text = DescribeMod(item.Mod);
            }

            _icon.enabled = true;
            if (!UiAssets.ApplyItemIcon(_icon, id, Color.white))
            {
                // 缺图降级：不显示图标，但面板照常给数值 —— 不能让详情整块消失
                _icon.enabled = false;
            }

            _footer.text = "链式触发：链内每项按顺序逐项生效，间隔 = 枪的击发间隔";

            _root.gameObject.SetActive(true);
            Follow();
        }

        public void Hide()
        {
            _currentId = null;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>跟随鼠标，并避让屏幕四边。</summary>
        void Follow()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || _canvas == null) return;

            Vector2 screen = mouse.position.ReadValue();
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, screen, null, out local);

            // Canvas 是 ScaleWithScreenSize，这里用画布矩形换算回参考分辨率下的尺寸
            var canvasRt = (RectTransform)_canvas.transform;
            Vector2 size = _root.rect.size;

            float x = local.x + 22f;
            float y = local.y - 16f;

            // 右/下越界则翻到鼠标另一侧
            if (x + size.x > canvasRt.rect.xMax - 8f) x = local.x - size.x - 22f;
            if (y - size.y < canvasRt.rect.yMin + 8f) y = local.y + size.y + 16f;

            _root.anchoredPosition = new Vector2(
                Mathf.Clamp(x, canvasRt.rect.xMin + 8f, canvasRt.rect.xMax - size.x - 8f),
                Mathf.Clamp(y, canvasRt.rect.yMin + size.y + 8f, canvasRt.rect.yMax - 8f));
        }

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
            Scope.Whole => "全场",
            Scope.After => "后继",
            _ => "枪械",
        };

        public static Color ScopeColor(Scope s) => s switch
        {
            Scope.Whole => new Color(0.45f, 0.72f, 1f),
            Scope.After => new Color(0.5f, 0.95f, 0.6f),
            _ => new Color(0.95f, 0.78f, 0.35f),
        };

        static string DescribeBullet(BulletDef b)
        {
            var lines = new System.Collections.Generic.List<string>
            {
                $"伤害 <b>{b.Damage:0.#}</b>    弹药 {b.Ammo}    散射 +{b.SpreadAdd:0.#}°",
                $"暴击 {b.Crit * 100f:0}% × {b.CritDmg:0.#}    存在 {b.Lifetime:0.##}s",
            };
            var feat = new System.Collections.Generic.List<string>();
            if (b.SplashRadius > 0f) feat.Add($"命中溅射 {b.SplashRadius:0.#}m");
            if (b.Pierce > 0) feat.Add($"穿透 {b.Pierce} 个");
            if (b.Bounce > 0) feat.Add($"反弹 {b.Bounce} 次");
            if (b.BurnDps > 0f) feat.Add($"燃烧 {b.BurnDps:0}/s × {b.BurnTime:0.#}s");
            if (b.SlowPct > 0f) feat.Add($"减速 {b.SlowPct * 100f:0}% × {b.SlowTime:0.#}s");
            if (b.IgnoreArmor) feat.Add("无视护甲");
            if (b.SpeedMul != 1f) feat.Add($"弹速 ×{b.SpeedMul:0.##}");
            if (feat.Count > 0) lines.Add(string.Join("    ", feat));
            return string.Join("\n", lines);
        }

        static string DescribeMod(ModifierDef m)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (m.DamageMul != 1f) parts.Add($"伤害 ×{m.DamageMul:0.##}");
            if (m.DamageFlat != 0f) parts.Add($"伤害 {(m.DamageFlat > 0 ? "+" : "")}{m.DamageFlat:0.#}");
            if (m.CritFlat != 0f) parts.Add($"暴击 +{m.CritFlat * 100f:0}%");
            if (m.CritMul != 1f) parts.Add($"暴击 ×{m.CritMul:0.##}");
            if (m.CritDmgFlat != 0f) parts.Add($"暴伤 +{m.CritDmgFlat:0.#}");
            if (m.SpreadMul != 1f) parts.Add($"散射 ×{m.SpreadMul:0.##}");
            if (m.SpreadAddFlat != 0f) parts.Add($"散射 +{m.SpreadAddFlat:0.#}°");
            if (m.LifetimeFlat != 0f) parts.Add($"存在 {(m.LifetimeFlat > 0 ? "+" : "")}{m.LifetimeFlat:0.#}s");
            if (m.LifetimeMul != 1f) parts.Add($"存在 ×{m.LifetimeMul:0.##}");
            if (m.AmmoFlat != 0) parts.Add($"弹药 {(m.AmmoFlat > 0 ? "+" : "")}{m.AmmoFlat}");
            if (m.SpeedMul != 1f) parts.Add($"弹速 ×{m.SpeedMul:0.##}");
            if (m.PierceFlat != 0) parts.Add($"穿透 +{m.PierceFlat}");
            if (m.BounceFlat != 0) parts.Add($"反弹 +{m.BounceFlat}");
            if (m.HomingRate != 0f) parts.Add($"追踪 {m.HomingRate:0}°/s");
            if (m.SplitCount > 1)
                parts.Add($"分裂 ×{m.SplitCount}（伤 ×{m.SplitDamageMul:0.##} 散 +{m.SplitSpreadAdd:0.#}°）");
            if (m.SplashAdd != 0f) parts.Add($"溅射 +{m.SplashAdd:0.#}m");
            if (m.GunMagAdd != 0) parts.Add($"弹匣 +{m.GunMagAdd}");
            if (m.GunFireIntervalMul != 1f) parts.Add($"射速 ×{1f / m.GunFireIntervalMul:0.##}");
            if (m.GunSpreadMul != 1f) parts.Add($"枪散射 ×{m.GunSpreadMul:0.##}");
            if (m.GunReloadMul != 1f) parts.Add($"换弹 ×{m.GunReloadMul:0.##}");

            string note = m.Scope switch
            {
                Scope.After => "\n<b>后继</b>：只作用于排在它**之后**的子弹（放在子弹后 = 白放）",
                Scope.Whole => "\n<b>全场</b>：作用于施加时刻链内全部投射物（与位置无关）",
                _ => "\n<b>枪械</b>：装配时作用于整把枪，不占链的触发时间轴",
            };
            return string.Join("    ", parts) + note;
        }
    }
}

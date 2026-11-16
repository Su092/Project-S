using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 圆形配件图标（含稀有度色环 + 触发序号角标）。
    ///
    /// 为什么单独成类：图标现在出现在**四个**地方 —— 背包格、链区卡片、三选一卡、HUD 链条。
    /// 每处的尺寸与附加信息不同（HUD 要序号、背包要最简），但「底图 = item_&lt;id&gt; 素材、
    /// 外环颜色 = 稀有度、缺图降级为字母」这一套必须完全一致，否则四个界面看起来像四套资产。
    ///
    /// 组成（层级从下到上）：
    ///   底环（icon_frame 切角框，可选）→ 圆形图标（item_* 自带圆底板）→ 序号/角标文字
    /// </summary>
    public sealed class ItemIconView : MonoBehaviour
    {
        public Image Ring;        // 稀有度外环（icon_frame），可为空
        public Image Icon;        // 圆形图标本体（item_*）
        public Text Badge;        // 右下角标（HUD 里是触发序号）
        public Image ScopeMark;   // 右上作用域小标（衍生件才有），可为空

        ChainItem _item;

        public ChainItem Item => _item;

        /// <summary>
        /// 绑定一个配件并刷新外观。`showRing` 控制是否画外环，
        /// `badge` 非空时写到角标上（HUD 的触发顺序用它）。
        /// </summary>
        public void Bind(ChainItem item, float size, bool showRing = true,
            string badge = null, bool showScope = false)
        {
            _item = item;
            if (item != null && item.Level > 1 && (badge == null || !badge.StartsWith("Lv."))) badge = string.IsNullOrEmpty(badge) ? "Lv." + item.Level : badge + " · L" + item.Level;
            if (item == null) { gameObject.SetActive(false); return; }

            gameObject.SetActive(true);

            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(size, size);

            string id = item.IsBullet ? item.Bullet.Id : item.Mod.Id;
            Rarity rar = item.IsBullet ? item.Bullet.Rarity : item.Mod.Rarity;
            Color rarColor = ItemTooltip.RarityColor(rar);

            // 外环：稀有度着色。icon_frame 是切角菱形，preserveAspect + Simple。
            if (Ring != null)
            {
                Ring.enabled = showRing && UiAssets.IconFrame != null;
                if (Ring.enabled)
                {
                    Ring.sprite = UiAssets.IconFrame;
                    Ring.type = Image.Type.Simple;
                    Ring.preserveAspect = true;
                    Ring.color = new Color(rarColor.r, rarColor.g, rarColor.b, 0.9f);
                }
            }

            // 圆形图标：素材自带暗底圆，不能用加色材质（见 UiAssets.ApplyItemIcon）。
            if (Icon != null)
            {
                if (!UiAssets.ApplyItemIcon(Icon, id, Color.white))
                {
                    // 缺图降级：用稀有度色块 + 名称首字，至少还能分辨是谁。
                    // 必须给一张有形状的贴图 —— Image 的 sprite 为 null 时画的是整个矩形，
                    // 在一排圆图标里会突然冒出一个方块，看着像渲染错误。
                    Icon.enabled = true;
                    Icon.material = null;
                    Icon.sprite = UiAssets.SlotFilled;
                    Icon.type = Image.Type.Sliced;
                    Icon.color = new Color(rarColor.r * 0.55f, rarColor.g * 0.55f, rarColor.b * 0.55f, 0.95f);
                    if (Badge != null && string.IsNullOrEmpty(badge))
                    {
                        string nm = item.IsBullet ? item.Bullet.Name : item.Mod.Name;
                        badge = string.IsNullOrEmpty(nm) ? "?" : nm.Substring(0, 1);
                    }
                }
                else
                {
                    Icon.type = Image.Type.Simple;
                    Icon.color = Color.white;
                }
            }

            if (Badge != null)
            {
                bool has = !string.IsNullOrEmpty(badge);
                Badge.gameObject.SetActive(has);
                if (has)
                {
                    Badge.text = badge;
                    Badge.fontSize = Mathf.Max(10, Mathf.RoundToInt(size * 0.32f));
                    Badge.color = new Color(1f, 0.95f, 0.8f);
                }
            }

            if (ScopeMark != null)
            {
                bool show = showScope && !item.IsBullet && item.Mod != null;
                ScopeMark.gameObject.SetActive(show);
                if (show)
                {
                    var icon = UiAssets.IconForScope(item.Mod.Scope);
                    ScopeMark.enabled = icon != null;
                    if (icon != null)
                        UiAssets.ApplyIcon(ScopeMark, icon, ItemTooltip.ScopeColor(item.Mod.Scope));
                }
            }
        }
    }
}

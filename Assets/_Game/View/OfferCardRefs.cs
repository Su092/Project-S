using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>三选一候选卡预制体的引用表。</summary>
    public sealed class OfferCardRefs : MonoBehaviour
    {
        public Image Background;
        public Image Inner;
        public Button Button;
        public Image TagTop;
        public Text RarityName;
        public Text CardTitle;
        public Text Desc;
        public Text PickHint;

        [Header("圆形图标（本次新增）")]
        public ItemIconView IconNode;

        /// <summary>
        /// 把一张候选配件填进卡片。三选一是玩家第一次见到这件配件的地方，
        /// 所以必须同时给出：名称、稀有度、一句话效果、作用域。
        /// </summary>
        public void BindGun(GunDef gun, string action)
        {
            if (CardTitle != null) CardTitle.text = gun.Name;
            if (RarityName != null) { RarityName.text = "稀有 · 枪械"; RarityName.color = InventoryView.RarityColor(Rarity.Rare); }
            if (TagTop != null) TagTop.color = InventoryView.RarityColor(Rarity.Rare);
            if (Desc != null)
            {
                Desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Desc.text = $"{gun.Desc}\n槽位 {gun.SlotCount} · 弹匣 {gun.MagSize}\n换弹 {gun.ReloadTime:0.0}s · 自带基础子弹";
            }
            if (PickHint != null) PickHint.text = action;
            if (IconNode != null)
            {
                IconNode.gameObject.SetActive(true);
                ((RectTransform)IconNode.transform).sizeDelta = new Vector2(96f, 96f);
                if (IconNode.Icon != null)
                {
                    IconNode.Icon.enabled = true;
                    IconNode.Icon.type = Image.Type.Simple;
                    IconNode.Icon.sprite = UiAssets.Named("hud_gun_" + gun.Id);
                    IconNode.Icon.material = null;
                    IconNode.Icon.color = Color.white;
                    IconNode.Icon.preserveAspect = true;
                }
                if (IconNode.ScopeMark != null) IconNode.ScopeMark.enabled = false;
                if (IconNode.Badge != null) { IconNode.Badge.gameObject.SetActive(true); IconNode.Badge.text = "枪"; }
            }
        }

        public void Bind(ChainItem item, int index)
        {
            if (item == null) return;

            if (CardTitle != null) CardTitle.text = item.Name;

            if (RarityName != null)
            {
                RarityName.text = item.Rarity switch
                {
                    Rarity.Epic => "史诗",
                    Rarity.Rare => "稀有",
                    _ => "普通",
                };
                RarityName.color = InventoryView.RarityColor(item.Rarity);
            }

            if (TagTop != null) TagTop.color = InventoryView.RarityColor(item.Rarity);

            if (Desc != null)
            {
                // 预制体 Label 默认 Overflow；长描述（并联扳机等）必须换行，否则单行顶出卡片。
                Desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Desc.verticalOverflow = VerticalWrapMode.Truncate;
                string desc = item.IsBullet
                    ? item.Bullet.Desc
                    : (item.Mod.Desc ?? "") +
                      (item.Mod.Scope != Scope.All
                          ? $"\n作用范围：{ItemTooltip.ScopeName(item.Mod.Scope)}"
                          : "\n作用范围：全枪");
                Desc.text = desc;
            }

            if (PickHint != null) PickHint.text = $"点击拾取（{index + 1}）";

            if (IconNode != null) IconNode.Bind(item, 96f, showRing: true, badge: null, showScope: true);
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 悬停 → 在配件正下方弹出详情浮窗。挂在任何「代表某个配件」的可见元素上
    /// （卡片、圆形图标、链条格）。
    ///
    /// 为什么不直接写进卡片脚本：卡片在不同界面（背包格、链区、三选一）是不同预制体，
    /// 但「悬停看详情」这一件事完全相同。抽成独立组件后，加一处图标只要挂一个它，
    /// 详情内容与排版只在 ItemTooltip 里维护一份。
    /// </summary>
    public sealed class ItemHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IEndDragHandler
    {
        public ItemTooltip Tooltip;
        [System.NonSerialized] public ChainItem Item;
        /// <summary>所属的改装界面（可选）。目前只用于诊断，不参与悬停逻辑。</summary>
        [System.NonSerialized] public InventoryView Owner;

        bool _hovering;
        RectTransform _rt;

        void Awake()
        {
            _rt = transform as RectTransform;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hovering = true;
            if (Item == null) return;

            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            if (Tooltip == null) return;

            // 游玩态光标锁定时鼠标钉在屏幕中心：HUD 角落图标若压到中心会永远挡准星。
            // 暂停/背包态（光标解锁）才允许显示。
            if (Cursor.lockState == CursorLockMode.Locked) return;

            // 浮窗钉在本配件正下方（不是背包底部常驻区，也不是跟鼠标飘）
            Tooltip.ShowUnder(Item, _rt != null ? _rt : (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hovering = false;
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Hide();
        }

        // 拖拽期间先收起「钉在原卡片下」的浮窗。
        // 背包拖拽的落点预览由 InventoryView.UpdateDropHints 接手（跟手显示）；
        // 其它界面（HUD）直接 Suppress，避免详情跟着拖影闪。
        public void OnBeginDrag(PointerEventData e)
        {
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            if (Owner != null) { Tooltip?.Hide(); return; }
            Tooltip?.Suppress(true);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Suppress(false);

            // 拖拽结束后鼠标通常还在原处：重新钉回本配件下方
            if (_hovering && Item != null)
                Tooltip?.ShowUnder(Item, _rt != null ? _rt : (RectTransform)transform);
        }

        void OnDisable()
        {
            // 销毁/禁用时必须收起，否则详情会留在屏幕上（背包刷新会销毁全部卡片）
            if (!_hovering) return;
            _hovering = false;
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Hide();
        }
    }
}

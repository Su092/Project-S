using UnityEngine;
using UnityEngine.EventSystems;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 悬停 → 显示配件详情。挂在任何「代表某个配件」的可见元素上（卡片、圆形图标、链条格）。
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

        /// <summary>
        /// 背包内的**固定详情区**。非空时优先填它，而不是弹出跟随鼠标的浮动提示。
        ///
        /// 为什么两套并存而不是只留一套：背包里玩家要「对比」，浮动提示会挡住相邻格；
        /// 而 HUD 链条、三选一卡片没有常驻区域可用，只能靠浮动提示。
        /// </summary>
        [System.NonSerialized] public ItemDetailPanel Detail;

        bool _hovering;

        public void OnPointerEnter(PointerEventData e)
        {
            _hovering = true;

            if (Item == null) return;

            // 固定详情区不依赖光标锁定，优先走它
            if (Detail != null)
            {
                Detail.Show(Item);
                return;
            }

            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            if (Tooltip == null) return;

            // 游玩态光标是锁定的：此时鼠标固定在屏幕中心，HUD 角落的描述符图标
            // 若恰好压到中心就会弹出一块永远跟手的详情，等于遮住准星。
            // 只要光标锁定就当作"没有悬停"，暂停/背包态（光标解锁）才允许显示。
            if (Cursor.lockState == CursorLockMode.Locked) return;

            Tooltip.Show(Item);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hovering = false;

            if (Detail != null)
            {
                Detail.Clear();
                return;
            }

            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Hide();
        }

        // 拖拽期间必须收起详情：卡片会跟着鼠标走，详情面板会一直闪。
        public void OnBeginDrag(PointerEventData e)
        {
            if (Detail != null) { Detail.Clear(); return; }
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Suppress(true);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Suppress(false);

            // 拖拽结束后鼠标通常还在原处：重新弹一次，避免"松手后详情消失"
            if (_hovering && Item != null)
            {
                if (Detail != null) Detail.Show(Item);
                else Tooltip?.Show(Item);
            }
        }

        void OnDisable()
        {
            // 销毁/禁用时必须收起，否则详情会留在屏幕上（背包刷新会销毁全部卡片）
            if (!_hovering) return;
            _hovering = false;

            if (Detail != null) { Detail.Clear(); return; }
            if (Tooltip == null) Tooltip = ItemTooltip.Instance;
            Tooltip?.Hide();
        }
    }
}

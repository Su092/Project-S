using UnityEngine;
using UnityEngine.EventSystems;

namespace S.View
{
    /// <summary>
    /// 背包槽位的拖放目标。每个槽代表链中的一格。
    ///
    /// 该组件用于 Inventory 预制体和运行时生成的槽位，因此必须单独成文件：
    /// Unity 按「文件名 = 类名」定位脚本，混在别的文件里会变成 Missing (Mono Script)。
    /// </summary>
    public sealed class SlotDropTarget : MonoBehaviour, IDropHandler, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        public InventoryView Owner;
        public int SlotIndex;
        public int GunIndex;
        public int ChainIndex;      // -1 表示追加到末尾
        public bool IsBagArea;

        public void OnDrop(PointerEventData e)
        {
            if (Owner == null) return;
            var src = e.pointerDrag != null ? e.pointerDrag.GetComponent<DraggableItem>() : null;
            if (src == null) return;
            Owner.HandleDrop(src, this);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (!IsBagArea) Owner?.PreviewSlot(SlotIndex, true, GunIndex);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (!IsBagArea) Owner?.PreviewSlot(SlotIndex, false, GunIndex);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (Owner != null && !IsBagArea && e.button == PointerEventData.InputButton.Left)
                Owner.HandleSlotClick(SlotIndex);
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 可拖拽的配件卡。
    ///
    /// 运行时由 `InventoryView.MakeCard` 挂到卡片实例上（卡片本身是 ItemCard 预制体），
    /// 因为 `Item` 是运行时数据、Owner 是场景对象，都无法序列化进预制体。
    /// </summary>
    public sealed class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerClickHandler, IDropHandler
    {
        public InventoryView Owner;
        [System.NonSerialized] public ChainItem Item;
        public int FromSlot = -1;       // -1 表示来自背包
        public int FromChain = -1;
        public bool WasDropped;

        CanvasGroup _cg;
        Transform _dragLayer;

        void Awake()
        {
            _cg = gameObject.GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Owner == null || !Owner.CanEditAttachments) return;
            WasDropped = false;
            _dragLayer = Owner.DragLayer;

            transform.SetParent(_dragLayer, true);
            _cg.blocksRaycasts = false;
            _cg.alpha = 0.85f;
            Owner.NotifyDragStart(this);
        }

        public void OnDrag(PointerEventData e)
        {
            ((RectTransform)transform).position = e.position;
            Owner.UpdateDropHints(Item, FromSlot, FromChain, e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            _cg.blocksRaycasts = true;
            _cg.alpha = 1f;
            Owner.NotifyDragEnd(this, e);
        }

        public void OnDrop(PointerEventData e)
        {
            var src = e.pointerDrag != null ? e.pointerDrag.GetComponent<DraggableItem>() : null;
            if (src != null) Owner?.HandleItemDrop(src, this);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (Owner == null) return;
            Owner.HandleItemClick(this, e.button);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>背包（改装）预制体的引用表。</summary>
    public sealed class InvRefs : MonoBehaviour
    {
        public GameObject Root;
        public RectTransform ChainArea;
        public RectTransform BagGrid;
        public SlotDropTarget BagDropPanel;
        public Text Hint;
        public Button CloseButton, SortButton;

        [Header("详情面板（对齐原型 #inv-detail）")]
        /// <summary>固定详情区：显示悬停配件的名称/稀有度/作用域/完整数值。</summary>
        public ItemDetailPanel Detail;

        [Header("模板（保持非激活，运行时克隆）")]
        public GameObject GunTabTemplate;
    }
}

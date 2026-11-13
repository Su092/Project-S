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

        [Header("模板（保持非激活，运行时克隆）")]
        public GameObject ChainRowTemplate;
        public GameObject GunTabTemplate;
    }
}

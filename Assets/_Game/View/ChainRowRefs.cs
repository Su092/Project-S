using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>链区一行（一个配件位）的引用表。</summary>
    public sealed class ChainRowRefs : MonoBehaviour
    {
        public Image Background;
        public SlotDropTarget Drop;
        public Text RowLabel;
        public RectTransform Tail;
        public Image TailImage;
        public SlotDropTarget TailDrop;
    }
}

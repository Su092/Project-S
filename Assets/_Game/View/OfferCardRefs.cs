using UnityEngine;
using UnityEngine.UI;

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
    }
}

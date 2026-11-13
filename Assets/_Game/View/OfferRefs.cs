using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>三选一面板预制体的引用表。</summary>
    public sealed class OfferRefs : MonoBehaviour
    {
        public GameObject Root;
        public Text Title;
        public RectTransform CardArea;
        public GameObject CardTemplate;
    }
}

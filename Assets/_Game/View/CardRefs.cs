using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>链内 / 背包里的配件卡预制体的引用表。</summary>
    public sealed class CardRefs : MonoBehaviour
    {
        public Image Background;
        public Image Inner;
        public Image Tag;
        public Image ScopeIcon;
        public Text Label;

        [Header("圆形图标（本次新增：配件统一用圆形图标代表）")]
        public ItemIconView IconNode;
    }
}

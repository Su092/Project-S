using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>
    /// 主菜单预制体的引用表。由预制体作者在 Inspector 中拖好，运行时只读。
    ///
    /// 为什么不按名字查找子节点：改个名字或调一下层级，`transform.Find` 就会静默返回 null，
    /// 表现为「按钮点了没反应」；序列化引用在改名时由 Unity 自动跟随，改层级也不会断。
    /// </summary>
    public sealed class MenuRefs : MonoBehaviour
    {
        [Header("结构")]
        public GameObject Root;
        public RectTransform UnlockPanel;
        public RectTransform UnlockList;

        [Header("文字")]
        public Text ShardText;
        public Text StatsText;
        public Text Hint;

        [Header("按钮")]
        public Button StartButton;
        public Button QuitButton;

        [Header("解锁行模板（保持非激活，运行时克隆）")]
        public GameObject RowTemplate;

        [Header("音量（顺序：主音量 / 音效 / 音乐 / 界面音）")]
        public Text[] VolumeValues;
        public Button[] VolumeMinus;
        public Button[] VolumePlus;
    }
}

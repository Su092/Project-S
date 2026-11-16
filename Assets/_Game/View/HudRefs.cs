using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>HUD 预制体的引用表。</summary>
    public sealed class HudRefs : MonoBehaviour
    {
        [Header("生命")]
        public Image HpFill;
        public Text HpText;

        [Header("弹药")]
        public Text AmmoText;
        /// <summary>弹匣进度条填充（耗弹变短；换弹时从左往右涨满）。</summary>
        public Image AmmoFill;
        public Text GunText;
        public Text DpsText;

        [Header("轮转序列（顺序即格位 1..N）")]
        public Image[] RotationPips;
        public Text[] RotationLabels;

        [Header("当前链的触发进度（圆形配件图标，按链顺序）")]
        public ItemIconView[] ChainIcons;
        /// <summary>链区标题：位 N · 第 k/L 项触发。</summary>
        public Text ChainTitle;
        /// <summary>链区容器：整排图标的父节点（用于对齐与显隐）。</summary>
        public RectTransform ChainRoot;

        [Header("经验")]
        public Image XpFill;
        public Text LevelText;

        [Header("威胁")]
        public Image ThreatFill;
        public Text ThreatText;

        [Header("准星与反馈")]
        public Image Crosshair;
        public Image DamageVignette;
        public Text Toast;

        [Header("中央横幅")]
        public Image BannerPlate;
        public Text CenterBig;

        [Header("区域与 Boss")]
        public Text ZoneText;

        [Header("中央大计时器（对齐原型 #timer）")]
        public Text Timer;
        /// <summary>倒计时标签：距精英 / 距 Boss。</summary>
        public Text TimerLabel;
        public GameObject BossBarRoot;
        public Image BossFill;
        public Text BossNameText;
    }
}

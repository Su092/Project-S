using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>
    /// UI 运行时工具。
    ///
    /// 只保留两类东西：
    ///  1. 字体访问（预制体里的 Text 已经把字体序列化好，这里给运行时临时文本用）；
    ///  2. `MakeText` —— 给**瞬时反馈**造一小段文字（如"背包已满"的 2.5s 提示）。
    ///
    /// 为什么允许这类文本不走预制体：它们数量不定、生命周期极短、位置依赖上下文，
    /// 为每一种提示做一个预制体反而更难维护。界面结构（面板/按钮/卡片/行）一律走预制体。
    /// </summary>
    public static class UiKit
    {
        static Font s_font;

        /// <summary>工程统一字体（`Resources/Font/UiRounded.ttf`，原名「极影毁片圆」）。</summary>
        public const string FontPath = "Font/UiRounded";

        /// <summary>
        /// **唯一**字体入口。所有文本都必须走这里，不要在别处写内置字体名。
        ///
        /// 为什么保留内置回退：字体是 `Resources` 资源，万一被误删或未随包带上，
        /// 界面会变成一排空白方块 —— 那是比"字体不对"严重得多的事故。
        /// 所以拿不到就静默退到内置 Arial，界面难看但可读。
        ///
        /// ⚠ `Resources.Load<Font>` 取的是 **Font 资产**，不是 TMP_FontAsset。
        /// 本项目 UI 全部用 Legacy `UnityEngine.UI.Text`，两者不通用。
        /// </summary>
        public static Font Font
        {
            get
            {
                if (s_font != null) return s_font;
                s_font = Resources.Load<Font>(FontPath);
                if (s_font == null)
                {
                    Debug.LogWarning($"[S] 字体缺失：Resources/{FontPath}.ttf，已回退内置字体");
                    s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (s_font == null) s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return s_font;
            }
        }

        /// <summary>编辑器侧取字体用（预制体工厂不能走 Resources，要拿资产本体去序列化）。</summary>
        public const string FontAssetPath = "Assets/Resources/Font/UiRounded.ttf";

        /// <summary>造一段临时文字（锚点 + 四边偏移定位，与预制体里的写法一致）。</summary>
        public static Text MakeText(Transform parent, string name, int size, TextAnchor align,
            Color color, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;

            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}

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

        public static Font Font
        {
            get
            {
                if (s_font != null) return s_font;
                s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (s_font == null) s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return s_font;
            }
        }

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

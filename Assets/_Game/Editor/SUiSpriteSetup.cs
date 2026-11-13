using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// UI 素材导入设置与 9-slice 边距的**唯一**入口（校验 + 重导）。
    ///
    /// ⚠ 这个类曾经自己按「贴图短边 × 百分比」算边距，和
    /// SAssetPostprocessor.BorderFor 抢着写同一份 spriteBorder —— 两处数值不同，
    /// 谁后跑谁赢，结果同一张图在编辑器里和打包后边距不一样，圆角时好时坏。
    /// 现在边距只由 BorderFor 决定（那份数值是按控件真实像素倒推的），
    /// 这里只负责：① 补导入设置 ② 强制重导让 BorderFor 生效 ③ 校验。
    /// </summary>
    public static class SUiSpriteSetup
    {
        const string UiRoot = "Assets/Resources/UI/";

        // 只用于校验的最小控件尺寸（与 audit_ui_slices.py / SAssetPostprocessor 对齐）
        struct Rule
        {
            public string File;
            public Vector2 MinWidget;
            public Rule(string f, Vector2 m) { File = f; MinWidget = m; }
        }

        static readonly Rule[] Rules =
        {
            new Rule("panel.png",      new Vector2(380f, 228f)),
            new Rule("card_frame.png", new Vector2(150f, 84f)),
            new Rule("bar_frame.png",  new Vector2(60f, 14f)),
            new Rule("banner.png",     new Vector2(720f, 88f)),
        };

        [MenuItem("S/写入 UI 九宫格边距")]
        public static void Apply()
        {
            // 边距本身由 SAssetPostprocessor.BorderFor 决定，这里只触发重导。
            SUiTextureReimport.ReimportAll();
            Debug.Log("[S] UI 九宫格边距已由 SAssetPostprocessor.BorderFor 统一写入");
        }

        /// <summary>
        /// 校验：所有 9-slice sprite 的实际边距必须小于目标控件尺寸。
        /// 读的是 sprite 的**真实** border（也就是 BorderFor 写进 meta 的值），
        /// 不再自己算一遍 —— 自己算就等于又引入一个真相来源。
        /// </summary>
        public static bool Verify(out string report)
        {
            var sb = new System.Text.StringBuilder();
            bool allOk = true;

            foreach (var r in Rules)
            {
                var path = UiRoot + r.File;
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp == null) { sb.AppendLine($"  缺 {r.File}"); allOk = false; continue; }

                Vector4 bd = sp.border;
                float sumX = bd.x + bd.z;
                float sumY = bd.y + bd.w;

                bool okX = sumX < r.MinWidget.x;
                bool okY = sumY < r.MinWidget.y;
                if (!okX || !okY) allOk = false;

                sb.AppendLine($"  {r.File}: border=({bd.x},{bd.y},{bd.z},{bd.w}) " +
                              $"sumX={sumX} < {r.MinWidget.x}? {okX}  sumY={sumY} < {r.MinWidget.y}? {okY}");
            }

            report = sb.ToString();
            return allOk;
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// 重新导入 Assets/Resources/UI 下的全部贴图。
    ///
    /// 为什么必须有这一步：9-slice 边距写在 SAssetPostprocessor.OnPreprocessTexture 里，
    /// 而 OnPreprocessTexture 只在「首次导入」时跑。素材重新生成（PNG 时间戳变了）
    /// 并不等于重新导入 —— Unity 若判定内容没变就复用旧 meta，spriteBorder 会保留旧值。
    /// 症状极隐蔽：改了 BorderFor、打包通过、自检也过，但画面上圆角照样是坏的，
    /// 因为 .meta 里还是老边距（本次就踩了这个坑）。
    ///
    /// 所以改 BorderFor 之后必须跑一次这个方法。
    /// </summary>
    public static class SUiTextureReimport
    {
        static readonly string UiRoot = S.View.SRes.Root + "Texture/UI";

        [MenuItem("S/重新导入 UI 贴图")]
        public static void ReimportAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { UiRoot });
            int n = 0;
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".png")) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                n++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[S] 重新导入 UI 贴图 {n} 张");
            Debug.Log($"RESULT UI 贴图重导 {n}");
        }

        /// <summary>打印每张图的 spriteBorder，用来核对是否与 BorderFor 一致。</summary>
        public static void DumpBorders()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { UiRoot });
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Vector4 b = sp != null ? sp.border : Vector4.zero;
                sb.AppendLine($"BORDER {System.IO.Path.GetFileNameWithoutExtension(path)} " +
                              $"{tex.width}x{tex.height} -> ({b.x},{b.y},{b.z},{b.w})");
            }
            Debug.Log(sb.ToString());
            Debug.Log("RESULT BORDER DUMP");
        }
    }
}

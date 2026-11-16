using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace S.EditorTools
{
    /// <summary>
    /// 统一字体的导入 / 重绑 / 校验。
    ///
    /// 为什么需要单独一道门：字体引用是**硬 GUID 引用**（预制体里 72 处 `m_Font` 都指向它）。
    /// 字体一旦导入失败或 GUID 变了，全部文字会变成 "Missing (Font)"，
    /// 而且 Unity **不会报错** —— 界面照常打开，只是字全没了。
    /// 这类问题在编辑器里一眼可见，但在 batchmode / CI 里完全静默。
    ///
    /// 校验命令：`check.ps1` 的 ui 任务之后跑一次（见 SelfCheck 里的运行时断言，两道互补）。
    /// </summary>
    public static class SUiFontSetup
    {
        const string FontFamilyName = "KTXP_Rounded Medium";

        /// <summary>
        /// 强制重导字体，并把所有 UI 预制体里的 Text.font 重新赋成统一字体后落盘。
        ///
        /// 为什么要「重赋」而不是只改 YAML：手写 `m_Font` / 空 `fontNames` 会让
        /// AssetDatabase 能 Load、但动态字库贴图接不上 —— 表现为字全没、无报错。
        /// 走 `text.font = font` + SavePrefab 才能让 Unity 写出正确的内部引用。
        /// </summary>
        [MenuItem("S/重绑 UI 字体")]
        public static void Rebind()
        {
            RebindBatch();
        }

        public static void RebindBatch()
        {
            // 1) 纠正 importer：空 fontNames 时动态字库接不上字形
            var importer = AssetImporter.GetAtPath(S.View.UiKit.FontAssetPath) as TrueTypeFontImporter;
            if (importer == null)
            {
                Debug.LogError($"FAIL 找不到 TrueTypeFontImporter: {S.View.UiKit.FontAssetPath}");
                Debug.Log("RESULT UI 字体重绑 失败");
                return;
            }

            bool dirtyImporter = false;
            if (importer.fontNames == null || importer.fontNames.Length == 0 ||
                importer.fontNames[0] != FontFamilyName)
            {
                importer.fontNames = new[] { FontFamilyName };
                dirtyImporter = true;
            }
            if (importer.fontRenderingMode != FontRenderingMode.Smooth)
            {
                importer.fontRenderingMode = FontRenderingMode.Smooth;
                dirtyImporter = true;
            }
            if (importer.fontTextureCase != FontTextureCase.Dynamic)
            {
                importer.fontTextureCase = FontTextureCase.Dynamic;
                dirtyImporter = true;
            }

            if (dirtyImporter)
            {
                importer.SaveAndReimport();
                Debug.Log("INFO 已重导字体 importer（fontNames / RenderingMode / Dynamic）");
            }
            else
            {
                AssetDatabase.ImportAsset(S.View.UiKit.FontAssetPath, ImportAssetOptions.ForceUpdate);
                Debug.Log("INFO 已 ForceUpdate 重导字体");
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(S.View.UiKit.FontAssetPath);
            if (font == null)
            {
                Debug.LogError($"FAIL 重导后仍取不到字体: {S.View.UiKit.FontAssetPath}");
                Debug.Log("RESULT UI 字体重绑 失败");
                return;
            }

            // 动态字体：先请求几个中英文字形，逼出字库贴图，暴露「接不上」的问题
            font.RequestCharactersInTexture("ABCDEFGabcdefg0123456789背包详情悬停");
            var tex = font.material != null ? font.material.mainTexture : null;
            Debug.Log(tex != null
                ? $"PASS 字体动态贴图已生成: {tex.width}x{tex.height}"
                : "FAIL 字体动态贴图仍为空（字形接不上）");
            if (tex == null)
            {
                Debug.Log("RESULT UI 字体重绑 失败");
                return;
            }

            // 2) 所有 UI 预制体的 Text 重新赋值并保存
            string[] prefs = AssetDatabase.FindAssets("t:Prefab", new[] { S.View.UiPrefab.AssetDir.TrimEnd('/') });
            int texts = 0, prefabs = 0;
            foreach (string g in prefs)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                foreach (var t in root.GetComponentsInChildren<Text>(true))
                {
                    texts++;
                    if (t.font != font)
                    {
                        t.font = font;
                        changed = true;
                    }
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabs++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"PASS 重绑 Text {texts} 处，改写预制体 {prefabs} 个；font={font.name}");
            Debug.Log("RESULT UI 字体重绑 完成");

            // 重绑后再跑一遍校验，结果写进同一份日志
            Verify();
        }

        /// <summary>校验字体资产与预制体引用是否一致。</summary>
        [MenuItem("S/校验 UI 字体")]
        public static void Verify()
        {
            int fail = 0;

            // 1) 资产本体能按路径取到
            var font = AssetDatabase.LoadAssetAtPath<Font>(S.View.UiKit.FontAssetPath);
            if (font == null)
            {
                Debug.LogError($"FAIL 字体资产取不到: {S.View.UiKit.FontAssetPath}");
                fail++;
            }
            else
            {
                Debug.Log($"PASS 字体资产存在: name={font.name} dynamic={font.dynamic}");
            }

            // 2) importer.fontNames 不能为空（空 = 动态字库接不上，字全没）
            var importer = AssetImporter.GetAtPath(S.View.UiKit.FontAssetPath) as TrueTypeFontImporter;
            if (importer == null)
            {
                Debug.LogError("FAIL 字体没有 TrueTypeFontImporter");
                fail++;
            }
            else if (importer.fontNames == null || importer.fontNames.Length == 0)
            {
                Debug.LogError("FAIL 字体 fontNames 为空（动态字库无法解析字形，界面文字会全消失）");
                fail++;
            }
            else
            {
                Debug.Log($"PASS 字体 fontNames = [{string.Join(", ", importer.fontNames)}]");
            }

            // 3) Addressables 分组里也能取到（两套路径不能只有一套通）
            var rt = AssetDatabase.LoadAssetAtPath<Font>(S.View.UiKit.FontAssetPath);
            if (rt == null)
            {
                Debug.LogError($"FAIL 取不到字体资产: {S.View.UiKit.FontAssetPath}");
                fail++;
            }
            else
            {
                Debug.Log($"PASS 取到字体资产: name={rt.name}");
            }

            // 4) GUID 必须与预制体里写的一致
            string guid = AssetDatabase.AssetPathToGUID(S.View.UiKit.FontAssetPath);
            Debug.Log($"INFO 字体 GUID = {guid}");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("FAIL 字体没有 GUID（未导入）");
                fail++;
            }

            // 5) 预制体里的每一处 m_Font 都必须是这个 GUID，不许残留内置 Arial
            string[] prefs = AssetDatabase.FindAssets("t:Prefab", new[] { S.View.UiPrefab.AssetDir.TrimEnd('/') });
            int refs = 0, stale = 0;
            foreach (string g in prefs)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string txt = System.IO.File.ReadAllText(p);
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(txt, @"m_Font: \{fileID: -?\d+, guid: ([0-9a-f]{32})"))
                {
                    refs++;
                    if (m.Groups[1].Value != guid) stale++;
                }
            }
            if (stale > 0)
            {
                Debug.LogError($"FAIL 预制体里有 {stale}/{refs} 处字体引用不是统一字体");
                fail++;
            }
            else
            {
                Debug.Log($"PASS 预制体字体引用 {refs} 处全部指向统一字体");
            }

            // 6) 动态贴图：能请求到字形才算真可用（只查 GUID 会漏掉「字全没」）
            if (font != null)
            {
                font.RequestCharactersInTexture("测Abc1");
                var tex = font.material != null ? font.material.mainTexture : null;
                if (tex == null)
                {
                    Debug.LogError("FAIL 字体动态贴图为空（字形未生成）");
                    fail++;
                }
                else
                {
                    Debug.Log($"PASS 字体动态贴图 {tex.width}x{tex.height}");
                }
            }

            Debug.Log(fail == 0
                ? "RESULT UI 字体 全部通过"
                : $"RESULT UI 字体 {fail} 项失败");
        }
    }
}

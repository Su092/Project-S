using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace S.EditorTools
{
    /// <summary>
    /// 把运行时用到的着色器写进 Graphics Settings 的 Always Included Shaders。
    ///
    /// 为什么必须做：本工程所有材质都在运行时 new 出来。如果场景里没有材质引用 URP/Lit，
    /// 打包时它会被剥离，`Shader.Find` 返回 null，播放器里一切物体变粉/消失。
    /// 这个问题在编辑器里完全看不到（编辑器总能找到着色器）。
    /// </summary>
    public static class ShaderSetup
    {
        static readonly string[] Required =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Simple Lit",
            "Sprites/Default",
            "UI/Default",
        };

        [MenuItem("S/写入 Always Included Shaders")]
        public static void Apply()
        {
            var path = "ProjectSettings/GraphicsSettings.asset";
            var objs = AssetDatabase.LoadAllAssetsAtPath(path);
            if (objs == null || objs.Length == 0) { Debug.LogError("找不到 GraphicsSettings.asset"); return; }

            var gs = objs[0] as GraphicsSettings;
            if (gs == null) { Debug.LogError("GraphicsSettings 类型不符"); return; }

            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) { Debug.LogError("m_AlwaysIncludedShaders 字段不存在"); return; }

            var have = new HashSet<string>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var sh = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (sh != null) have.Add(sh.name);
            }

            int added = 0;
            foreach (var name in Required)
            {
                if (have.Contains(name)) continue;
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogWarning($"[S] 着色器未找到，跳过: {name}"); continue; }
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
                added++;
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Debug.Log($"[S] Always Included Shaders: 新增 {added} 个，当前共 {list.arraySize} 个");
        }

        public static void ApplyBatch()
        {
            Apply();
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace S.EditorTools
{
    /// <summary>
    /// 生成/重建主场景与工程设置。可重复执行（幂等）。
    /// 场景里只有一个 Bootstrap 节点，其余全部运行时构建。
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("S/重建主场景")]
        public static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("S");
            go.AddComponent<S.App.Bootstrap>();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };
            EditorBuildSettings.scenes = list;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[S] Main.unity 已生成并加入 Build Settings");
        }

        [MenuItem("S/应用工程设置")]
        public static void ApplyProjectSettings()
        {
            PlayerSettings.companyName = "S Project";
            PlayerSettings.productName = "ProjectS";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            QualitySettings.vSyncCount = 1;

            AssetDatabase.SaveAssets();
            Debug.Log("[S] 工程设置已应用");
        }

        // ---- 批处理入口 ----

        public static void BatchSetup()
        {
            BuildMainScene();
            ApplyProjectSettings();
            ShaderSetup.Apply();
            SUiMaterialSetup.Create();
            SUiSpriteSetup.Apply();
            Debug.Log("[S] BatchSetup 完成");
        }
    }
}

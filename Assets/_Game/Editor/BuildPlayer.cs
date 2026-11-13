using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

namespace S.EditorTools
{
    /// <summary>命令行打包入口，供 check.ps1 调用。</summary>
    public static class BuildPlayer
    {
        const string OutDir = "Build/Windows";
        const string OutExe = "Build/Windows/S.exe";

        [MenuItem("S/打包 Windows")]
        public static void BuildWindows()
        {
            var scenes = new[] { SceneBuilder.ScenePath };

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutExe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;

            Debug.Log($"BUILD RESULT {s.result}");
            Debug.Log($"BUILD SIZE {s.totalSize}");
            Debug.Log($"BUILD ERRORS {s.totalErrors}");
            Debug.Log($"BUILD TIME {s.totalTime}");

            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError("BUILD FAILED");
                EditorApplication.Exit(1);
            }
            else
            {
                EditorApplication.Exit(0);
            }
        }

        public static void BuildBatch()
        {
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x);
            BuildWindows();
        }
    }
}

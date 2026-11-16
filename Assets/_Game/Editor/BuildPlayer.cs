using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
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

        /// <summary>
        /// 只构建 Addressables 内容包（不打包 Player），供 check.ps1 单独调用。
        ///
        /// ⚠ 必须**不带 -quit** 跑：Addressables 的构建是挂在 EditorApplication.update 上的
        /// 异步任务链，`-quit` 会在第一帧后就结束进程，于是 BuildPlayerContent 立刻返回、
        /// 任务一个都没跑，日志里既没有报错也没有产物 —— 这是最难查的一种「静默成功」。
        /// 所以这里用 delayCall 把「跑构建 + 校验产物 + 主动 Exit」串起来。
        /// </summary>
        public static void BuildContentBatch()
        {
            // ⚠ 顺序很重要：Build() 里的 AddressableAssetSettings.Create 只改内存，
            // 不落盘。之后调用静态的 AddressableAssetSettings.BuildPlayerContent 时，
            // 它会重新从磁盘读 settings —— 读不到就打印
            // 「Addressable Asset Settings does not exist. Failed to create.」，
            // 然后**返回一个非 null 的 buildData**（0 bundle、无异常），
            // 于是上层断言全绿、产物为空。这是本工程踩过的最隐蔽的一次静默失败。
            // 所以：先在磁盘上确保 settings 存在，再重建分组，最后构建。
            SAddressablesSetup.Build();

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(
                    AddressableAssetSettingsDefaultObject.kDefaultConfigFolder + "/" +
                    AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName + ".asset");
            }

            if (settings == null)
            {
                Debug.LogError("[S][Build] 磁盘上没有 AddressableAssetSettings，内容包无法构建");
                EditorApplication.Exit(1);
                return;
            }

            AddressableAssetSettings.BuildPlayerContent(out var buildData);

            // 构建链是异步的，立刻查产物一定查不到。等它跑完再断言。
            EditorApplication.delayCall += () =>
            {
                string dir = "Library/com.unity.addressables/aa/Windows";
                int bundles = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories).Length : 0;

                Debug.Log($"[S][Build] 内容构建 {(buildData == null ? "失败（buildData 为空）" : "返回值 ok")}，" +
                          $"{dir} 内 bundle = {bundles}");

                if (buildData == null || bundles == 0)
                {
                    Debug.LogError("[S][Build] Addressables 内容包为空 —— 运行时必缺资源");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"RESULT Addressables 内容 bundle {bundles} 个");
                EditorApplication.Exit(0);
            };
        }

        public static void BuildBatch()
        {
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x);

            // 内容包已由 check.ps1 先行构建（见 BuildContentBatch）；这里只校验产物在，
            // 不在就直接失败 —— 不在这里重跑，避免依赖 EditorApplication.update 的异步链
            // 与 -quit 打架（那个坑见 BuildContentBatch 的注释）。
            string dir = "Library/com.unity.addressables/aa/Windows";
            int bundles = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories).Length : 0;
            if (bundles == 0)
            {
                Debug.LogError($"[S][Build] {dir} 里没有 bundle，先跑 -executeMethod S.EditorTools.BuildPlayer.BuildContentBatch");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[S][Build] Addressables 内容已就绪：{bundles} 个 bundle");

            BuildWindows();
        }
    }
}

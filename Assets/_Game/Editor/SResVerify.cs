using System.Text;
using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// Addressables 预加载的**端到端探针**。
    ///
    /// 为什么需要它：这次把资产从 `Resources/` 搬到 `Assets/_Game/Res/` 并改走
    /// Addressables，最容易出的错是「分组建了但预加载取不到」——
    /// 那种情况编译全绿、打包也可能成功，只有界面降级成纯色块、模型变胶囊，
    /// 靠肉眼翻十几张截图才能发现。这里把「预加载 → 同步查表 → 装配配置」整条链
    /// 在一个批处理里跑完，逐项断言。
    ///
    /// 跑法：-executeMethod S.EditorTools.SResVerify.VerifyBatch
    /// </summary>
    public static class SResVerify
    {
        public static void VerifyBatch()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Addressables 预加载契约 ===");

            int pass = 0, fail = 0;
            void Check(string name, bool ok, string detail = null)
            {
                if (ok) { pass++; sb.AppendLine($"PASS {name}" + (detail != null ? $"  {detail}" : "")); }
                else { fail++; sb.AppendLine($"FAIL {name}" + (detail != null ? $"  {detail}" : "")); }
            }

            // 1) 分组文件存在（分组没建时后面全是无意义的失败）
            // 直接查落盘资产：batchmode 下 DefaultObject.GetSettings 可能还没实例化出对象，
            // 传 false 拿到 null 会让人误判「分组不存在」，而实际文件都在（已验证过）。
            const string settingsPath = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(false);
            bool hasSettings = settings != null || AssetDatabase.LoadAssetAtPath<UnityEditor.AddressableAssets.Settings.AddressableAssetSettings>(settingsPath) != null;
            Check("AddressableAssetSettings 存在", hasSettings,
                hasSettings ? settingsPath : $"{settingsPath} 缺失（先跑 S/资源/构建 Addressables 分组）");

            // 2) 预加载
            S.View.SRes.PreloadAll();
            Check("预加载已完成", S.View.SRes.IsLoaded, $"载入 {S.View.SRes.Count} 件");
            Check("预加载无失败", !S.View.SRes.HasFailed);

            // 3) 逐类抽查：每类至少取到一件，缺哪类一眼能看出
            Check("正式地图可取", S.View.SRes.Get<GameObject>(S.View.FoundryArena.ActiveAssetName) != null);
            foreach (var name in S.View.MusicEnemyAssets.AllCharacters)
                Check("敌人预制体可取 "+name, S.View.SRes.Get<GameObject>(name) != null);
            Check("模型可取", S.View.SRes.Get<GameObject>("SW01_Violin") != null, "SW01_Violin");
            Check("贴图可取", S.View.SRes.Get<Texture2D>("tex_floor_concrete") != null, "tex_floor_concrete");
            Check("UI 素材可取", S.View.SRes.Get<Sprite>("panel") != null, "panel");
            Check("配件图标可取", S.View.SRes.Get<Sprite>("item_pierce") != null, "item_pierce");

            // 诊断：把已载入的 UI 类资产名打几个出来，看 Sprite 到底以什么名字登记
            sb.AppendLine("---- 诊断：已载入的 UI 类资产 ----");
            int shown = 0;
            foreach (var kv in S.View.SRes.DumpAll())
            {
                var t = kv.Value == null ? "null" : kv.Value.GetType().Name;
                if (kv.Key.StartsWith("panel") || kv.Key.StartsWith("item_pierce") || kv.Key.StartsWith("banner"))
                {
                    sb.AppendLine($"     '{kv.Key}' -> {t}");
                    shown++;
                }
            }
            if (shown == 0)
            {
                int n = 0;
                foreach (var kv in S.View.SRes.DumpAll())
                {
                    sb.AppendLine($"     sample '{kv.Key}' -> {(kv.Value == null ? "null" : kv.Value.GetType().Name)}");
                    if (++n >= 10) break;
                }
            }
            sb.AppendLine($"     （Sprite 总数 {S.View.SRes.CountOf<Sprite>()}，Texture2D 总数 {S.View.SRes.CountOf<Texture2D>()}，载入总数 {S.View.SRes.Count}）");
            Check("UI 预制体可取", S.View.SRes.Get<GameObject>("HUD") != null, "HUD");
            Check("字体可取", S.View.SRes.Get<Font>("UiRounded") != null, "UiRounded");
            Check("配置表可取", S.View.SRes.Get<TextAsset>("Enemy") != null, "Enemy.tsv");
            Check("材质可取", S.View.SRes.Get<Material>("mat_ui_additive") != null, "mat_ui_additive");

            // 4) 配置装配（这一步通过 = 整条 Cfg 链路通）
            var data = S.Core.SDataLoader.Reload();
            Check("SData 装配成功", data != null);
            if (data != null)
            {
                Check("枪非空", data.GunOrder.Count > 0, $"{data.GunOrder.Count} 把");
                Check("敌人非空", data.EnemyOrder.Count > 0, $"{data.EnemyOrder.Count} 种");
                Check("时间轴非空", data.Director != null && data.Director.Phases.Count > 0);
            }

            // 5) 全量清点：预加载件数应当 >= 分组件数（同组内可能有依赖被一并带入）
            int expected = 0;
            if (settings != null)
            {
                foreach (var g in settings.groups)
                {
                    if (g != null && g.name.StartsWith("S_")) expected += g.entries.Count;
                }
            }
            Check("预加载件数覆盖分组", S.View.SRes.Count >= expected,
                $"预加载 {S.View.SRes.Count} / 分组 {expected}");

            sb.AppendLine($"RESULT Addressables 预加载 {pass}/{pass + fail} passed");
            Debug.Log(sb.ToString());

            if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}

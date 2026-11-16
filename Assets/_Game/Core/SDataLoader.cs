using System.Collections.Generic;
using UnityEngine;

namespace S.Core
{
    /// <summary>
    /// Unity 侧的桥：从 Resources 读 `Data/Cfg/*.tsv`，交给 SCfgLoader 装配。
    /// Core 本身不依赖 UnityEngine，只有这个文件把两边接起来。
    ///
    /// 为什么用 Resources 文本而不是自定义 ScriptedImporter：
    /// 本工程没有 Addressables，表只有 11 张、总量几十 KB，
    /// 引入 `.tsv` 导入器（识别 + meta 维护 + 平台差异）的收益抵不上成本。
    /// 若以后要给玩家热更配置，换 Addressables 只需改 <see cref="SCfgResourcesSource"/>。
    /// </summary>
    public static class SDataLoader
    {
        static SData s_cache;
        static bool s_hookInstalled;

        public static SData Get()
        {
            if (s_cache != null) return s_cache;

            InstallLogHook();

            var source = new SCfgResourcesSource();
            try
            {
                // SCfgLoader.Load 内部会跑校验：有 Error 直接抛，不返回半套数据
                s_cache = SCfgLoader.Load(source);

                Debug.Log("[S][Cfg] 配置已加载 枪 " + s_cache.GunOrder.Count +
                          " / 子弹 " + s_cache.BulletOrder.Count +
                          " / 衍生 " + s_cache.ModifierOrder.Count +
                          " / 敌人 " + s_cache.EnemyOrder.Count);
            }
            catch (System.Exception ex)
            {
                // 配置坏了必须一眼看到。不吞异常、也不让游戏带着半套数据跑 ——
                // 半套数据（如敌人 0 血）在实机上表现成「敌人打不死」，
                // 比直接黑屏更难查。
                Debug.LogError("[S][Cfg] 配置加载失败，配件与刷怪系统无法启动\n" + ex);
                s_cache = null;
            }

            return s_cache;
        }

        /// <summary>编辑器/自检用：强制重读。</summary>
        public static SData Reload()
        {
            s_cache = null;
            return Get();
        }

        /// <summary>把 Core 的日志钩子接到 Unity 控制台。幂等。</summary>
        static void InstallLogHook()
        {
            if (s_hookInstalled) return;
            s_hookInstalled = true;
            SLog.Warn = msg => Debug.LogWarning(msg);
            SLog.Error = msg => Debug.LogError(msg);
        }
    }

    /// <summary>从 `Assets/Resources/Data/Cfg/` 取 TSV 文本。读过的表会缓存。</summary>
    public sealed class SCfgResourcesSource : ISCfgSource
    {
        public string RootKey => "Assets/Resources/" + SCfgAddresses.ResourceFolder;

        readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

        public bool TryGetText(string table, out string text)
        {
            if (_cache.TryGetValue(table, out text)) return text != null;

            var asset = Resources.Load<TextAsset>(SCfgAddresses.ResourcePath(table));
            text = asset != null ? asset.text : null;
            _cache[table] = text;
            return text != null;
        }
    }
}

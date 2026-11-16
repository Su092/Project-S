using System.Collections.Generic;
using UnityEngine;

namespace S.Core
{
    /// <summary>
    /// Unity 侧的桥：从 Addressables（`Assets/_Game/Res/Cfg/*.tsv`）取表，交给 SCfgLoader 装配。
    /// Core 本身不依赖 UnityEngine，只有这个文件把两边接起来。
    ///
    /// 加载机制变更说明：原先走 `Resources.Load<TextAsset>`，资产搬出 `Resources/` 后
    /// 改为 Addressables 预加载（见 <see cref="S.View.SRes"/>）。表是**同步必需**的
    /// （Bootstrap 在 Awake 里就要拿到），所以和美术资产一样走预加载 + 查表。
    /// </summary>
    public static class SDataLoader
    {
        static SData s_cache;
        static bool s_hookInstalled;

        public static SData Get()
        {
            if (s_cache != null) return s_cache;

            InstallLogHook();

            // 兜底：正常路径是 Bootstrap 先跑 SRes.PreloadAll()。但如果有人直接从
            // 编辑器菜单/测试方法里调 SDataLoader（如 SCfgImportVerify），预加载还没发生，
            // 表就取不到，表现成「配置加载失败」而非「没预加载」—— 假故障很难查。
            // 这里补一次，PreloadAll 自身幂等。
            if (!S.View.SRes.IsLoaded) S.View.SRes.PreloadAll();

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

    /// <summary>从 Addressables（预加载到 <see cref="S.View.SRes"/>）取 TSV 文本。读过的表会缓存。</summary>
    public sealed class SCfgResourcesSource : ISCfgSource
    {
        public string RootKey => SCfgAddresses.AssetFolder;

        readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

        public bool TryGetText(string table, out string text)
        {
            if (_cache.TryGetValue(table, out text)) return text != null;

            var asset = S.View.SRes.Get<TextAsset>(table);
            text = asset != null ? asset.text : null;
            _cache[table] = text;
            return text != null;
        }
    }
}

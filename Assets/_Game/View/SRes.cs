using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace S.View
{
    /// <summary>
    /// 资源加载门面：**Addressables 分组 + 启动预加载 + 同步查表**。
    ///
    /// 为什么要这层：
    ///   资产已经搬出 `Resources/`（对齐参考工程 `Assets/_Game/Res/`），
    ///   `Resources.Load` 取不到任何东西了。而 Addressables 的公开 API 是**异步**的，
    ///   但游戏侧有约 10 处调用点是同步的（`SAssets.Model(name)` 直接返回 GameObject），
    ///   把它们全改成 async 会把世界构建流程（竞技场、刷怪、武器）连根拔起。
    ///
    /// 所以采用三层结构：
    ///   1. `PreloadAll()` —— 启动时一次性把全部资源加载并持有句柄（总量约 34 MB，可接受）
    ///   2. 本类维护 `地址 → 资产` 的同步字典
    ///   3. `SAssets` / `UiAssets` / `UiPrefab` / `UiKit` 继续用原签名，内部改成查本表
    ///
    /// 为什么不走「按需 LoadAssetAsync + 协程等待」：那会让每个调用点都要处理
    /// 「资源还没到」的中间态，而本工程是**垂直切片**，资源总量小、全部一次性进内存
    /// 反而更简单可靠 —— 缺件仍然可查、可断言、可降级，与原先 `Resources.Load` 的
    /// 语义（取不到就回退占位）完全一致。
    ///
    /// ⚠ 句柄必须持有到进程结束。释放句柄会把资产卸载，界面会变空白而**不报错**。
    /// </summary>
    public static class SRes
    {
        /// <summary>资产根目录（相对工程根）。地址表与分组脚本都以它为基准。</summary>
        public const string Root = "Assets/_Game/Res/";

        // ---- 地址前缀：与 Addressables 分组脚本（SAddressablesSetup）里的标签一一对应 ----
        public const string PrefixModel = "model/";
        public const string PrefixTexture = "tex/";
        public const string PrefixUi = "ui/";
        public const string PrefixUiPrefab = "uiprefab/";
        public const string PrefixFont = "font/";
        public const string PrefixCfg = "cfg/";
        public const string PrefixMat = "mat/";

        static readonly Dictionary<string, Object> s_map = new Dictionary<string, Object>();
        /// <summary>按 Addressables 地址建立的索引（ui/panel → Sprite）。
        /// 为什么需要它：Sprite 资产被导入后 Unity 会改 `asset.name`（子精灵名带 `_0` 后缀之类），
        /// 按资产名查会漏。地址是文件名派生的，稳定不乱（踩过）。</summary>
        static readonly Dictionary<string, Object> s_byAddress = new Dictionary<string, Object>();
        static readonly List<AsyncOperationHandle> s_handles = new List<AsyncOperationHandle>();
        static bool s_loaded;
        static bool s_failed;

        /// <summary>预加载是否已完成（自检与断言用）。</summary>
        public static bool IsLoaded => s_loaded;

        /// <summary>预加载是否出过错（有错时各处会走回退路径）。</summary>
        public static bool HasFailed => s_failed;

        /// <summary>已载入的资产数（自检用）。</summary>
        public static int Count => s_map.Count;

        /// <summary>
        /// 启动时调用一次：按标签批量加载全部资产。
        ///
        /// ⚠ 必须**按类型**分批加载，不能一律用 `LoadAssetsAsync&lt;Object&gt;`：
        /// 用父类型 `Object` 加载贴图时拿回来的是 **Texture2D**，不是 Sprite ——
        /// 于是 72 张 UI 图全部取不到（Sprite 计数为 0），界面退化成纯色块，
        /// 而「载入 109 件、无失败」看起来完全正常。这个坑实测过。
        /// 所以 UI 图（已设 Sprite 导入类型）单独用 `LoadAssetsAsync&lt;Sprite&gt;` 加载。
        /// </summary>
        public static void PreloadAll()
        {
            if (s_loaded) return;

            // Sprite 必须精确类型加载，见方法注释
            LoadBatch<Sprite>(Req.Ui);
            // 其余都是各自唯一的资产类型，用 Object 安全
            LoadBatch<Object>(Req.Model);
            LoadBatch<Object>(Req.Texture);
            LoadBatch<Object>(Req.UiPrefab);
            LoadBatch<Object>(Req.Font);
            LoadBatch<Object>(Req.Cfg);
            LoadBatch<Object>(Req.Mat);

            s_loaded = true;
            Debug.Log($"[S][Res] 预加载完成：{s_map.Count} 件（Sprite {CountOf<Sprite>()} / " +
                      $"Texture2D {CountOf<Texture2D>()} / GameObject {CountOf<GameObject>()}）");
        }

        /// <summary>按标签 + 精确类型加载一批，登记进同步查表。</summary>
        static void LoadBatch<T>(string label) where T : Object
        {
            AsyncOperationHandle<System.Collections.Generic.IList<T>> handle = default;
            try
            {
                handle = Addressables.LoadAssetsAsync<T>(label, null);
                handle.WaitForCompletion();
            }
            catch (System.Exception ex)
            {
                s_failed = true;
                Debug.LogError($"[S][Res] 标签 {label} 加载异常：{ex.Message}");
                return;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                s_failed = true;
                Debug.LogError($"[S][Res] 标签 {label} 加载失败（分组可能未构建，跑 S/资源/构建 Addressables 分组）");
                return;
            }

            // 句柄必须留着：释放 = 卸载资产
            s_handles.Add(handle);

            foreach (var asset in handle.Result)
            {
                if (asset == null) continue;
                // 用资产名做键，与原先「按名字取」的调用形状保持一致
                if (!string.IsNullOrEmpty(asset.name) && !s_map.ContainsKey(asset.name))
                    s_map[asset.name] = asset;
            }
        }

        /// <summary>按资产名取已载入的资产；未命中返回 null（调用方回退占位）。</summary>
        public static T Get<T>(string name) where T : Object
        {
            if (string.IsNullOrEmpty(name)) return null;
            return s_map.TryGetValue(name, out var o) ? o as T : null;
        }

        public static bool Has(string name) => !string.IsNullOrEmpty(name) && s_map.ContainsKey(name);

        /// <summary>诊断用：只读遍历已载入的资产。</summary>
        public static IEnumerable<KeyValuePair<string, Object>> DumpAll() => s_map;

        /// <summary>诊断用：某类型已载入的件数。</summary>
        public static int CountOf<T>() where T : Object
        {
            int n = 0;
            foreach (var kv in s_map) if (kv.Value is T) n++;
            return n;
        }

        /// <summary>
        /// 自检用：某名字应当存在却取不到时，把诊断信息说全。
        /// 缺件的典型原因是「新资产没进分组」，光说「取不到」定位不了。
        /// </summary>
        public static string Diagnose(string name)
        {
            if (!s_loaded) return $"预加载未执行（SRes.PreloadAll 未被调用），无法取 {name}";
            if (s_failed) return $"预加载有失败项，{name} 可能在其中（看上面的 [S][Res] 报错）";
            return $"已预加载 {s_map.Count} 件，但没有 {name} —— 检查它是否被加进 Addressables 分组";
        }

        /// <summary>仅供编辑器/自检：清空缓存并释放句柄（会卸载资产，不要在有界面时调用）。</summary>
        public static void Reset()
        {
            foreach (var h in s_handles)
            {
                if (h.IsValid()) Addressables.Release(h);
            }
            s_handles.Clear();
            s_map.Clear();
            s_loaded = false;
            s_failed = false;
        }
    }

    /// <summary>
    /// Addressables 标签名（**唯一真相**）。
    ///
    /// 分组脚本 `SAddressablesSetup` 与运行时预加载都从这里取，
    /// 杜绝「脚本打标签 A、代码按标签 B 加载」这种静默不一致
    /// —— 那种错的表现是「资源全缺」，但编译、打包全绿。
    /// </summary>
    public static class Req
    {
        public const string Model = "s_model";
        public const string Texture = "s_texture";
        public const string Ui = "s_ui";
        public const string UiPrefab = "s_uiprefab";
        public const string Font = "s_font";
        public const string Cfg = "s_cfg";
        public const string Mat = "s_mat";
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using S.View;

namespace S.EditorTools
{
    /// <summary>
    /// 按目录自动生成 Addressables 分组、地址与标签。
    ///
    /// 为什么需要脚本而不是手拖：
    ///   参考工程 `Haunted Match 3` 有 4224 张贴图、72 个预制体，它的分组是
    ///   `LiteAddressablesSetup.cs` 按**文件夹**批量登记出来的。本工程资产虽少
    ///   （110 件），但「新增一张图忘了拖进分组」的表现是**静默缺件**
    ///   —— 界面退化成纯色块、模型退化成胶囊，编辑器不报错、打包也成功。
    ///   所以分组必须由目录推导，且能在 `check.ps1` 里一键重建并断言。
    ///
    /// 约定（与 <see cref="S.View.SRes"/> 的标签常量一一对应）：
    ///   目录                      分组名           标签          地址
    ///   Res/Model/                 S_Model          s_model       model/&lt;文件名&gt;
    ///   Res/Texture/*.jpg|png      S_Texture        s_texture     tex/&lt;文件名&gt;
    ///   Res/Texture/UI/**          S_Ui             s_ui          ui/&lt;文件名&gt;
    ///   Res/UIPrefabs/             S_UiPrefab       s_uiprefab    uiprefab/&lt;文件名&gt;
    ///   Res/Font/                  S_Font           s_font        font/&lt;文件名&gt;
    ///   Res/Cfg/                   S_Cfg            s_cfg         cfg/&lt;文件名&gt;
    ///   Res/Shaders/               S_Mat            s_mat         mat/&lt;文件名&gt;
    ///
    /// ⚠ 地址用**文件名**（不含扩展名）而不是完整路径：运行时 `SRes` 按资产名取件，
    /// 与原先 `Resources.Load("Models/X")` 的形状一致。前提是资产名全局唯一
    /// —— 本工程 72 张 UI 图、12 个模型、11 张表均已验证无重名，
    /// 由 <see cref="VerifyUnique"/> 在构建时断言，重名直接失败而不是静默覆盖。
    /// </summary>
    public static class SAddressablesSetup
    {
        const string Root = "Assets/_Game/Res/";

        /// <summary>目录 → (分组名, 标签, 地址前缀, 是否递归)</summary>
        sealed class Rule
        {
            public string Dir;
            public string Group;
            public string Label;
            public string Prefix;
            public bool Recursive;
            public string[] Extensions;
        }

        static readonly Rule[] Rules =
        {
            new Rule { Dir = Root + "Prefab/Enemies", Group = "S_Enemy", Label = Req.Enemy, Prefix = SRes.PrefixEnemy, Recursive = false, Extensions = new[] { ".prefab" } },
            new Rule { Dir = Root + "Prefab/Maps", Group = "S_Map", Label = Req.Map, Prefix = SRes.PrefixMap, Recursive = false, Extensions = new[] { ".prefab" } },
            new Rule { Dir = Root + "Prefab/Weapons", Group = "S_Weapon", Label = Req.Model, Prefix = SRes.PrefixModel, Recursive = false, Extensions = new[] { ".prefab" } },
            new Rule { Dir = Root + "Model",      Group = "S_Model",    Label = Req.Model,    Prefix = SRes.PrefixModel,    Recursive = true,  Extensions = new[] { ".fbx", ".prefab" } },
            new Rule { Dir = Root + "Texture/VFX", Group = "S_VfxTexture", Label = Req.Texture, Prefix = SRes.PrefixTexture, Recursive = true, Extensions = new[] { ".png", ".jpg" } },
            new Rule { Dir = Root + "Texture/UI", Group = "S_Ui",       Label = Req.Ui,       Prefix = SRes.PrefixUi,       Recursive = true,  Extensions = new[] { ".png", ".jpg" } },
            new Rule { Dir = Root + "Texture",    Group = "S_Texture",  Label = Req.Texture,  Prefix = SRes.PrefixTexture,  Recursive = false, Extensions = new[] { ".png", ".jpg" } },
            new Rule { Dir = Root + "UIPrefabs",  Group = "S_UiPrefab", Label = Req.UiPrefab, Prefix = SRes.PrefixUiPrefab, Recursive = false, Extensions = new[] { ".prefab" } },
            new Rule { Dir = Root + "Font",       Group = "S_Font",     Label = Req.Font,     Prefix = SRes.PrefixFont,     Recursive = false, Extensions = new[] { ".ttf", ".otf" } },
            new Rule { Dir = Root + "Cfg",        Group = "S_Cfg",      Label = Req.Cfg,      Prefix = SRes.PrefixCfg,      Recursive = false, Extensions = new[] { ".tsv" } },
            new Rule { Dir = Root + "Shaders",    Group = "S_Mat",      Label = Req.Mat,      Prefix = SRes.PrefixMat,      Recursive = false, Extensions = new[] { ".mat" } },
        };

        // Req / SRes.Prefix* 来自运行时程序集，这里只做引用，避免字符串各写一份

        [MenuItem("S/资源/构建 Addressables 分组", false, 40)]
        public static void BuildMenu() => Build();

        /// <summary>
        /// 幂等重建：先清空本工程的分组（按 Rules 的名字），再按目录登记。
        ///
        /// 为什么要清空重建而不是增量：增量会留下「文件已删但条目还在」的幽灵条目，
        /// 那种条目在构建时会报错，或更糟 —— 打包成功但运行时取到空。全量重建最稳。
        /// </summary>
        public static void Build() => BuildInternal();

        static int BuildInternal()
        {
            // ⚠ 取 settings 的顺序必须是「磁盘资产优先」。
            // 原先是 DefaultObject → Create，而 batchmode 下 DefaultObject 常为 null，
            // 于是走到 Create() —— 它只建**内存实例**、不落盘。
            // 后果：紧接着调用静态的 AddressableAssetSettings.BuildPlayerContent（它会从磁盘重读）
            // 就报「Addressable Asset Settings does not exist. Failed to create.」，
            // 但**照样返回非 null 的 buildData、0 bundle、无异常** → 上层断言全绿而产物为空。
            var settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(
                AddressableAssetSettingsDefaultObject.kDefaultConfigFolder + "/" +
                AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName + ".asset");

            if (settings == null)
            {
                settings = AddressableAssetSettingsDefaultObject.Settings;
            }

            if (settings == null)
            {
                // 磁盘上确实没有：Create 会连带把资产落盘（createDefaultGroups 传 false 避免污染）
                settings = AddressableAssetSettings.Create(
                    AddressableAssetSettingsDefaultObject.kDefaultConfigFolder,
                    AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName,
                    false, true);
            }

            if (settings == null)
            {
                Debug.LogError("[S][Addr] 无法创建/找到 AddressableAssetSettings");
                return 0;
            }

            // 先查重：重名会让两个资产抢同一个地址，行为不确定
            if (!VerifyUnique(out string dupMsg))
            {
                Debug.LogError("[S][Addr] 资产重名，地址会冲突：\n" + dupMsg);
                return 0;
            }

            // 清掉本工程自己的分组（保留 Built In Data 等系统组）
            foreach (var rule in Rules)
            {
                var existing = settings.FindGroup(rule.Group);
                if (existing != null) settings.RemoveGroup(existing);
            }

            int total = 0;
            foreach (var rule in Rules)
            {
                if (!Directory.Exists(rule.Dir))
                {
                    Debug.LogWarning($"[S][Addr] 跳过不存在的目录：{rule.Dir}");
                    continue;
                }

                var group = settings.CreateGroup(rule.Group, false, false, false, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

                // 构建参数：本地包（单机游戏，不走远程 CDN）
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null)
                {
                    // ⚠ 用 SetVariableByName 前必须确保 profile 变量存在且**有值**。
                    // 批处理里新建的 settings 变量可能是空值，那样 m_BuildPath 会写入
                    // 一个只有 m_Id 没有 m_Value 的引用，内容构建于是静默失败
                    // （没有任何报错日志，buildData 也非空，只是不产出 bundle）。
                    EnsureLocalPaths(settings);

                    schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                    schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
                    schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                    schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
                }

                var option = rule.Recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                int n = 0;
                foreach (var file in Directory.GetFiles(rule.Dir, "*", option))
                {
                    if (file.EndsWith(".meta")) continue;

                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (System.Array.IndexOf(rule.Extensions, ext) < 0) continue;

                    string path = file.Replace('\\', '/');
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogWarning($"[S][Addr] 无 GUID，跳过：{path}");
                        continue;
                    }

                    var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                    if (entry == null) continue;

                    string name = Path.GetFileNameWithoutExtension(path);
                    entry.address = rule.Prefix + name;
                    entry.SetLabel(rule.Label, true, true, false);
                    n++;
                    total++;
                }

                Debug.Log($"[S][Addr] {rule.Group,-12} {n,3} 件  ({rule.Dir})");
            }

            EditorUtility.SetDirty(settings);

            // 新建设置时资产可能还没有 .meta，SaveAssets 会顺带刷出来。
            // 但不保证 DefaultObject 指针已指向它 —— 而静态的 BuildPlayerContent
            // 正是通过这一层去磁盘取 settings。显式注册一次，把这个洞堵死。
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var onDisk = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(
                AddressableAssetSettingsDefaultObject.kDefaultConfigFolder + "/" +
                AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName + ".asset");
            if (onDisk == null)
            {
                Debug.LogError("[S][Addr] 分组已建但 settings 未落盘，内容构建会静默产出 0 bundle");
                return 0;
            }

            AddressableAssetSettingsDefaultObject.Settings = onDisk;

            Debug.Log($"[S][Addr] 分组重建完成，共 {total} 件（settings 已落盘）");
            return total;
        }

        /// <summary>
        /// 确保 LocalBuildPath / LocalLoadPath 两个 profile 变量有值。
        ///
        /// 为什么单独做：Addressables 默认 profile 的这两个变量在某些模板下是空字符串，
        /// 此时 `SetVariableByName` 仍会把引用写进 schema，但值是空的 ——
        /// 内容构建**不报错、不产 bundle**，只在运行时表现为「全部资源缺失」。
        /// 这是整套方案里最难定位的一类静默失败。
        /// </summary>
        static void EnsureLocalPaths(AddressableAssetSettings settings)
        {
            string profileId = settings.activeProfileId;

            void Ensure(string name, string value)
            {
                string cur = settings.profileSettings.GetValueByName(profileId, name);
                if (string.IsNullOrEmpty(cur))
                {
                    settings.profileSettings.SetValue(profileId, name, value);
                    Debug.Log($"[S][Addr] 补 profile 变量 {name} = {value}");
                }
            }

            Ensure(AddressableAssetSettings.kLocalBuildPath, "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]");
            Ensure(AddressableAssetSettings.kLocalLoadPath, "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]");
        }

        /// <summary>
        /// 断言资产名全局唯一（地址以文件名为键）。
        /// 重名时两个资产抢同一个地址，运行时取到哪个不确定 —— 必须拦在构建前。
        /// </summary>
        public static bool VerifyUnique(out string message)
        {
            var seen = new Dictionary<string, List<string>>();
            foreach (var rule in Rules)
            {
                if (!Directory.Exists(rule.Dir)) continue;
                var option = rule.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.GetFiles(rule.Dir, "*", option))
                {
                    if (file.EndsWith(".meta")) continue;
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (System.Array.IndexOf(rule.Extensions, ext) < 0) continue;

                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!seen.TryGetValue(name, out var list))
                    {
                        list = new List<string>();
                        seen[name] = list;
                    }
                    list.Add(file.Replace('\\', '/'));
                }
            }

            var bad = new List<string>();
            foreach (var kv in seen)
            {
                if (kv.Value.Count > 1) bad.Add($"  {kv.Key}\n    " + string.Join("\n    ", kv.Value));
            }

            if (bad.Count > 0)
            {
                message = string.Join("\n", bad);
                return false;
            }

            message = $"资产名唯一（{seen.Count} 个名字）";
            return true;
        }

        /// <summary>
        /// 构建 Addressables 内容包（生成 ServerData / StreamingAssets 里的 bundle 与 catalog）。
        ///
        /// ⚠ 这一步不能省：分组只描述「谁进哪个包」，真正的 bundle 由这里产出。
        /// 只建分组不构建的话，运行时 Play 会走「Use Asset Database」碰巧能取到，
        /// 但**打包出来的 exe 里没有任何资源** —— 表现是启动即黑屏报缺表。
        /// </summary>
        public static void BuildContentMenu() => BuildContent();

        public static void BuildContent()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                Debug.LogError("[S][Addr] 没有 AddressableAssetSettings，先跑「构建 Addressables 分组」");
                return;
            }

            // 先重建分组，保证内容与目录一致（避免幽灵条目）
            BuildInternal();

            AddressableAssetSettings.BuildPlayerContent(out var buildData);
            Debug.Log($"[S][Addr] 内容构建完成：{buildData} 个 bundle");
        }

        /// <summary>
        /// 分组 + 内容包一次做完（编辑器菜单用，同步返回）。
        ///
        /// ⚠ 命令行别用这个 —— 内容构建是异步任务链，必须在
        /// `EditorApplication.delayCall` 之后才能校验产物。batchmode 请用
        /// `BuildPlayer.BuildContentBatch`。
        /// </summary>
        public static void BuildContentBatch()
        {
            BuildInternal();

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                Debug.LogError("[S][Addr] 没有 AddressableAssetSettings");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            AddressableAssetSettings.BuildPlayerContent(out var buildData);
            Debug.Log($"[S][Addr] 内容构建完成：{buildData} 个 bundle");
        }

        /// <summary>
        /// 把 Play Mode Script 设为「Use Asset Database」。
        ///
        /// 为什么必须显式设：默认值在部分工程里是「Use Existing Build」，
        /// 那种模式下编辑器里取资源需要先跑一遍内容构建，否则**取不到任何东西**，
        /// 而报错只有一句「无法加载」，根本看不出是这个原因（踩过）。
        /// 用 Asset Database 模式，编辑器里直接走资产库，改图立刻生效，迭代最快。
        ///
        /// 打包不受影响：`BuildPlayerContent` 与 Player 构建永远走真实 bundle。
        /// </summary>
        public static void UseAssetDatabasePlayMode()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null) return;

            var dbField = typeof(AddressableAssetSettings)
                .GetField("m_ActivePlayModeDataBuilderIndex",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            // 按 DataBuilder 名字找 FastMode（= Use Asset Database）的下标
            int idx = -1;
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                var b = settings.DataBuilders[i];
                if (b != null && b.name.Contains("Fast")) { idx = i; break; }
            }

            if (idx < 0) return;

            dbField?.SetValue(settings, idx);
            EditorUtility.SetDirty(settings);
            Debug.Log($"[S][Addr] Play Mode = {settings.DataBuilders[idx].name}（Use Asset Database）");
        }

        /// <summary>batchmode / check.ps1 入口：只重建分组（不打包），输出可断言的计数。</summary>
        public static void BuildBatch()
        {
            int written = BuildInternal();

            bool ok = VerifyUnique(out string msg);

            // 从落盘的组资产读条目数：同一次批处理里 settings 的内存引用刷不过来，
            // 实测 FindGroup 数出 0，而磁盘上 109 条都在。所以直接数 .asset 文件。
            int entries = 0;
            string groupsDir = "Assets/AddressableAssetsData/AssetGroups";
            foreach (var rule in Rules)
            {
                string path = groupsDir + "/" + rule.Group + ".asset";
                if (!File.Exists(path)) continue;
                var group = AssetDatabase.LoadAssetAtPath<AddressableAssetGroup>(path);
                if (group != null) entries += group.entries.Count;
            }

            Debug.Log($"VERIFY 资产名唯一 {ok}（{msg}）");
            Debug.Log($"RESULT Addressables 分组 {entries} 件（本次写入 {written}）");

            // 顺带把 Play Mode 设成 Asset Database 模式，省得下次在编辑器里取不到资源
            UseAssetDatabasePlayMode();
        }
    }
}

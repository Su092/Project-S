using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>
    /// UI 预制体加载入口。
    ///
    /// 为什么要预制体：此前所有界面都是运行时 `new GameObject` + `AddComponent` 拼出来的，
    /// 布局只存在于代码里 —— 想在编辑器里微调一个间距，必须改代码、重编译、重进 Play。
    /// 现在结构（层级/锚点/颜色/九宫格/字体）全部落在 `Assets/Resources/UI/Prefabs/*.prefab`，
    /// 运行时代码只负责**填数据与挂事件**，不再决定界面长什么样。
    ///
    /// 加载路径：资产在 `Assets/_Game/Res/UIPrefabs/`，由 Addressables 预加载进
    /// <see cref="SRes"/>，这里按名字查表（详见 SRes 类注释里为什么不用 async）。
    ///
    /// 注意：所有挂进预制体的 MonoBehaviour（各类 `*Refs`）都**单独成文件**。
    /// Unity 反序列化预制体时按「文件名 = 类名」定位脚本，
    /// 多个 MonoBehaviour 挤在一个文件里会导致预制体里只剩「Missing (Mono Script)」。
    /// </summary>
    public static class UiPrefab
    {
        /// <summary>编辑器侧用的资产目录（拼接 AssetDatabase 路径）。运行时不用它取件。</summary>
        public const string AssetDir = SRes.Root + "UIPrefabs/";

        public const string Menu = "Menu";
        public const string Hud = "HUD";
        public const string Inventory = "Inventory";
        public const string Offer = "Offer";
        public const string ItemCard = "ItemCard";
        public const string OfferCard = "OfferCard";

        static readonly System.Collections.Generic.Dictionary<string, GameObject> s_cache =
            new System.Collections.Generic.Dictionary<string, GameObject>();

        public static GameObject Load(string name)
        {
            if (s_cache.TryGetValue(name, out var cached) && cached != null) return cached;

            var go = SRes.Get<GameObject>(name);
            if (go == null)
                Debug.LogError($"[S] 缺少 UI 预制体：{name}（{SRes.Diagnose(name)}）");

            s_cache[name] = go;
            return go;
        }

        /// <summary>实例化一个 UI 预制体到 parent 下，保持预制体的本地变换。</summary>
        public static GameObject Spawn(string name, Transform parent, string instanceName = null)
        {
            var prefab = Load(name);
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, parent, false);
            go.name = string.IsNullOrEmpty(instanceName) ? prefab.name : instanceName;
            return go;
        }
    }

    /// <summary>UI 公共设施：只处理「基础设施」（事件系统），不构建任何界面内容。</summary>
    public static class UiUtil
    {
        /// <summary>
        /// 保证场景里有 EventSystem，且挂的是新输入系统的输入模块。
        ///
        /// 为什么必须检查：本工程 `activeInputHandler=1`（仅新输入系统），
        /// 若误挂 StandaloneInputModule，光标能动但点击/拖拽全部无人处理 ——
        /// 表现就是「鼠标没反应」，这个坑踩过两次。
        /// </summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;

            // 场景里可能已有一个未激活的，优先启用而不是再建一个
            var existing = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                if (existing.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
                    existing.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                return;
            }

            var go = new GameObject("EventSystem");
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}

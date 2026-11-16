using System.Collections.Generic;
using UnityEngine;

namespace S.View
{
    /// <summary>
    /// 模型与贴图的统一加载入口。
    ///
    /// 关键设计：**模型可以缺，游戏不能崩**。
    /// 美术资产是逐步接入的，任何一件缺失时都必须回退到程序化占位体，
    /// 否则一次资产漏拷就让整局黑屏（初版已踩过 URP 着色器剥离的同类坑）。
    ///
    /// 路径约定（资产已搬出 Resources，改由 Addressables 预加载，见 <see cref="SRes"/>）：
    ///   模型  Assets/_Game/Res/Model/<Name>     （FBX，由 blender_s_build_assets.py 产出）
    ///   贴图  Assets/_Game/Res/Texture/<Name>   （2k 平铺）
    ///
    /// ⚠ 这里仍按**资产名**取，不拼路径：SRes 以资产名（= 文件名去扩展名）为键，
    /// 与原先 `Resources.Load("Models/<Name>")` 的调用形状一致，调用点无需改动。
    /// </summary>
    public static class SAssets
    {
        /// <summary>编辑器侧用的资产目录（拼接 AssetDatabase 路径）。运行时不用它取件。</summary>
        public const string ModelDir = SRes.Root + "Model/";
        public const string TexDir = SRes.Root + "Texture/";

        static readonly Dictionary<string, GameObject> s_models = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Material> s_materials = new Dictionary<string, Material>();

        /// <summary>加载 FBX 预制体。缺失返回 null（调用方回退占位体）。</summary>
        public static GameObject Model(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_models.TryGetValue(name, out var cached)) return cached;

            var go = SRes.Get<GameObject>(name);
            s_models[name] = go;
            return go;
        }

        public static Texture2D Tex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_textures.TryGetValue(name, out var cached)) return cached;

            var t = SRes.Get<Texture2D>(name);
            s_textures[name] = t;
            return t;
        }

        /// <summary>
        /// 取一个带贴图的受光材质。贴图缺失时退回纯色。
        ///
        /// 注意：这里**不能**用 SMat.Lit 的共享缓存 —— 贴图材质需要各自的 _BaseMap 与平铺参数，
        /// 复用同一个实例会让不同区域的地面互相覆盖（踩过）。
        /// 因此本方法自己维护一份按 (贴图, 色调, smoothness) 缓存的独立材质表。
        /// </summary>
        public static Material Textured(string texName, Color tint, float smoothness = 0.35f,
            float tiling = 4f)
        {
            string key = $"{texName}|{tint.r:F2}{tint.g:F2}{tint.b:F2}|{smoothness:F2}|{tiling:F1}";
            if (s_materials.TryGetValue(key, out var m) && m != null) return m;

            // 基于模板新建（不走共享缓存，避免改到别人）
            m = SMat.NewLitInstance(smoothness);
            if (m != null)
            {
                var t = Tex(texName);
                if (t != null && m.HasProperty("_BaseMap"))
                {
                    m.SetTexture("_BaseMap", t);
                    m.SetTextureScale("_BaseMap", Vector2.one * tiling);
                }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                m.color = tint;
            }
            s_materials[key] = m;
            return m;
        }

        /// <summary>
        /// 把预制体实例化并适配到「给定的高度」。
        ///
        /// 为什么按高度适配：Blender 产出的模型尺寸已定，但敌人/枪/道具各自的
        /// 目标高度不同（爬行者 0.9m、盾卫 2.1m）。硬编码缩放系数在模型改了尺寸后
        /// 立刻失效，按包围盒高度归一才稳（与工作区 STGroundSnap 的思路一致）。
        /// </summary>
        public static GameObject SpawnFit(string modelName, Transform parent, Vector3 localPos,
            float targetHeight, Color tint, float smoothness = 0.35f, string texName = null,
            bool keepMaterials = true, bool groundAlign = true)
        {
            var prefab = Model(modelName);
            GameObject go;

            if (prefab != null)
            {
                go = Object.Instantiate(prefab, parent);
                go.name = modelName;

                // 模型自带多材质槽（Blender 里的分色）。默认保留，
                // 强行统一成单色会把参考图的分色设计全抹掉（踩过）。
                if (!keepMaterials)
                {
                    var renderers = go.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        var mat = texName != null
                            ? Textured(texName, tint, smoothness)
                            : SMat.Lit(tint, smoothness);
                        if (mat != null)
                            foreach (var r in renderers) r.sharedMaterial = mat;
                    }
                }

                // 按真实包围盒归一高度，并把最低点对准 localPos.y。
                // 顺序很重要：先缩放到目标高度，再量一次做定位，否则缩放会改变落点。
                //
                // ⚠ 绝对不要把 localRotation 清零（踩过：全模型躺倒）
                // FBX 导入时 Unity 会在根节点写一个 X≈-90°（270°）的旋转，
                // 那正是 Blender(Z 上) → Unity(Y 上) 的轴转换。清掉它之后
                // 「上」就变成了 Z 轴，敌人与柱子全部横躺，而按 Y 归一高度会
                // 把模型压扁 —— 表现为「所有模型都变小且倒在地上」。
                // 保留导入旋转后的约定就是 **上=+Y、前=+Z**（见 SModelContract 断言）。
                //
                // 只清零位置，旋转沿用导入值。
                go.transform.localPosition = Vector3.zero;

                var b = MeasureBoundsLocal(go);
                // 按「指定的主轴尺寸」归一。默认按高度（敌人/道具/柱子），
                // 枪械这类横长物件由调用方指定按长度归一，否则躺倒后会被缩得极短（踩过）。
                float measured = groundAlign ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (measured > 0.001f)
                    go.transform.localScale *= targetHeight / measured;

                var b2 = MeasureBoundsLocal(go);
                if (groundAlign)
                {
                    // 把最低点对准 localPos.y（敌人、道具、柱子用）
                    go.transform.localPosition = new Vector3(
                        localPos.x - b2.center.x,
                        localPos.y - b2.min.y,
                        localPos.z - b2.center.z);
                }
                else
                {
                    // 体心对准 localPos（第一人称枪械用：枪不该「站在地上」）
                    go.transform.localPosition = new Vector3(
                        localPos.x - b2.center.x,
                        localPos.y - b2.center.y,
                        localPos.z - b2.center.z);
                }
            }
            else
            {
                // 回退：胶囊占位（颜色仍按类型区分，保证可玩）
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = modelName + "_Fallback";
                go.transform.SetParent(parent, false);
                go.transform.localScale = new Vector3(targetHeight * 0.45f, targetHeight * 0.5f, targetHeight * 0.45f);
                go.transform.localPosition = localPos + Vector3.up * (targetHeight * 0.5f);
                SAssets.RemoveColliders(go);
                SMat.Apply(go.GetComponent<Renderer>(), SMat.Lit(tint, smoothness));
            }

            RemoveColliders(go);
            return go;
        }

        public static Bounds MeasureBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>
        /// 以「go 的父空间」量包围盒。定位前必须用这个而不是世界包围盒，
        /// 否则父节点已带偏移时落点会重复叠加（踩过）。
        /// </summary>
        public static Bounds MeasureBoundsLocal(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            var parent = go.transform.parent;
            Matrix4x4 toParent = parent != null
                ? parent.worldToLocalMatrix
                : Matrix4x4.identity;

            bool inited = false;
            var b = new Bounds();
            foreach (var r in rs)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                // 从渲染器局部包围盒直接转换，避免世界 AABB 经旋转后再次膨胀。
                var wb = r.localBounds;
                var meshToParent = toParent * r.transform.localToWorldMatrix;
                // 8 个角点变换到父空间
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3(
                        (i & 1) == 0 ? wb.min.x : wb.max.x,
                        (i & 2) == 0 ? wb.min.y : wb.max.y,
                        (i & 4) == 0 ? wb.min.z : wb.max.z);
                    var p = meshToParent.MultiplyPoint3x4(c);
                    if (!inited) { b = new Bounds(p, Vector3.zero); inited = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>纯视觉对象立即退出物理世界，不等 Destroy 在帧末执行。</summary>
        public static void RemoveColliders(GameObject go)
        {
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
        }

        /// <summary>静态障碍直接使用可见网格，保留凹槽和模型轴转换，不用放大的包围箱。</summary>
        public static void AddStaticMeshColliders(GameObject visual)
        {
            RemoveColliders(visual);
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }
        }

        /// <summary>自检用：统计已成功加载的模型数（缺件时能立刻看出来）。</summary>
        public static int LoadedModelCount()
        {
            int n = 0;
            foreach (var kv in s_models) if (kv.Value != null) n++;
            return n;
        }
    }
}

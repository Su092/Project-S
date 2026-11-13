using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// 模型朝向契约：**上=+Y、前=+Z**，且导入自带的轴转换必须保留。
    ///
    /// 为什么需要这个契约（踩过的坑）：
    ///   Blender 里是 Z 上、-Y 前。FBX 导出为 axis_forward=-Z / axis_up=Y，
    ///   Unity 导入后会在根节点带一个 X≈-90°（即 270°）的旋转 ——
    ///   那正是 Blender(Z 上) → Unity(Y 上) 的轴转换，**不是「多余的旋转」**。
    ///   早先 SAssets.SpawnFit 里写了一句 `localRotation = identity`，
    ///   把这个转换抹掉了，结果：
    ///     · 所有模型的「上」变成了 Z 轴 → 敌人/柱子/墙全部躺倒；
    ///     · 而 SpawnFit 仍按世界 Y 尺寸归一高度 → 躺倒后的 Y 是「厚度」，
    ///       于是模型被放大/压扁，看起来「全都变小且倒在地上」。
    ///   正确做法是保留导入旋转，它已经给出 上=+Y、前=+Z。
    ///
    /// 本类做三件事：
    ///   1. 断言每个模型导入后根旋转是轴转换值（±90 的整数倍，非 identity）；
    ///   2. 断言落地后高度 = Blender 建模高度（说明「上」确实是 Y）；
    ///   3. 断言「正面特征」材质的质心落在 +Z 侧（说明「前」确实是 +Z）。
    ///
    /// 新增模型时：在 Specs 里登记一行。若该件旋转对称（圆柱/球），front 填 null。
    /// 跑法：S/诊断/校验模型朝向，或 -executeMethod S.EditorTools.SModelContract.VerifyBatch
    /// </summary>
    public static class SModelContract
    {
        // Resources.Load 的路径本身就相对于某个 Resources 目录，
        // 所以这里不能写 "Resources/..."，否则会变成 Resources/Resources/...（踩过）
        const string ModelPath = "Models/";

        sealed class Spec
        {
            public string Name;
            /// <summary>Blender 建模高度（米）。用于验证「上」= Y。</summary>
            public float RawHeight;
            /// <summary>正面特征所在的材质名。null = 旋转对称，跳过前向判定。</summary>
            public string FrontMat;
        }

        // 数值来自 tools/blender_s_build_assets.py 的建模尺寸与材质分配。
        static readonly Spec[] Specs =
        {
            new Spec { Name = "Modle_Enemy_Crawler",  RawHeight = 0.861f, FrontMat = "M_Cyan"  },
            new Spec { Name = "Modle_Enemy_Spitter",  RawHeight = 1.735f, FrontMat = "M_Lime"  },
            new Spec { Name = "Modle_Enemy_Shield",   RawHeight = 1.970f, FrontMat = "M_Cyan"  },
            new Spec { Name = "Modle_Enemy_Drone",    RawHeight = 0.380f, FrontMat = "M_Amber" },
            new Spec { Name = "Modle_Boss_ForgeCore", RawHeight = 3.230f, FrontMat = null      },
            new Spec { Name = "Modle_Prop_Pillar",    RawHeight = 4.960f, FrontMat = "M_Magenta" },
            new Spec { Name = "Modle_Prop_Crate",     RawHeight = 0.920f, FrontMat = "M_Amber" },
            new Spec { Name = "Modle_Prop_Barrel",    RawHeight = 0.940f, FrontMat = null      },
            new Spec { Name = "Modle_Prop_Wall",      RawHeight = 5.000f, FrontMat = "M_Body"  },
            new Spec { Name = "Modle_Gun_Auto",       RawHeight = 0.380f, FrontMat = "M_Metal" },
            new Spec { Name = "Modle_Gun_Grenade",    RawHeight = 0.403f, FrontMat = "M_Panel" },
            new Spec { Name = "Modle_Gun_Single",     RawHeight = 0.394f, FrontMat = "M_Metal" },
        };

        [MenuItem("S/诊断/校验模型朝向")]
        public static void VerifyMenu() => Verify(false);

        /// <summary>批处理入口：失败时以非 0 退出码结束，便于 check.ps1 卡门。</summary>
        public static void VerifyBatch() => Verify(true);

        static void Verify(bool exitOnFail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 模型朝向契约（上=+Y / 前=+Z）===");

            int pass = 0, fail = 0;

            foreach (var spec in Specs)
            {
                var prefab = Resources.Load<GameObject>(ModelPath + spec.Name);
                if (prefab == null)
                {
                    sb.AppendLine($"FAIL {spec.Name,-24} 缺失（{ModelPath}{spec.Name}）");                    fail++;
                    continue;
                }

                var go = Object.Instantiate(prefab);
                try
                {
                    go.transform.position = Vector3.zero;
                    go.transform.localScale = Vector3.one;

                    var rot = go.transform.localRotation.eulerAngles;
                    var b = WorldBounds(go);

                    // 1) 轴转换必须保留：X 应为 ±90 的奇数倍（实测 270），不能是 0
                    float ax = Mathf.Abs(Mathf.DeltaAngle(rot.x, 0f));
                    bool rotOk = ax > 45f && ax < 135f
                              && Mathf.Abs(Mathf.DeltaAngle(rot.y, 0f)) < 1f
                              && Mathf.Abs(Mathf.DeltaAngle(rot.z, 0f)) < 1f;

                    // 2) 「上」=Y：高度必须等于建模高度（容差 2cm）
                    bool upOk = Mathf.Abs(b.size.y - spec.RawHeight) < 0.02f;

                    // 3) 「前」=+Z：正面特征材质质心在 +Z 侧
                    bool frontOk = spec.FrontMat == null || FrontOnPositiveZ(go, spec.FrontMat, b);

                    bool ok = rotOk && upOk && frontOk;
                    if (ok) pass++; else fail++;

                    sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {spec.Name,-24} " +
                                  $"rotX={rot.x,7:F2}[{(rotOk ? "ok" : "轴转换被抹掉")}] " +
                                  $"h={b.size.y:F3}[{(upOk ? "ok" : $"应为 {spec.RawHeight:F3}")}] " +
                                  $"front={(spec.FrontMat == null ? "n/a" : (frontOk ? "ok" : "不在 +Z"))}");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            sb.AppendLine($"RESULT 模型朝向 {pass}/{pass + fail} passed");
            Debug.Log(sb.ToString());

            if (fail > 0 && exitOnFail)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 端到端：验证**运行时真正用的那条路径**（SAssets.SpawnFit）产出的实例也是直立的。
        ///
        /// 为什么单独跑这一个：契约那一段只证明「FBX 导入正确」，
        /// 而这次 bug 出在 SpawnFit 里把导入旋转清零 —— FBX 再正确也会被它搞躺。
        /// 所以必须直接量 SpawnFit 的产物，否则同类回归会静默通过。
        /// </summary>
        [MenuItem("S/诊断/校验运行时模型生成")]
        public static void VerifySpawnMenu() => VerifySpawn(false);

        public static void VerifySpawnBatch() => VerifySpawn(true);

        static void VerifySpawn(bool exitOnFail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 运行时生成契约（SAssets.SpawnFit）===");

            // 与运行时一致的调用参数：每种敌人按 GameRoot.KindHeight 的目标高度
            var cases = new (string model, float targetH, bool groundAlign)[]
            {
                ("Modle_Enemy_Crawler",  0.86f, true),
                ("Modle_Enemy_Spitter",  1.74f, true),
                ("Modle_Enemy_Shield",   1.97f, true),
                ("Modle_Enemy_Drone",    0.38f, true),
                ("Modle_Boss_ForgeCore", 3.23f, true),
                ("Modle_Prop_Pillar",    4.96f, true),
                ("Modle_Prop_Crate",     0.92f, true),
                ("Modle_Prop_Barrel",    0.94f, true),
                ("Modle_Prop_Wall",      2.50f, true),
                ("Modle_Gun_Auto",       0.62f, false),
                ("Modle_Gun_Grenade",    0.62f, false),
                ("Modle_Gun_Single",     0.62f, false),
            };

            var host = new GameObject("SpawnHost");
            int pass = 0, fail = 0;

            try
            {
                foreach (var c in cases)
                {
                    var go = S.View.SAssets.SpawnFit(
                        c.model, host.transform, Vector3.zero,
                        c.targetH, Color.white, 0.4f, null, true, c.groundAlign);

                    if (go == null)
                    {
                        sb.AppendLine($"FAIL {c.model,-24} SpawnFit 返回 null");
                        fail++;
                        continue;
                    }

                    var b = WorldBounds(go);

                    // 判据 1：直立。按长边归一（groundAlign=false 的枪械）时 targetH 是
                    // 「最长边」而不是高度，所以两种模式要分别核对。
                    float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    float measured = c.groundAlign ? b.size.y : longest;
                    bool upright = Mathf.Abs(measured - c.targetH) < c.targetH * 0.06f;

                    // 判据 2：落地。groundAlign 时最低点应贴 localPos.y（此处 0）。
                    bool grounded = !c.groundAlign || Mathf.Abs(b.min.y) < 0.02f;

                    // 判据 3：导入旋转没被抹掉。
                    var visual = FindVisual(go);
                    bool rotKept = visual == null
                        || Mathf.Abs(Mathf.DeltaAngle(visual.localRotation.eulerAngles.x, 0f)) > 45f;

                    bool ok = upright && grounded && rotKept;
                    if (ok) pass++; else fail++;

                    sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {c.model,-24} " +
                                  $"{(c.groundAlign ? "h" : "longest")}={measured:F3}" +
                                  $"[{(upright ? "ok" : $"应为 {c.targetH:F2}")}] " +
                                  $"minY={b.min.y:F3}[{(grounded ? "ok" : "未落地")}] " +
                                  $"rotKept={(rotKept ? "ok" : "被清零")}");

                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            sb.AppendLine($"RESULT 运行时生成 {pass}/{pass + fail} passed");
            Debug.Log(sb.ToString());

            if (fail > 0 && exitOnFail && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>取实例下真正带网格的子节点（SpawnFit 的产物本身就是根，这里兜底找子层）。</summary>
        static Transform FindVisual(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>())
            {
                if (t != go.transform && t.GetComponent<MeshRenderer>() != null) return t;
            }
            return go.GetComponent<MeshRenderer>() != null ? go.transform : null;
        }

        /// <summary>指定材质（正面特征）的三角面质心是否落在包围盒中心的 +Z 侧。</summary>
        static bool FrontOnPositiveZ(GameObject go, string matName, Bounds b)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var verts = mesh.vertices;
                var xf = mf.transform.localToWorldMatrix;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (s >= mr.sharedMaterials.Length || mr.sharedMaterials[s] == null) continue;
                    if (mr.sharedMaterials[s].name != matName) continue;

                    var tris = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        sum += (xf.MultiplyPoint3x4(verts[tris[i]])
                              + xf.MultiplyPoint3x4(verts[tris[i + 1]])
                              + xf.MultiplyPoint3x4(verts[tris[i + 2]])) / 3f;
                        n++;
                    }
                }
            }

            if (n == 0) return false;

            // 阈值取高度的 1.5%：既能滤掉居中对称件，又不会被贴面装饰的微小偏移误判。
            return (sum / n).z - b.center.z > b.size.y * 0.015f;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds();
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}

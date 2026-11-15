using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// 全模型对照渲染：把 S 的全部模型摆进**同一个场景**，用同一机位渲成一张图。
    ///
    /// 机位在 +Z 侧朝 -Z 看 —— 因为本工程的朝向契约是 **上=+Y、前=+Z**，
    /// 这样镜头里看到的就是每一件模型的「正脸」。若某件模型背对镜头，
    /// 说明它的前向与契约不符（跑一次 SModelContract 就能定位是哪件）。
    ///
    /// 摆放规则与运行时完全一致：用 SAssets 的归一思路（按高度缩放到目标尺寸、
    /// 最低点落地），否则「渲染好看但游戏里不对」就没有意义。
    ///
    /// 菜单：S/诊断/渲染全部模型    批处理：-executeMethod S.EditorTools.SModelSheet.RenderBatch
    /// 输出：Shots/models_all.png
    /// </summary>
    public static class SModelSheet
    {
        const string OutPath = "Shots/models_all.png";
        const int Width = 1920;
        const int Height = 820;

        // 与运行时目标高度对齐（GameRoot.KindHeight / BossView / PlayerWeapons / ArenaBuilder）
        static readonly (string name, float targetH)[] Items =
        {
            ("Modle_Enemy_Crawler",   0.86f),
            ("Modle_Enemy_Spitter",   1.74f),
            ("Modle_Enemy_Shield",    1.97f),
            ("Modle_Enemy_Drone",     0.38f),
            ("Modle_Boss_ForgeCore",  3.23f),
            ("Modle_Prop_Pillar",     4.96f),
            ("Modle_Prop_Crate",      0.92f),
            ("Modle_Prop_Barrel",     0.94f),
            ("Modle_Prop_Wall",       2.50f),
            ("Modle_Gun_Auto",        0.62f),
            ("Modle_Gun_Grenade",     0.62f),
            ("Modle_Gun_Single",      0.62f),
        };

        [MenuItem("S/诊断/渲染全部模型")]
        public static void RenderMenu() => Render();

        public static void RenderBatch()
        {
            Render();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Render()
        {
            var log = new StringBuilder();
            log.AppendLine("=== 全部模型同场景渲染 ===");

            var sceneRoot = new GameObject("ModelSheet").transform;

            // 地面：给模型一个落点参照，否则悬空件看起来像贴在天上
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(sceneRoot, false);
            ground.transform.localScale = Vector3.one * 8f;
            ground.transform.position = new Vector3(0f, 0f, 3f);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.19f, 0.20f, 0.23f)
            };
            if (groundMat.HasProperty("_BaseColor"))
                groundMat.SetColor("_BaseColor", new Color(0.19f, 0.20f, 0.23f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            const int cols = 6;
            const float cellW = 2.7f;
            const float cellD = 4.2f;

            for (int i = 0; i < Items.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var slot = new Vector3((col - (cols - 1) * 0.5f) * cellW, 0f, -row * cellD + 3.2f);

                var prefab = Resources.Load<GameObject>("Models/" + Items[i].name);
                if (prefab == null)
                {
                    log.AppendLine($"MISSING {Items[i].name}");
                    continue;
                }

                var go = Object.Instantiate(prefab, sceneRoot);
                go.name = Items[i].name;

                // 关键：**保留导入旋转**（它是 Blender Z上 → Unity Y上 的轴转换）。
                // 清零就会让「上」变成 Z 轴，模型全部躺倒 —— 这是本次修的根因。
                float importRotX = go.transform.localRotation.eulerAngles.x;
                go.transform.position = Vector3.zero;
                go.transform.localScale = Vector3.one;

                var b = Bounds(go);
                if (b.size.y > 0.001f) go.transform.localScale *= Items[i].targetH / b.size.y;

                var b2 = Bounds(go);
                go.transform.position = new Vector3(
                    slot.x - b2.center.x, slot.y - b2.min.y, slot.z - b2.center.z);

                var fin = Bounds(go);
                log.AppendLine($"{Items[i].name,-24} importRotX={importRotX,7:F2} " +
                               $"h={fin.size.y:F2} size=({fin.size.x:F2},{fin.size.y:F2},{fin.size.z:F2})");
            }

            // 光：与游戏内方向一致（GameRoot.BuildWorld 的 48/-35）
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.88f);
            lightGo.transform.rotation = Quaternion.Euler(34f, -28f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.27f, 0.30f, 0.37f);

            // 相机：+Z 侧朝 -Z 看 → 正对模型正面
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
            camGo.transform.position = new Vector3(0f, 12.0f, 20.5f);
            camGo.transform.LookAt(new Vector3(0f, 1.5f, -1.0f));

            Directory.CreateDirectory("Shots");

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(OutPath, tex.EncodeToPNG());
            log.AppendLine($"SHEET -> {Path.GetFullPath(OutPath)}");

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(groundMat);
            Object.DestroyImmediate(sceneRoot.gameObject);

            Debug.Log(log.ToString());
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds();
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}

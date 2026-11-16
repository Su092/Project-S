using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using S.Core;
using S.View;

namespace S.EditorTools
{
    public static class SProjectileVisualCheck
    {
        const string Request = "Logs/projectile-visual.request";
        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += RunRequested;
        }
        static void RunRequested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RunRequested; return; }
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            Verify();
        }

        [MenuItem("S/诊断/子弹与爆炸视觉总览")]
        public static void Verify()
        {
            var report = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string label, bool ok)
            { report.AppendLine((ok ? "PASS " : "FAIL ") + label); if (ok) pass++; else fail++; }
            var host = new GameObject("ProjectileVisualAudit");
            host.hideFlags = HideFlags.HideAndDontSave;
            host.transform.position = new Vector3(1000, 1000, 1000);
            RenderTexture rt = null;
            Texture2D output = null;
            var previous = RenderTexture.active;
            try
            {
                var data = SCfgLoader.Load(new SCfgDirectorySource("Assets/_Game/Res/Cfg"));
                var shader = Shader.Find("S/CombatGlow");
                Check("combat shader compiled", shader != null && !ShaderUtil.ShaderHasError(shader));
                int index = 0;
                foreach (var def in data.BulletOrder)
                {
                    int column = index % 6, row = index / 6;
                    var pos = new Vector3((column - 2.5f) * 2.8f, 4.5f - row * 2.3f, 0f);
                    var go = new GameObject(def.Id);
                    go.transform.SetParent(host.transform, false);
                    go.transform.localPosition = pos;
                    go.transform.localRotation = Quaternion.Euler(20f, 65f, -20f);
                    var desc = def.ToDesc();
                    var visual = go.AddComponent<ProjectileVisual>();
                    visual.Init(desc); visual.Animate(0.15f);
                    go.transform.localScale = Vector3.one * (def.Id == "hole" ? 1.1f : def.Id == "saw" ? 2.1f : 3f);
                    if (def.Kind == BulletKind.Beam || def.Kind == BulletKind.Chain)
                        CombatFx.Beam(go.transform.position - Vector3.right * 0.8f, go.transform.position + Vector3.right * 0.8f,
                            PlayerWeapons.ParseColor(def.Color), def.Kind == BulletKind.Chain);
                    Check(def.Id + " correct style", visual.StyleId == def.Id);
                    Check(def.Id + " no solid collider", Array.TrueForAll(go.GetComponentsInChildren<Collider>(), c => !c.enabled));
                    Check(def.Id + " visible geometry", def.Kind == BulletKind.Beam || def.Kind == BulletKind.Chain ||
                        go.GetComponentsInChildren<Renderer>().Length > 0);
                    Label(host.transform, pos + Vector3.down * 0.85f, def.Id.ToUpperInvariant());
                    if (def.Kind == BulletKind.Mine || def.Kind == BulletKind.Field)
                    {
                        visual.Deploy(Vector3.up, desc.Radius);
                        Check(def.Id + " deployment has no collider", Array.TrueForAll(go.GetComponentsInChildren<Collider>(), c => !c.enabled));
                        // 总览只展示弹体，实际半径的电网另由部署测试覆盖。
                        foreach (Transform child in go.transform)
                            if (child.name == "ElectricGrid" || child.name == "EnergyRing") child.gameObject.SetActive(false);
                        go.transform.localRotation = Quaternion.Euler(-55f, 0f, -15f);
                    }
                    index++;
                }
                Check("all 17 bullets covered", index == 17);
                // 三个爆炸时刻，使用同一组粒子种子，便于判断膨胀和消散。
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pos = host.transform.position + new Vector3((i - 1) * 5f, -3.4f, 0f);
                    CombatFx.Explosion(pos, 0.85f, new Color(1f, 0.65f, 0.1f));
                    Label(host.transform, new Vector3((i - 1) * 5f, -5.2f, 0f), new[] { "BLAST / 0.08s", "BLAST / 0.25s", "BLAST / 0.65s" }[i]);
                }
                foreach (var fx in UnityEngine.Object.FindObjectsByType<CombatFx>())
                {
                    if (Vector3.Distance(fx.transform.position, host.transform.position) > 30f) continue;
                    fx.transform.SetParent(host.transform, true);
                    float age = fx.name == "ExplosionFx" ? (fx.transform.localPosition.x < -1 ? 0.08f : fx.transform.localPosition.x > 1 ? 0.65f : 0.25f) : 0f;
                    fx.PreviewAt(age);
                    if (fx.name == "ExplosionFx")
                    {
                        var ring = fx.transform.Find("EnergyRing");
                        if (ring != null) ring.localRotation = Quaternion.Euler(55f, 0f, 0f);
                    }
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(age, true, true);
                    Check(fx.name + " no collider", fx.GetComponentsInChildren<Collider>().Length == 0);
                }
                foreach (var ps in host.GetComponentsInChildren<ParticleSystem>())
                    if (ps.name == "FlamePlume") ps.Simulate(0.15f, true, true);
                int cached = SMat.CreatedCount;
                for (int i = 0; i < 100; i++) { SMat.Combat(false); SMat.Combat(true); SMat.Combat(true, true); }
                Check("FX materials reuse cache", cached == SMat.CreatedCount);
                foreach (var r in host.GetComponentsInChildren<Renderer>())
                    foreach (var material in r.sharedMaterials)
                        Check(r.name + " material shader", material != null && material.shader != null &&
                            material.shader.name != "Hidden/InternalErrorShader");

                var lightGo = new GameObject("PreviewLight"); lightGo.transform.SetParent(host.transform, false);
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional;
                light.intensity = 2f; light.cullingMask = 1 << 31;
                light.transform.rotation = Quaternion.Euler(25f, -30f, 0f);
                var camGo = new GameObject("PreviewCamera"); camGo.transform.SetParent(host.transform, false);
                var camera = camGo.AddComponent<Camera>();
                camera.transform.localPosition = new Vector3(0f, 0f, -22f);
                camera.orthographic = true; camera.orthographicSize = 6.2f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.018f, 0.03f, 0.055f);
                camera.cullingMask = 1 << 31; camera.farClipPlane = 50f; camera.enabled = false;
                foreach (var tr in host.GetComponentsInChildren<Transform>()) tr.gameObject.layer = 31;
                rt = new RenderTexture(1920, 1200, 24) { antiAliasing = 4 };
                camera.targetTexture = rt; camera.Render();
                RenderTexture.active = rt;
                output = new Texture2D(1920, 1200, TextureFormat.RGB24, false);
                output.ReadPixels(new Rect(0, 0, 1920, 1200), 0, 0); output.Apply();
                Directory.CreateDirectory("Shots");
                File.WriteAllBytes("Shots/projectile-visual-sheet.png", output.EncodeToPNG());
                camera.targetTexture = null;
            }
            catch (Exception e) { fail++; report.AppendLine("FAIL " + e); }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(host);
                report.AppendLine($"RESULT {pass}/{pass + fail} passed");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/projectile-visual-check.log", report.ToString());
                Debug.Log(report.ToString());
            }
        }
        static void Label(Transform parent, Vector3 pos, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text; mesh.fontSize = 42; mesh.characterSize = 0.055f;
            mesh.anchor = TextAnchor.MiddleCenter; mesh.color = new Color(0.65f, 0.78f, 0.9f);
        }
    }
}

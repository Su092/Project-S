using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using S.Core;
using S.View;

namespace S.EditorTools
{
    /// <summary>构建隔离的竞技场，校验所有地形碰撞及障碍外侧的子弹/角色通路。</summary>
    public static class SObstacleCollisionCheck
    {
        const string Request = "Logs/obstacle-check.request";
        const string Report = "Logs/obstacle-collision-check.log";

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += RunRequested;
        }

        static void RunRequested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunRequested;
                return;
            }
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            Verify();
        }

        [MenuItem("S/诊断/校验障碍碰撞与视觉")]
        public static void Verify()
        {
            var log = new StringBuilder();
            int passed = 0, failed = 0;
            void Check(string name, bool ok)
            {
                log.AppendLine((ok ? "PASS " : "FAIL ") + name);
                if (ok) passed++; else failed++;
            }
            GameObject host = null;
            try
            {
                SRes.PreloadAll();
                foreach (var model in new[] { "Crate", "Barrel", "Wall", "Pillar" })
                    Check("model loaded: " + model, SAssets.Model("Modle_Prop_" + model) != null);
                host = new GameObject("CollisionAudit");
                host.hideFlags = HideFlags.HideAndDontSave;
                host.transform.position = new Vector3(1000f, 0f, 1000f);
                var arena = host.AddComponent<ArenaBuilder>();
                arena.Build(new ArenaDef());
                arena.SpawnPillars();
                Physics.SyncTransforms();
                foreach (var col in host.GetComponentsInChildren<Collider>())
                {
                    if (!col.enabled) continue;
                    var renderer = col.GetComponent<Renderer>();
                    Check(col.transform.parent.name + "/" + col.name + " has visible geometry",
                        renderer != null && renderer.enabled);
                    if (renderer == null || !renderer.enabled) continue;
                    Check(col.name + " collider matches visual bounds",
                        Vector3.Distance(col.bounds.center, renderer.bounds.center) < 0.03f &&
                        Vector3.Distance(col.bounds.size, renderer.bounds.size) < 0.03f);
                }
                foreach (Transform obstacle in host.transform)
                {
                    var visual = obstacle.Find("Model");
                    if (visual == null) continue;
                    var local = SAssets.MeasureBoundsLocal(visual.gameObject);
                    log.AppendLine(obstacle.name + " size=" + local.size.ToString("F3") +
                        " minY=" + local.min.y.ToString("F3"));
                    Check(obstacle.name + " rests on ground", Mathf.Abs(local.min.y) < 0.01f);
                    if (obstacle.name == "CoverWall")
                        Check("wall dimensions", local.size.x >= 4.99f && local.size.x <= 8.01f &&
                            local.size.y >= 1.39f && local.size.y <= 2.61f);
                    if (obstacle.name == "Crate") Check("crate height", Mathf.Abs(local.size.y - 0.92f) < 0.01f);
                    if (obstacle.name == "Barrel") Check("barrel height", Mathf.Abs(local.size.y - 0.94f) < 0.01f);
                    if (obstacle.name.StartsWith("Pillar")) Check("pillar height", Mathf.Abs(local.size.y - 5.2f) < 0.01f);

                    var b = SAssets.MeasureBounds(visual.gameObject);
                    var cols = obstacle.GetComponentsInChildren<Collider>();
                    // 光束穿过可见实体时仍应被挡，不能以关闭碰撞掩盖空气墙。
                    bool bodyHit = false;
                    foreach (var col in cols)
                        if (col.enabled && col.Raycast(new Ray(b.center - Vector3.forward * (b.extents.z + 1f),
                            Vector3.forward), out _, b.size.z + 2f)) bodyHit = true;
                    Check(obstacle.name + " solid body blocks bullet", bodyHit);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var start = new Vector3(b.center.x + side * (b.extents.x + 0.5f),
                            b.center.y, b.min.z - 1f);
                        bool blocked = false;
                        foreach (var col in cols)
                            if (col.enabled && col.Raycast(new Ray(start, Vector3.forward), out _, b.size.z + 2f)) blocked = true;
                        Check(obstacle.name + " exterior bullet path " + side, !blocked);
                        var hits = Physics.CapsuleCastAll(start + Vector3.up * 0.5f,
                            start - Vector3.up * 0.5f, 0.34f, Vector3.forward, b.size.z + 2f,
                            ~0, QueryTriggerInteraction.Ignore);
                        bool playerBlocked = Array.Exists(hits, hit => hit.collider.transform.IsChildOf(obstacle));
                        Check(obstacle.name + " exterior player path " + side, !playerBlocked);
                    }
                    // 旋转父节点不应改变父空间尺寸。
                    var savedRotation = obstacle.localRotation;
                    obstacle.localRotation = Quaternion.Euler(0f, 37f, 0f);
                    var rotated = SAssets.MeasureBoundsLocal(visual.gameObject);
                    Check(obstacle.name + " rotation invariant measurement",
                        Vector3.Distance(local.size, rotated.size) < 0.01f);
                    obstacle.localRotation = savedRotation;
                }
                var fx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fx.transform.SetParent(host.transform, false);
                SAssets.RemoveColliders(fx);
                Check("visual collider disabled immediately",
                    Array.TrueForAll(fx.GetComponents<Collider>(), c => !c.enabled));
            }
            catch (Exception e) { failed++; log.AppendLine("FAIL " + e); }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                log.AppendLine($"RESULT {passed}/{passed + failed} passed");
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Report, log.ToString());
                Debug.Log(log.ToString());
            }
        }
    }
}

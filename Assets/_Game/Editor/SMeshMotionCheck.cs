using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using S.Core;
using S.View;

namespace S.EditorTools
{
    public static class SMeshMotionCheck
    {
        const string Request = "Logs/mesh-motion.request";
        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += Requested;
        }
        static void Requested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += Requested; return; }
            if (!File.Exists(Request) || EditorApplication.isPlaying) return;
            File.Delete(Request); Verify();
        }
        [MenuItem("S/诊断/网格动作与选枪")]
        public static void Verify()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("请在停止运行后执行网格诊断"); return; }
            var log = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, bool ok)
            { log.AppendLine((ok ? "PASS " : "FAIL ") + name); if (ok) pass++; else fail++; }
            var host = new GameObject("MeshMotionAudit") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.position = new Vector3(1000, 1000, 1000);
            try
            {
                var data = SCfgLoader.Load(new SCfgDirectorySource("Assets/_Game/Res/Cfg"));
                int index = 0;
                foreach (var model in data.GunOrder.Select(g => g.Model).Distinct())
                {
                    var holder = Child(host.transform, model);
                    holder.transform.localPosition = new Vector3((index - 1) * 3.3f, 2.4f, 0);
                    holder.transform.localRotation = Quaternion.Euler(0, 65, 0);
                    holder.transform.localScale = Vector3.one * 3f;
                    var source = Model(model, holder.transform, 0.62f, false);
                    int triangles = Triangles(source);
                    var original = VisibleVertices(holder);
                    var motion = holder.AddComponent<WeaponMotion>(); motion.Bind(null, source);
                    var rig = holder.transform.Find("WeaponRig");
                    Check(model + " all faces retained", Triangles(rig.gameObject) == triangles);
                    Check(model + " rest bounds retained", VerticesNear(original, VisibleVertices(holder)));
                    Check(model + " magazine joint", rig.Find("Joint1") != null);
                    Check(model + " bolt joint", rig.Find("Joint2") != null);
                    var mag = rig.Find("Joint1");
                    var magRest = mag != null ? mag.localPosition : Vector3.zero;
                    motion.Sample(0.5f, 0);
                    Check(model + " reload detaches magazine", mag != null && mag.localPosition.y < magRest.y - 0.15f);
                    motion.Sample(1f, 0);
                    Check(model + " reload returns home", VerticesNear(original, VisibleVertices(holder)));
                    motion.Sample(0, 1f);
                    Check(model + " recoil raises and kicks model", holder.transform.localPosition.z < -0.05f && Quaternion.Angle(holder.transform.localRotation, Quaternion.Euler(0,65,0)) > 5);
                    motion.Sample(0.5f, 0); // 总览展示换弹中段
                    Label(host.transform, new Vector3((index - 1) * 3.3f, 1.4f, 0), model.Replace("Modle_Gun_", "") + " / RELOAD");
                    index++;
                }
                index = 0;
                foreach (var def in data.EnemyOrder)
                {
                    var holder = Child(host.transform, def.Id);
                    holder.transform.localPosition = new Vector3((index - 1.5f) * 3f, -1.9f, 0);
                    holder.transform.localRotation = Quaternion.Euler(0, 155, 0);
                    var cc = holder.AddComponent<CharacterController>(); cc.height = def.Height;
                    var source = Model(def.Model, holder.transform, def.Height, true); source.name = "Body";
                    int triangles = Triangles(source); var original = VisibleVertices(holder);
                    var enemy = holder.AddComponent<Enemy>(); enemy.Configure(def, 1, 1, 1, 1);
                    var motion = holder.AddComponent<EnemyMotion>(); motion.Bind(enemy, source);
                    var rig = holder.transform.Find("EnemyRig");
                    Check(def.Id + " all faces retained", Triangles(rig.gameObject) == triangles);
                    Check(def.Id + " rest bounds retained", VerticesNear(original, VisibleVertices(holder)));
                    Check(def.Id + " movable limbs", motion.JointCount >= 3);
                    Vector3 rootPos = holder.transform.localPosition;
                    motion.Sample(1.1f, 1, 0.8f);
                    Check(def.Id + " animation leaves collision root unchanged", holder.transform.localPosition == rootPos && cc.height == def.Height);
                    Check(def.Id + " posed joints", rig.GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("Joint") && Quaternion.Angle(t.localRotation, Quaternion.identity) > 5));
                    Check(def.Id + " visual has no collider", rig.GetComponentsInChildren<Collider>().Length == 0);
                    Label(host.transform, new Vector3((index - 1.5f) * 3f, -2.5f, 0), def.Id.ToUpperInvariant() + " / ATTACK");
                    index++;
                }
                var inventoryHost = Child(host.transform, "InventoryTest");
                var weapons = inventoryHost.AddComponent<PlayerWeapons>(); weapons.Init(data);
                var run = new RunState(data, 100);
                foreach (var gun in data.GunOrder)
                {
                    Check(gun.Id + " selectable as sole starter", weapons.StartWith(gun.Id) && weapons.GunCount == 1 && weapons.Active.Base.Id == gun.Id && weapons.Active.HasAnyBullet);
                }
                weapons.StartWith("rifle");
                Check("second gun acquired", weapons.AcquireGun("smg", run, out _) && weapons.GunCount == 2 && weapons.Active.Base.Id == "smg");
                var saved = weapons.Active.Slots.Cells.Where(i => i != null).ToArray();
                Check("full weapon slots replace current", weapons.AcquireGun("sniper", run, out _) && weapons.GunCount == 2 && weapons.Active.Base.Id == "sniper");
                Check("old attachments returned without loss", saved.All(i => run.Bag.Contains(i)));
                Check("duplicate gun rejected", !weapons.AcquireGun("rifle", run, out _));
                while (run.Bag.Count < run.BagCapacity) run.Bag.Add(data.MakeItem("std"));
                var before = weapons.Active;
                Check("full bag replacement rejects atomically", !weapons.AcquireGun("shotgun", run, out _) && weapons.Active == before && run.Bag.Count == run.BagCapacity);
                Render(host);
                SRes.PreloadAll();
                var menuHost = Child(host.transform, "MenuTest"); var menu = menuHost.AddComponent<MenuView>();
                int starts = 0;
                menu.Init(new MetaProgress(), () => starts++, data); menu.Open();
                menu.StartOrChoose();
                Check("menu lists every gun", menu.ChoosingGun && menu.RowCount == data.GunOrder.Count);
                menu.StartOrChoose();
                Check("menu cannot start without selection", starts == 0 && menu.IsOpen);
                foreach (var gun in data.GunOrder)
                { menu.SelectGun(gun.Id); Check(gun.Id + " menu choice", menu.SelectedGunId == gun.Id); }
                menu.StartOrChoose();
                Check("selected gun confirmed before start", starts == 1 && !menu.IsOpen && menu.SelectedGunId == data.GunOrder.Last().Id);
                var card = UnityEngine.Object.Instantiate(UiPrefab.Load(UiPrefab.OfferCard), host.transform).GetComponent<OfferCardRefs>();
                card.BindGun(data.Guns["sniper"], "领取并装备");
                Check("gun offer has title and visible icon", card.CardTitle.text == data.Guns["sniper"].Name && card.IconNode.Icon.enabled && card.IconNode.Icon.sprite != null);
            }
            catch (Exception e) { fail++; log.AppendLine("FAIL " + e); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                log.AppendLine($"RESULT {pass}/{pass + fail} passed");
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/mesh-motion-check.log", log.ToString());
                Debug.Log(log.ToString());
            }
        }
        static GameObject Child(Transform parent, string name)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
        static GameObject Model(string name, Transform parent, float size, bool ground)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SAssets.ModelDir + name + ".fbx");
            var go = UnityEngine.Object.Instantiate(source, parent, false);
            go.transform.localPosition = Vector3.zero;
            var b = SAssets.MeasureBoundsLocal(go);
            go.transform.localScale *= size / (ground ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z));
            b = SAssets.MeasureBoundsLocal(go);
            go.transform.localPosition = ground ? new Vector3(-b.center.x, -b.min.y, -b.center.z) : -b.center;
            SAssets.RemoveColliders(go);
            return go;
        }
        static int Triangles(GameObject go) => go.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length);
        static Vector3[] VisibleVertices(GameObject go) => go.GetComponentsInChildren<MeshFilter>()
            .Where(m => m.GetComponent<Renderer>().enabled)
            .SelectMany(m => m.sharedMesh.vertices.Select(v => m.transform.TransformPoint(v))).ToArray();
        static bool VerticesNear(Vector3[] a, Vector3[] b) =>
            a.All(p => b.Any(q => (p-q).sqrMagnitude < 0.000001f)) &&
            b.All(p => a.Any(q => (p-q).sqrMagnitude < 0.000001f));
        static void Label(Transform parent, Vector3 position, string text)
        {
            var go = Child(parent, "Label"); go.transform.localPosition = position;
            var label = go.AddComponent<TextMesh>(); label.text = text; label.fontSize = 38; label.characterSize = 0.04f; label.anchor = TextAnchor.MiddleCenter;
        }
        static void Render(GameObject host)
        {
            var light = Child(host.transform,"Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.cullingMask = 1 << 31;
            light.transform.localRotation = Quaternion.Euler(30,-25,0);
            var camera = Child(host.transform,"Camera").AddComponent<Camera>();
            camera.transform.localPosition = new Vector3(0,0,-20); camera.orthographic = true; camera.orthographicSize = 4f;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.025f,0.04f,0.06f); camera.enabled = false;
            foreach (var tr in host.GetComponentsInChildren<Transform>()) tr.gameObject.layer = 31;
            var old = RenderTexture.active;
            var rt = new RenderTexture(1920,1080,24) { antiAliasing = 4 }; var image = new Texture2D(1920,1080,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply();
                Directory.CreateDirectory("Shots"); File.WriteAllBytes("Shots/mesh-motion-sheet.png",image.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}

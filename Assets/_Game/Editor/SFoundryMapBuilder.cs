using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SFoundryMapBuilder
    {
        const string Source = "_Inbox/Environment/AbandonedFoundry/Reports/layout.json";
        const string Modules = "Assets/_Game/Res/Prefab/Environment/AbandonedFoundry/";
        [Serializable] public class Entry { public string key, group; public float x,y,z,rotation; }
        [Serializable] public class Point { public float x,y,z; }
        [Serializable] public class Layout { public Entry[] instances; public Point[] spawns; public Point player; }
        static double next;
        static SFoundryMapBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            if (File.Exists("Logs/foundry-open.request"))
            {
                File.Delete("Logs/foundry-open.request");
                OpenMainScene();
            }
            if (File.Exists("Logs/foundry-state.request"))
            {
                File.Delete("Logs/foundry-state.request");
                File.WriteAllLines("Logs/foundry-editor-state.txt", Enumerable.Range(0, SceneManager.sceneCount)
                    .Select(i => SceneManager.GetSceneAt(i)).Select(s => s.path + " dirty=" + s.isDirty));
            }
            if (File.Exists("Logs/foundry-build.request"))
            {
                File.Delete("Logs/foundry-build.request");
                try { Build(); } catch(Exception e) { File.WriteAllText("Logs/foundry-build-error.log",e.ToString());Debug.LogException(e); }
            }
            if (File.Exists("Logs/foundry-close.request"))
            {
                File.Delete("Logs/foundry-close.request");
                var dirty = Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Where(s=>s.isDirty).ToArray();
                if (dirty.Length > 0) File.WriteAllLines("Logs/foundry-close-blocked.txt",dirty.Select(s=>s.path));
                else EditorApplication.Exit(0);
            }
        }
        [MenuItem("S/Map/Open Main Scene")]
        public static void OpenMainScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene before opening Main.");
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
        }
        static void EnsureNamedBatchScene()
        {
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
        }
        public static Layout ReadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText(Source));
        static Vector3 Position(Point p) => new Vector3(p.x,p.z,-p.y);
        static GameObject Module(string key, Transform parent, Vector3 position, float rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Modules + key + ".prefab");
            if (!prefab) throw new InvalidOperationException("Missing foundry module " + key);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = key; go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0,rotation,0);
            return go;
        }
        static Transform Marker(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go.transform;
        }
        [MenuItem("S/Map/Build Foundry Map")]
        public static void Build()
        {
            Directory.CreateDirectory("Logs");Directory.CreateDirectory("Shots");
            foreach(string p in new[]{"Logs/foundry-build-error.log","Logs/foundry-build.log"})
                if(File.Exists(p)) File.Delete(p);
            EnsureNamedBatchScene();
            var loadedMain = SceneManager.GetSceneByPath(SceneBuilder.ScenePath);
            if (loadedMain.IsValid() && loadedMain.isDirty)
                throw new InvalidOperationException("Main scene has unsaved changes. Save it before map integration.");
            var layout = ReadLayout();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject(FoundryArena.AssetName);
                var map = root.AddComponent<FoundryArena>();
                var groups = new Dictionary<string,Transform>();
                var pillars = new List<Pillar>();
                foreach(var e in layout.instances)
                {
                    if(!groups.TryGetValue(e.group,out var group))
                        groups[e.group]=group=Marker(e.group,root.transform,Vector3.zero);
                    var pos = new Vector3(e.x,e.z,-e.y);
                    if(e.key=="Core_EnergyPillar")
                    {
                        var socket=Marker("Pillar"+pillars.Count,group,pos);
                        var intact=Module(e.key,socket,Vector3.zero,e.rotation);
                        var broken=Module("Core_EnergyRuin",socket,Vector3.zero,e.rotation);broken.SetActive(false);
                        var pillar=socket.gameObject.AddComponent<Pillar>();
                        pillar.IntactVisual=intact;pillar.BrokenVisual=broken;pillar.Index=pillars.Count;
                        pillars.Add(pillar);socket.gameObject.SetActive(false);
                    }
                    else Module(e.key,group,pos,e.rotation);
                }
                map.LayoutInstanceCount=layout.instances.Length;
                using(var sha=SHA256.Create())map.LayoutSha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Source))).Replace("-","").ToLowerInvariant();
                map.PlayerSpawn=Marker("PlayerSpawn",root.transform,Position(layout.player));
                map.BossSpawn=Marker("BossSpawn",root.transform,new Vector3(0,.35f,0));
                var spawnRoot=Marker("SpawnPoints",root.transform,Vector3.zero);
                map.EnemySpawns=layout.spawns.Select((p,i)=>Marker("Spawn_"+i.ToString("00"),spawnRoot,Position(p)+Vector3.up*.02f)).ToArray();
                map.EnergyPillars=pillars.ToArray();
                var sun=Marker("Overcast Sun",root.transform,Vector3.zero).gameObject.AddComponent<Light>();
                sun.type=LightType.Directional;sun.intensity=1.5f;sun.color=Color.white;
                sun.shadows=LightShadows.Soft;sun.transform.localRotation=Quaternion.Euler(45,-30,0);
                foreach(var pillar in pillars)
                {
                    var glow=Marker("Core Cyan",root.transform,pillar.transform.localPosition+Vector3.up*2.4f).gameObject.AddComponent<Light>();
                    glow.type=LightType.Point;glow.color=Color.cyan;glow.intensity=5;glow.range=9;
                }
                map.ValidateBindings();
                Directory.CreateDirectory(Path.GetDirectoryName(FoundryArena.AssetPath));
                PrefabUtility.SaveAsPrefabAsset(root,FoundryArena.AssetPath);
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);
            }
            bool opened=!loadedMain.IsValid();
            var main=opened?EditorSceneManager.OpenScene(SceneBuilder.ScenePath,OpenSceneMode.Additive):loadedMain;
            try { AttachToScene(main);EditorSceneManager.SaveScene(main); }
            finally { if(opened)EditorSceneManager.CloseScene(main,true); }
            SAddressablesSetup.Build();
            AssetDatabase.SaveAssets();
            Validate();
            File.AppendAllText("Logs/foundry-build.log","RESULT Foundry map prefab and Main scene ready\n");
        }
        public static void AttachToScene(Scene scene)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FoundryArena.AssetPath);
            if(!prefab)throw new InvalidOperationException("Build FoundryArena prefab before creating Main.");
            foreach(var root in scene.GetRootGameObjects())
                if(root.GetComponent<FoundryArena>())UnityEngine.Object.DestroyImmediate(root);
            PrefabUtility.InstantiatePrefab(prefab,scene);
        }
        [MenuItem("S/Map/Verify Foundry Map")]
        public static void Validate()
        {
            EnsureNamedBatchScene();
            var log=new StringBuilder();int pass=0,fail=0;
            void Check(string name,bool ok){log.AppendLine((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;}
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FoundryArena.AssetPath);
                var map=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<FoundryArena>();
                map.ValidateBindings();map.ApplyEnvironment();
                // Offset test scene so collisions in an open inspection/Main scene cannot contaminate probes.
                map.transform.position=new Vector3(1000,0,1000);
                Vector3 offset=map.transform.position;
                var layout=ReadLayout();
                Check("authored layout instance count",map.LayoutInstanceCount==layout.instances.Length);
                Check("16 spawn markers and 4 boss pillars",map.EnemySpawns.Length==16&&map.EnergyPillars.Length==4);
                Check("spawn position preserved",Vector3.Distance(map.PlayerSpawn.position-offset,new Vector3(0,1.2f,-38))<.001f);
                Check("no inspection camera/controller",!map.GetComponentInChildren<Camera>(true)&&!map.GetComponentInChildren<FoundryDelivery.FoundryWalkthrough>(true));
                Check("all shared materials resolve",map.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m&&m.shader)));
                Check("two LODs for every module",map.GetComponentsInChildren<LODGroup>(true).All(l=>l.GetLODs().Length==2));
                Check("collision proxy meshes invisible",map.GetComponentsInChildren<Renderer>(true).All(r=>!r.name.StartsWith("COL_")));
                Check("positive unit map scale",map.transform.localScale==Vector3.one);
                Physics.SyncTransforms();
                var body=new GameObject("ProductionControllerProbe");SceneManager.MoveGameObjectToScene(body,scene);
                var cc=body.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.34f;cc.center=Vector3.up*.9f;cc.slopeLimit=50;cc.stepOffset=.4f;
                body.AddComponent<PlayerController>();cc.enabled=false;
                bool Clear(Vector3 p)=>!Physics.OverlapCapsule(p+Vector3.up*.36f,p+Vector3.up*1.44f,.34f,WorldPhysics.GroundMask,QueryTriggerInteraction.Ignore).Any(c=>c.gameObject.scene==scene);
                Check("player ground landing clear",Clear(offset+new Vector3(0,.05f,-38)));
                foreach(var spawn in map.EnemySpawns)Check(spawn.name+" clear",Clear(spawn.position+Vector3.up*.03f));
                foreach(var e in layout.instances.Where(e=>e.key=="Platform_Ramp4x8"||e.key=="Platform_Stairs4x5"))
                {
                    float len=e.key.Contains("Stairs")?5:8;var rot=Quaternion.Euler(0,e.rotation,0);
                    var start=offset+new Vector3(e.x,.05f,-e.y)+rot*new Vector3(2,0,1);
                    cc.enabled=false;body.transform.position=start;cc.enabled=true;
                    for(int i=0;i<(len+2)*100;i++)cc.Move(rot*new Vector3(0,-.025f,-.01f));
                    Check("production controller ascent "+e.key+" "+e.x+","+e.y,body.transform.position.y>2.4f);
                    cc.enabled=false;
                    if(e.key.Contains("Stairs"))
                    {
                        var origin=offset+new Vector3(e.x,5,-e.y)+rot*new Vector3(2,0,-2.33f);
                        bool a=Physics.Raycast(origin,Vector3.down,out var moving,10,WorldPhysics.GroundMask,QueryTriggerInteraction.Ignore);
                        bool b=Physics.Raycast(origin,Vector3.down,out var shot,10,WorldPhysics.ShotMask,QueryTriggerInteraction.Ignore);
                        Check("stairs shot excludes ramp "+e.x+","+e.y,a&&b&&moving.collider.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.MovementLayer)&&shot.collider.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.ShotLayer));
                    }
                }
                foreach(var p in map.EnergyPillars)
                {
                    Check("pillar hidden before boss "+p.Index,!p.gameObject.activeSelf);
                    p.gameObject.SetActive(true);p.Hp=p.MaxHp=800;Physics.SyncTransforms();
                    Check("pillar intact colliders "+p.Index,p.IntactVisual.GetComponentsInChildren<Collider>().Any(c=>c.enabled));
                }
                // Capture using the same camera position as delivery, translated with the isolated test map.
                var camera=new GameObject("MapCapture").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.09f,.12f);camera.farClipPlane=300;camera.fieldOfView=75;
                camera.transform.position=offset+new Vector3(80,75,-94);camera.transform.LookAt(offset);
                Capture(camera,"Shots/foundry-map-overview.png");
                camera.transform.position=offset+new Vector3(0,1.7f,-38);camera.transform.rotation=Quaternion.identity;
                Capture(camera,"Shots/foundry-map-player.png");
                foreach(var p in map.EnergyPillars)
                {
                    p.TakeDamage(800);
                    Check("pillar switches to authored ruin "+p.Index,!p.IntactVisual.activeSelf&&p.BrokenVisual.activeSelf&&p.BrokenVisual.GetComponentsInChildren<Collider>().All(c=>c.enabled));
                    Check("pillar cannot break twice "+p.Index,!p.TakeDamage(800));
                }
            }
            finally {SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
            log.AppendLine($"RESULT Foundry map {pass}/{pass+fail} passed");
            File.WriteAllText("Logs/foundry-map-check.log",log.ToString());Debug.Log(log.ToString());
            if(fail>0)throw new InvalidOperationException("Foundry map verification failed: "+fail);
        }
        public static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1600,1000,24);var old=RenderTexture.active;var target=camera.targetTexture;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=target;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);}
        }
    }
}

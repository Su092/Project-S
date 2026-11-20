using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class STheaterMapBuilder
    {
        public const string Delivery = "_Inbox/Environment/GrandTheater";
        public const string ScenePath = "Assets/Scenes/Environment/GrandTheater_Walkthrough.unity";
        const string DataRoot = "Assets/_Game/Editor/TheaterData";
        const string ModelRoot = "Assets/_Game/Res/Model/Environment/GrandTheater";
        const string NavPath = "Assets/_Game/Res/Prefab/Environment/GrandTheater/Navigation.asset";
        [Serializable] public class Node { public float[] position; }
        [Serializable] public class Route { public string id; public Node[] nodes; }
        [Serializable] public class Markers { public Node spawn; public Route[] routes; }
        static double next;
        static STheaterMapBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup<next)return;
            next=EditorApplication.timeSinceStartup+1;
            foreach(var action in new[]{"build","check"})
            {
                string request="Logs/theater-"+action+".request";
                if(!File.Exists(request))continue;
                File.Delete(request);
                try { if(action=="build")Build();else Validate(); }
                catch(Exception e){File.WriteAllText("Logs/theater-error.log",e.ToString());Debug.LogException(e);}
            }
        }
        static void Move(string src,string dst)
        {
            if(!AssetDatabase.LoadMainAssetAtPath(src) && !AssetDatabase.IsValidFolder(src))return;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));AssetDatabase.Refresh();
            string error=AssetDatabase.MoveAsset(src,dst);
            if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
        }
        static Transform Marker(string name,Transform parent,Vector3 at)
        { var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=at;return go.transform; }
        public static Markers ReadMarkers()=>JsonUtility.FromJson<Markers>(File.ReadAllText(Delivery+"/Layout/TheaterMarkers.json"));
        public static Vector3 Pos(Node n)=>new Vector3(n.position[0],n.position[1],n.position[2]);
        [MenuItem("S/Map/Build Grand Theater")]
        public static void Build()
        {
            foreach(var s in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
                if(s.isDirty)throw new InvalidOperationException("Unsaved scene: "+s.path);
            Directory.CreateDirectory("Logs");Directory.CreateDirectory("Shots");
            if(File.Exists("Logs/theater-error.log"))File.Delete("Logs/theater-error.log");
            Move("Assets/GrandTheater/Modules",ModelRoot);
            Move("Assets/GrandTheater/GrandTheater_Collisions.fbx",ModelRoot+"/GrandTheater_Collisions.fbx");
            Move("Assets/GrandTheater/Textures","Assets/_Game/Res/Texture/Environment/GrandTheater");
            Move("Assets/GrandTheater/Materials","Assets/_Game/Res/Material/Environment/GrandTheater");
            Move("Assets/GrandTheater/GrandTheater_Walkthrough.unity",ScenePath);
            Move("Assets/GrandTheater/TheaterMarkers.json",DataRoot+"/TheaterMarkers.json");
            Move("Assets/GrandTheater/TheaterLayout.json",DataRoot+"/TheaterLayout.json");
            Move("Assets/GrandTheater/TheaterPreviewController.cs","Assets/TheaterTools/TheaterPreviewController.cs");
            AssetDatabase.Refresh();
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root=scene.GetRootGameObjects().Single(x=>x.name=="GrandTheater");
                var map=root.GetComponent<FoundryArena>()??root.AddComponent<FoundryArena>();
                map.IsTheater=true;map.Width=64;map.Depth=72;map.EnergyPillars=Array.Empty<Pillar>();
                foreach(var c in root.GetComponentsInChildren<Collider>(true))
                {
                    string mesh=c is MeshCollider mc && mc.sharedMesh ? mc.sharedMesh.name : c.name;
                    c.gameObject.layer=mesh.Contains("Movement")?LayerMask.NameToLayer(WorldPhysics.MovementLayer):
                        mesh.Contains("Shot")?LayerMask.NameToLayer(WorldPhysics.ShotLayer):0;
                    c.includeLayers=0;c.excludeLayers=0;
                }
                foreach(var c in root.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(c.gameObject);
                foreach(var c in root.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(c);
                // Delivery preview lights need URP exposure compensation at gameplay distances.
                foreach(var light in root.GetComponentsInChildren<Light>()) light.intensity *= 12f;
                map.LayoutInstanceCount=root.GetComponentsInChildren<LODGroup>().Length;
                using(var sha=SHA256.Create())map.LayoutSha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Delivery+"/Layout/TheaterLayout.json"))).Replace("-","").ToLowerInvariant();
                map.PlayerSpawn=Marker("PlayerSpawn",root.transform,Pos(ReadMarkers().spawn)+Vector3.up*.06f);
                map.BossSpawn=Marker("BossSpawn",root.transform,new Vector3(0,1.24f,19));
                Vector3[] spawns={
                    new(-21,0,-20),new(21,0,-20),new(-21,0,6),new(21,0,6),
                    new(-23,0,32),new(23,0,32),new(-10,1.2f,17),new(10,1.2f,17),
                    new(-20.5f,6,-20),new(20.5f,6,-20),new(-20.5f,6,2),new(20.5f,6,2),
                    new(-20.5f,6,18),new(20.5f,6,18),new(-12,6,31),new(12,6,31)};
                var spawnRoot=Marker("SpawnPoints",root.transform,Vector3.zero);
                map.EnemySpawns=spawns.Select((p,i)=>Marker("Spawn_"+i.ToString("00"),spawnRoot,p+Vector3.up*.06f)).ToArray();
                var sources=new List<NavMeshBuildSource>();
                NavMeshBuilder.CollectSources(root.transform,WorldPhysics.GroundMask,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
                var settings=NavMesh.GetSettingsByID(0);
                settings.agentRadius=.5f;settings.agentHeight=2;settings.agentClimb=.4f;settings.agentSlope=50;
                settings.overrideVoxelSize=true;settings.voxelSize=.12f;
                var nav=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(0,9,0),new Vector3(70,24,78)),Vector3.zero,Quaternion.identity);
                if(!nav)throw new InvalidOperationException("Theater navigation bake failed.");
                Directory.CreateDirectory(Path.GetDirectoryName(NavPath));
                var existing=AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
                if(existing){EditorUtility.CopySerialized(nav,existing);UnityEngine.Object.DestroyImmediate(nav);nav=existing;EditorUtility.SetDirty(nav);}
                else AssetDatabase.CreateAsset(nav,NavPath);
                map.Navigation=nav;
                map.ValidateBindings();
                SScoreWorkbenchSetup.AddDevice(root);
                Directory.CreateDirectory(Path.GetDirectoryName(FoundryArena.ActiveAssetPath));
                PrefabUtility.SaveAsPrefabAsset(root,FoundryArena.ActiveAssetPath);
                File.WriteAllText("Logs/theater-build.log",$"modules={map.LayoutInstanceCount} colliders={root.GetComponentsInChildren<Collider>().Length} navSources={sources.Count}\n"+
                    string.Join("\n",root.GetComponentsInChildren<Collider>().GroupBy(x=>x.gameObject.layer).Select(g=>$"layer={g.Key} count={g.Count()}")));
            }
            finally{SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
            var main=SceneManager.GetSceneByPath(SceneBuilder.ScenePath);
            bool opened=!main.IsValid();
            if(opened)main=EditorSceneManager.OpenScene(SceneBuilder.ScenePath,OpenSceneMode.Additive);
            try { AttachToScene(main);EditorSceneManager.SaveScene(main); }
            finally{if(opened)EditorSceneManager.CloseScene(main,true);}
            SAddressablesSetup.Build();AssetDatabase.SaveAssets();
            Validate();
            File.AppendAllText("Logs/theater-build.log","\nRESULT Theater prefab and Main scene replaced\n");
        }
        public static void AttachToScene(Scene scene)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FoundryArena.ActiveAssetPath);
            if(!prefab)throw new InvalidOperationException("Build GrandTheater before creating Main.");
            foreach(var root in scene.GetRootGameObjects())
                if(root.GetComponent<FoundryArena>())UnityEngine.Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            instance.GetComponent<FoundryArena>().ApplyEnvironment();
            SceneManager.SetActiveScene(previous);
        }
        [MenuItem("S/Map/Verify Grand Theater")]
        public static void Validate()
        {
            var log=new StringBuilder();int pass=0,fail=0;
            void Check(string text,bool ok){log.AppendLine((ok?"PASS ":"FAIL ")+text);if(ok)pass++;else fail++;}
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            NavMeshDataInstance nav=default;
            try
            {
                var map=((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FoundryArena.ActiveAssetPath),scene)).GetComponent<FoundryArena>();
                var offset=new Vector3(1000,0,1000);map.transform.position=offset;map.ApplyEnvironment();Physics.SyncTransforms();
                nav=NavMesh.AddNavMeshData(map.Navigation,offset,Quaternion.identity);
                Check("1191 authored instances preserved",map.LayoutInstanceCount==1191);
                Check("all material and mesh references resolve",map.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m&&m.shader))&&map.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh));
                Check("no old pillars or preview player",map.EnergyPillars.Length==0&&!map.GetComponentInChildren<Pillar>(true)&&!map.GetComponentInChildren<Camera>(true)&&!map.GetComponentInChildren<CharacterController>(true));
                Check("all modules keep two LODs",map.GetComponentsInChildren<LODGroup>().All(x=>x.GetLODs().Length==2));
                Check("stairs movement and shot surfaces separated",map.GetComponentsInChildren<Collider>().Any(c=>c.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.MovementLayer))&&map.GetComponentsInChildren<Collider>().Any(c=>c.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.ShotLayer)));
                bool Clear(Vector3 p)=>!Physics.OverlapCapsule(p+Vector3.up*.38f,p+Vector3.up*1.42f,.34f,WorldPhysics.GroundMask,QueryTriggerInteraction.Ignore).Any(c=>c.gameObject.scene==scene);
                Check("player spawn clear",Clear(map.PlayerSpawn.position));
                foreach(var spawn in map.EnemySpawns)Check(spawn.name+" clear and grounded",Clear(spawn.position)&&Physics.Raycast(spawn.position+Vector3.up*.5f,Vector3.down,1,WorldPhysics.GroundMask,QueryTriggerInteraction.Ignore));
                var body=new GameObject("ProductionControllerProbe");SceneManager.MoveGameObjectToScene(body,scene);
                var cc=body.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.34f;cc.center=Vector3.up*.9f;cc.stepOffset=.4f;cc.slopeLimit=50;
                WorldPhysics.ConfigureActor(body);
                foreach(var route in ReadMarkers().routes)
                {
                    foreach(bool reverse in new[]{false,true})
                    {
                        var nodes=route.nodes.Select(Pos).ToArray();if(reverse)Array.Reverse(nodes);
                        cc.enabled=false;body.transform.position=offset+nodes[0]+Vector3.up*.06f;cc.enabled=true;
                        bool ok=true;
                        for(int n=1;n<nodes.Length;n++)
                        {
                            Vector3 end=offset+nodes[n];int limit=25000;
                            while(limit-->0)
                            {
                                Vector3 delta=end-body.transform.position;delta.y=0;
                                if(delta.magnitude<.055f)break;
                                cc.Move(delta.normalized*.025f+Vector3.down*.015f);
                            }
                            if(limit<=0||Mathf.Abs(body.transform.position.y-end.y)>.28f){ok=false;break;}
                        }
                        Check("controller route "+route.id+(reverse?" reverse":" forward"),ok);
                    }
                }
                cc.enabled=false;
                foreach(var spawn in map.EnemySpawns)
                {
                    var path=new NavMeshPath();
                    bool a=NavMesh.SamplePosition(map.PlayerSpawn.position,out var start,.8f,NavMesh.AllAreas);
                    bool b=NavMesh.SamplePosition(spawn.position,out var end,.8f,NavMesh.AllAreas);
                    Check("navigation "+spawn.name,a&&b&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete);
                }
                var cam=new GameObject("TheaterCapture").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(cam.gameObject,scene);
                cam.farClipPlane=160;cam.nearClipPlane=.05f;cam.fieldOfView=75;map.ApplyEnvironment(cam);
                foreach(var view in new[]{(name:"entrance",p:new Vector3(0,1.65f,-29),target:new Vector3(0,5,20)),(name:"auditorium",p:new Vector3(0,1.65f,-8),target:new Vector3(0,5,24)),(name:"balcony",p:new Vector3(-20,7.65f,-14),target:new Vector3(0,3,18)),(name:"stage",p:new Vector3(0,2.85f,19),target:new Vector3(0,4,-15))})
                {cam.transform.position=offset+view.p;cam.transform.LookAt(offset+view.target);SFoundryMapBuilder.Capture(cam,"Shots/theater-"+view.name+".png");}
            }
            finally {if(nav.valid)nav.Remove();SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
            log.AppendLine($"RESULT Theater {pass}/{pass+fail} passed");File.WriteAllText("Logs/theater-check.log",log.ToString());Debug.Log(log);
            if(fail>0)throw new InvalidOperationException("Theater validation failed: "+fail);
        }
    }
}

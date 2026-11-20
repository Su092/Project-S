using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
namespace FoundryDelivery {
[Serializable] public class Entry { public string key,group; public float x,y,z,rotation; }
[Serializable] public class Point { public float x,y,z; }
[Serializable] public class Layout { public Entry[] instances; public Point[] spawns; public Point player; }
[InitializeOnLoad] public static class FoundryAssembly {
const string Root="E:/S/_Inbox/Environment/AbandonedFoundry";
const string Res="Assets/_Game/Res/";
const string Model=Res+"Model/Environment/AbandonedFoundry/";
const string Tex=Res+"Texture/Environment/AbandonedFoundry/";
const string Pref=Res+"Prefab/Environment/AbandonedFoundry/";
const string Mats=Res+"Material/Environment/AbandonedFoundry/";
const string Scenes=Res+"Scene/Environment/AbandonedFoundry/";
static double next;
static FoundryAssembly(){EditorApplication.update+=Poll;}
static void Poll(){
 if(EditorApplication.timeSinceStartup<next || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
 next=EditorApplication.timeSinceStartup+5;
 if(!File.Exists(Root+"/BUILD_UNITY"))return;
 File.Delete(Root+"/BUILD_UNITY");
 try{Build();}catch(Exception e){File.WriteAllText(Root+"/Reports/UnityError.txt",e.ToString());Debug.LogException(e);}
}
[MenuItem("Tools/Foundry/Build Delivery Example")]
public static void Build(){
 foreach(var leaked in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(g=>g.name=="ENV_Foundry_Floor_Concrete"&&g.transform.parent==null).ToArray())UnityEngine.Object.DestroyImmediate(leaked);
 Directory.CreateDirectory(Pref);Directory.CreateDirectory(Mats);Directory.CreateDirectory(Scenes);
 var report=new List<string>();
 int move=Layer("FoundryMovement"),shot=Layer("FoundryShot"),player=Layer("FoundryPlayer");
 Physics.IgnoreLayerCollision(player,shot,true);
 var colors=new Dictionary<string,Color>{{"Concrete",new Color(.29f,.33f,.35f)},{"Steel",new Color(.13f,.19f,.23f)},{"Rust",new Color(.26f,.13f,.065f)},{"Trim",new Color(.22f,.27f,.29f)},{"Orange",new Color(.7f,.16f,.025f)},{"Cyan",new Color(.02f,.55f,.65f)}};
 var mats=new Dictionary<string,Material>();
 foreach(var k in colors.Keys){
  foreach(string suffix in new[]{"BaseColor","Normal","Mask"}){
   string path=Tex+"T_Foundry_"+k+"_"+suffix+".png";
   var imp=AssetImporter.GetAtPath(path) as TextureImporter;
   if(imp==null)throw new Exception("Missing texture "+path);
   imp.textureType=suffix=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
   imp.sRGBTexture=suffix=="BaseColor";imp.alphaSource=TextureImporterAlphaSource.FromInput;imp.wrapMode=TextureWrapMode.Repeat;
   imp.maxTextureSize=1024;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();
  }
  string mp=Mats+"MAT_Foundry_"+k+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);
  if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,mp);}
  mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Tex+"T_Foundry_"+k+"_BaseColor.png"));
  mat.SetColor("_BaseColor",Color.white);
  mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Tex+"T_Foundry_"+k+"_Normal.png"));mat.EnableKeyword("_NORMALMAP");
  mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Tex+"T_Foundry_"+k+"_Mask.png"));mat.EnableKeyword("_METALLICSPECGLOSSMAP");
  mat.SetFloat("_Smoothness",1);mat.SetFloat("_BumpScale",.4f);
  mat.SetTexture("_OcclusionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Tex+"T_Foundry_"+k+"_Mask.png"));
  if(k=="Cyan"||k=="Orange"){mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",colors[k]*2);}
  EditorUtility.SetDirty(mat);mats[k]=mat;
 }
 var prefabs=new Dictionary<string,GameObject>();
 foreach(string full in Directory.GetFiles(Model,"*.fbx")){
  string path=full.Replace('\\','/');string key=Path.GetFileNameWithoutExtension(path);
  var imp=(ModelImporter)AssetImporter.GetAtPath(path);
  imp.globalScale=1;imp.useFileScale=true;imp.bakeAxisConversion=true;imp.importCameras=false;imp.importLights=false;
  imp.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
  imp.generateSecondaryUV=false;imp.isReadable=true;imp.SaveAndReimport();
  var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
  var go=(GameObject)PrefabUtility.InstantiatePrefab(asset);go.name="ENV_Foundry_"+key;
  // Bake FBX handedness conversion once into reusable Unity meshes, preserving unit scale and pivots.
  string meshDir=Model+"UnityMeshes";Directory.CreateDirectory(meshDir);
  foreach(var f in go.GetComponentsInChildren<MeshFilter>(true)){
   var mesh=UnityEngine.Object.Instantiate(f.sharedMesh);
   Matrix4x4 transform=go.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
   var vs=mesh.vertices;for(int vi=0;vi<vs.Length;vi++){var p=transform.MultiplyPoint3x4(vs[vi]);vs[vi]=new Vector3(-p.x,p.y,p.z);}mesh.vertices=vs;
   var ns=mesh.normals;var normalMatrix=transform.inverse.transpose;for(int vi=0;vi<ns.Length;vi++){var p=normalMatrix.MultiplyVector(ns[vi]).normalized;ns[vi]=new Vector3(-p.x,p.y,p.z);}mesh.normals=ns;
   for(int sub=0;sub<mesh.subMeshCount;sub++){var tr=mesh.GetTriangles(sub);for(int ti=0;ti<tr.Length;ti+=3){int swap=tr[ti];tr[ti]=tr[ti+2];tr[ti+2]=swap;}mesh.SetTriangles(tr,sub);}
   mesh.RecalculateBounds();if(mesh.uv.Length>0)mesh.RecalculateTangents();
   string meshPath=meshDir+"/"+f.name+".asset";var oldMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
   if(oldMesh){EditorUtility.CopySerialized(mesh,oldMesh);UnityEngine.Object.DestroyImmediate(mesh);mesh=oldMesh;}
   else AssetDatabase.CreateAsset(mesh,meshPath);
   f.transform.SetParent(go.transform);f.transform.localPosition=Vector3.zero;f.transform.localRotation=Quaternion.identity;f.transform.localScale=Vector3.one;f.sharedMesh=mesh;
  }
  var renderers=go.GetComponentsInChildren<MeshRenderer>(true);
  var l0=new List<Renderer>();var l1=new List<Renderer>();
  foreach(var r in renderers){
   var filter=r.GetComponent<MeshFilter>();
   if(r.name.StartsWith("COL_")){
    var mb=filter.sharedMesh.bounds;
    bool box=filter.sharedMesh.vertices.All(v=>(Mathf.Abs(v.x-mb.min.x)<.0001f||Mathf.Abs(v.x-mb.max.x)<.0001f)&&(Mathf.Abs(v.y-mb.min.y)<.0001f||Mathf.Abs(v.y-mb.max.y)<.0001f)&&(Mathf.Abs(v.z-mb.min.z)<.0001f||Mathf.Abs(v.z-mb.max.z)<.0001f))&&filter.sharedMesh.triangles.Length==36;
    if(box){var bc=r.gameObject.AddComponent<BoxCollider>();bc.center=mb.center;bc.size=mb.size;}
    else {var mc=r.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=filter.sharedMesh;mc.convex=false;}
    r.gameObject.layer=r.name.EndsWith("_Move")?move:(r.name.EndsWith("_Shot")?shot:0);
    UnityEngine.Object.DestroyImmediate(r);continue;
   }
   r.sharedMaterials=r.sharedMaterials.Select(m=>{
    string n=m?m.name:"MAT_Foundry_Steel";return mats.FirstOrDefault(kv=>n.Contains(kv.Key)).Value??mats["Steel"];
   }).ToArray();
   if(r.name.Contains("_LOD1"))l1.Add(r);else l0.Add(r);
   r.gameObject.isStatic=true;
  }
  foreach(var lg in go.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(lg);
  var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new UnityEngine.LOD(.12f,l0.ToArray()),new UnityEngine.LOD(.001f,l1.ToArray())});lod.RecalculateBounds();
  // Assert the imported floor has the authored Unity coordinate convention and meters.
  if(key=="Floor_Concrete"){
   Bounds b=l0[0].bounds;report.Add("Floor import bounds "+b);
   if(Mathf.Abs(b.size.x-4)>.01f||Mathf.Abs(b.size.z-4)>.01f)throw new Exception("FBX meter-scale failed: "+b);
   report.Add("Axis probe center="+b.center+" expected (2,-0.125,-2)");
   if(Vector3.Distance(b.center,new Vector3(2,-.125f,-2))>.02f)throw new Exception("FBX axis convention mismatch: "+b.center);
  }
  prefabs[key]=PrefabUtility.SaveAsPrefabAsset(go,Pref+key+".prefab");UnityEngine.Object.DestroyImmediate(go);
 }
 AssetDatabase.SaveAssets();
 Scene prev=SceneManager.GetActiveScene();
 Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
 SceneManager.SetActiveScene(scene);
 var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(Root+"/Reports/layout.json"));
 var groups=new Dictionary<string,Transform>();
 foreach(var e in layout.instances){
  if(!groups.ContainsKey(e.group)){var g=new GameObject(e.group);groups[e.group]=g.transform;}
  var o=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[e.key],scene);
  o.transform.SetParent(groups[e.group]);o.transform.position=new Vector3(e.x,e.z,-e.y);o.transform.rotation=Quaternion.Euler(0,e.rotation,0);
 }
 var spawnRoot=new GameObject("SpawnPoints").transform;
 for(int i=0;i<layout.spawns.Length;i++){var p=layout.spawns[i];var o=new GameObject("Spawn_"+i.ToString("00"));o.transform.SetParent(spawnRoot);o.transform.position=new Vector3(p.x,.1f,-p.y);}
 var pl=new GameObject("PlayerSpawn");pl.transform.position=new Vector3(0,1.2f,-38);
 var cameraObj=new GameObject("PlayerView");cameraObj.tag="MainCamera";var cam=cameraObj.AddComponent<Camera>();cameraObj.transform.position=new Vector3(0,1.7f,-38);cam.fieldOfView=75;cam.clearFlags=CameraClearFlags.SolidColor;cam.farClipPlane=300;cam.backgroundColor=new Color(.065f,.09f,.12f);
 var sun=new GameObject("Overcast Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(45,-30,0);sun.shadows=LightShadows.Soft;
 RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.32f,.39f,.47f);
 RenderSettings.fog=true;RenderSettings.fogColor=new Color(.12f,.16f,.2f);RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=75;RenderSettings.fogEndDistance=190;
 foreach(var p in new[]{new Vector3(-10,3,-10),new Vector3(10,3,-10),new Vector3(-10,3,10),new Vector3(10,3,10)}){
 var light=new GameObject("Core Cyan").AddComponent<Light>();light.type=LightType.Point;light.color=Color.cyan;light.intensity=5;light.range=9;light.transform.position=p;
 }
 var body=new GameObject("InspectionPlayer");body.layer=player;body.transform.position=new Vector3(0,.05f,-38);
 var cc=body.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.35f;cc.center=Vector3.up*.9f;cc.stepOffset=.3f;cc.slopeLimit=45;cc.skinWidth=.03f;
 cameraObj.transform.SetParent(body.transform);cameraObj.transform.localPosition=Vector3.up*1.7f;
 body.AddComponent<FoundryWalkthrough>().view=cam;
 cc.enabled=false;
 Physics.SyncTransforms();
 int moveMask=~((1<<shot)|(1<<player));int shotMask=~(1<<move);
 report.Add("Prefabs="+prefabs.Count+" Instances="+layout.instances.Length+" SpawnPoints="+layout.spawns.Length);
 report.Add("Movement layer="+move+" Shot layer="+shot+" Player layer="+player);
 // Only collisions belonging to this additive scene count.
 Func<Vector3,bool> blocked=p=>Physics.OverlapCapsule(p+Vector3.up*.36f,p+Vector3.up*1.44f,.35f,moveMask,QueryTriggerInteraction.Ignore).Any(c=>c.gameObject.scene==scene);
 int bad=0;
 foreach(var p in layout.spawns)if(blocked(new Vector3(p.x,.05f,-p.y)))bad++;
 report.Add("Spawn capsule overlaps="+bad);
 if(blocked(new Vector3(0,.05f,-38)))throw new Exception("Player spawn blocked");
 // 0.5m ground-plane occupancy grid; flood from player; nodes use 0.35m capsule.
 int n=199;bool[,] free=new bool[n,n];bool[,] seen=new bool[n,n];int total=0,reached=0;
 for(int x=0;x<n;x++)for(int z=0;z<n;z++){var p=new Vector3((x-99)*.5f,.32f,(z-99)*.5f);free[x,z]=!blocked(p);if(free[x,z])total++;}
 var queue=new Queue<Vector2Int>();queue.Enqueue(new Vector2Int(99,23));seen[99,23]=true;
 var dirs=new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
 while(queue.Count>0){var p=queue.Dequeue();reached++;foreach(var d in dirs){var q=p+d;if(q.x<0||q.x>=n||q.y<0||q.y>=n||seen[q.x,q.y]||!free[q.x,q.y])continue;seen[q.x,q.y]=true;queue.Enqueue(q);}}
 report.Add("Ground connected capsule grid="+reached+"/"+total+" (platform interior not ground route)");
 int spawnConnected=0;foreach(var p in layout.spawns){int x=Mathf.RoundToInt(p.x*2)+99,z=Mathf.RoundToInt(-p.y*2)+99;if(seen[x,z])spawnConnected++;}
 report.Add("Connected spawn routes="+spawnConnected+"/16");
 bool coreClear=true;
 for(int a=0;a<360;a+=5){float r=a*Mathf.Deg2Rad;if(blocked(new Vector3(Mathf.Cos(r)*8,.32f,Mathf.Sin(r)*8)))coreClear=false;}
 report.Add("Boss radius8 capsule orbit="+coreClear);
 // Wedges and steps: vertical ray samples must have matching heights.
 int rampFailures=0;
 foreach(var e in layout.instances.Where(e=>e.key=="Platform_Ramp4x8"||e.key=="Platform_Stairs4x5")){
  float len=e.key.Contains("Stairs")?5:8;
  for(int j=1;j<20;j++){
   Vector3 local=new Vector3(2,0,-len*j/20f);var p=new Vector3(e.x,e.z,-e.y)+Quaternion.Euler(0,e.rotation,0)*local;
   var hits=Physics.RaycastAll(p+Vector3.up*5,Vector3.down,10,moveMask).Where(h=>h.collider.gameObject.scene==scene).OrderBy(h=>h.distance).ToArray();
   if(hits.Length==0||Mathf.Abs(hits[0].point.y-2.5f*j/20f)>.03f)rampFailures++;
  }
 }
 report.Add("Ramp/stair movement profile failures="+rampFailures);
 report.Add("Collision renderers remaining="+scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>()).Count(r=>r.name.StartsWith("COL_")));
 // Real CharacterController.Move traversal of every ascent; no modification of existing gameplay controller.
 int walkFailures=0;
 cc.enabled=true;
 foreach(var e in layout.instances.Where(e=>e.key=="Platform_Ramp4x8"||e.key=="Platform_Stairs4x5")){
  float len=e.key.Contains("Stairs")?5:8;var rot=Quaternion.Euler(0,e.rotation,0);var start=new Vector3(e.x,0,-e.y)+rot*new Vector3(2,.05f,1);
  cc.enabled=false;body.transform.position=start;cc.enabled=true;
  for(int k=0;k<(len+2)*100;k++)cc.Move(rot*new Vector3(0,-.025f,-.01f));
  if(body.transform.position.y<2.4f)walkFailures++;
 }
 report.Add("CharacterController eight ascents failures="+walkFailures);
 cc.enabled=false;body.transform.position=new Vector3(0,.05f,-38);cc.enabled=true;
 report.Add("Inspection player movement uses layer mask separation; production gameplay integration remains separate.");

 string scenePath=Scenes+"Foundry_Arena.unity";
 EditorSceneManager.SaveScene(scene,scenePath);
 Capture(cam,Root+"/Previews/Unity_Player.png");
 cameraObj.transform.position=new Vector3(80,75,-94);cameraObj.transform.LookAt(Vector3.zero);Capture(cam,Root+"/Previews/Unity_Arena.png");
 cameraObj.transform.position=new Vector3(0,1.7f,-38);cameraObj.transform.rotation=Quaternion.identity;
 EditorSceneManager.SaveScene(scene,scenePath);
 File.WriteAllLines(Root+"/Reports/UnityValidation.txt",report);
 SceneManager.SetActiveScene(prev);EditorSceneManager.CloseScene(scene,true);
 AssetDatabase.SaveAssets();File.WriteAllText(Root+"/UNITY_DONE","complete");if(File.Exists(Root+"/Reports/UnityError.txt"))File.Move(Root+"/Reports/UnityError.txt",Root+"/Reports/Resolved_InitialAxisError.txt");
}
static void Capture(Camera c,string path){
 var rt=new RenderTexture(1600,1000,24);c.targetTexture=rt;c.Render();var old=RenderTexture.active;RenderTexture.active=rt;
 var t=new Texture2D(1600,1000,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1600,1000),0,0);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());
 c.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(t);UnityEngine.Object.DestroyImmediate(rt);
}
static int Layer(string name){
 int i=LayerMask.NameToLayer(name);if(i>=0)return i;
 var obj=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var ar=obj.FindProperty("layers");
 for(i=31;i>=8;i--)if(string.IsNullOrEmpty(ar.GetArrayElementAtIndex(i).stringValue)){ar.GetArrayElementAtIndex(i).stringValue=name;obj.ApplyModifiedProperties();return i;}
 throw new Exception("No free physics layer for "+name);
}
}
}

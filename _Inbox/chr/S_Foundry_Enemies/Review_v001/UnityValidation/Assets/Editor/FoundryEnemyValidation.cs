using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class FoundryEnemyValidation {
 [Serializable] public class Entry { public string asset; public Vector3 dimensions; public int meshes,bones,materialSlots,animationClips,invalidNormals; public bool genericAvatar; public string forwardEvidence; public float forwardZ; }
 [Serializable] public class Report { public string engine; public string scope; public List<Entry> assets=new(); }
 public static void Run() {
  var report=new Report {engine=Application.unityVersion,scope="Independent validation project. Imported FBX bounds, normals, Generic avatar and animation clips. No gameplay integration."};
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var modelPaths=Directory.GetFiles("Assets/Enemies","*_Model.fbx",SearchOption.AllDirectories);
  float cursor=0;
  foreach(var raw in modelPaths) {
   var path=raw.Replace('\\','/');string name=Path.GetFileName(path).Replace("_Model.fbx","");
   var importer=(ModelImporter)AssetImporter.GetAtPath(path);
   importer.animationType=ModelImporterAnimationType.Generic; importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
   importer.importAnimation=false; importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
   var entry=new Entry {asset=name};var renderers=go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   var lod0=renderers.FirstOrDefault(x=>x.name.EndsWith("_LOD0"));
   if(lod0!=null) {
    var baked=new Mesh();lod0.BakeMesh(baked);
    var points=baked.vertices.Select(v=>lod0.transform.TransformPoint(v)).ToArray();
    var bounds=new Bounds(points[0],Vector3.zero);foreach(var v in points)bounds.Encapsulate(v);
    entry.dimensions=bounds.size;entry.bones=lod0.bones.Length;entry.materialSlots=lod0.sharedMaterials.Length;
    foreach(var n in baked.normals)if(float.IsNaN(n.x)||float.IsInfinity(n.x)||n.sqrMagnitude<.5f)entry.invalidNormals++;
   }
   entry.meshes=renderers.Length;
   var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
   entry.genericAvatar=avatar!=null&&avatar.isValid&&!avatar.isHuman;
   var transforms=go.GetComponentsInChildren<Transform>(true);
   var front=transforms.FirstOrDefault(x=>x.name=="SOCKET_Muzzle")??transforms.FirstOrDefault(x=>x.name=="SOCKET_Attack")??transforms.FirstOrDefault(x=>x.name=="SOCKET_Volley_C");
   entry.forwardEvidence=front!=null?front.name:"No directional socket; covered by common export transform, manual check pending";
   entry.forwardZ=front!=null?(front.position-go.transform.position).z:0;
   foreach(var af in Directory.GetFiles(Path.GetDirectoryName(path),"AN_*.fbx")) {
    var ap=af.Replace('\\','/');var ai=(ModelImporter)AssetImporter.GetAtPath(ap);
    ai.animationType=ModelImporterAnimationType.Generic;ai.importAnimation=true;ai.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;ai.SaveAndReimport();
    entry.animationClips+=AssetDatabase.LoadAllAssetsAtPath(ap).OfType<AnimationClip>().Count(x=>!x.name.StartsWith("__preview__"));
   }
   // Explicit LOD assignment for validation scene only, no collision auto-binding.
   var lg=go.GetComponent<LODGroup>()??go.AddComponent<LODGroup>();var levels=new List<LOD>();
   for(int i=0;i<3;i++){var rr=renderers.Where(x=>x.name.EndsWith("_LOD"+i)).Cast<Renderer>().ToArray();levels.Add(new LOD(new[]{.45f,.20f,.06f}[i],rr));}
   lg.SetLODs(levels.ToArray());lg.RecalculateBounds();
   go.transform.position=new Vector3(cursor,0,0);cursor+=entry.dimensions.x+1;
   report.assets.Add(entry);
  }
  File.WriteAllText("UnityImportReport.json",JsonUtility.ToJson(report,true));
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/EnemyValidation.unity");
  AssetDatabase.SaveAssets();Debug.Log("FOUNDRY_VALIDATION_COMPLETE "+report.assets.Count);
 }
}

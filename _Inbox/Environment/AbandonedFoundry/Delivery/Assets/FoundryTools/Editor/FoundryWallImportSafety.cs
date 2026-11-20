using System.Linq;
using UnityEngine;
using UnityEditor;
namespace FoundryDelivery {
public sealed class FoundryWallImportSafety : AssetPostprocessor {
 public override int GetPostprocessOrder(){return 2000;}
 void OnPreprocessModel(){
 if(!assetPath.EndsWith("/Environment/AbandonedFoundry/Wall_Straight4.fbx"))return;
 var m=(ModelImporter)assetImporter;m.importNormals=ModelImporterNormals.Import;m.importTangents=ModelImporterTangents.CalculateMikk;m.generateSecondaryUV=false;m.isReadable=true;m.addCollider=false;
 }
 void OnPostprocessModel(GameObject root){
 if(!assetPath.EndsWith("/Environment/AbandonedFoundry/Wall_Straight4.fbx"))return;
 foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){
 if(!f.name.StartsWith("COL_"))continue;
 foreach(var r in f.GetComponents<Renderer>())Object.DestroyImmediate(r);
 foreach(var c in f.GetComponents<Collider>())Object.DestroyImmediate(c);
 var mc=f.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=f.sharedMesh;mc.convex=false;
 }
 foreach(var g in root.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(g);
 var rs=root.GetComponentsInChildren<Renderer>(true);
 var zero=rs.Where(r=>r.name.EndsWith("_LOD0")).ToArray();
 var one=rs.Where(r=>r.name.EndsWith("_LOD1")).ToArray();
 var group=root.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.animateCrossFading=false;
 group.SetLODs(new[]{new LOD(.12f,zero),new LOD(.001f,one)});group.RecalculateBounds();
 foreach(var r in rs){
 var shared=r.sharedMaterials;
 for(int i=0;i<shared.Length;i++){
 if(!shared[i])continue;
 var replacement=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Res/Material/Environment/AbandonedFoundry/"+shared[i].name+".mat");
 if(replacement)shared[i]=replacement;
 }
 r.sharedMaterials=shared;
 }
 }
}
}
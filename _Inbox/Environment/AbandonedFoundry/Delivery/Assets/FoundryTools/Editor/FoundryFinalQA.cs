using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace FoundryDelivery{
[InitializeOnLoad] public static class FoundryFinalQA{
 const string Root="E:/S/_Inbox/Environment/AbandonedFoundry";
 static double next;
 static FoundryFinalQA(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating)return;next=EditorApplication.timeSinceStartup+3;
 if(!File.Exists(Root+"/QA_FINAL"))return;File.Delete(Root+"/QA_FINAL");
 try{Run();}catch(Exception e){File.WriteAllText(Root+"/Reports/FinalQAError.txt",e.ToString());}
 }
 static void Run(){
 var prev=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
 var lines=new List<string>();int total=0,early=0;
 foreach(string key in new[]{"Wall_WideDoor8","Wall_InnerCorner","Platform_Stairs4x5","Platform_Ramp4x8","Cover_CrateLow","Cover_CrateHigh","Cover_ShortWall","Cover_Barrel","Core_EnergyBase","Core_EnergyPillar"}){
 var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Res/Prefab/Environment/AbandonedFoundry/"+key+".prefab");
 var root=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
 var colliders=root.GetComponentsInChildren<Collider>().Where(c=>!c.name.EndsWith("_Move")).ToArray();
 var visual=new List<MeshCollider>();Bounds bounds=new Bounds();bool first=true;
 foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.Contains("_LOD0"))){
 if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);
 var ob=new GameObject("visualShotProbe");ob.transform.SetParent(r.transform,false);var c=ob.AddComponent<MeshCollider>();c.sharedMesh=r.GetComponent<MeshFilter>().sharedMesh;visual.Add(c);
 }
 Physics.SyncTransforms();int errors=0;
 for(int axis=0;axis<3;axis++)for(int side=-1;side<=1;side+=2)for(int a=1;a<16;a++)for(int b=1;b<16;b++){
 int ai=(axis+1)%3,bi=(axis+2)%3;var p=bounds.min;p[axis]=bounds.center[axis]+side*(bounds.extents[axis]+2);p[ai]+=bounds.size[ai]*a/16;p[bi]+=bounds.size[bi]*b/16;
 var dir=Vector3.zero;dir[axis]=-side;Ray ray=new Ray(p,dir);
 float vd=float.PositiveInfinity,cd=float.PositiveInfinity;
 foreach(var c in visual)if(c.Raycast(ray,out var h,100))vd=Mathf.Min(vd,h.distance);
 foreach(var c in colliders)if(c.Raycast(ray,out var h,100))cd=Mathf.Min(cd,h.distance);
 total++;if(cd+.06f<vd){early++;errors++;}
 }
 lines.Add(key+": early collision >6cm = "+errors+" / 1350 rays");
 UnityEngine.Object.DestroyImmediate(root);
 }
 lines.Add("TOTAL rays="+total+" early="+early+"; sampled proxy-vs-LOD0 surface comparison, not exhaustive projectile integration.");
 File.WriteAllLines(Root+"/Reports/ShotCollisionQA.txt",lines);
 SceneManager.SetActiveScene(prev);EditorSceneManager.CloseScene(scene,true);
 File.WriteAllText(Root+"/QA_DONE","complete");
 }
}
}

using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
namespace FoundryDelivery{
[InitializeOnLoad]public static class FoundryFlickerAudit{
const string R="E:/S/_Inbox/Environment/AbandonedFoundry";
static double next;static FoundryFlickerAudit(){EditorApplication.update+=Poll;}
static void Poll(){if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating)return;next=EditorApplication.timeSinceStartup+2;if(!File.Exists(R+"/AUDIT_FLICKER"))return;File.Delete(R+"/AUDIT_FLICKER");Run();}
static string Info(Renderer r){var f=r.GetComponent<MeshFilter>();var lg=r.GetComponentInParent<LODGroup>();string mem=lg?string.Join(",",lg.GetLODs().Select((l,i)=>l.renderers.Contains(r)?i.ToString():"-")):"NO LOD";
return r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" pos="+r.transform.position+" mesh="+(f&&f.sharedMesh?AssetDatabase.GetAssetPath(f.sharedMesh):"none")+" submeshes="+(f&&f.sharedMesh?f.sharedMesh.subMeshCount:0)+" mats="+r.sharedMaterials.Length+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"null"))+" group="+(lg?lg.name:"none")+" membership="+mem;}
[Serializable]class V{public float x,y,z;}[Serializable]class MeshDump{public V[] vertices;public int[] triangles;}
static void Run(){var lines=new List<string>();
foreach(var p in new[]{"Assets/_Game/Res/Model/Environment/AbandonedFoundry/Wall_Straight4.fbx","Assets/_Game/Res/Prefab/Environment/AbandonedFoundry/Wall_Straight4.prefab"}){
var a=AssetDatabase.LoadAssetAtPath<GameObject>(p);lines.Add("ASSET "+p);foreach(var r in a.GetComponentsInChildren<Renderer>(true))lines.Add(Info(r));
foreach(var f in a.GetComponentsInChildren<MeshFilter>(true)){var m=f.sharedMesh;if(p.EndsWith(".prefab")){var payload=new MeshDump{vertices=m.vertices.Select(v=>new V{x=v.x,y=v.y,z=v.z}).ToArray(),triangles=m.triangles};File.WriteAllText(R+"/Reports/"+f.name+"_UnityMesh.json",JsonUtility.ToJson(payload));}
lines.Add("MESH "+f.name+" tris per submesh="+string.Join(",",Enumerable.Range(0,m.subMeshCount).Select(i=>m.GetIndexCount(i)/3)));}}
var all=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include).Where(r=>r.gameObject.scene.IsValid()).ToArray();
lines.Add("SCENE "+string.Join(",",Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path)));
foreach(var r in all.Where(r=>r.name.Contains("Wall_Straight4")||r.name.StartsWith("COL_")).Take(500))lines.Add(Info(r));
foreach(var g in all.Where(r=>r.name.Contains("Wall_Straight4")).GroupBy(r=>r.bounds.center.ToString("F3")+" "+r.bounds.size.ToString("F3")).Where(g=>g.Count()>1))lines.Add("COINCIDENT BOUNDS "+g.Key+" : "+string.Join(" | ",g.Select(r=>r.name)));
File.WriteAllLines(R+"/Reports/FlickerAudit.txt",lines);
}}
}
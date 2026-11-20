using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine.SceneManagement;
namespace FoundryDelivery{
[InitializeOnLoad]public static class FoundryWallRenderCheck{
const string R="E:/S/_Inbox/Environment/AbandonedFoundry";static double next;
static FoundryWallRenderCheck(){EditorApplication.update+=Poll;}
static void Poll(){if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlaying)return;next=EditorApplication.timeSinceStartup+3;if(!File.Exists(R+"/CHECK_WALL_RENDER"))return;File.Delete(R+"/CHECK_WALL_RENDER");try{Run();}catch(Exception e){File.WriteAllText(R+"/Reports/WallRenderError.txt",e.ToString());}}
static void Run(){
var prev=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
var report=new List<string>();int failures=0,samples=0;
var cam=new GameObject("Wall QA Camera").AddComponent<Camera>();cam.cullingMask=1<<2;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.15f,.18f);cam.fieldOfView=55;cam.nearClipPlane=.1f;cam.farClipPlane=400;var rt=new RenderTexture(1000,800,24);cam.targetTexture=rt;
var sun=new GameObject("Wall QA Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.cullingMask=1<<2;sun.transform.rotation=Quaternion.Euler(35,45,0);
RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
foreach(var kind in new[]{"FBX","Prefab"}){
string path=kind=="FBX"?"Assets/_Game/Res/Model/Environment/AbandonedFoundry/Wall_Straight4.fbx":"Assets/_Game/Res/Prefab/Environment/AbandonedFoundry/Wall_Straight4.prefab";
var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=2;
var groups=root.GetComponentsInChildren<LODGroup>(true);
var renderers=root.GetComponentsInChildren<Renderer>(true);int colR=renderers.Count(r=>r.name.StartsWith("COL_"));
report.Add(kind+" groups="+groups.Length+" renderers="+renderers.Length+" collisionRenderers="+colR);
if(groups.Length!=1||renderers.Length!=2||colR!=0)failures++;
var red=new Material(Shader.Find("Universal Render Pipeline/Unlit"));red.SetColor("_BaseColor",Color.red);
var green=new Material(Shader.Find("Universal Render Pipeline/Unlit"));green.SetColor("_BaseColor",Color.green);
foreach(var r in renderers)r.sharedMaterials=Enumerable.Repeat(r.name.EndsWith("_LOD0")?red:green,r.sharedMaterials.Length).ToArray();
var bounds=renderers.First(r=>r.name.EndsWith("_LOD0")).bounds;
foreach(var force in new[]{0,1,-1})foreach(float distance in new[]{8f,24f,64f,128f})foreach(float yaw in new[]{0f,45f,180f,225f}){
groups[0].ForceLOD(force);cam.transform.position=bounds.center+Quaternion.Euler(0,yaw,0)*new Vector3(0,distance*.25f,-distance);cam.transform.LookAt(bounds.center);
cam.Render();
var saved=RenderTexture.active;RenderTexture.active=rt;var readback=new Texture2D(1000,800,TextureFormat.RGB24,false);readback.ReadPixels(new Rect(0,0,1000,800),0,0);readback.Apply();
int reds=0,greens=0;foreach(var px in readback.GetPixels32()){if(px.r>100&&px.g<50&&px.b<50)reds++;if(px.g>100&&px.r<50&&px.b<50)greens++;}
UnityEngine.Object.DestroyImmediate(readback);RenderTexture.active=saved;
string[] drawn=(reds>0&&greens>0)?new[]{"LOD0","LOD1"}:reds>0?new[]{"LOD0"}:greens>0?new[]{"LOD1"}:new string[0];
samples++;if(drawn.Length!=1||(force==0&&greens>0)||(force==1&&reds>0))failures++;
report.Add(kind+" force="+force+" distance="+distance+" yaw="+yaw+" drawn="+string.Join(",",drawn));
if(distance==8&&yaw==0){
var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1000,800,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1000,800),0,0);tex.Apply();File.WriteAllBytes(R+"/Previews/WallFlicker_"+kind+"_"+force+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);RenderTexture.active=old;
}}
groups[0].ForceLOD(-1);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(red);UnityEngine.Object.DestroyImmediate(green);
}
FoundryWallRenderProbe.Target=null;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);
report.Add("RENDER_SAMPLES="+samples+" FAILURES="+failures);
File.WriteAllLines(R+"/Reports/WallRenderValidation.txt",report);
SceneManager.SetActiveScene(prev);EditorSceneManager.CloseScene(scene,true);
}
}
}
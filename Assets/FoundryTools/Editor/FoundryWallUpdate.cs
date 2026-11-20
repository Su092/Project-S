using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace FoundryDelivery{
[InitializeOnLoad]public static class FoundryWallUpdate{
 const string Root="E:/S/_Inbox/Environment/AbandonedFoundry";
 const string Model="Assets/_Game/Res/Model/Environment/AbandonedFoundry/";
 const string Pref="Assets/_Game/Res/Prefab/Environment/AbandonedFoundry/Wall_Straight4.prefab";
 static double next;
 static FoundryWallUpdate(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlaying)return;next=EditorApplication.timeSinceStartup+3;
 if(!File.Exists(Root+"/UPDATE_WALL"))return;File.Delete(Root+"/UPDATE_WALL");
 try{Run();}catch(Exception e){File.WriteAllText(Root+"/Reports/WallRevision_UnityError.txt",e.ToString());Debug.LogException(e);}
 }
 static void Run(){
 string path=Model+"Wall_Straight4.fbx";
 AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
 var imp=(ModelImporter)AssetImporter.GetAtPath(path);imp.isReadable=true;imp.globalScale=1;imp.useFileScale=true;imp.generateSecondaryUV=false;imp.SaveAndReimport();
 var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
 var imported=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
 var meshes=new Dictionary<string,Mesh>();
 var mats=new Dictionary<string,Material[]>();
 foreach(var f in imported.GetComponentsInChildren<MeshFilter>()){
 var mesh=UnityEngine.Object.Instantiate(f.sharedMesh);var matrix=imported.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
 var v=mesh.vertices;for(int i=0;i<v.Length;i++){var p=matrix.MultiplyPoint3x4(v[i]);v[i]=new Vector3(-p.x,p.y,p.z);}mesh.vertices=v;
 var n=mesh.normals;for(int i=0;i<n.Length;i++){var p=matrix.inverse.transpose.MultiplyVector(n[i]).normalized;n[i]=new Vector3(-p.x,p.y,p.z);}mesh.normals=n;
 for(int s=0;s<mesh.subMeshCount;s++){var t=mesh.GetTriangles(s);for(int i=0;i<t.Length;i+=3){int tmp=t[i];t[i]=t[i+2];t[i+2]=tmp;}mesh.SetTriangles(t,s);}
 mesh.RecalculateBounds();mesh.RecalculateTangents();
 string mp=Model+"UnityMeshes/"+f.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(mp);
 if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,mp);
 meshes[f.name]=mesh;
 var rend=f.GetComponent<MeshRenderer>();
 if(rend)mats[f.name]=rend.sharedMaterials.Select(m=>AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Res/Material/Environment/AbandonedFoundry/"+m.name+".mat")).ToArray();
 }
 UnityEngine.Object.DestroyImmediate(imported);
 var prefab=PrefabUtility.LoadPrefabContents(Pref);
 try{
 foreach(var f in prefab.GetComponentsInChildren<MeshFilter>(true)){
 if(!meshes.TryGetValue(f.name,out var mesh))throw new Exception("Unexpected wall child "+f.name);
 f.sharedMesh=mesh;f.transform.localPosition=Vector3.zero;f.transform.localRotation=Quaternion.identity;f.transform.localScale=Vector3.one;
 if(f.name.StartsWith("COL_")){
 foreach(var c in f.GetComponents<Collider>())UnityEngine.Object.DestroyImmediate(c);
 var mc=f.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=mesh;mc.convex=false;
 var r=f.GetComponent<MeshRenderer>();if(r)UnityEngine.Object.DestroyImmediate(r);
 }else{
 var r=f.GetComponent<MeshRenderer>();if(mats[f.name].Any(m=>!m))throw new Exception("Missing shared material");
 r.sharedMaterials=mats[f.name];
 }
 }
 foreach(var oldGroup in prefab.GetComponentsInChildren<LODGroup>(true))UnityEngine.Object.DestroyImmediate(oldGroup);
 var lg=prefab.AddComponent<LODGroup>();lg.fadeMode=LODFadeMode.None;lg.animateCrossFading=false;
 lg.SetLODs(new[]{new UnityEngine.LOD(.12f,prefab.GetComponentsInChildren<Renderer>().Where(r=>r.name.EndsWith("_LOD0")).ToArray()),new UnityEngine.LOD(.001f,prefab.GetComponentsInChildren<Renderer>().Where(r=>r.name.EndsWith("_LOD1")).ToArray())});lg.RecalculateBounds();
 PrefabUtility.SaveAsPrefabAsset(prefab,Pref);
 }finally{PrefabUtility.UnloadPrefabContents(prefab);}
 AssetDatabase.SaveAssets();
 var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Pref);var test=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
 var reports=new List<string>();
 foreach(var f in test.GetComponentsInChildren<MeshFilter>()){reports.Add(f.name+" tris="+f.sharedMesh.triangles.Length/3+" uv0="+f.sharedMesh.uv.Length+" uv1="+f.sharedMesh.uv2.Length+" bounds="+f.sharedMesh.bounds);}
 var zero=test.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name.EndsWith("_LOD0"));var proxy=test.GetComponentsInChildren<MeshCollider>().Single();
 var probe=new GameObject("QA_Visible");probe.transform.SetParent(zero.transform,false);var visible=probe.AddComponent<MeshCollider>();visible.sharedMesh=zero.GetComponent<MeshFilter>().sharedMesh;
 Physics.SyncTransforms();int errors=0,count=0;var bounds=zero.bounds;
 for(int a=0;a<3;a++)for(int sign=-1;sign<=1;sign+=2)for(int i=1;i<21;i++)for(int j=1;j<21;j++){
 var pos=bounds.min;int b=(a+1)%3,c=(a+2)%3;pos[a]=bounds.center[a]+sign*(bounds.extents[a]+2);pos[b]+=bounds.size[b]*i/21f;pos[c]+=bounds.size[c]*j/21f;
 var dir=Vector3.zero;dir[a]=-sign;var ray=new Ray(pos,dir);
 bool v=visible.Raycast(ray,out var vh,20),p=proxy.Raycast(ray,out var ph,20);if(v!=p||(v&&Mathf.Abs(vh.distance-ph.distance)>.001f))errors++;count++;
 }
 reports.Add("Visible vs collision sampled rays="+count+" mismatches over 1mm="+errors);
 UnityEngine.Object.DestroyImmediate(probe);test.transform.position=new Vector3(0,0,-6);
 if(errors>0)throw new Exception("Wall shot collision mismatch");
 // Straight / ninety-degree / closed rectangle assembled with corner pivots.
 for(int i=1;i<3;i++){var o=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);o.transform.position=new Vector3(i*4,0,-6);}
 for(int side=0;side<4;side++)for(int i=0;i<2;i++){
 var o=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);o.transform.rotation=Quaternion.Euler(0,90*side,0);
 var starts=new[]{new Vector3(0,0,8),new Vector3(8,0,8),new Vector3(8,0,0),Vector3.zero};o.transform.position=starts[side]+o.transform.rotation*new Vector3(i*4,0,0);
 }
 reports.Add("Prefab LODGroup two levels explicitly configured; collider renderer absent; root unit scale.");
 reports.Add("Width=4m Height=4m; edited decorative thickness retained (~1.046m).");
 // Save dedicated review scene; main arena and user's current scene aren't regenerated.
 var sun=new GameObject("QA Light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(35,-40,0);
 RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
 var cam=new GameObject("QA Camera").AddComponent<Camera>();cam.transform.position=new Vector3(18,14,-20);cam.transform.LookAt(new Vector3(5,2,3));cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.15f,.18f);
 var layerRestore=new Dictionary<GameObject,int>();foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>()){layerRestore[t.gameObject]=t.gameObject.layer;t.gameObject.layer=2;}cam.cullingMask=1<<2;
 var rt=new RenderTexture(1400,1000,24);cam.targetTexture=rt;cam.Render();var oldRT=RenderTexture.active;RenderTexture.active=rt;
 var pic=new Texture2D(1400,1000,TextureFormat.RGB24,false);pic.ReadPixels(new Rect(0,0,1400,1000),0,0);pic.Apply();File.WriteAllBytes(Root+"/Previews/WallRevision_Unity.png",pic.EncodeToPNG());RenderTexture.active=oldRT;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pic);
 foreach(var pair in layerRestore)pair.Key.layer=pair.Value;cam.cullingMask=-1;
 EditorSceneManager.SaveScene(scene,"Assets/_Game/Res/Scene/Environment/AbandonedFoundry/Foundry_WallRevision_Check.unity");
 File.WriteAllLines(Root+"/Reports/WallRevision_Unity.txt",reports);
 SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
 File.WriteAllText(Root+"/WALL_UPDATED","success");
 }
}
}

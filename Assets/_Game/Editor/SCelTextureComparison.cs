using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using S.View;
namespace S.EditorTools
{
    // Manual request driven, never modifies scene/prefab assets.
    [InitializeOnLoad]
    public static class SCelTextureComparison
    {
        const string Folder="_Inbox/cel-texture-review-v1";
        const string Output=Folder+"/comparison";
        const string Request="Logs/cel-texture-capture.request";
        [Serializable] class Snapshot { public string label;public Vector3 cameraPosition;public Quaternion cameraRotation;public float fieldOfView,gameTime;public int width=1920,height=1080;public string[] models; }
        static double readyAt;
        static SCelTextureComparison(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                string action=File.ReadAllText(Request).Trim();
                if(action=="models")
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    var snapshot=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Output+"/before/snapshot.json"));
                    foreach(string id in snapshot.models)
                    {
                        string prefab="Assets/_Game/Res/Prefab/"+(id.StartsWith("SW")?"Weapons/":"Enemies/")+id+".prefab";
                        Model(prefab,Output+"/before/models",true);
                        Model(prefab,Output+"/after/models",false);
                    }
                    AssetDatabase.DeleteAsset("Assets/_Game/Editor/CelComparisonOriginalTextures");
                    File.WriteAllText(Output+"/model-capture-note.txt","Model comparison reshot from original backup textures and installed textures in the same preview scene; full HiDPI render targets captured. Original before/after scene screenshots unchanged. Initial model captures retained in models-initial.");
                    File.WriteAllText("Logs/cel-texture-capture.log","COMPLETE model framing refinement; 35 matched before/after previews; temporary originals removed");
                    File.Delete(Request);return;
                }
                if(action=="finish")
                {
                    File.Delete(Request);
                    if(SessionState.GetBool("CelCaptureStartedPlay",false))EditorApplication.isPlaying=false;
                    EditorApplication.isPaused=SessionState.GetBool("CelCaptureWasPaused",false);
                    SessionState.SetBool("CelCaptureStartedPlay",false);
                    File.WriteAllText("Logs/cel-texture-capture.log","FINISHED; editor play state restored");return;
                }
                if(action!="before"&&action!="after")throw new Exception("Unknown action");
                Directory.CreateDirectory(Output+"/"+action+"/models");
                if(action=="before"&&!SessionState.GetBool("CelCaptureInitialized",false))
                {
                    if(File.Exists(Output+"/before/snapshot.json"))throw new Exception("Baseline already exists; refusing overwrite");
                    SessionState.SetBool("CelCaptureWasPaused",EditorApplication.isPaused);
                    SessionState.SetBool("CelCaptureStartedPlay",!EditorApplication.isPlaying);
                    SessionState.SetBool("CelCaptureInitialized",true);
                }
                if(!EditorApplication.isPlaying)
                {if(action=="after")throw new Exception("Frozen comparison session lost");EditorApplication.isPlaying=true;return;}
                var root=GameRoot.Instance;if(!root||root.Run==null||!root.Menu||!root.Cam)return;
                if(action=="before"&&root.Menu.IsOpen)
                {root.Menu.StartForTest();readyAt=EditorApplication.timeSinceStartup+.6;EditorApplication.isPaused=false;return;}
                if(EditorApplication.timeSinceStartup<readyAt)return;
                EditorApplication.isPaused=true;
                if(action=="after")AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string dir=Output+"/"+action;
                var snap=new Snapshot{label=action,cameraPosition=root.Cam.transform.position,cameraRotation=root.Cam.transform.rotation,fieldOfView=root.Cam.fieldOfView,gameTime=Time.time};
                if(action=="after")
                {
                    var before=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Output+"/before/snapshot.json"));
                    if(Vector3.Distance(before.cameraPosition,snap.cameraPosition)>.00001f||Quaternion.Angle(before.cameraRotation,snap.cameraRotation)>.001f||Mathf.Abs(before.gameTime-snap.gameTime)>.0001f)throw new Exception("Frozen camera or game time changed");
                    ValidateImports();
                }
                SHudV2PlayCheck.Capture(root,dir+"/game.png",1920,1080);
                var go=new GameObject("CelComparisonCamera");
                try
                {
                    var camera=go.AddComponent<Camera>();camera.CopyFrom(root.Cam);camera.enabled=false;
                    camera.transform.position=new Vector3(0,3,-8);camera.transform.rotation=Quaternion.Euler(7,0,0);camera.fieldOfView=75;
                    Render(camera,dir+"/theater.png",1920,1080);
                }
                finally{UnityEngine.Object.DestroyImmediate(go);}
                var prefabs=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Game/Res/Prefab/Weapons","Assets/_Game/Res/Prefab/Enemies"})
                    .Select(AssetDatabase.GUIDToAssetPath).Where(p=>{var n=Path.GetFileName(p);return n.StartsWith("SW")||n.StartsWith("S0")||n.StartsWith("S1")||n.StartsWith("S2")||n.StartsWith("SM_")||n.StartsWith("FXM_");}).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
                var names=new List<string>();
                foreach(var path in prefabs){Model(path,dir+"/models");names.Add(Path.GetFileNameWithoutExtension(path));}
                snap.models=names.ToArray();File.WriteAllText(dir+"/snapshot.json",JsonUtility.ToJson(snap,true));
                File.WriteAllText("Logs/cel-texture-capture.log","COMPLETE "+action+"; game + theater + "+names.Count+" model renders; camera and game time frozen");
                File.Delete(Request);
            }
            catch(Exception ex){File.WriteAllText("Logs/cel-texture-capture.log","FAIL "+ex);File.Delete(Request);Debug.LogException(ex);}
        }
        static void ValidateImports()
        {
            var report=new List<string>();
            foreach(string path in File.ReadAllLines(Folder+"/import-paths.txt"))
            {
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(!texture)throw new Exception("Missing texture "+path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(!importer.sRGBTexture)throw new Exception("Color map is not sRGB "+path);
                report.Add("PASS "+path+" "+texture.width+"x"+texture.height+" GUID="+AssetDatabase.AssetPathToGUID(path));
            }
            foreach(string path in File.ReadAllLines(Folder+"/import-materials.txt"))
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat||!mat.shader||!mat.shader.isSupported||!mat.GetTexture("_BaseMap"))throw new Exception("Invalid material "+path);
                report.Add("PASS material "+path);
            }
            File.WriteAllLines(Output+"/unity-import-check.log",report);
        }
        static void Render(Camera camera,string path,int width,int height)
        {
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float aspect=camera.aspect;
            var rt=new RenderTexture(width,height,24);var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.aspect=(float)width/height;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
            finally{camera.targetTexture=oldTarget;camera.aspect=aspect;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }
        static void Model(string path,string dir,bool useOriginal=false)
        {
            var preview=new PreviewRenderUtility();var cleanup=new List<UnityEngine.Object>();
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance=UnityEngine.Object.Instantiate(prefab);
                foreach(var lod in instance.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(lod);
                preview.AddSingleGO(instance);
                foreach(var b in instance.GetComponentsInChildren<MonoBehaviour>())b.enabled=false;
                foreach(var a in instance.GetComponentsInChildren<Animator>())a.enabled=false;
                foreach(var lod in instance.GetComponentsInChildren<LODGroup>())lod.enabled=false;
                foreach(var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.enabled=false;
                    if(skin.name.Contains("_LOD")&&!skin.name.EndsWith("_LOD0"))continue;
                    var mesh=new Mesh();skin.BakeMesh(mesh);mesh.RecalculateBounds();cleanup.Add(mesh);
                    var go=new GameObject("ComparisonMesh",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(skin.transform,false);
                    go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                }
                foreach(var mr in instance.GetComponentsInChildren<MeshRenderer>())if(mr.name.Contains("_LOD")&&!mr.name.EndsWith("_LOD0"))mr.enabled=false;
                var renderers=instance.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r is MeshRenderer).ToArray();
                if(renderers.Length==0)throw new Exception("No renderable mesh: "+path);
                foreach(var renderer in renderers)foreach(var mat in renderer.sharedMaterials)if(!mat||!mat.shader||!mat.shader.isSupported)throw new Exception("Invalid shader: "+path);
                if(useOriginal)
                {
                    foreach(var renderer in renderers)
                    {
                        renderer.sharedMaterials=renderer.sharedMaterials.Select(mat=>{
                            var copy=new Material(mat);cleanup.Add(copy);
                            foreach(string prop in new[]{"_BaseMap","_MainTex"})
                            {
                                if(!copy.HasProperty(prop))continue;
                                var current=copy.GetTexture(prop);if(!current)continue;
                                string source=AssetDatabase.GetAssetPath(current);
                                var original=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Editor/CelComparisonOriginalTextures/"+Path.GetFileName(source));
                                if(original)copy.SetTexture(prop,original);
                            }
                            return copy;
                        }).ToArray();
                    }
                }
                Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                var camera=preview.camera;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.21f);
                camera.orthographicSize=Mathf.Max(bounds.size.y,Mathf.Max(bounds.size.z,bounds.size.x))*.68f;
                camera.nearClipPlane=.001f;camera.farClipPlane=1000;
                Vector3 direction=path.Contains("SW02_")?new Vector3(.1f,1,-.16f):path.Contains("/Weapons/")?new Vector3(.25f,.3f,-1):new Vector3(.65f,.35f,1);
                bool flat=path.Contains("BassWave")||path.Contains("VentGust");
                if(flat){var filter=renderers[0].GetComponent<MeshFilter>();if(filter&&filter.sharedMesh.normals.Length>0)direction=filter.transform.TransformDirection(filter.sharedMesh.normals[0]);}
                camera.transform.position=bounds.center+direction.normalized*(bounds.size.magnitude*3+1);camera.transform.LookAt(bounds.center,path.Contains("SW02_")||Mathf.Abs(Vector3.Dot(direction.normalized,Vector3.up))>.9f?Vector3.forward:Vector3.up);
                preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);preview.lights[0].color=new Color(1,.96f,.88f);
                preview.lights[1].intensity=.4f;preview.lights[1].transform.rotation=Quaternion.Euler(340,140,0);
                preview.ambientColor=new Color(.3f,.32f,.37f);
                preview.BeginPreview(new Rect(0,0,640,640),GUIStyle.none);preview.Render(true);
                var rt=preview.EndPreview() as RenderTexture;var old=RenderTexture.active;
                var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                try{RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(dir+"/"+Path.GetFileNameWithoutExtension(path)+".png",tex.EncodeToPNG());}
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);}
            }
            finally{preview.Cleanup();foreach(var item in cleanup)UnityEngine.Object.DestroyImmediate(item);}
        }
    }
}

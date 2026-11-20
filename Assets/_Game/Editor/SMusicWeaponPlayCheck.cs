using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicWeaponPlayCheck
    {
        const string Request="Logs/music-weapon-play.request";
        static readonly List<string> log=new();
        static int phase,index,pass,fail;
        static double start,at;
        static float reloadLeft;
        static Vector3[] frozen;
        static GameRoot previous;
        static SMusicWeaponImporter.Asset[] assets;
        static SMusicWeaponPlayCheck(){EditorApplication.update+=Tick;}
        public static void Batch()
        {
            File.WriteAllText(Request,"batch");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorApplication.isPlaying=true;
        }
        [MenuItem("S/Weapons/Run Music Weapon Play Check")]
        static void Menu(){File.WriteAllText(Request,"interactive");}
        static void Check(string text,bool ok)
        {
            log.Add((ok?"PASS ":"FAIL ")+text);if(ok)pass++;else fail++;
            File.WriteAllLines("Logs/music-weapon-play.log",log);
        }
        static void Next(int p){phase=p;at=EditorApplication.timeSinceStartup;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(start==0)start=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-start>240)throw new TimeoutException("Weapon phase "+phase);
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                var root=GameRoot.Instance;
                if(!root||root.Run==null||root.Menu==null)return;
                var weapons=root.Weapons;var visual=weapons.MusicVisual;
                double elapsed=EditorApplication.timeSinceStartup-at;
                if(phase==0)
                {
                    assets=SMusicWeaponImporter.ReadPlan().assets;
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    root.InvulnerableForTest=true;root.enabled=false;root.Player.InputLocked=true;root.Player.enabled=false;
                    var cc=root.Player.GetComponent<CharacterController>();cc.enabled=false;
                    root.Player.transform.position=new Vector3(0,.05f,-21);root.Player.transform.rotation=Quaternion.identity;
                    root.Cam.transform.localRotation=Quaternion.identity;root.Cam.fieldOfView=78;
                    Check("six prefabs available via Addressables",assets.All(a=>SAssets.Model(a.assetId))&&!SRes.HasFailed);
                    Next(1);
                }
                else if(phase==1)
                {
                    weapons.StartWith(assets[index].gunId);Next(2);
                }
                else if(phase==2&&elapsed>.30)
                {
                    string id=assets[index].assetId;
                    Check(id+" real selection uses music model",visual&&visual.AssetId==id);
                    Check(id+" no legacy rig/fallback",!weapons.GunModelRoot.Find("WeaponRig")&&!weapons.GunModelRoot.GetComponentsInChildren<Transform>().Any(t=>t.name.Contains("Fallback")));
                    Check(id+" no collision; metric scale",visual.GetComponentsInChildren<Collider>().Length==0&&visual.transform.localScale==Vector3.one);
                    foreach(var state in visual.States)
                    {
                        visual.PreviewPose(state,.5f);
                        Check(id+"/"+state+" graph evaluates",visual.CurrentState==state&&Finite(visual));
                        float error=GraphError(assets[index],visual,state,.5f);
                        Check(id+"/"+state+" live graph geometry matches source ("+error.ToString("F7")+"m)",error<.0001f);
                    }
                    visual.PreviewPose("Idle",0);
                    Capture(root.Cam,"Shots/weapons/"+id+"_idle.png",1600,900);
                    CheckViewport(root.Cam,visual,id+" idle");
                    var cast=weapons.Active.TryFire(out _);
                    Check(id+" actual fire consumes ammo",cast!=null&&weapons.Active.Ammo<weapons.Active.Stats.Mag);
                    weapons.SpawnCastForAI(cast);Next(3);
                }
                else if(phase==3&&elapsed>.07)
                {
                    Check(assets[index].assetId+" actual shot drives animation",visual.PresentedShots>0);
                    Capture(root.Cam,"Shots/weapons/"+assets[index].assetId+"_fire.png",1600,900);
                    weapons.Active.StartReload();Next(4);
                }
                else if(phase==4&&elapsed>weapons.Active.Stats.Reload*.45f)
                {
                    Check(assets[index].assetId+" reload synced to GunRuntime",visual.CurrentState=="Reload"&&Mathf.Abs(visual.StateTime/visual.Clips[Array.IndexOf(visual.States,"Reload")].length-weapons.Active.MagBarFill)<.05f);
                    Capture(root.Cam,"Shots/weapons/"+assets[index].assetId+"_reload.png",1600,900);
                    root.SetPaused(true);reloadLeft=weapons.Active.ReloadLeft;
                    frozen=visual.GetComponentsInChildren<Transform>().Select(t=>t.localPosition).ToArray();Next(5);
                }
                else if(phase==5&&elapsed>.20)
                {
                    Check(assets[index].assetId+" pause freezes reload and bones",Mathf.Abs(weapons.Active.ReloadLeft-reloadLeft)<.000001f&&visual.GetComponentsInChildren<Transform>().Select((t,i)=>Vector3.Distance(t.localPosition,frozen[i])<.000001f).All(b=>b));
                    root.SetPaused(false);root.Player.InputLocked=true;Next(6);
                }
                else if(phase==6&&!weapons.Active.Reloading&&elapsed>.15)
                {
                    Check(assets[index].assetId+" reload refills",weapons.Active.Ammo==weapons.Active.Stats.Mag);
                    root.Cam.fieldOfView=weapons.Active.Base.ZoomDistance;Next(7);
                }
                else if(phase==7&&elapsed>.15)
                {
                    CheckViewport(root.Cam,visual,assets[index].assetId+" zoom");
                    Capture(root.Cam,"Shots/weapons/"+assets[index].assetId+"_zoom.png",1600,900);
                    Capture(root.Cam,"Shots/weapons/"+assets[index].assetId+"_wide.png",2100,900);
                    root.Cam.fieldOfView=78;
                    index++;if(index<assets.Length)Next(1);else
                    {
                        Check("second weapon acquisition",weapons.AcquireGun("rifle",root.Run,out _));
                        Next(8);
                    }
                }
                else if(phase==8&&elapsed>.4)
                {
                    weapons.SwitchTo(0);Next(9);
                }
                else if(phase==9&&elapsed>.4)
                {
                    Check("switch replaces model and avoids stacked visuals",weapons.MusicVisual.AssetId==assets[5].assetId&&weapons.GunModelRoot.GetComponentsInChildren<MusicWeaponVisual>().Length==1);
                    previous=root;Time.timeScale=1;UnityEngine.SceneManagement.SceneManager.LoadScene("Main");Next(10);
                }
                else if(phase==10&&root!=previous)
                {
                    Check("restart selects one music weapon",root.Weapons.MusicVisual&&root.Weapons.GunCount==1&&root.Menu.IsOpen);
                    Finish();
                }
            }
            catch(Exception e){Check(e.ToString(),false);Finish();}
        }
        static float GraphError(SMusicWeaponImporter.Asset asset,MusicWeaponVisual visual,string state,float normalized)
        {
            string dir=SMusicWeaponImporter.Source+"/"+asset.assetId+"/";
            var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(dir+"Exports/"+asset.assetId+"_Model.fbx"));
            try
            {
                var spec=asset.animations.Single(c=>c.state==state);
                var clip=AssetDatabase.LoadAllAssetsAtPath(dir+spec.file).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                clip.SampleAnimation(source,spec.duration*normalized);
                var a=source.GetComponentsInChildren<SkinnedMeshRenderer>().OrderBy(r=>r.name).ToArray();
                var b=visual.GetComponentsInChildren<SkinnedMeshRenderer>().OrderBy(r=>r.name).ToArray();float error=0;
                for(int i=0;i<a.Length;i++)
                {
                    var ma=new Mesh();var mb=new Mesh();a[i].BakeMesh(ma);b[i].BakeMesh(mb);
                    var va=ma.vertices;var vb=mb.vertices;
                    var ta=source.transform.worldToLocalMatrix*a[i].transform.localToWorldMatrix;
                    var tb=visual.Animator.transform.worldToLocalMatrix*b[i].transform.localToWorldMatrix;
                    for(int j=0;j<va.Length;j++)error=Mathf.Max(error,Vector3.Distance(ta.MultiplyPoint3x4(va[j]),tb.MultiplyPoint3x4(vb[j])));
                    UnityEngine.Object.DestroyImmediate(ma);UnityEngine.Object.DestroyImmediate(mb);
                }
                return error;
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }
        static bool Finite(MusicWeaponVisual v)
        {
            foreach(var r in v.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=new Mesh();r.BakeMesh(mesh);
                bool ok=mesh.vertices.All(p=>!float.IsNaN(p.sqrMagnitude)&&!float.IsInfinity(p.sqrMagnitude)&&p.magnitude<10);
                UnityEngine.Object.DestroyImmediate(mesh);if(!ok)return false;
            }return true;
        }
        static void CheckViewport(Camera camera,MusicWeaponVisual visual,string tag)
        {
            int visible=0,count=0,near=0,center=0;
            var renderer=visual.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name.EndsWith("_LOD0"));
            var mesh=new Mesh();renderer.BakeMesh(mesh);
            Vector2 min=new Vector2(99,99),max=new Vector2(-99,-99);
            foreach(var v in mesh.vertices)
            {
                var p=camera.WorldToViewportPoint(renderer.transform.TransformPoint(v));count++;
                if(p.z<camera.nearClipPlane)near++;
                if(p.z>0&&p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1)visible++;
                if(p.x>.48f&&p.x<.52f&&p.y>.48f&&p.y<.52f)center++;
                min=Vector2.Min(min,p);max=Vector2.Max(max,p);
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            Check(tag+" visible (vertices "+visible+"/"+count+", rect "+min+" "+max+")",visible>count*.50f);
            Check(tag+" stays beyond near plane",near==0);
            // Vertex samples are diagnostic only; a rendered silhouette is reviewed separately.
            log.Add("INFO "+tag+" center vertices "+center);
        }
        public static void Capture(Camera camera,string path,int width,int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var rt=new RenderTexture(width,height,24);var old=RenderTexture.active;var target=camera.targetTexture;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);float aspect=camera.aspect;
            try
            {
                camera.aspect=(float)width/height;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {camera.targetTexture=target;camera.aspect=aspect;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);}
        }
        static void Finish()
        {
            bool batch=File.ReadAllText(Request)=="batch";File.Delete(Request);
            log.Add("RESULT "+pass+"/"+(pass+fail)+" passed");File.WriteAllLines("Logs/music-weapon-play.log",log);
            Time.timeScale=1;EditorApplication.isPlaying=false;
            if(batch)EditorApplication.delayCall+=()=>EditorApplication.Exit(fail>0?1:0);
        }
    }
}

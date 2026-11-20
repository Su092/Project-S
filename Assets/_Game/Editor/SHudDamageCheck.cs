using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SHudDamageCheck
    {
        const string Request="Logs/hud-damage-check.request";
        static readonly List<string> Log=new();
        static int phase,pass,fail;
        static double at,started;
        static float beforeHp;
        static GameRoot previous;
        static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static SHudDamageCheck(){EditorApplication.update+=Tick;}
        static void Check(string text,bool ok)
        {
            Log.Add((ok?"PASS ":"FAIL ")+text);if(ok)pass++;else fail++;
            File.WriteAllLines("Logs/hud-damage-check.log",Log);
        }
        public static void Batch()
        {
            // Targeted prefab edit; do not regenerate the user's authored HUD layout.
            string path=UiPrefab.AssetDir+"HUD.prefab";
            var prefab=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var image=prefab.GetComponent<HudRefs>().DamageVignette;
                if(!image||!image.sprite)throw new InvalidOperationException("Damage vignette binding/sprite missing");
                image.color=new Color(1,1,1,0);image.raycastTarget=false;
                PrefabUtility.SaveAsPrefabAsset(prefab,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            AssetDatabase.SaveAssets();
            File.WriteAllText(Request,"batch");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorApplication.isPlaying=true;
        }
        static void Next(int p){phase=p;at=EditorApplication.timeSinceStartup;}
        static Image Vignette(GameRoot root)=>root.GetComponentsInChildren<HudRefs>(true).Single().DamageVignette;
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>120)throw new TimeoutException("Damage check phase "+phase);
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                var root=GameRoot.Instance;if(!root||root.Menu==null||root.Run==null)return;
                double elapsed=EditorApplication.timeSinceStartup-at;
                var image=Vignette(root);
                if(phase==0)
                {
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    root.enabled=false;root.Player.InputLocked=true;root.Player.enabled=false;root.InvulnerableForTest=false;
                    Check("initially transparent, RGB retained",image.color==new Color(1,1,1,0));
                    Check("bound red edge sprite",image.sprite&&image.sprite.name.Contains("vignette_damage"));
                    Check("full screen stretch, no mouse blocking",image.rectTransform.anchorMin==Vector2.zero&&image.rectTransform.anchorMax==Vector2.one&&image.rectTransform.sizeDelta==Vector2.zero&&!image.raycastTarget);
                    Next(1);
                }
                else if(phase==1&&elapsed>.3)
                {
                    beforeHp=root.Hp;
                    RenderComparison(root,1280,720,"16x9");
                    Check("real damage removes health",Mathf.Abs(root.Hp-(beforeHp-5))<.001f);
                    Check("flash visible on damage call",image.color.r==1&&Mathf.Abs(image.color.a-.6f)<.001f);
                    float hp=root.Hp;root.DamagePlayer(5);
                    Check("invulnerability frame prevents second damage",root.Hp==hp);
                    root.SetPaused(true);Next(2);
                }
                else if(phase==2&&elapsed>.65)
                {
                    Check("unscaled fade clears while paused",image.color.a==0);
                    root.SetPaused(false);root.Player.InputLocked=true;
                    typeof(GameRoot).GetField("_iFrameLeft",Private).SetValue(root,0f);
                    RenderComparison(root,1680,720,"21x9");
                    Next(3);
                }
                else if(phase==3&&elapsed>.15)
                {
                    typeof(GameRoot).GetField("_iFrameLeft",Private).SetValue(root,0f);
                    root.DamagePlayer(2);
                    Check("new valid hit restores peak",Mathf.Abs(image.color.a-.6f)<.001f);
                    Check("one overlay after repeated hits",root.GetComponentsInChildren<Image>(true).Count(i=>i.name=="DamageVignette")==1);
                    Next(4);
                }
                else if(phase==4&&elapsed>.65)
                {
                    Check("flash returns completely transparent",image.color.a==0);
                    root.InvulnerableForTest=true;float hp=root.Hp;root.DamagePlayer(5);
                    Check("invulnerable player does not flash",root.Hp==hp&&image.color.a==0);
                    root.InvulnerableForTest=false;root.DamagePlayer(0);
                    Check("zero damage does not flash",image.color.a==0);
                    root.DamagePlayer(2);
                    previous=root;Time.timeScale=1;UnityEngine.SceneManagement.SceneManager.LoadScene("Main");Next(5);
                }
                else if(phase==5&&root!=previous)
                {
                    Check("restart clears old edge flash",image.color.a==0);
                    Check("restart returns to menu",root.Menu.IsOpen);
                    Finish();
                }
            }
            catch(Exception e){Check(e.ToString(),false);Finish();}
        }
        static Texture2D Capture(GameRoot root,int width,int height)
        {
            var camera=root.Cam;var canvas=root.GetComponentsInChildren<HudRefs>(true).Single().GetComponentInParent<Canvas>();
            var mode=canvas.renderMode;var canvasCam=canvas.worldCamera;float plane=canvas.planeDistance,aspect=camera.aspect;
            var target=camera.targetTexture;var active=RenderTexture.active;
            var rt=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.aspect=(float)width/height;
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.1f;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                return texture;
            }
            finally
            {
                canvas.renderMode=mode;canvas.worldCamera=canvasCam;canvas.planeDistance=plane;
                camera.targetTexture=target;camera.aspect=aspect;RenderTexture.active=active;
                UnityEngine.Object.DestroyImmediate(rt);Canvas.ForceUpdateCanvases();
            }
        }
        static void RenderComparison(GameRoot root,int width,int height,string suffix)
        {
            var image=Vignette(root);image.color=new Color(1,1,1,0);
            // Warm up URP/GPU-resident visibility for the new offscreen target before comparing pixels.
            for(int warmup=0;warmup<2;warmup++)UnityEngine.Object.DestroyImmediate(Capture(root,width,height));
            var before=Capture(root,width,height);
            root.DamagePlayer(5);
            var after=Capture(root,width,height);
            try
            {
                Directory.CreateDirectory("Shots/hud-damage");
                File.WriteAllBytes("Shots/hud-damage/before-"+suffix+".png",before.EncodeToPNG());
                File.WriteAllBytes("Shots/hud-damage/hit-"+suffix+".png",after.EncodeToPNG());
                float red=0,center=0;
                foreach(float y in new[]{.35f,.5f,.65f})
                    foreach(float x in new[]{.01f,.99f})
                    {
                        Color a=after.GetPixelBilinear(x,y),b=before.GetPixelBilinear(x,y);
                        red+=(a.r-a.g)-(b.r-b.g);
                    }
                foreach(var p in new[]{new Vector2(.4f,.5f),new Vector2(.6f,.5f),new Vector2(.5f,.4f),new Vector2(.5f,.6f)})
                {
                    var a=after.GetPixelBilinear(p.x,p.y);var b=before.GetPixelBilinear(p.x,p.y);
                    center+=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);
                }
                Check(suffix+" rendered edges gain red ("+(red/6).ToString("F3")+")",red/6>.06f);
                Check(suffix+" central gameplay stays clear ("+(center/4).ToString("F3")+")",center/4<.02f);
            }
            finally{UnityEngine.Object.DestroyImmediate(before);UnityEngine.Object.DestroyImmediate(after);}
        }
        static void Finish()
        {
            File.Delete(Request);Log.Add("RESULT "+pass+"/"+(pass+fail)+" passed");
            File.WriteAllLines("Logs/hud-damage-check.log",Log);
            Time.timeScale=1;EditorApplication.isPlaying=false;
            EditorApplication.delayCall+=()=>EditorApplication.Exit(fail>0?1:0);
        }
    }
}

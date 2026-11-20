using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using S.View;
using S.Core;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicUpgradeCheck
    {
        const string Request="Logs/music-upgrade-check.request";
        static readonly List<string> Log=new();
        static int phase,pass,fail,capacity;
        static double start,at,readySince;
        static bool openingSeen,closingSeen,flightSeen;
        static GameRoot old;
        static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static SMusicUpgradeCheck(){EditorApplication.update+=Tick;}
        public static void ImportAndBatch(){SMusicUpgradeImport.Build();Batch();}
        public static void Batch(){
            File.WriteAllText(Request,"batch");EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");EditorApplication.isPlaying=true;
        }
        static List<Offer> Offers()=>new(){new Offer{Id="std",IsBullet=true},new Offer{Id="dmg"},new Offer{Id="smg",IsGun=true}};
        static void Check(string text,bool ok){Log.Add((ok?"PASS ":"FAIL ")+text);if(ok)pass++;else fail++;File.WriteAllLines("Logs/music-upgrade-check.log",Log);}
        static void Next(int p){phase=p;at=EditorApplication.timeSinceStartup;}
        static OfferCardRefs[] Cards(InventoryView inv)=>inv.OfferPresentation.GetComponentsInChildren<OfferCardRefs>();
        static void Click(InventoryView inv,int i)=>Cards(inv)[i].Button.onClick.Invoke();
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try {
                if(start==0)start=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-start>240)throw new TimeoutException("Phase "+phase);
                if(!EditorApplication.isPlaying)return;
                var root=GameRoot.Instance;if(!root||root.Menu==null||root.Run==null)return;
                var inv=root.GetComponent<InventoryView>();var p=inv.OfferPresentation;
                double dt=EditorApplication.timeSinceStartup-at;
                if(phase==0){
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    root.enabled=false;root.Player.InputLocked=true;root.Player.enabled=false;
                    root.Run.Bag.Clear();inv.ShowOffers(Offers());
                    Check("entrance pauses world",root.Paused&&Time.timeScale==0);
                    Check("three offers / no duplicated travel icons",inv.OfferCardCount==3&&p.CleanIconCount==3);
                    Click(inv,0);Check("entrance rejects early choice",root.Run.Bag.Count==0);
                    inv.Close();Check("entrance cannot be interrupted by Tab",inv.IsOfferVisible&&!inv.IsBagVisible);
                    Next(1);
                }
                else if(phase==1){
                    if(p.Phase==MusicOfferPresentation.Stage.Entering&&p.Progress>.42f&&!File.Exists("Shots/upgrade-ui/01-entering.png"))Capture(root,"01-entering");
                    if(p.Phase==MusicOfferPresentation.Stage.Expanding&&p.Progress>.25f&&p.Progress<.8f&&!openingSeen){
                        openingSeen=true;Check("frame expands with partial alpha",p.FrameAlpha>0&&p.FrameAlpha<1);
                        Capture(root,"02-opening");
                    }
                    if(p.CanChoose){
                        if(readySince==0){readySince=EditorApplication.timeSinceStartup;return;}
                        if(EditorApplication.timeSinceStartup-readySince<.2)return;
                        Check("unscaled opening reaches choice while world remains paused",root.Paused&&Time.timeScale==0);
                        Check("opening was observed between hidden and visible",openingSeen);
                        Check("default blue / derived red / weapon green",Cards(inv).Select(c=>c.Background.sprite.name).SequenceEqual(new[]{"offer_frame_blue","offer_frame_red","offer_frame_green"}));
                        foreach(var c in Cards(inv)){
                            Check(c.name+" has icon with no disk/ring/badge",c.IconNode.Icon.sprite&&!c.IconNode.Ring.enabled&&!c.IconNode.ScopeMark.enabled&&!c.IconNode.Badge.gameObject.activeSelf);
                            Check(c.name+" mouse raycast reaches card",RaycastCard(c));
                            Check(c.name+" content visible and clickable",c.GetComponent<CanvasGroup>().alpha==1&&c.GetComponent<CanvasGroup>().interactable&&c.Desc.text.Length>0);
                        }
                        Check("all live data ids have clean art",root.Data.Bullets.Keys.Concat(root.Data.Modifiers.Keys).All(id=>UiAssets.Named("offer_item_"+id)!=null));
                        Check("ribbon renderers bound",p.GetComponentsInChildren<MusicStaffGraphic>().All(g=>g.Sprite&&g.GetComponent<CanvasRenderer>()&&g.canvasRenderer.absoluteDepth>=0));
                        CheckFlight(p);
                        Check("six separate note assets",Enumerable.Range(0,6).All(i=>UiAssets.Named("offer_note_"+i)!=null));
                        var core=p.GetComponentsInChildren<MusicStaffGraphic>().First(g=>g.name=="FiveLineCore");
                        var notes=p.GetComponentsInChildren<Image>().Where(n=>n.name.StartsWith("FloatingNote_")).ToArray();
                        Check("12 enlarged notes render in front of staff without blocking clicks",notes.Length==12&&notes.All(n=>n.rectTransform.sizeDelta.x>=25.5f&&n.canvasRenderer.absoluteDepth>core.canvasRenderer.absoluteDepth&&!n.raycastTarget));
                        Check("six dedicated note glow sprites loaded",Enumerable.Range(0,6).All(i=>UiAssets.Named("offer_note_glow_"+i)!=null));
                        Capture(root,"03-choice");Capture(root,"03-choice-wide",1680,720);
                        if(System.Environment.GetCommandLineArgs().Contains("-music-upgrade-preview")){RecordPreview(root,p);Next(8);return;}
                        Click(inv,1);Click(inv,1);
                        Check("choice commits exactly once despite double click",root.Run.Bag.Count==1&&root.Run.Bag[0].Id=="dmg");
                        Check("choosing keeps UI and battle paused",inv.Open&&root.Paused);
                        inv.Close();Check("exit transaction blocks Tab",inv.IsOfferVisible);
                        Next(2);
                    }
                }
                else if(phase==8&&dt>.2){
                    Click(inv,1);Click(inv,1);
                    Check("choice commits exactly once despite double click",root.Run.Bag.Count==1&&root.Run.Bag[0].Id=="dmg");
                    Check("choosing keeps UI and battle paused",inv.Open&&root.Paused);
                    inv.Close();Check("exit transaction blocks Tab",inv.IsOfferVisible);
                    Next(2);
                }
                else if(phase==2){
                    if(p.Phase==MusicOfferPresentation.Stage.Closing&&p.Progress>.2f&&!closingSeen){
                        closingSeen=true;Check("closing reverses frame alpha",p.FrameAlpha>0&&p.FrameAlpha<1);Capture(root,"04-closing");
                    }
                    if(p.Phase==MusicOfferPresentation.Stage.Leaving&&p.Progress>.4f&&!flightSeen){
                        flightSeen=true;Check("landing resolves HUD upper left",p.LandingPosition().x<0&&p.LandingPosition().y>0);Capture(root,"05-flight");
                    }
                    if(!inv.Open){
                        Check("both exit stages completed",closingSeen&&flightSeen);
                        Check("battle resumes only after exit",!root.Paused&&Time.timeScale==1);
                        capacity=root.Run.BagCapacity;root.Run.BagCapacity=root.Run.Bag.Count;
                        inv.ShowOffers(Offers());Next(3);
                    }
                }
                else if(phase==3&&p.CanChoose){
                    Click(inv,0);
                    Check("full bag retains reward and ready state",p.CanChoose&&inv.IsOfferVisible&&root.Run.Bag.Count==1);
                    inv.Close();Check("full bag can use existing bag management",inv.IsBagVisible&&root.Paused);
                    inv.Close();Check("return replays retained three offers",inv.IsOfferVisible&&inv.OfferCardCount==3);
                    root.Run.BagCapacity=capacity;Next(4);
                }
                else if(phase==4&&p.CanChoose){
                    var queued=(Queue<List<Offer>>)typeof(GameRoot).GetField("_pendingOffers",Private).GetValue(root);
                    queued.Enqueue(new List<Offer>{new Offer{Id="ap",IsBullet=true},new Offer{Id="light",IsBullet=true},new Offer{Id="crit"}});
                    Click(inv,0);Next(5);
                }
                else if(phase==5&&p.CanChoose){
                    Check("queued upgrade appears without unpausing",root.Paused&&inv.IsOfferVisible&&root.Run.Bag.Count==2);
                    Check("existing higher bullet uses gold frame",Cards(inv)[0].Background.sprite.name=="offer_frame_gold");
                    Check("return and queue leave exactly one ribbon and icon layer",p.GetComponentsInChildren<MusicStaffGraphic>().Length==2&&p.CleanIconCount==3);
                    Capture(root,"06-gold");Click(inv,0);Next(6);
                }
                else if(phase==6&&!inv.Open){
                    Check("queued rewards awarded once",root.Run.Bag.Count==3);
                    Check("no active ribbons after finish",root.GetComponentsInChildren<MusicStaffGraphic>().Length==0);
                    old=root;Time.timeScale=1;UnityEngine.SceneManagement.SceneManager.LoadScene("Main");Next(7);
                }
                else if(phase==7&&root!=old){
                    Check("restart clears presentation and returns to menu",root.Menu.IsOpen&&!inv.Open&&p.Phase==MusicOfferPresentation.Stage.Hidden);
                    Finish();
                }
            }catch(Exception e){Check(e.ToString(),false);Finish();}
        }
        static void CheckFlight(MusicOfferPresentation p)
        {
            var g=p.GetComponentsInChildren<MusicStaffGraphic>().First();
            float progress=g.FlightProgress;int mode=g.FlightMode;
            g.FlightMode=1;g.FlightProgress=0;
            Check("entire ribbon starts off left",Enumerable.Range(0,21).All(i=>g.Point(i/20f).x<-g.ViewSize.x*.5f));
            bool onCurve=true,spacing=true;
            g.FlightMode=0;float restGap=g.DistanceFor(.8f)-g.DistanceFor(.2f);
            foreach(int direction in new[]{1,-1})
                for(int frame=0;frame<=40;frame++){
                    g.FlightMode=direction;g.FlightProgress=frame/40f;
                    for(int point=0;point<=20;point++){
                        var v=g.Point(point/20f);
                        onCurve &= Mathf.Abs(v.y-g.CurveY(v.x))<.05f;
                    }
                    spacing &= Mathf.Abs((g.DistanceFor(.8f)-g.DistanceFor(.2f))-restGap)<.01f;
                }
            Check("all entrance/exit samples stay on the single fixed curve",onCurve);
            Check("material arc spacing is constant without stretch or compression",spacing);
            g.FlightMode=1;
            g.FlightProgress=1;
            Check("arrival settles continuously on resting staff",Enumerable.Range(0,21).All(i=>(g.Point(i/20f)-g.RestPoint(i/20f)).magnitude<.01f));
            g.FlightMode=-1;g.FlightProgress=0;
            Check("departure starts on resting staff",Enumerable.Range(0,21).All(i=>(g.Point(i/20f)-g.RestPoint(i/20f)).magnitude<.01f));
            g.FlightProgress=1;
            Check("entire ribbon exits right",Enumerable.Range(0,21).All(i=>g.Point(i/20f).x>g.ViewSize.x*.5f));
            g.FlightMode=0;
            float maxNormalJump=0,maxCenterSlopeJump=0;
            for(int k=2;k<MusicOfferArt.Rail.Length-2;k++){
                float u=(float)k/(MusicOfferArt.Rail.Length-1),e=.00001f;
                maxNormalJump=Mathf.Max(maxNormalJump,Vector2.Angle(g.Normal(u-e),g.Normal(u+e)));
                float left=(g.SourceCenter(u)-g.SourceCenter(u-e))/e;
                float right=(g.SourceCenter(u+e)-g.SourceCenter(u))/e;
                maxCenterSlopeJump=Mathf.Max(maxCenterSlopeJump,Mathf.Abs(left-right)/g.ViewSize.x);
            }
            Check("curve normals continuous across all source samples",maxNormalJump<.1f);
            Check("source compensation has continuous slopes, no piecewise corners",maxCenterSlopeJump<.05f);
            g.FlightMode=mode;g.FlightProgress=progress;p.RefreshLayout();
        }
        static bool RaycastCard(OfferCardRefs card)
        {
            Canvas.ForceUpdateCanvases();
            var events=UnityEngine.EventSystems.EventSystem.current;
            var canvas=card.GetComponentInParent<Canvas>();
            var screen=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,card.transform.position);
            var result=new List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events){position=screen},result);
            return result.Count>0&&result[0].gameObject.GetComponentInParent<OfferCardRefs>()==card;
        }
        static void RecordPreview(GameRoot root,MusicOfferPresentation p)
        {
            Directory.CreateDirectory("Shots/upgrade-ui/frames");
            foreach(var oldFrame in Directory.GetFiles("Shots/upgrade-ui/frames","frame-*.png"))File.Delete(oldFrame);
            var type=typeof(MusicOfferPresentation);
            var phaseField=type.GetField("<Phase>k__BackingField",Private);
            var progressField=type.GetField("<Progress>k__BackingField",Private);
            var clockField=type.GetField("clock",Private);
            var selectedField=type.GetField("selected",Private);
            float savedClock=(float)clockField.GetValue(p);
            float enter=MusicOfferPresentation.EnterDuration,expanded=enter+.6f,closing=expanded+1.2f,leaving=closing+.4f;
            float exit=p.LeaveDuration;
            int count=Mathf.CeilToInt((leaving+exit+.2f)*12);
            File.WriteAllText("Shots/upgrade-ui/recording.json","{\"frames\":"+count+",\"fps\":12}");
            for(int f=0;f<count;f++){
                float t=f/12f,progress=0;var stage=MusicOfferPresentation.Stage.Ready;
                if(t<enter){stage=MusicOfferPresentation.Stage.Entering;progress=t/enter;}
                else if(t<expanded){stage=MusicOfferPresentation.Stage.Expanding;progress=(t-enter)/.6f;}
                else if(t>=closing&&t<leaving){stage=MusicOfferPresentation.Stage.Closing;progress=(t-closing)/.4f;}
                else if(t>=leaving){stage=MusicOfferPresentation.Stage.Leaving;progress=Mathf.Min(1,(t-leaving)/exit);}
                phaseField.SetValue(p,stage);progressField.SetValue(p,progress);clockField.SetValue(p,t);selectedField.SetValue(p,1);
                p.RefreshLayout();foreach(var c in p.GetComponentsInChildren<OfferCardRefs>())c.Background.CrossFadeColor(Color.white,0,true,true);
                Capture(root,"frames/frame-"+f.ToString("D3"),960,540);
            }
            phaseField.SetValue(p,MusicOfferPresentation.Stage.Ready);progressField.SetValue(p,0f);clockField.SetValue(p,savedClock);selectedField.SetValue(p,-1);
            p.RefreshLayout();
        }
        static void Capture(GameRoot root,string name,int width=1280,int height=720)
        {
            Directory.CreateDirectory("Shots/upgrade-ui");
            var camera=root.Cam;var canvases=root.GetComponentsInChildren<Canvas>(true).Where(c=>c.isRootCanvas).ToArray();
            var modes=canvases.Select(c=>c.renderMode).ToArray();var cameras=canvases.Select(c=>c.worldCamera).ToArray();var planes=canvases.Select(c=>c.planeDistance).ToArray();
            float aspect=camera.aspect;var target=camera.targetTexture;var active=RenderTexture.active;
            var rt=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{
                camera.targetTexture=rt;camera.aspect=(float)width/height;
                for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=RenderMode.ScreenSpaceCamera;canvases[i].worldCamera=camera;canvases[i].planeDistance=.11f+i*.005f;}
                Canvas.ForceUpdateCanvases();root.GetComponent<InventoryView>().OfferPresentation.RefreshLayout();
                camera.Render();camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes("Shots/upgrade-ui/"+name+".png",texture.EncodeToPNG());
            }finally{
                for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=modes[i];canvases[i].worldCamera=cameras[i];canvases[i].planeDistance=planes[i];}
                camera.targetTexture=target;camera.aspect=aspect;RenderTexture.active=active;
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);Canvas.ForceUpdateCanvases();
                root.GetComponent<InventoryView>().OfferPresentation.RefreshLayout();
            }
        }
        static void Finish(){
            File.Delete(Request);Log.Add("RESULT "+pass+"/"+(pass+fail)+" passed");File.WriteAllLines("Logs/music-upgrade-check.log",Log);
            Time.timeScale=1;EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(fail==0?0:1);
        }
    }
}

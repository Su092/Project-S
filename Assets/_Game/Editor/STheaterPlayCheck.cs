using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class STheaterPlayCheck
    {
        const string Request="Logs/theater-play.request";
        static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static readonly StringBuilder Log=new();
        static int stage,pass,fail,navCount;
        static double started,at;
        static Enemy probe;
        static GameRoot previous;
        static STheaterPlayCheck(){EditorApplication.update+=Tick;}
        static void Check(string name,bool ok){Log.AppendLine((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;File.WriteAllText("Logs/theater-play-check.log",Log.ToString());}
        static object Call(GameRoot root,string name,params object[] args)=>typeof(GameRoot).GetMethod(name,Private).Invoke(root,args);
        static void Next(){stage++;at=EditorApplication.timeSinceStartup;}
        static void Place(GameRoot root,Vector3 p)
        {
            var cc=root.Player.GetComponent<CharacterController>();cc.enabled=false;root.Player.transform.position=p;cc.enabled=true;
            root.Player.AiMove=Vector3.zero;root.Player.AiLookDelta=Vector2.zero;
        }
        static void Capture(GameRoot root,string name)
        {
            typeof(SHudV2PlayCheck).GetMethod("Capture",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,new object[]{root,"Shots/theater-play-"+name+".png",1600,900});
        }
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>120)throw new TimeoutException("Theater stage "+stage);
                if(!EditorApplication.isPlaying){EditorApplication.isPaused=false;EditorApplication.isPlaying=true;return;}
                EditorApplication.isPaused=false;
                var root=GameRoot.Instance;
                if(!root||root.Run==null||root.Menu==null||root.Zones==null) { File.WriteAllText("Logs/theater-play-state.txt", $"root={root} run={root?.Run!=null} menu={root?.Menu} zones={root?.Zones}");return; }
                if(stage==0)
                {
                    Check("one theater in real gameplay",UnityEngine.Object.FindObjectsByType<FoundryArena>().Length==1&&root.Zones.Map.IsTheater);
                    Check("theater loads through Addressables",SRes.Get<GameObject>(FoundryArena.ActiveAssetName)!=null&&!SRes.HasFailed);
                    Check("arena bounds 64x72",root.Zones.HalfWidth==32&&root.Zones.HalfDepth==36);
                    root.InvulnerableForTest=true;root.AutoAcceptOffers=true;root.Player.AiControlled=true;
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    typeof(GameRoot).GetField("_spawnTimer",Private).SetValue(root,10000f);
                    navCount=NavMesh.CalculateTriangulation().vertices.Length;
                    Check("navigation registered",navCount>0);
                    Next();
                }
                else if(stage==1&&EditorApplication.timeSinceStartup-at>1)
                {
                    Check("starts at lobby and grounded",Mathf.Abs(root.Player.transform.position.z+32)<.2f&&Mathf.Abs(root.Player.transform.position.y)<.15f);
                    Capture(root,"entrance");
                    Place(root,new Vector3(0,.08f,-8));Next();
                }
                else if(stage==2&&EditorApplication.timeSinceStartup-at>.5)
                {
                    Capture(root,"auditorium");
                    Place(root,new Vector3(-21,6.08f,-21.2f));
                    probe=(Enemy)Call(root,"SpawnAt",root.Data.Enemies["Crawler"],new Vector3(-29.75f,.08f,-20),false);
                    probe.Speed=7;
                    Next();
                }
                else if(stage==3&&((probe&&probe.transform.position.y>5.8f)||EditorApplication.timeSinceStartup-at>18))
                {
                    Check("real enemy follows stairs to second floor",probe&&probe.transform.position.y>5.8f);
                    Log.AppendLine("Enemy position "+(probe?probe.transform.position.ToString():"missing"));
                    Check("path follower attached",probe&&probe.GetComponent<TheaterPathFollower>());
                    if(probe)probe.Speed=0;
                    Place(root,new Vector3(-20.5f,6.08f,-14));
                    root.Player.transform.rotation=Quaternion.Euler(0,35,0);Next();
                }
                else if(stage==4&&EditorApplication.timeSinceStartup-at>.5)
                {
                    Capture(root,"balcony");
                    Call(root,"SpawnExperience",new Vector3(-20.5f,6.1f,-6),10);
                    var drop=root.ExperiencePickups.Last();
                    Check("second floor experience stays on second floor",Mathf.Abs(drop.transform.position.y-6)<.12f);
                    Check("upper/lower floor cannot pick up remotely",Vector3.Distance(drop.transform.position,new Vector3(-20.5f,0,-6))>ExperiencePickup.PickupRadius);
                    var follower=probe.GetComponent<TheaterPathFollower>();
                    Check("upper floor blocks attack line",!follower.ClearSight(new Vector3(-20.5f,1,-6),new Vector3(-20.5f,7,-6)));
                    Call(root,"SpawnBoss");
                    Check("boss starts without obsolete pillars",root.Boss&&root.Boss.State.PillarCount==0&&root.Zones.Pillars.Count==0);
                    Check("boss starts on stage",root.Boss&&root.Boss.transform.position.y>1.1f&&root.Boss.transform.position.z>16);
                    previous=root;Next();
                }
                else if(stage==5&&EditorApplication.timeSinceStartup-at>.5)
                {
                    Check("boss remains on stage",root.Boss&&root.Boss.transform.position.y>1.1f&&root.Boss.transform.position.z>16);
                    Time.timeScale=1;SceneManager.LoadScene(SceneBuilder.ScenePath);Next();
                }
                else if(stage==6&&root!=previous)
                {
                    Check("restart gets a fresh theater run",root.Zones.Map.IsTheater&&root.RunElapsed<.1f&&root.Boss==null);
                    Check("restart replaces navmesh instead of stacking it",NavMesh.CalculateTriangulation().vertices.Length==navCount);
                    Check("restart clears old experience",root.ExperiencePickups.Count==0);
                    Finish();
                }
            }
            catch(Exception e){Log.AppendLine(e.ToString());fail++;Finish();}
        }
        static void Finish()
        {
            Log.AppendLine($"RESULT Theater play {pass}/{pass+fail} passed");File.WriteAllText("Logs/theater-play-check.log",Log.ToString());
            File.Delete(Request);EditorApplication.isPlaying=false;stage=pass=fail=navCount=0;started=at=0;probe=null;previous=null;Log.Clear();
        }
    }
}

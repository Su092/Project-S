using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicCombatAutoCheck
    {
        const string Request="Logs/music-combat-auto.request";
        static bool initialized;static double started;static float at;static Enemy[] actors;static bool[] seen;static BossView boss;
        static SMusicCombatAutoCheck(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>120)throw new TimeoutException("native scheduler");
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                var r=GameRoot.Instance;if(!r||r.Run==null||r.Menu==null)return;
                if(!initialized)
                {
                    r.InvulnerableForTest=true;r.AutoAcceptOffers=true;r.Player.AiControlled=true;
                    r.Menu.StartOrChoose();r.Menu.SelectGun("rifle");r.Menu.StartOrChoose();
                    typeof(GameRoot).GetField("_spawnTimer",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(r,10000f);
                    r.Player.enabled=false;r.Weapons.enabled=false;
                    var cc=r.Player.GetComponent<CharacterController>();cc.enabled=false;r.Player.transform.position=new Vector3(0,.06f,-10);cc.enabled=true;
                    actors=new[]{r.SpawnMusicElite(new Vector3(-3,0,-6),"S08_MetronomeGuard_Rehearsal"),r.SpawnMusicElite(new Vector3(0,0,-4),"S14_XylophoneStegosaur_Ebony"),r.SpawnMusicElite(new Vector3(3,0,-6),"S21_BagpipeBoar_Ceremony")};seen=new bool[3];
                    var go=new GameObject("AutoConductor");go.transform.position=r.Zones.BossSpawn;
                    boss=go.AddComponent<BossView>();boss.Init(6000,r.Data.Boss,MusicEnemyAssets.Conductor);typeof(GameRoot).GetProperty("Boss").SetValue(r,boss);
                    initialized=true;at=Time.time;return;
                }
                for(int i=0;i<3;i++)seen[i]|=actors[i]&&actors[i].MusicBrain.Busy;
                if(Time.time-at<7)return;
                var checks=new[]{
                    ("08 native AI chooses attack",seen[0]),("14 native AI chooses attack",seen[1]),("21 native AI chooses attack",seen[2]),
                    ("29 native AI summons elite",boss.Music.Summon&&boss.Music.Summon.Alive),
                    ("29 native AI summons ordinary gun",boss.Music.Guns.Count>0),
                    ("new bosses never run old shield cycle",!boss.State.ShieldUp),
                    ("fresh run keeps game active",!r.CombatEnded)
                };
                File.WriteAllLines("Logs/music-combat-auto.log",checks.Select(c=>(c.Item2?"PASS ":"FAIL ")+c.Item1).Concat(new[]{"RESULT native scheduler "+checks.Count(c=>c.Item2)+"/"+checks.Length+" passed"}));
                var fixture=actors[0].MusicBrain.Spotlight.Fixture;
                var review=new GameObject("FollowSpotFrontReview");var camera=review.AddComponent<Camera>();
                camera.transform.position=fixture.LightOrigin.position+fixture.LightOrigin.forward*2+fixture.LightOrigin.right*.6f;
                camera.transform.LookAt(fixture.HeadPivot);camera.fieldOfView=40;
                SFoundryMapBuilder.Capture(camera,"Shots/music-follow-spot-front.png");UnityEngine.Object.Destroy(review);
                Finish();
            }
            catch(Exception e){File.WriteAllText("Logs/music-combat-auto.log","FAIL "+e);Finish();}
        }
        static void Finish(){File.Delete(Request);Time.timeScale=1;EditorApplication.isPlaying=false;initialized=false;started=0;actors=null;boss=null;}
    }
}

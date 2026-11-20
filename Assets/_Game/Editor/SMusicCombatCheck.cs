using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using S.View;
using S.Core;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicCombatCheck
    {
        const string Request="Logs/music-combat-play.request";
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> Log=new();
        static int phase,pass,fail;static double started,wall;static float at;
        static GameRoot root,oldRoot;static Enemy guard,dragon,boar;static BossView conductor,dancer;
        static Vector3 spotMount;static Quaternion pausedLamp;
        static MusicHazard fall;static float frozen;static int kills,drops;
        static SMusicCombatCheck(){EditorApplication.update+=Tick;}
        [MenuItem("S/Enemies/Run Music Combat Check")]
        public static void RequestRun()=>File.WriteAllText(Request,"");
        static void Check(string name,bool ok){Log.Add((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;File.WriteAllLines("Logs/music-combat-play.log",Log);}
        static void Next(){phase++;at=Time.time;wall=EditorApplication.timeSinceStartup;}
        static Enemy Elite(string id,Vector3 pos)
        {
            var e=root.SpawnMusicElite(pos,id);e.enabled=false;return e;
        }
        static BossView Boss(string id,Vector3 pos)
        {
            var go=new GameObject("MusicBossCheck_"+id);go.transform.position=pos;
            var boss=go.AddComponent<BossView>();boss.Init(6000,root.Data.Boss,id);boss.enabled=false;return boss;
        }
        static void PlayerAt(Vector3 p)
        {
            var cc=root.Player.GetComponent<CharacterController>();cc.enabled=false;root.Player.transform.position=p;cc.enabled=true;Physics.SyncTransforms();
        }
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>240)throw new TimeoutException("phase "+phase);
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                root=GameRoot.Instance;if(!root||root.Run==null||root.Menu==null)return;
                if(phase==0)
                {
                    root.InvulnerableForTest=true;root.AutoAcceptOffers=true;root.Player.AiControlled=true;
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    typeof(GameRoot).GetField("_spawnTimer",Hidden).SetValue(root,10000f);
                    root.Player.enabled=false;root.Weapons.enabled=false;Next();
                }
                else if(phase==1&&Time.time-at>.5f)
                {
                    PlayerAt(new Vector3(0,.06f,-20));
                    Check("music rules do not leave old shield",!root.CombatEnded);
                    var rails=MusicCombatWorld.RailAnchors();
                    File.WriteAllLines("Logs/music-rail-anchors.log",rails.Select(x=>x.ToString()));
                    Check("balcony has at least three distinct turret mounts",rails.Length>=3);
                    if(rails.Length<3)
                    {
                        File.WriteAllLines("Logs/music-rail-debug.log",root.Zones.Map.GetComponentsInChildren<Transform>().Where(t=>t.name.ToLowerInvariant().Contains("rail")).Select(t=>t.name+" "+t.position));
                    }
                    guard=Elite("S08_MetronomeGuard",new Vector3(-3,0,-14));
                    dragon=Elite("S14_XylophoneStegosaur",new Vector3(0,0,-14));
                    boar=Elite("S21_BagpipeBoar",new Vector3(3,0,-14));
                    Check("elite variants share a family scheduler",guard.MusicBrain.Family=="S08"&&dragon.MusicBrain.Family=="S14"&&boar.MusicBrain.Family=="S21");
                    Check("elite stage focus exists",guard.MusicBrain.Spotlight&&dragon.MusicBrain.Spotlight&&boar.MusicBrain.Spotlight);
                    var fixture=guard.MusicBrain.Spotlight.Fixture;
                    Check("delivered lamp loaded with animated rig",fixture&&fixture.Animator&&fixture.LightOrigin);
                    Check("lamp has no blocking collider",fixture.GetComponentsInChildren<Collider>().Length==0);
                    spotMount=guard.MusicBrain.Spotlight.transform.position;pausedLamp=fixture.Pitch.rotation;
                    fall=MusicHazard.Falling(dragon,new Vector3(0,0,-20),10);fall.enabled=false;
                    fall.Step(.49f);Check("falling note holds for first 0.49s",fall.Age<.5f&&!fall.Impacted&&Mathf.Abs(fall.transform.GetChild(0).localPosition.y-4)<.001f);
                    fall.Step(.02f);Check("fall begins after 0.5s, not instant impact",!fall.Impacted&&fall.transform.GetChild(0).localPosition.y<4);
                    fall.enabled=true;root.SetPaused(true);frozen=fall.Age;Next();
                }
                else if(phase==2&&EditorApplication.timeSinceStartup-wall>.3)
                {
                    Check("pause freezes lamp rig",Quaternion.Angle(pausedLamp,guard.MusicBrain.Spotlight.Fixture.Pitch.rotation)<.01f);
                    Check("pause freezes hazard clock",fall&&fall.Age==frozen);
                    root.SetPaused(false);
                    guard.MusicBrain.Begin("Sweep");dragon.MusicBrain.Begin("Falling");boar.MusicBrain.Begin("Vent");
                    Check("elite skill starts",guard.MusicBrain.Busy&&dragon.MusicBrain.Busy&&boar.MusicBrain.Busy);
                    Next();
                }
                else if(phase==3&&Time.time-at>5.4f)
                {
                    var focus=guard.MusicBrain.Spotlight;var fixture=focus.Fixture;
                    Check("spotlight fixed ceiling mount",Vector3.Distance(spotMount,focus.transform.position)<.001f);
                    Check("lamp socket tracks target",Vector3.Angle(fixture.LightOrigin.forward,focus.Target.position+Vector3.up*.5f-fixture.LightOrigin.position)<1);
                    Check("beam starts at delivered lamp socket",Vector3.Distance(focus.GetComponent<LineRenderer>().GetPosition(0),fixture.LightOrigin.position)<.001f);
                    var lampCameraObject=new GameObject("LampReviewCamera");var lampCamera=lampCameraObject.AddComponent<Camera>();
                    lampCamera.transform.position=fixture.HeadPivot.position+new Vector3(1.3f,-.7f,1.6f);lampCamera.transform.LookAt(fixture.HeadPivot);lampCamera.fieldOfView=45;
                    SFoundryMapBuilder.Capture(lampCamera,"Shots/music-follow-spot.png");UnityEngine.Object.Destroy(lampCameraObject);
                    Check("14 four falling strikes",dragon.MusicBrain.LastFallingCount==4);
                    Check("08 and 21 complete recovery",!guard.MusicBrain.Busy&&!boar.MusicBrain.Busy);
                    dragon.MusicBrain.Begin("Lanes");guard.MusicBrain.Begin("Bass");boar.MusicBrain.Begin("Charge");
                    Next();
                }
                else if(phase==4&&Time.time-at>.95f)
                {
                    var gate=UnityEngine.Object.FindObjectsByType<MusicHazard>().FirstOrDefault(h=>h.Kind=="Wave"&&h.Height>2);
                    Check("14 uses delivered note gate",gate&&gate.transform.GetChild(0).name.StartsWith("FXM_S14_NoteGate"));
                    Check("note gate replaces temporary wireframe",gate&&!gate.GetComponent<LineRenderer>()&&gate.GetComponentsInChildren<Collider>().Length==0);
                    root.SetPaused(true);
                    var camObj=new GameObject("MusicSkillReviewCamera");var cam=camObj.AddComponent<Camera>();cam.transform.position=new Vector3(6,4,-16);cam.transform.LookAt(new Vector3(0,0,-7));cam.fieldOfView=65;
                    SFoundryMapBuilder.Capture(cam,"Shots/music-elite-skills.png");UnityEngine.Object.Destroy(camObj);
                    root.SetPaused(false);Next();
                }
                else if(phase==5&&!dragon.MusicBrain.Busy&&Time.time-at>2)
                {
                    Check("14 4-8 slots produce N through 2N actual notes",dragon.MusicBrain.LastLaneSlots>=4&&dragon.MusicBrain.LastLaneSlots<=8&&dragon.MusicBrain.LastLaneNotes>=dragon.MusicBrain.LastLaneSlots&&dragon.MusicBrain.LastLaneNotes<=dragon.MusicBrain.LastLaneSlots*2);
                    Check("21 charge completes or wall-stuns then recovers",!boar.MusicBrain.Busy);
                    // Sweep damage uses the real player position, including jump and map cover.
                    foreach(var hazard in UnityEngine.Object.FindObjectsByType<MusicHazard>()){hazard.enabled=false;UnityEngine.Object.Destroy(hazard.gameObject);}
                    PlayerAt(new Vector3(0,1.3f,-8));
                    var high=MusicHazard.Wave(guard,new Vector3(0,0,-10),Vector3.forward,2.2f,5,10,3.6f,10,"FXM_S14_NoteGate");high.enabled=false;high.Step(.2f);
                    Check("marching note catches normal jump",high.HitPlayer);UnityEngine.Object.Destroy(high.gameObject);
                    var low=MusicHazard.Wave(guard,new Vector3(0,0,-10),Vector3.forward,3.5f,5,10,.45f,10,"FXM_S08_BassWave");low.enabled=false;low.Step(.2f);
                    Check("bass wave can be jumped",!low.HitPlayer);UnityEngine.Object.Destroy(low.gameObject);
                    PlayerAt(new Vector3(0,.05f,-8));
                    var cover=GameObject.CreatePrimitive(PrimitiveType.Cube);cover.name="MusicTestCover";cover.transform.SetParent(root.Zones.Map.transform,true);cover.transform.position=new Vector3(0,1,-9);cover.transform.localScale=new Vector3(3,2,.3f);Physics.SyncTransforms();
                    var blocked=MusicHazard.Wave(guard,new Vector3(0,0,-10),Vector3.forward,2.2f,5,10,3.6f,10,"FXM_S14_NoteGate");blocked.enabled=false;blocked.Step(.3f);
                    Check("map cover stops lane before player",!blocked.HitPlayer&&blocked.transform.position.z<-9);
                    Check("cover also stops laser ray",MusicCombatWorld.ClearLength(new Vector3(0,1,-10),Vector3.forward,5)<1);
                    UnityEngine.Object.Destroy(blocked.gameObject);UnityEngine.Object.DestroyImmediate(cover);Physics.SyncTransforms();
                    // Multi-floor and jump checks use the actual theater surfaces.
                    var ground=MusicCombatWorld.Floor(new Vector3(0,.1f,-20));
                    PlayerAt(ground+Vector3.up*1.3f);
                    Check("jump remains on same ground floor",MusicCombatWorld.SameFloor(ground));
                    var upstairs=root.Zones.Map.EnemySpawns.First(t=>t.position.y>5).position;
                    PlayerAt(upstairs);
                    Check("ground telegraph cannot hit balcony player",!MusicCombatWorld.SameFloor(ground));
                    PlayerAt(new Vector3(0,.06f,-20));
                    conductor=Boss(MusicEnemyAssets.Conductor,root.Zones.BossSpawn);typeof(GameRoot).GetProperty("Boss").SetValue(root,conductor);
                    Check("conductor summon begins",conductor.Music.Begin("Summon"));Next();
                }
                else if(phase==6&&Time.time-at>2.5f)
                {
                    var brain=conductor.Music;
                    Check("conductor creates exactly one elite",brain.Summon&&brain.Summon.IsElite);
                    if(!brain.Summon)throw new Exception("No summon");
                    brain.Summon.enabled=false;
                    Check("summon only health reduced",Mathf.Abs(brain.Summon.MaxHp/(root.Data.Enemy("Brute").Hp*root.Director.Elite.HpMul*root.Run.HpScale)-.35f)<.0035f&&Mathf.Abs(brain.Summon.ContactDamage/(root.Data.Enemy("Brute").Damage*root.Director.Elite.DmgMul*root.Run.DmgScale)-1)<.01f&&Mathf.Abs(brain.Summon.Speed-guard.Speed)<.01f);
                    var lamp=brain.Spotlight.Fixture;
                    Check("lamp physically aims at summon",Vector3.Angle(lamp.LightOrigin.forward,brain.Summon.transform.position+Vector3.up*.5f-lamp.LightOrigin.position)<1);
                    Check("spotlight transfers",brain.Spotlight.Target==brain.Summon.transform);
                    float hp=conductor.State.Hp;conductor.OnHit(null,conductor.AimPoint,100);
                    Check("summon reduces boss damage by 80%",Mathf.Abs(hp-conductor.State.Hp-20)<.01f);
                    Check("cannot summon second living elite",!brain.Begin("Summon"));
                    for(int i=0;i<4;i++)brain.SpawnGun();
                    Check("gun cap three",brain.Guns.Count==3);
                    Check("normal guns 5% boss max HP",brain.Guns.All(g=>Mathf.Abs(g.Actor.MaxHp-300)<.01f));
                    Check("ability 2 leaves spotlight on summon",brain.Spotlight.Target==brain.Summon.transform);
                    conductor.State.TakeDamage(3200,false);
                    Check("finale starts under half HP",brain.Begin("Finale"));Next();
                }
                else if(phase==7&&Time.time-at>.1f)
                {
                    var brain=conductor.Music;
                    Check("finale tops up to three and heals to 10%",brain.Guns.Count==3&&brain.Guns.All(g=>g.Actor.Hp==600&&g.Actor.MaxHp==600));
                    Check("two reductions do not stack",brain.DamageMultiplier==.2f);
                    kills=root.Run.Kills;drops=root.ExperiencePickups.Count;
                    var dead=brain.Guns[0];dead.Actor.ApplyDamage(99999);
                    Check("destructible gun no farm reward",!dead.Actor.Alive&&root.Run.Kills==kills&&root.ExperiencePickups.Count==drops&&!dead.Busy);
                    brain.Summon.ApplyDamage(99999);brain.Tick(.01f);
                    Check("spotlight returns while finale reduction remains",brain.Spotlight.Target==conductor.transform&&brain.DamageMultiplier==.2f);
                    Next();
                }
                else if(phase==8&&Time.time-at>15)
                {
                    var brain=conductor.Music;
                    Check("two rounds each total 3-5 queued shots",brain.RoundShotCounts.Count==2&&brain.RoundShotCounts.All(x=>x>=3&&x<=5));
                    Check("destroyed gun not replenished mid finale",brain.Guns.Count==2);
                    Check("live guns enter laser round",brain.Guns.All(g=>g.LaserTicks>0));
                    Check("laser target speed 30% standard walk",brain.Guns.All(g=>Mathf.Abs(g.LaserFollowSpeed-root.Data.PlayerWalk*.3f)<.001f));
                    var liveGun=brain.Guns.First();var pitch=liveGun.Visual.GetComponentsInChildren<Transform>().First(t=>t.name=="pitch");
                    Check("gun barrel follows advertised aim",Vector3.Angle(liveGun.Visual.Muzzle.position-pitch.position,liveGun.AimPosition-pitch.position)<8);
                    root.SetPaused(true);
                    var reviewObj=new GameObject("MusicTurretReview");var reviewCam=reviewObj.AddComponent<Camera>();
                    var inward=MusicCombatWorld.Flat(root.PlayerTransform.position-liveGun.transform.position).normalized;
                    reviewCam.transform.position=liveGun.transform.position+inward*4+Vector3.up*1.2f+Vector3.Cross(Vector3.up,inward)*2;
                    reviewCam.transform.LookAt(liveGun.Actor.AimPoint);reviewCam.fieldOfView=65;
                    SFoundryMapBuilder.Capture(reviewCam,"Shots/music-note-gun.png");UnityEngine.Object.Destroy(reviewObj);root.SetPaused(false);
                    Next();
                }
                else if(phase==9&&!conductor.Music.Busy)
                {
                    var brain=conductor.Music;
                    Check("finale completes, restores reduction",!brain.FinaleActive&&brain.DamageMultiplier==1);
                    Check("survivor gun caps return to 5%",brain.Guns.All(g=>Mathf.Abs(g.Actor.MaxHp-300)<.01f));
                    // Verify the real weapon path can damage a turret, not its boss owner.
                    var gun=brain.Guns.First();gun.Cancel();gun.Finale=true;Physics.SyncTransforms();
                    Check("aim point stays inside animated gun hurtbox",gun.Visual.HitColliders.Any(c=>c.bounds.Contains(gun.Actor.AimPoint)));
                    float hp=gun.Actor.Hp;var shot=new GameObject("MusicGunHitProbe");var towardPlayer=(root.PlayerAimPoint-gun.Actor.AimPoint).normalized;shot.transform.position=gun.Actor.AimPoint+towardPlayer*2;
                    shot.AddComponent<Projectile>().Init(new ProjectileDesc{Kind=BulletKind.Beam,Range=4,Damage=7,Pierce=0,Size=.01f},-towardPlayer);
                    Check("player projectile damages turret body",gun.Actor.Hp<hp);
                    Vector3 mounted=gun.transform.position;gun.Actor.PullToward(Vector3.forward*3);
                    Check("attraction cannot detach rail turret",gun.transform.position==mounted);
                    conductor.State.TakeDamage(999999,false);brain.Cancel();
                    Check("boss cleanup unregisters owned guns",brain.Guns.Count==0&&!brain.Summon);
                    dancer=Boss(MusicEnemyAssets.Ballerina,new Vector3(0,0,-20));typeof(GameRoot).GetProperty("Boss").SetValue(root,dancer);
                    dancer.State.TakeDamage(3200,false);
                    PlayerAt(new Vector3(0,.06f,-28));dancer.transform.rotation=Quaternion.identity;
                    Check("dancer waltz starts",dancer.Music.Begin("Waltz"));Next();
                }
                else if(phase==10&&!dancer.Music.Busy&&Time.time-at>2)
                {
                    File.WriteAllText("Logs/music-dance-debug.log",dancer.Music.PlannedDancePoints+" "+dancer.Music.DanceStopReason);
                    Check("dancer recorded traversed arc",dancer.Music.LastDancePath.Count>1);
                    Check("rewind returns to actual starting point",Vector3.Distance(dancer.transform.position,dancer.Music.LastDancePath[0])<.08f);
                    Check("dancer pluck starts",dancer.Music.Begin("Pluck"));Next();
                }
                else if(phase==11&&Time.time-at>3.2f)
                {
                    Check("dancer curtsey exposes weak window",dancer.Music.WeakWindow&&dancer.Music.DamageMultiplier==1.5f);
                    var weak=dancer.Visual.HitColliders.First(c=>c.name.Contains("WeakPoint"));var body=dancer.Visual.HitColliders.First(c=>!c.name.Contains("WeakPoint"));
                    Check("dancer weak bonus only on cylinder proxy",dancer.Music.HitMultiplier(weak)==1.5f&&dancer.Music.HitMultiplier(body)==1);
                    Check("API warning textures and beam loaded",SRes.Get<Texture2D>("T_MusicWarningRing")&&SRes.Get<Texture2D>("T_MusicWarningLane")&&SRes.Get<Material>("MAT_MusicSpotBeam"));
                    root.SetPaused(true);
                    foreach(var r in root.Player.GetComponentsInChildren<Renderer>(true))r.forceRenderingOff=true;
                    root.Cam.nearClipPlane=3;root.Cam.transform.rotation=Quaternion.LookRotation(dancer.AimPoint-root.Cam.transform.position);
                    SFoundryMapBuilder.Capture(root.Cam,"Shots/music-boss-skills.png");
                    root.SetPaused(false);
                    MusicCombatWorld.EndEncounter();Next();
                }
                else if(phase==12&&Time.time-at>.1f)
                {
                    Check("end cleanup removes warnings and projectiles",UnityEngine.Object.FindObjectsByType<MusicHazard>().Length==0&&UnityEngine.Object.FindObjectsByType<MusicTelegraph>().Length==0);
                    Check("end cleanup removes lamp fixtures",UnityEngine.Object.FindObjectsByType<MusicFollowSpotRig>().Length==0);
                    oldRoot=root;SceneManager.LoadScene(SceneBuilder.ScenePath);Next();
                }
                else if(phase==13&&root!=oldRoot)
                {
                    Check("restart clears summons and turret schedules",root.Enemies.Count==0&&UnityEngine.Object.FindObjectsByType<MusicNoteGun>().Length==0);
                    Check("restart opens menu with zero time",root.Menu.IsOpen&&root.RunElapsed<.1f);Finish();
                }
            }
            catch(Exception e){Check(e.ToString(),false);Finish();}
        }
        static void Finish()
        {
            Log.Add("RESULT Music combat "+pass+"/"+(pass+fail)+" passed");File.WriteAllLines("Logs/music-combat-play.log",Log);
            File.Delete(Request);Time.timeScale=1;EditorApplication.isPlaying=false;phase=pass=fail=0;started=0;Log.Clear();
        }
    }
}

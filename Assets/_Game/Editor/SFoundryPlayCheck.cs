using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using S.Core;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SFoundryPlayCheck
    {
        const string Request="Logs/foundry-play.request";
        static readonly StringBuilder Log=new StringBuilder();
        static int phase,pass,fail;static double started,at;
        static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static SFoundryPlayCheck(){EditorApplication.update+=Tick;}
        static void Check(string name,bool ok){Log.AppendLine((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>90){Check("play timeout",false);Finish();return;}
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                var root=GameRoot.Instance;if(!root||root.Menu==null||root.Run==null)return;
                if(phase==0)
                {
                    var maps=UnityEngine.Object.FindObjectsByType<FoundryArena>();
                    Check("one authored map at normal bootstrap",maps.Length==1&&root.Zones.Map==maps[0]);
                    Check("map Addressables preload",SRes.Get<GameObject>(FoundryArena.AssetName)!=null&&!SRes.HasFailed);
                    Check("legacy procedural arena absent",GameObject.Find("ArenaFloor")==null&&GameObject.Find("WallX")==null);
                    Check("pillars hidden before boss",root.Zones.Map.EnergyPillars.All(p=>!p.gameObject.activeSelf));
                    Check("player movement layer configured",root.Player.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.ActorLayer));
                    root.InvulnerableForTest=true;root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    Check("normal selected weapon starts",!root.Menu.IsOpen&&!root.Paused&&root.Weapons.GunCount==1);
                    root.Player.AiControlled=true;root.Player.InputLocked=false;phase=1;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==1&&EditorApplication.timeSinceStartup-at>2)
                {
                    Check("normal enemy spawning",root.Enemies.Count>0);
                    Check("enemies use movement layer",root.Enemies.All(e=>e.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.MovementLayer)));
                    var cc=root.Player.GetComponent<CharacterController>();
                    Check("player settles onto authored floor",root.Player.transform.position.y<.15f&&root.Player.transform.position.y>-.1f);
                    root.SetPaused(true);
                    // Actual projectile flight must ignore the smooth ramp when the visible step is lower.
                    var rampProxy=new GameObject("MovementOnlyShotProbe");
                    rampProxy.layer=LayerMask.NameToLayer(WorldPhysics.MovementLayer);
                    rampProxy.transform.position=new Vector3(0,30,0);
                    var proxyCollider=rampProxy.AddComponent<BoxCollider>();proxyCollider.size=new Vector3(2,.1f,2);
                    Physics.SyncTransforms();
                    var origin=rampProxy.transform.position+Vector3.up;
                    Check("movement-only probe participates in movement queries",Physics.Raycast(origin,Vector3.down,2,WorldPhysics.GroundMask));
                    var probe=new GameObject("ProductionShotProbe");probe.transform.position=origin;
                    var bullet=probe.AddComponent<Projectile>();
                    bullet.Init(new ProjectileDesc{Kind=BulletKind.Proj,Speed=100,Lifetime=10,Size=.01f,Damage=1},Vector3.down);
                    typeof(Projectile).GetMethod("TickFlight",Private).Invoke(bullet,new object[]{.02f});
                    Check("real projectile crosses movement-only collision",probe.transform.position.y<29.1f);
                    UnityEngine.Object.Destroy(probe);UnityEngine.Object.Destroy(rampProxy);
                    // Drop onto a high deck using the production death/reward path.
                    var deck=root.Zones.Map.transform.Find("HighDecks").Cast<Transform>().First(t=>t.name=="Platform_Deck4");
                    var drop=deck.TransformPoint(new Vector3(2,3f,-2));
                    var enemyGo=new GameObject("DropProbe");enemyGo.transform.position=drop;
                    var enemy=enemyGo.AddComponent<Enemy>();enemy.enabled=false;
                    root.OnEnemyKilled(enemy,1);
                    var pickup=root.ExperiencePickups.Last();
                    Check("experience lands on authored high deck",Mathf.Abs(pickup.transform.position.y-2.5f)<.06f);
                    cc.enabled=false;root.Player.transform.position=pickup.transform.position+Vector3.up*.05f;cc.enabled=true;
                    Check("pause prevents collection",!root.TryCollectExperience(pickup));
                    root.SetPaused(false);Check("production collection succeeds on high deck",root.TryCollectExperience(pickup));
                    Check("drop cannot pay twice",!root.TryCollectExperience(pickup));root.SetPaused(true);
                    UnityEngine.Object.Destroy(enemyGo);
                    typeof(GameRoot).GetMethod("SpawnBoss",Private).Invoke(root,null);
                    Check("boss uses raised platform marker",root.Boss&&Vector3.Distance(root.Boss.transform.position,root.Zones.BossSpawn)<.001f);
                    Check("four authored pillars activated",root.Zones.Pillars.Count==4&&root.Zones.Pillars.All(p=>p.gameObject.activeInHierarchy));
                    Physics.SyncTransforms();
                    var pillar=root.Zones.Pillars[0];
                    var beamGo=new GameObject("PillarBeamProbe");beamGo.transform.position=pillar.transform.position+new Vector3(0,1.5f,-4);
                    beamGo.AddComponent<Projectile>().Init(new ProjectileDesc{Kind=BulletKind.Beam,Range=8,Damage=1000,Lifetime=1,Size=.1f},Vector3.forward);
                    Check("real beam breaks authored pillar",!pillar.Alive&&pillar.BrokenVisual.activeSelf&&!pillar.IntactVisual.activeSelf);
                    Check("boss notified of broken pillar",root.Boss.State.PillarsAlive==3);
                    cc.enabled=false;root.Player.transform.position=root.Zones.PlayerSpawn;root.Player.transform.rotation=Quaternion.identity;root.Cam.transform.localRotation=Quaternion.identity;cc.enabled=true;
                    root.SetPaused(false);root.Player.InputLocked=false;
                    phase=2;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==2&&EditorApplication.timeSinceStartup-at>.7)
                {
                    SFoundryMapBuilder.Capture(root.Cam,"Shots/foundry-gameplay-camera.png");
                    phase=3;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==3&&EditorApplication.timeSinceStartup-at>.5)Finish();
            }
            catch(Exception e){fail++;Log.AppendLine("FAIL "+e);Finish();}
        }
        static void Finish()
        {
            File.Delete(Request);Log.AppendLine($"RESULT Foundry play {pass}/{pass+fail} passed");
            File.WriteAllText("Logs/foundry-play-check.log",Log.ToString());Debug.Log(Log.ToString());
            EditorApplication.isPlaying=false;phase=0;started=0;
        }
    }
}

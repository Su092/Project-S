using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using S.Core;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SEnemyPlayCheck
    {
        const string Request="Logs/enemy-play.request";
        static readonly StringBuilder Log=new();
        static int phase,pass,fail;
        static double start,at;
        static Enemy victim;
        static Vector3 pausedBone;
        static Transform tracked;
        static readonly List<Enemy> enemies=new();
        static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static SEnemyPlayCheck(){EditorApplication.update+=Tick;}
        static void Check(string text,bool ok){Log.AppendLine((ok?"PASS ":"FAIL ")+text);if(ok)pass++;else fail++;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(start==0)start=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-start>120){Check("timeout",false);Finish();return;}
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                var root=GameRoot.Instance;if(!root||root.Menu==null||root.Run==null)return;
                if(phase==0)
                {
                    Check("all nine addressable prefabs loaded",SEnemyImporter.Names.All(n=>SRes.Get<GameObject>(n))&&!SRes.HasFailed);
                    root.InvulnerableForTest=true;root.AutoAcceptOffers=true;
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    root.Player.AiControlled=true;root.Player.InputLocked=false;
                    phase=1;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==1&&EditorApplication.timeSinceStartup-at>1)
                {
                    Check("normal spawning uses authored model",root.Enemies.Count>0&&root.Enemies.All(e=>e.Visual));
                    root.SetPaused(true);
                    var spawn=typeof(GameRoot).GetMethod("SpawnAt",Flags);
                    int index=0;
                    foreach(bool elite in new[]{false,true})
                    foreach(var kind in new[]{"Crawler","Brute","Bomber","Turret"})
                    {
                        var e=(Enemy)spawn.Invoke(root,new object[]{root.Data.Enemy(kind),new Vector3(500+index*10,10,0),elite});
                        e.enabled=false;enemies.Add(e);var v=e.Visual;
                        Check(v.AssetId+" mapping",v.AssetId==EnemyAssets.Name(kind,elite)&&e.IsElite==elite);
                        Check(v.AssetId+" no extra elite scale",(v.transform.localScale-Vector3.one).sqrMagnitude<.0001f);
                        Check(v.AssetId+" controller excluded from shots",(WorldPhysics.ShotMask&(1<<e.gameObject.layer))==0);
                        Check(v.AssetId+" hit proxies follow skeleton",v.HitColliders.All(c=>c.enabled&&c.transform.IsChildOf(v.Animator.transform)));
                        var bone=v.Animator.GetComponentsInChildren<Transform>().First(t=>t.name=="body");
                        var rs=v.GetComponentsInChildren<SkinnedMeshRenderer>();
                        var renderer=rs.First(r=>r.name.EndsWith("_LOD0"));
                        var before=SEnemyImporter.MeshBounds(renderer);
                        v.Animator.Play(v.IdleState,0,0);v.Animator.Update(.2f);
                        var after=SEnemyImporter.MeshBounds(renderer);
                        Check(v.AssetId+" idle axis/scale before="+before+" after="+after+" rootScale="+v.Animator.transform.localScale+" rootRot="+v.Animator.transform.localEulerAngles,Mathf.Abs(after.size.y-before.size.y)<.12f&&Vector3.Distance(after.center,before.center)<.12f);
                        var rest=v.Animator.GetComponentsInChildren<Transform>().Select(t=>t.localPosition).ToArray();
                        v.Animator.Play(v.AttackState,0,.52f);v.Animator.Update(0);
                        var attack=v.Animator.GetComponentsInChildren<Transform>().Select(t=>t.localPosition).ToArray();
                        bool moved=rest.Zip(attack,(a,b)=>Vector3.Distance(a,b)).Any(x=>x>.0005f);
                        // Rotation-only attack parts are also valid.
                        Check(v.AssetId+" imported attack plays moved="+moved+" state="+v.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash+" expected="+Animator.StringToHash(v.AttackState),v.Animator.GetCurrentAnimatorStateInfo(0).IsName(v.AttackState)&&moved);
                        v.Animator.Play(v.IdleState,0,0);v.Animator.Update(0);
                        index++;
                    }
                    Physics.SyncTransforms();
                    victim=enemies[0];var origin=victim.AimPoint+Vector3.up*5;
                    var rayHits=Physics.RaycastAll(origin,Vector3.down,10,WorldPhysics.ShotMask,QueryTriggerInteraction.Collide)
                        .Count(h=>h.collider.GetComponentInParent<Enemy>()==victim);
                    Check("beam probe crosses multiple body proxies ("+rayHits+")",rayHits>1);
                    float hp=victim.Hp;
                    var shot=new GameObject("CompoundBeamProbe");shot.transform.position=origin;
                    shot.AddComponent<Projectile>().Init(new ProjectileDesc{Kind=BulletKind.Beam,Range=10,Damage=7,Pierce=30,Lifetime=1,Size=.02f},Vector3.down);
                    Check("beam hits compound enemy exactly once",Mathf.Abs(victim.Hp-(hp-7))<.001f);
                    hp=victim.Hp;
                    shot=new GameObject("CompoundBulletProbe");shot.transform.position=origin;
                    var projectile=shot.AddComponent<Projectile>();
                    projectile.Init(new ProjectileDesc{Kind=BulletKind.Proj,Speed=20,Lifetime=10,Damage=7,Pierce=30,Size=.02f},Vector3.down);
                    var tick=typeof(Projectile).GetMethod("TickFlight",Flags);
                    tick.Invoke(projectile,new object[]{.3f});tick.Invoke(projectile,new object[]{.3f});
                    Check("piercing bullet hits compound enemy once",Mathf.Abs(victim.Hp-(hp-7))<.001f);
                    UnityEngine.Object.Destroy(shot);
                    foreach(var e in enemies.Where(e=>e.KindId=="Turret"))
                    {
                        var point=e.Visual.transform.position+new Vector3(0,e.Visual.Dimensions.y*.5f+.4f+e.Visual.Dimensions.y*.25f,0);
                        bool blocked=Physics.RaycastAll(point+Vector3.forward*3,Vector3.back,6,WorldPhysics.ShotMask,QueryTriggerInteraction.Collide)
                            .Any(h=>h.collider.GetComponentInParent<Enemy>()==e);
                        Check(e.Visual.AssetId+" ring upper opening passes shots",!blocked);
                    }
                    tracked=enemies[1].Visual.Animator.GetComponentsInChildren<Transform>().First(t=>t.name=="body");
                    pausedBone=tracked.localPosition;phase=2;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==2&&EditorApplication.timeSinceStartup-at>.3)
                {
                    Check("pause freezes imported animation",(tracked.localPosition-pausedBone).sqrMagnitude<.000001f);
                    int kills=root.Run.Kills,drops=root.ExperiencePickups.Count,xp=root.Run.Xp;
                    victim.ApplyDamage(10000);victim.ApplyDamage(10000);
                    Check("death awards exactly one kill and drop",root.Run.Kills==kills+1&&root.ExperiencePickups.Count==drops+1&&root.Run.Xp==xp);
                    Check("corpse visuals remain but cannot be hit",victim&&victim.Visual.IsDying&&victim.Visual.HitColliders.All(c=>!c.enabled)&&!victim.GetComponent<CharacterController>().enabled);
                    typeof(GameRoot).GetMethod("SpawnBoss",Flags).Invoke(root,null);
                    var boss=root.Boss;
                    Check("Boss uses new authored prefab",boss.Visual&&boss.Visual.AssetId==EnemyAssets.Boss);
                    Check("Boss has no oversized sphere",boss.GetComponent<SphereCollider>()==null&&boss.Visual.HitColliders.Length>0);
                    Check("Boss muzzle follows skeleton",boss.Visual.Muzzle&&boss.Visual.Muzzle.IsChildOf(boss.Visual.Animator.transform));
                    var target=(Vector3)typeof(BossView).GetField("_target",Flags).GetValue(boss);
                    Check("Boss roaming keeps feet inside central platform",new Vector2(target.x,target.z).magnitude<=6f);
                    var bossRenderer=boss.Visual.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name.EndsWith("_LOD0"));
                    Check("Boss feet sit on platform",Mathf.Abs(SEnemyImporter.MeshBounds(bossRenderer).min.y-.25f)<.02f);
                    boss.Visual.BossState(false,true,2);
                    boss.Visual.Action("SpikeCast",.5f,3);
                    root.SetPaused(false);
                    phase=3;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==3&&EditorApplication.timeSinceStartup-at>2)
                {
                    Check("corpse cleans up after death clip",victim==null);
                    Check("Boss skill animation advances",root.Boss.Visual.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime>0);
                    // Stage actual production instances in the map for a readable comparison.
                    for(int i=1;i<enemies.Count;i++)
                    {
                        var cc=enemies[i].GetComponent<CharacterController>();cc.enabled=false;
                        enemies[i].transform.position=new Vector3(-12+(i-1)*4,0,-24);
                        enemies[i].transform.rotation=Quaternion.Euler(0,180,0);
                    }
                    root.Boss.transform.position=new Vector3(0,.35f,0);
                    var playerCC=root.Player.GetComponent<CharacterController>();playerCC.enabled=false;
                    root.Player.transform.position=new Vector3(0,1,-38);
                    root.Player.transform.rotation=Quaternion.identity;root.Cam.transform.localRotation=Quaternion.Euler(0,0,0);
                    root.SetPaused(true);
                    SFoundryMapBuilder.Capture(root.Cam,"Shots/enemies-gameplay.png");
                    phase=4;at=EditorApplication.timeSinceStartup;
                }
                else if(phase==4&&EditorApplication.timeSinceStartup-at>.2)Finish();
            }
            catch(Exception e){fail++;Log.AppendLine("FAIL "+e);Finish();}
        }
        static void Finish()
        {
            File.Delete(Request);Log.AppendLine("RESULT Enemy play "+pass+"/"+(pass+fail)+" passed");
            File.WriteAllText("Logs/enemy-play-check.log",Log.ToString());Debug.Log(Log.ToString());
            EditorApplication.isPlaying=false;phase=0;start=0;enemies.Clear();
        }
    }
}

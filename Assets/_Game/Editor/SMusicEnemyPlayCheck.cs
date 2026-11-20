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
    public static class SMusicEnemyPlayCheck
    {
        const string Request="Logs/music-enemy-play.request";
        static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        static readonly List<string> Log=new();
        static readonly List<GameObject> Review=new();
        static int phase,pass,fail;
        static double started,at;
        static Enemy victim;
        static Transform pausedBone;
        static Vector3 pausedPosition;
        static Quaternion pausedRotation;
        static float killedAt;
        static GameRoot previous;
        static SMusicEnemyPlayCheck(){EditorApplication.update+=Tick;}
        [MenuItem("S/Enemies/Run Music Play Check")]
        public static void RequestRun(){File.WriteAllText(Request,"");}
        static object Call(GameRoot root,string name,params object[] args)=>typeof(GameRoot).GetMethod(name,Hidden).Invoke(root,args);
        static void Check(string text,bool ok)
        {
            Log.Add((ok?"PASS ":"FAIL ")+text);if(ok)pass++;else fail++;
            File.WriteAllLines("Logs/music-enemy-play.log",Log);
        }
        static void Next(){phase++;at=EditorApplication.timeSinceStartup;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>240)throw new TimeoutException("Music check phase "+phase);
                if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
                var root=GameRoot.Instance;
                if(!root||root.Run==null||root.Menu==null)return;
                if(phase==0)
                {
                    root.InvulnerableForTest=true;root.AutoAcceptOffers=true;root.Player.AiControlled=true;
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    typeof(GameRoot).GetField("_spawnTimer",Hidden).SetValue(root,10000f);
                    Check("music catalog loaded via Addressables",SMusicEnemyImporter.ReadPlan().assets.All(a=>SRes.Get<GameObject>(a.id))&&!SRes.HasFailed);
                    Next();
                }
                else if(phase==1&&EditorApplication.timeSinceStartup-at>.6)
                {
                    root.SetPaused(true);
                    int index=0;
                    foreach(var a in SMusicEnemyImporter.ReadPlan().assets)
                    {
                        var go=UnityEngine.Object.Instantiate(SRes.Get<GameObject>(a.id));
                        go.transform.position=new Vector3(400+(index%6)*5,0,(index/6)*6);Review.Add(go);go.transform.rotation=Quaternion.Euler(0,180,0);
                        var v=go.GetComponent<EnemyVisual>();
                        Check(a.id+" runtime root unit scale",v.transform.localScale==Vector3.one);
                        foreach(var state in v.ClipNames)
                        {
                            v.Animator.Play(state,0,.51f);v.Animator.Update(0);
                            Check(a.id+"/"+state+" Animator plays",v.Animator.GetCurrentAnimatorStateInfo(0).IsName(state));
                            float error=SMusicGeometryCheck.AnimatorDelta(a,v,v.Clips.First(c=>c.name==state),.51f);
                            Check(a.id+"/"+state+" actual skinned geometry matches source ("+error.ToString("F7")+"m)",error<.001f);
                        }
                        if(a.id=="S08_MetronomeGuard")SMusicGeometryCheck.CaptureFixedGuard(v);
                        v.Animator.Play(v.IdleState,0,0);v.Animator.Update(0);Physics.SyncTransforms();
                        Check(a.id+" ray can hit visible core",v.HitColliders.Any(c=>c.bounds.Contains(v.AimPoint)));
                        index++;
                    }
                    Physics.SyncTransforms();
                    victim=(Enemy)Call(root,"SpawnAt",root.Data.Enemy("Crawler"),new Vector3(200,0,0),false);
                    victim.enabled=false;
                    Check("real spawn selects common music model",MusicEnemyAssets.Commons.Contains(victim.Visual.AssetId));
                    var elite=(Enemy)Call(root,"SpawnAt",root.Data.Enemy("Brute"),new Vector3(210,0,0),true);
                    elite.enabled=false;
                    Check("real elite selects music variant without extra scale",MusicEnemyAssets.Elites.Contains(elite.Visual.AssetId)&&elite.Visual.transform.localScale==Vector3.one);
                    pausedBone=Review[0].GetComponent<EnemyVisual>().Animator.GetComponentsInChildren<Transform>().First(t=>t.name=="body");
                    pausedPosition=pausedBone.localPosition;pausedRotation=pausedBone.localRotation;
                    Call(root,"SpawnBoss");
                    Check("boss music visual",root.Boss&&(root.Boss.Visual.AssetId==MusicEnemyAssets.Ballerina||root.Boss.Visual.AssetId==MusicEnemyAssets.Conductor));
                    root.Boss.Visual.Action("Volley",.65f,2);
                    Check("legacy skill animation is mapped",root.Boss.Visual.CurrentState=="Pluck_Beat"||root.Boss.Visual.CurrentState=="Conduct_Stagger");
                    Next();
                }
                else if(phase==2&&EditorApplication.timeSinceStartup-at>.3)
                {
                    Check("pause freezes animated pose",Vector3.Distance(pausedPosition,pausedBone.localPosition)<.00001f&&Quaternion.Angle(pausedRotation,pausedBone.localRotation)<.001f);
                    CheckMovementAndOuterHit(victim);
                    float hp=victim.Hp;
                    var shot=new GameObject("MusicBeamProbe");shot.transform.position=victim.AimPoint+Vector3.forward*4;
                    Physics.SyncTransforms();
                    shot.AddComponent<Projectile>().Init(new ProjectileDesc{Kind=BulletKind.Beam,Range=8,Damage=7,Pierce=30,Lifetime=1,Size=.02f},Vector3.back);
                    Check("real projectile damages music hurtbox exactly once",Mathf.Abs(victim.Hp-(hp-7))<.01f);
                    UnityEngine.Object.Destroy(shot);
                    int kills=root.Run.Kills,drops=root.ExperiencePickups.Count,xp=root.Run.Xp;
                    victim.ApplyDamage(10000);victim.ApplyDamage(10000);
                    Check("death counted once; XP remains pickup",root.Run.Kills==kills+1&&root.ExperiencePickups.Count==drops+1&&root.Run.Xp==xp);
                    Check("death immediately disables collisions",!victim.GetComponent<CharacterController>().enabled&&victim.Visual.HitColliders.All(c=>!c.enabled));
                    Check("death clip 0.4 seconds",Mathf.Abs(victim.Visual.ClipLength("Death")-.4f)<.015f);
                    root.SetPaused(false);killedAt=Time.time;Next();
                }
                else if(phase==3&&Time.time-killedAt>.46f)
                {
                    Check("common removed within 0.46s without old corpse hold",!victim);
                    CaptureLineup();
                    // Put five commons and two bosses in the actual auditorium for visual review.
                    for(int i=0;i<Review.Count;i++)UnityEngine.Object.Destroy(Review[i]);Review.Clear();
                    var ids=MusicEnemyAssets.Commons.Concat(new[]{MusicEnemyAssets.Ballerina,MusicEnemyAssets.Conductor}).ToArray();
                    for(int i=0;i<ids.Length;i++)
                    {
                        var go=UnityEngine.Object.Instantiate(SRes.Get<GameObject>(ids[i]));Review.Add(go);
                        go.transform.position=new Vector3(-9+i*3,0,-10);
                        go.transform.rotation=Quaternion.Euler(0,180,0);
                    }
                    root.Player.enabled=false;var cc=root.Player.GetComponent<CharacterController>();cc.enabled=false;
                    root.Player.transform.position=new Vector3(0,.05f,-21);root.Player.transform.rotation=Quaternion.identity;
                    root.Cam.transform.localRotation=Quaternion.Euler(0,0,0);
                    Next();
                }
                else if(phase==4&&EditorApplication.timeSinceStartup-at>.5)
                {
                    root.SetPaused(true);
                    foreach(var renderer in root.Player.GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
                    root.Cam.nearClipPlane=3f; // Review capture excludes the existing first-person fallback weapon.
                    SFoundryMapBuilder.Capture(root.Cam,"Shots/music-enemies-theater.png");
                    previous=root;Time.timeScale=1;SceneManager.LoadScene(SceneBuilder.ScenePath);Next();
                }
                else if(phase==5&&root!=previous)
                {
                    Check("restart removes review models",!GameObject.Find("MusicBeamProbe")&&root.Run.Kills==0);
                    Check("restart returns to weapon selection",root.Menu.IsOpen&&root.Enemies.Count==0);
                    Finish();
                }
            }
            catch(Exception e){Check(e.ToString(),false);Finish();}
        }
        static void CheckMovementAndOuterHit(Enemy enemy)
        {
            var cc=enemy.GetComponent<CharacterController>();var origin=enemy.transform.position;var rotation=enemy.transform.rotation;
            cc.enabled=false;enemy.transform.position=new Vector3(200,10,0);cc.enabled=true;
            enemy.transform.rotation=Quaternion.identity;
            var before=enemy.transform.position;enemy.MoveDelta(Vector3.right*.3f);
            var moved=enemy.transform.position-before;moved.y=0;
            Check("movement faces actual displacement instead of player",moved.magnitude>.2f&&Vector3.Dot(enemy.transform.forward,moved.normalized)>.999f);
            var facing=enemy.transform.rotation;enemy.MoveDelta(Vector3.zero);enemy.MoveDelta(Vector3.up*.2f);
            Check("zero and vertical movement preserve heading",Quaternion.Angle(facing,enemy.transform.rotation)<.01f);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="FacingWallProbe";
            wall.transform.position=enemy.transform.position+Vector3.right*(cc.radius+.2f);wall.transform.localScale=new Vector3(.2f,8,12);
            Physics.SyncTransforms();before=enemy.transform.position;enemy.MoveDelta(new Vector3(1,0,1));moved=enemy.transform.position-before;moved.y=0;
            Check("wall slide changes requested direction",moved.z>.4f&&moved.x<.6f);
            Check("wall slide faces collision-resolved direction",moved.sqrMagnitude>.01f&&Vector3.Dot(enemy.transform.forward,moved.normalized)>.999f);
            UnityEngine.Object.DestroyImmediate(wall);
            cc.enabled=false;enemy.transform.position=origin;enemy.transform.rotation=rotation;cc.enabled=true;
            Physics.SyncTransforms();
            var box=enemy.Visual.HitColliders.OrderByDescending(c=>Mathf.Abs(c.bounds.center.x-enemy.AimPoint.x)).First();
            var target=box.bounds.center;float hp=enemy.Hp;
            var shot=new GameObject("OuterContourBeamProbe");shot.transform.position=target+Vector3.up*5;
            shot.AddComponent<Projectile>().Init(new ProjectileDesc{Kind=BulletKind.Beam,Range=10,Damage=7,Pierce=100,Lifetime=1,Size=.01f},Vector3.down);
            Check("outer silhouette hit damages once across overlapping boxes",Mathf.Abs(enemy.Hp-(hp-7))<.01f);
            UnityEngine.Object.Destroy(shot);
        }
        static void CaptureLineup()
        {
            var camObject=new GameObject("MusicReviewCamera");var cam=camObject.AddComponent<Camera>();
            cam.transform.position=new Vector3(412,15,-24);cam.transform.LookAt(new Vector3(412,1,6));
            cam.orthographic=true;cam.orthographicSize=9;cam.backgroundColor=new Color(.22f,.26f,.32f);cam.clearFlags=CameraClearFlags.SolidColor;
            cam.nearClipPlane=.1f;cam.farClipPlane=70;
            SFoundryMapBuilder.Capture(cam,"Shots/music-enemies-lineup.png");
            UnityEngine.Object.Destroy(camObject);
        }
        static void Finish()
        {
            Log.Add("RESULT Music play "+pass+"/"+(pass+fail)+" passed");
            File.WriteAllLines("Logs/music-enemy-play.log",Log);File.Delete(Request);
            foreach(var go in Review)if(go)UnityEngine.Object.Destroy(go);
            Review.Clear();Time.timeScale=1;EditorApplication.isPlaying=false;
            phase=pass=fail=0;started=at=0;Log.Clear();
        }
    }
}

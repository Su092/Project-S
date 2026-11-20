using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using S.Core;
namespace S.View
{
    public sealed class MusicBossBrain:MonoBehaviour
    {
        public BossView Boss;
        public bool IsConductor,FinaleActive,Busy,WeakWindow;
        public Enemy Summon;
        public MusicSpotlight Spotlight;
        public readonly List<MusicNoteGun> Guns=new();
        public readonly List<Vector3> LastDancePath=new();
        public readonly List<int> RoundShotCounts=new();
        public string CurrentSkill;
        public int PlannedDancePoints;public string DanceStopReason;
        public float SkillDamage=>24*(GameRoot.Instance?.Run?.DmgScale??1);
        public float DamageMultiplier=>IsConductor?MusicCombatRules.DamageMultiplier(Summon&&Summon.Alive,FinaleActive):WeakWindow?1.5f:1;
        public float HitMultiplier(Collider collider) => IsConductor?DamageMultiplier:WeakWindow&&collider&&collider.name.Contains("WeakPoint")?1.5f:1;
        public float Cooldown=2;
        float _gunCd=5,_summonCd=3,_finaleCd=8;bool _dance=true;
        readonly List<MusicTelegraph> _marks=new();
        EnemyVisual V=>Boss.Visual;
        public void Init(BossView boss)
        {
            Boss=boss;IsConductor=V.AssetId==MusicEnemyAssets.Conductor;
            Spotlight=MusicSpotlight.Create(boss,boss.transform);
        }
        public void Tick(float dt)
        {
            if(dt<=0||!MusicCombatWorld.Active)return;
            Guns.RemoveAll(g=>!g||!g.Actor||!g.Actor.Alive);
            if(Summon&&!Summon.Alive)Summon=null;
            if(Spotlight)Spotlight.Target=Summon&&Summon.Alive?Summon.transform:transform;
            if(IsConductor)
            {
                _gunCd-=dt;_summonCd-=dt;_finaleCd-=dt;
                if(!FinaleActive&&_gunCd<=0)
                {
                    if(Guns.Count<3){SpawnGun();if(!Busy)V.Action("GunSummon_Cue",.9f,4);}
                    _gunCd=9;
                }
                if(!Busy)
                {
                    if(Boss.State.HpRatio<=.5f&&_finaleCd<=0){Begin("Finale");_finaleCd=24;}
                    else if(!Summon&&_summonCd<=0){Begin("Summon");_summonCd=22;}
                }
                if(!Busy)Face(MusicCombatWorld.Player-transform.position);
            }
            else if(!Busy)
            {
                Cooldown-=dt;
                if(!MusicCombatWorld.Sight(Boss.AimPoint,MusicCombatWorld.PlayerAim)||!MusicCombatWorld.SameFloor(transform.position))
                {Approach(dt);return;}
                Face(MusicCombatWorld.Player-transform.position);
                if(Cooldown<=0){Begin(_dance?"Waltz":"Pluck");_dance=!_dance;}
            }
        }
        void Face(Vector3 d){d=MusicCombatWorld.Flat(d);if(d.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(d);}
        void Approach(float dt)
        {
            var nav=GetComponent<TheaterPathFollower>();if(!nav)nav=gameObject.AddComponent<TheaterPathFollower>();
            Vector3 d=nav.Direction(MusicCombatWorld.Player);
            if(d.sqrMagnitude>.001f)
            {
                var next=transform.position+d*2.5f*dt;
                if(NavMesh.SamplePosition(next,out var hit,.6f,NavMesh.AllAreas)){var before=transform.position;transform.position=hit.position;ActorFacing.AlongMovement(transform,transform.position-before);}
            }
        }
        public bool Begin(string skill)
        {
            if(Busy||!Boss.State.Alive)return false;
            if(skill=="Summon"&&Summon&&Summon.Alive)return false;
            if(skill=="Finale"&&Boss.State.HpRatio>.5f)return false;
            Busy=true;CurrentSkill=skill;StartCoroutine(Run(skill));return true;
        }
        void Play(string name,float seconds)
        {if(name.EndsWith("Loop")||name=="Laser_Hold"||name=="Command_Idle")V.LoopAction(name,seconds,6);else V.Action(name,seconds,6);}
        MusicTelegraph Mark(Vector3 p,Vector3 d,float l,float w,bool circle=false)
        {var m=MusicTelegraph.Shape(Boss,p,d,l,w,circle);_marks.Add(m);return m;}
        void ClearMarks(){foreach(var m in _marks)if(m)Destroy(m.gameObject);_marks.Clear();}
        public MusicNoteGun SpawnGun()
        {
            Guns.RemoveAll(g=>!g||!g.Actor||!g.Actor.Alive);
            if(Guns.Count>=3)return null;
            var anchors=MusicCombatWorld.RailAnchors();
            var available=anchors.Where(p=>!Guns.Any(g=>Vector3.Distance(g.transform.position,p)<3)&&MusicCombatWorld.Sight(p+Vector3.up*.6f,MusicCombatWorld.PlayerAim)).ToArray();
            if(available.Length==0)available=anchors.Where(p=>!Guns.Any(g=>Vector3.Distance(g.transform.position,p)<3)).ToArray();
            if(available.Length==0){Debug.LogWarning("[Music] No unoccupied balcony gun anchor");return null;}
            Vector3 pos=available[Random.Range(0,available.Length)];
            var actor=GameRoot.Instance.SpawnMusicAuxiliary(pos,Boss.State.MaxHp*.05f);
            FaceGun(actor);
            var gun=actor.gameObject.AddComponent<MusicNoteGun>();gun.Init(this,actor);Guns.Add(gun);return gun;
        }
        void FaceGun(Enemy actor)
        {
            Vector3 dir=MusicCombatWorld.Flat(MusicCombatWorld.Player-actor.transform.position);
            if(dir.sqrMagnitude>.001f)actor.transform.rotation=Quaternion.LookRotation(dir);
        }
        IEnumerator Run(string skill)
        {
            if(skill=="Summon")
            {
                Play("Teleport_Out",.4f);yield return new WaitForSeconds(.4f);
                transform.position=GameRoot.Instance.Zones.Map.BossSpawn.position;
                V.transform.localPosition=Vector3.zero;
                Play("Teleport_In",.4f);yield return new WaitForSeconds(.4f);
                Play("Summon_Cue",1.2f);yield return new WaitForSeconds(.8f);
                var map=GameRoot.Instance.Zones.Map;
                Vector3 desired=MusicCombatWorld.Player+MusicCombatWorld.Flat(transform.position-MusicCombatWorld.Player).normalized*9;
                Vector3 at=NavMesh.SamplePosition(desired,out var nh,5,NavMesh.AllAreas)?nh.position:map.EnemySpawns[0].position;
                Summon=GameRoot.Instance.SpawnMusicElite(at);
                Summon.MaxHp*=MusicCombatRules.SummonHealth;Summon.Hp=Summon.MaxHp;
                if(Summon.GetComponent<MusicEliteBrain>().Spotlight)Destroy(Summon.GetComponent<MusicEliteBrain>().Spotlight.gameObject);
                Spotlight.Target=Summon.transform;
                yield return new WaitForSeconds(.4f);Play("Command_Idle",2);
            }
            else if(skill=="Finale")
            {
                FinaleActive=true;RoundShotCounts.Clear();
                foreach(var gun in Guns.ToArray())if(gun&&gun.Actor.Alive)gun.SetFinale(true);
                int missing=3-Guns.Count;
                for(int i=0;i<missing;i++){var g=SpawnGun();if(g)g.SetFinale(true);}
                Play("Finale_Windup",1.5f);yield return new WaitForSeconds(1.5f);
                for(int round=0;round<2;round++)
                {
                    var live=Guns.Where(g=>g&&g.Actor.Alive).ToArray();
                    var plan=MusicCombatRules.StaggeredRound(new System.Random(Random.Range(0,int.MaxValue)),live.Length);
                    RoundShotCounts.Add(plan.Length);
                    foreach(int index in plan)
                    {
                        // Assignment is made before this round. Destroyed guns lose their queued beats.
                        Play("Conduct_Stagger",.8f);
                        if(live[index]&&live[index].Actor.Alive)live[index].Fire();
                        yield return new WaitForSeconds(1);
                    }
                    Play("Round_Break",.8f);yield return new WaitForSeconds(.8f);
                }
                Play("Laser_Cue",.8f);
                foreach(var gun in Guns.ToArray())if(gun&&gun.Actor.Alive)gun.StartLaser(4);
                yield return new WaitForSeconds(.8f);Play("Laser_Hold",4);yield return new WaitForSeconds(4);
                foreach(var gun in Guns.ToArray())if(gun&&gun.Actor.Alive)gun.SetFinale(false);
                FinaleActive=false;Play("Finale_Recover",2);yield return new WaitForSeconds(2);
            }
            else if(skill=="Pluck")
            {
                Vector3 dir=MusicCombatWorld.Flat(MusicCombatWorld.Player-transform.position).normalized;Face(dir);
                Mark(MusicCombatWorld.Floor(transform.position),dir,2,0,true);Play("Pluck_Prepare",.8f);yield return new WaitForSeconds(.8f);ClearMarks();
                for(int row=0;row<3;row++)
                {
                    Play("Pluck_Beat",.6f);
                    int gap=Random.Range(2,7);
                    for(int i=0;i<9;i++)if(Mathf.Abs(i-gap)>1)
                    {
                        var shotDir=Quaternion.AngleAxis(-55+i*13.75f,Vector3.up)*dir;
                        MusicHazard.Shot(Boss,Boss.AimPoint,shotDir,8,SkillDamage*.7f,"FXM_S16_PluckedNote");
                    }
                    yield return new WaitForSeconds(.75f);
                }
                WeakWindow=true;Play("Curtsey",1.5f);yield return new WaitForSeconds(1.5f);WeakWindow=false;
            }
            else if(skill=="Waltz")
            {
                LastDancePath.Clear();var planned=new List<Vector3>();Vector3 start=transform.position,dir=transform.forward;
                planned.Add(start);Vector3 prior=start;DanceStopReason="";
                for(int i=1;i<=24;i++)
                {
                    float t=i/24f;
                    Vector3 next=start+dir*(Mathf.Sin(t*Mathf.PI*.8f)*6)+transform.right*((1-Mathf.Cos(t*Mathf.PI*.8f))*4);
                    if(!MusicCombatWorld.Ground(next,out var floor,.5f)||Mathf.Abs(floor.y-start.y)>.6f||!MusicCombatWorld.GroundLine(prior,floor)){DanceStopReason="plan blocked "+prior+" -> "+next+" floor "+floor;break;}
                    next=floor;planned.Add(next);prior=next;
                }
                PlannedDancePoints=planned.Count;
                for(int i=1;i<planned.Count;i++)Mark(planned[i-1],planned[i]-planned[i-1],Vector3.Distance(planned[i],planned[i-1]),2.8f);
                Play("Waltz_Windup",1);yield return new WaitForSeconds(1);ClearMarks();
                LastDancePath.Add(transform.position);Play("Waltz_Loop",3);
                yield return Follow(planned,false);
                Play("Waltz_Stop",.4f);yield return new WaitForSeconds(.4f);
                if(Boss.State.HpRatio<=.5f&&LastDancePath.Count>1)
                {
                    var back=LastDancePath.ToArray();System.Array.Reverse(back);
                    for(int i=1;i<back.Length;i++)Mark(back[i-1],back[i]-back[i-1],Vector3.Distance(back[i],back[i-1]),2.8f);
                    Play("Rewind_Windup",.9f);yield return new WaitForSeconds(.9f);ClearMarks();
                    Play("Rewind_Loop",3);yield return Follow(new List<Vector3>(back),true);
                    WeakWindow=true;Play("Curtsey_Long",2);yield return new WaitForSeconds(2);WeakWindow=false;
                }
            }
            ClearMarks();Busy=false;CurrentSkill=null;Cooldown=1.5f;
        }
        IEnumerator Follow(List<Vector3> path,bool reverse)
        {
            bool hit=false;
            for(int i=1;i<path.Count;i++)
            {
                while(Vector3.Distance(transform.position,path[i])>.03f)
                {
                    float dt=Time.deltaTime;if(dt<=0){yield return null;continue;}
                    var next=Vector3.MoveTowards(transform.position,path[i],5*dt);
                    if(!MusicCombatWorld.GroundLine(transform.position,next)){DanceStopReason="travel blocked "+transform.position+" -> "+next;yield break;}
                    var before=transform.position;transform.position=next;ActorFacing.AlongMovement(transform,transform.position-before);
                    if(!hit&&MusicCombatWorld.InSector(transform.position,transform.forward,1.4f,360)){MusicCombatWorld.Damage(SkillDamage);hit=true;}
                    yield return null;
                }
                if(!reverse)LastDancePath.Add(transform.position);
            }
        }
        public void Cancel()
        {
            StopAllCoroutines();ClearMarks();Busy=false;FinaleActive=false;WeakWindow=false;
            foreach(var gun in Guns.ToArray())if(gun){gun.Cancel();GameRoot.Instance?.DespawnMusicActor(gun.Actor);}
            Guns.Clear();if(Summon){GameRoot.Instance?.DespawnMusicActor(Summon);Summon=null;}
            if(Spotlight)Destroy(Spotlight.gameObject);
        }
        void OnDisable()=>Cancel();
    }
}

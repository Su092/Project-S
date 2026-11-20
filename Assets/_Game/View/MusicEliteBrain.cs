using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using S.Core;
namespace S.View
{
    /// <summary>Authored elite attacks; all cosmetic variants share this same scheduler.</summary>
    public sealed class MusicEliteBrain:MonoBehaviour
    {
        public Enemy Actor;public string Family;
        public bool Busy {get;private set;}
        public string CurrentSkill {get;private set;}
        public int LastLaneSlots,LastLaneNotes,LastFallingCount;
        public MusicSpotlight Spotlight;
        readonly List<MusicTelegraph> _marks=new();
        float _cooldown=1.2f;bool _alternate;
        EnemyVisual V=>Actor.Visual;
        public void Init(Enemy actor)
        {
            Actor=actor;Family=actor.Visual.AssetId.Substring(0,3);
            Spotlight=MusicSpotlight.Create(actor,actor.transform);
        }
        public void Tick(float dt,float speedMultiplier)
        {
            if(!Actor.Alive){Cancel();return;}
            if(dt<=0)return;
            Actor.GroundStep(dt);
            if(Busy)return;
            _cooldown-=dt;
            Vector3 delta=MusicCombatWorld.Player-transform.position;
            bool sight=MusicCombatWorld.Sight(Actor.AimPoint,MusicCombatWorld.PlayerAim);
            bool floor=MusicCombatWorld.SameFloor(transform.position);
            float desired=Family=="S08"?6:Family=="S14"?13:9;
            if(!sight||!floor||MusicCombatWorld.Flat(delta).magnitude>desired)
            {Actor.TravelTo(MusicCombatWorld.Player,dt,speedMultiplier);return;}
            Face(delta);
            if(_cooldown>0)return;
            string skill=Family=="S08"?(MusicCombatWorld.Flat(delta).magnitude<3.2f?"Sweep":"Bass"):
                Family=="S14"?(_alternate?"Lanes":"Falling"):
                (MusicCombatWorld.Flat(delta).magnitude<3.5f?"Vent":"Charge");
            _alternate=!_alternate;Begin(skill);
        }
        public bool Begin(string skill)
        {
            if(Busy||!Actor||!Actor.Alive)return false;
            Busy=true;CurrentSkill=skill;StartCoroutine(Run(skill));return true;
        }
        void Face(Vector3 d){d=MusicCombatWorld.Flat(d);if(d.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(d);}
        MusicTelegraph Mark(Vector3 at,Vector3 dir,float length,float width,bool circle=false,float angle=360)
        {var m=MusicTelegraph.Shape(Actor,at,dir,length,width,circle,angle);_marks.Add(m);return m;}
        void ClearMarks(){foreach(var m in _marks)if(m)Destroy(m.gameObject);_marks.Clear();}
        void Play(string state,float duration)
        {if(state.EndsWith("Loop"))V.LoopAction(state,duration,5);else V.Action(state,duration,5);}
        IEnumerator Run(string skill)
        {
            if(skill=="Sweep")
            {
                Vector3 at=MusicCombatWorld.Floor(transform.position),dir=transform.forward;
                Mark(at,dir,3,0,true,120);Play("Sweep_Windup",.9f);yield return new WaitForSeconds(.9f);
                Play("Sweep_Strike",.35f);
                if(MusicCombatWorld.InSector(at,dir,3,120))MusicCombatWorld.Damage(Actor.ContactDamage);
                ClearMarks();yield return new WaitForSeconds(.35f);Play("Sweep_Recover",1);yield return new WaitForSeconds(1);
            }
            else if(skill=="Bass")
            {
                var at=MusicCombatWorld.Floor(transform.position);var dir=transform.forward;
                float range=MusicCombatWorld.ClearLength(at+Vector3.up*.4f,dir,7);
                Mark(at,dir,range,3.5f);Play("Slam_Windup",.8f);yield return new WaitForSeconds(.8f);
                Play("Slam_Strike",.3f);ClearMarks();
                if(range>.6f)MusicHazard.Wave(Actor,at+dir*.6f,dir,3.5f,range-.6f,7,.45f,Actor.ContactDamage,"FXM_S08_BassWave");
                yield return new WaitForSeconds(.3f);Play("Slam_Recover",.8f);yield return new WaitForSeconds(.8f);
            }
            else if(skill=="Falling")
            {
                Play("Cast_Prepare",.9f);yield return new WaitForSeconds(.9f);LastFallingCount=0;
                for(int i=0;i<4;i++)
                {
                    if(MusicCombatWorld.Sight(Actor.AimPoint,MusicCombatWorld.PlayerAim))
                    {
                        Play("Strike_Plate_0"+(i+1),.8f);
                        yield return new WaitForSeconds(.2f);
                        var at=MusicCombatWorld.Floor(MusicCombatWorld.Player);
                        MusicHazard.Falling(Actor,at,Actor.ContactDamage);LastFallingCount++;
                    }
                    else yield return new WaitForSeconds(.2f);
                    yield return new WaitForSeconds(MusicCombatRules.FallingInterval-.2f);
                }
                Play("Cast_Recover",.7f);yield return new WaitForSeconds(.7f);
            }
            else if(skill=="Lanes")
            {
                Vector3 at=MusicCombatWorld.Floor(transform.position),dir=transform.forward,right=transform.right;
                var origins=new Vector3[4];var lengths=new float[4];
                for(int i=0;i<4;i++)
                {
                    origins[i]=at+right*((i-1.5f)*2.2f)+dir*.8f;
                    lengths[i]=MusicCombatWorld.GroundLine(at,origins[i])?MusicCombatWorld.ClearLength(origins[i]+Vector3.up*.4f,dir,18):0;
                    Mark(origins[i],dir,lengths[i],2.2f);
                }
                var plan=MusicCombatRules.Lanes(new System.Random(Random.Range(0,int.MaxValue)));
                LastLaneSlots=plan[plan.Count-1].Slot+1;LastLaneNotes=0;float elapsed=-.9f;
                Play("Cast_Prepare",.9f);
                for(int slot=0;slot<LastLaneSlots;slot++)
                {
                    var group=plan.FindAll(x=>x.Slot==slot);
                    while(elapsed<group[0].At-.2f){yield return null;elapsed+=Time.deltaTime;}
                    foreach(var mark in _marks)if(mark)mark.Tint(new Color(1,.25f,.1f,.3f));
                    foreach(var beat in group)if(_marks[beat.Lane])_marks[beat.Lane].Tint(new Color(1,.65f,.15f,.95f));
                    Play(group.Count==2?"Double_0"+(group[0].Lane+1)+"_0"+(group[1].Lane+1):"Strike_Plate_0"+(group[0].Lane+1),.8f);
                    foreach(var beat in group)
                    {
                        while(elapsed+.0001f<beat.At){yield return null;elapsed+=Time.deltaTime;}
                        if(!Actor.Alive)yield break;
                        MusicHazard.Wave(Actor,origins[beat.Lane],dir,2.2f,lengths[beat.Lane],10,3.6f,Actor.ContactDamage,"FXM_S14_NoteGate");
                        LastLaneNotes++;
                    }
                }
                yield return new WaitForSeconds(1.9f);ClearMarks();Play("Cast_Recover",.7f);yield return new WaitForSeconds(.7f);
            }
            else if(skill=="Charge")
            {
                Play("Charge_Windup",1.2f);float elapsed=0;Vector3 dir=transform.forward;
                MusicTelegraph lane=null;
                while(elapsed<1.2f)
                {
                    if(elapsed<.8f)
                    {
                        Face(MusicCombatWorld.Player-transform.position);dir=transform.forward;
                        if(lane)Destroy(lane.gameObject);
                        lane=Mark(MusicCombatWorld.Floor(transform.position),dir,12,2);
                    }
                    yield return null;elapsed+=Time.deltaTime;
                }
                ClearMarks();Play("Charge_Loop",1.2f);float travel=0;bool wall=false,hit=false;
                while(travel<12)
                {
                    float dt=Time.deltaTime;if(dt<=0){yield return null;continue;}
                    float step=10*dt;
                    var pos=transform.position;
                    if(!MusicCombatWorld.Ground(pos+dir*step,out var ground)||Mathf.Abs(ground.y-pos.y)>.65f){wall=true;break;}
                    var flags=Actor.MoveDelta(dir*step);
                    travel+=MusicCombatWorld.Flat(transform.position-pos).magnitude;
                    if((flags&CollisionFlags.Sides)!=0||MusicCombatWorld.Flat(transform.position-pos).magnitude<step*.25f){wall=true;break;}
                    if(!hit&&MusicCombatWorld.InSector(transform.position,dir,1.8f,150)){MusicCombatWorld.Damage(Actor.ContactDamage*1.2f);hit=true;}
                    yield return null;
                }
                if(wall){Play("WallImpact",.35f);yield return new WaitForSeconds(.35f);Play("Stun",2);yield return new WaitForSeconds(2);Play("GetUp",.6f);yield return new WaitForSeconds(.6f);}
                else{Play("Charge_Stop",.5f);yield return new WaitForSeconds(.5f);}
            }
            else if(skill=="Vent")
            {
                bool left=Vector3.Dot(MusicCombatWorld.Player-transform.position,transform.right)<0;string suffix=left?"_L":"_R";
                Vector3 dir=left?-transform.right:transform.right,at=MusicCombatWorld.Floor(transform.position);
                Mark(at,dir,4,0,true,100);Play("Vent_Windup"+suffix,.8f);yield return new WaitForSeconds(.8f);
                Play("Vent_Burst"+suffix,.4f);ClearMarks();
                float range=MusicCombatWorld.ClearLength(at+Vector3.up*.5f,dir,4);
                if(range>.4f)MusicHazard.Wave(Actor,at+dir*.4f,dir,3,range-.4f,8,2,Actor.ContactDamage,"FXM_S21_VentGust");
                yield return new WaitForSeconds(.4f);Play("Vent_Recover",.7f);yield return new WaitForSeconds(.7f);
            }
            ClearMarks();Busy=false;CurrentSkill=null;_cooldown=Family=="S14"?2.5f:1.8f;
        }
        public void Cancel(){StopAllCoroutines();ClearMarks();Busy=false;CurrentSkill=null;}
        void OnDisable()=>Cancel();
    }
}

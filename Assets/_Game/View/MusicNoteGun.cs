using System.Collections;
using System.Linq;
using UnityEngine;
namespace S.View
{
    public sealed class MusicNoteGun:MonoBehaviour
    {
        public MusicBossBrain Commander;public Enemy Actor;public EnemyVisual Visual;
        public bool Busy {get;private set;}
        public bool Finale;
        public int ShotsFired,LaserTicks;
        public float LaserFollowSpeed;
        public Vector3 AimPosition;
        LineRenderer _line;float _automatic=2;Transform _yaw,_pitch;
        Vector3 Muzzle=>Visual.Muzzle?Visual.Muzzle.position:Actor.AimPoint;
        public void Init(MusicBossBrain commander,Enemy actor)
        {Commander=commander;Actor=actor;Visual=actor.Visual;AimPosition=MusicCombatWorld.PlayerAim;
            var bones=Visual.GetComponentsInChildren<Transform>();_yaw=bones.FirstOrDefault(t=>t.name=="yaw");_pitch=bones.FirstOrDefault(t=>t.name=="pitch");}
        void LateUpdate()
        {
            if(Time.deltaTime<=0||!Actor||!Actor.Alive||!Visual.Muzzle)return;
            if(!Busy)AimPosition=MusicCombatWorld.PlayerAim;
            if(_yaw)
            {
                var from=MusicCombatWorld.Flat(Muzzle-_yaw.position);var to=MusicCombatWorld.Flat(AimPosition-_yaw.position);
                if(from.sqrMagnitude>.0001f&&to.sqrMagnitude>.0001f)_yaw.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(from,to,Vector3.up),Vector3.up)*_yaw.rotation;
            }
            if(_pitch)
            {var from=Muzzle-_pitch.position;var to=AimPosition-_pitch.position;if(from.sqrMagnitude>.0001f&&to.sqrMagnitude>.0001f)_pitch.rotation=Quaternion.FromToRotation(from,to)*_pitch.rotation;}
        }
        void Update()
        {
            if(!Commander||!Commander.Boss.State.Alive||!Actor.Alive){Cancel();return;}
            if(Time.deltaTime<=0||!MusicCombatWorld.Active)return;
            if(!Finale&&!Busy)
            {
                _automatic-=Time.deltaTime;
                if(_automatic<=0){_automatic=3.4f;Fire();}
            }
        }
        void Line(Color color,float width)
        {
            if(!_line){var go=new GameObject("MusicGunAim");go.transform.SetParent(transform,false);_line=go.AddComponent<LineRenderer>();_line.positionCount=2;}
            _line.sharedMaterial=SMat.Unlit(color);_line.widthMultiplier=width;
        }
        Vector3 Draw(Vector3 target)
        {
            Vector3 from=Muzzle,d=target-from;float len=MusicCombatWorld.ClearLength(from,d.normalized,Mathf.Max(1,d.magnitude+5));
            var end=from+d.normalized*len;_line.SetPosition(0,from);_line.SetPosition(1,end);return end;
        }
        public bool Fire()
        {
            if(Busy||!Actor||!Actor.Alive)return false;
            Busy=true;StartCoroutine(Single());return true;
        }
        IEnumerator Single()
        {
            Line(new Color(1,.07f,.04f,.9f),.025f);
            float t=0;AimPosition=MusicCombatWorld.PlayerAim;
            while(t<.8f)
            {
                if(!Actor.Alive)yield break;
                if(t<.6f)AimPosition=MusicCombatWorld.PlayerAim;
                Draw(AimPosition);yield return null;t+=Time.deltaTime;
            }
            Visual.Action("Fire",.25f,5);
            MusicHazard.Shot(Actor,Muzzle,(AimPosition-Muzzle).normalized,26,Commander.SkillDamage*.65f);
            ShotsFired++;if(_line)Destroy(_line.gameObject);Busy=false;
        }
        public void StartLaser(float seconds=4)
        {
            Cancel();if(!Actor||!Actor.Alive)return;Busy=true;StartCoroutine(Laser(seconds));
        }
        IEnumerator Laser(float seconds)
        {
            AimPosition=MusicCombatWorld.PlayerAim;
            LaserFollowSpeed=GameRoot.Instance.Data.PlayerWalk*S.Core.MusicCombatRules.LaserFollow;
            Line(new Color(1,.06f,.03f,.95f),.025f);
            Visual.Action("LaserStart",.8f,5);float t=0;
            while(t<.8f){Draw(AimPosition);yield return null;t+=Time.deltaTime;}
            Line(new Color(1,.18f,.08f,1),.16f);Visual.LoopAction("LaserLoop",seconds,5);
            float tick=0;t=0;
            while(t<seconds&&Actor.Alive)
            {
                float dt=Time.deltaTime;
                AimPosition=Vector3.MoveTowards(AimPosition,MusicCombatWorld.PlayerAim,LaserFollowSpeed*dt);
                Vector3 end=Draw(AimPosition),from=Muzzle,axis=end-from;
                tick-=dt;
                if(dt>0&&tick<=0)
                {
                    tick+=.25f;LaserTicks++;
                    var cc=GameRoot.Instance.Player.GetComponent<CharacterController>();
                    foreach(var hit in Physics.SphereCastAll(from,.1f,axis.normalized,axis.magnitude,WorldPhysics.ShotMask,QueryTriggerInteraction.Ignore))
                        if(hit.collider==cc){MusicCombatWorld.Damage(Commander.SkillDamage*.2f);break;}
                }
                yield return null;t+=dt;
            }
            if(_line)Destroy(_line.gameObject);Visual.Action("LaserEnd",.3f,5);Busy=false;
        }
        public void SetFinale(bool value)
        {
            Finale=value;Cancel();
            float newMax=Commander.Boss.State.MaxHp*(value?.10f:.05f);
            float hp=S.Core.MusicCombatRules.RescaleHealth(Actor.Hp,Actor.MaxHp,newMax);
            Actor.MaxHp=newMax;Actor.Hp=value?newMax:hp;
            _automatic=2;
        }
        public void BreakApart()
        {
            Cancel();MusicImpact.Show(Actor.AimPoint);MusicGunDebris.Spawn(Actor.AimPoint);
        }
        public void Cancel(){StopAllCoroutines();Busy=false;if(_line)Destroy(_line.gameObject);}
        void OnDisable()=>Cancel();
    }
}

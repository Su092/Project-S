using UnityEngine;
using S.Core;
namespace S.View
{
    /// <summary>Owned, pause-aware attacks. All queries respect scene surfaces and the warning's floor.</summary>
    public sealed class MusicHazard:MonoBehaviour
    {
        public Component Owner;
        public string Kind;
        public float Age, Damage, Radius=1.5f, Speed=10, Distance=18, Height=3.6f, Hold=.5f, Flight=.3f;
        public Vector3 Direction, Origin;
        public bool HitPlayer, Impacted;
        GameObject _model;MusicTelegraph _warning;float _travel;
        public static MusicHazard Falling(Component owner,Vector3 at,float damage)
        {
            var h=New(owner,"FallingNote",at,damage);h.Hold=MusicCombatRules.FallingHold;
            h._model=MusicCombatWorld.Model("FXM_S14_FallingNote",h.transform);
            h._model.transform.localPosition=Vector3.up*4;
            h._warning=MusicTelegraph.Shape(owner,at,Vector3.forward,1.5f,0,true);return h;
        }
        public static MusicHazard Wave(Component owner,Vector3 at,Vector3 dir,float width,float range,float speed,float height,float damage,string model)
        {
            var h=New(owner,"Wave",at,damage);h.Direction=dir.normalized;h.Radius=width*.5f;h.Distance=range;h.Speed=speed;h.Height=height;
            h.transform.rotation=Quaternion.LookRotation(h.Direction);h._model=MusicCombatWorld.Model(model,h.transform);
            // The authored curtain matches the gameplay lane; preserve its full vertical silhouette.
            if(model=="FXM_S14_NoteGate")h._model.transform.localScale=new Vector3(width/2.2f,height/3.6f,1);
            return h;
        }
        public static MusicHazard Shot(Component owner,Vector3 from,Vector3 dir,float speed,float damage,string model="FXM_S29_NoteShot")
        {
            var h=New(owner,"Shot",from,damage);h.Direction=dir.normalized;h.Speed=speed;h.Distance=85;h.Radius=.15f;
            h.transform.rotation=Quaternion.LookRotation(h.Direction);h._model=MusicCombatWorld.Model(model,h.transform);h._model.transform.localScale=Vector3.one*.4f;return h;
        }
        static MusicHazard New(Component owner,string kind,Vector3 at,float damage)
        {
            var go=new GameObject("Music"+kind);go.transform.position=at;
            var h=go.AddComponent<MusicHazard>();h.Owner=owner;h.Kind=kind;h.Origin=at;h.Damage=damage;return h;
        }
        public void Step(float dt)
        {
            if(dt<=0)return;
            if(!MusicCombatWorld.Alive(Owner)){Destroy(gameObject);return;}
            if(Kind!="FallingNote"&&Distance<=.001f){Destroy(gameObject);return;}
            Age+=dt;
            if(Kind=="FallingNote")
            {
                if(Age<Hold)return;
                float t=Mathf.Clamp01((Age-Hold)/Flight);_model.transform.localPosition=Vector3.up*Mathf.Lerp(4,0,t);
                if(t>=1&&!Impacted)
                {
                    Impacted=true;
                    if(MusicCombatWorld.SameFloor(Origin)&&MusicCombatWorld.Flat(MusicCombatWorld.Player-Origin).magnitude<=Radius+.34f&&MusicCombatWorld.Sight(Origin+Vector3.up*.5f,MusicCombatWorld.PlayerAim))
                    {MusicCombatWorld.Damage(Damage);HitPlayer=true;}
                    MusicImpact.Show(Origin+Vector3.up*.15f);Destroy(gameObject);
                }
                return;
            }
            Vector3 old=transform.position;float step=Mathf.Min(Speed*dt,Distance-_travel);
            float clear=MusicCombatWorld.ClearLength(old+Vector3.up*(Kind=="Wave"?.35f:0),Direction,step,Kind=="Shot"?Radius:.1f);
            Vector3 next=old+Direction*clear;
            if(Kind=="Wave")
            {
                if(!MusicCombatWorld.Ground(next,out var ground,.5f)||Mathf.Abs(ground.y-Origin.y)>.6f){Destroy(gameObject);return;}
                next.y=ground.y;
                Vector3 right=Vector3.Cross(Vector3.up,Direction);var d=MusicCombatWorld.Player-old;
                float along=Vector3.Dot(d,Direction);
                if(!HitPlayer&&MusicCombatWorld.SameFloor(Origin)&&d.y>=-.3f&&d.y<=Height&&Mathf.Abs(Vector3.Dot(d,right))<=Radius+.34f&&along>=-.35f&&along<=clear+.35f&&MusicCombatWorld.Sight(old+Vector3.up*.5f,MusicCombatWorld.Player+Vector3.up*.5f))
                {MusicCombatWorld.Damage(Damage);HitPlayer=true;}
            }
            else
            {
                var player=GameRoot.Instance.Player.GetComponent<CharacterController>();
                var hits=Physics.SphereCastAll(old,Radius,Direction,clear+.001f,WorldPhysics.ShotMask,QueryTriggerInteraction.Ignore);
                foreach(var hit in hits)if(hit.collider==player)
                {MusicCombatWorld.Damage(Damage);HitPlayer=true;Destroy(gameObject);return;}
            }
            transform.position=next;_travel+=clear;
            if(clear<step-.001f||_travel>=Distance-.001f||Age>12)Destroy(gameObject);
        }
        void Update()=>Step(Time.deltaTime);
        void OnDestroy(){if(_warning)Destroy(_warning.gameObject);}
    }
}

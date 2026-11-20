using System;
using System.Collections.Generic;
using UnityEngine;
namespace S.View
{
    public static class MusicCombatWorld
    {
        public static Color Warning=new Color(1f,.22f,.12f,.85f), Focus=new Color(1f,.78f,.32f,.45f);
        public static bool Active=>GameRoot.Instance && !GameRoot.Instance.CombatEnded;
        public static bool Alive(Component owner)
        {
            if(!owner||!Active)return false;
            if(owner is Enemy e)return e.Alive;
            if(owner is BossView b)return b.State!=null&&b.State.Alive;
            return true;
        }
        public static Vector3 Player=>GameRoot.Instance.Player.transform.position;
        public static Vector3 PlayerAim=>GameRoot.Instance.PlayerAimPoint;
        public static Vector3 Flat(Vector3 v){v.y=0;return v;}
        public static bool Terrain(Collider c)=>c&&c.GetComponentInParent<FoundryArena>();
        public static bool Ground(Vector3 near,out Vector3 point,float up=.3f)
        {
            point=near;float distance=float.MaxValue;bool found=false;
            foreach(var h in Physics.RaycastAll(near+Vector3.up*up,Vector3.down,30,WorldPhysics.GroundMask,QueryTriggerInteraction.Ignore))
                if(Terrain(h.collider)&&h.normal.y>.65f&&h.distance<distance){point=h.point;distance=h.distance;found=true;}
            return found;
        }
        public static Vector3 Floor(Vector3 near){return Ground(near,out var p)?p:near;}
        public static bool SameFloor(Vector3 p)
        {
            // Probe from the player's feet, not their head: jumping must not select the balcony above.
            return Ground(Player,out var g)&&Mathf.Abs(g.y-p.y)<.7f;
        }
        public static float ClearLength(Vector3 from,Vector3 dir,float distance,float radius=0)
        {
            var hits=radius>0?Physics.SphereCastAll(from,radius,dir,distance,WorldPhysics.ShotMask,QueryTriggerInteraction.Ignore):
                Physics.RaycastAll(from,dir,distance,WorldPhysics.ShotMask,QueryTriggerInteraction.Ignore);
            foreach(var h in hits)if(Terrain(h.collider))distance=Mathf.Min(distance,h.distance);
            return distance;
        }
        public static bool Sight(Vector3 from,Vector3 to)
        {
            var d=to-from;return ClearLength(from,d.normalized,d.magnitude)>=d.magnitude-.03f;
        }
        public static bool GroundLine(Vector3 from,Vector3 to)
        {
            if(Mathf.Abs(Floor(from).y-Floor(to).y)>.65f)return false;
            float d=Vector3.Distance(from,to);
            for(float x=0;x<=d;x+=.5f)
            {var q=Vector3.Lerp(from,to,d>0?x/d:0);if(!Ground(q,out var g,.4f)||Mathf.Abs(g.y-from.y)>.65f)return false;}
            return Sight(from+Vector3.up*.45f,to+Vector3.up*.45f);
        }
        public static bool InSector(Vector3 at,Vector3 direction,float radius,float angle,float height=3.6f)
        {
            var d=Player-at;
            return SameFloor(at)&&d.y>=-.3f&&d.y<=height&&Flat(d).magnitude<=radius+.34f&&
                Vector3.Angle(Flat(direction),Flat(d))<=angle*.5f&&Sight(at+Vector3.up*.6f,Player+Vector3.up*.6f);
        }
        public static void Damage(float amount)=>GameRoot.Instance.DamagePlayer(amount);
        public static GameObject Model(string id,Transform parent=null)
        {
            var prefab=SRes.Get<GameObject>(id);
            if(!prefab)throw new InvalidOperationException("Missing music combat model: "+id);
            return UnityEngine.Object.Instantiate(prefab,parent,false);
        }
        public static Vector3[] RailAnchors()
        {
            var map=GameRoot.Instance.Zones.Map;var points=new List<Vector3>();
            // Derive mounts from the delivered railing modules, so layout edits carry through.
            foreach(var t in map.GetComponentsInChildren<Transform>())
            {
                string n=t.name.ToLowerInvariant();
                if(!(n.Contains("rail")||n.Contains("balustr"))||t.position.y<4.5f||t.position.y>8f)continue;
                var rs=t.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;
                var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                if(b.size.y<.25f||b.size.y>2.5f)continue;
                var p=new Vector3(b.center.x,b.max.y+.12f,b.center.z);
                // Place just toward the auditorium; barrel and core remain above the rail.
                var toward=Flat(map.BossSpawn.position-p).normalized;p+=toward*.3f;
                if(points.Exists(q=>Vector3.Distance(q,p)<2.5f))continue;
                points.Add(p);
            }
            return points.ToArray();
        }
        public static void EndEncounter()
        {
            foreach(var brain in UnityEngine.Object.FindObjectsByType<MusicEliteBrain>())brain.Cancel();
            foreach(var brain in UnityEngine.Object.FindObjectsByType<MusicBossBrain>())brain.Cancel();
            foreach(var fx in UnityEngine.Object.FindObjectsByType<MusicHazard>())UnityEngine.Object.Destroy(fx.gameObject);
            foreach(var fx in UnityEngine.Object.FindObjectsByType<MusicTelegraph>())UnityEngine.Object.Destroy(fx.gameObject);
            foreach(var fx in UnityEngine.Object.FindObjectsByType<MusicSpotlight>())UnityEngine.Object.Destroy(fx.gameObject);
        }
    }
    public sealed class MusicTelegraph:MonoBehaviour
    {
        public Component Owner;
        Mesh _mesh;Material _mat;
        public static MusicTelegraph Shape(Component owner,Vector3 origin,Vector3 direction,float length,float width,bool circle=false,float angle=360)
        {
            var go=new GameObject("MusicWarning");var f=go.AddComponent<MusicTelegraph>();f.Owner=owner;
            go.transform.position=origin+Vector3.up*.045f;
            go.transform.rotation=Quaternion.LookRotation(MusicCombatWorld.Flat(direction).sqrMagnitude>.001f?MusicCombatWorld.Flat(direction):Vector3.forward);
            var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            if(circle)
            {
                verts.Add(Vector3.zero);uv.Add(Vector2.one*.5f);
                const int steps=64;
                for(int i=0;i<=steps;i++){float a=(-angle*.5f+angle*i/steps)*Mathf.Deg2Rad;var v=new Vector3(Mathf.Sin(a)*length,0,Mathf.Cos(a)*length);verts.Add(v);uv.Add(new Vector2(v.x/length,v.z/length)*.5f+Vector2.one*.5f);}
                for(int i=1;i<=steps;i++){tris.Add(0);tris.Add(i);tris.Add(i+1);}
            }
            else
            {
                verts.AddRange(new[]{new Vector3(-width*.5f,0,0),new Vector3(-width*.5f,0,length),new Vector3(width*.5f,0,length),new Vector3(width*.5f,0,0)});
                uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});tris.AddRange(new[]{0,1,2,0,2,3});
            }
            f._mesh=new Mesh();f._mesh.SetVertices(verts);f._mesh.SetUVs(0,uv);f._mesh.SetTriangles(tris,0);f._mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh=f._mesh;
            f._mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            f._mat.SetFloat("_Surface",1);f._mat.SetFloat("_Blend",0);f._mat.SetFloat("_SrcBlend",5);f._mat.SetFloat("_DstBlend",10);f._mat.SetFloat("_ZWrite",0);f._mat.SetFloat("_Cull",0);f._mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");f._mat.renderQueue=3000;
            f._mat.SetColor("_BaseColor",MusicCombatWorld.Warning);
            var tex=SRes.Get<Texture2D>(circle?"T_MusicWarningRing":"T_MusicWarningLane");if(tex)f._mat.SetTexture("_BaseMap",tex);
            go.AddComponent<MeshRenderer>().sharedMaterial=f._mat;
            // Exact geometry outline remains readable even where the decorative texture is sparse.
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.widthMultiplier=.045f;line.sharedMaterial=SMat.Unlit(MusicCombatWorld.Warning);
            var boundary=new List<Vector3>();
            if(circle){if(angle<359)boundary.Add(Vector3.zero);for(int i=1;i<verts.Count;i++)boundary.Add(verts[i]);}
            else boundary.AddRange(verts);
            line.positionCount=boundary.Count;line.SetPositions(boundary.ToArray());return f;
        }
        public void Tint(Color color){if(_mat)_mat.SetColor("_BaseColor",color);var line=GetComponent<LineRenderer>();line.sharedMaterial=SMat.Unlit(color);}
        void Update(){if(!MusicCombatWorld.Alive(Owner))Destroy(gameObject);}
        void OnDestroy(){if(_mesh)Destroy(_mesh);if(_mat)Destroy(_mat);}
    }
    public sealed class MusicSpotlight:MonoBehaviour
    {
        public Component Owner;public Transform Target;
        public MusicFollowSpotRig Fixture {get;private set;}
        LineRenderer _beam;Light _light;MusicTelegraph _spot;
        float _fade;bool _closing;Transform _previousTarget;FoundryArena _arena;
        public static MusicSpotlight Create(Component owner,Transform target)
        {
            var go=new GameObject("MusicStageSpotlight");var s=go.AddComponent<MusicSpotlight>();s.Owner=owner;s.Target=target;
            var floor=MusicCombatWorld.Floor(target.position);
            // One fixed ceiling mount per encounter; only the yoke and head track the actor.
            var mount=floor+new Vector3(2,12,-2);
            var probe=new Vector3(mount.x,Mathf.Max(floor.y+3.8f,4),mount.z);
            float nearest=30;
            foreach(var hit in Physics.RaycastAll(probe,Vector3.up,30,WorldPhysics.GroundMask,QueryTriggerInteraction.Ignore))
                if(MusicCombatWorld.Terrain(hit.collider)&&hit.distance<nearest){nearest=hit.distance;mount=hit.point-Vector3.up*.05f;}
            go.transform.position=mount;
            s.Fixture=MusicCombatWorld.Model("SM_MusicFollowSpot",go.transform).GetComponentInChildren<MusicFollowSpotRig>();
            s.Fixture.Focus(true);s._previousTarget=target;
            s._beam=go.AddComponent<LineRenderer>();s._beam.positionCount=2;s._beam.startWidth=.04f;s._beam.endWidth=2.6f;
            s._beam.sharedMaterial=SRes.Get<Material>("MAT_MusicSpotBeam")??SMat.Combat(true);
            var lamp=new GameObject("MusicFocusLight");lamp.transform.SetParent(s.Fixture.LightOrigin,false);
            s._light=lamp.AddComponent<Light>();s._light.type=LightType.Spot;s._light.color=new Color(1,.78f,.4f);
            s._light.range=45;s._light.spotAngle=26;s._light.innerSpotAngle=16;s._light.intensity=0;s._light.shadows=LightShadows.Soft;s._light.shadowStrength=.85f;s._light.renderMode=LightRenderMode.ForcePixel;
            s._spot=MusicTelegraph.Shape(owner,target.position,Vector3.forward,1.3f,0,true);s._spot.Tint(MusicCombatWorld.Focus);
            s._arena=GameRoot.Instance.Zones.Map;
            if(s._arena)s._arena.SetSpotlightFocus(s,true);
            return s;
        }
        void LateUpdate()
        {
            if(Time.deltaTime<=0)return;
            if(!Target||!MusicCombatWorld.Alive(Owner))
            {
                if(!_closing){_closing=true;Fixture.Focus(false);if(_arena)_arena.SetSpotlightFocus(this,false);}
                _fade=Mathf.MoveTowards(_fade,0,Time.deltaTime*2);
                ApplyFade();if(_fade<=0)Destroy(gameObject);return;
            }
            if(_previousTarget!=Target){Fixture.Focus(true);_previousTarget=Target;}
            // Bring the room down before revealing the follow spot.
            float reveal=_arena&&_arena.IsTheater?Mathf.InverseLerp(.65f,1,_arena.FocusDimming):1;
            _fade=Mathf.MoveTowards(_fade,reveal,Time.deltaTime*3);ApplyFade();
            var floor=MusicCombatWorld.Floor(Target.position);
            var aim=Target.position+Vector3.up*.5f;
            Fixture.Aim(aim);
            _beam.SetPosition(0,Fixture.LightOrigin.position);_beam.SetPosition(1,aim);
            if(_spot)_spot.transform.position=floor+Vector3.up*.06f;
        }
        void ApplyFade()
        {
            _light.intensity=120*_fade;
            _beam.startColor=new Color(1,.8f,.35f,.22f*_fade);_beam.endColor=new Color(1,.8f,.35f,.08f*_fade);
        }
        void OnEnable()
        {
            if(_arena&&!_closing)_arena.SetSpotlightFocus(this,true);
            if(_beam)_beam.enabled=true;
            if(_spot)_spot.gameObject.SetActive(true);
        }
        void OnDisable()
        {
            if(_arena)_arena.SetSpotlightFocus(this,false);
            if(_light)_light.intensity=0;
            if(_beam)_beam.enabled=false;
            if(_spot)_spot.gameObject.SetActive(false);
            _fade=0;
        }
        void OnDestroy(){if(_arena)_arena.SetSpotlightFocus(this,false);if(_spot)Destroy(_spot.gameObject);}
    }
}

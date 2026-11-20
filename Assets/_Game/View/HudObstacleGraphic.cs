using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    /// <summary>North-up vector footprints from map colliders, clipped by the circular UI mask.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudObstacleGraphic : MaskableGraphic
    {
        readonly List<(Collider collider,Vector3[] corners)> _footprints=new();
        Transform _player;
        float _radius;
        public int FootprintCount => _footprints.Count;
        public void Init(FoundryArena map,Transform player,float radius)
        {
            _player=player;_radius=radius;_footprints.Clear();
            Physics.SyncTransforms();
            int movementMask=WorldPhysics.GroundMask;
            foreach(var c in map.GetComponentsInChildren<Collider>(true))
            {
                if(c.isTrigger||(movementMask & (1 << c.gameObject.layer))==0||c.GetComponentInParent<Pillar>())continue;
                var b=c.bounds;
                // Ignore walkable floors / thin ground surfaces; retain walls, columns and cover.
                if(b.size.y<.45f || b.max.y<.5f || b.min.y>10f)continue;
                Vector3[] p;
                if(c is BoxCollider box)
                {
                    var s=box.size*.5f;var o=box.center;
                    p=new[]{o+new Vector3(-s.x,0,-s.z),o+new Vector3(-s.x,0,s.z),o+new Vector3(s.x,0,s.z),o+new Vector3(s.x,0,-s.z)};
                    for(int i=0;i<4;i++)p[i]=box.transform.TransformPoint(p[i]);
                }
                else p=new[]{new Vector3(b.min.x,0,b.min.z),new Vector3(b.min.x,0,b.max.z),new Vector3(b.max.x,0,b.max.z),new Vector3(b.max.x,0,b.min.z)};
                _footprints.Add((c,p));
            }
            raycastTarget=false;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!_player||_radius<=0)return;
            float scale=rectTransform.rect.width*.5f/_radius;
            foreach(var f in _footprints)
            {
                if(!f.collider||!f.collider.enabled||!f.collider.gameObject.activeInHierarchy)continue;
                var b=f.collider.bounds;
                if (b.max.y < _player.position.y + .35f || b.min.y > _player.position.y + 2.5f) continue;
                var d=b.center-_player.position;
                if(Mathf.Abs(d.x)>_radius+b.extents.x||Mathf.Abs(d.z)>_radius+b.extents.z)continue;
                int start=vh.currentVertCount;
                for(int i=0;i<4;i++)
                {
                    var delta=f.corners[i]-_player.position;
                    vh.AddVert(new Vector3(delta.x*scale,delta.z*scale,0),color,Vector2.zero);
                }
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
    }
}

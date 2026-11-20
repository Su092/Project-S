using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace S.View
{
    public sealed class HudMinimapView : MonoBehaviour
    {
        public HudObstacleGraphic Obstacles;
        public Image PlayerArrow, EnemyTemplate, ChestTemplate;
        public RectTransform MarkerRoot;
        public float WorldRadius=35;
        readonly List<Image> _enemies=new(), _chests=new();
        GameRoot _root;
        float _next;
        public int VisibleEnemies { get; private set; }
        public int VisibleChests { get; private set; }
        public void Init(GameRoot root)
        {
            _root=root;
            Obstacles.Init(root.Zones.Map,root.PlayerTransform,WorldRadius);
        }
        public Vector2 Project(Vector3 world)
        {
            var d=world-_root.PlayerTransform.position;
            float scale=MarkerRoot.rect.width*.5f/WorldRadius;
            return new Vector2(d.x,d.z)*scale;
        }
        Image Get(List<Image> list,Image template,int i)
        {
            while(list.Count<=i) { var img=Instantiate(template,MarkerRoot);img.name=template.name+"_"+list.Count;list.Add(img); }
            return list[i];
        }
        bool Inside(Vector3 p) { var d=p-_root.PlayerTransform.position;return d.x*d.x+d.z*d.z<(WorldRadius-1)*(WorldRadius-1); }
        public void Refresh()
        {
            if (_root==null||!_root.PlayerTransform)return;
            PlayerArrow.rectTransform.anchoredPosition=Vector2.zero;
            PlayerArrow.rectTransform.localEulerAngles=new Vector3(0,0,-_root.PlayerTransform.eulerAngles.y);
            if (Time.unscaledTime<_next)return;
            _next=Time.unscaledTime+.1f;
            Obstacles.SetVerticesDirty();
            int n=0;
            foreach(var e in _root.Enemies)
                if(e && e.Alive && Inside(e.transform.position))
                { var img=Get(_enemies,EnemyTemplate,n++);img.gameObject.SetActive(true);img.rectTransform.anchoredPosition=Project(e.transform.position); }
            if(_root.Boss && _root.Boss.State.Alive && Inside(_root.Boss.transform.position))
            { var img=Get(_enemies,EnemyTemplate,n++);img.gameObject.SetActive(true);img.rectTransform.anchoredPosition=Project(_root.Boss.transform.position); }
            VisibleEnemies=n;
            for(int i=n;i<_enemies.Count;i++)_enemies[i].gameObject.SetActive(false);
            n=0;
            foreach(var chest in MinimapChestMarker.Active)
                if(chest && chest.Revealed && !chest.Opened && Inside(chest.transform.position))
                {var img=Get(_chests,ChestTemplate,n++);img.gameObject.SetActive(true);img.rectTransform.anchoredPosition=Project(chest.transform.position);}
            VisibleChests=n;
            for(int i=n;i<_chests.Count;i++)_chests[i].gameObject.SetActive(false);
            PlayerArrow.transform.SetAsLastSibling();
        }
    }
}

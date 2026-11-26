using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    public sealed class BackstageMenu : MonoBehaviour
    {
        enum Page { Hidden, Title, Settings, Walk, Mirror, Score, Archive }
        static readonly string[] Names={"绯燕","青瓷","夜航","紫鸢","金雀","松影"};
        static readonly string[] StartingInstruments={"rifle","smg","shotgun","sniper","beam","launcher"};
        readonly Texture2D[] _portraits=new Texture2D[Names.Length];
        RectTransform _mirrorControls, _abilityBubble, _instrumentBubble;
        CanvasGroup _bubbles;
        float _bubbleStart;
        static readonly string[] Volumes={"主音量","音效","音乐","界面音"};
        static readonly string[] Lessons={"生命上限 +10","经验获取 +10%","吸取范围 +0.3 米"};
        Transform _ui;
        BackstageWorld _world;
        MetaProgress _progress;
        
        Action<string> _start;
        Page _page;
        int _performer;
        public Sprite StartingInstrumentIcon => UiAssets.Named("hud_gun_" + StartingInstruments[_progress.Performer]);
        public bool Walking => _page==Page.Walk;
        public bool Visible => _page!=Page.Hidden;
        public bool Archive => _page==Page.Archive;
        public Transform StartButton => _ui.Find("Title/StartBtn");

        public void Init(Canvas canvas,MetaProgress progress,SData data,Action<string> start,
                         Action<int,float> stepVolume,Action archive)
        {
            _progress=progress; _start=start;
            _ui=Instantiate(Resources.Load<GameObject>("Backstage/FrontDesk"),canvas.transform,false).transform;
            var rect=(RectTransform)_ui;
            rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero;
            _world=new GameObject("Backstage set").AddComponent<BackstageWorld>();
            _world.transform.SetParent(transform,false);
            _world.Build();
            _ui.Find("Title/Emblem").GetComponent<RawImage>().texture=_world.Portrait;
            _ui.Find("Title/Backdrop").GetComponent<RawImage>().texture=Resources.Load<Texture2D>("Backstage/Overture");
            Bind("Title/StartBtn",()=>EnterRoom(true));
            Bind("Title/Settings",()=>{ Turn(Page.Settings); RefreshVolume(); });
            Bind("Title/Quit",Quit);
            Bind("Settings/Back",OpenTitle);
            for(int i=0;i<4;i++)
            {
                int channel=i;
                Bind("Settings/Minus"+i,()=>{ stepVolume(channel,-.1f); RefreshVolume(); });
                Bind("Settings/Plus"+i,()=>{ stepVolume(channel,.1f); RefreshVolume(); });
            }
            _mirrorControls=(RectTransform)_ui.Find("Mirror/Controls");
            _bubbles=_mirrorControls.Find("Bubbles").GetComponent<CanvasGroup>();
            _abilityBubble=(RectTransform)_bubbles.transform.Find("Ability");
            _instrumentBubble=(RectTransform)_bubbles.transform.Find("Instrument");
            for(int i=0;i<_portraits.Length;i++)
                _portraits[i]=Resources.Load<Texture2D>("Backstage/Portraits/Performer"+(i+1));
            Bind("Mirror/Controls/Prev",()=>Cycle(-1));
            Bind("Mirror/Controls/Next",()=>Cycle(1));
            Bind("Mirror/Controls/Back",LeaveMirror);
            Bind("Score/Back",()=>EnterRoom(false));
            Bind("Score/Archive",()=>{ Turn(Page.Archive); archive(); });
            for(int i=0;i<3;i++)
            {
                int track=i;
                Bind("Score/Train"+i,()=>{
                    if (_progress.Train(track)) MetaSave.Save(_progress);
                    RefreshScore();
                });
            }
            Hide();
        }

        void Bind(string path,Action callback)
        {
            _ui.Find(path).GetComponent<Button>().onClick.AddListener(()=>{
                SfxBank.Instance?.PlayUiClick(); callback();
            });
        }

        void Write(string path,string text) => _ui.Find(path).GetComponent<Text>().text=text;

        void Turn(Page page)
        {
            _page=page;
            foreach(Transform child in _ui) child.gameObject.SetActive(child.name==page.ToString());
            GameRoot.Instance?.SyncCursorToState();
        }

        public void Hide()
        {
            Turn(Page.Hidden);
            _world.gameObject.SetActive(false);
        }

        public void OpenTitle()
        {
            _world.Title();
            Turn(Page.Title);
        }

        void EnterRoom(bool reset)
        {
            _world.Room(reset);
            _world.SetReflection(_portraits[_progress.Performer]);
            Turn(Page.Walk);
        }

        void RefreshVolume()
        {
            float[] values={AudioMix.Master,AudioMix.Sfx,AudioMix.Music,AudioMix.Ui};
            for(int i=0;i<values.Length;i++) Write("Settings/Volume"+i,$"{Volumes[i]}   {values[i]:P0}");
        }

        void Cycle(int delta)
        {
            _performer=(_performer+delta+Names.Length)%Names.Length;
            var gun=StartingInstruments[_performer];
            Write("Mirror/Controls/Performer",Names[_performer]);
            Write("Mirror/Controls/Bubbles/Instrument/Name",HudV2View.InstrumentName(gun,gun));
            _instrumentBubble.Find("Icon").GetComponent<Image>().sprite=UiAssets.Named("hud_gun_"+gun);
            _world.SetReflection(_portraits[_performer]);
            _bubbleStart=Time.unscaledTime;
            AnimateBubbles();
        }

        void AnimateBubbles()
        {
            _world.FrameMirror();
            var canvas=_ui.GetComponentInParent<Canvas>();
            float scale=canvas.pixelRect.height/(400f*_world.MirrorHalfHeight*canvas.scaleFactor);
            _mirrorControls.localScale=Vector3.one*scale;
            float elapsed=Time.unscaledTime-_bubbleStart;
            float appear=Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/.24f));
            _bubbles.alpha=.28f+.72f*appear;
            float drift=Mathf.Sin(elapsed*2)*3;
            _abilityBubble.anchoredPosition=new Vector2(-371,143+drift-(1-appear)*13);
            _instrumentBubble.anchoredPosition=new Vector2(-371,-75-drift-(1-appear)*17);
        }

        void LeaveMirror()
        {
            if (_progress.Performer!=_performer)
            {
                _progress.Performer=_performer;
                MetaSave.Save(_progress);
            }
            EnterRoom(false);
        }

        public void OpenScore() { Turn(Page.Score); RefreshScore(); }

        void RefreshScore()
        {
            Write("Score/Balance",$"乐章碎片   {_progress.Shard}");
            for(int i=0;i<3;i++)
            {
                int rank=_progress.Training[i], price=_progress.TrainingCost(i);
                bool full=rank>=MetaProgress.MaxTraining;
                string cost=full?"已满级":price+" 枚碎片";
                string path="Score/Train"+i;
                Write(path+"/Label",$"{Lessons[i]}      {rank} / {MetaProgress.MaxTraining}      {cost}");
                _ui.Find(path).GetComponent<Button>().interactable=!full && _progress.Shard>=price;
            }
        }

        public void UseStation(BackstageWorld.Station station)
        {
            if (!Walking) return;
            if(station==BackstageWorld.Station.Door) _start(StartingInstruments[_progress.Performer]);
            if(station==BackstageWorld.Station.Score) OpenScore();
            if(station==BackstageWorld.Station.Mirror)
            {
                _performer=_progress.Performer;
                Turn(Page.Mirror); Cycle(0);
            }
        }

        void Update()
        {
            if(_page==Page.Hidden || _page==Page.Archive) return;
            var kb=Keyboard.current;
            if(_page==Page.Title && Mouse.current!=null)
            {
                Vector2 p=Mouse.current.position.ReadValue();
                _world.Tilt(new Vector2(p.x/Mathf.Max(1,Screen.width)*2-1,p.y/Mathf.Max(1,Screen.height)*2-1));
            }
            if(_page==Page.Mirror) AnimateBubbles();
            if(kb==null) return;
            if(kb.escapeKey.wasPressedThisFrame)
            {
                if(_page==Page.Mirror) LeaveMirror();
                else if(_page==Page.Score) EnterRoom(false);
                else if(_page!=Page.Title) OpenTitle();
                return;
            }
            if(!Walking) return;
            var station=_world.Walk();
            string hint=station switch {
                BackstageWorld.Station.Door=>"F   推门进入关卡",
                BackstageWorld.Station.Score=>"F   翻开养成乐谱",
                BackstageWorld.Station.Mirror=>"F   选择演奏者",
                _=>"WASD 移动   ·   鼠标环顾   ·   Esc 返回"
            };
            Write("Walk/Prompt",hint);
            if(kb.fKey.wasPressedThisFrame) UseStation(station);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
    }
}

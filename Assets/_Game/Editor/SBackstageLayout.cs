using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SBackstageLayout
    {
        const string Path = "Assets/_Game/Resources/Backstage/FrontDesk.prefab";
        static readonly Color Cream = new Color(.98f,.92f,.80f);
        static readonly Color Brass = new Color(.77f,.60f,.34f);
        static readonly Color Ink = new Color(.055f,.09f,.13f,.98f);

        static SBackstageLayout() => EditorApplication.delayCall += Ensure;
        internal static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (!prefab || !prefab.transform.Find("Mirror/Controls")) Build();
        }

        static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size)
        {
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=anchor;
            rect.anchoredPosition=pos;
            rect.sizeDelta=size;
            return rect;
        }

        static RectTransform Page(Transform parent,string name)
        {
            var r=Rect(parent,name,Vector2.zero,Vector2.zero,Vector2.zero);
            r.anchorMax=Vector2.one;
            r.offsetMin=r.offsetMax=Vector2.zero;
            return r;
        }

        static Text Label(Transform parent,string name,string value,float x,float y,int size,int width=520)
        {
            var t=Rect(parent,name,Vector2.one*.5f,new Vector2(x,y),new Vector2(width,size*1.8f)).gameObject.AddComponent<Text>();
            t.font=AssetDatabase.LoadAssetAtPath<Font>(S.View.UiKit.FontAssetPath);
            t.fontSize=size; t.text=value; t.color=Cream;
            t.alignment=TextAnchor.MiddleCenter; t.raycastTarget=false;
            return t;
        }

        static Button Action(Transform parent,string name,string caption,float x,float y,int width=300)
        {
            var frame=Rect(parent,name,Vector2.one*.5f,new Vector2(x,y),new Vector2(width,58));
            var edge=frame.gameObject.AddComponent<Image>(); edge.color=Brass;
            var plate=Page(frame,"Plate");
            plate.offsetMin=Vector2.one*1.5f; plate.offsetMax=-plate.offsetMin;
            var fill=plate.gameObject.AddComponent<Image>(); fill.color=Color.white;
            var button=frame.gameObject.AddComponent<Button>(); button.targetGraphic=fill;
            var tint=button.colors;
            tint.normalColor=new Color(.14f,.18f,.22f);
            tint.highlightedColor=new Color(.36f,.25f,.19f);
            tint.selectedColor=tint.highlightedColor;
            tint.pressedColor=new Color(.51f,.32f,.20f);
            tint.disabledColor=new Color(.095f,.105f,.12f);
            button.colors=tint;
            Label(frame,"Label",caption,0,0,22,width-8);
            return button;
        }

        static RectTransform Bubble(Transform parent,string name,float y)
        {
            var rect=Rect(parent,name,Vector2.one*.5f,new Vector2(-371,y),new Vector2(154,154));
            var art=rect.gameObject.AddComponent<RawImage>();
            art.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Resources/Backstage/Portraits/Bubble.png");
            art.raycastTarget=false;
            return rect;
        }

        [MenuItem("S/界面/重建后台菜单")]
        public static void Build()
        {
            var root=new GameObject("FrontDesk",typeof(RectTransform));
            try
            {
                var title=Page(root.transform,"Title");
                var bg=Page(title,"Backdrop").gameObject.AddComponent<RawImage>();
                bg.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Resources/Backstage/Overture.png");
                bg.raycastTarget=false;
                Label(title,"Heading","P R O J E C T     S",0,308,60,900);
                Rect(title,"Emblem",Vector2.one*.5f,new Vector2(0,122),new Vector2(225,225)).gameObject.AddComponent<RawImage>().raycastTarget=false;
                Action(title,"StartBtn","开始游戏",0,-42,250);
                Action(title,"Settings","设置",0,-124,250);
                Action(title,"Quit","退出游戏",0,-206,250);

                var settings=Page(root.transform,"Settings");
                settings.gameObject.AddComponent<Image>().color=Ink;
                Label(settings,"Heading","声音设置",0,240,42);
                string[] channels={"主音量","音效","音乐","界面音"};
                for(int i=0;i<channels.Length;i++)
                {
                    float y=127-i*75;
                    Label(settings,"Volume"+i,channels[i],0,y,25);
                    Action(settings,"Minus"+i,"−",-220,y,65);
                    Action(settings,"Plus"+i,"+",220,y,65);
                }
                Action(settings,"Back","返回",0,-228);
                var walk=Page(root.transform,"Walk");
                Label(walk,"Reticle","·",0,0,24);
                var prompt=Label(walk,"Prompt","",0,0,22,980);
                var pr=prompt.rectTransform;pr.anchorMin=pr.anchorMax=new Vector2(.5f,0);
                pr.anchoredPosition=new Vector2(0,75);
                prompt.gameObject.AddComponent<Shadow>().effectDistance=new Vector2(2,-2);
                var mirror=Page(root.transform,"Mirror");
                var controls=Rect(mirror,"Controls",Vector2.one*.5f,Vector2.zero,new Vector2(960,770));
                Action(controls,"Prev","‹",-236,0,66);
                Action(controls,"Next","›",236,0,66);
                var name=Label(controls,"Performer","",0,-276,23,240);
                name.gameObject.AddComponent<Shadow>().effectDistance=new Vector2(1,-2);
                Action(controls,"Back","确定",0,-346,136);
                var bubbles=Page(controls,"Bubbles");
                bubbles.gameObject.AddComponent<CanvasGroup>().blocksRaycasts=false;
                var ability=Bubble(bubbles,"Ability",143);
                Label(ability,"Heading","角色能力",0,17,18,128).color=Brass;
                Label(ability,"Name","暂无专属能力",0,-12,17,134);
                var instrument=Bubble(bubbles,"Instrument",-75);
                var icon=Rect(instrument,"Icon",Vector2.one*.5f,new Vector2(0,9),new Vector2(83,83)).gameObject.AddComponent<Image>();
                icon.preserveAspect=true; icon.raycastTarget=false;
                Label(instrument,"Name","",0,-40,18,124);

                var score=Page(root.transform,"Score");
                score.gameObject.AddComponent<Image>().color=Ink;
                Label(score,"Heading","排 练 乐 谱",0,287,40);
                Label(score,"Balance","",0,205,23);
                for(int i=0;i<3;i++) Action(score,"Train"+i,"",0,104-i*80,610);
                Label(score,"RewardHint","每存活 1 分钟或提升 1 级获得 1 枚碎片，通关另得 1 枚。",0,-144,18,850);
                Action(score,"Archive","曲库解锁",-160,-236,285);Action(score,"Back","合上乐谱",160,-236,285);
                PrefabUtility.SaveAsPrefabAsset(root,Path);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

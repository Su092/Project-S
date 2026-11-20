using UnityEngine;
using UnityEngine.UI;
namespace S.View
{
    /// <summary>Fixed-device score layout using approved artwork.</summary>
    public static class ScoreUiLayout
    {
        public static readonly Vector2 TL=new(0,1), C=new(.5f,.5f);
        public static RectTransform Node(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent,false);r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=pos;r.sizeDelta=size;return r;
        }
        public static Text Text(string name,Transform parent,Font font,string text,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,int fontSize=20)
        {
            var t=Node(name,parent,anchor,pivot,pos,size).gameObject.AddComponent<Text>();
            t.font=font;t.text=text;t.fontSize=fontSize;t.color=new Color(.18f,.18f,.22f);t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
        }
        public static Image Image(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,Color color,Sprite sprite=null)
        {
            var im=Node(name,parent,anchor,pivot,pos,size).gameObject.AddComponent<Image>();
            im.color=color;im.sprite=sprite;im.raycastTarget=false;return im;
        }
        public static Button Button(string name,Transform p,Font font,string caption,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
        {
            var im=Image(name,p,anchor,pivot,pos,size,new Color(.12f,.16f,.23f,.95f));im.raycastTarget=true;
            var b=im.gameObject.AddComponent<Button>();b.targetGraphic=im;b.navigation=new Navigation{mode=Navigation.Mode.None};
            Text("Label",im.transform,font,caption,C,C,Vector2.zero,size-Vector2.one*6,20).color=new Color(.96f,.91f,.78f);
            return b;
        }
        public static void ApplyPreviewRail(Image image,Sprite sprite)
        {
            image.sprite=sprite;image.type=UnityEngine.UI.Image.Type.Sliced;image.preserveAspect=false;
            image.color=sprite?new Color(1,1,1,.9f):new Color(.06f,.09f,.14f,.72f);
            image.pixelsPerUnitMultiplier=sprite?Mathf.Max(.1f,sprite.rect.height/80f):1;
            image.raycastTarget=false;
        }
        public static void ApplyControlArt(InvRefs refs,Sprite sort,Sprite close)
        {
            void Glyph(Button button,Sprite sprite,Vector2 pos,Vector2 size)
            {
                var existing=button.transform.Find("Glyph");
                var im=existing?existing.GetComponent<Image>():Image("Glyph",button.transform,C,C,pos,size,Color.white);
                im.sprite=sprite;im.preserveAspect=true;im.raycastTarget=false;
                im.rectTransform.anchoredPosition=pos;im.rectTransform.sizeDelta=size;
                // Preserve the approved ink outline at small on-screen icon sizes.
                var outline=im.GetComponent<Outline>();
                if(!outline)outline=im.gameObject.AddComponent<Outline>();
                outline.effectColor=new Color(.157f,.204f,.278f,1);
                outline.effectDistance=new Vector2(.75f,-.75f);outline.useGraphicAlpha=true;
                var hitArea=button.GetComponent<Image>();
                hitArea.sprite=null;hitArea.color=Color.clear;hitArea.raycastTarget=true;
                button.targetGraphic=im;
            }
            Glyph(refs.CloseButton,close,Vector2.zero,new Vector2(30,30));
            refs.CloseButton.transform.Find("Label").gameObject.SetActive(false);
            refs.SortButton.GetComponent<RectTransform>().sizeDelta=new Vector2(48,48);
            refs.SortButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(-142,18);
            Glyph(refs.SortButton,sort,Vector2.zero,new Vector2(34,34));
            var label=refs.SortButton.transform.Find("Label").GetComponent<Text>();
            label.color=new Color(.157f,.204f,.278f);
            label.text="";label.gameObject.SetActive(false);
            label.rectTransform.sizeDelta=new Vector2(120,40);
        }
        public static GameObject Build(Font font, Sprite pageSprite=null, Sprite sortSprite=null, Sprite closeSprite=null)
        {
            var root=Node("ScoreInventory",null,C,C,Vector2.zero,Vector2.zero);
            root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;
            var refs=root.gameObject.AddComponent<InvRefs>();refs.Root=root.gameObject;
            // No scene-light changes; only a click-catching UI scrim behind the manuscript.
            var scrim=Image("Scrim",root,C,C,Vector2.zero,Vector2.zero,new Color(0,0,0,.12f));
            scrim.rectTransform.anchorMin=Vector2.zero;scrim.rectTransform.anchorMax=Vector2.one;scrim.raycastTarget=true;
            var page=Image("ScorePage",root,C,C,Vector2.zero,Vector2.zero,pageSprite?Color.white:new Color(.94f,.92f,.85f),pageSprite);
            page.rectTransform.anchorMin=new Vector2(.18f,.035f);page.rectTransform.anchorMax=new Vector2(.82f,.77f);
            page.rectTransform.offsetMin=page.rectTransform.offsetMax=Vector2.zero;page.raycastTarget=true;
            refs.BagDropPanel=page.gameObject.AddComponent<SlotDropTarget>();refs.BagDropPanel.IsBagArea=true;
            Text("Title",page.transform,font,"曲谱",new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-12),new Vector2(300,44),30);
            refs.CloseButton=Button("Close",page.transform,font,"×",new Vector2(1,1),new Vector2(1,1),new Vector2(-14,-12),new Vector2(48,48));
            refs.SortButton=Button("Sort",page.transform,font,"≡  整理  R",new Vector2(1,0),new Vector2(1,0),new Vector2(-22,18),new Vector2(170,46));
            refs.Hint=Text("Hint",page.transform,font,"",new Vector2(0,0),new Vector2(0,0),new Vector2(22,12),new Vector2(870,54),17);
            refs.Hint.alignment=TextAnchor.MiddleLeft;
            refs.Hint.gameObject.SetActive(false);
            var viewport=Image("Viewport",page.transform,C,C,Vector2.zero,Vector2.zero,new Color(1,1,1,0));
            viewport.raycastTarget=true;viewport.rectTransform.anchorMin=new Vector2(0,0);viewport.rectTransform.anchorMax=Vector2.one;
            viewport.rectTransform.offsetMin=new Vector2(28,80);viewport.rectTransform.offsetMax=new Vector2(-28,-70);
            viewport.gameObject.AddComponent<RectMask2D>();
            refs.BagGrid=Node("Notes",viewport.transform,new Vector2(0,1),new Vector2(0,1),Vector2.zero,new Vector2(1100,720));
            refs.BagGrid.anchorMax=new Vector2(1,1);refs.BagGrid.sizeDelta=new Vector2(0,720);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport.rectTransform;scroll.content=refs.BagGrid;
            scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;
            // Both equipment rows live in the upper-left HUD area, not inside the page.
            refs.ChainArea=Node("EquipmentRows",root,TL,TL,new Vector2(20,-20),new Vector2(1100,230));
            ApplyControlArt(refs,sortSprite,closeSprite);
            return root.gameObject;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using S.Core;
namespace S.View
{
    public sealed class MusicOfferPresentation : MonoBehaviour
    {
        public const float EnterDuration=1.3f/1.4f;
        public float LeaveDuration => EnterDuration*(staff?staff.ExitToEntryDistanceRatio:1f)/1.3f;
        public enum Stage { Hidden, Entering, Expanding, Ready, Closing, Leaving }
        public Stage Phase {get; private set;}
        public bool CanChoose => Phase==Stage.Ready;
        public bool Busy => Phase!=Stage.Hidden && !CanChoose;
        public float Progress {get; private set;}
        public float FrameAlpha => cards.Count>0 ? cards[0].group.alpha : 0;
        public int CleanIconCount => cards.Count;
        public Vector2 LastLanding {get;private set;}
        sealed class Card
        {
            public RectTransform rt,icon;
            public CanvasGroup group,content;
            public OfferCardRefs refs;
        }
        readonly List<Card> cards=new();
        readonly List<Image> notes=new();
        readonly List<Image> noteGlows=new();
        RectTransform area,ribbon,iconLayer;
        MusicStaffGraphic staff,glow;
        RectTransform target;
        float elapsed,clock;
        int selected=-1;
        Action complete;
        static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        static RectTransform Node(string name,Transform parent)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);return r;
        }
        static CanvasGroup Group(GameObject go){var g=go.GetComponent<CanvasGroup>();return g?g:go.AddComponent<CanvasGroup>();}
        static void TextPlace(Text t,Transform parent,Vector2 position,Vector2 size,int font,Color color)
        {
            if(!t)return;t.transform.SetParent(parent,false);var r=t.rectTransform;
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;
            t.fontSize=font;t.color=color;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;
            t.resizeTextForBestFit=true;t.resizeTextMinSize=13;t.resizeTextMaxSize=font;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;
        }
        public static string FrameFor(Offer offer,SData data)
        {
            if(offer.IsGun)return "green";
            return ItemFrameFor(data.MakeItem(offer.Id));
        }
        // Shared by reward cards and the equipped accessory HUD.
        public static string ItemFrameFor(ChainItem item)
        {
            if(item!=null&&!item.IsBullet)return "red";
            return item!=null&&(item.Rarity==Rarity.Rare||item.Rarity==Rarity.Epic)?"gold":"blue";
        }
        public void Prepare(OfferRefs refs,List<GameObject> objects,List<Offer> offers,SData data,RectTransform landing)
        {
            ResetPresentation();
            area=refs.CardArea;target=landing;
            var layout=area.GetComponent<HorizontalLayoutGroup>();if(layout)layout.enabled=false;
            area.anchorMin=Vector2.zero;area.anchorMax=Vector2.one;area.offsetMin=area.offsetMax=Vector2.zero;
            var bg=transform.Find("BG")?.GetComponent<Image>();
            if(bg){bg.sprite=null;bg.color=new Color(.025f,.035f,.065f,.72f);}
            if(refs.Title){refs.Title.text="选择一段新的旋律";refs.Title.color=new Color(1,.91f,.65f);
                var tr=refs.Title.rectTransform;tr.anchorMin=tr.anchorMax=new Vector2(.5f,1);tr.anchoredPosition=new Vector2(0,-80);}
            ribbon=Node("MusicRibbon",area);ribbon.SetAsFirstSibling();
            var gr=Node("IndependentGlow",ribbon);glow=gr.gameObject.AddComponent<MusicStaffGraphic>();
            glow.Sprite=UiAssets.Named("offer_staff_glow");glow.material=SRes.Get<Material>("mat_ui_additive");glow.color=new Color(1,1,1,.85f);glow.raycastTarget=false;
            var sr=Node("FiveLineCore",ribbon);staff=sr.gameObject.AddComponent<MusicStaffGraphic>();
            staff.Sprite=UiAssets.Named("offer_staff");staff.raycastTarget=false;
            var noteLayer=Node("ForegroundNotes",ribbon);noteLayer.SetAsLastSibling();
            for(int i=0;i<12;i++){
                var halo=Node("NoteGlow_"+i,noteLayer).gameObject.AddComponent<Image>();
                halo.sprite=UiAssets.Named("offer_note_glow_"+i%6);halo.preserveAspect=true;halo.raycastTarget=false;
                halo.material=SRes.Get<Material>("mat_ui_additive");noteGlows.Add(halo);
                var nr=Node("FloatingNote_"+i,noteLayer);var im=nr.gameObject.AddComponent<Image>();
                im.sprite=UiAssets.Named("offer_note_"+i%6);im.preserveAspect=true;im.raycastTarget=false;notes.Add(im);
            }
            iconLayer=Node("TravelIcons",area);iconLayer.SetAsLastSibling();
            for(int i=0;i<objects.Count;i++)
            {
                var c=objects[i].GetComponent<OfferCardRefs>();if(!c)continue;
                var card=new Card{rt=(RectTransform)c.transform,refs=c,group=Group(c.gameObject)};
                card.rt.anchorMin=card.rt.anchorMax=card.rt.pivot=new Vector2(.5f,.5f);
                card.group.interactable=false;card.group.blocksRaycasts=false;
                if(c.Inner)c.Inner.gameObject.SetActive(false);
                if(c.TagTop)c.TagTop.gameObject.SetActive(false);
                var rule=c.transform.Find("IconRule");if(rule)rule.gameObject.SetActive(false);
                string key=FrameFor(offers[i],data);
                c.Background.sprite=UiAssets.Named("offer_frame_"+key);c.Background.color=Color.white;c.Background.material=null;
                c.Background.type=Image.Type.Sliced;c.Background.pixelsPerUnitMultiplier=1;
                c.Button.targetGraphic=c.Background;
                var cb=ColorBlock.defaultColorBlock;cb.highlightedColor=new Color(1,.96f,.82f);cb.pressedColor=new Color(.9f,.83f,.65f);cb.fadeDuration=.14f;c.Button.colors=cb;
                var content=Node("InformationContent",c.transform);card.content=Group(content.gameObject);
                foreach(var t in new[]{c.CardTitle,c.RarityName,c.Desc,c.PickHint})if(t)t.transform.SetParent(content,false);
                if(offers[i].IsGun&&c.CardTitle)c.CardTitle.text=HudV2View.InstrumentName(offers[i].Id,data.Guns[offers[i].Id].Name);
                if(c.RarityName)c.RarityName.text=offers[i].IsGun?"乐器":key=="red"?"衍生配件":"子弹";
                if(c.PickHint)c.PickHint.text="点击选择";
                if(c.IconNode){
                    var n=c.IconNode;
                    if(n.Ring)n.Ring.enabled=false;if(n.ScopeMark)n.ScopeMark.enabled=false;if(n.Badge)n.Badge.gameObject.SetActive(false);
                    foreach(var graphic in n.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
                    card.icon=(RectTransform)n.transform;card.icon.SetParent(iconLayer,false);
                    card.icon.anchorMin=card.icon.anchorMax=card.icon.pivot=new Vector2(.5f,.5f);
                    var sprite=UiAssets.Named(offers[i].IsGun?"hud_gun_"+offers[i].Id:"offer_item_"+offers[i].Id);
                    if(sprite)n.Icon.sprite=sprite;
                    n.Icon.rectTransform.anchorMin=Vector2.zero;n.Icon.rectTransform.anchorMax=Vector2.one;n.Icon.rectTransform.offsetMin=n.Icon.rectTransform.offsetMax=Vector2.zero;
                    n.Icon.material=null;n.Icon.color=Color.white;n.Icon.type=Image.Type.Simple;n.Icon.preserveAspect=true;
                }
                cards.Add(card);
            }
            Canvas.ForceUpdateCanvases();Go(Stage.Entering);Draw();
        }
        void Go(Stage stage){Phase=stage;elapsed=0;Progress=0;Draw();}
        public void Choose(int index,Action done)
        {
            if(!CanChoose)return;
            selected=index;complete=done;Go(Stage.Closing);Draw();
        }
        public void ResetPresentation()
        {
            complete=null;Phase=Stage.Hidden;selected=-1;
            if(ribbon){ribbon.gameObject.SetActive(false);Destroy(ribbon.gameObject);}
            if(iconLayer){iconLayer.gameObject.SetActive(false);Destroy(iconLayer.gameObject);}
            ribbon=null;iconLayer=null;cards.Clear();notes.Clear();noteGlows.Clear();
        }
        void OnDisable(){complete=null;Phase=Stage.Hidden;}
        void Update()
        {
            if(Phase==Stage.Hidden)return;
            clock+=Time.unscaledDeltaTime;elapsed+=Time.unscaledDeltaTime;
            float duration=Phase switch{Stage.Entering=>EnterDuration,Stage.Expanding=>.6f,Stage.Closing=>.4f,Stage.Leaving=>LeaveDuration,_=>float.PositiveInfinity};
            Progress=Mathf.Clamp01(elapsed/duration);Draw();
            if(Progress<1)return;
            switch(Phase){
                case Stage.Entering:Go(Stage.Expanding);break;
                case Stage.Expanding:Go(Stage.Ready);break;
                case Stage.Closing:Go(Stage.Leaving);break;
                case Stage.Leaving:
                    var done=complete;complete=null;Phase=Stage.Hidden;done?.Invoke();break;
            }
        }
        public void RefreshLayout()=>Draw();
        public Vector2 LandingPosition()
        {
            if(!target||!area)return new Vector2(-area.rect.width*.4f,area.rect.height*.4f);
            var tc=target.GetComponentInParent<Canvas>();
            var ac=area.GetComponentInParent<Canvas>();
            Vector2 screen=RectTransformUtility.WorldToScreenPoint(tc.renderMode==RenderMode.ScreenSpaceOverlay?null:tc.worldCamera,target.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area,screen,ac.renderMode==RenderMode.ScreenSpaceOverlay?null:ac.worldCamera,out var local);
            return local;
        }
        void Draw()
        {
            if(!area||!staff)return;
            float w=area.rect.width,h=area.rect.height;if(w<1||h<1)return;
            float cw=Mathf.Min(280,w*.195f),ch=Mathf.Min(460,h*.53f),baseY=-h*.11f,centerY=h*.025f;
            var bg=transform.Find("BG")?.GetComponent<Image>();
            float curtain=Phase==Stage.Entering?Ease(Progress*2):Phase==Stage.Leaving?1-Ease(Progress):1;
            if(bg)bg.color=new Color(.025f,.035f,.065f,.78f*curtain);
            var title=GetComponent<OfferRefs>().Title;if(title)title.color=new Color(1,.91f,.65f,curtain);
            ribbon.anchoredPosition=new Vector2(0,baseY);
            float rw=w*1.12f,rh=rw/MusicOfferArt.Aspect*.45f;
            foreach(var g in new[]{staff,glow}){
                g.rectTransform.sizeDelta=new Vector2(rw,rh);g.Clock=clock;
                g.ViewSize=new Vector2(w,h);g.FlightProgress=Progress;
                g.FlightMode=Phase==Stage.Entering?1:Phase==Stage.Leaving?-1:0;
                g.SetVerticesDirty();
            }
            float expand=Phase==Stage.Ready?1:Phase==Stage.Expanding?Ease(Progress):Phase==Stage.Closing?1-Ease(Progress):0;
            for(int i=0;i<notes.Count;i++){
                float u=(i+.5f)/notes.Count;
                float floatWeight=Phase==Stage.Ready?1:Phase==Stage.Expanding?Ease(Progress):Phase==Stage.Closing?1-Ease(Progress):0;
                notes[i].rectTransform.anchoredPosition=staff.Point(u)+staff.Normal(u)*floatWeight*(Mathf.Sin(clock*1.1f+i*2.1f)*17+(i%2==0?30:-30));
                notes[i].rectTransform.sizeDelta=Vector2.one*(i%3==0?24:17)*1.5f;
                notes[i].rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Sin(clock+i)*12+staff.Bank(u)*.65f);
                notes[i].color=new Color(1,1,1,.8f+.15f*Mathf.Sin(clock*.7f+i));
                var halo=noteGlows[i];var nr=notes[i].rectTransform;
                halo.rectTransform.anchoredPosition=nr.anchoredPosition;halo.rectTransform.sizeDelta=nr.sizeDelta;
                halo.rectTransform.localRotation=nr.localRotation;halo.color=new Color(1,1,1,notes[i].color.a*.9f);
            }
            for(int i=0;i<cards.Count;i++){
                var c=cards[i];float x=(i-(cards.Count-1)*.5f)*w*.245f;
                float u=staff.MaterialAtX(x);
                var onRail=ribbon.anchoredPosition+staff.Point(u);
                var raised=new Vector2(x,centerY+ch*.255f);
                c.rt.sizeDelta=new Vector2(cw,ch);
                c.rt.anchoredPosition=Vector2.Lerp(new Vector2(x,baseY),new Vector2(x,centerY),expand);
                c.rt.localScale=new Vector3(Mathf.Lerp(.35f,1,expand),Mathf.Lerp(.025f,1,expand),1);
                c.group.alpha=expand;c.group.interactable=c.group.blocksRaycasts=CanChoose;
                c.content.alpha=Ease((expand-.25f)/.75f);
                float textW=cw*.75f;
                TextPlace(c.refs.CardTitle,c.content.transform,new Vector2(0,ch*.075f),new Vector2(textW,ch*.1f),23,new Color(.14f,.17f,.22f));
                TextPlace(c.refs.RarityName,c.content.transform,new Vector2(0,-ch*.015f),new Vector2(textW,24),14,new Color(.38f,.34f,.26f));
                TextPlace(c.refs.Desc,c.content.transform,new Vector2(0,-ch*.18f),new Vector2(textW,ch*.25f),18,new Color(.18f,.21f,.26f));
                TextPlace(c.refs.PickHint,c.content.transform,new Vector2(0,-ch*.36f),new Vector2(textW,24),15,new Color(.3f,.29f,.25f));
                if(!c.icon)continue;
                c.icon.anchoredPosition=Vector2.Lerp(onRail,raised,expand);
                c.icon.sizeDelta=Vector2.one*Mathf.Lerp(68,92,expand);
                c.icon.localScale=Vector3.one;
                c.icon.localRotation=Quaternion.Euler(0,0,staff.Bank(u)*(1-expand)*.65f);
                c.refs.IconNode.Icon.color=Color.Lerp(Color.white,new Color(.37f,.28f,.11f),expand*.65f);
                if(Phase==Stage.Leaving&&i==selected){
                    float t=Ease(Progress);var start=ribbon.anchoredPosition+staff.RestPoint(u);
                    c.icon.localRotation=Quaternion.identity;
                    var end=LandingPosition();LastLanding=end;
                    var a=start+new Vector2(-w*.08f,h*.28f);var b=end+new Vector2(w*.14f,-h*.06f);
                    c.icon.anchoredPosition=(1-t)*(1-t)*(1-t)*start+3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t*end;
                    c.icon.localScale=Vector3.one*Mathf.Lerp(1,.32f,t);
                    c.refs.IconNode.Icon.color=new Color(1,1,1,1-Ease((t-.9f)/.1f));
                }
            }
        }
    }
}

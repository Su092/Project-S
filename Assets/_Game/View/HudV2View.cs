using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>Bindings and value presentation for the authored HUD V2 prefab.</summary>
    public sealed class HudV2View : MonoBehaviour
    {
        public RectTransform WeaponRail;
        public Image CurrentWeapon, SpareWeapon, SwapGlyph, ReloadSpinner;
        public Button CurrentWeaponButton, SpareWeaponButton, PauseButton, ResumeButton;
        public GameObject SpareRoot, PauseOverlay;
        public Image[] SlotBases, SlotIcons, SlotReadRings;
        public Text[] SlotLabels;
        public Text WeaponName, SpareName, InputHint;
        public HudMinimapView Minimap;
        public Image ScoreIcon;
        public RectTransform ScoreRail;
        public Image[] ScorePreviewIcons, ScorePreviewBorders;
        public Text ScoreCount;
        GameRoot _root;
        HudRefs _refs;

        public void Init(GameRoot root, HudRefs refs)
        {
            _root = root; _refs = refs;
            ConfigureAccessorySlots(this, UiAssets.Named("hud_map_mask"));
            ConfigureScorePreview(UiKit.Font,UiAssets.Named("score_preview_rail"));
            ScoreIcon.sprite=UiAssets.Named("score_bag_icon");
            ConfigureInstrumentFrames(UiAssets.Named("hud_music_badge_mask"),UiAssets.Named("hud_portrait_frame"));
            // Scale the artwork after layout so the live spread and reload rotation still apply.
            var reticleScale = new Vector3(1.3f, 1.3f, 1f);
            refs.Crosshair.rectTransform.localScale = reticleScale;
            ReloadSpinner.rectTransform.localScale = reticleScale;
            CurrentWeaponButton.onClick.AddListener(() => { if (CanUse()) root.ToggleAttachmentPreview(); });
            SpareWeaponButton.onClick.AddListener(() => { if (CanUse()) root.Weapons.SwitchTo(1-root.Weapons.ActiveIndex); });
            PauseButton.onClick.AddListener(root.TogglePause);
            ResumeButton.onClick.AddListener(root.TogglePause);
            Minimap.Init(root);
            gameObject.SetActive(root.RunStarted);
        }

        public void ConfigureInstrumentFrames(Sprite background,Sprite frameSprite)
        {
            void Apply(Button button,Image icon)
            {
                var backing=button.GetComponent<Image>();
                backing.sprite=background;backing.color=new Color(.12f,.16f,.23f,1);
                backing.enabled=true;backing.preserveAspect=true;backing.raycastTarget=true;
                var existing=button.transform.Find("Frame");
                var frame=existing?existing.GetComponent<Image>():ScoreUiLayout.Image("Frame",button.transform,
                    ScoreUiLayout.C,ScoreUiLayout.C,Vector2.zero,button.GetComponent<RectTransform>().sizeDelta,Color.white);
                frame.sprite=frameSprite;frame.color=Color.white;frame.enabled=true;
                frame.gameObject.SetActive(true);frame.preserveAspect=true;frame.raycastTarget=false;
                frame.rectTransform.anchorMin=frame.rectTransform.anchorMax=frame.rectTransform.pivot=ScoreUiLayout.C;
                frame.rectTransform.anchoredPosition=Vector2.zero;
                frame.rectTransform.sizeDelta=button.GetComponent<RectTransform>().sizeDelta;
                frame.transform.SetAsFirstSibling();icon.transform.SetAsLastSibling();
                // Input availability affects the icon only; its backing and frame remain visible.
                button.targetGraphic=icon;
                var colors=button.colors;colors.disabledColor=Color.white;button.colors=colors;
                backing.canvasRenderer.SetColor(Color.white);frame.canvasRenderer.SetColor(Color.white);
            }
            Apply(CurrentWeaponButton,CurrentWeapon);
            Apply(SpareWeaponButton,SpareWeapon);
        }

        public static void ConfigureAccessorySlots(HudV2View view, Sprite circleMask)
        {
            for (int i=0;i<view.SlotBases.Length;i++)
            {
                // Retain the existing layout bindings, remove the socket graphic.
                view.SlotBases[i].sprite=null;
                view.SlotBases[i].enabled=false;
                var icon=view.SlotIcons[i];
                var parent=view.SlotBases[i].transform;
                var clip=parent.Find("IconClip") as RectTransform;
                if(!clip)
                {
                    clip=new GameObject("IconClip",typeof(RectTransform)).GetComponent<RectTransform>();
                    clip.SetParent(parent,false);
                    clip.anchorMin=clip.anchorMax=clip.pivot=new Vector2(.5f,.5f);
                    clip.sizeDelta=new Vector2(56,56);
                    var graphic=clip.gameObject.AddComponent<Image>();
                    graphic.sprite=circleMask;graphic.raycastTarget=false;
                    clip.gameObject.AddComponent<Mask>().showMaskGraphic=false;
                    icon.transform.SetParent(clip,false);
                    icon.rectTransform.anchoredPosition=Vector2.zero;
                }
                clip.SetAsFirstSibling();
                view.SlotReadRings[i].enabled=true;
            }
        }

        public void ConfigureScorePreview(Font font, Sprite railSprite=null)
        {
            var tr=new Vector2(1,1);
            if(!ScoreIcon)
            {
                ScoreIcon=ScoreUiLayout.Image("ScoreBagIcon",transform,ScoreUiLayout.TL,ScoreUiLayout.TL,
                    new Vector2(20,-22),new Vector2(68,60),Color.white);
                ScoreIcon.preserveAspect=true;
                ScoreCount=ScoreUiLayout.Text("Count",ScoreIcon.transform,font,"曲谱",new Vector2(.5f,0),new Vector2(.5f,1),
                    Vector2.zero,new Vector2(100,20),13);ScoreCount.color=Color.white;
            }
            ScoreIcon.rectTransform.anchoredPosition=new Vector2(20,-22);ScoreIcon.rectTransform.sizeDelta=new Vector2(68,60);
            if(!ScoreRail)
                ScoreRail=ScoreUiLayout.Image("ScorePreview",transform,ScoreUiLayout.TL,ScoreUiLayout.TL,
                    new Vector2(100,-8),new Vector2(1140,80),new Color(.06f,.09f,.14f,.72f)).rectTransform;
            ScoreRail.anchoredPosition=new Vector2(100,-8);ScoreRail.sizeDelta=new Vector2(1140,80);
            ScoreUiLayout.ApplyPreviewRail(ScoreRail.GetComponent<Image>(),railSprite);
            if(ScorePreviewIcons==null||ScorePreviewIcons.Length!=15||ScoreRail.GetComponentInChildren<PlainSlotRing>(true)!=null)
            {
                for(int i=ScoreRail.childCount-1;i>=0;i--)
                {
                    var child=ScoreRail.GetChild(i).gameObject;child.SetActive(false);
                    if(Application.isPlaying)Destroy(child);else DestroyImmediate(child);
                }
                ScorePreviewIcons=new Image[15];ScorePreviewBorders=new Image[15];
                for(int i=0;i<15;i++)
                {
                    var border=ScoreUiLayout.Image("Slot_"+i,ScoreRail,ScoreUiLayout.TL,ScoreUiLayout.TL,
                        new Vector2(9+i*75,-6),new Vector2(68,68),Color.white);
                    ScorePreviewBorders[i]=border;
                    var mask=ScoreUiLayout.Image("Mask",border.transform,ScoreUiLayout.C,ScoreUiLayout.C,Vector2.zero,new Vector2(56,56),Color.white);
                    mask.gameObject.AddComponent<Mask>().showMaskGraphic=false;
                    ScorePreviewIcons[i]=ScoreUiLayout.Image("Icon",mask.transform,ScoreUiLayout.C,ScoreUiLayout.C,Vector2.zero,new Vector2(62,62),Color.white);
                }
            }
            for(int i=0;i<ScorePreviewBorders.Length;i++)
            {
                var border=ScorePreviewBorders[i];
                border.rectTransform.anchoredPosition=new Vector2(9+i*75,-6);
                border.rectTransform.sizeDelta=new Vector2(68,68);
                border.preserveAspect=true;
                ScorePreviewIcons[i].transform.parent.GetComponent<RectTransform>().sizeDelta=new Vector2(56,56);
                ScorePreviewIcons[i].rectTransform.sizeDelta=new Vector2(62,62);
            }
            ScoreCount.gameObject.SetActive(false);
            InputHint.gameObject.SetActive(false);
            WeaponName.gameObject.SetActive(false);
            SpareName.gameObject.SetActive(false);
            CurrentWeaponButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(16,-110);
            CurrentWeaponButton.transform.localScale=Vector3.one*.65f;
            WeaponRail.anchoredPosition=new Vector2(100,-106);
            WeaponRail.sizeDelta=new Vector2(15+8*75,80);
            for(int i=0;i<SlotBases.Length;i++)
            {
                SlotBases[i].rectTransform.anchoredPosition=new Vector2(9+i*75,-6);
                SlotBases[i].rectTransform.localScale=Vector3.one;
                SlotBases[i].rectTransform.sizeDelta=new Vector2(68,68);
                SlotReadRings[i].rectTransform.sizeDelta=new Vector2(68,68);
                SlotLabels[i].fontSize=12;
            }
            ScoreUiLayout.ApplyPreviewRail(WeaponRail.GetComponent<Image>(),railSprite);
            WeaponName.rectTransform.anchoredPosition=new Vector2(100,-190);WeaponName.fontSize=13;
            SwapGlyph.rectTransform.anchoredPosition=new Vector2(40,-224);SwapGlyph.rectTransform.sizeDelta=new Vector2(18,14);
            SpareRoot.GetComponent<RectTransform>().anchoredPosition=new Vector2(26,-248);SpareRoot.transform.localScale=Vector3.one*.7f;
            var map=Minimap.GetComponent<RectTransform>();map.anchorMin=map.anchorMax=map.pivot=tr;
            map.anchoredPosition=new Vector2(-24,-128);map.localScale=Vector3.one*.82f;
            var refs=GetComponent<HudRefs>();
            var timer=refs.Timer.transform.parent.GetComponent<RectTransform>();timer.anchorMin=timer.anchorMax=timer.pivot=tr;
            timer.anchoredPosition=new Vector2(-112,-30);timer.sizeDelta=new Vector2(160,58);
            timer.GetComponent<Image>().sprite=null;timer.GetComponent<Image>().enabled=false;
            refs.Timer.fontSize=34;
            var label=refs.TimerLabel.rectTransform;label.anchorMin=label.anchorMax=label.pivot=tr;
            label.anchoredPosition=new Vector2(-112,-87);label.sizeDelta=new Vector2(160,24);refs.TimerLabel.fontSize=13;refs.TimerLabel.gameObject.SetActive(false);
        }

        bool CanUse() => _root != null && _root.RunStarted && !_root.RunEnded &&
            !_root.Paused && !_root.InventoryOpen && _root.HudPointerHeld;

        public void Refresh(GunRuntime gun)
        {
            if (_root == null) return;
            bool visible = _root.RunStarted && (_root.Menu == null || !_root.Menu.IsOpen);
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            _refs.Timer.text=Countdown(_root.RunElapsed);
            if (!visible) return;
            PauseOverlay.SetActive(_root.Paused && !_root.InventoryOpen && !_root.RunEnded);
            PauseButton.interactable = !_root.RunEnded && !_root.InventoryOpen;
            CurrentWeaponButton.interactable = CanUse();
            SpareWeaponButton.interactable = CanUse() && !_root.Weapons.IsSwitching;
            if (gun == null) return;
            CurrentWeapon.sprite = UiAssets.Named("hud_gun_" + gun.Base.Id);
            WeaponName.text = InstrumentName(gun.Base.Id, gun.Base.Name);
            bool hasSpare = _root.Weapons.GunCount > 1;
            SpareRoot.SetActive(hasSpare); SwapGlyph.gameObject.SetActive(hasSpare);
            if (hasSpare)
            {
                var spare = _root.Weapons.AllGuns[1-_root.Weapons.ActiveIndex];
                SpareWeapon.sprite = UiAssets.Named("hud_gun_" + spare.Base.Id);
                SpareName.text = InstrumentName(spare.Base.Id, spare.Base.Name);
            }
            bool editing=_root.GetComponent<InventoryView>()?.IsBagVisible==true;
            bool expanded=_root.AttachmentPreviewExpanded;
            ScoreIcon.gameObject.SetActive(!editing);
            ScoreRail.gameObject.SetActive(expanded&&!editing);
            CurrentWeaponButton.gameObject.SetActive(!editing);
            WeaponName.gameObject.SetActive(false);
            SpareRoot.SetActive(hasSpare&&!editing);SwapGlyph.gameObject.SetActive(hasSpare&&!editing);
            WeaponRail.gameObject.SetActive(expanded&&!editing);
            for(int i=0;i<ScorePreviewIcons.Length;i++)
            {
                var item=i<_root.Run.Bag.Count?_root.Run.Bag[i]:null;
                ScorePreviewBorders[i].enabled=true;
                ScorePreviewBorders[i].sprite=UiAssets.Named(item==null?"score_slot_empty":QualityFrame(item));
                ScorePreviewIcons[i].transform.parent.GetComponent<Image>().sprite=UiAssets.Named("hud_map_mask");
                ScorePreviewIcons[i].enabled=item!=null;
                if(item!=null)ScorePreviewIcons[i].sprite=UiAssets.ItemIcon(item.Id);
            }
            int count = gun.Slots.Length;
            float step=75;
            WeaponRail.sizeDelta = new Vector2(15 + count * step, 80);
            for (int i=0;i<SlotBases.Length;i++)
            {
                SlotBases[i].rectTransform.anchoredPosition=new Vector2(9+i*step,-6);
                SlotBases[i].rectTransform.localScale=Vector3.one;
                SlotBases[i].rectTransform.sizeDelta=new Vector2(68,68);
                SlotReadRings[i].rectTransform.sizeDelta=new Vector2(68,68);
                bool show = i<count;
                SlotBases[i].gameObject.SetActive(show);
                if (!show) continue;
                var item=gun.Slots.Cells[i];
                SlotIcons[i].enabled=item!=null;
                if (item!=null) SlotIcons[i].sprite=UiAssets.ItemIcon(item.Id);
                // One persistent type/quality border per slot; progress selection is intentionally deferred.
                SlotReadRings[i].sprite=UiAssets.Named(item==null ? "hud_attachment_read_ring" : QualityFrame(item));
                SlotReadRings[i].color=Color.white;
                SlotReadRings[i].type=Image.Type.Simple;
                SlotReadRings[i].preserveAspect=true;
                SlotReadRings[i].enabled=true;
                SlotLabels[i].fontSize=12;
                SlotLabels[i].text=item==null?(i+1).ToString():"";
                SlotLabels[i].gameObject.SetActive(item==null);
            }
            _refs.LevelText.text="Lv. "+_root.Run.Level;
            _refs.HpFill.color=Color.white;
            _refs.XpFill.color=Color.white;
            _refs.Timer.text=Countdown(_root.RunElapsed);
            _refs.Timer.color=_root.RunElapsed>=570 ? new Color(1,.45f,.3f) : new Color(.97f,.90f,.76f);
            _refs.Crosshair.enabled=!gun.Reloading;
            ReloadSpinner.gameObject.SetActive(gun.Reloading);
            if (gun.Reloading && !_root.Paused) ReloadSpinner.rectTransform.Rotate(0,0,-240*Time.deltaTime);
            else if (!gun.Reloading) ReloadSpinner.rectTransform.localRotation=Quaternion.identity;
            // Filled Image already accounts for sprite padding; 1 must reveal the entire arc.
            _refs.AmmoFill.enabled=gun.MagBarFill>0;
            _refs.AmmoFill.fillAmount=Mathf.Clamp01(gun.MagBarFill);
            // Art already carries the cyan energy colour; keep low-ammo text warning from HudView.
            _refs.AmmoFill.color=Color.white;
            if (gun.Reloading) _refs.AmmoText.text=$"调律 {gun.MagBarFill*100f:0}%";
            Minimap.Refresh();
        }

        // HUD presentation only. Configuration IDs, weapon rules and other screens are unchanged.
        public static string InstrumentName(string id, string fallback) => id switch
        {
            "rifle" => "小提琴", "smg" => "拇指琴", "shotgun" => "手鼓",
            "sniper" => "大提琴", "beam" => "竖琴", "launcher" => "手风琴",
            _ => fallback
        };

        public static string QualityFrame(ChainItem item) => "hud_quality_" + MusicOfferPresentation.ItemFrameFor(item);

        public static string Countdown(float elapsed)
        {
            int seconds=Mathf.CeilToInt(Mathf.Max(0,600-elapsed));
            return (seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
        }
    }
}

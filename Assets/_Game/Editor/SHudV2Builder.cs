using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SHudV2Builder
    {
        const string Request="Logs/hud-v2-build.request";
        static readonly Vector2 TL=new(0,1), TR=new(1,1), BL=new(0,0), BR=new(1,0), C=new(.5f,.5f), TC=new(.5f,1);
        static SHudV2Builder(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(!File.Exists(Request)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            File.Delete(Request);
            try{Build();}catch(Exception e){File.WriteAllText("Logs/hud-v2-build-error.log",e.ToString());Debug.LogException(e);}
        }
        static RectTransform Node(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
        {
            var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=anchor;rt.pivot=pivot;rt.anchoredPosition=pos;rt.sizeDelta=size;return rt;
        }
        static Sprite Sp(string name)
        {
            var path=UiAssets.DirOf(name)+name+".png";
            var sp=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if(!sp)throw new InvalidOperationException("Missing sprite "+path);
            return sp;
        }
        static Image Pic(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,string sprite,bool sliced=false)
        {
            var rt=Node(name,parent,anchor,pivot,pos,size);var img=rt.gameObject.AddComponent<Image>();
            if(sprite!=null)img.sprite=Sp(sprite);
            img.type=sliced?Image.Type.Sliced:Image.Type.Simple;img.raycastTarget=false;return img;
        }
        static Text Label(string name,Transform p,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,string text,int fontSize=18)
        {
            var t=Node(name,p,anchor,pivot,pos,size).gameObject.AddComponent<Text>();
            t.font=AssetDatabase.LoadAssetAtPath<Font>(UiKit.FontAssetPath);
            t.fontSize=fontSize;t.text=text;t.alignment=TextAnchor.MiddleCenter;t.color=new Color(.97f,.90f,.76f);t.raycastTarget=false;
            t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;return t;
        }
        static Button Button(Image image,bool opaqueBacking=false)
        {
            image.raycastTarget=true;var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;
            var c=b.colors;c.highlightedColor=new Color(1,.88f,.65f);c.pressedColor=new Color(.7f,.8f,.9f);if(opaqueBacking)c.disabledColor=Color.white;b.colors=c;
            b.navigation=new Navigation{mode=Navigation.Mode.None};return b;
        }
        static Image Bar(string name,Transform p,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,string frame,string fill)
        {
            var bg=Pic(name,p,anchor,pivot,pos,size,frame,true);
            // Source-pixel slices preserve the complete end ornaments at a uniform vertical scale.
            float ppu=bg.sprite.rect.height/size.y;
            bg.pixelsPerUnitMultiplier=ppu;
            Vector4 inset=frame switch
            {
                "hud_hp_track_frame" => new Vector4(84,15,84,14),
                "hud_xp_track_frame" => new Vector4(61,13,61,14),
                "hud_boss_track_frame" => new Vector4(93,14,93,14),
                _ => throw new InvalidOperationException("Missing bar content bounds: "+frame)
            };
            inset/=ppu;
            var posFill=new Vector2((inset.x-inset.z)*.5f,(inset.y-inset.w)*.5f);
            var sizeFill=new Vector2(size.x-inset.x-inset.z,size.y-inset.y-inset.w);
            var f=Pic("Fill",bg.transform,C,C,posFill,sizeFill,fill);
            f.type=Image.Type.Filled;f.fillMethod=Image.FillMethod.Horizontal;f.fillOrigin=0;return f;
        }
        public static string BuildHud()
        {
            var rt=Node("HUD",null,C,C,Vector2.zero,new Vector2(1920,1080));
            rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.sizeDelta=Vector2.zero;
            var r=rt.gameObject.AddComponent<HudRefs>();var v=rt.gameObject.AddComponent<HudV2View>();
            r.RotationPips=Array.Empty<Image>();r.RotationLabels=Array.Empty<Text>();r.ChainIcons=Array.Empty<ItemIconView>();
            // Weapons and physical attachment slots.
            var rail=Pic("WeaponRail",rt,TL,TL,new Vector2(20,-20),new Vector2(772,90),"hud_weapon_rail",true);
            v.WeaponRail=rail.rectTransform;
            var weapon=Pic("CurrentWeapon",rt,TL,TL,new Vector2(16,-12),new Vector2(110,110),"hud_music_badge_mask");
            weapon.color=new Color(.12f,.16f,.23f,1f);
            Pic("Frame",weapon.transform,C,C,Vector2.zero,new Vector2(110,110),"hud_portrait_frame");
            v.CurrentWeaponButton=Button(weapon,true);
            v.CurrentWeapon=Pic("Icon",weapon.transform,C,C,Vector2.zero,new Vector2(88,64),"hud_gun_rifle");
            v.CurrentWeapon.preserveAspect=true;
            v.WeaponName=Label("WeaponName",rt,TL,TL,new Vector2(138,-112),new Vector2(200,24),"小提琴",16);
            v.WeaponName.alignment=TextAnchor.MiddleLeft;
            v.SlotBases=new Image[12];v.SlotIcons=new Image[12];v.SlotReadRings=new Image[12];v.SlotLabels=new Text[12];
            for(int i=0;i<12;i++)
            {
                var slot=Pic("Slot_"+i,rail.transform,TL,TL,new Vector2(116+i*76,-11),new Vector2(68,68),null);slot.enabled=false;
                v.SlotBases[i]=slot;
                v.SlotIcons[i]=Pic("Item",slot.transform,C,C,Vector2.zero,new Vector2(62,62),null);v.SlotIcons[i].enabled=false;
                v.SlotReadRings[i]=Pic("ReadRing",slot.transform,C,C,Vector2.zero,new Vector2(70,70),"hud_attachment_read_ring");
                v.SlotLabels[i]=Label("SlotNumber",slot.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,2),new Vector2(66,18),(i+1).ToString(),13);
            }
            v.SwapGlyph=Pic("Swap",rt,TL,TL,new Vector2(54,-128),new Vector2(24,20),"hud_weapon_swap");
            var spare=Pic("SpareWeapon",rt,TL,TL,new Vector2(28,-156),new Vector2(78,78),"hud_music_badge_mask");
            spare.color=new Color(.12f,.16f,.23f,1f);
            Pic("Frame",spare.transform,C,C,Vector2.zero,new Vector2(78,78),"hud_portrait_frame");
            v.SpareRoot=spare.gameObject;v.SpareWeaponButton=Button(spare,true);
            v.SpareWeapon=Pic("Icon",spare.transform,C,C,Vector2.zero,new Vector2(62,48),"hud_gun_smg");v.SpareWeapon.preserveAspect=true;
            v.SpareName=Label("SpareName",spare.transform,new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-3),new Vector2(130,22),"",13);
            // Minimap: stencil mask, real footprints, prefab templates for pooled live markers.
            var map=Node("Minimap",rt,TL,TL,new Vector2(145,-146),new Vector2(232,232));
            v.Minimap=map.gameObject.AddComponent<HudMinimapView>();
            var mask=Pic("CircularMask",map,C,C,Vector2.zero,new Vector2(226,226),"hud_map_mask");
            mask.color=new Color(.12f,.16f,.23f,.94f);mask.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var obstacle=Node("Obstacles",mask.transform,C,C,Vector2.zero,new Vector2(226,226));
            v.Minimap.Obstacles=obstacle.gameObject.AddComponent<HudObstacleGraphic>();
            v.Minimap.Obstacles.color=new Color(.35f,.44f,.51f,.92f);
            var markers=Node("Markers",mask.transform,C,C,Vector2.zero,new Vector2(226,226));v.Minimap.MarkerRoot=markers;
            v.Minimap.EnemyTemplate=Pic("EnemyTemplate",markers,C,C,Vector2.zero,new Vector2(9,9),"hud_map_enemy");v.Minimap.EnemyTemplate.gameObject.SetActive(false);
            v.Minimap.ChestTemplate=Pic("ChestTemplate",markers,C,C,Vector2.zero,new Vector2(17,15),"hud_map_chest");v.Minimap.ChestTemplate.gameObject.SetActive(false);
            v.Minimap.PlayerArrow=Pic("PlayerHeading",markers,C,C,Vector2.zero,new Vector2(20,24),"hud_map_player_heading");
            // User's full 376x420 cutout: anchor on the measured circle, not the art bounding box.
            // Keep the entire north ornament and the original aspect ratio.
            const float mapSourceW=376, mapSourceH=420, circleX=186.703f, circleY=232.431f, circleRadius=180.691f;
            float mapArtScale=113.5f/circleRadius;
            var bezel=Pic("Bezel",map,C,new Vector2(circleX/mapSourceW,1-circleY/mapSourceH),Vector2.zero,
                new Vector2(mapSourceW,mapSourceH)*mapArtScale,"hud_minimap_bezel");
            bezel.preserveAspect=true;
            // North sits below the complete diamond, inside the map, clear of the weapon rail.
            Label("North",map,TC,new Vector2(.5f,1),new Vector2(0,-24),new Vector2(24,22),"N",16);
            // Countdown and Boss (above countdown).
            var timer=Pic("TimerPlate",rt,TC,TC,new Vector2(0,-176),new Vector2(174,60),"hud_timer_plate",true);
            r.Timer=Label("Timer",timer.transform,C,C,Vector2.zero,new Vector2(154,48),"10:00",40);
            r.TimerLabel=Label("TimerLabel",rt,TC,TC,new Vector2(0,-239),new Vector2(240,26),"生存倒计时",16);
            r.BossFill=Bar("BossBar",rt,TC,TC,new Vector2(0,-132),new Vector2(530,28),"hud_boss_track_frame","hud_music_boss_fill");
            r.BossBarRoot=r.BossFill.transform.parent.gameObject;
            r.BossNameText=Label("BossName",rt,TC,TC,new Vector2(0,-108),new Vector2(700,22),"",16);
            r.BossBarRoot.SetActive(false);
            // Reticle, clockwise rotating reload indicator, right-hand ammo arc.
            r.Crosshair=Pic("Crosshair",rt,C,C,Vector2.zero,new Vector2(34,34),"hud_music_crosshair");
            r.Crosshair.preserveAspect=true;
            v.ReloadSpinner=Pic("ReloadSpinner",rt,C,C,Vector2.zero,new Vector2(38,38),"hud_reload_spinner");v.ReloadSpinner.gameObject.SetActive(false);
            Pic("AmmoTrack",rt,C,C,Vector2.zero,new Vector2(216,216),"hud_ammo_arc_track");
            r.AmmoFill=Pic("AmmoFill",rt,C,C,Vector2.zero,new Vector2(216,216),"hud_ammo_arc_fill");
            r.AmmoFill.type=Image.Type.Filled;r.AmmoFill.fillMethod=Image.FillMethod.Vertical;r.AmmoFill.fillOrigin=0;
            r.AmmoText=Label("AmmoText",rt,C,new Vector2(0,.5f),new Vector2(120,0),new Vector2(150,34),"30 / 30",22);
            r.AmmoText.alignment=TextAnchor.MiddleLeft;
            // Player state.
            var portrait=Pic("Portrait",rt,BL,BL,new Vector2(20,38),new Vector2(110,110),"hud_operator_portrait");
            // A circular mask prevents the source portrait's rectangular shoulders from leaking.
            var portraitMask=Pic("PortraitMask",rt,BL,BL,new Vector2(20,38),new Vector2(110,110),"hud_music_badge_mask");
            portraitMask.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            portrait.transform.SetParent(portraitMask.transform,false);
            portrait.rectTransform.anchorMin=portrait.rectTransform.anchorMax=C;portrait.rectTransform.pivot=C;portrait.rectTransform.anchoredPosition=Vector2.zero;
            Pic("PortraitFrame",rt,BL,BL,new Vector2(20,38),new Vector2(110,110),"hud_portrait_frame");
            r.HpFill=Bar("Health",rt,BL,BL,new Vector2(128,91),new Vector2(470,28),"hud_hp_track_frame","hud_music_hp_fill");
            r.XpFill=Bar("Experience",rt,BL,BL,new Vector2(128,65),new Vector2(430,22),"hud_xp_track_frame","hud_music_xp_fill");
            r.HpText=Label("HealthNumber",rt,BL,BL,new Vector2(135,120),new Vector2(240,26),"100 / 100",18);r.HpText.alignment=TextAnchor.MiddleLeft;
            r.LevelText=Label("Level",rt,BL,BL,new Vector2(20,12),new Vector2(110,24),"Lv. 1",20);
            v.InputHint=Label("InputHint",rt,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,18),new Vector2(640,24),"",16);
            // Visual placeholders only: no buttons or inventory actions.
            for(int i=0;i<3;i++)
            {
                var item=Pic("UtilityPlaceholder_"+i,rt,BR,BR,new Vector2(-30-(2-i)*122,36),new Vector2(116,116),"hud_item_slot");
                var symbol=Pic("Inactive",item.transform,C,C,Vector2.zero,new Vector2(30,30),"hud_utility_placeholder");symbol.color=new Color(.55f,.62f,.66f);
            }
            var pause=Pic("Pause",rt,TR,TR,new Vector2(-24,-24),new Vector2(72,72),"hud_pause_button_base");
            v.PauseButton=Button(pause);Pic("Glyph",pause.transform,C,C,Vector2.zero,new Vector2(26,30),"hud_pause_glyph");
            // Feedback and overlays remain separate from the combat layout.
            r.DamageVignette=Pic("DamageVignette",rt,C,C,Vector2.zero,new Vector2(1920,1080),"vignette_damage");
            r.DamageVignette.rectTransform.anchorMin=Vector2.zero;r.DamageVignette.rectTransform.anchorMax=Vector2.one;r.DamageVignette.rectTransform.sizeDelta=Vector2.zero;r.DamageVignette.color=new Color(1f,1f,1f,0f);
            r.Toast=Label("Toast",rt,TC,TC,new Vector2(0,-288),new Vector2(1000,38),"",22);
            var overlay=Pic("PauseOverlay",rt,C,C,Vector2.zero,new Vector2(1920,1080),null);
            overlay.rectTransform.anchorMin=Vector2.zero;overlay.rectTransform.anchorMax=Vector2.one;overlay.rectTransform.sizeDelta=Vector2.zero;
            overlay.color=new Color(.02f,.03f,.04f,.7f);overlay.raycastTarget=true;v.PauseOverlay=overlay.gameObject;
            Label("Title",overlay.transform,C,C,new Vector2(0,65),new Vector2(500,60),"已暂停",40);
            var resume=Pic("Resume",overlay.transform,C,C,new Vector2(0,-10),new Vector2(280,64),"hud_music_resume",true);
            v.ResumeButton=Button(resume);Label("Label",resume.transform,C,C,Vector2.zero,new Vector2(250,50),"继续游戏 · Esc",22);
            overlay.gameObject.SetActive(false);
            r.BannerPlate=Pic("BannerPlate",rt,C,C,Vector2.zero,new Vector2(760,230),"hud_music_banner",true);r.BannerPlate.gameObject.SetActive(false);
            r.CenterBig=Label("CenterBig",rt,C,C,Vector2.zero,new Vector2(730,210),"",40);
            HudV2View.ConfigureAccessorySlots(v,Sp("hud_map_mask"));
            v.ConfigureScorePreview(AssetDatabase.LoadAssetAtPath<Font>(UiKit.FontAssetPath),Sp("score_preview_rail"));
            v.ScoreIcon.sprite=Sp("score_bag_icon");
            v.ConfigureInstrumentFrames(Sp("hud_music_badge_mask"),Sp("hud_portrait_frame"));
            string path=UiPrefab.AssetDir+"HUD.prefab";
            PrefabUtility.SaveAsPrefabAsset(rt.gameObject,path);UnityEngine.Object.DestroyImmediate(rt.gameObject);
            return path;
        }
        public static void ApplySingleAccessoryBorder()
        {
            string path=UiPrefab.AssetDir+"HUD.prefab";
            var go=PrefabUtility.LoadPrefabContents(path);
            try
            {
                HudV2View.ConfigureAccessorySlots(go.GetComponent<HudV2View>(),Sp("hud_map_mask"));
                PrefabUtility.SaveAsPrefabAsset(go,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }
        }
        [MenuItem("S/UI/Build HUD V2 Only")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var p in Directory.GetFiles(SRes.Root+"Texture/UI","*.png",SearchOption.AllDirectories))
                AssetDatabase.ImportAsset(p.Replace('\\','/'),ImportAssetOptions.ForceUpdate);
            BuildHud();SAddressablesSetup.Build();AssetDatabase.SaveAssets();
            var hud=AssetDatabase.LoadAssetAtPath<GameObject>(UiPrefab.AssetDir+"HUD.prefab");
            var v=hud.GetComponent<HudV2View>();
            if(!v||v.SlotBases.Length!=12||!v.Minimap||!v.ReloadSpinner)throw new InvalidOperationException("HUD V2 bindings missing.");
            File.WriteAllText("Logs/hud-v2-build.log","RESULT HUD V2 prefab and classified sprites ready\n");
        }
    }
}

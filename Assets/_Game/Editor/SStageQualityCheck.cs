using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using S.View;
using S.Core;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SStageQualityCheck
    {
        const string Request="Logs/stage-quality.request";
        static int phase=SessionState.GetInt("SStageQualityPhase",0);
        static double started;
        static float at;
        static MusicSpotlight spot, secondSpot;
        static Light[] stageLights;static float[] normalIntensities;
        static readonly System.Collections.Generic.List<string> log=new();
        static SStageQualityCheck(){EditorApplication.update+=Tick;}
        static void Check(string name,bool ok){log.Add((ok?"PASS ":"FAIL ")+name);File.WriteAllLines("Logs/stage-quality-check.log",log);if(!ok)throw new Exception(name);}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>150)throw new TimeoutException("stage "+phase);
                if(phase==0)
                {
                    if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    SHudV2Builder.ApplySingleAccessoryBorder();
                    SAddressablesSetup.Build();AssetDatabase.SaveAssets();
                    Check("three attachment sprites import",new[]{"blue","gold","red"}.All(k=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/Res/Texture/UI/Frame/hud_quality_"+k+".png")));
                    phase=1;SessionState.SetInt("SStageQualityPhase",phase);EditorApplication.isPlaying=true;return;
                }
                var r=GameRoot.Instance;if(!r||r.Run==null||r.Menu==null)return;
                if(phase==1)
                {
                    r.InvulnerableForTest=true;r.AutoAcceptOffers=true;r.Player.AiControlled=true;
                    r.Menu.StartOrChoose();r.Menu.SelectGun("rifle");r.Menu.StartOrChoose();
                    r.Player.enabled=false;r.Weapons.enabled=false;
                    var map=r.Zones.Map;
                    Check("normal lighting without a spotlight",RenderSettings.ambientLight.maxColorComponent>.6f && r.Zones.Map.FocusDimming==0 && Shader.GetGlobalFloat("_MusicStageDim")==0);
                    typeof(GameRoot).GetField("_spawnTimer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(r,10000f);
                    var lights=map.GetComponentsInChildren<Light>();var values=lights.Select(l=>l.intensity).ToArray();stageLights=lights;normalIntensities=values;
                    map.ApplyEnvironment();Check("environment setup is idempotent",lights.Select((l,i)=>Mathf.Approximately(l.intensity,values[i])).All(x=>x));
                    var hud=UnityEngine.Object.FindAnyObjectByType<HudV2View>(FindObjectsInactive.Include);
                    foreach(Rarity rarity in Enum.GetValues(typeof(Rarity)))
                    {
                        // Test both item types independently, including uncommon combinations.
                        var bullet=new ChainItem { Bullet=new BulletDef { Id="std", Rarity=rarity } };
                        r.Weapons.Active.Slots.Cells[0]=bullet;hud.Refresh(r.Weapons.Active);
                        string expected=(rarity==Rarity.Rare||rarity==Rarity.Epic)?"gold":"blue";
                        Check(rarity+" bullet uses "+expected,hud.SlotReadRings[0].sprite&&hud.SlotReadRings[0].sprite.name=="hud_quality_"+expected);
                        var modifier=new ChainItem { Mod=new ModifierDef { Id="dmg", Rarity=rarity } };
                        r.Weapons.Active.Slots.Cells[0]=modifier;hud.Refresh(r.Weapons.Active);
                        Check(rarity+" derivative stays red",hud.SlotReadRings[0].sprite&&hud.SlotReadRings[0].sprite.name=="hud_quality_red");
                    }
                    foreach(var item in r.Data.BulletOrder.Select(d=>r.Data.MakeItem(d.Id)).Concat(r.Data.ModifierOrder.Select(d=>r.Data.MakeItem(d.Id))))
                    {
                        string expected=!item.IsBullet?"red":item.Rarity==Rarity.Rare||item.Rarity==Rarity.Epic?"gold":"blue";
                        Check(item.Id+" HUD/offer share agreed color",HudV2View.QualityFrame(item)=="hud_quality_"+expected && MusicOfferPresentation.ItemFrameFor(item)==expected);
                    }
                    r.Weapons.Active.Slots.Cells[0]=null;hud.Refresh(r.Weapons.Active);
                    Check("empty reading ring uses replaced asset",hud.SlotReadRings[0].sprite.name=="hud_attachment_read_ring");
                    Check("socket graphics removed",hud.SlotBases.All(x=>!x.enabled&&!x.sprite));
                    Check("icons retain authored 62px size",hud.SlotIcons.All(x=>x.rectTransform.sizeDelta==new Vector2(62,62)));
                    Check("all slot borders stay visible without read-progress selection",hud.SlotReadRings.Where((x,i)=>hud.SlotBases[i].gameObject.activeSelf).All(x=>x.enabled));
                    Check("baked icon rims clipped in HUD only",hud.SlotIcons.All(x=>x.transform.parent.name=="IconClip"&&x.transform.parent.GetComponent<UnityEngine.UI.Mask>()&&!x.transform.parent.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic));
                    var cells=r.Weapons.Active.Slots.Cells;
                    cells[0]=new ChainItem { Bullet=new BulletDef { Id="std", Rarity=Rarity.Common } };
                    cells[1]=new ChainItem { Bullet=new BulletDef { Id="std", Rarity=Rarity.Rare } };
                    cells[2]=new ChainItem { Mod=new ModifierDef { Id="dmg", Rarity=Rarity.Common } };
                    hud.Refresh(r.Weapons.Active);
                    Camera.main.transform.position=new Vector3(0,3,-14);Camera.main.transform.LookAt(new Vector3(0,2.5f,-4));
                    SFoundryMapBuilder.Capture(Camera.main,"Shots/stage-lighting-normal.png");
                    var actor=r.SpawnMusicElite(new Vector3(0,0,-4),"S08_MetronomeGuard_Rehearsal");actor.enabled=false;
                    spot=actor.MusicBrain.Spotlight;
                    Check("spot starts dark before dim cue",spot.GetComponentInChildren<Light>().intensity==0);
                    secondSpot=MusicSpotlight.Create(actor,actor.transform);
                    at=Time.time;phase=2;SessionState.SetInt("SStageQualityPhase",phase);return;
                }
                if(phase==2&&Time.time-at>2)
                {
                    Check("spot is bright after fade",spot.GetComponentInChildren<Light>().intensity>30);
                    Check("active spotlight dims environment",r.Zones.Map.FocusDimming>.99f && RenderSettings.ambientLight.maxColorComponent<=.21f && Shader.GetGlobalFloat("_MusicStageDim")>.99f);
                    Check("authored lights dim to ten percent",stageLights.Select((l,i)=>Mathf.Abs(l.intensity-normalIntensities[i]*.1f)<.001f).All(x=>x));
                    SFoundryMapBuilder.Capture(Camera.main,"Shots/stage-quality-spotlight.png");
                    SHudV2PlayCheck.Capture(r,"Shots/hud-single-border.png",1920,1080);
                    UnityEngine.Object.Destroy(secondSpot.gameObject);
                    at=Time.time;phase=3;return;
                }
                if(phase==3&&Time.time-at>.8f)
                {
                    Check("remaining spotlight keeps scene dim",r.Zones.Map.FocusDimming>.99f);
                    spot.gameObject.SetActive(false);
                    at=Time.time;phase=4;return;
                }
                if(phase==4&&Time.time-at>1)
                {
                    Check("last spotlight disabled restores lighting",r.Zones.Map.FocusDimming==0 && Shader.GetGlobalFloat("_MusicStageDim")==0 && RenderSettings.ambientLight.maxColorComponent>.6f);
                    Check("authored light intensities restored exactly",stageLights.Select((l,i)=>Mathf.Abs(l.intensity-normalIntensities[i])<.001f).All(x=>x));
                    SFoundryMapBuilder.Capture(Camera.main,"Shots/stage-lighting-restored.png");
                    spot.gameObject.SetActive(true);
                    at=Time.time;phase=5;return;
                }
                if(phase==5&&Time.time-at>1)
                {
                    Check("reenabled spotlight dims again",r.Zones.Map.FocusDimming>.99f);
                    spot.Target=null;
                    at=Time.time;phase=6;return;
                }
                if(phase==6&&Time.time-at>1.2f)
                {
                    Check("lost target closes spotlight and restores scene",!spot && r.Zones.Map.FocusDimming==0 && Shader.GetGlobalFloat("_MusicStageDim")==0);
                    log.Add("RESULT stage quality passed");File.WriteAllLines("Logs/stage-quality-check.log",log);
                    Finish();
                }
            } catch(Exception e){log.Add("FAIL "+e);File.WriteAllLines("Logs/stage-quality-check.log",log);Finish();}
        }
        static void Finish(){File.Delete(Request);EditorApplication.isPlaying=false;phase=0;SessionState.SetInt("SStageQualityPhase",0);started=0;}
    }
}

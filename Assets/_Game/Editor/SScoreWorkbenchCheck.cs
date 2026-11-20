using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using S.View;
using S.Core;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SScoreWorkbenchCheck
    {
        const string Request="Logs/score-workbench-check.request";
        static int phase=SessionState.GetInt("ScoreCheckPhase",0);
        static double started,at;
        static readonly List<string> log=new();
        static Keyboard keyboard;
        static bool driving;
        static SScoreWorkbenchCheck(){EditorApplication.update+=Tick;}
        static void C(string name,bool ok)
        {
            log.Add((ok?"PASS ":"FAIL ")+name);File.WriteAllLines("Logs/score-workbench-check.log",log);
            if(!ok)throw new Exception(name);
        }
        static void Key(Key key)
        { InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));InputSystem.Update(); }
        static void Press(GameRoot root,Key key,bool inventory=false)
        {
            if(keyboard==null)keyboard=InputSystem.AddDevice<Keyboard>();
            Key(key);
            object owner=inventory?(object)root.GetComponent<InventoryView>():root;
            owner.GetType().GetMethod(inventory?"Update":"HandlePauseInput",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(owner,null);
            Release();
        }
        static void Release(){InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();}
        static DraggableItem Card(InventoryView inv,ChainItem item)=>inv.GetComponentsInChildren<DraggableItem>().First(d=>ReferenceEquals(d.Item,item));
        static void Click(InventoryView inv,ChainItem item,PointerEventData.InputButton button)
        {Card(inv,item).OnPointerClick(new PointerEventData(EventSystem.current){button=button});}
        static void MovePlayer(GameRoot r,Vector3 p)
        {var cc=r.Player.GetComponent<CharacterController>();cc.enabled=false;r.Player.transform.position=p;cc.enabled=true;Physics.SyncTransforms();}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(phase==0){EditorApplication.isPaused=false;Step();return;}
            var root=GameRoot.Instance;
            if(EditorApplication.isPlaying&&root&&!driving){driving=true;root.StartCoroutine(Drive());}
        }
        static System.Collections.IEnumerator Drive()
        {
            // EditorApplication.update sees an editor fallback Screen size (640x480).
            // Run UI raycasts in the player loop, where Screen and Canvas agree.
            yield return null;
            try{while(File.Exists(Request)&&EditorApplication.isPlaying){Step();yield return null;}}
            finally{driving=false;}
        }
        static void Step()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>180)throw new TimeoutException("phase "+phase);
                if(phase==0)
                {
                    if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
                    phase=1;SessionState.SetInt("ScoreCheckPhase",phase);EditorApplication.isPlaying=true;return;
                }
                var r=GameRoot.Instance;if(!r||r.Run==null||!r.Menu)return;
                var inv=r.GetComponent<InventoryView>();var hud=r.GetComponentInChildren<HudV2View>(true);
                if(phase==1)
                {
                    r.InvulnerableForTest=true;r.AutoAcceptOffers=true;r.Menu.StartForTest();
                    r.Player.enabled=false;r.Weapons.enabled=false;
                    typeof(GameRoot).GetField("_spawnTimer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(r,10000f);
                    C("configured bag capacity is 15",r.Run.BagCapacity==15&&r.Data.Player.BagSize==15);
                    C("spawn has one fixed cube device",r.Zones.Map.GetComponentsInChildren<ScoreWorkbench>().Length==1);
                    var station=r.Zones.Map.GetComponentInChildren<ScoreWorkbench>();
                    MovePlayer(r,r.Zones.PlayerSpawn+Vector3.back*8);
                    inv.OpenBag();C("old bag entry cannot open away from device",!inv.IsBagVisible&&!r.Paused);
                    Press(r,UnityEngine.InputSystem.Key.Tab);hud.Refresh(r.Weapons.Active);
                    C("Tab state folds rails but retains instrument and borderless score icon",!hud.WeaponRail.gameObject.activeSelf&&!hud.ScoreRail.gameObject.activeSelf&&hud.ScoreIcon.gameObject.activeSelf&&hud.CurrentWeapon.gameObject.activeInHierarchy&&hud.ScoreIcon.transform.Find("Frame")==null);
                    Press(r,UnityEngine.InputSystem.Key.Tab);hud.Refresh(r.Weapons.Active);
                    C("preview does not open inventory or pause",!r.InventoryOpen&&!r.Paused&&hud.ScoreRail.gameObject.activeSelf);
                    r.Run.Bag.Clear();
                    for(int i=0;i<15;i++)r.Run.Bag.Add(r.Data.MakeItem(i%2==0?"std":"dmg"));
                    hud.Refresh(r.Weapons.Active);
                    C("all 15 bag items are visible in expanded preview",hud.ScorePreviewIcons.Length==15&&hud.ScorePreviewIcons.All(x=>x.enabled));
                    C("all 15 preview slots enlarged across reference red rectangle",hud.ScorePreviewBorders.All(x=>x.rectTransform.sizeDelta==new Vector2(68,68))&&hud.ScoreRail.sizeDelta==new Vector2(1140,80));
                    C("minimap anchored upper right",hud.Minimap.GetComponent<RectTransform>().anchorMax==Vector2.one);
                    var hr=hud.GetComponent<HudRefs>();
                    C("timer is adjacent to pause without ornamental frame",hr.Timer.transform.parent.GetComponent<RectTransform>().anchorMax==Vector2.one&&!hr.Timer.transform.parent.GetComponent<Image>().enabled);
                    C("approved score bag icon is bound without external border",hud.ScoreIcon.sprite&&hud.ScoreIcon.sprite.name=="score_bag_icon"&&hud.ScoreIcon.transform.Find("Frame")==null);
                    C("instrument preview matches backpack slot height and spacing",hud.WeaponRail.sizeDelta.y==hud.ScoreRail.sizeDelta.y&&hud.SlotBases.Take(r.Weapons.Active.Slots.Length).All(x=>x.rectTransform.sizeDelta==new Vector2(68,68)&&x.rectTransform.localScale==Vector3.one)&&hud.SlotBases[1].rectTransform.anchoredPosition.x-hud.SlotBases[0].rectTransform.anchoredPosition.x==75);
                    C("both HUD previews use sliced approved backplate",new[]{hud.WeaponRail,hud.ScoreRail}.All(x=>x.GetComponent<Image>().sprite&&x.GetComponent<Image>().sprite.name=="score_preview_rail"&&x.GetComponent<Image>().type==Image.Type.Sliced&&x.GetComponent<Image>().sprite.border.x>0));
                    SHudV2PlayCheck.Capture(r,"Shots/score-hud-full-15.png",1920,1080);
                    r.Run.Bag.RemoveRange(3,12);hud.Refresh(r.Weapons.Active);
                    C("empty bag slots use twelve imported ivory borders",hud.ScorePreviewBorders.Count(x=>x.enabled&&x.sprite&&x.sprite.name=="score_slot_empty")==12);
                    C("requested HUD descriptions are hidden",!hud.ScoreCount.gameObject.activeSelf&&!hr.TimerLabel.gameObject.activeSelf&&!hud.InputHint.gameObject.activeSelf);
                    SHudV2PlayCheck.Capture(r,"Shots/score-hud-expanded.png",1920,1080);
                    Press(r,UnityEngine.InputSystem.Key.Tab);
                    C("preview starts folded before station interaction",!r.AttachmentPreviewExpanded);
                    MovePlayer(r,r.Zones.PlayerSpawn);
                    C("F interaction range reaches spawn cube",r.NearbyWorkbench==station);
                    Press(r,UnityEngine.InputSystem.Key.F);C("F opens score and pauses",inv.IsBagVisible&&r.Paused&&Time.timeScale==0);
                    C("station interaction automatically expands instrument slots",r.AttachmentPreviewExpanded&&inv.GetComponentsInChildren<SlotDropTarget>().Any(x=>!x.IsBagArea));
                    C("approved score page is bound",inv.GetComponentInChildren<InvRefs>().transform.Find("ScorePage").GetComponent<Image>().sprite.name=="score_page");
                    string reason;r.Weapons.AcquireGun("smg",r.Run,out reason);
                    r.Run.Bag.Clear();r.Run.Bag.Add(r.Data.MakeItem("dmg"));r.Run.Bag.Add(r.Data.MakeItem("std"));r.Run.Bag.Add(r.Data.MakeItem("std"));r.Run.Bag.Add(r.Data.MakeItem("light"));
                    inv.Refresh();phase=2;SessionState.SetInt("ScoreCheckPhase",phase);at=EditorApplication.timeSinceStartup;return;
                }
                if(EditorApplication.timeSinceStartup-at<.5)return;
                if(phase==2)
                {
                    Canvas.ForceUpdateCanvases();
                    var initialCard=Card(inv,r.Run.Bag.First(x=>x.Id=="std"));
                    var pointer=new PointerEventData(EventSystem.current) {
                        position=RectTransformUtility.WorldToScreenPoint(null,((RectTransform)initialCard.transform).TransformPoint(((RectTransform)initialCard.transform).rect.center))
                    };
                    var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                    C("sheet cards receive real pointer raycasts",hits.Any(h=>h.gameObject.GetComponentInParent<DraggableItem>()==initialCard));
                    var bullet=r.Run.Bag.First(x=>x.Id=="std");
                    int first=Array.FindIndex(r.Weapons.AllGuns[0].Slots.Cells,x=>x==null);
                    Click(inv,bullet,PointerEventData.InputButton.Left);
                    C("left click fills first free equipped slot",r.Weapons.AllGuns[0].Slots.Cells[first]==bullet&&!r.Run.Bag.Contains(bullet));
                    Click(inv,bullet,PointerEventData.InputButton.Right);
                    C("right click unequips",r.Run.Bag.Contains(bullet)&&r.Weapons.AllGuns[0].Slots.Cells[first]==null);
                    var source=r.Run.Bag.First(x=>x.Id=="std");var target=r.Run.Bag.Last(x=>x.Id=="std");
                    var from=Card(inv,source);var to=Card(inv,target);
                    var e=new PointerEventData(EventSystem.current){pointerDrag=from.gameObject,button=PointerEventData.InputButton.Left};
                    from.OnBeginDrag(e);to.OnDrop(e);from.OnEndDrag(e);
                    C("UI drag fuses into level 2",r.Run.Bag.Any(x=>x.Id=="std"&&x.Level==2)&&r.Run.Bag.Count==3);
                    var refs=inv.GetComponentInChildren<InvRefs>();
                    C("sort and close controls bind approved artwork",refs.SortButton.transform.Find("Glyph").GetComponent<Image>().sprite.name=="score_sort"&&refs.CloseButton.transform.Find("Glyph").GetComponent<Image>().sprite.name=="score_close");
                    C("buttons have no background and dark readable sort label",refs.CloseButton.GetComponent<Image>().color.a==0&&refs.SortButton.GetComponent<Image>().color.a==0&&refs.SortButton.transform.Find("Label").GetComponent<Text>().color.r<.3f);
                    foreach(var button in new[]{refs.CloseButton,refs.SortButton})
                    {
                        var rect=button.GetComponent<RectTransform>();
                        var click=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center))};
                        var buttonHits=new List<RaycastResult>();EventSystem.current.RaycastAll(click,buttonHits);
                        C(button.name+" transparent hit area receives pointer",buttonHits.Count>0&&buttonHits[0].gameObject.GetComponentInParent<Button>()==button);
                    }
                    C("workbench rows use shared approved backplate",refs.ChainArea.GetComponentsInChildren<Image>().Where(x=>x.name.StartsWith("Rail_")).All(x=>x.sprite&&x.sprite.name=="score_preview_rail"&&x.type==Image.Type.Sliced));
                    refs.SortButton.onClick.Invoke();
                    C("sort button puts bullets before derivatives",r.Run.Bag[0].IsBullet&&!r.Run.Bag.Last().IsBullet);
                    r.Run.Bag.Reverse();Press(r,UnityEngine.InputSystem.Key.R,true);
                    C("R key uses same sort action",r.Run.Bag[0].IsBullet&&!r.Run.Bag.Last().IsBullet);
                    C("new screen has close and sort controls, no old detail panel",refs.CloseButton&&refs.SortButton&&refs.Detail==null);
                    C("score lower-left instructions hidden",!refs.Hint.gameObject.activeSelf&&string.IsNullOrEmpty(refs.Hint.text));
                    C("workbench instrument slots use unified sizing",refs.ChainArea.GetComponentsInChildren<SlotDropTarget>().All(x=>x.GetComponent<RectTransform>().sizeDelta==new Vector2(68,68)&&x.transform.localScale==Vector3.one));
                    C("both instrument rows accept drops",refs.ChainArea.GetComponentsInChildren<SlotDropTarget>().Select(x=>x.GunIndex).Distinct().Count()==2);
                    SHudV2PlayCheck.Capture(r,"Shots/score-workbench-prototype.png",1920,1080);
                    refs.CloseButton.onClick.Invoke();
                    C("close resumes battle",!inv.Open&&!r.Paused&&Time.timeScale==1);
                    Press(r,UnityEngine.InputSystem.Key.F);Press(r,UnityEngine.InputSystem.Key.Escape);
                    C("Esc closes station and resumes",!inv.Open&&!r.Paused&&Time.timeScale==1);
                    Press(r,UnityEngine.InputSystem.Key.Tab);hud.Refresh(r.Weapons.Active);
                    SHudV2PlayCheck.Capture(r,"Shots/score-hud-folded.png",1920,1080);
                    C("approved control glyphs loaded",UiAssets.Named("score_sort")&&UiAssets.Named("score_close")&&UiAssets.Named("score_slot_empty"));
                    inv.ShowOffers(new List<Offer>{new Offer{Id="std",IsBullet=true,Rarity=Rarity.Common}});
                    phase=3;SessionState.SetInt("ScoreCheckPhase",phase);at=EditorApplication.timeSinceStartup;return;
                }
                if(phase==3)
                {
                    if(!inv.OfferPresentation.CanChoose)return;
                    inv.Close();
                    C("Esc defers reward without opening portable inventory",inv.RewardsDeferred&&!inv.Open&&!r.Paused);
                    r.SetPaused(true);r.SetPaused(false);
                    C("pause resume does not force deferred reward open",!inv.Open&&!r.Paused);
                    MovePlayer(r,r.Zones.PlayerSpawn+Vector3.back*8);Press(r,UnityEngine.InputSystem.Key.F);
                    C("F away from device cannot edit",!inv.IsBagVisible);
                    MovePlayer(r,r.Zones.PlayerSpawn);Press(r,UnityEngine.InputSystem.Key.F);
                    C("deferred reward allows device editing",inv.IsBagVisible);
                    inv.Close();
                    C("leaving device restores exact deferred reward",inv.IsOfferVisible&&inv.OfferCardCount==1&&!inv.RewardsDeferred&&r.Paused);
                    r.CloseOverlayForTest();
                    log.Add("RESULT all score workbench checks passed");File.WriteAllLines("Logs/score-workbench-check.log",log);
                    Finish();return;
                }
            }catch(Exception ex){log.Add("FAIL "+ex);File.WriteAllLines("Logs/score-workbench-check.log",log);Finish();}
        }
        static void Finish()
        {if(keyboard!=null)InputSystem.RemoveDevice(keyboard);SessionState.SetInt("ScoreCheckPhase",0);phase=0;started=0;File.Delete(Request);EditorApplication.isPlaying=false;}
    }
}

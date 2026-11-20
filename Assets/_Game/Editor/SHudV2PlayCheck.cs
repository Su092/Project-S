using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SHudV2PlayCheck
    {
        const string Request="Logs/hud-v2-play.request";
        static readonly StringBuilder Log=new();
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static int stage,pass,fail;static double started,at;
        static float pausedElapsed,spin;
        static GameRoot previous;
        static MinimapChestMarker chest;
        static Quaternion look;
        static int ammo;
        static Keyboard testKeyboard, oldKeyboard;
        static Mouse testMouse, oldMouse;
        static Key[] heldKeys=Array.Empty<Key>();
        static MouseState heldMouse;
        static SHudV2PlayCheck()
        {
            EditorApplication.update+=()=>Tick(false);
            InputSystem.onBeforeUpdate+=()=>
            {
                if(InputState.currentUpdateType!=InputUpdateType.Dynamic || testKeyboard==null)return;
                testKeyboard.MakeCurrent();testMouse.MakeCurrent();
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(heldKeys));
                InputSystem.QueueStateEvent(testMouse,heldMouse);heldMouse.delta=Vector2.zero;
            };
            InputSystem.onAfterUpdate+=()=>{if(InputState.currentUpdateType==InputUpdateType.Dynamic){testKeyboard?.MakeCurrent();testMouse?.MakeCurrent();Tick(true);}};
        }
        static void Check(string name,bool ok){Log.AppendLine((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;File.WriteAllText("Logs/hud-v2-play-check.log",Log.ToString());}
        static void Next(){stage++;at=EditorApplication.timeSinceStartup;}
        static void SendKeys(params Key[] keys)=>heldKeys=keys;
        static void Tick(bool runtime)
        {
            if(File.Exists("Logs/hud-v2-stop.request"))
            {File.Delete("Logs/hud-v2-stop.request");Finish();return;}
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(started==0)started=EditorApplication.timeSinceStartup;
                if(EditorApplication.timeSinceStartup-started>90)throw new TimeoutException("HUD stage "+stage);
                if(!EditorApplication.isPlaying){EditorApplication.isPaused=false;EditorApplication.isPlaying=true;return;}
                if(!runtime)return;
                var root=GameRoot.Instance;
                if(!root||root.Run==null||root.Menu==null)return;
                var v=UnityEngine.Object.FindAnyObjectByType<HudV2View>(FindObjectsInactive.Include);
                if(!v)return;
                var refs=v.GetComponent<HudRefs>();
                if(stage==0)
                {
                    oldKeyboard=Keyboard.current;oldMouse=Mouse.current;
                    testKeyboard=InputSystem.AddDevice<Keyboard>("HUDCheckKeyboard");testKeyboard.MakeCurrent();
                    testMouse=InputSystem.AddDevice<Mouse>("HUDCheckMouse");testMouse.MakeCurrent();
                    Check("HUD V2 prefab has 12 physical slot bindings",v.SlotBases.Length==12);
                    Check("HUD hidden on menu",!v.gameObject.activeSelf);
                    Check("music HUD sprites are bound",refs.HpFill.sprite.name=="hud_music_hp_fill" &&
                        refs.XpFill.sprite.name=="hud_music_xp_fill" && refs.BossFill.sprite.name=="hud_music_boss_fill" &&
                        refs.Crosshair.sprite.name=="hud_music_crosshair" &&
                        ((Image)v.ResumeButton.targetGraphic).sprite.name=="hud_music_resume" &&
                        refs.BannerPlate.sprite.name=="hud_music_banner");
                    Check("51 unique item icons load",root.Data.BulletOrder.All(d=>UiAssets.ItemIcon(d.Id))&&root.Data.ModifierOrder.All(d=>UiAssets.ItemIcon(d.Id)));
                    Capture(root,"Shots/hud-v2-menu.png",1920,1080);
                    root.InvulnerableForTest=true;root.Player.AiControlled=true;
                    root.Menu.StartOrChoose();root.Menu.SelectGun("rifle");root.Menu.StartOrChoose();
                    Next();
                }
                else if(stage==1 && EditorApplication.timeSinceStartup-at>.6)
                {
                    Check("combat HUD visible and timer starts at ten minutes",v.gameObject.activeSelf&&refs.Timer.text=="10:00");
                    Check("single gun hides spare and swap",!v.SpareRoot.activeSelf&&!v.SwapGlyph.gameObject.activeSelf);
                    foreach(var def in root.Data.GunOrder)
                    {
                        root.Weapons.StartWith(def.Id);v.Refresh(root.Weapons.Active);
                        Check(def.Id+" physical slot count",v.SlotBases.Count(x=>x.gameObject.activeSelf)==def.SlotCount);
                        Check(def.Id+" music instrument label",v.WeaponName.text!=def.Name);
                        Check(def.Id+" distinct weapon icon",v.CurrentWeapon.sprite==UiAssets.Named("hud_gun_"+def.Id));
                        var last=v.SlotBases[def.SlotCount-1].rectTransform;
                        Check(def.Id+" last slot clears right endcap",v.WeaponRail.rect.width-last.anchoredPosition.x-last.rect.width >= v.WeaponRail.GetComponent<Image>().sprite.border.z+8);
                        Check(def.Id+" full magazine reveals full arc",refs.AmmoFill.enabled&&Mathf.Approximately(refs.AmmoFill.fillAmount,1));
                        if(def.Id=="rifle"||def.Id=="smg"||def.Id=="shotgun"||def.Id=="launcher")
                            Capture(root,"Shots/hud-edge-"+def.SlotCount+"-slots.png",1920,1080);
                    }
                    var bezel=v.Minimap.transform.Find("Bezel").GetComponent<Image>();
                    Check("user minimap art keeps native 376x420 source and aspect",
                        bezel.sprite.rect.size==new Vector2(376,420) &&
                        Mathf.Abs(bezel.rectTransform.rect.width/bezel.rectTransform.rect.height-376f/420)<.0001f);
                    Check("user portrait frame keeps native 256x256 source",
                        v.CurrentWeaponButton.transform.Find("Frame").GetComponent<Image>().sprite.rect.size==new Vector2(256,256));
                    foreach(var f in new[]{refs.HpFill,refs.XpFill,refs.BossFill})
                    {
                        var track=f.transform.parent.GetComponent<Image>();
                        Check(track.name+" caps retain uniform source scale",
                            Mathf.Abs(track.pixelsPerUnitMultiplier*track.rectTransform.rect.height-track.sprite.rect.height)<.01f &&
                            f.rectTransform.rect.width < track.rectTransform.rect.width-25 &&
                            f.rectTransform.rect.height < track.rectTransform.rect.height-6);
                    }
                    float oldHp=refs.HpFill.fillAmount,oldXp=refs.XpFill.fillAmount;
                    foreach(float ratio in new[]{1f,.5f,0f})
                    {
                        refs.HpFill.fillAmount=refs.XpFill.fillAmount=ratio;
                        Capture(root,"Shots/hud-border-bars-"+Mathf.RoundToInt(ratio*100)+".png",1920,1080);
                    }
                    refs.HpFill.fillAmount=oldHp;refs.XpFill.fillAmount=oldXp;
                    Check("current weapon has opaque backing below its frame",v.CurrentWeaponButton.targetGraphic is Image wb && wb.sprite.name=="hud_music_badge_mask"&&wb.color.a==1&&v.CurrentWeaponButton.colors.disabledColor.a==1&&wb.transform.GetChild(0).name=="Frame");
                    Check("spare weapon has opaque backing below its frame",v.SpareWeaponButton.targetGraphic is Image sb && sb.sprite.name=="hud_music_badge_mask"&&sb.color.a==1&&v.SpareWeaponButton.colors.disabledColor.a==1&&sb.transform.GetChild(0).name=="Frame");
                    root.Weapons.StartWith("launcher");
                    root.Weapons.Active.Ammo=0;v.Refresh(root.Weapons.Active);
                    Check("empty magazine hides arc fill",!refs.AmmoFill.enabled&&refs.AmmoFill.fillAmount==0);
                    root.Weapons.Active.Ammo=root.Weapons.Active.Stats.Mag;
                    root.Weapons.Active.Slots.Cells[1]=root.Data.MakeItem("rear_boost");
                    root.Weapons.Active.Slots.Cells[3]=root.Data.MakeItem("whole_guidance");
                    root.Weapons.Active.Recalc();
                    root.Weapons.AcquireGun("smg",root.Run,out _);
                    SendKeys(Key.LeftAlt);Next();
                }
                else if(stage==2 && EditorApplication.timeSinceStartup-at>.5 && !root.Weapons.IsSwitching)
                {
                    Check("Alt releases pointer held="+root.HudPointerHeld+" key="+Keyboard.current.leftAltKey.isPressed+" paused="+root.Paused+" cursor="+Cursor.lockState+" visible="+Cursor.visible,root.HudPointerHeld&&Cursor.visible&&Cursor.lockState==CursorLockMode.None);
                    Check("spare weapon visible",v.SpareRoot.activeSelf);
                    v.SpareWeaponButton.onClick.Invoke();
                    Next();
                }
                else if(stage==3 && EditorApplication.timeSinceStartup-at>.5)
                {
                    Check("click spare swaps active weapon and icons",root.Weapons.Active.Base.Id=="launcher"&&v.CurrentWeapon.sprite==UiAssets.Named("hud_gun_launcher")&&v.SpareWeapon.sprite==UiAssets.Named("hud_gun_smg"));
                    Check("empty physical slot retained",v.SlotBases[2].gameObject.activeSelf&&!v.SlotIcons[2].enabled&&v.SlotIcons[3].enabled);
                    Check("dedicated new attachment art is used",v.SlotIcons[1].sprite && v.SlotIcons[1].sprite.name=="item_rear_boost");
                    Check("minimap includes ordinary and movement obstacles: "+v.Minimap.Obstacles.FootprintCount,v.Minimap.Obstacles.FootprintCount>20);
                    var north=v.Minimap.Project(root.PlayerTransform.position+Vector3.forward*5);
                    Check("north projects up independent of heading",Mathf.Abs(north.x)<.01f&&north.y>0);
                    chest=new GameObject("HUDTestChest").AddComponent<MinimapChestMarker>();chest.transform.position=root.PlayerTransform.position+Vector3.right*8;
                    var enemy=(Enemy)typeof(GameRoot).GetMethod("SpawnAt",Private).Invoke(root,new object[]{root.Data.Enemy("Crawler"),root.PlayerTransform.position+Vector3.forward*10,false});
                    enemy.enabled=false;
                    root.Player.AiControlled=false;look=root.Cam.transform.rotation;ammo=root.Weapons.Active.Ammo;
                    heldMouse=new MouseState{delta=new Vector2(50,30)}.WithButton(MouseButton.Left);
                    Next();
                }
                else if(stage==4 && EditorApplication.timeSinceStartup-at>.25)
                {
                    Check("Alt click does not fire",root.Weapons.Active.Ammo==ammo);
                    Check("Alt mouse motion does not aim",Quaternion.Angle(look,root.Cam.transform.rotation)<.01f);
                    Check("player map marker stays centered",v.Minimap.PlayerArrow.rectTransform.anchoredPosition==Vector2.zero);
                    Check("enemy and explicit chest markers appear",v.Minimap.VisibleEnemies>0&&v.Minimap.VisibleChests==1);
                    chest.Opened=true;
                    heldMouse=new MouseState();SendKeys();
                    root.Player.AiControlled=true;
                    Next();
                }
                else if(stage==5 && EditorApplication.timeSinceStartup-at>.3)
                {
                    Check("release Alt restores aim cursor",!root.HudPointerHeld&&!Cursor.visible&&Cursor.lockState==CursorLockMode.Locked);
                    Check("opened chest marker disappears",v.Minimap.VisibleChests==0);
                    root.Director.SeekTo(65);refs.Timer.text=HudV2View.Countdown(root.RunElapsed);
                    Check("countdown at 65 seconds",refs.Timer.text=="08:55");
                    Capture(root,"Shots/hud-v2-gameplay-1920.png",1920,1080);
                    Capture(root,"Shots/hud-v2-gameplay-1280.png",1280,720);
                    Capture(root,"Shots/hud-v2-gameplay-wide.png",2560,1080);
                    Check("crosshair centered",refs.Crosshair.rectTransform.anchoredPosition==Vector2.zero);
                    Check("three inert utility placeholders",v.GetComponentsInChildren<RectTransform>().Count(t=>t.name.StartsWith("UtilityPlaceholder_"))==3 &&
                        !v.GetComponentsInChildren<Button>().Any(b=>b.name.StartsWith("UtilityPlaceholder_")));
                    root.Weapons.Active.Ammo=0;SendKeys(Key.R);Next();
                }
                else if(stage==6 && EditorApplication.timeSinceStartup-at>.3)
                {
                    SendKeys();
                    Check("R starts reload with spinner and hidden crosshair",root.Weapons.Active.Reloading&&v.ReloadSpinner.gameObject.activeSelf&&!refs.Crosshair.enabled);
                    Check("reload uses music presentation",refs.AmmoText.text.StartsWith("调律 "));
                    Check("ammo progress grows while reloading",refs.AmmoFill.fillAmount>0&&refs.AmmoFill.fillAmount<1);
                    Capture(root,"Shots/hud-v2-reload.png",1920,1080);
                    v.PauseButton.onClick.Invoke();spin=v.ReloadSpinner.rectTransform.localEulerAngles.z;pausedElapsed=root.RunElapsed;
                    Next();
                }
                else if(stage==7 && EditorApplication.timeSinceStartup-at>.35)
                {
                    Check("pause freezes timer and reload spinner",root.Paused&&Mathf.Abs(root.RunElapsed-pausedElapsed)<.001f&&Mathf.Abs(Mathf.DeltaAngle(spin,v.ReloadSpinner.rectTransform.localEulerAngles.z))<.01f);
                    Check("pause has an actionable resume overlay",v.PauseOverlay.activeSelf&&v.ResumeButton.interactable);
                    Capture(root,"Shots/hud-v2-paused.png",1920,1080);
                    v.ResumeButton.onClick.Invoke();root.Weapons.Active.Tick(10);
                    Next();
                }
                else if(stage==8 && EditorApplication.timeSinceStartup-at>.2)
                {
                    Check("resume restores combat and normal reticle",!root.Paused&&!v.ReloadSpinner.gameObject.activeSelf&&refs.Crosshair.enabled);
                    root.ToggleInventory();Next();
                }
                else if(stage==9 && EditorApplication.timeSinceStartup-at>.2)
                {
                    Check("inventory still opens after texture replacement",root.InventoryOpen);
                    Capture(root,"Shots/hud-v2-inventory.png",1920,1080);
                    root.CloseOverlayForTest();root.Director.SeekTo(600);
                    typeof(GameRoot).GetMethod("TickDirector",Private).Invoke(root,new object[]{.01f});Next();
                }
                else if(stage==10 && EditorApplication.timeSinceStartup-at>.3)
                {
                    Check("Boss bar shown above countdown",refs.BossBarRoot.activeSelf&&refs.BossBarRoot.GetComponent<RectTransform>().anchoredPosition.y>refs.Timer.transform.parent.GetComponent<RectTransform>().anchoredPosition.y);
                    Check("timer stops at zero",refs.Timer.text=="00:00");
                    Capture(root,"Shots/hud-v2-boss.png",1920,1080);
                    root.InvulnerableForTest=false;root.DamagePlayer(100000);
                    root.TogglePause();Check("end screen cannot resume through pause button",root.RunEnded&&root.Paused);
                    previous=root;SendKeys(Key.Escape);Next();
                }
                else if(stage==11 && EditorApplication.timeSinceStartup-at>.2)
                {
                    Check("Escape cannot resume ended run",root.Paused&&root.RunEnded);
                    SendKeys(Key.R);Next();
                }
                else if(stage==12 && root!=previous)
                {
                    SendKeys();
                    Check("restart restores menu and fresh HUD time",root.Menu.IsOpen&&root.RunElapsed==0&&refs.Timer.text=="10:00");
                    Check("restart clears map chest registry",MinimapChestMarker.Active.Count==0);
                    Finish();
                }
            }
            catch(Exception e){Check(e.ToString(),false);Finish();}
        }
        public static void Capture(GameRoot root,string path,int width,int height)
        {
            Directory.CreateDirectory("Shots");
            var cam=root.Cam;var target=cam.targetTexture;float aspect=cam.aspect;
            var rt=new RenderTexture(width,height,24);
            var oldActive=RenderTexture.active;
            var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
            var canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)
                .Where(c=>c.isRootCanvas).ToArray();
            var modes=canvases.Select(c=>c.renderMode).ToArray();
            var cameras=canvases.Select(c=>c.worldCamera).ToArray();
            var distances=canvases.Select(c=>c.planeDistance).ToArray();
            var factors=canvases.Select(c=>c.scaleFactor).ToArray();
            var scalers=canvases.Select(c=>c.GetComponent<CanvasScaler>()).ToArray();
            var enabled=scalers.Select(s=>s&&s.enabled).ToArray();
            try
            {
                cam.targetTexture=rt;cam.aspect=(float)width/height;
                for(int i=0;i<canvases.Length;i++)
                {
                    var c=canvases[i];c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=cam;c.planeDistance=cam.nearClipPlane+.001f;
                    var res=scalers[i]?scalers[i].referenceResolution:new Vector2(1920,1080);
                    if(scalers[i])scalers[i].enabled=false;
                    c.scaleFactor=Mathf.Sqrt(width/res.x*height/res.y);
                }
                Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture=target;cam.aspect=aspect;RenderTexture.active=oldActive;
                for(int i=0;i<canvases.Length;i++)
                {
                    canvases[i].renderMode=modes[i];canvases[i].worldCamera=cameras[i];canvases[i].planeDistance=distances[i];canvases[i].scaleFactor=factors[i];
                    if(scalers[i])scalers[i].enabled=enabled[i];
                }
                Canvas.ForceUpdateCanvases();UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);
            }
        }
        static void Finish()
        {
            if(Keyboard.current!=null)SendKeys();
            if(Mouse.current!=null)heldMouse=new MouseState();
            Log.AppendLine("RESULT "+pass+"/"+(pass+fail));File.WriteAllText("Logs/hud-v2-play-check.log",Log.ToString());
            if(testKeyboard!=null && testKeyboard.added)InputSystem.RemoveDevice(testKeyboard);
            if(testMouse!=null && testMouse.added)InputSystem.RemoveDevice(testMouse);
            if(oldKeyboard!=null && oldKeyboard.added)oldKeyboard.MakeCurrent();
            if(oldMouse!=null && oldMouse.added)oldMouse.MakeCurrent();
            testKeyboard=null;testMouse=null;heldKeys=Array.Empty<Key>();heldMouse=new MouseState();
            File.Delete(Request);EditorApplication.isPlaying=false;
            stage=pass=fail=0;started=at=0;previous=null;chest=null;Log.Clear();
        }
    }
}

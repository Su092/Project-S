using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using S.Core;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SFusionPlayCheck
    {
        const string Request = "Logs/fusion-play.request";
        static readonly StringBuilder Log = new StringBuilder();
        static int _phase, _pass, _fail;
        static double _started, _at;
        static SFusionPlayCheck()
        {
            if (!File.Exists(Request)) return;
            _started=EditorApplication.timeSinceStartup;
            EditorApplication.update+=Tick;
        }
        static void Check(string title,bool ok)
        { Log.AppendLine((ok?"PASS ":"FAIL ")+title); if(ok)_pass++;else _fail++; }
        static ChainItem Level(SData data,string id,int level) => level==1?data.MakeItem(id):
            ItemFusion.Preview(Level(data,id,level-1),Level(data,id,level-1));
        static DraggableItem Card(InventoryView inv,ChainItem item) =>
            inv.GetComponentsInChildren<DraggableItem>().First(d=>ReferenceEquals(d.Item,item));
        static void Drop(InventoryView inv,ChainItem source,ChainItem target,bool hover=false)
        {
            Canvas.ForceUpdateCanvases();
            var from=Card(inv,source);var to=Card(inv,target);
            var e=new PointerEventData(EventSystem.current){pointerDrag=from.gameObject,button=PointerEventData.InputButton.Left};
            var rt=(RectTransform)to.transform;
            e.position=RectTransformUtility.WorldToScreenPoint(null,rt.TransformPoint(rt.rect.center));
            from.OnBeginDrag(e);
            Canvas.ForceUpdateCanvases();
            e.position=RectTransformUtility.WorldToScreenPoint(null,rt.TransformPoint(rt.rect.center));
            if(hover)
            {
                var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
                Log.AppendLine("HOVER " + e.position + " target=" + rt.position + " hits=" + string.Join(",",hits.Select(h=>h.gameObject.name)));
                inv.UpdateDropHints(source,from.FromSlot,from.FromChain,e);
                Check("拖动悬停显示合成预告",inv.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("松开合成")));
            }
            ExecuteEvents.ExecuteHierarchy(to.gameObject,e,ExecuteEvents.dropHandler);
            ExecuteEvents.ExecuteHierarchy(to.gameObject,e,ExecuteEvents.dropHandler); // 同一次拖放重复事件
            from.OnEndDrag(e);
        }
        static void Reset(GameRoot root,InventoryView inv)
        {
            root.Run.Bag.Clear();
            Array.Clear(root.Weapons.Active.Slots.Cells,0,root.Weapons.Active.Slots.Length);
            root.Weapons.Active.Slots.Cells[0]=root.Data.MakeItem("std");
            root.Weapons.Active.Recalc();inv.OpenBag();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
                if(EditorApplication.timeSinceStartup-_started>45){Check("运行态测试超时",false);Finish();return;}
                if(!EditorApplication.isPlaying){if(_phase==0){_phase=-1;EditorApplication.isPlaying=true;}return;}
                var root=GameRoot.Instance;if(root==null||root.Run==null||root.Menu==null)return;
                if(_phase<=0)
                {
                    root.Menu.StartForTest();root.InvulnerableForTest=true;
                    root.GetComponent<InventoryView>().OpenBag();
                    _phase=1;_at=EditorApplication.timeSinceStartup;return;
                }
                if(EditorApplication.timeSinceStartup-_at<.2)return;
                var inv=root.GetComponent<InventoryView>();var gun=root.Weapons.Active;var data=root.Data;
                if (_phase == 1)
                {
                    // 新建 Graphic 必须经过渲染帧才有 raycast depth；先让真实 UI 完成布局和渲染。
                    Reset(root,inv);
                    root.Run.Bag.Add(data.MakeItem("dmg"));root.Run.Bag.Add(data.MakeItem("dmg"));
                    inv.OpenBag();_phase=2;_at=EditorApplication.timeSinceStartup;return;
                }
                Check("运行数据包含34种衍生配件",data.ModifierOrder.Count==34);
                Drop(inv,root.Run.Bag[0],root.Run.Bag[1],true);
                Check("真实射线定位的背包合成只留下一个二级配件",root.Run.Bag.Count==1 && root.Run.Bag[0].Level==2);
                for(int direction=0;direction<4;direction++)
                {
                    Reset(root,inv);
                    var source=data.MakeItem("dmg");var target=data.MakeItem("dmg");
                    if((direction&1)==0)root.Run.Bag.Add(source);else gun.Slots.Cells[2]=source;
                    if((direction&2)==0)root.Run.Bag.Add(target);else gun.Slots.Cells[4]=target;
                    inv.OpenBag();
                    Drop(inv,source,target);
                    var result=(direction&2)==0?root.Run.Bag.Single():gun.Slots.Cells[4];
                    Check("方向"+direction+" 拖放事件二合一、重复回调不再消费",result.Level==2 &&
                        gun.Slots.FilledCount+root.Run.Bag.Count==2 && !root.Run.Bag.Contains(source) && !gun.Slots.Cells.Contains(source));
                    Check("方向"+direction+" 卡片显示等级",Card(inv,result).GetComponent<CardRefs>().Label.text.Contains("Lv.2"));
                    ItemTooltip.Instance.Show(result);
                    Check("方向"+direction+" 详情显示升级名称",ItemTooltip.Instance.TitleText.Contains("Lv.2"));
                }
                Reset(root,inv);
                var bullet=data.MakeItem("std");root.Run.Bag.Add(bullet);inv.OpenBag();
                Drop(inv,bullet,gun.Slots.Cells[0]);
                Check("子弹拖入枪槽合成并实际增伤",gun.Slots.Cells[0].Level==2 && Mathf.Approximately(root.Weapons.PeekCast().Projectiles[0].Damage,18));
                Reset(root,inv);
                var small=data.MakeItem("dmg");var large=Level(data,"dmg",2);
                gun.Slots.Cells[2]=small;root.Run.Bag.Add(large);inv.OpenBag();Drop(inv,small,large);
                Check("等级不一致保持原位置、不卸下来源",gun.Slots.Cells[2]==small && root.Run.Bag.Single()==large);
                Reset(root,inv);
                var maxA=Level(data,"dmg",5);var maxB=Level(data,"dmg",5);
                root.Run.Bag.Add(maxA);root.Run.Bag.Add(maxB);inv.OpenBag();Drop(inv,maxA,maxB);
                Check("满级拖放不消耗配件",root.Run.Bag.Count==2 && root.Run.Bag.Contains(maxA) && root.Run.Bag.Contains(maxB));
                Reset(root,inv);
                var gunItem=data.MakeItem("dmg");gun.Slots.Cells[2]=gunItem;
                while(root.Run.Bag.Count<root.Run.BagCapacity)root.Run.Bag.Add(data.MakeItem("dmg"));
                var bagItem=root.Run.Bag[0];inv.OpenBag();Drop(inv,gunItem,bagItem);
                Check("满背包仍可接收合成且来源槽清空",root.Run.Bag.Count==root.Run.BagCapacity && root.Run.Bag[0].Level==2 && gun.Slots.Cells[2]==null);
                Reset(root,inv);
                var left=data.MakeItem("dmg");var right=data.MakeItem("pierce");
                gun.Slots.Cells[2]=left;gun.Slots.Cells[4]=right;inv.OpenBag();Drop(inv,left,right);
                Check("不同配件仍然交换",gun.Slots.Cells[2]==right && gun.Slots.Cells[4]==left);
                foreach(var scope in Enum.GetValues(typeof(Scope)).Cast<Scope>())
                {
                    Reset(root,inv);
                    for(int i=0;i<gun.Slots.Length;i++)gun.Slots.Cells[i]=data.MakeItem("std");
                    var def=data.ModifierOrder.First(m=>m.Scope==scope && !m.IsGunOnly);
                    var item=data.MakeItem(def.Id);gun.Slots.Cells[3]=item;inv.OpenBag();
                    var card=Card(inv,item);
                    inv.HandleItemClick(card,PointerEventData.InputButton.Left);
                    Check(scope+" 范围高亮覆盖实际子弹",inv.HasScopePreview &&
                        inv.PreviewTargetCount==Loadout.AffectedBulletCells(gun.Slots,3).Count);
                    Check(scope+" 范围文字可读",ItemTooltip.ScopeName(scope)!="全枪" || scope==Scope.All);
                }
                Check("所有配件有可用图标",data.ModifierOrder.All(m=>UiAssets.ItemIcon(m.Id)!=null));
                // 留一张升级后背包与范围预览，方便人工复核。
                Reset(root,inv);
                gun.Slots.Cells[0]=Level(data,"std",2);
                gun.Slots.Cells[2]=Level(data,"std",2);
                gun.Slots.Cells[4]=Level(data,"std",2);
                gun.Slots.Cells[3]=Level(data,"crossfire",3);
                foreach(var id in new[]{"rear_battery","gap_lens","rear_frost","ring_burn","gap_bounce","rear_blast","whole_boost","whole_guidance"})
                    root.Run.Bag.Add(Level(data,id,2));
                gun.Recalc();inv.OpenBag();
                inv.HandleItemClick(Card(inv,gun.Slots.Cells[3]),PointerEventData.InputButton.Left);
                ItemTooltip.Instance.Show(gun.Slots.Cells[3]);
                Directory.CreateDirectory("Shots");ScreenCapture.CaptureScreenshot("Shots/fusion-inventory.png");
                _phase=2;
                EditorApplication.update-=Tick;EditorApplication.update+=FinishAfterScreenshot;_at=EditorApplication.timeSinceStartup;
            }
            catch(Exception e){_fail++;Log.AppendLine("FAIL "+e);Finish();}
        }
        static void FinishAfterScreenshot()
        { if(EditorApplication.timeSinceStartup-_at>.7){EditorApplication.update-=FinishAfterScreenshot;Finish();} }
        static void Finish()
        {
            EditorApplication.update-=Tick;
            Log.AppendLine($"RESULT {_pass}/{_pass+_fail} passed");
            File.WriteAllText("Logs/fusion-play-check.log",Log.ToString());
            if(File.Exists(Request))File.Delete(Request);
            Debug.Log(Log.ToString());EditorApplication.isPlaying=false;
        }
    }
}

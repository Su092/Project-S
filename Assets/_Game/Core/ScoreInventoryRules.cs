using System;
using System.Collections.Generic;
namespace S.Core
{
    /// <summary>Inventory ordering and atomic transfers shared by click and drag controls.</summary>
    public static class ScoreInventoryRules
    {
        static int Rank(Rarity r) => r == Rarity.Epic ? 3 : r == Rarity.Rare ? 2 : r == Rarity.Common ? 1 : 0;
        public static void Sort(List<ChainItem> bag, SData data)
        {
            bag.RemoveAll(x => x == null);
            var order = new Dictionary<string,int>();
            for(int i=0;i<data.BulletOrder.Count;i++) order[data.BulletOrder[i].Id]=i;
            for(int i=0;i<data.ModifierOrder.Count;i++) order[data.ModifierOrder[i].Id]=i;
            bag.Sort((a,b) => {
                int c = (a.IsBullet ? 0 : 1).CompareTo(b.IsBullet ? 0 : 1);
                if(c!=0)return c;
                c=Rank(b.Rarity).CompareTo(Rank(a.Rarity));if(c!=0)return c;
                c=(order.TryGetValue(a.Id,out int ai)?ai:int.MaxValue).CompareTo(order.TryGetValue(b.Id,out int bi)?bi:int.MaxValue);
                if(c!=0)return c;
                c=string.CompareOrdinal(a.Id,b.Id);return c!=0?c:b.Level.CompareTo(a.Level);
            });
        }
        public static bool Locate(IList<ChainItem> bag, IList<GunRuntime> guns, ChainItem item, out int gun, out int cell)
        {
            gun=-1;cell=-1;if(item==null)return false;
            for(int i=0;i<bag.Count;i++)if(ReferenceEquals(bag[i],item)){cell=i;return true;}
            for(int g=0;g<guns.Count;g++)for(int s=0;s<guns[g].Slots.Length;s++)
                if(ReferenceEquals(guns[g].Slots.Cells[s],item)){gun=g;cell=s;return true;}
            return false;
        }
        static void Put(IList<ChainItem> bag,IList<GunRuntime> guns,int gun,int cell,ChainItem item)
        { if(gun<0)bag[cell]=item;else guns[gun].Slots.Cells[cell]=item; }
        static void Remove(IList<ChainItem> bag,IList<GunRuntime> guns,int gun,int cell)
        { if(gun<0)bag.RemoveAt(cell);else guns[gun].Slots.Cells[cell]=null; }
        public static bool Move(List<ChainItem> bag, int capacity, IList<GunRuntime> guns, ChainItem item,
            int targetGun, int targetCell, ChainItem bagTarget, out string reason)
        {
            reason=null;
            if(!Locate(bag,guns,item,out int fromGun,out int fromCell)){reason="配件位置已变化";return false;}
            ChainItem target=null;
            if(targetGun>=0)
            {
                if(targetGun>=guns.Count||targetCell<0||targetCell>=guns[targetGun].Slots.Length){reason="槽位不存在";return false;}
                target=guns[targetGun].Slots.Cells[targetCell];
            }
            else if(bagTarget!=null)
            {
                if(!Locate(bag,guns,bagTarget,out int bg,out targetCell)||bg>=0){reason="目标位置已变化";return false;}
                target=bagTarget;
            }
            else
            {
                if(fromGun<0)return true;
                if(bag.Count>=capacity){reason="曲谱已满，无法卸下";return false;}
                guns[fromGun].Slots.Cells[fromCell]=null;bag.Add(item);guns[fromGun].Recalc();return true;
            }
            if(ReferenceEquals(target,item))return true;
            if(target!=null && target.Id==item.Id && target.IsBullet==item.IsBullet)
            {
                if(!ItemFusion.CanMerge(item,target,out reason))return false;
                var merged=ItemFusion.Preview(item,target);
                Put(bag,guns,targetGun,targetCell,merged);
                Remove(bag,guns,fromGun,fromCell);
            }
            else
            {
                // Swaps conserve both objects; validate both equipped destinations before committing.
                if(targetGun>=0 && !item.IsBullet &&
                    Loadout.Validate(guns[targetGun].Slots,targetCell,item,out reason)!=Loadout.DropResult.Ok)return false;
                if(fromGun>=0 && target!=null && !target.IsBullet &&
                    Loadout.Validate(guns[fromGun].Slots,fromCell,target,out reason)!=Loadout.DropResult.Ok)return false;
                Put(bag,guns,targetGun,targetCell,item);
                if(target!=null)Put(bag,guns,fromGun,fromCell,target);
                else Remove(bag,guns,fromGun,fromCell);
            }
            foreach(var g in guns)g.Recalc();
            return true;
        }
        public static bool FillFirst(List<ChainItem> bag,IList<GunRuntime> guns,ChainItem item,int preferred,out string reason)
        {
            reason="乐器槽位已满";
            if(item==null||!bag.Contains(item))return false;
            // Fill the selected/current instrument, then the other owned instruments.
            for(int row=0;row<guns.Count;row++)
            {
                int gi=(Math.Max(0,preferred)+row)%guns.Count;
                for(int slot=0;slot<guns[gi].Slots.Length;slot++)
                {
                    if(guns[gi].Slots.Cells[slot]!=null)continue;
                    if(Move(bag,int.MaxValue,guns,item,gi,slot,null,out reason))return true;
                }
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using S.Core;
namespace LoadoutTests
{
    static class ScoreCases
    {
        public static void Run(SData d,Action<string,bool,string> check)
        {
            void C(string s,bool ok)=>check(s,ok,"");
            var run=new RunState(d,42);
            C("default capacity follows config at 15",run.BagCapacity==15);
            for(int i=0;i<15;i++)run.TryPick("std");
            C("sixteenth reward rejected without losing bag",!run.TryPick("std")&&run.Bag.Count==15);
            C("explicit test capacity override still works",new RunState(d,42,4).BagCapacity==4);
            var guns=new List<GunRuntime>{new GunRuntime(d.Guns["rifle"],new SlotState(8)),new GunRuntime(d.Guns["smg"],new SlotState(10))};
            var a=d.MakeItem("std");var b=d.MakeItem("light");var bag=new List<ChainItem>{a,b};
            C("click fills first slot",ScoreInventoryRules.FillFirst(bag,guns,a,0,out _)&&guns[0].Slots.Cells[0]==a&&bag.Count==1);
            C("click fills next empty slot",ScoreInventoryRules.FillFirst(bag,guns,b,0,out _)&&guns[0].Slots.Cells[1]==b);
            C("cross instrument drag moves",ScoreInventoryRules.Move(bag,15,guns,a,1,3,null,out _)&&guns[0].Slots.Cells[0]==null&&guns[1].Slots.Cells[3]==a);
            C("swap conserves both items",ScoreInventoryRules.Move(bag,15,guns,b,1,3,null,out _)&&guns[0].Slots.Cells[1]==a&&guns[1].Slots.Cells[3]==b);
            var another=d.MakeItem("std");guns[1].Slots.Cells[6]=another;
            C("cross instrument fusion retains target",ScoreInventoryRules.Move(bag,15,guns,a,1,6,null,out _)&&guns[0].Slots.Cells[1]==null&&guns[1].Slots.Cells[6].Level==2);
            var lv2=ItemFusion.Preview(d.MakeItem("std"),d.MakeItem("std"));bag.Add(lv2);
            C("level two pair fuses to three",ScoreInventoryRules.Move(bag,15,guns,lv2,1,6,null,out _)&&guns[1].Slots.Cells[6].Level==3);
            var lv3=ItemFusion.Preview(ItemFusion.Preview(d.MakeItem("std"),d.MakeItem("std")),ItemFusion.Preview(d.MakeItem("std"),d.MakeItem("std")));
            bag.Add(lv3);
            C("level three fusion rejected without consumption",!ScoreInventoryRules.Move(bag,15,guns,lv3,1,6,null,out _)&&bag.Contains(lv3)&&guns[1].Slots.Cells[6].Level==3);
            C("full bag prevents unequip",!ScoreInventoryRules.Move(bag,1,guns,b,-1,-1,null,out _)&&guns[1].Slots.Cells[3]==b);
            C("right-click unequip preserves identity",ScoreInventoryRules.Move(bag,15,guns,b,-1,-1,null,out _)&&bag.Contains(b)&&guns[1].Slots.Cells[3]==null);
            var sample=new List<ChainItem>{d.MakeItem("dmg"),d.MakeItem("std"),null,d.MakeItem("light"),d.MakeItem("grenade"),d.MakeItem("dmg")};
            var instances=sample.Where(x=>x!=null).ToArray();
            ScoreInventoryRules.Sort(sample,d);
            C("sort compacts holes without merging",sample.Count==instances.Length&&instances.All(x=>sample.Contains(x)));
            C("sort keeps bullet group ahead of derivatives",sample.TakeWhile(x=>x.IsBullet).Count()==3);
            var rare=d.BulletOrder.First(x=>x.Rarity==Rarity.Rare);
            var common=d.BulletOrder.First(x=>x.Rarity==Rarity.Common);
            var sorted=new List<ChainItem>{d.MakeItem("dmg"),d.MakeItem(common.Id),d.MakeItem(rare.Id)};
            ScoreInventoryRules.Sort(sorted,d);
            C("within type rarity desc before configuration number",sorted[0].Id==rare.Id&&sorted[1].Id==common.Id&&!sorted[2].IsBullet);
            var once=sample.ToArray();ScoreInventoryRules.Sort(sample,d);
            C("sort deterministic for distinct keys",sample.Select(x=>x.Id).SequenceEqual(once.Select(x=>x.Id)));
        }
    }
}

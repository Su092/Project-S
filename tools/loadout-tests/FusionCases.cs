using System;
using System.Collections.Generic;
using System.Linq;
using S.Core;

namespace LoadoutTests
{
    static class FusionCases
    {
        public static void Run(SData data, Action<string,bool,string> report)
        {
            void Check(string title, bool ok) => report(title,ok,"");
            ChainItem Level(string id,int level) => level <= 1 ? data.MakeItem(id) :
                ItemFusion.Preview(Level(id,level-1),Level(id,level-1));
            var gun = data.Guns["rifle"];
            var layout = new SlotState(8);
            bool correct = true;
            var expect = new Dictionary<Scope,int[]> {
                {Scope.Next,new[]{4}}, {Scope.Prev,new[]{2}}, {Scope.Adj,new[]{2,4}},
                {Scope.N2,new[]{4,5}}, {Scope.N3,new[]{4,5,6}}, {Scope.P2,new[]{1,2}},
                {Scope.P3,new[]{0,1,2}}, {Scope.Adj2,new[]{1,2,4,5}}, {Scope.Skip,new[]{1,5}},
                {Scope.All,new[]{0,1,2,4,5,6,7}}
            };
            foreach (var p in expect) correct &= Loadout.TargetsOf(p.Key,3,layout).SequenceEqual(p.Value);
            Check("十种作用域都按物理编号覆盖", correct);
            Check("前三格与隔格范围边界截断不环绕", Loadout.TargetsOf(Scope.P3,0,layout).Count == 0 &&
                Loadout.TargetsOf(Scope.Skip,0,layout).SequenceEqual(new[]{2}) &&
                Loadout.TargetsOf(Scope.Adj2,7,layout).SequenceEqual(new[]{5,6}));
            Check("每种作用域都有衍生配件", Enum.GetValues(typeof(Scope)).Cast<Scope>().All(s=>data.ModifierOrder.Any(m=>m.Scope==s)));
            Check("旧配件作用域已分散", data.Modifiers["pierce"].Scope==Scope.Prev &&
                data.Modifiers["speed"].Scope==Scope.N2 && data.Modifiers["burn"].Scope==Scope.N3 &&
                data.Modifiers["bounce"].Scope==Scope.Adj && data.Modifiers["homing"].Scope==Scope.P2);
            foreach (var m in data.ModifierOrder)
            {
                var slots = new SlotState(8);
                for(int i=0;i<8;i++) slots.Cells[i]=data.MakeItem("std");
                slots.Cells[3]=data.MakeItem(m.Id);
                var range=Loadout.EffectRangeOf(slots,3);
                bool agree=true;
                for(int i=0;i<8;i++) if(i!=3)
                    agree &= Loadout.PassivesOn(gun,slots,i).Any(p=>p.Id==m.Id) == (!m.IsGunOnly && range.Contains(i));
                Check(m.Id+" 高亮范围与实际吸附一致",agree);
            }
            for(int dir=0;dir<4;dir++)
            {
                var bag=new List<ChainItem>();var slots=new SlotState(8);
                var a=data.MakeItem("dmg");var b=data.MakeItem("dmg");
                if((dir&1)==0)bag.Add(a);else slots.Cells[1]=a;
                if((dir&2)==0)bag.Add(b);else slots.Cells[4]=b;
                bool ok=ItemFusion.TryMerge(bag,slots,a,b,out var result,out _);
                Check("合成方向"+dir+" 二合一且保留目标位置",ok && result.Level==2 &&
                    bag.Count+slots.FilledCount==1 && ((dir&2)==0 ? bag.Contains(result):slots.Cells[4]==result));
                Check("合成方向"+dir+" 重复事件不再消费",!ItemFusion.TryMerge(bag,slots,a,b,out _,out _) &&
                    bag.Count+slots.FilledCount==1);
            }
            var full=new List<ChainItem>();for(int i=0;i<44;i++)full.Add(data.MakeItem("std"));
            var fullSlots=new SlotState(2);var incoming=data.MakeItem("std");fullSlots.Cells[0]=incoming;
            Check("满背包仍可合成且不会溢出",ItemFusion.TryMerge(full,fullSlots,incoming,full[10],out var upgraded,out _) &&
                full.Count==44 && full[10]==upgraded && fullSlots.Cells[0]==null);
            var one=data.MakeItem("dmg");var two=Level("dmg",2);var five=Level("dmg",5);
            Check("不同配件不合成",!ItemFusion.CanMerge(one,data.MakeItem("pierce"),out _));
            Check("不同等级不合成",!ItemFusion.CanMerge(one,two,out _));
            Check("自身拖放不合成",!ItemFusion.CanMerge(one,one,out _));
            Check("最高等级五级",five.Level==5 && !ItemFusion.CanMerge(five,Level("dmg",5),out _));
            var bagInvalid=new List<ChainItem>{one,two};
            Check("失败事务保留所有实例",!ItemFusion.TryMerge(bagInvalid,null,one,two,out _,out _) && bagInvalid[0]==one && bagInvalid[1]==two);
            var missing=data.MakeItem("dmg");
            Check("失效拖拽拒绝凭空合成",!ItemFusion.TryMerge(bagInvalid,null,missing,one,out _,out _) && bagInvalid.Count==2);
            Check("升级不污染配置与其他实例",Near(data.Modifiers["dmg"].DamageMulAdd,.5f) &&
                Near(one.EffectiveMod.DamageMulAdd,.5f) && Near(two.EffectiveMod.DamageMulAdd,.75f));
            var damageSlots=new SlotState(8); damageSlots.Cells[0]=two;damageSlots.Cells[1]=Level("std",2);
            var cast=Loadout.ResolveCast(gun,damageSlots,Loadout.ResolveGun(gun,damageSlots),0);
            Check("二级子弹和二级增幅真实结算为31.5伤害",Near(cast.Projectiles[0].Damage,31.5f) && cast.AmmoCost==2);
            var grenade=Level("grenade",2);
            Check("榴弹升级同时强化爆炸且耗弹不涨",Near(grenade.EffectiveBullet.ExplodeDmg,57f) && grenade.EffectiveBullet.Cost==3);
            var heavy=Level("heavy",5).EffectiveMod;
            Check("升级保留负面代价而不放大",Near(heavy.SpeedMulAdd,-.4f) && heavy.Cost==1 && Near(heavy.Delay,.1f));
            var magSlots=new SlotState(8);magSlots.Cells[0]=Level("g_mag",2);
            Check("全枪配件等级参与结算",Loadout.ResolveGun(gun,magSlots).Mag==52);
            var parallel=Level("g_parallel",2).EffectiveMod;
            Check("并联升级增加读取但不加重弹匣惩罚",parallel.GunCastAdd==2 && Near(parallel.GunMagMulIfCast,.8f));
            Check("节能升级降低耗弹",Level("thrift",3).EffectiveMod.Cost==-3 && Level("thrift",3).EffectiveMod.AmmoReduce==3);
            var original=new SlotState(8);original.Cells[0]=data.MakeItem("dmg");original.Cells[2]=data.MakeItem("dmg");
            var probe=Loadout.PreviewPlacement(original,original.Cells[0],2,0);
            Check("合成预览移除来源但不改原布局",probe.Cells[0]==null && probe.Cells[2].Level==2 && original.Cells[0].Level==1 && original.Cells[2].Level==1);
            foreach(var id in data.RollPool())
            {
                var item=Level(id,5);
                Check(id+" 支持合成至五级且保留作用域/形态",item!=null && item.Id==id && item.Level==5 &&
                    (item.IsBullet ? item.EffectiveBullet.Kind==data.Bullets[id].Kind : item.EffectiveMod.Scope==data.Modifiers[id].Scope));
            }
            var run=new RunState(data,9001);bool duplicateEpic=false;var seen=new HashSet<string>();bool unique=true;
            for(int i=0;i<1500;i++)
            {
                var offers=run.Roll();
                unique &= offers.Select(o=>o.Id).Distinct().Count()==offers.Count;
                foreach(var o in offers) if(o.Rarity==Rarity.Epic && !seen.Add(o.Id)) duplicateEpic=true;
            }
            Check("史诗可跨轮重复用于合成，每轮仍不重复",duplicateEpic && unique);
        }
        static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
    }
}

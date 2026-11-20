using System;
using System.Collections.Generic;

namespace S.Core
{
    /// <summary>同种同级二合一。事务只消费来源，成品保留在目标位置，不需要额外背包空间。</summary>
    public static class ItemFusion
    {
        public const int MaxLevel = 3;
        public static bool CanMerge(ChainItem a, ChainItem b, out string reason)
        {
            reason = null;
            if (a == null || b == null || ReferenceEquals(a, b)) reason = "请选择两个不同的配件";
            else if (a.Id != b.Id || a.IsBullet != b.IsBullet) reason = "仅相同种类的配件可以合成";
            else if (a.Level != b.Level) reason = "需要两个同等级配件";
            else if (a.Level >= MaxLevel) reason = $"已达到最高等级 Lv.{MaxLevel}";
            return reason == null;
        }
        public static ChainItem Preview(ChainItem source, ChainItem target) =>
            CanMerge(source, target, out _) ? target.AtLevel(target.Level + 1) : null;

        public static bool TryMerge(IList<ChainItem> bag, SlotState slots, ChainItem source,
            ChainItem target, out ChainItem result, out string reason)
        {
            result = null;
            if (!CanMerge(source, target, out reason)) return false;
            int sourceBag = -1, targetBag = -1, sourceSlot = -1, targetSlot = -1, ns = 0, nt = 0;
            for (int i = 0; i < bag.Count; i++)
            {
                if (ReferenceEquals(bag[i], source)) { sourceBag = i; ns++; }
                if (ReferenceEquals(bag[i], target)) { targetBag = i; nt++; }
            }
            if (slots != null) for (int i = 0; i < slots.Length; i++)
            {
                if (ReferenceEquals(slots.Cells[i], source)) { sourceSlot = i; ns++; }
                if (ReferenceEquals(slots.Cells[i], target)) { targetSlot = i; nt++; }
            }
            if (ns != 1 || nt != 1) { reason = "配件位置已变化，请重新拖拽"; return false; }
            result = Preview(source, target);
            if (targetSlot >= 0) slots.Cells[targetSlot] = result; else bag[targetBag] = result;
            if (sourceSlot >= 0) slots.Cells[sourceSlot] = null; else bag.RemoveAt(sourceBag);
            return true;
        }

        public static float Strength(int level) => 1f + (level - 1) * 0.5f;
        static float Boost(float value, float strength) => value > 0f ? value * strength : value;
        static float Reduce(float multiplier, float strength) =>
            multiplier > 0f && multiplier < 1f ? (float)Math.Pow(multiplier, strength) : multiplier;
        static int Add(int value, int level) => value > 0 ? value + level - 1 : value;
        public static ModifierDef Upgrade(ModifierDef basis, int level)
        {
            if (basis == null || level <= 1) return basis;
            var m = basis.Clone(); float s = Strength(level);
            m.DamageMulAdd = Boost(m.DamageMulAdd,s); m.SizeMulAdd = Boost(m.SizeMulAdd,s);
            m.SpeedMulAdd = Boost(m.SpeedMulAdd,s); m.RangeMulAdd = Boost(m.RangeMulAdd,s);
            m.LifeMulAdd = Boost(m.LifeMulAdd,s); m.KnockMulAdd = Boost(m.KnockMulAdd,s);
            m.CountMulAdd = Boost(m.CountMulAdd,s); m.RadiusMulAdd = Boost(m.RadiusMulAdd,s);
            m.PiercePer = Add(m.PiercePer,level); m.BouncePer = Add(m.BouncePer,level);
            m.HomingPer *= s; m.BurnPer *= s;
            m.SlowPer = Math.Min(0.85f,m.SlowPer*s); m.CritPer = Math.Min(1f,m.CritPer*s);
            if (m.SpreadPer < 0f) m.SpreadPer *= s;
            m.ExplodeRAdd *= s; m.ExplodeRBase *= 1f + (level-1)*0.15f;
            m.ExplodeDmgFromDmg *= s; m.ExplodeDmgFlat *= s;
            m.AmmoReduce = Add(m.AmmoReduce,level);
            if (m.Cost < 0) m.Cost = -m.AmmoReduce;
            if (m.GunMagMul > 1f) m.GunMagMul = 1f + (m.GunMagMul-1f)*s;
            m.GunReloadMul = Reduce(m.GunReloadMul,s);
            m.GunDelayMul = Reduce(m.GunDelayMul,s); m.GunSpreadMul = Reduce(m.GunSpreadMul,s);
            m.GunRecycleAdd = Math.Min(0.9f,m.GunRecycleAdd*s);
            m.GunCastAdd = Add(m.GunCastAdd,level); m.GunLeech *= s;
            m.Multicast = Add(m.Multicast,level);
            return m;
        }
        public static BulletDef Upgrade(BulletDef basis, int level)
        {
            if (basis == null || level <= 1) return basis;
            var b = basis.Clone(); float s = Strength(level);
            b.Damage *= s; b.ExplodeDmg *= s; b.Burn *= s;
            return b;
        }
    }
}

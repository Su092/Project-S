using System;
using System.Collections.Generic;
using S.Core;

namespace LoadoutTests
{
    /// <summary>
    /// 数值基准：6 把枪在不同构筑下的 DPS 与命中率。
    ///
    /// 判据不是「三把枪必须一样强」—— 新版每把枪的槽位数、读取数、基础弹都不同，
    /// 强行拉平会毁掉「槽位多 = 能塞复杂连锁」这个设计支点。
    /// 这里只验两件事：
    ///   1. 没有哪把枪是**废的**（裸弹档 DPS 不应出现数量级差距）；
    ///   2. 散射确实造成可感知的命中率代价（否则「精准 vs 铺弹」的取舍不存在）。
    /// </summary>
    static class Bench
    {
        public static void RunAll(SData data)
        {
            D = data;
            Console.WriteLine();
            Console.WriteLine("===== 数值基准（纸面期望 + 命中率模拟）=====");

            // 裸弹档：每把枪只放自己的基础弹
            var naked = new List<Result>();
            foreach (var g in data.GunOrder)
            {
                var r = Evaluate(data, g.Id, Chain(g.BaseBullet), "裸弹");
                naked.Add(r);
                Console.WriteLine($"  {r.GunName,-14} 槽 {r.Slots,2} 读 {r.CastCount}  " +
                                  $"单发期望 {r.PerCast,7:0.0}  射速 {r.CastsPerSec,5:0.00}/s  " +
                                  $"DPS {r.Dps,7:0.0}");
            }

            Console.WriteLine();
            CheckNakedFloor(naked);

            // 浅构筑：各枪塞入「伤害增幅 + 基础弹」
            Console.WriteLine();
            Console.WriteLine("----- 浅构筑（伤害增幅 + 基础弹）-----");
            foreach (var g in data.GunOrder)
            {
                var r = Evaluate(data, g.Id, Chain("dmg", g.BaseBullet), "浅·加伤");
                Console.WriteLine($"  {r.GunName,-14} 单发期望 {r.PerCast,7:0.0}  DPS {r.Dps,7:0.0}  " +
                                  $"耗弹 {r.AmmoCost}");
            }

            // 深构筑：暴击 + 穿透 + 伤害
            Console.WriteLine();
            Console.WriteLine("----- 深构筑（伤 + 暴击 + 穿透）-----");
            foreach (var g in data.GunOrder)
            {
                var seq = Chain("dmg", "crit", "pierce", g.BaseBullet);
                var r = Evaluate(data, g.Id, seq, "深");
                Console.WriteLine($"  {r.GunName,-14} 单发期望 {r.PerCast,7:0.0}  DPS {r.Dps,7:0.0}  " +
                                  $"耗弹 {r.AmmoCost}  暴击 {r.Crit * 100f:0}%");
            }

            // 散射代价：霰弹 vs 标准弹
            Console.WriteLine();
            Console.WriteLine("----- 散射代价（命中率）-----");
            var tight = Evaluate(data, "rifle", Chain("std"), "精准");
            var loose = Evaluate(data, "rifle", Chain("scatter", "pellet"), "铺弹");
            Console.WriteLine($"  精准 {tight.HitRate * 100f:0.0}%  →  铺弹 {loose.HitRate * 100f:0.0}%");
            float drop = tight.HitRate - loose.HitRate;
            Console.WriteLine(drop > 0.10f
                ? "  PASS 散射造成显著命中率差异"
                : "  WARN 散射差异过小（散射参数不足以形成取舍）");

            // 触发弹回路
            Console.WriteLine();
            Console.WriteLine("----- 触发弹回路 -----");
            var trig = Evaluate(data, "launcher", Chain("trigger", "grenade"), "触发榴弹");
            Console.WriteLine($"  触发弹期望（含载荷）{trig.PerCast:0.0}  DPS {trig.Dps:0.0}");
        }

        sealed class Result
        {
            public string GunId, GunName, Label;
            public int Slots, CastCount;
            public float PerCast, Dps, CastsPerSec, AmmoCost, Crit;
            public float HitRate;
        }

        static ChainItem[] Chain(params string[] ids)
        {
            var list = new ChainItem[ids.Length];
            for (int i = 0; i < ids.Length; i++) list[i] = D.MakeItem(ids[i]);
            return list;
        }

        static SData D;

        static Result Evaluate(SData data, string gunId, ChainItem[] items, string label)
        {
            D = data;
            var gun = data.Guns[gunId];
            // 摆法：先放子弹，再把件依次排在它右边当被动（Prev 会向左找最近的**子弹**）。
            // 这样 [std, dmg, crit, pierce] 里三件都作用于 std，且都不占读取。
            var slots = new SlotState(gun.SlotCount);
            int cell = 0;
            for (int i = 0; i < items.Length && cell < slots.Length; i++)
                if (items[i] != null && items[i].IsBullet) { slots.Cells[cell++] = items[i]; break; }

            for (int i = 0; i < items.Length && cell < slots.Length; i++)
            {
                if (items[i] == null || items[i].IsBullet) continue;
                var m = Clone(items[i].Mod);
                m.Scope = Scope.Prev;
                slots.Cells[cell++] = new ChainItem { Mod = m };
            }

            var stats = Loadout.ResolveGun(gun, slots);
            var cast = Loadout.ResolveCast(gun, slots, stats, 0);
            float interval = stats.ShotInterval(cast.DelayAdd);

            var r = new Result
            {
                GunId = gunId,
                GunName = gun.Name,
                Label = label,
                Slots = gun.SlotCount,
                CastCount = stats.CastCount,
                PerCast = cast.ExpectedDamage,
                CastsPerSec = 1f / interval,
                AmmoCost = cast.AmmoCost,
            };
            r.Dps = r.PerCast * r.CastsPerSec;
            if (cast.Projectiles.Count > 0) r.Crit = cast.Projectiles[0].Crit;

            r.HitRate = SimulateHitRate(gun, cast);
            return r;
        }

        /// <summary>浅拷贝一件衍生配件（只为了在不污染单例的前提下改 scope）。</summary>
        static ModifierDef Clone(ModifierDef m)
        {
            if (m == null) return null;
            return new ModifierDef
            {
                Id = m.Id, Name = m.Name, Icon = m.Icon, Rarity = m.Rarity, Scope = m.Scope,
                Cost = m.Cost, Delay = m.Delay, Desc = m.Desc,
                DamageMulAdd = m.DamageMulAdd, SizeMulAdd = m.SizeMulAdd,
                SpeedMulAdd = m.SpeedMulAdd, RangeMulAdd = m.RangeMulAdd,
                LifeMulAdd = m.LifeMulAdd, KnockMulAdd = m.KnockMulAdd,
                CountMulAdd = m.CountMulAdd, RadiusMulAdd = m.RadiusMulAdd,
                PiercePer = m.PiercePer, BouncePer = m.BouncePer,
                HomingPer = m.HomingPer, BurnPer = m.BurnPer,
                SlowPer = m.SlowPer, CritPer = m.CritPer, SpreadPer = m.SpreadPer,
                ExplodeRAdd = m.ExplodeRAdd, ExplodeRBase = m.ExplodeRBase,
                ExplodeDmgFromDmg = m.ExplodeDmgFromDmg, ExplodeDmgFlat = m.ExplodeDmgFlat,
                AmmoReduce = m.AmmoReduce,
                GunMagMul = m.GunMagMul, GunReloadMul = m.GunReloadMul,
                GunDelayMul = m.GunDelayMul, GunSpreadMul = m.GunSpreadMul,
                GunRecycleAdd = m.GunRecycleAdd, GunCastAdd = m.GunCastAdd,
                GunMagMulIfCast = m.GunMagMulIfCast, GunLeech = m.GunLeech,
                Multicast = m.Multicast,
            };
        }

        /// <summary>
        /// 命中率模拟：20 m 处放半径 0.5 m 的球，按散射锥均匀采样，
        /// 检查弹道与球的最近距离。
        ///
        /// 有重力的弹（榴弹/地雷/力场）不参与这个判据 —— 它们本来就打不出直线弹道，
        /// 用直线模拟得出「命中率 0%」是方法错误，不是枪弱
        /// （旧版本就在这里把榴弹判成废枪，差点去改数值）。
        /// </summary>
        static float SimulateHitRate(GunDef gun, CastResult cast)
        {
            if (cast.Projectiles.Count == 0) return 0f;

            const float dist = 20f;
            const float radius = 0.5f;
            const int samples = 400;

            int hit = 0, total = 0;
            var rng = new Random(20261115);

            foreach (var p in cast.Projectiles)
            {
                // 受重力的抛射物按抛体弹道单独估，不混进直线判据
                if (p.Gravity > 0f) continue;

                float halfAngle = (p.Spread + gun.SpreadBase) * 0.5f * (float)Math.PI / 180f;
                for (int s = 0; s < samples; s++)
                {
                    total++;
                    float u = (float)rng.NextDouble() * 2f - 1f;
                    float phi = (float)rng.NextDouble() * (float)Math.PI * 2f;
                    float r = (float)Math.Sqrt(Math.Max(0f, 1f - u * u));
                    float ax = r * (float)Math.Cos(phi);
                    float ay = r * (float)Math.Sin(phi);

                    float ox = ax * halfAngle * dist;
                    float oy = ay * halfAngle * dist;

                    float d = (float)Math.Sqrt(ox * ox + oy * oy) - radius;
                    if (d <= 0f) hit++;
                }
            }

            return total == 0 ? 1f : (float)hit / total;
        }

        static void CheckNakedFloor(List<Result> rs)
        {
            float min = float.MaxValue, max = 0f;
            string minName = "", maxName = "";
            foreach (var r in rs)
            {
                if (r.Dps < min) { min = r.Dps; minName = r.GunName; }
                if (r.Dps > max) { max = r.Dps; maxName = r.GunName; }
            }

            if (min <= 0.01f)
            {
                Console.WriteLine($"  FAIL 裸弹档：{minName} 无输出（配件/枪械数据有误）");
                return;
            }

            float ratio = max / min;
            // 6 把枪差异天然比 3 把大（射速 0.06~0.85、读取数 1~2、槽位 6~12），
            // 判据放宽到 6×：超过说明某把枪被数据写废了，而不是「设计上有差异」。
            bool ok = ratio <= 6f;
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} 裸弹档无废枪：{minName} {min:0.0} / " +
                              $"{maxName} {max:0.0} = {ratio:0.00}×（判据 ≤6.00×）");
        }
    }
}

using System;
using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 一把枪的槽位布局。**一格一件**（对齐 MODCORE 原型），轮转指针在格位上推进。
    /// </summary>
    public sealed class SlotState
    {
        public ChainItem[] Cells;

        public SlotState(int count)
        {
            Cells = new ChainItem[Math.Max(1, count)];
        }

        public int Length => Cells.Length;

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < Cells.Length; i++) if (Cells[i] != null) return false;
                return true;
            }
        }

        public int FilledCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Cells.Length; i++) if (Cells[i] != null) n++;
                return n;
            }
        }

        public SlotState Clone()
        {
            var s = new SlotState(Cells.Length);
            Array.Copy(Cells, s.Cells, Cells.Length);
            return s;
        }
    }

    /// <summary>
    /// 枪械级的结算后属性（枪械向配件已并入）。原型 `Fn`。
    /// </summary>
    public sealed class GunStats
    {
        public int Mag = 30;
        public float Reload = 1.5f;
        public float Delay = 0.14f;
        public float DelayMul = 1f;
        public float Spread = 1.2f;
        public float SpreadMul = 1f;
        public int CastCount = 1;
        public float DmgMul = 1f;
        public float Recycle;
        public float Leech;

        /// <summary>实际射击间隔（秒）。</summary>
        public float ShotInterval(float delayAdd) =>
            Math.Max(0.03f, (Delay + delayAdd) * DelayMul);
    }

    /// <summary>一条链的解析结果：本次射击读到的子弹，以及下一次的起点。</summary>
    public sealed class CastPlan
    {
        public readonly List<ProjectileDesc> Projectiles = new List<ProjectileDesc>();
        public readonly List<int> UsedCells = new List<int>();
        public int AmmoCost;
        public float DelayAdd;
        public int NextPointer;
        public bool Wrapped;
    }

    /// <summary>单次施法的产物（HUD 预览与实战共用）。</summary>
    public sealed class CastResult
    {
        public readonly List<ProjectileDesc> Projectiles = new List<ProjectileDesc>();
        public readonly List<int> UsedCells = new List<int>();
        public SlotState Slot;
        public int AmmoCost;
        public float DelayAdd;
        public int NextPointer;
        public bool Wrapped;
        public readonly List<CastWarning> Warnings = new List<CastWarning>();

        public bool Succeeded => Projectiles.Count > 0;

        /// <summary>整次施法的期望伤害总和。</summary>
        public float ExpectedDamage
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < Projectiles.Count; i++) sum += Projectiles[i].ExpectedDamage;
                return sum;
            }
        }

        public float AverageSpread
        {
            get
            {
                if (Projectiles.Count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < Projectiles.Count; i++) sum += Projectiles[i].Spread;
                return sum / Projectiles.Count;
            }
        }
    }

    public struct CastWarning
    {
        public string ItemId;
        public int CellIndex;
        public string Code;
    }

    /// <summary>
    /// 结算引擎。**对齐 MODCORE 原型的读取式链**（docs/loadout-spec.md §3）：
    ///
    ///   1. 从轮转指针开始**顺时针扫格子**，跳过空格与被动件；
    ///   2. 遇到 `Next` 作用域的件 → **消耗一次读取**，其效果叠加到之后读到的第一个子弹；
    ///   3. 遇到子弹 → 产出一次射击，读到几次子弹由 `castCount` 决定；
    ///   4. 被动件（Prev/Adj/N2/N3/P2/All）**不占读取**，按作用域吸附到目标子弹；
    ///   5. 触发类子弹递归解析一个载荷（只读 1 次），载荷跟着子弹走。
    ///
    /// 与旧实现（顺序链、衍生件在时间轴上逐项触发）的差别是根本性的：
    /// 旧版一条链是「时间序列」，新版一次射击是「一次性解析出全部投射物」。
    /// 这换来的是组合空间——20 个子弹 × 44 个衍生 × 7 种作用域，而不是「往里塞更多件」。
    ///
    /// 零 Unity 依赖：所有随机都经由外部注入的种子，dotnet 下可逐条断言。
    /// </summary>
    public static class Loadout
    {
        public const int MaxCellScan = 64;      // 防止全空枪死循环
        public const int MaxRecursion = 6;      // 触发载荷递归上限
        public const int MaxCount = 24;         // 单发投射物数量上限（防指数爆炸）

        // ---------------- 枪械属性 ----------------

        /// <summary>结算枪械向配件（原型 `Fn`）。</summary>
        public static GunStats ResolveGun(GunDef gun, SlotState slots)
        {
            var s = new GunStats
            {
                Mag = gun.MagSize,
                Reload = gun.ReloadTime,
                Delay = gun.FireInterval,
                Spread = gun.SpreadBase,
                CastCount = Math.Max(1, gun.CastCount),
            };

            if (slots?.Cells == null) return Finish(s);

            // 弹匣用 float 累乘、最后取整：链上多件扩容时若逐步取整，
            // 30 ×1.5 = 45 → ×1.5 = 67.5，不同顺序会得到 67 或 68，玩家会以为「装反了变弱」。
            float mag = gun.MagSize;

            for (int i = 0; i < slots.Cells.Length; i++)
            {
                var it = slots.Cells[i];
                if (it == null || it.Mod == null) continue;
                var m = it.Mod;

                mag *= m.GunMagMul;
                s.Reload *= m.GunReloadMul;
                s.DelayMul *= m.GunDelayMul;
                s.SpreadMul *= m.GunSpreadMul;
                if (m.GunRecycleAdd != 0f)
                    s.Recycle = 1f - (1f - s.Recycle) * (1f - m.GunRecycleAdd);
                if (m.GunCastAdd != 0)
                {
                    s.CastCount += m.GunCastAdd;
                    if (m.GunMagMulIfCast != 1f) mag *= m.GunMagMulIfCast;
                }
                s.Leech += m.GunLeech;
            }

            s.Mag = (int)System.Math.Round(mag);
            return Finish(s);
        }

        static GunStats Finish(GunStats s)
        {
            if (s.Mag < 1) s.Mag = 1;
            if (s.CastCount < 1) s.CastCount = 1;
            return s;
        }

        // ---------------- 作用域 → 目标格子 ----------------

        /// <summary>
        /// 作用域覆盖的格子下标（原型 `sh`）。
        ///
        /// `Prev` 是「向左找最近的一个子弹」，不是「左一格」——
        /// 这条差别决定了玩家能不能用中间塞被动件的方式精确控制影响对象。
        /// </summary>
        public static List<int> TargetsOf(Scope scope, int cell, SlotState slots)
        {
            var res = new List<int>();
            int n = slots.Length;

            switch (scope)
            {
                case Scope.Prev:
                    for (int i = cell - 1; i >= 0; i--)
                    {
                        var it = slots.Cells[i];
                        if (it != null && it.IsBullet) { res.Add(i); break; }
                    }
                    break;
                case Scope.Adj:
                    res.Add(cell - 1); res.Add(cell + 1);
                    break;
                case Scope.N2:
                    res.Add(cell + 1); res.Add(cell + 2);
                    break;
                case Scope.N3:
                    res.Add(cell + 1); res.Add(cell + 2); res.Add(cell + 3);
                    break;
                case Scope.P2:
                    res.Add(cell - 2); res.Add(cell - 1);
                    break;
                case Scope.All:
                    for (int i = 0; i < n; i++) if (i != cell) res.Add(i);
                    break;
            }

            var filtered = new List<int>(res.Count);
            for (int i = 0; i < res.Count; i++)
                if (res[i] >= 0 && res[i] < n) filtered.Add(res[i]);
            return filtered;
        }

        /// <summary>
        /// `Next` 作用域的件指向哪个格子：顺时针找下一个子弹。
        /// 找不到返回 -1（此时该件无目标，UI 应提示）。
        /// </summary>
        public static int NextBulletCell(int cell, SlotState slots)
        {
            int n = slots.Length;
            for (int step = 1; step < n; step++)
            {
                int i = (cell + step) % n;
                var it = slots.Cells[i];
                if (it != null && it.IsBullet) return i;
            }
            return -1;
        }

        /// <summary>
        /// 作用于某个子弹格的所有被动件（原型 `ah`）。
        /// 只统计**不是 Next** 且有实际子弹效果的件；枪械件不参与。
        /// </summary>
        public static List<ModifierDef> PassivesOn(GunDef gun, SlotState slots, int bulletCell)
        {
            var res = new List<ModifierDef>();
            for (int i = 0; i < slots.Length; i++)
            {
                var it = slots.Cells[i];
                if (it == null || it.Mod == null) continue;
                var m = it.Mod;
                if (!m.IsPassive || m.IsGunOnly) continue;
                if (TargetsOf(m.Scope, i, slots).Contains(bulletCell)) res.Add(m);
            }
            return res;
        }

        // ---------------- 弹药 ----------------

        /// <summary>
        /// 某格被读取时的耗弹（原型 `oh`）：子弹自身 cost + 吸附到它的被动件 cost。
        /// </summary>
        public static int AmmoCostOf(GunDef gun, SlotState slots, int cell)
        {
            var it = slots.Cells[cell];
            if (it == null) return 0;
            if (!it.IsBullet) return Math.Max(0, it.Mod.Cost);

            int cost = it.Bullet.Cost;
            var passives = PassivesOn(gun, slots, cell);
            for (int i = 0; i < passives.Count; i++) cost += passives[i].Cost;
            return Math.Max(0, cost);
        }

        // ---------------- 读取扫描 ----------------

        /// <summary>
        /// 从 startCell 起读一轮（原型 `lh`）。
        /// 返回本次读到的东西 + 下一个起点。
        /// </summary>
        static CastPlan Scan(GunDef gun, SlotState slots, GunStats stats, int startCell, int generation)
        {
            var plan = new CastPlan { NextPointer = startCell };
            int n = slots.Length;
            int cursor = startCell;
            int scanned = 0;
            int budget = Math.Max(1, stats.CastCount);
            var pendingMods = new List<ModifierDef>();

            while (plan.Projectiles.Count < budget && scanned < MaxCellScan)
            {
                // 找下一个「会被读取」的格子：子弹，或 Next 作用域的件
                int cell = -1;
                ChainItem hit = null;
                while (scanned < MaxCellScan)
                {
                    int cur = cursor;
                    cursor = (cursor + 1) % n;
                    if (cursor == 0) plan.Wrapped = true;
                    scanned++;

                    var it = slots.Cells[cur];
                    if (it == null) continue;
                    if (it.IsBullet) { cell = cur; hit = it; break; }
                    if (it.Mod != null && it.Mod.Scope == Scope.Next) { cell = cur; hit = it; break; }
                }

                if (hit == null) break;

                plan.UsedCells.Add(cell);
                plan.AmmoCost += AmmoCostOf(gun, slots, cell);
                plan.DelayAdd += hit.IsBullet ? hit.Bullet.Delay : hit.Mod.Delay;

                if (!hit.IsBullet)
                {
                    // Next 件：多重施放加预算，其余攒着叠给下一个子弹
                    if (hit.Mod.Multicast > 0) budget += hit.Mod.Multicast;
                    else pendingMods.Add(hit.Mod);
                    continue;
                }

                var desc = BuildDesc(gun, slots, stats, cell, pendingMods, generation);
                pendingMods = new List<ModifierDef>();
                plan.Projectiles.Add(desc);
            }

            plan.NextPointer = cursor;
            return plan;
        }

        /// <summary>
        /// 把一格子弹 + 吸附的被动件 + 前置 Next 件，结算成最终描述符（原型 `ch`）。
        /// </summary>
        static ProjectileDesc BuildDesc(GunDef gun, SlotState slots, GunStats stats,
            int cell, List<ModifierDef> pendingMods, int generation)
        {
            var def = slots.Cells[cell].Bullet;
            var d = def.ToDesc();

            // 同一件在不同位置可能出现多次，按「件 id + 层数」累乘
            var passives = PassivesOn(gun, slots, cell);
            for (int i = 0; i < passives.Count; i++) ApplyMod(d, passives[i], 1);
            for (int i = 0; i < pendingMods.Count; i++) ApplyMod(d, pendingMods[i], 1);

            // 枪械固有倍率与散射在链结算之后
            if (stats.DmgMul != 1f)
            {
                d.Damage *= stats.DmgMul;
                d.ExplodeDmg *= stats.DmgMul;
            }
            d.Spread *= stats.SpreadMul;

            if (d.Count < 1) d.Count = 1;
            if (d.Count > MaxCount) d.Count = MaxCount;

            // 触发弹：递归读 1 个载荷
            if (d.Trigger != TriggerKind.None && generation < MaxRecursion)
            {
                int payloadStart = NextBulletCell(cell, slots);
                if (payloadStart >= 0)
                {
                    var sub = Scan(gun, slots, new GunStats
                    {
                        Mag = stats.Mag,
                        Reload = stats.Reload,
                        Delay = stats.Delay,
                        DelayMul = stats.DelayMul,
                        Spread = stats.Spread,
                        SpreadMul = stats.SpreadMul,
                        CastCount = 1,
                        DmgMul = stats.DmgMul,
                    }, payloadStart, generation + 1);

                    if (sub.Projectiles.Count > 0) d.Payload = sub.Projectiles;
                }
                else
                {
                    // 没有下一个子弹可作载荷
                }
            }

            return d;
        }

        /// <summary>
        /// 把一件衍生配件的效果叠加到描述符上。t = 叠加层数。
        /// 逐条对应原型 S_ 里的 apply 闭包。
        /// </summary>
        public static void ApplyMod(ProjectileDesc d, ModifierDef m, int t)
        {
            if (m == null || t <= 0) return;

            // 乘法组
            if (m.DamageMulAdd != 0f) Mul(d, 1f + m.DamageMulAdd * t);
            if (m.SizeMulAdd != 0f) d.Size *= 1f + m.SizeMulAdd * t;
            if (m.SpeedMulAdd != 0f) d.Speed *= 1f + m.SpeedMulAdd * t;
            if (m.RangeMulAdd != 0f) d.Range *= 1f + m.RangeMulAdd * t;
            if (m.LifeMulAdd != 0f) d.Lifetime *= 1f + m.LifeMulAdd * t;
            if (m.KnockMulAdd != 0f) d.Knock *= 1f + m.KnockMulAdd * t;
            if (m.CountMulAdd != 0f)
                d.Count = (int)Math.Round(d.Count * (1f + m.CountMulAdd * t));
            if (m.RadiusMulAdd != 0f) d.Radius *= 1f + m.RadiusMulAdd * t;

            // 加法组
            if (m.PiercePer != 0)
                d.Pierce += Math.Max(1, (int)Math.Round(m.PiercePer * (float)t));
            if (m.BouncePer != 0)
                d.Bounce += Math.Max(1, (int)Math.Round(m.BouncePer * (float)t));
            if (m.HomingPer != 0f) d.Homing += m.HomingPer * t;
            if (m.BurnPer != 0f) d.Burn += m.BurnPer * t;
            if (m.SlowPer != 0f) d.Slow = Math.Max(d.Slow, m.SlowPer * Math.Min(1f, t + 0.2f));
            if (m.CritPer != 0f) d.Crit += m.CritPer * t;
            if (m.SpreadPer != 0f) d.Spread += m.SpreadPer * t;

            // 爆裂：无爆裂时按基础半径建，已有则放大
            if (m.ExplodeRBase != 0f || m.ExplodeRAdd != 0f)
            {
                if (d.ExplodeR > 0f)
                    d.ExplodeR *= 1f + m.ExplodeRAdd * t;
                else
                    d.ExplodeR = m.ExplodeRBase;

                d.ExplodeDmg += (d.Damage * m.ExplodeDmgFromDmg + m.ExplodeDmgFlat) * t;
            }
        }

        static void Mul(ProjectileDesc d, float k)
        {
            d.Damage *= k;
            d.ExplodeDmg *= k;
        }

        // ---------------- 对外：单次施法 ----------------

        /// <summary>
        /// 从 pointer 起解析一次施法（原型 `lh` + `ch`）。
        /// 不修改 slots / pointer —— 调用方按结果推进指针。
        /// </summary>
        public static CastResult ResolveCast(GunDef gun, SlotState slots, GunStats stats, int pointer)
        {
            var result = new CastResult { Slot = slots };
            if (gun == null || slots == null || slots.Length == 0) return result;

            int p = ((pointer % slots.Length) + slots.Length) % slots.Length;
            var plan = Scan(gun, slots, stats, p, 0);

            result.Projectiles.AddRange(plan.Projectiles);
            result.UsedCells.AddRange(plan.UsedCells);
            result.AmmoCost = plan.AmmoCost;
            result.DelayAdd = plan.DelayAdd;
            result.NextPointer = plan.NextPointer;
            result.Wrapped = plan.Wrapped;

            CollectWarnings(gun, slots, result);
            return result;
        }

        /// <summary>把「Next 件没有目标」这类问题收集出来，UI 提示用。</summary>
        static void CollectWarnings(GunDef gun, SlotState slots, CastResult result)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var it = slots.Cells[i];
                if (it == null || it.Mod == null) continue;
                if (it.Mod.Scope != Scope.Next) continue;

                // 只有「这次会被读到」的 Next 件才需要提示
                if (!result.UsedCells.Contains(i)) continue;
                if (NextBulletCell(i, slots) < 0)
                    result.Warnings.Add(new CastWarning
                    {
                        ItemId = it.Id,
                        CellIndex = i,
                        Code = "no-target",
                    });
            }
        }

        /// <summary>
        /// 整把枪所有格位的施法序列（原型 `Vd`），HUD 的「接下来几发」用。
        /// 只做展示，不推进真实指针。
        /// </summary>
        public static List<CastResult> FullSequence(GunDef gun, SlotState slots, GunStats stats, int maxSteps = 12)
        {
            var list = new List<CastResult>();
            int p = 0;
            for (int step = 0; step < maxSteps && step < slots.Length; step++)
            {
                var r = ResolveCast(gun, slots, stats, p);
                if (!r.Succeeded) break;

                // 与已出现的结果完全重合（绕回来了）就停
                if (r.Wrapped && r.UsedCells.Count > 0 && r.UsedCells[0] < p) break;

                list.Add(r);
                if (r.Wrapped || r.NextPointer == 0) break;
                p = r.NextPointer;
            }
            return list;
        }

        // ---------------- 装配校验 ----------------

        public enum DropResult
        {
            Ok = 0,
            NotABullet = 1,
            NoTarget = 2,
        }

        /// <summary>
        /// 某件放到某格是否合法。只拦真正会让人困惑的情况：
        /// 链里一个子弹都没有时锁死（否则玩家开火永远没反应却不知道原因）。
        /// </summary>
        public static DropResult Validate(SlotState slots, int cell, ChainItem item, out string reason)
        {
            reason = null;
            if (item == null) { reason = "空件"; return DropResult.NotABullet; }

            if (item.IsBullet) return DropResult.Ok;

            // 放被动件前检查它有没有目标
            var probe = slots.Clone();
            probe.Cells[cell] = item;
            if (item.Mod.Scope != Scope.Next && TargetsOf(item.Mod.Scope, cell, probe).Count == 0
                && !item.Mod.IsGunOnly)
            {
                reason = "此作用域在本格没有目标";
                return DropResult.NoTarget;
            }
            return DropResult.Ok;
        }
    }
}

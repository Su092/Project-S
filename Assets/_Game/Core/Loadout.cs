using System;
using System.Collections.Generic;

namespace S.Core
{
    /// <summary>一条链的装配结果（枪械级配件结算后的枪属性 + 链快照）。</summary>
    public sealed class SlotState
    {
        public readonly List<ChainItem> Chain = new List<ChainItem>();

        public bool IsEmpty => Chain.Count == 0;
        public int BulletCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Chain.Count; i++) if (Chain[i].IsBullet) n++;
                return n;
            }
        }

        public SlotState Clone()
        {
            var s = new SlotState();
            s.Chain.AddRange(Chain);
            return s;
        }
    }

    /// <summary>链上的一项在某一时刻触发。见 docs/loadout-spec.md §1.2（链式触发）。</summary>
    public enum ChainStepKind
    {
        /// <summary>子弹件触发：此刻生成投射物。</summary>
        Bullet = 0,
        /// <summary>衍生件触发：此刻把效果施加到存活的投射物上。</summary>
        Modifier = 1,
    }

    /// <summary>
    /// 链式触发的一步。整条链按**链顺序**逐项触发，相邻两步间隔 = 枪的击发间隔。
    /// </summary>
    public sealed class ChainStep
    {
        public ChainStepKind Kind;
        public int ChainIndex;

        /// <summary>子弹件：该子弹在链中的子弹序号（用于后继作用域判定）。</summary>
        public int SourceIndex;

        /// <summary>衍生件：触发时刻链内已经出现过的子弹数（后继作用域的判据）。</summary>
        public int BulletsSeen;

        /// <summary>子弹件：该子弹的初始描述符（已并入枪基础散射/溅射，**未**并入配件与枪械倍率）。</summary>
        public ProjectileDesc Bullet;

        /// <summary>衍生件：要施加的配件。</summary>
        public ModifierDef Mod;

        /// <summary>距本次施法开始的时间（秒）= ChainIndex × 击发间隔。</summary>
        public float Delay;

        /// <summary>衍生件在触发时刻是否有作用目标（false = no-target，UI 要提示）。</summary>
        public bool Effective;
    }

    /// <summary>单次施法的产物。</summary>
    public sealed class CastResult
    {
        public readonly List<ProjectileDesc> Projectiles = new List<ProjectileDesc>();

        /// <summary>
        /// 本次施法用的格位（Runtime 侧 HUD 要用它把「哪条链在跑」画出来）。可为 null（测试构造）。
        /// 放在结果里而不是让 UI 自己去 `PeekNextSlot`：施法后轮转指针已前进，再取就是下一位，
        /// HUD 会把「上一层链的触发进度」标到另一条链上。
        /// </summary>
        public SlotState Slot;

        public int AmmoCost;
        public readonly List<CastWarning> Warnings = new List<CastWarning>();

        /// <summary>
        /// 链式触发的执行计划（按链顺序，Delay 单调递增）。运行时不直接铺 Projectiles，
        /// 而是按这个计划逐项触发 —— 这是「链式逐渐触发」的唯一数据源。
        /// </summary>
        public readonly List<ChainStep> Steps = new List<ChainStep>();

        /// <summary>整条链跑完所需时间（秒）。</summary>
        public float Duration
        {
            get
            {
                float d = 0f;
                for (int i = 0; i < Steps.Count; i++) if (Steps[i].Delay > d) d = Steps[i].Delay;
                return d;
            }
        }

        public bool Succeeded => Projectiles.Count > 0;

        /// <summary>整次施法的期望伤害总和（所有投射物）。</summary>
        public float ExpectedDamage
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < Projectiles.Count; i++) sum += Projectiles[i].ExpectedDamage;
                return sum;
            }
        }
    }

    public struct CastWarning
    {
        public string ItemId;
        public int ChainIndex;
        public string Code;   // "no-target" 等
    }

    /// <summary>拖拽落点预览结果。见 docs/loadout-spec.md §3.3。</summary>
    public struct InsertPreview
    {
        /// <summary>null 表示可以插入；否则是拒绝码（same-limit / chain-full / bad-index）。</summary>
        public string RejectCode;

        public bool CanInsert => RejectCode == null;

        public float BaseDamage;        // 插入前的整次施法期望
        public float NewDamage;         // 插入后的整次施法期望
        public int BaseProjectiles;
        public int NewProjectiles;
        public int BaseAmmoCost;
        public int NewAmmoCost;

        /// <summary>该件实际作用到的投射物数量（衍生件）／1（子弹或枪械件）。0 = 白放。</summary>
        public int AffectedCount;

        /// <summary>是否真的产生效果。false 表示落点会变红并提示「无作用目标」。</summary>
        public bool Effective;

        /// <summary>枪械作用域（不作用于投射物，装配时改枪属性）。</summary>
        public bool IsGunScope;

        /// <summary>该位置是否会产生 no-target 警告。</summary>
        public bool HasNoTarget;

        /// <summary>期望伤害变化率。BaseDamage 为 0 时返回 0。</summary>
        public float DamageDeltaRatio =>
            BaseDamage <= 0.0001f ? 0f : (NewDamage - BaseDamage) / BaseDamage;

        /// <summary>期望伤害变化量。</summary>
        public float DamageDelta => NewDamage - BaseDamage;
    }

    /// <summary>
    /// 装配与施法结算器。**纯逻辑、零 Unity 依赖、无随机**——
    /// 这样整条规则可以在 dotnet 下逐条断言（见 tools/loadout-tests）。
    /// 规则出处：docs/loadout-spec.md
    /// </summary>
    public static class Loadout
    {
        public const int MaxChain = 6;
        public const int MaxSameIdPerChain = 2;
        public const int MaxSplitGeneration = 3;

        // ---------------- 装配校验 ----------------

        /// <summary>尝试把 item 插入 slot 的 index（-1 表示追加）。返回 null 表示成功，否则返回拒绝码。</summary>
        public static string ValidateInsert(SlotState slot, ChainItem item, int index)
        {
            if (item == null) return "null-item";
            if (slot.Chain.Count >= MaxChain) return "chain-full";

            if (index < 0 || index > slot.Chain.Count) return "bad-index";

            // 同名上限：同一链内同名最多 2 个
            int same = 0;
            for (int i = 0; i < slot.Chain.Count; i++)
                if (slot.Chain[i].Id == item.Id) same++;
            if (same >= MaxSameIdPerChain) return "same-limit";

            return null;
        }

        public static bool Insert(SlotState slot, ChainItem item, int index = -1)
        {
            if (ValidateInsert(slot, item, index < 0 ? slot.Chain.Count : index) != null) return false;
            if (index < 0 || index > slot.Chain.Count) index = slot.Chain.Count;
            slot.Chain.Insert(index, item);
            return true;
        }

        // ---------------- 枪械级结算 ----------------

        /// <summary>
        /// 把枪械作用域配件并入枪属性。装配/卸下/换枪时调用一次，**不在施法时调用**。
        /// 同名只生效第一个（loadout-spec §5.3）。
        /// </summary>
        public static GunDef ResolveGun(GunDef baseGun, IList<SlotState> slots)
        {
            var g = new GunDef
            {
                Id = baseGun.Id, Name = baseGun.Name, Kind = baseGun.Kind,
                SlotCount = baseGun.SlotCount, FireInterval = baseGun.FireInterval,
                MagSize = baseGun.MagSize, ReloadTime = baseGun.ReloadTime,
                SpreadBase = baseGun.SpreadBase, MuzzleSpeed = baseGun.MuzzleSpeed,
                DamageMul = baseGun.DamageMul, SplashRadius = baseGun.SplashRadius,
                Mode = baseGun.Mode, Gravity = baseGun.Gravity,
            };

            var seen = new HashSet<string>();
            float spreadMul = 1f;

            for (int s = 0; s < slots.Count; s++)
            {
                var chain = slots[s].Chain;
                for (int i = 0; i < chain.Count; i++)
                {
                    if (chain[i].IsBullet) continue;
                    var m = chain[i].Mod;
                    if (m.Scope != Scope.Gun) continue;
                    if (!seen.Add(m.Id)) continue;   // 同名只生效一次

                    g.MagSize += m.GunMagAdd;
                    g.FireInterval *= m.GunFireIntervalMul;
                    g.ReloadTime *= m.GunReloadMul;
                    spreadMul *= m.GunSpreadMul;
                }
            }

            // 钳制（loadout-spec §5.3）
            if (g.MagSize < 1) g.MagSize = 1;
            if (g.FireInterval < 0.05f) g.FireInterval = 0.05f;
            if (g.ReloadTime < 0.1f) g.ReloadTime = 0.1f;
            if (spreadMul < 0.2f) spreadMul = 0.2f;
            if (spreadMul > 2f) spreadMul = 2f;
            g.SpreadBase *= spreadMul;

            return g;
        }

        // ---------------- 施法结算 ----------------

        /// <summary>
        /// 解析一个配件位的链，得到本次施法的全部投射物。
        /// 严格按 docs/loadout-spec.md §3 的算法实现，顺序不可调整。
        /// </summary>
        public static CastResult ResolveCast(GunDef gun, SlotState slot)
        {
            var result = new CastResult();
            result.Slot = slot;
            if (gun == null || slot == null || slot.Chain.Count == 0) return result;

            // 1) 为链中每个子弹配件生成描述符（sourceIndex = 链内子弹序号）
            var descs = new List<ProjectileDesc>();
            int bulletOrdinal = 0;
            for (int i = 0; i < slot.Chain.Count; i++)
            {
                if (!slot.Chain[i].IsBullet) continue;
                var d = slot.Chain[i].Bullet.ToDesc();
                d.SourceIndex = bulletOrdinal++;
                d.SpreadAdd += gun.SpreadBase;   // 枪基础散射在链结算前并入（§5.1）
                // 枪基础溅射取大者（§5.1）：榴弹枪让任何子弹都具备爆炸
                if (gun.SplashRadius > d.SplashRadius) d.SplashRadius = gun.SplashRadius;
                descs.Add(d);
            }

            // 2) 按链顺序逐项施法
            int bulletsSeen = 0;
            for (int i = 0; i < slot.Chain.Count; i++)
            {
                var item = slot.Chain[i];
                if (item.IsBullet) { bulletsSeen++; continue; }

                var mod = item.Mod;
                if (mod.Scope == Scope.Gun) continue;   // 枪械级已单独结算

                var targets = SelectTargets(descs, mod, bulletsSeen);
                if (targets.Count == 0)
                {
                    result.Warnings.Add(new CastWarning
                    {
                        ItemId = mod.Id, ChainIndex = i, Code = "no-target",
                    });
                    continue;
                }

                var next = new List<ProjectileDesc>(descs.Count);
                for (int t = 0; t < descs.Count; t++)
                {
                    var d = descs[t];
                    if (targets.Contains(t)) ApplyMod(d, mod, ref next);
                    else next.Add(d);
                }
                descs = next;
            }

            // 3) 枪械固有伤害倍率：链结算之后（§5.1）
            for (int i = 0; i < descs.Count; i++) descs[i].Damage *= gun.DamageMul;

            // 4) 弹药消耗
            result.AmmoCost = ComputeAmmo(gun, slot);
            result.Projectiles.AddRange(descs);
            BuildSteps(gun, slot, result);
            return result;
        }

        /// <summary>
        /// 生成链式触发的执行计划（loadout-spec §1.2）。
        ///
        /// 语义：链内每一项按**链顺序**逐项触发，相邻两步间隔 = 枪的 `fireInterval`。
        ///  - 子弹件触发：在那一刻生成它的投射物；
        ///  - 衍生件触发：作用到**那一刻活着的**投射物，并且对其后生成的子弹同样生效。
        ///
        /// 为什么必须「对后生成的子弹也生效」：规格 §3 的结算域里，链内所有子弹在第一步就都在
        /// `ctx` 里了 —— 所以 `[量子分裂, 制式弹]` 的分裂能作用到后面那颗粒子。
        /// 若运行时只改「此刻已经在飞的」，`[校准套件, 制式弹]` 这种最常见的链会少算一段，
        /// 总伤害与 `ResolveCast` 对不上（HUD 的期望值会骗人）。
        ///
        /// 因此运行时按「已触发配件清单」执行：子弹生成时先补上在它之前触发、且按作用域
        /// 确实作用于它的配件；之后触发的配件再实时改活着的实例。两条路径合起来，
        /// 每颗粒子得到的配件序列与全量结算**完全相同**（顺序也相同），总量必然一致。
        ///
        /// 子弹件携带的是「已并入枪基础项、已乘枪械倍率、未加任何配件」的模板描述符；
        /// 配件由 `ApplyModRuntime` 施加，它会把枪械倍率暂时除掉再乘回，与全量结算同口径。
        /// </summary>
        static void BuildSteps(GunDef gun, SlotState slot, CastResult result)
        {
            if (gun == null || slot == null) return;

            float dt = gun.FireInterval > 0f ? gun.FireInterval : 0f;

            // 每个子弹件的模板（按键内子弹序号索引）
            var templates = new List<ProjectileDesc>();
            int ord = 0;
            for (int i = 0; i < slot.Chain.Count; i++)
            {
                if (!slot.Chain[i].IsBullet) continue;
                var d = slot.Chain[i].Bullet.ToDesc();
                d.SourceIndex = ord++;
                d.SpreadAdd += gun.SpreadBase;
                if (gun.SplashRadius > d.SplashRadius) d.SplashRadius = gun.SplashRadius;
                d.Damage *= gun.DamageMul;   // 与 ResolveCast 同口径（链结算之后乘，这里提前并入模板）
                templates.Add(d);
            }

            // 计划：按链顺序（子弹件与衍生件都在时间轴上占一格）
            int seen = 0;
            int bulletsBefore = 0;
            for (int i = 0; i < slot.Chain.Count; i++)
            {
                var item = slot.Chain[i];
                if (item.IsBullet)
                {
                    result.Steps.Add(new ChainStep
                    {
                        Kind = ChainStepKind.Bullet,
                        ChainIndex = i,
                        Delay = i * dt,
                        SourceIndex = seen,
                        Bullet = templates[seen].Clone(),
                    });
                    seen++;
                    bulletsBefore++;
                    continue;
                }

                var mod = item.Mod;
                if (mod.Scope == Scope.Gun) continue;

                var step = new ChainStep
                {
                    Kind = ChainStepKind.Modifier,
                    ChainIndex = i,
                    Delay = i * dt,
                    Mod = mod,
                    BulletsSeen = bulletsBefore,
                };

                // 生效判定与 SelectTargets 同判据（全场 = 全部子弹；后继 = 子弹序号 ≥ 已出现数）
                int affected = 0;
                for (int b = 0; b < templates.Count; b++)
                {
                    if (mod.Scope == Scope.Whole || templates[b].SourceIndex >= bulletsBefore)
                        affected++;
                }
                step.Effective = affected > 0;
                result.Steps.Add(step);
            }
        }

        /// <summary>
        /// 把单个衍生件作用到单个已存在的投射物上（运行时链式触发用）。
        ///
        /// 返回**替换后的列表**：分裂会让 1 个变 n 个，调用方必须用返回值替换原元素。
        /// 与 <see cref="ApplyMod"/> 共享同一套加法→乘法→钳制顺序，不能各写一份。
        ///
        /// 注意枪械伤害倍率：`ApplyMod` 是在 damageMul **之前**执行的，而运行时触发时
        /// 子弹的伤害已经乘过 damageMul。这里先把倍率除掉、作用完再乘回去，
        /// 保证「同一颗子弹无论走全量结算还是链式触发，数值完全一致」。
        /// </summary>
        public static List<ProjectileDesc> ApplyModRuntime(ProjectileDesc d, ModifierDef m, GunDef gun)
        {
            var single = new List<ProjectileDesc>(1);
            if (d == null || m == null) { single.Add(d); return single; }

            float mul = gun != null ? gun.DamageMul : 1f;
            if (mul <= 0.0001f) mul = 1f;

            // 还原到「链结算前」的口径（ApplyMod 期望的是未乘枪械倍率的伤害）
            var work = d.Clone();
            work.Damage /= mul;

            var next = new List<ProjectileDesc>(m.SplitCount > 1 ? m.SplitCount : 1);
            ApplyMod(work, m, ref next);

            // 乘回枪械倍率
            for (int i = 0; i < next.Count; i++) next[i].Damage *= mul;
            return next;
        }

        /// <summary>
        /// 选出该衍生配件作用的描述符下标集合（loadout-spec §3.1）。
        /// Whole = 当前全部；After = sourceIndex ≥ bulletsSeen 的全部。
        /// </summary>
        static List<int> SelectTargets(List<ProjectileDesc> descs, ModifierDef mod, int bulletsSeen)
        {
            var idx = new List<int>();
            for (int i = 0; i < descs.Count; i++)
            {
                if (mod.Scope == Scope.Whole) idx.Add(i);
                else if (mod.Scope == Scope.After && descs[i].SourceIndex >= bulletsSeen) idx.Add(i);
            }
            return idx;
        }

        /// <summary>把一个衍生配件作用到单个描述符（先加法后乘法，再钳制，loadout-spec §3.2）。</summary>
        static void ApplyMod(ProjectileDesc d, ModifierDef m, ref List<ProjectileDesc> output)
        {
            // --- 分裂：产出多个子体，不进入其他加乘流程 ---
            if (m.SplitCount > 1 && d.Generation < MaxSplitGeneration)
            {
                for (int k = 0; k < m.SplitCount; k++)
                {
                    var c = d.Clone();
                    c.Generation = d.Generation + 1;
                    c.Damage *= m.SplitDamageMul;
                    c.SpreadAdd += m.SplitSpreadAdd;
                    c.Lifetime *= m.SplitLifetimeMul;
                    Clamp(c);
                    output.Add(c);
                }
                return;
            }

            // --- 加法 ---
            d.Damage += m.DamageFlat;
            d.Crit += m.CritFlat;
            d.CritDmg += m.CritDmgFlat;
            d.SpreadAdd += m.SpreadAddFlat;
            d.Lifetime += m.LifetimeFlat;
            d.Pierce += m.PierceFlat;
            d.Bounce += m.BounceFlat;

            // --- 乘法 ---
            d.Damage *= m.DamageMul;
            d.Crit *= m.CritMul;
            d.SpreadMul *= m.SpreadMul;
            d.Lifetime *= m.LifetimeMul;
            d.SpeedMul *= m.SpeedMul;

            // --- 行为 ---
            d.SplashRadius += m.SplashAdd;
            d.HomingRate += m.HomingRate;

            Clamp(d);
            output.Add(d);
        }

        static void Clamp(ProjectileDesc d)
        {
            if (d.SpreadAdd < 0f) d.SpreadAdd = 0f;
            if (d.SpreadMul < 0f) d.SpreadMul = 0f;
            if (d.Lifetime < 0.05f) d.Lifetime = 0.05f;
            if (d.Crit > 1f) d.Crit = 1f;
            if (d.Crit < 0f) d.Crit = 0f;
            if (d.CritDmg < 1f) d.CritDmg = 1f;
            if (d.SpeedMul < 0.2f) d.SpeedMul = 0.2f;
            if (d.SpeedMul > 4f) d.SpeedMul = 4f;
            if (d.Damage < 0f) d.Damage = 0f;
            if (d.Pierce < 0) d.Pierce = 0;
            if (d.Bounce < 0) d.Bounce = 0;
        }

        /// <summary>弹药消耗 = Σ子弹 ammo + 加法修正，乘乘法修正，最低 1（loadout-spec §4）。</summary>
        public static int ComputeAmmo(GunDef gun, SlotState slot)
        {
            float raw = 0f;
            float mul = 1f;
            for (int i = 0; i < slot.Chain.Count; i++)
            {
                var it = slot.Chain[i];
                if (it.IsBullet) { raw += it.Bullet.Ammo; continue; }
                var m = it.Mod;
                if (m.Scope == Scope.Gun) continue;
                raw += m.AmmoFlat;
                // 本项目没有弹药乘法件；字段保留供后续扩展
            }
            raw *= mul;
            int n = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        /// <summary>该位被轮转到的频率倍数（slotCount 位轮转，每 fireInterval 一次施法）。</summary>
        public static float CastsPerSecond(GunDef resolvedGun) =>
            resolvedGun.FireInterval <= 0f ? 0f : 1f / resolvedGun.FireInterval;

        /// <summary>单个位的持续 DPS = 该次施法期望 × 施法频率 / 位数。</summary>
        public static float SlotDps(GunDef resolvedGun, CastResult cast) =>
            cast.ExpectedDamage * CastsPerSecond(resolvedGun) / resolvedGun.SlotCount;

        // ---------------- 装配预览（拖拽落点用） ----------------

        /// <summary>
        /// 把 item 插到 slot 的 index 处会有什么后果。**不修改 slot**，只算结果。
        /// 这是拖拽预览的数据源：UI 必须显示「生效目标数」与「期望伤害变化」，
        /// 否则「顺序即解法」这条核心规则玩家无法感知（见 docs/loadout-spec.md §3.3）。
        /// </summary>
        public static InsertPreview PreviewInsert(GunDef resolvedGun, SlotState slot, ChainItem item, int index)
        {
            var p = new InsertPreview();

            // 装配合法性
            p.RejectCode = ValidateInsert(slot, item, index < 0 ? slot.Chain.Count : index);
            if (p.RejectCode != null) return p;

            int at = index < 0 || index > slot.Chain.Count ? slot.Chain.Count : index;

            // 基线：当前链的期望伤害与弹丸数
            var before = ResolveCast(resolvedGun, slot);
            p.BaseDamage = before.ExpectedDamage;
            p.BaseProjectiles = before.Projectiles.Count;

            // 试插（用副本，绝不改原链）
            var probe = slot.Clone();
            probe.Chain.Insert(at, item);

            var after = ResolveCast(resolvedGun, probe);
            p.NewDamage = after.ExpectedDamage;
            p.NewProjectiles = after.Projectiles.Count;
            p.NewAmmoCost = after.AmmoCost;
            p.BaseAmmoCost = before.AmmoCost;

            // 该件是否真的影响了东西
            p.IsGunScope = !item.IsBullet && item.Mod != null && item.Mod.Scope == Scope.Gun;
            if (item.IsBullet)
            {
                // 子弹本身总是「有效」（它就是要被装填的东西）
                p.AffectedCount = 1;
                p.Effective = true;
            }
            else if (p.IsGunScope)
            {
                p.AffectedCount = 1;   // 枪械件作用于整把枪
                p.Effective = true;
            }
            else
            {
                // 衍生件：数它在这个位置实际作用到几个投射物
                p.AffectedCount = CountAffected(resolvedGun, probe, at);
                p.Effective = p.AffectedCount > 0;
            }

            // 是否产生 no-target 警告（用于把落点标红）
            p.HasNoTarget = false;
            for (int i = 0; i < after.Warnings.Count; i++)
                if (after.Warnings[i].ChainIndex == at) { p.HasNoTarget = true; break; }

            return p;
        }

        /// <summary>
        /// 数某个位置的衍生件实际作用到几个投射物上。
        /// **必须与 ResolveCast 同序**：先把链内全部子弹入列，再逐项施加修正。
        /// 早期版本在同一个循环里边入列边施法，导致「修正在子弹之前」时目标集恒为 0（已修）。
        /// </summary>
        static int CountAffected(GunDef gun, SlotState probe, int at)
        {
            if (at < 0 || at >= probe.Chain.Count) return 0;
            var item = probe.Chain[at];
            if (item.IsBullet || item.Mod == null) return 0;
            var mod = item.Mod;
            if (mod.Scope == Scope.Gun) return 1;

            // 第一遍：全部子弹入列（与 ResolveCast 一致）
            var descs = new List<ProjectileDesc>();
            int bulletOrdinal = 0;
            for (int i = 0; i < probe.Chain.Count; i++)
            {
                if (!probe.Chain[i].IsBullet) continue;
                var d = probe.Chain[i].Bullet.ToDesc();
                d.SourceIndex = bulletOrdinal++;
                d.SpreadAdd += gun.SpreadBase;
                if (gun.SplashRadius > d.SplashRadius) d.SplashRadius = gun.SplashRadius;
                descs.Add(d);
            }

            // 第二遍：按链顺序施加修正，到达 at 时统计目标集
            int bulletsSeen = 0;
            for (int i = 0; i < probe.Chain.Count; i++)
            {
                var it = probe.Chain[i];
                if (it.IsBullet) { bulletsSeen++; continue; }

                var m = it.Mod;
                if (m.Scope == Scope.Gun) continue;

                var targets = new List<int>();
                for (int t = 0; t < descs.Count; t++)
                {
                    if (m.Scope == Scope.Whole) targets.Add(t);
                    else if (descs[t].SourceIndex >= bulletsSeen) targets.Add(t);
                }

                if (i == at) return targets.Count;

                if (targets.Count == 0) continue;

                var next = new List<ProjectileDesc>(descs.Count);
                for (int t = 0; t < descs.Count; t++)
                {
                    if (targets.Contains(t)) ApplyMod(descs[t], m, ref next);
                    else next.Add(descs[t]);
                }
                descs = next;
            }

            return 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using S.Core;

namespace LoadoutTests
{
    /// <summary>
    /// 结算引擎回归断言。直接链接 Assets/_Game/Core 源码运行。
    ///
    /// 判据纪律：期望值**写死在这里**，不从 sdata.json 反算。
    /// 数据被改坏时必须变红 —— 否则「数据 + 断言一起改」就永远绿，
    /// 这正是这类项目最容易烂掉的地方。
    ///
    /// 本轮重写：链模型从「时间序列」换成「读取式扫描」（docs/loadout-spec.md §3）。
    /// 旧断言全部作废，新断言按原型规则逐条覆盖。
    /// </summary>
    static class Program
    {
        static int s_pass, s_fail;
        static readonly List<string> s_fails = new List<string>();
        static SData D;

        static int Main()
        {
            var dir = FindCfgDir();
            Console.WriteLine("CFG " + dir);

            // 走和运行时**同一份**装配 + 校验路径（SCfgLoader → SCfgValidator）。
            // 旧版是 `SData.Parse(json)`，于是「编辑器里能跑、dotnet 断言却读另一套解析」
            // 成了可能 —— 现在两边只有一个加载器。
            try
            {
                D = SCfgLoader.Load(new SCfgDirectorySource(dir));
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL 配置装配失败：" + ex.Message);
                return 1;
            }
            Console.WriteLine($"LOADED guns={D.GunOrder.Count} bullets={D.BulletOrder.Count} " +
                              $"mods={D.ModifierOrder.Count} enemies={D.EnemyOrder.Count}");

            CfgCases();
            DataCases();
            ScopeCases();
            ScopePreviewCases();
            // Scope 测试会修改共享定义，重新加载避免污染其余断言。
            D = SCfgLoader.Load(new SCfgDirectorySource(dir));
            PhysicalScopeCastCases();
            D = SCfgLoader.Load(new SCfgDirectorySource(dir));
            WeightsCases();
            ScanCases();
            LoadoutRuleCases();
            TriggerCases();
            GunStatCases();
            SequenceCases();
            BagCases();
            GunOfferCases();
            TimelineCases();
            RestartCases();
            BossCases();
            MusicCombatCases.Run(Check);
            D = SCfgLoader.Load(new SCfgDirectorySource(dir));
            FusionCases.Run(D, Check);
            ScoreCases.Run(D, Check);

            if (HasFlag("-bench"))
            {
                Bench.RunAll(D);
                XpBench.Run(D);
            }

            Console.WriteLine();
            Console.WriteLine($"RESULT {s_pass}/{s_pass + s_fail} passed");
            if (s_fail > 0)
            {
                Console.WriteLine("FAILURES:");
                foreach (var f in s_fails) Console.WriteLine("  - " + f);
                return 1;
            }
            return 0;
        }

        static void GunOfferCases()
        {
            Section("枪械奖励");
            var owned = new HashSet<string> { "rifle", "smg" };
            var a = new RunState(D, 417);
            var b = new RunState(D, 417);
            int gunRolls = 0;
            bool shape = true, excludes = true, deterministic = true, allAvailable = true;
            var seen = new HashSet<string>();
            for (int i = 0; i < 10000; i++)
            {
                var aa = a.Roll(3, owned); var bb = b.Roll(3, owned);
                int count = 0;
                shape &= aa.Count == 3;
                for (int j = 0; j < aa.Count; j++)
                {
                    deterministic &= aa[j].Id == bb[j].Id && aa[j].IsGun == bb[j].IsGun;
                    if (!aa[j].IsGun) continue;
                    count++; seen.Add(aa[j].Id);
                    excludes &= !owned.Contains(aa[j].Id);
                    allAvailable &= D.Guns.ContainsKey(aa[j].Id);
                }
                shape &= count <= 1;
                if (count > 0) gunRolls++;
            }
            Check("每次三选一最多一把枪", shape);
            Check("排除已持有枪", excludes);
            Check("只生成有效枪且所有未持有枪都可出现", allAvailable && seen.Count == D.GunOrder.Count - 2);
            Check("相同种子枪械奖励一致", deterministic);
            Check("枪械出现率约10%", gunRolls >= 850 && gunRolls <= 1150, gunRolls.ToString());
            var all = new HashSet<string>(D.GunOrder.ConvertAll(g => g.Id));
            bool noGun = true;
            for (int i = 0; i < 100; i++)
                foreach (var o in a.Roll(3, all)) noGun &= !o.IsGun;
            Check("全部持有后只出配件", noGun);
            noGun = true;
            for (int i = 0; i < 100; i++)
                foreach (var o in a.Roll()) noGun &= !o.IsGun;
            Check("旧版纯配件抽取接口保持兼容", noGun);
            Check("零候选不消耗奖励", a.Roll(0, owned).Count == 0);
        }

        // ---------------- 数据 ----------------

        static void DataCases()
        {
            Section("数据契约（data-schema.md）");

            Check("6 把枪", D.GunOrder.Count == 6, $"{D.GunOrder.Count}");
            Check("17 个子弹配件", D.BulletOrder.Count == 17, $"{D.BulletOrder.Count}");
            Check("34 个衍生配件", D.ModifierOrder.Count == 34, $"{D.ModifierOrder.Count}");

            Check("步枪 8 槽", D.Guns["rifle"].SlotCount == 8, $"{D.Guns["rifle"].SlotCount}");
            Check("冲锋枪 10 槽", D.Guns["smg"].SlotCount == 10, $"{D.Guns["smg"].SlotCount}");
            Check("霰弹枪读取 2 发", D.Guns["shotgun"].CastCount == 2, $"{D.Guns["shotgun"].CastCount}");
            Check("发射器 12 槽", D.Guns["launcher"].SlotCount == 12, $"{D.Guns["launcher"].SlotCount}");
            Check("狙击枪零散布", D.Guns["sniper"].SpreadBase == 0f, $"{D.Guns["sniper"].SpreadBase}");

            Check("标准弹伤害 12", Near(D.Bullets["std"].Damage, 12f), $"{D.Bullets["std"].Damage}");
            Check("穿甲弹穿透 4", D.Bullets["ap"].Pierce == 4, $"{D.Bullets["ap"].Pierce}");
            Check("霰弹数量 6", D.Bullets["pellet"].Count == 6, $"{D.Bullets["pellet"].Count}");
            Check("激光是 Beam", D.Bullets["laser"].Kind == BulletKind.Beam, $"{D.Bullets["laser"].Kind}");
            Check("榴弹受重力", D.Bullets["grenade"].Gravity > 0f, $"{D.Bullets["grenade"].Gravity}");
            Check("触发弹为命中触发", D.Bullets["trigger"].Trigger == TriggerKind.Hit, $"{D.Bullets["trigger"].Trigger}");
            Check("消散弹为消散触发", D.Bullets["expire"].Trigger == TriggerKind.Expire, $"{D.Bullets["expire"].Trigger}");

            // 每把枪的 baseBullet 必须能查到
            bool allBase = true;
            foreach (var g in D.GunOrder)
                if (!D.Bullets.ContainsKey(g.BaseBullet)) allBase = false;
            Check("每把枪的 baseBullet 都能查到", allBase);

            // 起手槽位：第 1 格有子弹
            var st = D.BuildStarterSlots("rifle");
            Check("起手第 1 格有子弹", st.Cells[0] != null && st.Cells[0].IsBullet);
            Check("起手其余格为空", st.Cells[1] == null && st.Cells[7] == null);
        }

        // ---------------- 配置驱动（Cfg/*.tsv） ----------------

        static void CfgCases()
        {
            Section("配置驱动（Cfg/*.tsv → SData）");

            // ---- 敌人表 ----
            Check("敌人表 ≥4 种", D.EnemyOrder.Count >= 4, $"{D.EnemyOrder.Count}");
            Check("Crawler 存在且为 Chaser",
                D.Enemy("Crawler") != null && D.Enemy("Crawler").Ai == EnemyAi.Chaser);
            Check("Bomber 有攻击距离（能引爆）",
                D.Enemy("Bomber") != null && D.Enemy("Bomber").AttackRange > 0f,
                D.Enemy("Bomber")?.AttackRange.ToString() ?? "null");
            Check("Turret 攻击距离 > 0（能开火）",
                D.Enemy("Turret") != null && D.Enemy("Turret").AttackRange > 0f);

            // 每种敌人的关键字段都必须有值 —— 漏填会让敌人在场上静止或零伤
            foreach (var e in D.EnemyOrder)
            {
                bool ok = e.Hp > 0f && e.Damage > 0f && e.Height > 0f && e.Xp > 0
                          && !string.IsNullOrEmpty(e.Model);
                Check($"敌人 {e.Id} 字段完整", ok,
                    $"hp={e.Hp} dmg={e.Damage} h={e.Height} xp={e.Xp} model={e.Model}");
            }

            // 有盾的敌人必须两种系数都 <1，否则「破柱」这件事在数值上看不见
            var shield = D.Enemy("Brute");
            if (shield != null && shield.HasShield)
                Check("盾卫两种减伤系数都生效",
                    shield.ShieldMul < 1f && shield.ShieldPierceMul < 1f && shield.ShieldPierceMul > shield.ShieldMul,
                    $"mul={shield.ShieldMul} pierce={shield.ShieldPierceMul}");

            // ---- 起手枪 ----
            var starters = D.StarterGunIds();
            Check("起手枪来自 starter 列", starters.Count == 2, string.Join(",", starters));
            Check("起手枪都有模型名", starters.TrueForAll(id => !string.IsNullOrEmpty(D.Guns[id].Model)));

            // ---- 枪模：每种枪都必须能定位到一个模型（旧版是 switch 的 default 兜底，
            //      新增一把枪会静默显示成「自动步枪」，肉眼极难发现） ----
            foreach (var g in D.GunOrder)
                Check($"枪 {g.Id} 有模型名", !string.IsNullOrEmpty(g.Model), g.Model);

            // ---- 玩家成长曲线（原先写死在 RunState.NeedFor） ----
            Check("1 级需要 15 经验", D.XpNeedFor(1) == 15, $"{D.XpNeedFor(1)}");
            Check("2 级需要 26 经验", D.XpNeedFor(2) == 26, $"{D.XpNeedFor(2)}");
            Check("10 级需要 258 经验", D.XpNeedFor(10) == 258, $"{D.XpNeedFor(10)}");

            // ---- Boss 默认值必须与 BossState 的出厂默认一致 ----
            // BossState 有一份不读表的默认值（dotnet 断言直接 new BossState(hp)），
            // 两边不一致会导致「测试绿、实机是另一套节奏」。
            var bd = D.Boss;
            var bs = new BossState(100f);
            Check("Boss 柱数与 BossState 默认一致", bd.Pillars == bs.PillarCount,
                $"{bd.Pillars} vs {bs.PillarCount}");
            Check("Boss 柱血量与 BossState 默认一致", Near(bd.PillarHp, bs.PillarHp),
                $"{bd.PillarHp} vs {bs.PillarHp}");
            Check("Boss 护盾周期与 BossState 默认一致", Near(bd.ShieldPeriod, bs.ShieldPeriod),
                $"{bd.ShieldPeriod} vs {bs.ShieldPeriod}");
            Check("Boss 破防时长与 BossState 默认一致", Near(bd.BreachTime, bs.PillarBreachTime),
                $"{bd.BreachTime} vs {bs.PillarBreachTime}");

            // 数据注入必须真的生效（否则上面那些「一致」是摆设）
            var tuned = new BossState(100f, new BossDef
            {
                Pillars = 2, PillarHp = 100f, BreachTime = 1f, BreachDamageMul = 3f,
                ShieldPeriod = 10f, ShieldUpTime = 2f,
                SummonInterval = 3f, SummonCount = 5, ShockwaveInterval = 7f,
            });
            Check("BossDef 注入生效（柱数）", tuned.PillarCount == 2, $"{tuned.PillarCount}");
            Check("BossDef 注入生效（柱血）", Near(tuned.PillarHpAt(0), 100f), $"{tuned.PillarHpAt(0)}");
            Check("BossDef 注入生效（易伤）", Near(tuned.DamageTakenMul, 1f));
            tuned.DamagePillar(0, 100f);
            Check("BossDef 注入生效（破防倍率）", Near(tuned.DamageTakenMul, 3f), $"{tuned.DamageTakenMul}");

            // ---- 威胁缩放：数值来自 Player.tsv，不再是 RunState 里的字面量 ----
            var run = new RunState(D, 1u) { Threat = 100f };
            Check("威胁 100 时血量缩放 1.8", Near(run.HpScale, 1.8f), $"{run.HpScale:0.000}");
            Check("威胁 100 时伤害缩放 1.4", Near(run.DmgScale, 1.4f), $"{run.DmgScale:0.000}");

            // ---- 配置校验器：故意改坏一张表必须被拦下 ----
            CfgValidatorCases();
        }

        /// <summary>
        /// 校验器必须真的会红。这里做「最小破坏」的负例：把模板抄一份，只改一处，
        /// 断言 SCfgLoader.Load 抛异常。若这里全绿，说明校验器形同虚设。
        /// </summary>
        static void CfgValidatorCases()
        {
            var dir = FindCfgDir();

            // 负例 1：时间轴引用一个不存在的敌人 id
            var mem = SCfgMemorySource.CopyOf(new SCfgDirectorySource(dir));
            var timeline = mem.TryGetText(SCfgAddresses.Timeline, out var tl) ? tl : null;
            if (timeline != null)
            {
                var broken = timeline.Replace("Crawler", "NoSuchEnemy");
                mem.Set(SCfgAddresses.Timeline, broken);
                Check("引用不存在的敌人被拦下", Throws(() => SCfgLoader.Load(mem)));
            }

            // 负例 2：枪的 baseBullet 指向不存在的子弹
            var mem2 = SCfgMemorySource.CopyOf(new SCfgDirectorySource(dir));
            if (mem2.TryGetText(SCfgAddresses.Gun, out var gunTsv))
            {
                mem2.Set(SCfgAddresses.Gun, gunTsv.Replace("\tstd\t", "\tno_such_bullet\t"));
                Check("baseBullet 不存在被拦下", Throws(() => SCfgLoader.Load(mem2)));
            }

            // 负例 3：缺一张必需表
            var mem3 = SCfgMemorySource.CopyOf(new SCfgDirectorySource(dir));
            mem3.Set(SCfgAddresses.Enemy, string.Empty);
            Check("空敌人表被拦下", Throws(() => SCfgLoader.Load(mem3)));

            // 正例：原样复制必须能过
            var mem4 = SCfgMemorySource.CopyOf(new SCfgDirectorySource(dir));
            Check("整份复制能通过校验", !Throws(() => SCfgLoader.Load(mem4)));
        }

        static bool Throws(Action a)
        {
            try { a(); return false; }
            catch { return true; }
        }

        // ---------------- 作用域 ----------------

        static void ScopeCases()
        {
            Section("作用域取目标（loadout-spec §2）");

            var s = new SlotState(8);
            // 0:被动Prev 1:被动Adj 2:子弹 3:子弹 4:空 5:子弹 6:空 7:空
            s.Cells[0] = D.MakeItem("dmg");     // 默认 Next，先当占位
            s.Cells[1] = D.MakeItem("pierce");
            s.Cells[2] = D.MakeItem("std");
            s.Cells[3] = D.MakeItem("std");
            s.Cells[5] = D.MakeItem("std");

            // Prev：严格前一格
            var t = Loadout.TargetsOf(Scope.Prev, 3, s);
            Check("Prev 从 3 找到 2", t.Count == 1 && t[0] == 2, string.Join(",", t));

            // Adj：左右各一格
            t = Loadout.TargetsOf(Scope.Adj, 3, s);
            Check("Adj 覆盖 2 与 4", t.Contains(2) && t.Contains(4) && t.Count == 2, string.Join(",", t));

            // N2：右侧两格
            t = Loadout.TargetsOf(Scope.N2, 2, s);
            Check("N2 覆盖 3 与 4", t.Contains(3) && t.Contains(4), string.Join(",", t));

            // N3：右侧三格
            t = Loadout.TargetsOf(Scope.N3, 2, s);
            Check("N3 覆盖 3/4/5", t.Contains(3) && t.Contains(4) && t.Contains(5), string.Join(",", t));

            // P2：左侧两格
            t = Loadout.TargetsOf(Scope.P2, 3, s);
            Check("P2 覆盖 1 与 2", t.Contains(1) && t.Contains(2), string.Join(",", t));

            // All：除自己之外全部
            t = Loadout.TargetsOf(Scope.All, 0, s);
            Check("All 排除自身", !t.Contains(0) && t.Count == 7, string.Join(",", t));

            // 越界必须被裁掉
            t = Loadout.TargetsOf(Scope.N3, 6, s);
            Check("N3 在末尾裁掉越界（只剩 7）", t.Count == 1 && t[0] == 7, string.Join(",", t));

            // 触发弹载荷仍顺时针找下一颗子弹，与配件范围分离
            int nb = Loadout.NextBulletCell(0, s);
            Check("触发载荷从 0 找到 2", nb == 2, $"{nb}");
            nb = Loadout.NextBulletCell(5, s);
            Check("触发载荷扫描保留环绕", nb == 2, $"{nb}");
        }

        static void ScopePreviewCases()
        {
            Section("背包效果范围与安装预览");
            var slots = new SlotState(8);
            slots.Cells[0] = D.MakeItem("std");
            slots.Cells[2] = D.MakeItem("std");
            slots.Cells[5] = D.MakeItem("std");
            var mod = D.MakeItem("dmg");
            slots.Cells[3] = mod;

            mod.Mod.Scope = Scope.Adj;
            var range = Loadout.EffectRangeOf(slots, 3);
            var targets = Loadout.AffectedBulletCells(slots, 3);
            Check("相邻覆盖包含空格", range.Count == 2 && range.Contains(2) && range.Contains(4));
            Check("相邻生效目标不统计空格", targets.Count == 1 && targets[0] == 2);
            mod.Mod.Scope = Scope.Prev;
            slots.Cells[2] = null;
            targets = Loadout.AffectedBulletCells(slots, 3);
            Check("前一格为空时不跨格寻找子弹", targets.Count == 0);
            mod.Mod.Scope = Scope.N2;
            targets = Loadout.AffectedBulletCells(slots, 3);
            Check("N2 仅统计后两格内子弹", targets.Count == 1 && targets[0] == 5);
            mod.Mod.Scope = Scope.N3;
            range = Loadout.EffectRangeOf(slots, 3);
            Check("N3 覆盖后 3 格", range.Count == 3 && range.Contains(4) && range.Contains(6));
            mod.Mod.Scope = Scope.P2;
            Check("P2 范围有空格但无生效子弹", Loadout.EffectRangeOf(slots, 3).Count == 2 &&
                Loadout.AffectedBulletCells(slots, 3).Count == 0);
            mod.Mod.Scope = Scope.All;
            targets = Loadout.AffectedBulletCells(slots, 3);
            Check("All 覆盖全枪但仅两颗子弹生效", Loadout.EffectRangeOf(slots, 3).Count == 7 &&
                targets.Count == 2 && targets.Contains(0) && targets.Contains(5));
            mod.Mod.Scope = Scope.Next;
            targets = Loadout.AffectedBulletCells(slots, 3);
            Check("后一格为空时不跨格寻找子弹", targets.Count == 0);
            var wrapped = Loadout.PreviewPlacement(slots, mod, 7, 3);
            targets = Loadout.AffectedBulletCells(wrapped, 7);
            Check("后一格在末尾不环绕", targets.Count == 0 && Loadout.EffectRangeOf(wrapped, 7).Count == 0);
            Check("枪内移动预览移走来源，不复制配件", wrapped.Cells[3] == null && wrapped.Cells[7] == mod);
            Check("预览不改原布局", slots.Cells[3] == mod && slots.Cells[7] == null);

            var swap = Loadout.PreviewPlacement(slots, mod, 0, 3);
            Check("预览交换保留被替换子弹", swap.Cells[0] == mod && swap.Cells[3] == slots.Cells[0]);
            Check("交换预览不跨空格影响移走的子弹", Loadout.AffectedBulletCells(swap, 0).Count == 0);
            var same = Loadout.PreviewPlacement(slots, mod, 3, 3);
            Check("原格预览布局不变", same.Cells[3] == mod && same.FilledCount == slots.FilledCount);
            var bagItem = D.MakeItem("pierce");
            var bagProbe = Loadout.PreviewPlacement(slots, bagItem, 0);
            Check("背包装入预览替换目标，不动其余格", bagProbe.Cells[0] == bagItem &&
                bagProbe.Cells[3] == mod && slots.Cells[0].IsBullet);
            Check("子弹件没有被动作用范围", Loadout.EffectRangeOf(slots, 0).Count == 0);
            Check("无效格预览不崩溃", Loadout.EffectRangeOf(slots, -1).Count == 0 &&
                Loadout.AffectedBulletCells(slots, 8).Count == 0 && Loadout.EffectRangeOf(null, 0).Count == 0);

            var empty = new SlotState(4);
            empty.Cells[1] = D.MakeItem("g_mag"); // 数据标为 Next，实际由 ResolveGun 全局结算。
            range = Loadout.EffectRangeOf(empty, 1);
            Check("枪械件无子弹仍覆盖全枪", range.Count == 3 && range.Contains(0) && range.Contains(3));
            Check("枪械件不虚报生效子弹", Loadout.AffectedBulletCells(empty, 1).Count == 0);
            empty.Cells[1] = mod;
            Check("后一格覆盖空槽但不生效", Loadout.EffectRangeOf(empty, 1).Count == 1 &&
                Loadout.EffectRangeOf(empty, 1)[0] == 2 && Loadout.AffectedBulletCells(empty, 1).Count == 0);
            mod.Mod.Scope = Scope.P2;
            Check("P2 开头截断，不环绕", Loadout.EffectRangeOf(empty, 1).Count == 1 &&
                Loadout.EffectRangeOf(empty, 1)[0] == 0);
            mod.Mod.Scope = Scope.N3;
            Check("N3 末尾截断", Loadout.EffectRangeOf(empty, 1).Count == 2);

            slots.Cells[3] = D.MakeItem("g_double");
            targets = Loadout.AffectedBulletCells(slots, 3);
            Check("双重施放不跨空槽生效", targets.Count == 0);
            slots.Cells[4] = D.MakeItem("std");
            slots.Cells[3] = D.MakeItem("g_triple");
            targets = Loadout.AffectedBulletCells(slots, 3);
            Check("多重施放仅标出后一格触发目标", targets.Count == 1 && targets[0] == 4);
        }

        static void PhysicalScopeCastCases()
        {
            Section("物理前后格实战回归");
            var gun = D.Guns["rifle"];
            var slots = new SlotState(8);
            slots.Cells[0] = D.MakeItem("std");
            slots.Cells[1] = D.MakeItem("dmg");
            CastResult Cast(int pointer) => Loadout.ResolveCast(gun, slots, Loadout.ResolveGun(gun, slots), pointer);

            Check("子弹后放后一格配件不反向增伤", Near(Cast(1).Projectiles[0].Damage, 12f));
            Check("没有目标不收取配件耗弹", Cast(1).AmmoCost == 1);
            slots.Cells[3] = D.MakeItem("std");
            Check("隔空槽射击不增伤", Near(Cast(1).Projectiles[0].Damage, 12f));
            slots.Cells[2] = D.MakeItem("std");
            Check("紧邻后一格增伤", Near(Cast(1).Projectiles[0].Damage, 18f));
            Check("从子弹格开始读取也按物理范围增伤", Near(Cast(2).Projectiles[0].Damage, 18f));
            Check("相邻配件耗弹只计一次", Cast(2).AmmoCost == 2);
            slots.Cells[2] = D.MakeItem("speed");
            Check("配件不会将前一配件效果传递给子弹", Near(Cast(1).Projectiles[0].Damage, 12f));
            slots.Cells[1] = null;
            slots.Cells[2] = null;
            slots.Cells[7] = D.MakeItem("dmg");
            Check("末格配件不环绕增强首格子弹", Near(Cast(7).Projectiles[0].Damage, 12f));
            slots.Cells[7] = null;
            slots.Cells[2] = D.MakeItem("dmg");
            slots.Cells[2].Mod.Scope = Scope.Prev;
            Check("前一格为空不跨格增伤", Near(Cast(0).Projectiles[0].Damage, 12f));
            slots.Cells[1] = D.MakeItem("std");
            Check("紧邻前一格增伤", Near(Cast(1).Projectiles[0].Damage, 18f));
            slots.Cells[0] = D.MakeItem("dmg");
            slots.Cells[0].Mod.Scope = Scope.Prev;
            Check("首格前一格范围为空", Loadout.EffectRangeOf(slots, 0).Count == 0);
            slots = new SlotState(8);
            slots.Cells[0] = D.MakeItem("g_double");
            slots.Cells[2] = D.MakeItem("std");
            Check("多重施放隔空槽不触发", Cast(0).Projectiles.Count == 1);
            slots.Cells[1] = D.MakeItem("std");
            Check("多重施放从目标子弹格起读仍触发", Cast(1).Projectiles.Count == 2);
            slots.Cells[2] = null;
            Check("多重施放环绕不无限追加读取", Cast(1).Projectiles.Count == 2);
            slots.Cells[0] = D.MakeItem("heavy");
            Check("后一格配件击发延迟计入目标", Near(Cast(1).DelayAdd,
                D.Bullets["std"].Delay + D.Modifiers["heavy"].Delay));
        }

        // ---------------- 权重（同名同域叠加）----------------

        static void WeightsCases()
        {
            Section("配件叠加（loadout-spec §3.3）");

            var d = D.Bullets["std"].ToDesc();
            var m = D.Modifiers["dmg"];   // damageMulAdd = 0.5
            Loadout.ApplyMod(d, m, 1);
            Check("伤害增幅×1 → 12×1.5=18", Near(d.Damage, 18f), $"{d.Damage}");

            var d2 = D.Bullets["std"].ToDesc();
            Loadout.ApplyMod(d2, m, 2);
            Check("伤害增幅×2 → 12×2=24", Near(d2.Damage, 24f), $"{d2.Damage}");

            // 爆裂：无爆裂时按基础半径建
            var d3 = D.Bullets["std"].ToDesc();
            Loadout.ApplyMod(d3, D.Modifiers["explode"], 1);
            Check("爆裂建立半径 2.5", Near(d3.ExplodeR, 2.5f), $"{d3.ExplodeR}");
            Check("爆裂伤害 = 12×0.8+12 = 21.6", Near(d3.ExplodeDmg, 21.6f), $"{d3.ExplodeDmg}");

            // 已有爆裂的子弹：半径 ×1.25
            var d4 = D.Bullets["grenade"].ToDesc();
            Loadout.ApplyMod(d4, D.Modifiers["explode"], 1);
            Check("榴弹爆裂半径 4×1.25=5", Near(d4.ExplodeR, 5f), $"{d4.ExplodeR}");

            // 巨大化：体积、伤害、范围
            var d5 = D.Bullets["std"].ToDesc();
            Loadout.ApplyMod(d5, D.Modifiers["giant"], 1);
            Check("巨大化体积 ×2.2", Near(d5.Size, 0.12f * 2.2f), $"{d5.Size}");
            Check("巨大化伤害 ×1.3", Near(d5.Damage, 15.6f), $"{d5.Damage}");

            // 三重弹幕：数量 ×3、伤害 ×0.7、散射 +7
            var d6 = D.Bullets["std"].ToDesc();
            Loadout.ApplyMod(d6, D.Modifiers["scatter"], 1);
            Check("三重弹幕数量 ×3", d6.Count == 3, $"{d6.Count}");
            Check("三重弹幕伤害 ×0.7", Near(d6.Damage, 8.4f), $"{d6.Damage}");
            Check("三重弹幕散射 +7", Near(d6.Spread, 7f), $"{d6.Spread}");

            // 穿透：每层 +2
            var d7 = D.Bullets["std"].ToDesc();
            Loadout.ApplyMod(d7, D.Modifiers["pierce"], 1);
            Check("穿透×1 → +2", d7.Pierce == 2, $"{d7.Pierce}");
            Loadout.ApplyMod(d7, D.Modifiers["pierce"], 2);
            Check("再叠 2 层 → 2+4=6", d7.Pierce == 6, $"{d7.Pierce}");
        }

        // ---------------- 扫描 ----------------

        static void ScanCases()
        {
            Section("读取式扫描（loadout-spec §3.1）");

            var gun = D.Guns["rifle"];

            // 只有标准弹
            var s = new SlotState(8);
            s.Cells[0] = D.MakeItem("std");
            var st = Loadout.ResolveGun(gun, s);
            var r = Loadout.ResolveCast(gun, s, st, 0);
            Check("单子弹产 1 发", r.Projectiles.Count == 1, $"{r.Projectiles.Count}");
            Check("耗弹 1", r.AmmoCost == 1, $"{r.AmmoCost}");
            Check("指针推进到 1", r.NextPointer == 1, $"{r.NextPointer}");

            // Next 件占读取：dmg(0) + std(1) → 1 发，伤害 ×1.5
            var s2 = new SlotState(8);
            s2.Cells[0] = D.MakeItem("dmg");
            s2.Cells[1] = D.MakeItem("std");
            var st2 = Loadout.ResolveGun(gun, s2);
            var r2 = Loadout.ResolveCast(gun, s2, st2, 0);
            Check("Next+子弹 = 1 发", r2.Projectiles.Count == 1, $"{r2.Projectiles.Count}");
            Check("Next 加成生效 → 18", Near(r2.Projectiles[0].Damage, 18f), $"{r2.Projectiles[0].Damage}");
            Check("耗弹 = 1+1 = 2", r2.AmmoCost == 2, $"{r2.AmmoCost}");

            // 被动件不占读取
            var s3 = new SlotState(8);
            s3.Cells[0] = D.MakeItem("std");
            s3.Cells[1] = D.MakeItem("pierce");   // 默认 Next，需要改成被动
            s3.Cells[1].Mod.Scope = Scope.Prev;
            var st3 = Loadout.ResolveGun(gun, s3);
            var r3 = Loadout.ResolveCast(gun, s3, st3, 0);
            Check("被动件不占读取", r3.Projectiles.Count == 1, $"{r3.Projectiles.Count}");
            Check("被动 Prev 作用于格 0", r3.Projectiles[0].Pierce == 2, $"{r3.Projectiles[0].Pierce}");

            // castCount=2 的霰弹枪：读 2 个子弹
            var sg = D.Guns["shotgun"];
            var s4 = new SlotState(6);
            s4.Cells[0] = D.MakeItem("pellet");
            s4.Cells[1] = D.MakeItem("pellet");
            var st4 = Loadout.ResolveGun(sg, s4);
            var r4 = Loadout.ResolveCast(sg, s4, st4, 0);
            Check("霰弹枪一次读 2 发", r4.Projectiles.Count == 2, $"{r4.Projectiles.Count}");
            Check("两发都是 6 弹丸", r4.Projectiles[0].Count == 6 && r4.Projectiles[1].Count == 6,
                $"{r4.Projectiles[0].Count}/{r4.Projectiles[1].Count}");

            // 多重施放加预算
            var s5 = new SlotState(8);
            s5.Cells[0] = D.MakeItem("g_double");
            s5.Cells[1] = D.MakeItem("std");
            s5.Cells[2] = D.MakeItem("std");
            var st5 = Loadout.ResolveGun(gun, s5);
            var r5 = Loadout.ResolveCast(gun, s5, st5, 0);
            Check("双重施放读 2 个子弹", r5.Projectiles.Count == 2, $"{r5.Projectiles.Count}");
        }

        // ---------------- 规则细节 ----------------

        static void LoadoutRuleCases()
        {
            Section("结算规则（loadout-spec §3.2 §4）");

            var gun = D.Guns["rifle"];

            // 散射 × 枪械倍率
            var s = new SlotState(8);
            s.Cells[0] = D.MakeItem("std");
            s.Cells[1] = D.MakeItem("g_stable");   // 全枪散射 ×0.4
            var st = Loadout.ResolveGun(gun, s);
            Check("稳定器散射 ×0.4", Near(st.SpreadMul, 0.4f), $"{st.SpreadMul}");
            var r = Loadout.ResolveCast(gun, s, st, 0);
            // std spread 0 × 1.2(枪) × 0.4 = 0
            Check("零散布子弹仍是 0", Near(r.Projectiles[0].Spread, 0f), $"{r.Projectiles[0].Spread}");

            // 有基础散射的子弹才会被枪械散射放大
            var s2 = new SlotState(8);
            s2.Cells[0] = D.MakeItem("light");   // spread 2.5
            var st2 = Loadout.ResolveGun(gun, s2);
            var r2 = Loadout.ResolveCast(gun, s2, st2, 0);
            Check("轻型弹散射不被枪修正（GunStats.Spread 未并入）",
                Near(r2.Projectiles[0].Spread, 2.5f), $"{r2.Projectiles[0].Spread}");

            // 枪械件数量上限
            var s3 = new SlotState(8);
            s3.Cells[0] = D.MakeItem("pellet");
            for (int i = 1; i < 5; i++) { s3.Cells[i] = D.MakeItem("scatter"); s3.Cells[i].Mod.Scope = Scope.Prev; }
            var st3 = Loadout.ResolveGun(gun, s3);
            var r3 = Loadout.ResolveCast(gun, s3, st3, 0);
            Check("投射物数量被封顶 24", r3.Projectiles[0].Count <= Loadout.MaxCount,
                $"{r3.Projectiles[0].Count}");

            // 空格跳过：先读 0 号，指针落到下一个「可读」格（3 号）
            var s4 = new SlotState(8);
            s4.Cells[0] = D.MakeItem("std");
            s4.Cells[1] = null;
            s4.Cells[2] = null;
            s4.Cells[3] = D.MakeItem("std");
            var st4 = Loadout.ResolveGun(gun, s4);
            var r4 = Loadout.ResolveCast(gun, s4, st4, 0);
            // NextPointer 是「扫描停止位置的下一个」，再读一次应命中 3 号
            var r4b = Loadout.ResolveCast(gun, s4, st4, r4.NextPointer);
            Check("跳过空格后下一次读到 3 号格",
                r4b.UsedCells.Count > 0 && r4b.UsedCells[0] == 3,
                r4b.UsedCells.Count > 0 ? $"{r4b.UsedCells[0]}" : "无读取");

            // 空枪
            var s5 = new SlotState(8);
            var st5 = Loadout.ResolveGun(gun, s5);
            var r5 = Loadout.ResolveCast(gun, s5, st5, 0);
            Check("空枪不产出", !r5.Succeeded, $"{r5.Projectiles.Count}");
        }

        // ---------------- 触发弹 ----------------

        static void TriggerCases()
        {
            Section("触发弹载荷（loadout-spec §3.4）");

            var gun = D.Guns["rifle"];
            var s = new SlotState(8);
            s.Cells[0] = D.MakeItem("trigger");
            s.Cells[1] = D.MakeItem("grenade");
            var st = Loadout.ResolveGun(gun, s);
            var r = Loadout.ResolveCast(gun, s, st, 0);

            Check("触发弹本体产出 1 发", r.Projectiles.Count == 1, $"{r.Projectiles.Count}");
            var p = r.Projectiles[0];
            Check("触发条件为 Hit", p.Trigger == TriggerKind.Hit, $"{p.Trigger}");
            Check("有载荷", p.Payload != null && p.Payload.Count == 1,
                $"{p.Payload?.Count}");

            if (p.Payload != null && p.Payload.Count == 1)
            {
                Check("载荷是榴弹", p.Payload[0].ModuleId == "grenade", p.Payload[0].ModuleId);
                Check("载荷带爆裂半径", p.Payload[0].ExplodeR > 0f, $"{p.Payload[0].ExplodeR}");
            }

            // 载荷伤害计入期望
            float total = p.ExpectedDamage;
            Check("载荷伤害计入总期望", total > 8f, $"{total}");

            // 没有下一个子弹 → 无载荷但不崩
            var s2 = new SlotState(8);
            s2.Cells[0] = D.MakeItem("trigger");
            var st2 = Loadout.ResolveGun(gun, s2);
            var r2 = Loadout.ResolveCast(gun, s2, st2, 0);
            Check("无载荷时不崩且仍产出", r2.Projectiles.Count == 1, $"{r2.Projectiles.Count}");
            Check("无载荷时 Payload 为空", r2.Projectiles[0].Payload == null);

            // 递归上限
            var s3 = new SlotState(12);
            for (int i = 0; i < 10; i++) s3.Cells[i] = D.MakeItem("trigger");
            var st3 = Loadout.ResolveGun(gun, s3);
            var r3 = Loadout.ResolveCast(gun, s3, st3, 0);
            Check("深递归不崩", r3.Projectiles.Count >= 1, $"{r3.Projectiles.Count}");
        }

        // ---------------- 枪械属性 ----------------

        static void GunStatCases()
        {
            Section("枪械向配件（loadout-spec §5）");

            var gun = D.Guns["rifle"];
            var s = new SlotState(8);
            var st = Loadout.ResolveGun(gun, s);
            Check("基础弹匣 30", st.Mag == 30, $"{st.Mag}");

            s.Cells[0] = D.MakeItem("g_mag");
            st = Loadout.ResolveGun(gun, s);
            Check("扩容弹匣 ×1.5 = 45", st.Mag == 45, $"{st.Mag}");

            s.Cells[1] = D.MakeItem("g_reload");
            st = Loadout.ResolveGun(gun, s);
            Check("快速装填 ×0.65 = 0.975", Near(st.Reload, 1.5f * 0.65f), $"{st.Reload}");

            s.Cells[2] = D.MakeItem("g_rapid");
            st = Loadout.ResolveGun(gun, s);
            Check("速射 ×0.75 = 0.105", Near(st.ShotInterval(0f), 0.14f * 0.75f), $"{st.ShotInterval(0f)}");

            s.Cells[3] = D.MakeItem("g_parallel");
            st = Loadout.ResolveGun(gun, s);
            Check("并联扳机读取 +1", st.CastCount == 2, $"{st.CastCount}");
            Check("并联扳机弹匣 −20%", Near(st.Mag, 45f * 0.8f), $"{st.Mag}");

            // 弹药回收叠加
            var s2 = new SlotState(8);
            s2.Cells[0] = D.MakeItem("g_recycle");
            var st2 = Loadout.ResolveGun(gun, s2);
            Check("回收 25%", Near(st2.Recycle, 0.25f), $"{st2.Recycle}");
            s2.Cells[1] = D.MakeItem("g_recycle");
            st2 = Loadout.ResolveGun(gun, s2);
            Check("回收叠加 43.75%", Near(st2.Recycle, 1f - 0.75f * 0.75f), $"{st2.Recycle}");

            // 汲能
            var s3 = new SlotState(8);
            s3.Cells[0] = D.MakeItem("g_leech");
            var st3 = Loadout.ResolveGun(gun, s3);
            Check("汲能 +2", Near(st3.Leech, 2f), $"{st3.Leech}");
        }

        // ---------------- 施法序列 ----------------

        static void SequenceCases()
        {
            Section("施法序列（loadout-spec §3.5）");

            var gun = D.Guns["rifle"];
            var s = new SlotState(8);
            s.Cells[0] = D.MakeItem("std");
            s.Cells[1] = D.MakeItem("std");
            s.Cells[2] = D.MakeItem("std");

            var st = Loadout.ResolveGun(gun, s);
            var seq = Loadout.FullSequence(gun, s, st);
            Check("序列至少 2 步", seq.Count >= 2, $"{seq.Count}");
            Check("第 1 步从格 0 起", seq[0].UsedCells[0] == 0, $"{seq[0].UsedCells[0]}");
            Check("第 2 步从格 1 起", seq.Count > 1 && seq[1].UsedCells[0] == 1,
                seq.Count > 1 ? $"{seq[1].UsedCells[0]}" : "n/a");
        }

        // ---------------- 背包 ----------------

        static void BagCases()
        {
            Section("背包与分解（gameplay-spec §6）");

            var run = new RunState(D, 12345);
            Check("背包容量 15", run.BagCapacity == 15, $"{run.BagCapacity}");

            bool ok = run.TryPick("std");
            Check("拾取成功", ok && run.Bag.Count == 1, $"{run.Bag.Count}");

            int lvBefore = run.Level;
            int xpBefore = run.Xp;
            int gained = run.Salvage(run.Bag[0]);
            Check("分解普通件得 3 经验", gained == 3, $"{gained}");
            Check("分解后背包清空", run.Bag.Count == 0, $"{run.Bag.Count}");
            Check("分解经验已入账", run.Xp != xpBefore || run.Level != lvBefore,
                $"xp={run.Xp} lv={run.Level}");

            // 稀有 6 / 史诗 9
            var run2 = new RunState(D, 999);
            run2.TryPick("ap");
            Check("分解稀有件得 6", run2.Salvage(run2.Bag[0]) == 6);
            run2.TryPick("hole");
            Check("分解史诗件得 9", run2.Salvage(run2.Bag[0]) == 9);

            // 抽取不含重复
            var offers = run.Roll(3);
            Check("三选一产出 3 个", offers.Count == 3, $"{offers.Count}");
            Check("三选一无重复",
                offers.Count == 3 && offers[0].Id != offers[1].Id &&
                offers[1].Id != offers[2].Id && offers[0].Id != offers[2].Id);
        }

        // ---------------- 时间轴 ----------------

        static void TimelineCases()
        {
            Section("单图时间轴（gameplay-spec §2 §2.1）");

            var a = D.Arena;
            Check("场地 100×100", a.Width == 100f && a.Depth == 100f, $"{a.Width}×{a.Depth}");
            Check("掩体 18 件", a.CoverCount == 18, $"{a.CoverCount}");

            var d = D.Director;
            Check("时间轴 5 阶段", d.Phases.Count == 5, $"{d.Phases.Count}");
            Check("Boss 入场 600s（10:00）", Math.Abs(d.BossAt - 600f) < 0.001f, $"{d.BossAt}");
            Check("精英首现 300s（5:00）", Math.Abs(d.Elite.FirstAt - 300f) < 0.001f, $"{d.Elite.FirstAt}");

            var probe = new RunDirector(D.Director.PhaseList, null)
            {
                BossAt = D.Director.BossAt,
                ThreatFullAt = D.Director.ThreatFullAt,
                Elite = D.Director.Elite,
            };

            probe.SeekTo(300f);
            Check("5:00 威胁 50", Math.Abs(probe.Threat - 50f) < 0.5f, $"{probe.Threat:0.00}");
            probe.SeekTo(600f);
            Check("10:00 威胁 100", Math.Abs(probe.Threat - 100f) < 0.5f, $"{probe.Threat:0.00}");

            // 事件：5:00 首只精英、10:00 Boss
            var d2 = new RunDirector(D.Director.PhaseList, null)
            {
                BossAt = D.Director.BossAt,
                ThreatFullAt = D.Director.ThreatFullAt,
                Elite = D.Director.Elite,
            };
            for (int i = 0; i < 299; i++) d2.Tick(1f);
            Check("5:00 前无精英", d2.ElitesSpawned == 0, $"{d2.ElitesSpawned}");

            bool elite = false, boss = false;
            for (int i = 0; i < 302; i++)
            {
                foreach (var e in d2.Tick(1f))
                {
                    if (e.Kind == RunDirector.EventKind.EliteDue) elite = true;
                    if (e.Kind == RunDirector.EventKind.BossDue) boss = true;
                }
            }
            Check("5:00 触发精英", elite);
            Check("10:00 触发 Boss", boss);
            Check("Boss 已入场", d2.BossSpawned);
            Check("精英共 4 只（5:00/6:30/8:00/9:30）", d2.ElitesSpawned == 4, $"{d2.ElitesSpawned}");
        }

        static void RestartCases()
        {
            Section("新局时间轴隔离与连续重开");
            var template = D.Director;
            var previous = template.CreateRun();
            for (int cycle = 0; cycle < 3; cycle++)
            {
                previous.Tick(601f);
                var run = previous.CreateRun();
                Check($"重开 {cycle + 1}：时间和威胁归零", run.Elapsed == 0 && run.Threat == 0);
                Check($"重开 {cycle + 1}：初始阶段与倒计时", run.CurrentPhase.Name == template.Phases[0].Name &&
                    run.SecondsToElite == 300f && run.SecondsToBoss == 600f);
                Check($"重开 {cycle + 1}：事件标记清空", !run.BossSpawned && run.ElitesSpawned == 0);
                var events = run.Tick(300f);
                Check($"重开 {cycle + 1}：精英重新触发", events.Exists(e => e.Kind == RunDirector.EventKind.EliteDue) && run.ElitesSpawned == 1);
                events = run.Tick(300f);
                Check($"重开 {cycle + 1}：Boss 重新触发且保留 Boss 阶段", events.Exists(e => e.Kind == RunDirector.EventKind.BossDue) &&
                    run.CurrentPhase != null && run.CurrentPhase.Name == previous.CurrentPhase.Name && run.ElitesSpawned == 4);
                Check($"重开 {cycle + 1}：Boss 不重复触发", !run.Tick(1f).Exists(e => e.Kind == RunDirector.EventKind.BossDue));
                previous = run;
            }
            Check("推进副本不改变缓存模板", template.Elapsed == 0 && !template.BossSpawned && template.ElitesSpawned == 0);
            var isolated = template.CreateRun();
            isolated.Elite.FirstAt = 1;
            isolated.Phases[0].Pool[0] = "changed";
            isolated.Phases[0].Concurrency = -1;
            Check("局内配置修改不影响下局", template.Elite.FirstAt == 300f &&
                template.Phases[0].Pool[0] != "changed" && template.Phases[0].Concurrency > 0);
            Check("空时间轴可开局", new RunDirector(null, null).CreateRun().CurrentPhase == null);
        }

        // ---------------- Boss ----------------

        static void BossCases()
        {
            Section("Boss 状态机（gameplay-spec §3.1）");

            var mapDef = new BossDef();
            var theaterBoss = new BossState(6000f, mapDef, 0);
            Check("无柱地图不保留幽灵能量柱", theaterBoss.PillarCount == 0 && theaterBoss.PillarsAlive == 0, $"{theaterBoss.PillarsAlive}");
            Check("无柱地图拒绝不存在的能量柱伤害", !theaterBoss.DamagePillar(0, 1000f), "");
            Check("地图覆盖不污染共享Boss配置", mapDef.Pillars == BossState.DefaultPillars, "");
            var b = new BossState(6000f);
            Check("初始 P1", b.Phase == BossPhase.P1);
            Check("初始 4 柱", b.PillarsAlive == 4, $"{b.PillarsAlive}");
            Check("初始无护盾", !b.ShieldUp);

            // 阶段切换发生在 Tick 里，不在 TakeDamage 里 —— 这是有意的：
            // 掉血是「结算」，换阶段是「时序事件」，混在一起会让暂停/慢速下的阶段推进失真。
            b.TakeDamage(2100f, false);   // → 65%
            Check("扣血后血量到 65%", Near(b.HpRatio, 0.65f, 0.01f), $"{b.HpRatio:0.000}");
            b.Tick(0.05f);
            Check("Tick 后进 P2", b.Phase == BossPhase.P2, $"{b.Phase} {b.HpRatio:0.00}");

            b.TakeDamage(2100f, false);   // → 30%
            b.Tick(0.05f);
            Check("Tick 后进 P3", b.Phase == BossPhase.P3, $"{b.Phase} {b.HpRatio:0.00}");

            b.TakeDamage(9999f, false);
            b.Tick(0.05f);
            Check("Boss 死亡", b.Phase == BossPhase.Dead);
            Check("死亡后不再存活", !b.Alive);
        }

        // ---------------- 工具 ----------------

        static bool Near(float a, float b, float eps = 0.001f) => Math.Abs(a - b) < eps;

        static bool HasFlag(string flag) =>
            Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

        static string FindCfgDir()
        {
            // 沿目录上溯找工程内的配置表目录。
            // 路径取 SCfgAddresses.AssetFolder（唯一真相），不要再手写一份 ——
            // 资产从 Resources/ 搬到 Assets/_Game/Res/Cfg/ 时就是这里先漏掉的。
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var p = Path.Combine(dir, SCfgAddresses.AssetFolder.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            return Path.Combine(AppContext.BaseDirectory, SCfgAddresses.ResourceFolder);
        }

        static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        static void Check(string name, bool cond, string detail = "")
        {
            if (cond) { s_pass++; Console.WriteLine("  PASS " + name); }
            else
            {
                s_fail++;
                var msg = string.IsNullOrEmpty(detail) ? name : $"{name} :: {detail}";
                s_fails.Add(msg);
                Console.WriteLine("  FAIL " + msg);
            }
        }
    }
}

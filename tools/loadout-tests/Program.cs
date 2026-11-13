using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using S.Core;

namespace LoadoutTests
{
    /// <summary>
    /// 配件结算的回归断言集。直接链接 Assets/_Game/Core 源码运行，
    /// 每个 case 对应 docs/loadout-spec.md §9 的一条算例。
    /// 期望值写在程序里而不是从数据文件读 —— 这样数据被改动时测试会红，正是我们要的。
    /// </summary>
    static class Program
    {
        static int s_pass, s_fail;
        static readonly List<string> s_fails = new List<string>();
        static SData D;

        static int Main()
        {
            var path = FindData();
            Console.WriteLine("DATA " + path);
            D = SData.Parse(File.ReadAllText(path));
            Console.WriteLine($"LOADED guns={D.GunOrder.Count} bullets={D.Bullets.Count} mods={D.Modifiers.Count}");

            SpecCases();
            ChainValidationCases();
            RotationCases();
            GunModifierCases();
            PreviewCases();
            ChainTriggerCases();
            ZoneCases();
            BossCases();

            // 数值基准（-bench 时才跑；输出用于校准，不计入断言总数）
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

        // ---------------- docs/loadout-spec.md §9 ----------------

        static void SpecCases()
        {
            Section("§9 施法算例");

            // #1 制式弹
            {
                var r = Cast("g_auto", "b_slug");
                Eq("#1 弹丸数", 1, r.Projectiles.Count);
                Near("#1 伤害", 12f, r.Projectiles[0].Damage);
                Near("#1 暴击率", 0.05f, r.Projectiles[0].Crit);
                Near("#1 散射", 1.2f, r.Projectiles[0].Spread);
                Eq("#1 弹药", 2, r.AmmoCost);
                Near("#1 单发期望", 12.6f, r.Projectiles[0].ExpectedDamage);
            }

            // #2 校准 + 制式
            {
                var r = Cast("g_auto", "m_dmg_up", "b_slug");
                Near("#2 伤害", 16.2f, r.Projectiles[0].Damage);
                Near("#2 期望", 17.01f, r.Projectiles[0].ExpectedDamage);
                Eq("#2 弹药", 2, r.AmmoCost);
            }

            // #3 [增压, 校准, 制式] = (12+8)*1.35
            {
                var r = Cast("g_auto", "m_dmg_flat", "m_dmg_up", "b_slug");
                Near("#3 伤害", 27.0f, r.Projectiles[0].Damage);
                Near("#3 期望", 28.35f, r.Projectiles[0].ExpectedDamage);
            }

            // #4 [校准, 增压, 制式] = 12*1.35+8
            {
                var r = Cast("g_auto", "m_dmg_up", "m_dmg_flat", "b_slug");
                Near("#4 伤害", 24.2f, r.Projectiles[0].Damage);
                Near("#4 期望", 25.41f, r.Projectiles[0].ExpectedDamage);
            }

            // #5 [制式, 增压] → no-target 警告，伤害同 #1
            {
                var r = Cast("g_auto", "b_slug", "m_dmg_flat");
                Near("#5 伤害", 12f, r.Projectiles[0].Damage);
                Eq("#5 警告数", 1, r.Warnings.Count);
                Eq("#5 警告码", "no-target", r.Warnings.Count > 0 ? r.Warnings[0].Code : "");
            }

            // #6 [分裂, 制式] → 3 发 × 6.6
            {
                var r = Cast("g_auto", "m_split", "b_slug");
                Eq("#6 弹丸数", 3, r.Projectiles.Count);
                Near("#6 单个伤害", 6.6f, r.Projectiles[0].Damage);
                Eq("#6 弹药不变", 2, r.AmmoCost);
                Near("#6 整次期望", 20.79f, r.ExpectedDamage, 0.02f);
            }

            // #7 [分裂, 校准, 制式] → 分裂后校准作用于 3 个
            {
                var r = Cast("g_auto", "m_split", "m_dmg_up", "b_slug");
                Eq("#7 弹丸数", 3, r.Projectiles.Count);
                Near("#7 单个伤害", 8.91f, r.Projectiles[0].Damage);
                Near("#7 整次期望", 28.07f, r.ExpectedDamage, 0.03f);
            }

            // #8 [校准, 分裂, 制式] → 与 #7 等价（验证全场语义）
            {
                var r = Cast("g_auto", "m_dmg_up", "m_split", "b_slug");
                Eq("#8 弹丸数", 3, r.Projectiles.Count);
                Near("#8 单个伤害", 8.91f, r.Projectiles[0].Damage);
                Near("#8 整次期望", 28.07f, r.ExpectedDamage, 0.03f);
            }

            // #9 [制式, 制式]
            {
                var r = Cast("g_auto", "b_slug", "b_slug");
                Eq("#9 弹丸数", 2, r.Projectiles.Count);
                Eq("#9 弹药", 4, r.AmmoCost);
                Near("#9 整次期望", 25.2f, r.ExpectedDamage, 0.02f);
            }

            // #10 [分裂, 分裂, 制式] → 3 → 9
            {
                var r = Cast("g_auto", "m_split", "m_split", "b_slug");
                Eq("#10 弹丸数", 9, r.Projectiles.Count);
                Near("#10 单个伤害", 3.63f, r.Projectiles[0].Damage, 0.005f);
                Eq("#10 弹药", 2, r.AmmoCost);
            }

            // #12 [稳定器, 制式] on g_auto → 1.2*0.6
            {
                var r = Cast("g_auto", "m_spread_down", "b_slug");
                Near("#12 散射", 0.72f, r.Projectiles[0].Spread);
            }

            // #13 [弹链优化, 制式] → max(1, 2-1)
            {
                var r = Cast("g_auto", "m_ammo_down", "b_slug");
                Eq("#13 弹药", 1, r.AmmoCost);
            }

            // #14 [弹链优化 x2, 制式] → 钳制到 1
            {
                var r = Cast("g_auto", "m_ammo_down", "m_ammo_down", "b_slug");
                Eq("#14 弹药", 1, r.AmmoCost);
            }

            // #15 [霰粒, 霰粒]
            {
                var r = Cast("g_auto", "b_pellet", "b_pellet");
                Eq("#15 弹丸数", 2, r.Projectiles.Count);
                Near("#15 单个伤害", 6f, r.Projectiles[0].Damage);
                Eq("#15 弹药", 2, r.AmmoCost);
                Near("#15 每颗散射", 4.2f, r.Projectiles[0].Spread);
                Near("#15 期望", 12.18f, r.ExpectedDamage, 0.02f);
            }

            // #15b [分裂, 霰粒]
            {
                var r = Cast("g_auto", "m_split", "b_pellet");
                Eq("#15b 弹丸数", 3, r.Projectiles.Count);
                Near("#15b 单个伤害", 3.3f, r.Projectiles[0].Damage);
                Eq("#15b 弹药", 1, r.AmmoCost);
                Near("#15b 散射", 6.2f, r.Projectiles[0].Spread, 0.001f);
                Near("#15b 期望", 10.05f, r.ExpectedDamage, 0.02f);
            }

            // #16 榴弹 [制式] → 溅射 2.8
            {
                var r = Cast("g_grenade", "b_slug");
                Near("#16 溅射半径", 2.8f, r.Projectiles[0].SplashRadius);
            }

            // #17 榴弹 [电浆团] → max(2.8, 1.2) = 2.8
            {
                var r = Cast("g_grenade", "b_plasma");
                Near("#17 溅射半径", 2.8f, r.Projectiles[0].SplashRadius);
            }

            // #21 单发 [制式] → 12 × 5.4（damageMul 已按实测校准到 5.4）
            {
                var r = Cast("g_single", "b_slug");
                Near("#21 伤害", 64.8f, r.Projectiles[0].Damage, 0.01f);
                Near("#21 期望", 68.04f, r.Projectiles[0].ExpectedDamage, 0.03f);
            }

            // #22 单发 [校准, 制式]
            {
                var r = Cast("g_single", "m_dmg_up", "b_slug");
                Near("#22 伤害", 87.48f, r.Projectiles[0].Damage, 0.03f);
                Near("#22 期望", 91.854f, r.Projectiles[0].ExpectedDamage, 0.05f);
            }

            // #23 单发 [增压, 校准, 制式]
            {
                var r = Cast("g_single", "m_dmg_flat", "m_dmg_up", "b_slug");
                Near("#23 伤害", 145.8f, r.Projectiles[0].Damage, 0.05f);
                Near("#23 期望", 153.09f, r.Projectiles[0].ExpectedDamage, 0.08f);
            }

            // #24 榴弹 [校准, 制式] → 16.2 × 3.9
            {
                var r = Cast("g_grenade", "m_dmg_up", "b_slug");
                Near("#24 基础爆伤", 63.18f, r.Projectiles[0].Damage, 0.03f);
                Near("#24 边缘伤害", 28.431f, r.Projectiles[0].Damage * 0.45f, 0.05f);
            }

            // 额外：光矛弹速
            {
                var r = Cast("g_auto", "b_lance");
                Near("光矛 speedMul", 1.6f, r.Projectiles[0].SpeedMul);
            }

            // 额外：制式弹存在时间
            {
                var r = Cast("g_auto", "b_slug");
                Near("制式弹 lifetime", 1.2f, r.Projectiles[0].Lifetime);
            }

            // 额外：延时引信
            {
                var r = Cast("g_auto", "m_life_up", "b_slug");
                Near("延时引信 lifetime", 1.7f, r.Projectiles[0].Lifetime);
            }

            // 额外：激光准星 +12%
            {
                var r = Cast("g_auto", "m_crit_up", "b_slug");
                Near("激光准星 crit", 0.17f, r.Projectiles[0].Crit, 0.0005f);
            }

            // 额外：暴击率钳制到 1.0
            {
                var r = Cast("g_auto", "m_crit_up", "m_crit_up", "b_slug");
                Near("暴击钳制", 0.29f, r.Projectiles[0].Crit, 0.0005f);
            }

            // 额外：加速器速度与伤害代价
            {
                var r = Cast("g_auto", "m_speed_up", "b_slug");
                Near("加速器 speedMul", 1.6f, r.Projectiles[0].SpeedMul);
                Near("加速器伤害代价", 10.2f, r.Projectiles[0].Damage, 0.01f);
            }

            // 额外：穿透射击
            {
                var r = Cast("g_auto", "m_pierce", "b_slug");
                Eq("穿透射击 pierce", 2, r.Projectiles[0].Pierce);
                Near("穿透射击伤害代价", 10.2f, r.Projectiles[0].Damage, 0.01f);
            }

            // 额外：制导是全场作用域，放在子弹后面同样生效
            {
                var r = Cast("g_auto", "b_slug", "m_homing");
                Eq("制导全场无警告", 0, r.Warnings.Count);
                Near("制导全场生效", 180f, r.Projectiles[0].HomingRate);
                Near("制导全场伤害代价", 10.8f, r.Projectiles[0].Damage, 0.01f);
            }

            // 额外：爆裂装药的溅射与伤害（m_blast 已按实测降到 1.4 / ×1.00）
            {
                var r = Cast("g_auto", "m_blast", "b_slug");
                Near("爆裂装药溅射", 1.4f, r.Projectiles[0].SplashRadius, 0.001f);
                Near("爆裂装药伤害不增", 12f, r.Projectiles[0].Damage, 0.01f);
            }

            // 额外：爆裂装药叠加榴弹基础溅射
            {
                var r = Cast("g_grenade", "m_blast", "b_slug");
                Near("榴弹+爆裂溅射", 4.2f, r.Projectiles[0].SplashRadius, 0.001f);
            }
        }

        // ---------------- 装配校验 ----------------

        static void ChainValidationCases()
        {
            Section("装配校验");

            // #11 三颗制式弹违反同名上限
            {
                var slot = new SlotState();
                If("同名1 插入成功", Loadout.Insert(slot, D.MakeItem("b_slug")));
                If("同名2 插入成功", Loadout.Insert(slot, D.MakeItem("b_slug")));
                var code = Loadout.ValidateInsert(slot, D.MakeItem("b_slug"), slot.Chain.Count);
                Eq("同名3 被拒", "same-limit", code);
                Eq("同名拒绝后链长", 2, slot.Chain.Count);
            }

            // 链长上限 6
            {
                var slot = new SlotState();
                for (int i = 0; i < 5; i++) Loadout.Insert(slot, D.MakeItem("m_dmg_up"));
                // 同名上限会先拦住，改用不同件凑满
                slot = new SlotState();
                foreach (var id in new[] { "b_slug", "b_slug", "m_dmg_up", "m_crit_up", "m_life_up", "m_spread_down" })
                    If("链长填充 " + id, Loadout.Insert(slot, D.MakeItem(id)));
                Eq("链长=6", 6, slot.Chain.Count);
                Eq("第7项被拒", "chain-full", Loadout.ValidateInsert(slot, D.MakeItem("b_heavy"), 6));
            }

            // 插入位置
            {
                var slot = new SlotState();
                Loadout.Insert(slot, D.MakeItem("b_slug"));
                Loadout.Insert(slot, D.MakeItem("m_dmg_up"), 0);
                Eq("插入头部后第一项是衍生", false, slot.Chain[0].IsBullet);
                Eq("插入头部后第二项是子弹", true, slot.Chain[1].IsBullet);
            }

            // 越界插入被拒
            {
                var slot = new SlotState();
                Eq("越界索引被拒", "bad-index", Loadout.ValidateInsert(slot, D.MakeItem("b_slug"), 5));
            }
        }

        // ---------------- 轮转 ----------------

        static void RotationCases()
        {
            Section("§1.1 轮转与弹药");

            // #18 三链非空 → 1,2,3,1
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Add(D.MakeItem("b_slug"));
                gun.Slots[2].Chain.Add(D.MakeItem("b_slug"));

                var seq = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    int expect = i % 3;
                    Eq("#18 施放前 Peek", expect, gun.PeekNextSlot());
                    var r = gun.TryFire(out _);
                    If("#18 第" + (i + 1) + "发成功", r != null);
                    gun.FireCooldown = 0f; // 跳过射速等待，只测轮转
                    seq.Add(expect);
                }
                // 打满 4 发后 cursor 回到 1
                Eq("#18 第四发后指针", 1 % 3, gun.Cursor);
            }

            // #19 中间位为空 → 跳过
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Clear();
                gun.Slots[2].Chain.Add(D.MakeItem("b_slug"));

                Eq("#19 首跳 1 位", 0, gun.PeekNextSlot());
                gun.TryFire(out _); gun.FireCooldown = 0f;
                Eq("#19 空位被跳过 → 2", 2, gun.PeekNextSlot());
                gun.TryFire(out _); gun.FireCooldown = 0f;
                Eq("#19 回绕到 0", 0, gun.PeekNextSlot());
            }

            // #20 全空 → 空枪，不耗弹，指针不动
            {
                var gun = MakeGun("g_auto");
                for (int i = 0; i < gun.Slots.Length; i++) gun.Slots[i].Chain.Clear();
                int ammoBefore = gun.Ammo;
                var r = gun.TryFire(out var reason);
                Eq("#20 空枪返回 null", true, r == null);
                Eq("#20 原因", "no-bullet", reason);
                Eq("#20 弹药不变", ammoBefore, gun.Ammo);
                Eq("#20 指针不动", 0, gun.Cursor);
            }

            // 弹药不足 → 不施法 + 自动换弹
            {
                var gun = MakeGun("g_auto");
                gun.Ammo = 1;   // 制式弹需要 2
                var r = gun.TryFire(out var reason);
                Eq("弹药不足返回 null", true, r == null);
                Eq("弹药不足原因", "no-ammo", reason);
                Eq("弹药不足触发换弹", true, gun.Reloading);
                Eq("弹药不足指针不动", 0, gun.Cursor);
            }

            // 换弹后补满，且指针保持
            {
                var gun = MakeGun("g_auto");
                gun.TryFire(out _); // 消耗 2，指针 → 1
                int cursorBefore = gun.Cursor;
                gun.FireCooldown = 0f;
                gun.Ammo = 3;
                gun.StartReload();
                gun.Tick(gun.Resolved.ReloadTime + 0.01f);
                Eq("换弹补满", gun.Resolved.MagSize, gun.Ammo);
                Eq("换弹不重置指针", cursorBefore, gun.Cursor);
            }

            // 射速冷却阻止连点
            {
                var gun = MakeGun("g_auto");
                If("首发生效", gun.TryFire(out _) != null);
                var r2 = gun.TryFire(out var reason2);
                Eq("冷却中返回 null", true, r2 == null);
                Eq("冷却原因", "cooldown", reason2);
            }

            // 打空自动换弹
            {
                var gun = MakeGun("g_auto");
                gun.Ammo = 2;
                If("打空最后一发", gun.TryFire(out _) != null);
                Eq("打空后自动换弹", 0, gun.Ammo);
                Eq("打空后进入换弹", true, gun.Reloading);
            }
        }

        // ---------------- 枪械级配件 ----------------

        static void GunModifierCases()
        {
            Section("§5.3 枪械级配件");

            // 弹匣扩容 +12
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Add(D.MakeItem("m_mag"));
                gun.Recalc();
                Eq("弹匣扩容", 48, gun.Resolved.MagSize);
            }

            // 同名只生效一次
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Add(D.MakeItem("m_mag"));
                gun.Slots[2].Chain.Add(D.MakeItem("m_mag"));
                gun.Recalc();
                Eq("弹匣扩容同名只算一次", 48, gun.Resolved.MagSize);
            }

            // 高速击发
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Add(D.MakeItem("m_rate"));
                gun.Recalc();
                Near("高速击发间隔", 0.11f * 0.82f, gun.Resolved.FireInterval, 0.0005f);
            }

            // 后坐抑制：散射 ×0.75，换弹 ×0.80
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Add(D.MakeItem("m_recoil"));
                gun.Recalc();
                Near("后坐散射", 1.2f * 0.75f, gun.Resolved.SpreadBase, 0.0005f);
                Near("后坐换弹", 1.8f * 0.80f, gun.Resolved.ReloadTime, 0.0005f);
            }

            // 枪械件不影响投射物
            {
                var r = Cast("g_auto", "m_mag", "b_slug");
                Near("枪械件不作用于弹丸", 12f, r.Projectiles[0].Damage);
            }

            // 枪械件放在子弹后面也生效
            {
                var gun = MakeGun("g_auto");
                gun.Slots[0].Chain.Clear();
                gun.Slots[0].Chain.Add(D.MakeItem("b_slug"));
                gun.Slots[0].Chain.Add(D.MakeItem("m_mag"));
                gun.Recalc();
                Eq("枪械件位置无关", 48, gun.Resolved.MagSize);
            }

            // 弹匣缩小时钳制当前弹药
            {
                var gun = MakeGun("g_auto");
                gun.Slots[1].Chain.Add(D.MakeItem("m_mag"));
                gun.Recalc();
                gun.Ammo = 48;
                gun.Slots[1].Chain.Clear();
                gun.Recalc();
                Eq("卸下扩容后弹药被钳制", 36, gun.Ammo);
            }
        }

        // ---------------- 拖拽落点预览 ----------------

        static void PreviewCases()
        {
            Section("拖拽落点预览（§3.3）");

            var gun = D.Guns["g_auto"];

            // 空链插入子弹：基线 0 → 有伤害，且原链不被修改
            {
                var slot = new SlotState();
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("b_slug"), 0);
                If("预览可插入", pv.CanInsert);
                Near("空链基线伤害 0", 0f, pv.BaseDamage);
                Near("插子弹后 12.6", 12.6f, pv.NewDamage, 0.01f);
                Eq("子弹算有效", true, pv.Effective);
                Eq("原链未被修改", 0, slot.Chain.Count);
            }

            // 全场件插在子弹前：作用于 1 个
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_up"), 0);
                Eq("全场件生效目标数 1", 1, pv.AffectedCount);
                Eq("全场件有效", true, pv.Effective);
                Near("伤 12.6 → 17.01", 17.01f, pv.NewDamage, 0.02f);
                Near("变化率 +35%", 0.35f, pv.DamageDeltaRatio, 0.01f);
                Eq("原链仍为 1 项", 1, slot.Chain.Count);
            }

            // 全场件插在子弹后：仍然作用于 1 个（全场语义与位置无关）
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_up"), 1);
                Eq("全场件放子弹后仍生效", 1, pv.AffectedCount);
                Near("结果与放前面相同", 17.01f, pv.NewDamage, 0.02f);
            }

            // 后继件插在子弹后：0 个目标 → 落点应为红
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_flat"), 1);
                Eq("后继件放子弹后目标数 0", 0, pv.AffectedCount);
                Eq("后继件放子弹后无效", false, pv.Effective);
                Eq("产生 no-target 警告", true, pv.HasNoTarget);
                Near("伤害无变化", 12.6f, pv.NewDamage, 0.01f);
            }

            // 后继件插在子弹前：作用于它后面的 1 个
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_flat"), 0);
                Eq("后继件放前面目标数 1", 1, pv.AffectedCount);
                Eq("后继件放前面有效", true, pv.Effective);
                Near("伤 12.6 → (12+8)×1.05 = 21", 21f, pv.NewDamage, 0.02f);
            }

            // 两颗子弹 + 全场件：作用于 2 个
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_up"), 0);
                Eq("全场件作用于 2 颗", 2, pv.AffectedCount);
                Eq("弹丸数 2", 2, pv.NewProjectiles);
            }

            // 两颗子弹 + 后继件插在中间：只作用于第 2 颗
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_flat"), 1);
                Eq("后继件插中间目标数 1", 1, pv.AffectedCount);
                // 第 1 颗 12.6，第 2 颗 (12+8)=20 → (1+.05*1)=21
                Near("整次期望 12.6+21=33.6", 33.6f, pv.NewDamage, 0.05f);
            }

            // 枪械件：有效，但不改伤害
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_mag"), 0);
                Eq("枪械件有效", true, pv.Effective);
                Eq("枪械件标记", true, pv.IsGunScope);
                Near("枪械件不改该位伤害", 12.6f, pv.NewDamage, 0.01f);
            }

            // 拒绝码：同名上限
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("b_slug"), 0);
                Eq("同名超限被拒", "same-limit", pv.RejectCode);
                Eq("被拒时无效", false, pv.CanInsert);
            }

            // 拒绝码：链满
            {
                var slot = new SlotState();
                foreach (var id in new[] { "b_slug", "b_slug", "m_dmg_up", "m_crit_up", "m_life_up", "m_spread_down" })
                    slot.Chain.Add(D.MakeItem(id));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("b_heavy"), 6);
                Eq("链满被拒", "chain-full", pv.RejectCode);
            }

            // 分裂件的目标数：插在子弹前，作用 1 个但产出 3 发
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_split"), 0);
                Eq("分裂目标数 1", 1, pv.AffectedCount);
                Eq("分裂后弹丸 3", 3, pv.NewProjectiles);
                Near("分裂整次期望 20.79", 20.79f, pv.NewDamage, 0.03f);
            }

            // 弹药变化可预览
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var pv = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_ammo_down"), 0);
                Eq("弹药 2 → 1", 1, pv.NewAmmoCost);
                Eq("基线弹药 2", 2, pv.BaseAmmoCost);
            }

            // 预览绝不修改原链（连续调用两次结果一致）
            {
                var slot = new SlotState();
                slot.Chain.Add(D.MakeItem("b_slug"));
                var a = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_up"), 0);
                var b = Loadout.PreviewInsert(gun, slot, D.MakeItem("m_dmg_up"), 0);
                Near("两次预览结果一致", a.NewDamage, b.NewDamage, 0.0001f);
                Eq("原链长度不变", 1, slot.Chain.Count);
            }
        }

        // ---------------- 区域推进 ----------------

        static void ZoneCases()
        {
            Section("区域推进（gameplay-spec §2）");

            var zones = D.Zones;
            Check("数据: 6 个区域", zones.Count == 6, $"{zones.Count}");
            Check("数据: 末区是 Boss 场", zones.Count > 0 && zones[zones.Count - 1].IsBoss);

            var zp = new ZoneProgress(zones);

            Eq("起点为 1 区", 1, zp.Current);
            Eq("进入 1 区幂等", null, zp.TryEnter(1));
            Eq("不能跳到 3 区", "locked", zp.TryEnter(3));
            If("2 区初始锁定", !zp.CanEnter(2));
            If("1 区门初始关闭", !zp.IsDoorOpen(1));

            var def1 = zp.Get(1);
            for (int i = 0; i < def1.Waves; i++) If($"1 区第 {i + 1} 波开始", zp.BeginWave());
            If("波次用尽后不能再开", !zp.BeginWave());
            If("清区成功", zp.TryClearCurrent());
            Eq("1 区已清空", true, zp.CurrentCleared);
            If("1 区门已开", zp.IsDoorOpen(1));
            If("清空后可进 2 区", zp.CanEnter(2));

            Eq("进入 2 区成功", null, zp.TryEnter(2));
            Eq("当前为 2 区", 2, zp.Current);
            Eq("进区后波次归零", 0, zp.Wave);
            Eq("2 区未清空", false, zp.IsCleared(2));
            If("3 区仍锁定", !zp.CanEnter(3));

            zp.BeginWave();
            If("只打 1 波不能清区", !zp.TryClearCurrent());

            bool ok = true;
            for (int z = 2; z <= zp.ZoneCount; z++)
            {
                if (zp.Current != z && zp.TryEnter(z) != null) { ok = false; break; }
                var def = zp.Get(z);
                for (int w = 0; w < def.Waves; w++) zp.BeginWave();
                if (!zp.TryClearCurrent()) { ok = false; break; }
            }
            If("6 区可依次打通", ok);
            If("末区无门", !zp.IsDoorOpen(zp.ZoneCount));
            Eq("末区 NextZone 为 0", 0, zp.NextZone);
            If("末区标记为最终区", zp.IsFinalZone);

            Eq("越界区域被拒", "bad-zone", zp.TryEnter(99));
            Eq("区域 0 被拒", "bad-zone", zp.TryEnter(0));

            Eq("1 区威胁下限 0", 0f, zones[0].ThreatStart);
            Eq("Boss 区威胁下限 60", 60f, zones[5].ThreatStart);
        }

        // ---------------- Boss ----------------

        static void BossCases()
        {
            Section("Boss 状态机（gameplay-spec §3.1）");

            var b = new BossState(6000f);
            Eq("初始 P1", BossPhase.P1, b.Phase);
            If("初始存活", b.Alive);
            Eq("初始 4 根柱", 4, b.PillarsAlive);
            If("初始无护盾", !b.ShieldUp);
            Near("初始无破防加成", 1f, b.DamageTakenMul, 0.001f);

            b.TakeDamage(1800f, false);
            Near("血量 70%", 0.7f, b.HpRatio, 0.001f);
            Eq("70% 仍 P1", BossPhase.P1, b.Phase);

            b.TakeDamage(240f, false);
            Near("血量降到 66% 以下", b.HpRatio, 0.66f, 0.02f);
            b.Tick(0.016f);
            Eq("进入 P2", BossPhase.P2, b.Phase);

            bool shieldEverOn = false;
            for (int i = 0; i < 2000; i++)
            {
                b.Tick(0.05f);
                if (b.ShieldUp) { shieldEverOn = true; break; }
            }
            If("P2 会开护盾", shieldEverOn);
            If("护盾挡普通攻击", b.IsBlockedByShield(false));
            If("空蚀弹穿透护盾", !b.IsBlockedByShield(true));

            bool broke = b.DamagePillar(0, 800f);
            If("能量柱被打碎", broke);
            Eq("柱剩 3 根", 3, b.PillarsAlive);
            If("碎柱立刻关盾", !b.ShieldUp);
            If("碎柱进入破防", b.IsBreached);
            Near("破防受伤 ×1.5", 1.5f, b.DamageTakenMul, 0.001f);
            If("破防期护盾无效", !b.IsBlockedByShield(false));
            If("同柱不能重复打碎", !b.DamagePillar(0, 800f));

            for (int i = 0; i < 200; i++) b.Tick(0.05f);
            If("破防会结束", !b.IsBreached);
            Near("破防结束恢复 ×1", 1f, b.DamageTakenMul, 0.001f);

            b.TakeDamage(b.Hp * 0.5f, false);
            b.Tick(0.016f);
            Eq("进入 P3", BossPhase.P3, b.Phase);

            bool shock = false;
            for (int i = 0; i < 800; i++)
            {
                var e = b.Tick(0.05f);
                foreach (var x in e) if (x.Kind == BossEventKind.Shockwave) { shock = true; break; }
                if (shock) break;
            }
            If("P3 会放冲击波", shock);

            b.TakeDamage(b.Hp + 1f, false);
            Eq("Boss 死亡", BossPhase.Dead, b.Phase);
            If("死亡后不存活", !b.Alive);
            Near("死亡后血量 0", 0f, b.Hp, 0.001f);

            var b2 = new BossState(6000f);
            bool p1Shield = false;
            for (int i = 0; i < 2000; i++) { b2.Tick(0.05f); if (b2.ShieldUp) p1Shield = true; }
            If("P1 不会开护盾", !p1Shield);
        }

        // ---------------- 链式触发（loadout-spec §1.2） ----------------

        /// <summary>
        /// 链式触发的核心不变量：**总量不变**。
        ///
        /// 运行时不铺 `Projectiles`，而是按 `Steps` 逐项触发。这里用与 `ChainCast` 完全相同的
        /// 执行顺序在测试里重放一遍，然后逐颗比对伤害/散射/弹药等字段 —— 只要执行器与全量结算
        /// 有任何口径差异（例如「全场配件排在子弹前」漏算），这条就会红。
        /// </summary>
        static void ChainTriggerCases()
        {
            Section("§1.2 链式触发");

            // 时间轴：每项占一格，间隔 = 击发间隔
            {
                var r = Cast("g_auto", "m_dmg_up", "b_slug");
                Eq("步骤数 = 链长", 2, r.Steps.Count);
                If("首步在 0 时刻", Math.Abs(r.Steps[0].Delay) < 0.0001f);
                Near("次步间隔 = 击发间隔", D.Guns["g_auto"].FireInterval, r.Steps[1].Delay);
                Near("整链时长", D.Guns["g_auto"].FireInterval, r.Duration);
                Eq("子弹件带模板", ChainStepKind.Bullet, r.Steps[1].Kind);
                Eq("衍生件带配件", ChainStepKind.Modifier, r.Steps[0].Kind);
            }

            // 枪械级配件不占时间轴
            {
                var r = Cast("g_auto", "m_mag", "b_slug");
                Eq("枪械件不进时间轴", 1, r.Steps.Count);
                Eq("剩下的是子弹件", ChainStepKind.Bullet, r.Steps[0].Kind);
            }

            // 逐条链重放：链式触发总量必须等于全量结算
            string[][] chains =
            {
                new[] { "b_slug" },
                new[] { "m_dmg_up", "b_slug" },
                new[] { "m_dmg_flat", "m_dmg_up", "b_slug" },
                new[] { "m_dmg_up", "m_dmg_flat", "b_slug" },
                new[] { "b_slug", "m_dmg_flat" },
                new[] { "m_split", "b_slug" },
                new[] { "m_split", "m_dmg_up", "b_slug" },
                new[] { "m_dmg_up", "m_split", "b_slug" },
                new[] { "b_slug", "b_slug" },
                new[] { "m_split", "m_split", "b_slug" },
                new[] { "m_spread_down", "b_slug" },
                new[] { "m_ammo_down", "b_slug" },
                new[] { "b_pellet", "b_pellet" },
                new[] { "m_split", "b_pellet" },
                new[] { "m_dmg_up", "m_split", "m_dmg_flat", "b_heavy" },
                new[] { "m_crit_up", "m_critdmg", "m_pierce", "m_blast", "b_lance" },
            };

            string[] gunIds = { "g_auto", "g_grenade", "g_single" };
            foreach (var gunId in gunIds)
            {
                foreach (var chain in chains)
                {
                    var full = Cast(gunId, chain);
                    var replays = ReplayChain(gunId, full);
                    string label = $"{gunId} [{string.Join(",", chain)}]";

                    Eq(label + " 弹丸数", full.Projectiles.Count, replays.Count);

                    float want = 0f, got = 0f;
                    for (int i = 0; i < full.Projectiles.Count; i++) want += full.Projectiles[i].Damage;
                    for (int i = 0; i < replays.Count; i++) got += replays[i].Damage;
                    Near(label + " 总伤害", want, got, 0.01f);

                    float wantExp = full.ExpectedDamage, gotExp = 0f;
                    for (int i = 0; i < replays.Count; i++) gotExp += replays[i].ExpectedDamage;
                    Near(label + " 整次期望", wantExp, gotExp, 0.01f);

                    // 散射 / 穿透 / 溅射也要一致 —— 只比伤害会漏掉"行为类配件没生效"
                    for (int i = 0; i < Math.Min(full.Projectiles.Count, replays.Count); i++)
                    {
                        Near(label + $" 散射#{i}", full.Projectiles[i].Spread, replays[i].Spread, 0.001f);
                        Eq(label + $" 穿透#{i}", full.Projectiles[i].Pierce, replays[i].Pierce);
                        Eq(label + $" 反弹#{i}", full.Projectiles[i].Bounce, replays[i].Bounce);
                        Near(label + $" 溅射#{i}", full.Projectiles[i].SplashRadius, replays[i].SplashRadius, 0.001f);
                    }
                }
            }

            // 分裂代数上限：链式触发也必须遵守
            {
                var r = Cast("g_auto", "m_split", "m_split", "m_split", "m_split", "b_slug");
                var replays = ReplayChain("g_auto", r);
                Eq("分裂代上限 弹丸数", r.Projectiles.Count, replays.Count);
            }
        }

        /// <summary>
        /// 在测试里重放 `ChainCast` 的执行顺序（与 View/ChainCast.cs 同构，故意不复用 ——
        /// 复用就等于用被测代码证明被测代码是对的）。返回最终在场弹丸的描述符。
        /// </summary>
        static List<ProjectileDesc> ReplayChain(string gunId, CastResult cast)
        {
            var gun = D.Guns[gunId];
            var alive = new List<ProjectileDesc>();
            var firedMods = new List<ChainStep>();

            for (int s = 0; s < cast.Steps.Count; s++)
            {
                var step = cast.Steps[s];
                if (step.Kind == ChainStepKind.Bullet)
                {
                    var descs = new List<ProjectileDesc> { step.Bullet.Clone() };

                    // 补算在它之前触发、且作用于它的**全部**前置配件，按链顺序
                    for (int k = 0; k < firedMods.Count; k++)
                    {
                        var mod = firedMods[k].Mod;
                        if (mod == null) continue;

                        var nextStep = new List<ProjectileDesc>(descs.Count);
                        foreach (var d in descs)
                        {
                            if (!Hits(mod, firedMods[k].BulletsSeen, d.SourceIndex)) { nextStep.Add(d); continue; }
                            nextStep.AddRange(Loadout.ApplyModRuntime(d, mod, gun));
                        }
                        descs = nextStep;
                    }

                    alive.AddRange(descs);
                    continue;
                }

                if (step.Mod == null) continue;
                firedMods.Add(step);

                var next = new List<ProjectileDesc>(alive.Count);
                foreach (var d in alive)
                {
                    if (!Hits(step.Mod, step.BulletsSeen, d.SourceIndex)) { next.Add(d); continue; }
                    next.AddRange(Loadout.ApplyModRuntime(d, step.Mod, gun));
                }
                alive = next;
            }
            return alive;
        }

        static bool Hits(ModifierDef mod, int bulletsSeen, int sourceIndex)
        {
            if (mod == null) return false;
            if (mod.Scope == Scope.Whole) return true;
            if (mod.Scope == Scope.After) return sourceIndex >= bulletsSeen;
            return false;
        }

        static CastResult Cast(string gunId, params string[] chain)
        {
            var gun = D.Guns[gunId];
            var slot = new SlotState();
            foreach (var id in chain)
            {
                var it = D.MakeItem(id);
                if (it == null) throw new Exception("未知配件 id: " + id);
                slot.Chain.Add(it);
            }
            return Loadout.ResolveCast(gun, slot);
        }

        static GunRuntime MakeGun(string gunId)
        {
            var slots = D.BuildStarterSlots(gunId);
            return new GunRuntime(D.Guns[gunId], slots);
        }

        static ProjectileDesc FindDesc(CastResult r, string bulletId)
        {
            foreach (var d in r.Projectiles)
                if (d.Source != null && d.Source.Id == bulletId) return d;
            return null;
        }

        static void Section(string name) => Console.WriteLine("-- " + name);

        static void Eq<T>(string name, T expect, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(expect, actual)) Ok(name);
            else Fail($"{name}: 期望 {expect} 实际 {actual}");
        }

        static void Near(string name, float expect, float actual, float tol = 0.001f)
        {
            if (Math.Abs(expect - actual) <= tol) Ok(name);
            else Fail($"{name}: 期望 {Fmt(expect)} 实际 {Fmt(actual)} (容差 {tol})");
        }

        static void If(string name, bool cond)
        {
            if (cond) Ok(name); else Fail(name + ": 条件为假");
        }

        static string Fmt(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
        static bool HasFlag(string flag) =>
            Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

        static void Ok(string name) => s_pass++;
        static void Fail(string msg) { s_fail++; s_fails.Add(msg); }

        /// <summary>带说明的条件断言（与 `If` 等价，失败时带原因）。</summary>
        static void Check(string name, bool cond, string why = "")
        {
            if (cond) Ok(name);
            else Fail(why.Length > 0 ? $"{name} :: {why}" : name);
        }

        static string FindData()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var p = Path.Combine(dir, "Assets", "Resources", "Data", "sdata.json");
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            throw new FileNotFoundException("找不到 sdata.json");
        }
    }
}

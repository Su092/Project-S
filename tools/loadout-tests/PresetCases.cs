using System;
using System.Collections.Generic;
using S.Core;

namespace LoadoutTests
{
    /// <summary>
    /// 方案存档的回归断言。
    ///
    /// 重点防两类致命错误（都会直接击穿经济系统）：
    ///  1. **配件增殖**：存/读一轮后配件总数变多
    ///  2. **配件丢失**：读档失败后配件消失
    ///
    /// 所以每条用例都统计「背包 + 所有链」的配件总数，读档前后必须相等。
    /// </summary>
    static class PresetCases
    {
        public static void Run(SData data, Action<string, bool, string> check)
        {
            Console.WriteLine("-- 方案存档（§8 构筑切换）");

            // ---- 基本情况 ----
            {
                var run = new RunState(data, 1);
                var guns = MakeGuns(data);
                var store = new PresetStore(data);

                // 造一个两件链的设定
                var item1 = data.MakeItem("b_slug");
                var item2 = data.MakeItem("m_dmg_up");
                guns[0].Slots[0].Chain.Clear();
                guns[0].Slots[0].Chain.Add(item2);
                guns[0].Slots[0].Chain.Add(item1);
                guns[0].Recalc();

                int totalBefore = TotalItems(run, guns);

                var saveCode = store.Save(0, guns);
                check("存档成功", saveCode == null, saveCode ?? "");
                check("槽位有方案", store.Has(0), "Has(0) 为 false");
                // 方案统计所有枪的所有位：g_auto 3 位（位0 两件 + 位1/2 各有起手弹）+ g_single 1 位
                // 实际件数 = 2 + 1 + 1 + 1 = 5（MakeGuns 的起手链未清）
                int expectItems = TotalItems(new RunState(data, 1), guns);
                check("方案件数 = 当前链总件数",
                    PresetStore.CountItems(store.Get(0)) == expectItems,
                    $"{PresetStore.CountItems(store.Get(0))} vs {expectItems}");

                // 改掉当前链，再读档应还原
                guns[0].Slots[0].Chain.Clear();
                guns[0].Recalc();
                // 配件不能凭空消失：手动放回背包
                run.Bag.Add(item1);
                run.Bag.Add(item2);

                var loadCode = store.Load(0, guns, run);
                check("读档成功", loadCode == null, loadCode ?? "");
                check("读档后链还原为 2 件", guns[0].Slots[0].Chain.Count == 2,
                    $"{guns[0].Slots[0].Chain.Count}");
                check("读档后链顺序不变",
                    guns[0].Slots[0].Chain.Count == 2 &&
                    guns[0].Slots[0].Chain[0].Id == "m_dmg_up" &&
                    guns[0].Slots[0].Chain[1].Id == "b_slug",
                    "顺序错误");

                int totalAfter = TotalItems(run, guns);
                check("存读一轮配件总数不变", totalBefore == totalAfter,
                    $"{totalBefore} → {totalAfter}");
            }

            // ---- 空槽读档 ----
            {
                var run = new RunState(data, 2);
                var guns = MakeGuns(data);
                var store = new PresetStore(data);
                var code = store.Load(1, guns, run);
                check("空槽读档被拒", code == "empty-slot", code ?? "null");
            }

            // ---- 越界槽位 ----
            {
                var guns = MakeGuns(data);
                var store = new PresetStore(data);
                check("越界存档被拒", store.Save(9, guns) == "bad-slot", "code=" + store.Save(9, guns));
                check("负槽存档被拒", store.Save(-1, guns) == "bad-slot", "code=" + store.Save(-1, guns));
            }

            // ---- 背包满时读档应被拒，且不改变任何状态 ----
            {
                var run = new RunState(data, 3, bagCapacity: 4);
                var guns = MakeGuns(data);
                var store = new PresetStore(data);

                // 链里放 3 件（读档时需先退回背包）
                guns[0].Slots[0].Chain.Clear();
                for (int i = 0; i < 3; i++)
                    guns[0].Slots[0].Chain.Add(data.MakeItem("b_slug"));
                guns[0].Recalc();

                store.Save(0, guns);

                // 背包再塞满
                run.Bag.Add(data.MakeItem("m_dmg_up"));
                run.Bag.Add(data.MakeItem("m_crit_up"));
                // 现在背包 2，链里 3，容量 4 → 2+3=5 > 4，读档应失败

                int chainsBefore = guns[0].Slots[0].Chain.Count;
                int bagBefore = run.Bag.Count;

                var code = store.Load(0, guns, run);
                check("背包不够时读档被拒", code == "bag-full", code ?? "null");
                check("被拒后链未变", guns[0].Slots[0].Chain.Count == chainsBefore,
                    $"{guns[0].Slots[0].Chain.Count} vs {chainsBefore}");
                check("被拒后背包未变", run.Bag.Count == bagBefore,
                    $"{run.Bag.Count} vs {bagBefore}");
            }

            // ---- 配件已被丢弃时读档应失败并回滚 ----
            {
                var run = new RunState(data, 4);
                var guns = MakeGuns(data);
                var store = new PresetStore(data);

                var a = data.MakeItem("b_slug");
                var b = data.MakeItem("m_dmg_up");
                guns[0].Slots[0].Chain.Clear();
                guns[0].Slots[0].Chain.Add(a);
                guns[0].Slots[0].Chain.Add(b);
                guns[0].Recalc();

                store.Save(0, guns);

                // 模拟「其中一件被丢弃」：清链、只退回一件
                guns[0].Slots[0].Chain.Clear();
                run.Bag.Add(a);   // b 故意不退回 = 被丢弃

                var code = store.Load(0, guns, run);
                check("配件缺失时读档被拒", code == "missing-item", code ?? "null");
                check("回滚后配件不丢", run.Bag.Count == 1, $"bag={run.Bag.Count}");
                check("回滚后链为空", guns[0].Slots[0].Chain.Count == 0,
                    $"{guns[0].Slots[0].Chain.Count}");
            }

            // ---- 存档后继续改链，不应影响已存方案 ----
            {
                var run = new RunState(data, 5);
                var guns = MakeGuns(data);
                var store = new PresetStore(data);

                guns[0].Slots[0].Chain.Clear();
                guns[0].Slots[0].Chain.Add(data.MakeItem("b_slug"));
                guns[0].Recalc();

                store.Save(0, guns);
                int snapshot = PresetStore.CountItems(store.Get(0));

                // 继续往链里加东西
                guns[0].Slots[0].Chain.Add(data.MakeItem("m_dmg_up"));

                check("存档是快照（不受后续改动影响）",
                    PresetStore.CountItems(store.Get(0)) == snapshot,
                    $"{PresetStore.CountItems(store.Get(0))} vs {snapshot}");
            }

            // ---- 清除方案 ----
            {
                var guns = MakeGuns(data);
                var store = new PresetStore(data);
                store.Save(2, guns);
                store.Clear(2);
                check("清除后槽位为空", !store.Has(2), "Has(2) 仍为 true");
            }
        }

        static List<GunRuntime> MakeGuns(SData data)
        {
            var list = new List<GunRuntime>();
            foreach (var id in new[] { "g_auto", "g_single" })
            {
                if (data.Guns.TryGetValue(id, out var def))
                    list.Add(new GunRuntime(def, data.BuildStarterSlots(id)));
            }
            return list;
        }

        static int TotalItems(RunState run, List<GunRuntime> guns)
        {
            int n = run.Bag.Count;
            foreach (var g in guns)
                foreach (var s in g.Slots) n += s.Chain.Count;
            return n;
        }
    }
}

using System;
using S.Core;

namespace LoadoutTests
{
    static class XpBench
    {
        public static void Run(SData data)
        {
            Console.WriteLine();
            Console.WriteLine("===== 经验曲线实测（单图时间轴）=====");

            // 按时间轴的「玩家实际能清掉的量」估算经验投放。
            // 为什么不用「刷了多少只」：单图是无限刷怪，算出几只没有意义；
            // 真正的上限是玩家的清场速度（DPS × 时长）。
            var d = data.Director;
            float total = 0f;
            Console.WriteLine("  阶段      时长   并发  间隔  预估可清");
            var phases = d.Phases;
            for (int i = 0; i < phases.Count; i++)
            {
                float from = phases[i].At;
                float to = i + 1 < phases.Count ? phases[i + 1].At : d.BossAt;
                float dur = to - from;
                // 玩家清场速度约 0.55 只/秒（首轮校准实测的连发枪裸弹 DPS 60 / 敌人 60HP 量级）
                float clearable = Math.Min(dur * 0.55f, dur / Math.Max(0.1f, phases[i].Interval) * phases[i].Concurrency * 0.5f);
                float avgXp = 0f;
                foreach (var k in phases[i].Pool) avgXp += XpOf(k);
                avgXp /= Math.Max(1, phases[i].Pool.Length);
                total += clearable * avgXp;
                Console.WriteLine($"  {phases[i].Name,-8} {dur,6:0}s  {phases[i].Concurrency,3}  {phases[i].Interval,4:0.0}  {clearable,7:0} 只");
            }

            // 精英：3 只 × 48 XP
            float eliteXp = 3 * 8 * d.Elite.XpMul;
            Console.WriteLine($"  精英 3 只 × {8 * d.Elite.XpMul} = {eliteXp:0}");

            // Boss 战杂兵：约 20 只
            float bossAdds = 20 * 10f;
            Console.WriteLine($"  Boss 战杂兵 ≈ {bossAdds:0}");

            int grand = (int)(total + eliteXp + bossAdds);
            Console.WriteLine($"  合计 ≈ {grand}");

            // 推算到达等级
            int lv = 1;
            int acc = 0;
            while (lv < RunState.MaxLevel && acc + RunState.NeedFor(lv) <= grand)
            {
                acc += RunState.NeedFor(lv);
                lv++;
            }
            Console.WriteLine($"  → 预计到达 LV {lv}（累计 {acc} XP），三选一 {lv - 1} 次 + Boss 3 次 = {lv + 2} 次");

            // 判据：docs/gameplay-spec.md §11 要求 13–17 级、16–19 次三选一
            bool lvOk = lv >= 13 && lv <= 17;
            int picks = lv - 1 + 3;
            bool pickOk = picks >= 16 && picks <= 19;
            Console.WriteLine($"  {(lvOk ? "PASS" : "FAIL")} 到达等级 {lv} 落在 13–17");
            Console.WriteLine($"  {(pickOk ? "PASS" : "FAIL")} 三选一次数 {picks} 落在 16–19");
        }

        static int XpOf(string kind) => kind switch
        {
            "Spitter" => 10,
            "Shield" => 16,
            "Drone" => 14,
            _ => 8,
        };
    }
}

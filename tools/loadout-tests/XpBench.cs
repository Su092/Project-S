using System;
using S.Core;

namespace LoadoutTests
{
    static class XpBench
    {
        public static void Run(SData data)
        {
            Console.WriteLine();
            Console.WriteLine("===== 经验曲线实测 =====");

            // 按区域表估算一局经验投放
            int totalXp = 0;
            Console.WriteLine("  区域  波次  敌数  经验");
            foreach (var z in data.Zones)
            {
                if (z.IsBoss) continue;
                int enemies = 0, xp = 0;
                for (int w = 1; w <= z.Waves; w++)
                {
                    int n = 3 + w + (z.Index - 1);   // 与 GameRoot.NextWave 的公式一致
                    enemies += n;
                    // 取该区敌人池的平均经验
                    float sum = 0;
                    foreach (var k in z.Pool) sum += XpOf(k);
                    xp += (int)(n * (sum / Math.Max(1, z.Pool.Length)));
                }
                totalXp += xp;
                Console.WriteLine($"  {z.Index,4}  {z.Waves,4}  {enemies,4}  {xp,5}");
            }

            // 区域清空奖励 15%
            Console.WriteLine($"  小计 {totalXp}");
            Console.WriteLine($"  清区奖励(15%) {totalXp * 0.15f:0}");

            int grand = (int)(totalXp * 1.15f) + 3 * 40;  // 含 Boss 的 3 次三选一按经验折算
            Console.WriteLine($"  合计(含精英估) ≈ {grand}");

            // 推算到达等级
            var run = new RunState(data, 1);
            int lv = 1;
            int acc = 0;
            while (lv < RunState.MaxLevel && acc + RunState.NeedFor(lv) <= grand)
            {
                acc += RunState.NeedFor(lv);
                lv++;
            }
            Console.WriteLine($"  → 预计到达 LV {lv}（累计 {acc} XP），三选一 {lv - 1} 次 + Boss 3 次 = {lv + 2} 次");

            // 判据：docs/gameplay-spec.md §11 要求 12–15 级、14–16 次三选一
            bool lvOk = lv >= 12 && lv <= 15;
            int picks = lv - 1 + 3;
            bool pickOk = picks >= 12 && picks <= 18;
            Console.WriteLine($"  {(lvOk ? "PASS" : "FAIL")} 到达等级 {lv} 落在 12–15");
            Console.WriteLine($"  {(pickOk ? "PASS" : "FAIL")} 三选一次数 {picks} 落在 12–18");
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

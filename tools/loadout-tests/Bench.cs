using System;
using System.Collections.Generic;
using S.Core;

namespace LoadoutTests
{
    /// <summary>
    /// 数值校准基准：实测三把枪在不同构筑下的真实 DPS。
    ///
    /// 输出用于校准 docs/loadout-spec.md §5 的 damageMul 与 docs/gameplay-spec.md §5 的经验曲线。
    /// 判据：等资源投入下三把枪 Sustained DPS 应落在 ±30% 内（初版目标，实测后调整）。
    /// </summary>
    static class Bench
    {
        public static void RunAll(SData data)
        {
            Console.WriteLine();
            Console.WriteLine("===== 数值基准（实弹命中模拟，20m，目标半径 0.5m）=====");

            // 每条链代表一种「同资源量级」的投入。资源量级用配件总数近似：
            //   裸弹 = 1 件；浅构筑 = 3 件；深构筑 = 5 件。
            var cases = new (string gun, string desc, string label)[]
            {
                // ---- 裸弹基线 ----
                ("g_auto",    "b_slug",                                  "裸弹"),
                ("g_grenade", "b_slug",                                  "裸弹"),
                ("g_single",  "b_slug",                                  "裸弹"),

                // ---- 浅构筑（3 件）----
                ("g_auto",    "m_dmg_up,m_dmg_flat,b_slug",              "浅·加伤"),
                ("g_grenade", "m_dmg_up,m_dmg_flat,b_slug",              "浅·加伤"),
                ("g_single",  "m_dmg_up,m_dmg_flat,b_slug",              "浅·加伤"),

                // ---- 深构筑（5 件）----
                ("g_auto",    "m_dmg_up,m_dmg_flat,m_dmg_flat,m_crit_up,b_slug", "深·加伤+暴击"),
                ("g_grenade", "m_dmg_up,m_dmg_flat,m_crit_up,b_heavy,b_heavy",   "深·双发重芯"),
                ("g_single",  "m_dmg_up,m_dmg_flat,m_dmg_flat,m_crit_up,b_slug", "深·加伤+暴击"),

                // ---- 分裂流（散射代价最明显）----
                ("g_auto",    "m_split,m_dmg_up,b_pellet",               "分裂+霰粒"),
                ("g_single",  "m_split,m_dmg_up,b_heavy",                "分裂+重芯"),

                // ---- 精准流 ----
                ("g_single",  "m_spread_down,m_crit_up,m_critdmg,b_lance", "精准狙击"),
                ("g_auto",    "m_spread_down,b_pellet,b_pellet",         "稳定霰弹"),
            };

            var results = new List<DpsBench.Result>();
            foreach (var c in cases)
            {
                var r = DpsBench.Run(data, c.gun, c.desc);
                results.Add(r);

                Console.WriteLine(
                    $"  {r.GunName,-12} {c.label,-14} " +
                    $"纸面 {r.PaperDps,6:0.0}  实测 {r.RealDps,6:0.0}  " +
                    $"命中 {r.HitRate * 100f,5:0.0}%  " +
                    $"射速 {r.CastsPerSec,4:0.00}/s");
            }

            // ---- 均衡判据：单体档位 ----
            Console.WriteLine();
            var naked = results.GetRange(0, 3);
            var shallow = results.GetRange(3, 3);
            var deep = results.GetRange(6, 3);

            CheckSpread("裸弹档（单体）", naked, 0.30f);
            CheckSpread("浅构筑档（单体）", shallow, 0.30f);
            CheckSpread("深构筑档（单体）", deep, 0.30f);

            // ---- 群伤场景：溅射武器的真实价值在这里 ----
            Console.WriteLine();
            Console.WriteLine("----- 多目标场景（5 个敌人，6m×3m 区域）-----");
            foreach (var c in new (string gun, string desc, string label)[]
            {
                ("g_auto",    "m_dmg_up,m_dmg_flat,b_slug", "浅·加伤"),
                ("g_grenade", "m_dmg_up,m_dmg_flat,b_slug", "浅·加伤"),
                ("g_single",  "m_dmg_up,m_dmg_flat,b_slug", "浅·加伤"),
                ("g_auto",    "m_dmg_up,m_dmg_flat,m_dmg_flat,m_crit_up,b_slug", "深·加伤+暴击"),
                ("g_grenade", "m_dmg_up,m_dmg_flat,m_blast,b_heavy,b_heavy",     "深·爆裂双发"),
                ("g_single",  "m_dmg_up,m_dmg_flat,m_blast,b_heavy",             "深·爆裂"),
            })
            {
                var r = DpsBench.RunMulti(data, c.gun, c.desc);
                Console.WriteLine(
                    $"  {r.GunName,-12} {c.label,-14} 群伤DPS {r.RealDps,7:0.0}  " +
                    $"命中 {r.HitRate * 100f,5:0.0}%");
            }

            // ---- 命中率可读性：散射必须能造成可感知的差异 ----
            Console.WriteLine();
            var splitPellet = results.Find(r => r.ChainDesc.Contains("m_split") && r.ChainDesc.Contains("b_pellet"));
            var nakedAuto = results.Find(r => r.GunName.Contains("蜂鸣") && r.ChainDesc == "b_slug");
            if (splitPellet.ChainDesc != null && nakedAuto.ChainDesc != null)
            {
                float drop = nakedAuto.HitRate - splitPellet.HitRate;
                Console.WriteLine($"  散射影响：裸弹命中 {nakedAuto.HitRate * 100f:0.0}% → 分裂霰粒 {splitPellet.HitRate * 100f:0.0}%（差 {drop * 100f:0.0} 个百分点）");
                Console.WriteLine(drop > 0.10f
                    ? "  PASS 散射造成显著命中率差异（配件选择有真实代价）"
                    : "  WARN 散射差异过小（散射参数可能不足以形成取舍）");
            }
        }

        static void CheckSpread(string label, List<DpsBench.Result> rs, float tol)
        {
            float min = float.MaxValue, max = 0f;
            string minName = "", maxName = "";
            foreach (var r in rs)
            {
                if (r.RealDps < min) { min = r.RealDps; minName = r.GunName; }
                if (r.RealDps > max) { max = r.RealDps; maxName = r.GunName; }
            }
            if (min <= 0.01f)
            {
                Console.WriteLine($"  WARN {label}: 存在零输出，无法比较");
                return;
            }

            float ratio = max / min;
            bool ok = ratio <= 1f + tol;
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} {label}: {minName} {min:0.0} / {maxName} {max:0.0} = {ratio:0.00}×（判据 ≤{1f + tol:0.00}×）");
        }
    }
}

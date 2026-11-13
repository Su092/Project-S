using System;
using System.Collections.Generic;
using S.Core;

namespace LoadoutTests
{
    /// <summary>
    /// 数值校准工具：用**真实散射分布**做蒙特卡洛命中模拟，算实际 DPS。
    ///
    /// 为什么不能只算纸面 DPS：纸面公式假设 100% 命中，但霰粒散射 +3°、
    /// 分裂后每颗再 +2°，在 20m 距离上偏得离谱。真实 DPS = 纸面 DPS × 命中率。
    /// 命中率必须由弹道模拟得出，靠猜会错一个量级。
    ///
    /// 模型（对应 docs/gameplay-spec.md §3 与 loadout-spec §3）：
    ///  - 目标：半径 0.5m 的球（敌人躯干近似），位于正前方指定距离
    ///  - 每颗弹丸：在散射锥内均匀取偏移，直线飞行（榴弹走抛射）
    ///  - 命中判定：弹丸轨迹与目标球的最近距离 &lt; 半径
    ///  - 存在时间到期前未命中 = 未命中
    /// </summary>
    static class DpsBench
    {
        public struct Result
        {
            public string GunName;
            public string ChainDesc;
            public float PaperDps;      // 纸面（命中率 100%）
            public float RealDps;       // 实弹模拟（含换弹停机）
            public float HitRate;
            public float CastsPerSec;
            public int ProjectileSamples;
        }

        const float TargetRadius = 0.5f;   // 敌人躯干半径
        const float TargetDistance = 20f;  // 代表交战距离

        /// <summary>
        /// 多目标场景：评估溅射武器的真实价值。
        ///
        /// 为什么要单独测：单体模拟会系统性低估榴弹枪 —— 它的核心价值是溅射清群。
        /// 只测单体就下结论「榴弹枪弱 2.25 倍」是错的（这轮差点据此改了 damageMul）。
        /// </summary>
        public static Result RunMulti(SData data, string gunId, string chainDesc,
            int targetCount = 5, float distance = TargetDistance, int casts = 800, int seed = 777)
        {
            var gun = data.Guns[gunId];
            var slots = data.BuildStarterSlots(gunId);
            slots[0].Chain.Clear();
            slots[0].Chain.AddRange(ParseChain(data, chainDesc));

            var rt = new GunRuntime(gun, slots);
            var rng = new Random(seed);

            // 目标散布在正前方一个 6m 宽 × 3m 深的区域
            var targets = new List<(float x, float y, float z)>();
            for (int i = 0; i < targetCount; i++)
            {
                float tx = -3f + (float)rng.NextDouble() * 6f;
                float tz = distance - 1.5f + (float)rng.NextDouble() * 3f;
                targets.Add((tx, 0f, tz));
            }

            float damage = 0f;
            float simTime = 0f;
            int hits = 0, projectiles = 0;

            for (int c = 0; c < casts; c++)
            {
                int idx = rt.PeekNextSlot();
                if (idx < 0) break;
                var cast = Loadout.ResolveCast(rt.Resolved, rt.Slots[idx]);
                if (cast.Projectiles.Count == 0) break;

                simTime += rt.Resolved.FireInterval;

                foreach (var d in cast.Projectiles)
                {
                    projectiles++;
                    var hitPoint = Trace(d, gun, targets, rng);
                    if (hitPoint == null) continue;
                    hits++;

                    float baseDmg = d.Damage;
                    if (rng.NextDouble() < d.Crit) baseDmg *= d.CritDmg;

                    if (d.SplashRadius > 0.01f)
                    {
                        // 溅射：对每个目标按距离衰减（gameplay-spec §5.2 的 0.45 边缘）
                        foreach (var t in targets)
                        {
                            float dist = Dist(hitPoint.Value, t);
                            if (dist > d.SplashRadius) continue;
                            float falloff = 1f - 0.55f * (dist / d.SplashRadius);
                            damage += baseDmg * falloff;
                        }
                    }
                    else
                    {
                        damage += baseDmg;
                    }
                }

                if ((c + 1) % Math.Max(1, rt.Resolved.MagSize / Math.Max(1, cast.AmmoCost)) == 0)
                    simTime += rt.Resolved.ReloadTime;
            }

            return new Result
            {
                GunName = gun.Name,
                ChainDesc = chainDesc,
                PaperDps = PaperDamage(data, gunId, chainDesc) * (simTime > 0f ? casts / simTime : 0f),
                RealDps = simTime > 0f ? damage / simTime : 0f,
                HitRate = projectiles > 0 ? (float)hits / projectiles : 0f,
                CastsPerSec = simTime > 0f ? casts / simTime : 0f,
                ProjectileSamples = projectiles,
            };
        }

        static float Dist((float x, float y, float z) a, (float x, float y, float z) b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// 沿弹道采样，返回首个命中的目标点（无命中返回 null）。
        /// 朝向：瞄准**最近目标**的方向（真实玩家会瞄准敌人，不是盲射正前方）。
        /// 早先朝正前方 +Z 盲射、目标随机散布 → 命中率近 0，那是测试场景错了（踩过）。
        /// </summary>
        static (float x, float y, float z)? Trace(ProjectileDesc d, GunDef gun,
            List<(float x, float y, float z)> targets, Random rng)
        {
            if (targets.Count == 0) return null;

            // 瞄准最近的目标
            var aimAt = targets[0];
            float bestD = float.MaxValue;
            foreach (var t in targets)
            {
                float dd = t.x * t.x + t.y * t.y + t.z * t.z;
                if (dd < bestD) { bestD = dd; aimAt = t; }
            }

            float aimLen = (float)Math.Sqrt(aimAt.x * aimAt.x + aimAt.y * aimAt.y + aimAt.z * aimAt.z);
            if (aimLen < 0.01f) return null;
            float ax = aimAt.x / aimLen, ay = aimAt.y / aimLen, az = aimAt.z / aimLen;

            float spreadRad = d.Spread * 0.0174533f;
            double r = Math.Sqrt(rng.NextDouble()) * spreadRad * 0.5;
            double th = rng.NextDouble() * Math.PI * 2.0;

            // 在瞄准方向的垂直平面上取偏移
            var fwd = new Vector3(ax, ay, az);
            var up = Math.Abs(fwd.Y) > 0.9f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
            var right = Vector3.Normalize(Vector3.Cross(up, fwd));
            var realUp = Vector3.Cross(fwd, right);

            float ox = (float)(Math.Cos(th) * r);
            float oy = (float)(Math.Sin(th) * r);
            var dir = fwd + right * ox + realUp * oy;
            float len = dir.Length;
            if (len < 0.001f) return null;
            float dx = dir.X / len, dy = dir.Y / len, dz = dir.Z / len;

            float speed = gun.MuzzleSpeed * d.SpeedMul;
            float gravity = gun.Mode == ProjectileMode.Ballistic ? gun.Gravity : 0f;
            if (speed <= 0.01f) return null;

            const float StepSize = 0.1f;
            float totalLen = Math.Min(d.Lifetime * speed, aimLen + 20f);
            int steps = Math.Max(8, (int)(totalLen / StepSize));

            for (int i = 0; i <= steps; i++)
            {
                float t = d.Lifetime * i / steps;
                float px = dx * speed * t;
                float py = dy * speed * t;
                float pz = dz * speed * t - 0.5f * gravity * t * t;

                foreach (var tg in targets)
                {
                    float ex = px - tg.x, ey = py - tg.y, ez = pz - tg.z;
                    if (ex * ex + ey * ey + ez * ez <= TargetRadius * TargetRadius)
                        return (px, py, pz);
                }
            }
            return null;
        }

        /// <summary>极简 3D 向量（本工程是纯 C# 测试，不能引 UnityEngine）。</summary>
        struct Vector3
        {
            public float X, Y, Z;
            public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
            public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

            public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.X * s, a.Y * s, a.Z * s);

            public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

            public static Vector3 Normalize(Vector3 a)
            {
                float l = a.Length;
                return l < 1e-6f ? new Vector3(0, 0, 1) : new Vector3(a.X / l, a.Y / l, a.Z / l);
            }
        }

        public static Result Run(SData data, string gunId, string chainDesc,
            float distance = TargetDistance, int casts = 1200, int seed = 12345)
        {
            var gun = data.Guns[gunId];
            var slots = data.BuildStarterSlots(gunId);

            var chain = ParseChain(data, chainDesc);
            slots[0].Chain.Clear();
            slots[0].Chain.AddRange(chain);

            var rt = new GunRuntime(gun, slots);
            var rng = new Random(seed);

            int hits = 0, projectiles = 0;
            float damage = 0f;
            float simTime = 0f;

            for (int c = 0; c < casts; c++)
            {
                int idx = rt.PeekNextSlot();
                if (idx < 0) break;

                var cast = Loadout.ResolveCast(rt.Resolved, rt.Slots[idx]);
                if (cast.Projectiles.Count == 0) break;

                simTime += rt.Resolved.FireInterval;

                foreach (var d in cast.Projectiles)
                {
                    projectiles++;
                    if (Hits(d, gun, distance, rng))
                    {
                        hits++;
                        float dmg = d.Damage;
                        if (rng.NextDouble() < d.Crit) dmg *= d.CritDmg;
                        damage += dmg;
                    }
                }

                // 弹药预算：累计到弹匣上限就计一次换弹
                if ((c + 1) % Math.Max(1, rt.Resolved.MagSize / Math.Max(1, cast.AmmoCost)) == 0)
                    simTime += rt.Resolved.ReloadTime;
            }

            float paper = PaperDamage(data, gunId, chainDesc);

            return new Result
            {
                GunName = gun.Name,
                ChainDesc = chainDesc,
                PaperDps = paper * (simTime > 0f ? casts / simTime : 0f),
                RealDps = simTime > 0f ? damage / simTime : 0f,
                HitRate = projectiles > 0 ? (float)hits / projectiles : 0f,
                CastsPerSec = simTime > 0f ? casts / simTime : 0f,
                ProjectileSamples = projectiles,
            };
        }

        static List<ChainItem> ParseChain(SData data, string desc)
        {
            var list = new List<ChainItem>();
            foreach (var id in desc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var it = data.MakeItem(id.Trim());
                if (it != null) list.Add(it);
            }
            return list;
        }

        static float PaperDamage(SData data, string gunId, string chainDesc)
        {
            var gun = data.Guns[gunId];
            var slots = data.BuildStarterSlots(gunId);
            slots[0].Chain.Clear();
            slots[0].Chain.AddRange(ParseChain(data, chainDesc));
            var resolved = Loadout.ResolveGun(gun, slots);
            var cast = Loadout.ResolveCast(resolved, slots[0]);
            return cast.ExpectedDamage;
        }

        /// <summary>模拟一颗弹丸是否命中正前方 distance 处的球。</summary>
        static bool Hits(ProjectileDesc d, GunDef gun, float distance, Random rng)
        {
            float spreadRad = d.Spread * 0.0174533f;

            // 圆盘内均匀取偏移方向（比高斯更接近游戏常见随机）
            double r = Math.Sqrt(rng.NextDouble()) * spreadRad * 0.5;
            double th = rng.NextDouble() * Math.PI * 2.0;
            float dx = (float)(Math.Cos(th) * r);
            float dy = (float)(Math.Sin(th) * r);
            float dz = 1f;

            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= len; dy /= len; dz /= len;

            float speed = gun.MuzzleSpeed * d.SpeedMul;
            float gravity = gun.Mode == ProjectileMode.Ballistic ? gun.Gravity : 0f;
            if (speed <= 0.01f) return false;

            // 步长必须按**距离**自适应，不能按「生命周期均匀分 N 步」。
            // 反例：单发枪 140m/s × 1.2s = 168m 射程，均匀 48 步 = 每步 3.5m，
            // 而目标球直径只有 1m，会整步跨过去 → 命中率算成 0（踩过）。
            const float StepSize = 0.05f;   // 5cm 步长，远小于目标半径
            float totalLen = Math.Min(d.Lifetime * speed, distance + TargetRadius * 4f);
            int steps = Math.Max(8, (int)(totalLen / StepSize));

            float best = float.MaxValue;
            for (int i = 0; i <= steps; i++)
            {
                float t = d.Lifetime * i / steps;
                float px = dx * speed * t;
                float py = dy * speed * t;
                float pz = dz * speed * t - 0.5f * gravity * t * t;

                if (pz > distance + TargetRadius * 2f) break;

                float ex = px, ey = py, ez = pz - distance;
                float dist2 = ex * ex + ey * ey + ez * ez;
                if (dist2 < best) best = dist2;
                if (best <= TargetRadius * TargetRadius) return true;
            }
            return best <= TargetRadius * TargetRadius;
        }
    }
}

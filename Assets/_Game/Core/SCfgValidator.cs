using System;
using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 配置表的跨表校验。**零 Unity 依赖**，dotnet 断言与运行时共用。
    ///
    /// 为什么必须有一层校验（对齐 Haunted Match 3 的 GameCfgValidator）：
    /// 这类错误全部是**静默**的 —— 表能读、游戏能跑，只是行为不对：
    ///   - `baseBullet` 拼错 → 开局枪里没子弹，按左键毫无反应，不报错
    ///   - `pool` 里敌人名拼错 → 静默退化成第一个敌人，看数据以为生效了
    ///   - `phases[0].at != 0` → 开局到第一条之间没有刷怪规格，场上空着不动
    ///   - 枪械件带了投射物字段 → 写了不生效，后来者以为数据错
    /// 这些靠肉眼查几乎不可能，只能靠脚本。
    ///
    /// 严重度约定：Error 让加载失败；Warn 只上报。
    /// 区分标准是「错了会不会让人以为数据生效了」—— 会的话就是 Error。
    /// </summary>
    public static class SCfgValidator
    {
        public enum Severity { Warn = 0, Error = 1 }

        public sealed class Issue
        {
            public Severity Severity;
            public string Where;
            public string Message;

            public override string ToString() =>
                (Severity == Severity.Error ? "ERROR " : "WARN  ") +
                (string.IsNullOrEmpty(Where) ? "" : Where + " ") + Message;
        }

        /// <summary>
        /// 校验并**在有任何 Error 时抛异常**，返回 Warn 清单供调用方打印。
        ///
        /// 为什么不直接返回全集让调用方自己判断：运行时只有一个正确做法
        /// （Error 就不能启动），把判断权交出去只会有一个调用点忘记判断。
        /// </summary>
        public static List<Issue> Validate(SData d, string sourceKey)
        {
            var issues = Collect(d);

            var errors = new List<Issue>();
            var warns = new List<Issue>();
            for (int i = 0; i < issues.Count; i++)
                (issues[i].Severity == Severity.Error ? errors : warns).Add(issues[i]);

            if (errors.Count == 0) return warns;

            var sb = new System.Text.StringBuilder();
            sb.Append("配置校验失败（来源 ").Append(sourceKey).Append("），共 ")
              .Append(errors.Count).Append(" 项：");
            for (int i = 0; i < errors.Count; i++) sb.Append("\n  ").Append(errors[i]);
            throw new FormatException(sb.ToString());
        }

        /// <summary>只收集不抛异常。dotnet 断言用它把问题逐条打印出来。</summary>
        public static List<Issue> Collect(SData d)
        {
            var list = new List<Issue>();
            if (d == null)
            {
                Add(list, Severity.Error, "", "SData 为空。");
                return list;
            }

            CheckGuns(d, list);
            CheckBullets(d, list);
            CheckModifiers(d, list);
            CheckEnemies(d, list);
            CheckTimeline(d, list);
            CheckBoss(d, list);
            CheckPlayer(d, list);
            CheckArena(d, list);
            CheckRarity(d, list);

            return list;
        }

        // ---------------- 枪 ----------------

        static void CheckGuns(SData d, List<Issue> list)
        {
            if (d.GunOrder.Count == 0)
            {
                Add(list, Severity.Error, "Gun.tsv", "一把枪都没有。");
                return;
            }

            int starters = 0;
            for (int i = 0; i < d.GunOrder.Count; i++)
            {
                var g = d.GunOrder[i];
                string at = "Gun.tsv[" + g.Id + "]";

                if (g.SlotCount < 1)
                    Add(list, Severity.Error, at, "slotCount = " + g.SlotCount + "，至少要 1 格。");

                if (g.MagSize < 1)
                    Add(list, Severity.Error, at, "magSize = " + g.MagSize + "，至少要 1。");

                if (g.FireInterval <= 0f)
                    Add(list, Severity.Error, at, "fireInterval = " + g.FireInterval + "，必须 > 0（否则无法开火）。");

                // baseBullet 拼错是「开局按左键毫无反应」，最难自查的一类
                if (string.IsNullOrEmpty(g.BaseBullet))
                    Add(list, Severity.Error, at, "baseBullet 为空，开局枪里没有子弹。");
                else if (!d.Bullets.ContainsKey(g.BaseBullet))
                    Add(list, Severity.Error, at,
                        "baseBullet '" + g.BaseBullet + "' 在 Bullet.tsv 里不存在。");

                if (string.IsNullOrEmpty(g.Model))
                    Add(list, Severity.Warn, at, "model 为空，枪将回退到 Auto 模型。");

                if (g.Starter) starters++;
            }

            if (starters == 0)
                Add(list, Severity.Error, "Gun.tsv", "没有任何 starter=1 的枪，玩家开局没有武器。");
        }

        // ---------------- 子弹 ----------------

        static void CheckBullets(SData d, List<Issue> list)
        {
            if (d.BulletOrder.Count == 0)
            {
                Add(list, Severity.Error, "Bullet.tsv", "一个子弹配件都没有，配枪系统无法运作。");
                return;
            }

            for (int i = 0; i < d.BulletOrder.Count; i++)
            {
                var b = d.BulletOrder[i];
                string at = "Bullet.tsv[" + b.Id + "]";

                if (b.Cost < 0)
                    Add(list, Severity.Error, at, "cost = " + b.Cost + " 不能为负。");

                if (b.Count < 1)
                    Add(list, Severity.Error, at, "count = " + b.Count + "，至少 1。");

                if (b.Trigger != TriggerKind.None && b.Damage <= 0f)
                    Add(list, Severity.Warn, at, "是触发弹但 damage = 0，确认这是有意为之。");

                if (b.Kind == BulletKind.Proj && b.Speed <= 0f)
                    Add(list, Severity.Warn, at, "Proj 形态但 speed = 0，会原地不动。");

                if (b.Kind == BulletKind.Chain && b.ChainCount < 1)
                    Add(list, Severity.Error, at, "Chain 形态但 chainCount = 0，连锁不会发生。");

                if (b.Kind == BulletKind.Beam && b.Range <= 0f)
                    Add(list, Severity.Error, at, "Beam 形态但 range = 0，打不到任何东西。");

                if ((b.Kind == BulletKind.Field || b.Kind == BulletKind.Mine) && b.Duration <= 0f)
                    Add(list, Severity.Error, at, "Field/Mine 形态但 duration = 0，落地即消失。");

                if (b.ExplodeR > 0f && b.ExplodeDmg <= 0f)
                    Add(list, Severity.Warn, at, "有 explodeR 但 explodeDmg = 0，爆炸不造成伤害。");

                if (b.Crit < 0f || b.Crit > 1f)
                    Add(list, Severity.Warn, at, "crit = " + b.Crit + " 超出 0..1，会被钳制。");

                if (string.IsNullOrEmpty(b.Color))
                    Add(list, Severity.Warn, at, "color 为空，弹丸会是白色。");
            }
        }

        // ---------------- 衍生件 ----------------

        static void CheckModifiers(SData d, List<Issue> list)
        {
            // 跨两张表查重：Modifier 与 GunModifier 各自 AddUnique 查不到对方
            var seen = new Dictionary<string, string>();

            for (int i = 0; i < d.ModifierOrder.Count; i++)
            {
                var m = d.ModifierOrder[i];
                string at = "Modifier.tsv[" + m.Id + "]";

                if (!seen.ContainsKey(m.Id))
                {
                    seen[m.Id] = m.Id;
                }
                else
                {
                    Add(list, Severity.Error, at,
                        "id '" + m.Id + "' 在 Modifier.tsv 与 GunModifier.tsv 里都出现了，" +
                        "MakeItem 只会取到其中一个。");
                }

                if (!HasAnyEffect(m))
                    Add(list, Severity.Error, at, "所有效果字段都是默认值，这件配件没有任何作用。");

                if (m.Cost < 0 && m.AmmoReduce <= 0)
                    Add(list, Severity.Error, at,
                        "cost = " + m.Cost + " 为负但没有 ammoReduce，会让耗弹变成负数（等于白送弹药）。");

                // 枪械件不该带投射物字段：写了不生效，属数据错误
                if (m.IsGunOnly && HasProjectileEffect(m))
                    Add(list, Severity.Error, at,
                        "枪械件（只改枪）却带了投射物字段（伤害/穿透/爆裂等），这些字段不会生效。");

                if (!m.IsGunOnly && HasGunEffect(m))
                    Add(list, Severity.Error, at,
                        "带枪械字段（gunMagMul 等）却不是枪械件；加载器会强制改成 All 作用域，效果可能不是本意。");

                if (m.Multicast > 0 && m.Scope != Scope.Next)
                    Add(list, Severity.Error, at,
                        "multicast > 0 但 scope 不是 Next（加载器已强制改成 Next，请直接写对）。");
            }
        }

        static bool HasAnyEffect(ModifierDef m) =>
            HasProjectileEffect(m) || HasGunEffect(m) || m.Multicast != 0;

        static bool HasProjectileEffect(ModifierDef m) =>
            m.DamageMulAdd != 0f || m.SizeMulAdd != 0f || m.SpeedMulAdd != 0f ||
            m.RangeMulAdd != 0f || m.LifeMulAdd != 0f || m.KnockMulAdd != 0f ||
            m.CountMulAdd != 0f || m.RadiusMulAdd != 0f ||
            m.PiercePer != 0 || m.BouncePer != 0 || m.HomingPer != 0f ||
            m.BurnPer != 0f || m.SlowPer != 0f || m.CritPer != 0f || m.SpreadPer != 0f ||
            m.ExplodeRAdd != 0f || m.ExplodeRBase != 0f ||
            m.ExplodeDmgFromDmg != 0f || m.ExplodeDmgFlat != 0f ||
            m.AmmoReduce != 0;

        static bool HasGunEffect(ModifierDef m) =>
            m.GunMagMul != 1f || m.GunReloadMul != 1f || m.GunDelayMul != 1f ||
            m.GunSpreadMul != 1f || m.GunRecycleAdd != 0f || m.GunCastAdd != 0 ||
            m.GunMagMulIfCast != 1f || m.GunLeech != 0f;

        // ---------------- 敌人 ----------------

        static void CheckEnemies(SData d, List<Issue> list)
        {
            if (d.EnemyOrder.Count == 0)
            {
                Add(list, Severity.Error, "Enemy.tsv", "一个敌人都没有，时间轴会刷不出任何东西。");
                return;
            }

            for (int i = 0; i < d.EnemyOrder.Count; i++)
            {
                var e = d.EnemyOrder[i];
                string at = "Enemy.tsv[" + e.Id + "]";

                if (e.Hp <= 0f)
                    Add(list, Severity.Error, at, "hp = " + e.Hp + "，一出生就死。");

                if (e.Height <= 0f)
                    Add(list, Severity.Error, at, "height = " + e.Height + "，模型会被缩到不可见。");

                if (string.IsNullOrEmpty(e.Model))
                    Add(list, Severity.Warn, at, "model 为空，将回退到胶囊占位。");

                if (e.Xp < 0)
                    Add(list, Severity.Error, at, "xp = " + e.Xp + " 不能为负。");

                switch (e.Ai)
                {
                    case EnemyAi.Chaser:
                        if (e.Speed <= 0f)
                            Add(list, Severity.Error, at, "Chaser 但 speed = 0，永远走不到玩家身边。");
                        if (e.ContactRange <= 0f)
                            Add(list, Severity.Error, at, "Chaser 但 contactRange = 0，贴身也打不到人。");
                        break;

                    case EnemyAi.Bomber:
                        if (e.AttackRange <= 0f)
                            Add(list, Severity.Error, at, "Bomber 但 attackRange = 0，永远不会引爆。");
                        if (e.AttackCooldown <= 0f)
                            Add(list, Severity.Warn, at, "Bomber 但 attackCooldown = 0，会每帧尝试引爆。");
                        break;

                    case EnemyAi.Turret:
                        if (e.Speed > 0.01f)
                            Add(list, Severity.Warn, at, "Turret 通常是站桩的，但 speed = " + e.Speed + "。");
                        if (e.AttackRange <= 0f)
                            Add(list, Severity.Error, at, "Turret 但 attackRange = 0，永远不会开火。");
                        if (e.AttackCooldown <= 0f)
                            Add(list, Severity.Error, at, "Turret 但 attackCooldown = 0，会每帧开火。");
                        break;
                }

                if (e.HasShield && e.ShieldMul >= 1f && e.ShieldPierceMul >= 1f)
                    Add(list, Severity.Warn, at, "有盾面但两种减伤都 >= 1，盾没有实际效果。");
            }
        }

        // ---------------- 时间轴 ----------------

        static void CheckTimeline(SData d, List<Issue> list)
        {
            var dir = d.Director;
            if (dir == null || dir.Phases.Count == 0)
            {
                Add(list, Severity.Error, "Timeline.tsv", "没有任何普通阶段，游戏开局不会刷怪。");
                return;
            }

            var phases = dir.Phases;

            if (phases[0].At > 0.001f)
                Add(list, Severity.Error, "Timeline.tsv",
                    "第一个阶段的 at = " + phases[0].At + " 必须为 0，" +
                    "否则开局到该时刻之间没有刷怪规格，场上会空着不动。");

            float prev = -1f;
            for (int i = 0; i < phases.Count; i++)
            {
                var ph = phases[i];
                string at = "Timeline.tsv[" + ph.Name + "]";

                if (ph.At < prev)
                    Add(list, Severity.Error, at,
                        "at = " + ph.At + " 小于上一阶段的 " + prev + "，阶段表必须按时间递增。");
                prev = ph.At;

                if (ph.Concurrency < 1)
                    Add(list, Severity.Error, at, "concurrency = " + ph.Concurrency + "，至少为 1。");

                if (ph.Interval <= 0f)
                    Add(list, Severity.Error, at, "interval = " + ph.Interval + "，必须 > 0。");

                // 池子里的名字必须能查到 —— 拼错会静默退化成第一个敌人
                if (ph.Pool == null || ph.Pool.Length == 0)
                {
                    Add(list, Severity.Error, at, "pool 为空，这个阶段不会刷怪。");
                }
                else
                {
                    for (int k = 0; k < ph.Pool.Length; k++)
                    {
                        string id = ph.Pool[k];
                        if (!d.Enemies.ContainsKey(id))
                            Add(list, Severity.Error, at,
                                "pool 里的 '" + id + "' 在 Enemy.tsv 里不存在。可用：" + EnemyIds(d));
                    }
                }
            }

            if (dir.BossAt <= 0f)
                Add(list, Severity.Error, "Timeline.tsv", "Boss 入场时刻必须 > 0。");
            else if (dir.BossAt <= phases[phases.Count - 1].At)
                Add(list, Severity.Warn, "Timeline.tsv",
                    "Boss 入场（" + dir.BossAt + "s）不晚于最后一个普通阶段，" +
                    "最后一个阶段实际上见不到。");

            if (dir.ThreatFullAt <= 0f)
                Add(list, Severity.Warn, "Boss.tsv", "threatFullAt <= 0，威胁值会直接满。");
            else if (Math.Abs(dir.ThreatFullAt - dir.BossAt) > 0.5f)
                Add(list, Severity.Warn, "Boss.tsv",
                    "threatFullAt（" + dir.ThreatFullAt + "s）与 Boss 入场（" + dir.BossAt +
                    "s）不一致。设计意图是两者同时发生，玩家才会感到「威胁满 = Boss 来了」。");

            if (dir.Elite.FirstAt <= 0f)
                Add(list, Severity.Error, "Elite.tsv", "firstAt 必须 > 0。");
            if (dir.Elite.Interval <= 0f)
                Add(list, Severity.Error, "Elite.tsv", "interval 必须 > 0（否则精英总数算不出来）。");
            if (dir.Elite.FirstAt >= dir.BossAt)
                Add(list, Severity.Warn, "Elite.tsv",
                    "firstAt（" + dir.Elite.FirstAt + "s）不早于 Boss 入场，精英一只都出不来。");
        }

        static string EnemyIds(SData d)
        {
            var ids = new List<string>(d.EnemyOrder.Count);
            for (int i = 0; i < d.EnemyOrder.Count; i++) ids.Add(d.EnemyOrder[i].Id);
            return string.Join(" / ", ids);
        }

        // ---------------- Boss ----------------

        static void CheckBoss(SData d, List<Issue> list)
        {
            var b = d.Boss;

            if (b.Hp <= 0f)
                Add(list, Severity.Error, "Boss.tsv", "hp = " + b.Hp + "。");

            if (b.Pillars < 0)
                Add(list, Severity.Error, "Boss.tsv", "pillars 不能为负。");

            if (b.Pillars > 0 && b.PillarHp <= 0f)
                Add(list, Severity.Error, "Boss.tsv", "pillars > 0 但 pillarHp = 0，柱子一碰就碎。");

            if (b.ShieldPeriod > 0f && b.ShieldUpTime >= b.ShieldPeriod)
                Add(list, Severity.Error, "Boss.tsv",
                    "shieldUpTime（" + b.ShieldUpTime + "s）必须小于 shieldPeriod（" + b.ShieldPeriod +
                    "s），否则护盾永远开着，玩家打不到 Boss。");

            if (b.ShieldPeriod > 0f && b.ShieldUpTime <= 0f)
                Add(list, Severity.Warn, "Boss.tsv", "shieldUpTime = 0，护盾周期不会生效。");

            if (b.BreachTime <= 0f)
                Add(list, Severity.Warn, "Boss.tsv", "breachTime = 0，打碎柱子没有破防窗口。");

            if (b.SummonCount < 0)
                Add(list, Severity.Error, "Boss.tsv", "summonCount 不能为负。");
            if (b.SummonInterval < 0f || b.ShockwaveInterval < 0f)
                Add(list, Severity.Error, "Boss.tsv", "召唤/冲击波间隔不能为负。");

            // 召唤的敌人必须在 Enemy.tsv 里存在；拼错的表现是「Boss 召唤时什么也没出来」
            if (b.SummonCount > 0 && d.Enemies.Count > 0 && d.Enemy(b.SummonEnemy) == null)
                Add(list, Severity.Error, "Boss.tsv",
                    "summonEnemy = '" + b.SummonEnemy + "' 在 Enemy.tsv 里查不到。可选：" + EnemyIds(d));

            if (b.VolleyInterval <= 0f || b.TrackVolleyInterval <= 0f)
                Add(list, Severity.Error, "Boss.tsv", "弹幕间隔必须 > 0，否则每帧发射。");
            if (b.VolleyCount < 1)
                Add(list, Severity.Error, "Boss.tsv", "volleyCount 至少为 1。");
            if (b.SpikeInterval <= 0f)
                Add(list, Severity.Error, "Boss.tsv", "spikeInterval 必须 > 0。");
            if (b.ContactRange <= 0f)
                Add(list, Severity.Warn, "Boss.tsv", "contactRange = 0，Boss 贴身也不造成伤害。");
        }

        // ---------------- 玩家 ----------------

        static void CheckPlayer(SData d, List<Issue> list)
        {
            var p = d.Player;

            if (p.Hp <= 0f) Add(list, Severity.Error, "Player.tsv", "hp = " + p.Hp + "。");
            if (p.Walk <= 0f) Add(list, Severity.Error, "Player.tsv", "walk = " + p.Walk + "。");
            if (p.Gravity <= 0f) Add(list, Severity.Error, "Player.tsv", "gravity = " + p.Gravity + "。");
            if (p.GunSlots < 1) Add(list, Severity.Error, "Player.tsv", "gunSlots 至少为 1。");
            if (p.BagSize < 1) Add(list, Severity.Error, "Player.tsv", "bagSize 至少为 1。");

            if (p.SprintSpeed > 0f && p.SprintSpeed <= p.Walk)
                Add(list, Severity.Warn, "Player.tsv",
                    "sprintSpeed（" + p.SprintSpeed + "）不高于 walk（" + p.Walk + "），冲刺没有意义。");

            if (p.IFrames < 0f) Add(list, Severity.Error, "Player.tsv", "iFrames 不能为负。");

            int starters = 0;
            for (int i = 0; i < d.GunOrder.Count; i++) if (d.GunOrder[i].Starter) starters++;
            if (starters > p.GunSlots)
                Add(list, Severity.Error, "Player.tsv",
                    "gunSlots = " + p.GunSlots + " 但起手枪有 " + starters + " 把，玩家拿不全。");

            if (p.XpSquare < 0 || p.XpLinear < 0)
                Add(list, Severity.Warn, "Player.tsv",
                    "经验公式有负系数（" + p.XpSquare + "L² + " + p.XpLinear + "L + " + p.XpFlat +
                    "），升级所需经验可能不单调。");
        }

        // ---------------- 场地 ----------------

        static void CheckArena(SData d, List<Issue> list)
        {
            var a = d.Arena;

            if (a.Width < 10f || a.Depth < 10f)
                Add(list, Severity.Error, "Arena.tsv",
                    "场地 " + a.Width + "×" + a.Depth + " 太小，刷怪点与掩体放不下。");

            if (a.WallHeight <= 0f)
                Add(list, Severity.Error, "Arena.tsv", "wallHeight 必须 > 0（否则子弹会飞出场地）。");

            if (a.CoverCount < 0) Add(list, Severity.Error, "Arena.tsv", "coverCount 不能为负。");
            if (a.HighGroundCount < 0) Add(list, Severity.Error, "Arena.tsv", "highGroundCount 不能为负。");

            if (a.SpawnPoints < 4)
                Add(list, Severity.Error, "Arena.tsv",
                    "spawnPoints = " + a.SpawnPoints + "，环形刷怪点至少要 4 个，否则会刷在玩家脸上。");

            if (Math.Abs(a.PlayerSpawnZ) >= a.Depth * 0.5f)
                Add(list, Severity.Error, "Arena.tsv",
                    "playerSpawnZ = " + a.PlayerSpawnZ + " 已在场地外（半深 " + (a.Depth * 0.5f) + "）。");

            if (a.BossPadRadius >= Math.Min(a.Width, a.Depth) * 0.5f)
                Add(list, Severity.Error, "Arena.tsv",
                    "bossPadRadius（" + a.BossPadRadius + "）不小于场地半边长，Boss 台会被墙切掉。");

            float usable = Math.Min(a.Width, a.Depth) - 18f;   // 两侧各留 9m 边距
            if (a.CoverCount > 0 && usable <= 0f)
                Add(list, Severity.Error, "Arena.tsv",
                    "场地太小，掩体留边后没有可用空间（可用 " + usable + "m）。");
        }

        // ---------------- 稀有度 ----------------

        static void CheckRarity(SData d, List<Issue> list)
        {
            int total = 0;
            foreach (var kv in d.RarityWeights) total += kv.Value;

            if (d.RarityWeights.Count == 0)
            {
                Add(list, Severity.Error, "RarityWeight.tsv", "没有任何权重，三选一抽不出东西。");
                return;
            }

            if (total <= 0)
            {
                Add(list, Severity.Error, "RarityWeight.tsv", "权重合计为 " + total + "，永远抽不到。");
                return;
            }

            if (!d.RarityWeights.ContainsKey(Rarity.Common))
                Add(list, Severity.Warn, "RarityWeight.tsv", "没有 Common 权重，普通件将不会出现。");

            // 每档都要有至少一件可抽的配件，否则该档权重是死权重
            foreach (var kv in d.RarityWeights)
            {
                if (kv.Value <= 0) continue;
                bool any = false;
                for (int i = 0; i < d.BulletOrder.Count && !any; i++)
                    if (d.BulletOrder[i].Rarity == kv.Key) any = true;
                for (int i = 0; i < d.ModifierOrder.Count && !any; i++)
                    if (d.ModifierOrder[i].Rarity == kv.Key) any = true;

                if (!any)
                    Add(list, Severity.Warn, "RarityWeight.tsv",
                        "稀有度 " + kv.Key + " 有权重（" + kv.Value + "）但没有任何该档配件，是死权重。");
            }
        }

        static void Add(List<Issue> list, Severity sev, string where, string msg)
        {
            list.Add(new Issue { Severity = sev, Where = where, Message = msg });
        }
    }
}

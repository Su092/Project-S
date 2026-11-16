using System;
using System.Collections.Generic;
using System.Globalization;

namespace S.Core
{
    /// <summary>
    /// 从 TSV 装配 <see cref="SData"/>。**零 Unity 依赖**，dotnet 断言与运行时共用同一份逻辑。
    ///
    /// 纪律（与 Haunted Match 3 的 GameCfgLoader 对齐）：
    ///   1. 每张表都单独 `LoadXxx`，互不依赖表序 —— 除了 Timeline/Boss 里的 bossAt 快照；
    ///   2. 缺表、缺列、重复 id 一律**抛异常**，不静默跳过；
    ///      数值列留空走 fallback，因为「这列我不关心」是合法意图；
    ///   3. 装配完调 <see cref="SCfgValidator"/>，把跨表引用与规则矛盾一次性挑出来。
    ///
    /// 为什么第 2 条要区分：如果所有缺失都抛异常，策划被迫在每个空格填默认值，
    /// 反而让真正的笔误（如 `hp` 填了 `l00`）淹没在一堆占位值里。
    /// 反过来，重复 id 与缺表必须硬失败 —— 那类错会静默选错数据。
    /// </summary>
    public static class SCfgLoader
    {
        public static SData Load(ISCfgSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var d = new SData();

            LoadPlayer(Read(source, SCfgAddresses.Player), d);
            LoadGuns(Read(source, SCfgAddresses.Gun), d);
            LoadBullets(Read(source, SCfgAddresses.Bullet), d);
            LoadModifiers(Read(source, SCfgAddresses.Modifier), d);
            LoadModifiers(Read(source, SCfgAddresses.GunModifier), d);
            LoadRarityWeights(Read(source, SCfgAddresses.RarityWeight), d);
            LoadEnemies(Read(source, SCfgAddresses.Enemy), d);
            LoadArena(Read(source, SCfgAddresses.Arena), d);
            LoadElite(Read(source, SCfgAddresses.Elite), d);
            LoadBoss(Read(source, SCfgAddresses.Boss), d);
            LoadTimeline(Read(source, SCfgAddresses.Timeline), d);

            SCfgValidator.Validate(d, source.RootKey);
            return d;
        }

        static STsvTable Read(ISCfgSource source, string table)
        {
            if (!source.TryGetText(table, out string text))
                throw new InvalidOperationException(
                    "缺少配置表 " + SCfgAddresses.AssetPath(table) +
                    "（来源：" + source.RootKey + "）。全部必需表：" +
                    string.Join(" / ", SCfgAddresses.Required));

            return STsv.Parse(table, text);
        }

        /// <summary>
        /// 单行表（Player / Arena / Elite / Boss）取唯一那一行。
        /// 多行或多行表出现 0 行都报错 —— 「取第一行」会让人以为改的第二行生效了。
        /// </summary>
        static STsvRow Single(STsvTable t)
        {
            if (t.Rows.Count == 1) return t.Rows[0];
            throw new FormatException(
                t.Name + ".tsv 必须只有 1 行数据，实际 " + t.Rows.Count + " 行。");
        }

        // ---------------- Player ----------------

        static void LoadPlayer(STsvTable t, SData d)
        {
            var r = Single(t);
            var p = d.Player;
            p.Hp = r.F("hp", p.Hp);
            p.Walk = r.F("walk", p.Walk);
            p.SprintSpeed = r.F("sprintSpeed", p.SprintSpeed);
            p.SprintTime = r.F("sprintTime", p.SprintTime);
            p.SprintCooldown = r.F("sprintCooldown", p.SprintCooldown);
            p.JumpHeight = r.F("jumpHeight", p.JumpHeight);
            p.Gravity = r.F("gravity", p.Gravity);
            p.CapsuleRadius = r.F("capsuleRadius", p.CapsuleRadius);
            p.CapsuleHeight = r.F("capsuleHeight", p.CapsuleHeight);
            p.IFrames = r.F("iFrames", p.IFrames);
            p.GunSlots = r.I("gunSlots", p.GunSlots);
            p.BagSize = r.I("bagSize", p.BagSize);
            p.HealPickup = r.F("healPickup", p.HealPickup);
            p.XpSquare = r.I("xpSquare", p.XpSquare);
            p.XpLinear = r.I("xpLinear", p.XpLinear);
            p.XpFlat = r.I("xpFlat", p.XpFlat);
            p.HpScalePerThreat = r.F("hpScalePerThreat", p.HpScalePerThreat);
            p.DmgScalePerThreat = r.F("dmgScalePerThreat", p.DmgScalePerThreat);
            p.HuntAtThreat = r.F("huntAtThreat", p.HuntAtThreat);
            p.SalvageCommon = r.I("salvageCommon", p.SalvageCommon);
            p.SalvageRare = r.I("salvageRare", p.SalvageRare);
            p.SalvageEpic = r.I("salvageEpic", p.SalvageEpic);
        }

        // ---------------- Gun ----------------

        static void LoadGuns(STsvTable t, SData d)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id))
                    throw new FormatException(r.Where + " 的 id 为空。");

                var g = new GunDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Desc = r.Str("desc"),
                    SlotCount = r.I("slotCount", 8),
                    MagSize = r.I("magSize", 30),
                    ReloadTime = r.F("reloadTime", 1.5f),
                    FireInterval = r.F("fireInterval", 0.14f),
                    SpreadBase = r.F("spreadBase", 1.2f),
                    CastCount = r.I("castCount", 1),
                    BaseBullet = r.Str("baseBullet"),
                    Color = r.Str("color", "#8f9bb3"),
                    ZoomDistance = r.F("zoom", 55f),
                    Model = r.Str("model", "Modle_Gun_Auto"),
                    Starter = r.B("starter"),
                };

                AddUnique(d.Guns, g.Id, g, r);
                d.GunOrder.Add(g);
            }
        }

        // ---------------- Bullet ----------------

        static void LoadBullets(STsvTable t, SData d)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id))
                    throw new FormatException(r.Where + " 的 id 为空。");

                var b = new BulletDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Icon = r.Str("icon"),
                    Desc = r.Str("desc"),
                    Rarity = ParseEnum(r.Str("rarity"), Rarity.Common),
                    Cost = r.I("cost", 1),
                    Delay = r.F("delay"),

                    Kind = ParseEnum(r.Str("kind"), BulletKind.Proj),
                    Damage = r.F("damage", 10f),
                    Count = r.I("count", 1),
                    Spread = r.F("spread"),
                    Speed = r.F("speed", 80f),
                    Lifetime = r.F("lifetime", 1.5f),
                    Size = r.F("size", 0.12f),
                    Pierce = r.I("pierce"),
                    Bounce = r.I("bounce"),
                    Homing = r.F("homing"),
                    Gravity = r.F("gravity"),
                    ExplodeR = r.F("explodeR"),
                    ExplodeDmg = r.F("explodeDmg"),
                    Burn = r.F("burn"),
                    Slow = r.F("slow"),
                    Crit = r.F("crit", 0.05f),
                    CritMul = r.F("critMul", 2f),
                    Knock = r.F("knock", 1f),
                    ChainCount = r.I("chainCount"),
                    Range = r.F("range", 60f),
                    Radius = r.F("radius"),
                    Duration = r.F("duration"),
                    Trigger = ParseEnum(r.Str("trigger"), TriggerKind.None),
                    Color = r.Str("color", "#ffd25a"),
                };

                AddUnique(d.Bullets, b.Id, b, r);
                d.BulletOrder.Add(b);
            }
        }

        // ---------------- Modifier（Modifier.tsv 与 GunModifier.tsv 同一结构）----------------

        static void LoadModifiers(STsvTable t, SData d)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id))
                    throw new FormatException(r.Where + " 的 id 为空。");

                var m = new ModifierDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Icon = r.Str("icon"),
                    Rarity = ParseEnum(r.Str("rarity"), Rarity.Common),
                    Scope = ParseEnum(r.Str("scope"), Scope.Next),
                    Cost = r.I("cost", 1),
                    Delay = r.F("delay"),
                    Desc = r.Str("desc"),

                    DamageMulAdd = r.F("damageMulAdd"),
                    SizeMulAdd = r.F("sizeMulAdd"),
                    SpeedMulAdd = r.F("speedMulAdd"),
                    RangeMulAdd = r.F("rangeMulAdd"),
                    LifeMulAdd = r.F("lifeMulAdd"),
                    KnockMulAdd = r.F("knockMulAdd"),
                    CountMulAdd = r.F("countMulAdd"),
                    RadiusMulAdd = r.F("radiusMulAdd"),

                    PiercePer = r.I("piercePer"),
                    BouncePer = r.I("bouncePer"),
                    HomingPer = r.F("homingPer"),
                    BurnPer = r.F("burnPer"),
                    SlowPer = r.F("slowPer"),
                    CritPer = r.F("critPer"),
                    SpreadPer = r.F("spreadPer"),

                    ExplodeRAdd = r.F("explodeRAdd"),
                    ExplodeRBase = r.F("explodeRBase"),
                    ExplodeDmgFromDmg = r.F("explodeDmgFromDmg"),
                    ExplodeDmgFlat = r.F("explodeDmgFlat"),

                    AmmoReduce = r.I("ammoReduce"),

                    GunMagMul = r.F("gunMagMul", 1f),
                    GunReloadMul = r.F("gunReloadMul", 1f),
                    GunDelayMul = r.F("gunDelayMul", 1f),
                    GunSpreadMul = r.F("gunSpreadMul", 1f),
                    GunRecycleAdd = r.F("gunRecycleAdd"),
                    GunCastAdd = r.I("gunCastAdd"),
                    GunMagMulIfCast = r.F("gunMagMulIfCast", 1f),
                    GunLeech = r.F("gunLeech"),

                    Multicast = r.I("multicast"),
                };

                // 两条强制纠正（数据写错不报错，但语义必须唯一，否则结算会出现「写了没效果」）
                //   1. 多重施放是「下一发」语义 → 必为 Next
                //   2. 只改枪的件（IsGunOnly）→ 必为 All
                if (m.Multicast > 0 && m.Scope != Scope.Next) m.Scope = Scope.Next;
                if (m.IsGunOnly) m.Scope = Scope.All;

                AddUnique(d.Modifiers, m.Id, m, r);
                d.ModifierOrder.Add(m);
            }
        }

        // ---------------- RarityWeight ----------------

        static void LoadRarityWeights(STsvTable t, SData d)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                string key = r.Str("rarity");
                if (string.IsNullOrEmpty(key)) throw new FormatException(r.Where + " 的 rarity 为空。");

                var rar = ParseEnum(key, Rarity.Common);
                d.RarityWeights[rar] = r.I("weight", 10);
            }
        }

        // ---------------- Enemy ----------------

        static void LoadEnemies(STsvTable t, SData d)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id))
                    throw new FormatException(r.Where + " 的 id 为空。");

                var e = new EnemyDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Ai = ParseEnum(r.Str("ai"), EnemyAi.Chaser),
                    Model = r.Str("model"),
                    Height = r.F("height", 1f),
                    Hp = r.F("hp", 60f),
                    Damage = r.F("dmg", 10f),
                    Speed = r.F("speed", 2f),
                    Xp = r.I("xp", 8),
                    ContactRange = r.F("contactRange", 1.4f),
                    AttackRange = r.F("attackRange"),
                    AttackCooldown = r.F("attackCooldown"),
                    ShieldFacing = r.F("shieldFacing"),
                    ShieldMul = r.F("shieldMul", 1f),
                    ShieldPierceMul = r.F("shieldPierceMul", 1f),
                };

                AddUnique(d.Enemies, e.Id, e, r);
                d.EnemyOrder.Add(e);
            }
        }

        // ---------------- Arena ----------------

        static void LoadArena(STsvTable t, SData d)
        {
            var r = Single(t);
            var a = d.Arena;
            a.Width = r.F("width", a.Width);
            a.Depth = r.F("depth", a.Depth);
            a.WallHeight = r.F("wallHeight", a.WallHeight);
            a.BossPadRadius = r.F("bossPadRadius", a.BossPadRadius);
            a.CoverCount = r.I("coverCount", a.CoverCount);
            a.HighGroundCount = r.I("highGroundCount", a.HighGroundCount);
            a.SpawnPoints = r.I("spawnPoints", a.SpawnPoints);
            a.PlayerSpawnZ = r.F("playerSpawnZ", a.PlayerSpawnZ);
        }

        // ---------------- Elite ----------------

        static void LoadElite(STsvTable t, SData d)
        {
            var r = Single(t);
            var e = d.Director.Elite;
            e.FirstAt = r.F("firstAt", e.FirstAt);
            e.Interval = r.F("interval", e.Interval);
            e.HpMul = r.F("hpMul", e.HpMul);
            e.DmgMul = r.F("dmgMul", e.DmgMul);
            e.SpeedMul = r.F("speedMul", e.SpeedMul);
            e.XpMul = r.I("xpMul", e.XpMul);
            e.Scale = r.F("scale", e.Scale);
            e.HealOnKill = r.F("healOnKill", e.HealOnKill);
        }

        // ---------------- Boss ----------------
        // 注意：BossState 的 const（PillarCount / ShieldPeriod 等）改成了实例字段，
        // 由 GameRoot 在 new BossState 时注入。这里只负责把表里的值搬进 BossDef。
        static void LoadBoss(STsvTable t, SData d)
        {
            var r = Single(t);
            var b = d.Boss;
            b.Id = r.Str("id", b.Id);
            b.Name = r.Str("name", b.Name);
            b.Hp = r.F("hp", b.Hp);
            b.ContactDamage = r.F("contactDamage", b.ContactDamage);
            b.MoveSpeed = r.F("moveSpeed", b.MoveSpeed);
            b.Pillars = r.I("pillars", b.Pillars);
            b.PillarHp = r.F("pillarHp", b.PillarHp);
            b.BreachTime = r.F("breachTime", b.BreachTime);
            b.BreachDamageMul = r.F("breachDamageMul", b.BreachDamageMul);
            b.ShieldPeriod = r.F("shieldPeriod", b.ShieldPeriod);
            b.ShieldUpTime = r.F("shieldUpTime", b.ShieldUpTime);
            b.SummonInterval = r.F("summonInterval", b.SummonInterval);
            b.SummonCount = r.I("summonCount", b.SummonCount);
            b.SummonEnemy = r.Str("summonEnemy", b.SummonEnemy);
            b.ContactRange = r.F("contactRange", b.ContactRange);
            b.VolleyInterval = r.F("volleyInterval", b.VolleyInterval);
            b.VolleyCount = r.I("volleyCount", b.VolleyCount);
            b.VolleySpeed = r.F("volleySpeed", b.VolleySpeed);
            b.VolleyDamage = r.F("volleyDamage", b.VolleyDamage);
            b.TrackVolleyInterval = r.F("trackVolleyInterval", b.TrackVolleyInterval);
            b.TrackVolleySpeed = r.F("trackVolleySpeed", b.TrackVolleySpeed);
            b.TrackVolleyDamage = r.F("trackVolleyDamage", b.TrackVolleyDamage);
            b.SpikeInterval = r.F("spikeInterval", b.SpikeInterval);
            b.SpikeDamage = r.F("spikeDamage", b.SpikeDamage);
            b.SpikeTelegraphTime = r.F("spikeTelegraphTime", b.SpikeTelegraphTime);
            b.ShockwaveInterval = r.F("shockwaveInterval", b.ShockwaveInterval);
            b.ShockwaveDamage = r.F("shockwaveDamage", b.ShockwaveDamage);
            b.BossAt = r.F("bossAt", b.BossAt);
            b.ThreatFullAt = r.F("threatFullAt", b.ThreatFullAt);
        }

        // ---------------- Timeline ----------------

        static void LoadTimeline(STsvTable t, SData d)
        {
            var phases = new List<RunDirector.Phase>();
            RunDirector.Phase bossPhase = null;

            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                var ph = new RunDirector.Phase
                {
                    At = r.F("at"),
                    Name = r.Str("name", "阶段" + (i + 1)),
                    Pool = r.StrArr("pool").ToArray(),
                    Concurrency = r.I("concurrency", 12),
                    Interval = r.F("interval", 1.2f),
                };

                if (r.B("boss"))
                {
                    if (bossPhase != null)
                        throw new FormatException(r.Where + " 出现了第二条 boss=1 的行。");
                    bossPhase = ph;
                    // Boss 入场时刻以这一行为准；Boss.tsv 的 bossAt 只是冗余快照
                    d.Boss.BossAt = ph.At;
                }
                else
                {
                    phases.Add(ph);
                }
            }

            // 注意：RunDirector 的构造会新建一份 EliteDef（默认值），
            // 所以必须把 LoadElite 已经装好的那份接回去 —— 否则 Elite.tsv 整张表被静默丢弃，
            // 表现是「精英仍在第 4:00 出现」，看起来像表没生效。
            var elite = d.Director != null ? d.Director.Elite : null;

            d.Director = new RunDirector(phases, bossPhase);
            if (elite != null) d.Director.Elite = elite;
            d.Director.BossAt = d.Boss.BossAt;
            d.Director.ThreatFullAt = d.Boss.ThreatFullAt > 0f ? d.Boss.ThreatFullAt : d.Boss.BossAt;
        }

        // ---------------- 工具 ----------------

        static void AddUnique<T>(Dictionary<string, T> map, string id, T value, STsvRow r)
        {
            if (map.ContainsKey(id))
                throw new FormatException(
                    r.Where + " 的 id '" + id + "' 在 " + r.Source + ".tsv 里重复。");
            map.Add(id, value);
        }

        /// <summary>枚举按名字解析（大小写不敏感）。空串走 fallback，拼错也走 fallback —— 由校验器单独挑拼错。</summary>
        static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                if (Enum.IsDefined(typeof(T), n)) return (T)(object)n;
            }
            return Enum.TryParse(s, true, out T v) ? v : fallback;
        }
    }
}

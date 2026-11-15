using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 单张竞技场的地形参数（docs/gameplay-spec.md §2）。
    /// 放 Core 而不是 View：地形尺寸决定刷怪点分布与掩体密度，属于规则而非表现。
    /// </summary>
    public sealed class ArenaDef
    {
        public float Width = 100f;
        public float Depth = 100f;
        public float WallHeight = 8f;
        public float BossPadRadius = 9f;
        public int CoverCount = 18;
        public int HighGroundCount = 4;
        public int SpawnPoints = 16;
        public float PlayerSpawnZ = -38f;
    }

    /// <summary>
    /// sdata.json 的解析与纯查询。零 Unity 依赖：入口是 Parse(string)，
    /// Unity 侧用 Resources 读文本喂进来，dotnet 测试直接读文件，两条通路共用同一份代码。
    /// 字段契约见 docs/data-schema.md。
    /// </summary>
    public sealed class SData
    {
        public readonly Dictionary<string, GunDef> Guns = new Dictionary<string, GunDef>();
        public readonly List<GunDef> GunOrder = new List<GunDef>();
        public readonly Dictionary<string, BulletDef> Bullets = new Dictionary<string, BulletDef>();
        public readonly List<BulletDef> BulletOrder = new List<BulletDef>();
        public readonly Dictionary<string, ModifierDef> Modifiers = new Dictionary<string, ModifierDef>();
        public readonly List<ModifierDef> ModifierOrder = new List<ModifierDef>();
        public readonly Dictionary<Rarity, int> RarityWeights = new Dictionary<Rarity, int>();

        /// <summary>单张竞技场的地形参数（docs/gameplay-spec.md §2）。</summary>
        public ArenaDef Arena = new ArenaDef();

        /// <summary>时间轴与精英规格（docs/gameplay-spec.md §2.1 / §3）。</summary>
        public RunDirector Director = new RunDirector(null, null);

        /// <summary>Boss 入场时刻（秒）的冗余快照，供 UI 读。</summary>
        public float BossDurationHint = 600f;

        public float BossHp = 6000f;
        public float BossContactDamage = 25f;
        public float BossMoveSpeed = 2.5f;

        public float PlayerHp, PlayerWalk, PlayerGravity, PlayerJumpHeight, PlayerIFrames, PlayerSprintSpeed;
        public int PlayerGunSlots, PlayerBagSize;
        public float PlayerHealPickup;

        public static SData Parse(string json)
        {
            var root = SJson.Parse(json);
            var d = new SData();

            var p = root["player"];
            if (p != null)
            {
                d.PlayerHp = p.F("hp", 100);
                d.PlayerWalk = p.F("walk", 6.2f);
                d.PlayerGravity = p.F("gravity", 22f);
                d.PlayerJumpHeight = p.F("jumpHeight", 1.3f);
                d.PlayerIFrames = p.F("iFrames", 0.25f);
                d.PlayerSprintSpeed = p.F("sprintSpeed", 10.4f);
                d.PlayerGunSlots = p.I("gunSlots", 2);
                d.PlayerBagSize = p.I("bagSize", 44);
                d.PlayerHealPickup = p.F("healPickup", 35);
            }

            foreach (var g in root.Arr("guns"))
            {
                var gun = new GunDef
                {
                    Id = g.Str("id"),
                    Name = g.Str("name"),
                    Desc = g.Str("desc"),
                    SlotCount = g.I("slotCount", 8),
                    MagSize = g.I("magSize", 30),
                    ReloadTime = g.F("reloadTime", 1.5f),
                    FireInterval = g.F("fireInterval", 0.14f),
                    SpreadBase = g.F("spreadBase", 1.2f),
                    CastCount = g.I("castCount", 1),
                    BaseBullet = g.Str("baseBullet"),
                    Color = g.Str("color", "#8f9bb3"),
                    ZoomDistance = g.F("zoom", 55f),
                };
                d.Guns[gun.Id] = gun;
                d.GunOrder.Add(gun);
            }

            foreach (var b in root.Arr("bullets"))
            {
                var def = new BulletDef
                {
                    Id = b.Str("id"),
                    Name = b.Str("name"),
                    Icon = b.Str("icon"),
                    Rarity = ParseEnum(b.Str("rarity"), Rarity.Common),
                    Cost = b.I("cost", 1),
                    Delay = b.F("delay"),
                    Desc = b.Str("desc"),

                    Kind = ParseEnum(b.Str("kind"), BulletKind.Proj),
                    Damage = b.F("damage", 10f),
                    Count = b.I("count", 1),
                    Spread = b.F("spread"),
                    Speed = b.F("speed", 80f),
                    Lifetime = b.F("lifetime", 1.5f),
                    Size = b.F("size", 0.12f),
                    Pierce = b.I("pierce"),
                    Bounce = b.I("bounce"),
                    Homing = b.F("homing"),
                    Gravity = b.F("gravity"),
                    ExplodeR = b.F("explodeR"),
                    ExplodeDmg = b.F("explodeDmg"),
                    Burn = b.F("burn"),
                    Slow = b.F("slow"),
                    Crit = b.F("crit", 0.05f),
                    CritMul = b.F("critMul", 2f),
                    Knock = b.F("knock", 1f),
                    ChainCount = b.I("chainCount"),
                    Range = b.F("range", 60f),
                    Radius = b.F("radius"),
                    Duration = b.F("duration"),
                    Trigger = ParseEnum(b.Str("trigger"), TriggerKind.None),
                    Color = b.Str("color", "#ffd25a"),
                };
                d.Bullets[def.Id] = def;
                d.BulletOrder.Add(def);
            }

            foreach (var m in root.Arr("modifiers")) d.AddModifier(m);
            foreach (var m in root.Arr("gunModifiers")) d.AddModifier(m);

            var rw = root["rarityWeights"];
            if (rw != null)
            {
                d.RarityWeights[Rarity.Common] = rw.I("Common", 60);
                d.RarityWeights[Rarity.Rare] = rw.I("Rare", 30);
                d.RarityWeights[Rarity.Epic] = rw.I("Epic", 10);
            }

            ParseArenaAndTimeline(root, d);

            var boss = root["boss"];
            if (boss != null)
            {
                d.BossHp = boss.F("hp", 6000f);
                d.BossContactDamage = boss.F("contactDamage", 25f);
                d.BossMoveSpeed = boss.F("moveSpeed", 2.5f);
            }

            return d;
        }

        void AddModifier(SJson m)
        {
            var def = new ModifierDef
            {
                Id = m.Str("id"),
                Name = m.Str("name"),
                Icon = m.Str("icon"),
                Rarity = ParseEnum(m.Str("rarity"), Rarity.Common),
                Scope = ParseEnum(m.Str("scope"), Scope.Next),
                Cost = m.I("cost", 1),
                Delay = m.F("delay"),
                Desc = m.Str("desc"),

                DamageMulAdd = m.F("damageMulAdd"),
                SizeMulAdd = m.F("sizeMulAdd"),
                SpeedMulAdd = m.F("speedMulAdd"),
                RangeMulAdd = m.F("rangeMulAdd"),
                LifeMulAdd = m.F("lifeMulAdd"),
                KnockMulAdd = m.F("knockMulAdd"),
                CountMulAdd = m.F("countMulAdd"),
                RadiusMulAdd = m.F("radiusMulAdd"),

                PiercePer = m.I("piercePer"),
                BouncePer = m.I("bouncePer"),
                HomingPer = m.F("homingPer"),
                BurnPer = m.F("burnPer"),
                SlowPer = m.F("slowPer"),
                CritPer = m.F("critPer"),
                SpreadPer = m.F("spreadPer"),

                ExplodeRAdd = m.F("explodeRAdd"),
                ExplodeRBase = m.F("explodeRBase"),
                ExplodeDmgFromDmg = m.F("explodeDmgFromDmg"),
                ExplodeDmgFlat = m.F("explodeDmgFlat"),

                AmmoReduce = m.I("ammoReduce"),

                GunMagMul = m.F("gunMagMul", 1f),
                GunReloadMul = m.F("gunReloadMul", 1f),
                GunDelayMul = m.F("gunDelayMul", 1f),
                GunSpreadMul = m.F("gunSpreadMul", 1f),
                GunRecycleAdd = m.F("gunRecycleAdd"),
                GunCastAdd = m.I("gunCastAdd"),
                GunMagMulIfCast = m.F("gunMagMulIfCast", 1f),
                GunLeech = m.F("gunLeech"),

                Multicast = m.I("multicast"),
            };

            // 全枪作用域的件：如果带 multicast，仍按 Next 处理（双重施放是「下一发」）
            if (def.Multicast > 0 && def.Scope != Scope.Next) def.Scope = Scope.Next;

            // 枪械件一律 All
            if (def.IsGunOnly) def.Scope = Scope.All;

            Modifiers[def.Id] = def;
            ModifierOrder.Add(def);
        }

        /// <summary>
        /// 竞技场与时间轴（docs/gameplay-spec.md §2 §2.1）。
        /// </summary>
        static void ParseArenaAndTimeline(SJson root, SData d)
        {
            var arena = root["arena"];
            if (arena != null)
            {
                d.Arena.Width = arena.F("width", 100f);
                d.Arena.Depth = arena.F("depth", 100f);
                d.Arena.WallHeight = arena.F("wallHeight", 8f);
                d.Arena.BossPadRadius = arena.F("bossPadRadius", 9f);
                d.Arena.CoverCount = arena.I("coverCount", 18);
                d.Arena.HighGroundCount = arena.I("highGroundCount", 4);
                d.Arena.SpawnPoints = arena.I("spawnPoints", 16);
                d.Arena.PlayerSpawnZ = arena.F("playerSpawnZ", -38f);
            }

            var tl = root["timeline"];
            var phases = new List<RunDirector.Phase>();
            RunDirector.Phase bossPhase = null;
            if (tl != null)
            {
                foreach (var ph in tl.Arr("phases"))
                {
                    var poolList = new List<string>();
                    foreach (var pe in ph.Arr("pool"))
                        if (pe.Type == SJson.Kind.String) poolList.Add(pe.StringValue);

                    phases.Add(new RunDirector.Phase
                    {
                        At = ph.F("at"),
                        Name = ph.Str("name"),
                        Pool = poolList.ToArray(),
                        Concurrency = ph.I("concurrency", 12),
                        Interval = ph.F("interval", 1.2f),
                    });
                }

                var bp = tl["bossPhase"];
                if (bp != null)
                {
                    var poolList = new List<string>();
                    foreach (var pe in bp.Arr("pool"))
                        if (pe.Type == SJson.Kind.String) poolList.Add(pe.StringValue);

                    bossPhase = new RunDirector.Phase
                    {
                        At = bp.F("at"),
                        Name = bp.Str("name", "BOSS"),
                        Pool = poolList.ToArray(),
                        Concurrency = bp.I("concurrency", 8),
                        Interval = bp.F("interval", 2.0f),
                    };
                }

                d.BossDurationHint = tl.F("bossAt", 600f);
            }

            d.Director = new RunDirector(phases, bossPhase);
            if (tl != null)
            {
                d.Director.BossAt = tl.F("bossAt", 600f);
                d.Director.ThreatFullAt = tl.F("threatFullAt", d.Director.BossAt);
            }

            var elite = root["elite"];
            if (elite != null)
            {
                var e = d.Director.Elite;
                e.HpMul = elite.F("hpMul", e.HpMul);
                e.DmgMul = elite.F("dmgMul", e.DmgMul);
                e.SpeedMul = elite.F("speedMul", e.SpeedMul);
                e.XpMul = elite.I("xpMul", e.XpMul);
                e.Scale = elite.F("scale", e.Scale);
                e.HealOnKill = elite.F("healOnKill", e.HealOnKill);
                e.FirstAt = elite.F("firstAt", e.FirstAt);
                e.Interval = elite.F("interval", e.Interval);
            }
        }

        static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return System.Enum.TryParse<T>(s, true, out var v) ? v : fallback;
        }

        // ---- 便捷构造 ----

        /// <summary>按 id 造一件配件（子弹优先，其次衍生）。</summary>
        public ChainItem MakeItem(string id)
        {
            if (Bullets.TryGetValue(id, out var b)) return new ChainItem { Bullet = b };
            if (Modifiers.TryGetValue(id, out var m)) return new ChainItem { Mod = m };
            return null;
        }

        /// <summary>按枪的 baseBullet 建初始槽位：只有第 1 格有子弹，其余为空。</summary>
        public SlotState BuildStarterSlots(string gunId)
        {
            if (!Guns.TryGetValue(gunId, out var gun)) return new SlotState(1);

            var slots = new SlotState(gun.SlotCount);
            var baseItem = MakeItem(gun.BaseBullet);
            if (baseItem != null) slots.Cells[0] = baseItem;
            return slots;
        }

        /// <summary>可抽取的全部配件 id（子弹 + 衍生，不含起手件）。</summary>
        public List<string> RollPool()
        {
            var list = new List<string>();
            for (int i = 0; i < BulletOrder.Count; i++) list.Add(BulletOrder[i].Id);
            for (int i = 0; i < ModifierOrder.Count; i++) list.Add(ModifierOrder[i].Id);
            return list;
        }

        public Rarity RarityOf(string id)
        {
            if (Bullets.TryGetValue(id, out var b)) return b.Rarity;
            if (Modifiers.TryGetValue(id, out var m)) return m.Rarity;
            return Rarity.Common;
        }
    }
}

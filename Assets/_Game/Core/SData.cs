using System.Collections.Generic;

namespace S.Core
{
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
        public readonly Dictionary<string, ModifierDef> Modifiers = new Dictionary<string, ModifierDef>();
        public readonly Dictionary<string, List<string>> StarterChains = new Dictionary<string, List<string>>();
        public readonly Dictionary<Rarity, int> RarityWeights = new Dictionary<Rarity, int>();
        public readonly List<ZoneDef> Zones = new List<ZoneDef>();

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
                d.PlayerWalk = p.F("walk", 5.6f);
                d.PlayerGravity = p.F("gravity", 22f);
                d.PlayerJumpHeight = p.F("jumpHeight", 1.15f);
                d.PlayerIFrames = p.F("iFrames", 0.25f);
                d.PlayerSprintSpeed = p.F("sprintSpeed", 9.2f);
                d.PlayerGunSlots = p.I("gunSlots", 2);
                d.PlayerBagSize = p.I("bagSize", 24);
                d.PlayerHealPickup = p.F("healPickup", 35);
            }

            foreach (var g in root.Arr("guns"))
            {
                var gun = new GunDef
                {
                    Id = g.Str("id"),
                    Name = g.Str("name"),
                    Kind = ParseEnum(g.Str("kind"), GunKind.Auto),
                    SlotCount = g.I("slotCount", 1),
                    FireInterval = g.F("fireInterval", 0.2f),
                    MagSize = g.I("magSize", 10),
                    ReloadTime = g.F("reloadTime", 2f),
                    SpreadBase = g.F("spreadBase", 1f),
                    MuzzleSpeed = g.F("muzzleSpeed", 90f),
                    DamageMul = g.F("damageMul", 1f),
                    SplashRadius = g.F("splashRadius", 0f),
                    Mode = ParseEnum(g.Str("mode"), ProjectileMode.Straight),
                    Gravity = g.F("gravity", 0f),
                };
                d.Guns[gun.Id] = gun;
                d.GunOrder.Add(gun);

                var starter = new List<string>();
                foreach (var s in g.Arr("starterChain"))
                    if (s.Type == SJson.Kind.String) starter.Add(s.StringValue);
                d.StarterChains[gun.Id] = starter;
            }

            foreach (var b in root.Arr("bullets"))
            {
                var def = new BulletDef
                {
                    Id = b.Str("id"),
                    Name = b.Str("name"),
                    Damage = b.F("damage"),
                    Ammo = b.I("ammo", 1),
                    SpreadAdd = b.F("spreadAdd"),
                    Lifetime = b.F("lifetime", 1f),
                    Crit = b.F("crit"),
                    CritDmg = b.F("critDmg", 2f),
                    SplashRadius = b.F("splashRadius"),
                    Pierce = b.I("pierce"),
                    Bounce = b.I("bounce"),
                    SpeedMul = b.F("speedMul", 1f),
                    Rarity = ParseEnum(b.Str("rarity"), Rarity.Common),
                    BurnDps = b.F("burnDps"),
                    BurnTime = b.F("burnTime"),
                    BurnMaxStacks = b.I("burnMaxStacks"),
                    SlowPct = b.F("slowPct"),
                    SlowTime = b.F("slowTime"),
                    IgnoreArmor = b.B("ignoreArmor"),
                };
                d.Bullets[def.Id] = def;
            }

            foreach (var m in root.Arr("modifiers"))
            {
                var def = new ModifierDef
                {
                    Id = m.Str("id"),
                    Name = m.Str("name"),
                    Scope = ParseEnum(m.Str("scope"), Scope.Whole),
                    Rarity = ParseEnum(m.Str("rarity"), Rarity.Common),
                    DamageFlat = m.F("damageFlat"),
                    CritFlat = m.F("critFlat"),
                    CritDmgFlat = m.F("critDmgFlat"),
                    SpreadAddFlat = m.F("spreadAddFlat"),
                    LifetimeFlat = m.F("lifetimeFlat"),
                    PierceFlat = m.I("pierceFlat"),
                    BounceFlat = m.I("bounceFlat"),
                    AmmoFlat = m.I("ammoFlat"),
                    DamageMul = m.F("damageMul", 1f),
                    CritMul = m.F("critMul", 1f),
                    SpreadMul = m.F("spreadMul", 1f),
                    LifetimeMul = m.F("lifetimeMul", 1f),
                    SpeedMul = m.F("speedMul", 1f),
                    SplashAdd = m.F("splashAdd"),
                    HomingRate = m.F("homingRate"),
                    SplitCount = m.I("splitCount", 1),
                    SplitDamageMul = m.F("splitDamageMul", 1f),
                    SplitSpreadAdd = m.F("splitSpreadAdd"),
                    SplitLifetimeMul = m.F("splitLifetimeMul", 1f),
                    GunMagAdd = m.I("gunMagAdd"),
                    GunFireIntervalMul = m.F("gunFireIntervalMul", 1f),
                    GunSpreadMul = m.F("gunSpreadMul", 1f),
                    GunReloadMul = m.F("gunReloadMul", 1f),
                };
                d.Modifiers[def.Id] = def;
            }

            var rw = root["rarityWeights"];
            if (rw != null)
            {
                d.RarityWeights[Rarity.Common] = rw.I("Common", 58);
                d.RarityWeights[Rarity.Rare] = rw.I("Rare", 34);
                d.RarityWeights[Rarity.Epic] = rw.I("Epic", 8);
            }

            foreach (var z in root.Arr("zones"))
            {
                var poolList = new List<string>();
                foreach (var pe in z.Arr("pool"))
                    if (pe.Type == SJson.Kind.String) poolList.Add(pe.StringValue);

                d.Zones.Add(new ZoneDef
                {
                    Index = z.I("index"),
                    Name = z.Str("name"),
                    Waves = z.I("waves", 3),
                    Pool = poolList.ToArray(),
                    ThreatStart = z.F("threatStart"),
                    IsBoss = z.B("isBoss"),
                });
            }

            var boss = root["boss"];
            if (boss != null)
            {
                d.BossHp = boss.F("hp", 6000f);
                d.BossContactDamage = boss.F("contactDamage", 25f);
                d.BossMoveSpeed = boss.F("moveSpeed", 2.5f);
            }

            return d;
        }

        static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return System.Enum.TryParse<T>(s, true, out var v) ? v : fallback;
        }

        // ---- 便捷构造 ----

        /// <summary>按 id 造一条链项（子弹优先，其次衍生）。用于装配与测试。</summary>
        public ChainItem MakeItem(string id)
        {
            if (Bullets.TryGetValue(id, out var b)) return new ChainItem { Bullet = b };
            if (Modifiers.TryGetValue(id, out var m)) return new ChainItem { Mod = m };
            return null;
        }

        /// <summary>按枪的 starterChain 建初始配件位数组。</summary>
        public SlotState[] BuildStarterSlots(string gunId)
        {
            var gun = Guns[gunId];
            var slots = new SlotState[gun.SlotCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = new SlotState();

            if (StarterChains.TryGetValue(gunId, out var chain))
                foreach (var id in chain)
                {
                    var it = MakeItem(id);
                    if (it != null) slots[0].Chain.Add(it);
                }
            return slots;
        }
    }
}

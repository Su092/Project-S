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
    /// 玩家基础属性。原先散在 SData 的 8 个 `Player*` 字段里，
    /// 这里收成一个对象：加一个属性就要在 3 处（字段/解析/默认值）同步，
    /// 散着放迟早漏一处。
    /// </summary>
    public sealed class PlayerDef
    {
        public float Hp = 100f;
        public float Walk = 6.2f;
        public float SprintSpeed = 10.4f;
        public float SprintTime = 0.55f;
        public float SprintCooldown = 1.4f;
        public float JumpHeight = 1.3f;
        public float Gravity = 22f;
        public float CapsuleRadius = 0.34f;
        public float CapsuleHeight = 1.8f;
        public float IFrames = 0.25f;
        public int GunSlots = 2;
        public int BagSize = 15;
        public float HealPickup = 35f;

        /// <summary>升级所需经验公式：`A·L² + B·L + C`（默认 2/5/8，与原 RunState.NeedFor 一致）。</summary>
        public int XpSquare = 2;
        public int XpLinear = 5;
        public int XpFlat = 8;

        /// <summary>威胁每 1 点带来的敌人生命倍率增量（默认 0.008）。</summary>
        public float HpScalePerThreat = 0.008f;
        /// <summary>威胁每 1 点带来的敌人伤害倍率增量（默认 0.004）。</summary>
        public float DmgScalePerThreat = 0.004f;
        /// <summary>威胁值达到多少算「狩猎模式」（默认 88）。</summary>
        public float HuntAtThreat = 88f;

        /// <summary>分解配件换经验（普通 / 稀有 / 史诗），原先写死在 RunState.Salvage。</summary>
        public int SalvageCommon = 3;
        public int SalvageRare = 6;
        public int SalvageEpic = 9;
    }

    /// <summary>
    /// Boss 的规则参数。**原先全是 BossState 里的 const**（护盾周期、破防时长、
    /// 柱血量…），改一个数要重新编译；现在是数据。
    ///
    /// 注意 `BossState` 仍保留一份默认值：dotnet 断言会直接 `new BossState(hp)`，
    /// 那条路径不读表。两边的默认值必须一致，由 SCfgValidator 断言。
    /// </summary>
    public sealed class BossDef
    {
        public string Id = "e_boss";
        public string Name = "铸造核心";
        public float Hp = 6000f;
        public float ContactDamage = 25f;
        public float MoveSpeed = 2.5f;
        public int Pillars = 4;
        public float PillarHp = 800f;
        public float BreachTime = 4f;
        public float BreachDamageMul = 1.5f;
        public float ShieldPeriod = 18f;
        public float ShieldUpTime = 6f;
        public float SummonInterval = 12f;
        public int SummonCount = 2;
        /// <summary>召唤出来的杂兵 id（Enemy.tsv 的 id）。原先是 GameRoot 里写死的 "Crawler"。</summary>
        public string SummonEnemy = "Crawler";
        public float ShockwaveInterval = 20f;
        public float ShockwaveDamage = 22f;

        /// <summary>接触伤害的判定半径（米）。原先写死在 BossView 里的 3.2f。</summary>
        public float ContactRange = 3.2f;

        /// <summary>技能节奏（秒）：扇形弹幕 / P3 追踪弹幕 / 地刺。原先写死在 BossView.TickSkills。</summary>
        public float VolleyInterval = 3f;
        public float TrackVolleyInterval = 2.4f;
        public float SpikeInterval = 4.5f;
        public int VolleyCount = 5;
        /// <summary>弹幕速度与伤害、地刺伤害。</summary>
        public float VolleySpeed = 14f;
        public float VolleyDamage = 14f;
        public float TrackVolleySpeed = 16f;
        public float TrackVolleyDamage = 16f;
        public float SpikeDamage = 18f;
        public float SpikeTelegraphTime = 1.2f;

        /// <summary>Boss 入场时刻（秒）。时间轴行的 `boss=1` 才是定义处，这里是冗余快照供 UI 读。</summary>
        public float BossAt = 600f;
        /// <summary>威胁值达到 100 的时刻（秒）。</summary>
        public float ThreatFullAt = 600f;
    }

    /// <summary>
    /// 全部游戏数据。**唯一来源是 `Assets/Resources/Data/Cfg/*.tsv`**（由 SCfgLoader 装配）。
    ///
    /// 为什么从 sdata.json 换成 TSV（对齐 Haunted Match 3 的 Cfg 结构）：
    ///   1. 表格软件能直接打开，策划改数值不需要碰代码或引号语法；
    ///   2. 三行表头把「字段名 / 中文名 / 类型规则」分开，列序调整不影响代码；
    ///   3. 一行一条记录，新增一件配件是加一行，diff 只多一行。
    /// JSON 的问题是深层嵌套 + 手工逗号，一个括号错就整份文件失效，
    /// 而嵌套字段（子弹的 20 多个数值）在 diff 里看不出「改了哪件」。
    ///
    /// 层归属：本类只做**纯数据容器 + 查询**，零 Unity 依赖。
    /// 装配逻辑在 SCfgLoader，校验在 SCfgValidator，两边都可在 dotnet 下跑。

    /// 注意：旧的 `Resources/Data/sdata.json` 已移出运行时目录（现在只作迁移留档，
    /// 放在 `tools/legacy-sdata.json`）。任何「改 json 就生效」的旧习惯都会静默失效。
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

        /// <summary>敌人表（Cfg/Enemy.tsv）。顺序即表的行序。</summary>
        public readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public readonly List<EnemyDef> EnemyOrder = new List<EnemyDef>();

        public PlayerDef Player = new PlayerDef();

        /// <summary>单张竞技场的地形参数（docs/gameplay-spec.md §2）。</summary>
        public ArenaDef Arena = new ArenaDef();

        /// <summary>时间轴与精英规格（docs/gameplay-spec.md §2.1 / §3）。</summary>
        public RunDirector Director = new RunDirector(null, null);

        /// <summary>Boss 规则参数。</summary>
        public BossDef Boss = new BossDef();

        /// <summary>Boss 入场时刻（秒）的冗余快照，供 UI 读。</summary>
        public float BossDurationHint => Boss.BossAt;

        // ---- 兼容旧调用点：这些属性把 def 里的字段摊平出来 ----
        // 上层（View / App）原本大量引用 Data.PlayerHp 这类名字，一次性改完风险大，
        // 先把读取点保持可用；后续可以逐步换成 Data.Player.Hp。
        public float PlayerHp => Player.Hp;
        public float PlayerWalk => Player.Walk;
        public float PlayerGravity => Player.Gravity;
        public float PlayerJumpHeight => Player.JumpHeight;
        public float PlayerIFrames => Player.IFrames;
        public float PlayerSprintSpeed => Player.SprintSpeed;
        public int PlayerGunSlots => Player.GunSlots;
        public int PlayerBagSize => Player.BagSize;
        public float PlayerHealPickup => Player.HealPickup;

        public float BossHp => Boss.Hp;
        public float BossContactDamage => Boss.ContactDamage;
        public float BossMoveSpeed => Boss.MoveSpeed;

        // ================= 查询 =================

        /// <summary>按 id 造一件配件（子弹优先，其次衍生）。</summary>
        public ChainItem MakeItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Bullets.TryGetValue(id, out var b)) return new ChainItem { Bullet = b };
            if (Modifiers.TryGetValue(id, out var m)) return new ChainItem { Mod = m };
            return null;
        }

        /// <summary>按 id 取敌人定义。取不到返回 null（调用方必须兜底）。</summary>
        public EnemyDef Enemy(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Enemies.TryGetValue(id, out var e) ? e : null;
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

        /// <summary>开局持有的枪 id（Gun.tsv 的 starter=1，按表序）。</summary>
        public List<string> StarterGunIds()
        {
            var list = new List<string>();
            for (int i = 0; i < GunOrder.Count; i++)
                if (GunOrder[i].Starter) list.Add(GunOrder[i].Id);
            return list;
        }

        /// <summary>可抽取的全部配件 id（子弹 + 衍生，不含起手件）。</summary>
        public List<string> RollPool()
        {
            var list = new List<string>(BulletOrder.Count + ModifierOrder.Count);
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

        /// <summary>玩家等级 → 升级所需经验。公式可在 Player.tsv 覆盖（`xpPerLevelA/B/C`），此处保留默认。</summary>
        public int XpNeedFor(int level)
        {
            int a = Player.XpSquare, b = Player.XpLinear, c = Player.XpFlat;
            int need = a * level * level + b * level + c;
            return need < 1 ? 1 : need;
        }
    }
}

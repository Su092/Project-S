using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 配件作用域。出处 docs/loadout-spec.md §2（对齐 MODCORE 原型）。
    ///
    /// 为什么从 3 种扩到 7 种：原型把「作用到哪」做成了构筑的主要乐趣 ——
    /// 同一张「伤害增幅」放在不同作用域下是完全不同的牌，
    /// 玩家要靠选择作用域来分配有限槽位，这比「全场 / 仅后方」两档深得多。
    /// </summary>
    public enum Scope
    {
        /// <summary>下一发：**会被读取**（占读取次数），叠加到其后读到的第一个子弹。</summary>
        Next = 0,
        /// <summary>上一个：被动，作用于左侧最近的子弹。</summary>
        Prev = 1,
        /// <summary>相邻：被动，作用于左右两侧一格内的子弹。</summary>
        Adj = 2,
        /// <summary>后 2 格：被动，作用于右侧 2 格内。</summary>
        N2 = 3,
        /// <summary>后 3 格：被动，作用于右侧 3 格内（耗弹打折）。</summary>
        N3 = 4,
        /// <summary>前 2 格：被动，作用于左侧 2 格内。</summary>
        P2 = 5,
        /// <summary>全枪：被动，作用于整把枪械（枪属性或全部子弹）。</summary>
        All = 6,
    }

    public enum Rarity { Common = 0, Rare = 1, Epic = 2, Starter = 3 }

    /// <summary>
    /// 投射物形态。决定 View 层怎么把这个描述符变成场景里的东西。
    /// 原型里有 7 种：普通飞行物 / 瞬时光束 / 地雷 / 力场 / 连锁闪电 / 回旋锯 / 奇点。
    /// </summary>
    public enum BulletKind
    {
        Proj = 0,
        Beam = 1,
        Mine = 2,
        Field = 3,
        Chain = 4,
        Saw = 5,
        Hole = 6,
    }

    /// <summary>触发条件：命中 / 定时 / 消散。触发弹会把下一个子弹作为载荷施放。</summary>
    public enum TriggerKind
    {
        None = 0,
        Hit = 1,
        Timer = 2,
        Expire = 3,
    }

    /// <summary>
    /// 一颗弹丸的运行时描述符。结算产物，也是唯一载体。
    /// 字段语义见 docs/loadout-spec.md §3。
    /// </summary>
    public sealed class ProjectileDesc
    {
        public BulletKind Kind = BulletKind.Proj;
        public float Damage;
        public int Count = 1;          // 投射物数量（霰弹/分裂）
        public float Spread;           // 度
        public float Speed = 80f;
        public float Lifetime = 1.5f;
        public float Size = 0.12f;
        public int Pierce;
        public int Bounce;
        public float Homing;           // 转向强度
        public float Gravity;
        public float ExplodeR;
        public float ExplodeDmg;
        public float Burn;
        public float Slow;
        public float Crit = 0.05f;
        public float CritMul = 2f;
        public float Knock = 1f;
        public int ChainCount;
        public float Range = 60f;
        public float Radius;           // 力场/奇点半径
        public float Duration;         // 地雷/力场持续
        public TriggerKind Trigger = TriggerKind.None;

        /// <summary>载荷：触发弹施放的下一组子弹（可递归）。</summary>
        public List<ProjectileDesc> Payload;

        /// <summary>来源配件 id（UI 与统计用）。</summary>
        public string ModuleId;

        /// <summary>来源子弹定义（表现层取色/命名）。</summary>
        public BulletDef Source;

        /// <summary>
        /// 单发期望伤害（含暴击期望 + 爆炸 + 载荷）。
        ///
        /// 爆炸必须计入：榴弹的基础伤害只有 10、爆炸 38，漏掉爆炸会让 HUD 显示
        /// 「榴弹枪 DPS 11.7」而实际能打出 50+，玩家会认为这把枪是废的（基准里踩过）。
        /// 爆炸按「命中即对目标结算一次」计 —— 单体目标的期望就是 damage + explodeDmg。
        /// </summary>
        public float ExpectedDamage
        {
            get
            {
                float c = Crit > 1f ? 1f : Crit;
                float per = Damage * (1f + c * (CritMul - 1f));
                float payload = 0f;
                if (Payload != null)
                    for (int i = 0; i < Payload.Count; i++) payload += Payload[i].ExpectedDamage;
                return (per + ExplodeDmg) * Count + payload;
            }
        }

        /// <summary>
        /// 群伤期望：命中一次时对周围敌人的总产出估算。
        /// 爆炸半径内按 2.5 个额外目标计（掩体间的实测密度）。
        /// </summary>
        public float ExpectedGroupDamage
        {
            get
            {
                float single = ExpectedDamage;
                if (ExplodeR <= 0f) return single;
                return single + ExplodeDmg * 2.5f * Count;
            }
        }

        public ProjectileDesc Clone()
        {
            var d = (ProjectileDesc)MemberwiseClone();
            if (Payload != null)
            {
                d.Payload = new List<ProjectileDesc>(Payload.Count);
                for (int i = 0; i < Payload.Count; i++) d.Payload.Add(Payload[i].Clone());
            }
            return d;
        }
    }

    /// <summary>
    /// 子弹配件定义。数值为配件自身基础值，衍生配件在结算时改动它。
    /// 字段对齐原型 y_ 默认表 + 各件覆盖值。
    /// </summary>
    public sealed class BulletDef
    {
        public string Id;
        public string Name;
        public string Icon;
        public Rarity Rarity;
        public int Cost = 1;
        public float Delay;
        public string Desc;

        public BulletKind Kind = BulletKind.Proj;
        public float Damage = 10f;
        public int Count = 1;
        public float Spread;
        public float Speed = 80f;
        public float Lifetime = 1.5f;
        public float Size = 0.12f;
        public int Pierce;
        public int Bounce;
        public float Homing;
        public float Gravity;
        public float ExplodeR;
        public float ExplodeDmg;
        public float Burn;
        public float Slow;
        public float Crit = 0.05f;
        public float CritMul = 2f;
        public float Knock = 1f;
        public int ChainCount;
        public float Range = 60f;
        public float Radius;
        public float Duration;
        public TriggerKind Trigger = TriggerKind.None;
        public string Color = "#ffd25a";

        public ProjectileDesc ToDesc()
        {
            return new ProjectileDesc
            {
                Kind = Kind,
                Damage = Damage,
                Count = Count,
                Spread = Spread,
                Speed = Speed,
                Lifetime = Lifetime,
                Size = Size,
                Pierce = Pierce,
                Bounce = Bounce,
                Homing = Homing,
                Gravity = Gravity,
                ExplodeR = ExplodeR,
                ExplodeDmg = ExplodeDmg,
                Burn = Burn,
                Slow = Slow,
                Crit = Crit,
                CritMul = CritMul,
                Knock = Knock,
                ChainCount = ChainCount,
                Range = Range,
                Radius = Radius,
                Duration = Duration,
                Trigger = Trigger,
                ModuleId = Id,
                Source = this,
            };
        }
    }

    /// <summary>
    /// 衍生配件定义。
    ///
    /// 全部用**声明式字段**而不是函数：数据要能从 sdata.json 读到，
    /// 且结算必须能在 dotnet 下断言（Core 不引 Unity）。
    /// 原型里的 `apply(desc, t)` 被逐条翻译成「每层叠加量」，t = 同名同域叠加层数。
    /// </summary>
    public sealed class ModifierDef
    {
        public string Id;
        public string Name;
        public string Icon;
        public Rarity Rarity;
        public Scope Scope;
        public int Cost;
        public float Delay;
        public string Desc;

        // ---- 作用于子弹：乘法项（每层加成，最终 ×(1 + add·t)）----
        public float DamageMulAdd;      // 伤害
        public float SizeMulAdd;        // 体积
        public float SpeedMulAdd;       // 速度
        public float RangeMulAdd;       // 射程
        public float LifeMulAdd;        // 存在时间
        public float KnockMulAdd;       // 击退
        public float CountMulAdd;       // 数量
        public float RadiusMulAdd;      // 范围半径

        // ---- 作用于子弹：加法项（每层）----
        public int PiercePer;           // 每层额外穿透（最小 1）
        public int BouncePer;           // 每层额外弹跳（最小 1）
        public float HomingPer;         // 追踪强度
        public float BurnPer;           // 燃烧 dps
        public float SlowPer;           // 减速（取 max）
        public float CritPer;           // 暴击率
        public float SpreadPer;         // 散射（度）

        // ---- 爆裂（特殊：无爆裂时按基础半径建）----
        public float ExplodeRAdd;       // 已有爆裂时的半径加成
        public float ExplodeRBase;      // 无爆裂时的基础半径
        public float ExplodeDmgFromDmg; // 爆伤 = 伤害 × 该系数
        public float ExplodeDmgFlat;    // 爆伤固定加成

        // ---- 弹匣/弹药 ----
        public int AmmoReduce;          // 目标子弹耗弹 -N

        // ---- 全枪（仅 Scope == All 且为枪械件时使用）----
        public float GunMagMul = 1f;
        public float GunReloadMul = 1f;
        public float GunDelayMul = 1f;
        public float GunSpreadMul = 1f;
        public float GunRecycleAdd;     // 25% 不耗弹：recycle = 1-(1-recycle)*(1-x)
        public int GunCastAdd;
        public float GunMagMulIfCast;   // 并联扳机的弹匣惩罚
        public float GunLeech;

        // ---- 多重施放 ----
        public int Multicast;

        /// <summary>是否只作用于枪械（不参与投射物结算）。</summary>
        public bool IsGunOnly =>
            GunMagMul != 1f || GunReloadMul != 1f || GunDelayMul != 1f || GunSpreadMul != 1f ||
            GunRecycleAdd != 0f || GunCastAdd != 0 || GunLeech != 0f;

        /// <summary>是否为被动件（不被读取）。`Next` 以外的全部是被动。</summary>
        public bool IsPassive => Scope != Scope.Next;
    }

    /// <summary>一个槽位里的一项（子弹或衍生）。原型里一格只放一件。</summary>
    public sealed class ChainItem
    {
        public BulletDef Bullet;
        public ModifierDef Mod;
        public bool IsBullet => Bullet != null;
        public string Id => IsBullet ? Bullet.Id : Mod.Id;
        public string Name => IsBullet ? Bullet.Name : Mod.Name;
        public Rarity Rarity => IsBullet ? Bullet.Rarity : Mod.Rarity;
    }

    /// <summary>枪械定义。字段对齐原型 Zn 表。</summary>
    public sealed class GunDef
    {
        public string Id;
        public string Name;
        public string Desc;
        public int SlotCount;
        public int MagSize;
        public float ReloadTime;
        public float FireInterval;
        public float SpreadBase;
        public int CastCount = 1;
        public string BaseBullet;      // 开火时第 1 槽预置的子弹 id
        public string Color = "#8f8f8f";
        public float ZoomDistance = 55f;  // 右键瞄准的 FOV

        /// <summary>第一人称枪模名（Resources/Models/&lt;名&gt;）。原先硬编码在 PlayerWeapons 的 switch 里。</summary>
        public string Model = "Modle_Gun_Auto";

        /// <summary>是否开局就持有的枪。原先是 PlayerWeapons.Init 里的 string[]{"rifle","shotgun"}。</summary>
        public bool Starter;
    }

    /// <summary>
    /// 敌人行为。**取代**原先散在 GameRoot 与 Enemy 里的 `Enemy.Kind` 枚举。
    ///
    /// 为什么换掉枚举：旧枚举把「哪种敌人」写进了两处 switch ——
    /// GameRoot 的 6 个 `Kind*` 函数（模型/高度/血/伤/速/经验）和 Enemy.Update 的行为分支。
    /// 新增一种敌人要改 7 个地方，漏一个就静默退化成爬行者。
    /// 现在「哪种敌人」是一行数据，行为只是 3 选 1 的策略枚举。
    /// </summary>
    public enum EnemyAi
    {
        /// <summary>近身撞击（原 Crawler / Brute）。</summary>
        Chaser = 0,
        /// <summary>贴近后自爆（原 Bomber）。</summary>
        Bomber = 1,
        /// <summary>原地远程点射（原 Turret）。</summary>
        Turret = 2,
    }

    /// <summary>
    /// 一种敌人。数值与表现全部来自 Cfg/Enemy.tsv，代码里不再有按类型分支的常量。
    /// </summary>
    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public EnemyAi Ai = EnemyAi.Chaser;

        /// <summary>模型名（Resources/Models/&lt;名&gt;）。缺件时 SAssets 自动回退胶囊占位。</summary>
        public string Model;
        /// <summary>模型归一到的目标高度（米）。</summary>
        public float Height = 1f;

        public float Hp = 60f;
        public float Damage = 10f;
        public float Speed = 2f;
        public int Xp = 8;

        /// <summary>近身判定的距离（米）。0 = 不做接触伤害。</summary>
        public float ContactRange = 1.4f;
        /// <summary>远程/引爆的触发距离（米）。0 = 不用。</summary>
        public float AttackRange;

        /// <summary>重复攻击/引爆的冷却（秒）。0 = 一次性（引爆型用完即毁，不需要）。</summary>
        public float AttackCooldown;

        /// <summary>
        /// 正面盾减伤：朝向点积阈值（0 = 无盾）。
        /// 原先写死在 Enemy.TakeDamage：`facing > 0.3f` 才判减伤。
        /// </summary>
        public float ShieldFacing;
        /// <summary>盾面挡住时（普通弹）的伤害系数。</summary>
        public float ShieldMul = 1f;
        /// <summary>盾面挡住时（穿透弹）的伤害系数。</summary>
        public float ShieldPierceMul = 1f;

        public bool HasShield => ShieldFacing > 0f;
    }
}

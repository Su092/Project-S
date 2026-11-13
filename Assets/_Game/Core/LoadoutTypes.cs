using System.Collections.Generic;

namespace S.Core
{
    /// <summary>配件作用域。数值含义见 docs/loadout-spec.md §3.1。</summary>
    public enum Scope
    {
        /// <summary>全场：作用于施加时刻链内全部投射物。</summary>
        Whole = 0,
        /// <summary>后继：只作用于施加时刻之后出现的子弹。</summary>
        After = 1,
        /// <summary>枪械：不作用于投射物，装配时作用于整把枪。</summary>
        Gun = 2,
    }

    public enum Rarity { Common = 0, Rare = 1, Epic = 2, Starter = 3 }

    public enum GunKind { Auto = 0, Grenade = 1, Single = 2 }

    public enum ProjectileMode { Straight = 0, Ballistic = 1 }

    /// <summary>
    /// 一颗弹丸的运行时描述符。链解析的产物，也是结算的唯一载体。
    /// 字段语义见 docs/loadout-spec.md §2。
    /// </summary>
    public sealed class ProjectileDesc
    {
        public float Damage;
        public float SpreadAdd;      // 度，加法项
        public float SpreadMul = 1f; // 乘法项
        public float Lifetime;
        public float Crit;
        public float CritDmg = 2f;
        public float SpeedMul = 1f;
        public int Pierce;
        public int Bounce;
        public float SplashRadius;
        public float HomingRate;     // 度/秒
        public int SourceIndex;      // 来源子弹在链中的子弹序号（0 起）
        public int Generation;       // 分裂代数，用于禁止无限分裂
        public BulletDef Source;     // 来源子弹定义（行为特性用）

        /// <summary>有效散射（度）。钳制后结果，非负。</summary>
        public float Spread => SpreadAdd * SpreadMul < 0f ? 0f : SpreadAdd * SpreadMul;

        /// <summary>单发期望伤害 = damage × (1 + crit × (critDmg − 1))。</summary>
        public float ExpectedDamage
        {
            get
            {
                float c = Crit > 1f ? 1f : Crit;
                return Damage * (1f + c * (CritDmg - 1f));
            }
        }

        public ProjectileDesc Clone() => (ProjectileDesc)MemberwiseClone();
    }

    /// <summary>子弹配件定义。数值为配件自身值。</summary>
    public sealed class BulletDef
    {
        public string Id;
        public string Name;
        public float Damage;
        public int Ammo;
        public float SpreadAdd;   // 度
        public float Lifetime;
        public float Crit;        // 0..1
        public float CritDmg;
        public float SplashRadius;
        public int Pierce;
        public int Bounce;
        public float SpeedMul = 1f;
        public Rarity Rarity;

        // 行为特性（伤害以外的机制）
        public float BurnDps;       // 燃烧每秒伤害
        public float BurnTime;
        public int BurnMaxStacks;
        public float SlowPct;       // 减速百分比
        public float SlowTime;
        public bool IgnoreArmor;

        public ProjectileDesc ToDesc()
        {
            return new ProjectileDesc
            {
                Damage = Damage,
                SpreadAdd = SpreadAdd,
                Lifetime = Lifetime,
                Crit = Crit,
                CritDmg = CritDmg,
                SplashRadius = SplashRadius,
                Pierce = Pierce,
                Bounce = Bounce,
                SpeedMul = SpeedMul,
                Source = this,
            };
        }
    }

    /// <summary>衍生配件定义。所有效果都以「运算式」字段表达，见 loadout-spec §3.2。</summary>
    public sealed class ModifierDef
    {
        public string Id;
        public string Name;
        public Scope Scope;
        public Rarity Rarity;

        // 加法项
        public float DamageFlat;
        public float CritFlat;
        public float CritDmgFlat;
        public float SpreadAddFlat;
        public float LifetimeFlat;
        public int PierceFlat;
        public int BounceFlat;
        public int AmmoFlat;

        // 乘法项
        public float DamageMul = 1f;
        public float CritMul = 1f;
        public float SpreadMul = 1f;
        public float LifetimeMul = 1f;
        public float SpeedMul = 1f;

        // 行为项
        public float SplashAdd;
        public float HomingRate;
        public int SplitCount;      // >1 时为分裂
        public float SplitDamageMul = 1f;
        public float SplitSpreadAdd;
        public float SplitLifetimeMul = 1f;

        // 枪械项
        public int GunMagAdd;
        public float GunFireIntervalMul = 1f;
        public float GunSpreadMul = 1f;
        public float GunReloadMul = 1f;

        /// <summary>作用于投射物时是否有任何效果。false 且 Scope 非 Gun ⇒ 该件在此链位置无意义。</summary>
        public bool AffectsProjectile =>
            DamageFlat != 0f || CritFlat != 0f || CritDmgFlat != 0f || SpreadAddFlat != 0f ||
            LifetimeFlat != 0f || PierceFlat != 0 || BounceFlat != 0 || AmmoFlat != 0 ||
            DamageMul != 1f || CritMul != 1f || SpreadMul != 1f || LifetimeMul != 1f ||
            SpeedMul != 1f || SplashAdd != 0f || HomingRate != 0f || SplitCount > 1;
    }

    /// <summary>一条链里的一项（子弹或衍生）。</summary>
    public sealed class ChainItem
    {
        public BulletDef Bullet;
        public ModifierDef Mod;
        public bool IsBullet => Bullet != null;
        public string Id => IsBullet ? Bullet.Id : Mod.Id;
    }

    /// <summary>枪械定义。</summary>
    public sealed class GunDef
    {
        public string Id;
        public string Name;
        public GunKind Kind;
        public int SlotCount;
        public float FireInterval;
        public int MagSize;
        public float ReloadTime;
        public float SpreadBase;
        public float MuzzleSpeed;
        public float DamageMul = 1f;
        public float SplashRadius;
        public ProjectileMode Mode = ProjectileMode.Straight;
        public float Gravity;
    }
}

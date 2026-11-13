using System.Collections.Generic;

namespace S.Core
{
    /// <summary>Boss 阶段。出处 docs/gameplay-spec.md §3.1。</summary>
    public enum BossPhase { P1 = 1, P2 = 2, P3 = 3, Dead = 4 }

    /// <summary>
    /// 铸造核心 Boss 的状态机：血量分段、护盾周期、能量柱破坏与破防。
    /// 零 Unity 依赖 —— 「什么时候该开护盾」「破防窗口多长」这类时序最容易错，必须可断言。
    /// 时间由外部以 dt 推进（暂停时外部不调 Tick 即可）。
    /// </summary>
    public sealed class BossState
    {
        public const int PillarCount = 4;
        public const float PillarHp = 800f;
        public const float PillarBreachTime = 4f;     // 破坏一根后的破防时长
        public const float BreachDamageMul = 1.5f;    // 破防期受伤倍率
        public const float ShieldPeriod = 18f;        // P2 起护盾周期
        public const float ShieldUpTime = 6f;         // 护盾持续

        public float MaxHp;
        public float Hp;

        public BossPhase Phase { get; private set; } = BossPhase.P1;

        /// <summary>护盾是否开启（P2 起才有）。开盾时正面免伤。</summary>
        public bool ShieldUp { get; private set; }

        /// <summary>破防剩余时间（>0 时受伤 ×1.5）。</summary>
        public float BreachLeft { get; private set; }

        /// <summary>存活能量柱数（0..4）。</summary>
        public int PillarsAlive { get; private set; } = PillarCount;

        readonly float[] _pillarHp = new float[PillarCount];

        float _shieldTimer;
        float _addTimer;

        public BossState(float maxHp)
        {
            MaxHp = maxHp;
            Hp = maxHp;
            for (int i = 0; i < PillarCount; i++) _pillarHp[i] = PillarHp;
        }

        public bool Alive => Hp > 0f && Phase != BossPhase.Dead;

        public float HpRatio => MaxHp <= 0f ? 0f : Hp / MaxHp;

        /// <summary>破防中（受伤加成窗口）。</summary>
        public bool IsBreached => BreachLeft > 0f;

        /// <summary>当前受伤倍率：破防 ×1.5，否则 ×1。护盾由外部按方向判定。</summary>
        public float DamageTakenMul => IsBreached ? BreachDamageMul : 1f;

        public float PillarHpAt(int i) =>
            i >= 0 && i < PillarCount ? _pillarHp[i] : 0f;

        public bool IsPillarAlive(int i) => PillarHpAt(i) > 0f;

        /// <summary>
        /// 推进时间。dt 由外部按真实帧给（暂停时不调）。
        /// 返回本帧发生的事件，供表现层播特效。
        /// </summary>
        public List<BossEvent> Tick(float dt)
        {
            var ev = new List<BossEvent>();
            if (!Alive || dt <= 0f) return ev;

            if (BreachLeft > 0f) BreachLeft -= dt;

            // 阶段切换（只在跨阈值时触发一次）
            var newPhase = PhaseFor(HpRatio);
            if (newPhase != Phase)
            {
                Phase = newPhase;
                ev.Add(new BossEvent { Kind = BossEventKind.PhaseChange, Value = (int)Phase });
                if (Phase >= BossPhase.P2) { _shieldTimer = 0f; }
            }

            if (Phase == BossPhase.Dead) return ev;

            // P2 起护盾周期：先开 6 秒，再关直到下一周期
            if (Phase >= BossPhase.P2)
            {
                _shieldTimer -= dt;
                if (_shieldTimer <= 0f)
                {
                    ShieldUp = !ShieldUp;
                    _shieldTimer = ShieldUp ? ShieldUpTime : ShieldPeriod - ShieldUpTime;
                    ev.Add(new BossEvent
                    {
                        Kind = ShieldUp ? BossEventKind.ShieldOn : BossEventKind.ShieldOff,
                    });
                }
            }

            // P1/P2 每 12 秒召唤两只爬行者
            if (Phase < BossPhase.P3)
            {
                _addTimer += dt;
                if (_addTimer >= 12f)
                {
                    _addTimer = 0f;
                    ev.Add(new BossEvent { Kind = BossEventKind.SummonAdds, Value = 2 });
                }
            }

            // P3 每 20 秒全屏冲击波（需跳跃躲避）
            if (Phase == BossPhase.P3)
            {
                _addTimer += dt;
                if (_addTimer >= 20f)
                {
                    _addTimer = 0f;
                    ev.Add(new BossEvent { Kind = BossEventKind.Shockwave });
                }
            }

            return ev;
        }

        static BossPhase PhaseFor(float ratio)
        {
            if (ratio <= 0f) return BossPhase.Dead;
            if (ratio > 0.66f) return BossPhase.P1;
            if (ratio > 0.33f) return BossPhase.P2;
            return BossPhase.P3;
        }

        /// <summary>
        /// 受伤。splash 与破防都在这层结算。
        /// 返回实际扣掉的血量（供飘字用）。
        /// </summary>
        public float TakeDamage(float amount, bool splash)
        {
            if (!Alive || amount <= 0f) return 0f;

            float final = amount * DamageTakenMul;
            Hp -= final;
            if (Hp <= 0f)
            {
                Hp = 0f;
                Phase = BossPhase.Dead;
                ShieldUp = false;
            }
            return final;
        }

        /// <summary>
        /// 正面护盾免伤判定。返回 true 表示这次攻击被护盾挡下。
        /// 破防期间护盾无效；空蚀弹（ignoreArmor）穿透一半。
        /// </summary>
        public bool IsBlockedByShield(bool ignoreArmor)
        {
            if (!ShieldUp) return false;
            if (IsBreached) return false;
            return !ignoreArmor;
        }

        /// <summary>
        /// 打能量柱。破坏一根立刻关闭护盾并进入破防。
        /// 返回 true 表示这一击打碎了柱子。
        /// </summary>
        public bool DamagePillar(int index, float amount)
        {
            if (!Alive) return false;
            if (index < 0 || index >= PillarCount) return false;
            if (_pillarHp[index] <= 0f) return false;

            _pillarHp[index] -= amount;
            if (_pillarHp[index] > 0f) return false;

            _pillarHp[index] = 0f;
            PillarsAlive--;

            // 破坏 → 立刻关盾 + 破防窗口
            ShieldUp = false;
            BreachLeft = PillarBreachTime;
            _shieldTimer = ShieldUpTime;   // 从关盾开始重新计时
            return true;
        }
    }

    public enum BossEventKind
    {
        PhaseChange,
        ShieldOn,
        ShieldOff,
        SummonAdds,
        Shockwave,
    }

    public struct BossEvent
    {
        public BossEventKind Kind;
        public int Value;
    }
}

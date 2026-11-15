using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 单张箱庭的节奏总导演：**只由存活时间驱动**的阶段、刷怪规格、威胁值与精英计时。
    ///
    /// 为什么从「区域推进」改成「时间轴」（docs/gameplay-spec.md §2.1）：
    /// 原设计用 6 个区域 + 清区开门来分段加压，代价是每段都要重新暖场，
    /// 真正的高压只占全程三分之一。单图 + 时间轴把暖场压缩成一次，
    /// 玩家从第 1 分钟起就在同一套掩体里做走位决策，构筑差异能被连续放大。
    ///
    /// 这是纯逻辑：无 Unity 依赖、无随机、输入只有 dt 与「Boss 是否已入场」。
    /// 刷怪位置、模型、特效全归 View —— 这样「4 分钟出精英、8 分钟出 Boss」
    /// 这条最硬的验收判据可以在 dotnet 下逐秒断言，不用开编辑器。
    /// </summary>
    public sealed class RunDirector
    {
        public const float ThreatMax = 100f;

        /// <summary>一条刷怪时间轴条目（数据来自 sdata.json 的 timeline.phases）。</summary>
        public sealed class Phase
        {
            public float At;              // 从开局算起的秒数
            public string Name;           // UI 显示名（如「混编」）
            public string[] Pool;         // 敌人池（Enemy.Kind 名）
            public int Concurrency;       // 场上同时存活上限
            public float Interval;        // 单只刷出间隔（秒）
        }

        /// <summary>精英的强化系数（数据来自 sdata.json 的 elite）。</summary>
        public sealed class EliteDef
        {
            public float HpMul = 3.5f;
            public float DmgMul = 1.6f;
            public float SpeedMul = 0.85f;
            public int XpMul = 6;
            public float Scale = 1.35f;
            public float HealOnKill = 15f;
            public float FirstAt = 240f;      // 4:00
            public float Interval = 90f;      // 之后每 90 s 一只
        }

        readonly List<Phase> _phases = new List<Phase>();
        readonly Phase _bossPhase;

        public EliteDef Elite = new EliteDef();

        /// <summary>Boss 入场时刻（秒），数据来自 timeline.bossAt。</summary>
        public float BossAt = 480f;

        /// <summary>威胁值到满的时刻（秒），数据来自 timeline.threatFullAt。</summary>
        public float ThreatFullAt = 480f;

        /// <summary>战斗计时（秒）。暂停时不推进 —— 由外部不调用 Tick 实现。</summary>
        public float Elapsed { get; private set; }

        /// <summary>Boss 是否已入场（只置一次）。</summary>
        public bool BossSpawned { get; private set; }

        /// <summary>已刷出的精英数量。</summary>
        public int ElitesSpawned { get; private set; }

        public RunDirector(IList<Phase> phases, Phase bossPhase)
        {
            if (phases != null) _phases.AddRange(phases);
            _phases.Sort((a, b) => a.At.CompareTo(b.At));
            _bossPhase = bossPhase;
        }

        // ---------------- 阶段 ----------------

        /// <summary>
        /// 当前刷怪规格。Boss 入场后固定为 Boss 阶段（降并发，避免 Boss 战变成空场单挑）。
        /// 时间轴为空时返回 null，调用方必须能接受「不刷怪」。
        /// </summary>
        public Phase CurrentPhase
        {
            get
            {
                if (BossSpawned) return _bossPhase;
                Phase best = null;
                for (int i = 0; i < _phases.Count; i++)
                {
                    if (_phases[i].At <= Elapsed) best = _phases[i];
                    else break;
                }
                return best;
            }
        }

        public IReadOnlyList<Phase> Phases => _phases;

        /// <summary>可写视图：自检需要复制一份时间轴来独立快进，而不污染主实例。</summary>
        public List<Phase> PhaseList => _phases;

        // ---------------- 威胁 ----------------

        /// <summary>
        /// 威胁值只由存活时间决定（gameplay-spec §4）。
        ///
        /// 为什么不再「击杀 +0.4」：无限刷怪下击杀数增长极快，威胁会在一分半内爆表；
        /// 而且它惩罚「打得好」—— 与「给停留定价」的设计意图正好相反。
        /// </summary>
        public float Threat
        {
            get
            {
                if (ThreatFullAt <= 0f) return ThreatMax;
                float t = Elapsed / ThreatFullAt * ThreatMax;
                return t < 0f ? 0f : t > ThreatMax ? ThreatMax : t;
            }
        }

        // ---------------- 精英 ----------------

        /// <summary>下一只精英的入场时刻（秒）。已出满或 Boss 已入场时返回 <see cref="float.PositiveInfinity"/>。</summary>
        public float NextEliteAt
        {
            get
            {
                if (BossSpawned) return float.PositiveInfinity;
                return Elite.FirstAt + ElitesSpawned * Elite.Interval;
            }
        }

        /// <summary>距下一只精英还有多少秒（UI 用）；没有则返回 -1。</summary>
        public float SecondsToElite
        {
            get
            {
                float at = NextEliteAt;
                if (float.IsPositiveInfinity(at)) return -1f;
                float d = at - Elapsed;
                return d < 0f ? 0f : d;
            }
        }

        /// <summary>距 Boss 入场还有多少秒（UI 用）；已入场返回 -1。</summary>
        public float SecondsToBoss =>
            BossSpawned ? -1f : System.Math.Max(0f, BossAt - Elapsed);

        // ---------------- 推进 ----------------

        /// <summary>一帧内发生的事件。调用方按顺序播放表现（Toast / 特效 / 刷 Boss）。</summary>
        public enum EventKind
        {
            None = 0,
            EliteDue = 1,
            BossDue = 2,
            PhaseChanged = 3,
        }

        public struct RunEvent
        {
            public EventKind Kind;
            public int PhaseIndex;
            public int EliteIndex;
        }

        /// <summary>
        /// 推进 dt 秒。返回本帧应处理的事件（最多三条：阶段切换 / 精英 / Boss）。
        ///
        /// 注意：一次 Tick 里可能同时跨过「精英」与「Boss」两个阈值（dt 很大或刚好在 8:00 前一刻），
        /// 所以这里是「按当前时间补发」而不是「每帧只判一个」，否则会漏刷。
        /// </summary>
        public List<RunEvent> Tick(float dt)
        {
            var ev = new List<RunEvent>();
            if (dt <= 0f) return ev;

            int phaseBefore = IndexOf(CurrentPhase);
            Elapsed += dt;
            int phaseAfter = IndexOf(CurrentPhase);
            if (phaseAfter != phaseBefore && phaseAfter >= 0)
                ev.Add(new RunEvent { Kind = EventKind.PhaseChanged, PhaseIndex = phaseAfter });

            // 精英：可补多只（正常帧下最多 1 只）
            int guard = 0;
            while (!BossSpawned && ElitesSpawned < EliteCap && Elapsed >= NextEliteAt && guard++ < 16)
            {
                ev.Add(new RunEvent { Kind = EventKind.EliteDue, EliteIndex = ElitesSpawned });
                ElitesSpawned++;
            }

            if (!BossSpawned && Elapsed >= BossAt)
            {
                BossSpawned = true;
                ev.Add(new RunEvent { Kind = EventKind.BossDue });
            }

            return ev;
        }

        /// <summary>精英总数上限：8 分钟里 4:00 起每 90 s 一只 → 4:00 / 5:30 / 7:00 共 3 只，
        /// 之后 Boss 入场（8:00）不再出精英。用公式算而不是写死，改 Interval 时自动跟随。</summary>
        public int EliteCap
        {
            get
            {
                if (Elite.Interval <= 0f) return 1;
                float window = BossAt - Elite.FirstAt;
                if (window < 0f) return 0;
                return 1 + (int)(window / Elite.Interval);
            }
        }

        /// <summary>
        /// 把计时直接拨到 target 秒（**只用于自检**：验证 4:00 精英与 8:00 Boss 会按时触发，
        /// 否则一条断言要真等 8 分钟）。拨动后事件由下一次 Tick(0) 补发，
        /// 走的是与实战完全相同的结算路径，不是绕过规则。
        /// </summary>
        public void SeekTo(float target)
        {
            if (target > Elapsed) Elapsed = target;
        }

        int IndexOf(Phase p) => p == null ? -1 : _phases.IndexOf(p);
    }
}

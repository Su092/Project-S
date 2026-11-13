using System.Collections.Generic;

namespace S.Core
{
    /// <summary>一个区域的静态定义。出处 docs/gameplay-spec.md §2。</summary>
    public sealed class ZoneDef
    {
        public int Index;            // 1 起
        public string Name;
        public int Waves;
        public string[] Pool;        // 敌人池（Enemy.Kind 名）
        public float ThreatStart;    // 进入时的威胁下限
        public bool IsBoss;
    }

    /// <summary>
    /// 区域推进状态机：哪个区域已清空、哪道门开着、下一区是否可进。
    /// 零 Unity 依赖，可 headless 断言（区域锁/门的语义最容易写错，必须可验）。
    /// </summary>
    public sealed class ZoneProgress
    {
        readonly List<ZoneDef> _zones;
        readonly List<bool> _cleared;

        /// <summary>当前所在区域（1 起）。</summary>
        public int Current { get; private set; } = 1;

        /// <summary>本区当前波次（0 = 尚未开始）。</summary>
        public int Wave { get; private set; }

        /// <summary>本区剩余待刷敌人的计数（由外部递减）。</summary>
        public bool CurrentCleared => Current >= 1 && Current <= _zones.Count && _cleared[Current - 1];

        public int ZoneCount => _zones.Count;

        public ZoneProgress(IList<ZoneDef> zones)
        {
            _zones = new List<ZoneDef>(zones);
            _cleared = new List<bool>();
            for (int i = 0; i < _zones.Count; i++) _cleared.Add(false);
        }

        public ZoneDef Get(int index) =>
            index >= 1 && index <= _zones.Count ? _zones[index - 1] : null;

        public ZoneDef CurrentDef => Get(Current);

        public bool IsCleared(int index) =>
            index >= 1 && index <= _zones.Count && _cleared[index - 1];

        /// <summary>
        /// 第 from 区通往 from+1 区的门是否开启。
        /// 规则：只有走完当前区才开门；末尾区没有门。
        /// </summary>
        public bool IsDoorOpen(int from) => IsCleared(from) && from < _zones.Count;

        /// <summary>
        /// 能否进入第 target 区。必须先清空前一区，且不能跳区。
        /// </summary>
        public bool CanEnter(int target)
        {
            if (target == 1) return true;                  // 起点永远可进
            if (target < 1 || target > _zones.Count) return false;
            return IsCleared(target - 1);                  // 前一区清空才放行
        }

        /// <summary>
        /// 进入区域。返回 null 表示成功，否则返回拒绝码。
        /// 进入后波次归零（由 BeginWave 推进）。
        /// </summary>
        public string TryEnter(int target)
        {
            if (target < 1 || target > _zones.Count) return "bad-zone";
            if (target == Current) return null;            // 已在此区，幂等
            if (!CanEnter(target)) return "locked";
            Current = target;
            Wave = 0;
            return null;
        }

        /// <summary>开始下一波。超过该区波数返回 false（该清区了）。</summary>
        public bool BeginWave()
        {
            if (CurrentCleared) return false;
            var def = CurrentDef;
            if (def == null) return false;
            if (Wave >= def.Waves) return false;
            Wave++;
            return true;
        }

        /// <summary>
        /// 标记当前区清空（全部波次打完且场上无敌人）。
        /// 返回 false 表示还没打完。
        /// </summary>
        public bool TryClearCurrent()
        {
            var def = CurrentDef;
            if (def == null) return false;
            if (CurrentCleared) return false;
            if (Wave < def.Waves) return false;
            _cleared[Current - 1] = true;
            return true;
        }

        /// <summary>下一区的索引；没有则返回 0。</summary>
        public int NextZone => Current < _zones.Count ? Current + 1 : 0;

        /// <summary>是否为最后一个区域的 Boss 场。</summary>
        public bool IsFinalZone => Current == _zones.Count;

        /// <summary>本区进入时应保证的威胁下限（跨区累积，不清零）。</summary>
        public float ThreatFloor => CurrentDef != null ? CurrentDef.ThreatStart : 0f;
    }
}

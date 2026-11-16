using System.Collections.Generic;
using System.Text;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 对局遥测：把「手感问题」变成可测量的数字。
    ///
    /// 为什么需要它：威胁节奏、敌人血量手感、背包容量这三项只能靠玩才能判断，
    /// 但「凭感觉说太快/太慢」没法定位。这里在局内采样关键指标并写日志，
    /// 玩家玩完一局就能拿到数据，据此校准比反复试玩省事。
    ///
    /// 单图 + 时间轴改造后，最重要的采样项变成了**时间轴对齐**：
    /// 4:00 的精英与 8:00 的 Boss 是否真的按时出现、玩家在第几分钟到达什么等级，
    /// 这些是 `gameplay-spec.md` §2.1 唯一的验收依据。
    ///
    /// 产物：`Application.persistentDataPath/run-telemetry.txt`
    /// 只在正常游玩时启用（自检不写，避免污染数据）。
    /// </summary>
    public sealed class RunTelemetry : MonoBehaviour
    {
        const float SampleInterval = 5f;
        const string FileName = "run-telemetry.txt";

        GameRoot _root;
        float _sampleTimer;
        bool _enabled;

        // 敌人「生成时刻」表：用于算击杀耗时
        readonly Dictionary<Enemy, float> _spawnTime = new Dictionary<Enemy, float>();
        readonly Dictionary<string, List<float>> _killTimes = new Dictionary<string, List<float>>();

        struct TimelineMark
        {
            public string Label;
            public float Time;
            public float Threat;
            public int Kills;
            public int Level;
        }
        readonly List<TimelineMark> _marks = new List<TimelineMark>();
        readonly List<string> _curve = new List<string>();

        int _bagOpenCount;
        float _bagOpenTime;
        bool _bagWasOpen;
        int _bagPeak;
        int _offerCount;

        public void Init(GameRoot root)
        {
            _root = root;
        }

        /// <summary>由 GameRoot 在正常游玩开始时启用（自检不启用）。</summary>
        public void Enable() => _enabled = true;

        public bool Enabled => _enabled;

        /// <summary>登记敌人生成（用于算击杀耗时）。</summary>
        public void NoteSpawn(Enemy e)
        {
            if (!_enabled || e == null) return;
            _spawnTime[e] = Elapsed;
        }

        /// <summary>登记敌人死亡，记录该类敌人的击杀耗时。</summary>
        public void NoteKill(Enemy e)
        {
            if (!_enabled || e == null) return;
            if (!_spawnTime.TryGetValue(e, out float t0)) return;
            _spawnTime.Remove(e);

            float dt = Elapsed - t0;
            string key = e.IsElite ? "精英" : e.KindId;
            if (!_killTimes.TryGetValue(key, out var list))
            {
                list = new List<float>();
                _killTimes[key] = list;
            }
            list.Add(dt);
        }

        /// <summary>登记一次三选一（用于统计升级频率）。</summary>
        public void NoteOffer() => _offerCount++;

        public void NotePhase(string name, float at)
        {
            if (!_enabled || _root?.Run == null) return;
            Mark("阶段·" + name, at);
        }

        public void NoteElite(float at, string kind)
        {
            if (!_enabled || _root?.Run == null) return;
            Mark("精英·" + kind, at);
        }

        public void NoteBossSpawn(float at)
        {
            if (!_enabled || _root?.Run == null) return;
            Mark("BOSS 入场", at);
        }

        void Mark(string label, float at)
        {
            _marks.Add(new TimelineMark
            {
                Label = label,
                Time = at,
                Threat = _root.Run.Threat,
                Kills = _root.Run.Kills,
                Level = _root.Run.Level,
            });
        }

        float Elapsed => _root != null && _root.Director != null ? _root.Director.Elapsed : 0f;

        void Update()
        {
            if (!_enabled || _root?.Run == null) return;

            float dt = Time.unscaledDeltaTime;

            // 时间轴曲线（威胁值由时间决定，所以它同时也是威胁曲线）
            _sampleTimer -= dt;
            if (_sampleTimer <= 0f)
            {
                _sampleTimer = SampleInterval;
                _curve.Add($"{Elapsed:000.0}s  threat={_root.Run.Threat:0}  " +
                           $"kills={_root.Run.Kills}  lv={_root.Run.Level}  " +
                           $"alive={_root.AliveEnemies}");
            }

            // 背包开关时长
            bool open = _root.InventoryOpen;
            if (open && !_bagWasOpen)
            {
                _bagOpenCount++;
                int n = _root.Run.Bag.Count;
                if (n > _bagPeak) _bagPeak = n;
            }
            if (open) _bagOpenTime += dt;
            _bagWasOpen = open;

            int bagNow = _root.Run.Bag.Count;
            if (bagNow > _bagPeak) _bagPeak = bagNow;
        }

        /// <summary>对局结束时写报告。</summary>
        public void Finish(float elapsed, bool won)
        {
            if (!_enabled || _root?.Run == null) return;
            _enabled = false;

            var d = _root.Director;
            var sb = new StringBuilder();
            sb.AppendLine("===== 对局遥测 =====");
            sb.AppendLine($"结果: {(won ? "通关" : "失败")}");
            sb.AppendLine($"总时长: {elapsed:0.0} 秒（{elapsed / 60f:0.0} 分钟）");
            sb.AppendLine($"到达等级: {_root.Run.Level}   击杀: {_root.Run.Kills}");
            sb.AppendLine($"最终威胁: {_root.Run.Threat:0}");
            sb.AppendLine($"三选一次数: {_offerCount}");
            sb.AppendLine($"精英出场: {(d != null ? d.ElitesSpawned : 0)} 只");
            sb.AppendLine($"Boss 入场: {(d != null && d.BossSpawned ? "是" : "否")}");
            sb.AppendLine($"打开背包: {_bagOpenCount} 次，共 {_bagOpenTime:0.0} 秒" +
                          $"（占 {(elapsed > 0f ? _bagOpenTime / elapsed * 100f : 0f):0}%）");
            sb.AppendLine($"背包峰值占用: {_bagPeak}/{_root.Run.BagCapacity}");
            sb.AppendLine();

            sb.AppendLine("--- 时间轴关键点 ---");
            sb.AppendLine("标记                时间     威胁  击杀  等级");
            foreach (var m in _marks)
                sb.AppendLine($"{m.Label,-16}  {m.Time,7:0.0}s  {m.Threat,4:0}  {m.Kills,4}  {m.Level,4}");
            sb.AppendLine();

            sb.AppendLine("--- 时间轴曲线 ---");
            foreach (var line in _curve) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("--- 敌人击杀耗时 ---");
            sb.AppendLine("类型       次数   平均     最快     最慢");
            foreach (var kv in _killTimes)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                float sum = 0f, min = float.MaxValue, max = 0f;
                foreach (var t in list) { sum += t; if (t < min) min = t; if (t > max) max = t; }
                sb.AppendLine($"{kv.Key,-10} {list.Count,4}  {sum / list.Count,6:0.00}s  {min,6:0.00}s  {max,6:0.00}s");
            }

            var path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
            try
            {
                System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log($"[S] 遥测已写入 {path}\n{sb}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[S] 遥测写入失败：{e.Message}");
            }
        }

        public string ReportPath =>
            System.IO.Path.Combine(Application.persistentDataPath, FileName);
    }
}

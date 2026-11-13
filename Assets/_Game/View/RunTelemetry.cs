using System.Collections.Generic;
using System.Text;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 对局遥测：把「手感问题」变成可测量的数字。
    ///
    /// 为什么需要它：威胁值节奏、敌人血量手感、背包容量这三项只能靠玩才能判断，
    /// 但「凭感觉说太快/太慢」没法定位。这里在局内采样关键指标并写日志，
    /// 玩家玩完一局就能拿到数据，据此校准比反复试玩省事。
    ///
    /// 采样内容（对应 docs/gameplay-spec.md §4 §5 §6）：
    ///  - 威胁值时间曲线（每 5 秒一个点 + 每次清区时记录）
    ///  - 每类敌人的击杀耗时（从生成到死亡）
    ///  - 打开背包的次数与总时长
    ///  - 三选一出现次数、背包峰值占用
    ///
    /// 产物：`Application.persistentDataPath/run-telemetry.txt`
    /// 只在正常游玩时启用（自检不写，避免污染数据）。
    /// </summary>
    public sealed class RunTelemetry : MonoBehaviour
    {
        const float SampleInterval = 5f;
        const string FileName = "run-telemetry.txt";

        GameRoot _root;
        float _elapsed;
        float _sampleTimer;
        bool _enabled;

        // 敌人「生成时刻」表：用于算击杀耗时
        readonly Dictionary<Enemy, float> _spawnTime = new Dictionary<Enemy, float>();
        readonly Dictionary<string, List<float>> _killTimes = new Dictionary<string, List<float>>();

        struct ZoneMark
        {
            public int Zone;
            public float Time;
            public float Threat;
            public int Kills;
        }
        readonly List<ZoneMark> _zoneMarks = new List<ZoneMark>();
        readonly List<string> _threatCurve = new List<string>();

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
            _spawnTime[e] = _elapsed;
        }

        /// <summary>登记敌人死亡，记录该类敌人的击杀耗时。</summary>
        public void NoteKill(Enemy e)
        {
            if (!_enabled || e == null) return;
            if (!_spawnTime.TryGetValue(e, out float t0)) return;
            _spawnTime.Remove(e);

            float dt = _elapsed - t0;
            string key = e.Type.ToString();
            if (!_killTimes.TryGetValue(key, out var list))
            {
                list = new List<float>();
                _killTimes[key] = list;
            }
            list.Add(dt);
        }

        /// <summary>登记一次三选一（用于统计升级频率）。</summary>
        public void NoteOffer() => _offerCount++;

        /// <summary>登记一次区域清空。</summary>
        public void NoteZoneClear(int zone)
        {
            if (!_enabled || _root?.Run == null) return;
            _zoneMarks.Add(new ZoneMark
            {
                Zone = zone,
                Time = _elapsed,
                Threat = _root.Run.Threat,
                Kills = _root.Run.Kills,
            });
        }

        void Update()
        {
            if (!_enabled || _root?.Run == null) return;

            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;

            // 威胁值曲线
            _sampleTimer -= dt;
            if (_sampleTimer <= 0f)
            {
                _sampleTimer = SampleInterval;
                _threatCurve.Add($"{_elapsed:0.0}s  threat={_root.Run.Threat:0}  " +
                                 $"kills={_root.Run.Kills}  lv={_root.Run.Level}");
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
        public void Finish(bool won)
        {
            if (!_enabled || _root?.Run == null) return;
            _enabled = false;

            var sb = new StringBuilder();
            sb.AppendLine("===== 对局遥测 =====");
            sb.AppendLine($"结果: {(won ? "通关" : "失败")}");
            sb.AppendLine($"总时长: {_elapsed:0.0} 秒（{_elapsed / 60f:0.0} 分钟）");
            sb.AppendLine($"到达等级: {_root.Run.Level}   击杀: {_root.Run.Kills}");
            sb.AppendLine($"最终威胁: {_root.Run.Threat:0}");
            sb.AppendLine($"三选一次数: {_offerCount}");
            sb.AppendLine($"打开背包: {_bagOpenCount} 次，共 {_bagOpenTime:0.0} 秒" +
                          $"（占 {(_bagOpenTime / Mathf.Max(1f, _elapsed)) * 100f:0}%）");
            sb.AppendLine($"背包峰值占用: {_bagPeak}/{_root.Run.BagCapacity}");
            sb.AppendLine();

            sb.AppendLine("--- 区域清空时快照 ---");
            sb.AppendLine("区域  时间    威胁  击杀");
            foreach (var z in _zoneMarks)
                sb.AppendLine($"{z.Zone,4}  {z.Time,6:0.0}s  {z.Threat,4:0}  {z.Kills,4}");
            sb.AppendLine();

            sb.AppendLine("--- 威胁值曲线 ---");
            foreach (var line in _threatCurve) sb.AppendLine(line);
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

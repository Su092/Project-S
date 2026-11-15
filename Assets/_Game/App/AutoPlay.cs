using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using S.View;

namespace S.App
{
    /// <summary>
    /// 自动驾驶跑局：让 AI 自己玩一局，产出真实的节奏数据。
    ///
    /// 用途：威胁值节奏、敌人 TTK、等级曲线这三项只能靠「玩」才能测。
    /// 人工试玩成本高，所以让 AI 用与真人相同的输入管线跑一遍。
    ///
    /// 产物：`autoplay.log`（工程根）—— 含一局完整遥测与最终构筑。
    /// 用法：S.exe -autoplay -screen-width 1600 -screen-height 900 -screen-fullscreen 0
    /// </summary>
    public sealed class AutoPlay : MonoBehaviour
    {
        public static bool Requested =>
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autoplay") >= 0;

        GameRoot _root;
        S.View.AutoPilot _pilot;

        void Start()
        {
            if (!Requested) { enabled = false; return; }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            yield return null;
            yield return null;

            _root = S.View.GameRoot.Instance;
            if (_root == null) { Write("GameRoot 为 null"); yield break; }

            // 跳过菜单，直接开局（要测的是对局节奏，不是菜单）
            _root.ShowMenuOnStart = false;
            _root.StartRunForTest();

            _pilot = _root.gameObject.AddComponent<S.View.AutoPilot>();
            _pilot.Init(_root);

            // 等 AI 跑完（结算或超时）
            float guard = 0f;
            while (!_pilot.Finished && guard < 960f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;
            yield return null;

            Write(BuildReport());
            Application.Quit(0);
        }

        string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== AUTOPLAY =====");
            sb.AppendLine($"结果: {(_pilot.Won ? "通关" : "失败/超时")}");
            sb.AppendLine($"用时: {_pilot.Elapsed:0.0} 秒（{_pilot.Elapsed / 60f:0.0} 分钟）");
            sb.AppendLine($"到达等级: {_root.Run.Level}   击杀: {_root.Run.Kills}");
            sb.AppendLine($"最终威胁: {_root.Run.Threat:0}   时长: {_root.RunElapsed:000.0}s");
            sb.AppendLine($"剩余血量: {_root.Hp:0}/{_root.MaxHp:0}");

            sb.AppendLine();
            sb.AppendLine("--- 最终构筑 ---");
            if (_root.Weapons != null)
            {
                foreach (var g in _root.Weapons.AllGuns)
                {
                    sb.AppendLine($"[{g.Base.Name}]  槽 {g.Slots.Length}  读取 {g.Stats.CastCount}");
                    for (int i = 0; i < g.Slots.Length; i++)
                    {
                        var it = g.Slots.Cells[i];
                        if (it == null) { sb.AppendLine($"  槽{i + 1}: （空）"); continue; }
                        sb.AppendLine($"  槽{i + 1}: {it.Name}" +
                                      (it.IsBullet ? "" : $"（{ItemTooltip.ScopeName(it.Mod.Scope)}）"));
                    }
                }
            }
            sb.AppendLine($"背包剩余: {_root.Run.Bag.Count}/{_root.Run.BagCapacity}");

            var telPath = System.IO.Path.Combine(
                Application.persistentDataPath, "run-telemetry.txt");
            if (File.Exists(telPath))
            {
                sb.AppendLine();
                sb.AppendLine("--- 遥测详情 ---");
                sb.AppendLine(File.ReadAllText(telPath, Encoding.UTF8));
            }

            return sb.ToString();
        }

        void Write(string text)
        {
            var root = FindProjectRoot(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            var path = Path.Combine(root, "autoplay.log");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Debug.Log(text);
            Debug.Log("AUTOPLAY LOG AT " + path);
        }

        static string FindProjectRoot(string start)
        {
            var d = start;
            for (int i = 0; i < 6 && d != null; i++)
            {
                if (Directory.Exists(Path.Combine(d, "Assets"))) return d;
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
            }
            return start;
        }
    }
}

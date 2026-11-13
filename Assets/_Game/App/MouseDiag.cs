using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace S.App
{
    /// <summary>
    /// 编辑器内鼠标诊断。**只在编辑器的 Play 模式下**自动挂载，
    /// 把鼠标设备的真实状态每 0.5 秒追加写进 Logs/mouse-live.txt。
    ///
    /// 为什么需要它：本工程遇到「编辑器 Play 里键盘正常、鼠标完全无反应，
    /// 但打出来的 exe 一切正常」的现象。batchmode 里 Mouse.current 恒为 NULL，
    /// 复现不了；只有让玩家在编辑器里真实操作一次才能拿到证据。
    ///
    /// 它回答四个问题：
    ///   1. Unity 有没有把鼠标识别成设备（Mouse.current 是否 NULL）
    ///   2. 鼠标的坐标/位移/按键值到底有没有变化（是设备没数据，还是数据被丢掉）
    ///   3. 光标状态（visible / lockState）
    ///   4. EventSystem 的 UI 模块是否真的收到了 pointer 事件
    ///
    /// 打包版不会运行它（#if UNITY_EDITOR），不影响交付。
    /// </summary>
    public sealed class MouseDiag : MonoBehaviour
    {
#if UNITY_EDITOR
        const string OutPath = "Logs/mouse-live.txt";
        const float Interval = 0.5f;

        float _timer;
        int _tick;
        Vector2 _lastPos;
        Vector2 _lastDelta;
        int _lastButtons = -1;
        int _clickEvents;
        bool _hookInstalled;
        StringBuilder _sb;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            // 只在编辑器里装；打包版完全跳过。
            if (FindAnyObjectByType<MouseDiag>() != null) return;
            var go = new GameObject("__MouseDiag");
            DontDestroyOnLoad(go);
            go.AddComponent<MouseDiag>();
        }

        void Start()
        {
            _sb = new StringBuilder();
            _sb.AppendLine("=== 编辑器内鼠标实时诊断 ===");
            _sb.AppendLine($"启动时间 {System.DateTime.Now:HH:mm:ss}");
            _sb.AppendLine($"Mouse.current    = {(Mouse.current == null ? "NULL" : Mouse.current.displayName)}");
            _sb.AppendLine($"Keyboard.current = {(Keyboard.current == null ? "NULL" : Keyboard.current.displayName)}");
            _sb.AppendLine($"Pointer.current  = {(Pointer.current == null ? "NULL" : Pointer.current.displayName)}");

            // 设备清单：如果鼠标不在列表里，说明设备层就没接进来
            _sb.AppendLine($"InputSystem.devices 共 {InputSystem.devices.Count} 个：");
            foreach (var d in InputSystem.devices)
                _sb.AppendLine($"  {d.displayName,-26} {d.GetType().Name,-16} enabled={d.enabled}");

            var st = InputSystem.settings;
            _sb.AppendLine($"settings.editorInputBehaviorInPlayMode = {st.editorInputBehaviorInPlayMode}");
            _sb.AppendLine($"settings.backgroundBehavior            = {st.backgroundBehavior}");

            _sb.AppendLine("（在 Game 视图里移动鼠标、点击几下，然后停止 Play）");
            _sb.AppendLine();
            _sb.AppendLine("tick | time   | cursor(vis/lock) | mouse pos      | delta      | LMB | 变化 | 点击 | 射线");
            Flush();

            _lastPos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        }

        void Update()
        {
            // 捕获 UI 的 pointer 点击：如果这里一直是 0，说明 UI 模块根本没收到鼠标事件
            if (!_hookInstalled)
            {
                var es = EventSystem.current;
                if (es != null)
                {
                    var mod = es.GetComponent<InputSystemUIInputModule>();
                    if (mod != null) _hookInstalled = true;
                }
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < Interval) return;
            _timer = 0f;
            _tick++;

            var m = Mouse.current;
            string mousePart;
            string change = "";

            if (m == null)
            {
                mousePart = "NULL                                        ";
            }
            else
            {
                var pos = m.position.ReadValue();
                var delta = m.delta.ReadValue();
                int buttons = 0;
                if (m.leftButton.isPressed) buttons |= 1;
                if (m.rightButton.isPressed) buttons |= 2;

                if (pos != _lastPos) change += "pos ";
                if (delta != _lastDelta) change += "delta ";
                if (buttons != _lastButtons) change += $"btn({buttons}) ";
                if (m.leftButton.wasPressedThisFrame) _clickEvents++;

                _lastPos = pos;
                _lastDelta = delta;
                _lastButtons = buttons;

                mousePart = $"({pos.x,6:F0},{pos.y,6:F0}) ({delta.x,6:F1},{delta.y,6:F1}) {buttons}  ";
            }

            string rayInfo = "-";
            if (m != null && EventSystem.current != null)
            {
                var ped = new PointerEventData(EventSystem.current) { position = m.position.ReadValue() };
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, hits);
                rayInfo = hits.Count > 0 ? $"{hits.Count}个/{hits[0].gameObject.name}" : "0个";
            }

            _sb.AppendLine($"{_tick,4} | {Time.time,6:F1} | " +
                           $"{Cursor.visible}/{Cursor.lockState,-6} | {mousePart}| " +
                           $"{change,-14} | {_clickEvents,3} | {rayInfo}");

            if (_tick % 4 == 0) Flush();
        }

        void OnDestroy()
        {
            _sb?.AppendLine();
            _sb?.AppendLine($"=== 结束（共 {_tick} 次采样，捕获按下 {_clickEvents} 次）===");
            Flush();
        }

        void Flush()
        {
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(OutPath, _sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* 诊断失败不能影响游戏 */ }
        }
#endif
    }
}

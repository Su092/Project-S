using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace S.EditorTools
{
    /// <summary>
    /// 创建并持久化 InputSettings 资产，修正编辑器 Play 下的鼠标输入路由。
    ///
    /// 上一版只改了 `InputSystem.settings` 的内存对象：工程里**没有** .inputsettings 资产，
    /// 于是改动根本没落盘，重启编辑器就还原（踩过：日志显示改成功，磁盘上却什么都没有）。
    ///
    /// InputSettings 只认 `*.inputsettings.asset` 这类资产。这里显式创建一个，
    /// 写进目标值，并把工程指向它。
    ///
    /// 要修的两个默认值（都能造成「编辑器里键鼠不同命」）：
    ///   · editorInputBehaviorInPlayMode = PointersAndKeyboardsRespectGameViewFocus
    ///       → Game 视图无焦点时，**鼠标输入被路由给编辑器**，游戏收不到。
    ///         截图里 "Play Unfocused" 正是这种状态。
    ///   · backgroundBehavior = ResetAndDisableNonBackgroundDevices
    ///       → 失去焦点会**重置并禁用**设备，鼠标表现为彻底失灵。
    /// </summary>
    public static class SInputFix
    {
        const string AssetPath = "Assets/InputSystem.inputsettings.asset";

        [MenuItem("S/修复编辑器鼠标输入")]
        public static void ApplyMenu() => Apply(false);

        public static void ApplyBatch() => Apply(true);

        static void Apply(bool exit)
        {
            var settings = AssetDatabase.LoadAssetAtPath<InputSettings>(AssetPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<InputSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                AssetDatabase.CreateAsset(settings, AssetPath);
                Debug.Log($"[S] 已创建 {AssetPath}");
            }
            else
            {
                Debug.Log($"[S] 复用已有 {AssetPath}");
            }

            var beforeBehavior = settings.editorInputBehaviorInPlayMode;
            var beforeBg = settings.backgroundBehavior;

            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 让运行中的 InputSystem 立即采用这份资产
            if (InputSystem.settings != settings)
            {
                InputSystem.settings = settings;
                Debug.Log("[S] 已把工程设置指向该资产");
            }
            Debug.Log($"[S] editorInputBehaviorInPlayMode: {beforeBehavior} → {settings.editorInputBehaviorInPlayMode}");
            Debug.Log($"[S] backgroundBehavior:            {beforeBg} → {settings.backgroundBehavior}");

            // 落地校验：读回资产再比对**枚举值**。
            // 不能拿枚举名去搜文本 —— Unity 序列化枚举写的是整数
            // （上次就因此误判成「没落盘」，实际写的是 m_EditorInputBehaviorInPlayMode: 2）。
            var reloaded = AssetDatabase.LoadAssetAtPath<InputSettings>(AssetPath);
            bool ok = reloaded != null
                && reloaded.editorInputBehaviorInPlayMode
                    == InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView
                && reloaded.backgroundBehavior == InputSettings.BackgroundBehavior.IgnoreFocus;

            Debug.Log($"[S] 落盘校验：重新加载={(reloaded != null)}, " +
                      $"editorInputBehaviorInPlayMode={(reloaded != null ? reloaded.editorInputBehaviorInPlayMode.ToString() : "n/a")}, " +
                      $"backgroundBehavior={(reloaded != null ? reloaded.backgroundBehavior.ToString() : "n/a")}");

            // ---- 关闭 Touch Simulation ----
            // 这是「编辑器里鼠标彻底没反应」的真凶：
            // TouchSimulation 会把鼠标设备 **禁用**（TouchSimulation.cs:82
            // `InputSystem.DisableDevice(pointer, keepSendingEvents: true)`），
            // 然后把鼠标输入转成 Touchscreen 事件。实测日志证据：
            //   Mouse FastMouse enabled=False
            //   Pointer.current = Touchscreen
            //   mouse position 恒为 (0,0)、delta 恒为 (0,0)
            // 键盘不受影响，所以表现为「键盘能用、鼠标全废」——与用户现象完全一致。
            // 编辑器默认不会开它，一定是被误触（Input Debugger 里的开关）。
            bool touchOff = DisableTouchSimulation();
            ok = ok && touchOff;

            Debug.Log($"[S] 落盘校验结果={(ok ? "OK" : "FAIL")}（含 touchSimulation 关闭={touchOff}）");

            if (exit) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// 关闭编辑器的触摸模拟（它会禁用鼠标设备）。
        /// InputEditorUserSettings 是 internal，用反射访问；开关一经设置会写回
        /// Library/InputUserSettings.json，并在下次编辑器启动时继续生效。
        /// </summary>
        static bool DisableTouchSimulation()
        {
            var t = typeof(InputSystem).Assembly.GetType("UnityEngine.InputSystem.InputEditorUserSettings");
            if (t == null)
            {
                Debug.LogWarning("[S] 找不到 InputEditorUserSettings，无法关闭触摸模拟");
                return false;
            }

            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            var prop = t.GetProperty("simulateTouch", F);
            if (prop == null)
            {
                Debug.LogWarning("[S] 找不到 simulateTouch 属性");
                return false;
            }

            bool before = (bool)prop.GetValue(null);
            if (before)
            {
                prop.SetValue(null, false);
                Debug.Log("[S] simulateTouch: true → false（已关闭触摸模拟，鼠标设备会被重新启用）");
            }
            else
            {
                Debug.Log("[S] simulateTouch 本来就是 false");
            }

            // 双保险：即使偏好已是 false，也确保 TouchSimulation 没有在运行中
            var simType = typeof(InputSystem).Assembly.GetType("UnityEngine.InputSystem.EnhancedTouch.TouchSimulation");
            if (simType != null)
            {
                var inst = simType.GetProperty("instance", F)?.GetValue(null);
                if (inst is MonoBehaviour mb && mb.enabled)
                {
                    mb.enabled = false;
                    Debug.Log("[S] TouchSimulation 实例正在运行 → 已停用");
                }
            }

            // 关键：确认鼠标设备现在是否可用
            var mouse = Mouse.current;
            if (mouse != null)
                Debug.Log($"[S] Mouse.current = {mouse.displayName}, enabled={mouse.enabled}" +
                          (mouse.enabled ? "" : "  <-- 仍被禁用，需重启编辑器后才会恢复"));

            return !(bool)prop.GetValue(null);
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace S.View
{
    /// <summary>
    /// 输入设备守护：保证鼠标设备在游戏里**始终可用**。
    ///
    /// 为什么需要（实测踩到）：
    ///   在编辑器里按 Play 时出现 Mouse=FastMouse **enabled=False**、Pointer.current=Touchscreen、
    ///   鼠标位置与位移恒为 0 —— 键盘完好，鼠标全废，UI 点不到、视角转不了。
    ///   这是 Input System 的 EnhancedTouch **TouchSimulation** 干的：
    ///   它为了把鼠标事件转成触摸，会 `InputSystem.DisableDevice(mouse)` 并临时造一个 Touchscreen。
    ///
    /// 为什么不能只改偏好：`simulateTouch` 是编辑器用户偏好（Library/InputUserSettings.json），
    ///   属于**可再生缓存**，被重置、被别的编辑器实例覆盖、或上次 Play 残留的禁用状态，
    ///   都会让问题复现。仅靠改配置不足以保证玩家能操作。
    ///
    /// 本类只做一件事：每帧检查鼠标是否被禁用，被禁用就立即重新启用。
    ///   · 它是**幂等**的：设备正常时零开销（只读一个 bool）。
    ///   · 它是**安全**的：EnableDevice 对已启用的设备是空操作。
    ///   · 它覆盖打包版：即使播放器里出现同类禁用（如某些焦点切换路径），也能自愈。
    ///
    /// 这不是掩盖问题：真正的修复在工程配置（见 SInputFix / README「编辑器里鼠标失灵」），
    /// 这里是最后一道保险，确保「玩家一定会失去鼠标」这类不可接受的失败不会发生。
    /// </summary>
    public sealed class InputDeviceGuard : MonoBehaviour
    {
        static InputDeviceGuard s_instance;

        int _recovered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (s_instance != null) return;
            var go = new GameObject("[InputDeviceGuard]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<InputDeviceGuard>();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (!mouse.enabled) Recover(mouse);
                return;
            }

            // Mouse.current 为 null 时（设备被禁用会被置空），从设备表里找出鼠标重新启用
            foreach (var d in InputSystem.devices)
            {
                if (d is Mouse m && !m.enabled)
                {
                    Recover(m);
                    return;
                }
            }
        }

        void Recover(Mouse mouse)
        {
            InputSystem.EnableDevice(mouse);
            _recovered++;

            // 只在第一次打日志，避免每帧刷屏；同时把事实留在 Player.log 里便于事后定位
            if (_recovered == 1)
                Debug.LogWarning($"[S] 检测到鼠标设备被禁用（{mouse.displayName}），已自动恢复。" +
                                 "常见原因是 Input System 的 Touch Simulation —— " +
                                 "见 README「编辑器里鼠标失灵」。");
        }

        /// <summary>自检用：本局一共恢复过几次（>0 说明环境里存在禁用鼠标的因素）。</summary>
        public static int RecoveredCount => s_instance != null ? s_instance._recovered : 0;
    }
}

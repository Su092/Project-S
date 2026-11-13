using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace S.EditorTools
{
    /// <summary>
    /// 查清「鼠标设备为什么是 enabled=False」。
    ///
    /// 已有事实：Play 时 Mouse=FastMouse enabled=False、Pointer.current=Touchscreen、
    /// 键盘正常。而 simulateTouch 偏好已是 false，工程里也没有 TouchSimulation 组件。
    ///
    /// 因此禁用来源只可能是这几处，本探针逐一取证：
    ///   1. **TouchSimulation 实例**是否存在于场景/运行时（含 enabled=false 但已禁用过设备的情形）
    ///   2. 设备的 **disable scope**：是被前台临时禁用（焦点），还是被彻底禁用
    ///   3. `InputSettings.supportedDevices` 是否把鼠标过滤掉
    ///   4. 是否有 `VirtualMouseInput`（会 DisableDevice(m_SystemMouse)）
    ///   5. Touchscreen 是谁创建的：如果设备里有个 Touchscreen 但工程没用触摸，
    ///      那它一定是 TouchSimulation 造出来的
    /// </summary>
    [InitializeOnLoad]
    public static class MouseDisableRootCause
    {
        const string OutPath = "Logs/mouse-rootcause.txt";
        const string PendingKey = "S.MouseRootCause.Pending";
        const int Frames = 20;

        static int s_frames;

        static MouseDisableRootCause()
        {
            if (EditorPrefs.GetBool(PendingKey, false))
            {
                EditorApplication.update -= OnUpdate;
                EditorApplication.update += OnUpdate;
            }
        }

        public static void ProbeBatch()
        {
            EditorPrefs.SetBool(PendingKey, true);
            s_frames = 0;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath);
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorApplication.EnterPlaymode();
        }

        static void OnUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            if (++s_frames < Frames) return;

            EditorApplication.update -= OnUpdate;
            EditorPrefs.SetBool(PendingKey, false);

            var sb = new StringBuilder();
            sb.AppendLine("=== 鼠标被禁用的根因取证 ===");

            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

            // ---- 1. TouchSimulation 实例 ----
            var simType = typeof(InputSystem).Assembly.GetType(
                "UnityEngine.InputSystem.EnhancedTouch.TouchSimulation");
            sb.AppendLine();
            sb.AppendLine("[1] TouchSimulation");
            if (simType == null) sb.AppendLine("  类型未找到");
            else
            {
                var inst = simType.GetProperty("instance", F)?.GetValue(null) as MonoBehaviour;
                sb.AppendLine($"  instance = {(inst == null ? "null" : inst.name)}" +
                              (inst != null ? $"  enabled={inst.enabled}  activeInHierarchy={inst.gameObject.activeInHierarchy}" : ""));

                // 场景里所有 TouchSimulation 组件（含未启用）
                var all = Object.FindObjectsByType(simType, FindObjectsInactive.Include) as MonoBehaviour[];
                sb.AppendLine($"  场景中组件数 = {(all == null ? 0 : all.Length)}");
                if (all != null)
                    foreach (var c in all)
                        sb.AppendLine($"    {c.name}: enabled={c.enabled} active={c.gameObject.activeInHierarchy}");

                // 它记录的「被我禁用的指针」列表 —— 这是最直接的证据
                if (inst != null)
                {
                    var fld = simType.GetField("m_Pointers", F);
                    var ptrs = fld?.GetValue(inst) as System.Collections.IList;
                    if (ptrs != null)
                    {
                        sb.AppendLine($"  它禁用过的指针数 = {ptrs.Count}");
                        foreach (var p in ptrs)
                            if (p is InputDevice d)
                                sb.AppendLine($"    {d.displayName} enabled={d.enabled}  <-- 被 TouchSimulation 禁用");
                    }
                }
            }

            // ---- 2. 设备与禁用原因 ----
            sb.AppendLine();
            sb.AppendLine("[2] 设备清单");
            foreach (var d in InputSystem.devices)
            {
                sb.AppendLine($"  {d.displayName,-14} {d.GetType().Name,-18} enabled={d.enabled} " +
                              $"added={d.added} native={d.native}");

                // 读 manager 内部的禁用计数
                var mgr = typeof(InputSystem).GetProperty("manager", F)?.GetValue(null);
                if (mgr != null)
                {
                    var devicesField = mgr.GetType().GetField("m_Devices", F);
                    var arr = devicesField?.GetValue(mgr) as System.Collections.IList;
                    if (arr != null)
                    {
                        foreach (var entry in arr)
                        {
                            if (entry == null) continue;
                            var devField = entry.GetType().GetField("device", F);
                            var dev = devField?.GetValue(entry) as InputDevice;
                            if (dev != d) continue;

                            foreach (var f in entry.GetType().GetFields(F))
                            {
                                if (f.Name.Contains("isable") || f.Name.Contains("disabled"))
                                {
                                    var v = f.GetValue(entry);
                                    if (v is int i && i != 0)
                                        sb.AppendLine($"      {f.Name} = {i}  <-- 非零表示有禁用计数");
                                }
                            }
                        }
                    }
                }
            }

            // ---- 3. supportedDevices ----
            sb.AppendLine();
            sb.AppendLine("[3] supportedDevices");
            var sd = InputSystem.settings.supportedDevices;
            sb.AppendLine($"  数量={sd.Count}" + (sd.Count == 0 ? "（空=不过滤）" : ""));
            foreach (var s in sd) sb.AppendLine($"    {s}");

            // ---- 4. VirtualMouseInput ----
            sb.AppendLine();
            sb.AppendLine("[4] VirtualMouseInput 组件");
            var vmType = typeof(InputSystem).Assembly.GetType(
                "UnityEngine.InputSystem.UI.VirtualMouseInput");
            if (vmType == null) sb.AppendLine("  类型未找到");
            else
            {
                var vms = Object.FindObjectsByType(vmType, FindObjectsInactive.Include);
                sb.AppendLine($"  场景中组件数 = {vms.Length}" + (vms.Length > 0 ? "  <-- 它会禁用系统鼠标" : ""));
                foreach (var v in vms)
                    sb.AppendLine($"    {v.name}");
            }

            // ---- 5. 关键判定 ----
            sb.AppendLine();
            sb.AppendLine("[5] 判定");
            var mouse = Mouse.current;
            var ts = Touchscreen.current;
            sb.AppendLine($"  Mouse.current       = {(mouse == null ? "NULL" : $"enabled={mouse.enabled}")}");
            sb.AppendLine($"  Touchscreen.current = {(ts == null ? "NULL" : "存在")}");
            if (mouse != null && !mouse.enabled)
                sb.AppendLine("  结论：鼠标被禁用" +
                    (ts != null ? "，且存在 Touchscreen ⇒ 触摸模拟仍在工作" : "，但无 Touchscreen ⇒ 另有来源"));
            else
                sb.AppendLine("  结论：鼠标可用");

            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText(OutPath, sb.ToString());
            Debug.Log(sb.ToString());
            Debug.Log("ROOTCAUSE DONE");

            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}

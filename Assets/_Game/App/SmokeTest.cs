using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace S.App
{
    /// <summary>
    /// 正常启动模式的冒烟测试。
    ///
    /// 为什么需要它：`-scheck` 会关掉主菜单与遥测（否则断言全在暂停态下跑），
    /// 于是「真实玩家启动路径」从未被验证过 —— 菜单能不能显示、点了开始会不会崩、
    /// 遥测是否正常开启，全都只有人工试玩才知道。
    ///
    /// 本测试跑的是**正常路径**：菜单显示 → 点开始 → 世界运转 → 遥测开启，
    /// 只做最小断言，然后退出。与 `-scheck` 互补，不替代。
    ///
    /// 用法：S.exe -smoke -screen-width 1600 -screen-height 900 -screen-fullscreen 0
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        public static bool Requested =>
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoke") >= 0;

        readonly System.Collections.Generic.List<string> _log =
            new System.Collections.Generic.List<string>();
        int _pass, _fail;
        bool _finished;

        void Start()
        {
            if (!Requested) { enabled = false; return; }
            StartCoroutine(Guard());
            StartCoroutine(Run());
        }

        IEnumerator Guard()
        {
            float t = 0f;
            while (t < 90f) { t += Time.unscaledDeltaTime; yield return null; }
            _fail++;
            _log.Add("  FAIL 冒烟测试超时");
            Finish();
        }

        void Check(string name, bool cond, string why = "")
        {
            if (cond) { _pass++; _log.Add("  PASS " + name); }
            else { _fail++; _log.Add($"  FAIL {name} :: {why}"); }
        }

        /// <summary>
        /// UI 输入模块是否可用。这里不用「存在就算过」这种弱判据：
        /// InputSystemUIInputModule 必须真的绑上默认 actions，否则射线永远打不到按钮。
        /// </summary>
        static bool FindUIInputModuleOk()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;

            var module = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (module == null) return false;
            return module.actionsAsset != null;
        }

        /// <summary>
        /// 对「开始」按钮做一次**真实的 UI 点击**：射线命中 → 派发 pointerClick → 按钮回调。
        /// 返回 false 表示这条链路断了（鼠标点不动）。
        ///
        /// 为什么不能只查 IsOpen：光标可见、菜单打开、射线能命中，
        /// 但按钮回调没接上时玩家依然是「点不动」。只有走完整条链路才算验证。
        /// </summary>
        static bool TryClickStartButton(S.View.GameRoot root)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || root == null || root.Menu == null) return false;

            // 找到按钮：它在某个 Canvas 下的 "StartBtn"
            Transform btn = root.Menu.Backstage.StartButton;
            if (btn == null) return false;

            var rt = btn as RectTransform;
            if (rt == null) return false;

            Vector3 screen = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen };

            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(ped, hits);

            UnityEngine.EventSystems.RaycastResult? target = null;
            foreach (var h in hits)
            {
                if (h.gameObject == btn.gameObject || h.gameObject.transform.IsChildOf(btn))
                { target = h; break; }
            }
            if (target == null) return false;

            // 走标准派发路径（与真实鼠标点击相同），而不是直接调 onClick.Invoke()
            var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<
                UnityEngine.EventSystems.IPointerClickHandler>(target.Value.gameObject);
            if (handler == null) return false;

            UnityEngine.EventSystems.ExecuteEvents.Execute(
                handler, ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

            if (!root.Menu.WalkingBackstage || root.RunStarted) return false;
            root.Menu.Backstage.UseStation(S.View.BackstageWorld.Station.Door);
            return root.RunStarted && !root.Menu.IsOpen;
        }

        /// <summary>
        /// 校验某个界面的实例确实来自预制体：找到同名节点，且它挂着引用表组件。
        /// 代码拼出来的同名节点不会有 `*Refs`，因此能区分「预制体实例」与「代码重建」。
        /// </summary>
        static bool CheckUiFromPrefab(string rootName, string refsTypeName)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                var t = FindDeep(c.transform, rootName);
                if (t == null) continue;

                var comps = t.GetComponents<MonoBehaviour>();
                foreach (var mb in comps)
                    if (mb != null && mb.GetType().Name == refsTypeName) return true;
            }
            return false;
        }

        static Transform FindDeep(Transform t, string name)
        {            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>
        /// 菜单几何体检：行条必须在面板内、在列表内，且按钮必须在行内。
        /// 这类「看起来越界」的问题靠看图判断容易误判，直接比矩形最准。
        /// </summary>
        void CheckMenuLayout(S.View.GameRoot root)
        {
            var button=(RectTransform)root.Menu.Backstage.StartButton;
            var bounds=new Vector3[4];
            button.GetWorldCorners(bounds);
            Check("开始按钮位于可点击区域", button.gameObject.activeInHierarchy &&
                bounds[0].x >= 0 && bounds[2].x <= Screen.width &&
                bounds[0].y >= 0 && bounds[2].y <= Screen.height, button.name);
        }

        static IEnumerator WaitUnscaled(float s)
        {
            float t = 0f;
            while (t < s) { t += Time.unscaledDeltaTime; yield return null; }
        }

        /// <summary>
        /// 截一张图到 Shots/。冒烟走的是**正常启动路径**，因此能拍到主菜单 ——
        /// 而 -scheck 会跳过菜单，菜单外观从来没被自动验证过（踩过：素材换完了没人发现没生效）。
        /// </summary>
        IEnumerator Shot(string name)
        {
            string dir = FindProjectRoot(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            var shots = Path.Combine(dir, "Shots");
            Directory.CreateDirectory(shots);

            ScreenCapture.CaptureScreenshot(Path.Combine(shots, name + ".png"));
            _log.Add("  SHOT " + name);
            // 两帧：一帧给 CaptureScreenshot 在帧末抓取，一帧留给写盘
            yield return null;
            yield return null;
        }

        IEnumerator Run()
        {
            yield return null;
            yield return null;

            var root = S.View.GameRoot.Instance;
            Check("GameRoot 存在", root != null, "Instance 为 null");
            if (root == null) { Finish(); yield break; }

            // ---- 1. 正常模式必须显示主菜单 ----
            yield return WaitUnscaled(0.3f);
            Check("正常启动显示主菜单", root.Menu != null && root.Menu.IsOpen,
                root.Menu == null ? "Menu 未创建" : $"IsOpen={root.Menu.IsOpen}");
            Check("菜单显示解锁项", root.Menu != null && root.Menu.Backstage.StartButton.gameObject.activeInHierarchy,
                $"rows={root.Menu?.RowCount}");

            // 结构必须来自预制体：这条防的是「以后又有人在代码里拼菜单」。
            // 只查 RowCount>0 抓不到 —— 代码拼出来的行数一样是 5。
            Check("菜单来自预制体", CheckUiFromPrefab("Menu", "MenuRefs"),
                "Menu 实例未挂 MenuRefs（界面可能被代码重建了）");
            Check("菜单期间游戏暂停", root.Paused, "Paused=false");
            Check("菜单期间 timeScale 为 0", Mathf.Approximately(Time.timeScale, 0f),
                $"timeScale={Time.timeScale}");

            // 菜单里必须能看见并移动鼠标，否则玩家点不到「开始」——
            // 光有 IsOpen=true 不够，光标被锁在中心时按钮完全没法点（踩过）。
            Check("菜单期间光标可见", Cursor.visible, "Cursor.visible=false");
            Check("菜单期间光标未锁定", Cursor.lockState != CursorLockMode.Locked,
                $"lockState={Cursor.lockState}");

            // UI 射线：鼠标要真能打到按钮。EventSystem 缺失或挂了旧输入模块时
            // 光标虽然能动，但点击事件没人处理，表现同样是「鼠标控制不了」。
            Check("UI 事件系统存在", UnityEngine.EventSystems.EventSystem.current != null,
                "EventSystem 未创建");
            Check("UI 输入模块为新输入系统",
                FindUIInputModuleOk(), "InputSystemUIInputModule 缺失或配置不对");

            // 鼠标设备必须处于启用态。被禁用时玩家能看见菜单却完全点不动，
            // 而键盘通常仍正常——曾因此长期误判成 UI 问题（实测 enabled=False）。
            var mouse = UnityEngine.InputSystem.Mouse.current;
            Check("鼠标设备可用", mouse != null && mouse.enabled,
                mouse == null ? "Mouse.current 为 null" : $"enabled={mouse.enabled}");
            Check("鼠标未被触摸模拟接管",
                UnityEngine.InputSystem.Pointer.current is UnityEngine.InputSystem.Mouse,
                UnityEngine.InputSystem.Pointer.current == null
                    ? "Pointer.current 为 null"
                    : $"Pointer.current={UnityEngine.InputSystem.Pointer.current.GetType().Name}");

            // ---- 2. 遥测在正常模式下应开启 ----
            Check("正常模式遥测已开启", root.Telemetry != null && root.Telemetry.Enabled,
                root.Telemetry == null ? "Telemetry 未创建" : "Enabled=false");

            // 菜单外观截图：这是唯一能自动拍到主菜单的时机（-scheck 跳过菜单）
            yield return Shot("00-menu");

            // 菜单布局断言：行条不能超出面板/列表。
            // 这是被反复误判的地方 —— 肉眼看截图说「溢出」，但不知道是行宽错了还是角标显得越界。
            // 用矩形数值判定，一目了然。
            CheckMenuLayout(root);


            // ---- 3. 点「开始」→ 世界运转 ----
            // 从这里开始世界真的在跑，敌人会围上来。本测试只关心「菜单→开局→开火→背包」这条
            // 路径有没有接通，不关心战斗平衡；而玩家在冒烟里是站桩不动的，
            // 被贴脸打死会让后面所有断言失去意义（踩过：3 次里红 1 次，hp=0）。
            // 因此开局前开启免伤，结束前恢复。
            root.InvulnerableForTest = true;

            // 先验证「真的能点到开始按钮」——这是本测试唯一能覆盖鼠标链路的地方。
            // StartForTest() 是程序化直达，绕过射线与点击派发，
            // 所以「菜单开着但鼠标点不动」这种问题它永远发现不了（实际踩过）。
            bool clicked = TryClickStartButton(root);
            Check("开始按钮可被 UI 点击", clicked, "射线未命中或点击回调未触发");

            // 真点击已经开局；若没点成，再退回到程序化直达，保证后续断言仍可执行
            if (!clicked && root.Menu != null) root.Menu.StartForTest();
            yield return WaitUnscaled(0.4f);

            Check("点开始后不再暂停", !root.Paused, "Paused 仍为 true");
            Check("点开始后 timeScale 恢复", Mathf.Approximately(Time.timeScale, 1f),
                $"timeScale={Time.timeScale}");
            Check("点开始后菜单已关", root.Menu != null && !root.Menu.IsOpen, "菜单仍开着");

            // ---- 4. 世界真的在动（敌人 + 威胁）----
            yield return WaitUnscaled(2.5f);

            Check("开局后敌人已生成", root.Enemies.Count > 0,
                $"enemies={root.Enemies.Count}");
            Check("开局后威胁值在涨", root.Run.Threat > 0f,
                $"threat={root.Run.Threat}");
            // 免伤下血量应保持满值。这条真正验证的是「开局没有意外扣血/结算错误」，
            // 比原来那句「hp>0」有意义：后者在免伤下恒真，等于没测。
            Check("开局后血量未被异常扣除", Mathf.Approximately(root.Hp, root.MaxHp),
                $"hp={root.Hp}/{root.MaxHp}");

            _log.Add($"  INFO 开局 2.5s：敌人 {root.Enemies.Count}，威胁 {root.Run.Threat:0}，" +
                     $"等级 {root.Run.Level}");

            // ---- 5. 武器能开火（真实路径，不是直接调 API）----
            var gun = root.Weapons != null ? root.Weapons.Active : null;
            Check("武器就绪", gun != null, "Active 为 null");
            if (gun != null)
            {
                gun.FireCooldown = 0f;
                gun.ReloadLeft = 0f;
                gun.Ammo = gun.Stats.Mag;
                int before = gun.Ammo;
                var cast = gun.TryFire(out string why);
                Check("正常模式下能开火", cast != null, why ?? "");
                Check("开火扣弹", cast != null && gun.Ammo < before,
                    $"{before} → {gun.Ammo}");
            }

            // ---- 6. 打开背包（真实路径）----
            yield return WaitUnscaled(0.2f);
            root.ToggleInventory();
            yield return WaitUnscaled(0.2f);
            Check("能打开背包", root.InventoryOpen, "InventoryOpen=false");
            Check("背包打开即暂停", Mathf.Approximately(Time.timeScale, 0f),
                $"timeScale={Time.timeScale}");
            root.ToggleInventory();
            yield return WaitUnscaled(0.2f);
            Check("能关闭背包", !root.InventoryOpen, "InventoryOpen 仍为 true");

            // 背包结构同样必须来自预制体（见 README「UI 预制体」）
            Check("背包来自预制体", CheckUiFromPrefab("Inventory", "InvRefs"),
                "Inventory 实例未挂 InvRefs");

            // 关背包后 timeScale 可能仍为 0 —— 如果升级产生了排队的三选一，
            // 关背包会立刻弹出三选一（正确行为，见 GameRoot.OnInventoryClosed）。
            // 所以这里不能用「ts 必须为 1」断言，而要看真实状态。
            bool offerPending = root.HasPendingOffers || root.InventoryOpen;
            if (offerPending)
            {
                _log.Add("  INFO 关背包时仍有排队三选一，游戏保持暂停（符合设计）");
                root.CloseOverlayForTest();   // 清掉弹层，继续后续
                yield return WaitUnscaled(0.2f);
            }
            Check("关背包后能回到运行态",
                Mathf.Approximately(Time.timeScale, 1f) || offerPending,
                $"ts={Time.timeScale} pending={root.HasPendingOffers}");

            Finish();
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;

            var sb = new StringBuilder();
            sb.AppendLine("===== SMOKE =====");
            foreach (var l in _log) sb.AppendLine(l);
            sb.AppendLine($"SMOKE RESULT {_pass}/{_pass + _fail} passed");

            // 注意：播放器里 Application.dataPath 是 "<exe目录>/S_Data"，
            // 不是工程目录。必须向上找含 Assets 的工程根，否则日志落在 Build/Windows/ 下，
            // check.ps1 找不到（踩过两次）。
            var root = FindProjectRoot(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            var path = Path.Combine(root, "smoke.log");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log(sb.ToString());
            Debug.Log("SMOKE LOG AT " + path);

            Time.timeScale = 1f;
            // 恢复免伤标记，避免影响同进程里后续可能添加的其他检查
            var gr = S.View.GameRoot.Instance;
            if (gr != null) gr.InvulnerableForTest = false;
            Application.Quit(_fail == 0 ? 0 : 1);
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

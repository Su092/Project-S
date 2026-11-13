using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// HUD。结构来自 `Resources/UI/Prefabs/HUD.prefab`，本类只负责刷新数值。
    ///
    /// 为什么改成预制体：HUD 有二十多个元素，以前全靠 `AddComponent` + 像素偏移拼接，
    /// 想挪一条血条就得改代码重编译。现在锚点/尺寸/九宫格都在预制体里，
    /// 微调直接在 Prefab 模式下做，且改完不需要动脚本。
    ///
    /// 规格见 docs/gameplay-spec.md §9：轮转序列条与准星散射可视化是本作必需项。
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        GameRoot _root;

        Canvas _canvas;
        HudRefs _refs;

        readonly List<Image> _rotationPips = new List<Image>();
        readonly List<Text> _rotationLabels = new List<Text>();
        float _toastLeft;
        float _vignetteLeft;
        bool _endAwaitingRestart;

        readonly List<FloatText> _floats = new List<FloatText>();

        sealed class FloatText
        {
            public Text Label;
            public Vector3 World;
            public float Life;
        }

        public void Init(GameRoot root)
        {
            _root = root;
            BuildCanvas();

            var inst = UiPrefab.Spawn(UiPrefab.Hud, _canvas.transform);
            _refs = inst != null ? inst.GetComponent<HudRefs>() : null;
            if (_refs == null)
                Debug.LogError("[S] HUD.prefab 缺少 HudRefs（引用表未配置）");

            if (_refs != null)
            {
                _rotationPips.AddRange(_refs.RotationPips);
                _rotationLabels.AddRange(_refs.RotationLabels);
            }
        }

        void BuildCanvas()
        {
            var go = new GameObject("HUDCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }

        /// <summary>浮动伤害数字用的临时文本样式（每次伤害只有一格，不需要预制体）。</summary>
        static Font UiFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }

        // ---------------- 更新 ----------------

        void Update()
        {
            if (_root == null || _refs == null || _root.Data == null || _root.Run == null) return;
            var r = _root.Run;
            var g = _root.Weapons != null ? _root.Weapons.Active : null;

            // 生命
            float hpPct = Mathf.Clamp01(_root.Hp / Mathf.Max(1f, _root.MaxHp));
            if (_refs.HpFill != null)
            {
                _refs.HpFill.fillAmount = hpPct;
                _refs.HpFill.color = hpPct > 0.5f ? new Color(0.85f, 0.24f, 0.28f)
                    : hpPct > 0.25f ? new Color(0.95f, 0.55f, 0.2f)
                    : new Color(1f, 0.2f, 0.2f);
            }
            if (_refs.HpText != null) _refs.HpText.text = $"{_root.Hp:0} / {_root.MaxHp:0}";

            // 弹药与枪
            if (g != null)
            {
                if (_refs.AmmoText != null)
                {
                    _refs.AmmoText.text = g.Reloading
                        ? "装填…"
                        : $"{g.Ammo} / {g.Resolved.MagSize}";
                    _refs.AmmoText.color = g.Ammo <= 2 && !g.Reloading
                        ? new Color(1f, 0.45f, 0.35f) : Color.white;
                }
                if (_refs.GunText != null)
                    _refs.GunText.text = $"{g.Resolved.Name}   {1f / Mathf.Max(0.01f, g.Resolved.FireInterval):0.0} 发/秒";

                // 该位有效伤害
                if (_refs.DpsText != null)
                {
                    int peek = g.PeekNextSlot();
                    if (peek >= 0)
                    {
                        var cast = Loadout.ResolveCast(g.Resolved, g.Slots[peek]);
                        float dps = Loadout.SlotDps(g.Resolved, cast);
                        _refs.DpsText.text = $"下一发期望 {cast.ExpectedDamage:0.0}  ·  位 DPS {dps:0}";
                    }
                    else _refs.DpsText.text = "空链";
                }
            }

            UpdateRotation(g);
            UpdateChain(g);

            // 经验
            int need = RunState.NeedFor(r.Level);
            if (_refs.XpFill != null)
                _refs.XpFill.fillAmount = r.ReachedMaxLevel
                    ? 1f
                    : Mathf.Clamp01((float)r.Xp / Mathf.Max(1, need));
            if (_refs.LevelText != null)
                _refs.LevelText.text = $"LV {r.Level}   XP {r.Xp}/{need}   击杀 {r.Kills}";

            // 威胁
            if (_refs.ThreatFill != null)
            {
                _refs.ThreatFill.fillAmount = Mathf.Clamp01(r.Threat / RunState.ThreatMax);
                _refs.ThreatFill.color = r.Threat >= 88f ? new Color(1f, 0.2f, 0.2f)
                    : r.Threat >= 65f ? new Color(1f, 0.55f, 0.2f)
                    : r.Threat >= 35f ? new Color(0.95f, 0.75f, 0.3f)
                    : new Color(0.55f, 0.6f, 0.7f);
            }
            if (_refs.ThreatText != null) _refs.ThreatText.text = $"威胁 {r.Threat:0}";

            UpdateZoneBar();
            UpdateBossBar();
            UpdateCrosshair(g);

            // toast 淡出
            if (_toastLeft > 0f && _refs.Toast != null)
            {
                _toastLeft -= Time.unscaledDeltaTime;
                var c = _refs.Toast.color;
                c.a = Mathf.Clamp01(_toastLeft / 0.6f);
                _refs.Toast.color = c;
            }

            // 受击红晕
            if (_vignetteLeft > 0f && _refs.DamageVignette != null)
            {
                _vignetteLeft -= Time.unscaledDeltaTime;
                var c = _refs.DamageVignette.color;
                c.a = Mathf.Clamp01(_vignetteLeft / 0.5f) * 0.35f;
                _refs.DamageVignette.color = c;
            }

            UpdateFloats();
        }

        void UpdateRotation(GunRuntime g)
        {
            int slots = g != null ? g.Slots.Length : 0;
            for (int i = 0; i < _rotationPips.Count; i++)
            {
                var pip = _rotationPips[i];
                if (pip == null) continue;
                bool active = g != null && i < slots;
                pip.gameObject.SetActive(active);
                if (!active) continue;

                bool hasChain = !g.Slots[i].IsEmpty;
                int bullets = g.Slots[i].BulletCount;
                bool isNext = g.PeekNextSlot() == i;

                pip.color = !hasChain
                    ? new Color(0.14f, 0.16f, 0.2f, 0.7f)
                    : isNext
                        ? new Color(0.95f, 0.75f, 0.25f, 0.95f)
                        : new Color(0.25f, 0.32f, 0.42f, 0.9f);

                var lbl = i < _rotationLabels.Count ? _rotationLabels[i] : null;
                if (lbl != null)
                {
                    lbl.text = hasChain ? $"{i + 1}\n{bullets}弹" : $"{i + 1}\n空";
                    lbl.fontSize = 15;
                    lbl.color = hasChain ? Color.white : new Color(0.5f, 0.55f, 0.6f);
                }
            }
        }

        /// <summary>
        /// 链式触发的可视化（本次需求核心）：
        /// 把「下一发的格位链」按链顺序排成一排圆形配件图标，触发过的点亮 + 序号，
        /// 尚未触发的压暗，正在等待的那一项描金边。
        ///
        /// 为什么按「下一发」而不是「正在跑的那一发」来画：
        ///   链总时长 = 链长 × 击发间隔，最短也有 0.3s 左右，长的接近 1s；
        ///   如果只在施法途中显示，玩家在射速快的枪上几乎看不到，等于没画。
        ///   常驻显示「下一发会发生什么」+ 施法中实时点亮，两者结合才既稳定又有反馈。
        ///
        /// 数据来源必须是 ChainCast（真实触发顺序），不能再算一遍 ResolveCast：
        /// 两处各算一遍，迟早会因为配件顺序/作用域判据漂移而对不上，HUD 就成了假话。
        /// </summary>
        void UpdateChain(GunRuntime g)
        {
            if (_refs == null || _refs.ChainIcons == null) return;

            var pw = _root.Weapons;
            var cast = pw != null ? pw.PendingCast : null;

            // 施法进行中优先展示那一条（玩家正在看的反馈）；否则展示下一发的链。
            // 「正在跑」判据必须用 ChainCast 的 Finished 而不是「有没有子弹在飞」：
            // 链跑完后子弹还会飞一段，那段时间里已经触发过的配件应保持点亮，
            // 这样玩家能在整次飞行期间对照「这一发打了什么」。
            bool live = cast != null && cast.Steps.Count > 0;
            SlotState slot;
            if (live) slot = cast.Slot;
            else
            {
                int peek0 = g != null ? g.PeekNextSlot() : -1;
                slot = peek0 >= 0 ? g.Slots[peek0] : null;
            }

            int shownSlot = -1;
            if (g != null && slot != null)
                for (int i = 0; i < g.Slots.Length; i++)
                    if (ReferenceEquals(g.Slots[i], slot)) { shownSlot = i; break; }

            var chain = slot != null ? slot.Chain : null;
            int n = chain != null ? Mathf.Min(chain.Count, _refs.ChainIcons.Length) : 0;

            for (int i = 0; i < _refs.ChainIcons.Length; i++)
            {
                var icon = _refs.ChainIcons[i];
                if (icon == null) continue;

                if (i >= n) { icon.gameObject.SetActive(false); continue; }

                var item = chain[i];
                if (item == null) { icon.gameObject.SetActive(false); continue; }
                if (icon.Item == null || !ReferenceEquals(icon.Item, item))
                {
                    icon.Bind(item, 40f, showRing: true, badge: (i + 1).ToString(), showScope: true);
                    EnsureHover(icon, item);
                }
                else if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);

                // 触发状态：按 ChainCast 的已触发步数判断
                bool fired = false, next = false;
                if (live)
                {
                    var steps = cast.Steps;
                    // 找到本链项在计划里的步下标（枪械作用域件不占步，视为"已生效"）
                    int stepIdx = -1;
                    for (int s = 0; s < steps.Count; s++)
                        if (steps[s].ChainIndex == i) { stepIdx = s; break; }
                    fired = stepIdx < 0 || stepIdx < cast.FiredCount;
                    next = stepIdx == cast.FiredCount;
                }

                // 颜色：施法中才区分"已触发/正在触发/未触发"；
                // 常态（预览下一发）必须全部可读 —— 全部压暗会让玩家以为这一排是空的。
                Color col, ringCol, badgeCol;
                if (!live)
                {
                    col = new Color(0.95f, 0.97f, 1f);
                    ringCol = new Color(0.68f, 0.76f, 0.88f, 0.9f);
                    badgeCol = new Color(0.82f, 0.88f, 0.95f);
                }
                else if (fired)
                {
                    col = Color.white;
                    ringCol = new Color(1f, 0.92f, 0.7f, 0.95f);
                    badgeCol = new Color(1f, 0.95f, 0.8f);
                }
                else if (next)
                {
                    col = new Color(1f, 0.92f, 0.62f);
                    ringCol = new Color(1f, 0.85f, 0.4f, 0.98f);
                    badgeCol = new Color(1f, 0.9f, 0.5f);
                }
                else
                {
                    col = new Color(0.45f, 0.5f, 0.58f, 0.85f);
                    ringCol = new Color(0.4f, 0.46f, 0.55f, 0.6f);
                    badgeCol = new Color(0.52f, 0.58f, 0.66f);
                }

                if (icon.Icon != null) icon.Icon.color = col;
                if (icon.Ring != null) icon.Ring.color = ringCol;
                if (icon.Badge != null) icon.Badge.color = badgeCol;
            }

            if (_refs.ChainTitle != null)
            {
                if (n == 0) _refs.ChainTitle.text = "";
                else if (live)
                    _refs.ChainTitle.text =
                        $"链触发 {cast.FiredCount}/{cast.StepCount}" +
                        (shownSlot >= 0 ? $"   位 {shownSlot + 1}" : "");
                else
                    _refs.ChainTitle.text =
                        $"下一发{(shownSlot >= 0 ? $" 位 {shownSlot + 1}" : "")}   {n} 项";
            }
        }

        /// <summary>把链图标绑到当前配件上，并刷新悬停详情的数据源。</summary>
        static void EnsureHover(ItemIconView icon, ChainItem item)
        {
            if (icon == null) return;
            var hover = icon.GetComponent<ItemHover>();
            if (hover == null) hover = icon.gameObject.AddComponent<ItemHover>();
            hover.Item = item;
            hover.Tooltip = ItemTooltip.Instance;
        }

        void UpdateZoneBar()
        {
            if (_refs == null || _refs.ZoneText == null || _root.Progress == null) return;

            var p = _root.Progress;
            var def = p.CurrentDef;
            string name = def != null ? def.Name : "";

            if (p.CurrentCleared)
                _refs.ZoneText.text = $"区域 {p.Current}/{_root.ZoneCount} · {name}   ◀ 已清空，前进进门";
            else if (_root.Boss != null && _root.Boss.State != null && _root.Boss.State.Alive)
                _refs.ZoneText.text = $"区域 {p.Current}/{_root.ZoneCount} · {name}   BOSS";
            else
                _refs.ZoneText.text = $"区域 {p.Current}/{_root.ZoneCount} · {name}   波次 {p.Wave}/{_root.WaveCount}   剩余 {_root.AliveEnemies}";
        }

        void UpdateBossBar()
        {
            if (_refs == null) return;
            var boss = _root.Boss;
            bool show = boss != null && boss.State != null && boss.State.Alive;

            if (_refs.BossBarRoot != null && _refs.BossBarRoot.activeSelf != show)
                _refs.BossBarRoot.SetActive(show);
            if (_refs.BossNameText != null && _refs.BossNameText.enabled != show)
                _refs.BossNameText.enabled = show;
            if (!show) return;

            var s = boss.State;
            if (_refs.BossFill != null)
            {
                _refs.BossFill.fillAmount = Mathf.Clamp01(s.HpRatio);
                _refs.BossFill.color = s.IsBreached
                    ? new Color(1f, 0.85f, 0.35f)
                    : s.Phase == BossPhase.P3 ? new Color(0.95f, 0.28f, 0.40f)
                    : new Color(0.78f, 0.32f, 0.72f);
            }

            string shield = s.ShieldUp ? "  ·  护盾" : "";
            string breach = s.IsBreached ? $"  ·  破防 {s.BreachLeft:0.0}s" : "";
            if (_refs.BossNameText != null)
                _refs.BossNameText.text = $"铸造核心  {s.Hp:0}/{s.MaxHp:0}  ·  柱 {s.PillarsAlive}/{BossState.PillarCount}{shield}{breach}";
        }

        void UpdateCrosshair(GunRuntime g)
        {
            if (_refs == null || _refs.Crosshair == null) return;
            float spread = 1.2f;
            if (g != null)
            {
                int peek = g.PeekNextSlot();
                if (peek >= 0)
                {
                    var cast = Loadout.ResolveCast(g.Resolved, g.Slots[peek]);
                    if (cast.Projectiles.Count > 0)
                    {
                        float sum = 0f;
                        for (int i = 0; i < cast.Projectiles.Count; i++) sum += cast.Projectiles[i].Spread;
                        spread = sum / cast.Projectiles.Count;
                    }
                }
            }
            // 散射可视化：直径 px = 34 + spread × 7.0（上限 150）
            //
            // 为什么基准从 16 提到 34（这次踩的坑）：
            //   上一版准星素材的内容只占画布 88%，控件又只有 16px，实际可见约 14px，
            //   四条臂在屏幕上糊成一个小十字块，几乎看不清。现在素材是精确几何
            //   （臂长 78 / 间隙 18 / 线宽 10，基准 256），34px 时臂宽约 1.3px、
            //   中心间隙约 2.4px，刚好能读出"十字 + 空隙"。
            //   上限从 110 提到 150：高散射时张开得更明显，但不会盖住准星本身。
            float px = Mathf.Clamp(34f + spread * 7.0f, 34f, 150f);
            _refs.Crosshair.rectTransform.sizeDelta = new Vector2(px, px);
        }

        // ---------------- 事件 ----------------

        public void Toast(string msg)
        {
            if (_refs == null || _refs.Toast == null) return;
            _refs.Toast.text = msg;
            var c = _refs.Toast.color; c.a = 1f; _refs.Toast.color = c;
            _toastLeft = 2.2f;
        }

        public void FlashDamage() => _vignetteLeft = 0.5f;

        public void DamageNumber(Vector3 world, float amount)
        {
            if (_canvas == null) return;

            // 浮动数字是屏幕空间临时元素，数量随命中瞬时增减，用预制体反而要池化。
            // 这里只造一个空节点 + Text，样式与 HUD 文字一致。
            var go = new GameObject("FloatText", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(80f, 24f);

            var t = go.AddComponent<Text>();
            t.font = UiFont();
            t.fontSize = 18;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(1f, 0.9f, 0.5f);
            t.text = $"{amount:0.#}";
            t.raycastTarget = false;

            _floats.Add(new FloatText { Label = t, World = world, Life = 0.7f });
        }

        void UpdateFloats()
        {
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                f.Life -= Time.deltaTime;
                if (f.Life <= 0f)
                {
                    if (f.Label != null) Destroy(f.Label.gameObject);
                    _floats.RemoveAt(i);
                    continue;
                }
                f.World += Vector3.up * (1.6f * Time.deltaTime);
                var cam = _root.Cam;
                if (cam != null && f.Label != null)
                {
                    var sp = cam.WorldToScreenPoint(f.World);
                    if (sp.z > 0f)
                    {
                        f.Label.rectTransform.position = sp;
                        var c = f.Label.color;
                        c.a = Mathf.Clamp01(f.Life / 0.7f);
                        f.Label.color = c;
                    }
                }
            }
        }

        public void ShowEnd(bool win)
        {
            if (_refs == null || _refs.CenterBig == null) return;
            if (_refs.BannerPlate != null) _refs.BannerPlate.gameObject.SetActive(true);

            _refs.CenterBig.text = win
                ? $"通关\n<size=26>击杀 {_root.Run.Kills} · 等级 {_root.Run.Level}</size>"
                : $"阵亡\n<size=26>击杀 {_root.Run.Kills} · 等级 {_root.Run.Level}\n按 R 重开</size>";
            _refs.CenterBig.color = win ? new Color(0.6f, 1f, 0.7f) : new Color(1f, 0.4f, 0.4f);

            _endAwaitingRestart = true;
        }

        void LateUpdate()
        {
            if (!_endAwaitingRestart) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame)
            {
                Time.timeScale = 1f;
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
        }
    }
}

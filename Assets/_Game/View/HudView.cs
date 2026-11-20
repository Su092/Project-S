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
        HudV2View _v2;

        readonly List<Image> _rotationPips = new List<Image>();
        readonly List<Text> _rotationLabels = new List<Text>();
        float _toastLeft;
        const float DamageFlashDuration = 0.5f;
        const float DamageFlashPeakAlpha = 0.6f;
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
                EnsureAmmoBar();
                SetDamageFlashAlpha(0f);
                _v2 = inst.GetComponent<HudV2View>();
                if (_v2 != null)
                {
                    _canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
                    _v2.Init(root, _refs);
                }
            }
        }

        /// <summary>
        /// 在弹药数字下方补一条弹匣进度条。预制体若已有 AmmoFill 则跳过；
        /// 否则运行时造（避免为加一条条去整表重生成 HUD）。
        /// </summary>
        void EnsureAmmoBar()
        {
            if (_refs == null || _refs.AmmoFill != null) return;

            Transform parent = _refs.AmmoText != null
                ? _refs.AmmoText.transform.parent
                : _refs.transform;

            // 数字上移一点，条贴在数字正下方（原 AmmoText 在 y=30）
            if (_refs.AmmoText != null)
            {
                var tr = (RectTransform)_refs.AmmoText.transform;
                tr.anchoredPosition = new Vector2(-30f, 42f);
            }

            var barGo = new GameObject("AmmoBar", typeof(RectTransform));
            barGo.transform.SetParent(parent, false);
            var brt = (RectTransform)barGo.transform;
            brt.anchorMin = new Vector2(1f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.anchoredPosition = new Vector2(-30f, 26f);
            brt.sizeDelta = new Vector2(280f, 12f);

            var bg = barGo.AddComponent<Image>();
            bg.sprite = UiAssets.BarBg;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.color = new Color(1f, 1f, 1f, 0.85f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(barGo.transform, false);
            var frt = (RectTransform)fillGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);

            var fill = fillGo.AddComponent<Image>();
            fill.sprite = UiAssets.BarXp;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.raycastTarget = false;
            fill.color = new Color(0.45f, 0.85f, 1f, 1f);

            _refs.AmmoFill = fill;
            // 条在数字后面，避免挡住字
            barGo.transform.SetSiblingIndex(
                _refs.AmmoText != null ? _refs.AmmoText.transform.GetSiblingIndex() : 0);
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
            // 统一走 UiKit.Font，不要在视图里各自取内置字体 ——
            // 否则将来换字体要改多处，漏一处就有一批文字仍是旧字体（很难看出来）。
            return UiKit.Font;
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
                float magFill = g.MagBarFill;
                if (_refs.AmmoText != null)
                {
                    _refs.AmmoText.text = g.Reloading
                        ? $"装填 {magFill * 100f:0}%"
                        : $"{g.Ammo} / {g.Stats.Mag}";
                    _refs.AmmoText.color = g.Reloading
                        ? new Color(0.55f, 0.90f, 1f)
                        : g.Ammo <= 2
                            ? new Color(1f, 0.45f, 0.35f)
                            : Color.white;
                }
                if (_refs.AmmoFill != null)
                {
                    _refs.AmmoFill.fillAmount = magFill;
                    // 换弹：青色上涨；弹药充足：淡青；见底：转橙红
                    _refs.AmmoFill.color = g.Reloading
                        ? new Color(0.35f, 0.82f, 1f, 1f)
                        : magFill > 0.35f
                            ? new Color(0.45f, 0.85f, 1f, 1f)
                            : magFill > 0.15f
                                ? new Color(1f, 0.72f, 0.35f, 1f)
                                : new Color(1f, 0.35f, 0.32f, 1f);
                }
                if (_refs.GunText != null)
                    _refs.GunText.text = $"{g.Base.Name}   {1f / Mathf.Max(0.01f, g.Stats.ShotInterval(0f)):0.0} 发/秒";

                // 该位有效伤害
                if (_refs.DpsText != null)
                {
                    var peek = _root.Weapons != null ? _root.Weapons.PeekCast() : null;
                    if (peek != null && peek.Succeeded)
                        _refs.DpsText.text =
                            $"本次期望 {peek.ExpectedDamage:0.0}  ·  DPS {g.DpsEstimate:0}";
                    else
                        _refs.DpsText.text = "空枪";
                }
            }
            else if (_refs.AmmoFill != null)
            {
                _refs.AmmoFill.fillAmount = 0f;
            }

            if (_v2 == null) { UpdateRotation(g); UpdateChain(g); }

            // 经验
            int need = r.NeedFor(r.Level);
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
            _v2?.Refresh(g);

            // toast 淡出
            if (_toastLeft > 0f && _refs.Toast != null)
            {
                _toastLeft -= Time.unscaledDeltaTime;
                var c = _refs.Toast.color;
                c.a = Mathf.Clamp01(_toastLeft / 0.6f);
                _refs.Toast.color = c;
            }

            // 受击红晕
            if (_vignetteLeft > 0f)
            {
                _vignetteLeft = Mathf.Max(0f, _vignetteLeft - Time.unscaledDeltaTime);
                SetDamageFlashAlpha((_vignetteLeft / DamageFlashDuration) * DamageFlashPeakAlpha);
            }

            UpdateFloats();
        }

        /// <summary>
        /// 槽位条：一格一个方块，显示「这一格是什么 + 指针在不在」。
        ///
        /// 与旧版的区别：旧版一格是一条链（可叠多件），新版**一格一件**。
        /// 所以这里只需要「有/无 + 是否指针位 + 是子弹还是被动件」，不需要计数。
        /// </summary>
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

                var it = g.Slots.Cells[i];
                bool isNext = g.PeekNextCell() == i;

                Color col;
                if (it == null) col = new Color(0.14f, 0.16f, 0.2f, 0.7f);
                else if (isNext) col = new Color(0.95f, 0.75f, 0.25f, 0.95f);
                else if (it.IsBullet) col = new Color(0.55f, 0.62f, 0.75f, 0.9f);
                else col = ItemTooltip.ScopeColor(it.Mod.Scope) * 0.7f;
                pip.color = col;

                var lbl = i < _rotationLabels.Count ? _rotationLabels[i] : null;
                if (lbl != null)
                {
                    lbl.text = it == null ? "" : (it.IsBullet ? it.Bullet.Icon : it.Mod.Icon);
                    lbl.fontSize = 18;
                    lbl.color = it == null ? new Color(0.4f, 0.45f, 0.5f) : Color.white;
                }
            }
        }

        /// <summary>
        /// 「这一发会打出什么」的预览：把本次施法解析出的投射物排成一排。
        ///
        /// 为什么改成显示投射物而不是显示格子：
        /// 读取式链的产物可能比读到的格子多（一发分裂成 6 枚、触发弹带载荷），
        /// 只画格子玩家看不出「实际会打出多少」。画投射物直接把结果摆出来。
        ///
        /// 数据来源必须是 Loadout.ResolveCast（与实战同一个函数），不能在 HUD 里另算一遍 ——
        /// 两处各算一遍迟早漂移，HUD 就成了假话。
        /// </summary>
        void UpdateChain(GunRuntime g)
        {
            if (_refs == null || _refs.ChainIcons == null) return;

            var cast = _root.Weapons != null ? _root.Weapons.PeekCast() : null;
            int n = 0;

            if (cast != null && cast.Projectiles.Count > 0)
            {
                n = Mathf.Min(cast.Projectiles.Count, _refs.ChainIcons.Length);
                for (int i = 0; i < _refs.ChainIcons.Length; i++)
                {
                    var icon = _refs.ChainIcons[i];
                    if (icon == null) continue;

                    if (i >= n) { icon.gameObject.SetActive(false); continue; }

                    var d = cast.Projectiles[i];
                    var item = ResolveItem(d.ModuleId);
                    if (item == null) { icon.gameObject.SetActive(false); continue; }

                    if (icon.Item == null || icon.Item.Id != item.Id)
                    {
                        icon.Bind(item, 40f, showRing: true, badge: (i + 1).ToString(), showScope: false);
                        EnsureHover(icon, item);
                    }
                    else if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);

                    // 颜色按投射物实际表现：有载荷的描金（它还会再打一次）
                    bool hasPayload = d.Payload != null && d.Payload.Count > 0;
                    if (icon.Icon != null)
                        icon.Icon.color = hasPayload ? new Color(1f, 0.95f, 0.7f) : Color.white;
                    if (icon.Ring != null)
                        icon.Ring.color = hasPayload
                            ? new Color(1f, 0.85f, 0.4f, 0.98f)
                            : new Color(0.68f, 0.76f, 0.88f, 0.9f);
                    if (icon.Badge != null) icon.Badge.color = new Color(0.82f, 0.88f, 0.95f);
                }
            }
            else
            {
                for (int i = 0; i < _refs.ChainIcons.Length; i++)
                    if (_refs.ChainIcons[i] != null) _refs.ChainIcons[i].gameObject.SetActive(false);
            }

            if (_refs.ChainTitle != null)
            {
                if (n == 0) _refs.ChainTitle.text = "空槽 · 前往调律谱架装配";
                else
                    _refs.ChainTitle.text =
                        $"下一发 {n} 发 · 耗弹 {cast.AmmoCost} · 期望 {cast.ExpectedDamage:0}";
            }
        }

        /// <summary>按 id 找配件（子弹优先）。HUD 只读，不缓存指针以免装配后失效。</summary>
        ChainItem ResolveItem(string id)
        {
            if (string.IsNullOrEmpty(id) || _root.Data == null) return null;
            return _root.Data.MakeItem(id);
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
            if (_refs == null || _root.Director == null) return;

            var d = _root.Director;
            float elapsed = d.Elapsed;

            // 中央大计时器：只显示「已经活了多久」。
            // 不显示倒计时 —— 倒计时会让人下意识盯着数字等，而生存时长的正计时
            // 在最后 30 秒更有压迫感（看着数字变大，而不是变小）。
            if (_refs.Timer != null) _refs.Timer.text = Clock(elapsed);

            // 倒计时标签：只显示**最近的一个**目标。
            // 两个数字同时跳会让人无法判断优先级（这是原型里的处理，也是我们要的）。
            if (_refs.TimerLabel != null)
            {
                string goal;
                if (d.BossSpawned)
                {
                    bool bossAlive = _root.Boss != null && _root.Boss.State != null && _root.Boss.State.Alive;
                    goal = bossAlive ? "击败"+_root.Boss.DisplayName : "";
                }
                else
                {
                    float toBoss = d.SecondsToBoss;
                    float toElite = d.SecondsToElite;

                    if (toElite >= 0f && toElite < toBoss)
                        goal = $"距精英 {Clock(toElite)}";
                    else if (toBoss >= 0f)
                        goal = $"距 BOSS {Clock(toBoss)}";
                    else
                        goal = "BOSS";
                }
                _refs.TimerLabel.text = goal;
            }

            // 阶段行：阶段名 + 敌人数量 + 威胁
            if (_refs.ZoneText != null)
            {
                var phase = d.CurrentPhase;
                string phaseName = phase != null ? phase.Name : "—";
                _refs.ZoneText.text =
                    $"{phaseName}   ·   敌人 {_root.AliveEnemies}   ·   威胁 {_root.Run.Threat:0}";
            }
        }

        /// <summary>秒 → `mm:ss`。统一格式，避免 1:02 与 01:2 在同一行里跳变。</summary>
        static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
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
                _refs.BossNameText.text = $"{_root.Boss.DisplayName}  {s.Hp:0}/{s.MaxHp:0}" + (s.PillarCount > 0 ? $"  ·  柱 {s.PillarsAlive}/{s.PillarCount}" : "") + shield + breach;
        }

        void UpdateCrosshair(GunRuntime g)
        {
            if (_refs == null || _refs.Crosshair == null) return;
            float spread = 1.2f;
            if (g != null)
            {
                var cast = _root.Weapons != null ? _root.Weapons.PeekCast() : null;
                if (cast != null && cast.Projectiles.Count > 0)
                    spread = cast.AverageSpread + g.Base.SpreadBase;
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

        public void FlashDamage()
        {
            _vignetteLeft = DamageFlashDuration;
            SetDamageFlashAlpha(DamageFlashPeakAlpha);
        }

        void SetDamageFlashAlpha(float alpha)
        {
            if (_refs == null || _refs.DamageVignette == null) return;
            // The sprite already contains the red gradient; clear RGB would multiply it to black.
            _refs.DamageVignette.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            _refs.DamageVignette.raycastTarget = false;
        }

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
                ? $"通关\n<size=26>存活 {Clock(_root.RunElapsed)} · 击杀 {_root.Run.Kills} · 等级 {_root.Run.Level}</size>"
                : $"阵亡\n<size=26>存活 {Clock(_root.RunElapsed)} · 击杀 {_root.Run.Kills} · 等级 {_root.Run.Level}\n按 R 重开</size>";
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

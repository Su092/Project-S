using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 主菜单 + 局外解锁界面（shard 消费口）。
    ///
    /// 设计约束（docs/gameplay-spec.md §8）：
    ///  - 局外只解锁「更多选择」，不提供永久数值加成
    ///  - 所有解锁项从 Core/MetaProgress.Catalog 读取，不在这里硬编码
    ///
    /// 结构纪律：界面的层级/锚点/配色全部来自 `Resources/UI/Prefabs/Menu.prefab`。
    /// 本类只做三件事 —— 实例化预制体、把数据填进预制体上的引用、挂按钮回调。
    /// 想改间距或换配色，改预制体（或跑 S/生成 UI 预制体 再手工微调），不要在这里加 `AddComponent`。
    ///
    /// UI 纪律：打开菜单即暂停；点击有音效；不可购买时按钮变灰并给出原因。
    /// </summary>
    public sealed class MenuView : MonoBehaviour
    {
        Canvas _canvas;
        MenuRefs _refs;
        readonly List<GameObject> _rows = new List<GameObject>();

        MetaProgress _meta;
        System.Action _onStart;

        float[] _volValues;
        Text[] _volValueTexts;

        public bool IsOpen => _refs != null && _refs.Root != null && _refs.Root.activeSelf;

        public void Init(MetaProgress meta, System.Action onStart)
        {
            _meta = meta;
            _onStart = onStart;

            BuildCanvas();
            BuildMenu();
            Close();
        }

        // ---------------- 构建 ----------------

        void BuildCanvas()
        {
            var go = new GameObject("MenuCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;   // 盖住 HUD 与背包
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            UiUtil.EnsureEventSystem(transform);
        }

        void BuildMenu()
        {
            var inst = UiPrefab.Spawn(UiPrefab.Menu, _canvas.transform);
            if (inst == null) return;

            _refs = inst.GetComponent<MenuRefs>();
            if (_refs == null)
            {
                Debug.LogError("[S] Menu.prefab 缺少 MenuRefs（引用表未配置）");
                return;
            }

            // 开始 / 退出
            _refs.StartButton.onClick.AddListener(() =>
            {
                SfxBank.Instance?.PlayUiClick();
                Close();
                _onStart?.Invoke();
            });

            _refs.QuitButton.onClick.AddListener(() =>
            {
                SfxBank.Instance?.PlayUiClick();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });

            // 音量：- / 数值 / + 三段
            _volValues = new float[4];
            _volValueTexts = _refs.VolumeValues;
            for (int i = 0; i < 4 && i < _refs.VolumeMinus.Length; i++)
            {
                int idx = i;
                _refs.VolumeMinus[i].onClick.AddListener(() => StepVolume(idx, -0.1f));
                _refs.VolumePlus[i].onClick.AddListener(() => StepVolume(idx, +0.1f));
            }

            RefreshVolume();
        }

        // ---------------- 音量 ----------------

        void StepVolume(int index, float delta)
        {
            switch (index)
            {
                case 0: AudioMix.Master = AudioMix.Master + delta; break;
                case 1: AudioMix.Sfx = AudioMix.Sfx + delta; break;
                case 2: AudioMix.Music = AudioMix.Music + delta; break;
                case 3: AudioMix.Ui = AudioMix.Ui + delta; break;
            }
            SfxBank.Instance?.PlayUiClick();
            RefreshVolume();
        }

        void RefreshVolume()
        {
            if (_volValueTexts == null) return;
            float[] vals = { AudioMix.Master, AudioMix.Sfx, AudioMix.Music, AudioMix.Ui };
            for (int i = 0; i < _volValueTexts.Length; i++)
            {
                if (i < vals.Length) _volValues[i] = vals[i];
                if (_volValueTexts[i] != null)
                    _volValueTexts[i].text = $"{vals[i] * 100f:0}%";
            }
        }

        /// <summary>自检用：读取某条音量链的当前值。</summary>
        public float VolumeAt(int index) =>
            index >= 0 && index < _volValues.Length ? _volValues[index] : -1f;

        /// <summary>自检用：直接调音量（绕过点击）。</summary>
        public void StepVolumeForTest(int index, float delta) => StepVolume(index, delta);

        // ---------------- 开关 ----------------

        public void Open()
        {
            if (_refs == null) return;
            _refs.Root.SetActive(true);
            Refresh();
            RefreshVolume();
            // 光标由 GameRoot 统一裁决（它知道是否还有别的面板在等点击）
            GameRoot.Instance?.SyncCursorToState();
        }

        public void Close()
        {
            if (_refs != null && _refs.Root != null) _refs.Root.SetActive(false);
            // 关菜单后把光标交回玩家控制。这里不直接 LockCursor(true)，
            // 而是让 GameRoot 统一裁决 —— 它知道是否还有排队的三选一/背包要处理。
            var root = GameRoot.Instance;
            if (root != null) root.SyncCursorToState();
        }

        // ---------------- 刷新 ----------------

        public void Refresh()
        {
            if (_meta == null || _refs == null || _refs.UnlockList == null) return;

            if (_refs.ShardText != null)
                _refs.ShardText.text = $"shard  {_meta.Shard}    ·    通关 {_meta.Clears}";
            if (_refs.StatsText != null)
                _refs.StatsText.text = $"历史最高单发 {_meta.BestSingleHit:0.#}\n图鉴已记录 {_meta.Codex.Count} 种配件";

            foreach (var r in _rows) if (r != null) Destroy(r);
            _rows.Clear();

            // 行高按列表可用高度均分，填满而不留大片空白。
            // 5 行、列表 624 高 ⇒ 每行 124 太高；上限收到 96，剩余空间留作呼吸。
            // 下限 64 保证两行文字不挤。
            var items = MetaProgress.Catalog;
            int n = items.Length;
            float avail = _refs.UnlockList.rect.height;
            float gap = 8f;
            float rowH = Mathf.Clamp((avail - gap * (n - 1)) / Mathf.Max(1, n), 64f, 96f);

            float y = -4f;
            foreach (var u in items)
            {
                var row = MakeRow(u, y, rowH);
                if (row != null) _rows.Add(row);
                y -= (rowH + gap);
            }
        }

        /// <summary>
        /// 克隆一行解锁项并填数据。
        ///
        /// 布局纪律（这里踩过）：
        ///   · 行锚点必须左 (0,1) 右 (1,1)，否则行宽为 0，子元素只能靠负偏移撑出来，
        ///     表现为「按钮溢出面板右边界」；
        ///   · 行高由调用方按可用高度算，别写死 —— 固定 72 会在底部留出大片空白。
        /// 这两条现在由预制体保证（锚点写在 .prefab 里），这里只负责填数值。
        /// </summary>
        GameObject MakeRow(MetaProgress.UnlockDef u, float yOffset, float rowH)
        {
            if (_refs.RowTemplate == null)
            {
                Debug.LogError("[S] Menu.prefab 未指定 RowTemplate");
                return null;
            }

            bool owned = _meta.IsUnlocked(u.Id);
            bool afford = _meta.CanAfford(u.Id);

            var go = Instantiate(_refs.RowTemplate, _refs.UnlockList);
            go.name = "Row_" + u.Id;
            go.SetActive(true);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, yOffset - rowH);
            rt.offsetMax = new Vector2(0f, yOffset);

            var r = go.GetComponent<MenuRowRefs>();
            if (r == null) return go;

            // 三种状态：已解锁（绿）/ 买得起（蓝）/ 买不起（暗）。
            // 底色要**略亮于面板底**：卡片在层级上应比容器更靠前，
            // 比容器更暗会读成「挖空的洞」，与卡片语义相反。
            if (r.Background != null)
                r.Background.color = owned
                    ? new Color(0.10f, 0.20f, 0.14f, 0.98f)
                    : afford
                        ? new Color(0.13f, 0.17f, 0.24f, 0.98f)
                        : new Color(0.10f, 0.115f, 0.135f, 0.96f);

            if (r.Frame != null)
                r.Frame.color = owned
                    ? new Color(0.42f, 0.80f, 0.52f, 0.50f)
                    : afford
                        ? new Color(0.34f, 0.58f, 0.80f, 0.40f)
                        : new Color(0.28f, 0.31f, 0.37f, 0.38f);

            if (r.Accent != null)
                r.Accent.color = owned ? new Color(0.45f, 0.85f, 0.55f)
                    : afford ? new Color(0.35f, 0.62f, 0.85f)
                             : new Color(0.28f, 0.30f, 0.34f);

            if (r.IconSlot != null)
                r.IconSlot.color = owned
                    ? new Color(0.45f, 0.80f, 0.55f, 0.55f)
                    : new Color(0.42f, 0.55f, 0.70f, 0.50f);

            // 图标内容：起手枪用枪图标，起始配件用子弹图标。
            // 注意这里**不用** ApplyIcon：那个方法挂的是加色材质，适合黑底图标；
            // 而 icon_gun / icon_bullet 本身是带透明度的彩色图，直接 Simple + tint 更准。
            if (r.Icon != null)
            {
                var catSpr = u.Id == "gun_third" ? UiAssets.IconGun
                           : u.Id.Contains("start_") ? UiAssets.IconBullet
                           : UiAssets.IconWhole;
                r.Icon.sprite = catSpr;
                r.Icon.color = owned ? new Color(0.62f, 0.95f, 0.70f, 0.95f)
                                     : new Color(0.62f, 0.78f, 0.95f, 0.90f);
                r.Icon.enabled = catSpr != null;
            }

            // 状态角标：已解锁打勾，未解锁挂锁
            var badge = owned ? UiAssets.IconCheck : UiAssets.IconLock;
            if (r.Badge != null)
            {
                r.Badge.sprite = badge;
                r.Badge.enabled = badge != null;
            }
            if (r.BadgeBack != null) r.BadgeBack.enabled = badge != null;

            if (r.Name != null)
            {
                r.Name.text = u.Name;
                r.Name.color = owned ? new Color(0.70f, 0.97f, 0.75f) : Color.white;
            }
            if (r.Desc != null) r.Desc.text = u.Desc;

            // 右侧按钮：已解锁→灰绿；买得起→亮蓝（可点）；买不起→压灰（禁点）
            if (r.BuyButton != null)
            {
                r.BuyButton.onClick.RemoveAllListeners();
                r.BuyButton.interactable = !owned && afford;

                if (owned)
                {
                    r.BuyImage.color = new Color(0.75f, 1f, 0.80f);
                    if (r.BuyLabel != null) r.BuyLabel.text = "已解锁";
                }
                else
                {
                    // 买不起时不用 btn_danger（红框 + 压灰 = 脏色块），
                    // 只用同一套按钮素材配低亮度 tint，保持「一套语言」。
                    r.BuyImage.color = afford
                        ? Color.white
                        : new Color(0.42f, 0.45f, 0.50f, 0.90f);

                    if (r.BuyLabel != null)
                    {
                        r.BuyLabel.text = $"{u.Cost} shard";
                        r.BuyLabel.color = afford ? Color.white : new Color(0.68f, 0.70f, 0.74f);
                    }

                    if (afford)
                    {
                        string id = u.Id;
                        r.BuyButton.onClick.AddListener(() => Buy(id));
                    }
                }
            }

            return go;
        }

        void Buy(string id)
        {
            var code = _meta.Purchase(id);
            SfxBank.Instance?.PlayUiClick();

            if (_refs != null && _refs.Hint != null)
            {
                _refs.Hint.text = code switch
                {
                    null => $"已解锁：{MetaProgress.FindUnlock(id).Name}",
                    "already" => "该项已解锁",
                    "poor" => "shard 不足 —— 通关一次可获得 1 shard",
                    "bad-id" => "无效的解锁项",
                    _ => "无法解锁：" + code,
                };
            }

            if (code == null)
            {
                MetaSave.Save(_meta);
                // 起始配件类解锁后立即反映到「本局」不适用（本局已开始前配置），
                // 但下次开局会带上，刷新界面即可
                Refresh();
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                SfxBank.Instance?.PlayUiClick();
                Close();
                _onStart?.Invoke();
            }
        }

        // ---------------- 自检用 ----------------

        /// <summary>自检用：直接触发「开始」（等价于点按钮）。</summary>
        public void StartForTest()
        {
            Close();
            _onStart?.Invoke();
        }

        public int RowCount => _rows.Count;
        public string ShardText => _refs != null && _refs.ShardText != null ? _refs.ShardText.text : "";

        public bool HasRowFor(string id)
        {
            foreach (var r in _rows) if (r != null && r.name == "Row_" + id) return true;
            return false;
        }

        /// <summary>自检用：直接触发购买（绕过鼠标点击）。</summary>
        public void BuyForTest(string id) => Buy(id);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 改装台 / 背包（对齐 MODCORE 原型的 #inventory）。
    ///
    /// 布局与原型一致：左侧是枪械面板（页签 + 属性摘要 + **槽位网格**），
    /// 右侧是背包网格 + 分解框 + 详情卡。
    ///
    /// 与旧版的结构差异：**一格一件**。旧版一格是一条可叠多件的「链」，
    /// 新版把「作用范围」交给配件自身（7 种作用域），格子只负责提供位置。
    /// 这样玩家看的是「这张牌放在这个位置会影响谁」，而不是「这条链怎么排序」。
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        public bool Open { get; private set; }
        public Transform DragLayer => _dragLayer;

        public bool IsBagVisible => _bagRoot != null && _bagRoot.activeInHierarchy;

        /// <summary>背包右下角的常驻详情区（自检与外部只读用）。</summary>
        public ItemDetailPanel DetailPanel => _inv != null ? _inv.Detail : null;
        public bool IsOfferVisible => _offerRoot != null && _offerRoot.activeInHierarchy;
        public int VisibleCardCount => _bagGrid != null ? _bagGrid.childCount : 0;

        static string ItemName(ChainItem it) => it == null ? "（空）" : it.Name;

        /// <summary>当前查看的枪（背包左侧页签选中的那把）。</summary>
        GunRuntime CurrentGun()
        {
            var guns = _root?.Weapons?.AllGuns;
            if (guns == null || guns.Count == 0) return null;
            _viewGun = Mathf.Clamp(_viewGun, 0, guns.Count - 1);
            return guns[_viewGun];
        }

        /// <summary>当前枪的已装件数（自检判据：可见卡片数不能是 0）。</summary>
        public int ChainRowCount
        {
            get
            {
                var g = CurrentGun();
                return g != null ? g.Slots.FilledCount : 0;
            }
        }

        public int OfferCardCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _offerCards.Count; i++)
                    if (_offerCards[i] != null && _offerCards[i].activeInHierarchy) n++;
                return n;
            }
        }

        public int OfferIconBoundCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _offerCards.Count; i++)
                {
                    if (_offerCards[i] == null || !_offerCards[i].activeInHierarchy) continue;
                    var c = _offerCards[i].GetComponent<OfferCardRefs>();
                    if (c != null && c.IconNode != null && c.IconNode.Item != null) n++;
                }
                return n;
            }
        }

        GameRoot _root;
        SData _data;
        Canvas _canvas;
        RectTransform _dragLayer;

        GameObject _bagRoot, _offerRoot;
        InvRefs _inv;
        OfferRefs _offerRefs;
        RectTransform _chainArea;
        RectTransform _bagGrid;
        Text _hint;
        Text _gunSummary;

        int _viewGun;
        readonly List<GameObject> _cloneSpawned = new List<GameObject>();

        // 三选一
        bool _offerMode;
        List<Offer> _offer;
        readonly List<GameObject> _offerCards = new List<GameObject>();

        public void Init(GameRoot root)
        {
            _root = root;
            _data = root.Data;
            BuildCanvas();

            ItemTooltip.Ensure(_canvas.transform);

            BuildBag();
            BuildOffer();
            CloseUi();
        }

        void BuildCanvas()
        {
            var go = new GameObject("InvCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            UiUtil.EnsureEventSystem(transform);

            var dl = new GameObject("DragLayer", typeof(RectTransform));
            var dlRt = (RectTransform)dl.transform;
            dlRt.SetParent(_canvas.transform, false);
            dlRt.anchorMin = Vector2.zero; dlRt.anchorMax = Vector2.one;
            dlRt.offsetMin = Vector2.zero; dlRt.offsetMax = Vector2.zero;
            _dragLayer = dlRt;
        }

        void BuildBag()
        {
            _bagRoot = UiPrefab.Spawn("Inventory", _canvas.transform);
            if (_bagRoot == null) return;

            _inv = _bagRoot.GetComponent<InvRefs>();
            if (_inv == null)
            {
                Debug.LogError("[S] Inventory.prefab 缺少 InvRefs（引用表未配置）");
                return;
            }

            _chainArea = _inv.ChainArea;
            _bagGrid = _inv.BagGrid;
            _hint = _inv.Hint;

            if (_inv.BagDropPanel != null) _inv.BagDropPanel.Owner = this;
        }

        void BuildOffer()
        {
            _offerRoot = UiPrefab.Spawn("Offer", _canvas.transform);
            if (_offerRoot == null) return;

            _offerRefs = _offerRoot.GetComponent<OfferRefs>();
            if (_offerRefs == null)
                Debug.LogError("[S] Offer.prefab 缺少 OfferRefs（引用表未配置）");

            _offerRoot.SetActive(false);
        }

        // ---------------- 开关 ----------------

        void CloseUi()
        {
            Open = false;
            if (_bagRoot != null) _bagRoot.SetActive(false);
            if (_offerRoot != null) _offerRoot.SetActive(false);
        }

        public void OpenBag()
        {
            _offerMode = false;
            _offerRoot.SetActive(false);
            _bagRoot.SetActive(true);
            Open = true;
            _root.SetPaused(true);
            Refresh();
        }

        public void Close()
        {
            CloseUi();
            _root.OnInventoryClosed();
        }

        // ---------------- 自动驾驶 ----------------

        /// <summary>
        /// AI 用：自动处理当前弹层。走的是与真人点击完全相同的路径。
        /// </summary>
        public void AutoResolveForAI()
        {
            if (_offerMode && _offer != null && _offer.Count > 0)
            {
                if (_root.Run.Bag.Count >= _root.Run.BagCapacity) TryDiscardOneForAI();

                if (_root.Run.Bag.Count < _root.Run.BagCapacity)
                {
                    _root.Run.TryPick(_offer[0].Id);
                    _offer = null;
                    _offerMode = false;

                    if (_root.HasPendingOffers) _root.PopOfferIfAny();
                    else Close();
                    return;
                }
            }

            // 背包模式：装备一件然后关掉
            AutoEquipOneForAI();
            Close();
        }

        void TryDiscardOneForAI()
        {
            if (_root.Run.Bag.Count == 0) return;
            _root.Run.Salvage(_root.Run.Bag[0]);
        }

        /// <summary>
        /// AI 装件策略：优先把子弹放进空格；其次把件放到「上一格是子弹」的位置（Prev 生效）。
        /// 这不是最优构筑，只是让数值链路跑起来。
        /// </summary>
        void AutoEquipOneForAI()
        {
            var g = CurrentGun();
            if (g == null || _root.Run.Bag.Count == 0) return;

            var item = _root.Run.Bag[0];

            // 子弹优先放第一个空格（保证枪永远能开火）
            if (item.IsBullet)
            {
                for (int i = 0; i < g.Slots.Length; i++)
                {
                    if (g.Slots.Cells[i] != null) continue;
                    g.Slots.Cells[i] = item;
                    _root.Run.RemoveFromBag(item);
                    g.Recalc();
                    return;
                }
            }

            // 件：找一个能用 Prev 影响左侧子弹的空格
            for (int i = 1; i < g.Slots.Length; i++)
            {
                if (g.Slots.Cells[i] != null) continue;
                if (g.Slots.Cells[i - 1] == null || !g.Slots.Cells[i - 1].IsBullet) continue;
                g.Slots.Cells[i] = item;
                _root.Run.RemoveFromBag(item);
                g.Recalc();
                return;
            }

            // 实在没位置就分解掉
            _root.Run.Salvage(item);
        }

        // ---------------- 刷新 ----------------

        void ClearSpawned()
        {
            for (int i = 0; i < _cloneSpawned.Count; i++)
                if (_cloneSpawned[i] != null) Destroy(_cloneSpawned[i]);
            _cloneSpawned.Clear();
        }

        public void Refresh()
        {
            RefreshChain();
            RefreshBag();
            RefreshHint();
        }

        /// <summary>
        /// 左侧枪械面板：页签 + 属性摘要 + 槽位网格。
        ///
        /// 槽位网格用 GridLayoutGroup 而不是逐格算坐标：
        /// 槽位数在 6~12 之间变化，硬编码坐标会在 12 槽的发射器上溢出。
        /// </summary>
        void RefreshChain()
        {
            ClearSpawned();

            for (int i = _chainArea.childCount - 1; i >= 0; i--)
            {
                var ch = _chainArea.GetChild(i);
                if (ch.name == "GunSummary") continue;
                Destroy(ch.gameObject);
            }

            var guns = _root.Weapons != null ? _root.Weapons.AllGuns : null;
            if (guns == null || guns.Count == 0) return;

            _viewGun = Mathf.Clamp(_viewGun, 0, guns.Count - 1);
            var g = guns[_viewGun];

            // 枪页签
            if (_inv != null && _inv.GunTabTemplate != null)
            {
                for (int gi = 0; gi < guns.Count; gi++)
                {
                    int idx = gi;
                    var tab = Instantiate(_inv.GunTabTemplate, _chainArea);
                    tab.name = "Tab" + gi;
                    tab.SetActive(true);
                    _cloneSpawned.Add(tab);

                    var trt = (RectTransform)tab.transform;
                    trt.anchorMin = new Vector2(0f, 1f);
                    trt.anchorMax = new Vector2(0f, 1f);
                    trt.pivot = new Vector2(0f, 1f);
                    trt.anchoredPosition = new Vector2(gi * 230f, 0f);
                    trt.sizeDelta = new Vector2(220f, 52f);

                    var trefs = tab.GetComponent<GunTabRefs>();
                    if (trefs == null) continue;

                    if (trefs.Background != null)
                        trefs.Background.color = gi == _viewGun
                            ? new Color(0.28f, 0.38f, 0.52f)
                            : new Color(0.14f, 0.17f, 0.22f);

                    if (trefs.Button != null)
                    {
                        trefs.Button.onClick.RemoveAllListeners();
                        trefs.Button.onClick.AddListener(() => { _viewGun = idx; Refresh(); });
                    }

                    if (trefs.Label != null)
                        trefs.Label.text = $"{guns[idx].Base.Name}  ({guns[idx].Slots.Length}槽)";
                }
            }

            // 属性摘要
            if (_gunSummary == null)
            {
                var sgo = new GameObject("GunSummary", typeof(RectTransform));
                sgo.transform.SetParent(_chainArea, false);
                var srt = (RectTransform)sgo.transform;
                srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f);
                srt.pivot = new Vector2(0.5f, 1f);
                srt.offsetMin = new Vector2(2f, -80f); srt.offsetMax = new Vector2(-2f, -52f);
                _gunSummary = sgo.AddComponent<Text>();
                _gunSummary.font = UiKit.Font;
                _gunSummary.fontSize = 16;
                _gunSummary.alignment = TextAnchor.MiddleLeft;
                _gunSummary.horizontalOverflow = HorizontalWrapMode.Overflow;
                _gunSummary.verticalOverflow = VerticalWrapMode.Overflow;
                _gunSummary.raycastTarget = false;
            }
            _gunSummary.color = new Color(0.6f, 0.7f, 0.85f);
            _gunSummary.text =
                $"弹匣 {g.Ammo}/{g.Stats.Mag}   击发 {1f / Mathf.Max(0.01f, g.Stats.ShotInterval(0f)):0.0}/s   " +
                $"换弹 {g.Stats.Reload:0.0}s   读取 {g.Stats.CastCount} 发   散射 {g.Base.SpreadBase:0.00}°";

            // 槽位网格
            var gridRoot = new GameObject("SlotGrid", typeof(RectTransform));
            gridRoot.transform.SetParent(_chainArea, false);
            var grt = (RectTransform)gridRoot.transform;
            grt.anchorMin = new Vector2(0f, 1f); grt.anchorMax = new Vector2(1f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.offsetMin = new Vector2(0f, -560f);
            grt.offsetMax = new Vector2(0f, -92f);
            _cloneSpawned.Add(gridRoot);

            // 槽位网格：列宽按「左侧区域实际宽度」算，不能写死。
            // 区域宽度 = 屏宽 × 0.62 − 两侧内边距（40 + 10），1600 参考宽下约 942px。
            // 写死 5 列 × 124px 时一行占 670px 看似没问题，但只要显示器更窄或
            // 换到 1280 参考宽就会溢出到背包面板上，且这种溢出在截图里很像是「卡片没对齐」。
            var area = (RectTransform)_chainArea;
            float areaW = area != null && area.rect.width > 1f ? area.rect.width : 940f;
            const int cols = 5;
            const float gap = 10f;
            float cellW = Mathf.Floor((areaW - 8f - gap * (cols - 1)) / cols);
            cellW = Mathf.Clamp(cellW, 96f, 140f);

            var grid = gridRoot.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellW, 108f);
            grid.spacing = new Vector2(gap, gap);
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;

            int nextCell = g.PeekNextCell();

            for (int si = 0; si < g.Slots.Length; si++)
            {
                var cellGo = new GameObject("Slot" + si, typeof(RectTransform));
                cellGo.transform.SetParent(gridRoot.transform, false);

                var bg = cellGo.AddComponent<Image>();
                bool isNext = nextCell == si;
                bg.color = isNext
                    ? new Color(0.20f, 0.30f, 0.42f, 0.98f)
                    : new Color(0.10f, 0.12f, 0.16f, 0.95f);

                var drop = cellGo.AddComponent<SlotDropTarget>();
                drop.Owner = this;
                drop.SlotIndex = si;
                drop.ChainIndex = -1;

                var it = g.Slots.Cells[si];

                // 格号
                var idxGo = new GameObject("Idx", typeof(RectTransform));
                idxGo.transform.SetParent(cellGo.transform, false);
                var irt = (RectTransform)idxGo.transform;
                irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(0f, 1f);
                irt.pivot = new Vector2(0f, 1f);
                irt.anchoredPosition = new Vector2(6f, -2f);
                irt.sizeDelta = new Vector2(40f, 16f);
                var idxText = idxGo.AddComponent<Text>();
                idxText.font = UiKit.Font;
                idxText.fontSize = 11;
                idxText.color = isNext ? new Color(1f, 0.85f, 0.4f) : new Color(0.5f, 0.56f, 0.64f);
                idxText.alignment = TextAnchor.UpperLeft;
                idxText.raycastTarget = false;
                idxText.text = isNext ? $"{si + 1}▶" : (si + 1).ToString();

                if (it != null)
                {
                    var card = MakeCard(cellGo.transform, it, new Vector2(4f, 4f),
                        new Vector2(cellW - 8f, 100f), fromSlot: si, fromChain: -1);

                    var cd = card.AddComponent<SlotDropTarget>();
                    cd.Owner = this; cd.SlotIndex = si; cd.ChainIndex = -1;
                }
                else
                {
                    // 空格提示：告诉玩家这里可以放什么
                    var hintGo = new GameObject("Empty", typeof(RectTransform));
                    hintGo.transform.SetParent(cellGo.transform, false);
                    var hrt = (RectTransform)hintGo.transform;
                    hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
                    hrt.offsetMin = new Vector2(4f, 4f); hrt.offsetMax = new Vector2(-4f, -4f);
                    var ht = hintGo.AddComponent<Text>();
                    ht.font = UiKit.Font;
                    ht.fontSize = 13;
                    ht.color = new Color(0.35f, 0.40f, 0.48f);
                    ht.alignment = TextAnchor.MiddleCenter;
                    ht.raycastTarget = false;
                    ht.text = "空";
                }
            }
        }

        void RefreshBag()
        {
            if (_bagGrid == null) return;

            for (int i = _bagGrid.childCount - 1; i >= 0; i--)
                Destroy(_bagGrid.GetChild(i).gameObject);

            var bag = _root.Run.Bag;
            for (int i = 0; i < bag.Count; i++)
            {
                var item = bag[i];
                // 背包卡片尺寸与槽位卡一致（116×100），由 GridLayoutGroup 排布，
                // 这里只给左上锚点让布局组接管位置。
                MakeCard(_bagGrid, item, Vector2.zero, new Vector2(116f, 100f),
                    fromSlot: -1, fromChain: -1);
            }
        }

        void RefreshHint()
        {
            if (_hint == null) return;
            int filled = ChainRowCount;
            var g = CurrentGun();
            int total = g != null ? g.Slots.Length : 0;
            _hint.text =
                $"背包 {_root.Run.Bag.Count}/{_root.Run.BagCapacity}    ·    " +
                $"已装 {filled}/{total}    ·    " +
                "拖到槽位装入（已有件会交换）    ·    右键快速装/卸    ·    拖到空白处卸下";
        }

        /// <summary>造一张配件卡（背包与槽位共用）。</summary>
        GameObject MakeCard(Transform parent, ChainItem item, Vector2 pos, Vector2 size,
            int fromSlot, int fromChain)
        {
            var prefab = UiPrefab.Load("ItemCard");
            GameObject card;
            if (prefab != null)
            {
                card = Instantiate(prefab, parent);
                card.name = "Card_" + item.Id;
                card.SetActive(true);
            }
            else
            {
                card = new GameObject("Card_" + item.Id, typeof(RectTransform));
                card.transform.SetParent(parent, false);
                var bg = card.AddComponent<Image>();
                bg.color = new Color(0.10f, 0.14f, 0.20f, 0.95f);
            }

            var rt = (RectTransform)card.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var refs = card.GetComponent<CardRefs>();
            if (refs != null)
            {
                if (refs.Background != null)
                    refs.Background.color = new Color(0.08f, 0.10f, 0.14f, 0.96f);

                // 稀有度靠顶部色带（原型 .mod 的 border-top 就是这个语义）
                if (refs.Tag != null)
                    refs.Tag.color = RarityColor(item.Rarity);

                // 作用域色标：子弹给暖橙，被动件给各自作用域色
                if (refs.Inner != null)
                    refs.Inner.color = item.IsBullet
                        ? new Color(1f, 0.70f, 0.28f, 0.9f)
                        : ItemTooltip.ScopeColor(item.Mod.Scope);

                if (refs.Label != null)
                {
                    // 名称就是名称：不要拼图标的单字（「标 标准弹」里的「标」在窄卡片里
                    // 会被挤成孤零零一行，看起来像排版错位）。图标本身已经画了同样意思。
                    refs.Label.text = item.Name;
                    refs.Label.fontSize = 13;
                    refs.Label.color = Color.white;
                }

                if (refs.IconNode != null) refs.IconNode.Bind(item, 52f, showRing: true, badge: null, showScope: false);
            }

            // 拖拽与悬停
            var drag = card.AddComponent<DraggableItem>();
            drag.Owner = this;
            drag.Item = item;
            drag.FromSlot = fromSlot;
            drag.FromChain = fromChain;

            var hover = card.AddComponent<ItemHover>();
            hover.Item = item;
            hover.Tooltip = ItemTooltip.Instance;
            // 背包内的卡片优先填右下角的常驻详情区（#inv-detail），
            // 不再弹出跟随鼠标的浮动提示 —— 后者会挡住相邻格子，无法对比。
            if (_inv != null) hover.Detail = _inv.Detail;

            return card;
        }

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.Epic => new Color(0.78f, 0.49f, 1f),
            Rarity.Rare => new Color(0.31f, 0.76f, 1f),
            _ => new Color(0.62f, 0.70f, 0.78f),
        };

        // ---------------- 拖放 ----------------

        DraggableItem _dragging;
        int _hoverSlot = -1;

        public void NotifyDragStart(DraggableItem it)
        {
            _dragging = it;
            _hoverSlot = -1;
        }

        public void UpdateDropHints(ChainItem item, int fromSlot, int fromChain, PointerEventData e)
        {
            var g = CurrentGun();

            int targetSlot = -1;
            var hits = new List<RaycastResult>();
            if (EventSystem.current != null)
            {
                EventSystem.current.RaycastAll(e, hits);
                for (int i = 0; i < hits.Count; i++)
                {
                    var dt = hits[i].gameObject.GetComponent<SlotDropTarget>();
                    if (dt == null || dt.IsBagArea) continue;
                    targetSlot = dt.SlotIndex;
                    break;
                }
            }

            _hoverSlot = targetSlot;

            // 拖拽时详情区显示「装到这一格会发生什么」——这是原型里最有用的预览：
            // 光看数值说明玩家还得自己在脑子里算，直接把「期望伤害/耗弹/生效目标」
            // 前后对比摆出来，才知道这一格该不该放。
            if (_inv != null && _inv.Detail != null)
            {
                if (g == null || targetSlot < 0 || targetSlot >= g.Slots.Length)
                    _inv.Detail.Show(item);
                else
                    ShowPlacementPreview(g, item, targetSlot);
            }

            if (_hint == null) return;
            if (g == null) { _hint.text = $"拖动中：{ItemName(item)}"; return; }

            if (targetSlot < 0 || targetSlot >= g.Slots.Length)
            {
                _hint.text = $"{ItemName(item)}  ·  拖到槽位内装入，拖到空白处卸下";
                return;
            }

            _hint.text = DescribePlacement(g, item, targetSlot);
        }

        /// <summary>
        /// 把「装到这一格」的前后对比写进常驻详情区。
        ///
        /// 这里必须真的跑一次 Loadout（在槽位副本上），不能用估算：
        /// 读取式链的产出取决于扫描顺序与作用域吸附，纸面加法算不出来。
        /// </summary>
        void ShowPlacementPreview(GunRuntime g, ChainItem item, int cell)
        {
            var detail = _inv.Detail;
            if (detail == null) return;

            // 基线：当前枪的本次期望
            var before = Loadout.ResolveCast(g.Base, g.Slots, g.Stats, 0);

            // 试放：在副本上放到目标格
            var probe = g.Slots.Clone();
            probe.Cells[cell] = item;
            var stats = Loadout.ResolveGun(g.Base, probe);
            var after = Loadout.ResolveCast(g.Base, probe, stats, 0);

            int affected = 0;
            if (!item.IsBullet && item.Mod != null)
            {
                affected = item.Mod.Scope == Scope.Next
                    ? (Loadout.NextBulletCell(cell, probe) >= 0 ? 1 : 0)
                    : Loadout.TargetsOf(item.Mod.Scope, cell, probe).Count;
            }

            float dmgDelta = after.ExpectedDamage - before.ExpectedDamage;
            int ammoDelta = after.AmmoCost - before.AmmoCost;

            detail.Show(item, new ItemDetailPanel.PlacementPreview
            {
                Cell = cell,
                BaselineDamage = before.ExpectedDamage,
                NewDamage = after.ExpectedDamage,
                DamageDelta = dmgDelta,
                AmmoDelta = ammoDelta,
                AffectedTargets = affected,
                HasTarget = item.IsBullet || affected > 0,
            });
        }

        /// <summary>
        /// 把「放到这一格会怎样」写成一句话。
        ///
        /// 必须同时给出「占了什么」「影响了谁」两件事：
        /// 读取式链里最容易犯的错是把被动件放到没有子弹的范围里，
        /// 玩家看到「生效目标 0」就会立刻明白该换个位置。
        /// </summary>
        string DescribePlacement(GunRuntime g, ChainItem item, int cell)
        {
            var scope = item.IsBullet ? "子弹" : ItemTooltip.ScopeName(item.Mod.Scope);

            // 试放一次，看完工后的实际产出
            var probe = g.Slots.Clone();
            probe.Cells[cell] = item;
            var stats = Loadout.ResolveGun(g.Base, probe);
            var cast = Loadout.ResolveCast(g.Base, probe, stats, 0);

            int affected = 0;
            if (!item.IsBullet && item.Mod != null)
            {
                affected = item.Mod.Scope == Scope.Next
                    ? (Loadout.NextBulletCell(cell, probe) >= 0 ? 1 : 0)
                    : Loadout.TargetsOf(item.Mod.Scope, cell, probe).Count;
            }

            string target = item.IsBullet
                ? "本格提供一发子弹"
                : $"生效目标 {affected} 个";

            if (!item.IsBullet && affected == 0)
                return $"✕ 槽 {cell + 1}：{scope}件在此范围没有子弹可作用";

            return $"槽 {cell + 1}：{scope}    {target}    " +
                   $"本次期望 {cast.ExpectedDamage:0.#}    耗弹 {cast.AmmoCost}";
        }

        public void NotifyDragEnd(DraggableItem it, PointerEventData e)
        {
            if (!it.WasDropped && it.FromSlot >= 0) UnequipToBag(it.FromSlot);
            _dragging = null;
            _hoverSlot = -1;
            Refresh();
        }

        public void UnequipToBag(int slotIndex)
        {
            var g = CurrentGun();
            if (g == null || slotIndex < 0 || slotIndex >= g.Slots.Length) return;

            var it = g.Slots.Cells[slotIndex];
            if (it == null) return;
            if (_root.Run.Bag.Count >= _root.Run.BagCapacity)
            {
                _root.HudToast("背包已满，无法卸下");
                return;
            }

            g.Slots.Cells[slotIndex] = null;
            _root.Run.Bag.Add(it);
            g.Recalc();
        }

        /// <summary>
        /// 放下：从背包/其它槽位移到目标槽位。
        /// 目标已有件时**交换**（原型行为）—— 直接覆盖会让玩家无意中丢掉配件，
        /// 而配件是唯一资源，误丢的代价太高。
        /// </summary>
        public void HandleDrop(DraggableItem src, SlotDropTarget dst)
        {
            var g = CurrentGun();
            if (g == null || src == null) return;

            if (dst.IsBagArea)
            {
                if (src.FromSlot >= 0) UnequipToBag(src.FromSlot);
                return;
            }

            int to = dst.SlotIndex;
            if (to < 0 || to >= g.Slots.Length) return;
            if (src.FromSlot == to) { src.WasDropped = true; return; }

            var moving = src.Item;
            var displaced = g.Slots.Cells[to];

            // 从背包来：校验目标格能否接受这个件
            if (src.FromSlot < 0)
            {
                var verdict = Loadout.Validate(g.Slots, to, moving, out string reason);
                if (verdict != Loadout.DropResult.Ok && moving.IsBullet == false)
                {
                    _root.HudToast(reason ?? "无法放置");
                    return;
                }
                _root.Run.RemoveFromBag(moving);
                g.Slots.Cells[to] = moving;

                if (displaced != null) _root.Run.Bag.Add(displaced);
            }
            else
            {
                // 枪内移动：直接交换
                g.Slots.Cells[src.FromSlot] = displaced;
                g.Slots.Cells[to] = moving;
            }

            src.WasDropped = true;
            g.Recalc();
            Refresh();
        }

        public void HandleSlotClick(int slotIndex) { }

        // ---------------- 三选一 ----------------

        public void ShowOffers(List<Offer> offers)
        {
            if (_offerRoot == null || offers == null || offers.Count == 0) return;

            _offer = offers;
            _offerMode = true;
            _bagRoot.SetActive(false);
            _offerRoot.SetActive(true);
            Open = true;
            _root.SetPaused(true);

            // 卡片必须**克隆模板**：Offer 预制体里没有现成卡片，
            // 只提供 OfferRefs.CardTemplate。取现成子节点会永远得到 0 张卡（踩过）。
            if (_offerRefs == null || _offerRefs.CardTemplate == null || _offerRefs.CardArea == null)
            {
                Debug.LogError("[S] Offer.prefab 缺少 CardTemplate / CardArea，三选一无法显示");
                return;
            }

            // 清掉上一轮克隆的卡片
            for (int i = _offerCards.Count - 1; i >= 0; i--)
                if (_offerCards[i] != null) Destroy(_offerCards[i]);
            _offerCards.Clear();

            for (int i = 0; i < offers.Count; i++)
            {
                var item = _data.MakeItem(offers[i].Id);
                if (item == null) continue;

                var go = Instantiate(_offerRefs.CardTemplate, _offerRefs.CardArea);
                go.name = "Offer_" + item.Id;
                go.SetActive(true);
                _offerCards.Add(go);

                var c = go.GetComponent<OfferCardRefs>();
                if (c == null) continue;

                int idx = i;
                c.Bind(item, idx);
                if (c.Button != null)
                {
                    c.Button.onClick.RemoveAllListeners();
                    c.Button.onClick.AddListener(() => PickOffer(idx));
                }
            }
        }

        void PickOffer(int idx)
        {
            if (_offer == null || idx < 0 || idx >= _offer.Count) return;

            if (_root.Run.Bag.Count >= _root.Run.BagCapacity)
            {
                _root.HudToast("背包已满 · 先分解一件再选");
                return;
            }

            _root.Run.TryPick(_offer[idx].Id);
            _offer = null;
            _offerMode = false;
            _offerRoot.SetActive(false);

            if (_root.HasPendingOffers) _root.PopOfferIfAny();
            else Close();
        }

        void Update()
        {
            // 三选一没有键盘快捷键：必须点击（原型同）。
            // 这里只处理 Esc 关闭背包。
        }
    }
}

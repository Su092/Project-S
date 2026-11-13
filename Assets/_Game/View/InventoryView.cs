using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 背包与改装界面。打开即暂停（gameplay-spec §6）。
    /// 交互：拖拽入链 / 拖回背包 / 点击卸下 / 三选一。
    ///
    /// 结构纪律：面板、链区行、枪页签、配件卡、候选卡全部是 `Resources/UI/Prefabs/` 下的预制体。
    /// 本类只做数据绑定（谁在哪一格、显示什么文案、点了做什么），不创建任何可见元素。
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        public bool Open { get; private set; }
        public Transform DragLayer => _dragLayer;

        /// <summary>背包面板是否真的激活（自检用，不能只相信状态位）。</summary>
        public bool IsBagVisible => _bagRoot != null && _bagRoot.activeInHierarchy;

        /// <summary>三选一面板是否真的激活。</summary>
        public bool IsOfferVisible => _offerRoot != null && _offerRoot.activeInHierarchy;

        /// <summary>背包网格中实际存在的卡片数。</summary>
        public int VisibleCardCount => _bagGrid != null ? _bagGrid.childCount : 0;

        /// <summary>链区实际生成的行数（配件位）。</summary>
        public int ChainRowCount
        {
            get
            {
                if (_chainArea == null) return 0;
                int n = 0;
                for (int i = 0; i < _chainArea.childCount; i++)
                    if (_chainArea.GetChild(i).name.StartsWith("Slot")) n++;
                return n;
            }
        }

        /// <summary>三选一面板内的候选卡数（卡片在 CardArea 容器下，不直接挂在面板根上）。</summary>
        public int OfferCardCount
        {
            get
            {
                if (_offerRefs == null || _offerRefs.CardArea == null) return 0;
                int n = 0;
                var area = _offerRefs.CardArea;
                for (int i = 0; i < area.childCount; i++)
                    if (area.GetChild(i).name.StartsWith("Offer")) n++;
                return n;
            }
        }

        /// <summary>
        /// 三选一里成功绑上圆形图标的卡数（自检用）。
        /// 只看卡片数量会漏掉一类回归：卡片生成了、但图标节点没绑到素材
        /// （缺图、预制体丢引用、Bind 抛错）—— 玩家看到的是一排空圆环。
        /// </summary>
        public int OfferIconBoundCount
        {
            get
            {
                if (_offerRefs == null || _offerRefs.CardArea == null) return 0;
                int n = 0;
                var area = _offerRefs.CardArea;
                for (int i = 0; i < area.childCount; i++)
                {
                    var card = area.GetChild(i);
                    if (!card.name.StartsWith("Offer")) continue;
                    var cr = card.GetComponent<OfferCardRefs>();
                    if (cr != null && cr.IconNode != null && cr.IconNode.Item != null
                        && cr.IconNode.Icon != null && cr.IconNode.Icon.sprite != null) n++;
                }
                return n;
            }
        }

        GameRoot _root;
        SData _data;

        Canvas _canvas;
        InvRefs _inv;
        OfferRefs _offerRefs;
        GameObject _bagRoot;
        GameObject _offerRoot;
        RectTransform _bagGrid;
        RectTransform _chainArea;
        Text _hint;
        int _viewGun;
        int _viewSlot;

        Transform _dragLayer;
        Text _gunSummary;
        Text _emptyHint;
        readonly List<GameObject> _spawned = new List<GameObject>();
        // 页签与行克隆出来的对象登记在这里，刷新时统一清掉
        readonly List<GameObject> _cloneSpawned = new List<GameObject>();

        // 三选一当前状态
        List<Offer> _offer;
        bool _offerMode;

        public void Init(GameRoot root)
        {
            _root = root;
            _data = root.Data;
            BuildCanvas();

            // 详情面板：所有界面共用同一份，必须在卡片生成前就位
            ItemTooltip.Ensure(_canvas.transform);

            BuildBag();
            BuildOffer();
            CloseUi();
        }

        // ---------------- 构建 ----------------

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

            // 必须有 EventSystem 才能拖拽。模块由 UiUtil 统一保证（新输入系统）——
            // 若误挂 StandaloneInputModule，光标能动但点击/拖拽全无人处理，表现就是「鼠标没反应」。
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
            _bagRoot = UiPrefab.Spawn(UiPrefab.Inventory, _canvas.transform);
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

            // 落点组件在预制体里，这里只注入 Owner —— 它是运行时对象，无法序列化进预制体
            if (_inv.BagDropPanel != null) _inv.BagDropPanel.Owner = this;
        }

        void BuildOffer()
        {
            _offerRoot = UiPrefab.Spawn(UiPrefab.Offer, _canvas.transform);
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
            _root.SetPaused(true);   // 内部会同步光标（SyncCursorToState）
            Refresh();
        }

        public void Close()
        {
            CloseUi();
            _root.OnInventoryClosed();
        }

        // ---------------- 自动驾驶 ----------------

        /// <summary>
        /// AI 用：自动处理当前弹层。
        /// 三选一在选（背包满了先丢一件再拿）；背包在装（把包里的件塞进链，然后关背包）。
        /// 走的是与真人点击完全相同的 Run.TryPick / Loadout.ValidateInsert 路径，
        /// 因此它跑出来的构筑就是玩家真能做出的构筑。
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

                    // 还有排队的三选一就继续弹，交给下一帧再解
                    if (_root.HasPendingOffers)
                    {
                        _root.PopOfferIfAny();
                        return;
                    }
                }

                _offerRoot.SetActive(false);
                OpenBag();
            }
            else
            {
                AutoEquipOneForAI();
                Close();
            }
        }

        /// <summary>背包满了时丢一件：优先丢最普通的（非子弹、非史诗），实在没有就丢最后一件。</summary>
        void TryDiscardOneForAI()
        {
            var bag = _root.Run.Bag;
            for (int i = bag.Count - 1; i >= 0; i--)
            {
                var it = bag[i];
                if (it.IsBullet || it.Mod == null || it.Mod.Rarity != Rarity.Epic)
                {
                    bag.RemoveAt(i);
                    return;
                }
            }
            if (bag.Count > 0) bag.RemoveAt(bag.Count - 1);
        }

        /// <summary>把背包里第一件能插进去的配件插进当前枪，每帧只插一件（避免一帧装完看不出过程）。</summary>
        void AutoEquipOneForAI()
        {
            var g = CurrentGun();
            if (g == null) return;

            foreach (var item in _root.Run.Bag)
            {
                for (int i = 0; i < g.Slots.Length; i++)
                {
                    var slot = g.Slots[i];
                    if (slot.Chain.Count >= 6) continue;

                    // 子弹往后接、配件往前插（与 UI 拖拽的默认落位一致）
                    int index = item.IsBullet ? slot.Chain.Count : 0;
                    if (Loadout.ValidateInsert(slot, item, index) == null)
                    {
                        _root.Run.Bag.Remove(item);
                        slot.Chain.Insert(index, item);
                        g.Recalc();
                        return;
                    }
                }
            }
        }

        // ---------------- 刷新 ----------------

        void ClearSpawned()
        {
            // 顺序要紧：行是卡片的父节点，先销毁行会让卡片变孤儿，
            // 下一帧再销毁时已不在 _chainArea 下，拖拽层可能残留引用。
            // 所以先清卡片，再清行。
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();

            foreach (var go in _cloneSpawned) if (go != null) Destroy(go);
            _cloneSpawned.Clear();
        }

        void Refresh()
        {
            ClearSpawned();
            RefreshChain();
            RefreshBag();
            RefreshHint();
        }

        void RefreshChain()
        {
            // 清空链区残留（枪页签与行由 ClearSpawned 统一销毁，这里只兜底清空未登记的节点）
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

            // 枪页签：克隆 GunTab 预制体，只改位置与文案
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
                        trefs.Label.text = $"{guns[idx].Resolved.Name}  ({guns[idx].Slots.Length}位)";
                }
            }

            // 枪属性摘要（纯文本，用链区内的常驻节点）
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
                $"弹匣 {g.Ammo}/{g.Resolved.MagSize}   击发 {1f / g.Resolved.FireInterval:0.0}/s   " +
                $"换弹 {g.Resolved.ReloadTime:0.0}s   散射 {g.Resolved.SpreadBase:0.00}°";

            // 各位：克隆 ChainRow 预制体
            float top = -92f;
            float rowH = Mathf.Min(150f, (900f - 240f) / Mathf.Max(1, g.Slots.Length));
            for (int si = 0; si < g.Slots.Length; si++)
            {
                var slot = g.Slots[si];
                float y0 = top - si * (rowH + 10f);

                GameObject row;
                ChainRowRefs rrefs = null;

                if (_inv != null && _inv.ChainRowTemplate != null)
                {
                    row = Instantiate(_inv.ChainRowTemplate, _chainArea);
                    row.name = "Slot" + si;
                    row.SetActive(true);
                    _cloneSpawned.Add(row);
                    rrefs = row.GetComponent<ChainRowRefs>();
                }
                else
                {
                    Debug.LogError("[S] Inventory.prefab 未指定 ChainRowTemplate");
                    return;
                }

                var rowRt = (RectTransform)row.transform;
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 0.5f);
                rowRt.offsetMin = new Vector2(0f, y0 - rowH);
                rowRt.offsetMax = new Vector2(0f, y0);

                bool isNext = g.PeekNextSlot() == si;
                if (rrefs != null)
                {
                    if (rrefs.Background != null)
                        rrefs.Background.color = isNext
                            ? new Color(0.16f, 0.22f, 0.30f, 0.95f)
                            : new Color(0.10f, 0.12f, 0.16f, 0.95f);

                    if (rrefs.Drop != null)
                    {
                        rrefs.Drop.Owner = this;
                        rrefs.Drop.SlotIndex = si;
                        rrefs.Drop.ChainIndex = -1;
                    }

                    if (rrefs.RowLabel != null)
                    {
                        rrefs.RowLabel.text = $"位 {si + 1}{(isNext ? "  ◀ 下一发" : "")}";
                        rrefs.RowLabel.color = isNext
                            ? new Color(1f, 0.85f, 0.4f)
                            : new Color(0.6f, 0.65f, 0.72f);
                    }
                }

                // 链项
                float x = 10f;
                for (int ci = 0; ci < slot.Chain.Count; ci++)
                {
                    var item = slot.Chain[ci];
                    int cIdx = ci;
                    var card = MakeCard(row.transform, item, new Vector2(x, 8f), new Vector2(150f, 84f),
                        fromSlot: si, fromChain: ci);

                    var cd = card.AddComponent<SlotDropTarget>();
                    cd.Owner = this; cd.SlotIndex = si; cd.ChainIndex = ci;

                    x += 156f;

                    // 警告描边：该件无作用目标
                    if (!item.IsBullet && item.Mod != null)
                    {
                        var cast = Loadout.ResolveCast(g.Resolved, slot);
                        foreach (var w in cast.Warnings)
                            if (w.ChainIndex == cIdx)
                            {
                                var cr = card.GetComponent<CardRefs>();
                                if (cr != null && cr.Background != null)
                                    cr.Background.color = new Color(0.55f, 0.18f, 0.2f, 0.95f);
                            }
                    }
                }

                // 行尾追加落点：位置随链长移动
                if (rrefs != null && rrefs.Tail != null)
                {
                    float tx = Mathf.Max(x, 10f);
                    rrefs.Tail.anchorMin = new Vector2(0f, 1f);
                    rrefs.Tail.anchorMax = new Vector2(0f, 1f);
                    rrefs.Tail.pivot = new Vector2(0f, 1f);
                    rrefs.Tail.anchoredPosition = new Vector2(tx, -8f);
                    rrefs.Tail.sizeDelta = new Vector2(60f, 84f);

                    if (rrefs.TailDrop != null)
                    {
                        rrefs.TailDrop.Owner = this;
                        rrefs.TailDrop.SlotIndex = si;
                        rrefs.TailDrop.ChainIndex = -1;
                    }
                }
            }
        }

        void RefreshBag()
        {
            for (int i = _bagGrid.childCount - 1; i >= 0; i--)
                Destroy(_bagGrid.GetChild(i).gameObject);

            // 背包区落点（组件在预制体上，BuildBag 时已注入 Owner）
            foreach (var item in _root.Run.Bag)
            {
                MakeCard(_bagGrid, item, Vector2.zero, new Vector2(150f, 44f), -1, -1, compact: true);
            }

            if (_root.Run.Bag.Count == 0 && _emptyHint == null)
            {
                var go = new GameObject("Empty", typeof(RectTransform));
                go.transform.SetParent(_bagGrid, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(6f, -30f); rt.offsetMax = new Vector2(-6f, 0f);
                _emptyHint = go.AddComponent<Text>();
                _emptyHint.font = UiKit.Font;
                _emptyHint.fontSize = 18;
                _emptyHint.alignment = TextAnchor.UpperLeft;
                _emptyHint.horizontalOverflow = HorizontalWrapMode.Overflow;
                _emptyHint.verticalOverflow = VerticalWrapMode.Overflow;
                _emptyHint.raycastTarget = false;
                _emptyHint.color = new Color(0.45f, 0.5f, 0.58f);
                _emptyHint.text = "（空）";
            }
            else if (_root.Run.Bag.Count > 0 && _emptyHint != null)
            {
                Destroy(_emptyHint.gameObject);
                _emptyHint = null;
            }
        }

        Color RarityColor(Rarity r) => r switch
        {
            Rarity.Epic => new Color(0.78f, 0.42f, 0.95f),
            Rarity.Rare => new Color(0.35f, 0.65f, 0.95f),
            Rarity.Starter => new Color(0.45f, 0.48f, 0.52f),
            _ => new Color(0.42f, 0.52f, 0.42f),
        };

        string ScopeTag(Scope s) => s switch
        {
            Scope.Whole => "全场",
            Scope.After => "后继",
            _ => "枪械",
        };

        string Describe(ChainItem item)
        {
            if (item.IsBullet)
            {
                var b = item.Bullet;
                return $"{b.Name}\n伤 {b.Damage:0.#} · 弹 {b.Ammo} · 散 +{b.SpreadAdd:0.#}°";
            }
            var m = item.Mod;
            var parts = new List<string>();
            if (m.DamageMul != 1f) parts.Add($"伤×{m.DamageMul:0.##}");
            if (m.DamageFlat != 0f) parts.Add($"伤{(m.DamageFlat > 0 ? "+" : "")}{m.DamageFlat:0.#}");
            if (m.CritFlat != 0f) parts.Add($"暴+{m.CritFlat * 100f:0}%");
            if (m.CritDmgFlat != 0f) parts.Add($"暴伤+{m.CritDmgFlat:0.#}");
            if (m.SpreadMul != 1f) parts.Add($"散×{m.SpreadMul:0.##}");
            if (m.LifetimeFlat != 0f) parts.Add($"存{(m.LifetimeFlat > 0 ? "+" : "")}{m.LifetimeFlat:0.#}s");
            if (m.AmmoFlat != 0) parts.Add($"弹{(m.AmmoFlat > 0 ? "+" : "")}{m.AmmoFlat}");
            if (m.SpeedMul != 1f) parts.Add($"速×{m.SpeedMul:0.##}");
            if (m.PierceFlat != 0) parts.Add($"穿透+{m.PierceFlat}");
            if (m.HomingRate != 0f) parts.Add("追踪");
            if (m.SplitCount > 1) parts.Add($"分裂×{m.SplitCount}");
            if (m.SplashAdd != 0f) parts.Add($"溅射+{m.SplashAdd:0.#}m");
            if (m.GunMagAdd != 0) parts.Add($"弹匣+{m.GunMagAdd}");
            if (m.GunFireIntervalMul != 1f) parts.Add($"射速×{1f / m.GunFireIntervalMul:0.##}");
            if (m.GunSpreadMul != 1f) parts.Add($"散×{m.GunSpreadMul:0.##}");
            return $"{m.Name}  [{ScopeTag(m.Scope)}]\n" + string.Join(" · ", parts);
        }

        /// <summary>
        /// 造一张配件卡：克隆 `ItemCard.prefab`，只填内容与挂拖拽组件。
        ///
        /// 为什么连卡片也要预制体：卡片有「框线 + 内部底 + 稀有度条 + 作用域图标 + 文字」
        /// 五层叠加，全在代码里拼的话，任何一层想调位置都得改代码。
        /// 现在这些都写在预制体里，代码只决定颜色与文案。
        /// </summary>
        GameObject MakeCard(Transform parent, ChainItem item, Vector2 pos, Vector2 size,
            int fromSlot, int fromChain, bool compact = false)
        {
            var rar = item.IsBullet ? item.Bullet.Rarity : item.Mod.Rarity;

            var card = UiPrefab.Spawn(UiPrefab.ItemCard, parent);
            if (card == null) return null;

            var rt = (RectTransform)card.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(pos.x + size.x * 0.5f, -pos.y - size.y * 0.5f);
            rt.sizeDelta = size;

            var cr = card.GetComponent<CardRefs>();
            if (cr != null)
            {
                // 卡框：素材是「黑底 + 极细亮边」，缩到小尺寸后边框几乎不可见，
                // 所以预制体里还叠了一层半透明内部底 —— 框线给轮廓，内部底保文字可读。
                if (cr.Background != null)
                    cr.Background.color = UiAssets.CardFrame != null
                        ? new Color(0.75f, 0.82f, 0.92f, 0.85f)
                        : new Color(0.13f, 0.16f, 0.20f, 0.98f);

                // 稀有度色条：tag_* 带描边与小方块，编码更明确；缺图时靠颜色仍然分得出稀有度
                if (cr.Tag != null)
                {
                    var tagSpr = UiAssets.TagForRarity(rar);
                    if (tagSpr != null) cr.Tag.sprite = tagSpr;
                    cr.Tag.color = RarityColor(rar);

                    // 紧凑卡（背包格）条更窄
                    cr.Tag.rectTransform.anchorMin = new Vector2(0f, 0f);
                    cr.Tag.rectTransform.anchorMax = new Vector2(0f, 1f);
                    cr.Tag.rectTransform.offsetMin = Vector2.zero;
                    cr.Tag.rectTransform.offsetMax = new Vector2(compact ? 4f : 6f, 0f);
                }

                // 作用域图标（衍生件才有）：图标 + 文字同时出现，不单靠图标表意。
                // ⚠ 这张 Image 由 `IconNode.ScopeMark` 提供（挂在圆形图标右上角），
                // 所以它的显隐由下面 `IconNode.Bind(..., showScope:)` 统一决定 ——
                // 这里只负责给 sprite 与颜色，不要在这里 enabled = false，会被下一行覆盖掉。
                if (cr.ScopeIcon != null)
                {
                    var icon = (!item.IsBullet && item.Mod != null)
                        ? UiAssets.IconForScope(item.Mod.Scope) : null;
                    if (icon != null) UiAssets.ApplyIcon(cr.ScopeIcon, icon, new Color(1f, 1f, 1f, 0.95f));
                }

                if (cr.Label != null)
                {
                    cr.Label.text = Describe(item);
                    cr.Label.fontSize = compact ? 12 : 13;
                    cr.Label.color = item.IsBullet
                        ? new Color(0.88f, 0.93f, 1f)
                        : new Color(1f, 0.93f, 0.82f);
                }

                // 圆形图标：所有出现配件的位置都用它（本次要求）。
                // compact（背包格）尺寸更小、不画外环、不叠作用域小标
                // —— 44px 高的一行里外环和小标都会挤掉文字。
                if (cr.IconNode != null)
                {
                    float iconSize = compact ? 34f : 48f;
                    cr.IconNode.Bind(item, iconSize, showRing: !compact, showScope: !compact);
                }
            }

            var drag = card.AddComponent<DraggableItem>();
            drag.Owner = this; drag.Item = item;
            drag.FromSlot = fromSlot; drag.FromChain = fromChain;

            // 悬停详情：卡片本体响应。挂在最外层，这样图标和文字区都算"悬停在配件上"。
            var hover = card.AddComponent<ItemHover>();
            hover.Owner = this;
            hover.Item = item;
            hover.Tooltip = ItemTooltip.Instance;

            _spawned.Add(card);
            return card;
        }

        void RefreshHint()
        {
            if (_hint == null) return;
            int n = _root.Run.Bag.Count;
            _hint.text = $"背包 {n}/{_root.Run.BagCapacity}    ·    同名配件同链上限 2   ·   每位链长上限 {Loadout.MaxChain}   ·    拖拽落点会高亮生效目标数";
        }

        // ---------------- 拖放处理 ----------------

        public void NotifyDragStart(DraggableItem it)
        {
            _dragging = it;
            _hoverSlot = -1;
            _hoverChain = -1;
        }

        // 拖拽悬停状态（用于落点高亮与预览）
        DraggableItem _dragging;
        int _hoverSlot = -1;
        int _hoverChain = -1;

        public void UpdateDropHints(ChainItem item, int fromSlot, int fromChain, PointerEventData e)
        {
            if (_hint == null) return;

            var g = CurrentGun();
            if (g == null) { _hint.text = $"拖动中：{ItemName(item)}"; return; }

            // 找到指针当前覆盖的落点
            int targetSlot = -1, targetChain = -1;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(e, hits);
            for (int i = 0; i < hits.Count; i++)
            {
                var dt = hits[i].gameObject.GetComponent<SlotDropTarget>();
                if (dt == null || dt.IsBagArea) continue;
                targetSlot = dt.SlotIndex;
                targetChain = dt.ChainIndex;
                break;
            }

            _hoverSlot = targetSlot;
            _hoverChain = targetChain;

            if (targetSlot < 0 || targetSlot >= g.Slots.Length)
            {
                _hint.text = $"{ItemName(item)}  ·  拖到配件位内装入，拖到空白处丢弃";
                RefreshDropHighlights(null);
                return;
            }

            var slot = g.Slots[targetSlot];
            int at = targetChain < 0 ? slot.Chain.Count : targetChain;
            var pv = Loadout.PreviewInsert(g.Resolved, slot, item, at);

            _hint.text = DescribePreview(item, pv, targetSlot, at);
            RefreshDropHighlights(pv);
        }

        /// <summary>把预览结果写成一句人能读的话。</summary>
        string DescribePreview(ChainItem item, InsertPreview pv, int slotIndex, int at)
        {
            if (!pv.CanInsert)
            {
                return pv.RejectCode switch
                {
                    "chain-full" => $"✕ 该位已满（上限 {Loadout.MaxChain} 项）",
                    "same-limit" => $"✕ 同一条链内同名配件最多 {Loadout.MaxSameIdPerChain} 个",
                    "bad-index" => "✕ 落点无效",
                    _ => "✕ 无法放置",
                };
            }

            string scope = item.IsBullet ? "子弹"
                : pv.IsGunScope ? "枪械"
                : item.Mod.Scope == Scope.Whole ? "全场" : "后继";

            if (!pv.Effective)
                return $"✕ {scope}件在此位置无作用目标（后继件必须放在子弹之前）";

            string dmg;
            if (Mathf.Abs(pv.DamageDelta) < 0.05f)
            {
                dmg = pv.IsGunScope ? "枪属性变化（不占位伤害）" : "伤害不变";
            }
            else
            {
                float pct = pv.DamageDeltaRatio * 100f;
                dmg = $"{pv.BaseDamage:0.#} → {pv.NewDamage:0.#}  ({(pct >= 0 ? "+" : "")}{pct:0.#}%)";
            }

            string ammo = pv.NewAmmoCost != pv.BaseAmmoCost
                ? $"   弹药 {pv.BaseAmmoCost} → {pv.NewAmmoCost}"
                : "";

            string proj = pv.NewProjectiles != pv.BaseProjectiles
                ? $"   弹丸 {pv.BaseProjectiles} → {pv.NewProjectiles}"
                : "";

            return $"位{slotIndex + 1} 插在第 {at + 1} 格   生效目标 {pv.AffectedCount} 个   {dmg}{proj}{ammo}";
        }

        /// <summary>落点高亮：有效=绿框，无效=红框。传 null 清除。</summary>
        void RefreshDropHighlights(InsertPreview? pv)
        {
            for (int i = 0; i < _highlightTargets.Count; i++)
            {
                var img = _highlightTargets[i].Target;
                if (img != null) img.color = _highlightTargets[i].OriginalColor;
            }
            _highlightTargets.Clear();

            if (pv == null || _dragging == null) return;
            var g = CurrentGun();
            if (g == null) return;
            int slotIndex = _hoverSlot;
            if (slotIndex < 0 || slotIndex >= g.Slots.Length) return;

            int at = _hoverChain < 0 ? g.Slots[slotIndex].Chain.Count : _hoverChain;
            var p = pv.Value;

            Color tint;
            if (!p.CanInsert) tint = new Color(0.75f, 0.18f, 0.20f, 0.95f);
            else if (!p.Effective) tint = new Color(0.70f, 0.30f, 0.15f, 0.95f);
            else tint = new Color(0.22f, 0.60f, 0.35f, 0.95f);

            // 高亮目标行
            var row = FindSlotRow(slotIndex);
            if (row != null)
            {
                var img = row.GetComponent<Image>();
                if (img != null) PushHighlight(img, tint);
            }
        }

        struct Highlight
        {
            public Image Target;
            public Color OriginalColor;
        }

        readonly List<Highlight> _highlightTargets = new List<Highlight>();

        void PushHighlight(Image img, Color c)
        {
            _highlightTargets.Add(new Highlight { Target = img, OriginalColor = img.color });
            img.color = c;
        }

        GameObject FindSlotRow(int slotIndex)
        {
            if (_chainArea == null) return null;
            var name = "Slot" + slotIndex;
            for (int i = 0; i < _chainArea.childCount; i++)
                if (_chainArea.GetChild(i).name == name) return _chainArea.GetChild(i).gameObject;
            return null;
        }

        public void NotifyDragEnd(DraggableItem it, PointerEventData e)
        {
            // 若没有任何 DropTarget 接收（放到空白处）→ 从链卸下 / 从背包丢弃
            if (!it.WasDropped)
            {
                if (it.FromSlot >= 0) UnequipToBag(it.FromSlot, it.FromChain);
            }

            // 清掉高亮与悬停状态，否则下次打开背包会残留染色
            _dragging = null;
            _hoverSlot = -1;
            _hoverChain = -1;
            RefreshDropHighlights(null);

            Refresh();
        }

        public void UnequipToBag(int slotIndex, int chainIndex)
        {
            var g = CurrentGun();
            if (g == null || slotIndex < 0 || slotIndex >= g.Slots.Length) return;
            var chain = g.Slots[slotIndex].Chain;
            if (chainIndex < 0 || chainIndex >= chain.Count) return;

            var item = chain[chainIndex];
            if (_root.Run.Bag.Count >= _root.Run.BagCapacity) return;   // 背包满：拒绝卸下

            chain.RemoveAt(chainIndex);
            _root.Run.Bag.Add(item);
            g.Recalc();
            Refresh();
        }

        GunRuntime CurrentGun()
        {
            var guns = _root.Weapons != null ? _root.Weapons.AllGuns : null;
            if (guns == null || guns.Count == 0) return null;
            return guns[Mathf.Clamp(_viewGun, 0, guns.Count - 1)];
        }

        string ItemName(ChainItem i) => i.IsBullet ? i.Bullet.Name : i.Mod.Name;

        public void HandleDrop(DraggableItem src, SlotDropTarget dst)
        {
            src.WasDropped = true;

            // 落回背包区：从链卸下
            if (dst.IsBagArea)
            {
                if (src.FromSlot >= 0) UnequipToBag(src.FromSlot, src.FromChain);
                Refresh();
                return;
            }

            var g = CurrentGun();
            if (g == null) return;
            int slotIndex = dst.SlotIndex;
            if (slotIndex < 0 || slotIndex >= g.Slots.Length) return;
            var slot = g.Slots[slotIndex];

            // 计算目标插入下标
            int insertAt = dst.ChainIndex < 0 ? slot.Chain.Count : dst.ChainIndex;

            // 从链内移动到链内：先摘除（注意下标偏移）
            ChainItem moving = src.Item;
            if (src.FromSlot >= 0)
            {
                var fromChain = g.Slots[src.FromSlot].Chain;
                if (src.FromChain < 0 || src.FromChain >= fromChain.Count) return;
                fromChain.RemoveAt(src.FromChain);
                if (src.FromSlot == slotIndex && src.FromChain < insertAt) insertAt--;
            }
            else
            {
                // 从背包取走
                if (!_root.Run.Bag.Remove(moving)) return;
            }

            var code = Loadout.ValidateInsert(slot, moving, insertAt);
            if (code != null)
            {
                // 被拒：放回原处
                if (src.FromSlot >= 0) g.Slots[src.FromSlot].Chain.Insert(
                    Mathf.Clamp(src.FromChain, 0, g.Slots[src.FromSlot].Chain.Count), moving);
                else _root.Run.Bag.Add(moving);

                _hint.text = code switch
                {
                    "chain-full" => $"该位已满（上限 {Loadout.MaxChain} 项）",
                    "same-limit" => $"同一条链内同名配件最多 {Loadout.MaxSameIdPerChain} 个",
                    "bad-index" => "落点无效",
                    _ => "无法放置",
                };
                Refresh();
                return;
            }

            slot.Chain.Insert(insertAt, moving);
            g.Recalc();
            Refresh();
        }

        public void HandleSlotClick(int slotIndex) { }

        // ---------------- 三选一 ----------------

        public void ShowOffers(List<Offer> offers)
        {
            if (offers == null || offers.Count == 0) { _root.PopOfferIfAny(); return; }
            _offer = offers;
            _offerMode = true;
            _bagRoot.SetActive(false);
            _offerRoot.SetActive(true);
            Open = true;
            _root.SetPaused(true);   // 内部会同步光标

            if (_offerRefs == null) return;

            // 清掉上一轮的候选卡（只清 CardArea 的子节点，标题与底板不动）
            var area = _offerRefs.CardArea;
            for (int i = area.childCount - 1; i >= 0; i--)
                Destroy(area.GetChild(i).gameObject);

            if (_offerRefs.CardTemplate == null)
            {
                Debug.LogError("[S] Offer.prefab 未指定 CardTemplate");
                return;
            }

            int n = offers.Count;
            for (int i = 0; i < n; i++)
            {
                var o = offers[i];
                int idx = i;

                var card = Instantiate(_offerRefs.CardTemplate, area);
                card.name = "Offer" + i;
                card.SetActive(true);

                var cr = card.GetComponent<OfferCardRefs>();
                if (cr == null) continue;

                var item = _data.MakeItem(o.Id);
                string rarName = o.Rarity switch
                {
                    Rarity.Epic => "史诗",
                    Rarity.Rare => "稀有",
                    _ => "常见",
                };

                if (cr.Background != null)
                    cr.Background.color = UiAssets.CardFrame != null
                        ? new Color(0.78f, 0.85f, 0.95f, 0.9f)
                        : new Color(0.11f, 0.13f, 0.17f, 0.98f);

                if (cr.Button != null)
                {
                    cr.Button.onClick.RemoveAllListeners();
                    cr.Button.onClick.AddListener(() => PickOffer(idx));
                }

                // 顶部稀有度条：tag_* 带描边，卡片上更清楚；缺失则纯色
                if (cr.TagTop != null)
                {
                    var tagS = UiAssets.TagForRarity(o.Rarity);
                    if (tagS != null) cr.TagTop.sprite = tagS;
                    cr.TagTop.color = RarityColor(o.Rarity);
                }

                if (cr.RarityName != null)
                {
                    cr.RarityName.text = rarName;
                    cr.RarityName.color = RarityColor(o.Rarity);
                }
                if (cr.CardTitle != null)
                    cr.CardTitle.text = item.IsBullet ? item.Bullet.Name : item.Mod.Name;
                if (cr.Desc != null)
                    cr.Desc.text = Describe(item);

                // 圆形图标 + 悬停详情
                if (cr.IconNode != null) cr.IconNode.Bind(item, 96f, showRing: true, showScope: true);

                var hover = card.AddComponent<ItemHover>();
                hover.Item = item;
                hover.Tooltip = ItemTooltip.Instance;
            }
        }

        void PickOffer(int idx)
        {
            if (_offer == null || idx < 0 || idx >= _offer.Count) return;
            var o = _offer[idx];

            if (_root.Run.Bag.Count >= _root.Run.BagCapacity)
            {
                // 背包满：提示先去丢弃，保留选择界面。
                // 这是瞬时反馈文字，2.5s 后自毁，不值得占一个预制体槽位。
                var t = UiKit.MakeText(_offerRoot.transform, "BagFullTip", 20, TextAnchor.MiddleCenter,
                    new Color(1f, 0.45f, 0.4f),
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(-500f, 120f), new Vector2(500f, 150f));
                t.text = "背包已满：请先进入背包丢弃一个配件";
                Destroy(t.gameObject, 2.5f);
                return;
            }

            _root.Run.TryPick(o.Id);
            _offer = null;
            _offerMode = false;

            if (_root.HasPendingOffers) _root.PopOfferIfAny();
            else
            {
                _offerRoot.SetActive(false);
                OpenBag();   // 选完直接进背包，符合「先进背包再装」的流程
            }
        }

        void Update()
        {
            if (!Open) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame && !_offerMode)
            {
                // 三选一必须做出选择，Esc 不关闭；背包态下关闭
                Close();
            }
        }
    }
}

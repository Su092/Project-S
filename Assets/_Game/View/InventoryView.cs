using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using S.Core;

namespace S.View
{
    /// <summary>Fixed-device sheet music inventory and queued upgrade reward presentation.</summary>
    public sealed class InventoryView : MonoBehaviour
    {
        public bool Open { get; private set; }
        public Transform DragLayer => _dragLayer;

        public bool IsBagVisible => _bagRoot != null && _bagRoot.activeInHierarchy;

        /// <summary>
        /// 旧版背包底部常驻详情区（已停用，悬停改为配件正下方的 ItemTooltip）。
        /// 保留属性以免外部引用编译失败；始终为隐藏状态。
        /// </summary>
        public ItemDetailPanel DetailPanel => _inv != null ? _inv.Detail : null;
        public bool IsOfferVisible => _offerRoot != null && _offerRoot.activeInHierarchy;
        public int VisibleCardCount => _bagGrid != null ? _bagGrid.GetComponentsInChildren<DraggableItem>().Length : 0;

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
                    if (c != null && c.IconNode != null && c.IconNode.Icon != null && c.IconNode.Icon.enabled && c.IconNode.Icon.sprite != null) n++;
                }
                return n;
            }
        }

        GameRoot _root;
        SData _data;
        Canvas _canvas;
        RectTransform _dragLayer;

        GameObject _bagRoot, _offerRoot;
        ScoreWorkbench _workbench;
        public bool RewardsDeferred { get; private set; }
        public bool CanEditAttachments => IsBagVisible && _workbench && _workbench.InRange(_root);
        public ScoreWorkbench ActiveWorkbench => _workbench;
        InvRefs _inv;
        OfferRefs _offerRefs;
        MusicOfferPresentation _musicOffer;
        public MusicOfferPresentation OfferPresentation => _musicOffer;
        RectTransform _chainArea;
        RectTransform _bagGrid;
        Text _hint;

        int _viewGun;
        bool _lastExpanded;
        readonly List<GameObject> _cloneSpawned = new List<GameObject>();

        // 范围预览不修改真实装配；选中持久保留，悬停临时覆盖，拖拽优先。
        ChainItem _selectedItem;
        ItemHover _hoveredItem;
        int _previewSlot = -1;
        readonly List<Image> _rangeFrames = new List<Image>();
        readonly List<Text> _rangeLabels = new List<Text>();
        public int PreviewSourceCell { get; private set; } = -1;
        public int PreviewTargetCount { get; private set; }
        public bool HasScopePreview => PreviewSourceCell >= 0;

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
            scaler.referenceResolution = new Vector2(1920f, 1080f);
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
            _bagRoot = UiPrefab.Spawn("ScoreInventory", _canvas.transform);
            if (_bagRoot == null) return;

            _inv = _bagRoot.GetComponent<InvRefs>();
            if (_inv == null)
            {
                Debug.LogError("[S] ScoreInventory.prefab 缺少 InvRefs（引用表未配置）");
                return;
            }

            var page=_bagRoot.transform.Find("ScorePage").GetComponent<Image>();
            page.sprite=UiAssets.Named("score_page");page.color=Color.white;
            ScoreUiLayout.ApplyControlArt(_inv,UiAssets.Named("score_sort"),UiAssets.Named("score_close"));
            _chainArea = _inv.ChainArea;
            _bagGrid = _inv.BagGrid;
            _hint = _inv.Hint;
            if (_hint != null)
            {
                _hint.fontSize = 14;
                _hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            if (_inv.BagDropPanel != null) _inv.BagDropPanel.Owner = this;

            // 悬停详情改到配件正下方的浮动提示；背包底常驻详情区收回，网格吃满高度。
            if (_inv.Detail != null)
                _inv.Detail.gameObject.SetActive(false);
            _inv.CloseButton.onClick.AddListener(Close);
            _inv.SortButton.onClick.AddListener(SortScore);
            _dragLayer.SetAsLastSibling();
        }

        void BuildOffer()
        {
            _offerRoot = UiPrefab.Spawn("Offer", _canvas.transform);
            if (_offerRoot == null) return;

            _offerRefs = _offerRoot.GetComponent<OfferRefs>();
            _musicOffer = _offerRoot.GetComponent<MusicOfferPresentation>();
            if (!_musicOffer) _musicOffer = _offerRoot.AddComponent<MusicOfferPresentation>();
            if (_offerRefs == null)
                Debug.LogError("[S] Offer.prefab 缺少 OfferRefs（引用表未配置）");

            _offerRoot.SetActive(false);
        }

        // ---------------- 开关 ----------------

        void CloseUi()
        {
            Open = false;
            _workbench = null;
            ClearScopeSelection();
            _dragging = null;
            _hoverSlot = -1;
            // 拖拽中的卡被挂到 DragLayer（不在 bagRoot 下），只关面板会留「幽灵卡」到游戏画面
            ClearDragLayer();
            ItemTooltip.Instance?.Hide();
            if (_bagRoot != null) _bagRoot.SetActive(false);
            if (_offerRoot != null) _offerRoot.SetActive(false);
        }

        void ClearDragLayer()
        {
            if (_dragLayer == null) return;
            for (int i = _dragLayer.childCount - 1; i >= 0; i--)
            {
                var ch = _dragLayer.GetChild(i);
                if (ch != null) Destroy(ch.gameObject);
            }
        }

        public void OpenBag()
        {
            // Legacy callers cannot bypass the fixed map device.
            var station = _root.NearbyWorkbench;
            if (!station || !_root.TryOpenScoreWorkbench(station))
                _root.HudToast("请前往调律谱架装配配件");
        }

        public bool OpenAtWorkbench(ScoreWorkbench station)
        {
            if (!station || !station.InRange(_root) || !_root.RunStarted || _root.RunEnded ||
                _root.Paused || Open || !_bagRoot) return false;
            _root.ExpandAttachmentPreview();
            _workbench = station;
            _viewGun = _root.Weapons.ActiveIndex;
            _offerMode = false;
            _offerRoot.SetActive(false);
            _bagRoot.SetActive(true);
            Open = true;
            _root.SetPaused(true);
            Refresh();
            return true;
        }

        public bool ResumeDeferredOffers()
        {
            RewardsDeferred = false;
            if (_offer == null) return false;
            ShowOffers(_offer);
            return true;
        }

        public void Close()
        {
            if (_offerMode && _musicOffer != null && _musicOffer.Busy) return;
            if (_offerMode && _offer != null)
            {
                // Keep this exact reward set; the player must travel to a device to rearrange.
                RewardsDeferred = true;
                _offerMode = false;
                CloseUi();
                _root.ResumeAfterDeferredOffer();
                _root.HudToast("奖励已保留 · 前往调律谱架整理后继续领取");
                return;
            }
            CloseUi();
            _root.OnInventoryClosed();
        }

        public void DismissForTest()
        {
            _offer = null; _offerMode = false; RewardsDeferred = false;
            CloseUi();
        }

        // ---------------- 自动驾驶 ----------------

        /// <summary>
        /// AI 用：自动处理当前弹层。走的是与真人点击完全相同的路径。
        /// </summary>
        public void AutoResolveForAI()
        {
            if (_offerMode && _offer != null && _offer.Count > 0)
            {
                int needed = _offer[0].IsGun
                    ? (_root.Weapons.GunCount >= _root.Weapons.Capacity ? _root.Weapons.Active.Slots.FilledCount : 0) : 1;
                while (_root.Run.Bag.Count > 0 && _root.Run.Bag.Count + needed > _root.Run.BagCapacity)
                    TryDiscardOneForAI();
                PickOffer(0);
                return;
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
            RefreshScopePreview();
        }

        /// <summary>
        /// 左侧枪械面板：页签 + 属性摘要 + 槽位网格。
        ///
        /// 槽位网格用 GridLayoutGroup 而不是逐格算坐标：
        /// 槽位数在 6~12 之间变化，硬编码坐标会在 12 槽的发射器上溢出。
        /// </summary>
        void RefreshChain()
        {
            _hoveredItem=null; _previewSlot=-1;
            _rangeFrames.Clear();_rangeLabels.Clear();ClearSpawned();
            for(int i=_chainArea.childCount-1;i>=0;i--)
            { var ch=_chainArea.GetChild(i);ch.gameObject.SetActive(false);Destroy(ch.gameObject); }
            var guns=_root.Weapons.AllGuns;
            if(guns.Count==0)return;
            _viewGun=Mathf.Clamp(_viewGun,0,guns.Count-1);
            for(int row=0;row<guns.Count;row++)
            {
                int gi=row;var gun=guns[gi];
                var badge=ScoreUiLayout.Image("Instrument_"+gi,_chainArea,ScoreUiLayout.TL,ScoreUiLayout.TL,
                    new Vector2(0,-row*100-4),new Vector2(72,72),new Color(.12f,.16f,.23f,1),UiAssets.Named("hud_music_badge_mask"));
                badge.preserveAspect=true;badge.raycastTarget=true;
                ScoreUiLayout.Image("Frame",badge.transform,ScoreUiLayout.C,ScoreUiLayout.C,
                    Vector2.zero,new Vector2(72,72),Color.white,UiAssets.Named("hud_portrait_frame")).preserveAspect=true;
                var icon=ScoreUiLayout.Image("Icon",badge.transform,ScoreUiLayout.C,ScoreUiLayout.C,
                    Vector2.zero,new Vector2(58,48),Color.white,UiAssets.Named("hud_gun_"+gun.Base.Id));
                icon.preserveAspect=true;
                var button=badge.gameObject.AddComponent<Button>();button.targetGraphic=icon;
                button.onClick.AddListener(()=>{_viewGun=gi;ClearScopeSelection();RefreshHint();});
                float step=75;
                var rail=ScoreUiLayout.Image("Rail_"+gi,_chainArea,ScoreUiLayout.TL,ScoreUiLayout.TL,
                    new Vector2(80,-row*100),new Vector2(gun.Slots.Length*step+15,80),new Color(.06f,.09f,.14f,.72f));
                ScoreUiLayout.ApplyPreviewRail(rail,UiAssets.Named("score_preview_rail"));
                rail.gameObject.SetActive(_root.AttachmentPreviewExpanded);
                for(int si=0;si<gun.Slots.Length;si++)
                {
                    var slot=ScoreUiLayout.Image("Slot_"+gi+"_"+si,rail.transform,ScoreUiLayout.TL,ScoreUiLayout.TL,
                        new Vector2(9+si*step,-6),new Vector2(68,68),Color.white,UiAssets.Named("hud_attachment_read_ring"));
                    slot.raycastTarget=true;slot.rectTransform.localScale=Vector3.one;
                    var drop=slot.gameObject.AddComponent<SlotDropTarget>();drop.Owner=this;drop.SlotIndex=si;drop.GunIndex=gi;
                    var item=gun.Slots.Cells[si];
                    if(item!=null)
                    {
                        slot.sprite=null;slot.color=new Color(1,1,1,0);
                        MakeCard(slot.transform,item,Vector2.zero,new Vector2(68,68),si,gi);
                    }
                    else ScoreUiLayout.Text("Empty",slot.transform,UiKit.Font,(si+1).ToString(),ScoreUiLayout.C,ScoreUiLayout.C,
                        Vector2.zero,new Vector2(50,24),15).color=new Color(.8f,.8f,.85f);
                    BuildRangeFrame(slot.transform);
                }
            }
        }

        void RefreshBag()
        {
            if(!_bagGrid)return;
            for(int i=_bagGrid.childCount-1;i>=0;i--)
            {var old=_bagGrid.GetChild(i).gameObject;old.SetActive(false);Destroy(old);}
            int columns=8;
            float width=_bagGrid.rect.width>10?_bagGrid.rect.width:1100;
            float step=width/columns;
            int rows=Mathf.Max(4,Mathf.CeilToInt(_root.Run.Bag.Count/(float)columns));
            _bagGrid.sizeDelta=new Vector2(0,rows*116);
            for(int i=0;i<_root.Run.Bag.Count;i++)
            {
                var item=_root.Run.Bag[i];if(item==null)continue;
                MakeCard(_bagGrid,item,new Vector2(i%columns*step+(step-90)*.5f,-i/columns*116),
                    new Vector2(90,100),-1,-1);
            }
        }

        public void SortScore()
        {
            if(!CanEditAttachments || _dragging!=null)return;
            ScoreInventoryRules.Sort(_root.Run.Bag,_data);
            ClearScopeSelection();Refresh();
            _root.HudToast("曲谱已整理");
        }

        void RefreshHint()
        {
            if(!_hint)return;
            _hint.text="";
            _hint.gameObject.SetActive(false);
        }

        /// <summary>造一张配件卡（背包与槽位共用）。</summary>
        GameObject MakeCard(Transform parent,ChainItem item,Vector2 pos,Vector2 size,int fromSlot,int fromChain)
        {
            var rt=ScoreUiLayout.Node("Card_"+item.Id,parent,ScoreUiLayout.TL,ScoreUiLayout.TL,pos,size);
            var bg=rt.gameObject.AddComponent<Image>();bg.color=new Color(1,1,1,0);bg.raycastTarget=true;
            var refs=rt.gameObject.AddComponent<CardRefs>();refs.Background=bg;
            float diameter=fromSlot>=0?68:64;
            var ring=ScoreUiLayout.Image("Border",rt,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,
                new Vector2(diameter,diameter),Color.white,UiAssets.Named(HudV2View.QualityFrame(item)));
            var clip=ScoreUiLayout.Image("Clip",ring.transform,ScoreUiLayout.C,ScoreUiLayout.C,Vector2.zero,
                Vector2.one*(diameter-12),Color.white,UiAssets.Named("hud_map_mask"));
            clip.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var icon=ScoreUiLayout.Image("Icon",clip.transform,ScoreUiLayout.C,ScoreUiLayout.C,Vector2.zero,
                Vector2.one*(diameter-6),Color.white,UiAssets.ItemIcon(item.Id));icon.preserveAspect=true;
            refs.Label=ScoreUiLayout.Text("Label",rt,UiKit.Font,fromSlot>=0?"":item.Name,
                new Vector2(.5f,0),new Vector2(.5f,0),Vector2.zero,new Vector2(size.x+6,fromSlot>=0?18:34),fromSlot>=0?12:14);
            refs.Label.color=new Color(.12f,.15f,.2f);
            refs.Label.gameObject.SetActive(fromSlot<0);
            var drag=rt.gameObject.AddComponent<DraggableItem>();drag.Owner=this;drag.Item=item;drag.FromSlot=fromSlot;drag.FromChain=fromChain;
            var hover=rt.gameObject.AddComponent<ItemHover>();hover.Item=item;hover.Owner=this;hover.Tooltip=ItemTooltip.Instance;
            return rt.gameObject;
        }

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.Epic => new Color(0.78f, 0.49f, 1f),
            Rarity.Rare => new Color(0.31f, 0.76f, 1f),
            _ => new Color(0.62f, 0.70f, 0.78f),
        };

        // ---------------- 作用范围预览 ----------------

        int FindItemCell(ChainItem item)
        {
            var gun = CurrentGun();
            if (item != null && gun != null)
                for (int i = 0; i < gun.Slots.Length; i++)
                    if (ReferenceEquals(gun.Slots.Cells[i], item)) return i;
            return -1;
        }

        SlotState PlacementProbe(GunRuntime gun, ChainItem item, int cell) =>
            Loadout.PreviewPlacement(gun.Slots, item, cell, FindItemCell(item));

        // 覆盖层在卡片上方，但完全不参与射线，不遮挡点击/拖放。
        void BuildRangeFrame(Transform parent)
        {
            var go = new GameObject("EffectRange", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            for (int edge = 0; edge < 4; edge++)
            {
                var bar = new GameObject("Edge" + edge, typeof(RectTransform));
                bar.transform.SetParent(rt, false);
                var br = (RectTransform)bar.transform;
                bool horizontal = edge < 2;
                br.anchorMin = horizontal ? new Vector2(0f, edge) : new Vector2(edge - 2, 0f);
                br.anchorMax = horizontal ? new Vector2(1f, edge) : new Vector2(edge - 2, 1f);
                br.sizeDelta = horizontal ? new Vector2(0f, 3f) : new Vector2(3f, 0f);
                br.anchoredPosition = Vector2.zero;
                var bi = bar.AddComponent<Image>();
                bi.raycastTarget = false;
            }
            var label = new GameObject("RangeLabel", typeof(RectTransform));
            label.transform.SetParent(rt, false);
            var lr = (RectTransform)label.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.sizeDelta = new Vector2(-8f, 20f);
            lr.anchoredPosition = new Vector2(0f, 3f);
            var text = label.AddComponent<Text>();
            text.font = UiKit.Font; text.fontSize = 12; text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            var shadow = label.AddComponent<Shadow>();
            shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(1f, -1f);
            _rangeFrames.Add(image); _rangeLabels.Add(text);
            go.SetActive(false);
        }

        void PaintRangeCell(int cell, Color color, string label)
        {
            if (cell < 0) return;
            var target = _chainArea.Find("Rail_"+_viewGun+"/Slot_"+_viewGun+"_"+cell);
            if(target==null)return;
            var range=target.Find("EffectRange");
            if(!range){BuildRangeFrame(target);range=target.Find("EffectRange");}
            var frame = range.GetComponent<Image>();
            frame.gameObject.SetActive(true);
            frame.color = new Color(color.r, color.g, color.b, 0.12f);
            for (int i = 0; i < 4; i++) frame.transform.GetChild(i).GetComponent<Image>().color = color;
            var rangeText=frame.GetComponentInChildren<Text>(true);rangeText.text=label;rangeText.color=color;
        }

        void ClearRangeFrames()
        {
            PreviewSourceCell = -1;
            PreviewTargetCount = 0;
            foreach (var frame in _rangeFrames)
                if (frame != null) frame.gameObject.SetActive(false);
        }

        void ClearScopeSelection()
        {
            _selectedItem = null;
            _hoveredItem = null;
            _previewSlot = -1;
            ClearRangeFrames();
        }

        public void BeginItemHover(ItemHover card)
        {
            if (_dragging != null || !IsBagVisible) return;
            if(card.TryGetComponent<DraggableItem>(out var d) && d.FromChain>=0) _viewGun=d.FromChain;
            _hoveredItem = card;
            RefreshScopePreview();
        }

        public void EndItemHover(ItemHover card)
        {
            if (_hoveredItem != card) return;
            _hoveredItem = null;
            if (_dragging == null) RefreshScopePreview();
        }

        public void PreviewSlot(int cell, bool entering) => PreviewSlot(cell, entering, _viewGun);
        public void PreviewSlot(int cell, bool entering, int gunIndex)
        {
            if (!IsBagVisible) return;
            if (entering) { _viewGun=gunIndex; _previewSlot = cell; }
            else if (_previewSlot == cell) _previewSlot = -1;
            if (_dragging == null) RefreshScopePreview();
        }

        public void HandleItemClick(DraggableItem card,PointerEventData.InputButton button)
        {
            if(card==null||_dragging!=null||!CanEditAttachments)return;
            if(button==PointerEventData.InputButton.Right)
            {
                if(card.FromSlot>=0) MoveItem(card.Item,-1,-1,null);
                return;
            }
            if(button!=PointerEventData.InputButton.Left)return;
            if(card.FromSlot<0)
            {
                if(ScoreInventoryRules.FillFirst(_root.Run.Bag,_root.Weapons.AllGuns,card.Item,_viewGun,out string reason))
                {ClearScopeSelection();Refresh();}
                else _root.HudToast(reason??"没有可用槽位");
            }
            else {_viewGun=card.FromChain;_selectedItem=card.Item;RefreshScopePreview();}
        }

        bool MoveItem(ChainItem item,int gun,int slot,ChainItem bagTarget)
        {
            if(!CanEditAttachments)return false;
            bool ok=ScoreInventoryRules.Move(_root.Run.Bag,_root.Run.BagCapacity,_root.Weapons.AllGuns,
                item,gun,slot,bagTarget,out string reason);
            if(ok){ClearScopeSelection();Refresh();}
            else _root.HudToast(reason??"无法装配");
            return ok;
        }

        void RefreshScopePreview()
        {
            ClearRangeFrames();
            if (!IsBagVisible || _offerMode) return;
            var gun = CurrentGun();
            if (gun == null) return;
            if (_selectedItem != null && FindItemCell(_selectedItem) < 0 && !_root.Run.Bag.Contains(_selectedItem))
                _selectedItem = null;

            bool placingSelected = _selectedItem != null && FindItemCell(_selectedItem) < 0 && _previewSlot >= 0;
            var item = _dragging != null ? _dragging.Item
                : placingSelected ? _selectedItem : _hoveredItem != null ? _hoveredItem.Item : _selectedItem;
            if (item == null) { RefreshHint(); return; }
            int source = FindItemCell(item);
            int cell = _dragging != null ? _hoverSlot : placingSelected ? _previewSlot : source;
            if (cell < 0 || cell >= gun.Slots.Length)
            {
                if (_hint != null) _hint.text = item.Name + " · " +
                    (item.IsBullet ? "子弹配件" : item.Mod.IsGunOnly ? "全枪属性" : ItemTooltip.ScopeHint(item.Mod.Scope)) +
                    " · 拖到槽位，或左键选中后悬停槽位查看范围";
                return;
            }
            var probe = PlacementProbe(gun, item, cell);
            var range = Loadout.EffectRangeOf(probe, cell);
            var targets = Loadout.AffectedBulletCells(probe, cell);
            bool gunOnly = !item.IsBullet && item.Mod.IsGunOnly;
            foreach (int covered in range)
                PaintRangeCell(covered, targets.Contains(covered) && !gunOnly
                    ? new Color(0.32f, 1f, 0.58f) : new Color(0.35f, 0.72f, 1f),
                    gunOnly ? "全枪" : targets.Contains(covered) ? "生效" : "覆盖");
            PaintRangeCell(cell, new Color(1f, 0.78f, 0.28f), cell == source ? "选中" : "试装位置");
            PreviewSourceCell = cell;
            PreviewTargetCount = targets.Count;
            if (_hint != null)
            {
                var ids = targets.ConvertAll(i => (i + 1).ToString());
                string effect = item.IsBullet ? "本格提供子弹，无配件作用范围"
                    : gunOnly ? "影响整把枪的属性（不依赖范围内有无子弹）"
                    : targets.Count == 0 ? "范围内无子弹，当前没有生效目标"
                    : $"{ItemTooltip.ScopeName(item.Mod.Scope)} · 生效子弹槽：{string.Join("、", ids)}（{targets.Count} 个）";
                _hint.text = $"{item.Name} · 槽 {cell + 1} · {effect}\n金色：配件位置   蓝色：覆盖范围/全枪   绿色：生效子弹   ·   左键选中保留预览";
            }
        }

        // ---------------- 拖放 ----------------

        DraggableItem _dragging;
        int _hoverSlot = -1;

        public void NotifyDragStart(DraggableItem it)
        {
            _dragging = it;
            _selectedItem = it.Item;
            _hoverSlot = -1;
            RefreshScopePreview();
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
                    var dt = hits[i].gameObject.GetComponentInParent<SlotDropTarget>();
                    if (dt == null || dt.IsBagArea) continue;
                    _viewGun = dt.GunIndex;
                    targetSlot = dt.SlotIndex;
                    break;
                }
            }

            g = CurrentGun();
            _hoverSlot = targetSlot;
            RefreshScopePreview();

            // 同名目标优先显示真实合成结果，包括背包中的目标卡。
            foreach (var hit in hits)
            {
                var card = hit.gameObject.GetComponentInParent<DraggableItem>();
                if (card == null || ReferenceEquals(card.Item, item)) continue;
                if (card.Item.Id == item.Id && card.Item.IsBullet == item.IsBullet)
                {
                    var merged = ItemFusion.Preview(item, card.Item);
                    ItemFusion.CanMerge(item, card.Item, out string reason);
                    string text = merged != null ? $"松开合成：2 × Lv.{item.Level} → Lv.{merged.Level} · 成品保留在目标位置" : reason;
                    ItemTooltip.Instance?.Show(merged ?? card.Item, text);
                    if (_hint != null) _hint.text = text;
                    return;
                }
                break;
            }

            // 拖拽时用浮动提示显示「装到这一格会发生什么」（跟手，不钉在原卡片下）
            var tip = ItemTooltip.Instance;
            if (tip != null)
            {
                if (g == null || targetSlot < 0 || targetSlot >= g.Slots.Length)
                    tip.Show(item);
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

            // 范围说明保留在底栏，数值前后对比由浮窗展示。
        }

        /// <summary>
        /// 把「装到这一格」的前后对比写进常驻详情区。
        ///
        /// 这里必须真的跑一次 Loadout（在槽位副本上），不能用估算：
        /// 读取式链的产出取决于扫描顺序与作用域吸附，纸面加法算不出来。
        /// </summary>
        void ShowPlacementPreview(GunRuntime g, ChainItem item, int cell)
        {
            var tip = ItemTooltip.Instance;
            if (tip == null) return;

            // 基线：当前枪的本次期望
            var before = Loadout.ResolveCast(g.Base, g.Slots, g.Stats, 0);

            // 试放：在副本上放到目标格
            var probe = PlacementProbe(g, item, cell);
            var stats = Loadout.ResolveGun(g.Base, probe);
            var after = Loadout.ResolveCast(g.Base, probe, stats, 0);

            int affected = Loadout.AffectedBulletCells(probe, cell).Count;
            bool gunOnly = !item.IsBullet && item.Mod != null && item.Mod.IsGunOnly;

            float dmgDelta = after.ExpectedDamage - before.ExpectedDamage;
            int ammoDelta = after.AmmoCost - before.AmmoCost;
            bool hasTarget = item.IsBullet || gunOnly || affected > 0;

            string hint;
            if (!hasTarget)
            {
                hint = $"✕ 装到槽 {cell + 1} 后没有作用目标，效果会全部落空";
            }
            else
            {
                string sign = dmgDelta >= 0f ? "+" : "";
                string ammo = ammoDelta == 0 ? "耗弹不变" : $"耗弹 {ammoDelta:+0;-0}";
                string targets = item.IsBullet ? "占用 1 个读取" : gunOnly ? "全枪属性生效" : $"生效目标 {affected} 个";
                hint =
                    $"装到槽 {cell + 1}：本次期望 {before.ExpectedDamage:0.#} → {after.ExpectedDamage:0.#}" +
                    $"（{sign}{dmgDelta:0.#}）  {ammo}  {targets}";
            }

            tip.Show(item, hint);
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
            var scope = item.IsBullet ? "子弹" : item.Mod.IsGunOnly ? "全枪属性" : ItemTooltip.ScopeName(item.Mod.Scope);

            // 试放一次，看完工后的实际产出
            var probe = PlacementProbe(g, item, cell);
            var stats = Loadout.ResolveGun(g.Base, probe);
            var cast = Loadout.ResolveCast(g.Base, probe, stats, 0);

            int affected = Loadout.AffectedBulletCells(probe, cell).Count;
            bool gunOnly = !item.IsBullet && item.Mod != null && item.Mod.IsGunOnly;

            string target = item.IsBullet
                ? "本格提供一发子弹"
                : gunOnly ? "影响整把枪" : $"生效目标 {affected} 个";

            if (!item.IsBullet && !gunOnly && affected == 0)
                return $"✕ 槽 {cell + 1}：{scope}件在此范围没有子弹可作用";

            return $"槽 {cell + 1}：{scope}    {target}    " +
                   $"本次期望 {cast.ExpectedDamage:0.#}    耗弹 {cast.AmmoCost}";
        }

        public void NotifyDragEnd(DraggableItem it,PointerEventData e)
        {
            _dragging=null;_hoverSlot=-1;ItemTooltip.Instance?.Hide();
            if(it)Destroy(it.gameObject);
            ClearDragLayer();
            if(IsBagVisible)Refresh();
        }
        public void UnequipToBag(int slotIndex)
        {
            var g=CurrentGun();
            if(g!=null&&slotIndex>=0&&slotIndex<g.Slots.Length)MoveItem(g.Slots.Cells[slotIndex],-1,-1,null);
        }
        public void HandleItemDrop(DraggableItem src,DraggableItem target)
        {
            if(!CanEditAttachments||src==null||target==null||src.WasDropped||src.Owner!=this||target.Owner!=this)return;
            src.WasDropped=true;
            MoveItem(src.Item,target.FromSlot>=0?target.FromChain:-1,target.FromSlot,target.FromSlot<0?target.Item:null);
        }
        public void HandleDrop(DraggableItem src,SlotDropTarget dst)
        {
            if(!CanEditAttachments||src==null||dst==null||src.WasDropped||src.Owner!=this||dst.Owner!=this)return;
            src.WasDropped=true;
            MoveItem(src.Item,dst.IsBagArea?-1:dst.GunIndex,dst.SlotIndex,null);
        }
        public void HandleSlotClick(int slotIndex)
        { _previewSlot=slotIndex;RefreshScopePreview(); }

        // ---------------- 三选一 ----------------

        public void ShowOffers(List<Offer> offers)
        {
            if (_offerRoot == null || offers == null || offers.Count == 0) return;

            ClearScopeSelection();
            _offer = offers;
            RewardsDeferred = false;
            _workbench = null;
            _offerMode = true;
            ClearDragLayer();
            ItemTooltip.Instance?.Hide();
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

            _musicOffer.ResetPresentation();
            // 清掉上一轮克隆的卡片
            for (int i = _offerCards.Count - 1; i >= 0; i--)
                if (_offerCards[i] != null) { _offerCards[i].SetActive(false); Destroy(_offerCards[i]); }
            _offerCards.Clear();

            for (int i = 0; i < offers.Count; i++)
            {
                // 多级升级排队时，前一轮可能已经选走同一把枪；替换为普通配件。
                if (offers[i].IsGun && _root.Weapons.OwnedGunIds.Contains(offers[i].Id))
                {
                    var fallback = _root.Run.Roll(1);
                    if (fallback.Count > 0) offers[i] = fallback[0];
                }
                var item = offers[i].IsGun ? null : _data.MakeItem(offers[i].Id);
                if (!offers[i].IsGun && item == null) continue;

                var go = Instantiate(_offerRefs.CardTemplate, _offerRefs.CardArea);
                go.name = "Offer_" + offers[i].Id;
                go.SetActive(true);
                _offerCards.Add(go);

                var c = go.GetComponent<OfferCardRefs>();
                if (c == null) continue;

                int idx = i;
                if (offers[i].IsGun)
                {
                    bool full = _root.Weapons.GunCount >= _root.Weapons.Capacity;
                    c.BindGun(_data.Guns[offers[i].Id], full
                        ? "替换当前：" + _root.Weapons.Active.Base.Name + "（配件收回背包）"
                        : "领取并装备（增加一把枪）");
                }
                else c.Bind(item, idx);
                if (c.Button != null)
                {
                    c.Button.onClick.RemoveAllListeners();
                    c.Button.onClick.AddListener(() => PickOffer(idx));
                }
            }
            var hud = _root.GetComponentInChildren<HudV2View>(true);
            _musicOffer.Prepare(_offerRefs, _offerCards, offers, _data, hud && hud.CurrentWeapon ? hud.CurrentWeapon.rectTransform : null);
        }

        void PickOffer(int idx)
        {
            if (_offer == null || idx < 0 || idx >= _offer.Count || !_musicOffer.CanChoose) return;
            if (!_root.TryPickOffer(_offer[idx])) return;
            // Commit once, then keep battle paused until both reward flight and staff exit finish.
            _musicOffer.Choose(idx, () =>
            {
                _offer = null;
                _offerMode = false;
                _offerRoot.SetActive(false);
                if (_root.HasPendingOffers) _root.PopOfferIfAny();
                else Close();
            });
        }

        void Update()
        {
            if (IsBagVisible && !CanEditAttachments) { Close(); return; }
            if(IsBagVisible && UnityEngine.InputSystem.Keyboard.current?.rKey.wasPressedThisFrame==true)SortScore();
            if(IsBagVisible && _lastExpanded!=_root.AttachmentPreviewExpanded)
            { _lastExpanded=_root.AttachmentPreviewExpanded;RefreshChain(); }
        }
    }
}

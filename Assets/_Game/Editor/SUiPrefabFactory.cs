using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using S.View;

namespace S.EditorTools
{
    /// <summary>
    /// 生成 UI 预制体资产（`Assets/Resources/UI/Prefabs/`）。
    ///
    /// 定位：**只在编辑器里跑一次的资产作者**，不是运行时构建器。
    /// 跑完之后预制体就是唯一事实来源 —— 改间距、换图、调颜色都在 Prefab 模式下做，
    /// 运行时 `MenuView` / `HudView` / `InventoryView` 只负责填数据与挂事件。
    ///
    /// 为什么不再在运行时拼：界面一旦长在代码里，微调就必须改代码 + 重编译 + 重进 Play，
    /// 而且布局数值分散在几百行 `AddComponent` 中间，评审时看不出「界面长什么样」。
    ///
    /// 幂等：已存在的预制体会被覆盖重建（本脚本是唯一作者，手工改动会被覆盖，
    /// 想保留手工调整就直接改预制体并别再跑本脚本）。
    /// </summary>
    public static class SUiPrefabFactory
    {
        const string PrefabDir = "Assets/Resources/UI/Prefabs";
        const string UiRoot = "Assets/Resources/UI/";

        // ---------------- 基础工具 ----------------

        static Font F()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        static Sprite Sp(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(UiRoot + name + ".png");
            if (s == null) Debug.LogWarning($"[S] 预制体用图缺失: {name}.png");
            return s;
        }

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>锚点 + 四边偏移（与原先 `Rect()` 的语义一致）。</summary>
        static RectTransform Frame(string name, Transform parent, Vector2 aMin, Vector2 aMax,
            Vector2 oMin, Vector2 oMax)
        {
            var go = Node(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        /// <summary>固定尺寸 + 锚点定位（HUD 用：贴角、贴边）。</summary>
        static RectTransform Fixed(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 pos, Vector2 size)
        {
            var go = Node(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static Text Label(string name, Transform parent, string txt, int size, TextAnchor align,
            Color c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var rt = Frame(name, parent, aMin, aMax, oMin, oMax);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = F();
            t.text = txt;
            t.fontSize = size;
            t.alignment = align;
            t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Text LabelFixed(string name, Transform parent, string txt, int size, TextAnchor align,
            Color c, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box)
        {
            var rt = Fixed(name, parent, anchor, pivot, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = F();
            t.text = txt;
            t.fontSize = size;
            t.alignment = align;
            t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Image Pic(string name, Transform parent, Color c, Vector2 aMin, Vector2 aMax,
            Vector2 oMin, Vector2 oMax, Sprite sprite = null, bool sliced = false,
            bool raycast = false)
        {
            var rt = Frame(name, parent, aMin, aMax, oMin, oMax);
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c;
            if (sprite != null)
            {
                i.sprite = sprite;
                i.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                i.pixelsPerUnitMultiplier = 1f;
                if (sliced) i.color = Color.white;
            }
            i.raycastTarget = raycast;
            return i;
        }

        /// <summary>按钮：底图 + Button + ButtonHover（悬停换色，素材在时才有意义）。</summary>
        static Button Btn(RectTransform rt, Image img, Color normal, Color hover, Color pressed)
        {
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var hv = rt.gameObject.AddComponent<ButtonHover>();
            hv.Target = img;
            hv.Normal = normal;
            hv.Hover = hover;
            hv.Pressed = pressed;
            return b;
        }

        static string Save(GameObject root, string name)
        {
            System.IO.Directory.CreateDirectory(PrefabDir);
            var path = PrefabDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        static GameObject LoadPrefab(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + name + ".prefab");

        // ---------------- 主菜单 ----------------

        static GameObject CreateMenu()
        {
            var root = Node("Menu", null);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            var refs = root.AddComponent<MenuRefs>();
            refs.Root = root;

            // 遮罩：必须不透明且吃射线，否则菜单开着还能点到后面的 HUD
            var mask = Pic("Mask", root.transform, new Color(0.035f, 0.042f, 0.055f, 1f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, false, true);

            Pic("Grad", root.transform, new Color(0.055f, 0.070f, 0.092f, 1f),
                new Vector2(0.45f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            Label("Title", root.transform, "P R O J E C T   S", 52, TextAnchor.MiddleLeft,
                new Color(0.95f, 0.85f, 0.55f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(90f, -160f), new Vector2(900f, -90f));

            Label("Subtitle", root.transform, "第一人称射击 · 配件构筑", 22, TextAnchor.MiddleLeft,
                new Color(0.62f, 0.68f, 0.78f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -200f), new Vector2(900f, -160f));

            // 开始
            var startRt = Frame("StartBtn", root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(90f, -310f), new Vector2(430f, -240f));
            var startImg = startRt.gameObject.AddComponent<Image>();
            startImg.sprite = Sp("btn_primary");
            startImg.type = Image.Type.Sliced;
            startImg.pixelsPerUnitMultiplier = 1f;
            startImg.color = Color.white;
            startImg.raycastTarget = true;
            var startBtn = Btn(startRt, startImg, Color.white,
                new Color(0.82f, 0.96f, 1f), new Color(0.66f, 0.84f, 0.96f));
            Label("Label", startRt, "开 始 一 局  (Enter)", 26, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            refs.StartButton = startBtn;

            // 退出
            var quitRt = Frame("QuitBtn", root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(90f, -390f), new Vector2(430f, -320f));
            var quitImg = quitRt.gameObject.AddComponent<Image>();
            quitImg.sprite = Sp("btn_danger");
            quitImg.type = Image.Type.Sliced;
            quitImg.pixelsPerUnitMultiplier = 1f;
            quitImg.color = Color.white;
            quitImg.raycastTarget = true;
            var quitBtn = Btn(quitRt, quitImg, Color.white,
                new Color(1f, 0.86f, 0.86f), new Color(0.96f, 0.72f, 0.72f));
            Label("Label", quitRt, "退 出  (Esc)", 20, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.85f, 0.9f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            refs.QuitButton = quitBtn;

            // ---- 右侧解锁面板 ----
            var panelRt = Frame("UnlockPanel", root.transform,
                new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                new Vector2(10f, 60f), new Vector2(-70f, -120f));
            var panelImg = panelRt.gameObject.AddComponent<Image>();
            panelImg.sprite = Sp("panel");
            panelImg.type = Image.Type.Sliced;
            panelImg.pixelsPerUnitMultiplier = 1f;
            panelImg.color = new Color(1f, 1f, 1f, 0.96f);
            panelImg.raycastTarget = true;
            refs.UnlockPanel = panelRt;

            Label("PanelTitle", panelRt, "局 外 解 锁", 26, TextAnchor.UpperLeft,
                new Color(0.85f, 0.9f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -46f), new Vector2(400f, -12f));

            refs.ShardText = Label("ShardText", panelRt, "", 19, TextAnchor.UpperRight,
                new Color(0.95f, 0.78f, 0.35f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-420f, -46f), new Vector2(-28f, -12f));

            var divRt = Frame("Divider", panelRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -62f), new Vector2(-28f, -58f));
            var divImg = divRt.gameObject.AddComponent<Image>();
            divImg.sprite = Sp("divider");
            divImg.type = Image.Type.Sliced;
            divImg.pixelsPerUnitMultiplier = 1f;
            divImg.color = new Color(1f, 1f, 1f, 0.85f);
            divImg.raycastTarget = false;

            var listRt = Frame("List", panelRt,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(20f, 20f), new Vector2(-20f, -76f));
            refs.UnlockList = listRt;

            // 解锁行模板：保持非激活，运行时克隆（行高由列表高度动态决定）
            var row = CreateMenuRow(listRt);
            row.SetActive(false);
            refs.RowTemplate = row;

            // 左下统计与提示
            refs.StatsText = Label("StatsText", root.transform, "", 17, TextAnchor.LowerLeft,
                new Color(0.55f, 0.62f, 0.72f),
                new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(92f, 96f), new Vector2(-40f, 150f));

            refs.Hint = Label("Hint", root.transform, "", 18, TextAnchor.LowerLeft,
                new Color(0.85f, 0.9f, 1f),
                new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(92f, 52f), new Vector2(-40f, 92f));

            // ---- 音量面板 ----
            var volRt = Frame("VolumePanel", root.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(90f, 168f), new Vector2(470f, 396f));
            var volImg = volRt.gameObject.AddComponent<Image>();
            volImg.sprite = Sp("panel");
            volImg.type = Image.Type.Sliced;
            volImg.pixelsPerUnitMultiplier = 1f;
            volImg.color = new Color(1f, 1f, 1f, 0.92f);
            volImg.raycastTarget = true;

            Label("VolumeTitle", volRt, "音 量", 17, TextAnchor.MiddleLeft,
                new Color(0.72f, 0.78f, 0.88f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -34f), new Vector2(200f, -8f));

            refs.VolumeValues = new Text[4];
            refs.VolumeMinus = new Button[4];
            refs.VolumePlus = new Button[4];

            string[] volNames = { "主音量", "音效", "音乐", "界面音" };
            for (int i = 0; i < 4; i++)
            {
                float y = -44f - i * 44f;

                Label("Name" + i, volRt, volNames[i], 15, TextAnchor.MiddleLeft,
                    new Color(0.8f, 0.85f, 0.92f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, y - 26f), new Vector2(86f, y));

                var rowRt = Frame("Row" + i, volRt,
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(92f, y - 28f), new Vector2(-20f, y));

                refs.VolumeMinus[i] = MakeStepButton(rowRt, "Minus", "−", true);
                refs.VolumePlus[i] = MakeStepButton(rowRt, "Plus", "+", false);

                refs.VolumeValues[i] = Label("Value", rowRt, "", 15, TextAnchor.MiddleCenter, Color.white,
                    new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(46f, 0f), new Vector2(-46f, 0f));
            }

            refs.UnlockPanel.gameObject.SetActive(true);
            return root;
        }

        static Button MakeStepButton(RectTransform parent, string name, string label, bool left)
        {
            var rt = Frame(name, parent,
                new Vector2(left ? 0f : 1f, 0f), new Vector2(left ? 0f : 1f, 1f),
                left ? new Vector2(0f, 2f) : new Vector2(-42f, 2f),
                left ? new Vector2(42f, -2f) : new Vector2(0f, -2f));

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.18f, 0.22f, 0.28f, 0.95f);
            img.raycastTarget = true;
            var btn = Btn(rt, img,
                new Color(0.18f, 0.22f, 0.28f, 0.95f),
                new Color(0.32f, 0.46f, 0.60f, 1f),
                new Color(0.45f, 0.62f, 0.76f, 1f));

            Label("Label", rt, label, 18, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return btn;
        }

        static GameObject CreateMenuRow(Transform parent)
        {
            var rt = Frame("Row", parent, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -72f), new Vector2(0f, 0f));
            var refs = rt.gameObject.AddComponent<MenuRowRefs>();

            refs.Background = rt.gameObject.AddComponent<Image>();
            refs.Background.color = new Color(0.13f, 0.17f, 0.24f, 0.98f);
            refs.Background.raycastTarget = true;

            refs.Frame = Pic("Frame", rt, new Color(0.34f, 0.58f, 0.80f, 0.40f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                Sp("bar_bg"), true);

            refs.Accent = Pic("Accent", rt, new Color(0.35f, 0.62f, 0.85f),
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(4f, 0f));

            // 图标槽（52×52，垂直居中，左内边距 12）
            // 原来用的是 bar_bg（512×96 横向药丸），套在 52×52 方框上：
            // 左右边距 3 拉伸后两端被拉成尖角，中间的"药丸"完全消失。
            // 方形槽必须用方形图 —— slot_empty 是 256×256、圆角 18，52px 下刚好。
            refs.IconSlot = Pic("IconSlot", rt, new Color(0.13f, 0.15f, 0.19f, 0.95f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, -26f), new Vector2(64f, 26f),
                Sp("slot_empty"), true);

            refs.Icon = Pic("Icon", rt, new Color(0.62f, 0.78f, 0.95f, 0.90f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, -18f), new Vector2(56f, 18f));
            refs.Icon.preserveAspect = true;

            refs.BadgeBack = Pic("BadgeBack", rt, new Color(0.06f, 0.07f, 0.09f, 0.95f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(50f, -26f), new Vector2(70f, -6f),
                Sp("slot_filled"), true);

            refs.Badge = Pic("Badge", rt, Color.white,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(50f, -26f), new Vector2(70f, -6f));
            refs.Badge.preserveAspect = true;

            const float btnW = 108f, pad = 16f;
            float textLeft = 76f;
            float textRight = -(btnW + pad * 3f);

            refs.Name = Label("Name", rt, "名称", 19, TextAnchor.MiddleLeft, Color.white,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f),
                new Vector2(textLeft, 0f), new Vector2(textRight, 0f));

            refs.Desc = Label("Desc", rt, "描述", 14, TextAnchor.MiddleLeft,
                new Color(0.72f, 0.78f, 0.88f),
                new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                new Vector2(textLeft, 0f), new Vector2(textRight, 0f));

            var btnRt = Frame("BuyButton", rt,
                new Vector2(1f, 0.22f), new Vector2(1f, 0.78f),
                new Vector2(-(btnW + pad), 0f), new Vector2(-pad, 0f));
            refs.BuyImage = btnRt.gameObject.AddComponent<Image>();
            refs.BuyImage.sprite = Sp("btn_primary");
            refs.BuyImage.type = Image.Type.Sliced;
            refs.BuyImage.pixelsPerUnitMultiplier = 1f;
            refs.BuyImage.color = Color.white;
            refs.BuyImage.raycastTarget = true;

            refs.BuyButton = Btn(btnRt, refs.BuyImage, Color.white,
                new Color(0.85f, 0.95f, 1f), new Color(0.68f, 0.84f, 0.96f));

            refs.BuyLabel = Label("BuyLabel", btnRt, "", 15, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return rt.gameObject;
        }

        // ---------------- HUD ----------------

        static GameObject CreateHud()
        {
            var root = Node("HUD", null);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            var refs = root.AddComponent<HudRefs>();

            // 生命
            refs.HpFill = Bar(root.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 28f), new Vector2(360f, 26f), Sp("bar_bg"), Sp("bar_fill_hp"));

            refs.HpText = LabelFixed("HpText", root.transform, "100", 20, TextAnchor.LowerLeft, Color.white,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 58f), new Vector2(200f, 26f));

            // 弹药
            refs.AmmoText = LabelFixed("AmmoText", root.transform, "36 / 36", 40, TextAnchor.LowerRight,
                Color.white, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-30f, 30f), new Vector2(300f, 50f));

            refs.GunText = LabelFixed("GunText", root.transform, "", 18, TextAnchor.LowerRight,
                new Color(0.7f, 0.78f, 0.9f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-30f, 82f), new Vector2(400f, 24f));

            refs.DpsText = LabelFixed("DpsText", root.transform, "", 16, TextAnchor.LowerRight,
                new Color(0.95f, 0.8f, 0.4f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-30f, 106f), new Vector2(400f, 22f));

            // 轮转序列条（3 位）
            // ⚠ 槽底原来是纯色方块（Image 无 sprite），和旁边有贴图的 HUD 元素
            // 明显不是一套资产。改用 icon_frame（菱形切角框，本作的形状语言）：
            // 它本来就为"图标底座"设计，96px 用在 48×48 上刚好。
            refs.RotationPips = new Image[3];
            refs.RotationLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var pipRt = Fixed("Pip" + i, root.transform,
                    new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-30f - i * 54f, 140f), new Vector2(48f, 48f));
                var img = pipRt.gameObject.AddComponent<Image>();
                img.sprite = Sp("icon_frame");
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = new Color(0.85f, 0.90f, 1f, 0.95f);
                img.raycastTarget = false;
                refs.RotationPips[i] = img;

                refs.RotationLabels[i] = LabelFixed("Label", pipRt, (i + 1).ToString(), 22,
                    TextAnchor.MiddleCenter, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 48f));
            }

            // ---- 当前链的触发进度条（本次新增：链式逐渐触发的可视化）----
            // 位置：轮转条上方。每个配件一个圆形图标，触发过的点亮、未触发的压暗，
            // 这样"链正在一项一项触发"这件事在 HUD 上是可读的（否则玩家只看到弹丸陆续飞出）。
            refs.ChainRoot = Fixed("ChainStrip", root.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-30f, 198f), new Vector2(300f, 44f));

            refs.ChainTitle = LabelFixed("ChainTitle", refs.ChainRoot, "", 14, TextAnchor.MiddleRight,
                new Color(0.72f, 0.8f, 0.92f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 34f), new Vector2(300f, 20f));

            // 6 个格位（链长上限 S.Core.Loadout.MaxChain），按需显隐
            refs.ChainIcons = new ItemIconView[S.Core.Loadout.MaxChain];
            for (int i = 0; i < refs.ChainIcons.Length; i++)
            {
                var node = CreateItemIconNode("ChainIcon" + i, refs.ChainRoot, 40f, hoverable: true);
                var nrt = (RectTransform)node.transform;
                nrt.anchorMin = new Vector2(1f, 0.5f);
                nrt.anchorMax = new Vector2(1f, 0.5f);
                nrt.pivot = new Vector2(1f, 0.5f);
                nrt.anchoredPosition = new Vector2(-i * 46f, 0f);
                node.gameObject.SetActive(false);
                refs.ChainIcons[i] = node;
            }

            // 经验
            refs.XpFill = Bar(root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -28f), new Vector2(360f, 14f), Sp("bar_bg"), Sp("bar_fill_xp"));

            refs.LevelText = LabelFixed("LevelText", root.transform, "LV 1", 20, TextAnchor.UpperLeft,
                Color.white, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -50f), new Vector2(200f, 26f));

            // 威胁（竖向）
            // 底槽是 16×320 的细竖条。原来用 bar_bg（512×96 横向药丸）：
            // 左右边距各 3，在 16 宽里占掉 6，剩下 10px 被拉成一条竖线，
            // 横向的"内凹压暗"也被转成竖向，看起来像块脏污。
            // 细竖条不需要贴图纹理 —— 纯深色底 + 细描边更清楚，也符合 art-style 的硬边。
            refs.ThreatFill = BarPlain(root.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-30f, 0f), new Vector2(16f, 320f),
                new Color(0.09f, 0.11f, 0.14f, 0.92f), Sp("bar_fill_threat"));
            refs.ThreatFill.type = Image.Type.Filled;
            refs.ThreatFill.fillMethod = Image.FillMethod.Vertical;

            refs.ThreatText = LabelFixed("ThreatText", root.transform, "0", 18, TextAnchor.MiddleRight,
                new Color(0.9f, 0.6f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-54f, -170f), new Vector2(80f, 24f));

            // 准星
            // ⚠ 必须 Simple，不能 Sliced：crosshair 是「准星字形」不是九宫格框。
            // Sliced 会拿四边当固定边距，只拉伸中间那一小块 —— 16px 的控件里
            // 十字线会被拉成中间的粗块、四条臂消失。它现在是按包围盒归一的
            // 128px 图，Simple + 等比缩放才是正确用法。
            var crt = Fixed("Crosshair", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(16f, 16f));
            var cross = crt.gameObject.AddComponent<Image>();
            cross.sprite = Sp("crosshair");
            cross.type = Image.Type.Simple;
            cross.preserveAspect = true;
            cross.pixelsPerUnitMultiplier = 1f;
            cross.color = new Color(1f, 1f, 1f, 0.92f);
            cross.raycastTarget = false;
            refs.Crosshair = cross;

            // 受击红晕
            var vrt = Frame("DamageVignette", root.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            var vig = vrt.gameObject.AddComponent<Image>();
            vig.sprite = Sp("vignette_damage");
            vig.type = Image.Type.Simple;
            vig.color = new Color(1f, 0.25f, 0.22f, 0f);
            vig.raycastTarget = false;
            refs.DamageVignette = vig;

            // toast
            refs.Toast = LabelFixed("Toast", root.transform, "", 26, TextAnchor.MiddleCenter,
                new Color(1f, 0.95f, 0.7f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -110f), new Vector2(900f, 40f));

            // 横幅底板（默认隐藏）
            var brt = Fixed("BannerPlate", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 130f), new Vector2(720f, 88f));
            var plate = brt.gameObject.AddComponent<Image>();
            plate.sprite = Sp("banner");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 1f;
            plate.color = new Color(1f, 1f, 1f, 0.95f);
            plate.raycastTarget = false;
            brt.SetAsFirstSibling();
            brt.gameObject.SetActive(false);
            refs.BannerPlate = plate;

            refs.CenterBig = LabelFixed("CenterBig", root.transform, "", 54, TextAnchor.MiddleCenter,
                Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 130f), new Vector2(1200f, 90f));

            refs.ZoneText = LabelFixed("ZoneText", root.transform, "", 22, TextAnchor.UpperCenter,
                new Color(0.85f, 0.9f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(900f, 30f));

            // Boss 血条（默认隐藏）
            var bbrt = Fixed("BossBar", root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -62f), new Vector2(680f, 30f));
            var bbg = bbrt.gameObject.AddComponent<Image>();
            bbg.sprite = Sp("bar_bg");
            bbg.type = Image.Type.Sliced;
            bbg.pixelsPerUnitMultiplier = 1f;
            bbg.color = new Color(1f, 1f, 1f, 0.9f);
            bbg.raycastTarget = false;
            refs.BossBarRoot = bbrt.gameObject;

            refs.BossFill = BarIn(bbrt, Sp("bar_fill_boss"), new Color(0.78f, 0.32f, 0.72f));

            refs.BossNameText = LabelFixed("BossNameText", root.transform, "", 20, TextAnchor.UpperCenter,
                new Color(0.95f, 0.8f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(900f, 26f));

            bbrt.gameObject.SetActive(false);
            refs.BossNameText.gameObject.SetActive(false);
            return root;
        }

        /// <summary>底槽 + 可裁切填充进度条。</summary>
        static Image Bar(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            Sprite bgSprite, Sprite fillSprite)
        {
            var rt = Fixed("Bar", parent, anchor, pivot, pos, size);
            var b = rt.gameObject.AddComponent<Image>();
            b.sprite = bgSprite;
            b.type = Image.Type.Sliced;
            b.pixelsPerUnitMultiplier = 1f;
            b.color = new Color(1f, 1f, 1f, 0.85f);
            b.raycastTarget = false;
            return BarIn(rt, fillSprite, Color.white);
        }

        /// <summary>
        /// 纯色底槽 + 可裁切填充，不用贴图。
        /// 用于极端长宽比的条（如 16×320 的竖向威胁条）：
        /// 贴图的 9-slice 在这种比例下必定被拉坏，纯色反而干净。
        /// </summary>
        static Image BarPlain(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos,
            Vector2 size, Color bgColor, Sprite fillSprite)
        {
            var rt = Fixed("Bar", parent, anchor, pivot, pos, size);
            var b = rt.gameObject.AddComponent<Image>();
            b.color = bgColor;
            b.raycastTarget = false;
            return BarIn(rt, fillSprite, Color.white);
        }

        static Image BarIn(RectTransform slot, Sprite fillSprite, Color tint)
        {
            var frt = Frame("Fill", slot, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));
            var fill = frt.gameObject.AddComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
            fill.raycastTarget = false;
            if (fillSprite != null)
            {
                fill.sprite = fillSprite;
                fill.color = Color.white;
            }
            else
            {
                fill.color = tint;
            }
            return fill;
        }

        // ---------------- 背包 ----------------

        static GameObject CreateInventory()
        {
            var root = Node("Inventory", null);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            var refs = root.AddComponent<InvRefs>();

            Pic("Mask", root.transform, new Color(0.03f, 0.04f, 0.06f, 0.94f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, false, true);

            Label("Title", root.transform, "配 件 改 装", 34, TextAnchor.MiddleLeft,
                new Color(0.95f, 0.85f, 0.55f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -70f), new Vector2(600f, 40f));

            Label("Subtitle", root.transform,
                "Tab / Esc 关闭   ·   拖拽配件入链   ·   点击链内配件卸下   ·   拖到空白丢弃",
                17, TextAnchor.MiddleLeft, new Color(0.6f, 0.66f, 0.75f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -98f), new Vector2(1200f, 26f));

            // 左：链区
            refs.ChainArea = Frame("ChainArea", root.transform,
                new Vector2(0f, 0f), new Vector2(0.62f, 1f),
                new Vector2(40f, 140f), new Vector2(-10f, -120f));

            refs.ChainRowTemplate = LoadPrefab("ChainRow");
            refs.GunTabTemplate = LoadPrefab("GunTab");

            // 右：背包面板
            var bagPanelRt = Frame("BagPanel", root.transform,
                new Vector2(0.63f, 0f), new Vector2(1f, 1f),
                new Vector2(10f, 60f), new Vector2(-40f, -120f));
            var bagPanel = bagPanelRt.gameObject.AddComponent<Image>();
            bagPanel.sprite = Sp("panel");
            bagPanel.type = Image.Type.Sliced;
            bagPanel.pixelsPerUnitMultiplier = 1f;
            bagPanel.color = new Color(1f, 1f, 1f, 0.96f);
            bagPanel.raycastTarget = true;

            // 背包落点：拖回这里 = 从链卸下
            var bagDrop = bagPanelRt.gameObject.AddComponent<SlotDropTarget>();
            bagDrop.IsBagArea = true;
            bagDrop.SlotIndex = -1;
            bagDrop.ChainIndex = -1;
            refs.BagDropPanel = bagDrop;

            refs.BagGrid = Frame("Grid", bagPanelRt,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(12f, 12f), new Vector2(-12f, -46f));
            var glg = refs.BagGrid.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(150f, 44f);
            glg.spacing = new Vector2(6f, 6f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 3;
            glg.childAlignment = TextAnchor.UpperLeft;

            Label("BagTitle", bagPanelRt, "背包", 20, TextAnchor.MiddleLeft,
                new Color(0.8f, 0.85f, 0.95f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(200f, -6f));

            // 底部提示
            refs.Hint = Label("Hint", root.transform, "", 20, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.9f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 40f), new Vector2(-40f, 90f));

            return root;
        }

        /// <summary>链区里的一行（一个配件位）。高度由运行时按枪的位数分配。</summary>
        static GameObject CreateChainRow()
        {
            var root = Node("ChainRow", null);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, -150f);
            rt.offsetMax = new Vector2(0f, 0f);
            var refs = root.AddComponent<ChainRowRefs>();

            refs.Background = root.AddComponent<Image>();
            refs.Background.color = new Color(0.10f, 0.12f, 0.16f, 0.95f);
            refs.Background.raycastTarget = true;

            refs.Drop = root.AddComponent<SlotDropTarget>();

            refs.RowLabel = Label("RowLabel", root.transform, "", 16, TextAnchor.UpperLeft,
                new Color(0.6f, 0.65f, 0.72f),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -24f), new Vector2(240f, 0f));

            // 行尾追加落点：空槽外观 + "＋"
            var tailRt = Frame("Tail", root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -92f), new Vector2(70f, -8f));
            var tailImg = tailRt.gameObject.AddComponent<Image>();
            tailImg.sprite = Sp("slot_empty");
            tailImg.type = Image.Type.Sliced;
            tailImg.pixelsPerUnitMultiplier = 1f;
            tailImg.color = new Color(1f, 1f, 1f, 0.85f);
            tailImg.raycastTarget = true;
            refs.Tail = tailRt;
            refs.TailImage = tailImg;
            refs.TailDrop = tailRt.gameObject.AddComponent<SlotDropTarget>();

            Label("Plus", tailRt, "+", 30, TextAnchor.MiddleCenter, new Color(0.6f, 0.75f, 0.9f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return root;
        }

        /// <summary>背包上方的枪页签。</summary>
        static GameObject CreateGunTab()
        {
            var root = Node("GunTab", null);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(220f, 52f);
            var refs = root.AddComponent<GunTabRefs>();

            refs.Background = root.AddComponent<Image>();
            refs.Background.color = new Color(0.14f, 0.17f, 0.22f);
            refs.Background.raycastTarget = true;

            refs.Button = Btn(rt, refs.Background,
                new Color(1f, 1f, 1f, 0.92f), new Color(1f, 1f, 1f, 1f),
                new Color(0.85f, 0.92f, 1f, 1f));

            refs.Label = Label("Label", root.transform, "", 17, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, 0f));

            return root;
        }

        // ---------------- 三选一 ----------------

        static GameObject CreateOffer()
        {
            var root = Node("Offer", null);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            var refs = root.AddComponent<OfferRefs>();

            Pic("BG", root.transform, new Color(0.03f, 0.04f, 0.06f, 0.96f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, false, true);

            refs.Title = Label("Title", root.transform, "选 择 一 个 配 件  （先进背包）", 30,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.55f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-600f, -130f), new Vector2(600f, -90f));

            // 卡片横排容器：卡片由预制体克隆进这里
            refs.CardArea = Frame("CardArea", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-600f, -220f), new Vector2(600f, 220f));
            var hl = refs.CardArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 30f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            refs.Root = root;
            refs.CardTemplate = LoadPrefab("OfferCard");
            return root;
        }

        static GameObject CreateItemCard()
        {
            var root = Node("ItemCard", null);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(150f, 84f);
            var refs = root.AddComponent<CardRefs>();

            refs.Background = root.AddComponent<Image>();
            refs.Background.sprite = Sp("card_frame");
            refs.Background.type = Image.Type.Sliced;
            refs.Background.pixelsPerUnitMultiplier = 1f;
            refs.Background.color = new Color(0.75f, 0.82f, 0.92f, 0.85f);
            refs.Background.raycastTarget = true;

            // 内衬：原来是纯色 (0.07,0.09,0.12)，和卡框的 AI 金属不是一套材质，
            // 贴在一起有接缝感。改用 card_inner（同一批 AI 材质、同色系细边）。
            // ⚠ Pic 在 sliced=true 时会把 color 强制设成白色（见该方法），
            // 所以这里传白色、不要指望 tint 生效。
            refs.Inner = Pic("Inner", root.transform, Color.white,
                Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f),
                Sp("card_inner"), true);

            // 稀有度色条：6×84 的竖向细条。
            // 原来给的是 tag_common（256×64 横向标签图），套在 6px 宽的竖条上：
            // 左右边距 1+1 已经吃掉 1/3 宽度，中间的"左端小方块"被拉伸成整条竖线，
            // 观感是一根发虚的渐变条。竖向色条不需要贴图 —— 纯色块正是 art-style 要的。
            refs.Tag = Pic("Tag", root.transform, Color.white,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(6f, 0f));

            // 圆形图标：配件用圆形图标代表（本次要求）。
            // 卡片左侧 48×48，与 HUD 链条、三选一卡片保持同一个视觉元素 —— 玩家在
            // 背包里认熟的形状，进 HUD 还认得出来。
            refs.IconNode = CreateItemIconNode("Icon", root.transform, 48f);
            var iRt = (RectTransform)refs.IconNode.transform;
            iRt.anchorMin = new Vector2(0f, 0.5f);
            iRt.anchorMax = new Vector2(0f, 0.5f);
            iRt.pivot = new Vector2(0f, 0.5f);
            iRt.anchoredPosition = new Vector2(12f, 0f);

            // 用图标节点取代右上角的单色作用域小标：作用域现在叠在圆形图标右上角，
            // 这样"图标 + 作用域"是一个整体，文字区全部留给数值。
            refs.ScopeIcon = refs.IconNode.ScopeMark;

            refs.Label = Label("Label", root.transform, "", 13, TextAnchor.UpperLeft,
                new Color(1f, 0.93f, 0.82f),
                Vector2.zero, Vector2.one, new Vector2(70f, 4f), new Vector2(-6f, -4f));

            return root;
        }

        /// <summary>
        /// 造一个「圆形配件图标」节点（ItemIconView 组件 + 外环 + 图标 + 角标 + 作用域标）。
        /// 背包卡片、三选一卡片、HUD 链条都用它，保证三处形状完全一致。
        ///
        /// `hoverable`：是否让这个图标自己吃鼠标（HUD 链条用）。
        ///   ⚠ 卡片里**不要**打开：卡片根节点已经有一张吃射线的卡框 Image + 一个 ItemHover，
        ///   再给图标挂一个会导致「从图标移到卡面文字」时图标触发 OnPointerExit → 详情消失，
        ///   而卡框并没有重新 Enter，表现为鼠标还在卡上详情却没了。
        ///   一条悬停区只挂一个 ItemHover 是本工程的约定。
        /// </summary>
        static ItemIconView CreateItemIconNode(string name, Transform parent, float size,
            bool hoverable = false)
        {
            var go = Node(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);

            var view = go.AddComponent<ItemIconView>();

            // 可悬停时给根节点一张全透明 Image 当命中面：子层图标都是 raycastTarget=false，
            // 没有它的话鼠标事件无从命中。
            if (hoverable)
            {
                var hit = go.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;
                go.AddComponent<ItemHover>();
            }

            // 外环：icon_frame（切角菱形）稍微放大一圈，作为稀有度色环
            var ringRt = Frame("Ring", go.transform, Vector2.zero, Vector2.one,
                new Vector2(-3f, -3f), new Vector2(3f, 3f));
            var ring = ringRt.gameObject.AddComponent<Image>();
            ring.sprite = Sp("icon_frame");
            ring.type = Image.Type.Simple;
            ring.preserveAspect = true;
            ring.color = new Color(0.6f, 0.7f, 0.85f, 0.85f);
            ring.raycastTarget = false;
            view.Ring = ring;

            // 圆形图标本体（item_* 素材自带暗底圆 + 双环，缩放后仍是对齐的）
            var iconRt = Frame("Circle", go.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-size * 0.42f, -size * 0.42f),
                new Vector2(size * 0.42f, size * 0.42f));
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            view.Icon = icon;

            // 右下角标（HUD 的触发序号用；背包里留空自动隐藏）
            var badgeRt = Frame("Badge", go.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-size * 0.52f, 0f), new Vector2(-size * 0.02f, size * 0.36f));
            var badge = badgeRt.gameObject.AddComponent<Text>();
            badge.font = F();
            badge.text = "";
            badge.fontSize = 14;
            badge.alignment = TextAnchor.LowerRight;
            badge.color = new Color(1f, 0.95f, 0.8f);
            badge.horizontalOverflow = HorizontalWrapMode.Overflow;
            badge.verticalOverflow = VerticalWrapMode.Overflow;
            badge.raycastTarget = false;
            view.Badge = badge;

            // 右上作用域小标（衍生件才有）
            var scopeRt = Frame("ScopeMark", go.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-size * 0.42f, -size * 0.42f), new Vector2(size * 0.06f, size * 0.06f));
            var scope = scopeRt.gameObject.AddComponent<Image>();
            scope.type = Image.Type.Simple;
            scope.preserveAspect = true;
            scope.raycastTarget = false;
            scope.enabled = false;
            view.ScopeMark = scope;

            return view;
        }

        static GameObject CreateOfferCard()
        {
            var root = Node("OfferCard", null);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 440f);
            var refs = root.AddComponent<OfferCardRefs>();

            refs.Background = root.AddComponent<Image>();
            refs.Background.sprite = Sp("card_frame");
            refs.Background.type = Image.Type.Sliced;
            refs.Background.pixelsPerUnitMultiplier = 1f;
            refs.Background.color = new Color(0.78f, 0.85f, 0.95f, 0.9f);
            refs.Background.raycastTarget = true;

            refs.Inner = Pic("Inner", root.transform, Color.white,
                Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f),
                Sp("card_inner"), true);

            refs.Button = root.AddComponent<Button>();
            refs.Button.targetGraphic = refs.Background;

            refs.TagTop = Pic("TagTop", root.transform, Color.white,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(14f, -22f), new Vector2(-14f, -6f), Sp("tag_common"), true);

            refs.RarityName = Label("RarityName", root.transform, "", 15, TextAnchor.UpperRight,
                Color.white, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-140f, -34f), new Vector2(-12f, -10f));

            refs.CardTitle = Label("CardTitle", root.transform, "", 26, TextAnchor.UpperLeft, Color.white,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(18f, -66f), new Vector2(-18f, -22f));

            // 圆形图标：三选一是玩家第一次见到这件配件的地方，图标必须够大（96px），
            // 并且和背包/HD 里同一套形状 —— 靠"圆 + 稀有度色环"建立识别。
            refs.IconNode = CreateItemIconNode("Icon", root.transform, 96f);
            var iconRt = (RectTransform)refs.IconNode.transform;
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -78f);

            refs.Desc = Label("Desc", root.transform, "", 15, TextAnchor.UpperLeft,
                new Color(0.82f, 0.87f, 0.95f),
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(18f, 60f), new Vector2(-18f, -190f));

            refs.PickHint = Label("PickHint", root.transform, "点击拾取", 14, TextAnchor.LowerCenter,
                new Color(0.55f, 0.62f, 0.72f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f), new Vector2(0f, 40f));

            return root;
        }

        // ---------------- 入口 ----------------

        [MenuItem("S/生成 UI 预制体")]
        public static void BuildAll()
        {
            var names = new System.Collections.Generic.List<string>();

            // 顺序即依赖：被引用的模板必须先落盘，后面创建预制体时才能 Load 到。
            names.Add(Save(CreateItemCard(), "ItemCard"));
            names.Add(Save(CreateOfferCard(), "OfferCard"));
            names.Add(Save(CreateChainRow(), "ChainRow"));
            names.Add(Save(CreateGunTab(), "GunTab"));
            names.Add(Save(CreateMenu(), "Menu"));
            names.Add(Save(CreateHud(), "HUD"));
            names.Add(Save(CreateOffer(), "Offer"));
            names.Add(Save(CreateInventory(), "Inventory"));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (var n in names) Debug.Log("[S] UI 预制体: " + n);
            Debug.Log($"RESULT UI 预制体 {names.Count} 个");
        }

        public static void BuildBatch()
        {
            BuildAll();
        }
    }
}

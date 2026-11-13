using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using S.Core;
using S.View;

namespace S.App
{
    /// <summary>
    /// 实机自检：用 -scheck 启动后自动跑一遍核心流程并发截图，然后退出。
    /// 判据写在日志里（SELFCHECK 行），失败必须让退出码非 0。
    ///
    /// 用法：
    ///   S.exe -scheck -screen-width 1600 -screen-height 900 -screen-fullscreen 0
    /// </summary>
    public sealed class SelfCheck : MonoBehaviour
    {
        readonly List<string> _log = new List<string>();
        int _pass, _fail;
        string _shotDir;
        string _logPath;
        bool _finished;
        int _matBaseline;

        public static bool Requested =>
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-scheck") >= 0;

        void Start()
        {
            if (!Requested) { enabled = false; return; }

            // 注意：播放器中 Application.dataPath 是 "<exe目录>/S_Data"，
            // 不是工程目录。所以这里以 exe 所在目录为基准向上找工程根，
            // 否则日志与截图会落在 Build/Windows/ 下，check.ps1 找不到。
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var root = FindProjectRoot(baseDir);

            _shotDir = Path.Combine(root, "Shots");
            Directory.CreateDirectory(_shotDir);
            _logPath = Path.Combine(root, "selfcheck.log");

            StartCoroutine(Guard());
            StartCoroutine(Run());
        }

        /// <summary>从 exe 目录向上找含 Assets 的工程根；找不到就退回 exe 目录。</summary>
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

        /// <summary>超时保护：任何原因卡住都要写日志退出，不能挂死让 CI 等。</summary>
        IEnumerator Guard()
        {
            float t = 0f;
            while (t < 90f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _fail++;
            _log.Add("  FAIL 自检超时（90s 未完成）");
            Finish();
        }

        /// <summary>
        /// 9-slice 边距判据：四边之和必须小于控件尺寸，否则 Sliced 会退化成拉伸。
        /// </summary>
        static bool SpriteBorderFits(Sprite s, Vector2 widget)
        {
            if (s == null) return true;
            var b = s.border;
            return (b.x + b.z) < widget.x && (b.y + b.w) < widget.y;
        }

        void Ok(string name) { _pass++; _log.Add("  PASS " + name); }
        void Bad(string name, string why) { _fail++; _log.Add("  FAIL " + name + " :: " + why); }

        /// <summary>不受 timeScale 影响的等待。暂停期间 timeScale=0，WaitForSeconds 会永久挂起。</summary>
        static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        void Check(string name, bool cond, string why = "")
        {
            if (cond) Ok(name); else Bad(name, why);
        }

        IEnumerator Run()
        {
            // 等 Bootstrap 与 GameRoot 起完
            yield return null;
            yield return null;

            var root = S.View.GameRoot.Instance;
            Check("GameRoot 存在", root != null, "GameRoot.Instance 为 null");
            if (root == null) { Finish(); yield break; }

            var data = root.Data;
            Check("数据已加载", data != null, "SData 为 null");

            // 自检全程免伤：否则战斗段玩家猝死会触发 Lose → SetPaused(true)，
            // 后续所有断言都在暂停态下跑，失败原因被完全掩盖（踩过两次）。
            root.InvulnerableForTest = true;
            Check("数据: 3 把枪", data != null && data.GunOrder.Count == 3, $"{data?.GunOrder.Count}");
            Check("数据: 10 种子弹", data != null && data.Bullets.Count == 10, $"{data?.Bullets.Count}");
            Check("数据: 15 种衍生", data != null && data.Modifiers.Count == 15, $"{data?.Modifiers.Count}");

            // ---- 美术资产接入：模型与贴图必须真的加载到 ----
            {
                string[] models = {
                    "Modle_Enemy_Crawler", "Modle_Enemy_Spitter", "Modle_Enemy_Shield",
                    "Modle_Enemy_Drone", "Modle_Boss_ForgeCore",
                    "Modle_Gun_Auto", "Modle_Gun_Grenade", "Modle_Gun_Single",
                    "Modle_Prop_Crate", "Modle_Prop_Barrel", "Modle_Prop_Pillar", "Modle_Prop_Wall",
                };
                int missing = 0;
                var missNames = new List<string>();
                foreach (var m in models)
                {
                    if (S.View.SAssets.Model(m) == null) { missing++; missNames.Add(m); }
                }
                Check("12 个模型全部加载", missing == 0,
                    $"缺失 {missing}: {string.Join(",", missNames)}");

                string[] texs = { "tex_floor_metal", "tex_wall_panel", "tex_metal_rust", "tex_floor_concrete" };
                int texMissing = 0;
                foreach (var t in texs)
                    if (S.View.SAssets.Tex(t) == null) texMissing++;
                Check("4 张贴图全部加载", texMissing == 0, $"缺失 {texMissing}");

                // 场景里必须真的出现模型实例（不是全部回退成胶囊）。
                // 判定方式：数「非 primitive 网格」的实例数。
                // 不能用对象名——Instantiate(FBX) 后根节点名会被 Unity 改写，
                // 子节点名来自模型内部，与 "Modle_" 前缀无关（踩过）。
                var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
                int nonPrimitive = 0;
                var sampleNames = new List<string>();
                foreach (var r in allRenderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mn = mf.sharedMesh.name;
                    bool isPrim =
                        mn.StartsWith("Cube") || mn.StartsWith("Capsule") ||
                        mn.StartsWith("Sphere") || mn.StartsWith("Cylinder") ||
                        mn.StartsWith("Plane") || mn.StartsWith("Quad");
                    if (!isPrim)
                    {
                        nonPrimitive++;
                        if (sampleNames.Count < 4) sampleNames.Add(mn);
                    }
                }
                // 更硬的判据：模型实例的三角面数应显著高于 primitive 回退体
                Check("场景中存在模型实例", nonPrimitive > 0, $"非 primitive 网格数={nonPrimitive}");
                _log.Add($"  INFO 模型网格样例: {string.Join(", ", sampleNames)}");
            }

            // ---- UI 素材接入 ----
            {
                int uiLoaded = S.View.UiAssets.LoadedCount();
                // 原来是 >= 11 的弱判据：新增素材全失败也照样通过（踩过）。
                // 现在按实际张数精确断言，并逐个点名缺了哪张。
                var uiMissing = new List<string>();
                void Need(string label, UnityEngine.Sprite sp)
                {
                    if (sp == null) uiMissing.Add(label);
                }
                Need("panel", S.View.UiAssets.Panel);
                Need("card_frame", S.View.UiAssets.CardFrame);
                Need("card_inner", S.View.UiAssets.CardInner);
                Need("bar_frame", S.View.UiAssets.BarFrame);
                Need("banner", S.View.UiAssets.Banner);
                Need("vignette", S.View.UiAssets.Vignette);
                Need("keycap", S.View.UiAssets.Keycap);
                Need("crosshair", S.View.UiAssets.Crosshair);
                Need("icon_bullet", S.View.UiAssets.IconBullet);
                Need("icon_whole", S.View.UiAssets.IconWhole);
                Need("icon_after", S.View.UiAssets.IconAfter);
                Need("icon_gun", S.View.UiAssets.IconGun);
                // 第二批
                Need("btn_primary", S.View.UiAssets.BtnPrimary);
                Need("btn_primary_hover", S.View.UiAssets.BtnPrimaryHover);
                Need("btn_danger", S.View.UiAssets.BtnDanger);
                Need("slot_empty", S.View.UiAssets.SlotEmpty);
                Need("slot_filled", S.View.UiAssets.SlotFilled);
                Need("bar_bg", S.View.UiAssets.BarBg);
                Need("bar_fill_hp", S.View.UiAssets.BarHp);
                Need("bar_fill_xp", S.View.UiAssets.BarXp);
                Need("bar_fill_boss", S.View.UiAssets.BarBoss);
                Need("bar_fill_threat", S.View.UiAssets.BarThreat);
                Need("icon_frame", S.View.UiAssets.IconFrame);
                Need("divider", S.View.UiAssets.Divider);
                Need("hit_marker", S.View.UiAssets.HitMarker);
                Need("vignette_damage", S.View.UiAssets.VignetteDamage);
                Need("tag_starter", S.View.UiAssets.TagForRarity(S.Core.Rarity.Starter));
                Need("tag_common", S.View.UiAssets.TagForRarity(S.Core.Rarity.Common));
                Need("tag_rare", S.View.UiAssets.TagForRarity(S.Core.Rarity.Rare));
                Need("tag_epic", S.View.UiAssets.TagForRarity(S.Core.Rarity.Epic));
                Need("icon_lock", S.View.UiAssets.IconLock);
                Need("icon_check", S.View.UiAssets.IconCheck);
                Need("icon_arrow", S.View.UiAssets.IconArrow);

                Check("UI 素材全部加载", uiMissing.Count == 0,
                    $"缺失 {uiMissing.Count}: {string.Join(",", uiMissing)}");
                _log.Add($"  INFO UI 素材已加载 {uiLoaded}/32");

                // ---- 配件圆形图标（本次要求：每个配件一张素材）----
                // 判据用「数据里实际存在的配件 id」逐个点名检查，而不是数个数：
                // 数个数时若某两张图互相覆盖（旧命名残留），数量照样对得上，
                // 但界面上会出现"两件配件长一样"。所以逐个 id 断言，缺哪件直接报出来。
                {
                    var missingIcons = new List<string>();
                    var sdata = S.Core.SDataLoader.Get();
                    int wanted = 0;
                    foreach (var b in sdata.Bullets.Values)
                    {
                        wanted++;
                        if (S.View.UiAssets.ItemIcon(b.Id) == null) missingIcons.Add("b:" + b.Id);
                    }
                    foreach (var m in sdata.Modifiers.Values)
                    {
                        wanted++;
                        if (S.View.UiAssets.ItemIcon(m.Id) == null) missingIcons.Add("m:" + m.Id);
                    }
                    Check("每个配件都有圆形图标素材", missingIcons.Count == 0,
                        $"共 {wanted} 件，缺失 {missingIcons.Count}: {string.Join(",", missingIcons)}");
                    _log.Add($"  INFO 配件图标已加载 {S.View.UiAssets.ItemIconCount(sdata.Bullets.Keys)}" +
                             $"+{S.View.UiAssets.ItemIconCount(sdata.Modifiers.Keys)}/{wanted}");
                }

                // 9-slice 边距必须小于目标控件尺寸，否则圆角会被拉变形。
                // ⚠ 判据用的是「控件真实像素」，不是贴图尺寸 —— 之前只用贴图尺寸判断，
                // 结果 512×96 的条看着很安全，实际控件只有 360×14，边距 48 直接吃光高度。
                // 尺寸取自 prefab 实测最小值，改布局时必须同步改这里（见 tools/audit_ui_slices.py）。
                // bar_frame 当前没有任何预制体引用（audit_ui_slices.py 可证），
                // 它只是一张备用条框素材。所以这里不能拿 60×14 当判据 —— 那个尺寸
                // 是当初抄 bar_bg 的，实际根本不存在这样的控件。
                // 真实约束是：边距合计必须小于贴图自身高度，否则自己就不可切片。
                {
                    var bf = S.View.UiAssets.BarFrame;
                    bool bfOk = true;
                    if (bf != null)
                    {
                        var b = bf.border;
                        float th = bf.texture != null ? bf.texture.height : bf.rect.height;
                        bfOk = (b.y + b.w) < th;
                        _log.Add($"  INFO bar_frame border=({b.x},{b.y},{b.z},{b.w}) texH={th}");
                    }
                    Check("条槽边距合法", bfOk,
                        "bar_frame 边距合计超过自身贴图高度，图自身不可切片");
                }
                Check("卡框边距合法", S.View.UiAssets.CardFrame == null ||
                    SpriteBorderFits(S.View.UiAssets.CardFrame, new Vector2(150f, 84f)),
                    "card_frame 四边之和超出最小控件 150×84 的 ItemCard");
                Check("面板边距合法", S.View.UiAssets.Panel == null ||
                    SpriteBorderFits(S.View.UiAssets.Panel, new Vector2(380f, 228f)),
                    "panel 四边之和超出最小控件 380×228 的 VolumePanel");
                Check("横幅边距合法", S.View.UiAssets.Banner == null ||
                    SpriteBorderFits(S.View.UiAssets.Banner, new Vector2(720f, 88f)),
                    "banner 四边之和超出最小控件 720×88 的 BannerPlate");
                // 下面这批是上次漏掉的：按钮、条底槽、槽位、稀有度标签。
                // 它们的最小控件分别是 108×40 / 360×14 / 20×20 / 272×16，
                // 边距一旦超过其中一半，Unity 会钳制边距并把圆角拉成直角。
                Check("按钮边距合法", S.View.UiAssets.BtnPrimary == null ||
                    SpriteBorderFits(S.View.UiAssets.BtnPrimary, new Vector2(108f, 40f)),
                    "btn_primary 边距吃光 108×40 的背包按钮");
                Check("条底槽边距合法", S.View.UiAssets.BarBg == null ||
                    SpriteBorderFits(S.View.UiAssets.BarBg, new Vector2(360f, 14f)),
                    "bar_bg 边距吃光 360×14 的 XP 条");
                // 把实际读数打出来：断言失败时没有原始数值，排查会变成猜谜（踩过）。
                {
                    var bb = S.View.UiAssets.BarBg;
                    if (bb != null)
                        _log.Add($"  INFO bar_bg border=({bb.border.x},{bb.border.y}," +
                                 $"{bb.border.z},{bb.border.w}) rect={bb.rect.width}x{bb.rect.height}");
                    var bp = S.View.UiAssets.BtnPrimary;
                    if (bp != null)
                        _log.Add($"  INFO btn_primary border=({bp.border.x},{bp.border.y}," +
                                 $"{bp.border.z},{bp.border.w})");
                }
                Check("槽位边距合法", S.View.UiAssets.SlotFilled == null ||
                    SpriteBorderFits(S.View.UiAssets.SlotFilled, new Vector2(20f, 20f)),
                    "slot_filled 边距吃光 20×20 的徽章底");
                Check("标签边距合法", S.View.UiAssets.TagForRarity(S.Core.Rarity.Common) == null ||
                    SpriteBorderFits(S.View.UiAssets.TagForRarity(S.Core.Rarity.Common),
                        new Vector2(272f, 16f)),
                    "tag 边距吃光 272×16 的稀有度标签");

                // 图标必须走加色材质（黑底才不会显示成黑方块）
                if (S.View.UiAssets.IconWhole != null)
                {
                    var mat = Resources.Load<Material>("UI/mat_ui_additive");
                    Check("UI 加色材质存在", mat != null, "Resources/UI/mat_ui_additive 缺失");
                }
            }

            // 等 2 秒让敌人刷出来
            yield return WaitUnscaled(2.2f);
            yield return Shot("01-start");

            Check("玩家存在", root.PlayerTransform != null);
            Check("枪械已初始化", root.Weapons != null && root.Weapons.GunCount == 2,
                $"GunCount={root.Weapons?.GunCount}");
            Check("起手枪有链", root.Weapons != null && root.Weapons.Active.PeekNextSlot() == 0,
                "第一位应为非空");

            // 场景里刷出了敌人
            int enemies = root.Enemies.Count;
            Check("已刷出敌人", enemies > 0, $"Enemies={enemies}");

            // ---- 开火：直接驱动武器系统，验证从链到场景弹丸的链路 ----
            var gun = root.Weapons.Active;

            // 弹药与换弹必须一起归位，不能只清 ReloadLeft。
            // 开局这段是真机在跑：Ammo 可能已经打空并进入自动换弹，
            // 此时只把 ReloadLeft 清零会得到「不在换弹、但弹匣是 0」的状态，
            // TryFire 直接返回 no-ammo，而这条失败是随机出现的（踩过：3 次里红 1 次）。
            gun.ReloadLeft = 0f;
            gun.Ammo = gun.Resolved.MagSize;
            gun.FireCooldown = 0f;               // 清掉开局的射击冷却，否则首测会判 cooldown
            int ammoBefore = gun.Ammo;
            var cast = gun.TryFire(out var reason);
            Check("开火成功", cast != null, $"reason={reason}");
            Check("开火扣弹", cast != null && gun.Ammo == ammoBefore - cast.AmmoCost,
                $"{ammoBefore} -> {gun.Ammo}, cost={cast?.AmmoCost}");
            Check("开火产出弹丸", cast != null && cast.Projectiles.Count > 0,
                $"count={cast?.Projectiles.Count}");
            Check("轮转指针前进", gun.Cursor == 1, $"cursor={gun.Cursor}");

            yield return WaitUnscaled(0.35f);
            yield return Shot("02-fired");

            // ---- 链式逐渐触发（§1.2）：真的在时间轴上逐项兑现，而不是一次性铺完 ----
            {
                var g2 = root.Weapons.Active;

                // 造一条 3 项的链：3 项 × 击发间隔(0.11s) ≈ 0.22s 才能全部触发，
                // 所以 0.06s 时必然只触发了一部分 —— 这就是"逐渐"的硬判据。
                var s = new SlotState();
                s.Chain.Add(data.MakeItem("m_dmg_up"));
                s.Chain.Add(data.MakeItem("b_slug"));
                s.Chain.Add(data.MakeItem("m_crit_up"));
                var plan = Loadout.ResolveCast(g2.Resolved, s);
                Check("链式计划覆盖全链", plan.Steps.Count == 3, $"steps={plan.Steps.Count}");

                // 只驱动 ChainCast 本身，用空 spawn 避免污染场景
                var runner = new S.View.ChainCast(plan, g2.Resolved);
                int spawned = 0;
                runner.Tick(0.001f, d => { spawned++; return null; }, null);
                int afterFirst = runner.FiredCount;
                runner.Tick(0.03f, d => { spawned++; return null; }, null);
                Check("链式触发首帧只走第一步", afterFirst == 1, $"fired={afterFirst}");

                runner.Tick(1.0f, d => { spawned++; return null; }, null);
                Check("链式触发最终全部走完", runner.FiredCount == 3,
                    $"fired={runner.FiredCount} spawned={spawned}");
                Check("链式触发只生成一个子弹", spawned == 1, $"spawned={spawned}");
                Check("链式触发已结束", runner.Finished);

                // 重叠施法：连发枪每 0.11 s 扣一次扳机，而一条 5 项链要 0.55 s 才跑完。
                // 若新的一发取消旧的，长链永远只走到第 2 项 —— 这是最容易"看起来正常"的错误。
                {
                    int before = root.Weapons.ActiveCastCount;
                    var s2 = new SlotState();
                    for (int k = 0; k < 5; k++)
                        s2.Chain.Add(data.MakeItem(k == 1 ? "b_slug" : "m_dmg_up"));
                    var plan2 = Loadout.ResolveCast(g2.Resolved, s2);
                    root.Weapons.SpawnCastForAI(plan2);
                    yield return WaitUnscaled(0.02f);
                    root.Weapons.SpawnCastForAI(plan2);
                    yield return WaitUnscaled(0.04f);
                    Check("重叠施法可并存", root.Weapons.ActiveCastCount >= 2,
                        $"before={before} now={root.Weapons.ActiveCastCount}");
                    yield return WaitUnscaled(1.2f);
                    Check("重叠施法最终回收", root.Weapons.ActiveCastCount == 0,
                        $"now={root.Weapons.ActiveCastCount}");
                }

                // 顺手截一张"多项目链"的 HUD：起手链只有 1 项，看不出排布与点亮。
                // 临时改链只是为了截图，截完必须还原 —— 否则后面所有"轮转/位/伤害"
                // 断言都会跑在被改过的枪上（这类污染比漏测更难查）。
                var live = g2.Slots[g2.PeekNextSlot()];
                var backup = new List<ChainItem>(live.Chain);
                live.Chain.Clear();
                foreach (var id in new[] { "m_dmg_up", "b_slug", "m_crit_up", "b_plasma", "m_split" })
                {
                    var it = data.MakeItem(id);
                    if (it != null) live.Chain.Add(it);
                }
                yield return WaitUnscaled(0.12f);
                yield return Shot("02b-chain");
                live.Chain.Clear();
                live.Chain.AddRange(backup);
                yield return WaitUnscaled(0.08f);
            }

            // ---- 改装：装配配件后重算，验证 UI 数据源 ----
            var slot1 = gun.Slots[1];
            bool insMod = Loadout.Insert(slot1, data.MakeItem("m_dmg_up"));
            bool insBullet = Loadout.Insert(slot1, data.MakeItem("b_heavy"));
            Check("装配衍生件", insMod);
            Check("装配子弹件", insBullet);
            gun.Recalc();

            var cast2 = Loadout.ResolveCast(gun.Resolved, slot1);
            Check("改装后该位有弹丸", cast2.Projectiles.Count == 1, $"count={cast2.Projectiles.Count}");
            Check("改装后伤害提升", cast2.Projectiles.Count > 0 && cast2.Projectiles[0].Damage > 26f,
                $"damage={cast2.Projectiles[0].Damage}");

            // ---- 拖拽落点预览（实机 API 可用性，不依赖界面交互）----
            {
                var probe = new SlotState();
                probe.Chain.Add(data.MakeItem("b_slug"));

                var pvGood = Loadout.PreviewInsert(gun.Resolved, probe, data.MakeItem("m_dmg_up"), 0);
                Check("预览: 全场件有效", pvGood.Effective && pvGood.AffectedCount == 1,
                    $"eff={pvGood.Effective} n={pvGood.AffectedCount}");
                Check("预览: 伤害上升", pvGood.DamageDelta > 0f, $"delta={pvGood.DamageDelta}");

                var pvBad = Loadout.PreviewInsert(gun.Resolved, probe, data.MakeItem("m_dmg_flat"), 1);
                Check("预览: 后继件放子弹后无目标", !pvBad.Effective && pvBad.AffectedCount == 0,
                    $"eff={pvBad.Effective} n={pvBad.AffectedCount}");

                Check("预览: 不修改原链", probe.Chain.Count == 1, $"len={probe.Chain.Count}");
            }

            // ---- 经验与升级 ----
            int lvBefore = root.Run.Level;
            int ups = root.Run.AddXp(RunState.NeedFor(1) + 5);
            Check("升级触发", ups >= 1 && root.Run.Level == lvBefore + 1,
                $"ups={ups} level={root.Run.Level}");

            var offers = root.Run.Roll(3);
            Check("三选一生成 3 个", offers.Count == 3, $"count={offers.Count}");
            Check("三选一无重复",
                offers.Count == 3 && offers[0].Id != offers[1].Id &&
                offers[1].Id != offers[2].Id && offers[0].Id != offers[2].Id);

            // ---- 背包与暂停界面 ----
            int bagBefore = root.Run.Bag.Count;
            root.Run.TryPick(offers[0].Id);
            Check("拾取进背包", root.Run.Bag.Count == bagBefore + 1,
                $"{bagBefore} -> {root.Run.Bag.Count}");

            root.Weapons.Player.InputLocked = true;
            var inv = Object.FindAnyObjectByType<S.View.InventoryView>();
            Check("背包 UI 存在", inv != null);
            if (inv != null)
            {
                inv.OpenBag();
                _log.Add($"  INFO OpenBag 后 timeScale={Time.timeScale} Paused={root.Paused}");
                yield return WaitUnscaled(0.25f);
                Check("背包打开即暂停", Mathf.Approximately(Time.timeScale, 0f),
                    $"timeScale={Time.timeScale} Paused={root.Paused}");

                // 必须验证界面真的画出来了，而不是只验证状态位
                Check("背包面板可见", inv.IsBagVisible, "BagRoot 未激活");
                Check("背包有可交互卡片", inv.VisibleCardCount > 0,
                    $"cards={inv.VisibleCardCount}");
                Check("背包链区已生成", inv.ChainRowCount > 0, $"rows={inv.ChainRowCount}");

                yield return Shot("03-inventory");

                inv.Close();
                yield return WaitUnscaled(0.15f);
                Check("背包关闭恢复时间", Time.timeScale > 0.99f, $"timeScale={Time.timeScale}");
            }
            root.Weapons.Player.InputLocked = false;

            // ---- 三选一界面 ----
            if (inv != null)
            {
                inv.ShowOffers(offers);
                yield return WaitUnscaled(0.25f);
                Check("三选一界面可见", inv.IsOfferVisible, "OfferRoot 未激活");
                Check("三选一渲染出 3 张卡", inv.OfferCardCount == 3,
                    $"cards={inv.OfferCardCount}");
                yield return Shot("04-offer");
                Check("三选一卡有圆形图标", inv.OfferIconBoundCount == 3,
                    $"bound={inv.OfferIconBoundCount}");
                inv.Close();
                yield return WaitUnscaled(0.1f);
            }

            // ---- 音效 / 材质池 / 后处理 ----
            {
                var sfx = SfxBank.Instance;
                Check("音效库已初始化", sfx != null, "SfxBank.Instance 为 null");
                if (sfx != null)
                {
                    Check("合成音效 ≥12 个", sfx.ClipCount >= 12, $"clip={sfx.ClipCount}");
                    Check("音源池 ≤16", sfx.PoolSizeValue <= 16 && sfx.PoolSizeValue > 0,
                        $"pool={sfx.PoolSizeValue}");
                }

                var vol = GameObject.Find("PostFxVolume");
                Check("后处理 Volume 存在", vol != null, "找不到 PostFxVolume");
                if (vol != null)
                {
                    var v = vol.GetComponent<Volume>();
                    Check("Volume 是全局", v != null && v.isGlobal, "非全局或缺少组件");
                    Check("Volume 有 Profile", v != null && v.sharedProfile != null, "profile 为空");
                }

                // 材质池基线：记下来，射击后再比一次（泄漏判据）
                _matBaseline = SMat.CachedCount;
                _log.Add($"  INFO 材质池基线 = {_matBaseline}");
            }

            // 全程免伤，只在这里临时打开，验证 DamagePlayer 真的会扣血
            root.InvulnerableForTest = false;
            float hpBefore = root.Hp;
            Check("测试前玩家存活", root.Hp > 0f, $"hp={root.Hp}");
            Check("测试前未暂停", !root.Paused, "Paused=true 会短路伤害逻辑");
            root.DamagePlayer(10f);
            Check("玩家受伤", root.Hp < hpBefore, $"{hpBefore} -> {root.Hp}");
            root.InvulnerableForTest = true;

            // ---- 战斗跑一段，检查稳定性（无异常抛出） ----
            // 关键：战斗段会触发升级三选一，一旦弹窗游戏就暂停，
            // 后续所有断言都会在 timeScale=0 下跑（踩过）。
            // 所以这里临时接管弹窗，让它自动消失。
            root.AutoAcceptOffers = true;
            root.Weapons.Player.InputLocked = false;
            yield return WaitUnscaled(3.5f);
            yield return Shot("05-combat");
            root.AutoAcceptOffers = false;

            // 若仍有残留弹窗，关掉它，保证后续断言在运行态
            if (root.InventoryOpen) root.CloseOverlayForTest();
            yield return WaitUnscaled(0.1f);

            Check("运行中敌人仍有存活", root.Enemies.Count > 0 || root.Run.Kills > 0,
                "既无敌人也无击杀，战斗可能未运转");
            Check("威胁值在增长", root.Run.Threat > 0f, $"threat={root.Run.Threat}");

            // ---- 材质池泄漏判据 ----
            // 连发 + 分裂会产生大量瞬态对象（弹丸/命中火花/爆炸球）。
            // 材质必须走共享缓存，否则实例数会随开火线性增长。
            {
                int after = SMat.CachedCount;
                Check("材质池未泄漏", after <= _matBaseline + 4,
                    $"基线 {_matBaseline} → 战斗后 {after}");
                _log.Add($"  INFO 材质池 战斗后 = {after}");

                var sfx = SfxBank.Instance;
                if (sfx != null)
                {
                    Check("音源占用不超池", sfx.ActiveSources <= sfx.PoolSizeValue,
                        $"active={sfx.ActiveSources} pool={sfx.PoolSizeValue}");
                }
            }

            // ---- 区域推进：真机验证门、进区、Boss 场 ----
            {
                // 战斗段可能把玩家打死（Lose 会 SetPaused(true)），
                // 一旦暂停 Update 全部短路，进区检测就不会跑。
                // 这里先恢复运行态，并把「当前是否暂停」作为独立断言查出来，
                // 避免把「测试自己把游戏停了」误判成进区逻辑坏了（踩过）。
                Check("战斗段未因死亡而暂停", !root.Paused, "Paused=true，玩家可能在战斗段阵亡");
                if (root.Paused) root.CloseOverlayForTest();
                Check("恢复运行态", Mathf.Approximately(Time.timeScale, 1f), $"timeScale={Time.timeScale}");

                Check("区域系统已建立", root.Progress != null && root.Zones != null,
                    "Progress 或 Zones 为 null");
                Check("6 个区域已构建", root.Zones != null && root.Zones.ZoneCount == 6,
                    $"zones={root.Zones?.ZoneCount}");
                Check("起点在 1 区", root.Zone == 1, $"zone={root.Zone}");
                Check("初始门关闭", !root.Progress.IsDoorOpen(1));

                var def1 = root.Progress.Get(1);
                for (int w = 0; w < def1.Waves; w++) root.Progress.BeginWave();
                bool cleared = root.Progress.TryClearCurrent();
                Check("1 区可被标记清空", cleared);
                root.Zones.ApplyGateState(root.Progress);
                Check("清区后门开启", root.Progress.IsDoorOpen(1));

                // 走进门 → 进 2 区。
                // 注意：玩家用 CharacterController 移动，直接改 position 会被物理回滚，
                // 所以这里连续几帧把它压到门后，并观察区域是否推进。
                var entry2 = S.View.ZoneBuilder.EntryPoint(2);
                var target = entry2 + Vector3.forward * 3f;
                int frames = 0;
                while (root.Zone == 1 && frames < 40)
                {
                    root.PlayerTransform.position = target;
                    yield return null;
                    frames++;
                }
                _log.Add($"  INFO 进区尝试 {frames} 帧 / {root.ZoneProgressionDebug}");
                Check("走进门后进入 2 区", root.Zone == 2, $"zone={root.Zone} / {root.ZoneProgressionDebug}");
                Check("2 区有波次", root.WaveCount > 0, $"waves={root.WaveCount}");
                yield return Shot("07-zone2");
            }

            // ---- Boss：切到末区并验证状态机接入 ----
            {
                for (int z = 2; z <= 5; z++)
                {
                    var d = root.Progress.Get(z);
                    if (root.Progress.Current != z) root.Progress.TryEnter(z);
                    for (int w = 0; w < d.Waves; w++) root.Progress.BeginWave();
                    root.Progress.TryClearCurrent();
                }
                root.BeginZone(6);
                yield return WaitUnscaled(0.3f);

                Check("进入 Boss 区", root.Zone == 6, $"zone={root.Zone}");
                Check("Boss 已生成", root.Boss != null && root.Boss.State != null);
                if (root.Boss != null && root.Boss.State != null)
                {
                    var bs = root.Boss.State;
                    Check("Boss 满血", Mathf.Approximately(bs.HpRatio, 1f), $"hp={bs.HpRatio}");
                    Check("Boss 初始 P1", bs.Phase == S.Core.BossPhase.P1);
                    Check("四根能量柱存在", root.Zones.Pillars.Count == 4,
                        $"pillars={root.Zones.Pillars.Count}");
                    yield return Shot("08-boss");

                    var pillar = root.Zones.Pillars[0];
                    if (pillar != null)
                    {
                        root.OnPillarHit(pillar, S.Core.BossState.PillarHp);
                        Check("碎柱后 Boss 破防", bs.IsBreached, $"breach={bs.BreachLeft}");
                        Check("碎柱后柱数减少", bs.PillarsAlive == 3, $"pillars={bs.PillarsAlive}");
                    }

                    float before = bs.Hp;
                    bs.TakeDamage(500f, false);
                    Check("Boss 可受伤", bs.Hp < before, $"{before} -> {bs.Hp}");

                    // ---- Boss 真机技能：扇形弹幕 / P2 地刺 ----
                    // 这里只等时间流逝，不手动 Tick BossState —— BossView.Update 已经在推，
                    // 手动再 Tick 会让计时器双倍消耗、且事件被 BossView 抢先消费（踩过）。
                    //
                    // 另一点：必须先确保游戏在运行态，否则 BossView.Update 里的
                    // `if (dt <= 0f) return` 会短路，等多久都不会有技能（踩过：新增弹幕 = -3）。
                    //
                    // 统计口径用 **累计计数**，不是场景对象净增量：
                    // 弹幕会到期销毁，用对象数做差可能得到负数，断言会假失败（踩过）。
                    //
                    // 地刺只在 P2 起才出现，所以先把血量精确压到 P2 区间（66%–33%）。
                    // 两个坑都要躲开：
                    //   1. 不能按 MaxHp 乘固定比例 —— 前面已打过 500，会直接落进 P3；
                    //   2. 破防期有 DamageTakenMul（1.5×），所以「想扣多少」必须**除以**这个乘数，
                    //      否则实际扣血会放大，同样越过 P2（踩过：targetHp=0.5 结果 hp=0.31）。
                    float targetHp = bs.MaxHp * 0.5f;
                    if (bs.Hp > targetHp)
                    {
                        float want = bs.Hp - targetHp;
                        bs.TakeDamage(want / Mathf.Max(0.01f, bs.DamageTakenMul), false);
                    }
                    yield return null;
                    Check("Boss 已进入 P2（地刺前置）", bs.Phase == S.Core.BossPhase.P2,
                        $"phase={bs.Phase} hp={bs.HpRatio:0.00}");

                    root.CloseOverlayForTest();
                    Check("Boss 技能测试前已恢复运行", Mathf.Approximately(Time.timeScale, 1f),
                        $"timeScale={Time.timeScale}");

                    int shotsBefore = root.Boss.BossFanVolleyCount;
                    int spikesBefore = root.Boss.BossSpikeCount;
                    _log.Add($"  INFO Boss active={root.Boss != null && root.Boss.isActiveAndEnabled} " +
                             $"phase={bs.Phase} hp={bs.HpRatio:0.00} ts={Time.timeScale} paused={root.Paused}");

                    float skillT = 0f;
                    float diagT = 0f;
                    while (skillT < 9f)
                    {
                        skillT += Time.unscaledDeltaTime;
                        diagT += Time.unscaledDeltaTime;
                        // 每 1.5 秒记一次现场：失败时才知道到底是「没跑」还是「跑了没发射」
                        if (diagT >= 1.5f)
                        {
                            diagT = 0f;
                            var bv = root.Boss;
                            _log.Add($"  INFO t={skillT:0.0}s ts={Time.timeScale:0.00} " +
                                     $"paused={root.Paused} inv={root.InventoryOpen} " +
                                     $"bossActive={(bv != null && bv.isActiveAndEnabled)} " +
                                     $"phase={(bv != null && bv.State != null ? bv.State.Phase.ToString() : "n/a")} " +
                                     $"volley/boss={(bv != null ? (bv.BossFanVolleyCount - shotsBefore).ToString() : "n/a")} " +
                                     $"spike={(bv != null ? (bv.BossSpikeCount - spikesBefore).ToString() : "n/a")}");
                        }
                        yield return null;
                    }

                    int newShots = root.Boss.BossFanVolleyCount - shotsBefore;
                    int newSpikes = root.Boss.BossSpikeCount - spikesBefore;

                    Check("Boss 在真机发过弹幕", newShots > 0, $"新增弹幕={newShots}");
                    Check("Boss 在真机发过地刺", newSpikes > 0, $"新增地刺={newSpikes}");
                    _log.Add($"  INFO Boss 9 秒内新增弹幕 {newShots} 个，地刺预警 {newSpikes} 次 " +
                             $"(累计 {root.Boss.BossFanVolleyCount}/{root.Boss.BossSpikeCount})");

                    yield return Shot("09-boss-breached");
                }
            }

            // 结算界面
            root.Win();
            yield return WaitUnscaled(0.2f);
            yield return Shot("06-win");
            Check("结算暂停", Mathf.Approximately(Time.timeScale, 0f), $"timeScale={Time.timeScale}");

            Finish();
        }

        /// <summary>
        /// 截图。ScreenCapture.CaptureScreenshot 是「帧末」捕获，
        /// 所以必须让它多过两帧再改变 UI 状态，否则会截到状态已经被改回去的画面（踩过）。
        /// </summary>
        IEnumerator Shot(string name)
        {
            if (string.IsNullOrEmpty(_shotDir)) yield break;
            var path = Path.Combine(_shotDir, $"{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            _log.Add("  SHOT " + name);
            // 两帧：一帧给 CaptureScreenshot 在帧末抓取，一帧留给写盘
            yield return null;
            yield return null;
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;
            var sb = new StringBuilder();
            sb.AppendLine("===== SELFCHECK =====");
            foreach (var l in _log) sb.AppendLine(l);
            sb.AppendLine($"SELFCHECK RESULT {_pass}/{_pass + _fail} passed");

            var path = string.IsNullOrEmpty(_logPath)
                ? Path.Combine(Application.dataPath, "..", "selfcheck.log")
                : _logPath;
            File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            Debug.Log(sb.ToString());
            Debug.Log("SELFCHECK LOG AT " + Path.GetFullPath(path));

            Time.timeScale = 1f;
            Application.Quit(_fail == 0 ? 0 : 1);
        }
    }
}

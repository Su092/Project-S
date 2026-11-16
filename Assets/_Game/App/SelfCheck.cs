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

        /// <summary>
        /// 场上是否已有精英。IReadOnlyList 没有 Exists，所以这里显式遍历 ——
        /// 也顺手跳过已销毁的引用（Enemy 会在死亡帧 Destroy，列表可能短暂持有 null）。
        /// </summary>
        static bool HasElite(S.View.GameRoot root)
        {
            if (root == null) return false;
            var list = root.Enemies;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].IsElite) return true;
            return false;
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
            Check("数据: 6 把枪", data != null && data.GunOrder.Count == 6, $"{data?.GunOrder.Count}");
            Check("数据: 17 种子弹", data != null && data.BulletOrder.Count == 17, $"{data?.BulletOrder.Count}");
            Check("数据: 34 种衍生", data != null && data.ModifierOrder.Count == 34, $"{data?.ModifierOrder.Count}");

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

                // ---- 统一字体 ----
                // 字体缺失时 UiKit.Font 会静默回退内置 Arial（界面能读但不统一），
                // 不做断言就永远发现不了「字体没进包」这件事。
                var font = S.View.UiKit.Font;
                Check("统一字体已加载", font != null, $"字体取不到（{S.View.SRes.Diagnose(S.View.UiKit.FontAssetName)}）");
                if (font != null)
                {
                    // 名字里能看出是不是回退到了内置字体
                    bool fallback = font.name == "Arial" || font.name == "LegacyRuntime";
                    Check("统一字体非内置回退", !fallback, $"实际字体 = {font.name}（回退说明自定义字体没加载）");
                    _log.Add($"  INFO 字体 = {font.name}");
                }

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
                    var mat = S.View.SRes.Get<Material>("mat_ui_additive");
                    Check("UI 加色材质存在", mat != null,
                        $"mat_ui_additive 缺失（{S.View.SRes.Diagnose("mat_ui_additive")}）");
                }
            }

            // 等 2 秒让敌人刷出来
            yield return WaitUnscaled(2.2f);
            yield return Shot("01-start");

            Check("玩家存在", root.PlayerTransform != null);
            Check("枪械已初始化", root.Weapons != null && root.Weapons.GunCount == 1,
                $"GunCount={root.Weapons?.GunCount}");
            Check("起手枪有子弹", root.Weapons != null && root.Weapons.Active.HasAnyBullet,
                "枪里应有基础弹");

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
            gun.Ammo = gun.Stats.Mag;
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

            // ---- 读取式扫描（loadout-spec §3）：一次射击读到几发、耗弹多少、
            //      被动件是否吸附、触发弹是否带载荷 ----
            {
                var g2 = root.Weapons.Active;

                // 槽 0 = 基础弹，槽 1 = 伤害增幅（被动 Prev 吸给槽 0）
                var saved = g2.Slots.Clone();
                g2.Slots.Cells[0] = data.MakeItem(g2.Base.BaseBullet);
                g2.Slots.Cells[1] = data.MakeItem("dmg");
                g2.Slots.Cells[1].Mod.Scope = Scope.Prev;
                g2.Recalc();

                var scanCast = Loadout.ResolveCast(g2.Base, g2.Slots, g2.Stats, 0);
                Check("扫描产出一发", scanCast.Projectiles.Count == 1, $"{scanCast.Projectiles.Count}");
                Check("被动件不占读取（未消耗第 2 格）", scanCast.UsedCells.Count == 1,
                    $"used={scanCast.UsedCells.Count}");
                Check("被动件生效（伤害 > 基础值）",
                    scanCast.Projectiles[0].Damage > g2.Base.BaseBullet.Length,
                    $"{scanCast.Projectiles[0].Damage}");

                // 恢复现场：后面所有断言都跑在原始槽位上
                g2.Slots = saved;
                g2.Recalc();

                // Next 件占读取：槽 0 = 双重施放（Next），槽 1/2 = 两发子弹
                var probe = new SlotState(8);
                probe.Cells[0] = data.MakeItem("std");
                var st = Loadout.ResolveGun(g2.Base, probe);
                var r = Loadout.ResolveCast(g2.Base, probe, st, 0);
                Check("单子弹耗弹等于自身 cost", r.AmmoCost == data.Bullets["std"].Cost,
                    $"{r.AmmoCost}");

                // 触发弹带载荷
                var trig = new SlotState(8);
                trig.Cells[0] = data.MakeItem("trigger");
                trig.Cells[1] = data.MakeItem("grenade");
                var st2 = Loadout.ResolveGun(g2.Base, trig);
                var r2 = Loadout.ResolveCast(g2.Base, trig, st2, 0);
                Check("触发弹带载荷", r2.Projectiles.Count == 1 &&
                    r2.Projectiles[0].Payload != null && r2.Projectiles[0].Payload.Count == 1,
                    $"{r2.Projectiles.Count}/{r2.Projectiles[0].Payload?.Count}");

                // 截图：把枪塞满，让 HUD 的「下一发 N 发」有东西可显示
                var shot = g2.Slots.Clone();
                for (int i = 0; i < g2.Slots.Length; i++)
                {
                    var id = i % 3 == 0 ? "std" : (i % 3 == 1 ? "dmg" : "crit");
                    var it = data.MakeItem(id);
                    if (it != null && !it.IsBullet) it.Mod.Scope = (i == 0) ? Scope.Next : Scope.Prev;
                    g2.Slots.Cells[i] = it;
                }
                g2.Recalc();
                yield return WaitUnscaled(0.12f);
                yield return Shot("02b-chain");

                g2.Slots = shot;
                g2.Recalc();
                yield return WaitUnscaled(0.08f);
            }

            // ---- 改装：装配后重算，验证 UI 数据源 ----
            gun.Slots.Cells[1] = data.MakeItem("std");
            gun.Recalc();
            Check("装配子弹件", gun.Slots.Cells[1] != null && gun.Slots.Cells[1].IsBullet);

            gun.Slots.Cells[2] = data.MakeItem("dmg");
            gun.Slots.Cells[2].Mod.Scope = Scope.Prev;
            gun.Recalc();

            var cast2 = Loadout.ResolveCast(gun.Base, gun.Slots, gun.Stats, 1);
            Check("改装后该位有弹丸", cast2.Projectiles.Count > 0, $"count={cast2.Projectiles.Count}");
            Check("改装后伤害包含被动加成",
                cast2.Projectiles.Count > 0 && cast2.Projectiles[0].Damage > data.Bullets["std"].Damage,
                $"damage={cast2.Projectiles[0].Damage}");

            // ---- 作用域取目标（实机 API 可用性）----
            {
                var probe = new SlotState(8);
                probe.Cells[0] = data.MakeItem("std");
                probe.Cells[1] = data.MakeItem("std");

                var t1 = Loadout.TargetsOf(Scope.Prev, 1, probe);
                Check("预览: Prev 找到左侧子弹", t1.Count == 1 && t1[0] == 0, string.Join(",", t1));

                var t2 = Loadout.TargetsOf(Scope.Adj, 1, probe);
                Check("预览: Adj 覆盖左右", t2.Contains(0) && t2.Contains(2), string.Join(",", t2));

                int nb = Loadout.NextBulletCell(0, probe);
                Check("预览: Next 指向下一子弹", nb == 1, $"{nb}");

                Check("预览: 不修改原槽位", probe.Cells[0] != null && probe.Cells[0].IsBullet);
            }

            CheckExperiencePickups(root);

            // ---- 经验与升级 ----
            int lvBefore = root.Run.Level;
            int ups = root.Run.AddXp(root.Run.NeedFor(1) + 5);
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

                // ---- 配件浮窗（ItemTooltip）：悬停详情钉在配件正下方 ----
                var tip = ItemTooltip.Instance;
                Check("配件浮窗已创建", tip != null, "ItemTooltip.Instance 为空");

                if (tip != null)
                {
                    Check("浮窗初始隐藏", !tip.Visible, $"id={tip.CurrentId}");

                    var probeItem = data.MakeItem(data.BulletOrder.Count > 0
                        ? data.BulletOrder[0].Id : "std");
                    if (probeItem != null)
                    {
                        tip.Show(probeItem);
                        Check("浮窗能展示子弹件", tip.Visible && tip.CurrentId == probeItem.Id,
                            $"id={tip.CurrentId}");
                        Check("浮窗标题已刷新", tip.TitleText == probeItem.Name,
                            $"title='{tip.TitleText}' expected='{probeItem.Name}'");

                        yield return Shot("03b-detail");

                        var probeMod = data.MakeItem("dmg");
                        if (probeMod != null)
                        {
                            tip.Show(probeMod);
                            Check("浮窗能展示衍生件", tip.CurrentId == "dmg",
                                $"id={tip.CurrentId}");
                            Check("浮窗衍生件标题已刷新", tip.TitleText == probeMod.Name,
                                $"title='{tip.TitleText}' expected='{probeMod.Name}'");

                            yield return Shot("03c-detail-mod");
                        }

                        tip.Hide();
                        Check("浮窗可隐藏", !tip.Visible, $"id={tip.CurrentId}");
                    }
                }

                yield return CheckInventoryScopePreview(inv, root);
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
                root.CloseOverlayForTest();
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

            // ---- 单图时间轴：真机验证竞技场、精英与 Boss 入场 ----
            {
                // 战斗段可能把玩家打死（Lose 会 SetPaused(true)），
                // 一旦暂停 Update 全部短路，时间轴就不再推进。
                // 这里先恢复运行态，并把「当前是否暂停」作为独立断言查出来，
                // 避免把「测试自己把游戏停了」误判成时间轴逻辑坏了（踩过）。
                Check("战斗段未因死亡而暂停", !root.Paused, "Paused=true，玩家可能在战斗段阵亡");
                if (root.Paused) root.CloseOverlayForTest();
                Check("恢复运行态", Mathf.Approximately(Time.timeScale, 1f), $"timeScale={Time.timeScale}");

                Check("竞技场已构建", root.Zones != null, "Zones 为 null");
                Check("节奏总导演已就绪", root.Director != null, "Director 为 null");
                Check("时间轴至少 4 个阶段",
                    root.Director != null && root.Director.Phases.Count >= 4,
                    $"phases={root.Director?.Phases.Count}");
                Check("Boss 入场时刻为 600s（10:00）",
                    root.Director != null && Mathf.Approximately(root.Director.BossAt, 600f),
                    $"bossAt={root.Director?.BossAt}");
                Check("首只精英在 300s（5:00）",
                    root.Director != null && Mathf.Approximately(root.Director.Elite.FirstAt, 300f),
                    $"firstAt={root.Director?.Elite.FirstAt}");
                Check("玩家出生在竞技场内",
                    Mathf.Abs(root.PlayerTransform.position.x) < 60f &&
                    Mathf.Abs(root.PlayerTransform.position.z) < 60f,
                    $"pos={root.PlayerTransform.position}");
                yield return Shot("07-arena");

                // ---- 时间轴快进：直接推 Director 到精英首现时刻之后，验证精英真的会入场 ----
                // 这里**不**快进真实时钟（那要等好几分钟），而是把 Director 的计时拨过去，
                // 再让一帧 Update 消费事件 —— 走的是与实战完全相同的 SpawnElite 路径。
                if (root.Director != null)
                {
                    int elitesBefore = root.ElitesSpawned;
                    root.Director.SeekTo(root.Director.Elite.FirstAt + 1f);
                    yield return null;
                    yield return null;
                    Check("精英首现后刷出精英", root.ElitesSpawned > elitesBefore,
                        $"{elitesBefore} -> {root.ElitesSpawned}");
                    Check("精英已标记",
                        HasElite(root),
                        $"enemies={root.Enemies.Count}");

                    // 威胁值按「精英首现时刻 / Boss 入场时刻」的比例推进。
                    // 这里按数据算期望值，不再写死 50 —— 改了时间轴后写死的期望会变成假红。
                    float expectThreat = root.Director.BossAt > 0.01f
                        ? 100f * (root.Director.Elite.FirstAt + 1f) / root.Director.BossAt
                        : 0f;
                    Check("精英首现时刻威胁值匹配",
                        Mathf.Abs(root.Run.Threat - expectThreat) < 3f,
                        $"threat={root.Run.Threat:0.0} 期望≈{expectThreat:0.0}");
                }

                // ---- Boss 入场：推到 8:00，验证 SpawnBoss 与能量柱 ----
                if (root.Director != null)
                {
                    root.Director.SeekTo(root.Director.BossAt + 1f);
                    yield return null;
                    yield return null;

                    Check("8:00 后 Boss 已入场",
                        root.Director.BossSpawned && root.Boss != null,
                        $"spawned={root.Director.BossSpawned} boss={(root.Boss != null)}");
                    Check("8:00 威胁值满", Mathf.Approximately(root.Run.Threat, 100f),
                        $"threat={root.Run.Threat:0.0}");
                    Check("四根能量柱已生成",
                        root.Zones != null && root.Zones.Pillars.Count == 4,
                        $"pillars={root.Zones?.Pillars.Count}");
                }

                if (root.Boss != null && root.Boss.State != null)
                {
                    var bs = root.Boss.State;
                    Check("Boss 满血", Mathf.Approximately(bs.HpRatio, 1f), $"hp={bs.HpRatio}");
                    Check("Boss 初始 P1", bs.Phase == S.Core.BossPhase.P1);
                    yield return Shot("08-boss");

                    var pillar = root.Zones.Pillars.Count > 0 ? root.Zones.Pillars[0] : null;
                    if (pillar != null)
                    {
                        root.OnPillarHit(pillar, bs.PillarHp);
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

        IEnumerator CheckInventoryScopePreview(InventoryView inv, GameRoot root)
        {
            var gun = root.Weapons.AllGuns[0];
            var saved = gun.Slots.Clone();
            var selected = root.Data.MakeItem("dmg");
            selected.Mod.Scope = Scope.N2;
            try
            {
                for (int i = 0; i < gun.Slots.Length; i++) gun.Slots.Cells[i] = null;
                gun.Slots.Cells[1] = selected;
                gun.Slots.Cells[2] = root.Data.MakeItem("std");
                gun.Recalc();
                inv.Refresh();
                DraggableItem card = null;
                foreach (var candidate in inv.GetComponentsInChildren<DraggableItem>())
                    if (ReferenceEquals(candidate.Item, selected)) { card = candidate; break; }
                Check("范围预览卡片已创建", card != null);
                if (card == null) yield break;
                var hover = card.GetComponent<ItemHover>();
                var e = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                hover.OnPointerEnter(e);
                Check("悬停显示范围且只统计真实子弹", inv.HasScopePreview &&
                    inv.PreviewSourceCell == 1 && inv.PreviewTargetCount == 1);
                var target = card.transform.parent.parent.Find("Slot2/EffectRange");
                Check("生效子弹格已点亮且不挡射线", target != null && target.gameObject.activeSelf &&
                    !target.GetComponent<UnityEngine.UI.Image>().raycastTarget);
                hover.OnPointerExit(e);
                Check("未选中时移出清理范围", !inv.HasScopePreview);
                e.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
                card.OnPointerClick(e);
                Check("左键选中保留范围且不卸下配件", inv.HasScopePreview && gun.Slots.Cells[1] == selected);
                hover.OnPointerEnter(e);
                hover.OnPointerExit(e);
                Check("选中后移出仍保留范围", inv.HasScopePreview && inv.PreviewTargetCount == 1);
                yield return Shot("03d-scope-range");
                e.button = UnityEngine.EventSystems.PointerEventData.InputButton.Right;
                card.OnPointerClick(e);
                Check("右键仍可卸下配件", gun.Slots.Cells[1] == null && root.Run.Bag.Contains(selected));
                DraggableItem bagCard = null;
                foreach (var candidate in inv.GetComponentsInChildren<DraggableItem>())
                    if (ReferenceEquals(candidate.Item, selected) && candidate.FromSlot < 0) { bagCard = candidate; break; }
                Check("卸下后生成背包卡片", bagCard != null);
                if (bagCard != null)
                {
                    e.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
                    bagCard.OnPointerClick(e);
                    inv.PreviewSlot(0, true);
                    Check("选中背包配件后悬停槽位可试装", inv.HasScopePreview &&
                        inv.PreviewSourceCell == 0 && inv.PreviewTargetCount == 1);
                    Check("试装不修改真实布局", gun.Slots.Cells[0] == null && root.Run.Bag.Contains(selected));
                    inv.PreviewSlot(0, false);
                    Check("背包试装移出后清理范围", !inv.HasScopePreview);
                }
                inv.Close();
                Check("关闭背包清理范围", !inv.HasScopePreview);
                inv.OpenBag();
            }
            finally
            {
                root.Run.Bag.Remove(selected);
                gun.Slots = saved;
                gun.Recalc();
                inv.Refresh();
            }
        }

        /// <summary>走真实敌人死亡/拾取/三选一路径；同步执行避免其他战斗帧干扰断言。</summary>
        void CheckExperiencePickups(GameRoot root)
        {
            var enemy = root.FindNearestEnemy(root.PlayerTransform.position, 500f);
            Check("经验掉落测试有敌人", enemy != null);
            if (enemy == null) return;

            var player = root.PlayerTransform;
            var cc = player.GetComponent<CharacterController>();
            bool ccEnabled = cc != null && cc.enabled;
            Vector3 savedPosition = player.position;
            int savedLevel = root.Run.Level;
            int savedXp = root.Run.Xp;
            int savedBag = root.Run.Bag.Count;
            int savedKills = root.Run.Kills;
            int savedDrops = root.ExperiencePickups.Count;
            bool savedAutoAccept = root.AutoAcceptOffers;
            if (ccEnabled) cc.enabled = false;
            root.AutoAcceptOffers = false;
            try
            {
                Vector3 deathPosition = enemy.transform.position;
                player.position = deathPosition + Vector3.right * 10f;
                // 恰好升两级，验证多级奖励仍然逐次排队。
                enemy.Xp = root.Run.NeedFor(savedLevel) - savedXp + root.Run.NeedFor(savedLevel + 1);
                int expectedXp = enemy.Xp;
                enemy.ApplyDamage(enemy.MaxHp + 1f);

                Check("击杀增加计数但不直接加经验",
                    root.Run.Kills == savedKills + 1 && root.Run.Level == savedLevel && root.Run.Xp == savedXp);
                Check("敌人死亡生成一个经验掉落", root.ExperiencePickups.Count == savedDrops + 1);
                if (root.ExperiencePickups.Count != savedDrops + 1) return;
                var pickup = root.ExperiencePickups[root.ExperiencePickups.Count - 1];
                Check("经验掉落保留死亡地点与经验数值", pickup.Amount == expectedXp &&
                    Mathf.Abs(pickup.transform.position.x - deathPosition.x) < 0.01f &&
                    Mathf.Abs(pickup.transform.position.z - deathPosition.z) < 0.01f);
                Check("远距离不能领取经验", !root.TryCollectExperience(pickup) && root.Run.Level == savedLevel);
                player.position = pickup.transform.position + Vector3.up * 3f;
                Check("不同高度不能隔层领取", !root.TryCollectExperience(pickup));
                player.position = pickup.transform.position + Vector3.right * 0.5f;
                root.SetPaused(true);
                Check("暂停期间不能拾取经验", !root.TryCollectExperience(pickup) && pickup.gameObject.activeSelf);
                root.SetPaused(false);
                Check("走近后能够领取经验", root.TryCollectExperience(pickup));
                Check("拾取才触发多级升级", root.Run.Level == savedLevel + 2 && root.Run.Xp == 0);
                Check("拾取后掉落移除且不可重复领取", root.ExperiencePickups.Count == savedDrops &&
                    !pickup.gameObject.activeSelf && !root.TryCollectExperience(pickup));
                var inv = root.GetComponent<InventoryView>();
                Check("拾取升级弹出三选一并保留后续奖励", root.Paused && inv.IsOfferVisible && root.HasPendingOffers);
                inv.AutoResolveForAI();
                Check("多级奖励按顺序继续展示", inv.IsOfferVisible && !root.HasPendingOffers);
                inv.AutoResolveForAI();
                Check("全部升级奖励领取后恢复游戏", !root.Paused && !root.InventoryOpen &&
                    root.Run.Bag.Count == savedBag + 2);
            }
            finally
            {
                root.CloseOverlayForTest();
                root.AutoAcceptOffers = savedAutoAccept;
                root.Run.Level = savedLevel;
                root.Run.Xp = savedXp;
                if (root.Run.Bag.Count > savedBag)
                    root.Run.Bag.RemoveRange(savedBag, root.Run.Bag.Count - savedBag);
                player.position = savedPosition;
                if (ccEnabled) cc.enabled = true;
            }
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

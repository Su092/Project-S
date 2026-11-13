using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 玩家武器管理：持 2 把枪、开火、换弹、轮转、生成弹丸。
    /// 结算全部由 Core.Loadout 完成，本类只负责「把描述符变成场景里的子弹」。
    /// </summary>
    public sealed class PlayerWeapons : MonoBehaviour
    {
        public Transform MuzzlePoint;
        public PlayerController Player;

        SData _data;
        readonly List<GunRuntime> _guns = new List<GunRuntime>();
        int _active;
        float _switchCooldown;

        /// <summary>
        /// 正在执行的链式施法（可以有多条同时在跑）。
        ///
        /// 为什么是"多条"而不是"一条"：链的总时长 = (链长−1) × 击发间隔，最长接近 0.6 s，
        /// 而连发枪每 0.11 s 就扣一次扳机。若只保留一条并让新的一发把它 `Cancel()` 掉，
        /// 长链每一发都只能走到第 2 项就断 —— 总伤害远低于 HUD 显示的期望值。
        /// 每条施法本来就是独立结算，重叠执行才是符合规格的语义。
        /// </summary>
        readonly List<ChainCast> _casts = new List<ChainCast>();

        /// <summary>
        /// 最近一次施法（HUD 链条用它把「哪些配件已经触发」画出来）。
        /// HUD 只画一条：同时画多条会在同一排图标上互相覆盖。
        /// </summary>
        public ChainCast PendingCast => _casts.Count > 0 ? _casts[_casts.Count - 1] : null;

        /// <summary>正在执行的施法条数（自检用：验证重叠生效、且不会被无限堆积）。</summary>
        public int ActiveCastCount => _casts.Count;

        public GunRuntime Active => _guns.Count == 0 ? null : _guns[_active];
        public int GunCount => _guns.Count;
        public int ActiveIndex => _active;
        public bool IsSwitching => _switchCooldown > 0f;
        public float SwitchNormalized => _switchCooldown <= 0f ? 0f : _switchCooldown / 0.3f;

        public readonly List<GunRuntime> AllGuns = new List<GunRuntime>();

        public void Init(SData data)
        {
            _data = data;
            _guns.Clear();
            AllGuns.Clear();

            // 起手两把枪：连发 + 单发（榴弹留作局内替换演示）
            string[] starters = { "g_auto", "g_single" };
            foreach (var id in starters)
            {
                if (!data.Guns.TryGetValue(id, out var def)) continue;
                var rt = new GunRuntime(def, data.BuildStarterSlots(id));
                _guns.Add(rt);
                AllGuns.Add(rt);
            }
            _active = 0;
        }

        /// <summary>当前枪对应的模型名（用于第一人称枪模切换）。</summary>
        public string ActiveGunModel
        {
            get
            {
                var g = Active;
                if (g == null) return null;
                return g.Base.Id switch
                {
                    "g_grenade" => "Modle_Gun_Grenade",
                    "g_single" => "Modle_Gun_Single",
                    _ => "Modle_Gun_Auto",
                };
            }
        }

        /// <summary>枪模根节点（由 Bootstrap 提供，切枪时替换其下的模型）。</summary>
        public Transform GunModelRoot;

        void Update()
        {
            if (_data == null) return;

            float dt = Time.deltaTime;

            // 链式施法时间轴：必须在暂停/换枪判定之外推进，
            // 但真正触发要受 timeScale 控制（暂停时 Time.deltaTime 为 0，天然停住）。
            TickPending(dt);

            if (_switchCooldown > 0f) _switchCooldown -= dt;
            for (int i = 0; i < _guns.Count; i++) _guns[i].Tick(dt);

            var g = Active;
            if (g == null || Player == null || Player.InputLocked) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // 切枪
            if (kb.digit1Key.wasPressedThisFrame) SwitchTo(0);
            if (kb.digit2Key.wasPressedThisFrame) SwitchTo(1);

            // 换弹：丢弃全部在跑的施法（换弹后手上这批计划就作废了）
            if (kb.rKey.wasPressedThisFrame) { g.StartReload(); CancelAllCasts(); }

            // 开火（支持按住连发）
            if (mouse.leftButton.isPressed && !IsSwitching)
            {
                var cast = g.TryFire(out string reason);
                if (cast != null) BeginCast(cast);
                else if (reason == "no-bullet") { /* HUD 提示空链 */ }
            }
        }

        /// <summary>
        /// 开始一次链式施法。**不再**一次性生成全部弹丸：
        /// 只把计划交给 ChainCast，由它按「链顺序 + 击发间隔」逐项触发。
        ///
        /// ⚠ 不再取消上一条链：链时长可以超过击发间隔，连发时本来就会有 2~3 条重叠。
        /// 取消上一条会让长链永远只触发头两项，总伤害低于 §3 的期望值（HUD 会骗人）。
        /// 只有换枪 / 换弹 / 死亡才是真正需要打断的时机。
        /// </summary>
        void BeginCast(CastResult cast)
        {
            if (cast == null || cast.Steps.Count == 0) return;

            _casts.Add(new ChainCast(cast, Active != null ? Active.Resolved : null));

            // 音效按「整次施法」的实际构成决定三轨（audio-spec §3），
            // 仍然在扣扳机那一刻响一次，不随链拆成多次 —— 否则连发会糊成噪音。
            PlayCastSfx(cast);
        }

        /// <summary>丢弃所有尚未触发的部分（换枪 / 换弹 / 死亡）。</summary>
        void CancelAllCasts()
        {
            for (int i = 0; i < _casts.Count; i++) _casts[i].Cancel();
            _casts.Clear();
        }

        void TickPending(float dt)
        {
            for (int i = _casts.Count - 1; i >= 0; i--)
            {
                var c = _casts[i];
                c.Tick(dt, SpawnProjectile, RemoveProjectile);

                // 链跑完 + 它产生的弹丸都消失，才能回收这条施法
                if (c.Finished && c.RemainingProjectiles == 0) _casts.RemoveAt(i);
            }
        }

        /// <summary>造一颗场景弹丸（朝施法瞬间的准心方向，带散射偏移）。</summary>
        Projectile SpawnProjectile(ProjectileDesc desc)
        {
            var g = Active;
            if (g == null || desc == null) return null;

            Vector3 origin = MuzzlePoint != null ? MuzzlePoint.position : transform.position;
            Quaternion aim = Camera.main != null ? Camera.main.transform.rotation : transform.rotation;

            float yaw = Random.Range(-0.5f, 0.5f) * desc.Spread;
            float pitch = Random.Range(-0.5f, 0.5f) * desc.Spread;
            Quaternion rot = aim * Quaternion.Euler(pitch, yaw, 0f);

            var go = new GameObject("Projectile");
            go.transform.position = origin;
            go.transform.rotation = rot;

            var p = go.AddComponent<Projectile>();
            p.Init(desc, g.Resolved, rot * Vector3.forward);
            return p;
        }

        static void RemoveProjectile(Projectile p)
        {
            if (p != null) Destroy(p.gameObject);
        }

        void PlayCastSfx(CastResult cast)
        {
            float speedMul = 1f, lifetime = 1f;
            bool splash = false;
            if (cast.Projectiles.Count > 0)
            {
                var d0 = cast.Projectiles[0];
                speedMul = d0.SpeedMul;
                lifetime = d0.Lifetime;
                splash = d0.SplashRadius > 0f;
            }
            SfxBank.Instance?.PlayShoot(speedMul, lifetime, cast.Projectiles.Count, splash);
        }

        public void SwitchTo(int idx)
        {
            if (idx < 0 || idx >= _guns.Count || idx == _active || IsSwitching) return;
            _active = idx;
            _switchCooldown = 0.3f;
            CancelAllCasts();
            RefreshGunModel();
        }

        /// <summary>按当前枪替换第一人称枪模。</summary>
        public void RefreshGunModel()
        {
            if (GunModelRoot == null) return;

            for (int i = GunModelRoot.childCount - 1; i >= 0; i--)
                Destroy(GunModelRoot.GetChild(i).gameObject);

            var modelName = ActiveGunModel;
            if (modelName == null) return;

            var vis = SAssets.SpawnFit(modelName, GunModelRoot, Vector3.zero,
                0.62f, Color.white, 0.55f, null, true, groundAlign: false);
            vis.name = "GunVisual";

            // 枪口方向：模型在 Blender 里枪管沿 -Y、上方 +Z；FBX 导入的轴转换
            // 已把它映射成 **枪管 +Z、上方 +Y**，正好就是第一人称的「前」与「上」。
            // 所以这里**不要**再叠加旋转 —— 早先有一句 Euler(0,-90,0) 是为了补救
            // 「SpawnFit 把导入旋转清零导致枪躺倒」，那个根因已在 SAssets 修掉，
            // 这句补偿现在只会把已经正确的朝向再转歪。
        }

        /// <summary>自动驾驶用：把 AI 已经算好的施法结果交给同一条链式触发路径。</summary>
        public void SpawnCastForAI(CastResult cast) => BeginCast(cast);

        /// <summary>换弹/受击等 UI 需要读取的当前枪。</summary>
        public SlotState PeekSlot()
        {
            var g = Active;
            if (g == null) return null;
            int i = g.PeekNextSlot();
            return i < 0 ? null : g.Slots[i];
        }
    }
}

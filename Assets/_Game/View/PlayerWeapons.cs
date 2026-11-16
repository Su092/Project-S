using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 玩家武器管理：持 2 把枪、开火、换弹、轮转、生成弹丸（对齐 MODCORE 原型）。
    ///
    /// 结算全部由 Core.Loadout 完成，本类只负责「把描述符变成场景里的东西」。
    /// 与原型的差别：原型是一次射击立刻生成全部投射物（即时结算），
    /// 这里保留「按 delay 逐发飞出」的表现，否则散弹枪的 6 枚弹丸会同一帧叠成一个点。
    /// </summary>
    public sealed class PlayerWeapons : MonoBehaviour
    {
        public Transform MuzzlePoint;
        public PlayerController Player;

        SData _data;
        readonly List<GunRuntime> _guns = new List<GunRuntime>();
        int _active;
        float _switchCooldown;
        uint _rngState = 0x5EED1234u;

        /// <summary>正在按 delay 逐发飞出的施法（一个投射物一条队列）。</summary>
        readonly List<PendingShot> _pending = new List<PendingShot>();

        sealed class PendingShot
        {
            public ProjectileDesc Desc;
            public GunDef Gun;
            public float Delay;
            public int Fired;
            public Vector3 Origin;
            public Quaternion Aim;
        }

        public GunRuntime Active => _guns.Count == 0 ? null : _guns[_active];
        public int GunCount => _guns.Count;
        public int ActiveIndex => _active;
        public bool IsSwitching => _switchCooldown > 0f;
        public float SwitchNormalized => _switchCooldown <= 0f ? 0f : _switchCooldown / 0.3f;

        public readonly List<GunRuntime> AllGuns = new List<GunRuntime>();

        public SData Data => _data;

        public void Init(SData data)
        {
            _data = data;
            _guns.Clear();
            AllGuns.Clear();

            // 菜单确认前使用一把默认枪供预览与自动测试。
            StartWith(data.GunOrder[0].Id);
        }

        public HashSet<string> OwnedGunIds => new HashSet<string>(_guns.ConvertAll(g => g.Base.Id));
        public int Capacity => Mathf.Max(1, _data.Player.GunSlots);

        public bool StartWith(string id)
        {
            if (!_data.Guns.TryGetValue(id, out var def)) return false;
            CancelAll();
            _guns.Clear(); AllGuns.Clear();
            var gun = new GunRuntime(def, _data.BuildStarterSlots(id));
            _guns.Add(gun); AllGuns.Add(gun);
            _active = 0; _switchCooldown = 0f;
            RefreshGunModel();
            return true;
        }

        public bool AcquireGun(string id, RunState run, out string reason)
        {
            reason = null;
            if (!_data.Guns.TryGetValue(id, out var def)) { reason = "未知枪械"; return false; }
            if (OwnedGunIds.Contains(id)) { reason = "已经持有这把枪"; return false; }
            bool replace = _guns.Count >= Capacity;
            var old = replace ? Active : null;
            if (old != null && run.Bag.Count + old.Slots.FilledCount > run.BagCapacity)
            { reason = $"需要腾出 {old.Slots.FilledCount} 格背包，才能收回旧枪配件（按 Tab 整理）"; return false; }
            var gun = new GunRuntime(def, _data.BuildStarterSlots(id));
            CancelAll();
            if (replace)
            {
                foreach (var item in old.Slots.Cells) if (item != null) run.Bag.Add(item);
                _guns[_active] = gun; AllGuns[_active] = gun;
            }
            else
            { _guns.Add(gun); AllGuns.Add(gun); _active = _guns.Count - 1; }
            _switchCooldown = 0.3f;
            RefreshGunModel();
            return true;
        }

        /// <summary>当前枪对应的模型名。</summary>
        public string ActiveGunModel
        {
            get
            {
                var g = Active;
                if (g == null) return null;
                // 模型名来自 Gun.tsv 的 `model` 列（旧版这里是 5 个 case 的硬编码映射）
                return string.IsNullOrEmpty(g.Base.Model) ? "Modle_Gun_Auto" : g.Base.Model;
            }
        }

        public Transform GunModelRoot;
        WeaponMotion _motion;

        float NextRng()
        {
            _rngState += 0x6D2B79F5u;
            uint t = _rngState;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return ((t ^ (t >> 14)) >> 8) / 16777216f;
        }

        void Update()
        {
            if (_data == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            TickPending(dt);

            if (_switchCooldown > 0f) _switchCooldown -= dt;
            for (int i = 0; i < _guns.Count; i++) _guns[i].Tick(dt);

            var g = Active;
            if (g == null || Player == null || Player.InputLocked) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.digit1Key.wasPressedThisFrame) SwitchTo(0);
            if (kb.digit2Key.wasPressedThisFrame) SwitchTo(1);
            if (kb.qKey.wasPressedThisFrame) SwitchTo((_active + 1) % Mathf.Max(1, _guns.Count));

            g = Active; // 切枪输入后，换弹与开火使用新枪
            if (kb.rKey.wasPressedThisFrame) { g.StartReload(); CancelAll(); }

            if (mouse.leftButton.isPressed && !IsSwitching)
            {
                var cast = g.TryFire(out string reason, NextRng);
                if (cast != null) BeginCast(cast);
                else if (reason == "no-bullet" && Time.frameCount % 30 == 0)
                    GameRoot.Instance?.HudToast("枪里没有子弹配件 · Tab 打开改装台装一颗");
            }
        }

        /// <summary>
        /// 把一次施法拆成「按 delay 排队飞出」的投射物。
        ///
        /// 为什么不用一次性生成：原型是 2D 即时结算，一次射击的几十枚弹丸可以同帧出现；
        /// 3D 里同帧出现会变成一团，玩家看不清「这一发打了什么」。
        /// 逐发飞出 + 每发带散射，才看得出霰弹/多弹幕的构成。
        /// </summary>
        void BeginCast(CastResult cast)
        {
            if (cast == null || cast.Projectiles.Count == 0) return;

            var g = Active;
            Vector3 origin = MuzzlePoint != null ? MuzzlePoint.position : transform.position;
            Quaternion aim = Camera.main != null ? Camera.main.transform.rotation : transform.rotation;

            for (int i = 0; i < cast.Projectiles.Count; i++)
            {
                _pending.Add(new PendingShot
                {
                    Desc = cast.Projectiles[i],
                    Gun = g != null ? g.Base : null,
                    Delay = i * Mathf.Max(0.02f, g != null ? g.Stats.ShotInterval(0f) * 0.35f : 0.05f),
                    Origin = origin,
                    Aim = aim,
                });
            }

            PlayCastSfx(cast);
        }

        void TickPending(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                p.Delay -= dt;
                if (p.Delay > 0f) continue;

                FireDesc(p.Desc, p.Origin, p.Aim, p.Gun);
                _pending.RemoveAt(i);
            }
        }

        /// <summary>真正把一个描述符变成场景里的弹丸（含 count 多弹丸与载荷）。</summary>
        void FireDesc(ProjectileDesc desc, Vector3 origin, Quaternion aim, GunDef gun)
        {
            if (desc == null) return;

            _motion?.Fire(gun);
            CombatFx.Muzzle(origin, aim * Vector3.forward,
                ParseColor(desc.Source != null ? desc.Source.Color : "#ffd25a"));
            int n = Mathf.Clamp(desc.Count, 1, Loadout.MaxCount);
            for (int i = 0; i < n; i++)
            {
                float yaw = Random.Range(-0.5f, 0.5f) * (desc.Spread + (gun != null ? gun.SpreadBase : 0f));
                float pitch = Random.Range(-0.5f, 0.5f) * (desc.Spread + (gun != null ? gun.SpreadBase : 0f));
                Quaternion rot = aim * Quaternion.Euler(pitch, yaw, 0f);

                var go = new GameObject("Projectile");
                go.transform.position = origin;
                go.transform.rotation = rot;

                var p = go.AddComponent<Projectile>();
                p.Init(desc, rot * Vector3.forward);
            }
        }

        /// <summary>把 `#rrggbb` 解析成 Color。数据里存字符串是为了让 sdata.json 可读。</summary>
        public static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return Color.white;
            if (!int.TryParse(hex.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int v))
                return Color.white;
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
        }

        void CancelAll()
        {
            _pending.Clear();
        }

        void PlayCastSfx(CastResult cast)
        {
            float speedMul = 1f, lifetime = 1f;
            bool splash = false;
            if (cast.Projectiles.Count > 0)
            {
                var d0 = cast.Projectiles[0];
                speedMul = Mathf.Clamp(d0.Speed / 90f, 0.4f, 2.5f);
                lifetime = d0.Lifetime;
                splash = d0.ExplodeR > 0f;
            }
            SfxBank.Instance?.PlayShoot(speedMul, lifetime, cast.Projectiles.Count, splash);
        }

        public void SwitchTo(int idx)
        {
            if (idx < 0 || idx >= _guns.Count || idx == _active || IsSwitching) return;
            _active = idx;
            _switchCooldown = 0.3f;
            CancelAll();
            RefreshGunModel();
        }

        public void RefreshGunModel()
        {
            if (GunModelRoot == null) return;

            for (int i = GunModelRoot.childCount - 1; i >= 0; i--)
            {
                var old = GunModelRoot.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var modelName = ActiveGunModel;
            if (modelName == null) return;

            var vis = SAssets.SpawnFit(modelName, GunModelRoot, Vector3.zero,
                0.62f, Color.white, 0.55f, null, true, groundAlign: false);
            vis.name = "GunVisual";
            _motion = GunModelRoot.GetComponent<WeaponMotion>();
            if (_motion == null) _motion = GunModelRoot.gameObject.AddComponent<WeaponMotion>();
            _motion.Bind(this, vis);
        }

        /// <summary>自动驾驶用：把 AI 已经算好的施法结果交给同一条路径。</summary>
        public void SpawnCastForAI(CastResult cast) => BeginCast(cast);

        /// <summary>HUD 用：指针所指格子的施法预览。</summary>
        public CastResult PeekCast()
        {
            var g = Active;
            if (g == null || !g.HasAnyBullet) return null;
            return Loadout.ResolveCast(g.Base, g.Slots, g.Stats, g.Cursor);
        }
    }
}

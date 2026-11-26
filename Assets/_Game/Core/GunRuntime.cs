using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 一把枪的运行时状态：槽位 + 轮转指针 + 弹匣 + 换弹计时（对齐 MODCORE 原型）。
    ///
    /// 指针语义与旧版不同：**指针指向下一个要扫描的格子**，一次射击会跨过若干格，
    /// 指针落到最后一次读取之后的位置。所以「换弹不重置指针」这条要保留 ——
    /// 玩家能靠换弹打断节奏，但不能靠换弹重置构筑的读取顺序。
    /// </summary>
    public sealed class GunRuntime
    {
        public GunDef Base;
        public SlotState Slots;
        public GunStats Stats;

        public bool SecondBeatPending { get; private set; }

        public void CancelSecondBeat() => SecondBeatPending = false;

        public int Cursor;
        public int Ammo;
        public float FireCooldown;
        public float ReloadLeft;
        public bool Reloading => ReloadLeft > 0f;

        /// <summary>
        /// HUD 弹匣条用：平时 = 剩余弹药比例；换弹中 = 换弹完成比例（0→1）。
        /// </summary>
        public float MagBarFill
        {
            get
            {
                if (Reloading)
                {
                    float dur = Stats.Reload > 0.01f ? Stats.Reload : 0.01f;
                    float done = 1f - (ReloadLeft / dur);
                    if (done < 0f) done = 0f;
                    if (done > 1f) done = 1f;
                    return done;
                }
                int mag = Stats.Mag > 0 ? Stats.Mag : 1;
                float pct = (float)Ammo / mag;
                if (pct < 0f) pct = 0f;
                if (pct > 1f) pct = 1f;
                return pct;
            }
        }

        /// <summary>累计击杀（汲能模块按此回血）。</summary>
        public int Kills;

        public GunRuntime(GunDef baseDef, SlotState slots)
        {
            Base = baseDef;
            Slots = slots ?? new SlotState(baseDef != null ? baseDef.SlotCount : 1);
            Recalc();
            Ammo = Stats.Mag;
        }

        /// <summary>装配/卸下/换枪后必须调用：重算枪属性并钳制弹药。</summary>
        public void Recalc()
        {
            Stats = Loadout.ResolveGun(Base, Slots);
            if (Ammo > Stats.Mag) Ammo = Stats.Mag;
            if (Ammo < 0) Ammo = 0;
        }

        public void Tick(float dt)
        {
            if (FireCooldown > 0f) FireCooldown -= dt;
            if (ReloadLeft > 0f)
            {
                ReloadLeft -= dt;
                if (ReloadLeft <= 0f)
                {
                    ReloadLeft = 0f;
                    Ammo = Stats.Mag;
                }
            }
        }

        public bool CanFire => !Reloading && FireCooldown <= 0f;

        /// <summary>预览下一次会读取的子弹格；配件按物理范围吸附。</summary>
        public int PeekNextCell()
        {
            int n = Slots.Length;
            for (int k = 0; k < n; k++)
            {
                int i = (Cursor + k) % n;
                var it = Slots.Cells[i];
                if (it == null) continue;
                if (it.IsBullet) return i;
            }
            return -1;
        }

        /// <summary>枪里是否有任何子弹件（没有任何子弹时开火永远没反应）。</summary>
        public bool HasAnyBullet
        {
            get
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var it = Slots.Cells[i];
                    if (it != null && it.IsBullet) return true;
                }
                return false;
            }
        }

        public void StartReload()
        {
            if (Reloading) return;
            if (Ammo >= Stats.Mag) return;
            CancelSecondBeat();
            ReloadLeft = Stats.Reload;
        }

        /// <summary>
        /// 扣扳机。成功返回施法结果；失败返回 null 并给原因。
        /// 弹药不足 → 不施法、不推进指针、自动换弹。
        /// </summary>
        public CastResult TryFire(out string reason, System.Func<float> rng = null)
        {
            reason = null;
            if (Reloading) { reason = "reloading"; return null; }
            if (FireCooldown > 0f) { reason = "cooldown"; return null; }
            if (!HasAnyBullet) { reason = "no-bullet"; return null; }

            var cast = Loadout.ResolveCast(Base, Slots, Stats, Cursor);

            if (!cast.Succeeded) { reason = "no-bullet"; return null; }

            // 弹药回收：有一定几率整次射击不耗弹
            bool free = false;
            if (Stats.Recycle > 0f)
            {
                float r = rng != null ? rng() : 0.5f;
                free = r < Stats.Recycle;
            }

            if (!free)
            {
                if (cast.AmmoCost > Ammo)
                {
                    reason = "no-ammo";
                    StartReload();
                    return null;
                }
                Ammo -= cast.AmmoCost;
            }

            FireCooldown = Stats.ShotInterval(cast.DelayAdd);
            if (Base.Id == "castanets")
            {
                SecondBeatPending = !SecondBeatPending;
                if (SecondBeatPending) FireCooldown *= .18f / Base.FireInterval;
            }
            Cursor = cast.NextPointer % Slots.Length;

            if (Ammo <= 0) StartReload();

            return cast;
        }

        /// <summary>把一次施法换算成「每秒伤害」估值（HUD 与校准用）。</summary>
        public float DpsEstimate
        {
            get
            {
                if (Slots == null || !HasAnyBullet) return 0f;
                var cast = Loadout.ResolveCast(Base, Slots, Stats, Cursor);
                float interval = System.Math.Max(0.03f, Stats.ShotInterval(cast.DelayAdd));
                return cast.ExpectedDamage / interval;            }
        }
    }
}

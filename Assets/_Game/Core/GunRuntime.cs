using System.Collections.Generic;

namespace S.Core
{
    /// <summary>
    /// 一把枪的运行时状态：配件位 + 轮转指针 + 弹匣 + 换弹计时。
    /// 规则见 docs/loadout-spec.md §1.1（轮转）与 §4（弹药/换弹）。
    /// 零 Unity 依赖，时间由外部以 dt 推进。
    /// </summary>
    public sealed class GunRuntime
    {
        public GunDef Base;              // 数据原始定义
        public GunDef Resolved;          // 枪械级配件结算后的定义（ResolveGun 产物）
        public SlotState[] Slots;

        public int Cursor;               // 下一个要施放的位（换弹不重置）
        public int Ammo;
        public float FireCooldown;
        public float ReloadLeft;
        public bool Reloading => ReloadLeft > 0f;

        public GunRuntime(GunDef baseDef, SlotState[] slots)
        {
            Base = baseDef;
            Slots = slots;
            Recalc();
            Ammo = Resolved.MagSize;
        }

        /// <summary>装配/卸下/换枪后必须调用：重算枪属性并钳制弹药。</summary>
        public void Recalc()
        {
            Resolved = Loadout.ResolveGun(Base, Slots);
            if (Ammo > Resolved.MagSize) Ammo = Resolved.MagSize;
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
                    Ammo = Resolved.MagSize;
                }
            }
        }

        public bool CanFire => !Reloading && FireCooldown <= 0f;

        /// <summary>找一个可施放的位：从 Cursor 起首个链非空的位。全空返回 -1。</summary>
        public int PeekNextSlot()
        {
            int n = Slots.Length;
            if (n == 0) return -1;
            for (int k = 0; k < n; k++)
            {
                int i = (Cursor + k) % n;
                if (!Slots[i].IsEmpty) return i;
            }
            return -1;
        }

        public void StartReload()
        {
            if (Reloading) return;
            if (Ammo >= Resolved.MagSize) return;
            ReloadLeft = Resolved.ReloadTime;
        }

        /// <summary>
        /// 扣扳机。成功返回施法结果（调用方据此生成弹丸）；失败返回 null 并说明原因。
        /// 弹药不足 → 不施法、不前进指针、自动换弹。
        /// </summary>
        public CastResult TryFire(out string reason)
        {
            reason = null;
            if (Reloading) { reason = "reloading"; return null; }
            if (FireCooldown > 0f) { reason = "cooldown"; return null; }

            int idx = PeekNextSlot();
            if (idx < 0) { reason = "no-bullet"; return null; }

            var cast = Loadout.ResolveCast(Resolved, Slots[idx]);

            if (cast.AmmoCost > Ammo)
            {
                reason = "no-ammo";
                StartReload();
                return null;
            }

            Ammo -= cast.AmmoCost;
            FireCooldown = Resolved.FireInterval;
            Cursor = (idx + 1) % Slots.Length;

            // 弹药刚打空 → 自动换弹（不阻塞本次施法）
            if (Ammo <= 0) StartReload();

            return cast;
        }
    }
}

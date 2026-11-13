using System;

namespace S.Core
{
    /// <summary>
    /// 溅射结算的纯逻辑：把「爆炸点到目标的距离」抽象出来，便于 headless 断言。
    ///
    /// 关键规则（对应 docs/gameplay-spec.md §5.2 与 §3 无人机）：
    ///  - 水平距离按真实欧氏距离衰减
    ///  - **高度差单独惩罚**：地面爆炸对飞得高的目标伤害大幅衰减
    ///  - 飞行单位（Drone）对地面溅射有额外减免
    ///
    /// 为什么不能直接用 3D 距离：无人机在 4.2m 高，地面爆炸的 3D 距离天然很大，
    /// 但玩家站在旁边时视觉上「就在爆炸点上方」，用 3D 距离会让判定不可预测。
    /// 拆成水平 + 垂直两个因子，规则才可解释、可调。
    /// </summary>
    public static class Splash
    {
        /// <summary>边缘伤害系数（gameplay-spec §5.2）。</summary>
        public const float EdgeFactor = 0.45f;

        /// <summary>高度差每米额外增加的距离惩罚（等效把垂直差放大）。</summary>
        public const float VerticalWeight = 1.6f;

        /// <summary>飞行单位对地面溅射的额外减免。</summary>
        public const float FlyingReduction = 0.5f;

        /// <summary>
        /// 计算一次溅射对某个目标的伤害。
        /// </summary>
        /// <param name="baseDamage">爆炸基础伤害（已含暴击）</param>
        /// <param name="radius">溅射半径（米）</param>
        /// <param name="horizontalDist">水平距离</param>
        /// <param name="verticalDist">垂直距离（爆炸点高度 - 目标脚底高度）</param>
        /// <param name="isFlying">目标是否为飞行单位</param>
        /// <returns>实际伤害；0 表示不在范围内</returns>
        public static float Damage(float baseDamage, float radius,
            float horizontalDist, float verticalDist, bool isFlying)
        {
            if (radius <= 0f || baseDamage <= 0f) return 0f;

            // 等效距离：水平 + 垂直加权。垂直单独加权而不是直接相加，
            // 是为了让「贴地爆炸 vs 空中爆炸」的差异可调而不影响水平衰减曲线。
            float eff = horizontalDist + Math.Abs(verticalDist) * VerticalWeight;
            if (eff >= radius) return 0f;

            float t = eff / radius;
            float dmg = baseDamage * (1f - (1f - EdgeFactor) * t);

            // 飞行单位：地面来的爆炸额外减免（逼玩家抬头打）
            if (isFlying) dmg *= FlyingReduction;

            return dmg;
        }

        /// <summary>
        /// 目标是否处于溅射范围内（与 Damage 同一套判据，避免两处不一致）。
        /// </summary>
        public static bool InRange(float radius, float horizontalDist, float verticalDist)
        {
            if (radius <= 0f) return false;
            float eff = horizontalDist + Math.Abs(verticalDist) * VerticalWeight;
            return eff < radius;
        }
    }
}

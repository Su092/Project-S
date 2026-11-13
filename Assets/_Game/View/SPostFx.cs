using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace S.View
{
    /// <summary>
    /// 后处理：Bloom + Vignette + 色彩分级（冷阴影 / 暖高光）。
    /// 依据 docs/art-style.md §1：轻后处理，不做景深（FPS 里会晕）。
    ///
    /// 为什么用代码建 Volume 而不是 .asset：Profile 资产需要手拖引用，
    /// 场景是纯代码构建的，手拖必然断链。代码建 Volume 也不影响打包（URP 会包含所需 shader）。
    /// </summary>
    public static class SPostFx
    {
        public static void Apply(Camera cam)
        {
            if (cam == null) return;

            // 相机需要开启后处理
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;

            // 已存在就复用，避免重复挂
            var existing = GameObject.Find("PostFxVolume");
            if (existing != null) return;

            var go = new GameObject("PostFxVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = profile;

            // --- Bloom：让发光条与弹丸有光晕 ---
            var bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(0.92f);
            bloom.scatter.Override(0.62f);

            // --- Vignette：轻微暗角，聚焦中心 ---
            var vig = profile.Add<Vignette>(true);
            vig.active = true;
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.42f);
            vig.color.Override(new Color(0.02f, 0.03f, 0.05f));

            // --- 色彩分级：冷阴影 + 暖高光（art-style §1）---
            var color = profile.Add<ColorAdjustments>(true);
            color.active = true;
            color.postExposure.Override(0.08f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);

            var split = profile.Add<SplitToning>(true);
            if (split != null)
            {
                split.active = true;
                split.shadows.Override(new Color(0.16f, 0.24f, 0.42f));   // 冷阴影
                split.highlights.Override(new Color(0.52f, 0.44f, 0.30f)); // 暖高光
                split.balance.Override(-10f);
            }

            // --- 不用景深（FPS 会晕），也不用运动模糊 ---
        }
    }
}

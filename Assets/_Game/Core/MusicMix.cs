namespace S.Core
{
    /// <summary>
    /// 音乐层次决策：根据威胁值与是否 Boss 战，决定哪些层该开、各自音量。
    /// 纯逻辑、可 headless 断言 —— 「阈值边界该响哪一层」最容易写错。
    ///
    /// 层次依据 docs/audio-spec.md §5：
    ///  - 探索（威胁 &lt;35）：低音垫 + 稀疏节拍，无旋律
    ///  - 战斗（35–65）：加 4/4 打击层
    ///  - 高压（&gt;65）：加失真低音 + 心跳
    ///  - Boss：独立曲目
    /// </summary>
    public static class MusicMix
    {
        public const float CombatThreshold = 35f;
        public const float HighThreshold = 65f;

        public struct Layers
        {
            /// <summary>低音垫：始终开启（探索也有）。</summary>
            public float Pad;
            /// <summary>工业节拍：战斗起开启。</summary>
            public float Beat;
            /// <summary>失真低音：高压起开启。</summary>
            public float Distort;
            /// <summary>心跳：高压起开启。</summary>
            public float Heart;
            /// <summary>Boss 曲目：Boss 战时替代前四层。</summary>
            public float Boss;
            /// <summary>目标 BPM。</summary>
            public float Bpm;
            /// <summary>层次名（UI 与自检用）。</summary>
            public string Label;
        }

        /// <summary>
        /// 计算目标混音。threat 会被钳制到 0..100。
        /// </summary>
        public static Layers Compute(float threat, bool bossFight)
        {
            if (threat < 0f) threat = 0f;
            if (threat > 100f) threat = 100f;

            if (bossFight)
            {
                return new Layers
                {
                    Pad = 0f, Beat = 0f, Distort = 0f, Heart = 0f,
                    Boss = 1f,
                    Bpm = 140f,
                    Label = "boss",
                };
            }

            // 探索：只有低音垫，节拍很轻
            if (threat < CombatThreshold)
            {
                return new Layers
                {
                    Pad = 1f,
                    Beat = 0f,
                    Distort = 0f,
                    Heart = 0f,
                    Boss = 0f,
                    Bpm = 84f,
                    Label = "explore",
                };
            }

            // 战斗：加入 4/4 打击层，100 → 随威胁缓升
            if (threat <= HighThreshold)
            {
                float t = (threat - CombatThreshold) / (HighThreshold - CombatThreshold);
                return new Layers
                {
                    Pad = 1f,
                    Beat = Lerp(0.75f, 1f, t),
                    Distort = 0f,
                    Heart = 0f,
                    Boss = 0f,
                    Bpm = Lerp(100f, 116f, t),
                    Label = "combat",
                };
            }

            // 高压：加失真低音 + 心跳，128 BPM 封顶
            float h = (threat - HighThreshold) / (100f - HighThreshold);
            return new Layers
            {
                Pad = 1f,
                Beat = 1f,
                Distort = Lerp(0.55f, 1f, h),
                Heart = Lerp(0.45f, 0.95f, h),
                Boss = 0f,
                Bpm = Lerp(120f, 128f, h),
                Label = "high",
            };
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}

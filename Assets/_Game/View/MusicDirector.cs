using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 程序化音乐：4 个循环层（低音垫 / 打击 / 失真低音 / 心跳）+ Boss 层。
    ///
    /// 设计依据 docs/audio-spec.md §5：
    ///  - 层次由 Core/MusicMix 决定（纯逻辑、可断言）
    ///  - 层次间**交叉淡入淡出**，不硬切（硬切会有明显的断点）
    ///  - 打开背包时整体降 8dB 但**不停**：玩家需要「时间在流逝」的听觉压力
    ///
    /// 为什么用程序合成：初版没有音乐素材。合成能先验证「层次切换是否可感知」，
    /// 之后换真实素材只需替换 clip 表，混音逻辑不动。
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        AudioSource _pad, _beat, _distort, _heart, _boss;
        MusicMix.Layers _target;
        MusicMix.Layers _current;
        bool _muted;

        /// <summary>背包打开时的音量衰减（-8dB ≈ ×0.4）。</summary>
        const float BagDuck = 0.4f;
        const float MasterVolume = 0.32f;

        public string CurrentLabel => _target.Label;
        public float CurrentBpm => _target.Bpm;

        public void Init()
        {
            _pad = Make("MusicPad", 0.5f);
            _beat = Make("MusicBeat", 0.5f);
            _distort = Make("MusicDistort", 0.5f);
            _heart = Make("MusicHeart", 0.5f);
            _boss = Make("MusicBoss", 0.5f);

            _pad.clip = Loop("pad", BuildPad(4f), 4f);
            _beat.clip = Loop("beat", BuildBeat(2f, 100f), 2f);
            _distort.clip = Loop("distort", BuildDistort(2f), 2f);
            _heart.clip = Loop("heart", BuildHeart(2f), 2f);
            _boss.clip = Loop("boss", BuildBoss(4f), 4f);

            // 全部循环播放，音量从 0 开始
            foreach (var s in new[] { _pad, _beat, _distort, _heart, _boss })
            {
                s.loop = true;
                s.volume = 0f;
                s.Play();
            }

            _target = MusicMix.Compute(0f, false);
            _current = _target;
        }

        AudioSource Make(string name, float vol)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        /// <summary>由 GameRoot 每帧驱动（暂停时也要更新，因为背包只是降音量不停播）。</summary>
        public void UpdateMix(float threat, bool bossFight, bool bagOpen, float dt)
        {
            _target = MusicMix.Compute(threat, bossFight);

            // 交叉淡入淡出：用指数逼近，避免线性淡变听感生硬
            float k = 1f - Mathf.Exp(-3.2f * Mathf.Max(0.0001f, dt));
            _current.Pad = Mathf.Lerp(_current.Pad, _target.Pad, k);
            _current.Beat = Mathf.Lerp(_current.Beat, _target.Beat, k);
            _current.Distort = Mathf.Lerp(_current.Distort, _target.Distort, k);
            _current.Heart = Mathf.Lerp(_current.Heart, _target.Heart, k);
            _current.Boss = Mathf.Lerp(_current.Boss, _target.Boss, k);

            float duck = bagOpen ? BagDuck : 1f;
            if (_muted) duck = 0f;

            // Master × Music 分组增益（audio-spec §6）
            float gain = MasterVolume * AudioMix.MusicGain * duck;

            _pad.volume = _current.Pad * gain;
            _beat.volume = _current.Beat * gain;
            _distort.volume = _current.Distort * gain;
            _heart.volume = _current.Heart * gain;
            _boss.volume = _current.Boss * gain;

            // 节拍层用 pitch 表达 BPM 变化（2s 循环内嵌 4 拍 = 120 BPM 基准）
            float beatPitch = Mathf.Clamp(_target.Bpm / 120f, 0.6f, 1.6f);
            _beat.pitch = beatPitch;
            _heart.pitch = Mathf.Clamp(_target.Bpm / 120f, 0.7f, 1.5f);
        }

        public void SetMuted(bool m) => _muted = m;
        public bool IsMuted => _muted;

        // ---------------- 合成 ----------------

        AudioClip Loop(string name, float[] data, float dur)
        {
            int n = data.Length;
            var clip = AudioClip.Create("music_" + name, n, 1, (int)SfxBank.SampleRateValue, false);
            clip.SetData(data, 0);
            return clip;
        }

        static int Samples(float dur) => Mathf.RoundToInt(dur * SfxBank.SampleRateValue);

        /// <summary>低音垫：两个相差三度的正弦叠加 + 缓慢呼吸。</summary>
        float[] BuildPad(float dur)
        {
            int n = Samples(dur);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / SfxBank.SampleRateValue;
                // 55Hz + 82.5Hz（纯五度），避免三度的浑浊
                float v = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.3f;
                // 呼吸：0.25Hz 缓慢起伏
                float breath = 0.62f + 0.38f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
                d[i] = v * breath * 0.5f;
            }
            return CrossfadeLoop(d, 0.25f);
        }

        /// <summary>工业打击层：每拍一个噪声脉冲 + 低频体，2 秒 4 拍 = 120 BPM。</summary>
        float[] BuildBeat(float dur, float bpm)
        {
            int n = Samples(dur);
            var d = new float[n];
            float beatInterval = 60f / bpm;
            var rng = new System.Random(4242);

            for (int i = 0; i < n; i++)
            {
                float t = i / SfxBank.SampleRateValue;
                float phaseInBeat = t % beatInterval;
                float env = Mathf.Exp(-phaseInBeat * 26f);

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float body = Mathf.Sin(2f * Mathf.PI * 70f * t);
                // 每 2 拍加重一次（强弱拍）
                int beatIdx = (int)(t / beatInterval);
                float accent = (beatIdx % 2 == 0) ? 1f : 0.6f;

                d[i] = (noise * 0.30f + body * 0.42f) * env * accent * 0.55f;
            }
            return CrossfadeLoop(d, 0.12f);
        }

        /// <summary>失真低音：削波的正弦，制造压迫感。</summary>
        float[] BuildDistort(float dur)
        {
            int n = Samples(dur);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / SfxBank.SampleRateValue;
                float v = Mathf.Sin(2f * Mathf.PI * 41f * t) * 2.6f;
                // 硬削波
                v = Mathf.Clamp(v, -0.7f, 0.7f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 2f * t);
                d[i] = v * pulse * 0.42f;
            }
            return CrossfadeLoop(d, 0.2f);
        }

        /// <summary>心跳：双峰脉冲，60 BPM 附近。</summary>
        float[] BuildHeart(float dur)
        {
            int n = Samples(dur);
            var d = new float[n];
            const float beat = 1.0f;   // 1 秒一跳
            for (int i = 0; i < n; i++)
            {
                float t = i / SfxBank.SampleRateValue;
                float p = t % beat;
                // 第一峰 p≈0，第二峰 p≈0.26
                float e1 = Mathf.Exp(-p * 22f);
                float e2 = p > 0.24f ? Mathf.Exp(-(p - 0.24f) * 26f) * 0.62f : 0f;
                float v = Mathf.Sin(2f * Mathf.PI * 52f * t) * (e1 + e2);
                d[i] = v * 0.55f;
            }
            return CrossfadeLoop(d, 0.15f);
        }

        /// <summary>Boss 曲目：低频持续 + 不规则打击，比普通层更密。</summary>
        float[] BuildBoss(float dur)
        {
            int n = Samples(dur);
            var d = new float[n];
            var rng = new System.Random(9001);
            const float beat = 0.43f;   // ≈140 BPM
            for (int i = 0; i < n; i++)
            {
                float t = i / SfxBank.SampleRateValue;
                float p = t % beat;
                float env = Mathf.Exp(-p * 18f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float low = Mathf.Sin(2f * Mathf.PI * 44f * t) * 1.8f;
                low = Mathf.Clamp(low, -0.8f, 0.8f);
                d[i] = (low * 0.5f + noise * 0.28f * env) * 0.55f;
            }
            return CrossfadeLoop(d, 0.18f);
        }

        /// <summary>
        /// 首尾交叉淡化：让循环接缝听不出来。
        /// 不处理的话每 2–4 秒会有一次「咔」的断点（踩过）。
        /// </summary>
        static float[] CrossfadeLoop(float[] src, float seconds)
        {
            int fade = Mathf.Min(Samples(seconds), src.Length / 4);
            if (fade <= 1) return src;

            var d = (float[])src.Clone();
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                int tail = src.Length - fade + i;
                // 尾部淡出，同时把尾部内容叠到头部（保证循环连续）
                d[i] = src[i] * k + src[tail] * (1f - k);
                d[tail] = src[tail] * (1f - k) + src[i] * k;
            }
            return d;
        }

        // ---------------- 自检用 ----------------

        public float LayerVolume(string name)
        {
            switch (name)
            {
                case "pad": return _pad != null ? _pad.volume : 0f;
                case "beat": return _beat != null ? _beat.volume : 0f;
                case "distort": return _distort != null ? _distort.volume : 0f;
                case "heart": return _heart != null ? _heart.volume : 0f;
                case "boss": return _boss != null ? _boss.volume : 0f;
                default: return 0f;
            }
        }

        public int PlayingCount
        {
            get
            {
                int n = 0;
                foreach (var s in new[] { _pad, _beat, _distort, _heart, _boss })
                    if (s != null && s.isPlaying) n++;
                return n;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace S.View
{
    /// <summary>
    /// 程序化音效库：启动时用代码合成 AudioClip，避免外部素材依赖。
    ///
    /// 设计依据 docs/audio-spec.md：
    ///  - 枪械三轨分层（body 机械 / shot 弹药 / tail 收尾），按配件动态组合
    ///  - 全局 ≤16 个 AudioSource，按优先级丢弃
    ///  - 连发 9 发/秒 × 3 轨 = 27 次/秒，必须靠「同类限流 + 音高随机」而非全播
    ///
    /// 为什么程序化：初版没有任何音频素材，靠合成先验证节奏与限流，
    /// 之后换真素材只需替换 Resolve() 里的 clip 表，调用方不动。
    /// </summary>
    public sealed class SfxBank : MonoBehaviour
    {
        public static SfxBank Instance { get; private set; }

        const int PoolSize = 16;
        const float SampleRate = 44100f;

        /// <summary>采样率（MusicDirector 用它把秒数换算成采样点）。</summary>
        public const int SampleRateValue = 44100;

        AudioSource[] _pool;
        int _cursor;

        // 音高随机 ±8%（audio-spec §3），避免连发时机械重复
        const float PitchJitter = 0.08f;

        // 限流表：key = 音效类别，value = 上次播放的时间
        readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>();

        // 各类别的最小间隔（秒）。由 audio-spec §4 的优先级推出。
        static readonly Dictionary<string, float> MinInterval = new Dictionary<string, float>
        {
            { "shoot",  0.045f },   // 约 22 次/秒上限
            { "hit",    0.030f },
            { "hurt",   0.120f },   // 受伤最不该刷屏
            { "pickup", 0.070f },
            { "ui",     0.050f },
            { "enemy",  1.100f },   // 「敌人在附近」提示，1.1s 冷却
            { "explode",0.060f },
            { "boss",   0.200f },
        };

        // ---- 合成出的 clip ----
        AudioClip _shootBody, _shootShot, _shootTail;
        AudioClip _hitSoft, _hitWall, _explode;
        AudioClip _hurt, _pickup, _levelUp;
        AudioClip _uiClick, _uiHover;
        AudioClip _enemyNear, _bossPhase, _shield;

        public void Init()
        {
            Instance = this;

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;   // 初版全部 2D，避免空间化调试成本
                src.volume = 0.7f;
                _pool[i] = src;
            }

            BuildClips();
        }

        // ---------------- 合成 ----------------

        void BuildClips()
        {
            // 枪身机械：短促噪声脉冲 + 低频冲击
            _shootBody = MakeClip("shoot_body", 0.09f, t =>
            {
                float env = Mathf.Exp(-t * 55f);
                float noise = Random.value * 2f - 1f;
                float low = Mathf.Sin(2f * Mathf.PI * 95f * t);
                return (noise * 0.45f + low * 0.55f) * env;
            });

            // 弹药声：中频尖锐，音高由配件决定（调用方调 pitch）
            _shootShot = MakeClip("shoot_shot", 0.12f, t =>
            {
                float env = Mathf.Exp(-t * 34f);
                float body = Mathf.Sin(2f * Mathf.PI * 420f * t) * 0.5f;
                float crack = (Random.value * 2f - 1f) * 0.35f;
                return (body + crack) * env;
            });

            // 收尾：低频尾音，时长随 lifetime 缩放（调用方调 pitch/volume）
            _shootTail = MakeClip("shoot_tail", 0.26f, t =>
            {
                float env = Mathf.Exp(-t * 11f);
                return Mathf.Sin(2f * Mathf.PI * 130f * t) * env * 0.45f;
            });

            _hitSoft = MakeClip("hit_soft", 0.07f, t =>
                (Random.value * 2f - 1f) * Mathf.Exp(-t * 90f) * 0.5f);

            _hitWall = MakeClip("hit_wall", 0.10f, t =>
            {
                float env = Mathf.Exp(-t * 60f);
                return Mathf.Sin(2f * Mathf.PI * 210f * t) * env * 0.4f
                     + (Random.value * 2f - 1f) * 0.12f * env;
            });

            _explode = MakeClip("explode", 0.42f, t =>
            {
                float env = Mathf.Exp(-t * 7.5f);
                float low = Mathf.Sin(2f * Mathf.PI * 58f * t) * 0.7f;
                float noise = (Random.value * 2f - 1f) * 0.55f;
                return (low + noise) * env;
            });

            _hurt = MakeClip("hurt", 0.20f, t =>
            {
                float env = Mathf.Exp(-t * 16f);
                return Mathf.Sin(2f * Mathf.PI * 190f * t) * env * 0.55f;
            });

            _pickup = MakeClip("pickup", 0.10f, t =>
            {
                float f = Mathf.Lerp(760f, 1180f, t * 6f);
                return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * 0.42f;
            });

            _levelUp = MakeClip("levelup", 0.55f, t =>
            {
                float f = Mathf.Lerp(420f, 880f, t * 2.2f);
                float tremolo = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 9f * t);
                return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 3.2f) * tremolo * 0.45f;
            });

            _uiClick = MakeClip("ui_click", 0.05f, t =>
                Mathf.Sin(2f * Mathf.PI * 900f * t) * Mathf.Exp(-t * 120f) * 0.35f);

            _uiHover = MakeClip("ui_hover", 0.04f, t =>
                Mathf.Sin(2f * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 150f) * 0.18f);

            // 「敌人接近」：低频脉动，1.1s 冷却
            _enemyNear = MakeClip("enemy_near", 0.26f, t =>
            {
                float env = Mathf.Exp(-t * 9f);
                return Mathf.Sin(2f * Mathf.PI * 72f * t) * env * 0.4f;
            });

            _bossPhase = MakeClip("boss_phase", 0.70f, t =>
            {
                float f = Mathf.Lerp(90f, 45f, t * 1.4f);
                float env = Mathf.Exp(-t * 3.6f);
                return Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.6f;
            });

            _shield = MakeClip("shield", 0.30f, t =>
            {
                float f = Mathf.Lerp(1400f, 900f, t * 3f);
                return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 9f) * 0.3f;
            });
        }

        AudioClip MakeClip(string name, float dur, System.Func<float, float> gen)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(dur * SampleRate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / SampleRate;
                float v = gen(t);
                // 末端 8ms 淡出，消除爆音
                float fade = Mathf.Clamp01((dur - t) / 0.008f);
                data[i] = Mathf.Clamp(v * fade, -1f, 1f);
            }

            var clip = AudioClip.Create(name, n, 1, (int)SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------------- 播放 ----------------

        /// <summary>
        /// 播一个音效。返回是否真的播出（被限流或池满则 false）。
        /// 判据：同类最小间隔 + 全局池 ≤16 + 优先级丢弃。
        /// </summary>
        bool Play(AudioClip clip, string kind, float volume = 1f, float pitch = 1f,
                  float pitchJitter = PitchJitter)
        {
            if (clip == null || _pool == null) return false;

            // 限流
            if (MinInterval.TryGetValue(kind, out float minGap))
            {
                if (_lastPlay.TryGetValue(kind, out float last) && Time.time - last < minGap)
                    return false;
                _lastPlay[kind] = Time.time;
            }

            // 找一个空闲 source；全忙则复用最旧的那个（优先级由限流保证）
            AudioSource src = null;
            for (int i = 0; i < _pool.Length; i++)
            {
                int idx = (_cursor + i) % _pool.Length;
                if (!_pool[idx].isPlaying) { src = _pool[idx]; _cursor = (idx + 1) % _pool.Length; break; }
            }
            if (src == null)
            {
                src = _pool[_cursor];
                _cursor = (_cursor + 1) % _pool.Length;
            }

            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = Mathf.Clamp(pitch + Random.Range(-pitchJitter, pitchJitter), 0.4f, 2.6f);
            src.Play();
            return true;
        }

        // ---- 对外接口（GameRoot / 各系统调用） ----

        /// <summary>
        /// 开枪：三轨叠加（body 恒响；shot 随弹速变调；tail 随存在时间变长）。
        /// 依据 audio-spec §3。
        /// </summary>
        public void PlayShoot(float speedMul, float lifetime, int projectileCount, bool hasSplash)
        {
            Play(_shootBody, "shoot", 0.55f, 1f, 0.05f);

            // 弹速 >1.3 时音高 +2 半音（听感更快）
            float shotPitch = speedMul > 1.3f ? 1.122f : 1f;
            Play(_shootShot, "shoot", 0.42f, shotPitch, 0.10f);

            if (lifetime > 1.0f)
                Play(_shootTail, "shoot", Mathf.Lerp(0.12f, 0.30f, Mathf.Clamp01(lifetime / 2f)), 1f, 0.06f);

            // 分裂：追加一层弱化副本，模拟多发
            if (projectileCount > 1)
            {
                // 用同一 clip 的略高音高近似「多发感」（不额外占池）
                Play(_shootShot, "shoot", 0.22f, 1.18f, 0.08f);
            }

            if (hasSplash)
                Play(_hitWall, "explode", 0.25f, 0.8f);
        }

        public void PlayHit(bool onEnemy) =>
            Play(onEnemy ? _hitSoft : _hitWall, "hit", onEnemy ? 0.35f : 0.22f, 1f, 0.14f);

        public void PlayExplode() => Play(_explode, "explode", 0.65f, 1f, 0.10f);

        public void PlayHurt() => Play(_hurt, "hurt", 0.60f, 1f, 0.05f);

        public void PlayPickup() => Play(_pickup, "pickup", 0.42f, 1f, 0.10f);

        public void PlayLevelUp() => Play(_levelUp, "pickup", 0.55f, 1f, 0.02f);

        public void PlayUiClick() => Play(_uiClick, "ui", 0.30f, 1f, 0.04f);

        public void PlayUiHover() => Play(_uiHover, "ui", 0.16f, 1f, 0.04f);

        public void PlayEnemyNear() => Play(_enemyNear, "enemy", 0.30f, 1f, 0.05f);

        public void PlayBossPhase() => Play(_bossPhase, "boss", 0.70f, 1f, 0.02f);

        public void PlayShield() => Play(_shield, "boss", 0.40f, 1f, 0.06f);

        // ---- 自检用 ----

        /// <summary>正在播放的 source 数。</summary>
        public int ActiveSources
        {
            get
            {
                if (_pool == null) return 0;
                int n = 0;
                foreach (var s in _pool) if (s != null && s.isPlaying) n++;
                return n;
            }
        }

        public int PoolSizeValue => _pool != null ? _pool.Length : 0;

        /// <summary>合成出的 clip 数量。</summary>
        public int ClipCount =>
            (_shootBody != null ? 1 : 0) + (_shootShot != null ? 1 : 0) + (_shootTail != null ? 1 : 0) +
            (_hitSoft != null ? 1 : 0) + (_hitWall != null ? 1 : 0) + (_explode != null ? 1 : 0) +
            (_hurt != null ? 1 : 0) + (_pickup != null ? 1 : 0) + (_levelUp != null ? 1 : 0) +
            (_uiClick != null ? 1 : 0) + (_uiHover != null ? 1 : 0) + (_enemyNear != null ? 1 : 0) +
            (_bossPhase != null ? 1 : 0) + (_shield != null ? 1 : 0);
    }
}

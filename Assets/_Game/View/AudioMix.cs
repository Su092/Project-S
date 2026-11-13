using UnityEngine;

namespace S.View
{
    /// <summary>
    /// 音量中枢：Master / SFX / Music / UI 四条独立音量，持久化到 PlayerPrefs。
    ///
    /// 依据 docs/audio-spec.md §6「混音分组 Master / SFX / Music / UI，暴露音量设置」。
    ///
    /// 为什么用静态类而不是 MonoBehaviour：音量是全局设置，
    /// 与场景生命周期无关；且 SfxBank / MusicDirector / UI 都要读，静态最直接。
    /// 用 PlayerPrefs 而不是 meta.txt 的原因：音量属于「设备设置」，
    /// 换存档应该保留，不该跟着游戏进度一起重置。
    /// </summary>
    public static class AudioMix
    {
        const string KeyMaster = "vol.master";
        const string KeySfx = "vol.sfx";
        const string KeyMusic = "vol.music";
        const string KeyUi = "vol.ui";

        static bool _loaded;
        static float _master = 0.8f;
        static float _sfx = 1f;
        static float _music = 1f;
        static float _ui = 1f;

        public static float Master
        {
            get { EnsureLoaded(); return _master; }
            set { EnsureLoaded(); _master = Mathf.Clamp01(value); Save(); }
        }

        public static float Sfx
        {
            get { EnsureLoaded(); return _sfx; }
            set { EnsureLoaded(); _sfx = Mathf.Clamp01(value); Save(); }
        }

        public static float Music
        {
            get { EnsureLoaded(); return _music; }
            set { EnsureLoaded(); _music = Mathf.Clamp01(value); Save(); }
        }

        public static float Ui
        {
            get { EnsureLoaded(); return _ui; }
            set { EnsureLoaded(); _ui = Mathf.Clamp01(value); Save(); }
        }

        /// <summary>世界音效的最终增益（含 Master）。</summary>
        public static float SfxGain => Master * Sfx;

        /// <summary>音乐的最终增益（含 Master）。</summary>
        public static float MusicGain => Master * Music;

        /// <summary>UI 音的最终增益（含 Master）。</summary>
        public static float UiGain => Master * Ui;

        /// <summary>
        /// 分贝形式（UI 显示用）。0 音量返回 -60dB 而不是 -∞，
        /// 避免界面上显示「-Inf」这种玩家看不懂的东西。
        /// </summary>
        public static float ToDb(float linear)
        {
            if (linear <= 0.0001f) return -60f;
            return Mathf.Clamp(20f * Mathf.Log10(linear), -60f, 6f);
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _master = PlayerPrefs.GetFloat(KeyMaster, 0.8f);
            _sfx = PlayerPrefs.GetFloat(KeySfx, 1f);
            _music = PlayerPrefs.GetFloat(KeyMusic, 1f);
            _ui = PlayerPrefs.GetFloat(KeyUi, 1f);
        }

        static void Save()
        {
            PlayerPrefs.SetFloat(KeyMaster, _master);
            PlayerPrefs.SetFloat(KeySfx, _sfx);
            PlayerPrefs.SetFloat(KeyMusic, _music);
            PlayerPrefs.SetFloat(KeyUi, _ui);
            PlayerPrefs.Save();
        }

        /// <summary>恢复默认（设置界面用）。</summary>
        public static void ResetToDefault()
        {
            _master = 0.8f;
            _sfx = 1f;
            _music = 1f;
            _ui = 1f;
            _loaded = true;
            Save();
        }

        /// <summary>自检用：强制重读（验证持久化）。</summary>
        public static void ReloadForTest()
        {
            _loaded = false;
            EnsureLoaded();
        }
    }
}

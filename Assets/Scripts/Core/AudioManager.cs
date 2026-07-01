using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Quản lý âm thanh game: SFX và BGM.
    /// Dùng AudioSource pool đơn giản để tránh allocations.
    /// </summary>
    public class AudioManager : SingletonMonoBehaviour<AudioManager>
    {

        // ---- SFX ----
        [FoldoutGroup("SFX")]
        public AudioClip swapSFX;
        [FoldoutGroup("SFX")]
        public AudioClip clearSFX;
        [FoldoutGroup("SFX")]
        public AudioClip combo2SFX;
        [FoldoutGroup("SFX")]
        public AudioClip combo3SFX;
        [FoldoutGroup("SFX")]
        public AudioClip winSFX;
        [FoldoutGroup("SFX")]
        public AudioClip loseSFX;
        [FoldoutGroup("SFX")]
        public AudioClip snapBackSFX;
        [FoldoutGroup("SFX")]
        public AudioClip dropSFX;

        // ---- BGM ----
        [FoldoutGroup("BGM")]
        [Required] public AudioSource bgmSource;
        [FoldoutGroup("BGM")]
        public AudioClip[] bgmTracks;
        [FoldoutGroup("BGM")]
        [Range(0f, 1f)] public float bgmVolume = 0.5f;

        // ---- SFX Source Pool ----
        [FoldoutGroup("SFX")]
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
        private AudioSource[] _sfxPool;
        private int _poolIndex;
        private const int PoolSize = 8;

        // ---- Prefs Keys ----
        private const string BGMKey = "BGMEnabled";
        private const string SFXKey = "SFXEnabled";

        public bool BGMEnabled { get; private set; } = true;
        public bool SFXEnabled { get; private set; } = true;

        protected override void OnSingletonInitialized()
        {


            // Build SFX pool

            _sfxPool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform);
                _sfxPool[i] = go.AddComponent<AudioSource>();
                _sfxPool[i].playOnAwake = false;
            }

            // Load settings
            BGMEnabled = PlayerPrefs.GetInt(BGMKey, 1) == 1;
            SFXEnabled = PlayerPrefs.GetInt(SFXKey, 1) == 1;

            PlayBGM();
        }

        // ---- Public Play Methods ----

        public void PlaySwap() => PlaySFX(swapSFX);
        public void PlaySnapBack() => PlaySFX(snapBackSFX);
        public void PlayDrop() => PlaySFX(dropSFX);
        public void PlayWin() => PlaySFX(winSFX);
        public void PlayLose() => PlaySFX(loseSFX);

        public void PlayClear(int combo)
        {
            PlaySFX(clearSFX);
            if (combo == 2 && combo2SFX) PlaySFX(combo2SFX, 0.1f);
            else if (combo >= 3 && combo3SFX) PlaySFX(combo3SFX, 0.1f);
        }

        // ---- Toggle ----

        public void SetBGM(bool enabled)
        {
            BGMEnabled = enabled;
            bgmSource.mute = !enabled;
            PlayerPrefs.SetInt(BGMKey, enabled ? 1 : 0);
        }

        public void SetSFX(bool enabled)
        {
            SFXEnabled = enabled;
            PlayerPrefs.SetInt(SFXKey, enabled ? 1 : 0);
        }

        // ---- Internal ----

        private void PlaySFX(AudioClip clip, float delay = 0f)
        {
            if (!SFXEnabled || clip == null) return;
            var src = _sfxPool[_poolIndex % PoolSize];
            _poolIndex++;
            src.clip = clip;
            src.volume = sfxVolume;
            if (delay > 0f) src.PlayDelayed(delay);
            else src.Play();
        }

        private void PlayBGM()
        {
            if (bgmTracks == null || bgmTracks.Length == 0 || bgmSource == null) return;
            bgmSource.clip = bgmTracks[Random.Range(0, bgmTracks.Length)];
            bgmSource.volume = bgmVolume;
            bgmSource.loop = true;
            bgmSource.mute = !BGMEnabled;
            bgmSource.Play();
        }
    }
}

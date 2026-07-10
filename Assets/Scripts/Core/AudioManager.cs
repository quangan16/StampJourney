using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Manages audio playback: SFX (pooled AudioSources) and BGM.
    /// </summary>
    public class AudioManager : SingletonMonoBehaviour<AudioManager>
    {
        #region Inspector — SFX Clips

        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip swapSFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip clearSFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip combo2SFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip combo3SFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip winSFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip loseSFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip snapBackSFX;
        [FoldoutGroup("SFX")]
        [SerializeField] private AudioClip dropSFX;

        [FoldoutGroup("SFX")]
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.8f;

        #endregion

        #region Inspector — BGM

        [FoldoutGroup("BGM")]
        [Required] [SerializeField] private AudioSource bgmSource;
        [FoldoutGroup("BGM")]
        [SerializeField] private AudioClip[] bgmTracks;
        [FoldoutGroup("BGM")]
        [Range(0f, 1f)] [SerializeField] private float bgmVolume = 0.5f;

        #endregion

        #region SFX Pool

        private AudioSource[] _sfxPool;
        private int _poolIndex;
        private const int PoolSize = 8;

        #endregion

        #region Prefs Keys

        private const string BGMKey = "BGMEnabled";
        private const string SFXKey = "SFXEnabled";

        public bool BGMEnabled { get; private set; } = true;
        public bool SFXEnabled { get; private set; } = true;

        #endregion

        #region Initialization

        protected override void OnSingletonInitialized()
        {
            BuildSfxPool();
            LoadAudioSettings();
            PlayBGM();
        }

        private void BuildSfxPool()
        {
            _sfxPool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform);
                _sfxPool[i] = go.AddComponent<AudioSource>();
                _sfxPool[i].playOnAwake = false;
            }
        }

        private void LoadAudioSettings()
        {
            BGMEnabled = PlayerPrefs.GetInt(BGMKey, 1) == 1;
            SFXEnabled = PlayerPrefs.GetInt(SFXKey, 1) == 1;
        }

        #endregion

        #region Public Play Methods

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

        #endregion

        #region Toggle Settings

        public void SetBGM(bool enabled)
        {
            BGMEnabled = enabled;
            bgmSource.mute = !enabled;
            PlayerPrefs.SetInt(BGMKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetSFX(bool enabled)
        {
            SFXEnabled = enabled;
            PlayerPrefs.SetInt(SFXKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        #endregion

        #region Internal

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

        #endregion
    }
}

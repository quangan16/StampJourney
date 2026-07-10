using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StampJourney.Data;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace StampJourney.Core
{
    /// <summary>
    /// Manages level flow: load level data, start/restart/advance levels, save progress.
    /// </summary>
    public class LevelSystem : SerializedMonoBehaviour
    {
        #region Inspector

        [BoxGroup("Levels")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        [Required]
        [SerializeField] private Dictionary<int, LevelData> levels = new();

        [SerializeField] private string levelDataDirectory;

        #endregion

        #region Properties

        [BoxGroup("Level")]
        [ShowInInspector, ReadOnly]
        public int CurrentLevel
        {
            get => DataManager.Instance.CurrentLevel;
            set => DataManager.Instance.CurrentLevel = value;
        }

        [ShowInInspector, ReadOnly] private LevelData _cachedCurrentLevelData;
        public LevelData CachedLevelData => _cachedCurrentLevelData;

        /// <summary>Read-only access to the level list.</summary>
        public IReadOnlyDictionary<int, LevelData> Levels => levels;

        private AsyncOperationHandle<IList<LevelData>> loadedLevelDataHandle;
        #endregion

        #region Public API — Level Data

        public async UniTask<List<LevelData>> LoadAllLevelDataAsync()
        {
            levels?.Clear();
            IList<LevelData> res;
            loadedLevelDataHandle = Addressables.LoadAssetsAsync<LevelData>("level_data");
            res = await loadedLevelDataHandle.Task;
            if (res == null || res.Count <= 0)
            {
                AndyUtil.Logger.Log("[LevelSystem] Failed to load all level data.");
            }
            else
            {
                Debug.Log(res.Count);
                levels = new();
                foreach (var level in res)
                {
                    levels.Add(level.levelID, level);
                }
                AndyUtil.Logger.Log($"[LevelSystem] Loaded {levels.Count} levels.");
            }


            return levels.Values.ToList();
        }



        /// <summary>
        /// Loads and validates level data for the given 1-based level number.
        /// Returns null if the level is invalid or data is missing.
        /// </summary>
        public async UniTask<LevelData> LoadTargetLevelDataAsync(int targetLevel)
        {
            if (levels == null || levels.Count == 0)
            {
                Debug.LogError("[LevelSystem] levels array is EMPTY! Assign LevelData in the Inspector.");
                return null;
            }

            if (targetLevel <= 0 || targetLevel > levels.Count)
            {
                Debug.LogWarning($"[LevelSystem] Level {targetLevel} out of range (max={levels.Count}).");
                return null;
            }

            if (GameManager.Instance == null)
            {
                Debug.LogError("[LevelSystem] GameManager.Instance is NULL! Add GameManager to the Scene.");
                return null;
            }

            var targetLevelData = await Addressables.LoadAssetAsync<LevelData>($"Level_{targetLevel}");

            if (targetLevelData == null)
            {
                Debug.LogError($"[LevelSystem] Level data at index {targetLevel - 1} is null.");
                return null;
            }

            return targetLevelData;
        }

        public LevelData GetLevelData(int index) =>
            index > 0 && index <= levels.Count ? levels[index] : null;

        public bool IsLevelUnlocked(int index)
        {
            int unlocked = PlayerPrefs.GetInt("UnlockedLevel", 0);
            return index <= unlocked;
        }

        public int GetBestScore(int index) =>
            PlayerPrefs.GetInt($"Level_{index}_BestScore", 0);

        #endregion

        #region Public API — Level Flow

        public async UniTaskVoid StartLevel(int targetLevel)
        {
            if (targetLevel <= 0 || targetLevel > levels.Count)
            {
                Debug.LogError($"[LevelSystem] Invalid level index: {targetLevel}");
                return;
            }

            _cachedCurrentLevelData = GetLevelData(targetLevel);
            if (_cachedCurrentLevelData == null)
            {
                AndyUtil.Logger.LogError("Level data is null!");
                return;
            }

            await GameManager.Instance.LoadSceneAsync(SceneType.Gameplay);
            Debug.Log($"[LevelSystem] Started level {targetLevel}: {_cachedCurrentLevelData.levelID}");
        }

        public void RestartLevel() => StartLevel(CurrentLevel);

        public void GoToNextLevel()
        {
            int nextLevel = CurrentLevel + 1;
            if (nextLevel <= levels.Count)
                StartLevel(nextLevel);
        }

        #endregion


        public void OnDestroy()
        {
            Addressables.Release(_cachedCurrentLevelData);
        }
    }
}

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StampJourney.Core;
using StampJourney.Data;
using StampJourney.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StampJourney.Core
{
    /// <summary>
    /// Quản lý luồng level: load, start, chuyển màn.
    /// Gán danh sách LevelData và điều phối GameManager.
    /// </summary>
    public class LevelSystem : SerializedMonoBehaviour
    {
        [BoxGroup("Levels")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        [Required]
        public List<LevelData> levels;

        [BoxGroup("Level")]
        [ShowInInspector, ReadOnly]
        public int CurrentLevel
        {
            get => DataManager.Instance.CurrentLevel;
            set => DataManager.Instance.CurrentLevel = value;
        }




        private void Awake()
        {
            Debug.Log("[LevelManager] ✅ Awake — Instance set.");
        }

        private void Start()
        {
            Debug.Log($"[LevelManager] Start — levels.Length={levels?.Count ?? 0}, GameManager={GameManager.Instance}");
        }

        public LevelData LoadLevelData(int targetLevel)
        {
            Debug.Log($"[LevelManager] LoadLevel({targetLevel}) — levels.Count={levels?.Count ?? 0}");

            if (levels == null || levels.Count == 0)
            {
                Debug.LogError("[LevelManager] ❌ levels array is EMPTY! Gán LevelData vào Inspector.");
                return null;
            }

            if (targetLevel <= 0 || targetLevel > levels.Count)
            {
                Debug.LogWarning($"[LevelManager] Level {targetLevel} out of range (max={levels.Count}).");
                return null;
            }

            if (GameManager.Instance == null)
            {
                Debug.LogError("[LevelManager] ❌ GameManager.Instance is NULL! Thêm GameManager vào Scene.");
                return null;
            }

            var targetLevelData = levels[targetLevel - 1];
            if (targetLevelData == null)
            {

                return null;
            }


            Debug.Log($"[LevelManager] → Calling GameManager.Loaded level data ({targetLevelData.levelTitle})");
            return targetLevelData;

        }

        public void LoadNextLevelData() => LoadLevelData(CurrentLevel + 1);

        public void ReloadCurrentLevelData()
        {
            LoadLevelData(CurrentLevel);
        }

        public bool IsLevelUnlocked(int index)
        {
            int unlocked = PlayerPrefs.GetInt("UnlockedLevel", 0);
            return index <= unlocked;
        }

        public int GetBestScore(int index) =>
            PlayerPrefs.GetInt($"Level_{index}_BestScore", 0);

        public LevelData GetLevelData(int index) =>
            index >= 0 && index < levels.Count ? levels[index] : null;

        #region Win / Lose

        public async UniTaskVoid StartLevel(int targetLevel)
        {
            if (targetLevel <= 0 || targetLevel > levels.Count)
            {
                Debug.LogError($"[GameManager] Invalid level index: {targetLevel}");
                return;
            }

            var levelData = LoadLevelData(targetLevel);
            if (levelData == null)
            {
                AndyUtil.Logger.LogError("Level data is null!");
                return;
            }
            await SceneManager.LoadSceneAsync("Gameplay");

            Debug.Log($"[GameManager] Started level {targetLevel}: {levelData.levelTitle}");
        }

        public void RestartLevel() => StartLevel(CurrentLevel);

        public void GoToNextLevel()
        {
            int nextLevel = CurrentLevel + 1;
            if (nextLevel <= levels.Count)
                StartLevel(nextLevel);
            // else
            // GoToMainMenu();

        }


        private void SaveProgress(int score)
        {
            string key = $"Level_{CurrentLevel}_BestScore";
            int best = PlayerPrefs.GetInt(key, 0);
            if (score > best) PlayerPrefs.SetInt(key, score);

            // Mở khóa level tiếp theo
            int unlocked = PlayerPrefs.GetInt("UnlockedLevel", 0);
            if (CurrentLevel > unlocked)
                PlayerPrefs.SetInt("UnlockedLevel", CurrentLevel + 1);

            PlayerPrefs.Save();
        }

        #endregion
    }
}

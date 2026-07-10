using System;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StampJourney.Core
{
    /// <summary>
    /// Singleton managing global game state: start, win, lose, scene loading.
    /// </summary>
    public class GameManager : SingletonMonoBehaviour<GameManager>
    {
        #region Inspector

        [BoxGroup("References")]
        [field: SerializeField, Required] public LevelSystem LevelSystem { get; set; }

        [field: SerializeField, Required] public GameConfig GameConfig { get; set; }

        #endregion

        #region Runtime State

        [ShowInInspector, ReadOnly] private GameState _state = GameState.Idle;

        public GameState State
        {
            get => _state;
            internal set => _state = value;
        }

        public Action<SceneType> OnSceneLoadedSuccess;

        #endregion

        #region Unity Lifecycle

        protected override void OnSingletonInitialized()
        {

        }

        private async void Start()
        {
            Application.targetFrameRate = 60;
            await LevelSystem.LoadAllLevelDataAsync();
            StartGameplayLevel(1);
        }

        #endregion

        #region Public API

        public void StartGameplayLevel(int level)
        {
            LevelSystem.StartLevel(level);
        }

        public void GoToMainMenu()
        {
            SceneManager.LoadScene(0);
        }

        public async UniTask LoadSceneAsync(SceneType sceneType)
        {
            AsyncOperation loadSceneAsync = SceneManager.LoadSceneAsync(sceneType.ToString());
            while (!loadSceneAsync.isDone)
            {
                await UniTask.Yield();
            }

            OnSceneLoadedSuccess?.Invoke(sceneType);
        }

        #endregion
    }
}

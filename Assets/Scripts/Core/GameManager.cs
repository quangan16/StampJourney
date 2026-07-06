using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Data;
using StampJourney.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StampJourney.Core
{
    /// <summary>
    /// Singleton quản lý trạng thái game toàn cục: start, win, lose, scoring.
    /// </summary>
    public class GameManager : SingletonMonoBehaviour<GameManager>
    {
        // ---- Inspector ----
        [BoxGroup("References")]
        [field: SerializeField, Required] public LevelSystem LevelSystem { get; set; }
        public Action<SceneType> OnSceneLoadedSuccess;

        [ShowInInspector, ReadOnly] private GameState _state = GameState.Idle;

        [field: SerializeField, Required] public GameConfig GameConfig { get; set; }

        // ---- Constants ----

        private const int BaseStampScore = 100;
        private const int ComboMultiplier = 50;

        // ========================================================
        #region Unity Lifecycle

        protected override void OnSingletonInitialized()
        {
        }

        public void Start()
        {
            Application.targetFrameRate = 60;
            StartGameplayLevel(1);
        }


        #endregion

        // ========================================================
        #region Public API

        public GameState State
        {
            get => _state;
            set => _state = value;
        }



        public void StartGameplayLevel(int level)
        {
            LevelSystem.StartLevel(level);
        }

        public void GoToMainMenu()
        {
            SceneManager.LoadScene(0);
        }

        #endregion

        // ========================================================
        #region Event Handlers


        public async UniTask LoadSceneAsync(string sceneName)
        {
            AsyncOperation loadSceneAsync = SceneManager.LoadSceneAsync(sceneName);
            while (!loadSceneAsync.isDone)
            {
                await UniTask.Yield();
            }

            OnSceneLoadedSuccess?.Invoke(Enum.Parse<SceneType>(sceneName));
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

        // ========================================================



    }

    public enum GameState { Idle, Playing, Won, Lost, Paused }
    public enum SceneType { MainMenu, Gameplay }
}


using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using StampJourney.Data;
using StampJourney.Gameplay;
using StampJourney.UI;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Orchestrates a single gameplay session: scoring, moves, timer, win/lose conditions.
    /// Bridges Gameboard events to UI events.
    /// </summary>
    public class GameplayControl : MonoBehaviour
    {
        #region Inspector

        [SerializeField] private Gameboard gameboard;
        [SerializeField] private GameplayUI gameplayUI;

        #endregion

        #region Runtime State

        private LevelData _levelData;

        [ShowInInspector, ReadOnly] private int _score;
        [ShowInInspector, ReadOnly] private int _remainingMoves;
        [ShowInInspector, ReadOnly] private int _combo;
        [ShowInInspector, ReadOnly] private float _remainingTime;
        private bool _hasTimeLimit;
        private bool _hasMoveLimit;

        #endregion

        #region Public Properties (read-only for UI)

        public int Score => _score;
        public int RemainingMoves => _remainingMoves;
        public float RemainingTime => _remainingTime;
        public bool HasTimeLimit => _hasTimeLimit;
        public bool HasMoveLimit => _hasMoveLimit;
        public LevelData LevelData => _levelData;

        #endregion

        #region Events

        public event Action<int> OnScoreChanged;
        public event Action<int> OnMovesChanged;
        public event Action<int> OnComboChanged;
        public event Action<float> OnTimeChanged;
        public event Action OnGameWon;
        public event Action OnGameLost;
        public event Action OnGameplaySetupFinish;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            GameManager.Instance.OnSceneLoadedSuccess += OnGameplaySceneLoaded;
        }

        private void OnDestroy()
        {
            GameManager.Instance.OnSceneLoadedSuccess -= OnGameplaySceneLoaded;
            if (gameboard != null)
            {
                gameboard.OnSwapCompleted -= HandleSwapCompleted;
                gameboard.OnStampCleared -= HandleStampCleared;
                gameboard.OnBoardSettled -= HandleBoardSettled;
            }
        }

        private void Update()
        {
            if (!_hasTimeLimit) return;
            if (GameManager.Instance.State != GameState.Playing) return;

            _remainingTime -= Time.deltaTime;
            if (_remainingTime <= 0f)
            {
                _remainingTime = 0f;
                OnTimeChanged?.Invoke(_remainingTime);
                TriggerLose();
                return;
            }
            OnTimeChanged?.Invoke(_remainingTime);
        }

        #endregion

        #region Initialization

        public void Init(LevelData levelData)
        {
            if (levelData == null)
            {
                AndyUtil.Logger.LogError("Level data is null!");
                return;
            }
            _levelData = levelData;
            gameboard.Init(_levelData);
        }

        public void Setup()
        {
            if (_levelData == null)
            {
                AndyUtil.Logger.LogError("Level data is null!");
                return;
            }
            if (gameboard == null)
            {
                AndyUtil.Logger.LogError("Gameboard is null!");
                return;
            }

            _score = 0;
            _remainingMoves = _levelData.maxMoves;
            _combo = 0;
            _hasTimeLimit = _levelData.timeLimitSeconds > 0;
            _hasMoveLimit = _levelData.maxMoves > 0;
            _remainingTime = _hasTimeLimit ? _levelData.timeLimitSeconds : 0f;

            gameboard.SetupAsync();
            GameManager.Instance.State = GameState.Playing;

            // Subscribe to board events (unsubscribe first to avoid duplicates)
            gameboard.OnSwapCompleted -= HandleSwapCompleted;
            gameboard.OnSwapCompleted += HandleSwapCompleted;

            gameboard.OnStampCleared -= HandleStampCleared;
            gameboard.OnStampCleared += HandleStampCleared;

            gameboard.OnBoardSettled -= HandleBoardSettled;
            gameboard.OnBoardSettled += HandleBoardSettled;

            // Broadcast initial state
            OnScoreChanged?.Invoke(_score);
            if (_hasMoveLimit) OnMovesChanged?.Invoke(_remainingMoves);
            if (_hasTimeLimit) OnTimeChanged?.Invoke(_remainingTime);

            // Init UI after everything is ready — deterministic order
            if (gameplayUI != null) gameplayUI.Init(this);
            OnGameplaySetupFinish?.Invoke();
        }

        #endregion

        #region Scene Loading

        private void OnGameplaySceneLoaded(SceneType sceneType)
        {
            if (sceneType != SceneType.Gameplay) return;

            var levelData = GameManager.Instance.LevelSystem.CachedLevelData;
            Init(levelData);
            Setup();
        }

        #endregion

        #region Game Flow

        private void TriggerWin()
        {
            GameManager.Instance.State = GameState.Won;
            AudioManager.Instance.PlayWin();
            OnGameWon?.Invoke();
        }

        private void TriggerLose()
        {
            GameManager.Instance.State = GameState.Lost;
            AudioManager.Instance.PlayLose();
            OnGameLost?.Invoke();
        }

        #endregion

        #region Board Event Handlers

        /// <summary>Each successful swap deducts a move and resets the combo.</summary>
        private void HandleSwapCompleted(CardModel a, CardModel b)
        {
            if (GameManager.Instance.State != GameState.Playing) return;

            if (_hasMoveLimit)
            {
                _remainingMoves--;
                OnMovesChanged?.Invoke(_remainingMoves);
            }
            AudioManager.Instance.PlaySwap();

            // Reset combo on new swap
            _combo = 0;
        }

        private void HandleBoardSettled()
        {
            if (GameManager.Instance.State != GameState.Playing) return;

            if (gameboard.IsBoardAndQueuesEmpty())
                TriggerWin();
            else if (_hasMoveLimit && _remainingMoves <= 0)
                TriggerLose();
        }

        /// <summary>Each stamp group cleared increments the combo counter.</summary>
        private void HandleStampCleared(List<CardModel> clearedTiles)
        {
            if (GameManager.Instance.State != GameState.Playing) return;

            _combo++;
            OnScoreChanged?.Invoke(_score);
            OnComboChanged?.Invoke(_combo);
            AudioManager.Instance.PlayClear(_combo);
        }

        #endregion
    }
}
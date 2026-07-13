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

        [BoxGroup("Camera Framing")]
        [SerializeField] private Camera gameplayCamera;
        [BoxGroup("Camera Framing")]
        [Min(0f)][SerializeField] private float horizontalPaddingPixels = 24f;
        [BoxGroup("Camera Framing")]
        [Min(0f)][SerializeField] private float verticalPaddingPixels = 16f;
        [BoxGroup("Camera Framing")]
        [SerializeField] private bool includeWaitingQueue = true;

        #endregion

        #region Runtime State

        private LevelData _levelData;

        [ShowInInspector, ReadOnly] private int _score;
        [ShowInInspector, ReadOnly] private int _remainingMoves;
        [ShowInInspector, ReadOnly] private int _combo;
        [ShowInInspector, ReadOnly] private float _remainingTime;
        private bool _hasTimeLimit;
        private bool _hasMoveLimit;
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;

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
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnSceneLoadedSuccess -= OnGameplaySceneLoaded;
            }

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

        private void LateUpdate()
        {
            if (_levelData == null) return;

            RectTransform header = gameplayUI != null ? gameplayUI.HeaderArea : null;
            RectTransform footer = gameplayUI != null ? gameplayUI.FooterArea : null;
            bool screenChanged = _lastScreenSize.x != Screen.width || _lastScreenSize.y != Screen.height || _lastSafeArea != Screen.safeArea;
            bool uiChanged = (header != null && header.hasChanged) || (footer != null && footer.hasChanged);
            if (!screenChanged && !uiChanged) return;

            FitGameplayCamera();
            if (header != null) header.hasChanged = false;
            if (footer != null) footer.hasChanged = false;
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
            FitGameplayCamera();
        }

        /// <summary>Fits the board and waiting queue inside the safe screen area between the HUD header and footer.</summary>
        public void FitGameplayCamera()
        {
            Camera targetCamera = gameplayCamera != null ? gameplayCamera : Camera.main;
            if (targetCamera == null || !targetCamera.orthographic || gameboard == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            Canvas.ForceUpdateCanvases();
            Rect availableScreenRect = GetAvailableGameplayRect(targetCamera);
            if (availableScreenRect.width <= 1f || availableScreenRect.height <= 1f)
            {
                Debug.LogWarning("[Gameplay Camera] Header and footer leave no usable area for the board.", this);
                return;
            }

            Bounds contentBounds = gameboard.GetGameplayBounds(includeWaitingQueue);
            if (contentBounds.size.x <= 0f || contentBounds.size.y <= 0f) return;

            Rect cameraPixelRect = targetCamera.pixelRect;
            float sizeForWidth = contentBounds.size.x * cameraPixelRect.height / (2f * availableScreenRect.width);
            float sizeForHeight = contentBounds.size.y * cameraPixelRect.height / (2f * availableScreenRect.height);
            targetCamera.orthographicSize = Mathf.Max(sizeForWidth, sizeForHeight);

            float normalizedCenterX = (availableScreenRect.center.x - cameraPixelRect.xMin) / cameraPixelRect.width;
            float normalizedCenterY = (availableScreenRect.center.y - cameraPixelRect.yMin) / cameraPixelRect.height;
            float visibleWorldHeight = targetCamera.orthographicSize * 2f;
            float visibleWorldWidth = visibleWorldHeight * targetCamera.aspect;

            Vector3 cameraPosition = targetCamera.transform.position;
            cameraPosition.x = contentBounds.center.x - (normalizedCenterX - 0.5f) * visibleWorldWidth;
            cameraPosition.y = contentBounds.center.y - (normalizedCenterY - 0.5f) * visibleWorldHeight;
            targetCamera.transform.position = cameraPosition;

            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            _lastSafeArea = Screen.safeArea;
        }

        private Rect GetAvailableGameplayRect(Camera targetCamera)
        {
            Rect cameraRect = targetCamera.pixelRect;
            Rect safeArea = Screen.safeArea;
            float left = Mathf.Max(cameraRect.xMin, safeArea.xMin) + horizontalPaddingPixels;
            float right = Mathf.Min(cameraRect.xMax, safeArea.xMax) - horizontalPaddingPixels;
            float bottom = Mathf.Max(cameraRect.yMin, safeArea.yMin) + verticalPaddingPixels;
            float top = Mathf.Min(cameraRect.yMax, safeArea.yMax) - verticalPaddingPixels;

            RectTransform header = gameplayUI != null ? gameplayUI.HeaderArea : null;
            if (header != null && header.gameObject.activeInHierarchy)
                top = Mathf.Min(top, GetScreenRect(header).yMin - verticalPaddingPixels);

            RectTransform footer = gameplayUI != null ? gameplayUI.FooterArea : null;
            if (footer != null && footer.gameObject.activeInHierarchy)
                bottom = Mathf.Max(bottom, GetScreenRect(footer).yMax + verticalPaddingPixels);

            return Rect.MinMaxRect(left, bottom, right, top);
        }

        private static Rect GetScreenRect(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;

            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[i]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
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

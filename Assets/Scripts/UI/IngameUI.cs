using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using StampJourney.Gameplay;
using StampJourney.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameplayUI : MonoBehaviour, IScreen
{
    public string ScreenName => "Gameplay";
    private GameplayControl _gameplayControl;

    // ---- HUD ----
    [FoldoutGroup("HUD")]
    [Required] public TextMeshProUGUI scoreText;
    [FoldoutGroup("HUD")]
    public TextMeshProUGUI movesText;
    [FoldoutGroup("HUD")]
    [Required] public TextMeshProUGUI levelText;
    [FoldoutGroup("HUD")]
    public TextMeshProUGUI timerText;

    // ---- Panels ----
    [FoldoutGroup("Panels")]
    [Required] public GameObject gameplayPanel;
    [FoldoutGroup("Panels")]
    [Required] public GameObject winPanel;
    [FoldoutGroup("Panels")]
    [Required] public GameObject losePanel;
    [FoldoutGroup("Panels")]
    [Required] public GameObject pausePanel;

    // ---- Win Panel ----
    [FoldoutGroup("Win Panel")]
    [Required] public TextMeshProUGUI winScoreText;
    [FoldoutGroup("Win Panel")]
    public Image[] starImages;

    // ---- Lose Panel ----
    [FoldoutGroup("Lose Panel")]
    [Required] public TextMeshProUGUI loseScoreText;

    // ---- Combo Popup ----
    [FoldoutGroup("Combo")]
    [Required] public TextMeshProUGUI comboText;
    [FoldoutGroup("Combo")]
    public float comboDuration = 1.2f;



    public void Init(GameplayControl gameplayControl)
    {
        if (gameplayControl == null)
        {
            AndyUtil.Logger.LogError("Gameplay controler is null!");
            return;
        }
        if (UIManager.Instance.CurrentActiveScreen is GameplayUI) return;
        this._gameplayControl = gameplayControl;
        UIManager.Instance.CurrentActiveScreen = this;
        Setup();

    }

    public void Setup()
    {
        // Subscribe to data events
        _gameplayControl.OnScoreChanged += UpdateScore;
        _gameplayControl.OnMovesChanged += UpdateMoves;
        _gameplayControl.OnTimeChanged += UpdateTimer;

        // Subscribe to game flow events — UI reacts to these instead of being called directly
        _gameplayControl.OnGameWon += HandleGameWon;
        _gameplayControl.OnGameLost += HandleGameLost;
        _gameplayControl.OnGameplaySetupFinish += HandleSetupFinish;

        Show();
    }

    public void Show()
    {
        ShowGameplay();
    }

    private void OnDestroy()
    {
        if (_gameplayControl == null) return;
        _gameplayControl.OnScoreChanged -= UpdateScore;
        _gameplayControl.OnMovesChanged -= UpdateMoves;
        _gameplayControl.OnTimeChanged -= UpdateTimer;
        _gameplayControl.OnGameWon -= HandleGameWon;
        _gameplayControl.OnGameLost -= HandleGameLost;
        _gameplayControl.OnGameplaySetupFinish -= HandleSetupFinish;
    }

    // ========================================================
    #region Game Flow Handlers

    private void HandleSetupFinish()
    {
        // Show/hide HUD elements based on level config
        if (movesText != null)
            movesText.gameObject.SetActive(_gameplayControl.HasMoveLimit);
        if (timerText != null)
            timerText.gameObject.SetActive(_gameplayControl.HasTimeLimit);
    }

    private void HandleGameWon()
    {
        ShowWinScreen(_gameplayControl.Score, _gameplayControl.LevelData.levelID);
        UIManager.Instance.ShowToast("YOU WIN!");
    }

    private void HandleGameLost()
    {
        ShowLoseScreen(_gameplayControl.Score);

        // Show context-specific toast
        if (_gameplayControl.HasTimeLimit && _gameplayControl.RemainingTime <= 0f)
            UIManager.Instance.ShowToast("TIME'S UP!");
        else if (_gameplayControl.HasMoveLimit && _gameplayControl.RemainingMoves <= 0)
            UIManager.Instance.ShowToast("OUT OF MOVES!");
    }

    #endregion

    // ========================================================
    #region Panel Control

    public void ShowGameplay()
    {
        gameplayPanel.SetActive(true);
        winPanel.SetActive(false);
        losePanel.SetActive(false);
        pausePanel.SetActive(false);

        if (GameManager.Instance != null)
            levelText.text = $"LEVEL {DataManager.Instance.CurrentLevel + 1}";
    }

    public void ShowWinScreen(int score, int levelIndex)
    {
        winPanel.SetActive(true);
        winScoreText.text = score.ToString("N0");

        // Animate win panel bounce in
        winPanel.transform.localScale = Vector3.zero;
        winPanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
    }

    public void ShowLoseScreen(int score)
    {
        losePanel.SetActive(true);
        loseScoreText.text = score.ToString("N0");

        losePanel.transform.localScale = Vector3.zero;
        losePanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
    }

    #endregion

    // ========================================================
    #region HUD Updates

    private void UpdateScore(int score)
    {
        scoreText.text = score.ToString("N0");
        scoreText.transform.DOPunchScale(Vector3.one * 0.25f, 0.3f, 5, 0.5f);
    }

    private void UpdateMoves(int moves)
    {
        if (movesText == null) return;
        movesText.text = $"Move left: {moves}";

        // Đổi màu khi gần hết moves
        movesText.color = moves <= 5 ? Color.red : Color.white;
        if (moves <= 5)
            movesText.transform.DOPunchScale(Vector3.one * 0.3f, 0.25f, 5, 0.5f);
    }

    private void UpdateTimer(float remainingTime)
    {
        if (timerText == null) return;

        if (remainingTime <= 0f)
        {
            timerText.text = "00:00";
            timerText.color = Color.red;
            return;
        }

        int minutes = Mathf.FloorToInt(remainingTime / 60f);
        int seconds = Mathf.FloorToInt(remainingTime % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";

        // Warning color when ≤ 10s
        if (remainingTime <= 10f)
        {
            timerText.color = Color.red;
            if (Mathf.FloorToInt(remainingTime + Time.deltaTime) != seconds)
                timerText.transform.DOPunchScale(Vector3.one * 0.3f, 0.25f, 5, 0.5f);
        }
        else
        {
            timerText.color = Color.white;
        }
    }

    #endregion

    // ========================================================
    #region Combo Popup

    public void ShowComboText(int combo, int points)
    {
        if (comboText == null) return;

        comboText.gameObject.SetActive(true);
        comboText.text = combo > 1
            ? $"COMBO x{combo}!\n+{points}"
            : $"+{points}";

        comboText.transform.localScale = Vector3.zero;

        var seq = DOTween.Sequence();
        seq.Append(comboText.transform.DOScale(1.2f, 0.2f).SetEase(Ease.OutBack));
        seq.Append(comboText.transform.DOMoveY(
            comboText.transform.position.y + 80f, comboDuration).SetEase(Ease.OutQuad));
        seq.Join(comboText.DOFade(0f, comboDuration));
        seq.OnComplete(() =>
        {
            comboText.gameObject.SetActive(false);
            comboText.transform.localScale = Vector3.one;
        });
    }

    #endregion

    // ========================================================
    #region Button Handlers

    public void OnPauseClicked() => pausePanel.SetActive(true);
    public void OnResumeClicked() => pausePanel.SetActive(false);

    public void OnRestartClicked()
    {
        winPanel.SetActive(false);
        losePanel.SetActive(false);
        GameManager.Instance.LevelSystem.RestartLevel();
    }

    public void OnNextLevelClicked() => GameManager.Instance.LevelSystem.GoToNextLevel();
    public void OnHomeClicked() => GameManager.Instance.GoToMainMenu();

    #endregion
}

public struct UIGameplayData
{
    public int moveLeft;
    public float timeLeft;
    public int currentLevel;
}
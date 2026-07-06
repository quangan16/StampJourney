using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
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
    [Required] public TextMeshProUGUI movesText;
    [FoldoutGroup("HUD")]
    [Required] public TextMeshProUGUI levelText;

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

    public void Start()
    {
        var gameplayControl = FindObjectOfType<GameplayControl>();
        if (gameplayControl == null)
        {
            AndyUtil.Logger.LogError("Gameplay controler not found!");
            return;
        }
        Init(gameplayControl);
    }

    public void Init(GameplayControl gameplayControl)
    {
        if (gameplayControl == null)
        {
            AndyUtil.Logger.LogError("Gameplay controler is null!");
            return;
        }
        if (UIManager.Instance.currentActiveScreen == this) return;
        this._gameplayControl = gameplayControl;
        UIManager.Instance.currentActiveScreen = this;
        Setup();

    }

    public void Setup()
    {
        _gameplayControl.OnScoreChanged += UpdateScore;
        _gameplayControl.OnMovesChanged += UpdateMoves;
        Show();
    }

    public void Show()
    {
        ShowGameplay();
    }
    // ---- Subscribe to events ----



    private void OnDestroy()
    {
        _gameplayControl.OnScoreChanged -= UpdateScore;
        _gameplayControl.OnMovesChanged -= UpdateMoves;
    }

    // ---- Panel control ----

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

    // ---- HUD updates ----

    private void UpdateScore(int score)
    {
        scoreText.text = score.ToString("N0");
        scoreText.transform.DOPunchScale(Vector3.one * 0.25f, 0.3f, 5, 0.5f);
    }

    private void UpdateMoves(int moves)
    {
        movesText.text = $"Move left: {moves}";

        // Đổi màu khi gần hết moves
        movesText.color = moves <= 5 ? Color.red : Color.white;
        if (moves <= 5)
            movesText.transform.DOPunchScale(Vector3.one * 0.3f, 0.25f, 5, 0.5f);
    }

    // ---- Combo popup ----

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

    // ---- Button handlers (gán trong Inspector) ----

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
}

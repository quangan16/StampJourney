using System.Collections.Generic;
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

    public Button ReplayBtn;
    public Button PauseBtn;


    // ---- Gameplay framing ----

    [FoldoutGroup("Gameplay Framing")]
    [Tooltip("Top HUD area that gameplay must remain below. Defaults to the child named Header.")]
    [SerializeField] private RectTransform headerArea;
    [FoldoutGroup("Gameplay Framing")]
    [Tooltip("Bottom HUD area that gameplay must remain above. Defaults to the child named Footer.")]
    [SerializeField] private RectTransform footerArea;

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
    public Button nextBtn;
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

    // ---- Completed Topic Banner ----
    [FoldoutGroup("Completed Topic")]
    [Tooltip("Optional authored banner. A runtime bar is created automatically when left empty.")]
    [SerializeField] private RectTransform completedTopicBar;
    [FoldoutGroup("Completed Topic")]
    [Tooltip("Prefab instantiated once for each completed topic. It must contain a TMP text component.")]
    [SerializeField] private RectTransform completedTopicPrefab;
    [FoldoutGroup("Completed Topic")]
    [MinValue(0.1f)] public float completedTopicDuration = 1.5f;
    [FoldoutGroup("Completed Topic")]
    [MinValue(0.05f)] public float completedTopicFadeDuration = 0.22f;
    [FoldoutGroup("Completed Topic")]
    [MinValue(20f)] public float completedTopicSlideDistance = 420f;

    private CanvasGroup _completedTopicCanvasGroup;
    private Sequence _completedTopicSequence;
    private Vector2 _completedTopicRestPosition;
    private bool _completedTopicBarInitialized;
    private readonly HashSet<string> _visibleCompletedTopics = new();
    private readonly List<RectTransform> _completedTopicElements = new();
    private readonly List<TextMeshProUGUI> _completedTopicLabels = new();

    public RectTransform HeaderArea => ResolveFramingArea(ref headerArea, "Header");
    public RectTransform FooterArea => ResolveFramingArea(ref footerArea, "Footer");



    public void Init(GameplayControl gameplayControl)
    {
        if (gameplayControl == null)
        {
            AndyUtil.Logger.LogError("Gameplay controler is null!");
            return;
        }
        this._gameplayControl = gameplayControl;
        UIManager.Instance.CurrentActiveScreen = this;
        nextBtn?.onClick.AddListener(() => GameManager.Instance?.LevelSystem.GoToNextLevel());
        ReplayBtn?.onClick.AddListener(() => OnRestartClicked());
        Setup();
    }

    public void Setup()
    {
        // Subscribe to data events
        _gameplayControl.OnScoreChanged += UpdateScore;
        _gameplayControl.OnMovesChanged += UpdateMoves;
        _gameplayControl.OnTimeChanged += UpdateTimer;
        _gameplayControl.OnTopicCompleted += ShowCompletedTopic;

        // Subscribe to game flow events — UI reacts to these instead of being called directly
        _gameplayControl.OnGameWon += HandleGameWon;
        _gameplayControl.OnGameLost += HandleGameLost;
        _gameplayControl.OnGameplaySetupFinish += HandleSetupFinish;

        Show();
    }

    private RectTransform ResolveFramingArea(ref RectTransform area, string childName)
    {
        if (area != null) return area;

        foreach (RectTransform candidate in GetComponentsInChildren<RectTransform>(true))
        {
            if (candidate.name != childName) continue;
            area = candidate;
            break;
        }
        return area;
    }

    public void Show()
    {
        HideAllPanels();
        ShowGameplay();
    }

    private void OnDestroy()
    {
        if (_gameplayControl == null) return;
        _gameplayControl.OnScoreChanged -= UpdateScore;
        _gameplayControl.OnMovesChanged -= UpdateMoves;
        _gameplayControl.OnTimeChanged -= UpdateTimer;
        _gameplayControl.OnTopicCompleted -= ShowCompletedTopic;
        _gameplayControl.OnGameWon -= HandleGameWon;
        _gameplayControl.OnGameLost -= HandleGameLost;
        _gameplayControl.OnGameplaySetupFinish -= HandleSetupFinish;
        _completedTopicSequence?.Kill();
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
        Debug.Log("Game won!");
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
            levelText.text = $"LEVEL {DataManager.Instance.CurrentLevel}";
    }

    public void ShowWinScreen(int score, int levelIndex)
    {
        winPanel.SetActive(true);
        nextBtn.gameObject.SetActive(levelIndex <= GameManager.Instance.LevelSystem.TotalLevelCount);

        // Animate win panel bounce in
        winPanel.transform.localScale = Vector3.zero;
        winPanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
    }

    public void ShowLoseScreen(int score)
    {
        if (losePanel == null) return;

        losePanel.SetActive(true);
        if (loseScoreText != null)
            loseScoreText.text = score.ToString("N0");

        losePanel.transform.localScale = Vector3.zero;
        losePanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
    }

    public void HideAllPanels()
    {
        gameplayPanel.SetActive(false);
        winPanel.SetActive(false);
        losePanel.SetActive(false);
        pausePanel.SetActive(false);
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
    #region Completed Topic Banner

    public void ShowCompletedTopic(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName)) return;

        EnsureCompletedTopicBar();
        if (completedTopicBar == null || _completedTopicCanvasGroup == null)
            return;

        bool startingNewPresentation = !completedTopicBar.gameObject.activeSelf;
        if (startingNewPresentation)
        {
            ClearCompletedTopicLabels();
            _visibleCompletedTopics.Clear();
            completedTopicBar.gameObject.SetActive(true);
            completedTopicBar.SetAsLastSibling();
            _completedTopicCanvasGroup.alpha = 0f;
            completedTopicBar.anchoredPosition =
                _completedTopicRestPosition + new Vector2(completedTopicSlideDistance, 0f);
        }

        RectTransform enteringElement = null;
        if (_visibleCompletedTopics.Add(topicName))
        {
            enteringElement = CreateCompletedTopicElement(topicName);
            if (enteringElement == null)
            {
                _visibleCompletedTopics.Remove(topicName);
                return;
            }
            LayoutCompletedTopicElements(enteringElement);
        }

        // Another topic completed in the same batch joins the current banner and restarts its
        // hold time instead of replacing the first topic name.
        _completedTopicSequence?.Kill(false);
        float fadeDuration = Mathf.Max(0.05f, completedTopicFadeDuration);
        float holdDuration = Mathf.Max(0.1f, completedTopicDuration);

        _completedTopicSequence = DOTween.Sequence()
            .SetUpdate(true)
            .Append(_completedTopicCanvasGroup.DOFade(1f, fadeDuration).SetEase(Ease.OutSine))
            .Join(completedTopicBar.DOAnchorPos(_completedTopicRestPosition, fadeDuration)
                .SetEase(Ease.OutCubic))
            .AppendInterval(holdDuration)
            .Append(_completedTopicCanvasGroup.DOFade(0f, fadeDuration).SetEase(Ease.InSine))
            .OnComplete(() =>
            {
                if (completedTopicBar != null)
                {
                    completedTopicBar.gameObject.SetActive(false);
                    completedTopicBar.anchoredPosition = _completedTopicRestPosition;
                }
                ClearCompletedTopicLabels();
                _visibleCompletedTopics.Clear();
                _completedTopicSequence = null;
            });
    }

    private void EnsureCompletedTopicBar()
    {
        if (_completedTopicBarInitialized) return;

        if (completedTopicBar == null)
        {
            Transform parent = gameplayPanel != null ? gameplayPanel.transform : transform;
            var barObject = new GameObject(
                "Completed Topic Bar",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            completedTopicBar = barObject.GetComponent<RectTransform>();
            completedTopicBar.SetParent(parent, false);
            completedTopicBar.anchorMin = new Vector2(0.5f, 0.72f);
            completedTopicBar.anchorMax = new Vector2(0.5f, 0.72f);
            completedTopicBar.pivot = new Vector2(0.5f, 0.5f);
            completedTopicBar.sizeDelta = new Vector2(320f, 108f);
            completedTopicBar.anchoredPosition = Vector2.zero;

            Image barImage = barObject.GetComponent<Image>();
            // This object is only the centered slider container. Each topic creates its own
            // visible bar element below it.
            barImage.color = Color.clear;
            barImage.raycastTarget = false;

        }

        _completedTopicCanvasGroup = completedTopicBar.GetComponent<CanvasGroup>();
        if (_completedTopicCanvasGroup == null)
            _completedTopicCanvasGroup = completedTopicBar.gameObject.AddComponent<CanvasGroup>();

        // The bar is a layout/animation container; each generated topic element owns its
        // visible background.
        Image containerImage = completedTopicBar.GetComponent<Image>();
        if (containerImage != null)
        {
            containerImage.color = Color.clear;
            containerImage.raycastTarget = false;
        }

        _completedTopicCanvasGroup.interactable = false;
        _completedTopicCanvasGroup.blocksRaycasts = false;
        _completedTopicRestPosition = completedTopicBar.anchoredPosition;
        completedTopicBar.gameObject.SetActive(false);
        _completedTopicBarInitialized = true;
    }

    private RectTransform CreateCompletedTopicElement(string topicName)
    {
        RectTransform element = completedTopicPrefab != null
            ? Instantiate(completedTopicPrefab, completedTopicBar, false)
            : CreateFallbackCompletedTopicElement();
        element.name = $"Topic Element - {topicName}";
        element.anchorMin = new Vector2(0.5f, 0.5f);
        element.anchorMax = new Vector2(0.5f, 0.5f);
        element.pivot = new Vector2(0.5f, 0.5f);

        CanvasGroup elementCanvasGroup = element.GetComponent<CanvasGroup>();
        if (elementCanvasGroup == null)
            elementCanvasGroup = element.gameObject.AddComponent<CanvasGroup>();
        elementCanvasGroup.alpha = 0f;
        elementCanvasGroup.interactable = false;
        elementCanvasGroup.blocksRaycasts = false;

        TextMeshProUGUI label = element.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
        {
            Debug.LogError("Completed topic prefab must contain a TextMeshProUGUI component.", element);
            Destroy(element.gameObject);
            return null;
        }
        label.name = $"Topic - {topicName}";
        label.gameObject.SetActive(true);
        label.text = topicName;
        label.alpha = 1f;
        label.ForceMeshUpdate();

        float elementWidth = Mathf.Clamp(label.preferredWidth + 64f, 180f, 360f);
        element.sizeDelta = new Vector2(elementWidth, 88f);

        RectTransform textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = new Vector2(24f, 8f);
        textRect.offsetMax = new Vector2(-24f, -8f);

        _completedTopicElements.Add(element);
        _completedTopicLabels.Add(label);
        return element;
    }

    private RectTransform CreateFallbackCompletedTopicElement()
    {
        var elementObject = new GameObject(
            "Runtime Completed Topic Element",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup));
        RectTransform element = elementObject.GetComponent<RectTransform>();
        element.SetParent(completedTopicBar, false);
        elementObject.GetComponent<Image>().color = new Color(0.12f, 0.08f, 0.24f, 0.94f);

        var textObject = new GameObject(
            "Topic Name",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(element, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 8f);
        textRect.offsetMax = new Vector2(-24f, -8f);

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 42f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 24f;
        label.fontSizeMax = 42f;
        label.raycastTarget = false;
        return element;
    }

    private void LayoutCompletedTopicElements(RectTransform enteringElement)
    {
        const float spacing = 26f;
        const float horizontalPadding = 54f;
        float totalWidth = 0f;
        foreach (RectTransform element in _completedTopicElements)
        {
            if (element == null) continue;
            totalWidth += element.sizeDelta.x;
        }
        totalWidth += spacing * Mathf.Max(0, _completedTopicElements.Count - 1);

        float cursor = -totalWidth * 0.5f;
        float moveDuration = Mathf.Max(0.12f, completedTopicFadeDuration);
        foreach (RectTransform element in _completedTopicElements)
        {
            if (element == null) continue;
            float width = element.sizeDelta.x;
            Vector2 target = new Vector2(cursor + width * 0.5f, 0f);
            cursor += width + spacing;

            element.DOKill(false);
            CanvasGroup canvasGroup = element.GetComponent<CanvasGroup>();
            canvasGroup?.DOKill(false);
            if (element == enteringElement)
                element.anchoredPosition = target + new Vector2(180f, 0f);

            element.DOAnchorPos(target, moveDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
            if (canvasGroup != null)
                canvasGroup.DOFade(1f, moveDuration)
                    .SetEase(Ease.OutSine)
                    .SetUpdate(true);
        }

        Vector2 targetBarSize = new Vector2(
            Mathf.Max(320f, totalWidth + horizontalPadding * 2f),
            completedTopicBar.sizeDelta.y);
        completedTopicBar.DOSizeDelta(targetBarSize, moveDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true);
    }

    private void ClearCompletedTopicLabels()
    {
        foreach (RectTransform element in _completedTopicElements)
        {
            if (element == null) continue;
            element.DOKill(false);
            element.GetComponent<CanvasGroup>()?.DOKill(false);
            Destroy(element.gameObject);
        }
        _completedTopicElements.Clear();
        _completedTopicLabels.Clear();
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

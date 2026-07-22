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

    // ---- Development level cheat ----
    [FoldoutGroup("Debug Level Cheat")]
    [Tooltip("Shows a level-loading panel in the Editor and Development Builds.")]
    [SerializeField] private bool enableLevelCheat = true;
    [FoldoutGroup("Debug Level Cheat")]
    [Tooltip("Optional authored panel. Leave these references empty to create a simple runtime panel.")]
    [SerializeField] private RectTransform levelCheatPanel;
    [FoldoutGroup("Debug Level Cheat")]
    [SerializeField] private TMP_InputField levelCheatInput;
    [FoldoutGroup("Debug Level Cheat")]
    [SerializeField] private Button levelCheatLoadButton;

    private CanvasGroup _completedTopicCanvasGroup;
    private Sequence _completedTopicSequence;
    private Vector2 _completedTopicRestPosition;
    private bool _completedTopicBarInitialized;
    private readonly HashSet<string> _visibleCompletedTopics = new();
    private readonly List<RectTransform> _completedTopicElements = new();

    public RectTransform HeaderArea => ResolveFramingArea(ref headerArea, "Header");
    public RectTransform FooterArea => ResolveFramingArea(ref footerArea, "Footer");



    public void Init(GameplayControl gameplayControl)
    {
        if (gameplayControl == null)
        {
            AndyUtil.Logger.LogError("Gameplay controler is null!");
            return;
        }
        UnsubscribeFromGameplay();
        _gameplayControl = gameplayControl;
        UIManager.Instance.CurrentActiveScreen = this;
        if (nextBtn != null)
        {
            nextBtn.onClick.RemoveListener(OnNextLevelClicked);
            nextBtn.onClick.AddListener(OnNextLevelClicked);
        }
        if (ReplayBtn != null)
        {
            ReplayBtn.onClick.RemoveListener(OnRestartClicked);
            ReplayBtn.onClick.AddListener(OnRestartClicked);
        }

        SubscribeToGameplay();
        UpdateHudVisibility();
        Show();
        SetupLevelCheat();
    }

    private void SubscribeToGameplay()
    {
        // Subscribe to data events
        _gameplayControl.OnScoreChanged += UpdateScore;
        _gameplayControl.OnMovesChanged += UpdateMoves;
        _gameplayControl.OnTimeChanged += UpdateTimer;
        _gameplayControl.OnTopicCompleted += ShowCompletedTopic;

        // Subscribe to game flow events — UI reacts to these instead of being called directly
        _gameplayControl.OnGameWon += HandleGameWon;
        _gameplayControl.OnGameLost += HandleGameLost;
    }

    private void UnsubscribeFromGameplay()
    {
        if (_gameplayControl == null) return;
        _gameplayControl.OnScoreChanged -= UpdateScore;
        _gameplayControl.OnMovesChanged -= UpdateMoves;
        _gameplayControl.OnTimeChanged -= UpdateTimer;
        _gameplayControl.OnTopicCompleted -= ShowCompletedTopic;
        _gameplayControl.OnGameWon -= HandleGameWon;
        _gameplayControl.OnGameLost -= HandleGameLost;
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
        UnsubscribeFromGameplay();
        if (nextBtn != null) nextBtn.onClick.RemoveListener(OnNextLevelClicked);
        if (ReplayBtn != null) ReplayBtn.onClick.RemoveListener(OnRestartClicked);
        if (levelCheatLoadButton != null)
            levelCheatLoadButton.onClick.RemoveListener(LoadCheatLevel);
        if (levelCheatInput != null)
            levelCheatInput.onSubmit.RemoveListener(LoadCheatLevel);
        _completedTopicSequence?.Kill();
    }

    private void SetupLevelCheat()
    {
        bool isAllowed = enableLevelCheat && (Application.isEditor || Debug.isDebugBuild);
        if (!isAllowed)
        {
            if (levelCheatPanel != null)
                levelCheatPanel.gameObject.SetActive(false);
            return;
        }

        ResolveOrCreateLevelCheatPanel();
        if (levelCheatPanel == null || levelCheatInput == null || levelCheatLoadButton == null)
        {
            Debug.LogWarning("[GameplayUI] Could not create the level cheat panel.");
            return;
        }

        levelCheatInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        levelCheatInput.SetTextWithoutNotify(
            GameManager.Instance != null && GameManager.Instance.LevelSystem != null
                ? GameManager.Instance.LevelSystem.CurrentLevel.ToString()
                : "1");

        levelCheatLoadButton.onClick.RemoveListener(LoadCheatLevel);
        levelCheatLoadButton.onClick.AddListener(LoadCheatLevel);
        levelCheatInput.onSubmit.RemoveListener(LoadCheatLevel);
        levelCheatInput.onSubmit.AddListener(LoadCheatLevel);

        levelCheatPanel.gameObject.SetActive(true);
        levelCheatPanel.SetAsLastSibling();
    }

    private void ResolveOrCreateLevelCheatPanel()
    {
        if (levelCheatPanel != null)
        {
            if (levelCheatInput == null)
                levelCheatInput = levelCheatPanel.GetComponentInChildren<TMP_InputField>(true);
            if (levelCheatLoadButton == null)
                levelCheatLoadButton = levelCheatPanel.GetComponentInChildren<Button>(true);
        }

        if (levelCheatPanel != null && levelCheatInput != null && levelCheatLoadButton != null)
            return;

        if (levelCheatPanel != null)
            levelCheatPanel.gameObject.SetActive(false);

        Transform parent = gameplayPanel != null ? gameplayPanel.transform : transform;
        GameObject panelObject = new(
            "Level Cheat Panel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        levelCheatPanel = panelObject.GetComponent<RectTransform>();
        levelCheatPanel.SetParent(parent, false);
        levelCheatPanel.anchorMin = Vector2.one;
        levelCheatPanel.anchorMax = Vector2.one;
        levelCheatPanel.pivot = Vector2.one;
        levelCheatPanel.anchoredPosition = new Vector2(-20f, -160f);
        levelCheatPanel.sizeDelta = new Vector2(330f, 84f);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.05f, 0.07f, 0.1f, 0.92f);

        CreateCheatLabel(levelCheatPanel, "LEVEL", new Vector2(12f, -8f), new Vector2(306f, 24f),
            TextAlignmentOptions.Left, 18f);
        levelCheatInput = CreateCheatInput(levelCheatPanel);
        levelCheatLoadButton = CreateCheatButton(levelCheatPanel);
    }

    private static TMP_InputField CreateCheatInput(RectTransform parent)
    {
        GameObject inputObject = new(
            "Level Input",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(TMP_InputField));
        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.SetParent(parent, false);
        inputRect.anchorMin = new Vector2(0f, 1f);
        inputRect.anchorMax = new Vector2(0f, 1f);
        inputRect.pivot = new Vector2(0f, 1f);
        inputRect.anchoredPosition = new Vector2(12f, -36f);
        inputRect.sizeDelta = new Vector2(190f, 38f);
        inputObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);

        GameObject viewportObject = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(inputRect, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(10f, 2f);
        viewport.offsetMax = new Vector2(-10f, -2f);

        TextMeshProUGUI inputText = CreateCheatLabel(
            viewport,
            string.Empty,
            Vector2.zero,
            Vector2.zero,
            TextAlignmentOptions.MidlineLeft,
            22f,
            true);
        TextMeshProUGUI placeholder = CreateCheatLabel(
            viewport,
            "Level ID",
            Vector2.zero,
            Vector2.zero,
            TextAlignmentOptions.MidlineLeft,
            20f,
            true);
        placeholder.color = new Color(1f, 1f, 1f, 0.45f);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.textViewport = viewport;
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return input;
    }

    private static Button CreateCheatButton(RectTransform parent)
    {
        GameObject buttonObject = new(
            "Load Level Button",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(parent, false);
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-12f, -36f);
        buttonRect.sizeDelta = new Vector2(108f, 38f);
        buttonObject.GetComponent<Image>().color = new Color(0.24f, 0.55f, 0.95f, 1f);

        CreateCheatLabel(buttonRect, "LOAD", Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Center, 20f, true);
        return buttonObject.GetComponent<Button>();
    }

    private static TextMeshProUGUI CreateCheatLabel(
        RectTransform parent,
        string value,
        Vector2 anchoredPosition,
        Vector2 size,
        TextAlignmentOptions alignment,
        float fontSize,
        bool stretch = false)
    {
        GameObject labelObject = new("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(parent, false);
        if (stretch)
        {
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }
        else
        {
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = anchoredPosition;
            labelRect.sizeDelta = size;
        }

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private void LoadCheatLevel() =>
        LoadCheatLevel(levelCheatInput != null ? levelCheatInput.text : string.Empty);

    private void LoadCheatLevel(string enteredLevel)
    {
        if (!int.TryParse(enteredLevel, out int levelId) || levelId < 1)
        {
            Debug.LogWarning($"[GameplayUI] '{enteredLevel}' is not a valid level ID.");
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || gameManager.LevelSystem == null)
        {
            Debug.LogError("[GameplayUI] Cannot load cheat level because GameManager or LevelSystem is missing.");
            return;
        }

        if (!gameManager.LevelSystem.HasLevel(levelId))
        {
            Debug.LogWarning($"[GameplayUI] Level ID {levelId} is not authored or loaded.");
            return;
        }

        gameManager.StartGameplayLevel(levelId);
    }

    // ========================================================
    #region Game Flow Handlers

    private void UpdateHudVisibility()
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

using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using StampJourney.Gameplay;
using UnityEngine;

namespace StampJourney.Card
{
    /// <summary>
    /// Renders postage stamp perforation edges for a card.
    ///
    /// Uses 4 separate SpriteRenderers (one per edge) with a single shared sprite.
    /// Each edge is shown/hidden independently based on neighboring tiles.
    /// Advantages over 16-sprite approach:
    ///   - Only 1 art asset needed
    ///   - Each edge can animate independently (fade in/out when stamps connect)
    ///   - Simple, debuggable code
    /// </summary>
    public class CardEdgeRenderer : SerializedMonoBehaviour
    {
        #region Inspector — Edge Images

        [BoxGroup("Edge Images")]
        [InfoBox("Each SpriteRenderer is a perforation strip. Same sprite, different rotations.\n" +
                 "Top=0°, Right=90°, Bottom=180°, Left=270°")]
        [Required] public SpriteRenderer edgeTop;
        [BoxGroup("Edge Images")]
        [Required] public SpriteRenderer edgeRight;
        [BoxGroup("Edge Images")]
        [Required] public SpriteRenderer edgeBottom;
        [BoxGroup("Edge Images")]
        [Required] public SpriteRenderer edgeLeft;

        #endregion

        #region Inspector — Settings

        [BoxGroup("Settings")]
        [LabelText("Animate Edge Transitions")]
        [Tooltip("Fades edges in/out when stamps connect — disable for maximum performance")]
        public bool animateTransitions = true;

        [BoxGroup("Settings")]
        [ShowIf("animateTransitions")]
        public float transitionDuration = 0.15f;

        [BoxGroup("Liquid Merge")]
        [LabelText("Enable Liquid Merge")]
        public bool enableLiquidMerge = true;

        [BoxGroup("Liquid Merge")]
        [ShowIf("enableLiquidMerge")]
        [Required]
        [SerializeField] private SpriteRenderer _rightLiquidBridge;

        [BoxGroup("Liquid Merge")]
        [ShowIf("enableLiquidMerge")]
        [Required]
        [SerializeField] private SpriteRenderer _bottomLiquidBridge;

        [BoxGroup("Liquid Merge")]
        [ShowIf("enableLiquidMerge")]
        [MinValue(0.05f)]
        public float liquidMergeDuration = 0.65f;

        [BoxGroup("Liquid Merge")]
        [ShowIf("enableLiquidMerge")]
        [Range(0.5f, 1.2f)]
        [Tooltip("Final bridge width relative to the card background. Use 1 for full width.")]
        public float liquidThickness = 1f;

        [BoxGroup("Liquid Merge")]
        [ShowIf("enableLiquidMerge")]
        [Range(0f, 0.15f)]
        public float liquidWobble = 0.035f;

        #endregion

        #region Runtime

        private CardModel _model;
        private Gameboard _board;
        private MaterialPropertyBlock _liquidProperties;
        private Tween _rightLiquidTween;
        private Tween _bottomLiquidTween;
        private float _rightLiquidProgress;
        private float _bottomLiquidProgress;
        private bool _rightLiquidConnected;
        private bool _bottomLiquidConnected;
        private Color _currentLiquidColor = Color.white;
        private int _liquidBridgeSortingOrder;

        private static readonly int LiquidColorId = Shader.PropertyToID("_LiquidColor");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int CenterOffsetId = Shader.PropertyToID("_CenterOffset");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int MaxSmoothId = Shader.PropertyToID("_MaxSmooth");
        private static readonly int WobbleId = Shader.PropertyToID("_Wobble");

        #endregion

        #region Public API

        public void Init(CardModel model, Gameboard board)
        {
            _model = model;
            _board = board;
            CardView cardView = GetComponentInParent<CardView>();
            _currentLiquidColor = cardView != null && cardView.backGroundImg != null
                ? cardView.backGroundImg.color
                : Color.white;
            if (enableLiquidMerge)
                ConfigureLiquidBridges();
            ResetLiquidBridges();
            UpdateEdges();
        }

        /// <summary>Synchronizes active liquid bridges with the card background color tween.</summary>
        public void SetLiquidColor(Color color)
        {
            _currentLiquidColor = color;
            ApplyLiquidProperties(_rightLiquidBridge, _rightLiquidProgress, true);
            ApplyLiquidProperties(_bottomLiquidBridge, _bottomLiquidProgress, false);
        }

        /// <summary>
        /// Keeps the bridges at the same effective order as this card's background while
        /// following the card when its SortingGroup is raised for dragging or dropping.
        /// </summary>
        public void SetLiquidBridgeSortingOrder(int cardSortingOrder)
        {
            _liquidBridgeSortingOrder = cardSortingOrder;

            if (_rightLiquidBridge != null)
                _rightLiquidBridge.sortingOrder = _liquidBridgeSortingOrder;
            if (_bottomLiquidBridge != null)
                _bottomLiquidBridge.sortingOrder = _liquidBridgeSortingOrder;
        }

        /// <summary>
        /// Updates the visibility of all 4 edges based on neighboring tiles.
        /// Call after each swap or when the board changes.
        /// </summary>
        public void UpdateEdges()
        {
            if (_model == null) return;

            bool connectedRight = IsConnected(1, 0);
            bool connectedBottom = IsConnected(0, 1);

            SetEdge(edgeTop, IsConnected(0, -1));
            SetEdge(edgeRight, connectedRight);
            SetEdge(edgeBottom, connectedBottom);
            SetEdge(edgeLeft, IsConnected(-1, 0));

            if (enableLiquidMerge)
            {
                SetRightLiquidConnected(connectedRight);
                SetBottomLiquidConnected(connectedBottom);
            }
        }

        /// <summary>
        /// Immediately removes liquid bridges whose cards are no longer adjacent. This never
        /// creates a new connection; new bridges remain deferred until groups are rebuilt after
        /// the board has settled.
        /// </summary>
        public void HideBrokenLiquidBridges()
        {
            if (_model == null || !enableLiquidMerge) return;

            if (_rightLiquidConnected && !IsConnected(1, 0))
                SetRightLiquidConnected(false);
            if (_bottomLiquidConnected && !IsConnected(0, 1))
                SetBottomLiquidConnected(false);
        }

        /// <summary>Disables every liquid bridge on this card in the current frame.</summary>
        public void DisableAllLiquidBridgesImmediate()
        {
            ResetLiquidBridges();
        }

        /// <summary>Adds a sorting order offset to all edge renderers.</summary>
        public void AddSortingOrder(int order)
        {
            if (edgeTop) edgeTop.sortingOrder += order;
            if (edgeBottom) edgeBottom.sortingOrder += order;
            if (edgeLeft) edgeLeft.sortingOrder += order;
            if (edgeRight) edgeRight.sortingOrder += order;
        }

        #endregion

        #region Private

        /// <summary>
        /// Shows edge if it's an outer boundary (not connected to a same-stamp tile).
        /// Hides edge if it borders a same-stamp group member.
        /// </summary>
        private void SetEdge(SpriteRenderer edgeRenderer, bool isConnected)
        {
            if (edgeRenderer == null) return;

            bool shouldShow = !isConnected; // Perforations visible = NOT connected

            if (animateTransitions)
            {
                float targetAlpha = shouldShow ? 1f : 0f;
                if (!Mathf.Approximately(edgeRenderer.color.a, targetAlpha))
                {
                    edgeRenderer.DOFade(targetAlpha, transitionDuration);
                }
            }
            else
            {
                edgeRenderer.gameObject.SetActive(shouldShow);
            }
        }

        /// <summary>
        /// Checks if this edge connects to a tile in the same group.
        /// Connected = same topic group + adjacent board position.
        /// Edges are only hidden when tiles truly belong to the same group.
        /// </summary>
        private bool IsConnected(int dBoardCol, int dBoardRow)
        {
            if (_model.Group == null) return false;

            int targetBoardCol = _model.BoardCol + dBoardCol;
            int targetBoardRow = _model.BoardRow + dBoardRow;

            foreach (var member in _model.Group.Members)
            {
                if (member == _model) continue;
                if (member.BoardCol == targetBoardCol && member.BoardRow == targetBoardRow)
                    return true;
            }
            return false;
        }

        private void ConfigureLiquidBridges()
        {
            if (_rightLiquidBridge == null || _bottomLiquidBridge == null)
            {
                Debug.LogError(
                    "[CardEdgeRenderer] Assign the Right and Bottom liquid bridge renderers in the Card prefab.",
                    this);
                enableLiquidMerge = false;
                return;
            }

            _liquidProperties ??= new MaterialPropertyBlock();
            // ConfigureLiquidBridgeTransforms();
            ApplyLiquidProperties(_rightLiquidBridge, 0f, true);
            ApplyLiquidProperties(_bottomLiquidBridge, 0f, false);
        }


        private void SetRightLiquidConnected(bool connected)
        {
            if (_rightLiquidBridge == null || _rightLiquidConnected == connected) return;
            _rightLiquidConnected = connected;
            _rightLiquidTween?.Kill();

            if (!connected)
            {
                _rightLiquidProgress = 0f;
                ApplyLiquidProperties(_rightLiquidBridge, 0f, true);
                _rightLiquidBridge.enabled = false;
                _rightLiquidTween = null;
                return;
            }

            if (!_rightLiquidBridge.enabled)
                _rightLiquidProgress = 0f;

            ApplyLiquidProperties(_rightLiquidBridge, _rightLiquidProgress, true);
            _rightLiquidBridge.enabled = true;

            _rightLiquidTween = DOTween.To(
                    () => _rightLiquidProgress,
                    value =>
                    {
                        _rightLiquidProgress = value;
                        ApplyLiquidProperties(_rightLiquidBridge, value, true);
                    },
                    1f,
                    liquidMergeDuration)
                // The shader applies quintic easing to the visual phases.









                .SetEase(Ease.Linear)
                .OnComplete(() => _rightLiquidTween = null);
        }

        private void SetBottomLiquidConnected(bool connected)
        {
            if (_bottomLiquidBridge == null || _bottomLiquidConnected == connected) return;
            _bottomLiquidConnected = connected;
            _bottomLiquidTween?.Kill();

            if (!connected)
            {
                _bottomLiquidProgress = 0f;
                ApplyLiquidProperties(_bottomLiquidBridge, 0f, false);
                _bottomLiquidBridge.enabled = false;
                _bottomLiquidTween = null;
                return;
            }

            if (!_bottomLiquidBridge.enabled)
                _bottomLiquidProgress = 0f;

            ApplyLiquidProperties(_bottomLiquidBridge, _bottomLiquidProgress, false);
            _bottomLiquidBridge.enabled = true;

            _bottomLiquidTween = DOTween.To(
                    () => _bottomLiquidProgress,
                    value =>
                    {
                        _bottomLiquidProgress = value;
                        ApplyLiquidProperties(_bottomLiquidBridge, value, false);
                    },
                    1f,
                    liquidMergeDuration)
                // The shader applies quintic easing to the visual phases.









                .SetEase(Ease.Linear)
                .OnComplete(() => _bottomLiquidTween = null);
        }

        private void ApplyLiquidProperties(SpriteRenderer bridge, float progress, bool horizontal)
        {
            if (bridge == null || _model?.Topic == null) return;

            GameConfig config = GameManager.Instance.GameConfig;
            float length = horizontal
                ? config.cardWidth + config.cardGap
                : config.cardHeight + config.cardGap;
            float cardAlongBridge = horizontal ? config.cardWidth : config.cardHeight;
            float thickness = horizontal
                ? config.cardHeight * liquidThickness
                : config.cardWidth * liquidThickness;
            thickness = Mathf.Max(0.01f, thickness);

            float aspect = (length + cardAlongBridge * 0.1f) / thickness;
            float centerOffset = length / thickness;
            float radius = cardAlongBridge / thickness;
            float gapDistance = Mathf.Max(0f, centerOffset - radius);
            float maxSmooth = Mathf.Max(0.65f, gapDistance * 4.4f + 0.35f);

            bridge.GetPropertyBlock(_liquidProperties);
            _liquidProperties.SetColor(LiquidColorId, _currentLiquidColor);
            _liquidProperties.SetFloat(ProgressId, Mathf.Clamp01(progress));
            _liquidProperties.SetFloat(AspectId, aspect);
            _liquidProperties.SetFloat(CenterOffsetId, centerOffset);
            _liquidProperties.SetFloat(RadiusId, radius);
            _liquidProperties.SetFloat(MaxSmoothId, maxSmooth);
            _liquidProperties.SetFloat(WobbleId, liquidWobble);
            bridge.SetPropertyBlock(_liquidProperties);
        }

        private void ResetLiquidBridges()
        {
            _rightLiquidTween?.Kill();
            _bottomLiquidTween?.Kill();
            _rightLiquidProgress = 0f;
            _bottomLiquidProgress = 0f;
            _rightLiquidConnected = false;
            _bottomLiquidConnected = false;
            if (_rightLiquidBridge != null) _rightLiquidBridge.enabled = false;
            if (_bottomLiquidBridge != null) _bottomLiquidBridge.enabled = false;
        }

        private void OnDestroy()
        {
            _rightLiquidTween?.Kill();
            _bottomLiquidTween?.Kill();
        }

        #endregion
    }
}

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using StampJourney.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace StampJourney.Card
{
    /// <summary>
    /// View + Input handler for each card.
    /// Works with 2D Physics (SpriteRenderer + BoxCollider2D).
    /// When dragging a group: moves the permanent parent transform; scales/tilts during drag.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class CardView : MonoBehaviour
    {
        #region Inspector — Visuals

        [BoxGroup("Visuals")]
        [Required] public SpriteRenderer contentImg;
        [BoxGroup("Visuals")]
        public SpriteRenderer glowImg;
        [BoxGroup("Visuals")]
        public GameObject pressEffect;
        [BoxGroup("Visuals")]
        public SpriteRenderer backGroundImg;
        [BoxGroup("Visuals")]
        public CardEdgeRenderer cardEdgeRenderer;
        [BoxGroup("Visuals")]
        public SortingGroup sortingGroup;

        #endregion

        #region Inspector — Settings

        [BoxGroup("Settings")]
        public float liftScale = 1.05f;
        [BoxGroup("Settings")]
        public float liftDuration = 0.12f;
        [BoxGroup("Settings")]
        public float snapDuration = 0.18f;
        [BoxGroup("Settings")]
        public int baseSortingOrder = 10;
        [BoxGroup("Settings")]
        public int dragSortingOrder = 100;

        public int completeSortingOrder = 50;

        [BoxGroup("Link State Colors")]
        [LabelText("1 Item - Blue")]
        public Color oneItemColor = new Color32(77, 157, 224, 255);

        [BoxGroup("Link State Colors")]
        [LabelText("2 Items - Green")]
        public Color twoItemColor = new Color32(91, 203, 119, 255);

        [BoxGroup("Link State Colors")]
        [LabelText("3 Items - Orange")]
        public Color threeItemColor = new Color32(255, 159, 67, 255);

        [BoxGroup("Link State Colors")]
        [LabelText("4 Items - Purple")]
        public Color fourItemColor = new Color32(155, 93, 229, 255);

        [BoxGroup("Link State Colors")]
        [MinValue(0f)]
        public float linkColorTweenDuration = 0.24f;

        #endregion

        #region Inspector — Drag Effects

        [BoxGroup("Drag Effects")]
        [LabelText("Max Tilt Angle")]
        [Tooltip("Maximum tilt angle when dragging horizontally (degrees)")]
        public float maxTiltAngle = 32f;

        [BoxGroup("Drag Effects")]
        [LabelText("Tilt Smoothing")]
        [Tooltip("Lerp speed of tilt angle (higher = more responsive)")]
        public float tiltSmoothing = 20f;

        #endregion

        #region Constants

        private const float FlipDuration = 0.4f;
        private const float RippleScaleUp = 1.35f;
        private const float RippleScaleUpDuration = 0.18f;
        private const float RippleScaleDownDuration = 0.2f;

        #endregion

        #region Runtime State

        [ShowInInspector] private CardModel _model;
        private Gameboard _board;
        private bool _isDragging;
        private bool _isSnapping;
        private bool _releaseInProgress;
        private Camera _mainCamera;

        // Drag state
        private Vector2 _originPos;
        private Vector3 _dragOffset;
        private CardGroup _dragGroup;
        private Vector3 _prevDragPos;
        private float _currentTilt;
        private int _currentLinkedItemCount = -1;
        private Tween _backgroundColorTween;

        // Group drag state
        private Vector2 _groupDragOriginPos;

        #endregion

        #region Properties

        public CardModel Model => _model;

        #endregion

        #region Initialization

        public void Init(CardModel model, Gameboard board)
        {
            _backgroundColorTween?.Kill();
            _currentLinkedItemCount = -1;
            _model = model;
            _board = board;
            _mainCamera = Camera.main;

            _originPos = transform.position;
            SetSortingOrder(baseSortingOrder);
            RefreshVisual();

            if (cardEdgeRenderer != null)
                cardEdgeRenderer.Init(model, board);
        }

        public void RefreshVisual()
        {
            if (_model == null) return;
            if (_model.HasAssignedContent)
                SetLinkedItemCount(_model.Group?.Count ?? 1, false);
            else if (backGroundImg != null)
                backGroundImg.color = Color.white;
            contentImg.sprite = _model.HasAssignedContent
                ? _model.Topic.GetItemSprite(_model.ItemIndex)
                : null;
            contentImg.color = Color.white;
            pressEffect.SetActive(false);
            if (contentImg.sprite != null)
                FitSpriteContent(1, 1);
        }

        #endregion

        #region Link State Color

        /// <summary>
        /// Updates this card's background from the number of linked topic items.
        /// The bridge receives every tweened color so the connected shape stays unified.
        /// </summary>
        public void SetLinkedItemCount(int linkedItemCount, bool animate)
        {
            if (_model == null || !_model.HasAssignedContent || backGroundImg == null) return;

            int clampedCount = Mathf.Clamp(linkedItemCount, 1, 4);
            Color targetColor = GetLinkStateColor(clampedCount);
            if (_currentLinkedItemCount == clampedCount &&
                Approximately(backGroundImg.color, targetColor))
                return;

            _currentLinkedItemCount = clampedCount;
            _backgroundColorTween?.Kill();
            cardEdgeRenderer?.SetLiquidColor(backGroundImg.color);

            if (!animate || linkColorTweenDuration <= 0f)
            {
                backGroundImg.color = targetColor;
                cardEdgeRenderer?.SetLiquidColor(targetColor);
                return;
            }

            _backgroundColorTween = backGroundImg
                .DOColor(targetColor, linkColorTweenDuration)
                .SetEase(Ease.InOutSine)
                .OnUpdate(() => cardEdgeRenderer?.SetLiquidColor(backGroundImg.color))
                .OnComplete(() =>
                {
                    backGroundImg.color = targetColor;
                    cardEdgeRenderer?.SetLiquidColor(targetColor);
                    _backgroundColorTween = null;
                });
        }

        public Color GetLinkStateColor(int linkedItemCount)
        {
            return Mathf.Clamp(linkedItemCount, 1, 4) switch
            {
                2 => twoItemColor,
                3 => threeItemColor,
                4 => fourItemColor,
                _ => oneItemColor
            };
        }

        private static bool Approximately(Color a, Color b)
        {
            const float tolerance = 0.001f;
            return Mathf.Abs(a.r - b.r) < tolerance &&
                   Mathf.Abs(a.g - b.g) < tolerance &&
                   Mathf.Abs(a.b - b.b) < tolerance &&
                   Mathf.Abs(a.a - b.a) < tolerance;
        }

        #endregion

        #region Flip Animation

        public void PlayFlip(FlipState targetState, bool instantly = true)
        {
            if (_model == null) return;
            _model.FlipState = targetState;

            float targetY = targetState == FlipState.Up ? 0f : 180f;
            bool showContent = targetState == FlipState.Up;

            if (instantly)
            {
                transform.localEulerAngles = new Vector3(0f, targetY, 0f);
                if (contentImg != null) contentImg.gameObject.SetActive(showContent);
            }
            else
            {
                transform.DORotate(new Vector3(0f, targetY, 0f), FlipDuration).SetEase(Ease.InOutSine);
                DOVirtual.DelayedCall(FlipDuration / 2f, () =>
                {
                    if (contentImg != null) contentImg.gameObject.SetActive(showContent);
                });
            }
        }

        #endregion

        #region Sorting

        public void SetSortingOrder(int targetOrder)
        {
            if (sortingGroup == null)
            {
                AndyUtil.Logger.LogError("[CardView] sortingGroup is null", this);
                return;
            }
            sortingGroup.sortingOrder = targetOrder;
            cardEdgeRenderer?.SetLiquidBridgeSortingOrder(targetOrder);
        }

        #endregion

        #region Sprite Fitting

        public void FitSpriteContent(float maxWidth, float maxHeight)
        {
            Vector2 spriteSize = contentImg.sprite.bounds.size;
            float scaleX = maxWidth / spriteSize.x;
            float scaleY = maxHeight / spriteSize.y;
            var targetScale = Mathf.Min(scaleX, scaleY) * 0.9f;

            contentImg.transform.localScale = new Vector3(targetScale, targetScale, 1f);
        }

        #endregion

        #region Drag & Drop (Physics 2D)

        private void OnMouseDown()
        {
            if (_model == null || _model.IsAnimating || _isSnapping || !_model.CanDrag || _model.FlipState == FlipState.Down) return;
            if (_model.Group != null && _model.Group.IsTopicComplete) return;

            // A targeted booster gets first use of the card press. While a booster is executing,
            // the controller also consumes presses so drag logic cannot mutate the same board.
            if (_board != null && _board.Boosters != null &&
                _board.Boosters.HandleCardPressed(_model))
                return;

            if (_model.Group != null && _model.Group.HasCardAnimating) return;
            if (GameManager.Instance.State != GameState.Playing) return;

            // Block drag if this column has cards currently dropping
            if (_board != null && _board.IsColumnBusy(_model.BoardCol)) return;

            // Force-complete any running DOMove tweens to prevent position glitches
            // when quick-dragging before a previous swap animation finishes.
            ForceCompletePendingTweens();

            _isDragging = true;
            _dragGroup = _model.Group;
            _originPos = transform.position;
            _currentTilt = 0f;

            Vector3 mouseWorldPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0;
            _prevDragPos = mouseWorldPos;

            if (_dragGroup != null && _dragGroup.Count > 1 && _dragGroup.GroupTransform != null)
                BeginGroupDrag(mouseWorldPos);
            else
                BeginSingleDrag(mouseWorldPos);

            if (glowImg) glowImg.DOFade(1f, liftDuration);
            if (_dragGroup != null && _dragGroup.Count > 1)
                SetGroupPressEffects(true);
            else if (pressEffect)
                pressEffect.SetActive(true);
        }

        private void OnMouseDrag()
        {
            if (!_isDragging || _isSnapping) return;
            if (_model.Group != null && _model.Group.HasCardAnimating) return;
            Vector3 mouseWorldPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0;
            Vector3 newPosition = mouseWorldPos + _dragOffset;

            Transform dragT = (_dragGroup != null && _dragGroup.GroupTransform != null)
                ? _dragGroup.GroupTransform
                : transform;

            // Calculate tilt from horizontal velocity
            float velocityX = (newPosition.x - _prevDragPos.x) / Time.deltaTime;
            float targetTilt = Mathf.Clamp(-velocityX * 0.5f, -maxTiltAngle, maxTiltAngle);
            _currentTilt = Mathf.Lerp(_currentTilt, -targetTilt, Time.deltaTime * tiltSmoothing);

            dragT.position = newPosition;
            dragT.rotation = Quaternion.Euler(0f, 0f, _currentTilt * 2.5f);

            _prevDragPos = newPosition;
        }

        private void OnMouseUp()
        {
            ReleaseDragAsync().Forget();
        }

        /// <summary>
        /// Physics mouse messages can occasionally miss OnMouseUp when the dragged collider is
        /// released directly over another card. Polling the physical button state guarantees
        /// that every begun drag reaches the same release path exactly once.
        /// </summary>
        private void LateUpdate()
        {
            if (_isDragging && !_releaseInProgress && !Input.GetMouseButton(0))
                ReleaseDragAsync().Forget();
        }

        private async UniTask ReleaseDragAsync()
        {
            if (!_isDragging || _releaseInProgress) return;

            _releaseInProgress = true;
            _isDragging = false;

            if (glowImg) glowImg.DOFade(0f, snapDuration);
            if (_dragGroup != null && _dragGroup.Count > 1)
                SetGroupPressEffects(false);
            else if (pressEffect)
                pressEffect.SetActive(false);

            _currentTilt = 0f;

            try
            {
                if (_dragGroup != null && _dragGroup.Count > 1 && _dragGroup.GroupTransform != null)
                    await HandleGroupRelease();
                else
                    await HandleSingleRelease();
            }
            catch (System.Exception exception)
            {
                // A UI/event subscriber runs inside the awaited board settlement path. Its
                // failure must not leave this card permanently in a dragged/snapping state.
                Debug.LogException(exception, this);
            }
            finally
            {
                _isDragging = false;
                _isSnapping = false;
                _releaseInProgress = false;
                _currentTilt = 0f;
            }
        }

        #endregion

        #region Swap Logic

        private async UniTask DoSingleGridSwapAsync(Vector2Int gridDelta)
        {
            _isSnapping = true;
            try
            {
                await _board.TrySwapSingleGridAsync(_model, gridDelta.x, gridDelta.y);
            }
            finally
            {
                _isSnapping = false;
            }
        }

        private async UniTask DoGroupSwapAsync(Vector2Int gridDelta)
        {
            var group = _dragGroup;
            _isSnapping = true;
            _dragGroup = null;
            try
            {
                await _board.TrySwapGroupAsync(group, gridDelta.x, gridDelta.y);
            }
            finally
            {
                _isSnapping = false;
            }
        }

        private async UniTask SnapBackSingleAsync()
        {
            _isSnapping = true;
            Vector2 gridPos = _board.GetWorldPosition(_model.BoardCol, _model.BoardRow);
            var seq = DOTween.Sequence();
            seq.Append(transform.DOMove(gridPos, snapDuration).SetEase(Ease.Linear));
            await seq.AsyncWaitForCompletion();
            _dragGroup = null;
            _isSnapping = false;
        }

        /// <summary>Snaps the group back to its original position before the drag started.</summary>
        private async UniTask SnapBackGroupAsync()
        {
            if (_dragGroup == null) return;
            _isSnapping = true;

            if (_dragGroup.GroupTransform != null)
            {
                await _dragGroup.GroupTransform
                    .DOMove(_groupDragOriginPos, snapDuration)
                    .SetEase(Ease.OutCubic)
                    .AsyncWaitForCompletion();
            }

            _isSnapping = false;
            _dragGroup = null;
        }

        #endregion

        #region Effects

        public void PlayRippleEffect(float delay)
        {
            if (_model == null) return;

            var seq = DOTween.Sequence();
            seq.SetDelay(delay);
            seq.Append(transform.DOScale(Vector3.one * RippleScaleUp, RippleScaleUpDuration).SetEase(Ease.Linear));
            seq.Append(transform.DOScale(Vector3.one, RippleScaleDownDuration).SetEase(Ease.OutQuad))
                .OnComplete(() => transform.localScale = Vector3.one);
        }

        #endregion

        #region Private Helpers

        private void ForceCompletePendingTweens()
        {
            if (_model.Group != null)
            {
                foreach (var member in _model.Group.Members)
                {
                    var view = _board.cardFactory.GetView(member.TileId);
                    if (view != null) view.transform.DOComplete();
                }
            }
            else
            {
                transform.DOComplete();
            }
        }

        private void BeginGroupDrag(Vector3 mouseWorldPos)
        {
            _groupDragOriginPos = _dragGroup.GroupTransform.position;
            _dragGroup.GroupTransform.DOScale(liftScale, liftDuration).SetEase(Ease.OutBack);

            // The group parent is movement-only. Raise each card's own SortingGroup so bridge
            // renderers remain independent and cannot cover cards belonging to other groups.
            foreach (var member in _dragGroup.Members)
            {
                if (member == null) continue;
                var view = _board.cardFactory.GetView(member.TileId);
                if (view != null)
                    view.SetSortingOrder(dragSortingOrder);
            }

            _dragOffset = _dragGroup.GroupTransform.position - mouseWorldPos;
        }

        private void SetGroupPressEffects(bool active)
        {
            if (_dragGroup == null || _board == null || _board.cardFactory == null) return;

            foreach (var member in _dragGroup.Members)
            {
                if (member == null) continue;

                var view = _board.cardFactory.GetView(member.TileId);
                if (view != null && view.pressEffect != null)
                    view.pressEffect.SetActive(active);
            }
        }

        private void BeginSingleDrag(Vector3 mouseWorldPos)
        {
            _dragOffset = transform.position - mouseWorldPos;
            SetSortingOrder(dragSortingOrder);
            transform.DOScale(liftScale, liftDuration).SetEase(Ease.OutBack);
        }

        private async UniTask HandleGroupRelease()
        {
            var gridDelta = CalculateGroupGridDelta();
            var memberViews = new List<CardView>();
            if (_dragGroup != null)
            {
                foreach (var member in _dragGroup.Members)
                {
                    if (member == null) continue;
                    var view = _board.cardFactory.GetView(member.TileId);
                    if (view != null) memberViews.Add(view);
                }
            }

            _dragGroup?.GroupTransform.DOKill(true);

            // Reset scale immediately to prevent gaps between cards
            if (_dragGroup?.GroupTransform != null)
                _dragGroup.GroupTransform.localScale = Vector3.one;

            _dragGroup?.GroupTransform.DORotateQuaternion(Quaternion.identity, snapDuration).SetEase(Ease.OutBack);

            try
            {
                if (gridDelta.x != 0 || gridDelta.y != 0)
                    await DoGroupSwapAsync(gridDelta);
                else
                    await SnapBackGroupAsync();
            }
            finally
            {
                // RebuildGroups can replace the CardGroup and clear _dragGroup during the await,
                // so restore cached views even if settlement or a UI callback throws.
                foreach (var view in memberViews)
                {
                    SnapViewToCurrentGridImmediate(view);
                    if (view != null) view.SetSortingOrder(view.baseSortingOrder);
                }
                _dragGroup = null;
            }
        }

        private void OnDisable()
        {
            _backgroundColorTween?.Kill();
            _backgroundColorTween = null;
            _isDragging = false;
            _isSnapping = false;
            _releaseInProgress = false;
        }

        private async UniTask HandleSingleRelease()
        {
            transform.DOScale(1f, snapDuration);
            transform.DORotateQuaternion(Quaternion.identity, snapDuration).SetEase(Ease.OutBack);

            var gridDelta = CalculateSingleGridDelta();

            try
            {
                if (gridDelta.x != 0 || gridDelta.y != 0)
                    await DoSingleGridSwapAsync(gridDelta);
                else
                    await SnapBackSingleAsync();
            }
            finally
            {
                SnapViewToCurrentGridImmediate(this);
                SetSortingOrder(baseSortingOrder);
                _dragGroup = null;
            }
        }

        private void SnapViewToCurrentGridImmediate(CardView view)
        {
            if (view == null || view.Model == null || _board == null ||
                !view.gameObject.activeInHierarchy)
                return;

            view.transform.DOKill();
            view.transform.position = _board.GetWorldPosition(
                view.Model.BoardCol,
                view.Model.BoardRow);
            view.transform.localScale = Vector3.one;
            view.transform.rotation = Quaternion.identity;

            // Force-snapping kills any board DOMove before its OnComplete callback can run.
            // Clear this explicitly or IsColumnBusy will permanently lock the card's column.
            view.Model.IsAnimating = false;
        }

        /// <summary>Calculates the grid delta for a group based on parent transform offset.</summary>
        private Vector2Int CalculateGroupGridDelta()
        {
            if (_dragGroup == null || _dragGroup.GroupTransform == null) return Vector2Int.zero;

            var config = GameManager.Instance.GameConfig;
            Vector2 worldDelta = (Vector2)_dragGroup.GroupTransform.position - _groupDragOriginPos;
            float strideX = config.cardWidth + config.cardGap;
            float strideY = config.cardHeight + config.cardGap;

            int deltaCol = Mathf.RoundToInt(worldDelta.x / strideX);
            int deltaRow = -Mathf.RoundToInt(worldDelta.y / strideY);

            return new Vector2Int(deltaCol, deltaRow);
        }

        /// <summary>Calculates the grid delta for a single tile drag.</summary>
        private Vector2Int CalculateSingleGridDelta()
        {
            var config = GameManager.Instance.GameConfig;
            Vector2 worldDelta = (Vector2)transform.position - _originPos;
            float strideX = config.cardWidth + config.cardGap;
            float strideY = config.cardHeight + config.cardGap;

            int deltaCol = Mathf.RoundToInt(worldDelta.x / strideX);
            int deltaRow = -Mathf.RoundToInt(worldDelta.y / strideY);

            return new Vector2Int(deltaCol, deltaRow);
        }

        #endregion
    }
}

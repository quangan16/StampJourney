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

        private CardModel _model;
        private Gameboard _board;
        private bool _isDragging;
        private bool _isSnapping;
        private Camera _mainCamera;

        // Drag state
        private Vector2 _originPos;
        private Vector3 _dragOffset;
        private CardGroup _dragGroup;
        private Vector3 _prevDragPos;
        private float _currentTilt;

        // Group drag state
        private Vector2 _groupDragOriginPos;

        #endregion

        #region Properties

        public CardModel Model => _model;

        #endregion

        #region Initialization

        public void Init(CardModel model, Gameboard board)
        {
            _model = model;
            _board = board;
            _mainCamera = Camera.main;

            _originPos = transform.position;
            SetSortingOrder(baseSortingOrder);
            backGroundImg.color = Model.Stamp.stampColor;
            RefreshVisual();

            if (cardEdgeRenderer != null)
                cardEdgeRenderer.Init(model, board);
        }

        public void RefreshVisual()
        {
            if (_model == null) return;
            contentImg.sprite = _model.Stamp.GetPieceSprite(_model.PieceCol, _model.PieceRow);
            contentImg.color = Color.white;
            pressEffect.SetActive(false);
            FitSpriteContent(1, 1);
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
        }

        #endregion

        #region Sprite Fitting

        public void FitSpriteContent(float maxWidth, float maxHeight)
        {
            Vector2 spriteSize = contentImg.sprite.bounds.size;
            float scaleX = maxWidth / spriteSize.x;
            float scaleY = maxHeight / spriteSize.y;

            contentImg.transform.localScale = new Vector3(scaleX, scaleY * GameManager.Instance.GameConfig.cardHeight, 1f);
        }

        #endregion

        #region Drag & Drop (Physics 2D)

        private void OnMouseDown()
        {
            if (_model == null || _model.IsAnimating || _isSnapping || !_model.CanDrag) return;
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
            if (pressEffect) pressEffect.SetActive(true);
        }

        private void OnMouseDrag()
        {
            if (!_isDragging || _isSnapping) return;

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

        private async UniTaskVoid OnMouseUp()
        {
            if (!_isDragging || _isSnapping) return;
            _isDragging = false;

            if (glowImg) glowImg.DOFade(0f, snapDuration);
            if (pressEffect) pressEffect.SetActive(false);

            _currentTilt = 0f;

            if (_dragGroup != null && _dragGroup.Count > 1 && _dragGroup.GroupTransform != null)
                await HandleGroupRelease();
            else
                await HandleSingleRelease();
        }

        #endregion

        #region Swap Logic

        private async UniTask DoSingleGridSwapAsync(Vector2Int gridDelta)
        {
            _isSnapping = true;
            await _board.TrySwapSingleGridAsync(_model, gridDelta.x, gridDelta.y);
            _isSnapping = false;
        }

        private async UniTask DoGroupSwapAsync(Vector2Int gridDelta)
        {
            var group = _dragGroup;
            _isSnapping = true;
            _dragGroup = null;
            await _board.TrySwapGroupAsync(group, gridDelta.x, gridDelta.y);
            _isSnapping = false;
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

            var groupSorting = _dragGroup.GroupTransform.GetComponent<SortingGroup>();
            if (groupSorting != null)
            {
                groupSorting.sortingOrder = dragSortingOrder;
            }
            else
            {
                foreach (var member in _dragGroup.Members)
                {
                    var view = _board.cardFactory.GetView(member.TileId);
                    if (view != null)
                        view.SetSortingOrder(dragSortingOrder);
                }
            }

            _dragOffset = _dragGroup.GroupTransform.position - mouseWorldPos;
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
            _dragGroup?.GroupTransform.DOKill(true);

            // Reset scale immediately to prevent gaps between cards
            if (_dragGroup?.GroupTransform != null)
                _dragGroup.GroupTransform.localScale = Vector3.one;

            _dragGroup?.GroupTransform.DORotateQuaternion(Quaternion.identity, snapDuration).SetEase(Ease.OutBack);

            var groupSorting = _dragGroup?.GroupTransform.GetComponent<SortingGroup>();

            if (gridDelta.x != 0 || gridDelta.y != 0)
                await DoGroupSwapAsync(gridDelta);
            else
                await SnapBackGroupAsync();

            if (groupSorting != null)
            {
                groupSorting.sortingOrder = baseSortingOrder;
            }
            else if (_dragGroup != null && _dragGroup.Members.Count > 0)
            {
                foreach (var member in _dragGroup.Members)
                {
                    if (member == null) continue;
                    var view = _board.cardFactory.GetView(member.TileId);
                    if (view != null)
                        view.SetSortingOrder(baseSortingOrder);
                }
            }
        }

        private async UniTask HandleSingleRelease()
        {
            transform.DOScale(1f, snapDuration);
            transform.DORotateQuaternion(Quaternion.identity, snapDuration).SetEase(Ease.OutBack);

            var gridDelta = CalculateSingleGridDelta();

            if (gridDelta.x != 0 || gridDelta.y != 0)
                await DoSingleGridSwapAsync(gridDelta);
            else
                await SnapBackSingleAsync();

            SetSortingOrder(baseSortingOrder);
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

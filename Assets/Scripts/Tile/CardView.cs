using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace StampJourney.Tile
{
    /// <summary>
    /// View + Input handler cho mỗi tile.
    /// Hoạt động với 2D Physics (SpriteRenderer + BoxCollider2D).
    /// Khi drag group: tạo temporary parent tại center → scale/move parent → destroy khi thả.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class CardView : SerializedMonoBehaviour
    {
        // ---- Inspector ----
        [BoxGroup("Visuals")]
        [Required] public SpriteRenderer contentImg;
        [BoxGroup("Visuals")]
        public SpriteRenderer glowImg;
        [BoxGroup("Visual")]
        public SpriteRenderer backGroundImg;
        [BoxGroup("Visuals")]
        public CardEdgeRenderer cardEdgeRenderer;

        [BoxGroup("Visual")]
        public SortingGroup sortingGroup;

        [BoxGroup("Settings")]
        public float liftScale = 1.05f;
        public float liftDuration = 0.12f;
        public float snapDuration = 0.18f;
        public int baseSortingOrder = 10;
        public int dragSortingOrder = 100;

        [BoxGroup("Drag Effects")]
        [LabelText("Max Tilt Angle")]
        [Tooltip("Góc nghiêng tối đa khi kéo ngang (độ)")]
        public float maxTiltAngle = 32f;

        [BoxGroup("Drag Effects")]
        [LabelText("Tilt Smoothing")]
        [Tooltip("Tốc độ Lerp của góc nghiêng (càng lớn càng nhạy)")]
        public float tiltSmoothing = 20f;

        // ---- Runtime ----
        private CardModel _model;
        private Core.Gameboard _board;
        private bool _isDragging;
        private bool _isSnapping;
        private Camera _mainCamera;

        // ---- Drag State ----
        private Vector2 _originPos;
        private Vector3 _dragOffset;
        private CardGroup _dragGroup;
        private Vector3 _prevDragPos;     // Vị trí frame trước, dùng tính velocity
        private float _currentTilt;        // Góc nghiêng hiện tại

        // ---- Group Drag State ----
        private Vector2 _groupDragOriginPos;

        // ---- Init ----

        public CardModel Model => _model;

        public void Init(CardModel model, Core.Gameboard board)
        {
            _model = model;
            _board = board;
            _mainCamera = Camera.main;

            _originPos = transform.position;
            SetSortingOrder(baseSortingOrder);
            backGroundImg.color = Model.Stamp.stampColor;
            RefreshVisual();

            if (cardEdgeRenderer != null)
            {
                cardEdgeRenderer.Init(model, board);
            }
        }

        public void RefreshVisual()
        {
            if (_model == null) return;
            contentImg.sprite = _model.Stamp.GetPieceSprite(_model.PieceCol, _model.PieceRow);
            contentImg.color = Color.white;
            FitSpriteContent(1, 1);
        }

        public void SetSortingOrder(int targetOrder)
        {
            if (sortingGroup == null)
            {
                AndyUtil.Logger.LogError("[CardView] sortingGroup is null", this);
                return;
            }
            sortingGroup.sortingOrder = targetOrder;
        }

        public void FitSpriteContent(float maxWidth, float maxHeight)
        {
            Vector2 spriteSize = contentImg.sprite.bounds.size;
            float scaleX = maxWidth / spriteSize.x;
            float scaleY = maxHeight / spriteSize.y;

            contentImg.transform.localScale = new Vector3(scaleX, scaleY * GameManager.Instance.GameConfig.cardHeight, 1f);
        }

        // ========================================================
        #region Drag & Drop (Physics 2D)

        private void OnMouseDown()
        {
            if (_model == null || _model.IsAnimating || _isSnapping) return;
            if (Core.GameManager.Instance.State != Core.GameState.Playing) return;

            _isDragging = true;
            _dragGroup = _model.Group;
            _originPos = transform.position;
            _currentTilt = 0f;

            Vector3 mouseWorldPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0;
            _prevDragPos = mouseWorldPos;

            if (_dragGroup != null && _dragGroup.Count > 1 && _dragGroup.GroupTransform != null)
            {
                // ---- Group Drag (Permanent Parent) ----
                _groupDragOriginPos = _dragGroup.GroupTransform.position;

                // Nâng parent → tất cả children tự lift

                _dragGroup.GroupTransform.DOScale(liftScale, liftDuration).SetEase(Ease.OutBack);

                // Cập nhật sorting order cho tất cả members

                foreach (var member in _dragGroup.Members)
                {
                    var view = _board.cardFactory.GetView(member.TileId);
                    if (view != null)
                    {
                        view.SetSortingOrder(dragSortingOrder);
                    }
                }

                _dragOffset = _dragGroup.GroupTransform.position - mouseWorldPos;
            }
            else
            {
                // ---- Single tile drag ----
                _dragOffset = transform.position - mouseWorldPos;
                SetSortingOrder(dragSortingOrder);
                transform.DOScale(liftScale, liftDuration).SetEase(Ease.OutBack);
            }

            if (glowImg) glowImg.DOFade(1f, liftDuration);
        }

        private void OnMouseDrag()
        {
            if (!_isDragging || _isSnapping) return;

            Vector3 mouseWorldPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0;
            Vector3 newPosition = mouseWorldPos + _dragOffset;

            // Xác định drag transform
            Transform dragT = (_dragGroup != null && _dragGroup.GroupTransform != null) ? _dragGroup.GroupTransform : transform;

            // Tính velocity ngang để tilt
            float velocityX = (newPosition.x - _prevDragPos.x) / Time.deltaTime;
            float targetTilt = Mathf.Clamp(-velocityX * 0.5f, -maxTiltAngle, maxTiltAngle);
            _currentTilt = Mathf.Lerp(_currentTilt, -targetTilt, Time.deltaTime * tiltSmoothing);

            dragT.position = newPosition;
            dragT.rotation = Quaternion.Euler(0f, 0f, _currentTilt * 2.5f);

            _prevDragPos = newPosition;
        }

        private void OnMouseUp()
        {
            if (!_isDragging || _isSnapping) return;
            _isDragging = false;

            if (glowImg) glowImg.DOFade(0f, snapDuration);

            // Reset tilt về 0
            _currentTilt = 0f;

            if (_dragGroup != null && _dragGroup.Count > 1 && _dragGroup.GroupTransform != null)
            {
                // ---- Group: tính grid delta ----
                var gridDelta = CalculateGroupGridDelta();

                // Hạ parent scale
                _dragGroup?.GroupTransform.DOScale(1f, snapDuration);
                _dragGroup?.GroupTransform.DORotateQuaternion(Quaternion.identity, snapDuration).SetEase(Ease.OutBack);

                foreach (var member in _dragGroup.Members)
                {
                    var view = _board.cardFactory.GetView(member.TileId);
                    if (view != null)
                        view.SetSortingOrder(baseSortingOrder);
                }

                if (gridDelta.x != 0 || gridDelta.y != 0)
                {
                    DoGroupSwapAsync(gridDelta).Forget();
                }
                else
                {
                    SnapBackGroupAsync().Forget();
                }
            }
            else
            {
                // ---- Single tile ----
                transform.DOScale(1f, snapDuration);
                transform.DORotateQuaternion(Quaternion.identity, snapDuration).SetEase(Ease.OutBack);
                SetSortingOrder(baseSortingOrder);

                var target = FindTargetTile();
                if (target != null && target != this)
                {
                    DoSwapAsync(target).Forget();
                }
                else
                {
                    SnapBackSingleAsync().Forget();
                }
            }
        }

        // Không còn ReturnCardsFromTempParent()

        #endregion

        // ========================================================
        #region Swap Logic

        private async UniTaskVoid DoSwapAsync(CardView targetView)
        {
            _isSnapping = true;
            int colA = _model.BoardCol, rowA = _model.BoardRow;
            int colB = targetView._model.BoardCol, rowB = targetView._model.BoardRow;

            // Luôn lấy vị trí từ board grid — không dùng _originPos vì nó có thể bị stale
            Vector2 posA = _board.GetWorldPosition(colA, rowA);
            Vector2 posB = _board.GetWorldPosition(colB, rowB);

            transform.DOMove(posB, snapDuration).SetEase(Ease.Linear);
            targetView.transform.DOMove(posA, snapDuration).SetEase(Ease.Linear);

            await _board.TrySwapAsync(colA, rowA, colB, rowB);
            _isSnapping = false;
            _dragGroup = null;
        }

        private async UniTaskVoid DoGroupSwapAsync(Vector2Int gridDelta)
        {
            var group = _dragGroup;
            _isSnapping = true;
            _dragGroup = null;
            await _board.TrySwapGroupAsync(group, gridDelta.x, gridDelta.y);
            _isSnapping = false;
        }

        private async UniTaskVoid SnapBackSingleAsync()
        {
            _isSnapping = true;
            // Lấy đúng vị trí grid hiện tại từ board data
            Vector2 gridPos = _board.GetWorldPosition(_model.BoardCol, _model.BoardRow);
            var seq = DOTween.Sequence();
            // seq.Append(transform.DOShakePosition(0.2f, new Vector3(0.1f, 0.1f, 0f), 15));
            seq.Append(transform.DOMove(gridPos, snapDuration).SetEase(Ease.Linear));
            await seq.AsyncWaitForCompletion();
            _dragGroup = null;
            _isSnapping = false;
        }

        /// <summary>
        /// Snap back group về lại vị trí ban đầu.
        /// Do các card vẫn đang là child của GroupTransform, ta chỉ cần di chuyển parent.
        /// </summary>
        private async UniTaskVoid SnapBackGroupAsync()
        {
            if (_dragGroup == null) return;
            _isSnapping = true;

            if (_dragGroup.GroupTransform != null)
            {
                await _dragGroup.GroupTransform.DOMove(_groupDragOriginPos, snapDuration).SetEase(Ease.OutCubic).AsyncWaitForCompletion();
            }

            _isSnapping = false;
            _dragGroup = null;
        }

        #endregion

        // ========================================================
        #region Helpers

        /// <summary>
        /// Tính grid delta cho group dựa trên vị trí temp parent so với origin.
        /// </summary>
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

        /// <summary>
        /// Tính grid delta cho single tile.
        /// </summary>
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

        private CardView FindTargetTile()
        {
            Vector2 mousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, Vector2.zero);

            foreach (var hit in hits)
            {
                var view = hit.collider.GetComponentInParent<CardView>();
                if (view != null && view != this) return view;
            }
            return null;
        }

        public void PlayClearAnimation()
        {
            var seq = DOTween.Sequence();
            seq.Append(transform.DOScale(1.3f, 0.15f).SetEase(Ease.OutBack));
            seq.Append(transform.DOScale(0f, 0.2f).SetEase(Ease.InBack));
        }

        #endregion
    }
}

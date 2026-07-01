using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StampJourney.Tile
{
    /// <summary>
    /// View + Input handler cho mỗi tile.
    /// Implements Unity EventSystem drag interfaces — hoạt động với cả touch và mouse.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class CardView : SerializedMonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        // ---- Inspector ----
        [BoxGroup("Visuals")]
        [Required] public Image stampImage;
        [BoxGroup("Visuals")]
        [Required] public Image frameImage;       // Khung răng cưa overlay
        [BoxGroup("Visuals")]
        public Image glowImage;                   // Glow khi được chọn (optional)

        [BoxGroup("Settings")]
        public float liftScale = 1.15f;
        public float liftDuration = 0.12f;
        public float snapDuration = 0.18f;

        // ---- Runtime ----
        private CardModel _model;
        private Core.Gameboard _board;
        private Vector2 _originAnchoredPos; // Dùng anchoredPosition để tránh lỗi coordinate space
        private Canvas _canvas;
        private RectTransform _canvasRt;          // Canvas ROOT — dùng làm reference khi drag
        private RectTransform _rt;
        private bool _isDragging;

        // ---- Init ----

        public CardModel Model => _model;

        public void Init(CardModel model, Core.Gameboard board)
        {
            _model = model;
            _board = board;
            _canvas = GetComponentInParent<Canvas>();
            _rt = GetComponent<RectTransform>();

            // Lấy root canvas (quan trọng: phải là root, không phải nested canvas)
            var rootCanvas = _canvas;
            while (rootCanvas.transform.parent != null &&
                   rootCanvas.transform.parent.GetComponent<Canvas>() != null)
                rootCanvas = rootCanvas.transform.parent.GetComponent<Canvas>();
            _canvasRt = rootCanvas.GetComponent<RectTransform>();

            // Cache vị trí ban đầu ngay sau khi spawn
            _originAnchoredPos = _rt.anchoredPosition;

            RefreshVisual();
        }

        public void RefreshVisual()
        {
            if (_model == null) return;
            stampImage.sprite = _model.Stamp.GetPieceSprite(_model.PieceCol, _model.PieceRow);
            stampImage.color = Color.white;
        }

        // ========================================================
        #region Drag & Drop

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_model == null || _model.IsAnimating) return;
            if (Core.GameManager.Instance.State != Core.GameState.Playing) return;

            _isDragging = true;
            // Cache anchoredPosition tại thời điểm bắt đầu drag
            _originAnchoredPos = _rt.anchoredPosition;

            // Lift: scale up, bring to front
            transform.SetAsLastSibling();
            transform.DOScale(liftScale, liftDuration).SetEase(Ease.OutBack);

            if (glowImage) glowImage.DOFade(1f, liftDuration);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;

            // ✅ ĐÚNG: dùng _canvasRt (root Canvas) làm reference, không phải _rt (tile tự nó)
            // Tránh snap-to-center xảy ra khi tile di chuyển và làm thay đổi mặt phẳng chiếu
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                // Chuyển từ local canvas sang world position
                transform.position = _canvasRt.TransformPoint(localPoint);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;
            _isDragging = false;

            // Scale về bình thường
            transform.DOScale(1f, snapDuration);
            if (glowImage) glowImage.DOFade(0f, snapDuration);

            // Tìm tile đích dưới ngón tay
            var target = FindTargetTile(eventData);
            if (target != null && target != this)
            {
                // Thực hiện swap
                DoSwapAsync(target).Forget();
            }
            else
            {
                // Snap về vị trí cũ với hiệu ứng shake
                SnapBackAsync().Forget();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Tap nhẹ không drag → không làm gì (hoặc có thể dùng cho highlight)
        }

        #endregion

        // ========================================================
        #region Swap Logic

        private async UniTaskVoid DoSwapAsync(CardView targetView)
        {
            int colA = _model.BoardCol, rowA = _model.BoardRow;
            int colB = targetView._model.BoardCol, rowB = targetView._model.BoardRow;

            Vector2 posA = _originAnchoredPos;
            Vector2 posB = targetView._originAnchoredPos;

            // Animate bằng anchoredPosition — đúng trong không gian UI
            _rt.DOAnchorPos(posB, snapDuration).SetEase(Ease.OutCubic);
            targetView._rt.DOAnchorPos(posA, snapDuration).SetEase(Ease.OutCubic);

            // Cập nhật origin cho lần sau
            _originAnchoredPos = posB;
            targetView._originAnchoredPos = posA;

            // Swap trong data
            await _board.TrySwapAsync(colA, rowA, colB, rowB);
        }

        private async UniTaskVoid SnapBackAsync()
        {
            // Shake nhẹ rồi snap về anchoredPosition gốc
            var seq = DOTween.Sequence();
            seq.Append(transform.DOShakePosition(0.2f, new Vector3(8f, 8f, 0f), 15));
            seq.Append(_rt.DOAnchorPos(_originAnchoredPos, snapDuration).SetEase(Ease.OutBounce));
            await seq.AsyncWaitForCompletion();
        }

        #endregion

        // ========================================================
        #region Helpers

        private CardView FindTargetTile(PointerEventData eventData)
        {
            // Raycast UI để tìm tile ở vị trí thả
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            foreach (var result in results)
            {
                var view = result.gameObject.GetComponentInParent<CardView>();
                if (view != null && view != this) return view;
            }
            return null;
        }

        /// <summary>Animate bounce khi là phần của stamp vừa được clear.</summary>
        public void PlayClearAnimation()
        {
            var seq = DOTween.Sequence();
            seq.Append(transform.DOScale(1.3f, 0.15f).SetEase(Ease.OutBack));
            seq.Append(transform.DOScale(0f, 0.2f).SetEase(Ease.InBack));
        }

        #endregion
    }
}

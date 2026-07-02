using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using UnityEngine;

namespace StampJourney.Tile
{
    /// <summary>
    /// Render khung răng cưa (postage stamp perforations) cho tile.
    /// 
    /// APPROACH: 4 Image riêng biệt cho 4 cạnh — chỉ cần 1 sprite duy nhất.
    /// Mỗi cạnh được show/hide độc lập dựa vào tile lân cận.
    /// Ưu điểm so với 16-sprite:
    ///   - Chỉ cần 1 art asset thay vì 16
    ///   - Animate từng cạnh độc lập (fade in/out khi stamp ghép)
    ///   - Code rõ ràng, dễ debug
    /// </summary>
    public class CardEdgeRenderer : SerializedMonoBehaviour
    {
        // ---- Edge Images (4 cạnh riêng biệt) ----
        [BoxGroup("Edge Images")]
        [InfoBox("Mỗi SpriteRenderer là 1 dải perforation. Cùng dùng 1 sprite, chỉ xoay khác nhau.\n" +
                 "Top=0°, Right=90°, Bottom=180°, Left=270°")]
        [Required] public SpriteRenderer edgeTop;
        [BoxGroup("Edge Images")]
        [Required] public SpriteRenderer edgeRight;
        [BoxGroup("Edge Images")]
        [Required] public SpriteRenderer edgeBottom;
        [BoxGroup("Edge Images")]
        [Required] public SpriteRenderer edgeLeft;

        [BoxGroup("Settings")]
        [LabelText("Animate Edge Transitions")]
        [Tooltip("Fade in/out khi stamp ghép lại — tắt nếu cần performance tối đa")]
        public bool animateTransitions = true;

        [BoxGroup("Settings")]
        [ShowIf("animateTransitions")]
        public float transitionDuration = 0.15f;

        // ---- Runtime ----
        private CardModel _model;
        private Gameboard _board;

        // ---- Public API ----

        public void Init(CardModel model, Gameboard board)
        {
            _model = model;
            _board = board;
            UpdateEdges();
        }

        /// <summary>
        /// Cập nhật trạng thái 4 cạnh dựa trên tile lân cận.
        /// Gọi sau mỗi swap hoặc khi board thay đổi.
        /// </summary>
        public void UpdateEdges()
        {
            if (_model == null) return;

            SetEdge(edgeTop, IsConnected(0, -1, 0, -1));  // Tile phía trên
            SetEdge(edgeRight, IsConnected(1, 0, 1, 0));  // Tile bên phải
            SetEdge(edgeBottom, IsConnected(0, 1, 0, 1));  // Tile phía dưới
            SetEdge(edgeLeft, IsConnected(-1, 0, -1, 0));  // Tile bên trái
        }

        // ---- Internal ----

        /// <summary>
        /// Show edge nếu cạnh này là biên ngoài (không kết nối với tile cùng stamp).
        /// Hide edge nếu cạnh này chung với tile cùng stamp đúng thứ tự.
        /// </summary>
        private void SetEdge(SpriteRenderer edgeRenderer, bool isConnected)
        {
            if (edgeRenderer == null) return;

            bool shouldShow = !isConnected; // Có răng cưa = KHÔNG kết nối

            if (animateTransitions)
            {
                // Fade alpha thay vì SetActive để animation mượt hơn
                float targetAlpha = shouldShow ? 1f : 0f;
                if (!Mathf.Approximately(edgeRenderer.color.a, targetAlpha))
                {
#if DOTWEEN
                    edgeRenderer.DOFade(targetAlpha, transitionDuration);
#else
                    var c = edgeRenderer.color;
                    c.a = targetAlpha;
                    edgeRenderer.color = c;
#endif
                }
            }
            else
            {
                edgeRenderer.gameObject.SetActive(shouldShow);
            }
        }

        public void AddSortingOrder(int order)
        {
            if (edgeTop) edgeTop.sortingOrder += order;
            if (edgeBottom) edgeBottom.sortingOrder = order;
            if (edgeLeft) edgeLeft.sortingOrder = order;
            if (edgeRight) edgeRight.sortingOrder = order;
        }

        /// <summary>
        /// Kiểm tra cạnh này có kết nối với tile cùng group không.
        /// Connected = cùng group + đúng piece offset.
        /// Chỉ ẩn viền khi tile thực sự thuộc cùng group (đã "dính").
        /// </summary>
        private bool IsConnected(int dBoardCol, int dBoardRow, int dPieceCol, int dPieceRow)
        {
            if (_model.Group == null) return false;

            int targetPieceCol = _model.PieceCol + dPieceCol;
            int targetPieceRow = _model.PieceRow + dPieceRow;

            foreach (var member in _model.Group.Members)
            {
                if (member == _model) continue;
                if (member.PieceCol == targetPieceCol &&
                    member.PieceRow == targetPieceRow)
                    return true;
            }
            return false;
        }
    }
}

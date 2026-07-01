using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using UnityEngine;
using UnityEngine.UI;

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
    public class TileEdgeRenderer : SerializedMonoBehaviour
    {
        // ---- Edge Images (4 cạnh riêng biệt) ----
        [BoxGroup("Edge Images")]
        [InfoBox("Mỗi Image là 1 dải perforation. Cùng dùng 1 sprite, chỉ xoay khác nhau.\n" +
                 "Top=0°, Right=90°, Bottom=180°, Left=270°")]
        [Required] public Image edgeTop;
        [BoxGroup("Edge Images")]
        [Required] public Image edgeRight;
        [BoxGroup("Edge Images")]
        [Required] public Image edgeBottom;
        [BoxGroup("Edge Images")]
        [Required] public Image edgeLeft;

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
        private void SetEdge(Image edgeImage, bool isConnected)
        {
            if (edgeImage == null) return;

            bool shouldShow = !isConnected; // Có răng cưa = KHÔNG kết nối

            if (animateTransitions)
            {
                // Fade alpha thay vì SetActive để animation mượt hơn
                float targetAlpha = shouldShow ? 1f : 0f;
                if (!Mathf.Approximately(edgeImage.color.a, targetAlpha))
                {
                    // Dùng coroutine đơn giản hoặc DOTween nếu có
#if DOTWEEN
                    edgeImage.DOFade(targetAlpha, transitionDuration);
#else
                    var c = edgeImage.color;
                    c.a = targetAlpha;
                    edgeImage.color = c;
#endif
                }
            }
            else
            {
                edgeImage.gameObject.SetActive(shouldShow);
            }
        }

        /// <summary>
        /// Kiểm tra tile lân cận (offset board: dCol, dRow) có phải
        /// piece lân cận đúng (offset piece: dPieceCol, dPieceRow) của cùng stamp không.
        /// </summary>
        private bool IsConnected(int dBoardCol, int dBoardRow, int dPieceCol, int dPieceRow)
        {
            if (_board == null) return false;

            var neighbor = _board.GetTile(
                _model.BoardCol + dBoardCol,
                _model.BoardRow + dBoardRow
            );

            if (neighbor == null) return false;
            if (neighbor.Stamp.stampId != _model.Stamp.stampId) return false;
            if (neighbor.PieceCol != _model.PieceCol + dPieceCol) return false;
            if (neighbor.PieceRow != _model.PieceRow + dPieceRow) return false;

            return true;
        }
    }
}

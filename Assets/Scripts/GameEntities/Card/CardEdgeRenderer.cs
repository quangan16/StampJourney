using DG.Tweening;
using Sirenix.OdinInspector;
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

        #endregion

        #region Runtime

        private CardModel _model;
        private Gameboard _board;

        #endregion

        #region Public API

        public void Init(CardModel model, Gameboard board)
        {
            _model = model;
            _board = board;
            UpdateEdges();
        }

        /// <summary>
        /// Updates the visibility of all 4 edges based on neighboring tiles.
        /// Call after each swap or when the board changes.
        /// </summary>
        public void UpdateEdges()
        {
            if (_model == null) return;

            SetEdge(edgeTop, IsConnected(0, -1, 0, -1));
            SetEdge(edgeRight, IsConnected(1, 0, 1, 0));
            SetEdge(edgeBottom, IsConnected(0, 1, 0, 1));
            SetEdge(edgeLeft, IsConnected(-1, 0, -1, 0));
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
        /// Connected = same group + correct piece offset.
        /// Edges are only hidden when tiles truly belong to the same group.
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

        #endregion
    }
}

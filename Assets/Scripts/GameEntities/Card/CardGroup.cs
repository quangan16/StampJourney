using System.Collections.Generic;
using System.Linq;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Card
{
    /// <summary>
    /// A group of same-stamp tiles that have "stuck" together on the board.
    /// When dragging one member, the entire group moves.
    /// Edges between group members are hidden.
    /// </summary>
    public class CardGroup : MonoBehaviour
    {
        #region Static ID Counter

        private static int _nextGroupId;

        #endregion

        #region Identity

        public int GroupId { get; private set; }
        public StampData Stamp { get; private set; }

        /// <summary>The transform used as parent for group member views.</summary>
        public Transform GroupTransform => transform;

        #endregion

        #region Members

        private readonly List<CardModel> _members = new();
        public IReadOnlyList<CardModel> Members => _members;

        /// <summary>Number of members in this group.</summary>
        public int Count => _members.Count;

        #endregion

        #region Bounding Box (Computed)

        public int MinCol { get; private set; }
        public int MinRow { get; private set; }
        public int MaxCol { get; private set; }
        public int MaxRow { get; private set; }

        /// <summary>Width in board cells.</summary>
        public int Width => MaxCol - MinCol + 1;

        /// <summary>Height in board cells.</summary>
        public int Height => MaxRow - MinRow + 1;

        public bool HasCardAnimating => _members.Any(m => m != null && m.IsAnimating);

        #endregion

        #region Initialization

        public void Init(StampData stamp)
        {
            GroupId = _nextGroupId++;
            Stamp = stamp;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Checks if a candidate card can join this group.
        /// Requirements: same stampId, adjacent to at least one member, matching piece offset.
        /// </summary>
        public bool CanAccept(CardModel candidate)
        {
            if (candidate == null) return false;
            if (candidate.Stamp.stampId != Stamp.stampId) return false;
            if (candidate.Group == this) return false;

            foreach (var member in _members)
            {
                if (AreMatchingNeighbors(member, candidate))
                    return true;
            }
            return false;
        }

        /// <summary>Adds a card to this group. Sets card.Group = this.</summary>
        public void Add(CardModel tile)
        {
            if (tile == null || _members.Contains(tile)) return;

            // Remove from old group if any
            tile.Group?.Remove(tile);

            _members.Add(tile);
            tile.Group = this;
            RecalculateBounds();
        }

        /// <summary>Removes a card from this group. Sets card.Group = null.</summary>
        public void Remove(CardModel tile)
        {
            if (tile == null) return;
            if (_members.Remove(tile))
            {
                tile.Group = null;
                RecalculateBounds();
            }
        }

        /// <summary>Whether this group contains all pieces of its stamp.</summary>
        public bool IsStampComplete => _members.Count == Stamp.TotalPieces;

        /// <summary>Merges another group into this one. All members of other transfer to this.</summary>
        public void Absorb(CardGroup other)
        {
            if (other == null || other == this) return;

            // Copy list first since Add will modify other._members via Remove
            var otherMembers = other._members.ToList();
            foreach (var member in otherMembers)
            {
                Add(member);
            }
        }

        /// <summary>Disbands the group — all member.Group references are set to null.</summary>
        public void Disband()
        {
            foreach (var member in _members)
                member.Group = null;
            _members.Clear();
        }

        #endregion

        #region Neighbor Matching

        /// <summary>
        /// Checks if two cards are valid matching neighbors.
        /// Valid = same stampId + adjacent on board + piece offset matches board offset.
        /// </summary>
        public static bool AreMatchingNeighbors(CardModel a, CardModel b)
        {
            if (a == null || b == null) return false;
            if (a.Stamp.stampId != b.Stamp.stampId) return false;

            int dBoardCol = b.BoardCol - a.BoardCol;
            int dBoardRow = b.BoardRow - a.BoardRow;
            int dPieceCol = b.PieceCol - a.PieceCol;
            int dPieceRow = b.PieceRow - a.PieceRow;

            // Must be orthogonally adjacent on the board (no diagonals)
            if (Mathf.Abs(dBoardCol) + Mathf.Abs(dBoardRow) != 1) return false;

            // Board offset must match piece offset
            return dBoardCol == dPieceCol && dBoardRow == dPieceRow;
        }

        #endregion

        #region Bounds Calculation

        public void RecalculateBounds()
        {
            if (_members.Count == 0)
            {
                MinCol = MinRow = MaxCol = MaxRow = 0;
                return;
            }

            MinCol = int.MaxValue;
            MinRow = int.MaxValue;
            MaxCol = int.MinValue;
            MaxRow = int.MinValue;

            foreach (var m in _members)
            {
                if (m.BoardCol < MinCol) MinCol = m.BoardCol;
                if (m.BoardRow < MinRow) MinRow = m.BoardRow;
                if (m.BoardCol > MaxCol) MaxCol = m.BoardCol;
                if (m.BoardRow > MaxRow) MaxRow = m.BoardRow;
            }
        }

        #endregion

        public override string ToString() =>
            $"Group[{GroupId}] stamp={Stamp.stampName} members={_members.Count} bounds=({MinCol},{MinRow})-({MaxCol},{MaxRow})";
    }
}

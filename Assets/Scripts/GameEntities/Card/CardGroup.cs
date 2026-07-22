using System.Collections.Generic;
using System.Linq;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Card
{
    /// <summary>
    /// A group of adjacent items with the same topic ID that have stuck together.
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
        public StampData Topic { get; private set; }

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

        public void Init(StampData topic)
        {
            GroupId = _nextGroupId++;
            Topic = topic;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Checks if a candidate card can join this group.
        /// Requirements: same topic ID and adjacent to at least one member.
        /// </summary>
        public bool CanAccept(CardModel candidate)
        {
            if (candidate == null) return false;
            if (candidate.Topic.TopicId != Topic.TopicId) return false;
            if (candidate.Group == this) return false;
            return CanFormTopicSquareGroup(_members, candidate);
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

        /// <summary>
        /// True when the four authored items occupy every cell of a 2x2 board square.
        /// </summary>
        public bool IsTopicComplete =>
            Count == StampData.RequiredItemCount &&
            Width == 2 &&
            Height == 2 &&
            Topic.HasCompleteItemSet(_members.Select(member => member.ItemIndex));

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
        /// Valid = same topic ID + orthogonally adjacent on the board.
        /// </summary>
        public static bool AreMatchingNeighbors(CardModel a, CardModel b)
        {
            if (a == null || b == null) return false;
            if (a.IsIced || b.IsIced) return false;
            if (a.Topic.TopicId != b.Topic.TopicId) return false;

            int dBoardCol = b.BoardCol - a.BoardCol;
            int dBoardRow = b.BoardRow - a.BoardRow;
            // Must be orthogonally adjacent on the board (no diagonals)
            return Mathf.Abs(dBoardCol) + Mathf.Abs(dBoardRow) == 1;
        }

        /// <summary>
        /// Checks whether adding a card keeps a same-topic group capable of becoming a 2x2 square.
        /// Invalid lines and duplicate authored items stay separate so the player can rearrange them.
        /// </summary>
        public static bool CanFormTopicSquareGroup(IReadOnlyCollection<CardModel> members, CardModel candidate)
        {
            if (candidate == null || members == null || members.Count == 0) return false;
            if (candidate.IsIced || members.Any(member => member == null || member.IsIced)) return false;
            if (members.Count >= StampData.RequiredItemCount) return false;

            bool isAdjacent = false;
            int minCol = candidate.BoardCol;
            int maxCol = candidate.BoardCol;
            int minRow = candidate.BoardRow;
            int maxRow = candidate.BoardRow;

            foreach (CardModel member in members)
            {
                if (member == null || member.Topic.TopicId != candidate.Topic.TopicId) return false;
                if (member.ItemIndex == candidate.ItemIndex) return false;

                isAdjacent |= AreMatchingNeighbors(member, candidate);
                minCol = Mathf.Min(minCol, member.BoardCol);
                maxCol = Mathf.Max(maxCol, member.BoardCol);
                minRow = Mathf.Min(minRow, member.BoardRow);
                maxRow = Mathf.Max(maxRow, member.BoardRow);
            }

            return isAdjacent && maxCol - minCol < 2 && maxRow - minRow < 2;
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
            $"Group[{GroupId}] topic={Topic.TopicName} members={_members.Count} bounds=({MinCol},{MinRow})-({MaxCol},{MaxRow})";
    }
}

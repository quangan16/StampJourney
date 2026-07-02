using System.Collections.Generic;
using System.Linq;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Nhóm các tile cùng stamp đã "dính" lại với nhau trên board.
    /// Khi drag 1 member, cả group di chuyển.
    /// Viền giữa các member trong group bị ẩn.
    /// </summary>
    public class CardGroup
    {
        // ---- Static ID counter ----
        private static int _nextGroupId;

        // ---- Identity ----
        public readonly int GroupId;
        public readonly StampData Stamp;

        // ---- Members ----
        private readonly List<CardModel> _members = new();
        public IReadOnlyList<CardModel> Members => _members;

        // ---- Runtime parent object ----
        /// <summary>
        /// GameObject cha tạo ra khi group hình thành.
        /// Tâm = trung tâm bounding box, tất cả card views là children.
        /// </summary>
        public Transform GroupTransform { get; set; }

        // ---- Bounding box on board (computed) ----
        public int MinCol { get; private set; }
        public int MinRow { get; private set; }
        public int MaxCol { get; private set; }
        public int MaxRow { get; private set; }

        /// <summary>Width tính bằng số ô trên board.</summary>
        public int Width => MaxCol - MinCol + 1;

        /// <summary>Height tính bằng số ô trên board.</summary>
        public int Height => MaxRow - MinRow + 1;

        // ---- Constructor ----

        public CardGroup(StampData stamp)
        {
            GroupId = _nextGroupId++;
            Stamp = stamp;
        }

        // ---- Public API ----

        /// <summary>
        /// Kiểm tra candidate có thể thêm vào group không.
        /// Điều kiện: cùng stampId, nằm kề ít nhất 1 member, và pieceCol/pieceRow khớp offset tương đối.
        /// </summary>
        public bool CanAccept(CardModel candidate)
        {
            if (candidate == null) return false;
            if (candidate.Stamp.stampId != Stamp.stampId) return false;
            if (candidate.Group == this) return false; // Đã thuộc group này

            foreach (var member in _members)
            {
                if (AreMatchingNeighbors(member, candidate))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Thêm tile vào group. Gán tile.Group = this.
        /// </summary>
        public void Add(CardModel tile)
        {
            if (tile == null || _members.Contains(tile)) return;

            // Gỡ khỏi group cũ nếu có
            tile.Group?.Remove(tile);

            _members.Add(tile);
            tile.Group = this;
            RecalculateBounds();
        }

        /// <summary>
        /// Bỏ tile khỏi group. Gán tile.Group = null.
        /// </summary>
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
        /// Kiểm tra group đã đủ tất cả pieces của stamp chưa.
        /// </summary>
        public bool IsStampComplete => _members.Count == Stamp.TotalPieces;

        /// <summary>
        /// Merge group khác vào group này. Tất cả member của other chuyển sang this.
        /// </summary>
        public void Absorb(CardGroup other)
        {
            if (other == null || other == this) return;

            // Copy danh sách trước khi iterate vì Add sẽ modify other._members qua Remove
            var otherMembers = other._members.ToList();
            foreach (var member in otherMembers)
            {
                Add(member); // Add sẽ tự gọi other.Remove(member)
            }
        }

        /// <summary>
        /// Giải tán group — tất cả member.Group = null.
        /// </summary>
        public void Disband()
        {
            foreach (var member in _members)
                member.Group = null;
            _members.Clear();
        }

        /// <summary>Số lượng member.</summary>
        public int Count => _members.Count;

        // ---- Internal ----

        /// <summary>
        /// Kiểm tra 2 tile có phải neighbor hợp lệ không.
        /// Hợp lệ = cùng stampId + nằm kề trên board + pieceCol/pieceRow offset đúng.
        /// </summary>
        public static bool AreMatchingNeighbors(CardModel a, CardModel b)
        {
            if (a == null || b == null) return false;
            if (a.Stamp.stampId != b.Stamp.stampId) return false;

            int dBoardCol = b.BoardCol - a.BoardCol;
            int dBoardRow = b.BoardRow - a.BoardRow;
            int dPieceCol = b.PieceCol - a.PieceCol;
            int dPieceRow = b.PieceRow - a.PieceRow;

            // Phải kề nhau trên board (chỉ 4 hướng, không chéo)
            if (Mathf.Abs(dBoardCol) + Mathf.Abs(dBoardRow) != 1) return false;

            // Board offset phải khớp piece offset
            return dBoardCol == dPieceCol && dBoardRow == dPieceRow;
        }

        private void RecalculateBounds()
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

        public override string ToString() =>
            $"Group[{GroupId}] stamp={Stamp.stampName} members={_members.Count} bounds=({MinCol},{MinRow})-({MaxCol},{MaxRow})";
    }
}

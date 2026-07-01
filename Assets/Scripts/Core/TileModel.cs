using StampJourney.Data;

namespace StampJourney.Core
{
    /// <summary>
    /// Runtime model của một ô (tile) trên board.
    /// Immutable sau khi khởi tạo; thay đổi vị trí board thông qua BoardManager.
    /// </summary>
    public class CardModel
    {
        // ---- Stamp info ----
        /// <summary>Dữ liệu của tem mà tile này thuộc về.</summary>
        public readonly StampData Stamp;

        /// <summary>Vị trí cột của tile này trong stamp (0-based).</summary>
        public readonly int PieceCol;

        /// <summary>Vị trí hàng của tile này trong stamp (0-based).</summary>
        public readonly int PieceRow;

        // ---- Board position (mutable) ----
        /// <summary>Cột trên board (0-based).</summary>
        public int BoardCol { get; set; }

        /// <summary>Hàng trên board (0-based, 0 = top).</summary>
        public int BoardRow { get; set; }

        // ---- State ----
        /// <summary>True nếu tile đang trong animation và chưa thể tương tác.</summary>
        public bool IsAnimating { get; set; }

        // ---- Unique ID ----
        private static int _nextId;
        public readonly int TileId;

        public CardModel(StampData stamp, int pieceCol, int pieceRow, int boardCol, int boardRow)
        {
            Stamp = stamp;
            PieceCol = pieceCol;
            PieceRow = pieceRow;
            BoardCol = boardCol;
            BoardRow = boardRow;
            TileId = _nextId++;
        }

        public override string ToString() =>
            $"Tile[{TileId}] stamp={Stamp.stampName} piece=({PieceCol},{PieceRow}) board=({BoardCol},{BoardRow})";
    }
}

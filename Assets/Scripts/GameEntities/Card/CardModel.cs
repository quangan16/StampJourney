using StampJourney.Data;
using StampJourney.Gameplay;

namespace StampJourney.Card
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

        /// <summary>Vị trí cột của card này trong stamp (0-based).</summary>
        public readonly int PieceCol;

        /// <summary>Vị trí hàng của card này trong stamp (0-based).</summary>
        public readonly int PieceRow;

        // ---- Board position (managed by TileModel) ----
        /// <summary>Tile hiện tại chứa card này.</summary>
        public Tile CurrentTile { get; set; }

        /// <summary>Cột trên board (0-based).</summary>
        public int BoardCol => CurrentTile?.Col ?? -1;

        /// <summary>Hàng trên board (0-based, 0 = top).</summary>
        public int BoardRow => CurrentTile?.Row ?? -1;

        // ---- State ----
        /// <summary>True nếu tile đang trong animation và chưa thể tương tác.</summary>
        public bool IsAnimating { get; set; }

        public bool CanDrag { get; set; } = true;

        public FlipState FlipState;

        /// <summary>Group mà tile này thuộc về. Null nếu đứng riêng lẻ.</summary>
        public CardGroup Group
        { get; set; }

        // ---- Unique ID ----
        private static int _nextId;
        public readonly int TileId;

        public CardModel(StampData stamp, int pieceCol, int pieceRow)
        {
            Stamp = stamp;
            PieceCol = pieceCol;
            PieceRow = pieceRow;
            TileId = _nextId++;
            FlipState = FlipState.Down;
        }

        public override string ToString() =>
            $"Tile[{TileId}] stamp={Stamp.stampName} piece=({PieceCol},{PieceRow}) board=({BoardCol},{BoardRow})";
    }

    public enum FlipState
    {
        Down = 0,
        Up = 1,
    }
}

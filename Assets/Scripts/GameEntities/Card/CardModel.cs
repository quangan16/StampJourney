using StampJourney.Data;
using StampJourney.Gameplay;

namespace StampJourney.Card
{
    /// <summary>
    /// Runtime model for a card on the board.
    /// Topic and item identity are immutable after construction;
    /// board position changes are managed through the Gameboard.
    /// </summary>
    public class CardModel
    {
        #region Topic Identity (Immutable)

        /// <summary>The topic this item belongs to.</summary>
        public StampData Topic { get; private set; }

        /// <summary>Index of the complete item picture within its topic (0-based).</summary>
        public int ItemIndex { get; private set; }

        /// <summary>False only while an unauthored queue card is still face-down and undecided.</summary>
        public bool HasAssignedContent => Topic != null && ItemIndex >= 0;

        #endregion

        #region Board Position (Managed by Tile)

        /// <summary>The tile currently holding this card.</summary>
        public Tile CurrentTile { get; set; }

        /// <summary>Column on the board (0-based).</summary>
        public int BoardCol => CurrentTile?.Col ?? -1;

        /// <summary>Row on the board (0-based, 0 = top).</summary>
        public int BoardRow => CurrentTile?.Row ?? -1;

        #endregion

        #region State

        /// <summary>True if the card is mid-animation and cannot be interacted with.</summary>
        public bool IsAnimating { get; set; }

        public bool CanDrag { get; set; } = true;

        public FlipState FlipState;

        /// <summary>Group this card belongs to. Null if standalone.</summary>
        public CardGroup Group { get; set; }

        #endregion

        #region Unique ID

        private static int _nextId;
        public readonly int TileId;

        #endregion

        #region Constructor

        public CardModel(StampData topic, int itemIndex)
        {
            Topic = topic;
            ItemIndex = itemIndex;
            TileId = _nextId++;
            FlipState = FlipState.Down;
        }

        /// <summary>Assigns content to an undecided generated queue card immediately before drop.</summary>
        public void AssignContent(StampData topic, int itemIndex)
        {
            if (topic == null || !topic.IsValidItemIndex(itemIndex))
                throw new System.ArgumentException("Generated card content must reference a valid topic item.");

            Topic = topic;
            ItemIndex = itemIndex;
        }

        #endregion

        public override string ToString() =>
            HasAssignedContent
                ? $"Tile[{TileId}] topic={Topic.TopicName} item={ItemIndex} board=({BoardCol},{BoardRow})"
                : $"Tile[{TileId}] pending-content board=({BoardCol},{BoardRow})";
    }

    public enum FlipState
    {
        Down = 0,
        Up = 1,
    }
}

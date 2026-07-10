using Sirenix.OdinInspector;
using StampJourney.Card;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Represents a static background cell on the board.
    /// Holds a reference to the CardModel currently occupying it.
    /// </summary>
    public class Tile : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _backgroundRenderer;

        [field: SerializeField] public int Col { get; private set; }
        [field: SerializeField] public int Row { get; private set; }

        [ShowInInspector] public CardModel Card { get; set; }

        public bool IsOccupied => Card != null;

        public void Init(int col, int row, Gameboard gameboard)
        {
            Col = col;
            Row = row;
        }

        public void SetCard(CardModel card)
        {
            Card = card;
            if (card != null)
            {
                card.CurrentTile = this;
            }
        }
    }
}

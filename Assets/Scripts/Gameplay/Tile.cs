
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Hiển thị background cho mỗi ô tĩnh (Tile) trên board.
    /// </summary>
    public class Tile : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _backgroundRenderer;
        [field: SerializeField] public int Col { get; private set; }
        [field: SerializeField] public int Row { get; private set; }



        [ShowInInspector] public CardModel Card { get; set; }
        private Gameboard _gameboard;


        public bool IsOccupied => Card != null;

        public void Init(int col, int row, Gameboard gameboard)
        {
            Col = col;
            Row = row;
            _gameboard = gameboard;
        }




        public void SetCard(CardModel card)
        {
            Card = card;
            if (card != null)
            {
                card.CurrentTile = this;
            }
        }
        // Sau này có thể thêm các logic liên quan đến background (ví dụ highlight khi kéo thả)
    }

}

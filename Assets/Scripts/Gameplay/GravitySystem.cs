using System;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StampJourney.Card;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Gravity system: after tiles are cleared, tiles above drop down to fill gaps.
    /// Each tile drops independently; groups break if empty spaces are uneven.
    /// </summary>
    public class GravitySystem : SerializedMonoBehaviour
    {
        [BoxGroup("Settings")]
        [LabelText("Drop Duration per Cell (seconds)")]
        public float dropDurationPerCell = 0.14f;

        private Gameboard _gameboard;

        public void Init(Gameboard gameboard)
        {
            _gameboard = gameboard;
        }

        /// <summary>
        /// Applies gravity across the entire board.
        /// Every tile drops to the lowest empty cell in its column.
        /// </summary>
        public async UniTask ApplyGravityAsync()
        {
            if (_gameboard == null)
            {
                AndyUtil.Logger.LogError("Gameboard is null!");
                return;
            }

            // Most swaps happen on a full board. Avoid detaching/reparenting groups unless a
            // card really has an empty cell somewhere beneath it in the same column.
            if (!NeedsGravity()) return;

            // Groups are logical connections, but gravity moves every card independently.
            // Detach their views first so an old group parent or jelly tween cannot absorb or
            // distort the individual world-space drop animations.
            foreach (CardGroup group in _gameboard.stampDetector.AllGroups.Values)
                _gameboard.stampDetector.UnparentGroupCards(group);

            bool anyMoved = DropAllTiles();

            if (anyMoved)
            {
                _gameboard.HideBrokenLiquidBridges();
                float maxDelay = dropDurationPerCell * _gameboard.Rows;
                // await UniTask.Delay(TimeSpan.FromSeconds(maxDelay + 0.1f));
            }
        }

        private bool NeedsGravity()
        {
            for (int column = 0; column < _gameboard.Cols; column++)
            {
                bool foundEmptyBelow = false;
                for (int row = _gameboard.Rows - 1; row >= 0; row--)
                {
                    CardModel card = _gameboard.GetCard(column, row);
                    if (card == null)
                    {
                        foundEmptyBelow = true;
                    }
                    else if (foundEmptyBelow)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Drops all tiles independently (column by column).
        /// Returns true if any tile moved.
        /// </summary>
        private bool DropAllTiles()
        {
            bool anyMoved = false;

            for (int c = 0; c < _gameboard.Cols; c++)
            {
                int writeRow = _gameboard.Rows - 1;

                for (int readRow = _gameboard.Rows - 1; readRow >= 0; readRow--)
                {
                    var tile = _gameboard.GetCard(c, readRow);
                    if (tile == null) continue;

                    if (writeRow != readRow)
                    {
                        _gameboard.SetTile(c, writeRow, tile);
                        _gameboard.SetTile(c, readRow, null);

                        int distance = writeRow - readRow;
                        float duration = dropDurationPerCell * distance;
                        Vector2 targetPos = _gameboard.GetWorldPosition(c, writeRow);
                        _gameboard.cardFactory.AnimateTileDrop(tile, targetPos, duration);

                        anyMoved = true;
                    }

                    writeRow--;
                }
            }

            return anyMoved;
        }
    }
}

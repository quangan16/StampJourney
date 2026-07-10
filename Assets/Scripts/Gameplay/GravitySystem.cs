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
        public float dropDurationPerCell = 0.08f;

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

            bool anyMoved = DropAllTiles();

            if (anyMoved)
            {
                float maxDelay = dropDurationPerCell * _gameboard.Rows;
                await UniTask.Delay(TimeSpan.FromSeconds(maxDelay + 0.1f));
            }
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

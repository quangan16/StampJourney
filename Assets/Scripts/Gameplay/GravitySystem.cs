using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StampJourney.Card;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Hệ thống trọng lực: sau khi xóa tile, các tile phía trên rơi xuống.
    /// Ở phiên bản này, mọi tile rơi xuống độc lập, các group sẽ bị vỡ nếu không có chỗ trống đồng đều.
    /// </summary>
    public class GravitySystem : SerializedMonoBehaviour
    {
        private Gameboard gameboard;

        [BoxGroup("Settings")]
        [LabelText("Drop Duration per Cell (seconds)")]
        public float dropDurationPerCell = 0.08f;

        public void Init(Gameboard gameboard)
        {
            this.gameboard = gameboard;
        }

        /// <summary>
        /// Áp dụng trọng lực toàn board.
        /// Mọi tile sẽ rơi xuống ô trống bên dưới cùng trong cột của nó.
        /// </summary>
        public async UniTask ApplyGravityAsync()
        {
            if (gameboard == null)
            {
                AndyUtil.Logger.LogError("Gameboard is null!");
                return;
            }

            bool anyMoved = DropAllTiles();

            if (anyMoved)
            {
                // Chờ animation xong
                float maxDelay = dropDurationPerCell * gameboard.Rows;
                await UniTask.Delay(TimeSpan.FromSeconds(maxDelay + 0.1f));
            }
        }

        /// <summary>
        /// Rơi mọi tile độc lập (column-by-column).
        /// Trả về true nếu có tile nào di chuyển.
        /// </summary>
        private bool DropAllTiles()
        {
            bool anyMoved = false;

            for (int c = 0; c < gameboard.Cols; c++)
            {
                int writeRow = gameboard.Rows - 1;

                for (int readRow = gameboard.Rows - 1; readRow >= 0; readRow--)
                {
                    var tile = gameboard.GetCard(c, readRow);
                    if (tile == null) continue;

                    if (writeRow != readRow)
                    {
                        // Cập nhật model
                        gameboard.SetTile(c, writeRow, tile);
                        gameboard.SetTile(c, readRow, null);

                        // Tính thời gian và tạo animation
                        int distance = writeRow - readRow;
                        float duration = dropDurationPerCell * distance;
                        Vector2 targetPos = gameboard.GetWorldPosition(c, writeRow);
                        gameboard.cardFactory.AnimateTileDrop(tile, targetPos, duration);

                        anyMoved = true;
                    }

                    writeRow--;
                }
            }

            return anyMoved;
        }
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Hệ thống trọng lực: sau khi xóa tile, các tile phía trên rơi xuống.
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
        /// Mỗi cột xử lý độc lập: tile rơi xuống lấp chỗ trống.
        /// </summary>
        public async UniTask ApplyGravityAsync()
        {
            if (gameboard == null)
            {
                AndyUtil.Logger.LogError("Gameboard is null!");
                return;
            }
            var moveTasks = new List<UniTask>();

            for (int c = 0; c < gameboard.Cols; c++)
                moveTasks.Add(ProcessColumnAsync(c));

            await UniTask.WhenAll(moveTasks);
        }

        private async UniTask ProcessColumnAsync(int col)
        {
            if (gameboard == null)
            {
                AndyUtil.Logger.LogError("Gameboard is null!");
                return;
            }
            // Đi từ dưới lên, tìm ô trống và kéo tile từ trên xuống
            int writeRow = gameboard.Rows - 1;

            for (int readRow = gameboard.Rows - 1; readRow >= 0; readRow--)
            {
                var tile = gameboard.GetTile(col, readRow);
                if (tile == null) continue;

                if (writeRow != readRow)
                {
                    // Di chuyển tile xuống
                    gameboard.SetTile(col, writeRow, tile);
                    gameboard.SetTile(col, readRow, null);

                    int distance = writeRow - readRow;
                    float duration = dropDurationPerCell * distance;
                    Vector2 targetPos = gameboard.GetWorldPosition(col, writeRow);

                    // Thông báo View animate
                    gameboard.cardFactory.AnimateTileDrop(tile, targetPos, duration);
                }
                writeRow--;
            }

            // Chờ animation xong (ước lượng)
            float maxDelay = dropDurationPerCell * gameboard.Rows;
            await UniTask.Delay(TimeSpan.FromSeconds(maxDelay + 0.05f));
        }
    }
}

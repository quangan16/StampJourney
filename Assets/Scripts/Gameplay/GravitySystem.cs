using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Hệ thống trọng lực: sau khi xóa tile, các tile phía trên rơi xuống.
    /// Hỗ trợ cả tile riêng lẻ và group (group rơi nguyên khối).
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
        /// Bước 1: Rơi các group (nguyên khối).
        /// Bước 2: Rơi các tile riêng lẻ (không thuộc group).
        /// </summary>
        public async UniTask ApplyGravityAsync()
        {
            if (gameboard == null)
            {
                AndyUtil.Logger.LogError("Gameboard is null!");
                return;
            }

            // Lặp cho đến khi không còn tile/group nào có thể rơi
            bool anyMoved;
            do
            {
                anyMoved = false;

                // Bước 1: Rơi groups nguyên khối
                anyMoved |= DropGroups();

                // Bước 2: Rơi tile riêng lẻ (column-by-column)
                anyMoved |= DropSingleTiles();

            } while (anyMoved);

            // Chờ animation xong
            float maxDelay = dropDurationPerCell * gameboard.Rows;
            await UniTask.Delay(TimeSpan.FromSeconds(maxDelay + 0.1f));
        }

        // ---- Group Gravity ----

        /// <summary>
        /// Rơi tất cả groups xuống tối đa. Trả về true nếu có group nào rơi.
        /// </summary>
        private bool DropGroups()
        {
            bool anyMoved = false;
            var processedGroups = new HashSet<int>();

            // Duyệt từ dưới lên để xử lý groups thấp trước
            for (int r = gameboard.Rows - 1; r >= 0; r--)
            {
                for (int c = 0; c < gameboard.Cols; c++)
                {
                    var tile = gameboard.GetTile(c, r);
                    if (tile == null || tile.Group == null) continue;

                    var group = tile.Group;
                    if (processedGroups.Contains(group.GroupId)) continue;
                    processedGroups.Add(group.GroupId);

                    int dropDistance = CalculateGroupDropDistance(group);
                    if (dropDistance > 0)
                    {
                        MoveGroupDown(group, dropDistance);
                        anyMoved = true;
                    }
                }
            }

            return anyMoved;
        }

        /// <summary>
        /// Tính khoảng cách tối đa group có thể rơi.
        /// Group chỉ rơi khi TẤT CẢ cột mà nó chiếm đều có đủ chỗ trống bên dưới.
        /// </summary>
        private int CalculateGroupDropDistance(CardGroup group)
        {
            // Tìm hàng thấp nhất cho mỗi cột mà group chiếm
            var colBottomRow = new Dictionary<int, int>();
            foreach (var member in group.Members)
            {
                if (!colBottomRow.ContainsKey(member.BoardCol) ||
                    member.BoardRow > colBottomRow[member.BoardCol])
                {
                    colBottomRow[member.BoardCol] = member.BoardRow;
                }
            }

            // Cho mỗi cột, đếm số ô trống liên tiếp bên dưới bottom member
            int minDrop = int.MaxValue;
            var memberPositions = new HashSet<(int col, int row)>(
                group.Members.Select(m => (m.BoardCol, m.BoardRow)));

            foreach (var (col, bottomRow) in colBottomRow)
            {
                int freeBelow = 0;
                for (int r = bottomRow + 1; r < gameboard.Rows; r++)
                {
                    var tileAtPos = gameboard.GetTile(col, r);
                    // Ô trống hoặc ô thuộc chính group này (sẽ di chuyển cùng)
                    if (tileAtPos == null)
                        freeBelow++;
                    else if (memberPositions.Contains((col, r)))
                        continue; // Member khác của cùng group → skip
                    else
                        break; // Có tile khác chặn → dừng
                }
                minDrop = Mathf.Min(minDrop, freeBelow);
            }

            return minDrop == int.MaxValue ? 0 : minDrop;
        }

        /// <summary>
        /// Di chuyển group xuống distance hàng.
        /// </summary>
        private void MoveGroupDown(CardGroup group, int distance)
        {
            // Sắp xếp members từ dưới lên để tránh ghi đè
            var sortedMembers = group.Members
                .OrderByDescending(m => m.BoardRow)
                .ToList();

            foreach (var member in sortedMembers)
            {
                int oldCol = member.BoardCol;
                int oldRow = member.BoardRow;
                int newRow = oldRow + distance;

                gameboard.SetTile(oldCol, oldRow, null);
                gameboard.SetTile(oldCol, newRow, member);

                // Animate view
                float duration = dropDurationPerCell * distance;
                Vector2 targetPos = gameboard.GetWorldPosition(oldCol, newRow);
                gameboard.cardFactory.AnimateTileDrop(member, targetPos, duration);
            }
        }

        // ---- Single Tile Gravity ----

        /// <summary>
        /// Rơi tile riêng lẻ (không thuộc group). Trả về true nếu có tile nào rơi.
        /// </summary>
        private bool DropSingleTiles()
        {
            bool anyMoved = false;

            for (int c = 0; c < gameboard.Cols; c++)
            {
                if (ProcessColumn(c))
                    anyMoved = true;
            }

            return anyMoved;
        }

        /// <summary>
        /// Xử lý gravity cho 1 cột — chỉ di chuyển tile RIÊNG LẺ (không thuộc group).
        /// Tile thuộc group giữ nguyên vị trí (đã xử lý ở DropGroups).
        /// </summary>
        private bool ProcessColumn(int col)
        {
            bool anyMoved = false;
            int writeRow = gameboard.Rows - 1;

            for (int readRow = gameboard.Rows - 1; readRow >= 0; readRow--)
            {
                var tile = gameboard.GetTile(col, readRow);
                if (tile == null) continue;

                // Skip tiles thuộc group — chúng đã được xử lý trong DropGroups
                if (tile.Group != null)
                {
                    // Group tile chiếm vị trí → writeRow phải bỏ qua vị trí này
                    if (readRow == writeRow)
                        writeRow--;
                    continue;
                }

                // Tìm vị trí writeRow tiếp theo mà không bị chiếm bởi group tile
                while (writeRow >= 0)
                {
                    var tileAtWrite = gameboard.GetTile(col, writeRow);
                    if (tileAtWrite == null) break; // Ô trống — có thể đặt vào
                    if (tileAtWrite.Group != null)
                    {
                        writeRow--; // Bị group chiếm → skip
                        continue;
                    }
                    break;
                }

                if (writeRow < 0) break;

                if (writeRow != readRow)
                {
                    gameboard.SetTile(col, writeRow, tile);
                    gameboard.SetTile(col, readRow, null);

                    int distance = writeRow - readRow;
                    float duration = dropDurationPerCell * distance;
                    Vector2 targetPos = gameboard.GetWorldPosition(col, writeRow);
                    gameboard.cardFactory.AnimateTileDrop(tile, targetPos, duration);

                    anyMoved = true;
                }

                writeRow--;
            }

            return anyMoved;
        }
    }
}

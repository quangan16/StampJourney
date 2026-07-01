using System;
using System.Collections.Generic;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Thuật toán phát hiện các tem hoàn chỉnh trên board.
    /// Một tem hoàn chỉnh = n×m ô liền kề, mỗi ô thuộc đúng stamp và đúng piece position.
    /// </summary>
    public class StampDetector : MonoBehaviour
    {
        Gameboard gameboard;
        public void Init(Gameboard gameboard)
        {
            this.gameboard = gameboard;
        }
        /// <summary>
        /// Quét toàn bộ grid, trả về danh sách các nhóm tile tạo thành tem hoàn chỉnh.
        /// Mỗi phần tử trong kết quả là danh sách tiles của 1 tem hoàn chỉnh.
        /// </summary>
        public List<List<CardModel>> FindCompletedStamps(CardModel[,] grid, int boardCols, int boardRows)
        {
            var results = new List<List<CardModel>>();
            // Theo dõi các ô đã nằm trong match để không đếm trùng
            bool[,] used = new bool[boardCols, boardRows];

            for (int r = 0; r < boardRows; r++)
                for (int c = 0; c < boardCols; c++)
                {
                    if (used[c, r]) continue;

                    var tile = grid[c, r];
                    if (tile == null) continue;

                    // Chỉ xét tile là góc trên-trái của stamp (pieceCol==0, pieceRow==0)
                    if (tile.PieceCol != 0 || tile.PieceRow != 0) continue;

                    var stamp = tile.Stamp;
                    var match = TryMatchStamp(grid, boardCols, boardRows, c, r, stamp, used);
                    if (match != null)
                    {
                        results.Add(match);
                        // Đánh dấu used
                        foreach (var t in match)
                            used[t.BoardCol, t.BoardRow] = true;
                    }
                }

            return results;
        }

        /// <summary>
        /// Thử khớp stamp bắt đầu từ góc trên-trái (anchorCol, anchorRow).
        /// Trả về danh sách tiles nếu khớp, null nếu không.
        /// </summary>
        private List<CardModel> TryMatchStamp(
            CardModel[,] grid, int boardCols, int boardRows,
            int anchorCol, int anchorRow, StampData stamp, bool[,] used)
        {
            int sc = stamp.cols;
            int sr = stamp.rows;

            // Kiểm tra bounds
            if (anchorCol + sc > boardCols) return null;
            if (anchorRow + sr > boardRows) return null;

            var group = new List<CardModel>(sc * sr);

            for (int pr = 0; pr < sr; pr++)
                for (int pc = 0; pc < sc; pc++)
                {
                    int bc = anchorCol + pc;
                    int br = anchorRow + pr;

                    if (used[bc, br]) return null;

                    var t = grid[bc, br];
                    if (t == null) return null;
                    if (t.Stamp.stampId != stamp.stampId) return null;
                    if (t.PieceCol != pc) return null;
                    if (t.PieceRow != pr) return null;

                    group.Add(t);
                }

            return group;
        }
    }
}

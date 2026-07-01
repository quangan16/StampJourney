using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Data
{
    public class LevelData
    {
        public int levelIndex;
        public string levelTitle = "Level 1";
        [Range(3, 8)]
        public int boardCols = 5;

        [Range(4, 10)]
        public int boardRows = 7;

        [MinValue(0)]
        public int maxMoves = 20;

        public StampData[] stamps;
        [Range(1, 8)]
        public int maxStampTypesOnBoard = 3;
        public FillStrategy fillStrategy = FillStrategy.RandomFromAvailable;

        public bool IsValid =>
            stamps != null &&
            stamps.Length > 0 &&
            boardCols > 0 &&
            boardRows > 0;

        public int TotalCells => boardCols * boardRows;
    }

    public enum FillStrategy
    {
        RandomFromAvailable,    // Random stamp, random piece
        BalancedDistribution,   // Đảm bảo mỗi loại stamp xuất hiện đủ
    }
}

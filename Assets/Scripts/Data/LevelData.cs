using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Data
{
    public class LevelData
    {

        public LevelConfig levelConfig;

        public bool IsValid =>
            levelConfig.stamps != null &&
            levelConfig.stamps.Length > 0 &&
            levelConfig.boardCols > 0 &&
            levelConfig.boardRows > 0;

        public int TotalCells => levelConfig.boardCols * levelConfig.boardRows;
    }

    public enum FillStrategy
    {
        RandomFromAvailable,    // Random stamp, random piece
        BalancedDistribution,   // Đảm bảo mỗi loại stamp xuất hiện đủ
    }

    public class LevelConfig
    {
        public int levelID;


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
    }
}

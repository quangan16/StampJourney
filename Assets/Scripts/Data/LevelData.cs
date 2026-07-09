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

        [MinValue(-1)]
        public int maxMoves = -1;

        [MinValue(-1)]
        [InfoBox("Set to 0 to disable the timer.")]
        public float timeLimitSeconds = -1f;

        /// <summary>Whether this level has a time restriction.</summary>
        public bool HasTimeLimit => timeLimitSeconds > 0f;

        public bool HasMoveLimit => maxMoves > 0;

        public StampData[] stamps;
        [Range(1, 8)]
        public int maxStampTypesOnBoard = 3;
        public FillStrategy fillStrategy = FillStrategy.RandomFromAvailable;
    }
}

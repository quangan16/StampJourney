using System.Linq;
using Sirenix.OdinInspector;
using StampJourney.Data;
using UnityEngine;

/// <summary>
/// Serialized configuration for a level: board dimensions, constraints, and stamp pool.
/// </summary>
[CreateAssetMenu(fileName = "Level_", menuName = "Stamp Journey/LevelConfig")]
public class LevelData : ScriptableObject
{
    public int levelID;

    [Range(2, 8)]
    public int boardCols = 5;

    [Range(2, 10)]
    public int boardRows = 7;

    [MinValue(-1)]
    public int maxMoves = -1;

    [MinValue(-1)]
    [InfoBox("Set to 0 or less to disable the timer.")]
    public float timeLimitSeconds = -1f;

    /// <summary>Whether this level has a time restriction.</summary>
    public bool HasTimeLimit => timeLimitSeconds > 0f;

    /// <summary>Whether this level has a move limit.</summary>
    public bool HasMoveLimit => maxMoves > 0;

    public StampData[] stamps;

    [Range(1, 8)]
    public int maxStampTypesOnBoard = 3;

    public FillStrategy fillStrategy = FillStrategy.RandomFromAvailable;

    [Header("Authored Layout")]
    [Tooltip("When enabled, the board and the above-board queues are loaded exactly as arranged in the Level Designer window.")]
    public bool useAuthoredLayout;

    [Tooltip("One entry for each occupied board cell. Rows are top-down, columns are left-right.")]
    public System.Collections.Generic.List<CardPlacement> boardLayout = new();

    [Tooltip("Cards waiting above their column. Order 0 drops first.")]
    public System.Collections.Generic.List<QueueCardPlacement> queueLayout = new();

    [MinValue(0)]
    [Tooltip("Number of queue rows shown by the authored level editor, including empty rows.")]
    public int authoredQueueRows = 1;

    public bool TryGetBoardCard(int col, int row, out CardPlacement placement)
    {
        placement = boardLayout?.Find(card => card != null && card.column == col && card.row == row);
        return placement != null && placement.IsValid;
    }

    public System.Collections.Generic.IEnumerable<QueueCardPlacement> GetQueueCards(int column)
    {
        if (queueLayout == null) yield break;
        foreach (var card in queueLayout.Where(card => card != null).OrderBy(card => card.order))
            if (card != null && card.column == column && card.IsValid)
                yield return card;
    }

    public bool IsValid =>
          stamps != null &&
          stamps.Length > 0 &&
          boardCols > 0 &&
          boardRows > 0;

    /// <summary>Total number of cells on the board.</summary>
    public int TotalCells => boardCols * boardRows;
}

/// <summary>One stamp piece positioned on the playable board.</summary>
[System.Serializable]
public class CardPlacement
{
    public StampData stamp;
    [MinValue(0)] public int pieceCol;
    [MinValue(0)] public int pieceRow;
    [HideInInspector] public int column;
    [HideInInspector] public int row;

    public bool IsValid => stamp != null && pieceCol >= 0 && pieceCol < stamp.cols && pieceRow >= 0 && pieceRow < stamp.rows;
    public CardPlacement Clone() => new CardPlacement { stamp = stamp, pieceCol = pieceCol, pieceRow = pieceRow, column = column, row = row };
}

/// <summary>One stamp piece in a queue above a board column. Lower order drops first.</summary>
[System.Serializable]
public class QueueCardPlacement : CardPlacement
{
    [HideInInspector] public int order;

    public new QueueCardPlacement Clone() => new QueueCardPlacement
    {
        stamp = stamp,
        pieceCol = pieceCol,
        pieceRow = pieceRow,
        column = column,
        row = -1,
        order = order
    };
}

public enum FillStrategy
{
    /// <summary>Random stamp type and random piece per cell.</summary>
    RandomFromAvailable,

    /// <summary>Ensures each stamp type appears with balanced distribution.</summary>
    BalancedDistribution,
}

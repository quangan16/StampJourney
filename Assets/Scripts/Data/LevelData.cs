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

    [LabelText("Topics")]
    public StampData[] stamps;

    [Header("Authored Layout")]
    [Tooltip("Enabled: use the exact Level Designer board and queues. Disabled: generate one complete four-item set per configured topic and show every topic once.")]
    public bool useAuthoredLayout;

    [Tooltip("Generated layouts only. Keeps exactly one complete four-item topic on the board initially and after each queue release.")]
    public bool hardMode;

    [Tooltip("One entry for each occupied board cell. Rows are top-down, columns are left-right.")]
    public System.Collections.Generic.List<CardPlacement> boardLayout = new();

    [Tooltip("Cards waiting above their column. Order 0 drops first.")]
    public System.Collections.Generic.List<QueueCardPlacement> queueLayout = new();

    [MinValue(0)]
    [Tooltip("Number of queue rows shown by the authored level editor, including empty rows.")]
    public int authoredQueueRows = 1;

    [Header("Obstacles")]
    [Tooltip("Each entry creates one iced card. Its counter decreases whenever another topic is completed.")]
    public System.Collections.Generic.List<IcedCardConfig> icedCards = new();

    [Tooltip("Each entry creates one card that can only be moved along the selected axis.")]
    public System.Collections.Generic.List<DirectionalCardConfig> directionalCards = new();

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

[System.Serializable]
public class IcedCardConfig
{
    [MinValue(1)]
    [LabelText("Break Count")]
    public int breakCount = 1;

    [LabelText("Placement")]
    public ObstacleSpawnLocation spawnLocation = ObstacleSpawnLocation.Random;
}

[System.Serializable]
public class DirectionalCardConfig
{
    [LabelText("Allowed Direction")]
    public RestrictedMoveAxis allowedDirection = RestrictedMoveAxis.Horizontal;

    [LabelText("Placement")]
    public ObstacleSpawnLocation spawnLocation = ObstacleSpawnLocation.Random;
}

public enum RestrictedMoveAxis
{
    Horizontal = 0,
    Vertical = 1
}

public enum ObstacleSpawnLocation
{
    // Keep Random at zero so obstacle entries authored before this field existed retain
    // their original board-or-queue behavior.
    Random = 0,
    InitialBoard = 1,
    Queue = 2
}

/// <summary>One authored item positioned on the playable board.</summary>
[System.Serializable]
public class CardPlacement
{
    [LabelText("Topic")]
    public StampData stamp;
    [MinValue(0)] public int itemIndex;
    [HideInInspector] public int column;
    [HideInInspector] public int row;
    [HideInInspector] public bool hasAuthoredIce;
    [HideInInspector] public int authoredIceBreakCount = 1;
    [HideInInspector] public bool hasAuthoredDirectionRestriction;
    [HideInInspector] public RestrictedMoveAxis authoredAllowedDirection =
        RestrictedMoveAxis.Horizontal;

    public bool IsValid => stamp != null && stamp.IsValidItemIndex(itemIndex);
    public CardPlacement Clone() => new CardPlacement
    {
        stamp = stamp,
        itemIndex = itemIndex,
        column = column,
        row = row,
        hasAuthoredIce = hasAuthoredIce,
        authoredIceBreakCount = authoredIceBreakCount,
        hasAuthoredDirectionRestriction = hasAuthoredDirectionRestriction,
        authoredAllowedDirection = authoredAllowedDirection
    };
}

/// <summary>One authored item in a queue above a board column. Lower order drops first.</summary>
[System.Serializable]
public class QueueCardPlacement : CardPlacement
{
    [HideInInspector] public int order;

    public new QueueCardPlacement Clone() => new QueueCardPlacement
    {
        stamp = stamp,
        itemIndex = itemIndex,
        column = column,
        row = -1,
        order = order,
        hasAuthoredIce = hasAuthoredIce,
        authoredIceBreakCount = authoredIceBreakCount,
        hasAuthoredDirectionRestriction = hasAuthoredDirectionRestriction,
        authoredAllowedDirection = authoredAllowedDirection
    };
}

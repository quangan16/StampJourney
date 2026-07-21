using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Manages per-column card queues that feed into the board when cells become empty.
    /// </summary>
    public class QueueSystem : SerializedMonoBehaviour
    {
        #region Inspector

        [BoxGroup("Visuals")]
        [LabelText("Space Below UI Header")]
        public float queueSpacing = 0.5f;

        [BoxGroup("Visuals")]
        [LabelText("Stack Offset per Card")]
        public Vector2 stackOffset = new Vector2(0f, 0.15f);

        #endregion

        #region Runtime State

        [ShowInInspector, ReadOnly]
        private List<CardModel>[] _columnQueues;

        private Gameboard _gameboard;
        private LevelData _levelData;
        private Camera _queueCamera;
        private float _headerBottomScreenY;
        private bool _hasHeaderAnchor;
        private readonly List<CardModel> _generatedInitialCards = new();
        private readonly List<GeneratedContent> _generatedAvailableContent = new();

        private sealed class GeneratedContent
        {
            public readonly StampData Topic;
            public readonly int ItemIndex;

            public GeneratedContent(StampData topic, int itemIndex)
            {
                Topic = topic;
                ItemIndex = itemIndex;
            }
        }

        public IReadOnlyList<CardModel> GeneratedInitialCards => _generatedInitialCards;
        public int TotalQueueCount => _columnQueues?.Sum(queue => queue?.Count ?? 0) ?? 0;

        #endregion

        #region Initialization

        public void Init(Gameboard gameboard, LevelData levelData)
        {
            _gameboard = gameboard;
            _levelData = levelData;
            _columnQueues = new List<CardModel>[levelData.boardCols];
            for (int c = 0; c < levelData.boardCols; c++)
            {
                _columnQueues[c] = new List<CardModel>();
            }
        }

        public void SetupInitialQueues()
        {
            int cols = _levelData.boardCols;
            if (_levelData.useAuthoredLayout)
            {
                for (int c = 0; c < cols; c++)
                {
                    foreach (var card in _levelData.GetQueueCards(c))
                        AddCardToQueue(c, new CardModel(card.stamp, card.itemIndex));
                }
                return;
            }

            // These are fixed, undecided queue slots. Each slot permanently belongs to the
            // column where it spawns; its topic/item is chosen only when that column can drop it.
            for (int i = 0; i < _generatedAvailableContent.Count; i++)
            {
                int column = i % cols;
                AddCardToQueue(column, new CardModel(null, -1));
            }
        }

        /// <summary>
        /// Builds a randomized unauthored deck. Every valid configured topic contributes one
        /// complete four-item set, so a topic never appears more than once in the level.
        /// </summary>
        public void PrepareGeneratedLevel(int boardCols, int boardRows)
        {
            _generatedInitialCards.Clear();
            _generatedAvailableContent.Clear();

            List<StampData> topics = (_levelData.stamps ?? System.Array.Empty<StampData>())
                .Where(topic => topic != null)
                .ToList();

            StampData invalidTopic = topics.FirstOrDefault(topic => !topic.HasRequiredItemCount);
            if (invalidTopic != null)
            {
                Debug.LogError($"[QueueSystem] Cannot generate level: {invalidTopic.TopicName} must contain exactly {StampData.RequiredItemCount} item pictures.");
                return;
            }

            IGrouping<int, StampData> duplicateId = topics
                .GroupBy(topic => topic.TopicId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateId != null)
            {
                Debug.LogError($"[QueueSystem] Cannot generate level: topic ID {duplicateId.Key} is duplicated.");
                return;
            }

            int boardCapacity = boardCols * boardRows;
            if (boardCapacity < StampData.RequiredItemCount || topics.Count == 0)
            {
                Debug.LogError($"[QueueSystem] Cannot generate level: the board needs at least {StampData.RequiredItemCount} cells and one valid topic.");
                return;
            }

            int availableCardCount = topics.Count * StampData.RequiredItemCount;
            if (availableCardCount < boardCapacity)
            {
                Debug.LogError(
                    $"[QueueSystem] Cannot fill the {boardCols}x{boardRows} board without duplicating items. " +
                    $"The configured topics provide {availableCardCount} unique cards, but the board needs {boardCapacity}.");
                return;
            }

            Shuffle(topics);

            // Build the one-time content pool in randomized topic order. Keeping each topic's
            // four items together here guarantees that the first four initial cards form one
            // complete solvable set; the final board positions are randomized afterwards.
            var allContent = new List<GeneratedContent>(availableCardCount);
            foreach (StampData topic in topics)
            {
                var topicContent = new List<GeneratedContent>(StampData.RequiredItemCount);
                for (int itemIndex = 0; itemIndex < StampData.RequiredItemCount; itemIndex++)
                    topicContent.Add(new GeneratedContent(topic, itemIndex));
                Shuffle(topicContent);
                allContent.AddRange(topicContent);
            }

            // Fill every board cell, including any remainder after complete four-card sets.
            // Example: a 3x3 board receives two full topics plus one item from the next topic.
            for (int contentIndex = 0; contentIndex < boardCapacity; contentIndex++)
            {
                GeneratedContent content = allContent[contentIndex];
                _generatedInitialCards.Add(new CardModel(content.Topic, content.ItemIndex));
            }
            ShuffleInitialCards(boardCols, boardRows);

            // Every unused authored item becomes late-bound queue content. No topic or item is
            // duplicated, and partial topics created by odd board sizes can be completed later.
            for (int contentIndex = boardCapacity; contentIndex < allContent.Count; contentIndex++)
                _generatedAvailableContent.Add(allContent[contentIndex]);
            Shuffle(_generatedAvailableContent);
        }

        public void ClearAllQueues()
        {
            if (_columnQueues == null) return;
            for (int c = 0; c < _columnQueues.Length; c++)
            {
                _columnQueues[c].Clear();
            }
            _generatedInitialCards.Clear();
            _generatedAvailableContent.Clear();
        }

        #endregion

        #region Queue Operations

        public void AddCardToQueue(int col, CardModel model)
        {
            if (_columnQueues == null) return;
            _columnQueues[col].Add(model);
            int queueIndex = _columnQueues[col].Count - 1;
            _gameboard.cardFactory.SpawnCardInQueue(model, col, queueIndex);

            // A header-anchored stack keeps its top fixed, so adding a card also moves the
            // cards already waiting in this column.
            if (_hasHeaderAnchor)
                RefreshColumnVisualsImmediate(col);
        }

        public CardModel PopCard(int col)
        {
            if (GetQueueCount(col) == 0) return null;
            var model = _columnQueues[col][0];
            _columnQueues[col].RemoveAt(0);
            return model;
        }

        /// <summary>
        /// Assigns content only to front queue cards whose own columns have empty cells.
        /// Queue cards are never moved between columns. Their late-bound content is selected to
        /// preserve a solvable board while avoiding unnecessary same-topic clustering.
        /// </summary>
        public int PrepareGeneratedRelease(IReadOnlyList<int> emptyCellsByColumn, int requestedCount)
        {
            if (_columnQueues == null || emptyCellsByColumn == null) return 0;

            int availableSpaces = 0;
            for (int column = 0; column < _columnQueues.Length; column++)
            {
                availableSpaces += Mathf.Min(
                    Mathf.Max(0, emptyCellsByColumn[column]),
                    GetQueueCount(column));
            }

            int releaseCount = Mathf.Min(
                requestedCount,
                Mathf.Min(_generatedAvailableContent.Count, availableSpaces));
            if (releaseCount <= 0) return 0;

            List<GeneratedContent> selectedContent = ChooseGeneratedContent(releaseCount);
            Shuffle(selectedContent);

            int contentIndex = 0;
            for (int column = 0; column < _columnQueues.Length && contentIndex < releaseCount; column++)
            {
                int droppableCount = Mathf.Min(
                    Mathf.Max(0, emptyCellsByColumn[column]),
                    GetQueueCount(column));

                for (int queueIndex = 0;
                     queueIndex < droppableCount && contentIndex < releaseCount;
                     queueIndex++)
                {
                    CardModel queueCard = _columnQueues[column][queueIndex];
                    GeneratedContent content = selectedContent[contentIndex++];
                    _gameboard.cardFactory.AssignGeneratedContent(
                        queueCard,
                        content.Topic,
                        content.ItemIndex);
                    _generatedAvailableContent.Remove(content);
                }
            }

            return contentIndex;
        }

        /// <summary>
        /// Selects late-bound card identities while preserving at least one solvable topic.
        /// When the board is already solvable, content is spread across topics to reduce
        /// automatic links. Otherwise, the missing items of one topic are selected first.
        /// </summary>
        private List<GeneratedContent> ChooseGeneratedContent(int releaseCount)
        {
            var boardItems = new Dictionary<StampData, HashSet<int>>();
            for (int column = 0; column < _gameboard.Cols; column++)
            {
                for (int row = 0; row < _gameboard.Rows; row++)
                {
                    CardModel card = _gameboard.GetCard(column, row);
                    if (card == null || !card.HasAssignedContent) continue;
                    if (!boardItems.TryGetValue(card.Topic, out HashSet<int> items))
                    {
                        items = new HashSet<int>();
                        boardItems.Add(card.Topic, items);
                    }
                    items.Add(card.ItemIndex);
                }
            }

            var selected = new List<GeneratedContent>(releaseCount);
            bool boardAlreadySolvable = boardItems.Any(pair =>
                pair.Key.HasCompleteItemSet(pair.Value));

            if (!boardAlreadySolvable)
            {
                // Find a topic whose remaining queue items all fit in this drop. Selecting those
                // items restores a full four-item topic on the board.
                List<List<GeneratedContent>> completableTopics = _generatedAvailableContent
                    .GroupBy(content => content.Topic)
                    .Where(group =>
                    {
                        int boardCount = boardItems.TryGetValue(group.Key, out HashSet<int> items)
                            ? items.Count
                            : 0;
                        return boardCount + group.Count() >= StampData.RequiredItemCount &&
                               group.Count() <= releaseCount;
                    })
                    .Select(group => group.ToList())
                    .ToList();

                if (completableTopics.Count > 0)
                {
                    int smallestMissingCount = completableTopics.Min(topic => topic.Count);
                    List<List<GeneratedContent>> bestTopics = completableTopics
                        .Where(topic => topic.Count == smallestMissingCount)
                        .ToList();
                    selected.AddRange(bestTopics[Random.Range(0, bestTopics.Count)]);
                }
            }

            // Fill spare drop slots round-robin across random topics. This keeps the result
            // random while avoiding unnecessary clusters of identical-topic cards.
            List<List<GeneratedContent>> remainingByTopic = _generatedAvailableContent
                .Except(selected)
                .GroupBy(content => content.Topic)
                .Select(group =>
                {
                    List<GeneratedContent> values = group.ToList();
                    Shuffle(values);
                    return values;
                })
                .ToList();
            Shuffle(remainingByTopic);

            while (selected.Count < releaseCount && remainingByTopic.Count > 0)
            {
                for (int topicIndex = remainingByTopic.Count - 1;
                     topicIndex >= 0 && selected.Count < releaseCount;
                     topicIndex--)
                {
                    List<GeneratedContent> topicContent = remainingByTopic[topicIndex];
                    selected.Add(topicContent[topicContent.Count - 1]);
                    topicContent.RemoveAt(topicContent.Count - 1);
                    if (topicContent.Count == 0)
                        remainingByTopic.RemoveAt(topicIndex);
                }
                Shuffle(remainingByTopic);
            }

            return selected;
        }

        public void RefreshQueueVisuals()
        {
            for (int column = 0; column < _columnQueues.Length; column++)
            {
                for (int index = 0; index < _columnQueues[column].Count; index++)
                {
                    CardModel model = _columnQueues[column][index];
                    _gameboard.cardFactory.AnimateQueueShift(model, column, index);
                }
            }
        }

        public CardModel GetCardAt(int col, int index)
        {
            if (GetQueueCount(col) <= index) return null;
            return _columnQueues[col][index];
        }

        public int GetQueueCount(int col)
        {
            if (_columnQueues == null || col < 0 || col >= _columnQueues.Length) return 0;
            return _columnQueues[col].Count;
        }

        public bool IsAllQueuesEmpty()
        {
            if (_columnQueues == null) return true;
            for (int c = 0; c < _columnQueues.Length; c++)
            {
                if (_columnQueues[c].Count > 0) return false;
            }
            return true;
        }

        #endregion

        #region World Position

        public float GetHeaderReservedWorldHeight()
        {
            if (_columnQueues == null) return 0f;

            int largestQueueCount = 0;
            for (int col = 0; col < _columnQueues.Length; col++)
                largestQueueCount = Mathf.Max(largestQueueCount, _columnQueues[col].Count);

            if (largestQueueCount == 0) return 0f;

            var config = GameManager.Instance.GameConfig;
            float stackHeight = Mathf.Abs(stackOffset.y) * (largestQueueCount - 1);
            return Mathf.Max(0f, queueSpacing) + config.cardHeight + stackHeight;
        }

        public bool TryGetQueueScreenRect(Camera targetCamera, out Rect screenRect)
        {
            screenRect = default;
            if (targetCamera == null || _columnQueues == null) return false;

            var config = GameManager.Instance.GameConfig;
            float halfWidth = config.cardWidth * 0.5f;
            float halfHeight = config.cardHeight * 0.5f;
            bool hasCard = false;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int col = 0; col < _columnQueues.Length; col++)
            {
                for (int index = 0; index < _columnQueues[col].Count; index++)
                {
                    Vector2 center = GetQueueWorldPosition(col, index);
                    Vector3 bottomLeft = targetCamera.WorldToScreenPoint(
                        new Vector3(center.x - halfWidth, center.y - halfHeight, _gameboard.transform.position.z));
                    Vector3 topRight = targetCamera.WorldToScreenPoint(
                        new Vector3(center.x + halfWidth, center.y + halfHeight, _gameboard.transform.position.z));

                    minX = Mathf.Min(minX, bottomLeft.x, topRight.x);
                    maxX = Mathf.Max(maxX, bottomLeft.x, topRight.x);
                    minY = Mathf.Min(minY, bottomLeft.y, topRight.y);
                    maxY = Mathf.Max(maxY, bottomLeft.y, topRight.y);
                    hasCard = true;
                }
            }

            if (!hasCard) return false;
            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        public void SetHeaderScreenAnchor(Camera queueCamera, float headerBottomScreenY)
        {
            _queueCamera = queueCamera;
            _headerBottomScreenY = headerBottomScreenY;
            _hasHeaderAnchor = queueCamera != null;
        }

        public void ClearHeaderScreenAnchor()
        {
            _queueCamera = null;
            _hasHeaderAnchor = false;
        }

        /// <summary>
        /// Gets the queue position aligned to its board column and vertically anchored below
        /// the HUD header. Falls back to the legacy board-relative position when no header exists.
        /// </summary>
        public Vector2 GetQueueWorldPosition(int col, int queueIndex)
        {
            Vector2 topRowPos = _gameboard.GetWorldPosition(col, 0);
            var config = GameManager.Instance.GameConfig;

            if (_hasHeaderAnchor && _queueCamera != null)
            {
                float worldDepth = Mathf.Abs(
                    _gameboard.transform.position.z - _queueCamera.transform.position.z);
                Vector3 headerBottomWorld = _queueCamera.ScreenToWorldPoint(
                    new Vector3(0f, _headerBottomScreenY, worldDepth));

                float topCardY = headerBottomWorld.y - queueSpacing - config.cardHeight * 0.5f;
                int lastQueueIndex = Mathf.Max(0, GetQueueCount(col) - 1);
                float firstCardY = topCardY - stackOffset.y * lastQueueIndex;

                return new Vector2(
                    topRowPos.x + stackOffset.x * queueIndex,
                    firstCardY + stackOffset.y * queueIndex);
            }

            float strideY = config.cardHeight + config.cardGap;
            Vector2 basePos = topRowPos + Vector2.up * (strideY + queueSpacing);

            return basePos + stackOffset * queueIndex;
        }

        private void RefreshColumnVisualsImmediate(int col)
        {
            for (int index = 0; index < GetQueueCount(col); index++)
            {
                CardModel model = _columnQueues[col][index];
                _gameboard.cardFactory.SetQueuePositionImmediate(model, col, index);
            }
        }

        #endregion

        #region Generated Shuffle Helpers

        private void ShuffleInitialCards(int boardCols, int boardRows)
        {
            // Compare many random layouts and retain the one with the fewest orthogonally
            // adjacent same-topic cards. A completed 2x2 square receives a large penalty.
            // Equal-scoring layouts are chosen randomly, so generated levels still vary.
            const int attempts = 256;
            var bestLayout = new List<CardModel>(_generatedInitialCards);
            int bestScore = int.MaxValue;
            int equalBestCount = 0;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Shuffle(_generatedInitialCards);
                int score = ScoreInitialLayout(boardCols, boardRows);

                if (score < bestScore)
                {
                    bestScore = score;
                    equalBestCount = 1;
                    bestLayout.Clear();
                    bestLayout.AddRange(_generatedInitialCards);
                }
                else if (score == bestScore)
                {
                    equalBestCount++;
                    if (Random.Range(0, equalBestCount) == 0)
                    {
                        bestLayout.Clear();
                        bestLayout.AddRange(_generatedInitialCards);
                    }
                }

                // Zero means there are no linked same-topic neighbors at all.
                if (bestScore == 0) break;
            }

            _generatedInitialCards.Clear();
            _generatedInitialCards.AddRange(bestLayout);
        }

        private int ScoreInitialLayout(int boardCols, int boardRows)
        {
            var topics = new StampData[boardCols, boardRows];
            for (int index = 0; index < _generatedInitialCards.Count; index++)
            {
                int column = index % boardCols;
                int queueIndex = index / boardCols;
                int row = boardRows - 1 - queueIndex;
                if (row >= 0)
                    topics[column, row] = _generatedInitialCards[index].Topic;
            }

            int linkedEdges = 0;
            int completedSquares = 0;

            for (int column = 0; column < boardCols; column++)
            {
                for (int row = 0; row < boardRows; row++)
                {
                    StampData topic = topics[column, row];
                    if (topic == null) continue;

                    if (column + 1 < boardCols && topics[column + 1, row] == topic)
                        linkedEdges++;
                    if (row + 1 < boardRows && topics[column, row + 1] == topic)
                        linkedEdges++;

                    if (column + 1 < boardCols && row + 1 < boardRows &&
                        topics[column + 1, row] == topic &&
                        topics[column, row + 1] == topic &&
                        topics[column + 1, row + 1] == topic)
                        completedSquares++;
                }
            }

            return linkedEdges + completedSquares * 1000;
        }

        private static void Shuffle<T>(IList<T> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int swapIndex = Random.Range(0, i + 1);
                (values[i], values[swapIndex]) = (values[swapIndex], values[i]);
            }
        }

        #endregion

    }
}

using System.Collections.Generic;
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

        [BoxGroup("Settings")]
        [LabelText("Queue Size")]
        public int queueSize = 4;

        [BoxGroup("Visuals")]
        [LabelText("Space Between Board and Queue")]
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

            for (int c = 0; c < cols; c++)
            {
                int startIndex = _columnQueues[c].Count;
                for (int i = 0; i < queueSize; i++)
                {
                    _columnQueues[c].Add(CreateRandomCard(c, startIndex + i));
                }
            }
        }

        public void ClearAllQueues()
        {
            if (_columnQueues == null) return;
            for (int c = 0; c < _columnQueues.Length; c++)
            {
                _columnQueues[c].Clear();
            }
        }

        #endregion

        #region Queue Operations

        public void AddCardToQueue(int col, CardModel model)
        {
            if (_columnQueues == null) return;
            _columnQueues[col].Add(model);
            int queueIndex = _columnQueues[col].Count - 1;
            _gameboard.cardFactory.SpawnCardInQueue(model, col, queueIndex);
        }

        public CardModel PopCard(int col)
        {
            if (GetQueueCount(col) == 0) return null;
            var model = _columnQueues[col][0];
            _columnQueues[col].RemoveAt(0);
            return model;
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

        /// <summary>
        /// Gets the world position for a card in the queue above the board.
        /// </summary>
        public Vector2 GetQueueWorldPosition(int col, int queueIndex)
        {
            Vector2 topRowPos = _gameboard.GetWorldPosition(col, 0);

            var config = GameManager.Instance.GameConfig;
            float strideY = config.cardHeight + config.cardGap;
            Vector2 basePos = topRowPos + Vector2.up * (strideY + queueSpacing);

            return basePos + stackOffset * queueIndex;
        }

        #endregion

        #region Private

        private CardModel CreateRandomCard(int col, int queueIndex)
        {
            var stamps = _levelData.stamps;
            var topic = stamps[UnityEngine.Random.Range(0, stamps.Length)];
            int itemIndex = UnityEngine.Random.Range(0, topic.TotalItems);
            var model = new CardModel(topic, itemIndex);
            _gameboard.cardFactory.SpawnCardInQueue(model, col, queueIndex);
            return model;
        }

        #endregion
    }
}

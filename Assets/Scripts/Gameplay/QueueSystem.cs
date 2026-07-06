using System.Collections.Generic;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Gameplay
{
    public class QueueSystem : SerializedMonoBehaviour
    {
        [BoxGroup("Settings")]
        [LabelText("Queue Size")]
        public int queueSize = 4;

        [BoxGroup("Visuals")]
        [LabelText("Space Between Board and Queue")]
        public float queueSpacing = 0.5f;

        [BoxGroup("Visuals")]
        [LabelText("Stack Offset per Card")]
        public Vector2 stackOffset = new Vector2(0f, 0.15f);

        [ShowInInspector, ReadOnly]
        private List<CardModel>[] _columnQueues;

        private Gameboard _gameboard;
        private LevelData _levelData;

        public void Init(Gameboard gameboard, LevelData levelData)
        {
            _gameboard = gameboard;
            _levelData = levelData;
            _columnQueues = new List<CardModel>[levelData.levelConfig.boardCols];
            for (int c = 0; c < levelData.levelConfig.boardCols; c++)
            {
                _columnQueues[c] = new List<CardModel>();
            }
        }

        public void SetupInitialQueues()
        {
            int cols = _levelData.levelConfig.boardCols;
            for (int c = 0; c < cols; c++)
            {
                int startIndex = _columnQueues[c].Count;
                for (int i = 0; i < queueSize; i++)
                {
                    _columnQueues[c].Add(CreateRandomCard(c, startIndex + i));
                }
            }
        }

        public void AddCardToQueue(int col, CardModel model)
        {
            if (_columnQueues == null) return;
            _columnQueues[col].Add(model);
            int queueIndex = _columnQueues[col].Count - 1;
            _gameboard.cardFactory.SpawnCardInQueue(model, col, queueIndex);
        }

        public void ClearAllQueues()
        {
            if (_columnQueues == null) return;
            for (int c = 0; c < _columnQueues.Length; c++)
            {
                _columnQueues[c].Clear();
            }
        }

        private CardModel CreateRandomCard(int col, int queueIndex)
        {
            var stamps = _levelData.levelConfig.stamps;
            var stamp = stamps[UnityEngine.Random.Range(0, stamps.Length)];
            int pc = UnityEngine.Random.Range(0, stamp.cols);
            int pr = UnityEngine.Random.Range(0, stamp.rows);
            var model = new CardModel(stamp, pc, pr);
            _gameboard.cardFactory.SpawnCardInQueue(model, col, queueIndex);
            return model;
        }

        public Vector2 GetQueueWorldPosition(int col, int queueIndex)
        {
            // Base position is just above the board's top row (row 0)
            Vector2 topRowPos = _gameboard.GetWorldPosition(col, 0);
            
            // Add spacing to jump above the board
            var config = Core.GameManager.Instance.GameConfig;
            float strideY = config.cardHeight + config.cardGap;
            Vector2 basePos = topRowPos + Vector2.up * (strideY + queueSpacing);

            // Add the stack offset for each subsequent card in the queue
            return basePos + stackOffset * queueIndex;
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

        public int GetQueueCount(int col)
        {
            if (_columnQueues == null || col < 0 || col >= _columnQueues.Length) return 0;
            return _columnQueues[col].Count;
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
    }
}

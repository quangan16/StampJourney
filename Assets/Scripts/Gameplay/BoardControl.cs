using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Manages the entire board grid state.
    /// Responsible for: board initialization, tile swapping, gravity, and event broadcasting.
    /// </summary>
    public class Gameboard : SerializedMonoBehaviour
    {
        #region Inspector

        [BoxGroup("References")]
        public StampDetector stampDetector;
        [BoxGroup("References")]
        public GravitySystem gravitySystem;
        [BoxGroup("References")]
        public CardFactory cardFactory;
        [BoxGroup("References")]
        public Tile tilePrefab;
        [BoxGroup("References")]
        public QueueSystem queueSystem;

        #endregion

        #region Grid Data

        /// <summary>Grid[col, row] — holds fixed Tile references.</summary>
        [ShowInInspector, ReadOnly]
        private Tile[,] _tiles;

        private LevelData _levelData;
        private int _cols, _rows;

        #endregion

        #region Events

        public event Action<CardModel, CardModel> OnSwapCompleted;
        public event Action<List<CardModel>> OnStampCleared;
        public event Action OnBoardSettled;

        #endregion

        #region Properties

        public int Cols => _cols;
        public int Rows => _rows;

        #endregion

        #region Public API — Initialization

        /// <summary>Initializes the board with LevelData. Clears old tiles if present.</summary>
        public void Init(LevelData levelData)
        {
            Debug.Log($"[Gameboard] Init — cols={levelData.boardCols} rows={levelData.boardRows}");

            _levelData = levelData;
            _cols = levelData.boardCols;
            _rows = levelData.boardRows;

            stampDetector.Init(this);
            gravitySystem.Init(this);
            cardFactory.Init(this);
            queueSystem?.Init(this, _levelData);
        }

        public async UniTask SetupAsync()
        {
            cardFactory?.DespawnAll();
            SpawnTiles();

            queueSystem?.ClearAllQueues();
            FillBoardInitial();
            queueSystem?.SetupInitialQueues();

            await StartupEffectAsync();

            // Rebuild groups + edges after initial fill
            stampDetector?.RebuildGroups();
            UpdateAllEdges();
            await CheckAndClearAsync();
        }

        #endregion

        #region Public API — Tile Access

        /// <summary>Gets the Tile at (col, row). Returns null if out of bounds.</summary>
        public Tile GetTile(int col, int row)
        {
            if (!IsInBounds(col, row)) return null;
            return _tiles[col, row];
        }

        /// <summary>Gets the CardModel at (col, row). Returns null if empty or out of bounds.</summary>
        public CardModel GetCard(int col, int row)
        {
            var tile = GetTile(col, row);
            return tile?.Card;
        }

        /// <summary>Assigns a card to the grid cell (used by GravitySystem).</summary>
        public void SetTile(int col, int row, CardModel card)
        {
            if (!IsInBounds(col, row)) return;
            _tiles[col, row].SetCard(card);
        }

        public bool IsInBounds(int col, int row) =>
            col >= 0 && col < _cols && row >= 0 && row < _rows;

        /// <summary>Whether the cell is empty.</summary>
        public bool IsEmpty(int col, int row) =>
            IsInBounds(col, row) && !_tiles[col, row].IsOccupied;

        public bool IsColumnBusy(int col)
        {
            if (col < 0 || col >= _cols) return true;
            for (int r = 0; r < _rows; r++)
            {
                var card = GetCard(col, r);
                if (card != null && card.IsAnimating) return true;
            }
            return false;
        }

        public bool IsBoardAndQueuesEmpty()
        {
            if (_tiles == null) return false;
            if (queueSystem != null && !queueSystem.IsAllQueuesEmpty()) return false;

            for (int c = 0; c < _cols; c++)
                for (int r = 0; r < _rows; r++)
                    if (_tiles[c, r].IsOccupied) return false;

            return true;
        }

        #endregion

        #region Public API — Swap

        /// <summary>Attempts to swap two tiles by board position.</summary>
        public async UniTask TrySwapAsync(int colA, int rowA, int colB, int rowB)
        {
            if (!IsInBounds(colA, rowA) || !IsInBounds(colB, rowB)) return;

            var cardA = GetCard(colA, rowA);
            var cardB = GetCard(colB, rowB);
            if (cardA == null || cardB == null) return;

            SwapInGrid(colA, rowA, colB, rowB);
            OnSwapCompleted?.Invoke(cardA, cardB);

            // Rebuild groups after swap
            stampDetector.RebuildGroups();
            UpdateAllEdges();
            await CheckAndClearAsync();
        }

        /// <summary>Swaps an entire group by a grid delta. Called by CardView during group drag.</summary>
        public async UniTask TrySwapGroupAsync(CardGroup group, int deltaCol, int deltaRow)
        {
            if (group == null) return;

            // Cache members BEFORE RebuildGroups — Rebuild will Disband the old group
            var cachedMembers = new List<CardModel>(group.Members);

            // Reject if any target column is busy (cards are dropping)
            foreach (var member in cachedMembers)
            {
                int newCol = member.BoardCol + deltaCol;
                if (IsColumnBusy(newCol))
                {
                    AnimateAllTilesToGridPositions(0.18f);
                    return;
                }
            }

            bool success = stampDetector.TrySwapGroup(group, deltaCol, deltaRow);

            if (success)
            {
                AnimateAllTilesToGridPositions(0.25f);

                if (cachedMembers.Count > 0)
                    OnSwapCompleted?.Invoke(cachedMembers[0], cachedMembers[0]);

                stampDetector.RebuildGroups();
                UpdateAllEdges();
                await UniTask.WaitForSeconds(0.2f);
                await CheckAndClearAsync();
            }
            else
            {
                // Snap back all members
                AnimateAllTilesToGridPositions(0.18f);
                stampDetector.RebuildGroups();
                UpdateAllEdges();
            }

            // Unmark animating — use cachedMembers since group.Members has been cleared
        }

        public async UniTask TrySwapSingleGridAsync(CardModel card, int deltaCol, int deltaRow)
        {
            if (card == null || (deltaCol == 0 && deltaRow == 0)) return;

            int newCol = card.BoardCol + deltaCol;
            int newRow = card.BoardRow + deltaRow;

            // Reject if target column is busy
            if (IsColumnBusy(newCol))
            {
                AnimateAllTilesToGridPositions(0.18f);
                return;
            }

            if (!IsInBounds(newCol, newRow))
            {
                AnimateAllTilesToGridPositions(0.18f);
                return;
            }

            var targetCard = GetCard(newCol, newRow);
            int origCol = card.BoardCol;
            int origRow = card.BoardRow;

            SetTile(origCol, origRow, targetCard);
            SetTile(newCol, newRow, card);

            AnimateAllTilesToGridPositions(0.18f);

            if (targetCard != null)
                OnSwapCompleted?.Invoke(card, targetCard);
            else
                OnSwapCompleted?.Invoke(card, card);

            stampDetector.RebuildGroups();
            UpdateAllEdges();

            await UniTask.Delay(TimeSpan.FromSeconds(0.2f));
            await CheckAndClearAsync();
        }

        #endregion

        #region Public API — World Position

        /// <summary>
        /// World position of the center of cell (col, row).
        /// Computed from GameConfig card dimensions.
        /// </summary>
        public Vector2 GetWorldPosition(int col, int row)
        {
            var config = GameManager.Instance.GameConfig;
            float cardWidth = config.cardWidth;
            float cardHeight = config.cardHeight;
            float cardGap = config.cardGap;

            float strideX = cardWidth + cardGap;
            float strideY = cardHeight + cardGap;

            float boardWidth = _cols * strideX;
            float boardHeight = _rows * strideY;

            Vector2 boardCenter = transform.position;
            float startX = boardCenter.x - boardWidth / 2f + cardWidth / 2f;
            float startY = boardCenter.y + boardHeight / 2f - cardHeight / 2f;

            return new Vector2(
                startX + col * strideX,
                startY - row * strideY
            );
        }

        /// <summary>
        /// World-space bounds occupied by the playable board and, optionally, its waiting queues.
        /// </summary>
        public Bounds GetGameplayBounds(bool includeQueues = true)
        {
            if (_cols <= 0 || _rows <= 0)
                return new Bounds(transform.position, Vector3.zero);

            var config = GameManager.Instance.GameConfig;
            float halfCardWidth = config.cardWidth * 0.5f;
            float halfCardHeight = config.cardHeight * 0.5f;
            Vector2 firstCard = GetWorldPosition(0, 0);
            Vector2 lastCard = GetWorldPosition(_cols - 1, _rows - 1);

            float minX = Mathf.Min(firstCard.x, lastCard.x) - halfCardWidth;
            float maxX = Mathf.Max(firstCard.x, lastCard.x) + halfCardWidth;
            float minY = Mathf.Min(firstCard.y, lastCard.y) - halfCardHeight;
            float maxY = Mathf.Max(firstCard.y, lastCard.y) + halfCardHeight;

            if (includeQueues && queueSystem != null && _levelData != null)
            {
                for (int col = 0; col < _cols; col++)
                {
                    int queueCardCount = _levelData.useAuthoredLayout
                        ? _levelData.GetQueueCards(col).Count()
                        : queueSystem.queueSize;

                    for (int queueIndex = 0; queueIndex < queueCardCount; queueIndex++)
                    {
                        Vector2 queuePosition = queueSystem.GetQueueWorldPosition(col, queueIndex);
                        minX = Mathf.Min(minX, queuePosition.x - halfCardWidth);
                        maxX = Mathf.Max(maxX, queuePosition.x + halfCardWidth);
                        minY = Mathf.Min(minY, queuePosition.y - halfCardHeight);
                        maxY = Mathf.Max(maxY, queuePosition.y + halfCardHeight);
                    }
                }
            }

            Vector3 center = new((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
            Vector3 size = new(maxX - minX, maxY - minY, 0f);
            return new Bounds(center, size);
        }

        #endregion

        #region Public API — Visual Updates

        /// <summary>Updates edge renderers for all active tiles on the board.</summary>
        public void UpdateAllEdges()
        {
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    var card = GetCard(c, r);
                    if (card == null) continue;
                    var view = cardFactory.GetView(card.TileId);
                    if (view == null) continue;
                    view.cardEdgeRenderer?.UpdateEdges();
                }
            }
        }

        /// <summary>
        /// Animates all tiles to their correct grid positions.
        /// Used after group swaps when multiple tiles have shifted.
        /// </summary>
        public void AnimateAllTilesToGridPositions(float duration)
        {
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    var card = GetCard(c, r);
                    if (card == null) continue;
                    var view = cardFactory.GetView(card.TileId);
                    if (view == null) continue;
                    var targetPos = GetWorldPosition(c, r);
                    if ((view.transform.position - (Vector3)targetPos).sqrMagnitude <= 0.0001f)
                        continue;

                    var movingCard = card;
                    movingCard.IsAnimating = true;
                    view.transform.DOMove(targetPos, duration)
                        .SetEase(Ease.OutCubic)
                        .OnComplete(() => movingCard.IsAnimating = false);
                }
            }
        }

        #endregion

        #region Private — Board Fill

        private void FillBoardInitial()
        {
            if (_levelData.useAuthoredLayout)
            {
                FillAuthoredBoard();
                return;
            }

            if (_levelData.fillStrategy == FillStrategy.BalancedDistribution)
                FillBalanced();
            else
                FillRandom();
        }

        private void FillAuthoredBoard()
        {
            // Startup pops from each column bottom-up. Enqueue the authored cards in that same order.
            for (int c = 0; c < _cols; c++)
            {
                for (int r = _rows - 1; r >= 0; r--)
                {
                    if (!_levelData.TryGetBoardCard(c, r, out var card)) continue;
                    queueSystem.AddCardToQueue(c, new CardModel(card.stamp, card.itemIndex));
                }
            }
        }

        private void FillRandom()
        {
            var stamps = _levelData.stamps;
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var stamp = stamps[UnityEngine.Random.Range(0, stamps.Length)];
                    int itemIndex = UnityEngine.Random.Range(0, stamp.TotalItems);
                    var model = new CardModel(stamp, itemIndex);
                    queueSystem.AddCardToQueue(c, model);
                }
            }
        }

        private void FillBalanced()
        {
            // Build a pool ensuring each topic has every authored item represented.
            var pool = new List<(StampData topic, int itemIndex)>();
            var stamps = _levelData.stamps;
            int total = _cols * _rows;

            while (pool.Count < total)
            {
                foreach (var topic in stamps)
                    for (int itemIndex = 0; itemIndex < topic.TotalItems; itemIndex++)
                        pool.Add((topic, itemIndex));
            }

            // Fisher-Yates shuffle
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    int idx = c + r * _cols;
                    if (idx >= pool.Count) break;
                    var (topic, itemIndex) = pool[idx];
                    var model = new CardModel(topic, itemIndex);
                    queueSystem.AddCardToQueue(c, model);
                }
            }
        }

        #endregion

        #region Private — Tile Spawning

        private void SpawnTiles()
        {
            // Destroy old tiles if present
            if (_tiles != null && _tiles.Length > 0)
            {
                foreach (var tile in _tiles)
                {
                    if (tile != null) Destroy(tile.gameObject);
                }
            }

            _tiles = new Tile[_cols, _rows];

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var newTile = Instantiate(tilePrefab, transform);
                    newTile.gameObject.SetActive(true);
                    newTile.Init(c, r, this);
                    newTile.transform.position = GetWorldPosition(c, r);
                    _tiles[c, r] = newTile;
                }
            }
        }

        private async UniTask StartupEffectAsync()
        {
            var droppedCards = new List<CardModel>();

            // Drop cards row by row from bottom to top
            for (int r = _rows - 1; r >= 0; r--)
            {
                for (int c = 0; c < _cols; c++)
                {
                    if (queueSystem.GetQueueCount(c) > 0)
                    {
                        var model = queueSystem.PopCard(c);
                        _tiles[c, r].SetCard(model);
                        cardFactory.AnimateDropOnly(model, c, r);
                        droppedCards.Add(model);
                    }
                }
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f));
            }

            // Animate remaining queue cards shifting
            for (int c = 0; c < _cols; c++)
            {
                for (int i = 0; i < queueSystem.GetQueueCount(c); i++)
                {
                    var queuedModel = queueSystem.GetCardAt(c, i);
                    cardFactory.AnimateQueueShift(queuedModel, c, i);
                }
            }

            await UniTask.Delay(TimeSpan.FromSeconds(cardFactory.dropEaseTime + 0.2f));

            // Flip all dropped cards face-up
            foreach (var model in droppedCards)
            {
                var view = cardFactory.GetView(model.TileId);
                if (view != null) view.PlayFlip(FlipState.Up, false);
                model.CanDrag = true;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(0.4f));
        }

        #endregion

        #region Private — Match & Clear

        private async UniTask CheckAndClearAsync()
        {
            bool anyCleared;
            do
            {
                var matches = stampDetector.FindCompletedStamps(_tiles, _cols, _rows);
                anyCleared = matches.Count > 0;

                if (anyCleared)
                {
                    foreach (var group in matches)
                    {
                        // Calculate group center for ripple effect
                        Vector2 gridCenter = Vector2.zero;
                        foreach (var tile in group.Members)
                            gridCenter += new Vector2(tile.BoardCol, tile.BoardRow);

                        if (group.Members.Count > 0)
                            gridCenter /= group.Members.Count;

                        // Ripple effect on non-cleared tiles
                        ApplyRippleEffect(group, gridCenter);

                        // Clear tiles from grid
                        foreach (var tile in group.Members)
                            _tiles[tile.BoardCol, tile.BoardRow].SetCard(null);

                        await cardFactory.DespawnStampGroupAsync(group);
                        OnStampCleared?.Invoke(new List<CardModel>(group.Members));
                    }

                    await UniTask.Delay(TimeSpan.FromSeconds(0.1f));
                }

                // Apply gravity then fill empty cells
                gravitySystem.ApplyGravityAsync().Forget();
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f));
                await FillEmptyCellsAsync();

            } while (anyCleared);

            OnBoardSettled?.Invoke();
        }

        private void ApplyRippleEffect(CardGroup clearedGroup, Vector2 gridCenter)
        {
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    var model = GetCard(c, r);
                    if (model == null || clearedGroup.Members.Contains(model)) continue;

                    // Skip cards that belong to other completed stamps
                    if (model.Group != null && model.Group.IsTopicComplete) continue;

                    var view = cardFactory.GetView(model.TileId);
                    if (view != null)
                    {
                        float distance = Vector2.Distance(new Vector2(c, r), gridCenter);
                        float delay = distance * 0.15f;
                        view.PlayRippleEffect(delay);
                    }
                }
            }
        }

        private async UniTask FillEmptyCellsAsync()
        {
            bool spawnedAny = false;
            for (int c = 0; c < _cols; c++)
            {
                int emptyCount = 0;
                for (int r = _rows - 1; r >= 0; r--)
                {
                    if (!_tiles[c, r].IsOccupied && queueSystem.GetQueueCount(c) > 0)
                    {
                        var model = queueSystem.PopCard(c);
                        _tiles[c, r].SetCard(model);
                        cardFactory.AnimateDropAndFlip(model, c, r);
                        emptyCount++;
                        spawnedAny = true;
                    }
                }

                // Shift remaining queue visuals
                if (emptyCount > 0 && queueSystem.GetQueueCount(c) > 0)
                {
                    for (int i = 0; i < queueSystem.GetQueueCount(c); i++)
                    {
                        var queuedModel = queueSystem.GetCardAt(c, i);
                        cardFactory.AnimateQueueShift(queuedModel, c, i);
                    }
                }
            }

            if (spawnedAny)
                await UniTask.Delay(TimeSpan.FromSeconds(0.4f));

            // Rebuild groups for newly spawned tiles
            stampDetector.RebuildGroups();
            UpdateAllEdges();
        }

        #endregion

        #region Private — Helpers

        private void SwapInGrid(int colA, int rowA, int colB, int rowB)
        {
            var cardA = GetCard(colA, rowA);
            var cardB = GetCard(colB, rowB);

            _tiles[colA, rowA].SetCard(cardB);
            _tiles[colB, rowB].SetCard(cardA);
        }

        #endregion
    }
}

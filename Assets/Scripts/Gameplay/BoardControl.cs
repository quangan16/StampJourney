using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using StampJourney.Data;
using StampJourney.Gameplay.Boosters;
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
        [BoxGroup("References")]
        [SerializeField] private BoosterController boosterController;
        [BoxGroup("References")]
        [SerializeField] private GameplayControl _gameplayControl;

        #endregion

        #region Hierarchy

        [BoxGroup("Hierarchy")]
        public Transform queue;
        [BoxGroup("Hierarchy")]
        public Transform Mainboard;
        [BoxGroup("Hierarchy")]
        public Transform Tiles;
        [BoxGroup("Hierarchy")]
        public Transform CardPool;

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
        public BoosterController Boosters => boosterController;
        public Transform TilesHolder => Tiles != null ? Tiles : transform;
        public Transform QueueHolder => queue != null ? queue : transform;
        public Transform MainboardHolder => Mainboard != null ? Mainboard : transform;
        public Transform CardPoolHolder => CardPool != null ? CardPool : transform;

        #endregion

        #region Public API — Initialization

        /// <summary>Initializes the board with LevelData. Clears old tiles if present.</summary>
        public void Init(GameplayControl gameplayControl)
        {

            if (gameplayControl == null) return;
            _gameplayControl = gameplayControl;
            _levelData = gameplayControl.LevelData;
            _cols = gameplayControl.LevelData.boardCols;
            _rows = gameplayControl.LevelData.boardRows;

            ResolveHierarchyHolders();

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
            if (!_levelData.useAuthoredLayout)
                queueSystem?.PrepareGeneratedLevel(_cols, _rows);
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

        public bool HasAnimatingCards()
        {
            for (int c = 0; c < _cols; c++)
                for (int r = 0; r < _rows; r++)
                    if (GetCard(c, r)?.IsAnimating == true)
                        return true;

            return false;
        }

        #endregion

        #region Public API - Boosters

        /// <summary>Finishes a booster-driven clear through the normal board settlement path.</summary>
        public async UniTask SettleBoosterClearAsync(IReadOnlyCollection<CardModel> clearedCards)
        {
            if (clearedCards == null || clearedCards.Count == 0) return;

            var cards = new List<CardModel>(clearedCards);
            ReduceIceObstacles(1);
            OnStampCleared?.Invoke(cards);
            await gravitySystem.ApplyGravityAsync();
            await FillEmptyCellsAsync(cards.Count);
            await CheckAndClearAsync();
        }

        #endregion

        #region Public API - Swap

        /// <summary>Swaps an entire group by a grid delta. Called by CardView during group drag.</summary>
        public async UniTask TrySwapGroupAsync(CardGroup group, int deltaCol, int deltaRow)
        {
            if (group == null) return;

            // Cache members BEFORE RebuildGroups — Rebuild will Disband the old group
            var cachedMembers = new List<CardModel>(group.Members);

            // Ice is a locked obstacle. Reject the move if an iced member somehow reaches
            // this API or any destination cell contains an iced card outside the group.
            if (cachedMembers.Any(member => member == null || member.IsIced))
            {
                await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                return;
            }

            var memberIds = new HashSet<int>(cachedMembers.Select(member => member.TileId));
            foreach (var member in cachedMembers)
            {
                CardModel destination = GetCard(
                    member.BoardCol + deltaCol,
                    member.BoardRow + deltaRow);
                if (destination != null && destination.IsIced && !memberIds.Contains(destination.TileId))
                {
                    await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                    return;
                }
            }

            // Reject if any target column is busy (cards are dropping)
            foreach (var member in cachedMembers)
            {
                int newCol = member.BoardCol + deltaCol;
                if (IsColumnBusy(newCol))
                {
                    await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                    return;
                }
            }

            bool success = stampDetector.TrySwapGroup(group, deltaCol, deltaRow);

            if (success)
            {
                HideBrokenLiquidBridges();
                AnimateAllTilesToGridPositions(0.25f);

                if (cachedMembers.Count > 0)
                    OnSwapCompleted?.Invoke(cachedMembers[0], cachedMembers[0]);

                await SettleAfterPlayerMoveAsync(0.25f);
            }
            else
            {
                // Snap back all members
                await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                stampDetector.RebuildGroups();
                UpdateAllEdges();
            }

            // Unmark animating — use cachedMembers since group.Members has been cleared
        }

        public async UniTask TrySwapSingleGridAsync(CardModel card, int deltaCol, int deltaRow)
        {
            if (card == null || (deltaCol == 0 && deltaRow == 0)) return;
            if (card.IsIced)
            {
                await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                return;
            }

            int newCol = card.BoardCol + deltaCol;
            int newRow = card.BoardRow + deltaRow;

            // Reject if target column is busy
            if (IsColumnBusy(newCol))
            {
                await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                return;
            }

            if (!IsInBounds(newCol, newRow))
            {
                await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                return;
            }

            var targetCard = GetCard(newCol, newRow);
            if (targetCard != null && targetCard.IsIced)
            {
                await AnimateAllTilesToGridPositionsAndWaitAsync(0.18f);
                return;
            }

            int origCol = card.BoardCol;
            int origRow = card.BoardRow;

            SetTile(origCol, origRow, targetCard);
            SetTile(newCol, newRow, card);

            HideBrokenLiquidBridges();
            AnimateAllTilesToGridPositions(0.18f);

            if (targetCard != null)
                OnSwapCompleted?.Invoke(card, targetCard);
            else
                OnSwapCompleted?.Invoke(card, card);

            await SettleAfterPlayerMoveAsync(0.2f);
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
        /// World-space bounds occupied by the playable board. The waiting queue is positioned
        /// independently relative to the HUD header and is intentionally excluded.
        /// </summary>
        public Bounds GetGameplayBounds()
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
                        .SetEase(Ease.OutQuad)
                        .OnComplete(() => movingCard.IsAnimating = false);
                }
            }
        }

        /// <summary>
        /// Commits every active card view to the position stored in the board grid.
        /// This is deliberately separate from the animated move: a card can be reparented when
        /// groups are rebuilt, or its tween can be killed by another visual effect. In either
        /// case the logical swap has already succeeded, so leaving the view at the mouse release
        /// position would make it appear permanently stuck.
        /// </summary>
        public void SnapAllTilesToGridPositionsImmediate()
        {
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    CardModel card = GetCard(c, r);
                    if (card == null) continue;

                    CardView view = cardFactory.GetView(card.TileId);
                    if (view == null || !view.gameObject.activeInHierarchy) continue;

                    view.transform.DOKill();
                    view.transform.position = GetWorldPosition(c, r);
                    view.transform.localScale = Vector3.one;
                    view.transform.rotation = Quaternion.identity;
                    card.IsAnimating = false;
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

            FillGeneratedBoard();
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

        private void FillGeneratedBoard()
        {
            IReadOnlyList<CardModel> cards = queueSystem.GeneratedInitialCards;
            for (int index = 0; index < cards.Count; index++)
            {
                int column = index % _cols;
                queueSystem.AddCardToQueue(column, cards[index]);
            }
        }

        /// <summary>
        /// Removes stale bridges immediately after logical board coordinates change, without
        /// showing any newly possible bridges before the movement animation has settled.
        /// </summary>
        public void HideBrokenLiquidBridges()
        {
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    CardModel card = GetCard(c, r);
                    if (card == null) continue;

                    CardView view = cardFactory.GetView(card.TileId);
                    view?.cardEdgeRenderer?.HideBrokenLiquidBridges();
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
                    var newTile = Instantiate(tilePrefab, TilesHolder);
                    newTile.gameObject.SetActive(true);
                    newTile.Init(c, r, this);
                    newTile.transform.position = GetWorldPosition(c, r);
                    _tiles[c, r] = newTile;
                }
            }
        }

        private void ResolveHierarchyHolders()
        {
            if (Tiles == null)
                Tiles = transform.Find("TilesHolder");

            if (queue == null)
                queue = transform.Find("Queue");

            if (queue == null && queueSystem != null)
                queue = queueSystem.transform;

            if (Mainboard == null)
                Mainboard = transform.Find("Mainboard");

            if (CardPool == null)
                CardPool = transform.Find("CardPool");
        }

        private async UniTask StartupEffectAsync()
        {
            var droppedCards = new List<CardModel>();
            var startupCards = new CardModel[_cols, _rows];

            // Remove every board-bound card from the queue before fitting the camera. The queue
            // bounds now represent only the real waiting cards, so framing does not change after
            // the startup animation completes.
            for (int r = _rows - 1; r >= 0; r--)
            {
                for (int c = 0; c < _cols; c++)
                {
                    if (queueSystem.GetQueueCount(c) == 0) continue;

                    CardModel model = queueSystem.PopCard(c);
                    _tiles[c, r].SetCard(model);
                    startupCards[c, r] = model;
                    droppedCards.Add(model);
                }
            }

            _gameplayControl.FitGameplayCamera();
            ApplyConfiguredIceObstacles();

            // Play the same bottom-to-top drop after the final camera position is established.
            for (int r = _rows - 1; r >= 0; r--)
            {
                for (int c = 0; c < _cols; c++)
                {
                    CardModel model = startupCards[c, r];
                    if (model != null)
                        cardFactory.AnimateDropOnly(model, c, r);
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
                int clearedCardCount = 0;

                if (anyCleared)
                {
                    ReduceIceObstacles(matches.Count);
                    var clearAnimations = new List<UniTask>(matches.Count);
                    var clearedCardBatches = new List<List<CardModel>>(matches.Count);

                    foreach (var group in matches)
                    {
                        var clearedCards = new List<CardModel>(group.Members);
                        clearedCardBatches.Add(clearedCards);
                        clearedCardCount += clearedCards.Count;

                        // Completion presentation owns these cards from this frame onward.
                        // Lock them before any awaited animation so CardView cannot begin a
                        // drag during the brief interval before fade/despawn starts.
                        foreach (CardModel card in clearedCards)
                        {
                            card.CanDrag = false;
                            card.IsAnimating = true;
                        }

                        // Calculate group center for ripple effect
                        Vector2 gridCenter = Vector2.zero;
                        foreach (var tile in clearedCards)
                            gridCenter += new Vector2(tile.BoardCol, tile.BoardRow);

                        if (clearedCards.Count > 0)
                            gridCenter /= clearedCards.Count;

                        // Ripple effect on non-cleared tiles
                        ApplyRippleEffect(group, gridCenter);

                        // Clear tiles from grid
                        foreach (var tile in clearedCards)
                            _tiles[tile.BoardCol, tile.BoardRow].SetCard(null);

                        // Start every completed group's presentation in this frame. Waiting is
                        // deferred until the whole batch has been launched so simultaneous
                        // matches cross-fade and despawn together.
                        clearAnimations.Add(cardFactory.DespawnStampGroupAsync(group));
                    }

                    await UniTask.WhenAll(clearAnimations);

                    foreach (var clearedCards in clearedCardBatches)
                        OnStampCleared?.Invoke(clearedCards);

                    await UniTask.Delay(TimeSpan.FromSeconds(0.1f));
                }

                if (anyCleared)
                {
                    // Apply gravity fully before choosing the next complete generated topic set.
                    await gravitySystem.ApplyGravityAsync();
                    await FillEmptyCellsAsync(clearedCardCount);
                }

            } while (anyCleared);

            OnBoardSettled?.Invoke();
        }

        private void ApplyConfiguredIceObstacles()
        {
            List<IcedCardConfig> iceConfigs = _levelData.icedCards?
                .Where(config => config != null && config.breakCount > 0)
                .ToList();
            if (iceConfigs == null || iceConfigs.Count == 0) return;

            List<CardModel> boardCards = new();
            for (int col = 0; col < _cols; col++)
                for (int row = 0; row < _rows; row++)
                {
                    CardModel card = GetCard(col, row);
                    if (card != null) boardCards.Add(card);
                }

            // Reserve one complete topic so obstacles can never remove the initial solution.
            List<CardModel> reservedSolution = boardCards
                .Where(card => card.HasAssignedContent)
                .GroupBy(card => card.Topic)
                .Where(group => group.Select(card => card.ItemIndex).Distinct().Count() == StampData.RequiredItemCount)
                .OrderBy(_ => UnityEngine.Random.value)
                .Select(group => group.ToList())
                .FirstOrDefault();

            if (reservedSolution == null)
            {
                Debug.LogError("[Gameboard] Iced cards require at least one complete topic on the initial board.");
                return;
            }

            var reservedIds = new HashSet<int>(reservedSolution.Select(card => card.TileId));
            List<CardModel> candidates = boardCards
                .Concat(queueSystem != null ? queueSystem.GetAllCards() : Enumerable.Empty<CardModel>())
                .Where(card => card != null && !reservedIds.Contains(card.TileId))
                .OrderBy(_ => UnityEngine.Random.value)
                .ToList();

            var icedQueueSlots = new HashSet<(int Column, int Index)>();
            int placedObstacleCount = 0;
            foreach (IcedCardConfig config in iceConfigs)
            {
                int candidateIndex = candidates.FindIndex(card =>
                {
                    if (queueSystem == null ||
                        !queueSystem.TryGetCardPosition(card, out int column, out int queueIndex))
                        return true;

                    // A 2x2 clear releases two cards per affected column. Keeping consecutive
                    // queue positions from both being iced guarantees an unfrozen release slot.
                    return !icedQueueSlots.Contains((column, queueIndex - 1)) &&
                           !icedQueueSlots.Contains((column, queueIndex + 1));
                });
                if (candidateIndex < 0) break;

                CardModel card = candidates[candidateIndex];
                candidates.RemoveAt(candidateIndex);
                card.SetIce(config.breakCount);
                cardFactory.RefreshIceVisual(card, false);
                placedObstacleCount++;

                if (queueSystem != null &&
                    queueSystem.TryGetCardPosition(card, out int column, out int queueIndex))
                    icedQueueSlots.Add((column, queueIndex));
            }

            if (placedObstacleCount < iceConfigs.Count)
            {
                Debug.LogWarning(
                    $"[Gameboard] Only {placedObstacleCount} of {iceConfigs.Count} iced cards can be placed " +
                    "while preserving the initial solution and safe queue releases.");
            }
        }

        /// <summary>
        /// Lets an invalid drag visibly return to its logical grid position before CardView's
        /// final safety cleanup force-aligns the card.
        /// </summary>
        private async UniTask AnimateAllTilesToGridPositionsAndWaitAsync(float duration)
        {
            AnimateAllTilesToGridPositions(duration);
            await UniTask.Delay(TimeSpan.FromSeconds(duration));
        }

        private void ReduceIceObstacles(int solvedTopicCount)
        {
            if (solvedTopicCount <= 0) return;

            IEnumerable<CardModel> boardCards = Enumerable.Range(0, _cols)
                .SelectMany(col => Enumerable.Range(0, _rows).Select(row => GetCard(col, row)))
                .Where(card => card != null);
            IEnumerable<CardModel> queueCards = queueSystem != null
                ? queueSystem.GetAllCards()
                : Enumerable.Empty<CardModel>();

            foreach (CardModel card in boardCards.Concat(queueCards).Distinct())
            {
                if (!card.ReduceIce(solvedTopicCount)) continue;
                cardFactory.RefreshIceVisual(card, true);
            }
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

        private async UniTask FillEmptyCellsAsync(int clearedCardCount)
        {
            bool spawnedAny = false;
            var droppedCards = new List<CardModel>();
            int generatedCardsToRelease = int.MaxValue;

            if (!_levelData.useAuthoredLayout)
            {
                var emptyCellsByColumn = new int[_cols];
                for (int c = 0; c < _cols; c++)
                {
                    for (int r = 0; r < _rows; r++)
                        if (!_tiles[c, r].IsOccupied)
                            emptyCellsByColumn[c]++;
                }

                generatedCardsToRelease = queueSystem.PrepareGeneratedRelease(
                    emptyCellsByColumn,
                    clearedCardCount);
            }

            // Both modes use the same forward per-column queue release after gravity.
            for (int c = 0; c < _cols; c++)
            {
                int emptyCount = 0;
                for (int r = _rows - 1; r >= 0; r--)
                {
                    if (!_tiles[c, r].IsOccupied &&
                        queueSystem.GetQueueCount(c) > 0 &&
                        generatedCardsToRelease > 0)
                    {
                        var model = queueSystem.PopCard(c);
                        _tiles[c, r].SetCard(model);
                        cardFactory.AnimateDropAndFlip(model, c, r);
                        droppedCards.Add(model);
                        emptyCount++;
                        generatedCardsToRelease--;
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

            // Do not rebuild connections while a card is still falling. Rebuilding here would
            // enable the liquid bridge at its final grid coordinates before the visual reaches
            // that cell, making the bridge appear detached from the moving card.
            if (spawnedAny)
                await UniTask.WaitUntil(() => droppedCards.All(card => !card.IsAnimating));

            // DOMove has finished, but group parenting changes the transform space. Commit the
            // exact grid coordinates first so no sub-frame tween residue becomes a local offset
            // inside the newly created group.
            SnapAllTilesToGridPositionsImmediate();

            // New liquid bridges may now animate from cards that are visually settled.
            stampDetector.RebuildGroups();
            UpdateAllEdges();
        }

        #endregion

        #region Private — Helpers

        /// <summary>
        /// Waits for the placement tween, compacts every column, then releases waiting cards
        /// into empty cells in their own columns. This settlement path runs even when no topic
        /// was cleared, so moving a board card can immediately make room for its column queue.
        /// </summary>
        private async UniTask SettleAfterPlayerMoveAsync(float placementDuration)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(placementDuration));

            // Treat the logical grid as the source of truth once the placement animation has
            // had time to finish. This also recovers a dragged card whose DOMove was interrupted
            // by group reparenting while it was released over another card.
            SnapAllTilesToGridPositionsImmediate();

            await gravitySystem.ApplyGravityAsync();
            await FillEmptyCellsAsync(int.MaxValue);
            await CheckAndClearAsync();
        }

        #endregion
    }
}

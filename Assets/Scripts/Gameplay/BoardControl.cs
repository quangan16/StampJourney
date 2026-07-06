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
    /// Quản lý toàn bộ trạng thái lưới (grid) của game.
    /// Chịu trách nhiệm: khởi tạo board, swap tile, gravity, và thông báo sự kiện.
    /// </summary>
    public class Gameboard : SerializedMonoBehaviour
    {
        // ---- Inspector ----
        [BoxGroup("References")]
        public StampDetector stampDetector;
        [BoxGroup("References")]
        public GravitySystem gravitySystem;
        [BoxGroup("References")]
        public CardFactory cardFactory;
        // [BoxGroup("References"), Required]
        public Tile tilePrefab;

        [BoxGroup("Settings")]
        [LabelText("Pixels Per Unit")]
        public float pixelsPerUnit = 100f; // Hệ số chuyển đổi pixel -> world unit

        [BoxGroup("References")]
        public QueueSystem queueSystem;

        // ---- Grid Data ----
        /// <summary>Grid[col, row] — chứa TileModel cố định.</summary>
        [ShowInInspector, ReadOnly]
        private Tile[,] tiles;

        private LevelData _levelData;
        private int _cols, _rows;

        // ---- Events ----
        public event Action<CardModel, CardModel> OnSwapCompleted;
        public event Action<List<CardModel>> OnStampCleared;
        public event Action OnBoardSettled;

        // ---- Public API ----

        public int Cols => _cols;
        public int Rows => _rows;

        /// <summary>Khởi tạo board với LevelData. Tự động clear tile cũ nếu có.</summary>
        public void Init(LevelData levelData)
        {
            Debug.Log($"[BoardControl] InitBoard called — cols={levelData.levelConfig.boardCols} rows={levelData.levelConfig.boardRows}");
            _levelData = levelData;
            _cols = levelData.levelConfig.boardCols;
            _rows = levelData.levelConfig.boardRows;
            // Clear old background tiles


            stampDetector.Init(this);
            gravitySystem.Init(this);
            cardFactory.Init(this);
            queueSystem?.Init(this, _levelData);
            Setup();


        }

        public void Setup()
        {
            cardFactory?.DespawnAll();
            SpawnTiles();

            queueSystem?.SetupInitialQueues();
            FillBoardInitial();

            // Rebuild groups + edges sau khi fill xong
            stampDetector?.RebuildGroups();
            UpdateAllEdges();
        }

        public void SpawnTiles()
        {
            if (tiles != null && tiles.Length > 0)
            {
                foreach (var tile in tiles)
                {
                    if (tile != null) Destroy(tile.gameObject);
                }
            }



            tiles = new Tile[_cols, _rows];

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var newTile = Instantiate(tilePrefab, transform);
                    newTile.gameObject.SetActive(true);

                    newTile.Init(c, r, this);
                    newTile.transform.position = GetWorldPosition(c, r);
                    tiles[c, r] = newTile;
                }
            }
        }

        /// <summary>Lấy TileModel tại (col, row). Trả null nếu ngoài bounds.</summary>
        public Tile GetTile(int col, int row)
        {
            if (!IsInBounds(col, row)) return null;
            return tiles[col, row];
        }





        /// <summary>Lấy CardModel tại (col, row). Trả null nếu trống hoặc out-of-bounds.</summary>
        public CardModel GetCard(int col, int row)
        {
            var tile = GetTile(col, row);
            return tile?.Card;
        }

        /// <summary>Thực hiện swap hai tile theo vị trí board. Gọi bởi TileController.</summary>
        public async UniTask TrySwapAsync(int colA, int rowA, int colB, int rowB)
        {
            if (!IsInBounds(colA, rowA) || !IsInBounds(colB, rowB)) return;

            var tileA = GetCard(colA, rowA);
            var tileB = GetCard(colB, rowB);

            if (tileA == null || tileB == null) return;

            // Đánh dấu animating để block input
            tileA.IsAnimating = true;
            tileB.IsAnimating = true;

            // Swap trong data
            SwapInGrid(colA, rowA, colB, rowB);

            // Thông báo để View animate
            OnSwapCompleted?.Invoke(tileA, tileB);

            // Chờ animation swap xong (~0.25s)
            // await UniTask.Delay(TimeSpan.FromSeconds(0.3f));

            tileA.IsAnimating = false;
            tileB.IsAnimating = false;

            // Rebuild groups sau swap
            stampDetector.RebuildGroups();
            UpdateAllEdges();

            // Kiểm tra stamp sau swap
            await CheckAndClearAsync();
        }

        /// <summary>
        /// Swap cả group theo delta. Gọi bởi CardView khi drag group.
        /// </summary>
        public async UniTask TrySwapGroupAsync(CardGroup group, int deltaCol, int deltaRow)
        {
            if (group == null) return;

            // ⚠️ Cache members TRƯỚC khi RebuildGroups — vì Rebuild sẽ Disband group cũ
            var cachedMembers = new List<CardModel>(group.Members);

            // Mark tất cả members animating
            foreach (var member in cachedMembers)
                member.IsAnimating = true;

            bool success = stampDetector.TrySwapGroup(group, deltaCol, deltaRow);

            if (success)
            {
                // Animate tất cả tiles về vị trí mới
                AnimateAllTilesToGridPositions(0.25f);

                // await UniTask.Delay(TimeSpan.FromSeconds(0.3f));

                // Rebuild groups sau swap (sẽ tạo parent objects mới)
                stampDetector.RebuildGroups();
                UpdateAllEdges();

                // Kiểm tra stamp
                await CheckAndClearAsync();
            }
            else
            {
                // Snap back tất cả members
                AnimateAllTilesToGridPositions(0.18f);
                // await UniTask.Delay(TimeSpan.FromSeconds(0.2f));

                // Rebuild lại groups
                stampDetector.RebuildGroups();
                UpdateAllEdges();
            }

            // Unmark animating — dùng cachedMembers vì group.Members đã bị clear
            foreach (var member in cachedMembers)
                member.IsAnimating = false;
        }

        /// <summary>
        /// Vị trí transform.position của tâm ô (col, row) trong world space.
        /// Tính toán dựa trên pixel settings chia cho PixelsPerUnit.
        /// </summary>
        public Vector2 GetWorldPosition(int col, int row)
        {
            var config = GameManager.Instance.GameConfig;
            var cardWidth = config.cardWidth;
            var cardHeight = config.cardHeight;
            var cardGap = config.cardGap;
            float strideX = (cardWidth + cardGap);
            float strideY = (cardHeight + cardGap);

            float boardWidth = (_cols * (cardWidth + cardGap));
            float boardHeight = (_rows * (cardHeight + cardGap));

            // Lấy vị trí trung tâm của gameboard GameObject làm mốc (thường là 0,0,0)
            Vector2 boardCenter = transform.position;

            float startX = boardCenter.x - boardWidth / 2f + (cardWidth) / 2f;
            float startY = boardCenter.y + boardHeight / 2f - (cardHeight) / 2f;

            return new Vector2(
                startX + col * strideX,
                startY - row * strideY
            );
        }

        // ---- Internal ----

        private void FillBoardInitial()
        {
            if (_levelData.levelConfig.fillStrategy == FillStrategy.BalancedDistribution)
                FillBalanced();
            else
                FillRandom();
        }

        private void FillRandom()
        {
            var stamps = _levelData.levelConfig.stamps;
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                {
                    var stamp = stamps[UnityEngine.Random.Range(0, stamps.Length)];
                    int pc = UnityEngine.Random.Range(0, stamp.cols);
                    int pr = UnityEngine.Random.Range(0, stamp.rows);
                    var model = new CardModel(stamp, pc, pr);
                    tiles[c, r].SetCard(model);
                    cardFactory.SpawnCard(model);
                }
        }

        private void FillBalanced()
        {
            // Tạo pool đảm bảo mỗi loại stamp xuất hiện đủ mảnh
            var pool = new List<(StampData stamp, int pc, int pr)>();
            var stamps = _levelData.levelConfig.stamps;
            int total = _cols * _rows;

            while (pool.Count < total)
            {
                foreach (var stamp in stamps)
                    for (int pr = 0; pr < stamp.rows; pr++)
                        for (int pc = 0; pc < stamp.cols; pc++)
                            pool.Add((stamp, pc, pr));
            }

            // Shuffle
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                {
                    int idx = c + r * _cols;
                    if (idx >= pool.Count) break;
                    var (stamp, pc, pr) = pool[idx];
                    var model = new CardModel(stamp, pc, pr);
                    tiles[c, r].SetCard(model);
                    cardFactory.SpawnCard(model);
                }
        }

        private async UniTask CheckAndClearAsync()
        {
            bool anyCleared;
            do
            {
                var matches = stampDetector.FindCompletedStamps(tiles, _cols, _rows);
                anyCleared = matches.Count > 0;

                if (anyCleared)
                {
                    // Xóa tất cả matches
                    foreach (var group in matches)
                    {
                        foreach (var tile in group)
                        {
                            tiles[tile.BoardCol, tile.BoardRow].SetCard(null);
                            cardFactory.DespawnTile(tile);
                        }
                        OnStampCleared?.Invoke(group);
                    }

                    // Chờ animation clear (~0.5s)
                    // await UniTask.Delay(TimeSpan.FromSeconds(0.5f));

                    // Gravity
                    await gravitySystem.ApplyGravityAsync();

                    // Fill new tiles từ trên
                    await FillEmptyCellsAsync();

                    await UniTask.Delay(TimeSpan.FromSeconds(0.2f));
                }
            } while (anyCleared);

            OnBoardSettled?.Invoke();
        }

        private async UniTask FillEmptyCellsAsync()
        {
            bool spawnedAny = false;
            for (int c = 0; c < _cols; c++)
            {
                int emptyCount = 0;
                for (int r = 0; r < _rows; r++)
                {
                    if (!tiles[c, r].IsOccupied && queueSystem.GetQueueCount(c) > 0)
                    {
                        var model = queueSystem.PopCard(c);

                        tiles[c, r].SetCard(model);
                        cardFactory.AnimateDropAndFlip(model, c, r);

                        emptyCount++;
                        spawnedAny = true;
                    }
                }

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

            // Rebuild groups cho tile mới spawn
            stampDetector.RebuildGroups();
            UpdateAllEdges();
        }

        // ---- Helpers ----

        private void SwapInGrid(int colA, int rowA, int colB, int rowB)
        {
            var cardA = GetCard(colA, rowA);
            var cardB = GetCard(colB, rowB);

            tiles[colA, rowA].SetCard(cardB);
            tiles[colB, rowB].SetCard(cardA);
        }

        public bool IsInBounds(int col, int row) =>
            col >= 0 && col < _cols && row >= 0 && row < _rows;

        /// <summary>Gán card vào grid (dùng bởi GravitySystem).</summary>
        public void SetTile(int col, int row, CardModel card)
        {
            if (!IsInBounds(col, row)) return;
            tiles[col, row].SetCard(card);
        }

        /// <summary>Kiểm tra ô có trống không.</summary>
        public bool IsEmpty(int col, int row) =>
            IsInBounds(col, row) && !tiles[col, row].IsOccupied;

        /// <summary>
        /// Cập nhật edges cho tất cả active tiles trên board.
        /// Gọi sau mỗi lần group thay đổi.
        /// </summary>
        public void UpdateAllEdges()
        {
            for (int c = 0; c < _cols; c++)
                for (int r = 0; r < _rows; r++)
                {
                    var card = GetCard(c, r);
                    if (card == null) continue;
                    var view = cardFactory.GetView(card.TileId);
                    if (view == null) continue;
                    view.cardEdgeRenderer?.UpdateEdges();
                }
        }

        /// <summary>
        /// Animate tất cả tiles về đúng vị trí grid của chúng.
        /// Dùng sau group swap khi nhiều tile bị dịch chuyển.
        /// </summary>
        public void AnimateAllTilesToGridPositions(float duration)
        {
            for (int c = 0; c < _cols; c++)
                for (int r = 0; r < _rows; r++)
                {
                    var card = GetCard(c, r);
                    if (card == null) continue;
                    var view = cardFactory.GetView(card.TileId);
                    if (view == null) continue;
                    var targetPos = GetWorldPosition(c, r);
                    view.transform.DOMove(targetPos, duration).SetEase(Ease.OutCubic);
                }
        }

        public bool IsBoardAndQueuesEmpty()
        {
            if (tiles == null) return false;
            if (queueSystem != null && !queueSystem.IsAllQueuesEmpty()) return false;
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    if (tiles[c, r].IsOccupied) return false;
                }
            }
            return true;
        }
    }
}

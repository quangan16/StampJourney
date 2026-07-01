using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Core
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

        [BoxGroup("Settings")]
        [LabelText("Tile Size (pixels)")]
        public float tileSize = 100f;   // Đơn vị: pixels trong Canvas (không phải world units)

        [BoxGroup("Settings")]
        [LabelText("Tile Gap (pixels)")]
        public float tileGap = 4f;      // Khoảng cách giữa các tile

        // ---- Grid Data ----
        /// <summary>Grid[col, row] — null nếu ô trống.</summary>
        [ShowInInspector, ReadOnly]
        private CardModel[,] _grid;

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
            Debug.Log($"[BoardControl] InitBoard called — cols={levelData.boardCols} rows={levelData.boardRows}");
            _levelData = levelData;
            _cols = levelData.boardCols;
            _rows = levelData.boardRows;
            _grid = new CardModel[_cols, _rows];

            stampDetector.Init(this);
            gravitySystem.Init(this);
            cardFactory.Init(this);
            Setup();
        }

        public void Setup()
        {
            cardFactory.DespawnAll();
            FillBoardInitial();
        }

        /// <summary>Lấy TileModel tại (col, row). Trả null nếu trống hoặc out-of-bounds.</summary>
        public CardModel GetTile(int col, int row)
        {
            if (!IsInBounds(col, row)) return null;
            return _grid[col, row];
        }

        /// <summary>Thực hiện swap hai tile theo vị trí board. Gọi bởi TileController.</summary>
        public async UniTask TrySwapAsync(int colA, int rowA, int colB, int rowB)
        {
            if (!IsInBounds(colA, rowA) || !IsInBounds(colB, rowB)) return;

            var tileA = _grid[colA, rowA];
            var tileB = _grid[colB, rowB];

            if (tileA == null || tileB == null) return;

            // Đánh dấu animating để block input
            tileA.IsAnimating = true;
            tileB.IsAnimating = true;

            // Swap trong data
            SwapInGrid(colA, rowA, colB, rowB);

            // Thông báo để View animate
            OnSwapCompleted?.Invoke(tileA, tileB);

            // Chờ animation swap xong (~0.25s)
            await UniTask.Delay(TimeSpan.FromSeconds(0.3f));

            tileA.IsAnimating = false;
            tileB.IsAnimating = false;

            // Kiểm tra stamp sau swap
            await CheckAndClearAsync();
        }

        /// <summary>
        /// Vị trí anchoredPosition của tâm ô (col, row) trong boardContainer.
        /// Điểm gốc (0,0) là tâm của boardContainer, tự động căn giữa.
        /// </summary>
        public Vector2 GetWorldPosition(int col, int row)
        {
            // Tính stride = size + gap
            float stride = tileSize + tileGap;

            // Tổng chiều rộng và cao của board
            float boardWidth = _cols * stride - tileGap;
            float boardHeight = _rows * stride - tileGap;

            // Điểm bắt đầu (góc trên-trái) relative to boardContainer center
            float startX = -boardWidth / 2f + tileSize / 2f;
            float startY = boardHeight / 2f - tileSize / 2f;

            return new Vector2(
                startX + col * stride,
                startY - row * stride  // row 0 = trên cùng, nên trừ xuống
            );
        }

        /// <summary>Chuyển anchoredPosition → chỉ số grid. Trả (-1,-1) nếu ngoài bounds.</summary>
        public Vector2Int WorldToGrid(Vector2 anchoredPos)
        {
            float stride = tileSize + tileGap;
            float boardWidth = _cols * stride - tileGap;
            float boardHeight = _rows * stride - tileGap;
            float startX = -boardWidth / 2f + tileSize / 2f;
            float startY = boardHeight / 2f - tileSize / 2f;

            int col = Mathf.RoundToInt((anchoredPos.x - startX) / stride);
            int row = Mathf.RoundToInt((startY - anchoredPos.y) / stride);

            if (!IsInBounds(col, row)) return new Vector2Int(-1, -1);
            return new Vector2Int(col, row);
        }

        // ---- Internal ----

        private void FillBoardInitial()
        {
            if (_levelData.fillStrategy == FillStrategy.BalancedDistribution)
                FillBalanced();
            else
                FillRandom();
        }

        private void FillRandom()
        {
            var stamps = _levelData.stamps;
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                {
                    var stamp = stamps[UnityEngine.Random.Range(0, stamps.Length)];
                    int pc = UnityEngine.Random.Range(0, stamp.cols);
                    int pr = UnityEngine.Random.Range(0, stamp.rows);
                    var model = new CardModel(stamp, pc, pr, c, r);
                    _grid[c, r] = model;
                    cardFactory.SpawnTile(model);
                }
        }

        private void FillBalanced()
        {
            // Tạo pool đảm bảo mỗi loại stamp xuất hiện đủ mảnh
            var pool = new List<(StampData stamp, int pc, int pr)>();
            var stamps = _levelData.stamps;
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
                    var model = new CardModel(stamp, pc, pr, c, r);
                    _grid[c, r] = model;
                    cardFactory.SpawnTile(model);
                }
        }

        private async UniTask CheckAndClearAsync()
        {
            bool anyCleared;
            do
            {
                var matches = stampDetector.FindCompletedStamps(_grid, _cols, _rows);
                anyCleared = matches.Count > 0;

                if (anyCleared)
                {
                    // Xóa tất cả matches
                    foreach (var group in matches)
                    {
                        foreach (var tile in group)
                        {
                            _grid[tile.BoardCol, tile.BoardRow] = null;
                            cardFactory.DespawnTile(tile);
                        }
                        OnStampCleared?.Invoke(group);
                    }

                    // Chờ animation clear (~0.5s)
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f));

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
            var stamps = _levelData.stamps;
            for (int c = 0; c < _cols; c++)
                for (int r = 0; r < _rows; r++)
                {
                    if (_grid[c, r] == null)
                    {
                        var stamp = stamps[UnityEngine.Random.Range(0, stamps.Length)];
                        int pc = UnityEngine.Random.Range(0, stamp.cols);
                        int pr = UnityEngine.Random.Range(0, stamp.rows);
                        var model = new CardModel(stamp, pc, pr, c, r);
                        _grid[c, r] = model;
                        cardFactory.SpawnTileFromAbove(model);
                    }
                }
            await UniTask.Delay(TimeSpan.FromSeconds(0.3f));
        }

        // ---- Helpers ----

        private void SwapInGrid(int colA, int rowA, int colB, int rowB)
        {
            var tileA = _grid[colA, rowA];
            var tileB = _grid[colB, rowB];

            _grid[colA, rowA] = tileB;
            _grid[colB, rowB] = tileA;

            if (tileA != null) { tileA.BoardCol = colB; tileA.BoardRow = rowB; }
            if (tileB != null) { tileB.BoardCol = colA; tileB.BoardRow = rowA; }
        }

        public bool IsInBounds(int col, int row) =>
            col >= 0 && col < _cols && row >= 0 && row < _rows;

        /// <summary>Gán tile vào grid (dùng bởi GravitySystem).</summary>
        public void SetTile(int col, int row, CardModel tile)
        {
            if (!IsInBounds(col, row)) return;
            _grid[col, row] = tile;
            if (tile != null)
            {
                tile.BoardCol = col;
                tile.BoardRow = row;
            }
        }

        /// <summary>Kiểm tra ô có trống không.</summary>
        public bool IsEmpty(int col, int row) =>
            IsInBounds(col, row) && _grid[col, row] == null;

        /// <summary>Board settled sau chain reaction → kiểm tra win/lose.</summary>
        // private void HandleBoardSettled()
        // {
        //     if (GameManager.Instance.State != GameState.Playing) return;

        //     var levelData = allLevels[_currentLevelIndex];


        //     // Kiểm tra lose (hết moves)

        //     if (levelData.maxMoves > 0 && _remainingMoves <= 0)
        //     {
        //         TriggerLose();
        //     }
        // }
    }
}

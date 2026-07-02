using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Tile;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Factory: tạo, hủy và điều khiển GameObject của tile.
    /// Duy trì pool TileView để tối ưu hiệu năng.
    /// Tiles được spawn trực tiếp vào Gameboard (World Space).
    /// </summary>
    public class CardFactory : SerializedMonoBehaviour
    {
        Gameboard gameboard;

        [BoxGroup("References")]
        [Required] public CardView tilePrefab;





        [BoxGroup("Settings")]
        [LabelText("Spawn Above Offset (rows above board)")]
        public float spawnAboveOffset = 3f;

        [BoxGroup("Settings")]
        public float dropEaseTime = 0.35f;

        // ---- Pool ----
        private readonly Dictionary<int, CardView> _activeCards = new();
        private readonly Queue<CardView> _pool = new();

        // ---- Public API ----

        public void Init(Gameboard gameboard)
        {
            this.gameboard = gameboard;
        }

        /// <summary>Spawn tile ngay tại vị trí board của nó.</summary>
        public void SpawnCard(CardModel model)
        {
            var view = GetFromPool();

            Vector2 worldPos = gameboard.GetWorldPosition(model.BoardCol, model.BoardRow);
            view.transform.position = worldPos;

            view.Init(model, gameboard);
            _activeCards[model.TileId] = view;
        }

        /// <summary>Spawn tile từ phía trên board, rơi xuống vị trí target.</summary>
        public void SpawnTileFromAbove(CardModel model)
        {

            var view = GetFromPool();
            Vector2 targetPos = gameboard.GetWorldPosition(model.BoardCol, model.BoardRow);

            // Tính toán offset dựa trên pixelsPerUnit

            float offsetInUnits = (spawnAboveOffset * GameManager.Instance.GameConfig.cardHeight);
            Vector2 spawnPos = targetPos + Vector2.up * offsetInUnits;

            view.transform.position = spawnPos;
            view.Init(model, gameboard);
            _activeCards[model.TileId] = view;

            float duration = dropEaseTime + model.BoardRow * 0.03f;
            view.transform.DOMove(targetPos, duration).SetEase(Ease.InBounce);
        }

        /// <summary>Hủy tile (trả về pool) và play animation clear.</summary>
        public void DespawnTile(CardModel model)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            _activeCards.Remove(model.TileId);

            var seq = DOTween.Sequence();
            seq.Append(view.transform.DOScale(1.3f, 0.2f).SetEase(Ease.OutBack));
            seq.Append(view.transform.DOScale(0f, 0.2f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                view.transform.localScale = Vector3.one;
                ReturnToPool(view);
            });
        }

        /// <summary>
        /// Hủy TOÀN BỘ tile đang active — không có animation.
        /// Gọi khi restart/load level mới để tránh tile cũ bị giữ lại.
        /// </summary>
        public void DespawnAll()
        {
            int count = _activeCards.Count;

            // Kill tất cả tweens đang chạy trên các tiles
            DOTween.KillAll();

            // Trả toàn bộ tile về pool ngay lập tức
            foreach (var view in _activeCards.Values)
            {
                view.transform.localScale = Vector3.one;
                ReturnToPool(view);
            }
            _activeCards.Clear();

            Debug.Log($"[TileFactory] DespawnAll — cleared {count} tiles. Pool size: {_pool.Count}");
        }


        /// <summary>Animate tile rơi xuống vị trí mới (gọi bởi GravitySystem).</summary>
        public void AnimateTileDrop(CardModel model, Vector2 targetWorldPos, float duration)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            view.transform.DOMove(targetWorldPos, duration).SetEase(Ease.InQuad);
        }

        /// <summary>Lấy TileView theo TileId.</summary>
        public CardView GetView(int tileId) =>
            _activeCards.TryGetValue(tileId, out var v) ? v : null;

        // ---- Pool helpers ----

        private CardView GetFromPool()
        {
            if (_pool.Count > 0)
            {
                var v = _pool.Dequeue();
                v.gameObject.SetActive(true);
                v.transform.SetParent(gameboard.transform, false);
                return v;
            }
            // Spawn vào Gameboard (World Space)
            return Instantiate(tilePrefab, gameboard.transform);
        }

        private void ReturnToPool(CardView view)
        {
            view.gameObject.SetActive(false);
            _pool.Enqueue(view);
        }
    }
}

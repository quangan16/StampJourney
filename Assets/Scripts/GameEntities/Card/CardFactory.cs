using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using StampJourney.Gameplay;
using UnityEngine;

namespace StampJourney.Card
{
    /// <summary>
    /// Factory: tạo, hủy và điều khiển GameObject của tile.
    /// Duy trì pool TileView để tối ưu hiệu năng.
    /// Tiles được spawn trực tiếp vào Gameboard (World Space).
    /// </summary>
    public class CardFactory : MonoBehaviour
    {
        Gameboard gameboard;

        [BoxGroup("References")]
        public CardView cardPrefab;

        [BoxGroup("References")]
        public GameObject clearEffectPrefab;

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

        public void SpawnCard(CardModel model)
        {
            model.CanDrag = true;
            var view = GetFromPool();

            Vector2 worldPos = gameboard.GetWorldPosition(model.BoardCol, model.BoardRow);
            view.transform.position = worldPos;

            view.Init(model, gameboard);
            view.PlayFlip(FlipState.Up, true);
            _activeCards[model.TileId] = view;
        }

        public void SpawnCardInQueue(CardModel model, int col, int queueIndex)
        {
            model.CanDrag = false;
            var view = GetFromPool();
            Vector2 worldPos = gameboard.queueSystem.GetQueueWorldPosition(col, queueIndex);
            view.transform.position = worldPos;

            view.Init(model, gameboard);
            view.PlayFlip(FlipState.Down, true);
            view.SetSortingOrder(queueIndex); // Càng ở trên queue (index cao) thì order càng lớn để đè lên lá ở dưới
            _activeCards[model.TileId] = view;
        }

        public void AnimateDropAndFlip(CardModel model, int col, int row)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            Vector2 targetPos = gameboard.GetWorldPosition(col, row);

            model.IsAnimating = true;
            float duration = dropEaseTime + row * 0.03f;
            view.transform.DOMove(targetPos, duration).SetEase(Ease.InQuad)
                .OnComplete(() =>
                {
                    model.CanDrag = true;
                    model.IsAnimating = false;
                    view.SetSortingOrder(0); // Reset order sau khi hạ cánh
                });

            // Lên lịch flip animation ở giữa chặng rơi
            DOVirtual.DelayedCall(duration * 0.4f, () =>
            {
                if (view != null) view.PlayFlip(FlipState.Up, false);
            });
        }

        public void AnimateDropOnly(CardModel model, int col, int row)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            Vector2 targetPos = gameboard.GetWorldPosition(col, row);

            model.IsAnimating = true;
            float duration = dropEaseTime + row * 0.03f;
            view.transform.DOMove(targetPos, duration).SetEase(Ease.InQuad)
                .OnComplete(() =>
                {
                    model.IsAnimating = false;
                    view.SetSortingOrder(0);
                });
        }

        public void AnimateQueueShift(CardModel model, int col, int queueIndex)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            Vector2 targetPos = gameboard.queueSystem.GetQueueWorldPosition(col, queueIndex);

            // Cập nhật lại Order để lá bài tụt xuống dưới sẽ bị đè bởi lá ở trên
            view.SetSortingOrder(queueIndex);

            view.transform.DOMove(targetPos, dropEaseTime).SetEase(Ease.OutQuad);
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

        public async UniTask DespawnStampGroupAsync(CardGroup group)
        {
            if (clearEffectPrefab != null)
            {
                // Instantiate effect exactly at the group's center
                var effect = Instantiate(clearEffectPrefab, group.transform.position, Quaternion.identity);
                Destroy(effect, 2f);
            }

            // Đảm bảo group transform ở đúng tâm và không bị dính tween rác
            group.transform.DOKill(true);
            group.transform.localScale = Vector3.one;

            var views = new List<CardView>();
            foreach (var tile in group.Members)
            {
                if (_activeCards.TryGetValue(tile.TileId, out var view))
                {
                    _activeCards.Remove(tile.TileId);
                    views.Add(view);
                    // BẮT BUỘC phải parent để view scale chung với group
                    view.transform.SetParent(group.transform, true);
                    view.transform.localScale = Vector3.one;
                    view.transform.localRotation = Quaternion.identity;
                }
            }

            var seq = DOTween.Sequence();
            seq.Append(group.transform.DOScale(1.2f, 0.15f).SetEase(Ease.OutQuad));
            seq.Append(group.transform.DOScale(0f, 0.2f).SetEase(Ease.InQuad));

            // Đợi animation hoàn thành
            await seq.AsyncWaitForCompletion();

            // Dọn dẹp
            foreach (var view in views)
            {
                view.transform.SetParent(transform, true);
                ReturnToPool(view);
            }
            if (group != null && group.gameObject != null)
            {
                Destroy(group.gameObject);
            }
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

            model.IsAnimating = true;
            view.transform.DOMove(targetWorldPos, duration).SetEase(Ease.InQuad)
                .OnComplete(() => model.IsAnimating = false);
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
                v.transform.SetParent(gameboard.transform, true);
                return v;
            }

            return Instantiate(cardPrefab, gameboard.transform);
        }

        private void ReturnToPool(CardView view)
        {
            view.gameObject.SetActive(false);
            _pool.Enqueue(view);
        }
    }
}

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
    /// Factory for creating, destroying, and animating card GameObjects.
    /// Maintains an object pool for CardViews to optimize performance.
    /// Cards are spawned directly into the Gameboard (World Space).
    /// </summary>
    public class CardFactory : MonoBehaviour
    {
        #region Inspector

        [BoxGroup("References")]
        public CardView cardPrefab;

        [BoxGroup("References")]
        public GameObject clearEffectPrefab;

        [BoxGroup("Settings")]
        [LabelText("Spawn Above Offset (rows above board)")]
        public float spawnAboveOffset = 3f;

        [BoxGroup("Settings")]
        public float dropEaseTime = 0.35f;

        [BoxGroup("Settings")]
        [LabelText("Drop Sorting Order")]
        public int dropSortingOrder = 50;

        [BoxGroup("Topic Complete")]
        [LabelText("Background Cross Fade")]
        public float completeBackgroundFadeDuration = 0.25f;

        [BoxGroup("Topic Complete")]
        [LabelText("Completed Background Hold")]
        public float completeBackgroundHoldDuration = 0.15f;

        [BoxGroup("Topic Complete")]
        [LabelText("Completion Sorting Order")]
        [Tooltip("Raised above normal, falling, and dragged cards while a completed topic is presented.")]
        public int completeSortingOrder = 200;

        #endregion

        #region Pool

        private Gameboard _gameboard;
        private readonly Dictionary<int, CardView> _activeCards = new();
        private readonly Queue<CardView> _pool = new();

        #endregion

        #region Initialization

        public void Init(Gameboard gameboard)
        {
            _gameboard = gameboard;
        }

        #endregion

        #region Spawn

        public void SpawnCard(CardModel model)
        {
            model.CanDrag = true;
            var view = GetFromPool();

            Vector2 worldPos = _gameboard.GetWorldPosition(model.BoardCol, model.BoardRow);
            view.transform.position = worldPos;

            view.Init(model, _gameboard);
            view.PlayFlip(FlipState.Up, true);
            _activeCards[model.TileId] = view;
        }

        public void SpawnCardInQueue(CardModel model, int col, int queueIndex)
        {
            model.CanDrag = false;
            var view = GetFromPool();
            Vector2 worldPos = _gameboard.queueSystem.GetQueueWorldPosition(col, queueIndex);
            view.transform.position = worldPos;

            view.Init(model, _gameboard);
            view.PlayFlip(FlipState.Down, true);
            // Higher queue index → higher sorting order so upper cards overlay lower ones
            view.SetSortingOrder(queueIndex);
            _activeCards[model.TileId] = view;
        }

        #endregion

        #region Drop Animations

        public void AnimateDropAndFlip(CardModel model, int col, int row)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            Vector2 targetPos = _gameboard.GetWorldPosition(col, row);

            model.IsAnimating = true;
            float duration = dropEaseTime + row * 0.03f;
            view.SetSortingOrder(dropSortingOrder);

            view.transform.DOMove(targetPos, duration).SetEase(Ease.InQuad)
                .OnComplete(() =>
                {
                    model.CanDrag = true;
                    model.IsAnimating = false;
                    view.SetSortingOrder(view.baseSortingOrder);
                });

            // Schedule flip animation mid-drop
            DOVirtual.DelayedCall(duration * 0.4f, () =>
            {
                if (view != null) view.PlayFlip(FlipState.Up, false);
            });
        }

        public void AnimateDropOnly(CardModel model, int col, int row)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            Vector2 targetPos = _gameboard.GetWorldPosition(col, row);

            model.IsAnimating = true;
            float duration = dropEaseTime + row * 0.03f;
            view.SetSortingOrder(dropSortingOrder);

            view.transform.DOMove(targetPos, duration).SetEase(Ease.InQuad)
                .OnComplete(() =>
                {
                    model.IsAnimating = false;
                    view.SetSortingOrder(view.baseSortingOrder);
                });
        }

        public void AnimateQueueShift(CardModel model, int col, int queueIndex)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            Vector2 targetPos = _gameboard.queueSystem.GetQueueWorldPosition(col, queueIndex);

            view.SetSortingOrder(queueIndex);
            view.transform.DOMove(targetPos, dropEaseTime).SetEase(Ease.OutQuad);
        }

        /// <summary>Reveals the content chosen for a generated face-down queue card.</summary>
        public void AssignGeneratedContent(CardModel model, StampJourney.Data.StampData topic, int itemIndex)
        {
            model.AssignContent(topic, itemIndex);
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            view.RefreshVisual();
        }

        /// <summary>Animates a tile dropping to a new position (called by GravitySystem).</summary>
        public void AnimateTileDrop(CardModel model, Vector2 targetWorldPos, float duration)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;

            model.IsAnimating = true;
            view.SetSortingOrder(dropSortingOrder);
            view.transform.DOMove(targetWorldPos, duration).SetEase(Ease.InOutBack)
                .OnComplete(() =>
                {
                    model.IsAnimating = false;
                    view.SetSortingOrder(view.baseSortingOrder);
                });
        }

        #endregion

        #region Despawn

        /// <summary>Despawns a single tile with a scale animation.</summary>
        public void DespawnTile(CardModel model)
        {
            if (!_activeCards.TryGetValue(model.TileId, out var view)) return;
            _activeCards.Remove(model.TileId);

            var seq = DOTween.Sequence();
            seq.Append(view.transform.DOScale(1.1f, 0.2f).SetEase(Ease.OutBack));
            seq.Append(view.transform.DOScale(0f, 0.2f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                view.transform.localScale = Vector3.one;
                ReturnToPool(view);
            });
        }

        /// <summary>Despawns an entire stamp group with a combined scale-down animation.</summary>
        public async UniTask DespawnStampGroupAsync(CardGroup group)
        {
            if (clearEffectPrefab != null)
            {
                var effect = Instantiate(clearEffectPrefab, group.transform.position, Quaternion.identity);
                Destroy(effect, 2f);
            }

            group.transform.DOKill(true);
            group.transform.localScale = Vector3.one;

            var views = new List<CardView>();
            foreach (var tile in group.Members)
            {
                if (_activeCards.TryGetValue(tile.TileId, out var view))
                {
                    _activeCards.Remove(tile.TileId);
                    views.Add(view);
                    // Must parent so view scales with the group
                    view.transform.SetParent(group.transform, true);
                    view.transform.localScale = Vector3.one;
                    view.transform.localRotation = Quaternion.identity;
                }
            }

            // Raise each card independently; the movement-only group parent intentionally has no
            // SortingGroup. The combined background sits one order below the raised item cards.
            foreach (CardView view in views)
                view.SetSortingOrder(completeSortingOrder);

            SpriteRenderer completedBackground = CreateCompletedGroupBackground(
                group,
                views,
                completeSortingOrder - 1);
            float backgroundFadeDuration = Mathf.Max(0f, completeBackgroundFadeDuration);
            var backgroundTransition = DOTween.Sequence();

            foreach (CardView view in views)
            {
                view.cardEdgeRenderer?.DisableAllLiquidBridgesImmediate();
                if (view.backGroundImg == null) continue;

                view.backGroundImg.DOKill();
                backgroundTransition.Join(
                    view.backGroundImg.DOFade(0f, backgroundFadeDuration).SetEase(Ease.Linear));
            }

            if (completedBackground != null)
            {
                Color visibleColor = completedBackground.color;
                visibleColor.a = 1f;
                completedBackground.color = new Color(
                    visibleColor.r,
                    visibleColor.g,
                    visibleColor.b,
                    0f);
                backgroundTransition.Join(
                    completedBackground.DOColor(visibleColor, backgroundFadeDuration)
                        .SetEase(Ease.InOutSine));
            }

            // await backgroundTransition.AsyncWaitForCompletion();



            var seq = DOTween.Sequence();
            seq.Append(group.transform.DOScale(1.15f, 0.2f).SetEase(Ease.OutBack));
            // seq.Append(group.transform.DOShakeRotation(0.5f, 20f, 3, 0, true));
            seq.AppendInterval(completeBackgroundHoldDuration);
            seq.Append(group.transform.DOScale(0f, 0.3f).SetEase(Ease.InBack));
            await seq.AsyncWaitForCompletion();

            // Clean up
            foreach (var view in views)
            {
                view.transform.SetParent(transform, true);
                ReturnToPool(view);
            }

            if (group != null && group.gameObject != null)
                Destroy(group.gameObject);
        }

        /// <summary>
        /// Creates one sliced world-space background covering the completed 2x2 topic while
        /// leaving the four authored item pictures visible above it.
        /// </summary>
        private static SpriteRenderer CreateCompletedGroupBackground(
            CardGroup group,
            IReadOnlyList<CardView> views,
            int sortingOrder)
        {
            if (group == null || views == null) return null;

            CardView sourceView = null;
            foreach (CardView view in views)
            {
                if (view != null && view.backGroundImg != null)
                {
                    sourceView = view;
                    break;
                }
            }

            if (sourceView == null || sourceView.backGroundImg.sprite == null) return null;

            var imageObject = new GameObject("Completed Topic Background");
            imageObject.layer = sourceView.gameObject.layer;
            imageObject.transform.SetParent(group.transform, false);
            imageObject.transform.localPosition = Vector3.zero;

            SpriteRenderer image = imageObject.AddComponent<SpriteRenderer>();
            image.sprite = sourceView.backGroundImg.sprite;
            image.sharedMaterial = sourceView.backGroundImg.sharedMaterial;
            image.color = sourceView.GetLinkStateColor(4);
            image.sortingLayerID = sourceView.backGroundImg.sortingLayerID;
            image.sortingOrder = sortingOrder;
            image.drawMode = SpriteDrawMode.Sliced;

            var config = GameManager.Instance.GameConfig;
            image.size = new Vector2(
                config.cardWidth * 2f + config.cardGap,
                config.cardHeight * 2f + config.cardGap);

            return image;
        }

        /// <summary>
        /// Immediately despawns ALL active tiles without animation.
        /// Called on restart/level load to prevent stale tiles.
        /// WARNING: DOTween.KillAll() affects all tweens globally.
        /// </summary>
        public void DespawnAll()
        {
            DOTween.KillAll();

            foreach (var view in _activeCards.Values)
            {
                view.transform.localScale = Vector3.one;
                ReturnToPool(view);
            }
            _activeCards.Clear();

            Debug.Log($"[CardFactory] DespawnAll — Pool size: {_pool.Count}");
        }

        #endregion

        #region View Access

        /// <summary>Gets the CardView for a given TileId.</summary>
        public CardView GetView(int tileId) =>
            _activeCards.TryGetValue(tileId, out var v) ? v : null;

        #endregion

        #region Pool Helpers

        private CardView GetFromPool()
        {
            if (_pool.Count > 0)
            {
                var v = _pool.Dequeue();
                v.gameObject.SetActive(true);
                v.transform.SetParent(_gameboard.transform, true);
                return v;
            }

            return Instantiate(cardPrefab, _gameboard.transform);
        }

        private void ReturnToPool(CardView view)
        {
            view.gameObject.SetActive(false);
            _pool.Enqueue(view);
        }

        #endregion
    }
}

using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using StampJourney.Card;
using StampJourney.Core;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Gameplay.Boosters
{
    [CreateAssetMenu(fileName = "AutoMergeBooster", menuName = "Stamp Journey/Boosters/Auto Merge")]
    public sealed class AutoMergeBoosterAction : BoosterAction
    {
        public override bool CanExecute(BoosterContext context, out string reason)
        {
            Gameboard board = context.Board;
            if (board == null)
            {
                reason = "The board is not available.";
                return false;
            }
            if (board.HasAnimatingCards())
            {
                reason = "Wait for the board to finish moving.";
                return false;
            }
            if (FindCandidates(board).Count == 0)
            {
                reason = "No topic currently has all four items on the board.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public override async UniTask<bool> ExecuteAsync(BoosterContext context)
        {
            Gameboard board = context.Board;
            List<List<CardModel>> candidates = FindCandidates(board);
            if (candidates.Count == 0 || board.HasAnimatingCards()) return false;

            List<CardModel> cards = candidates[Random.Range(0, candidates.Count)];
            DetachOldGroups(board, cards);
            RemoveCardsFromGrid(board, cards);

            CardGroup mergeGroup = CreateMergeGroup(board, cards, GetScreenCenter(board));
            await AnimateIntoGroupAsync(board, cards, mergeGroup.transform.position);
            await board.cardFactory.DespawnStampGroupAsync(mergeGroup);
            await board.SettleBoosterClearAsync(cards);
            return true;
        }

        private static List<List<CardModel>> FindCandidates(Gameboard board)
        {
            var cardsByTopic = new Dictionary<int, List<CardModel>>();
            for (int column = 0; column < board.Cols; column++)
            {
                for (int row = 0; row < board.Rows; row++)
                {
                    CardModel card = board.GetCard(column, row);
                    if (card == null ||
                        !card.CanBeGuaranteedSolutionCard ||
                        !card.HasAssignedContent)
                        continue;

                    int topicId = card.Topic.TopicId;
                    if (!cardsByTopic.TryGetValue(topicId, out List<CardModel> topicCards))
                    {
                        topicCards = new List<CardModel>();
                        cardsByTopic.Add(topicId, topicCards);
                    }
                    topicCards.Add(card);
                }
            }

            var candidates = new List<List<CardModel>>();
            foreach (List<CardModel> topicCards in cardsByTopic.Values)
            {
                List<CardModel> distinctItems = topicCards
                    .GroupBy(card => card.ItemIndex)
                    .Select(group => group.First())
                    .OrderBy(card => card.ItemIndex)
                    .ToList();

                if (distinctItems.Count == StampData.RequiredItemCount &&
                    distinctItems[0].Topic.HasCompleteItemSet(
                        distinctItems.Select(card => card.ItemIndex)))
                {
                    candidates.Add(distinctItems);
                }
            }

            return candidates;
        }

        private static void DetachOldGroups(Gameboard board, IEnumerable<CardModel> cards)
        {
            foreach (CardGroup group in cards
                         .Select(card => card.Group)
                         .Where(group => group != null)
                         .Distinct())
            {
                board.stampDetector.UnparentGroupCards(group);
            }
        }

        private static void RemoveCardsFromGrid(Gameboard board, IEnumerable<CardModel> cards)
        {
            foreach (CardModel card in cards)
            {
                CardView view = board.cardFactory.GetView(card.TileId);
                view?.cardEdgeRenderer?.DisableAllLiquidBridgesImmediate();
                board.SetTile(card.BoardCol, card.BoardRow, null);
                card.CanDrag = false;
                card.IsAnimating = true;
            }
            board.HideBrokenLiquidBridges();
        }

        private static CardGroup CreateMergeGroup(
            Gameboard board,
            IEnumerable<CardModel> cards,
            Vector3 center)
        {
            List<CardModel> cardList = cards.ToList();
            var groupObject = new GameObject($"Auto Merge - {cardList[0].Topic.TopicName}");
            groupObject.transform.SetParent(board.MainboardHolder, true);
            groupObject.transform.position = center;

            var group = groupObject.AddComponent<CardGroup>();
            group.Init(cardList[0].Topic);
            foreach (CardModel card in cardList)
                group.Add(card);
            return group;
        }

        private static async UniTask AnimateIntoGroupAsync(
            Gameboard board,
            IReadOnlyList<CardModel> cards,
            Vector3 center)
        {
            var config = GameManager.Instance.GameConfig;
            float halfStrideX = (config.cardWidth + config.cardGap) * 0.5f;
            float halfStrideY = (config.cardHeight + config.cardGap) * 0.5f;
            var sequence = DOTween.Sequence();

            for (int index = 0; index < cards.Count; index++)
            {
                CardView view = board.cardFactory.GetView(cards[index].TileId);
                if (view == null) continue;

                view.transform.DOKill();
                view.transform.SetParent(board.MainboardHolder, true);
                view.SetSortingOrder(view.completeSortingOrder);

                Vector3 target = center + new Vector3(
                    index % 2 == 0 ? -halfStrideX : halfStrideX,
                    index / 2 == 0 ? halfStrideY : -halfStrideY,
                    0f);
                sequence.Join(view.transform.DOMove(target, 0.48f).SetEase(Ease.InOutCubic));
                sequence.Join(view.transform.DOScale(1.08f, 0.24f)
                    .SetLoops(2, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine));
            }

            await sequence.AsyncWaitForCompletion();
            foreach (CardModel card in cards)
                card.IsAnimating = false;
        }

        private static Vector3 GetScreenCenter(Gameboard board)
        {
            Camera camera = Camera.main;
            if (camera == null) return board.transform.position;

            Vector3 center = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0f));
            center.z = board.transform.position.z;
            return center;
        }
    }
}

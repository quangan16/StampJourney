using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StampJourney.Gameplay.Boosters
{
    [CreateAssetMenu(fileName = "AutoMergeBooster", menuName = "Stamp Journey/Boosters/Auto Merge")]
    public sealed class AutoMergeBoosterAction : BoosterAction
    {
        public override bool CanExecute(BoosterContext context, out string reason)
        {
            if (context.Board == null)
            {
                reason = "The board is not available.";
                return false;
            }

            return context.Board.CanAutoMerge(out reason);
        }

        public override UniTask<bool> ExecuteAsync(BoosterContext context) =>
            context.Board.AutoMergeOneTopicAsync();
    }
}

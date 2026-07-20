using Cysharp.Threading.Tasks;
using StampJourney.Card;
using UnityEngine;

namespace StampJourney.Gameplay.Boosters
{
    public enum BoosterTargetMode
    {
        Immediate,
        Card
    }

    /// <summary>Everything a booster is allowed to access while it executes.</summary>
    public readonly struct BoosterContext
    {
        public Gameboard Board { get; }
        public GameplayControl Gameplay { get; }
        public CardModel TargetCard { get; }

        public BoosterContext(Gameboard board, GameplayControl gameplay, CardModel targetCard)
        {
            Board = board;
            Gameplay = gameplay;
            TargetCard = targetCard;
        }
    }

    /// <summary>
    /// Base asset for one booster behavior. Create a small derived class per booster instead of
    /// growing BoosterController into a switch statement.
    /// </summary>
    public abstract class BoosterAction : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private BoosterTargetMode targetMode;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public Sprite Icon => icon;
        public BoosterTargetMode TargetMode => targetMode;

        /// <summary>Override for booster-specific validation and a user-facing failure reason.</summary>
        public virtual bool CanExecute(BoosterContext context, out string reason)
        {
            reason = string.Empty;
            return true;
        }

        /// <summary>Returns true only when the effect succeeded and one use should be consumed.</summary>
        public abstract UniTask<bool> ExecuteAsync(BoosterContext context);

        protected virtual void OnValidate()
        {
            id = id?.Trim();
        }
    }
}

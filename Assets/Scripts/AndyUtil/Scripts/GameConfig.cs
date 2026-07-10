using UnityEngine;

namespace StampJourney
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "StampJourney/GameConfig")]
    public class GameConfig : ScriptableObject
    {
        public float cardWidth = 1;
        public float cardHeight = 1f;
        public float spritePixelPerUnit = 100f;
        public float cardGap = 0.02f;
    }
}
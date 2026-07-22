using StampJourney.Core;
using UnityEngine;

namespace StampJourney.Cheat
{
    public static class Cheat
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Cheat_SetLevel(int levelID)
        {
            GameManager.Instance.StartGameplayLevel(levelID);
        }
    }
}

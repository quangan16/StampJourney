using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Persistent data access layer using PlayerPrefs.
    /// </summary>
    public class DataManager : SingletonMonoBehaviour<DataManager>
    {
        private const string CurrentLevelKey = "CurrentLevel";

        protected override void OnSingletonInitialized() { }

        public int CurrentLevel
        {
            get => PlayerPrefs.GetInt(CurrentLevelKey, 0);
            set
            {
                PlayerPrefs.SetInt(CurrentLevelKey, value);
                PlayerPrefs.Save();
            }
        }
    }
}
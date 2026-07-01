using UnityEngine;

public class DataManager : PersistentSingletonMonoBehaviour<DataManager>
{
    protected override void OnSingletonInitialized() { }
    public int CurrentLevel
    {
        get => PlayerPrefs.GetInt("CurrentLevel", 0);
        set
        {
            PlayerPrefs.SetInt("CurrentLevel", value);
            PlayerPrefs.Save();
        }
    }
}
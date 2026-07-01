using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple entry point that activates the LoadingScreen
/// LoadingScreen now handles all initialization logic internally
/// </summary>
public class Initializer : PersistentSingletonMonoBehaviour<Initializer>
{
    // [Header("Loading UI")]
    // [Tooltip("Loading screen with animations and initialization logic")]
    // [SerializeField] private LoadingScreen loadingScreen;

    protected void Start()
    {
        // // Simply enable the loading screen - it handles everything else
        // if (loadingScreen != null)
        // {
        //     loadingScreen.gameObject.SetActive(true);
        // }
        // else
        // {
        //     Debug.LogError("Initializer: LoadingScreen is not assigned!");
        // }
    }

    protected override void OnSingletonInitialized()
    {
    }
}

using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StampJourney.UI
{
    /// <summary>
    /// Quản lý toàn bộ UI trong game: HUD, win screen, lose screen, combo popup.
    /// </summary>
    public class UIManager : SingletonMonoBehaviour<UIManager>
    {
        public IScreen currentActiveScreen;
        protected override void OnSingletonInitialized()
        {

        }
    }


}

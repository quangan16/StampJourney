using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Data;
using StampJourney.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StampJourney.Core
{
    /// <summary>
    /// Singleton quản lý trạng thái game toàn cục: start, win, lose, scoring.
    /// </summary>
    public class GameManager : PersistentSingletonMonoBehaviour<GameManager>
    {
        // ---- Inspector ----
        [BoxGroup("References")]
        [field: SerializeField, Required] public LevelSystem LevelSystem { get; set; }
        // ---- Score & Moves ----



        // ---- State ----

        [ShowInInspector, ReadOnly] private GameState _state = GameState.Idle;



        // ---- Constants ----

        private const int BaseStampScore = 100;
        private const int ComboMultiplier = 50;

        // ========================================================
        #region Unity Lifecycle

        protected override void OnSingletonInitialized()
        {
        }


        #endregion

        // ========================================================
        #region Public API

        public GameState State
        {
            get => _state;
            set => _state = value;
        }



        public void GoToGameplay()
        {
            LevelSystem.StartLevel(0);
        }

        public void GoToMainMenu()
        {
            SceneManager.LoadScene(0);
        }

        #endregion

        // ========================================================
        #region Event Handlers







        #endregion

        // ========================================================



    }

    public enum GameState { Idle, Playing, Won, Lost, Paused }
}


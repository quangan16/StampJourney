using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using StampJourney.Core;
using StampJourney.Data;
using StampJourney.UI;
using UnityEngine;

public class GameplayControl : MonoBehaviour
{

    LevelData _levelData;
    [ShowInInspector, ReadOnly] private int _score;
    [ShowInInspector, ReadOnly] private int _remainingMoves;
    [ShowInInspector, ReadOnly] private int _combo;
    // ---- Events ----
    public event Action<int> OnScoreChanged;
    public event Action<int> OnMovesChanged;
    public event Action<int> OnComboChanged;
    public event Action OnGameWon;
    public event Action OnGameLost;

    public void Init(LevelData levelData)
    {
        _levelData = levelData;
    }



    public void Setup()
    {
        _score = 0;
        _remainingMoves = _levelData.maxMoves;
        _combo = 0;
        GameManager.Instance.State = GameState.Playing;

        OnScoreChanged?.Invoke(_score);
        OnMovesChanged?.Invoke(_remainingMoves);
    }

    private void TriggerWin()
    {
        GameManager.Instance.State = GameState.Won;
        AudioManager.Instance.PlayWin();
        OnGameWon?.Invoke();
    }

    private void TriggerLose()
    {
        GameManager.Instance.State = GameState.Lost;
        AudioManager.Instance.PlayLose();
        OnGameLost?.Invoke();
    }

    /// <summary>Mỗi lần swap thành công → trừ move.</summary>
    private void HandleSwapCompleted(CardModel a, CardModel b)
    {
        if (GameManager.Instance.State != GameState.Playing) return;

        _remainingMoves--;
        OnMovesChanged?.Invoke(_remainingMoves);
        AudioManager.Instance.PlaySwap();

        // Reset combo vì đây là swap mới
        _combo = 0;
    }

    /// <summary>Mỗi nhóm stamp cleared → cộng điểm combo.</summary>
    private void HandleStampCleared(List<CardModel> clearedTiles)
    {
        if (GameManager.Instance.State != GameState.Playing) return;

        _combo++;
        // int points = BaseStampScore + (_combo - 1) * ComboMultiplier;
        // _score += points;

        OnScoreChanged?.Invoke(_score);
        OnComboChanged?.Invoke(_combo);
        AudioManager.Instance.PlayClear(_combo);

        // UIManager.Instance.ShowComboText(_combo, 0);
    }
}
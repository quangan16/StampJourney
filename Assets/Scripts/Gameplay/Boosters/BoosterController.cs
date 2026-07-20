using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using UnityEngine;

namespace StampJourney.Gameplay.Boosters
{
    public enum BoosterControllerState
    {
        Idle,
        WaitingForCard,
        Executing
    }

    [Serializable]
    public sealed class BoosterStock
    {
        [SerializeField, Required] private BoosterAction action;
        [SerializeField, Min(0)] private int count;

        public BoosterAction Action => action;
        public int Count => count;

        public void SetCount(int value) => count = Mathf.Max(0, value);
        public bool TryConsume()
        {
            if (count <= 0) return false;
            count--;
            return true;
        }
    }

    /// <summary>
    /// Owns the in-level booster lifecycle: inventory, selection, targeting, execution and UI
    /// notifications. Booster effects live in BoosterAction assets and remain independently
    /// testable and readable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoosterController : MonoBehaviour
    {
        [BoxGroup("References")]
        [SerializeField] private Gameboard board;
        [BoxGroup("References")]
        [SerializeField] private GameplayControl gameplay;

        [BoxGroup("Inventory")]
        [SerializeField] private List<BoosterStock> boosters = new();

        private readonly Dictionary<string, BoosterStock> _stockById = new();
        private BoosterAction _selected;
        private BoosterControllerState _state;

        public BoosterAction Selected => _selected;
        public BoosterControllerState State => _state;
        public bool BlocksCardInput => _state != BoosterControllerState.Idle;
        public IReadOnlyList<BoosterStock> Boosters => boosters;

        public event Action<BoosterAction> OnSelectionChanged;
        public event Action<BoosterControllerState> OnStateChanged;
        public event Action<BoosterAction, int> OnCountChanged;
        public event Action<BoosterAction> OnBoosterUsed;
        public event Action<BoosterAction, string> OnBoosterFailed;

        private void Awake()
        {
            ResolveReferences();
            RebuildInventoryIndex();
            SetState(BoosterControllerState.Idle);
        }

        private void OnDisable()
        {
            _selected = null;
            SetState(BoosterControllerState.Idle);
            OnSelectionChanged?.Invoke(null);
        }

        /// <summary>Selects a booster by its stable asset ID.</summary>
        public bool TrySelect(string boosterId)
        {
            if (string.IsNullOrWhiteSpace(boosterId) ||
                !_stockById.TryGetValue(boosterId, out BoosterStock stock))
                return false;

            return TrySelect(stock.Action);
        }

        /// <summary>
        /// UnityEvent-friendly wrapper for UI Buttons. Set the Inspector string argument to the
        /// booster asset ID, for example "auto_merge".
        /// </summary>
        public void SelectFromUI(string boosterId)
        {
            TrySelect(boosterId);
        }

        /// <summary>
        /// Selects a booster. Selecting the active booster again cancels it. Immediate boosters
        /// execute at once; card-targeted boosters wait for CardView to forward a card press.
        /// </summary>
        public bool TrySelect(BoosterAction action)
        {
            if (!CanStart(action, out BoosterStock stock, out string reason))
            {
                OnBoosterFailed?.Invoke(action, reason);
                return false;
            }

            if (_selected == action && _state == BoosterControllerState.WaitingForCard)
            {
                CancelSelection();
                return true;
            }

            _selected = action;
            OnSelectionChanged?.Invoke(_selected);

            if (action.TargetMode == BoosterTargetMode.Card)
            {
                SetState(BoosterControllerState.WaitingForCard);
            }
            else
            {
                ExecuteAsync(stock, null).Forget();
            }

            return true;
        }

        /// <summary>
        /// Called by CardView before normal dragging. Returns true when the booster consumed the
        /// press or while booster execution is locking card input.
        /// </summary>
        public bool HandleCardPressed(CardModel card)
        {
            if (_state == BoosterControllerState.Idle) return false;
            if (_state == BoosterControllerState.Executing) return true;
            if (_selected == null || _selected.TargetMode != BoosterTargetMode.Card) return true;

            if (!_stockById.TryGetValue(_selected.Id, out BoosterStock stock))
            {
                FailAndCancel("The selected booster is no longer available.");
                return true;
            }

            ExecuteAsync(stock, card).Forget();
            return true;
        }

        public void CancelSelection()
        {
            if (_state == BoosterControllerState.Executing) return;

            _selected = null;
            SetState(BoosterControllerState.Idle);
            OnSelectionChanged?.Invoke(null);
        }

        public int GetCount(string boosterId) =>
            !string.IsNullOrWhiteSpace(boosterId) &&
            _stockById.TryGetValue(boosterId, out BoosterStock stock)
                ? stock.Count
                : 0;

        public bool SetCount(string boosterId, int count)
        {
            if (string.IsNullOrWhiteSpace(boosterId) ||
                !_stockById.TryGetValue(boosterId, out BoosterStock stock))
                return false;

            stock.SetCount(count);
            OnCountChanged?.Invoke(stock.Action, stock.Count);
            return true;
        }

        private async UniTaskVoid ExecuteAsync(BoosterStock stock, CardModel targetCard)
        {
            BoosterAction action = stock?.Action;
            if (action == null)
            {
                FailAndCancel("Booster action is missing.");
                return;
            }

            SetState(BoosterControllerState.Executing);
            var context = new BoosterContext(board, gameplay, targetCard);

            try
            {
                if (!action.CanExecute(context, out string reason))
                {
                    OnBoosterFailed?.Invoke(action, reason);
                    return;
                }

                bool succeeded = await action.ExecuteAsync(context);
                if (!succeeded)
                {
                    OnBoosterFailed?.Invoke(action, "The booster could not be applied.");
                    return;
                }

                if (!stock.TryConsume())
                {
                    OnBoosterFailed?.Invoke(action, "No booster uses remain.");
                    return;
                }

                OnCountChanged?.Invoke(action, stock.Count);
                OnBoosterUsed?.Invoke(action);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                OnBoosterFailed?.Invoke(action, "The booster encountered an unexpected error.");
            }
            finally
            {
                _selected = null;
                SetState(BoosterControllerState.Idle);
                OnSelectionChanged?.Invoke(null);
            }
        }

        private bool CanStart(
            BoosterAction action,
            out BoosterStock stock,
            out string reason)
        {
            stock = null;

            if (action == null)
            {
                reason = "Booster action is missing.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(action.Id))
            {
                reason = "The booster does not have a valid ID.";
                return false;
            }
            if (_state == BoosterControllerState.Executing)
            {
                reason = "Another booster is already running.";
                return false;
            }
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing)
            {
                reason = "Boosters can only be used while the level is playing.";
                return false;
            }
            if (!_stockById.TryGetValue(action.Id, out stock))
            {
                reason = "This booster is not configured for the level.";
                return false;
            }
            if (stock.Count <= 0)
            {
                reason = "No booster uses remain.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void ResolveReferences()
        {
            if (board == null) board = GetComponent<Gameboard>();
            if (board == null) board = FindFirstObjectByType<Gameboard>();
            if (gameplay == null) gameplay = FindFirstObjectByType<GameplayControl>();
        }

        private void RebuildInventoryIndex()
        {
            _stockById.Clear();

            foreach (BoosterStock stock in boosters)
            {
                BoosterAction action = stock?.Action;
                if (action == null || string.IsNullOrWhiteSpace(action.Id))
                {
                    Debug.LogWarning("[BoosterController] Ignored a booster with no action or ID.", this);
                    continue;
                }
                if (_stockById.ContainsKey(action.Id))
                {
                    Debug.LogWarning($"[BoosterController] Duplicate booster ID '{action.Id}' ignored.", this);
                    continue;
                }

                _stockById.Add(action.Id, stock);
            }
        }

        private void FailAndCancel(string reason)
        {
            OnBoosterFailed?.Invoke(_selected, reason);
            CancelSelection();
        }

        private void SetState(BoosterControllerState state)
        {
            if (_state == state) return;
            _state = state;
            OnStateChanged?.Invoke(_state);
        }
    }
}

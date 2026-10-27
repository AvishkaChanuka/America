using System.Collections.Generic;
using UnityEngine;

namespace AmericanBattlefront.Core
{
    /// <summary>
    /// Single authoritative owner of GameState.
    /// All state transitions go through RequestTransition().
    /// Idempotent: requesting the current state is a no-op.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────
        public static GameStateManager Instance { get; private set; }

        // ── State ────────────────────────────────────────────────────────────
        public GameState CurrentState { get; private set; } = GameState.Loading;

        /// <summary>
        /// Valid transitions table. Only listed transitions are allowed.
        /// </summary>
        private static readonly Dictionary<GameState, HashSet<GameState>> s_ValidTransitions
            = new Dictionary<GameState, HashSet<GameState>>
        {
            { GameState.Loading,    new HashSet<GameState> { GameState.Ready,   GameState.Error } },
            { GameState.Ready,      new HashSet<GameState> { GameState.Playing, GameState.Loading } },
            { GameState.Playing,    new HashSet<GameState> { GameState.WaveBreak, GameState.Victory, GameState.Defeat, GameState.Paused } },
            { GameState.WaveBreak,  new HashSet<GameState> { GameState.Playing, GameState.Victory } },
            { GameState.Paused,     new HashSet<GameState> { GameState.Playing, GameState.Ready, GameState.Loading } },
            { GameState.Victory,    new HashSet<GameState> { GameState.Loading } },
            { GameState.Defeat,     new HashSet<GameState> { GameState.Loading } },
            { GameState.Error,      new HashSet<GameState> { GameState.Loading } },
        };

        // ── Unity lifecycle ──────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // GameStateManager persists across scene reloads if needed;
            // remove DontDestroyOnLoad if you prefer full scene reloads.
            // DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Request a transition to the next state.
        /// Returns true if the transition was accepted; false if it was illegal or redundant.
        /// </summary>
        public bool RequestTransition(GameState next)
        {
            // Idempotent: already there
            if (CurrentState == next) return false;

            // Validate
            if (!s_ValidTransitions.TryGetValue(CurrentState, out var allowed)
                || !allowed.Contains(next))
            {
                Debug.LogWarning($"[GameStateManager] Invalid transition: {CurrentState} → {next}. Ignored.");
                return false;
            }

            GameState previous = CurrentState;
            CurrentState = next;

            Debug.Log($"[GameStateManager] {previous} → {next}");
            GameEvents.RaiseGameStateChanged(previous, next);
            return true;
        }

        // ── Convenience helpers ──────────────────────────────────────────────

        public bool IsPlaying => CurrentState == GameState.Playing;
        public bool IsTerminal => CurrentState == GameState.Victory
                                  || CurrentState == GameState.Defeat;
        public bool CanFire => CurrentState == GameState.Playing;
        public bool CanSpawn => CurrentState == GameState.Playing;
        public bool CanTakeDamage => CurrentState == GameState.Playing
                                  || CurrentState == GameState.WaveBreak;

        /// <summary>
        /// Force-reset to Loading state for a clean restart.
        /// Only call this from GameBootstrap, never from UI or weapons.
        /// </summary>
        public void ForceReset()
        {
            GameState previous = CurrentState;
            CurrentState = GameState.Loading;
            //GameEvents.ClearAllListeners();
            Debug.Log($"[GameStateManager] ForceReset: {previous} → Loading");
            GameEvents.RaiseGameStateChanged(previous, GameState.Loading);
        }
    }
}

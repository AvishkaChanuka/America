using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace AmericanBattlefront.Core
{
    /// <summary>
    /// Central static event bus.
    /// Systems fire events here; listeners subscribe here.
    /// Never put game-rule logic inside this class.
    /// </summary>
    public static class GameEvents
    {
        // ── Health ──────────────────────────────────────────────────────────
        /// <summary>Fired by any health component when value changes. (current, maximum)</summary>
        public static event Action<float, float> OnPlayerHealthChanged;

        /// <summary>Fired exactly once when player health reaches zero.</summary>
        public static event Action OnPlayerDied;

        /// <summary>Fired by enemy health when the enemy dies. Carries the enemy GameObject and XP value.</summary>
        public static event Action<GameObject, float> OnEnemyDefeated;

        // ── Weapon ──────────────────────────────────────────────────────────
        /// <summary>Fired after a valid shot is accepted.</summary>
        public static event Action OnWeaponFired;

        /// <summary>Fired when magazine or reserve changes. (currentMag, reserve)</summary>
        public static event Action<int, int> OnAmmoChanged;

        // ── Waves ───────────────────────────────────────────────────────────
        /// <summary>Fired when a wave begins. (waveIndex 0-based, totalWaves)</summary>
        public static event Action<int, int> OnWaveStarted;

        /// <summary>Fired when all enemies in a wave are cleared. (waveIndex 0-based)</summary>
        public static event Action<int> OnWaveCleared;

        // ── XP ──────────────────────────────────────────────────────────────
        /// <summary>Fired whenever session XP changes. (newTotal)</summary>
        public static event Action<float> OnXPChanged;

        // ── Game State ──────────────────────────────────────────────────────
        /// <summary>Fired on every game state transition. (previous, next)</summary>
        public static event Action<GameState, GameState> OnGameStateChanged;

        // ── Invoke helpers (called by systems, not external code) ────────────
        public static void RaisePlayerHealthChanged(float current, float max)
            => OnPlayerHealthChanged?.Invoke(current, max);

        public static void RaisePlayerDied()
            => OnPlayerDied?.Invoke();

        public static void RaiseEnemyDefeated(GameObject enemy, float xp)
            => OnEnemyDefeated?.Invoke(enemy, xp);

        public static void RaiseWeaponFired()
            => OnWeaponFired?.Invoke();

        public static void RaiseAmmoChanged(int mag, int reserve)
            => OnAmmoChanged?.Invoke(mag, reserve);

        public static void RaiseWaveStarted(int index, int total)
            => OnWaveStarted?.Invoke(index, total);

        public static void RaiseWaveCleared(int index)
            => OnWaveCleared?.Invoke(index);

        public static void RaiseXPChanged(float total)
            => OnXPChanged?.Invoke(total);

        public static void RaiseGameStateChanged(GameState prev, GameState next)
            => OnGameStateChanged?.Invoke(prev, next);

        /// <summary>
        /// Call this on every clean restart to prevent stale listeners 
        /// from a previous session accumulating.
        /// </summary>
        public static void ClearAllListeners()
        {
            OnPlayerHealthChanged = null;
            OnPlayerDied = null;
            OnEnemyDefeated = null;
            OnWeaponFired = null;
            OnAmmoChanged = null;
            OnWaveStarted = null;
            OnWaveCleared = null;
            OnXPChanged = null;
            OnGameStateChanged = null;
        }
    }
}

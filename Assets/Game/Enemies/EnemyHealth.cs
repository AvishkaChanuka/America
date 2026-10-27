using System;
using UnityEngine;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Enemies
{
    /// <summary>
    /// Enemy health component. Implements IDamageable.
    /// Death is strictly idempotent: OnDied fires exactly once.
    /// XP is awarded exactly once via GameEvents.RaiseEnemyDefeated.
    ///
    /// GameBootstrap / WaveDirector calls Configure() after spawning.
    /// </summary>
    public class EnemyHealth : MonoBehaviour, IDamageable
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Defaults (overridden by Configure())")]
        [SerializeField] private float _maxHealth = 50f;
        [SerializeField] private float _xpReward = 10f;

        [Header("Feedback (optional)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _hurtSFX;
        [SerializeField] private AudioClip _deathSFX;

        // ── Events ─────────────────────────────────────────────────────────────
        /// <summary>Fired exactly once when health reaches zero.</summary>
        public event Action OnDied;

        // ── Runtime state ──────────────────────────────────────────────────────
        private float _currentHealth;
        private bool _isDead;
        private bool _isConfigured;

        // ── IDamageable ────────────────────────────────────────────────────────
        public bool IsAlive => !_isDead;

        // ── Public accessors ───────────────────────────────────────────────────
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;

        // ── Unity lifecycle ────────────────────────────────────────────────────
        private void OnEnable()
        {
            // Reset when object is re-enabled (pooling support)
            if (_isConfigured) ResetHealth();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Configure this enemy's health and XP reward.
        /// Called by WaveDirector (Phase 7) immediately after spawning.
        /// </summary>
        public void Configure(float maxHealth, float xpReward)
        {
            _maxHealth = maxHealth;
            _xpReward = xpReward;
            _isConfigured = true;
            ResetHealth();
        }

        /// <summary>Restore to full health (used on re-enable/pooling).</summary>
        public void ResetHealth()
        {
            _currentHealth = _maxHealth;
            _isDead = false;
        }

        // ── IDamageable implementation ─────────────────────────────────────────

        public void TakeDamage(DamageInfo info)
        {
            if (_isDead) return; // idempotent — ignore all hits after death

            // Ignore damage in non-combat states
            if (GameStateManager.Instance != null &&
                !GameStateManager.Instance.CanTakeDamage)
                return;

            _currentHealth -= info.Amount;
            Debug.Log($"[EnemyHealth:{name}] Took {info.Amount} dmg. HP: {_currentHealth:F0}/{_maxHealth:F0}");

            PlaySound(_hurtSFX);

            if (_currentHealth <= 0f)
                Die();
        }

        // ── Private ────────────────────────────────────────────────────────────

        private void Die()
        {
            if (_isDead) return; // double-guard
            _isDead = true;
            _currentHealth = 0f;

            PlaySound(_deathSFX);

            // Notify WaveDirector and XP system — exactly once
            float xp = _xpReward > 0f ? _xpReward : 10f;
            GameEvents.RaiseEnemyDefeated(gameObject, xp);

            Debug.Log($"[EnemyHealth:{name}] Died. XP awarded: {xp}");

            // Notify EnemyBrain on the same object
            OnDied?.Invoke();
        }

        private void PlaySound(AudioClip clip)
        {
            if (_audioSource == null || clip == null) return;
            _audioSource.PlayOneShot(clip);
        }
    }
}

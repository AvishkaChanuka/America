using UnityEngine;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Player
{
    /// <summary>
    /// Player health component. Implements IDamageable.
    /// Attach to the XR Origin or a dedicated Player child.
    ///
    /// GameBootstrap calls Configure() after each level load.
    /// Fires GameEvents.RaisePlayerHealthChanged and RaisePlayerDied.
    /// Defeat transition is requested on death — only once, idempotent.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Feedback (optional)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _hurtSFX;
        [SerializeField] private AudioClip _deathSFX;

        // ── Runtime state ──────────────────────────────────────────────────────
        private float _maxHealth;
        private float _currentHealth;
        private float _damageCooldown;     // seconds of invincibility after a hit
        private float _cooldownTimer;
        private bool _isDead;
        private bool _isConfigured;

        // ── IDamageable ────────────────────────────────────────────────────────
        public bool IsAlive => !_isDead;

        // ── Unity lifecycle ────────────────────────────────────────────────────
        private void Awake()
        {
            // Self-register with PlayerReference
            if (PlayerReference.Instance != null)
            {
                PlayerReference.Instance.PlayerDamageable = this;
                PlayerReference.Instance.PlayerHealthComponent = this;
            }
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called by GameBootstrap after level load with values from LevelConfig.
        /// </summary>
        public void Configure(float maxHealth, float damageCooldown)
        {
            _maxHealth = maxHealth;
            _damageCooldown = damageCooldown;
            _isConfigured = true;
            ResetHealth();
        }

        /// <summary>
        /// Restore health to max (called on restart).
        /// </summary>
        public void ResetHealth()
        {
            _currentHealth = _maxHealth;
            _isDead = false;
            _cooldownTimer = 0f;
            GameEvents.RaisePlayerHealthChanged(_currentHealth, _maxHealth);
        }

        // ── IDamageable implementation ─────────────────────────────────────────

        public void TakeDamage(DamageInfo info)
        {
            if (!_isConfigured) return;
            if (_isDead) return;

            // Respect game state
            if (GameStateManager.Instance != null &&
                !GameStateManager.Instance.CanTakeDamage)
                return;

            // Damage cooldown window
            if (_cooldownTimer > 0f) return;

            _currentHealth -= info.Amount;
            _cooldownTimer = _damageCooldown;

            Debug.Log($"[PlayerHealth] Took {info.Amount} damage. HP: {_currentHealth:F0}/{_maxHealth:F0}");
            PlaySound(_hurtSFX);
            GameEvents.RaisePlayerHealthChanged(_currentHealth, _maxHealth);

            if (_currentHealth <= 0f)
                Die();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void Die()
        {
            if (_isDead) return; // idempotent
            _isDead = true;

            _currentHealth = 0f;
            GameEvents.RaisePlayerHealthChanged(0f, _maxHealth);
            GameEvents.RaisePlayerDied();

            PlaySound(_deathSFX);
            Debug.Log("[PlayerHealth] Player died — requesting Defeat state.");

            GameStateManager.Instance?.RequestTransition(GameState.Defeat);
        }

        private void PlaySound(AudioClip clip)
        {
            if (_audioSource == null || clip == null) return;
            _audioSource.PlayOneShot(clip);
        }
    }
}

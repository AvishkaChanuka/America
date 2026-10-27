using UnityEngine;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Core
{
    /// <summary>
    /// Accumulates session XP from enemy defeats.
    /// Subscribes to GameEvents.OnEnemyDefeated — XP is awarded exactly once
    /// per enemy (idempotency is guaranteed by EnemyHealth.Die()).
    /// Reset() is called by GameBootstrap on restart.
    /// </summary>
    public class XPTracker : MonoBehaviour
    {
        public float SessionXP { get; private set; }

        private void OnEnable()
            => GameEvents.OnEnemyDefeated += OnEnemyDefeated;

        private void OnDisable()
            => GameEvents.OnEnemyDefeated -= OnEnemyDefeated;

        private void OnEnemyDefeated(GameObject enemy, float xp)
        {
            SessionXP += xp;
            Debug.Log($"[XPTracker] +{xp} XP  |  Total: {SessionXP}");
            GameEvents.RaiseXPChanged(SessionXP);
        }

        /// <summary>Reset to zero on level restart.</summary>
        public void ResetXP()
        {
            SessionXP = 0f;
            GameEvents.RaiseXPChanged(SessionXP);
        }
    }
}

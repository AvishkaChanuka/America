using UnityEngine;

namespace AmericanBattlefront.Core
{
    /// <summary>
    /// Payload passed with every damage call.
    /// Source is optional – useful for death attribution but never required.
    /// </summary>
    [System.Serializable]
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 HitPoint;        // world-space hit position (optional, can be zero)
        public GameObject Instigator;   // who caused the damage (optional)

        public DamageInfo(float amount, Vector3 hitPoint = default, GameObject instigator = null)
        {
            Amount = amount;
            HitPoint = hitPoint;
            Instigator = instigator;
        }
    }

    /// <summary>
    /// Common damage contract for player and enemies.
    /// Resolve via GetComponentInParent<IDamageable>() on any hit collider.
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
        bool IsAlive { get; }
    }
}

using UnityEngine;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Player
{
    /// <summary>
    /// Constrains the XR Origin root to the configured arena rectangle.
    /// Attach to the same GameObject as (or a child of) GameBootstrap,
    /// then assign the XR Origin root transform.
    ///
    /// Works by clamping XZ every FixedUpdate — does not fight the locomotion
    /// system because it applies after the provider has moved the origin.
    ///
    /// Call Configure() from GameBootstrap when a new level loads.
    /// </summary>
    public class PlayerBoundary : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The XR Origin root transform (NOT the camera — the root that locomotion moves).")]
        [SerializeField] private Transform _xrOriginRoot;

        [Header("Debug")]
        [SerializeField] private bool _showGizmo = true;

        // ── Runtime ──────────────────────────────────────────────────────────
        private bool _active = false;
        private Vector3 _centre = Vector3.zero;
        private Vector2 _halfExtents = new Vector2(10f, 10f); // half of arena dims
        private float _wallBuffer = 0.3f; // how far from wall edge to stop player

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by GameBootstrap after a level loads.
        /// Centre is the arena's world-space centre (usually Vector3.zero).
        /// Dimensions is the full width × depth of the arena.
        /// </summary>
        public void Configure(Vector3 centre, Vector2 arenaDimensions, float wallBuffer = 0.3f)
        {
            _centre = centre;
            _halfExtents = arenaDimensions * 0.5f;
            _wallBuffer = wallBuffer;
            _active = true;
        }

        public void Deactivate() => _active = false;

        // ── Unity lifecycle ──────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (!_active || _xrOriginRoot == null) return;

            Vector3 pos = _xrOriginRoot.position;

            float minX = _centre.x - _halfExtents.x + _wallBuffer;
            float maxX = _centre.x + _halfExtents.x - _wallBuffer;
            float minZ = _centre.z - _halfExtents.y + _wallBuffer;
            float maxZ = _centre.z + _halfExtents.y - _wallBuffer;

            float clampedX = Mathf.Clamp(pos.x, minX, maxX);
            float clampedZ = Mathf.Clamp(pos.z, minZ, maxZ);

            if (!Mathf.Approximately(clampedX, pos.x) ||
                !Mathf.Approximately(clampedZ, pos.z))
            {
                _xrOriginRoot.position = new Vector3(clampedX, pos.y, clampedZ);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_showGizmo || !_active) return;

            Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
            Vector3 size = new Vector3(
                (_halfExtents.x - _wallBuffer) * 2f,
                0.05f,
                (_halfExtents.y - _wallBuffer) * 2f);
            Gizmos.DrawWireCube(_centre, size);
        }
#endif
    }
}

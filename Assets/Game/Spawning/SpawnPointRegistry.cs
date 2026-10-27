using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Spawning
{
    /// <summary>
    /// Holds a list of candidate enemy spawn transforms.
    /// After NavMesh is baked, call ValidateAll() to discard points that
    /// are not on the NavMesh or are too close to the player start.
    /// </summary>
    public class SpawnPointRegistry : MonoBehaviour
    {
        [Tooltip("Drag spawn-point transforms here, OR let ArenaBuilder populate them at runtime.")]
        [SerializeField] private List<Transform> _candidatePoints = new List<Transform>();

        private readonly List<Transform> _validPoints = new List<Transform>();

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Validate all candidate points against the baked NavMesh and
        /// minimum distance from the player start.
        /// Call AFTER NavMesh is baked and BEFORE first enemy spawn.
        /// </summary>
        public void ValidateAll(Vector3 playerStart, float minDistanceFromPlayer)
        {
            _validPoints.Clear();

            foreach (Transform t in _candidatePoints)
            {
                if (t == null) continue;

                Vector3 pos = t.position;

                // 1. Must be on NavMesh
                if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"[SpawnPointRegistry] Point '{t.name}' is not on NavMesh — skipped.");
                    continue;
                }

                // 2. Must be far enough from player start
                if (Vector3.Distance(hit.position, playerStart) < minDistanceFromPlayer)
                {
                    Debug.LogWarning($"[SpawnPointRegistry] Point '{t.name}' is too close to player start ({Vector3.Distance(hit.position, playerStart):F1}m < {minDistanceFromPlayer}m) — skipped.");
                    continue;
                }

                _validPoints.Add(t);
            }

            Debug.Log($"[SpawnPointRegistry] Valid spawn points: {_validPoints.Count} / {_candidatePoints.Count}");
        }

        /// <summary>
        /// Returns a random valid spawn point, or null if none available.
        /// </summary>
        public Transform GetRandomValidPoint()
        {
            if (_validPoints.Count == 0) return null;
            return _validPoints[Random.Range(0, _validPoints.Count)];
        }

        /// <summary>
        /// Returns all currently valid spawn points (read-only view).
        /// </summary>
        public IReadOnlyList<Transform> ValidPoints => _validPoints;

        /// <summary>
        /// Adds a runtime-generated point (called by ArenaBuilder).
        /// </summary>
        public void RegisterPoint(Transform t)
        {
            if (t != null && !_candidatePoints.Contains(t))
                _candidatePoints.Add(t);
        }

        /// <summary>
        /// Clears all registered points (called during layout reset).
        /// </summary>
        public void ClearAll()
        {
            //_candidatePoints.Clear();
            _validPoints.Clear();
        }
    }
}

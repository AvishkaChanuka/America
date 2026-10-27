using AmericanBattlefront.Core;
using AmericanBattlefront.Spawning;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace AmericanBattlefront.Environment
{
    /// <summary>
    /// Orchestrates:
    ///   1. Panorama sphere setup
    ///   2. Arena prefab instantiation
    ///   3. Seeded, deterministic prop placement with protected-zone and
    ///      overlap checks
    ///   4. Runtime NavMesh bake (via NavMeshSurface)
    ///   5. Spawn point registration
    ///
    /// Call BuildAsync() from GameBootstrap after state reset.
    /// Never rebakes NavMesh after initial build.
    /// </summary>
    public class ArenaBuilder : MonoBehaviour
    {
        // ── Inspector refs ───────────────────────────────────────────────────
        [Header("References")]
        [Tooltip("Panorama sphere in the scene (visual only, no collider).")]
        [SerializeField] private GameObject _panoramaSphere;

        [Tooltip("NavMeshSurface component in the scene used for baking.")]
        [SerializeField] private NavMeshSurface _navMeshSurface;

        [Tooltip("Spawn Point Registry used to register generated spawn points.")]
        [SerializeField] private SpawnPointRegistry _spawnRegistry;

        [Tooltip("Parent transform for generated props (keeps hierarchy clean).")]
        [SerializeField] private Transform _generatedPropsRoot;

        // ── Runtime state ────────────────────────────────────────────────────
        private readonly List<GameObject> _generatedProps = new List<GameObject>();
        private readonly List<Bounds> _occupiedBounds = new List<Bounds>();
        private GameObject _spawnedArenaPrefab;
        private int _activeSeed;

        // ── Public status ────────────────────────────────────────────────────
        public bool IsBuilt { get; private set; }
        public int ActiveSeed => _activeSeed;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Full build sequence. Awaited by GameBootstrap.
        /// Returns null on success, or an error string on failure.
        /// </summary>
        public IEnumerator BuildAsync(LevelConfig cfg, int seedOverride = -1)
        {
            IsBuilt = false;

            // ── 1. Determine seed ────────────────────────────────────────────
            _activeSeed = (seedOverride >= 0) ? seedOverride : cfg.Seed;
            Debug.Log($"[ArenaBuilder] Building level '{cfg.LevelId}' with seed {_activeSeed}");

            // ── 2. Clear previous generated layout ───────────────────────────
            ClearGeneratedLayout();

            // ── 3. Panorama ──────────────────────────────────────────────────
            SetupPanorama(cfg);

            // ── 4. Arena prefab ──────────────────────────────────────────────
            if (cfg.Arena.ArenaPrefab != null)
            {
                _spawnedArenaPrefab = Instantiate(
                    cfg.Arena.ArenaPrefab,
                    Vector3.zero,
                    Quaternion.identity);
                _spawnedArenaPrefab.name = "ARENA_Generated";
            }
            else
            {
                Debug.LogError("[ArenaBuilder] Arena.ArenaPrefab is null — cannot build arena.");
                yield break;
            }

            // Wait one frame for Unity to register new colliders
            yield return null;

            // ── 5. Place props ───────────────────────────────────────────────
            PlaceProps(cfg);

            // Wait one frame for placed prop colliders to register
            yield return null;

            // ── 6. Bake NavMesh ──────────────────────────────────────────────
            if (_navMeshSurface != null)
            {
                Debug.Log("[ArenaBuilder] Baking NavMesh…");
                _navMeshSurface.BuildNavMesh();
                Debug.Log("[ArenaBuilder] NavMesh bake complete.");
            }
            else
            {
                Debug.LogWarning("[ArenaBuilder] No NavMeshSurface assigned — skipping NavMesh bake.");
            }

            // Wait one more frame so NavMesh data is queryable
            yield return null;

            // ── 7. Validate & register spawn points ──────────────────────────
            if (_spawnRegistry != null)
            {
                _spawnRegistry.ValidateAll(
                    cfg.Arena.PlayerStartPosition,
                    minDistanceFromPlayer: 3.5f);
            }

            IsBuilt = true;
            Debug.Log($"[ArenaBuilder] Build complete. Props placed: {_generatedProps.Count}");
        }

        /// <summary>
        /// Destroys all generated objects (props + arena prefab) from the
        /// previous session. Does NOT touch the XR rig, UI or static scene objects.
        /// </summary>
        public void ClearGeneratedLayout()
        {
            foreach (var go in _generatedProps)
                if (go != null) Destroy(go);

            _generatedProps.Clear();
            _occupiedBounds.Clear();

            if (_spawnedArenaPrefab != null)
            {
                Destroy(_spawnedArenaPrefab);
                _spawnedArenaPrefab = null;
            }

            if (_spawnRegistry != null)
                _spawnRegistry.ClearAll();

            IsBuilt = false;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private void SetupPanorama(LevelConfig cfg)
        {
            if (_panoramaSphere == null) return;

            // Apply material
            if (cfg.PanoramaMaterial != null)
            {
                var renderer = _panoramaSphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Instantiate material to avoid modifying the asset
                    var mat = new Material(cfg.PanoramaMaterial);

                    // Exposure (works for Unlit/Skybox shaders that use _Exposure)
                    if (mat.HasProperty("_Exposure"))
                        mat.SetFloat("_Exposure", cfg.PanoramaExposure);

                    renderer.material = mat;
                }
            }

            // Rotation offset
            _panoramaSphere.transform.rotation =
                Quaternion.Euler(0f, cfg.PanoramaRotation, 0f);
        }

        private void PlaceProps(LevelConfig cfg)
        {
            if (cfg.Props == null || cfg.Props.Count == 0) return;

            // Deterministic RNG
            Random.InitState(_activeSeed);

            Vector2 halfArena = cfg.Arena.ArenaDimensions * 0.5f;
            int totalPlaced = 0;

            foreach (var entry in cfg.Props)
            {
                if (entry.Prefab == null)
                {
                    Debug.LogWarning("[ArenaBuilder] PropEntry has null prefab — skipped.");
                    continue;
                }

                int count = Random.Range(entry.MinCount, entry.MaxCount + 1);

                for (int i = 0; i < count; i++)
                {
                    if (totalPlaced >= cfg.MaxTotalProps)
                    {
                        Debug.LogWarning($"[ArenaBuilder] MaxTotalProps ({cfg.MaxTotalProps}) reached — stopping placement.");
                        return;
                    }

                    bool placed = false;

                    for (int attempt = 0; attempt < cfg.MaxAttemptsPerProp; attempt++)
                    {
                        // Random position inside arena XZ bounds
                        float x = Random.Range(-halfArena.x, halfArena.x);
                        float z = Random.Range(-halfArena.y, halfArena.y);

                        // Raycast down to find floor
                        Vector3 candidate = new Vector3(x, 10f, z);
                        if (!Physics.Raycast(candidate, Vector3.down, out RaycastHit hit, 20f))
                            continue; // no floor found

                        Vector3 position = hit.point;

                        // Check protected zones
                        if (IsInProtectedZone(position, cfg))
                            continue;

                        // Random scale
                        float scale = Random.Range(entry.ScaleRange.x, entry.ScaleRange.y);

                        // Estimate bounds for overlap check
                        // Use prefab renderer bounds scaled — approximation
                        Bounds candidateBounds = EstimatePrefabBounds(entry.Prefab, position, scale);

                        // Check separation against already-placed props
                        if (OverlapsExisting(candidateBounds, entry.MinSeparation))
                            continue;

                        // ── Place it ─────────────────────────────────────────
                        float yRotation = Random.Range(0f, 360f);
                        Quaternion rotation = Quaternion.Euler(0f, yRotation, 0f);

                        Transform parent = _generatedPropsRoot != null
                            ? _generatedPropsRoot
                            : transform;

                        GameObject prop = Instantiate(entry.Prefab, position, rotation, parent);
                        prop.transform.localScale = Vector3.one * scale;
                        prop.name = $"Prop_{entry.Prefab.name}_{totalPlaced}";

                        /*// Mark as static for NavMesh if required
                        if (entry.IsNavigationStatic)
                            SetStaticRecursive(prop, StaticEditorFlags.NavigationStatic);
                        */
                        _generatedProps.Add(prop);
                        _occupiedBounds.Add(candidateBounds);
                        totalPlaced++;
                        placed = true;
                        break;
                    }

                    if (!placed)
                    {
                        Debug.LogWarning(
                            $"[ArenaBuilder] Could not place '{entry.Prefab.name}' " +
                            $"instance {i + 1} after {cfg.MaxAttemptsPerProp} attempts — skipped.");
                    }
                }
            }
        }

        private bool IsInProtectedZone(Vector3 position, LevelConfig cfg)
        {
            // Always protect player start
            if (Vector3.Distance(
                    new Vector3(position.x, 0, position.z),
                    new Vector3(cfg.Arena.PlayerStartPosition.x, 0, cfg.Arena.PlayerStartPosition.z))
                < 2.5f)
                return true;

            // Check configured zones
            foreach (var zone in cfg.ProtectedZones)
            {
                Vector3 flatPos = new Vector3(position.x, 0, position.z);
                Vector3 flatZone = new Vector3(zone.Centre.x, 0, zone.Centre.z);
                if (Vector3.Distance(flatPos, flatZone) < zone.Radius)
                    return true;
            }

            return false;
        }

        private bool OverlapsExisting(Bounds candidate, float minSep)
        {
            // Expand the candidate by minSeparation for clearance check
            Bounds expanded = candidate;
            expanded.Expand(minSep * 2f);

            foreach (var existing in _occupiedBounds)
            {
                if (expanded.Intersects(existing))
                    return true;
            }
            return false;
        }

        private Bounds EstimatePrefabBounds(GameObject prefab, Vector3 position, float scale)
        {
            // Use the prefab's renderer bounds as a size estimate
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(position, Vector3.one * scale);

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers)
                b.Encapsulate(r.bounds);

            // b is in prefab local space at origin — re-centre on placement pos
            Vector3 size = b.size * scale;
            return new Bounds(position + Vector3.up * (size.y * 0.5f), size);
        }

        /// <summary>
        /// Sets a StaticEditorFlag on a GameObject and all children.
        /// Only meaningful in Editor; safe to call at runtime (no-op on device).
        /// For runtime NavMesh baking, GameObjects need not be editor-static.
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AmericanBattlefront.Core;
using AmericanBattlefront.Enemies;
using AmericanBattlefront.Spawning;

namespace AmericanBattlefront.Spawning
{
    /// <summary>
    /// Owns wave progression: spawns enemies, tracks alive count,
    /// advances to next wave, triggers Victory after final wave.
    ///
    /// Configure() is called by GameBootstrap after level setup.
    /// Wave progression begins when GameState transitions to Playing
    /// (WaveDirector listens to GameEvents.OnGameStateChanged).
    ///
    /// On restart, GameBootstrap calls ResetAndStop() before re-configuring.
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        // ── Inspector refs ────────────────────────────────────────────────────
        [SerializeField] private SpawnPointRegistry _spawnRegistry;
        [Tooltip("Parent transform for spawned enemies (keeps hierarchy clean).")]
        [SerializeField] private Transform _enemiesRoot;

        // ── Runtime state ─────────────────────────────────────────────────────
        private LevelConfig _config;
        private int _currentWaveIndex = -1;
        private int _spawnedThisWave = 0;
        private int _defeatedThisWave = 0;
        private bool _waveInProgress = false;
        private bool _isConfigured = false;

        private readonly List<GameObject> _activeEnemies = new List<GameObject>();
        private Coroutine _waveCoroutine;

        // ── Public state ──────────────────────────────────────────────────────
        public int CurrentWave => _currentWaveIndex + 1;    // 1-based for UI
        public int TotalWaves => _config?.Waves?.Count ?? 0;
        public int AliveEnemies => _spawnedThisWave - _defeatedThisWave;
        public bool IsRunning => _waveInProgress;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable()
        {
            GameEvents.OnGameStateChanged += OnGameStateChanged;
            GameEvents.OnEnemyDefeated += OnEnemyDefeated;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStateChanged -= OnGameStateChanged;
            GameEvents.OnEnemyDefeated -= OnEnemyDefeated;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Called by GameBootstrap after arena + NavMesh setup.
        /// Stores config and resets state. Does NOT start waves yet.
        /// </summary>
        public void Configure(LevelConfig config, SpawnPointRegistry registry)
        {
            _config = config;

            // Only overwrite if a non-null registry was passed; otherwise keep Inspector reference or auto-find
            if (registry != null)
                _spawnRegistry = registry;
            else if (_spawnRegistry == null)
                _spawnRegistry = FindFirstObjectByType<SpawnPointRegistry>();

            _spawnRegistry = registry;
            _isConfigured = true;

            // Ensure event subscriptions are active
            GameEvents.OnGameStateChanged -= OnGameStateChanged;
            GameEvents.OnEnemyDefeated -= OnEnemyDefeated;
            GameEvents.OnGameStateChanged += OnGameStateChanged;
            GameEvents.OnEnemyDefeated += OnEnemyDefeated;

            Debug.Log($"[WaveDirector] Configured: {config.Waves.Count} waves, " +
                      $"{config.Enemies.Count} enemy type(s).");
        }

        /// <summary>
        /// Hard stop + full reset. Called by GameBootstrap before re-configuring on restart.
        /// </summary>
        public void ResetAndStop()
        {
            if (_waveCoroutine != null)
            {
                StopCoroutine(_waveCoroutine);
                _waveCoroutine = null;
            }

            // Disable all spawned enemies
            foreach (var go in _activeEnemies)
            {
                if (go != null) go.SetActive(false);
            }
            _activeEnemies.Clear();

            _currentWaveIndex = -1;
            _spawnedThisWave = 0;
            _defeatedThisWave = 0;
            _waveInProgress = false;
            _isConfigured = false;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnGameStateChanged(GameState prev, GameState next)
        {
            if (next == GameState.Playing && prev == GameState.Ready && _isConfigured)
            {
                // First time entering Playing from Ready → start wave 0
                StartNextWave();
            }
            else if (next == GameState.Playing && prev == GameState.WaveBreak)
            {
                // Returning from wave break → start next wave
                StartNextWave();
            }
        }

        private void OnEnemyDefeated(GameObject enemy, float xp)
        {
            if (!_waveInProgress) return;

            _activeEnemies.Remove(enemy);
            _defeatedThisWave++;

            Debug.Log($"[WaveDirector] Enemy defeated. " +
                      $"Defeated this wave: {_defeatedThisWave}/{_spawnedThisWave} " +
                      $"(Total expected: {CurrentWaveDefinition()?.TotalEnemies})");

            // Check wave completion
            WaveDefinition wave = CurrentWaveDefinition();
            if (wave != null &&
                _defeatedThisWave >= wave.TotalEnemies &&
                _spawnedThisWave >= wave.TotalEnemies)
            {
                OnWaveCleared();
            }
        }

        // ── Wave flow ─────────────────────────────────────────────────────────

        private void StartNextWave()
        {
            _currentWaveIndex++;

            if (_currentWaveIndex >= _config.Waves.Count)
            {
                // Shouldn't happen — Victory should have been triggered already
                Debug.LogWarning("[WaveDirector] StartNextWave called past last wave — triggering Victory.");
                TriggerVictory();
                return;
            }

            WaveDefinition wave = CurrentWaveDefinition();
            _spawnedThisWave = 0;
            _defeatedThisWave = 0;
            _waveInProgress = true;

            Debug.Log($"[WaveDirector] Starting wave {CurrentWave}/{TotalWaves} " +
                      $"({wave.TotalEnemies} enemies, max {wave.MaxAliveAtOnce} alive)");

            GameEvents.RaiseWaveStarted(_currentWaveIndex, TotalWaves);

            if (_waveCoroutine != null)
                StopCoroutine(_waveCoroutine);

            _waveCoroutine = StartCoroutine(SpawnWaveCoroutine(wave));
        }

        private IEnumerator SpawnWaveCoroutine(WaveDefinition wave)
        {
            // Pre-wave delay
            if (wave.PreWaveDelay > 0f)
                yield return new WaitForSeconds(wave.PreWaveDelay);

            int totalToSpawn = wave.TotalEnemies;

            while (_spawnedThisWave < totalToSpawn)
            {
                // Guard: stop if game leaves Playing state
                if (GameStateManager.Instance == null || !GameStateManager.Instance.CanSpawn)
                {
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // Respect alive cap (per-wave and global)
                int alive = _spawnedThisWave - _defeatedThisWave;
                int maxCap = Mathf.Min(wave.MaxAliveAtOnce, _config.MaxActiveEnemies);

                if (alive >= maxCap)
                {
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // Get a valid spawn point
                Transform spawnPoint = _spawnRegistry?.GetRandomValidPoint();
                if (spawnPoint == null)
                {
                    Debug.LogWarning("[WaveDirector] No valid spawn point available — retrying in 1s.");
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                // Spawn enemy
                bool spawned = TrySpawnEnemy(spawnPoint.position);
                if (spawned) _spawnedThisWave++;

                yield return new WaitForSeconds(wave.SpawnInterval);
            }

            // All enemies for this wave spawned.
            // WaveCleared is triggered by OnEnemyDefeated when defeatedCount reaches total.
        }

        private bool TrySpawnEnemy(Vector3 position)
        {
            EnemyEntry entry = PickRandomEnemyEntry();
            if (entry == null || entry.Prefab == null)
            {
                Debug.LogWarning("[WaveDirector] No valid EnemyEntry to spawn.");
                return false;
            }

            Transform parent = _enemiesRoot != null ? _enemiesRoot : transform;
            GameObject enemy = Instantiate(entry.Prefab, position, Quaternion.identity, parent);
            enemy.name = $"Enemy_{entry.Prefab.name}_{_spawnedThisWave}";

            // Configure health
            var health = enemy.GetComponent<EnemyHealth>();
            if (health != null)
                health.Configure(entry.MaxHealth,
                                 entry.XPReward > 0f ? entry.XPReward : _config.DefaultXPPerKill);
            else
                Debug.LogWarning($"[WaveDirector] Spawned '{enemy.name}' has no EnemyHealth!");

            // Configure brain
            var brain = enemy.GetComponent<EnemyBrain>();
            if (brain != null)
                brain.Configure(
                    entry.MoveSpeed,
                    entry.DetectionRange,
                    entry.AttackRange,
                    entry.AttackDamage,
                    entry.AttackCooldown,
                    entry.RecoverDuration,
                    entry.RequireLineOfSight);
            else
                Debug.LogWarning($"[WaveDirector] Spawned '{enemy.name}' has no EnemyBrain!");

            _activeEnemies.Add(enemy);
            Debug.Log($"[WaveDirector] Spawned '{enemy.name}' at {position}");
            return true;
        }

        private void OnWaveCleared()
        {
            _waveInProgress = false;

            if (_waveCoroutine != null)
            {
                StopCoroutine(_waveCoroutine);
                _waveCoroutine = null;
            }

            Debug.Log($"[WaveDirector] Wave {CurrentWave} cleared!");
            GameEvents.RaiseWaveCleared(_currentWaveIndex);

            bool isFinalWave = (_currentWaveIndex >= _config.Waves.Count - 1);

            if (isFinalWave)
            {
                TriggerVictory();
            }
            else
            {
                // Enter WaveBreak — OnGameStateChanged will call StartNextWave()
                GameStateManager.Instance?.RequestTransition(GameState.WaveBreak);

                // Auto-advance from WaveBreak after a short pause
                StartCoroutine(AutoAdvanceFromWaveBreak());
            }
        }

        private IEnumerator AutoAdvanceFromWaveBreak()
        {
            yield return new WaitForSeconds(3f); // 3s inter-wave breather
            if (GameStateManager.Instance?.CurrentState == GameState.WaveBreak)
                GameStateManager.Instance.RequestTransition(GameState.Playing);
        }

        private void TriggerVictory()
        {
            Debug.Log("[WaveDirector] All waves cleared — Victory!");
            GameStateManager.Instance?.RequestTransition(GameState.Victory);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private WaveDefinition CurrentWaveDefinition()
        {
            if (_config == null || _currentWaveIndex < 0 ||
                _currentWaveIndex >= _config.Waves.Count)
                return null;
            return _config.Waves[_currentWaveIndex];
        }

        /// <summary>Weighted random selection from LevelConfig.Enemies.</summary>
        private EnemyEntry PickRandomEnemyEntry()
        {
            if (_config.Enemies == null || _config.Enemies.Count == 0)
                return null;

            int totalWeight = 0;
            foreach (var e in _config.Enemies)
                totalWeight += Mathf.Max(1, e.Weight);

            int roll = Random.Range(0, totalWeight);
            int acc = 0;
            foreach (var e in _config.Enemies)
            {
                acc += Mathf.Max(1, e.Weight);
                if (roll < acc) return e;
            }

            return _config.Enemies[0]; // fallback
        }
    }
}

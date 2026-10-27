using AmericanBattlefront.Core;
using AmericanBattlefront.Environment;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AmericanBattlefront.Core
{
    /// <summary>
    /// Orchestrates the complete level setup sequence.
    /// Place this on a single GameObject in the scene called "GameBootstrap".
    ///
    /// Setup order (from SRS §4.1):
    ///   1. Validate LevelConfig
    ///   2. Reset prior session
    ///   3. Build arena (panorama + floor + props)
    ///   4. Bake NavMesh
    ///   5. Validate spawn points
    ///   6. Enter Ready state
    ///
    /// Call RestartLevel() to cleanly restart from Victory or Defeat.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        // ── Inspector refs ───────────────────────────────────────────────────
        [Header("Level Configuration")]
        [Tooltip("The level to load. Assign a LevelConfig asset here.")]
        [SerializeField] private LevelConfig _levelConfig;

        [Header("Scene References")]
        [SerializeField] private GameStateManager _gameStateManager;
        [SerializeField] private ArenaBuilder _arenaBuilder;

        [Tooltip("Transform of the XR Origin root (NOT the camera). Used to set player start position.")]
        [SerializeField] private Transform _xrOriginRoot;

        [SerializeField] private AmericanBattlefront.Weapons.WeaponHolder _weaponHolder;


        [Tooltip("PlayerBoundary component — constrains player to arena.")]
        [SerializeField] private AmericanBattlefront.Player.PlayerBoundary _playerBoundary;

        [Tooltip("PlayerHealth component on the XR Origin.")]
        [SerializeField] private AmericanBattlefront.Player.PlayerHealth _playerHealth;

        [Tooltip("PlayerReference singleton on the XR Origin.")]
        [SerializeField] private AmericanBattlefront.Player.PlayerReference _playerReference;

        [SerializeField] private AmericanBattlefront.Spawning.WaveDirector _waveDirector;
        [SerializeField] private AmericanBattlefront.Core.XPTracker _xpTracker;
        [SerializeField] private AmericanBattlefront.UI.GameHUD _gameHUD;

        [SerializeField] private AmericanBattlefront.Spawning.SpawnPointRegistry _spawnRegistry;


        [Header("Settings")]
        [Tooltip("If true, setup begins automatically on Start. If false, call BeginSetup() manually.")]
        [SerializeField] private bool _autoStartOnPlay = true;

        [Tooltip("Seconds to wait after entering Ready state before auto-starting combat. 0 = wait for explicit player input.")]
        [SerializeField] private float _autoStartDelay = 0f;

        // ── Runtime state ────────────────────────────────────────────────────
        private bool _isRestarting = false;
        private int _restartSeedOverride = -1;

        // ── Unity lifecycle ──────────────────────────────────────────────────
        private void Start()
        {
            if (_autoStartOnPlay)
                StartCoroutine(SetupSequence());
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Call from a UI button to begin setup when autoStart is off.
        /// </summary>
        public void BeginSetup()
        {
            if (_isRestarting) return; // debounce
            StartCoroutine(SetupSequence());
        }

        /// <summary>
        /// Call from Victory/Defeat UI to restart the level.
        /// Respects the LevelConfig.Restart.LayoutSeedPolicy.
        /// </summary>
        public void RestartLevel()
        {
            if (_isRestarting) return; // debounce
            _isRestarting = true;

            // Reset weapon
            if (_weaponHolder != null)
                _weaponHolder.DetachWeapon();

            if (_playerHealth != null && _levelConfig.Player.RestoreHealthOnRestart)
                _playerHealth.ResetHealth();

            if (_xpTracker != null)
                _xpTracker.ResetXP();

            if (_waveDirector != null)
                _waveDirector.ResetAndStop();



            // Decide seed for next run
            if (_levelConfig != null &&
                _levelConfig.Restart.LayoutSeedPolicy ==
                    RestartConfig.SeedPolicy.GenerateNewSeed)
            {
                _restartSeedOverride = Random.Range(0, int.MaxValue);
                Debug.Log($"[GameBootstrap] Restart with new seed: {_restartSeedOverride}");
            }
            else
            {
                _restartSeedOverride = -1; // use config seed
            }

            StartCoroutine(SetupSequence());
        }

        /// <summary>
        /// Called by UI Start button to move from Ready → Playing.
        /// </summary>
        public void PlayerRequestedStart()
        {
            if (_gameStateManager == null) return;
            _gameStateManager.RequestTransition(GameState.Playing);
        }

        // ── Setup sequence ───────────────────────────────────────────────────

        private IEnumerator SetupSequence()
        {
            // ── Step 0: Validate references ──────────────────────────────────
            if (!ValidateBootstrapReferences())
            {
                _isRestarting = false;
                yield break;
            }

            // ── Step 1: Validate LevelConfig ─────────────────────────────────
            string configError = _levelConfig.Validate();
            if (configError != null)
            {
                Debug.LogError($"[GameBootstrap] LevelConfig validation failed: {configError}");
                _gameStateManager.RequestTransition(GameState.Error);
                _isRestarting = false;
                yield break;
            }

            Debug.Log($"[GameBootstrap] Starting setup for '{_levelConfig.DisplayName}'");

            // ── Step 2: Reset prior session ───────────────────────────────────
            _gameStateManager.ForceReset();
            // ForceReset already sets state to Loading and clears event listeners

            yield return null; // let systems receive the reset

            // ── Step 3: Build arena (panorama + floor + props + NavMesh) ──────
            yield return StartCoroutine(
                _arenaBuilder.BuildAsync(_levelConfig, _restartSeedOverride));

            if (!_arenaBuilder.IsBuilt)
            {
                Debug.LogError("[GameBootstrap] ArenaBuilder.BuildAsync failed.");
                _gameStateManager.RequestTransition(GameState.Error);
                _isRestarting = false;
                yield break;
            }

            // ── Step 4: Position player at configured start ───────────────────
            if (_xrOriginRoot != null)
            {
                _xrOriginRoot.position = _levelConfig.Arena.PlayerStartPosition;
                Debug.Log($"[GameBootstrap] XR Origin moved to {_levelConfig.Arena.PlayerStartPosition}");
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] XR Origin Root not assigned — player start position not applied.");
            }

            // Configure player boundary with arena dimensions
            if (_playerBoundary != null)
                _playerBoundary.Configure(Vector3.zero, _levelConfig.Arena.ArenaDimensions);


            // ── Step 5: Attach and configure weapon ──────────────────────────────
            if (_weaponHolder != null && _levelConfig.Weapon.Prefab != null)
            {
                var weaponCtrl = _weaponHolder.AttachWeapon(_levelConfig.Weapon.Prefab);
                if (weaponCtrl != null)
                    weaponCtrl.Configure(_levelConfig.Weapon);
            }
            else if (_levelConfig.Weapon.Prefab == null)
            {
                Debug.LogWarning("[GameBootstrap] No Weapon.Prefab assigned in LevelConfig — player has no weapon.");
            }

            // ── Step 5b: Configure player health ─────────────────────────────────
            if (_playerHealth != null)
            {
                _playerHealth.Configure(
                    _levelConfig.Player.MaxHealth,
                    _levelConfig.Player.DamageCooldown);
                Debug.Log($"[GameBootstrap] PlayerHealth configured: {_levelConfig.Player.MaxHealth} HP");
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] PlayerHealth not assigned — player cannot take damage.");
            }


            // Placeholder: broadcast setup complete so listeners can init
            // GameEvents will be wired here in future phases

            // ── Step 6: Configure WaveDirector and XP ───────────────────────────
            if (_spawnRegistry == null)
                _spawnRegistry = FindFirstObjectByType<AmericanBattlefront.Spawning.SpawnPointRegistry>();

            if (_gameHUD == null)
                _gameHUD = FindFirstObjectByType<AmericanBattlefront.UI.GameHUD>();

            if (_xpTracker != null)
                _xpTracker.ResetXP();

            if (_waveDirector != null)
            {
                _waveDirector.ResetAndStop();
                _waveDirector.Configure(_levelConfig, _spawnRegistry);
            }
            else
                Debug.LogWarning("[GameBootstrap] WaveDirector not assigned!");


            // ── Step 6: Enter Ready state ─────────────────────────────────────
            _gameStateManager.RequestTransition(GameState.Ready);

            // Show briefing panel now that we're in Ready state
            if (_gameHUD != null)
                _gameHUD.ShowBriefing(_levelConfig);

            Debug.Log($"[GameBootstrap] Setup complete. Seed used: {_arenaBuilder.ActiveSeed}. Awaiting player start.");

            // ── Step 7: Optional auto-start ───────────────────────────────────
            if (_autoStartDelay > 0f)
            {
                yield return new WaitForSeconds(_autoStartDelay);
                if (_gameStateManager.CurrentState == GameState.Ready)
                    _gameStateManager.RequestTransition(GameState.Playing);
            }

            _isRestarting = false;
        }

        // ── Validation ───────────────────────────────────────────────────────

        private bool ValidateBootstrapReferences()
        {
            bool ok = true;

            if (_levelConfig == null)
            {
                Debug.LogError("[GameBootstrap] LevelConfig is not assigned!");
                ok = false;
            }
            if (_gameStateManager == null)
            {
                Debug.LogError("[GameBootstrap] GameStateManager is not assigned!");
                ok = false;
            }
            if (_arenaBuilder == null)
            {
                Debug.LogError("[GameBootstrap] ArenaBuilder is not assigned!");
                ok = false;
            }

            if (!ok && _gameStateManager != null)
                _gameStateManager.RequestTransition(GameState.Error);

            return ok;
        }

        // ── TEMP: Dev shortcut — press Space to skip Ready→Playing ───────────────
        // Remove this block before final Quest build.
        private void Update()
        {
            if (Keyboard.current == null)
                return;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                if (_gameStateManager.CurrentState == GameState.Ready)
                {
                    Debug.Log("[GameBootstrap] DEV: Space pressed — forcing Ready → Playing");
                    _gameStateManager.RequestTransition(GameState.Playing);
                }
            }
        }


#if UNITY_EDITOR
        // ── Editor helper ─────────────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            if (_levelConfig == null) return;

            // Draw arena bounds
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Vector3 arenaSize = new Vector3(
                _levelConfig.Arena.ArenaDimensions.x,
                3f,
                _levelConfig.Arena.ArenaDimensions.y);
            Gizmos.DrawWireCube(Vector3.up * 1.5f, arenaSize);

            // Draw player start
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(_levelConfig.Arena.PlayerStartPosition, 0.3f);

            // Draw protected zones
            Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
            foreach (var zone in _levelConfig.ProtectedZones)
                Gizmos.DrawWireSphere(zone.Centre, zone.Radius);
        }
#endif
    }
}

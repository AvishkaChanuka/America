using AmericanBattlefront.Core;
using AmericanBattlefront.Environment;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AmericanBattlefront.UI
{
    /// <summary>
    /// In-game debug overlay. Reads state every frame and displays it.
    /// Disable the GameObject in release builds.
    /// Can function with a world-space Canvas or screen-space Canvas.
    /// </summary>
    public class DebugPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text _debugText;
        [SerializeField] private GameStateManager _stateMgr;
        [SerializeField] private ArenaBuilder _arenaBuilder;

        [Header("Settings")]
        [SerializeField] private bool _enabled = true;
        [SerializeField] private float _updateInterval = 0.25f; // seconds between refresh

        // runtime
        private float _timer;
        private int _activeEnemies;
        private int _currentWave;
        private int _totalWaves;
        private float _sessionXP;
        private string _lastPlacementLog = "–";

        private void OnEnable()
        {
            GameEvents.OnGameStateChanged += OnStateChanged;
            GameEvents.OnWaveStarted += OnWaveStarted;
            GameEvents.OnEnemyDefeated += OnEnemyDefeated;
            GameEvents.OnXPChanged += OnXPChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStateChanged -= OnStateChanged;
            GameEvents.OnWaveStarted -= OnWaveStarted;
            GameEvents.OnEnemyDefeated -= OnEnemyDefeated;
            GameEvents.OnXPChanged -= OnXPChanged;
        }

        private void Update()
        {
            if (!_enabled || _debugText == null) return;

            _timer += Time.deltaTime;
            if (_timer < _updateInterval) return;
            _timer = 0f;

            Refresh();
        }

        private void Refresh()
        {
            string state = _stateMgr != null ? _stateMgr.CurrentState.ToString() : "–";
            string seed = _arenaBuilder != null ? _arenaBuilder.ActiveSeed.ToString() : "–";
            bool navBuilt = _arenaBuilder != null && _arenaBuilder.IsBuilt;

            _debugText.text =
                $"<b>[ DEBUG ]</b>\n" +
                $"State:    {state}\n" +
                $"Seed:     {seed}\n" +
                $"Wave:     {_currentWave} / {_totalWaves}\n" +
                $"Enemies:  {_activeEnemies} alive\n" +
                $"XP:       {_sessionXP:F0}\n" +
                $"NavMesh:  {(navBuilt ? "BUILT" : "pending")}\n" +
                $"FPS:      {(1f / Time.smoothDeltaTime):F0}";
        }

        // ── Event handlers ───────────────────────────────────────────────────
        private void OnStateChanged(GameState prev, GameState next) => Refresh();

        private void OnWaveStarted(int index, int total)
        {
            _currentWave = index + 1;
            _totalWaves = total;
        }

        private void OnEnemyDefeated(GameObject enemy, float xp)
        {
            _activeEnemies = Mathf.Max(0, _activeEnemies - 1);
        }

        private void OnXPChanged(float total) => _sessionXP = total;

        // Called externally by WaveDirector (Phase 7)
        public void SetActiveEnemyCount(int count) => _activeEnemies = count;
    }
}

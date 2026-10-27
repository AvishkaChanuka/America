using AmericanBattlefront.Core;
using AmericanBattlefront.Spawning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AmericanBattlefront.UI
{
    /// <summary>
    /// Subscribes to GameEvents and updates UI Text elements.
    /// Uses a polling fallback for values that don't have events (e.g. wave enemy count).
    ///
    /// All Text fields are optional — missing ones are silently skipped.
    /// Result panels (Victory/Defeat) are GameObjects toggled on/off.
    ///
    /// For VR: attach this to a World Space Canvas parented to the XR Origin,
    /// positioned at a comfortable arm's-length reading distance.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        // ── Combat HUD refs ───────────────────────────────────────────────────
        [Header("Combat HUD")]
        [SerializeField] private TMP_Text _healthText;
        [SerializeField] private TMP_Text _ammoText;
        [SerializeField] private TMP_Text _waveText;
        [SerializeField] private TMP_Text _enemiesText;
        [SerializeField] private TMP_Text _xpText;
        [SerializeField] private TMP_Text _stateText;

        // ── Result panels ─────────────────────────────────────────────────────
        [Header("Result Panels")]
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private GameObject _defeatPanel;
        [SerializeField] private GameObject _briefingPanel;
        [SerializeField] private TMP_Text _finalXPText;


        // ── Briefing refs ─────────────────────────────────────────────────────
        [Header("Briefing Panel")]
        [SerializeField] private TMP_Text _briefingTitle;
        [SerializeField] private TMP_Text _briefingBody;
        [SerializeField] private TMP_Text _briefingObjective;
        [SerializeField] private TMP_Text _briefingControls;

        // ── Runtime refs ──────────────────────────────────────────────────────
        [Header("Runtime References")]
        [SerializeField] private WaveDirector _waveDirector;

        // ── Cached values ─────────────────────────────────────────────────────
        private float _currentHealth;
        private float _maxHealth;
        private int _currentMag;
        private int _currentReserve;
        private float _sessionXP;
        private bool _isReloading;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable()
        {
            GameEvents.OnPlayerHealthChanged += OnHealthChanged;
            GameEvents.OnAmmoChanged += OnAmmoChanged;
            GameEvents.OnWaveStarted += OnWaveStarted;
            GameEvents.OnWaveCleared += OnWaveCleared;
            GameEvents.OnXPChanged += OnXPChanged;
            GameEvents.OnGameStateChanged += OnGameStateChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerHealthChanged -= OnHealthChanged;
            GameEvents.OnAmmoChanged -= OnAmmoChanged;
            GameEvents.OnWaveStarted -= OnWaveStarted;
            GameEvents.OnWaveCleared -= OnWaveCleared;
            GameEvents.OnXPChanged -= OnXPChanged;
            GameEvents.OnGameStateChanged -= OnGameStateChanged;
        }

        private void Update()
        {
            // Refresh enemy count from WaveDirector (no event for this)
            if (_waveDirector != null && _enemiesText != null)
                _enemiesText.text = $"Enemies: {_waveDirector.AliveEnemies}";
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Populate briefing panel text from LevelConfig. Called by GameBootstrap.</summary>
        public void ShowBriefing(LevelConfig config)
        {
            SetPanelsActive(briefing: true, victory: false, defeat: false);

            if (_briefingTitle != null) _briefingTitle.text = config.DisplayName;
            if (_briefingBody != null) _briefingBody.text = config.ShortBriefing;
            if (_briefingObjective != null) _briefingObjective.text = $"Objective: {config.ObjectiveText}";
            if (_briefingControls != null) _briefingControls.text = config.ControlHelp;
        }

        /// <summary>Hide briefing, show combat HUD. Called when Playing begins.</summary>
        public void ShowCombatHUD()
        {
            SetPanelsActive(briefing: false, victory: false, defeat: false);
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnHealthChanged(float current, float max)
        {
            _currentHealth = current;
            _maxHealth = max;
            if (_healthText != null)
                _healthText.text = $"HP  {current:F0} / {max:F0}";
        }

        private void OnAmmoChanged(int mag, int reserve)
        {
            _currentMag = mag;
            _currentReserve = reserve;
            if (_ammoText != null)
                _ammoText.text = mag > 0
                    ? $"Ammo  {mag} | {reserve}"
                    : "<color=red>RELOAD</color>";
        }

        private void OnWaveStarted(int index, int total)
        {
            if (_waveText != null)
                _waveText.text = $"Wave  {index + 1} / {total}";
        }

        private void OnWaveCleared(int index)
        {
            if (_waveText != null)
                _waveText.text = $"Wave {index + 1} — CLEARED";
        }

        private void OnXPChanged(float total)
        {
            _sessionXP = total;
            if (_xpText != null)
                _xpText.text = $"XP  {total:F0}";
        }

        private void OnGameStateChanged(GameState prev, GameState next)
        {
            if (_stateText != null)
                _stateText.text = next.ToString();

            switch (next)
            {
                case GameState.Ready:
                    // Briefing is already shown by GameBootstrap via ShowBriefing()
                    break;

                case GameState.Playing:
                    ShowCombatHUD();
                    break;

                case GameState.WaveBreak:
                    if (_waveText != null)
                        _waveText.text += "\n<size=70%>Next wave incoming…</size>";
                    break;

                case GameState.Victory:
                    SetPanelsActive(briefing: false, victory: true, defeat: false);
                    if (_finalXPText != null)
                        _finalXPText.text = $"Total XP: {_sessionXP:F0}";
                    break;

                case GameState.Defeat:
                    SetPanelsActive(briefing: false, victory: false, defeat: true);
                    if (_finalXPText != null)
                        _finalXPText.text = $"XP earned: {_sessionXP:F0}";
                    break;

                case GameState.Loading:
                    SetPanelsActive(briefing: false, victory: false, defeat: false);
                    break;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetPanelsActive(bool briefing, bool victory, bool defeat)
        {
            if (_briefingPanel != null) _briefingPanel.SetActive(briefing);
            if (_victoryPanel != null) _victoryPanel.SetActive(victory);
            if (_defeatPanel != null) _defeatPanel.SetActive(defeat);
        }
    }
}

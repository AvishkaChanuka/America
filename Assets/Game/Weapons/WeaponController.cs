using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Weapons
{
    /// <summary>
    /// Hitscan ranged weapon.
    ///
    /// Setup order:
    ///   1. Attach to the weapon prefab's root.
    ///   2. Assign MuzzleTransform (child empty called "Muzzle" pointing forward).
    ///   3. Assign FireAction and ReloadAction InputActionReferences.
    ///   4. GameBootstrap calls Configure(WeaponConfig) after instantiation.
    ///
    /// Reads GameStateManager.CanFire each frame — no firing during Victory/Defeat/Loading.
    /// All ammo changes broadcast through GameEvents.RaiseAmmoChanged().
    /// Death is handled by IDamageable targets; this script only queries hits.
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────
        [Header("Transforms")]
        [Tooltip("Empty child transform pointing forward from the muzzle. Required.")]
        [SerializeField] private Transform _muzzleTransform;

        [Header("Input Actions (assign from XRI Default Input Actions)")]
        [Tooltip("XRI Right Interaction / Activate — right trigger button.")]
        [SerializeField] private InputActionReference _fireAction;

        [Tooltip("Any button action for reload — e.g. XRI Right Interaction / Select (grip), or add a new binding.")]
        [SerializeField] private InputActionReference _reloadAction;

        [Header("Feedback (all optional)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _fireSFX;
        [SerializeField] private AudioClip _emptySFX;
        [SerializeField] private AudioClip _reloadStartSFX;
        [SerializeField] private AudioClip _reloadCompleteSFX;
        [SerializeField] private ParticleSystem _muzzleFlash;

        [Header("Debug")]
        [SerializeField] private bool _drawDebugRay = true;

        // ── Runtime state ─────────────────────────────────────────────────────
        private float _damage;
        private float _range;
        private float _fireInterval;
        private int _magazineSize;
        private int _reserveAmmo;
        private float _reloadTime;
        private float _spreadAngle;
        private LayerMask _hitLayers;

        private int _currentMagazine;
        private int _currentReserve;
        private bool _isReloading;
        private float _nextFireTime;
        private bool _isConfigured;

        private LineRenderer _tracerLine; // lazy-created for debug tracer


        // Rising-edge detection (fire once per press, not per frame held)
        private bool _firePressedLastFrame;

        // ── Public state ──────────────────────────────────────────────────────
        public bool IsReloading => _isReloading;
        public int CurrentAmmo => _currentMagazine;
        public int ReserveAmmo => _currentReserve;

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void OnEnable()
        {
            _fireAction?.action.Enable();
            _reloadAction?.action.Enable();
        }

        private void OnDisable()
        {
            _fireAction?.action.Disable();
            _reloadAction?.action.Disable();
        }

        private void Update()
        {
            if (!_isConfigured) return;

            HandleFireInput();
            HandleReloadInput();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Apply weapon values from LevelConfig. Called by GameBootstrap after
        /// the weapon prefab is instantiated. Resets ammo to full magazine + reserve.
        /// </summary>
        public void Configure(WeaponConfig cfg)
        {
            _damage = cfg.Damage;
            _range = cfg.Range;
            _fireInterval = cfg.FireInterval;
            _magazineSize = cfg.MagazineSize;
            _reserveAmmo = cfg.ReserveAmmo;
            _reloadTime = cfg.ReloadTime;
            _spreadAngle = cfg.SpreadAngle;
            _hitLayers = cfg.HitLayers;

            ResetAmmo();
            _isReloading = false;
            _nextFireTime = 0f;
            _isConfigured = true;

            Debug.Log($"[WeaponController] Configured: dmg={_damage} range={_range} mag={_magazineSize} reserve={_reserveAmmo}");
        }

        /// <summary>
        /// Restore ammo to full magazine and configured reserve. Called on restart.
        /// </summary>
        public void ResetAmmo()
        {
            _currentMagazine = _magazineSize;
            _currentReserve = _reserveAmmo;
            _isReloading = false;
            StopAllCoroutines();
            GameEvents.RaiseAmmoChanged(_currentMagazine, _currentReserve);
        }

        // ── Input handling ────────────────────────────────────────────────────
        private void HandleFireInput()
        {
            if (_fireAction == null) return;

            bool isPressed = _fireAction.action.IsPressed();

            // Rising edge: fire once per press (semi-auto behaviour)
            bool justPressed = isPressed && !_firePressedLastFrame;
            _firePressedLastFrame = isPressed;

            if (!justPressed) return;

            TryFire();
        }

        private void HandleReloadInput()
        {
            if (_reloadAction == null) return;

            if (_reloadAction.action.WasPressedThisFrame())
                TryStartReload();
        }

        // ── Fire logic ────────────────────────────────────────────────────────
        private void TryFire()
        {
            // Guard: only fire in Playing state
            if (GameStateManager.Instance != null && !GameStateManager.Instance.CanFire)
                return;

            // Guard: not reloading
            if (_isReloading)
                return;

            // Guard: fire rate
            if (Time.time < _nextFireTime)
                return;

            // Guard: ammo
            if (_currentMagazine <= 0)
            {
                PlaySound(_emptySFX);
                Debug.Log("[WeaponController] Click — magazine empty.");
                return;
            }

            // Accept the shot
            _currentMagazine--;
            _nextFireTime = Time.time + _fireInterval;

            PerformHitscan();
            PlayMuzzleFlash();
            PlaySound(_fireSFX);

            GameEvents.RaiseWeaponFired();
            GameEvents.RaiseAmmoChanged(_currentMagazine, _currentReserve);

            // Auto-reload when magazine hits zero
            if (_currentMagazine <= 0 && _currentReserve > 0)
                TryStartReload();
        }

        private void PerformHitscan()
        {
            if (_muzzleTransform == null)
                Debug.LogWarning("[WeaponController] MuzzleTransform not assigned — using weapon root.");

            Transform origin = _muzzleTransform != null ? _muzzleTransform : transform;
            Vector3 rayOrigin = origin.position;
            Vector3 rayDir = ApplySpread(origin.forward);

            // Scene-view ray (editor only)
            Debug.DrawRay(rayOrigin, rayDir * _range, Color.yellow, 0.5f);

            // Game-view tracer: brief bright line using a coroutine + LineRenderer
            if (_drawDebugRay)
                StartCoroutine(DrawGameViewTracer(rayOrigin, rayOrigin + rayDir * _range));

            if (!Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, _range, _hitLayers,
                                 QueryTriggerInteraction.Ignore))
            {
                Debug.Log("[WeaponController] Fired — missed (no hit within range).");
                return;
            }

            Debug.Log($"[WeaponController] Hit: {hit.collider.name} at {hit.distance:F1}m");

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target == null)
            {
                Debug.Log($"[WeaponController] Hit '{hit.collider.name}' — no IDamageable found.");
                return;
            }

            if (!target.IsAlive) return;

            var dmgInfo = new DamageInfo(
                amount: _damage,
                hitPoint: hit.point,
                instigator: gameObject);

            target.TakeDamage(dmgInfo);
        }

        /// <summary>Draws a bright line in Game view for 0.1s after each shot.</summary>
        private IEnumerator DrawGameViewTracer(Vector3 from, Vector3 to)
        {
            // Lazy-create a LineRenderer on a child object
            if (_tracerLine == null)
            {
                var go = new GameObject("_Tracer");
                go.transform.SetParent(transform);
                _tracerLine = go.AddComponent<LineRenderer>();
                _tracerLine.positionCount = 2;
                _tracerLine.startWidth = 0.01f;
                _tracerLine.endWidth = 0.003f;
                _tracerLine.useWorldSpace = true;
                _tracerLine.material = new Material(Shader.Find("Sprites/Default"));
                _tracerLine.startColor = new Color(1f, 0.9f, 0.3f, 1f); // yellow
                _tracerLine.endColor = new Color(1f, 0.3f, 0f, 0f);   // fade to transparent
            }

            _tracerLine.enabled = true;
            _tracerLine.SetPosition(0, from);
            _tracerLine.SetPosition(1, to);

            yield return new WaitForSeconds(0.08f);
            _tracerLine.enabled = false;
        }


        private Vector3 ApplySpread(Vector3 forward)
        {
            if (_spreadAngle <= 0f) return forward;

            // Random cone spread
            float halfAngle = _spreadAngle * 0.5f * Mathf.Deg2Rad;
            float angle = Random.Range(0f, halfAngle);
            float rotation = Random.Range(0f, 360f) * Mathf.Deg2Rad;

            // Perpendicular vector in the spread plane
            Vector3 perp = Vector3.Cross(forward, Vector3.up);
            if (perp.sqrMagnitude < 0.001f)
                perp = Vector3.Cross(forward, Vector3.right);
            perp.Normalize();

            Vector3 spread = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, forward) *
                             Quaternion.AngleAxis(rotation * Mathf.Rad2Deg, Vector3.up) *
                             perp;

            return (forward + spread * Mathf.Sin(angle)).normalized;
        }

        // ── Reload logic ──────────────────────────────────────────────────────
        private void TryStartReload()
        {
            if (_isReloading) return;
            if (_currentMagazine >= _magazineSize) return; // already full
            if (_currentReserve <= 0) return;              // no ammo to reload with

            StartCoroutine(ReloadCoroutine());
        }

        private IEnumerator ReloadCoroutine()
        {
            _isReloading = true;
            Debug.Log($"[WeaponController] Reloading… ({_reloadTime:F1}s)");
            PlaySound(_reloadStartSFX);

            yield return new WaitForSeconds(_reloadTime);

            // Check we're still in a valid state after waiting
            if (GameStateManager.Instance != null && GameStateManager.Instance.IsTerminal)
            {
                _isReloading = false;
                yield break;
            }

            // Refill magazine from reserve
            int needed = _magazineSize - _currentMagazine;
            int taken = Mathf.Min(needed, _currentReserve);
            _currentMagazine += taken;
            _currentReserve -= taken;

            _isReloading = false;

            PlaySound(_reloadCompleteSFX);
            GameEvents.RaiseAmmoChanged(_currentMagazine, _currentReserve);
            Debug.Log($"[WeaponController] Reload complete. Mag={_currentMagazine} Reserve={_currentReserve}");
        }

        // ── Feedback helpers ──────────────────────────────────────────────────
        private void PlaySound(AudioClip clip)
        {
            if (_audioSource == null || clip == null) return;
            _audioSource.PlayOneShot(clip);
        }

        private void PlayMuzzleFlash()
        {
            if (_muzzleFlash == null) return;
            _muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _muzzleFlash.Play();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_muzzleTransform == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(_muzzleTransform.position, _muzzleTransform.forward * 3f);
        }
#endif
    }
}

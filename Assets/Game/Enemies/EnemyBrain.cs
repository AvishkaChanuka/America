using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using AmericanBattlefront.Core;
using AmericanBattlefront.Player;

namespace AmericanBattlefront.Enemies
{
    /// <summary>
    /// NavMesh-based enemy AI with explicit states.
    ///
    /// States:  Idle → Approach → Attack → Recover → (back to Approach)
    ///          Any  → Dead     (on EnemyHealth.OnDied)
    ///
    /// Configure() is called by WaveDirector immediately after spawning.
    /// Destination is updated every DEST_UPDATE_INTERVAL seconds (not per frame).
    /// NavMesh is never rebuilt here.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EnemyHealth))]
    public class EnemyBrain : MonoBehaviour
    {
        // ── State enum ─────────────────────────────────────────────────────────
        private enum EnemyState { Idle, Approach, Attack, Recover, Dead }

        // ── Config defaults (overridden by Configure()) ────────────────────────
        private float _moveSpeed = 2.5f;
        private float _detectionRange = 15f;
        private float _attackRange = 1.8f;
        private float _attackDamage = 10f;
        private float _attackCooldown = 2f;
        private float _recoverDuration = 0.5f;
        private bool _requireLOS = false;
        private float _spawnDelay = 0.5f;  // brief idle before first approach

        // ── Destination update throttle ────────────────────────────────────────
        private const float DEST_UPDATE_INTERVAL = 0.4f;
        private float _destTimer = 0f;

        // ── Components ─────────────────────────────────────────────────────────
        private NavMeshAgent _agent;
        private EnemyHealth _health;

        private Animator _animator;

        // Animator parameter hashes (fast and garbage-free)
        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimShoot = Animator.StringToHash("Shoot");
        private static readonly int AnimDie = Animator.StringToHash("Die");

        [Header("Ranged Weapon")]
        [Tooltip("Tip of the enemy musket barrel where bullets originate.")]
        [SerializeField] private Transform _muzzlePoint;
        [SerializeField] private LineRenderer _tracerLine;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _gunshotSFX;



        // ── State ──────────────────────────────────────────────────────────────
        private EnemyState _state = EnemyState.Idle;
        private float _stateTimer = 0f;
        private bool _attackCoroutineRunning = false;

        private Coroutine _attackCoroutine;


        // ── Player reference (cached at Start) ────────────────────────────────
        private Transform _playerTransform;
        private IDamageable _playerDamageable;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<EnemyHealth>();
            _animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            _health.OnDied += HandleDeath;
        }

        private void OnDisable()
        {
            _health.OnDied -= HandleDeath;
        }

        private void Start()
        {
            CachePlayerReference();

            _agent.speed = _moveSpeed;
            _agent.stoppingDistance = _attackRange * 0.8f;

            EnterState(EnemyState.Idle);
        }

        private void Update()
        {
            if (_state == EnemyState.Dead) return;

            // Pause AI in non-playing states
            if (GameStateManager.Instance != null && !GameStateManager.Instance.IsPlaying)
            {
                if (_agent.isOnNavMesh && !_agent.isStopped)
                    _agent.isStopped = true;
                return;
            }

            if (_state == EnemyState.Approach && _agent.isOnNavMesh && _agent.isStopped)
                _agent.isStopped = false;

            _stateTimer -= Time.deltaTime;
            _destTimer -= Time.deltaTime;

            switch (_state)
            {
                case EnemyState.Idle: UpdateIdle(); break;
                case EnemyState.Approach: UpdateApproach(); break;
                case EnemyState.Attack: UpdateAttack(); break;
                case EnemyState.Recover: UpdateRecover(); break;
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called by WaveDirector immediately after Instantiate().
        /// Sets all combat values from LevelConfig EnemyEntry.
        /// </summary>
        public void Configure(
            float moveSpeed, float detectionRange, float attackRange,
            float attackDamage, float attackCooldown, float recoverDuration,
            bool requireLOS)
        {
            _moveSpeed = moveSpeed;
            _detectionRange = detectionRange;
            _attackRange = attackRange;
            _attackDamage = attackDamage;
            _attackCooldown = attackCooldown;
            _recoverDuration = recoverDuration;
            _requireLOS = requireLOS;

            if (_agent != null)
            {
                _agent.speed = _moveSpeed;
                _agent.stoppingDistance = _attackRange * 0.8f;
            }
        }

        // ── State updates ──────────────────────────────────────────────────────

        private void UpdateIdle()
        {
            if (_stateTimer <= 0f)
                EnterState(EnemyState.Approach);
        }

        private void UpdateApproach()
        {
            // Retry player reference each frame in case it wasn't ready at Start()
            if (_playerTransform == null)
            {
                CachePlayerReference();
                if (_playerTransform == null)
                {
                    Debug.LogWarning($"[EnemyBrain:{name}] UpdateApproach: _playerTransform still null. Is PlayerReference in scene and XR Origin tagged 'Player'?");
                    return;
                }
            }

            // If agent fell off NavMesh (e.g. placed before bake), warp it back
            if (!_agent.isOnNavMesh)
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(
                        transform.position, out UnityEngine.AI.NavMeshHit navHit, 3f,
                        UnityEngine.AI.NavMesh.AllAreas))
                {
                    _agent.Warp(navHit.position);
                    Debug.Log($"[EnemyBrain:{name}] Warped onto NavMesh at {navHit.position}");
                }
                else
                {
                    Debug.LogWarning($"[EnemyBrain:{name}] Cannot find NavMesh within 3m — is NavMesh baked?");
                    return;
                }
            }

            if (_animator != null)
                _animator.SetFloat(AnimSpeed, _agent.velocity.magnitude);

            float dist = DistanceToPlayer();

            // Throttled destination update (not every frame — Quest performance)
            if (_destTimer <= 0f)
            {
                _destTimer = DEST_UPDATE_INTERVAL;
                Vector3 targetPos = _playerTransform.position;
                if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
                else
                    _agent.SetDestination(targetPos);
            }


            // Transition to Attack when close enough
            if (dist <= _attackRange && CanSeePlayer())
                EnterState(EnemyState.Attack);
        }

        private void UpdateAttack()
        {
            if (_playerTransform == null) return;

            // Continuously track and aim at the player
            FacePlayerSmooth();

            // If player ran out of ranged distance, advance again
            float dist = DistanceToPlayer();
            if (dist > _attackRange * 1.35f)
                EnterState(EnemyState.Approach);
        }

        private void FacePlayerSmooth()
        {
            if (_playerTransform == null) return;
            Vector3 dir = _playerTransform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 150f * Time.deltaTime);
            }
        }


        private void UpdateRecover()
        {
            if (_stateTimer <= 0f)
                EnterState(EnemyState.Approach);
        }

        // ── State transitions ──────────────────────────────────────────────────

        private void EnterState(EnemyState next)
        {
            if (_state == EnemyState.Dead && next != EnemyState.Dead) return;

            EnemyState prev = _state;
            _state = next;

            switch (next)
            {
                case EnemyState.Idle:
                    _stateTimer = _spawnDelay;
                    StopAgentMovement();
                    break;

                case EnemyState.Approach:
                    _destTimer = 0f;
                    if (_attackCoroutine != null)
                    {
                        StopCoroutine(_attackCoroutine);
                        _attackCoroutine = null;
                        _attackCoroutineRunning = false;
                    }
                    if (_agent.isOnNavMesh)
                        _agent.isStopped = false;
                    break;

                case EnemyState.Attack:
                    StopAgentMovement();
                    if (_animator != null)
                        _animator.SetTrigger(AnimShoot);

                    if (!_attackCoroutineRunning)
                        _attackCoroutine = StartCoroutine(AttackCoroutine());
                    break;

                case EnemyState.Recover:
                    _stateTimer = _recoverDuration;
                    StopAgentMovement();
                    break;

                case EnemyState.Dead:
                    StopAgentMovement();
                    _agent.enabled = false;
                    if (_animator != null)
                        _animator.SetTrigger(AnimDie);

                    StopAllCoroutines();
                    StartCoroutine(DespawnCoroutine(3.5f)); // Give 3.5s for death animation
                    break;

            }

            if (prev != next)
                Debug.Log($"[EnemyBrain:{name}] {prev} → {next}");
        }

        // ── Attack coroutine ───────────────────────────────────────────────────

        private IEnumerator AttackCoroutine()
        {
            _attackCoroutineRunning = true;

            // Aim wind-up (0.45s to synchronize with the shooting animation)
            yield return new WaitForSeconds(0.45f);

            if (_state == EnemyState.Dead)
            {
                _attackCoroutineRunning = false;
                yield break;
            }

            // ── FIRE MUSKET ──────────────────────────────────────────────────
            PerformRangedShot();

            // Musket reload / cooldown period
            yield return new WaitForSeconds(Mathf.Max(1.5f, _attackCooldown - 0.45f));

            _attackCoroutineRunning = false;
            _attackCoroutine = null;

            if (_state == EnemyState.Attack)
                EnterState(EnemyState.Recover);
        }

        private void PerformRangedShot()
        {
            if (_playerTransform == null) return;

            Vector3 origin = _muzzlePoint != null ? _muzzlePoint.position : transform.position + Vector3.up * 1.3f;
            Vector3 target = _playerTransform.position;
            Vector3 dir = (target - origin).normalized;
            float maxDist = Vector3.Distance(origin, target) + 1f;

            // Ignore enemy layer so enemy doesn't hit himself
            int layerMask = ~LayerMask.GetMask("Enemy");

            bool blockedByCover = false;
            Vector3 hitPoint = target;

            // Check if bullet trajectory hits cover (walls, barricades)
            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, layerMask))
            {
                hitPoint = hit.point;

                // If hit object is not the player, cover protected the player!
                if (!hit.collider.CompareTag("Player") && hit.collider.GetComponentInParent<PlayerHealth>() == null)
                {
                    blockedByCover = true;
                    Debug.Log($"[EnemyBrain:{name}] Musket shot hit cover: '{hit.collider.name}' (Player protected!)");
                }
            }

            // Optional visual tracer line
            if (_tracerLine != null)
                StartCoroutine(DrawShotTracer(origin, hitPoint));

            // Sound
            if (_audioSource != null && _gunshotSFX != null)
                _audioSource.PlayOneShot(_gunshotSFX);

            // Deal damage if player is exposed
            if (!blockedByCover)
            {
                var damageable = _playerDamageable ?? PlayerReference.Instance?.PlayerDamageable;
                if (damageable != null && damageable.IsAlive)
                {
                    damageable.TakeDamage(new DamageInfo(_attackDamage, hitPoint, gameObject));
                    Debug.Log($"[EnemyBrain:{name}] Musket shot HIT player for {_attackDamage} damage!");
                }
            }
        }

        private IEnumerator DrawShotTracer(Vector3 start, Vector3 end)
        {
            _tracerLine.enabled = true;
            _tracerLine.SetPosition(0, start);
            _tracerLine.SetPosition(1, end);
            yield return new WaitForSeconds(0.06f);
            _tracerLine.enabled = false;
        }


        // ── Death ──────────────────────────────────────────────────────────────

        private void HandleDeath()
        {
            if (_state == EnemyState.Dead) return;
            EnterState(EnemyState.Dead);
        }

        private IEnumerator DespawnCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            gameObject.SetActive(false);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void CachePlayerReference()
        {
            if (PlayerReference.Instance == null)
            {
                // Don't spam — only log once every 2s
                return;
            }

            _playerTransform  = PlayerReference.Instance.PlayerTransform;
            _playerDamageable = PlayerReference.Instance.PlayerDamageable;

            if (_playerTransform != null)
                Debug.Log($"[EnemyBrain:{name}] PlayerTransform cached: {_playerTransform.name}");
            else
                Debug.LogWarning($"[EnemyBrain:{name}] PlayerReference exists but PlayerTransform is null. Check the 'Player Transform' field on PlayerReference component.");
        }

        private float DistanceToPlayer()
        {
            if (_playerTransform == null) return float.MaxValue;
            // Flat XZ distance only — more reliable for floor-level combat
            Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
            Vector3 b = new Vector3(_playerTransform.position.x, 0f, _playerTransform.position.z);
            return Vector3.Distance(a, b);
        }

        private bool CanSeePlayer()
        {
            if (!_requireLOS) return true;
            if (_playerTransform == null) return false;

            Vector3 eyePos = transform.position + Vector3.up * 1.0f;
            Vector3 dir = (_playerTransform.position + Vector3.up * 0.8f - eyePos).normalized;
            float dist = Vector3.Distance(eyePos, _playerTransform.position);

            if (Physics.Raycast(eyePos, dir, out RaycastHit hit, dist + 0.5f))
            {
                // LOS confirmed if ray reaches the player
                return hit.collider.CompareTag("Player") ||
                       hit.collider.GetComponentInParent<PlayerHealth>() != null;
            }

            return true; // nothing in the way — can see
        }

        private void FacePlayer()
        {
            if (_playerTransform == null) return;
            Vector3 dir = (_playerTransform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        private void StopAgentMovement()
        {
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
            if (_animator != null)
                _animator.SetFloat(AnimSpeed, 0f);
        }


#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Detection range
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, _detectionRange);

            // Attack range
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _attackRange);
        }
#endif
    }
}

using UnityEngine;
using AmericanBattlefront.Core;

namespace AmericanBattlefront.Player
{
    /// <summary>
    /// Lightweight singleton placed on the XR Origin root.
    /// Enemies read PlayerTransform and PlayerDamageable from here
    /// instead of doing FindObjectOfType every frame.
    ///
    /// Assign in Inspector: PlayerTransform = XR Origin root transform,
    /// PlayerHealthComponent will self-register via PlayerHealth.Awake().
    /// </summary>
    public class PlayerReference : MonoBehaviour
    {
        public static PlayerReference Instance { get; private set; }

        [Tooltip("The XR Origin root transform. Used by enemies for distance checks.")]
        [SerializeField] private Transform _playerTransform;

        // Set automatically by PlayerHealth.Awake()
        private IDamageable _playerDamageable;
        public IDamageable PlayerDamageable
        {
            get
            {
                if (_playerDamageable != null) return _playerDamageable;
                _playerDamageable = GetComponentInChildren<PlayerHealth>();
                if (_playerDamageable == null)
                    _playerDamageable = FindFirstObjectByType<PlayerHealth>();
                return _playerDamageable;
            }
            set => _playerDamageable = value;
        }

        public PlayerHealth PlayerHealthComponent { get; set; }


        public Transform PlayerTransform
        {
            get
            {
                if (_playerTransform != null) return _playerTransform;
                if (Camera.main != null) return Camera.main.transform;
                return transform;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // Automatically target the Main Camera (head) if not explicitly set
            if (_playerTransform == null || _playerTransform == transform)
            {
                Camera cam = GetComponentInChildren<Camera>();
                if (cam != null)
                    _playerTransform = cam.transform;
                else if (Camera.main != null)
                    _playerTransform = Camera.main.transform;
            }
        }


        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}

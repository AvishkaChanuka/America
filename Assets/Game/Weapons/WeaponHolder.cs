using UnityEngine;

namespace AmericanBattlefront.Weapons
{
    /// <summary>
    /// Parents the active weapon to the right controller's attach point.
    /// Place this component on the same GameObject as GameBootstrap, or on
    /// a dedicated "WeaponManager" GameObject in the scene.
    ///
    /// The attach point should be a child of the Right Controller transform
    /// inside the XR Origin hierarchy — name it "WeaponAttach".
    /// </summary>
    public class WeaponHolder : MonoBehaviour
    {
        [Tooltip("The 'WeaponAttach' empty child of the Right Controller inside XR Origin.")]
        [SerializeField] private Transform _weaponAttachPoint;

        [Tooltip("Local position offset for fine-tuning the weapon in-hand.")]
        [SerializeField] private Vector3 _positionOffset = new Vector3(0f, -0.02f, 0.05f);

        [Tooltip("Local rotation offset (Euler angles) for fine-tuning the weapon grip.")]
        [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

        private GameObject _currentWeapon;
        public WeaponController CurrentWeaponController { get; private set; }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Instantiate the weapon prefab and parent it to the attach point.
        /// Destroys any previously held weapon first.
        /// </summary>
        public WeaponController AttachWeapon(GameObject weaponPrefab)
        {
            // Clean up previous weapon
            if (_currentWeapon != null)
            {
                Destroy(_currentWeapon);
                _currentWeapon = null;
                CurrentWeaponController = null;
            }

            if (weaponPrefab == null)
            {
                Debug.LogWarning("[WeaponHolder] weaponPrefab is null — no weapon equipped.");
                return null;
            }

            if (_weaponAttachPoint == null)
            {
                Debug.LogError("[WeaponHolder] WeaponAttachPoint not assigned!");
                return null;
            }

            // Instantiate and parent
            _currentWeapon = Instantiate(weaponPrefab, _weaponAttachPoint);
            _currentWeapon.transform.localPosition = _positionOffset;
            _currentWeapon.transform.localRotation = Quaternion.Euler(_rotationOffset);
            _currentWeapon.name = "Weapon_Active";

            CurrentWeaponController = _currentWeapon.GetComponent<WeaponController>();

            if (CurrentWeaponController == null)
                Debug.LogError("[WeaponHolder] Weapon prefab has no WeaponController component!");

            Debug.Log($"[WeaponHolder] Weapon '{weaponPrefab.name}' attached to {_weaponAttachPoint.name}");
            return CurrentWeaponController;
        }

        /// <summary>
        /// Detach and destroy the current weapon (called on level teardown / restart).
        /// </summary>
        public void DetachWeapon()
        {
            if (_currentWeapon != null)
            {
                Destroy(_currentWeapon);
                _currentWeapon = null;
                CurrentWeaponController = null;
            }
        }
    }
}

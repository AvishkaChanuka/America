using UnityEngine;

public class VRHUDController : MonoBehaviour
{
    [Header("Tracking References")]
    [Tooltip("Assign the Main Camera or Center Eye Anchor here.")]
    [SerializeField] private Transform vrCamera;

    [Header("Position Tuning")]
    [Tooltip("How far in front of the player the HUD floats.")]
    [SerializeField] private float distanceFromCamera = 1.5f;
    [Tooltip("Smoothing speed for position translation.")]
    [SerializeField] private float positionFollowSpeed = 5.0f;

    [Header("Rotation & UX Tuning")]
    [Tooltip("Smoothing speed for rotation adjustment.")]
    [SerializeField] private float rotationFollowSpeed = 4.0f;
    [Tooltip("The angle (in degrees) the player can look away before the HUD begins rotating to catch up.")]
    [SerializeField] private float rotationDeadzoneDegrees = 15f;

    private void Start()
    {
        // If not assigned in inspector, fallback to the main camera
        if (vrCamera == null && Camera.main != null)
        {
            vrCamera = Camera.main.transform;
        }

        // Snap immediately to the initial camera view on start to avoid a long drift-in
        if (vrCamera != null)
        {
            Vector3 startPos = vrCamera.position + (vrCamera.forward * distanceFromCamera);
            transform.position = startPos;
            transform.rotation = vrCamera.rotation;
        }
    }

    // LateUpdate prevents the micro-jitter commonly caused by Update loop timing mismatches with VR tracking
    private void LateUpdate()
    {
        if (vrCamera == null) return;

        HandleSmoothPosition();
        HandleSmoothRotationUX();
    }

    private void HandleSmoothPosition()
    {
        // Calculate the ideal target position directly ahead of the VR camera
        Vector3 targetPosition = vrCamera.position + (vrCamera.forward * distanceFromCamera);

        // Smoothly slide the position to match camera translation
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * positionFollowSpeed);
    }

    private void HandleSmoothRotationUX()
    {
        // Check the angle difference between the current HUD orientation and the camera's looking direction
        float angleDifference = Quaternion.Angle(transform.rotation, vrCamera.rotation);

        // HIGH UX: If the player's head rotation is within the deadzone, do not fight their eyes. 
        // Only interpolate rotation if they look past the threshold.
        if (angleDifference > rotationDeadzoneDegrees)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, vrCamera.rotation, Time.deltaTime * rotationFollowSpeed);
        }
    }
}

using Unity.Netcode;
using UnityEngine;

public class PhoneTargetHandler : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;   // Assign your main player Camera
    [SerializeField] private PlayerLook playerLook; // For Pitch access (owner only)

    // BAKED position settings (hidden from Inspector)
    private const float offsetDistance   = 0.2f;   // forward from camera
    private const float horizontalOffset = 0.2f;   // right(+)/left(-)
    private const float verticalOffset   = -0.06f; // up(+)/down(-)

    [Header("Smoothing & Clamp")]
    [SerializeField] private float rotationSmoothSpeed = 10f; // inertia of look-induced rotation
    [SerializeField] private float maxPitchUp = 45f;          // clamp up
    [SerializeField] private float maxPitchDown = -45f;       // clamp down (negative)

    [Header("IK Anchor (child of PhoneTarget)")]
    [SerializeField] private string ikAnchorName = "IKAnchor";

    private Transform phoneTarget;
    private Transform ikAnchor;
    private bool isPhoneActive = false;
    private Quaternion smoothedRotation;
    private bool _initialized; // lazy init for smoothing

    public Transform Target   => phoneTarget;
    public Transform IKAnchor => ikAnchor;
    public bool IsActive      => isPhoneActive;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner) return;

        // Create PhoneTarget at scene root (not parented) to avoid inheriting body transforms
        GameObject targetObj = new GameObject("PhoneTarget");
        phoneTarget = targetObj.transform;
        phoneTarget.parent = null;
        phoneTarget.gameObject.SetActive(false);

        CreateOrFindIKAnchor();
        // Wait to init smoothing until first Update (camera/look can be fully ready)
        _initialized = false;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (playerCamera == null || playerLook == null || phoneTarget == null) return;

        // Lazy-initialize smoothing to the current aim exactly once
        if (!_initialized)
        {
            float initPitch = Mathf.Clamp(playerLook.Pitch, maxPitchDown, maxPitchUp);
            smoothedRotation = Quaternion.Euler(initPitch, playerCamera.transform.eulerAngles.y, 0f);
            _initialized = true;
        }

        // --- Always update transform, even while hidden/disabled ---
        float pitch = Mathf.Clamp(playerLook.Pitch, maxPitchDown, maxPitchUp);
        Quaternion targetRotation = Quaternion.Euler(pitch, playerCamera.transform.eulerAngles.y, 0f);

        // Rotation smoothing (used to compute offset directions)
        smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, rotationSmoothSpeed * Time.deltaTime);

        // Position uses current camera position + smoothed direction offsets
        Vector3 forwardOffset = smoothedRotation * Vector3.forward * offsetDistance;
        Vector3 rightOffset   = smoothedRotation * Vector3.right   * horizontalOffset;
        Vector3 upOffset      = smoothedRotation * Vector3.up      * verticalOffset;

        Vector3 targetPosition = playerCamera.transform.position + forwardOffset + rightOffset + upOffset;

        phoneTarget.position = targetPosition;

        // Make the target face the camera (phone looks oriented toward you)
        phoneTarget.rotation = Quaternion.LookRotation(playerCamera.transform.position - phoneTarget.position);
    }

    /// <summary>
    /// Shows/hides the PhoneTarget + IKAnchor (visual/IK usage).
    /// Transforms keep updating regardless, so re-activating won't snap.
    /// </summary>
    public void SetPhoneActive(bool value)
    {
        if (!IsOwner) return;
        if (value == isPhoneActive) return;

        isPhoneActive = value;

        if (phoneTarget != null)
            phoneTarget.gameObject.SetActive(isPhoneActive);

        // Ensure IK anchor exists under the target
        if (isPhoneActive && ikAnchor == null)
            CreateOrFindIKAnchor();
    }

    private void CreateOrFindIKAnchor()
    {
        if (!IsOwner || phoneTarget == null) return;

        var t = phoneTarget.Find(ikAnchorName);
        if (t == null)
        {
            var go = new GameObject(ikAnchorName);
            t = go.transform;
            t.SetParent(phoneTarget, worldPositionStays: false);
            // Default at local zero; no extra baked offsets.
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }

        ikAnchor = t;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (phoneTarget != null)
            Destroy(phoneTarget.gameObject);
    }
}

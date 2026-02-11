using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class PhoneTargetHandler : NetworkBehaviour
{
    [Header("References")]
    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    private PlayerLook playerLook;

    [SerializeField]
    private PlayerMotor motor; // NEW: read input + states

    [Header("Offsets (camera/yaw space)")]
    [SerializeField]
    private float offsetDistance = 0.185f; // forward

    [SerializeField]
    private float horizontalOffset = 0.20f; // right

    [SerializeField]
    private float verticalOffset = -0.06f; // world up (negative = down)

    [Header("Rotation (what the phone faces)")]
    [SerializeField]
    private float rotationSmoothSpeed = 10f;

    [SerializeField]
    private float maxRotPitchUp = 75f;

    [SerializeField]
    private float maxRotPitchDown = -75f;

    [Header("Position pitch influence (prevents extreme stretch)")]
    [Range(0f, 1f)]
    [SerializeField]
    private float positionPitchInfluence = 0.35f;

    [SerializeField]
    private float maxPosPitchUp = 35f;

    [SerializeField]
    private float maxPosPitchDown = -35f;

    [Header("Move adjust (closer + lower)")]
    [Tooltip("Distance multiplier when the move-adjust is active (sprint/crouch-move/sideways).")]
    [SerializeField]
    private float sprintDistanceMultiplier = 0.755f;

    [Tooltip("Added vertical offset when move-adjust is active (negative = lower).")]
    [SerializeField]
    private float sprintVerticalDelta = -0.05f;

    [Tooltip("Smooth time for move-adjust blending.")]
    [SerializeField]
    private float sprintAdjustSmooth = 0.5f;

    [Tooltip("Threshold for treating input as sideways-only (A/D).")]
    [SerializeField]
    private float sidewaysOnlyThreshold = 0.10f;

    [Header("IK Anchor (child of PhoneTarget)")]
    [SerializeField]
    private string ikAnchorName = "IKAnchor";

    [SerializeField]
    private Vector3 ikAnchorLocalEuler = new Vector3(-12f, -160f, 0f);

    [SerializeField]
    private float faceCameraYawOffset = 15f;

    private Transform phoneTarget;
    private Transform ikAnchor;
    private bool isPhoneActive = false;

    private Quaternion smoothedRotation;
    private bool _initialized;
    private Transform _camT;

    // move-adjust smoothing
    private float _moveAdj01; // 0..1
    private float _moveAdjVel; // SmoothDamp velocity

    public Transform Target => phoneTarget;
    public Transform IKAnchor => ikAnchor;
    public bool IsActive => isPhoneActive;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner)
            return;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
        _camT = playerCamera ? playerCamera.transform : null;

        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>() ?? GetComponentInParent<PlayerLook>();

        if (motor == null)
            motor = GetComponent<PlayerMotor>() ?? GetComponentInParent<PlayerMotor>();

        EnsurePhoneTarget();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene) => _initialized = false;

    public override void OnDestroy()
    {
        base.OnDestroy();
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (IsOwner && phoneTarget != null)
        {
            Destroy(phoneTarget.gameObject);
            phoneTarget = null;
            ikAnchor = null;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (phoneTarget != null)
        {
            Destroy(phoneTarget.gameObject);
            phoneTarget = null;
            ikAnchor = null;
        }
    }

    void Update()
    {
        if (!IsOwner || playerLook == null || _camT == null)
            return;

        if (phoneTarget == null)
            EnsurePhoneTarget();

        UpdateAnchorImmediate(Time.deltaTime);
    }

    public void UpdateAnchorImmediate(float dt)
    {
        if (_camT == null || playerLook == null || phoneTarget == null)
            return;

        // --- Stable yaw, even at pitch = +/-90 ---
        Vector3 flatForward = _camT.forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 1e-6f)
        {
            Vector3 flatRight = _camT.right;
            flatRight.y = 0f;
            if (flatRight.sqrMagnitude < 1e-6f)
                flatRight = transform.right;

            flatRight.Normalize();
            flatForward = Vector3.Cross(flatRight, Vector3.up);
        }

        flatForward.Normalize();
        Quaternion yawRot = Quaternion.LookRotation(flatForward, Vector3.up);

        // --- Rotation: full pitch (clamped) ---
        float rawPitch = playerLook.Pitch;
        float rotPitch = Mathf.Clamp(rawPitch, maxRotPitchDown, maxRotPitchUp);
        Quaternion rotPitchQ = Quaternion.AngleAxis(rotPitch, yawRot * Vector3.right);
        Quaternion targetRotation = rotPitchQ * yawRot;

        if (!_initialized)
        {
            smoothedRotation = targetRotation;
            _initialized = true;
        }

        smoothedRotation = Quaternion.Slerp(
            smoothedRotation,
            targetRotation,
            rotationSmoothSpeed * dt
        );

        // --- Decide if we should apply the "closer + lower" move-adjust ---
        bool applyMoveAdjust = false;
        if (isPhoneActive && motor != null)
        {
            Vector2 inDir = motor.inputDirection;

            bool isMoving = inDir.sqrMagnitude > 0.0001f;

            // sideways-only = A/D with essentially no W/S
            bool sidewaysOnly =
                Mathf.Abs(inDir.x) > sidewaysOnlyThreshold
                && Mathf.Abs(inDir.y) < sidewaysOnlyThreshold;

            bool sprinting = motor.IsActuallySprinting;
            bool crouchMoving = motor.crouching && isMoving;

            applyMoveAdjust = sprinting || crouchMoving || sidewaysOnly;
        }

        float targetAdj = applyMoveAdjust ? 1f : 0f;
        float smooth = Mathf.Max(0.0001f, sprintAdjustSmooth);
        _moveAdj01 = Mathf.SmoothDamp(
            _moveAdj01,
            targetAdj,
            ref _moveAdjVel,
            smooth,
            Mathf.Infinity,
            dt
        );
        _moveAdj01 = Mathf.Clamp01(_moveAdj01);

        // --- Position: reduced/clamped pitch influence ---
        float posPitch =
            Mathf.Clamp(rawPitch, maxPosPitchDown, maxPosPitchUp) * positionPitchInfluence;
        Quaternion posPitchQ = Quaternion.AngleAxis(posPitch, yawRot * Vector3.right);
        Quaternion posRot = posPitchQ * yawRot;

        // Apply move adjust to forward + vertical offsets
        float dist = offsetDistance * Mathf.Lerp(1f, sprintDistanceMultiplier, _moveAdj01);
        float vert = verticalOffset + (sprintVerticalDelta * _moveAdj01);

        Vector3 forwardOffset = (posRot * Vector3.forward) * dist;
        Vector3 rightOffset = (yawRot * Vector3.right) * horizontalOffset;
        Vector3 upOffset = Vector3.up * vert;

        phoneTarget.position = _camT.position + forwardOffset + rightOffset + upOffset;
        phoneTarget.rotation = smoothedRotation * Quaternion.Euler(0f, faceCameraYawOffset, 0f);
    }

    public void SetPhoneActive(bool value)
    {
        if (!IsOwner)
            return;

        if (value && phoneTarget == null)
            EnsurePhoneTarget();

        if (value == isPhoneActive)
            return;

        isPhoneActive = value;

        if (phoneTarget != null)
            phoneTarget.gameObject.SetActive(isPhoneActive);

        if (isPhoneActive && ikAnchor == null)
            CreateOrFindIKAnchor();

        // When turning off, blend the move-adjust back to 0 smoothly
        // (no snap; just changes targetAdj in UpdateAnchorImmediate)
    }

    private void EnsurePhoneTarget()
    {
        if (phoneTarget != null)
            return;

        var name = $"PhoneTarget_{(NetworkManager ? NetworkManager.LocalClientId : 0)}";
        GameObject targetObj = new GameObject(name);
        phoneTarget = targetObj.transform;

        phoneTarget.parent = null;
        DontDestroyOnLoad(targetObj);
        phoneTarget.gameObject.SetActive(isPhoneActive);

        CreateOrFindIKAnchor();
        _initialized = false;
    }

    private void CreateOrFindIKAnchor()
    {
        if (phoneTarget == null)
            return;

        var t = phoneTarget.Find(ikAnchorName);
        if (t == null)
        {
            var go = new GameObject(ikAnchorName);
            t = go.transform;
            t.SetParent(phoneTarget, worldPositionStays: false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(ikAnchorLocalEuler);
        }
        ikAnchor = t;
    }
}

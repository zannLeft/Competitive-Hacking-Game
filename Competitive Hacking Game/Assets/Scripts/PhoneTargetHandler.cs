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
    private PlayerMotor motor;

    [Header("Offsets (camera/yaw space)")]
    [SerializeField]
    private float offsetDistance = 0.185f; // forward

    [SerializeField]
    private float horizontalOffset = 0.20f; // right

    [SerializeField]
    private float verticalOffset = -0.06f; // base world up (negative = down)

    [Header("Move Adjust: bring phone closer + lower (sprint AND crouch-moving)")]
    [SerializeField, Range(0.6f, 1.0f)]
    private float sprintDistanceMultiplier = 0.86f; // you set to 0.755

    [SerializeField]
    private float sprintVerticalDelta = -0.025f; // you set to -0.05 (extra down)

    [SerializeField]
    private float sprintAdjustSmoothTime = 0.14f; // you set to 0.5 (smooth in/out)

    [SerializeField]
    private float sprintAdjustMaxSpeed = 0f; // 0 = unlimited

    [Header("Rotation (what the phone faces)")]
    [SerializeField]
    private float rotationSmoothSpeed = 10f;

    [Tooltip(">1 = less lag (more responsive). <1 = more lag (floatier).")]
    [SerializeField, Range(0.5f, 2.0f)]
    private float swayResponsivenessMultiplier = 2.0f;

    [Header("Facing Offset (angle screen toward camera)")]
    [Tooltip(
        "Applied AFTER the smoothed facing. Usually tweak Y to yaw the phone so the screen faces the camera more."
    )]
    [SerializeField]
    private Vector3 phoneFacingOffsetEuler = new Vector3(0f, 0f, 0f); // try Y = 10..25

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

    [Header("IK Anchor (child of PhoneTarget)")]
    [SerializeField]
    private string ikAnchorName = "IKAnchor";

    [SerializeField]
    private Vector3 ikAnchorLocalEuler = new Vector3(-12f, -160f, 0f);

    private Transform phoneTarget;
    private Transform ikAnchor;
    private bool isPhoneActive = false;

    private Quaternion smoothedRotation;
    private bool _initialized;
    private Transform _camT;

    // Smoothed move-adjust values
    private float _smoothedDist;
    private float _distVel;

    private float _smoothedMoveY; // 0 -> sprintVerticalDelta
    private float _yVel;

    public Transform Target => phoneTarget;
    public Transform IKAnchor => ikAnchor;
    public bool IsActive => isPhoneActive;

    void Reset()
    {
        playerLook = GetComponent<PlayerLook>() ?? GetComponentInParent<PlayerLook>();
        motor = GetComponent<PlayerMotor>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
    }

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
            motor = GetComponent<PlayerMotor>();

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
            flatForward = Vector3.Cross(flatRight, Vector3.up); // right × up = forward
        }

        flatForward.Normalize();
        Quaternion yawRot = Quaternion.LookRotation(flatForward, Vector3.up);

        // --- Rotation: full pitch (clamped) ---
        float rawPitch = playerLook.Pitch;
        float rotPitch = Mathf.Clamp(rawPitch, maxRotPitchDown, maxRotPitchUp);
        Quaternion rotPitchQ = Quaternion.AngleAxis(rotPitch, yawRot * Vector3.right);
        Quaternion targetRotation = rotPitchQ * yawRot;

        // Seed smoothers
        if (!_initialized)
        {
            smoothedRotation = targetRotation;

            _smoothedDist = ResolveDesiredDistance();
            _distVel = 0f;

            _smoothedMoveY = ResolveDesiredMoveY();
            _yVel = 0f;

            _initialized = true;
        }

        // --- Less sway delay: more responsive rotation smoothing ---
        float rotSpeed =
            Mathf.Max(0.01f, rotationSmoothSpeed) * Mathf.Max(0.01f, swayResponsivenessMultiplier);
        smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, rotSpeed * dt);

        // --- Position: reduced/clamped pitch influence ---
        float posPitch =
            Mathf.Clamp(rawPitch, maxPosPitchDown, maxPosPitchUp) * positionPitchInfluence;
        Quaternion posPitchQ = Quaternion.AngleAxis(posPitch, yawRot * Vector3.right);
        Quaternion posRot = posPitchQ * yawRot;

        // --- Move adjustments: distance + extra down offset (sprint OR crouch-moving) ---
        float desiredDist = ResolveDesiredDistance();
        float desiredMoveY = ResolveDesiredMoveY();

        float smoothTime = Mathf.Max(0.0001f, sprintAdjustSmoothTime);
        float maxSpeed =
            (sprintAdjustMaxSpeed <= 0f) ? float.PositiveInfinity : sprintAdjustMaxSpeed;

        _smoothedDist = Mathf.SmoothDamp(
            _smoothedDist,
            desiredDist,
            ref _distVel,
            smoothTime,
            maxSpeed,
            dt
        );
        _smoothedMoveY = Mathf.SmoothDamp(
            _smoothedMoveY,
            desiredMoveY,
            ref _yVel,
            smoothTime,
            maxSpeed,
            dt
        );

        Vector3 forwardOffset = (posRot * Vector3.forward) * _smoothedDist;
        Vector3 rightOffset = (yawRot * Vector3.right) * horizontalOffset;

        Vector3 upOffset = Vector3.up * (verticalOffset + _smoothedMoveY);

        phoneTarget.position = _camT.position + forwardOffset + rightOffset + upOffset;

        // ✅ Facing offset: yaw/tilt/roll the phone so the screen angles toward the camera more
        phoneTarget.rotation = smoothedRotation * Quaternion.Euler(phoneFacingOffsetEuler);
    }

    // True when we should apply the "closer + lower" adjustment
    private bool ResolveMoveAdjustActive()
    {
        if (playerLook == null || motor == null)
            return false;

        // Only when phone is up
        if (!playerLook.IsPhoneAiming)
            return false;

        // Sprinting path
        if (motor.IsActuallySprinting)
            return true;

        // Crouch-moving path (your requested new behavior)
        bool moving = motor.inputDirection.sqrMagnitude > 0.0001f;
        if (motor.crouching && moving)
            return true;

        return false;
    }

    private float ResolveDesiredDistance()
    {
        if (ResolveMoveAdjustActive())
            return offsetDistance * sprintDistanceMultiplier;

        return offsetDistance;
    }

    private float ResolveDesiredMoveY()
    {
        if (ResolveMoveAdjustActive())
            return sprintVerticalDelta;

        return 0f;
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

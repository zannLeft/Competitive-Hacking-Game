using Unity.Netcode;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    private PlayerMotor motor;

    [Header("References")]
    public Camera cam;
    [Tooltip("Parent pivot of the main camera (your empty 'Camera' object in the middle of the player).")]
    public Transform cameraRoot;

    [Header("Camera Positions (Inspector)")]
    [SerializeField] private bool   useInspectorPositions = true; // if false, we auto-fill from current on Start
    [SerializeField] private Vector3 standingLocalPos;
    [SerializeField] private Vector3 crouchingLocalPos;
    [SerializeField] private Vector3 slidingLocalPos;
    [SerializeField] private Vector3 sprintingLocalPos;
    [SerializeField] private Vector3 walkForwardLocalPos;
    [SerializeField] private Vector3 walkBackwardLocalPos;
    [SerializeField] private Vector3 walkSideLocalPos; // sideways (pure strafe or lateral-dominant diagonals)

    [Header("Strafe Handling")]
    [SerializeField] private float moveDirThreshold = 0.10f;

    [Header("Sensitivity")]
    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    private float xRotation = 0f;

    [Header("FOV")]
    [SerializeField] private float defaultFOV = 90f;
    [SerializeField] private float sprintFOV = 100f;
    [SerializeField] private float fovTransitionSpeed = 5f;

    [Header("Shoulder Look (Yaw Decoupling)")]
    public float maxShoulderYaw = 90f;
    public float catchUpThreshold = 90f;
    public float bodyCatchUpSpeed = 720f;
    public bool  shoulderWhileCrouching = true;

    [Header("Catch-up on Move")]
    public bool  instantCatchUpOnMove = true;
    public float catchUpOnMoveSpeed   = 1080f;

    // ---------- PITCH (Instant) ----------
    [Header("Pitch Offset (Instant) – Standing/Walking/Sprinting")]
    [SerializeField] private float pitchForwardMax = 0.10f;
    [SerializeField] private float pitchBackwardMax = 0.12f;
    [SerializeField] private float pitchDownY = 0.04f;
    [SerializeField] private float pitchUpY = 0.04f;

    [Header("Pitch Offset (Instant) – While Crouching")]
    [SerializeField] private float crouchPitchForwardMax = 0.07f;
    [SerializeField] private float crouchPitchBackwardMax = 0.10f;
    [SerializeField] private float crouchPitchDownY = 0.03f;
    [SerializeField] private float crouchPitchUpY = 0.03f;

    [Header("Base Position Smoothing")]
    [Tooltip("Lerp speed for all non-crouch transitions.")]
    [SerializeField] private float positionLerpSpeed = 7f;
    [Tooltip("LINEAR speed (units/sec) for crouch <-> uncrouch camera movement.")]
    [SerializeField] private float crouchPositionSpeed = 4f;

    private float yawOffset = 0f;
    private bool  catchingUpStationary = false;
    private bool  wasMoving = false;

    // We smooth ONLY the base/local camera position (without pitch offset)
    private Vector3 _smoothedBaseLocalPos;
    private bool _wasCrouching; // track last-frame crouch state

    public float Pitch => xRotation;
    public float YawOffset => yawOffset;

    // Phone/aim flags
    private bool phoneAimActive = false;
    public  bool IsPhoneAiming => phoneAimActive;
    private bool rmbHeld = false;
    public  bool IsRmbHeld => rmbHeld;

    public Quaternion WorldLookRotation =>
        transform.rotation * Quaternion.Euler(0f, yawOffset, 0f) * Quaternion.Euler(Pitch, 0f, 0f);

    public Vector3 WorldLookDirection => WorldLookRotation * Vector3.forward;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        if (!IsOwner)
        {
            if (cam != null) cam.gameObject.SetActive(false);
            return;
        }

        motor = GetComponent<PlayerMotor>();

        if (!useInspectorPositions) AutoFillFromCurrentCamera();

        _smoothedBaseLocalPos = standingLocalPos;
        cam.fieldOfView = defaultFOV;

        if (cameraRoot == null && cam != null)
            cameraRoot = cam.transform.parent;

        _wasCrouching = motor != null && motor.crouching;
    }

    void Update()
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;

        if (motor.sliding && xRotation > 0f)
            xRotation = Mathf.Lerp(xRotation, 0f, dt * 3.5f);

        bool actuallySprinting = motor != null && motor.IsActuallySprinting;

        // Select BASE target (no pitch here)
        Vector3 baseTargetPos;
        if (actuallySprinting)            baseTargetPos = sprintingLocalPos;
        else if (motor.sliding)           baseTargetPos = slidingLocalPos;
        else if (motor.crouching)         baseTargetPos = crouchingLocalPos;
        else
        {
            Vector2 move = motor.inputDirection;
            float ax = Mathf.Abs(move.x);
            float ay = Mathf.Abs(move.y);
            float thr = moveDirThreshold;
            bool moving = (ax > thr) || (ay > thr);

            if (moving)
            {
                if (ax > ay)                  baseTargetPos = walkSideLocalPos;
                else if (move.y >  0f)        baseTargetPos = walkForwardLocalPos;
                else                          baseTargetPos = walkBackwardLocalPos;
            }
            else baseTargetPos = standingLocalPos;
        }

        // ---------- Base position: linear for crouch transitions, lerp otherwise ----------
        bool useLinear = motor.crouching || _wasCrouching; // entering OR exiting crouch
        if (useLinear)
        {
            _smoothedBaseLocalPos = Vector3.MoveTowards(
                _smoothedBaseLocalPos, baseTargetPos, crouchPositionSpeed * dt);
        }
        else
        {
            _smoothedBaseLocalPos = Vector3.Lerp(
                _smoothedBaseLocalPos, baseTargetPos, dt * positionLerpSpeed);
        }

        // ---------- Instant pitch offset (Y and Z) ----------
        float up01   = Mathf.Clamp01(-xRotation / 90f);
        float down01 = Mathf.Clamp01( xRotation / 90f);

        bool isCrouched = motor.crouching;
        float fwdMax   = isCrouched ? crouchPitchForwardMax   : pitchForwardMax;
        float backMax  = isCrouched ? crouchPitchBackwardMax  : pitchBackwardMax;
        float downMaxY = isCrouched ? crouchPitchDownY        : pitchDownY;
        float upMaxY   = isCrouched ? crouchPitchUpY          : pitchUpY;

        float yPitch = (-down01 * downMaxY) + (up01 * upMaxY);
        float zPitch = ( down01 * fwdMax )  - (up01 * backMax);

        cam.transform.localPosition = _smoothedBaseLocalPos + new Vector3(0f, yPitch, zPitch);

        // FOV
        bool fastState = actuallySprinting || motor.sliding;
        float targetFOV = fastState ? sprintFOV : defaultFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, dt * fovTransitionSpeed);

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        // remember crouch state for next frame
        _wasCrouching = motor.crouching;
    }

    public void ProcessLook(Vector2 input)
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;
        float mouseX = input.x;
        float mouseY = input.y;

        // PITCH
        if (!(motor.sliding && xRotation > 0f && mouseY <= 0f))
        {
            xRotation -= mouseY * dt * ySensitivity;
            xRotation  = Mathf.Clamp(xRotation, -90f, 90f);
        }
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // YAW
        bool hasMoveInput = motor.inputDirection.sqrMagnitude > 0.0001f;
        bool shoulderMode =
            !hasMoveInput && !motor.sliding && !motor.sprinting &&
            (motor.crouching ? shoulderWhileCrouching : true);

        float yawDelta = mouseX * dt * xSensitivity;

        if (phoneAimActive)
        {
            transform.rotation *= Quaternion.Euler(0f, yawDelta, 0f);

            if (Mathf.Abs(yawOffset) > 0.01f)
            {
                if (instantCatchUpOnMove)
                {
                    transform.rotation *= Quaternion.Euler(0f, yawOffset, 0f);
                    yawOffset = 0f;
                }
                else
                {
                    CatchUp(catchUpOnMoveSpeed * dt);
                }
            }
            catchingUpStationary = false;
        }
        else if (shoulderMode)
        {
            yawOffset = Mathf.Clamp(yawOffset + yawDelta, -maxShoulderYaw, maxShoulderYaw);

            if (!catchingUpStationary && Mathf.Abs(yawOffset) >= (catchUpThreshold - 0.01f))
                catchingUpStationary = true;

            if (catchingUpStationary)
            {
                CatchUp(bodyCatchUpSpeed * dt);
                if (Mathf.Abs(yawOffset) <= 0.01f) catchingUpStationary = false;
            }
        }
        else
        {
            if (hasMoveInput && !wasMoving && Mathf.Abs(yawOffset) > 0.01f)
            {
                if (instantCatchUpOnMove)
                {
                    transform.rotation *= Quaternion.Euler(0f, yawOffset, 0f);
                    yawOffset = 0f;
                }
                else
                {
                    CatchUp(catchUpOnMoveSpeed * dt);
                }
            }

            transform.rotation *= Quaternion.Euler(0f, yawDelta, 0f);

            if (!instantCatchUpOnMove && hasMoveInput && Mathf.Abs(yawOffset) > 0.01f)
                CatchUp(catchUpOnMoveSpeed * dt);

            catchingUpStationary = false;
        }

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        wasMoving = hasMoveInput;
    }

    private void CatchUp(float stepDegrees)
    {
        if (Mathf.Abs(yawOffset) <= 0.0001f) return;

        float delta = Mathf.Clamp(yawOffset, -stepDegrees, stepDegrees);
        transform.rotation *= Quaternion.Euler(0f, delta, 0f);
        yawOffset -= delta;

        if (Mathf.Abs(yawOffset) <= 0.01f)
            yawOffset = 0f;
    }

    public Quaternion MoveSpaceRotation => transform.rotation * Quaternion.Euler(0f, yawOffset, 0f);

    public void SetPhoneAim(bool aiming) => phoneAimActive = aiming;
    public void SetAimHeld(bool held)    => rmbHeld = held;

    [ContextMenu("Auto-Fill Positions From Current Camera")]
    private void AutoFillFromCurrentCamera()
    {
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        var basePos = cam.transform.localPosition;
        standingLocalPos   = basePos;
        crouchingLocalPos  = basePos + new Vector3(0f, -1.1f,  0f);
        slidingLocalPos    = basePos + new Vector3(0f, -1.1f, -0.1f);
        sprintingLocalPos  = basePos + new Vector3(0f, -0.15f, +0.15f);

        if (walkForwardLocalPos  == Vector3.zero) walkForwardLocalPos  = basePos + new Vector3(0f, -0.02f, +0.05f);
        if (walkBackwardLocalPos == Vector3.zero) walkBackwardLocalPos = basePos + new Vector3(0f,  0f,    -0.05f);
        if (walkSideLocalPos     == Vector3.zero) walkSideLocalPos     = basePos + new Vector3(0f, -0.02f, +0.04f);

        // Default crouch pitch mirrors standing until you tune it
        if (Mathf.Approximately(crouchPitchForwardMax, 0f)) crouchPitchForwardMax = pitchForwardMax;
        if (Mathf.Approximately(crouchPitchBackwardMax,0f)) crouchPitchBackwardMax= pitchBackwardMax;
        if (Mathf.Approximately(crouchPitchDownY,      0f)) crouchPitchDownY      = pitchDownY;
        if (Mathf.Approximately(crouchPitchUpY,        0f)) crouchPitchUpY        = pitchUpY;
    }
}

using Unity.Netcode;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    private PlayerMotor motor;
    private Animator animator;

    [Header("References")]
    public Camera cam;

    [Tooltip(
        "Parent pivot of the main camera (your empty 'Camera' object in the middle of the player)."
    )]
    public Transform cameraRoot;

    [Header("Camera Positions (Inspector)")]
    [SerializeField]
    private bool useInspectorPositions = true;

    [SerializeField]
    private Vector3 standingLocalPos;

    [SerializeField]
    private Vector3 crouchingLocalPos;

    [SerializeField]
    private Vector3 slidingLocalPos;

    [SerializeField]
    private Vector3 sprintingLocalPos;

    [SerializeField]
    private Vector3 walkForwardLocalPos;

    [SerializeField]
    private Vector3 walkBackwardLocalPos;

    [SerializeField]
    private Vector3 walkSideLocalPos;

    [SerializeField]
    private Vector3 coilLocalPos;

    [SerializeField]
    private Vector3 sprintJumpLocalPos;

    [Header("Sprint-Jump Detection")]
    [SerializeField]
    private float sprintJumpZThreshold = 2.1f;

    [Header("Strafe Handling")]
    [SerializeField]
    private float moveDirThreshold = 0.10f;

    [Header("Sensitivity")]
    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    [Header("Mouse Smoothing")]
    [SerializeField]
    private bool smoothMouse = true;

    [SerializeField]
    private float mouseSmoothingTime = 0.05f;

    private Vector2 _smoothedDelta;
    private float xRotation = 0f;

    [Header("FOV")]
    [SerializeField]
    private float defaultFOV = 90f;

    [SerializeField]
    private float sprintFOV = 100f;

    [SerializeField]
    private float fovTransitionSpeed = 5f;

    [Header("Shoulder Look (Yaw Decoupling)")]
    public float maxShoulderYaw = 90f;
    public float catchUpThreshold = 90f;
    public float bodyCatchUpSpeed = 720f;
    public bool shoulderWhileCrouching = true;

    [Header("Catch-up on Move")]
    public bool instantCatchUpOnMove = true;
    public float catchUpOnMoveSpeed = 1080f;

    [Header("Phone Aim Anti-Jank (Shoulder Look still works)")]
    [SerializeField]
    private bool disableInstantCatchUpWhilePhone = true;

    [SerializeField, Range(0.1f, 1f)]
    private float phoneCatchUpSpeedMultiplier = 0.45f;

    [Header("Phone Pitch Centering (RMB)")]
    [SerializeField]
    private bool lockPitchWhilePhoneUp = true;

    [Tooltip(
        "If ON: you can still move pitch a bit, but it's constantly pulled toward 0 (soft spring). If OFF: hard lock to 0."
    )]
    [SerializeField]
    private bool phoneSoftPitchCentering = true;

    [Tooltip(
        "How much mouse pitch input is allowed while phone is up. Lower = stronger 'resistance'."
    )]
    [SerializeField, Range(0f, 1f)]
    private float phonePitchInputScale = 0.35f;

    [SerializeField]
    private float phonePitchSmoothTime = 0.22f;

    [SerializeField]
    private float phonePitchMaxSpeed = 0f;

    [SerializeField]
    private float phonePitchSnapEps = 0.10f;

    private float _phonePitchVel;

    [Header("Crouch + Phone Yaw Lock")]
    [SerializeField]
    private float crouchPhoneAlignEps = 0.02f; // degrees

    [Tooltip("Multiplier on the crouch+phone align speed (1 = same as standing catch-up).")]
    [SerializeField]
    private float crouchPhoneAlignSpeedMultiplier = 1.0f;

    [Header("Pitch Offset (Instant) – Standing/Walking/Sprinting")]
    [SerializeField]
    private float pitchForwardMax = 0.10f;

    [SerializeField]
    private float pitchBackwardMax = 0.12f;

    [SerializeField]
    private float pitchDownY = 0.04f;

    [SerializeField]
    private float pitchUpY = 0.04f;

    [Header("Pitch Offset (Instant) – While Crouching")]
    [SerializeField]
    private float crouchPitchForwardMax = 0.07f;

    [SerializeField]
    private float crouchPitchBackwardMax = 0.10f;

    [SerializeField]
    private float crouchPitchDownY = 0.03f;

    [SerializeField]
    private float crouchPitchUpY = 0.03f;

    [Header("Base Position Smoothing")]
    [SerializeField]
    private float positionLerpSpeed = 7f;

    [SerializeField]
    private float crouchPositionSpeed = 4f;

    private float yawOffset = 0f;
    private bool catchingUpStationary = false;
    private bool wasMoving = false;

    private Vector3 _smoothedBaseLocalPos;
    private bool _wasCrouching;

    public float Pitch => xRotation;
    public float YawOffset => yawOffset;

    private bool phoneAimActive = false;
    public bool IsPhoneAiming => phoneAimActive;

    private bool rmbHeld = false;
    public bool IsRmbHeld => rmbHeld;

    private const float PitchClamp = 90f;

    void Start()
    {
        if (!IsOwner)
        {
            if (cam != null)
                cam.gameObject.SetActive(false);
            return;
        }

        motor = GetComponent<PlayerMotor>();
        animator = GetComponent<Animator>() ?? GetComponentInParent<Animator>();

        if (!useInspectorPositions)
            AutoFillFromCurrentCamera();

        _smoothedBaseLocalPos = standingLocalPos;

        if (cam != null)
            cam.fieldOfView = defaultFOV;

        if (cameraRoot == null && cam != null)
            cameraRoot = cam.transform.parent;

        _wasCrouching = motor != null && motor.crouching;
    }

    void Update()
    {
        if (!IsOwner)
            return;

        float dt = Time.deltaTime;

        // slide/coil "recentre from looking down"
        if ((motor.sliding || motor.Coiling) && xRotation > 0f)
            xRotation = Mathf.Lerp(xRotation, 0f, dt * 3.5f);

        bool actuallySprinting = motor != null && motor.IsActuallySprinting;

        Vector3 baseTargetPos;

        if (motor.sliding)
            baseTargetPos = slidingLocalPos;
        else if (motor.Coiling)
            baseTargetPos = coilLocalPos;
        else
        {
            bool isAirborne = animator != null && !animator.GetBool("IsGrounded");
            float zVel = animator != null ? animator.GetFloat("Z_Velocity") : 0f;

            if (isAirborne && zVel > sprintJumpZThreshold)
                baseTargetPos = sprintJumpLocalPos;
            else if (actuallySprinting)
                baseTargetPos = sprintingLocalPos;
            else if (motor.crouching)
                baseTargetPos = crouchingLocalPos;
            else
            {
                Vector2 move = motor.inputDirection;
                float ax = Mathf.Abs(move.x);
                float ay = Mathf.Abs(move.y);
                float thr = moveDirThreshold;
                bool moving = (ax > thr) || (ay > thr);

                if (moving)
                {
                    if (ax > ay)
                        baseTargetPos = walkSideLocalPos;
                    else if (move.y > 0f)
                        baseTargetPos = walkForwardLocalPos;
                    else
                        baseTargetPos = walkBackwardLocalPos;
                }
                else
                    baseTargetPos = standingLocalPos;
            }
        }

        bool useLinear = motor.crouching || _wasCrouching;
        if (useLinear)
            _smoothedBaseLocalPos = Vector3.MoveTowards(
                _smoothedBaseLocalPos,
                baseTargetPos,
                crouchPositionSpeed * dt
            );
        else
            _smoothedBaseLocalPos = Vector3.Lerp(
                _smoothedBaseLocalPos,
                baseTargetPos,
                dt * positionLerpSpeed
            );

        float up01 = Mathf.Clamp01(-xRotation / 90f);
        float down01 = Mathf.Clamp01(xRotation / 90f);

        bool isCrouched = motor.crouching;
        float fwdMax = isCrouched ? crouchPitchForwardMax : pitchForwardMax;
        float backMax = isCrouched ? crouchPitchBackwardMax : pitchBackwardMax;
        float downMaxY = isCrouched ? crouchPitchDownY : pitchDownY;
        float upMaxY = isCrouched ? crouchPitchUpY : pitchUpY;

        float yPitch = (-down01 * downMaxY) + (up01 * upMaxY);
        float zPitch = (down01 * fwdMax) - (up01 * backMax);

        if (cam != null)
            cam.transform.localPosition = _smoothedBaseLocalPos + new Vector3(0f, yPitch, zPitch);

        bool fastState = actuallySprinting || motor.sliding;
        float targetFOV = fastState ? sprintFOV : defaultFOV;
        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, dt * fovTransitionSpeed);

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        _wasCrouching = motor.crouching;
    }

    public void ProcessLook(Vector2 input)
    {
        if (!IsOwner)
            return;

        float dt = Time.deltaTime;

        Vector2 raw = input;
        if (smoothMouse && mouseSmoothingTime > 0f)
        {
            float alpha = 1f - Mathf.Exp(-dt / mouseSmoothingTime);
            _smoothedDelta = Vector2.Lerp(_smoothedDelta, raw, alpha);
        }
        else
        {
            _smoothedDelta = raw;
        }

        float mouseX = _smoothedDelta.x;
        float mouseY = _smoothedDelta.y;

        bool phoneAllowedNow = phoneAimActive && motor != null && !motor.sliding && !motor.Coiling;

        // ---------------- PITCH ----------------
        if (lockPitchWhilePhoneUp && phoneAllowedNow)
        {
            float smoothTime = Mathf.Max(0.0001f, phonePitchSmoothTime);
            float maxSpeed =
                (phonePitchMaxSpeed <= 0f) ? float.PositiveInfinity : phonePitchMaxSpeed;

            if (phoneSoftPitchCentering)
            {
                float inputPitch = -mouseY * dt * ySensitivity * phonePitchInputScale;
                xRotation += inputPitch;

                xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);

                xRotation = Mathf.SmoothDampAngle(
                    xRotation,
                    0f,
                    ref _phonePitchVel,
                    smoothTime,
                    maxSpeed,
                    dt
                );

                float clamped = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);
                if (!Mathf.Approximately(clamped, xRotation))
                {
                    xRotation = clamped;
                    _phonePitchVel = 0f;
                }

                if (Mathf.Abs(xRotation) <= phonePitchSnapEps && Mathf.Abs(mouseY) < 0.001f)
                {
                    xRotation = 0f;
                    _phonePitchVel = 0f;
                }
            }
            else
            {
                xRotation = Mathf.SmoothDampAngle(
                    xRotation,
                    0f,
                    ref _phonePitchVel,
                    smoothTime,
                    maxSpeed,
                    dt
                );

                xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);

                if (Mathf.Abs(xRotation) <= phonePitchSnapEps)
                {
                    xRotation = 0f;
                    _phonePitchVel = 0f;
                }
            }
        }
        else
        {
            _phonePitchVel = 0f;

            if (!(motor.sliding && xRotation > 0f && mouseY <= 0f))
            {
                xRotation -= mouseY * dt * ySensitivity;
                xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);
            }
        }

        if (cam != null)
            cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // ---------------- YAW ----------------
        float yawDelta = mouseX * dt * xSensitivity;
        bool hasMoveInput = motor.inputDirection.sqrMagnitude > 0.0001f;

        // ✅ Phone up while crouching: align/catch-up at FULL standing speed
        if (phoneAllowedNow && motor.crouching)
        {
            float alignStep =
                bodyCatchUpSpeed * dt * Mathf.Max(0.01f, crouchPhoneAlignSpeedMultiplier);

            if (Mathf.Abs(yawOffset) > crouchPhoneAlignEps)
            {
                float newOffset = yawOffset + yawDelta;
                float clampedYaw = Mathf.Clamp(newOffset, -maxShoulderYaw, maxShoulderYaw);
                float overflow = newOffset - clampedYaw;

                yawOffset = clampedYaw;

                if (Mathf.Abs(overflow) > 0.0001f)
                    transform.rotation *= Quaternion.Euler(0f, overflow, 0f);

                // No threshold here; always align until centered
                CatchUp(alignStep);

                if (Mathf.Abs(yawOffset) <= crouchPhoneAlignEps)
                    yawOffset = 0f;

                catchingUpStationary = false;
            }
            else
            {
                yawOffset = 0f;
                transform.rotation *= Quaternion.Euler(0f, yawDelta, 0f);
                catchingUpStationary = false;
            }

            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

            wasMoving = hasMoveInput;
            return;
        }

        bool shoulderMode =
            !hasMoveInput
            && !motor.sliding
            && !motor.sprinting
            && (motor.crouching ? shoulderWhileCrouching : true);

        // While phone is up, no instant catch-up (avoid snapping body)
        bool allowInstantCatchUp =
            instantCatchUpOnMove && !(phoneAllowedNow && disableInstantCatchUpWhilePhone);

        float speedMulNormal = phoneAllowedNow ? phoneCatchUpSpeedMultiplier : 1f;

        if (shoulderMode)
        {
            float newOffset = yawOffset + yawDelta;
            float clampedYaw = Mathf.Clamp(newOffset, -maxShoulderYaw, maxShoulderYaw);
            float overflow = newOffset - clampedYaw;

            yawOffset = clampedYaw;

            if (Mathf.Abs(overflow) > 0.0001f)
                transform.rotation *= Quaternion.Euler(0f, overflow, 0f);

            if (!catchingUpStationary && Mathf.Abs(yawOffset) >= (catchUpThreshold - 0.01f))
                catchingUpStationary = true;

            if (catchingUpStationary)
            {
                CatchUp(bodyCatchUpSpeed * dt * speedMulNormal);
                if (Mathf.Abs(yawOffset) <= 0.01f)
                    catchingUpStationary = false;
            }
        }
        else
        {
            if (hasMoveInput && !wasMoving && Mathf.Abs(yawOffset) > 0.01f)
            {
                if (allowInstantCatchUp)
                {
                    transform.rotation *= Quaternion.Euler(0f, yawOffset, 0f);
                    yawOffset = 0f;
                }
                else
                {
                    CatchUp(catchUpOnMoveSpeed * dt * speedMulNormal);
                }
            }

            transform.rotation *= Quaternion.Euler(0f, yawDelta, 0f);

            if (!allowInstantCatchUp && hasMoveInput && Mathf.Abs(yawOffset) > 0.01f)
                CatchUp(catchUpOnMoveSpeed * dt * speedMulNormal);

            catchingUpStationary = false;
        }

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        wasMoving = hasMoveInput;
    }

    private void CatchUp(float stepDegrees)
    {
        if (Mathf.Abs(yawOffset) <= 0.0001f)
            return;

        float delta = Mathf.Clamp(yawOffset, -stepDegrees, stepDegrees);
        transform.rotation *= Quaternion.Euler(0f, delta, 0f);
        yawOffset -= delta;

        if (Mathf.Abs(yawOffset) <= 0.01f)
            yawOffset = 0f;
    }

    public Quaternion MoveSpaceRotation => transform.rotation * Quaternion.Euler(0f, yawOffset, 0f);

    public void SetPhoneAim(bool aiming) => phoneAimActive = aiming;

    public void SetAimHeld(bool held) => rmbHeld = held;

    [ContextMenu("Auto-Fill Positions From Current Camera")]
    private void AutoFillFromCurrentCamera()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>();
        if (cam == null)
            return;

        var basePos = cam.transform.localPosition;
        standingLocalPos = basePos;
        crouchingLocalPos = basePos + new Vector3(0f, -1.1f, 0f);
        slidingLocalPos = basePos + new Vector3(0f, -1.1f, -0.1f);
        sprintingLocalPos = basePos + new Vector3(0f, -0.15f, +0.15f);

        if (walkForwardLocalPos == Vector3.zero)
            walkForwardLocalPos = basePos + new Vector3(0f, -0.02f, +0.05f);
        if (walkBackwardLocalPos == Vector3.zero)
            walkBackwardLocalPos = basePos + new Vector3(0f, 0f, -0.05f);
        if (walkSideLocalPos == Vector3.zero)
            walkSideLocalPos = basePos + new Vector3(0f, -0.02f, +0.04f);

        if (coilLocalPos == Vector3.zero)
            coilLocalPos = basePos + new Vector3(0f, -0.15f, -0.05f);
        if (sprintJumpLocalPos == Vector3.zero)
            sprintJumpLocalPos = basePos + new Vector3(0f, -0.08f, +0.06f);

        if (Mathf.Approximately(crouchPitchForwardMax, 0f))
            crouchPitchForwardMax = pitchForwardMax;
        if (Mathf.Approximately(crouchPitchBackwardMax, 0f))
            crouchPitchBackwardMax = pitchBackwardMax;
        if (Mathf.Approximately(crouchPitchDownY, 0f))
            crouchPitchDownY = pitchDownY;
        if (Mathf.Approximately(crouchPitchUpY, 0f))
            crouchPitchUpY = pitchUpY;
    }
}

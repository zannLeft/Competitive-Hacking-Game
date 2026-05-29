using Unity.Netcode;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    private PlayerMotor motor;
    private Animator animator;
    private PlayerDeathState deathState;

    [Header("References")]
    public Camera cam;

    [Tooltip("Parent pivot of the main camera (your empty 'Camera' object in the middle of the player).")]
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

    [Header("Dead Camera")]
    [Tooltip("Optional fallback dead camera position if no Dead Camera Socket is assigned.")]
    [SerializeField]
    private Vector3 deadLocalPos;

    [Tooltip("Assign a child object on/near the head bone. Used for dead camera POSITION only.")]
    [SerializeField]
    private Transform deadCameraSocket;

    [Tooltip("Small local offset from CameraRoot while using the dead socket. Usually leave at zero.")]
    [SerializeField]
    private Vector3 deadSocketCameraLocalOffset = Vector3.zero;

    [Tooltip("How fast the camera moves to/from the dead floor/head position.")]
    [SerializeField]
    private float deadCameraLerpSpeed = 8f;

    [Tooltip("How far left/right the dead player can look.")]
    [SerializeField]
    private float deadMaxYaw = 90f;

    [Tooltip("How far up the dead player can look.")]
    [SerializeField]
    private float deadMaxPitchUp = 75f;

    [Tooltip("How far down the dead player can look. 0 means horizon only, no looking down into chest.")]
    [SerializeField]
    private float deadMaxPitchDown = 0f;

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

    [Header("Phone Pitch (RMB)")]
    [SerializeField]
    private bool lockPitchWhilePhoneUp = true;

    [SerializeField]
    private float phoneHorizonOffsetDegrees = 0f;

    [SerializeField]
    private bool phoneSoftPitchCentering = true;

    [SerializeField, Range(0f, 1f)]
    private float phonePitchInputScale = 0.35f;

    [SerializeField]
    private float phonePitchSmoothTime = 0.22f;

    [SerializeField]
    private float phonePitchMaxSpeed = 0f;

    [SerializeField]
    private float phonePitchSnapEps = 0.10f;

    private float _phonePitchVel;
    private float _slidePitchVel;
    private float _coilPitchVel;

    [Header("Crouch + Phone Yaw Lock")]
    [SerializeField]
    private float crouchPhoneAlignEps = 0.02f;

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

    private bool _deadLookMode;
    private bool _usingDeadSocketLastFrame;
    private Vector3 _baseCameraRootLocalPos;

    public float Pitch => xRotation;
    public float YawOffset => yawOffset;
    public bool DeadLookMode => _deadLookMode;

    private bool phoneAimActive = false;
    private bool rmbHeld = false;

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
        deathState = GetComponent<PlayerDeathState>();

        if (cameraRoot == null && cam != null)
            cameraRoot = cam.transform.parent;

        if (cameraRoot != null)
            _baseCameraRootLocalPos = cameraRoot.localPosition;

        if (!useInspectorPositions)
            AutoFillFromCurrentCamera();

        if (deadLocalPos == Vector3.zero)
            deadLocalPos = standingLocalPos + new Vector3(0f, -1.35f, 0.08f);

        _smoothedBaseLocalPos = standingLocalPos;

        if (cam != null)
            cam.fieldOfView = defaultFOV;

        _wasCrouching = motor != null && motor.crouching;
    }

    void Update()
    {
        if (!IsOwner)
            return;

        float dt = Time.deltaTime;

        if (deathState != null)
            _deadLookMode = deathState.IsDead;

        bool usingDeadSocket = _deadLookMode && deadCameraSocket != null && cameraRoot != null;

        bool actuallySprinting = !_deadLookMode && motor != null && motor.IsActuallySprinting;

        Vector3 baseTargetPos;

        if (_deadLookMode && !usingDeadSocket)
        {
            baseTargetPos = deadLocalPos;
        }
        else if (!_deadLookMode && motor != null && motor.sliding)
        {
            baseTargetPos = slidingLocalPos;
        }
        else if (!_deadLookMode && motor != null && motor.Coiling)
        {
            baseTargetPos = coilLocalPos;
        }
        else
        {
            bool isAirborne = animator != null && !animator.GetBool("IsGrounded");
            float zVel = animator != null ? animator.GetFloat("Z_Velocity") : 0f;

            if (!_deadLookMode && isAirborne && zVel > sprintJumpZThreshold)
                baseTargetPos = sprintJumpLocalPos;
            else if (!_deadLookMode && actuallySprinting)
                baseTargetPos = sprintingLocalPos;
            else if (!_deadLookMode && motor != null && motor.crouching)
                baseTargetPos = crouchingLocalPos;
            else
            {
                Vector2 move = motor != null ? motor.inputDirection : Vector2.zero;
                float ax = Mathf.Abs(move.x);
                float ay = Mathf.Abs(move.y);
                float thr = moveDirThreshold;
                bool moving = (ax > thr) || (ay > thr);

                if (!_deadLookMode && moving)
                {
                    if (ax > ay)
                        baseTargetPos = walkSideLocalPos;
                    else if (move.y > 0f)
                        baseTargetPos = walkForwardLocalPos;
                    else
                        baseTargetPos = walkBackwardLocalPos;
                }
                else
                {
                    baseTargetPos = standingLocalPos;
                }
            }
        }

        if (usingDeadSocket)
        {
            ApplyDeadSocketCamera(dt);
        }
        else
        {
            RestoreCameraRootIfNeeded();

            if (_deadLookMode)
            {
                _smoothedBaseLocalPos = Vector3.Lerp(
                    _smoothedBaseLocalPos,
                    baseTargetPos,
                    dt * deadCameraLerpSpeed
                );
            }
            else
            {
                bool useLinear = motor != null && (motor.crouching || _wasCrouching);

                if (useLinear)
                {
                    _smoothedBaseLocalPos = Vector3.MoveTowards(
                        _smoothedBaseLocalPos,
                        baseTargetPos,
                        crouchPositionSpeed * dt
                    );
                }
                else
                {
                    _smoothedBaseLocalPos = Vector3.Lerp(
                        _smoothedBaseLocalPos,
                        baseTargetPos,
                        dt * positionLerpSpeed
                    );
                }
            }

            Vector3 pitchOffset = Vector3.zero;

            if (!_deadLookMode)
            {
                float up01 = Mathf.Clamp01(-xRotation / 90f);
                float down01 = Mathf.Clamp01(xRotation / 90f);

                bool isCrouched = motor != null && motor.crouching;
                float fwdMax = isCrouched ? crouchPitchForwardMax : pitchForwardMax;
                float backMax = isCrouched ? crouchPitchBackwardMax : pitchBackwardMax;
                float downMaxY = isCrouched ? crouchPitchDownY : pitchDownY;
                float upMaxY = isCrouched ? crouchPitchUpY : pitchUpY;

                float yPitch = (-down01 * downMaxY) + (up01 * upMaxY);
                float zPitch = (down01 * fwdMax) - (up01 * backMax);

                pitchOffset = new Vector3(0f, yPitch, zPitch);
            }

            if (cam != null)
                cam.transform.localPosition = _smoothedBaseLocalPos + pitchOffset;
        }

        bool fastState = !_deadLookMode && (actuallySprinting || (motor != null && motor.sliding));
        float targetFOV = fastState ? sprintFOV : defaultFOV;

        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, dt * fovTransitionSpeed);

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        _wasCrouching = motor != null && motor.crouching;
    }

    private void ApplyDeadSocketCamera(float dt)
    {
        _usingDeadSocketLastFrame = true;

        if (cameraRoot == null || deadCameraSocket == null)
            return;

        cameraRoot.position = Vector3.Lerp(
            cameraRoot.position,
            deadCameraSocket.position,
            dt * deadCameraLerpSpeed
        );

        if (cam != null)
        {
            cam.transform.localPosition = Vector3.Lerp(
                cam.transform.localPosition,
                deadSocketCameraLocalOffset,
                dt * deadCameraLerpSpeed
            );
        }
    }

    private void RestoreCameraRootIfNeeded()
    {
        if (!_usingDeadSocketLastFrame)
            return;

        _usingDeadSocketLastFrame = false;

        if (cameraRoot != null)
            cameraRoot.localPosition = _baseCameraRootLocalPos;

        _smoothedBaseLocalPos = standingLocalPos;
    }

    public void ProcessLook(Vector2 input)
    {
        if (!IsOwner)
            return;

        float dt = Time.deltaTime;

        if (deathState != null)
            _deadLookMode = deathState.IsDead;

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

        float rawPitchDelta = -mouseY * dt * ySensitivity;

        if (_deadLookMode)
        {
            xRotation += rawPitchDelta;
            xRotation = Mathf.Clamp(xRotation, -deadMaxPitchUp, deadMaxPitchDown);

            if (cam != null)
                cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

            float yawDelta = mouseX * dt * xSensitivity;
            yawOffset = Mathf.Clamp(yawOffset + yawDelta, -deadMaxYaw, deadMaxYaw);

            catchingUpStationary = false;
            wasMoving = false;

            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

            return;
        }

        bool isSliding = motor != null && motor.sliding;
        bool isCoiling = motor != null && motor.Coiling;

        bool phoneAllowedNow = phoneAimActive && motor != null && !isSliding && !isCoiling;

        if (lockPitchWhilePhoneUp && phoneAllowedNow)
        {
            float target = Mathf.Clamp(0f + phoneHorizonOffsetDegrees, -PitchClamp, PitchClamp);

            float smoothTime = Mathf.Max(0.0001f, phonePitchSmoothTime);
            float maxSpeed =
                (phonePitchMaxSpeed <= 0f) ? float.PositiveInfinity : phonePitchMaxSpeed;

            if (phoneSoftPitchCentering)
            {
                xRotation += rawPitchDelta * phonePitchInputScale;
                xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);

                xRotation = Mathf.SmoothDampAngle(
                    xRotation,
                    target,
                    ref _phonePitchVel,
                    smoothTime,
                    maxSpeed,
                    dt
                );
            }
            else
            {
                xRotation = Mathf.SmoothDampAngle(
                    xRotation,
                    target,
                    ref _phonePitchVel,
                    smoothTime,
                    maxSpeed,
                    dt
                );
            }

            xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);

            if (Mathf.Abs(Mathf.DeltaAngle(xRotation, target)) <= phonePitchSnapEps)
            {
                xRotation = target;
                _phonePitchVel = 0f;
            }

            _slidePitchVel = 0f;
            _coilPitchVel = 0f;
        }
        else if ((isSliding || isCoiling) && xRotation > 0f)
        {
            float smoothTime = Mathf.Max(0.0001f, phonePitchSmoothTime);
            float maxSpeed =
                (phonePitchMaxSpeed <= 0f) ? float.PositiveInfinity : phonePitchMaxSpeed;

            xRotation += rawPitchDelta * phonePitchInputScale;
            xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);

            if (isSliding)
            {
                xRotation = Mathf.SmoothDampAngle(
                    xRotation,
                    0f,
                    ref _slidePitchVel,
                    smoothTime,
                    maxSpeed,
                    dt
                );

                if (xRotation < 0f)
                {
                    xRotation = 0f;
                    _slidePitchVel = 0f;
                }

                _coilPitchVel = 0f;
            }
            else
            {
                xRotation = Mathf.SmoothDampAngle(
                    xRotation,
                    0f,
                    ref _coilPitchVel,
                    smoothTime,
                    maxSpeed,
                    dt
                );

                if (xRotation < 0f)
                {
                    xRotation = 0f;
                    _coilPitchVel = 0f;
                }

                _slidePitchVel = 0f;
            }

            if (xRotation <= phonePitchSnapEps)
            {
                xRotation = 0f;
                _slidePitchVel = 0f;
                _coilPitchVel = 0f;
            }

            _phonePitchVel = 0f;
        }
        else
        {
            _phonePitchVel = 0f;
            _slidePitchVel = 0f;
            _coilPitchVel = 0f;

            xRotation += rawPitchDelta;
            xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);
        }

        if (cam != null)
            cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        float normalYawDelta = mouseX * dt * xSensitivity;
        bool hasMoveInput = motor != null && motor.inputDirection.sqrMagnitude > 0.0001f;

        if (phoneAllowedNow && motor != null && motor.crouching)
        {
            float alignStep =
                bodyCatchUpSpeed * dt * Mathf.Max(0.01f, crouchPhoneAlignSpeedMultiplier);

            if (Mathf.Abs(yawOffset) > crouchPhoneAlignEps)
            {
                float newOffset = yawOffset + normalYawDelta;
                float clampedYaw = Mathf.Clamp(newOffset, -maxShoulderYaw, maxShoulderYaw);
                float overflow = newOffset - clampedYaw;

                yawOffset = clampedYaw;

                if (Mathf.Abs(overflow) > 0.0001f)
                    transform.rotation *= Quaternion.Euler(0f, overflow, 0f);

                CatchUp(alignStep);

                if (Mathf.Abs(yawOffset) <= crouchPhoneAlignEps)
                    yawOffset = 0f;

                catchingUpStationary = false;
            }
            else
            {
                yawOffset = 0f;
                transform.rotation *= Quaternion.Euler(0f, normalYawDelta, 0f);
                catchingUpStationary = false;
            }

            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

            wasMoving = hasMoveInput;
            return;
        }

        bool shoulderMode =
            !hasMoveInput
            && motor != null
            && !motor.sliding
            && !motor.sprinting
            && (motor.crouching ? shoulderWhileCrouching : true);

        bool allowInstantCatchUp =
            instantCatchUpOnMove && !(phoneAllowedNow && disableInstantCatchUpWhilePhone);

        float speedMulNormal = phoneAllowedNow ? phoneCatchUpSpeedMultiplier : 1f;

        if (shoulderMode)
        {
            float newOffset = yawOffset + normalYawDelta;
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

            transform.rotation *= Quaternion.Euler(0f, normalYawDelta, 0f);

            if (!allowInstantCatchUp && hasMoveInput && Mathf.Abs(yawOffset) > 0.01f)
                CatchUp(catchUpOnMoveSpeed * dt * speedMulNormal);

            catchingUpStationary = false;
        }

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        wasMoving = hasMoveInput;
    }

    public void SetDeadLookMode(bool dead)
    {
        _deadLookMode = dead;

        catchingUpStationary = false;
        wasMoving = false;

        if (dead)
        {
            yawOffset = Mathf.Clamp(yawOffset, -deadMaxYaw, deadMaxYaw);
            xRotation = Mathf.Clamp(xRotation, -deadMaxPitchUp, deadMaxPitchDown);
        }
        else
        {
            RestoreCameraRootIfNeeded();

            if (Mathf.Abs(yawOffset) > maxShoulderYaw)
                yawOffset = Mathf.Clamp(yawOffset, -maxShoulderYaw, maxShoulderYaw);

            xRotation = Mathf.Clamp(xRotation, -PitchClamp, PitchClamp);
        }

        if (cam != null)
            cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
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

        if (deadLocalPos == Vector3.zero)
            deadLocalPos = basePos + new Vector3(0f, -1.35f, 0.08f);

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
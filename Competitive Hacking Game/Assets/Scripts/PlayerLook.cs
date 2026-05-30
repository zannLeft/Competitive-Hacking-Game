using Unity.Netcode;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    private PlayerMotor motor;
    private Animator animator;

    [Header("References")]
    public Camera cam;

    [Tooltip("Parent pivot of the main camera (your empty 'Camera' object in the middle of the player).")]
    public Transform cameraRoot;

    [SerializeField]
    private PlayerSitAction sitAction;

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

    [Header("Laptop / Sitting Camera")]
    [SerializeField]
    private Vector3 sittingLocalPos;

    [Tooltip("Duration of the camera lowering/rising while sitting/standing.")]
    [SerializeField]
    private float sittingCameraSmoothTime = 0.8f;

    [Tooltip("How close the camera must be before the sitting/standing transition is considered finished.")]
    [SerializeField]
    private float sittingCameraArriveDistance = 0.01f;

    [Tooltip("FOV while laptop is open/focused.")]
    [SerializeField]
    private float laptopFocusFOV = 40f;

    [SerializeField]
    private float laptopFocusFOVSpeed = 1f;

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

    [Header("Sitting Look Limit")]
    [SerializeField]
    private bool lockBodyYawWhileSitting = true;

    [SerializeField]
    private float sittingMaxYaw = 80f;

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

    [Tooltip("Optional tiny offset if your camera prefab isn't perfectly level. Try -2..+2.")]
    [SerializeField]
    private float phoneHorizonOffsetDegrees = 0f;

    [Tooltip("If ON: you can still move pitch a bit, but it's constantly pulled toward horizon.")]
    [SerializeField]
    private bool phoneSoftPitchCentering = true;

    [Tooltip("How much mouse pitch input is allowed while phone is up.")]
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
    private Vector3 _sittingCamVel;

    private bool _wasCrouching;
    private bool _wasUsingSittingCamera;
    private bool _wasLaptopFocus;

    private bool _sittingCameraTransitionActive;

    private enum SittingCameraBlendMode
    {
        None,
        SittingDown,
        StandingUp,
    }

    private SittingCameraBlendMode _sittingCamBlendMode = SittingCameraBlendMode.None;
    private Vector3 _sittingCamStartLocalPos;
    private Vector3 _sittingCamTargetLocalPos;
    private float _sittingCamBlendTimer;

    public float Pitch => xRotation;
    public float YawOffset => yawOffset;

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

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (!useInspectorPositions)
            AutoFillFromCurrentCamera();

        _smoothedBaseLocalPos = standingLocalPos;

        if (cam != null)
            cam.fieldOfView = defaultFOV;

        if (cameraRoot == null && cam != null)
            cameraRoot = cam.transform.parent;

        _wasCrouching = motor != null && motor.crouching;
        _wasUsingSittingCamera = sitAction != null && sitAction.UseSittingCameraPosition;
        _wasLaptopFocus = sitAction != null && sitAction.LaptopCameraFocus;
        _sittingCameraTransitionActive = false;
    }

    void Update()
    {
        if (!IsOwner)
            return;

        float dt = Time.deltaTime;

        bool actuallySprinting = motor != null && motor.IsActuallySprinting;
        bool useSittingCamera = sitAction != null && sitAction.UseSittingCameraPosition;
        bool laptopFocus = sitAction != null && sitAction.LaptopCameraFocus;

        Vector3 baseTargetPos;

        if (useSittingCamera || laptopFocus)
        {
            baseTargetPos = sittingLocalPos;
        }
        else if (motor != null && motor.sliding)
        {
            baseTargetPos = slidingLocalPos;
        }
        else if (motor != null && motor.Coiling)
        {
            baseTargetPos = coilLocalPos;
        }
        else
        {
            bool isAirborne = animator != null && !animator.GetBool("IsGrounded");
            float zVel = animator != null ? animator.GetFloat("Z_Velocity") : 0f;

            if (isAirborne && zVel > sprintJumpZThreshold)
            {
                baseTargetPos = sprintJumpLocalPos;
            }
            else if (actuallySprinting)
            {
                baseTargetPos = sprintingLocalPos;
            }
            else if (motor != null && motor.crouching)
            {
                baseTargetPos = crouchingLocalPos;
            }
            else
            {
                Vector2 move = motor != null ? motor.inputDirection : Vector2.zero;
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
                {
                    baseTargetPos = standingLocalPos;
                }
            }
        }

        bool sittingCameraState = useSittingCamera || laptopFocus;
        bool previousSittingCameraState = _wasUsingSittingCamera || _wasLaptopFocus;

        // Start a custom camera transition whenever we enter OR leave sitting camera mode.
        if (sittingCameraState != previousSittingCameraState)
        {
            _sittingCameraTransitionActive = true;

            _sittingCamBlendMode = sittingCameraState
                ? SittingCameraBlendMode.SittingDown
                : SittingCameraBlendMode.StandingUp;

            _sittingCamStartLocalPos = _smoothedBaseLocalPos;
            _sittingCamTargetLocalPos = baseTargetPos;
            _sittingCamBlendTimer = 0f;
            _sittingCamVel = Vector3.zero;
        }

        if (sittingCameraState || _sittingCameraTransitionActive)
        {
            if (_sittingCameraTransitionActive)
            {
                // Keep the target fresh while standing up, in case the normal target changes.
                _sittingCamTargetLocalPos = baseTargetPos;

                float duration = Mathf.Max(0.0001f, sittingCameraSmoothTime);
                _sittingCamBlendTimer += dt;

                float t = Mathf.Clamp01(_sittingCamBlendTimer / duration);

                float easedT =
                    _sittingCamBlendMode == SittingCameraBlendMode.StandingUp
                        ? EaseInOutCubic(t) // slow start -> speed up -> slow at top
                        : EaseOutCubic(t); // fast start -> slow into seated position

                _smoothedBaseLocalPos = Vector3.LerpUnclamped(
                    _sittingCamStartLocalPos,
                    _sittingCamTargetLocalPos,
                    easedT
                );

                float dist = Vector3.Distance(
                    _smoothedBaseLocalPos,
                    _sittingCamTargetLocalPos
                );

                if (t >= 1f || dist <= sittingCameraArriveDistance)
                {
                    _smoothedBaseLocalPos = _sittingCamTargetLocalPos;
                    _sittingCameraTransitionActive = false;
                    _sittingCamBlendMode = SittingCameraBlendMode.None;
                    _sittingCamBlendTimer = 0f;
                    _sittingCamVel = Vector3.zero;
                }
            }
            else
            {
                // Already sitting/focused: stay smoothly locked to the sitting camera position.
                _smoothedBaseLocalPos = Vector3.SmoothDamp(
                    _smoothedBaseLocalPos,
                    baseTargetPos,
                    ref _sittingCamVel,
                    Mathf.Max(0.0001f, sittingCameraSmoothTime),
                    Mathf.Infinity,
                    dt
                );
            }
        }
        else
        {
            _sittingCamVel = Vector3.zero;

            bool useLinear = (motor != null && motor.crouching) || _wasCrouching;

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

        float up01 = Mathf.Clamp01(-xRotation / 90f);
        float down01 = Mathf.Clamp01(xRotation / 90f);

        bool isCrouched = motor != null && motor.crouching;
        float fwdMax = isCrouched ? crouchPitchForwardMax : pitchForwardMax;
        float backMax = isCrouched ? crouchPitchBackwardMax : pitchBackwardMax;
        float downMaxY = isCrouched ? crouchPitchDownY : pitchDownY;
        float upMaxY = isCrouched ? crouchPitchUpY : pitchUpY;

        float yPitch = (-down01 * downMaxY) + (up01 * upMaxY);
        float zPitch = (down01 * fwdMax) - (up01 * backMax);

        if (cam != null)
            cam.transform.localPosition =
                _smoothedBaseLocalPos + new Vector3(0f, yPitch, zPitch);

        bool fastState = actuallySprinting || (motor != null && motor.sliding);

        float targetFOV = laptopFocus ? laptopFocusFOV : (fastState ? sprintFOV : defaultFOV);
        float fovSpeed = (laptopFocus || _wasLaptopFocus)
            ? laptopFocusFOVSpeed
            : fovTransitionSpeed;

        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, dt * fovSpeed);

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        _wasCrouching = motor != null && motor.crouching;
        _wasUsingSittingCamera = useSittingCamera;
        _wasLaptopFocus = laptopFocus;
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

        bool isSliding = motor != null && motor.sliding;
        bool isCoiling = motor != null && motor.Coiling;

        bool phoneAllowedNow = phoneAimActive && motor != null && !isSliding && !isCoiling;

        float rawPitchDelta = -mouseY * dt * ySensitivity;

        // ---------------- PITCH ----------------
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

        // ---------------- YAW ----------------
        float yawDelta = mouseX * dt * xSensitivity;
        bool hasMoveInput = motor != null && motor.inputDirection.sqrMagnitude > 0.0001f;

        // ---------------- SITTING YAW LOCK ----------------
        // While sitting / sitting down / standing up, allow camera shoulder-look,
        // but do NOT rotate the whole player body. Just clamp at the sitting limit.
        bool sittingYawLocked =
            lockBodyYawWhileSitting
            && sitAction != null
            && sitAction.IsSittingOrTransitioning;

        if (sittingYawLocked)
        {
            yawOffset = Mathf.Clamp(
                yawOffset + yawDelta,
                -Mathf.Abs(sittingMaxYaw),
                Mathf.Abs(sittingMaxYaw)
            );

            catchingUpStationary = false;

            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

            wasMoving = hasMoveInput;
            return;
        }

        if (phoneAllowedNow && motor != null && motor.crouching)
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
            && motor != null
            && !motor.sliding
            && !motor.sprinting
            && (motor.crouching ? shoulderWhileCrouching : true);

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

    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);

        if (t < 0.5f)
            return 4f * t * t * t;

        return 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
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

        if (sittingLocalPos == Vector3.zero)
            sittingLocalPos = basePos + new Vector3(0f, -1f, 0.1f);

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
using System;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

public class PlayerMotor : NetworkBehaviour
{
    private CharacterController controller;
    private Animator animator;
    private PlayerLook look;

    private bool controllerCollisionEnabled = true;

    private Vector3 playerVelocity;
    private Vector3 moveDirection = Vector3.zero;

    private bool isGrounded;
    private bool wasGrounded = false;
    private bool couldStand = true;
    private bool canStand = true;

    public bool sprinting = false;
    public bool crouching = false;
    public bool sliding = false;

    private bool sprintButtonHeld = false;
    private bool crouchButtonHeld = false;
    private bool jumpButtonHeld = false;
    private bool jumpedFromSlide = false;
    private bool reCrouchAfterJump = false;

    private bool startedCrouchingInAir = false;

    // NEW: sitting uses crouch-sized collider without setting animator crouch bool.
    private bool sittingCollider = false;

    [Header("Networked Collider")]
    [Tooltip("Minimum height/center-Y change before the owner sends a collider update.")]
    [SerializeField]
    private float colliderSyncThreshold = 0.002f;

    private NetworkVariable<float> networkedColliderHeight = new NetworkVariable<float>(
        1.685f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<float> networkedColliderCenterY = new NetworkVariable<float>(
        0.92f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public bool IsGrounded => isGrounded;
    public bool UsingSittingCollider => sittingCollider;

    [Header("Speeds")]
    [SerializeField]
    private float speed = 5f;

    [SerializeField]
    private float sprintSpeed = 7.5f;

    [SerializeField]
    private float jumpHeight = 2f;

    [Header("Collider (Stand/Crouch)")]
    [SerializeField]
    private float crouchHeight = 0.80129076f;

    [SerializeField]
    private float standHeight = 1.685f;

    [SerializeField]
    private float crouchCenterY = 0.47814538f;

    [SerializeField]
    private float standCenterY = 0.92f;

    [Header("Slide")]
    [SerializeField]
    private float slideSpeed = 8f;

    [SerializeField]
    private float slideSpeedDecay = 6f;

    [SerializeField]
    private float minSlideSpeed = 2f;

    [SerializeField]
    private float slideCancelGrace = 0.33f;

    private float slideElapsed;
    private float slideTimer;
    private const float slideTimerMax = 1.3f;

    [Header("Coil (Air-Only)")]
    [SerializeField]
    private float coilHeight = 1.2f;

    [SerializeField]
    private bool coiling = false;

    public bool Coiling => coiling;

    [Header("Stand Check")]
    [Tooltip("Layers that can block standing up. Exclude Player, include ceilings/walls/level geo.")]
    [SerializeField]
    private LayerMask standObstructionMask = ~0;

    [SerializeField]
    private float standCheckEpsilon = 0.01f;

    // Camera wall protection is intentionally fixed in code rather than exposed as a
    // large group of tuning fields. It uses the same environment mask as the stand
    // obstruction check and automatically accounts for the camera's current local
    // position, pitch, yaw, near clip plane, aspect ratio, and FOV (including sprint FOV).
    private const float CameraWallPadding = 0.0125f;
    private const float CameraWallGroundStickDistance = 0.002f;
    private const float CameraWallMaxCorrectionPerPass = 0.5f;
    private const int CameraWallResolvePasses = 4;
    private const float CameraWallMaxNormalY = 0.65f;

    private readonly RaycastHit[] cameraWallHits = new RaycastHit[16];

    // Full near-plane coverage. The corners are especially important while sprinting,
    // because the wider FOV makes them extend farther sideways into nearby walls.
    private static readonly Vector2[] CameraNearPlaneSamples =
    {
        new Vector2(0f, 0f),
        new Vector2(-1f, -1f),
        new Vector2(1f, -1f),
        new Vector2(-1f, 1f),
        new Vector2(1f, 1f),
        new Vector2(0f, -1f),
        new Vector2(0f, 1f),
        new Vector2(-1f, 0f),
        new Vector2(1f, 0f),
    };

    [Header("Airborne Behavior")]
    [SerializeField]
    private float fallLeaveGroundVy = 0.05f;

    private float currentHeight;
    private Vector3 currentCenter;
    private float currentSpeed;
    private float currentSpeedVertical;
    private float speedLerpTime = 8f;
    private float targetSpeed = 0f;
    private float smoothSpeed = 7.5f;
    private float fallSpeed = 1.5f;

    public Vector2 inputDirection;

    private int xVelHash;
    private int zVelHash;
    private int yVelHash;

    private bool useMirror;

    private float lastAirbornePlanarSpeed = 0f;

    // CharacterController.velocity reflects the most recent Move call. Camera wall
    // correction runs later in the frame, so keep gameplay velocity separately to
    // avoid that correction affecting slide cancellation, landing, or animations.
    private Vector3 lastGameplayControllerVelocity = Vector3.zero;

    public bool IsSprintHeld => sprintButtonHeld;
    public bool IsCrouchHeld => crouchButtonHeld;

    public bool IsActuallySprinting =>
        sprinting && isGrounded && !sliding && inputDirection.y > 0.01f && currentSpeed > 0.1f;

    private void Awake()
    {
        CacheComponents();
    }

    void Start()
    {
        CacheComponents();

        crouchCenterY = standCenterY - (standHeight - crouchHeight) / 2f;

        ResetCrouchAndSlide();

        xVelHash = Animator.StringToHash("X_Velocity");
        zVelHash = Animator.StringToHash("Z_Velocity");
        yVelHash = Animator.StringToHash("Y_Velocity");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CacheComponents();

        networkedColliderHeight.OnValueChanged += OnNetworkedColliderDimensionsChanged;
        networkedColliderCenterY.OnValueChanged += OnNetworkedColliderDimensionsChanged;

        if (IsOwner)
            SyncOwnedColliderDimensions(force: true);
        else
            ApplyNetworkedColliderDimensions();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        networkedColliderHeight.OnValueChanged -= OnNetworkedColliderDimensionsChanged;
        networkedColliderCenterY.OnValueChanged -= OnNetworkedColliderDimensionsChanged;
    }

    private void CacheComponents()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (look == null)
            look = GetComponent<PlayerLook>();
    }

    public void SetControllerCollisionEnabled(bool enabled)
    {
        CacheComponents();

        if (controller == null)
            return;

        controllerCollisionEnabled = enabled;

        if (enabled)
        {
            if (!controller.enabled)
                controller.enabled = true;

            ClearMovementState();
            ResetCrouchAndSlide();
            SyncOwnedColliderDimensions(force: true);
            return;
        }

        ClearMovementState();

        if (controller.enabled)
            controller.enabled = false;
    }

    private void ClearMovementState()
    {
        playerVelocity = Vector3.zero;
        moveDirection = Vector3.zero;
        inputDirection = Vector2.zero;

        currentSpeed = 0f;
        currentSpeedVertical = 0f;
        targetSpeed = 0f;
        lastAirbornePlanarSpeed = 0f;
        lastGameplayControllerVelocity = Vector3.zero;

        sprinting = false;
        crouching = false;
        sliding = false;
        coiling = false;
        sittingCollider = false;

        sprintButtonHeld = false;
        crouchButtonHeld = false;
        jumpButtonHeld = false;
        jumpedFromSlide = false;
        reCrouchAfterJump = false;
        startedCrouchingInAir = false;

        slideTimer = slideTimerMax;
        slideElapsed = 0f;
        slideSpeed = 8f;

        if (animator == null)
            return;

        animator.SetBool("Crouching", false);
        animator.SetBool("Sliding", false);
        animator.SetBool("Coiling", false);

        if (xVelHash != 0)
            animator.SetFloat(xVelHash, 0f);

        if (zVelHash != 0)
            animator.SetFloat(zVelHash, 0f);

        if (yVelHash != 0)
            animator.SetFloat(yVelHash, 0f);
    }

    private void OnNetworkedColliderDimensionsChanged(float previousValue, float newValue)
    {
        if (IsOwner)
            return;

        ApplyNetworkedColliderDimensions();
    }

    private void ApplyNetworkedColliderDimensions()
    {
        CacheComponents();

        if (controller == null)
            return;

        float safeHeight = Mathf.Max(networkedColliderHeight.Value, controller.radius * 2f);
        Vector3 center = controller.center;
        center.y = networkedColliderCenterY.Value;

        controller.height = safeHeight;
        controller.center = center;
    }

    private void SyncOwnedColliderDimensions(bool force = false)
    {
        if (!IsSpawned || !IsOwner || controller == null)
            return;

        if (
            force
            || Mathf.Abs(networkedColliderHeight.Value - controller.height) > colliderSyncThreshold
        )
            networkedColliderHeight.Value = controller.height;

        if (
            force
            || Mathf.Abs(networkedColliderCenterY.Value - controller.center.y) > colliderSyncThreshold
        )
            networkedColliderCenterY.Value = controller.center.y;
    }

    public Vector3 GetColliderTopWorldPosition()
    {
        CacheComponents();

        if (controller == null)
            return transform.position + Vector3.up * standHeight;

        Vector3 localTop = controller.center + Vector3.up * (controller.height * 0.5f);
        return transform.TransformPoint(localTop);
    }

    void Update()
    {
        CacheComponents();

        if (controller == null || !controller.enabled || !controllerCollisionEnabled)
            return;

        if (!IsOwner)
        {
            ApplyNetworkedColliderDimensions();
            return;
        }

        UpdateGroundStatus();
        HandleSliding();
        HandleJumping();
        UpdateStandStatus();
        UpdateCharacterDimensions();
        SyncOwnedColliderDimensions();
    }

    public void SetSittingCollider(bool sitting)
    {
        if (sittingCollider == sitting)
            return;

        sittingCollider = sitting;

        if (sittingCollider)
        {
            // Sitting should cancel movement posture states without pretending to be crouching.
            sprinting = false;
            sliding = false;
            coiling = false;

            if (animator != null)
            {
                animator.SetBool("Sliding", false);
                animator.SetBool("Coiling", false);
            }

            slideTimer = slideTimerMax;
            slideElapsed = 0f;
            slideSpeed = 8f;
        }
    }

    private void ResetCrouchAndSlide()
    {
        if (controller == null)
            return;

        currentHeight = standHeight;
        controller.height = currentHeight;
        controller.center = new Vector3(controller.center.x, standCenterY, controller.center.z);
        slideTimer = slideTimerMax;
        slideElapsed = 0f;

        SyncOwnedColliderDimensions(force: true);
    }

    private void HandleSliding()
    {
        if (sittingCollider)
            return;

        if (!sliding)
            return;

        float dt = Time.deltaTime;
        slideElapsed += dt;
        slideTimer -= dt;

        slideSpeed = Mathf.Max(slideSpeed - slideSpeedDecay * dt, minSlideSpeed);

        bool graceOver = slideElapsed >= slideCancelGrace;
        if (
            slideTimer <= 0f
            || (graceOver && currentSpeed < 2f)
            || (graceOver && currentSpeedVertical > 1f)
        )
        {
            if (crouchButtonHeld)
            {
                crouching = true;
                sprinting = false;
            }
            else if (sprintButtonHeld && CanStand())
            {
                crouching = false;
                sprinting = true;
            }
            else
            {
                crouching = true;
                sprinting = false;
            }

            sliding = false;
            animator.SetBool("Sliding", false);
            animator.SetBool("Crouching", crouching);

            slideTimer = slideTimerMax;
            slideElapsed = 0f;
            slideSpeed = 8f;
        }
    }

    private void UpdateStandStatus()
    {
        if (sittingCollider)
            return;

        canStand = CanStand();

        if (crouching && !sliding && canStand && !crouchButtonHeld)
        {
            crouching = false;
            animator.SetBool("Crouching", false);
            sprinting = sprintButtonHeld;
        }

        couldStand = canStand;
    }

    private void HandleJumping()
    {
        if (sittingCollider)
            return;

        if (jumpButtonHeld && isGrounded && !sliding && CanStand())
        {
            bool jumpedFromCrouch = crouching;

            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            animator.SetTrigger("Jump");
            animator.SetBool("useMirror", useMirror);

            if (jumpedFromCrouch)
            {
                reCrouchAfterJump = true;
                crouching = false;
                animator.SetBool("Crouching", false);
            }

            if (sliding)
            {
                slideTimer = 0f;
                jumpedFromSlide = true;
            }
        }
    }

    private void UpdateGroundStatus()
    {
        isGrounded = controller.isGrounded;
        animator.SetBool("IsGrounded", isGrounded);

        if (isGrounded && !wasGrounded)
        {
            useMirror = !useMirror;
            Land();
        }
        else if (!isGrounded && wasGrounded)
        {
            BecameAirborne();
        }

        wasGrounded = isGrounded;
    }

    private void BecameAirborne()
    {
        float vy = lastGameplayControllerVelocity.y;
        bool falling = vy <= fallLeaveGroundVy;

        if (falling && crouching && !sliding)
        {
            if (CanStand())
            {
                crouching = false;
                animator.SetBool("Crouching", false);
            }
        }
    }

    private void UpdateCharacterDimensions()
    {
        float targetHeight;
        float targetCenterY;

        if (sittingCollider)
        {
            targetHeight = crouchHeight;
            targetCenterY = crouchCenterY;
        }
        else if (coiling && !isGrounded)
        {
            targetHeight = coilHeight;
            targetCenterY = standCenterY;
        }
        else if (crouching || sliding)
        {
            targetHeight = crouchHeight;
            targetCenterY = crouchCenterY;
        }
        else
        {
            targetHeight = standHeight;
            targetCenterY = standCenterY;
        }

        if (
            Mathf.Abs(controller.height - targetHeight) > 0.001f
            || Mathf.Abs(controller.center.y - targetCenterY) > 0.001f
        )
        {
            float dt = Time.deltaTime;
            float newHeight = Mathf.Lerp(controller.height, targetHeight, 20.0f * dt);
            Vector3 desiredCenter = new Vector3(
                controller.center.x,
                targetCenterY,
                controller.center.z
            );
            Vector3 newCenter = Vector3.Lerp(controller.center, desiredCenter, 20.0f * dt);

            if (isGrounded)
            {
                float oldBottom =
                    transform.position.y
                    + controller.center.y
                    - controller.height * 0.5f
                    + controller.radius;
                float newBottom =
                    transform.position.y + newCenter.y - newHeight * 0.5f + controller.radius;

                controller.height = newHeight;
                controller.center = newCenter;

                float deltaY = oldBottom - newBottom;
                if (Mathf.Abs(deltaY) > 0.0001f)
                    controller.Move(new Vector3(0f, deltaY, 0f));
            }
            else
            {
                controller.height = newHeight;
                controller.center = newCenter;
            }
        }
    }

    #region Movement
    public void ProcessMove(Vector2 input)
    {
        inputDirection = input;

        if (controller == null || !controller.enabled || !controllerCollisionEnabled)
        {
            inputDirection = Vector2.zero;
            return;
        }

        Vector3 planarVel = new Vector3(
            lastGameplayControllerVelocity.x,
            0f,
            lastGameplayControllerVelocity.z
        );
        currentSpeed = planarVel.magnitude;
        currentSpeedVertical = lastGameplayControllerVelocity.y;

        if (!isGrounded)
            lastAirbornePlanarSpeed = Mathf.Max(lastAirbornePlanarSpeed * 0.9f, currentSpeed);

        playerVelocity.y += Physics.gravity.y * Time.deltaTime * fallSpeed;

        if (isGrounded && !sliding)
            smoothSpeed = 9f;
        else if (sliding)
            smoothSpeed = 0.5f;
        else
            smoothSpeed = 1f;

        Quaternion moveSpace = (look != null) ? look.MoveSpaceRotation : transform.rotation;
        Vector3 targetDirection = moveSpace * new Vector3(input.x, 0f, input.y);

        moveDirection = Vector3.Lerp(moveDirection, targetDirection, Time.deltaTime * smoothSpeed);

        if (isGrounded)
        {
            if (sliding)
            {
                targetSpeed = slideSpeed;
            }
            else if (input.y > 0 && sprinting)
            {
                targetSpeed = sprintSpeed;
                speedLerpTime = 6f;
            }
            else if (input.x != 0 || input.y != 0)
            {
                targetSpeed = 2f;
                speedLerpTime = 8f;
            }
            else
            {
                speedLerpTime = 8f;
            }
        }

        speed = Mathf.Lerp(speed, targetSpeed, Time.deltaTime * speedLerpTime);

        Vector3 move = moveDirection * speed + playerVelocity;
        controller.Move(move * Time.deltaTime);
        lastGameplayControllerVelocity = controller.velocity;

        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;

        Vector3 localVelocity = transform.InverseTransformDirection(
            lastGameplayControllerVelocity
        );

        float animX = localVelocity.x;
        float animZ = localVelocity.z;
        float desiredMax = Mathf.Max(currentSpeed, 0.001f);
        float maxComp = Mathf.Max(Mathf.Abs(animX), Mathf.Abs(animZ));
        if (maxComp > 0.001f)
        {
            float scale = desiredMax / maxComp;
            animX *= scale;
            animZ *= scale;
        }

        float dampTime = 0.08f;
        animator.SetFloat(xVelHash, animX, dampTime, Time.deltaTime);
        animator.SetFloat(zVelHash, animZ, dampTime, Time.deltaTime);
        animator.SetFloat(yVelHash, lastGameplayControllerVelocity.y);
    }
    #endregion

    public void ResolveCameraWallPenetration(Camera playerCamera)
    {
        CacheComponents();

        if (
            !IsOwner
            || playerCamera == null
            || controller == null
            || !controller.enabled
            || !controllerCollisionEnabled
            || sittingCollider
            || standObstructionMask.value == 0
        )
        {
            return;
        }

        // More than one pass lets the controller resolve against two walls in a corner.
        for (int pass = 0; pass < CameraWallResolvePasses; pass++)
        {
            if (!TryGetCameraWallCorrection(playerCamera, out Vector3 correction))
                break;

            correction = Vector3.ClampMagnitude(
                correction,
                CameraWallMaxCorrectionPerPass
            );

            bool keepGrounded = isGrounded || controller.isGrounded;
            Vector3 controllerMove = correction;

            if (keepGrounded)
                controllerMove += Vector3.down * CameraWallGroundStickDistance;

            controller.Move(controllerMove);
        }
    }

    private bool TryGetCameraWallCorrection(Camera playerCamera, out Vector3 correction)
    {
        correction = Vector3.zero;

        Transform cameraTransform = playerCamera.transform;

        // IMPORTANT: cast from the CharacterController's protected center axis, not
        // from the camera. During sprint the camera moves farther forward; if the camera
        // itself has already crossed a wall, a ray starting at the camera can begin
        // inside that wall and miss it completely. The controller axis remains safely
        // behind the wall, so these casts catch both the camera position and near plane.
        Vector3 safeOrigin = transform.TransformPoint(controller.center);
        Bounds controllerBounds = controller.bounds;
        float verticalInset = Mathf.Min(0.05f, controller.radius * 0.25f);
        safeOrigin.y = Mathf.Clamp(
            cameraTransform.position.y,
            controllerBounds.min.y + verticalInset,
            controllerBounds.max.y - verticalInset
        );

        float bestCorrectionSqr = 0f;

        // Protect the physical camera position too. This is what makes the guard remain
        // reliable when the sprint pose pushes the camera farther forward than normal.
        EvaluateCameraProtectionPoint(
            safeOrigin,
            cameraTransform.position,
            ref correction,
            ref bestCorrectionSqr
        );

        float nearDistance = Mathf.Max(0.001f, playerCamera.nearClipPlane);

        for (int i = 0; i < CameraNearPlaneSamples.Length; i++)
        {
            Vector2 sample = CameraNearPlaneSamples[i];
            float viewportX = 0.5f + sample.x * 0.5f;
            float viewportY = 0.5f + sample.y * 0.5f;

            Vector3 desiredPoint = playerCamera.ViewportToWorldPoint(
                new Vector3(viewportX, viewportY, nearDistance)
            );

            EvaluateCameraProtectionPoint(
                safeOrigin,
                desiredPoint,
                ref correction,
                ref bestCorrectionSqr
            );
        }

        return bestCorrectionSqr > 0.0000001f;
    }

    private void EvaluateCameraProtectionPoint(
        Vector3 safeOrigin,
        Vector3 desiredPoint,
        ref Vector3 bestCorrection,
        ref float bestCorrectionSqr
    )
    {
        Vector3 ray = desiredPoint - safeOrigin;
        float desiredDistance = ray.magnitude;

        if (desiredDistance <= 0.0001f)
            return;

        Vector3 direction = ray / desiredDistance;
        float castDistance = desiredDistance + CameraWallPadding * 2f;

        if (!TryGetNearestCameraWallHit(safeOrigin, direction, castDistance, out RaycastHit hit))
            return;

        // This guard is for walls. Floors and ceilings are already handled by the motor
        // and should not shove the player sideways.
        if (Mathf.Abs(hit.normal.y) > CameraWallMaxNormalY)
            return;

        if (Vector3.Dot(direction, hit.normal) >= -0.0001f)
            return;

        Vector3 planarNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        float planarNormalMagnitude = planarNormal.magnitude;

        if (planarNormalMagnitude <= 0.0001f)
            return;

        // Negative signed clearance means the protected point has crossed behind the
        // wall surface. Keep a tiny positive clearance so the near plane never flickers
        // through at high sprint speed or during FOV interpolation.
        float signedClearance = Vector3.Dot(desiredPoint - hit.point, hit.normal);
        float requiredNormalClearance = CameraWallPadding - signedClearance;

        if (requiredNormalClearance <= 0f)
            return;

        Vector3 planarDirection = planarNormal / planarNormalMagnitude;
        float requiredPlanarDistance = requiredNormalClearance / planarNormalMagnitude;
        Vector3 candidate = planarDirection * requiredPlanarDistance;

        if (candidate.sqrMagnitude > bestCorrectionSqr)
        {
            bestCorrection = candidate;
            bestCorrectionSqr = candidate.sqrMagnitude;
        }
    }

    private bool TryGetNearestCameraWallHit(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out RaycastHit nearestHit
    )
    {
        nearestHit = default;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            cameraWallHits,
            distance,
            standObstructionMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = cameraWallHits[i];
            Collider hitCollider = hit.collider;

            if (hitCollider == null)
                continue;

            Transform hitTransform = hitCollider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestHit = hit;
            found = true;
        }

        return found;
    }

    private bool CanStand()
    {
        float radius = controller.radius;

        Vector3 worldPos = transform.position;
        Vector3 c = controller.center;

        float bottomY = worldPos.y + c.y - controller.height * 0.5f + radius + standCheckEpsilon;
        Vector3 capsuleBottom = new Vector3(worldPos.x + c.x, bottomY, worldPos.z + c.z);

        float standSpan = Mathf.Max(standHeight - 2f * radius, 0.01f);
        Vector3 capsuleTop = capsuleBottom + Vector3.up * standSpan;

        bool blocked = Physics.CheckCapsule(
            capsuleBottom,
            capsuleTop,
            radius,
            standObstructionMask,
            QueryTriggerInteraction.Ignore
        );

        Debug.DrawLine(capsuleBottom, capsuleTop, blocked ? Color.red : Color.green, 0.1f);
        return !blocked;
    }

    public void Jump(bool value)
    {
        if (sittingCollider)
        {
            jumpButtonHeld = false;
            return;
        }

        jumpButtonHeld = value;
    }

    public void Sprint(bool value)
    {
        if (sittingCollider)
        {
            sprintButtonHeld = false;
            sprinting = false;
            return;
        }

        sprintButtonHeld = value;

        if (jumpedFromSlide && crouching && !isGrounded)
        {
            crouching = false;
            jumpedFromSlide = false;
            animator.SetBool("Crouching", false);
        }

        if (!crouching && isGrounded && !sliding)
        {
            sprinting = value;
        }
        else if (crouching && CanStand())
        {
            if (isGrounded && !sliding && !crouchButtonHeld)
            {
                sprinting = value;
                crouching = !value;
                animator.SetBool("Crouching", !value);
            }
        }
        else
        {
            if (!sliding)
                sprinting = false;
        }
    }

    public void Land()
    {
        float planarNow = new Vector3(
            lastGameplayControllerVelocity.x,
            0f,
            lastGameplayControllerVelocity.z
        ).magnitude;
        float effectiveSpeed = Mathf.Max(planarNow, lastAirbornePlanarSpeed);
        lastAirbornePlanarSpeed = 0f;

        bool landedFromCoil = coiling || startedCrouchingInAir;

        if (coiling)
        {
            coiling = false;
            animator.SetBool("Coiling", false);
        }
        startedCrouchingInAir = false;

        if (landedFromCoil)
        {
            if (sprintButtonHeld && crouchButtonHeld && effectiveSpeed > 5.85f)
            {
                crouching = false;
                animator.SetBool("Crouching", false);
                Slide();
                reCrouchAfterJump = false;
                return;
            }

            if (crouchButtonHeld)
            {
                crouching = true;
                sprinting = false;
                animator.SetBool("Crouching", true);
                reCrouchAfterJump = false;
                return;
            }

            if (CanStand())
            {
                crouching = false;
                animator.SetBool("Crouching", false);
                sprinting = sprintButtonHeld;
            }
            else
            {
                crouching = true;
                sprinting = false;
                animator.SetBool("Crouching", true);
            }

            reCrouchAfterJump = false;
            return;
        }

        if (reCrouchAfterJump)
        {
            reCrouchAfterJump = false;
            if (crouchButtonHeld)
            {
                crouching = true;
                sprinting = false;
                animator.SetBool("Crouching", true);
                return;
            }
        }

        if (sprintButtonHeld && !crouching)
        {
            sprinting = true;
        }
        else if (sprintButtonHeld && crouching)
        {
            if (CanStand())
            {
                crouching = false;
                animator.SetBool("Crouching", false);
                sprinting = true;
            }
            else
            {
                sprinting = false;
            }
        }
        else
        {
            sprinting = false;
        }
    }

    public void Crouch(bool held)
    {
        if (sittingCollider)
        {
            crouchButtonHeld = false;
            return;
        }

        crouchButtonHeld = held;

        if (sliding)
            return;

        if (isGrounded)
        {
            if (held)
            {
                if (
                    !crouching
                    && sprinting
                    && currentSpeed > 5.85f
                    && Mathf.Abs(inputDirection.x) == 0
                )
                {
                    Slide();
                    return;
                }

                if (!crouching)
                {
                    crouching = true;
                    animator.SetBool("Crouching", true);
                    sprinting = false;
                }
            }
            else
            {
                if (crouching && CanStand())
                {
                    crouching = false;
                    animator.SetBool("Crouching", false);
                }

                sprinting = sprintButtonHeld && !crouching;
            }
            return;
        }

        if (held)
        {
            if (!crouching && !coiling)
            {
                coiling = true;
                animator.SetBool("Coiling", true);
                startedCrouchingInAir = true;
            }
        }
        else
        {
            if (coiling && CanStand())
            {
                coiling = false;
                animator.SetBool("Coiling", false);
                startedCrouchingInAir = false;
            }
        }
    }

    private void Slide()
    {
        if (sittingCollider)
            return;

        sliding = true;
        animator.SetBool("Sliding", true);

        float planarNow = new Vector3(
            lastGameplayControllerVelocity.x,
            0f,
            lastGameplayControllerVelocity.z
        ).magnitude;
        slideSpeed = Mathf.Max(8f, planarNow, lastAirbornePlanarSpeed);

        sprinting = false;
        crouching = false;
        animator.SetBool("Crouching", false);

        slideTimer = slideTimerMax;
        slideElapsed = 0f;
    }
}
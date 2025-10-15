using System;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

public class PlayerMotor : NetworkBehaviour
{
    private CharacterController controller;
    private Animator animator;
    private PlayerLook look;

    private Vector3 playerVelocity;
    private Vector3 moveDirection = Vector3.zero;

    private bool isGrounded;
    private bool wasGrounded = false;
    private bool couldStand = true;
    private bool canStand = true;

    public  bool sprinting = false;
    public  bool crouching = false;
    public  bool sliding = false;

    private bool sprintButtonHeld = false;
    private bool crouchButtonHeld = false;   // NEW: crouch is now a hold
    private bool jumpButtonHeld = false;
    private bool jumpedFromSlide = false;

    // Tracks if crouch was pressed while airborne (coil trigger)
    private bool startedCrouchingInAir = false;

    [Header("Speeds")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float sprintSpeed = 7.5f;
    [SerializeField] private float jumpHeight = 2f;

    [Header("Collider (Stand/Crouch)")]
    [SerializeField] private float crouchHeight = 0.80129076f;
    [SerializeField] private float standHeight = 1.685f;

    [SerializeField] private float crouchCenterY = 0.47814538f;
    [SerializeField] private float standCenterY = 0.92f;

    [Header("Slide")]
    [SerializeField] private float slideSpeed = 8f;
    [SerializeField] private float slideSpeedDecay = 6f;
    [SerializeField] private float minSlideSpeed = 2f;
    [SerializeField] private float slideCancelGrace = 0.33f; // grace window before evaluating cancel
    private float slideElapsed;                               // time since slide started
    private float slideTimer;
    private const float slideTimerMax = 1.3f;

    [Header("Coil (Air-Only)")]
    [SerializeField] private float coilHeight = 1.2f; // collider height while coiling (around center)
    [SerializeField] private bool  coiling = false;   // visible in the inspector
    public  bool Coiling => coiling;

    [Header("Stand Check")]
    [Tooltip("Layers that can block standing up. Exclude Player, include ceilings/walls/level geo.")]
    [SerializeField] private LayerMask standObstructionMask = ~0;
    [SerializeField] private float    standCheckEpsilon = 0.01f;

    [Header("Airborne Behavior")]
    [SerializeField] private float fallLeaveGroundVy = 0.05f; // <= this means we fell (not jumped)

    private float currentHeight;
    private Vector3 currentCenter;
    private float currentSpeed;
    private float currentSpeedVertical;
    private float speedLerpTime = 8f;
    private float targetSpeed = 0f;
    private float smoothSpeed = 7.5f;
    private float fallSpeed = 1.5f;

    public  Vector2 inputDirection;

    private int xVelHash;
    private int zVelHash;
    private int yVelHash;

    private bool useMirror;

    // Track last airborne planar speed to make landing checks robust
    private float lastAirbornePlanarSpeed = 0f;

    public bool IsSprintHeld => sprintButtonHeld;
    public bool IsCrouchHeld => crouchButtonHeld;     // NEW
    public bool IsActuallySprinting =>
        sprinting && isGrounded && !sliding && inputDirection.y > 0.01f && currentSpeed > 0.1f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        look = GetComponent<PlayerLook>();

        crouchCenterY = standCenterY - (standHeight - crouchHeight) / 2f;

        ResetCrouchAndSlide();

        xVelHash = Animator.StringToHash("X_Velocity");
        zVelHash = Animator.StringToHash("Z_Velocity");
        yVelHash = Animator.StringToHash("Y_Velocity");
    }

    void Update()
    {
        if (!IsOwner) return;

        // Gate sprint only while RMB is down; do not change the 'held' flag.
        if (look != null && look.IsRmbHeld && sprinting)
            sprinting = false;

        // Order matters
        UpdateGroundStatus();   // may call Land() or BecameAirborne()
        HandleSliding();        // consume slide state this frame
        HandleJumping();
        UpdateStandStatus();
        UpdateCharacterDimensions();
    }

    private void ResetCrouchAndSlide()
    {
        currentHeight = standHeight;
        controller.height = currentHeight;
        controller.center = new Vector3(controller.center.x, standCenterY, controller.center.z);
        slideTimer = slideTimerMax;
        slideElapsed = 0f;
    }

    private void HandleSliding()
    {
        if (!sliding) return;

        float dt = Time.deltaTime;
        slideElapsed += dt;
        slideTimer   -= dt;

        // slide speed decays but keeps a floor
        slideSpeed = Mathf.Max(slideSpeed - slideSpeedDecay * dt, minSlideSpeed);

        // Only evaluate "bad" conditions after a short grace window
        bool graceOver = slideElapsed >= slideCancelGrace;
        if (slideTimer <= 0f
            || (graceOver && currentSpeed < 2f)
            || (graceOver && currentSpeedVertical > 1f))
        {
            // END SLIDE:
            // 1) If crouch is held, remain/go to crouch (priority).
            // 2) Else if sprint is held and there's headroom, stand+sprint.
            // 3) Else, crouch.
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

            // Reset slide seeds
            slideTimer   = slideTimerMax;
            slideElapsed = 0f;
            slideSpeed   = 8f;
        }
    }

    private void UpdateStandStatus()
    {
        canStand = CanStand();

        // If we're crouching, not sliding, there's headroom, and the crouch key is NOT held,
        // auto-stand and optionally resume sprint if Shift is held.
        if (crouching && !sliding && canStand && !crouchButtonHeld)
        {
            crouching = false;
            animator.SetBool("Crouching", false);
            sprinting = sprintButtonHeld; // stand+run if Shift is down
        }

        couldStand = canStand;
    }

    private void HandleJumping()
    {
        // cannot jump while sliding
        if (jumpButtonHeld && isGrounded && !sliding && CanStand())
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            animator.SetTrigger("Jump");
            animator.SetBool("useMirror", useMirror);

            // (kept for clarity; condition excludes sliding so this won't run)
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
            BecameAirborne(); // uncrouch when walking off ledges while crouched
        }

        wasGrounded = isGrounded;
    }

    private void BecameAirborne()
    {
        // Consider it a "drop/fall" if our vertical velocity is not upward
        float vy = controller.velocity.y;
        bool falling = vy <= fallLeaveGroundVy;

        // If we walked off a ledge while crouching (and not sliding), stand up if there's headroom.
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

        if (coiling && !isGrounded)
        {
            targetHeight = coilHeight;
            targetCenterY = standCenterY; // centered coil
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

        if (Mathf.Abs(controller.height - targetHeight) > 0.001f ||
            Mathf.Abs(controller.center.y - targetCenterY) > 0.001f)
        {
            float dt = Time.deltaTime;
            float newHeight = Mathf.Lerp(controller.height, targetHeight, 20.0f * dt);
            Vector3 desiredCenter = new Vector3(controller.center.x, targetCenterY, controller.center.z);
            Vector3 newCenter = Vector3.Lerp(controller.center, desiredCenter, 20.0f * dt);

            // Feet-lock when grounded
            if (isGrounded)
            {
                float oldBottom = transform.position.y + controller.center.y - controller.height * 0.5f + controller.radius;
                float newBottom = transform.position.y + newCenter.y       - newHeight       * 0.5f + controller.radius;

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

        Vector3 planarVel = new Vector3(controller.velocity.x, 0, controller.velocity.z);
        currentSpeed = planarVel.magnitude;
        currentSpeedVertical = controller.velocity.y;

        if (!isGrounded)
            lastAirbornePlanarSpeed = Mathf.Max(lastAirbornePlanarSpeed * 0.9f, currentSpeed);

        playerVelocity.y += Physics.gravity.y * Time.deltaTime * fallSpeed;

        if (isGrounded && !sliding)       smoothSpeed = 9f;
        else if (sliding)                 smoothSpeed = 0.5f;
        else                               smoothSpeed = 1f;

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

        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;

        Vector3 localVelocity = transform.InverseTransformDirection(controller.velocity);

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
        animator.SetFloat(yVelHash, controller.velocity.y);
    }
    #endregion

    // Foot-anchored stand test (ground doesn't block the check)
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
            capsuleBottom, capsuleTop, radius, standObstructionMask, QueryTriggerInteraction.Ignore);

        Debug.DrawLine(capsuleBottom, capsuleTop, blocked ? Color.red : Color.green, 0.1f);
        return !blocked;
    }

    public void Jump(bool value)
    {
        jumpButtonHeld = value;
    }

    public void Sprint(bool value)
    {
        sprintButtonHeld = value;

        // Sprint no longer cancels slide.

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
            if (!sliding) sprinting = false;
        }
    }

    public void Land()
    {
        // Robust effective speed at touchdown
        float planarNow     = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        float effectiveSpeed = Mathf.Max(planarNow, lastAirbornePlanarSpeed);
        lastAirbornePlanarSpeed = 0f;

        bool landedFromCoil = coiling || startedCrouchingInAir;

        // Coil ends on landing
        if (coiling)
        {
            coiling = false;
            animator.SetBool("Coiling", false);
        }
        startedCrouchingInAir = false;

        if (landedFromCoil)
        {
            // Slide ONLY if sprint + crouch are held and fast enough
            if (sprintButtonHeld && crouchButtonHeld && effectiveSpeed > 5.85f)
            {
                crouching = false;
                animator.SetBool("Crouching", false);
                Slide();
                return;
            }

            // If crouch is held on coil landing, go to crouch (priority)
            if (crouchButtonHeld)
            {
                crouching = true;
                sprinting = false;
                animator.SetBool("Crouching", true);
                return;
            }

            // Otherwise: stand if there’s headroom (and sprint if sprint is held), else crouch
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
            return;
        }

        // Normal landing (unchanged)
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

    // --- CROUCH is now a HOLD ---
    public void Crouch(bool held)
    {
        crouchButtonHeld = held;

        // Ignore crouch input while sliding
        if (sliding) return;

        if (isGrounded)
        {
            if (held)
            {
                // Press-to-slide on ground if sprinting & fast enough
                if (!crouching && sprinting && currentSpeed > 5.85f && Mathf.Abs(inputDirection.x) == 0)
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
                // Auto-stand later when headroom appears (UpdateStandStatus handles this)
                sprinting = sprintButtonHeld && !crouching;
            }
            return;
        }

        // ---------- AIR RULES (COIL HOLD) ----------
        if (held)
        {
            // Start COIL only if we weren't already crouching or coiling
            if (!crouching && !coiling)
            {
                coiling = true;
                animator.SetBool("Coiling", true);
                startedCrouchingInAir = true;
            }
            // If you were crouching in air (shouldn't happen), we still don't start coil.
        }
        else
        {
            // RELEASE in air: try to exit COIL immediately if we can fit the stand capsule.
            if (coiling && CanStand())
            {
                coiling = false;
                animator.SetBool("Coiling", false);
                startedCrouchingInAir = false; // so landing isn't treated as "from coil"
            }
            // If there's no headroom yet, keep coiling; we'll re-check on the next release or on landing.
        }
    }

    private void Slide()
    {
        sliding = true;
        animator.SetBool("Sliding", true);

        // Seed slide momentum from the best available source
        float planarNow = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        slideSpeed = Mathf.Max(8f, planarNow, lastAirbornePlanarSpeed);

        sprinting = false;
        crouching = false;
        animator.SetBool("Crouching", false);

        slideTimer = slideTimerMax;
        slideElapsed = 0f;
    }
}

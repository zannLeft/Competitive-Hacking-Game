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
    public bool sprinting = false;
    public bool crouching = false;
    public bool sliding = false;
    private bool sprintButtonHeld = false;
    private bool jumpButtonHeld = false;
    private bool jumpedFromSlide = false;
    private bool startedCrouchingInAir = false;

    [SerializeField] private float speed = 5f;
    [SerializeField] private float sprintSpeed = 7.5f;
    [SerializeField] private float jumpHeight = 2f;

    [SerializeField] private float crouchHeight = 0.80129076f;
    [SerializeField] private float standHeight = 1.685f;
    [SerializeField] private float slideSpeed = 8f;
    [SerializeField] private float slideSpeedDecay = 6f;
    [SerializeField] private float minSlideSpeed = 2f;

    [SerializeField] private float crouchCenterY = 0.47814538f;
    [SerializeField] private float standCenterY = 0.92f;

    private float currentHeight;
    private Vector3 currentCenter;
    private float currentSpeed;
    private float currentSpeedVertical;
    private float speedLerpTime = 8f;
    private float targetSpeed = 0f;
    private float smoothSpeed = 7.5f;
    private float slideTimer;
    private float fallSpeed = 1.5f;
    private const float slideTimerMax = 1.3f;
    private const float crouchTransitionSpeed = 20.0f;
    public  Vector2 inputDirection;

    private int xVelHash;
    private int zVelHash;

    private bool useMirror;

    // Expose sprint key state so InputManager can check it on RMB release
    public bool IsSprintHeld => sprintButtonHeld;

    // "Actually sprinting" used by camera/FOV
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
    }

    void Update()
    {
        if (!IsOwner) return;

        // Gate sprint only while RMB is down; do not change the 'held' flag.
        if (look != null && look.IsRmbHeld && sprinting)
            sprinting = false;

        HandleSliding();
        HandleJumping();
        UpdateStandStatus();
        UpdateGroundStatus();
        UpdateCharacterDimensions();
    }

    private void ResetCrouchAndSlide()
    {
        currentHeight = standHeight;
        controller.height = currentHeight;
        controller.center = new Vector3(controller.center.x, standCenterY, controller.center.z);
        slideTimer = slideTimerMax;
    }

    private void HandleSliding()
    {
        if (!sliding) return;

        slideTimer -= Time.deltaTime;

        if (currentSpeed < 2f) slideTimer = 0f;
        if (currentSpeedVertical > 1f) slideTimer = 0f;

        slideSpeed = Mathf.Max(slideSpeed - slideSpeedDecay * Time.deltaTime, minSlideSpeed);

        if (slideTimer <= 0f)
        {
            if (sprintButtonHeld && CanStand()) {
                sprinting = true;
                crouching = false;
            } else {
                crouching = true;
                sprinting = false;
            }
            sliding = false;
            slideSpeed = 8f;
            animator.SetBool("Sliding", sliding);
            slideTimer = slideTimerMax;
            animator.SetBool("Crouching", crouching);
        }
    }

    private void UpdateStandStatus()
    {
        canStand = CanStand();
        if (canStand && !couldStand)
        {
            if (crouching && sprintButtonHeld && CanStand()) {
                sprinting = true;
                crouching = false;
                animator.SetBool("Crouching", false);
            }
        }
        couldStand = canStand;
    }

    private void HandleJumping()
    {
        if (jumpButtonHeld && isGrounded && CanStand())
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            animator.SetTrigger("Jump");
            animator.SetBool("useMirror", useMirror);

            if (sliding)
            {
                slideTimer = 0;
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
        wasGrounded = isGrounded;
    }

    private void UpdateCharacterDimensions()
    {
        float targetHeight = crouching || sliding ? crouchHeight : standHeight;
        float targetCenterY = crouching || sliding ? crouchCenterY : standCenterY;

        if (Mathf.Abs(controller.height - targetHeight) > 0.01f)
        {
            currentHeight = Mathf.Lerp(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            controller.height = currentHeight;

            currentCenter = Vector3.Lerp(controller.center, new Vector3(controller.center.x, targetCenterY, controller.center.z), crouchTransitionSpeed * Time.deltaTime);
            controller.center = currentCenter;
        }
    }

    #region Movement
    public void ProcessMove(Vector2 input)
    {
        // Block movement when crouching AND RMB is held (but still allow rotation/gravity)
        bool movementBlocked = (look != null && look.IsRmbHeld && crouching && !sliding);

        // Feed what PlayerLook considers "moving" — when blocked, report no move input
        inputDirection = movementBlocked ? Vector2.zero : input;

        currentSpeed         = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        currentSpeedVertical = controller.velocity.y;

        // Gravity
        playerVelocity.y += Physics.gravity.y * Time.deltaTime * fallSpeed;

        // Movement smoothing mode
        if (isGrounded && !sliding)       smoothSpeed = 9f;
        else if (sliding)                 smoothSpeed = 0.5f;
        else                               smoothSpeed = 1f;

        Quaternion moveSpace = (look != null) ? look.MoveSpaceRotation : transform.rotation;

        // If blocked, target no horizontal direction
        Vector3 targetDirection = movementBlocked
            ? Vector3.zero
            : moveSpace * new Vector3(input.x, 0f, input.y);

        moveDirection = Vector3.Lerp(moveDirection, targetDirection, Time.deltaTime * smoothSpeed);

        if (isGrounded)
        {
            if (sliding)
            {
                targetSpeed   = slideSpeed;
            }
            else if (movementBlocked)
            {
                // Snap quickly to a stop when blocked
                targetSpeed   = 0f;
                speedLerpTime = 20f;
            }
            else if (input.y > 0 && sprinting)
            {
                targetSpeed   = sprintSpeed;
                speedLerpTime = 6f;
            }
            else if (input.x != 0 || input.y != 0)
            {
                targetSpeed   = 2f;
                speedLerpTime = 8f;
            }
            else
            {
                speedLerpTime = 8f;
            }
        }

        speed = Mathf.Lerp(speed, targetSpeed, Time.deltaTime * speedLerpTime);

        // Apply move: if blocked, zero horizontal translation
        Vector3 move = movementBlocked
            ? new Vector3(0f, playerVelocity.y, 0f)
            : moveDirection * speed + playerVelocity;

        controller.Move(move * Time.deltaTime);

        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;

        // Animator velocities — zero them while blocked so feet stay planted
        Vector3 localVelocity = transform.InverseTransformDirection(controller.velocity);
        float animX = movementBlocked ? 0f : localVelocity.x;
        float animZ = movementBlocked ? 0f : localVelocity.z;

        float desiredMax = Mathf.Max(currentSpeed, 0.001f);
        float maxComp    = Mathf.Max(Mathf.Abs(animX), Mathf.Abs(animZ));
        if (!movementBlocked && maxComp > 0.001f)
        {
            float scale = desiredMax / maxComp;
            animX *= scale;
            animZ *= scale;
        }

        float dampTime = 0.08f;
        animator.SetFloat(xVelHash, animX, dampTime, Time.deltaTime);
        animator.SetFloat(zVelHash, animZ, dampTime, Time.deltaTime);
    }

    #endregion

    private bool CanStand()
    {
        Vector3 centerWorld = transform.position + new Vector3(controller.center.x, standCenterY, controller.center.z);
        float halfHeight = standHeight / 2f;
        float radius = controller.radius;

        Vector3 capsuleBottom = centerWorld + Vector3.up * (-halfHeight + radius);
        Vector3 capsuleTop    = centerWorld + Vector3.up * ( halfHeight - radius);

        bool canStandLocal = !Physics.CheckCapsule(capsuleBottom, capsuleTop, radius, LayerMask.GetMask("Default"));
        Debug.DrawLine(capsuleBottom, capsuleTop, canStandLocal ? Color.green : Color.red, 0.1f);
        return canStandLocal;
    }

    public void Jump(bool value)
    {
        jumpButtonHeld = value;
    }

    public void Sprint(bool value)
    {
        sprintButtonHeld = value;

        if (sliding && sprintButtonHeld) {
            slideTimer = 0f;
        }
        if (jumpedFromSlide && crouching && !isGrounded) {
            crouching = false;
            jumpedFromSlide = false;
            animator.SetBool("Crouching", false);
        }

        if (!crouching && isGrounded) {
            sprinting = value;
        }
        else if (crouching && CanStand()) {
            if (isGrounded) {
                sprinting = value;
                crouching = !value;
                animator.SetBool("Crouching", !value);
            } else {
                if (!startedCrouchingInAir) {
                    crouching = !value;
                    animator.SetBool("Crouching", !value);
                }
            }
        } else {
            sprinting = false;
        }
    }


    public void Land()
    {
        if (sprintButtonHeld && !crouching)
        {
            sprinting = true;
        }
        else if (sprintButtonHeld && crouching)
        {
            if (currentSpeed > 5.85f) {
                crouching = false;
                animator.SetBool("Crouching", false);
                Slide();
            } else {
                crouching = false;
                animator.SetBool("Crouching", false);
                sprinting = true;
            }
        }
        else
        {
            sprinting = false;
        }
        startedCrouchingInAir = false;
    }

    public void Crouch()
    {
        if (sliding) {
            slideTimer = 0;
        } else if (sprinting && isGrounded && !crouching && currentSpeed > 5.85f && inputDirection.x == 0) {
            Slide();
        } else {
            if (crouching && CanStand()) {
                crouching = false;
            } else {
                crouching = true;
                if (!isGrounded) {
                    startedCrouchingInAir = true;
                }
            }
            sprinting = !crouching && sprintButtonHeld;
            animator.SetBool("Crouching", crouching);
        }
    }

    private void Slide()
    {
        sliding = true;
        animator.SetBool("Sliding", sliding);
        sprinting = false;
    }
}

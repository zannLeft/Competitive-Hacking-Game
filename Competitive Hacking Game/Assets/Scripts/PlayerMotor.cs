using System;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

public class PlayerMotor : NetworkBehaviour
{
    private CharacterController controller;
    private Animator animator;

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
    [SerializeField] private float crouchHeight = 0.95f;
    [SerializeField] private float standHeight = 2f;
    [SerializeField] private float slideSpeed = 8f;
    [SerializeField] private float slideSpeedDecay = 6f;  // How fast the sliding speed decreases
    [SerializeField] private float minSlideSpeed = 2f;    // Minimum sliding speed
    [SerializeField] private GameObject playerMesh;
    
    private float crouchCenterY = -0.5f;
    private float standCenterY = 0f;
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
    public Vector2 inputDirection;


    private int xVelHash;
    private int zVelHash;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = playerMesh.GetComponent<Animator>();
        ResetCrouchAndSlide();

        xVelHash = Animator.StringToHash("X_Velocity");
        zVelHash = Animator.StringToHash("Z_Velocity");
    }

    void Update()
    {
        if (!IsOwner) return;

        HandleSliding();
        HandleJumping();
        UpdateStandStatus();
        UpdateGroundStatus();
        UpdateCharacterDimensions();
        //Debug.Log(sprinting + ", " + crouching + ", " + sliding + ", " + currentSpeed);
        //Debug.Log(currentSpeedVertical);
        
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

        // Gradually reduce the sliding speed over time
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

    private void HandleJumping() {
        if (jumpButtonHeld && isGrounded && CanStand())
        {   
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            if (sliding) {
                    slideTimer = 0;
                    jumpedFromSlide = true;
            }
        }
    }

    private void UpdateGroundStatus()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && !wasGrounded)
        {
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
    public void ProcessMove(Vector2 input) {

        inputDirection = input;
        currentSpeed = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        currentSpeedVertical = controller.velocity.y;

        playerVelocity.y += Physics.gravity.y * Time.deltaTime * fallSpeed;
        
        if (isGrounded && !sliding) {
            smoothSpeed = 9f;
        } else if (sliding) {
            smoothSpeed = 0.5f;
        } else {
            smoothSpeed = 1f;
        }

        Vector3 targetDirection = transform.TransformDirection(new Vector3(input.x, 0, input.y));

        moveDirection = Vector3.Lerp(moveDirection, targetDirection, Time.deltaTime * smoothSpeed);
        
        if (isGrounded) {
            if (sliding) {
                targetSpeed = slideSpeed;  // Use the decaying slide speed
            } else if (input.y > 0 && sprinting) {
                targetSpeed = sprintSpeed;
                speedLerpTime = 6f;
            } else if (input.x != 0 || input.y != 0) {
                targetSpeed = 2f;
                speedLerpTime = 8f;
            } else {
                speedLerpTime = 8f;
            }
        }

        speed = Mathf.Lerp(speed, targetSpeed, Time.deltaTime * speedLerpTime);

        Vector3 move = moveDirection * speed + playerVelocity;

        // Debug.Log("Move direction: " + moveDirection);
        // Debug.Log("speed: " + speed);
        // Debug.Log("move: " + move);
        controller.Move(move * Time.deltaTime);

        if (isGrounded) {       
            if (playerVelocity.y < 0) {
                playerVelocity.y = -2f;
            }
        }

        Vector3 localVelocity = transform.InverseTransformDirection(controller.velocity);

        // localVelocity.x is the movement along the local right (strafing)
        // localVelocity.z is the movement along the local forward (walking forward/backward)
        animator.SetFloat(xVelHash, localVelocity.x); // Strafing
        animator.SetFloat(zVelHash, localVelocity.z); // Forward/Backward

    }

    #endregion

    private bool CanStand()
    {
        Vector3 start = transform.position;
        Vector3 end = transform.position + Vector3.up * (standHeight / 2);

        bool canStand = !Physics.CheckCapsule(start, end, controller.radius, LayerMask.GetMask("Default"));

        //Debug.DrawLine(start, end, canStand ? Color.green : Color.red, 0.1f);

    return canStand;
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


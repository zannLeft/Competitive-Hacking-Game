using System;
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
    private bool sprinting = false;
    public bool crouching = false;
    public bool sliding = false;
    private bool sprintButtonHeld = false;

    [SerializeField] private float speed = 5f;
    [SerializeField] private float sprintSpeed = 7.5f;
    [SerializeField] private float jumpHeight = 3f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float standHeight = 2f;
    [SerializeField] private float slideSpeed = 6f;
    [SerializeField] private GameObject playerMesh;
    
    private float crouchCenterY = -0.5f;
    private float standCenterY = 0f;
    private float currentHeight;
    private Vector3 currentCenter;
    private float currentSpeed;
    private float speedLerpTime = 8f;
    private float targetSpeed = 0f;
    private float smoothSpeed = 7.5f;
    private float slideTimer;
    private const float slideTimerMax = 1f;
    private const float crouchTransitionSpeed = 5.0f;
    private Vector2 inputDirection;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = playerMesh.GetComponent<Animator>();
        ResetCrouchAndSlide();
    }

    void Update()
    {
        if (!IsOwner) return;

        HandleSliding();
        UpdateGroundStatus();
        UpdateCharacterDimensions();
        Debug.Log(sprinting + ", " + crouching + ", " + sliding + ", " + currentSpeed);
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
        if (slideTimer <= 0f)
        {
            crouching = !sprintButtonHeld;
            sprinting = sprintButtonHeld;
            sliding = false;
            animator.SetBool("Sliding", sliding);
            slideTimer = slideTimerMax;
            animator.SetBool("Crouching", crouching);
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

        playerVelocity.y += Physics.gravity.y * Time.deltaTime;
        
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
                targetSpeed = slideSpeed;
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
    }

    #endregion

    public void Jump()
    {
        if (isGrounded)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
        }
    }

    public void Sprint(bool value)
    {
        sprintButtonHeld = value;

        if (!crouching && isGrounded)
        {
            sprinting = value;
        }
        else if (crouching && isGrounded)
        {
            sprinting = value;
            crouching = !value;
            animator.SetBool("Crouching", !value);
        }
        else
        {
            sprinting = false;
        }
    }

    public void Land()
    {
        if (sprintButtonHeld && !crouching)
        {
            sprinting = true;
        }
        else if (sprintButtonHeld && currentSpeed > 4.75f && crouching)
        {
            crouching = false;
            animator.SetBool("Crouching", false);
            Slide();
        }
        else
        {
            sprinting = false;
        }
    }

    public void Crouch()
    {
        if (sprinting && isGrounded && !crouching && currentSpeed > 4.75f && inputDirection.x == 0)
        {
            Slide();
        }
        else
        {
            crouching = !crouching;
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
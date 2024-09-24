using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerMotor : NetworkBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private Vector3 moveDirection = Vector3.zero;

    private bool isGrounded;

    [SerializeField] private float speed = 5;
    private float speedLerpTime = 5f;
    [SerializeField] private float sprintSpeed = 5;
    private bool sprinting = false;
    float smoothSpeed = 7.5f;
    float targetSpeed = 0f;



    [SerializeField] private float jumpHeight = 3;


    [SerializeField] private float crouchHeight = 0.1f;
    [SerializeField] private float standHeight = 2f;
    private bool crouching = false;
    private float crouchTimer = 1;
    private bool lerpCrouch = false;
    

    private bool sprintButtonHeld = false;
    private bool wasGrounded = false;


    void Start() {
        controller = GetComponent<CharacterController>();
    }

    void Update() {
        isGrounded = controller.isGrounded;
        if (isGrounded && !wasGrounded)
        {
            Land();
        }

        wasGrounded = isGrounded;

        if (lerpCrouch) {
            crouchTimer += Time.deltaTime;
            float p = crouchTimer / 1;
            controller.height = Mathf.Lerp(controller.height, crouching ? crouchHeight : standHeight, p * p);

            if (p > 1) {
                lerpCrouch = false;
                crouchTimer = 0f;
            }
        }
    }
    
    #region Movement
    public void ProcessMove(Vector2 input) {
        playerVelocity.y += Physics.gravity.y * Time.deltaTime;
        
        if (isGrounded) {
            smoothSpeed = 7.5f;
        } else {
            smoothSpeed = 1f;
        }

        Vector3 targetDirection = transform.TransformDirection(new Vector3(input.x, 0, input.y));

        moveDirection = Vector3.Lerp(moveDirection, targetDirection, Time.deltaTime * smoothSpeed);
        
        if (isGrounded) {
            if (input.y > 0 && sprinting) {
                targetSpeed = sprintSpeed;
                speedLerpTime = 12;
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

    public void Jump() {
        if (isGrounded) {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
        }
    }

    public void Sprint(bool value) {
        sprintButtonHeld = value;

        if (isGrounded) {
            sprinting = value;
        }
    }

    public void Land() {
    if (!sprintButtonHeld) {
        sprinting = false;
    } else {
        sprinting = true;
    }
}

    public void Crouch()
    {
        crouching = !crouching;
        crouchTimer = 0;
        lerpCrouch = true;
    }
}

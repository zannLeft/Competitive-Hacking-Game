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



    private float crouchHeight = 1f;
    private float standHeight = 2f;
    public float crouchCenterY = -0.5f;
    public float standCenterY = 0f;
    
    public bool crouching = false;
    private float currentHeight;
    private Vector3 currentCenter;
    public float crouchTransitionSpeed = 5.0f; // Speed of the transition

    

    private bool sprintButtonHeld = false;
    private bool wasGrounded = false;



    [SerializeField] private GameObject playerMesh;
    private Animator animator;


    void Start() {

        controller = GetComponent<CharacterController>();

        animator = playerMesh.GetComponent<Animator>();

        currentHeight = standHeight;
        controller.height = currentHeight;
        controller.center = new Vector3(controller.center.x, standCenterY, controller.center.z);
    }

    void Update() {

        if (!IsOwner) {
            return;
        }




        isGrounded = controller.isGrounded;
        if (isGrounded && !wasGrounded)
        {
            Land();
        }

        wasGrounded = isGrounded;

        float targetCenterY = crouching ? crouchCenterY : standCenterY;
        float targetHeight = crouching ? crouchHeight : standHeight;
         if (Mathf.Abs(controller.height - targetHeight) > 0.01f || Mathf.Abs(controller.center.y - targetCenterY) > 0.01f)
        {
            currentHeight = Mathf.Lerp(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            controller.height = currentHeight;

            Vector3 targetCenter = new Vector3(controller.center.x, targetCenterY, controller.center.z);
            currentCenter = Vector3.Lerp(controller.center, targetCenter, crouchTransitionSpeed * Time.deltaTime);
            controller.center = currentCenter;
        }
    }
    
    #region Movement
    public void ProcessMove(Vector2 input) {

        playerVelocity.y += Physics.gravity.y * Time.deltaTime;
        
        if (isGrounded) {
            smoothSpeed = 9f;
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

    public void Crouch() {
        crouching = !crouching;
        animator.SetBool("Crouching", crouching);
        }
}


using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SocialPlatforms;

public class PlayerMotor : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private Vector3 moveDirection = Vector3.zero;
    private bool isGrounded;
    public float speed = 5;
    public float sprintSpeed = 5;
    public float gravity = -9.8f;
    public float jumpHeight = 3;
    private bool sprinting = false;
    private bool crouching = false;
    private float crouchTimer = 1;
    private bool lerpCrouch = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        isGrounded = controller.isGrounded;

        if (lerpCrouch)
        {
            crouchTimer += Time.deltaTime;
            float p = crouchTimer / 1 ;
            p *= p;
            if (crouching)
                controller.height = Mathf.Lerp(controller.height, 0.1f, p);
            else
                controller.height = Mathf.Lerp(controller.height, 2, p);
            
            if (p > 1)
            {
                lerpCrouch = false;
                crouchTimer = 0f;
            }
        }
    }

    // receive the vector2 inputs from InputManager.cs and apply them to the character controller.
    public void ProcessMove(Vector2 input)
    {
        if (isGrounded)
        {
            moveDirection.x = input.x;
            moveDirection.z = input.y;

            moveDirection = transform.TransformDirection(moveDirection);
        }
        
        controller.Move(moveDirection * speed * Time.deltaTime);
        

        playerVelocity.y += gravity * Time.deltaTime;
        if (isGrounded && playerVelocity.y < 0)
        {
            playerVelocity.y = -2f;
        }
        controller.Move(playerVelocity * Time.deltaTime);

        if (isGrounded)
        {
            if (sprinting)
            {
                speed = sprintSpeed;
            }
            else
            {
                speed = 2;
            }
        }
    } 

    public void Jump()
    {
        if (isGrounded)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -3.0f * gravity);
        }
    }

    public void Sprint()
    {
        sprinting = !sprinting;
    }

    public void Crouch()
    {
        crouching = !crouching;
        crouchTimer = 0;
        lerpCrouch = true;
    }
}

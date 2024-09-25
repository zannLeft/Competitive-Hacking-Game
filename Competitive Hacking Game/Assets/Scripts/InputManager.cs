using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : NetworkBehaviour
{
    private PlayerInput playerInput;
    private PlayerInput.OnFootActions onFoot;
    private PlayerMotor motor;
    private PlayerLook look;

    void Awake()
    {
        // Initialize input system and references
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;

        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
    }

    // This method is called after the object has been spawned and network ownership is established
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Set up input actions for the local player
            onFoot.Jump.performed += ctx => motor.Jump();
            onFoot.Sprint.started += ctx => motor.Sprint(true);
            onFoot.Sprint.canceled += ctx => motor.Sprint(false);
            onFoot.Crouch.performed += ctx => motor.Crouch();

            // Enable input for the local player
            onFoot.Enable();
        }
    }

    void Update()
    {
        // Only allow the owner of the player object to control movement
        if (!IsOwner) return;

        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
    }

    void LateUpdate()
    {
        // Only allow the owner of the player object to control camera look
        if (!IsOwner) return;

        look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
    }

    private void OnDisable()
    {
        // Disable input when the object is destroyed or disabled, preventing memory leaks
        if (IsOwner)
        {
            onFoot.Jump.performed -= ctx => motor.Jump();
            onFoot.Sprint.started -= ctx => motor.Sprint(true);
            onFoot.Sprint.canceled -= ctx => motor.Sprint(false);
            onFoot.Crouch.performed -= ctx => motor.Crouch();

            onFoot.Disable();
        }
    }
}

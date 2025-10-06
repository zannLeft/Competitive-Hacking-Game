using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : NetworkBehaviour
{
    private PlayerInput playerInput;
    private PlayerInput.OnFootActions onFoot;
    private PlayerInput.UIActions ui; // <-- added

    //private PlayerInput.HandItemsActions handItems;
    private PlayerMotor motor;
    private PlayerLook look;
    //private HandItems items;
    private PlayerPhone phone;
    private PhoneTargetHandler phoneTarget;

    void Awake()
    {
        // Initialize input system and references
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        ui = playerInput.UI; // <-- added

        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
        phone = GetComponent<PlayerPhone>();
        phoneTarget = GetComponent<PhoneTargetHandler>();
        //items = GetComponent<HandItems>();
    }

    // This method is called after the object has been spawned and network ownership is established
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Set up input actions for the local player
            onFoot.Jump.started += OnJumpStarted;
            onFoot.Jump.canceled += OnJumpCanceled;
            onFoot.Sprint.started += OnSprintStarted;
            onFoot.Sprint.canceled += OnSprintCanceled;
            onFoot.Crouch.performed += OnCrouchPerformed;

            //handItems.PhoneScreen.performed += ctx => items.Equip(0);

            // Enable gameplay + UI maps (UI kept on so Pause works while gameplay is disabled)
            onFoot.Enable();
            //handItems.Enable();

            ui.Start.performed += OnStartPerformed;
            ui.Pause.performed += OnPausePerformed; // <-- added
            ui.Enable();                             // <-- added

            onFoot.Phone.started  += OnPhoneHoldStarted;
            onFoot.Phone.canceled += OnPhoneHoldCanceled;

        }
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        PauseMenuUI.Instance?.Toggle();
    }

    private void OnJumpStarted(InputAction.CallbackContext ctx) => motor.Jump(true);
    private void OnJumpCanceled(InputAction.CallbackContext ctx) => motor.Jump(false);
    private void OnSprintStarted(InputAction.CallbackContext ctx) => motor.Sprint(true);
    private void OnSprintCanceled(InputAction.CallbackContext ctx) => motor.Sprint(false);
    private void OnCrouchPerformed(InputAction.CallbackContext ctx) => motor.Crouch();

    void Update()
    {
        if (!IsOwner) return;

        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());

        // Moved from LateUpdate → Update (fixes the 1-frame lag)
        look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
    }

    private void OnDisable()
    {
        // Disable input when the object is destroyed or disabled, preventing memory leaks
        if (IsOwner)
        {
            // Properly unsubscribe (fixed the accidental += you had on Jump)
            onFoot.Jump.started -= OnJumpStarted;
            onFoot.Jump.canceled -= OnJumpCanceled;
            onFoot.Sprint.started -= OnSprintStarted;
            onFoot.Sprint.canceled -= OnSprintCanceled;
            onFoot.Crouch.performed -= OnCrouchPerformed;

            ui.Pause.performed -= OnPausePerformed;
            ui.Start.performed -= OnStartPerformed;

            onFoot.Disable();
            ui.Disable();
            //handItems.Disable();

            onFoot.Phone.started -= OnPhoneHoldStarted;
            onFoot.Phone.canceled -= OnPhoneHoldCanceled;

        }
    }

    // Called by PauseMenuUI to toggle gameplay input
    public void SetGameplayEnabled(bool enabled)
    {
        if (!IsOwner) return;
        if (enabled) onFoot.Enable(); else onFoot.Disable();
    }

    // In InputManager
    private void OnStartPerformed(InputAction.CallbackContext ctx)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            LobbyManager.Instance.StartGameAsHost(); // host-only
    }
    private void OnPhoneHoldStarted(InputAction.CallbackContext ctx)
    {
        phone?.SetHolding(true);
        look?.SetPhoneAim(true);   // NEW: snap + rotate with camera while held
    }
    private void OnPhoneHoldCanceled(InputAction.CallbackContext ctx)
    {
        phone?.SetHolding(false);
        look?.SetPhoneAim(false);  // NEW: restore normal shoulder/look behavior
    }
}

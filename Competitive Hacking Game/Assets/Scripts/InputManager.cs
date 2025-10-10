using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : NetworkBehaviour
{
    private PlayerInput playerInput;
    private PlayerInput.OnFootActions onFoot;
    private PlayerInput.UIActions ui;

    private PlayerMotor motor;
    private PlayerLook look;
    private PlayerPhone phone;
    private PhoneTargetHandler phoneTarget;

    private bool _rmbHeld;

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        ui = playerInput.UI;

        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
        phone = GetComponent<PlayerPhone>();
        phoneTarget = GetComponent<PhoneTargetHandler>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            onFoot.Jump.started += OnJumpStarted;
            onFoot.Jump.canceled += OnJumpCanceled;
            onFoot.Sprint.started += OnSprintStarted;
            onFoot.Sprint.canceled += OnSprintCanceled;
            onFoot.Crouch.performed += OnCrouchPerformed;

            onFoot.Enable();

            ui.Start.performed += OnStartPerformed;
            ui.Pause.performed += OnPausePerformed;
            ui.Enable();

            onFoot.Phone.started  += OnPhoneHoldStarted;
            onFoot.Phone.canceled += OnPhoneHoldCanceled;

            onFoot.Flashlight.performed += OnFlashlightPerformed;
        }
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        PauseMenuUI.Instance?.Toggle();
    }

    private void OnJumpStarted (InputAction.CallbackContext ctx) => motor.Jump(true);
    private void OnJumpCanceled(InputAction.CallbackContext ctx) => motor.Jump(false);
    private void OnSprintStarted(InputAction.CallbackContext ctx) => motor.Sprint(true);
    private void OnSprintCanceled(InputAction.CallbackContext ctx)=> motor.Sprint(false);
    private void OnCrouchPerformed(InputAction.CallbackContext ctx)=> motor.Crouch();

    void Update()
    {
        if (!IsOwner) return;
        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
        look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
    }

    private void OnDisable()
    {
        if (IsOwner)
        {
            onFoot.Jump.started -= OnJumpStarted;
            onFoot.Jump.canceled -= OnJumpCanceled;
            onFoot.Sprint.started -= OnSprintStarted;
            onFoot.Sprint.canceled -= OnSprintCanceled;
            onFoot.Crouch.performed -= OnCrouchPerformed;

            ui.Pause.performed -= OnPausePerformed;
            ui.Start.performed -= OnStartPerformed;

            onFoot.Disable();
            ui.Disable();

            onFoot.Phone.started  -= OnPhoneHoldStarted;
            onFoot.Phone.canceled -= OnPhoneHoldCanceled;

            onFoot.Flashlight.performed -= OnFlashlightPerformed;
        }
    }

    public void SetGameplayEnabled(bool enabled)
    {
        if (!IsOwner) return;
        if (enabled) onFoot.Enable(); else onFoot.Disable();
    }

    private void OnStartPerformed(InputAction.CallbackContext ctx)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            LobbyManager.Instance.StartGameAsHost();
    }

    private void OnPhoneHoldStarted(InputAction.CallbackContext ctx)
    {
        _rmbHeld = true;
        phone?.SetHolding(true);
        look?.SetAimHeld(true);   // RMB-only flag
        look?.SetPhoneAim(true);  // aim-style camera while RMB is down
    }

    private void OnPhoneHoldCanceled(InputAction.CallbackContext ctx)
    {
        _rmbHeld = false;

        phone?.SetHolding(false);

        bool aiming = phone != null && phone.IsFlashlightOn;
        look?.SetPhoneAim(aiming);
        look?.SetAimHeld(false);

        // If Shift is still held, resume sprint immediately
        if (motor != null && motor.IsSprintHeld)
            motor.Sprint(true);
    }


    private void OnFlashlightPerformed(InputAction.CallbackContext ctx)
    {
        if (phone == null) return;

        phone.ToggleFlashlight();

        // Aim-style camera if either RMB or flashlight is active
        bool aiming = _rmbHeld || phone.IsFlashlightOn;
        look?.SetPhoneAim(aiming);

        // NOTE: No sprint cancel here — flashlight alone should NOT block sprint.
    }
}

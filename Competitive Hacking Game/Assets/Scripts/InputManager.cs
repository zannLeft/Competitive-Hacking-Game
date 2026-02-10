// InputManager.cs  (RMB phone only, no flashlight input here)
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

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        ui = playerInput.UI;

        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
        phone = GetComponent<PlayerPhone>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        onFoot.Jump.started += OnJumpStarted;
        onFoot.Jump.canceled += OnJumpCanceled;

        onFoot.Sprint.started += OnSprintStarted;
        onFoot.Sprint.canceled += OnSprintCanceled;

        onFoot.Crouch.started += OnCrouchStarted;
        onFoot.Crouch.canceled += OnCrouchCanceled;

        onFoot.Phone.started += OnPhoneHoldStarted;
        onFoot.Phone.canceled += OnPhoneHoldCanceled;

        onFoot.Enable();

        ui.Start.performed += OnStartPerformed;
        ui.Pause.performed += OnPausePerformed;
        ui.Enable();
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        PauseMenuUI.Instance?.Toggle();
    }

    private void OnJumpStarted(InputAction.CallbackContext ctx) => motor.Jump(true);

    private void OnJumpCanceled(InputAction.CallbackContext ctx) => motor.Jump(false);

    private void OnSprintStarted(InputAction.CallbackContext ctx) => motor.Sprint(true);

    private void OnSprintCanceled(InputAction.CallbackContext ctx) => motor.Sprint(false);

    private void OnCrouchStarted(InputAction.CallbackContext ctx) => motor.Crouch(true);

    private void OnCrouchCanceled(InputAction.CallbackContext ctx) => motor.Crouch(false);

    void Update()
    {
        if (!IsOwner)
            return;

        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
        look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
    }

    private void OnDisable()
    {
        if (!IsOwner)
            return;

        onFoot.Jump.started -= OnJumpStarted;
        onFoot.Jump.canceled -= OnJumpCanceled;

        onFoot.Sprint.started -= OnSprintStarted;
        onFoot.Sprint.canceled -= OnSprintCanceled;

        onFoot.Crouch.started -= OnCrouchStarted;
        onFoot.Crouch.canceled -= OnCrouchCanceled;

        onFoot.Phone.started -= OnPhoneHoldStarted;
        onFoot.Phone.canceled -= OnPhoneHoldCanceled;

        ui.Pause.performed -= OnPausePerformed;
        ui.Start.performed -= OnStartPerformed;

        onFoot.Disable();
        ui.Disable();
    }

    public void SetGameplayEnabled(bool enabled)
    {
        if (!IsOwner)
            return;

        if (enabled)
            onFoot.Enable();
        else
            onFoot.Disable();
    }

    private void OnStartPerformed(InputAction.CallbackContext ctx)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            LobbyManager.Instance.StartGameAsHost();
    }

    private void OnPhoneHoldStarted(InputAction.CallbackContext ctx)
    {
        phone?.SetHolding(true);
        look?.SetAimHeld(true);
        look?.SetPhoneAim(true); // this triggers pitch lock + anti-jank catch-up
    }

    private void OnPhoneHoldCanceled(InputAction.CallbackContext ctx)
    {
        phone?.SetHolding(false);

        look?.SetPhoneAim(false);
        look?.SetAimHeld(false);

        if (motor != null && motor.IsSprintHeld)
            motor.Sprint(true);
    }
}

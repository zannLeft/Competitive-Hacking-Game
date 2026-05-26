// InputManager.cs  (RMB phone only, plus SitDown action)
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
    private PlayerSitAction sit;

    private bool _wasMovementBlocked;

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        ui = playerInput.UI;

        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
        phone = GetComponent<PlayerPhone>();
        sit = GetComponent<PlayerSitAction>();
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

        onFoot.SitDown.performed += OnSitDownPerformed;

        onFoot.Enable();

        ui.Start.performed += OnStartPerformed;
        ui.Pause.performed += OnPausePerformed;
        ui.Enable();
    }

    private bool MovementBlocked()
    {
        return sit != null && sit.BlocksGameplayMovement;
    }

    private void ClearMovementInputs()
    {
        motor?.Jump(false);
        motor?.Sprint(false);
        motor?.Crouch(false);

        phone?.SetHolding(false);
        look?.SetPhoneAim(false);
        look?.SetAimHeld(false);
    }

    private void ReapplyHeldInputsAfterUnblock()
    {
        if (motor == null)
            return;

        // Important:
        // Sprint.started will NOT fire if the player was already holding Shift
        // while movement was blocked, so we manually restore it here.
        if (onFoot.Sprint.IsPressed())
            motor.Sprint(true);
        else
            motor.Sprint(false);

        // Do NOT reapply crouch/jump automatically.
        // That would make the player crouch/jump immediately after standing up,
        // which usually feels bad.
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        PauseMenuUI.Instance?.Toggle();
    }

    private void OnJumpStarted(InputAction.CallbackContext ctx)
    {
        if (MovementBlocked())
            return;

        motor.Jump(true);
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        motor.Jump(false);
    }

    private void OnSprintStarted(InputAction.CallbackContext ctx)
    {
        if (MovementBlocked())
            return;

        motor.Sprint(true);
    }

    private void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        motor.Sprint(false);
    }

    private void OnCrouchStarted(InputAction.CallbackContext ctx)
    {
        if (MovementBlocked())
            return;

        motor.Crouch(true);
    }

    private void OnCrouchCanceled(InputAction.CallbackContext ctx)
    {
        motor.Crouch(false);
    }

    private void OnSitDownPerformed(InputAction.CallbackContext ctx)
    {
        sit?.TriggerSitDown();
    }

    void Update()
    {
        if (!IsOwner)
            return;

        bool blocked = MovementBlocked();

        if (blocked && !_wasMovementBlocked)
        {
            ClearMovementInputs();
        }
        else if (!blocked && _wasMovementBlocked)
        {
            ReapplyHeldInputsAfterUnblock();
        }

        _wasMovementBlocked = blocked;

        if (!blocked)
        {
            motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
        }

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

        onFoot.SitDown.performed -= OnSitDownPerformed;

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
        if (MovementBlocked())
            return;

        phone?.SetHolding(true);
        look?.SetAimHeld(true);
        look?.SetPhoneAim(true);
    }

    private void OnPhoneHoldCanceled(InputAction.CallbackContext ctx)
    {
        phone?.SetHolding(false);

        look?.SetPhoneAim(false);
        look?.SetAimHeld(false);

        if (!MovementBlocked() && motor != null && motor.IsSprintHeld)
            motor.Sprint(true);
    }
}
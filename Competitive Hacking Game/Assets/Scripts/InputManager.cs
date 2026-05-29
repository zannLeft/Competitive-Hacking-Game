// InputManager.cs
// RMB phone, SitDown action, Laptop Hack action, bad-guy restrictions, death restrictions, bad-guy attack.
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
    private PlayerLaptopHacker laptopHacker;
    private PlayerSetup playerSetup;
    private PlayerDeathState deathState;
    private BadGuyAttack badGuyAttack;

    private bool _wasMovementBlocked;
    private bool _wasBadGuy;
    private bool _wasDead;

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        ui = playerInput.UI;

        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
        phone = GetComponent<PlayerPhone>();
        sit = GetComponent<PlayerSitAction>();
        laptopHacker = GetComponent<PlayerLaptopHacker>();
        playerSetup = GetComponent<PlayerSetup>();
        deathState = GetComponent<PlayerDeathState>();
        badGuyAttack = GetComponent<BadGuyAttack>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (deathState == null)
            deathState = GetComponent<PlayerDeathState>();

        if (badGuyAttack == null)
            badGuyAttack = GetComponent<BadGuyAttack>();

        onFoot.Jump.started += OnJumpStarted;
        onFoot.Jump.canceled += OnJumpCanceled;

        onFoot.Sprint.started += OnSprintStarted;
        onFoot.Sprint.canceled += OnSprintCanceled;

        onFoot.Crouch.started += OnCrouchStarted;
        onFoot.Crouch.canceled += OnCrouchCanceled;

        onFoot.Phone.started += OnPhoneHoldStarted;
        onFoot.Phone.canceled += OnPhoneHoldCanceled;

        onFoot.SitDown.performed += OnSitDownPerformed;

        onFoot.Hack.started += OnHackStarted;
        onFoot.Hack.canceled += OnHackCanceled;

        onFoot.Attack.started += OnAttackStarted;

        onFoot.Enable();

        ui.Start.performed += OnStartPerformed;
        ui.Pause.performed += OnPausePerformed;
        ui.Enable();

        _wasBadGuy = IsBadGuy();
        _wasDead = IsDead();
    }

    private bool IsBadGuy()
    {
        return playerSetup != null && playerSetup.IsBadGuy.Value;
    }

    private bool IsDead()
    {
        return deathState != null && deathState.IsDead;
    }

    private bool MovementBlocked()
    {
        return sit != null && sit.BlocksGameplayMovement;
    }

    private bool CanMove()
    {
        return !IsDead() && !MovementBlocked();
    }

    private bool CanUseSurvivorTools()
    {
        return !IsDead() && !IsBadGuy();
    }

    private bool CanUsePhone()
    {
        return CanUseSurvivorTools() && !MovementBlocked();
    }

    private bool CanUseLaptop()
    {
        return CanUseSurvivorTools();
    }

    private bool CanHack()
    {
        return CanUseSurvivorTools();
    }

    private bool CanBadGuyAttack()
    {
        return !IsDead() && IsBadGuy() && !MovementBlocked();
    }

    private void ClearMovementInputs()
    {
        motor?.Jump(false);
        motor?.Sprint(false);
        motor?.Crouch(false);

        ClearSurvivorToolInputs();
    }

    private void ClearSurvivorToolInputs()
    {
        phone?.SetHolding(false);

        look?.SetPhoneAim(false);
        look?.SetAimHeld(false);

        laptopHacker?.SetHackHeld(false);
    }

    private void ClearAllActionInputs()
    {
        ClearMovementInputs();
        ClearSurvivorToolInputs();
    }

    private void ReapplyHeldInputsAfterUnblock()
    {
        if (motor == null)
            return;

        if (IsDead())
        {
            motor.Sprint(false);
            return;
        }

        if (onFoot.Sprint.IsPressed())
            motor.Sprint(true);
        else
            motor.Sprint(false);
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        PauseMenuUI.Instance?.Toggle();
    }

    private void OnJumpStarted(InputAction.CallbackContext ctx)
    {
        if (!CanMove())
            return;

        motor?.Jump(true);
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        motor?.Jump(false);
    }

    private void OnSprintStarted(InputAction.CallbackContext ctx)
    {
        if (!CanMove())
            return;

        motor?.Sprint(true);
    }

    private void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        motor?.Sprint(false);
    }

    private void OnCrouchStarted(InputAction.CallbackContext ctx)
    {
        if (!CanMove())
            return;

        motor?.Crouch(true);
    }

    private void OnCrouchCanceled(InputAction.CallbackContext ctx)
    {
        motor?.Crouch(false);
    }

    private void OnSitDownPerformed(InputAction.CallbackContext ctx)
    {
        if (!CanUseLaptop())
            return;

        sit?.TriggerSitDown();
    }

    private void OnHackStarted(InputAction.CallbackContext ctx)
    {
        if (!CanHack())
            return;

        laptopHacker?.SetHackHeld(true);
    }

    private void OnHackCanceled(InputAction.CallbackContext ctx)
    {
        laptopHacker?.SetHackHeld(false);
    }

    private void OnAttackStarted(InputAction.CallbackContext ctx)
    {
        if (!CanBadGuyAttack())
            return;

        badGuyAttack?.TryAttack();
    }

    void Update()
    {
        if (!IsOwner)
            return;

        bool isBadGuyNow = IsBadGuy();
        bool isDeadNow = IsDead();

        if (isBadGuyNow && !_wasBadGuy)
            ClearSurvivorToolInputs();

        if (isDeadNow && !_wasDead)
            ClearAllActionInputs();

        _wasBadGuy = isBadGuyNow;
        _wasDead = isDeadNow;

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

        if (CanMove())
            motor?.ProcessMove(onFoot.Movement.ReadValue<Vector2>());

        // Let dead players still look around for now.
        look?.ProcessLook(onFoot.Look.ReadValue<Vector2>());
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

        onFoot.Hack.started -= OnHackStarted;
        onFoot.Hack.canceled -= OnHackCanceled;

        onFoot.Attack.started -= OnAttackStarted;

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
        if (!CanUsePhone())
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

        if (CanMove() && motor != null && motor.IsSprintHeld)
            motor.Sprint(true);
    }
}
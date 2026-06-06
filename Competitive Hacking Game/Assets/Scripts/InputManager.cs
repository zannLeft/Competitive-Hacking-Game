// InputManager.cs  (RMB phone only, SitDown action, Laptop Hack action)
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : NetworkBehaviour
{
    private PlayerInput playerInput;
    private PlayerInput.OnFootActions onFoot;
    private PlayerInput.UIActions ui;
    private PlayerInput.SpectatorActions spectator;
    private PlayerInput.DownedActions downed;

    private PlayerMotor motor;
    private PlayerLook look;
    private PlayerPhone phone;
    private PlayerSitAction sit;
    private PlayerLaptopHacker laptopHacker;
    private PlayerSetup playerSetup;
    private PlayerLifeState lifeState;
    private PlayerBadGuyAttack badGuyAttack;
    private PlayerReviver reviver;

    private bool _wasMovementBlocked;
    private bool _gameplaySuppressed;
    private bool _spectatorInputEnabled;
    private bool _downedInputEnabled;

    public bool GameplaySuppressed => _gameplaySuppressed;
    public bool SpectatorInputEnabled => _spectatorInputEnabled;
    public bool DownedInputEnabled => _downedInputEnabled;

    public event Action SpectatorPreviousTargetPressed;
    public event Action SpectatorNextTargetPressed;

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        ui = playerInput.UI;
        spectator = playerInput.Spectator;
        downed = playerInput.Downed;

        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        CacheReferences();

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

        onFoot.Hack.started += OnHackStarted;
        onFoot.Hack.canceled += OnHackCanceled;

        onFoot.Attack.performed += OnAttackPerformed;

        onFoot.Enable();

        ui.Start.performed += OnStartPerformed;
        ui.Pause.performed += OnPausePerformed;

        spectator.PreviousTarget.performed += OnSpectatorPreviousTargetPerformed;
        spectator.NextTarget.performed += OnSpectatorNextTargetPerformed;

        ui.Enable();

        if (lifeState != null && lifeState.ShouldSuppressGameplayInput)
            SetGameplaySuppressed(true);
    }

    private void CacheReferences()
    {
        if (motor == null)
            motor = GetComponent<PlayerMotor>();

        if (look == null)
            look = GetComponent<PlayerLook>();

        if (phone == null)
            phone = GetComponent<PlayerPhone>();

        if (sit == null)
            sit = GetComponent<PlayerSitAction>();

        if (laptopHacker == null)
            laptopHacker = GetComponent<PlayerLaptopHacker>();

        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (badGuyAttack == null)
            badGuyAttack = GetComponent<PlayerBadGuyAttack>();

        if (reviver == null)
            reviver = GetComponent<PlayerReviver>();
    }

    private bool MovementBlocked()
    {
        return sit != null && sit.BlocksGameplayMovement;
    }

    private bool IsSittingOrTransitioning()
    {
        return sit != null && sit.IsSittingOrTransitioning;
    }

    private bool IsBadGuy()
    {
        return playerSetup != null && playerSetup.IsBadGuy.Value;
    }

    private bool CanMove()
    {
        return lifeState == null || lifeState.CanMove;
    }

    private bool CanUseSurvivorTools()
    {
        if (lifeState != null)
            return lifeState.CanUseSurvivorTools;

        return !IsBadGuy();
    }

    private bool CanUseBadGuyAttack()
    {
        if (lifeState != null)
            return lifeState.CanAttackSurvivors;

        return IsBadGuy();
    }

    private bool SurvivorToolInputBlocked()
    {
        return _gameplaySuppressed || !CanUseSurvivorTools();
    }

    private bool GameplayInputBlocked()
    {
        return _gameplaySuppressed || !CanMove() || MovementBlocked();
    }

    public Vector2 ReadSpectatorLookInput()
    {
        if (!IsOwner)
            return Vector2.zero;

        if (!_spectatorInputEnabled)
            return Vector2.zero;

        return spectator.Look.ReadValue<Vector2>();
    }

    public Vector2 ReadDownedLookInput()
    {
        if (!IsOwner)
            return Vector2.zero;

        if (!_downedInputEnabled)
            return Vector2.zero;

        return downed.Look.ReadValue<Vector2>();
    }

    public void SetSpectatorInputEnabled(bool enabled)
    {
        if (!IsOwner)
            return;

        if (_spectatorInputEnabled == enabled)
            return;

        _spectatorInputEnabled = enabled;

        if (_spectatorInputEnabled)
        {
            ClearMovementInputs();

            _downedInputEnabled = false;
            downed.Disable();

            onFoot.Disable();
            spectator.Enable();
        }
        else
        {
            spectator.Disable();

            if (!_gameplaySuppressed && !_downedInputEnabled && CanMove() && isActiveAndEnabled)
            {
                onFoot.Enable();
                ReapplyHeldInputsAfterUnblock();
            }
        }
    }

    public void SetDownedInputEnabled(bool enabled)
    {
        if (!IsOwner)
            return;

        if (_downedInputEnabled == enabled)
            return;

        _downedInputEnabled = enabled;

        if (_downedInputEnabled)
        {
            ClearMovementInputs();

            _spectatorInputEnabled = false;
            spectator.Disable();

            onFoot.Disable();
            downed.Enable();
        }
        else
        {
            downed.Disable();

            if (!_gameplaySuppressed && !_spectatorInputEnabled && CanMove() && isActiveAndEnabled)
            {
                onFoot.Enable();
                ReapplyHeldInputsAfterUnblock();
            }
        }
    }

    public void SetGameplaySuppressed(bool suppressed)
    {
        if (!IsOwner)
            return;

        if (_gameplaySuppressed == suppressed)
            return;

        _gameplaySuppressed = suppressed;

        if (_gameplaySuppressed)
        {
            ClearMovementInputs();
            onFoot.Disable();
        }
        else
        {
            if (!_spectatorInputEnabled && !_downedInputEnabled)
            {
                onFoot.Enable();
                ReapplyHeldInputsAfterUnblock();
            }
        }
    }

    public void ForceClearGameplaySuppression()
    {
        if (!IsOwner)
            return;

        bool wasSuppressed = _gameplaySuppressed;
        _gameplaySuppressed = false;

        if (!isActiveAndEnabled)
            return;

        if (!_spectatorInputEnabled && !_downedInputEnabled)
        {
            onFoot.Enable();

            if (wasSuppressed)
                ReapplyHeldInputsAfterUnblock();
        }
    }

    private void ClearMovementInputs()
    {
        motor?.Jump(false);
        motor?.Sprint(false);
        motor?.Crouch(false);

        phone?.SetHolding(false);
        look?.SetPhoneAim(false);
        look?.SetAimHeld(false);

        laptopHacker?.SetHackHeld(false);
        reviver?.SetReviveHeld(false);
    }

    private void ReapplyHeldInputsAfterUnblock()
    {
        if (motor == null)
            return;

        if (!CanMove())
        {
            ClearMovementInputs();
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
        if (GameplayInputBlocked())
            return;

        motor.Jump(true);
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        motor.Jump(false);
    }

    private void OnSprintStarted(InputAction.CallbackContext ctx)
    {
        if (GameplayInputBlocked())
            return;

        motor.Sprint(true);
    }

    private void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        motor.Sprint(false);
    }

    private void OnCrouchStarted(InputAction.CallbackContext ctx)
    {
        if (GameplayInputBlocked())
            return;

        motor.Crouch(true);
    }

    private void OnCrouchCanceled(InputAction.CallbackContext ctx)
    {
        motor.Crouch(false);
    }

    private void OnSitDownPerformed(InputAction.CallbackContext ctx)
    {
        if (SurvivorToolInputBlocked())
            return;

        sit?.TriggerSitDown();
    }

    private void OnHackStarted(InputAction.CallbackContext ctx)
    {
        if (SurvivorToolInputBlocked())
            return;

        if (!IsSittingOrTransitioning())
        {
            reviver?.SetReviveHeld(true);
            return;
        }

        laptopHacker?.SetHackHeld(true);
    }

    private void OnHackCanceled(InputAction.CallbackContext ctx)
    {
        reviver?.SetReviveHeld(false);
        laptopHacker?.SetHackHeld(false);
    }

    void Update()
    {
        if (!IsOwner)
            return;

        if (_gameplaySuppressed || !CanMove())
        {
            ClearMovementInputs();
            return;
        }

        if (!CanUseSurvivorTools())
        {
            phone?.SetHolding(false);
            look?.SetPhoneAim(false);
            look?.SetAimHeld(false);
            laptopHacker?.SetHackHeld(false);
            reviver?.SetReviveHeld(false);
        }

        bool blocked = MovementBlocked();

        if (blocked && !_wasMovementBlocked)
            ClearMovementInputs();
        else if (!blocked && _wasMovementBlocked)
            ReapplyHeldInputsAfterUnblock();

        _wasMovementBlocked = blocked;

        if (!blocked)
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

        onFoot.SitDown.performed -= OnSitDownPerformed;

        onFoot.Hack.started -= OnHackStarted;
        onFoot.Hack.canceled -= OnHackCanceled;

        onFoot.Attack.performed -= OnAttackPerformed;

        ui.Pause.performed -= OnPausePerformed;
        ui.Start.performed -= OnStartPerformed;

        spectator.PreviousTarget.performed -= OnSpectatorPreviousTargetPerformed;
        spectator.NextTarget.performed -= OnSpectatorNextTargetPerformed;

        onFoot.Disable();
        spectator.Disable();
        downed.Disable();
        ui.Disable();
    }

    public void SetGameplayEnabled(bool enabled)
    {
        if (!IsOwner)
            return;

        if (!enabled)
        {
            ClearMovementInputs();
            onFoot.Disable();
            return;
        }

        if (_gameplaySuppressed || !CanMove() || _spectatorInputEnabled || _downedInputEnabled)
        {
            ClearMovementInputs();
            onFoot.Disable();
            return;
        }

        onFoot.Enable();
        ReapplyHeldInputsAfterUnblock();
    }

    private void OnSpectatorPreviousTargetPerformed(InputAction.CallbackContext ctx)
    {
        if (!_spectatorInputEnabled)
            return;

        SpectatorPreviousTargetPressed?.Invoke();
    }

    private void OnSpectatorNextTargetPerformed(InputAction.CallbackContext ctx)
    {
        if (!_spectatorInputEnabled)
            return;

        SpectatorNextTargetPressed?.Invoke();
    }

    private void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (_gameplaySuppressed)
            return;

        if (_spectatorInputEnabled || _downedInputEnabled)
            return;

        if (!CanUseBadGuyAttack())
            return;

        badGuyAttack?.TryAttack();
    }

    private void OnStartPerformed(InputAction.CallbackContext ctx)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            LobbyManager.Instance.StartGameAsHost();
    }

    private void OnPhoneHoldStarted(InputAction.CallbackContext ctx)
    {
        if (GameplayInputBlocked() || !CanUseSurvivorTools())
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

        if (!_gameplaySuppressed && CanMove() && !MovementBlocked() && CanUseSurvivorTools() && motor != null && motor.IsSprintHeld)
            motor.Sprint(true);
    }
}
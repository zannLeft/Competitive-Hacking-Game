using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerSitAction
    : NetworkBehaviour,
        IPlayerRoundResettable,
        IPlayerRoundServerResettable
{
    [Header("Refs")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private PlayerMotor motor;

    [Header("Animator Param")]
    [SerializeField]
    private string laptopSittingBoolParam = "LaptopSitting";

    [Header("Animator State Names")]
    [SerializeField]
    private int baseLayerIndex = 0;

    [SerializeField]
    private string sitDownStateName = "SitDown";

    [SerializeField]
    private string sittingStateName = "Sitting";

    [SerializeField]
    private string standUpStateName = "StandUp";

    [Header("Instant Round Reset")]
    [Tooltip("State to force-play when a round starts/ends so we do not play the stand-up animation.")]
    [SerializeField]
    private string instantResetStateName = "Base State";

    [SerializeField]
    private float instantResetNormalizedTime = 0f;

    private int _laptopSittingHash;

    private bool _useSittingCameraPosition;
    private bool _laptopCameraFocus;

    private bool _blocksGameplayMovement;
    private bool _waitingForStandUpToFinish;
    private bool _hasEnteredStandUp;

    private bool _localWantsSitting;

    private NetworkVariable<bool> WantsSitting = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool UseSittingCameraPosition => _useSittingCameraPosition;
    public bool LaptopCameraFocus => _laptopCameraFocus;
    public bool BlocksGameplayMovement => _blocksGameplayMovement;
    public bool WantsSittingValue => _localWantsSitting;

    public bool IsSittingOrTransitioning
    {
        get
        {
            if (animator == null)
                return false;

            return IsCurrentOrNextState(sitDownStateName)
                || IsCurrentOrNextState(sittingStateName)
                || IsCurrentOrNextState(standUpStateName);
        }
    }

    public bool IsFullySitting
    {
        get
        {
            if (animator == null)
                return false;

            return IsCurrentOrNextState(sittingStateName);
        }
    }

    void Reset()
    {
        animator = GetComponent<Animator>();
        motor = GetComponent<PlayerMotor>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (motor == null)
            motor = GetComponent<PlayerMotor>();

        _laptopSittingHash = Animator.StringToHash(laptopSittingBoolParam);

        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;

        _blocksGameplayMovement = false;
        _waitingForStandUpToFinish = false;
        _hasEnteredStandUp = false;

        _localWantsSitting = WantsSitting.Value;

        WantsSitting.OnValueChanged += OnWantsSittingChanged;

        ApplyWantsSitting(WantsSitting.Value);

        motor?.SetSittingCollider(false);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        WantsSitting.OnValueChanged -= OnWantsSittingChanged;

        motor?.SetSittingCollider(false);
    }

    private void Update()
    {
        if (!_blocksGameplayMovement)
            return;

        if (!_waitingForStandUpToFinish)
            return;

        bool inStandUp = IsCurrentOrNextState(standUpStateName);

        if (inStandUp)
            _hasEnteredStandUp = true;

        if (_hasEnteredStandUp && !inStandUp)
            AE_MovementUnlock();
    }

    public void TriggerSitDown()
    {
        if (!IsOwner || animator == null)
            return;

        bool currentlyTryingToSit = _localWantsSitting || IsFullySitting;
        bool targetWantsSitting = !currentlyTryingToSit;

        if (
            targetWantsSitting
            && motor != null
            && (!motor.IsGrounded || motor.sliding || motor.Coiling)
        )
            return;

        if (IsCurrentOrNextState(sitDownStateName) || IsCurrentOrNextState(standUpStateName))
            return;

        ApplyWantsSitting(targetWantsSitting);
        RequestSetWantsSittingServerRpc(targetWantsSitting);
    }

    [ServerRpc]
    private void RequestSetWantsSittingServerRpc(bool wantsSitting)
    {
        WantsSitting.Value = wantsSitting;
    }

    public void ServerResetForRound()
    {
        if (!IsServer)
            return;

        WantsSitting.Value = false;
    }

    public void ServerForceResetSitNetworkState()
    {
        ServerResetForRound();
    }

    private void OnWantsSittingChanged(bool previousValue, bool newValue)
    {
        ApplyWantsSitting(newValue);
    }

    private void ApplyWantsSitting(bool wantsSitting)
    {
        _localWantsSitting = wantsSitting;

        if (animator != null)
            animator.SetBool(_laptopSittingHash, wantsSitting);

        if (wantsSitting)
        {
            _blocksGameplayMovement = true;
            _waitingForStandUpToFinish = false;
            _hasEnteredStandUp = false;
        }
        else
        {
            if (IsSittingOrTransitioning)
            {
                _blocksGameplayMovement = true;
                _waitingForStandUpToFinish = true;
                _hasEnteredStandUp = false;
            }
            else
            {
                _blocksGameplayMovement = false;
                _waitingForStandUpToFinish = false;
                _hasEnteredStandUp = false;
            }
        }
    }

    public void ResetForRound()
    {
        ForceResetLocalForRound();
    }

    public void ForceResetLocalForRound()
    {
        ApplyInstantStandingReset();
    }

    private void ApplyInstantStandingReset()
    {
        _localWantsSitting = false;

        _blocksGameplayMovement = false;
        _waitingForStandUpToFinish = false;
        _hasEnteredStandUp = false;

        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;

        motor?.SetSittingCollider(false);

        if (animator != null)
        {
            animator.SetBool(_laptopSittingHash, false);

            if (!string.IsNullOrWhiteSpace(instantResetStateName))
            {
                animator.Play(
                    instantResetStateName,
                    baseLayerIndex,
                    Mathf.Clamp01(instantResetNormalizedTime)
                );

                animator.Update(0f);
            }
        }
    }

    public void AE_CameraUseSittingPosition()
    {
        _useSittingCameraPosition = true;
        motor?.SetSittingCollider(true);
    }

    public void AE_CameraUseNormalPosition()
    {
        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;
        motor?.SetSittingCollider(false);
    }

    public void AE_LaptopCameraFocusOn()
    {
        _laptopCameraFocus = true;
    }

    public void AE_LaptopCameraFocusOff()
    {
        _laptopCameraFocus = false;
    }

    public void AE_MovementUnlock()
    {
        _blocksGameplayMovement = false;
        _waitingForStandUpToFinish = false;
        _hasEnteredStandUp = false;
        _localWantsSitting = false;

        if (animator != null)
            animator.SetBool(_laptopSittingHash, false);

        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;
        motor?.SetSittingCollider(false);
    }

    private bool IsCurrentOrNextState(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return false;

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(baseLayerIndex);

        if (current.IsName(stateName))
            return true;

        if (animator.IsInTransition(baseLayerIndex))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(baseLayerIndex);

            if (next.IsName(stateName))
                return true;
        }

        return false;
    }
}
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerSitAction : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private PlayerMotor motor;

    [SerializeField]
    private NetworkAnimator networkAnimator;

    [Header("Animator Param")]
    [SerializeField]
    private string sitTriggerParam = "SitDown";

    [Header("Animator State Names")]
    [SerializeField]
    private int baseLayerIndex = 0;

    [SerializeField]
    private string sitDownStateName = "SitDown";

    [SerializeField]
    private string sittingStateName = "Sitting";

    [SerializeField]
    private string standUpStateName = "StandUp";

    private int _sitTrigHash;

    private bool _useSittingCameraPosition;
    private bool _laptopCameraFocus;

    private bool _blocksGameplayMovement;
    private bool _waitingForStandUpToFinish;
    private bool _hasEnteredStandUp;

    public bool UseSittingCameraPosition => _useSittingCameraPosition;
    public bool LaptopCameraFocus => _laptopCameraFocus;
    public bool BlocksGameplayMovement => _blocksGameplayMovement;

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
        networkAnimator = GetComponent<NetworkAnimator>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (motor == null)
            motor = GetComponent<PlayerMotor>();

        if (networkAnimator == null)
            networkAnimator = GetComponent<NetworkAnimator>();

        _sitTrigHash = Animator.StringToHash(sitTriggerParam);

        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;

        _blocksGameplayMovement = false;
        _waitingForStandUpToFinish = false;
        _hasEnteredStandUp = false;
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

        if (motor != null && (motor.sliding || motor.Coiling))
            return;

        if (IsCurrentOrNextState(sitDownStateName) || IsCurrentOrNextState(standUpStateName))
            return;

        bool shouldStandUp = IsFullySitting;

        _blocksGameplayMovement = true;
        _waitingForStandUpToFinish = shouldStandUp;
        _hasEnteredStandUp = false;

        if (networkAnimator != null)
        {
            networkAnimator.SetTrigger(_sitTrigHash);
        }
        else
        {
            animator.SetTrigger(_sitTrigHash);
        }
    }

    // ----------------------------
    // Animation Events
    // ----------------------------

    public void AE_CameraUseSittingPosition()
    {
        _useSittingCameraPosition = true;
    }

    public void AE_CameraUseNormalPosition()
    {
        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;
    }

    public void AE_LaptopCameraFocusOn()
    {
        _laptopCameraFocus = true;
    }

    public void AE_LaptopCameraFocusOff()
    {
        _laptopCameraFocus = false;
    }

    // Put this at the very end of StandUp.
    public void AE_MovementUnlock()
    {
        _blocksGameplayMovement = false;
        _waitingForStandUpToFinish = false;
        _hasEnteredStandUp = false;
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
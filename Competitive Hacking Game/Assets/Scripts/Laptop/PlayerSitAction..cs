using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerSitAction : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private PlayerMotor motor;

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

    public bool UseSittingCameraPosition => _useSittingCameraPosition;
    public bool LaptopCameraFocus => _laptopCameraFocus;

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

        _sitTrigHash = Animator.StringToHash(sitTriggerParam);

        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;
    }

    public void TriggerSitDown()
    {
        if (!IsOwner || animator == null)
            return;

        if (motor != null && (motor.sliding || motor.Coiling))
            return;

        animator.SetTrigger(_sitTrigHash);
    }

    // ----------------------------
    // Animation Events for camera timing
    // ----------------------------

    // Put this later in SitDown clip, when you actually want camera to start lowering.
    public void AE_CameraUseSittingPosition()
    {
        _useSittingCameraPosition = true;
    }

    // Put this early in StandUp clip, when you want camera to start rising.
    public void AE_CameraUseNormalPosition()
    {
        _useSittingCameraPosition = false;
        _laptopCameraFocus = false;
    }

    // Put this when laptop opens.
    public void AE_LaptopCameraFocusOn()
    {
        _laptopCameraFocus = true;
    }

    // Put this when laptop closes.
    public void AE_LaptopCameraFocusOff()
    {
        _laptopCameraFocus = false;
    }

    private bool IsCurrentOrNextState(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
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
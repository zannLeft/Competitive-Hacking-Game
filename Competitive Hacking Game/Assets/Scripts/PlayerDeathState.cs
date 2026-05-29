using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDeathState
    : NetworkBehaviour,
        IPlayerRoundResettable,
        IPlayerRoundServerResettable
{
    [Header("Refs")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private PlayerSitAction sitAction;

    [SerializeField]
    private PlayerLaptopVisual laptopVisual;

    [SerializeField]
    private PlayerLaptopHacker laptopHacker;

    [SerializeField]
    private PlayerPhone phone;

    [SerializeField]
    private PlayerLook look;

    [Header("Animator")]
    [SerializeField]
    private string deadBoolParam = "Dead";

    [Header("Round Reset")]
    [Tooltip("Optional state to force-play when the player is revived/reset. Use your normal standing/locomotion state.")]
    [SerializeField]
    private string instantAliveStateName = "Base State";

    [SerializeField]
    private int baseLayerIndex = 0;

    [SerializeField]
    private float instantAliveNormalizedTime = 0f;

    private int _deadHash;

    private NetworkVariable<bool> IsDeadNet = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsDead => IsDeadNet.Value;

    void Reset()
    {
        animator = GetComponent<Animator>();
        sitAction = GetComponent<PlayerSitAction>();
        laptopVisual = GetComponent<PlayerLaptopVisual>();
        laptopHacker = GetComponent<PlayerLaptopHacker>();
        phone = GetComponent<PlayerPhone>();
        look = GetComponent<PlayerLook>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (laptopVisual == null)
            laptopVisual = GetComponent<PlayerLaptopVisual>();

        if (laptopHacker == null)
            laptopHacker = GetComponent<PlayerLaptopHacker>();

        if (phone == null)
            phone = GetComponent<PlayerPhone>();

        if (look == null)
            look = GetComponent<PlayerLook>();

        _deadHash = Animator.StringToHash(deadBoolParam);

        IsDeadNet.OnValueChanged += OnDeadChanged;
        ApplyDeadState(IsDeadNet.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        IsDeadNet.OnValueChanged -= OnDeadChanged;
    }

    public void ServerSetDead(bool dead)
    {
        if (!IsServer)
            return;

        if (IsDeadNet.Value == dead)
            return;

        if (dead)
        {
            var sit = sitAction != null ? sitAction : GetComponent<PlayerSitAction>();
            sit?.ServerResetForRound();
        }

        IsDeadNet.Value = dead;
    }

    public void ServerResetForRound()
    {
        ServerSetDead(false);
    }

    public void ResetForRound()
    {
        ApplyDeadState(false, forceAliveAnimatorState: true);
    }

    private void OnDeadChanged(bool previousValue, bool newValue)
    {
        ApplyDeadState(newValue);
    }

    private void ApplyDeadState(bool dead, bool forceAliveAnimatorState = false)
    {
        if (look != null)
            look.SetDeadLookMode(dead);

        if (dead)
        {
            ClearActiveToolsLocal();

            if (animator != null)
                animator.SetBool(_deadHash, true);

            return;
        }

        if (animator != null)
        {
            animator.SetBool(_deadHash, false);

            if (forceAliveAnimatorState && !string.IsNullOrWhiteSpace(instantAliveStateName))
            {
                animator.Play(
                    instantAliveStateName,
                    baseLayerIndex,
                    Mathf.Clamp01(instantAliveNormalizedTime)
                );

                animator.Update(0f);
            }
        }
    }

    private void ClearActiveToolsLocal()
    {
        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();
        laptopHacker?.ForceResetLocalForRound();
        phone?.ForceResetPhoneLocal();

        if (look != null)
        {
            look.SetPhoneAim(false);
            look.SetAimHeld(false);
        }
    }
}
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

    private int _sitTrigHash;

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
    }

    public void TriggerSitDown()
    {
        if (!IsOwner || animator == null)
            return;

        if (motor != null && (motor.sliding || motor.Coiling))
            return;

        animator.SetTrigger(_sitTrigHash);
    }
}

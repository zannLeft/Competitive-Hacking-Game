using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class BadGuyAttack : NetworkBehaviour, IPlayerRoundResettable
{
    [Header("Refs")]
    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private PlayerDeathState deathState;

    [Header("Attack")]
    [SerializeField]
    private float attackRange = 2.2f;

    [SerializeField]
    private float serverRangeTolerance = 0.45f;

    [SerializeField]
    private float attackCooldown = 0.65f;

    [SerializeField]
    private LayerMask attackMask = ~0;

    [SerializeField]
    private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    private float _nextAttackTime;

    void Reset()
    {
        playerSetup = GetComponent<PlayerSetup>();
        deathState = GetComponent<PlayerDeathState>();

        var look = GetComponent<PlayerLook>();
        if (look != null)
            playerCamera = look.cam;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (deathState == null)
            deathState = GetComponent<PlayerDeathState>();

        if (playerCamera == null)
        {
            var look = GetComponent<PlayerLook>();
            if (look != null)
                playerCamera = look.cam;
        }
    }

    public void TryAttack()
    {
        if (!IsOwner)
            return;

        if (Time.time < _nextAttackTime)
            return;

        if (!CanLocalAttack())
            return;

        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, attackRange, attackMask, triggerInteraction))
            return;

        PlayerDeathState targetDeath = hit.collider.GetComponentInParent<PlayerDeathState>();

        if (targetDeath == null)
            return;

        if (targetDeath.NetworkObject == null)
            return;

        if (targetDeath.NetworkObject == NetworkObject)
            return;

        _nextAttackTime = Time.time + attackCooldown;

        RequestAttackServerRpc(targetDeath.NetworkObject.NetworkObjectId);
    }

    private bool CanLocalAttack()
    {
        if (playerSetup == null || !playerSetup.IsBadGuy.Value)
            return false;

        if (deathState != null && deathState.IsDead)
            return false;

        return true;
    }

    [ServerRpc]
    private void RequestAttackServerRpc(ulong targetNetworkObjectId)
    {
        if (!ValidateAttackerServer())
            return;

        var nm = NetworkManager.Singleton;

        if (nm == null)
            return;

        if (!nm.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
            return;

        if (targetObj == null || targetObj == NetworkObject)
            return;

        PlayerDeathState targetDeath =
            targetObj.GetComponent<PlayerDeathState>()
            ?? targetObj.GetComponentInChildren<PlayerDeathState>(true);

        PlayerSetup targetSetup =
            targetObj.GetComponent<PlayerSetup>()
            ?? targetObj.GetComponentInChildren<PlayerSetup>(true);

        if (targetDeath == null || targetSetup == null)
            return;

        if (targetDeath.IsDead)
            return;

        // Bad guy cannot kill another bad guy.
        if (targetSetup.IsBadGuy.Value)
            return;

        float allowedRange = attackRange + serverRangeTolerance;
        float dist = Vector3.Distance(transform.position, targetObj.transform.position);

        if (dist > allowedRange)
        {
            Debug.LogWarning(
                $"[BadGuyAttack] Rejected attack. Distance {dist:0.00} > allowed {allowedRange:0.00}"
            );
            return;
        }

        targetDeath.ServerSetDead(true);
    }

    private bool ValidateAttackerServer()
    {
        PlayerSetup setup =
            playerSetup != null
                ? playerSetup
                : GetComponent<PlayerSetup>() ?? GetComponentInChildren<PlayerSetup>(true);

        if (setup == null || !setup.IsBadGuy.Value)
            return false;

        PlayerDeathState selfDeath =
            deathState != null
                ? deathState
                : GetComponent<PlayerDeathState>() ?? GetComponentInChildren<PlayerDeathState>(true);

        if (selfDeath != null && selfDeath.IsDead)
            return false;

        return true;
    }

    public void ResetForRound()
    {
        _nextAttackTime = 0f;
    }
}
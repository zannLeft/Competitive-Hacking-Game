using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerReviver : NetworkBehaviour, IPlayerRoundResettable
{
    [Header("References")]
    [SerializeField]
    private PlayerLifeState lifeState;

    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private PlayerSitAction sitAction;

    [Tooltip("Required. Assign the real PlayerCamera transform here.")]
    [SerializeField]
    private Transform reviveOriginOverride;

    [Header("Revive")]
    [SerializeField]
    private float reviveSeconds = 3.0f;

    [SerializeField]
    private float reviveRange = 2.2f;

    [Tooltip("How far from the center of the screen the body can be and still count as the revive target.")]
    [SerializeField]
    private float maxTargetAngle = 35f;

    [Tooltip("Server-side distance allowance from this player root to the body revive anchor.")]
    [SerializeField]
    private float maxServerReviveDistance = 3.5f;

    private DownedBodyObject currentTarget;
    private bool reviveHeld;
    private bool mustReleaseRevive;
    private float reviveProgress;
    private bool warnedMissingReviveOrigin;

    public DownedBodyObject CurrentTarget => currentTarget;
    public bool HasReviveTarget => currentTarget != null;
    public bool IsHoldingRevive => reviveHeld;
    public float ReviveProgress01 =>
        reviveSeconds <= 0f ? 0f : Mathf.Clamp01(reviveProgress / reviveSeconds);

    private void Reset()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheReferences();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (!CanReviveLocally())
        {
            ForceResetLocalForRound();
            return;
        }

        RefreshReviveTarget();

        if (currentTarget == null)
        {
            ResetReviveProgress();
            return;
        }

        if (!reviveHeld || mustReleaseRevive)
        {
            reviveProgress = 0f;
            return;
        }

        reviveProgress += Time.deltaTime;

        if (reviveProgress < reviveSeconds)
            return;

        ulong targetBodyNetworkObjectId = currentTarget.NetworkObject.NetworkObjectId;

        reviveProgress = 0f;
        mustReleaseRevive = true;

        RequestReviveServerRpc(targetBodyNetworkObjectId);
    }

    private void CacheReferences()
    {
        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();
    }

    public void SetReviveHeld(bool held)
    {
        reviveHeld = held && CanReviveLocally();

        if (!reviveHeld)
        {
            mustReleaseRevive = false;
            reviveProgress = 0f;
        }
    }

    public void ForceResetLocalForRound()
    {
        reviveHeld = false;
        mustReleaseRevive = false;
        reviveProgress = 0f;
        currentTarget = null;
    }

    private void ResetReviveProgress()
    {
        reviveProgress = 0f;
    }

    private bool CanReviveLocally()
    {
        if (lifeState != null)
            return lifeState.CanUseSurvivorTools;

        if (playerSetup != null && playerSetup.IsBadGuy.Value)
            return false;

        return true;
    }

    private bool IsBusyWithLaptopOrSitting()
    {
        return sitAction != null && sitAction.IsSittingOrTransitioning;
    }

    private void RefreshReviveTarget()
    {
        currentTarget = null;

        if (IsBusyWithLaptopOrSitting())
            return;

        if (reviveOriginOverride == null)
        {
            WarnMissingReviveOriginOnce();
            return;
        }

        Vector3 origin = reviveOriginOverride.position;
        Vector3 forward = reviveOriginOverride.forward;

        float bestScore = float.MaxValue;
        DownedBodyObject bestBody = null;

        var bodies = DownedBodyObject.SpawnedBodies;

        for (int i = 0; i < bodies.Count; i++)
        {
            DownedBodyObject body = bodies[i];

            if (!IsValidLocalBodyTarget(body))
                continue;

            Vector3 targetPosition = body.ReviveAnchor.position;
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;

            if (distance > reviveRange)
                continue;

            if (distance <= 0.001f)
                continue;

            float angle = Vector3.Angle(forward, toTarget / distance);

            if (angle > maxTargetAngle)
                continue;

            float score = angle * 100f + distance;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestBody = body;
        }

        currentTarget = bestBody;
    }

    private bool IsValidLocalBodyTarget(DownedBodyObject body)
    {
        if (body == null)
            return false;

        if (body.NetworkObject == null || !body.NetworkObject.IsSpawned)
            return false;

        if (!body.IsRevivable)
            return false;

        if (body.DownedPlayerClientId.Value == OwnerClientId)
            return false;

        if (body.SourcePlayerNetworkObjectId.Value == ulong.MaxValue)
            return false;

        return true;
    }

    private void WarnMissingReviveOriginOnce()
    {
        if (warnedMissingReviveOrigin)
            return;

        warnedMissingReviveOrigin = true;

        Debug.LogWarning(
            "[PlayerReviver] Revive Origin Override is not assigned. Drag the real PlayerCamera transform into this field on the player prefab.",
            this
        );
    }

    [ServerRpc]
    private void RequestReviveServerRpc(
        ulong bodyNetworkObjectId,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanReviveOnServer())
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                bodyNetworkObjectId,
                out NetworkObject bodyNetworkObject
            )
        )
            return;

        DownedBodyObject body = bodyNetworkObject.GetComponent<DownedBodyObject>();

        if (body == null)
            return;

        if (!body.IsRevivable)
            return;

        if (body.DownedPlayerClientId.Value == OwnerClientId)
            return;

        Vector3 revivePosition = body.ReviveAnchor.position;

        if ((revivePosition - transform.position).sqrMagnitude >
            maxServerReviveDistance * maxServerReviveDistance)
            return;

        ulong sourcePlayerNetworkObjectId = body.SourcePlayerNetworkObjectId.Value;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                sourcePlayerNetworkObjectId,
                out NetworkObject sourcePlayerNetworkObject
            )
        )
            return;

        PlayerLifeState targetLifeState = sourcePlayerNetworkObject.GetComponent<PlayerLifeState>();

        if (targetLifeState == null)
            return;

        if (!targetLifeState.CanBeRevived)
            return;

        targetLifeState.ServerReviveFromDowned(revivePosition);
    }

    private bool CanReviveOnServer()
    {
        if (lifeState != null)
            return lifeState.CanUseSurvivorTools;

        if (playerSetup != null && playerSetup.IsBadGuy.Value)
            return false;

        return true;
    }

    public void ResetForRound()
    {
        ForceResetLocalForRound();
        warnedMissingReviveOrigin = false;
    }
}

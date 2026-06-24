using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerReviver : NetworkBehaviour, IPlayerRoundResettable
{
    private const ulong NoBodyNetworkObjectId = ulong.MaxValue;
    private const uint NoReviveRequestId = 0u;

    private enum LocalRevivePhase : byte
    {
        Idle = 0,
        WaitingForServer = 1,
        Active = 2,
        WaitingForCompletion = 3,
    }

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
    [Min(0.1f)]
    [SerializeField]
    private float reviveSeconds = 3.0f;

    [Min(0.1f)]
    [SerializeField]
    private float reviveRange = 2.2f;

    [Tooltip("How far from the center of the screen the body can be and still count as the revive target.")]
    [Range(1f, 90f)]
    [SerializeField]
    private float maxTargetAngle = 35f;

    [Tooltip("Server-side distance allowance from this player root to the body revive anchor.")]
    [Min(0.1f)]
    [SerializeField]
    private float maxServerReviveDistance = 3.5f;

    [Tooltip("How far the reviver may move from the position where the hold began before the revive is cancelled.")]
    [Min(0f)]
    [SerializeField]
    private float maxMovementWhileReviving = 0.45f;

    [Tooltip("Extra time after the authoritative hold duration for the completion request to arrive before the server cancels the session.")]
    [Min(0.1f)]
    [SerializeField]
    private float serverCompletionGraceSeconds = 2f;

    [Header("Line Of Sight")]
    [Tooltip("Layers that can block a revive. Player and ragdoll colliders are ignored automatically.")]
    [SerializeField]
    private LayerMask reviveLineOfSightMask = ~0;

    [Tooltip("Approximate server-side eye height used for authoritative line-of-sight validation.")]
    [Min(0f)]
    [SerializeField]
    private float serverLineOfSightOriginHeight = 1.4f;

    private readonly RaycastHit[] lineOfSightHits = new RaycastHit[32];

    private DownedBodyObject currentTarget;
    private DownedBodyObject lockedTarget;
    private bool reviveHeld;
    private bool mustReleaseRevive;
    private bool warnedMissingReviveOrigin;

    private LocalRevivePhase localPhase;
    private uint nextLocalRequestId = 1u;
    private uint localRequestId;
    private ulong localTargetBodyNetworkObjectId = NoBodyNetworkObjectId;
    private double localServerStartTime;
    private Vector3 localReviveStartPosition;

    private bool serverSessionActive;
    private uint serverRequestId;
    private ulong serverTargetBodyNetworkObjectId = NoBodyNetworkObjectId;
    private ulong serverTargetPlayerNetworkObjectId = NoBodyNetworkObjectId;
    private double serverReviveStartTime;
    private Vector3 serverReviveStartPosition;
    private bool serverCompletionRequested;
    private bool serverWaitingForSafePlacement;

    public DownedBodyObject CurrentTarget => lockedTarget != null ? lockedTarget : currentTarget;
    public bool HasReviveTarget => CurrentTarget != null;
    public bool IsHoldingRevive =>
        reviveHeld
        && !mustReleaseRevive
        && localPhase != LocalRevivePhase.Idle;

    public float ReviveProgress01
    {
        get
        {
            if (
                localPhase != LocalRevivePhase.Active
                && localPhase != LocalRevivePhase.WaitingForCompletion
            )
                return 0f;

            if (reviveSeconds <= 0f || localServerStartTime <= 0d)
                return 0f;

            double elapsed = Math.Max(0d, GetNetworkTime() - localServerStartTime);
            return Mathf.Clamp01((float)(elapsed / reviveSeconds));
        }
    }

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
        ResetLocalState(requireRelease: false);
        ClearServerSession();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            ClearServerSession();

        ResetLocalState(requireRelease: false);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsServer)
            ServerUpdateReviveSession();

        if (!IsOwner)
            return;

        UpdateLocalRevive();
    }

    private void UpdateLocalRevive()
    {
        if (!CanReviveLocally() || IsBusyWithLaptopOrSitting())
        {
            if (localPhase != LocalRevivePhase.Idle)
                CancelLocalSession(requireRelease: reviveHeld, notifyServer: true);

            currentTarget = null;
            return;
        }

        if (localPhase == LocalRevivePhase.Idle)
        {
            RefreshReviveTarget();

            if (reviveHeld && !mustReleaseRevive && currentTarget != null)
                BeginLocalRevive(currentTarget);

            return;
        }

        currentTarget = lockedTarget;

        if (!reviveHeld || mustReleaseRevive)
        {
            CancelLocalSession(requireRelease: false, notifyServer: true);
            return;
        }

        if (!IsLockedTargetStillValidLocally())
        {
            CancelLocalSession(requireRelease: true, notifyServer: true);
            return;
        }

        float maxMoveSqr = maxMovementWhileReviving * maxMovementWhileReviving;
        if ((transform.position - localReviveStartPosition).sqrMagnitude > maxMoveSqr)
        {
            CancelLocalSession(requireRelease: true, notifyServer: true);
            return;
        }

        if (localPhase == LocalRevivePhase.WaitingForCompletion)
            return;

        if (localPhase != LocalRevivePhase.Active)
            return;

        if (ReviveProgress01 < 1f)
            return;

        localPhase = LocalRevivePhase.WaitingForCompletion;
        CompleteReviveServerRpc(localRequestId, localTargetBodyNetworkObjectId);
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
        if (!IsOwner)
            return;

        if (!held)
        {
            reviveHeld = false;
            mustReleaseRevive = false;

            if (localPhase != LocalRevivePhase.Idle)
                CancelLocalSession(requireRelease: false, notifyServer: true);

            return;
        }

        reviveHeld = CanReviveLocally();
    }

    public void ForceResetLocalForRound()
    {
        if (IsOwner && localPhase != LocalRevivePhase.Idle)
            CancelReviveServerRpc(localRequestId);

        ResetLocalState(requireRelease: false);
    }

    private void BeginLocalRevive(DownedBodyObject body)
    {
        if (!IsValidLocalBodyTarget(body))
            return;

        localRequestId = GetNextLocalRequestId();
        localTargetBodyNetworkObjectId = body.NetworkObjectId;
        localServerStartTime = 0d;
        localReviveStartPosition = transform.position;
        lockedTarget = body;
        currentTarget = body;
        localPhase = LocalRevivePhase.WaitingForServer;

        BeginReviveServerRpc(localRequestId, localTargetBodyNetworkObjectId);
    }

    private void CancelLocalSession(bool requireRelease, bool notifyServer)
    {
        uint requestIdToCancel = localRequestId;
        bool hadRequest = localPhase != LocalRevivePhase.Idle && requestIdToCancel != NoReviveRequestId;

        ResetLocalState(requireRelease);

        if (notifyServer && hadRequest && IsSpawned)
            CancelReviveServerRpc(requestIdToCancel);
    }

    private void ResetLocalState(bool requireRelease)
    {
        localPhase = LocalRevivePhase.Idle;
        localRequestId = NoReviveRequestId;
        localTargetBodyNetworkObjectId = NoBodyNetworkObjectId;
        localServerStartTime = 0d;
        localReviveStartPosition = Vector3.zero;
        lockedTarget = null;
        currentTarget = null;
        mustReleaseRevive = requireRelease;

        if (!requireRelease)
            reviveHeld = false;
    }

    private uint GetNextLocalRequestId()
    {
        uint result = nextLocalRequestId++;

        if (result == NoReviveRequestId)
            result = nextLocalRequestId++;

        if (nextLocalRequestId == NoReviveRequestId)
            nextLocalRequestId = 1u;

        return result;
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

            if (distance > reviveRange || distance <= 0.001f)
                continue;

            float angle = Vector3.Angle(forward, toTarget / distance);

            if (angle > maxTargetAngle)
                continue;

            if (!HasClearReviveLineOfSight(origin, body))
                continue;

            float score = angle * 100f + distance;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestBody = body;
        }

        currentTarget = bestBody;
    }

    private bool IsLockedTargetStillValidLocally()
    {
        if (!IsValidLocalBodyTarget(lockedTarget))
            return false;

        if (lockedTarget.NetworkObjectId != localTargetBodyNetworkObjectId)
            return false;

        if (reviveOriginOverride == null)
            return false;

        Vector3 origin = reviveOriginOverride.position;
        Vector3 targetPosition = lockedTarget.ReviveAnchor.position;
        Vector3 toTarget = targetPosition - origin;
        float distance = toTarget.magnitude;

        if (distance > reviveRange || distance <= 0.001f)
            return false;

        float angle = Vector3.Angle(reviveOriginOverride.forward, toTarget / distance);
        if (angle > maxTargetAngle)
            return false;

        return HasClearReviveLineOfSight(origin, lockedTarget);
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

    private bool HasClearReviveLineOfSight(Vector3 origin, DownedBodyObject body)
    {
        if (body == null)
            return false;

        Vector3 target = body.ReviveAnchor.position;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;

        if (distance <= 0.001f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            delta / distance,
            lineOfSightHits,
            distance,
            reviveLineOfSightMask,
            QueryTriggerInteraction.Ignore
        );

        SortHitsByDistance(lineOfSightHits, hitCount);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = lineOfSightHits[i].collider;

            if (hitCollider == null)
                continue;

            if (ShouldIgnoreLineOfSightCollider(hitCollider, body))
                continue;

            return false;
        }

        return true;
    }

    private bool ShouldIgnoreLineOfSightCollider(
        Collider hitCollider,
        DownedBodyObject targetBody
    )
    {
        Transform hitTransform = hitCollider.transform;

        if (hitTransform == transform || hitTransform.IsChildOf(transform))
            return true;

        DownedBodyObject hitBody = hitCollider.GetComponentInParent<DownedBodyObject>();
        if (hitBody != null)
            return true;

        PlayerLifeState hitPlayer = hitCollider.GetComponentInParent<PlayerLifeState>();
        if (hitPlayer != null)
            return true;

        return targetBody != null
            && (hitTransform == targetBody.transform || hitTransform.IsChildOf(targetBody.transform));
    }

    private static void SortHitsByDistance(RaycastHit[] hits, int count)
    {
        for (int i = 1; i < count; i++)
        {
            RaycastHit value = hits[i];
            int j = i - 1;

            while (j >= 0 && hits[j].distance > value.distance)
            {
                hits[j + 1] = hits[j];
                j--;
            }

            hits[j + 1] = value;
        }
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
    private void BeginReviveServerRpc(
        uint requestId,
        ulong bodyNetworkObjectId,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (requestId == NoReviveRequestId)
            return;

        if (serverSessionActive)
            ServerEndSession(notifyOwner: true, requireRelease: true);

        if (
            !TryResolveServerReviveTarget(
                bodyNetworkObjectId,
                enforceMovementLimit: false,
                out DownedBodyObject body,
                out PlayerLifeState targetLifeState
            )
        )
        {
            SendSessionEndedToOwner(requestId, requireRelease: true);
            return;
        }

        double now = GetNetworkTime();
        if (targetLifeState.ServerHasBleedOutExpired(now))
        {
            targetLifeState.ServerSetDead(targetLifeState.LastAttackerClientId.Value);
            SendSessionEndedToOwner(requestId, requireRelease: true);
            return;
        }

        serverSessionActive = true;
        serverRequestId = requestId;
        serverTargetBodyNetworkObjectId = body.NetworkObjectId;
        serverTargetPlayerNetworkObjectId = targetLifeState.NetworkObjectId;
        serverReviveStartTime = now;
        serverReviveStartPosition = transform.position;
        serverCompletionRequested = false;
        serverWaitingForSafePlacement = false;

        ReviveSessionStartedClientRpc(
            requestId,
            body.NetworkObjectId,
            now,
            BuildOwnerClientRpcParams()
        );
    }

    [ServerRpc]
    private void CancelReviveServerRpc(
        uint requestId,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!serverSessionActive || requestId != serverRequestId)
            return;

        ServerEndSession(notifyOwner: false, requireRelease: false);
    }

    [ServerRpc]
    private void CompleteReviveServerRpc(
        uint requestId,
        ulong bodyNetworkObjectId,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (
            !serverSessionActive
            || requestId != serverRequestId
            || bodyNetworkObjectId != serverTargetBodyNetworkObjectId
        )
        {
            SendSessionEndedToOwner(requestId, requireRelease: true);
            return;
        }

        // The client's synchronized server clock can reach 100% a fraction of a tick
        // before the server's own clock. Remember the request and complete it as soon as
        // the authoritative duration has elapsed instead of incorrectly cancelling it.
        serverCompletionRequested = true;
        ServerTryCompleteRequestedRevive();
    }

    private void ServerTryCompleteRequestedRevive()
    {
        if (!serverSessionActive || !serverCompletionRequested)
            return;

        double now = GetNetworkTime();
        double completionTime =
            serverReviveStartTime + Math.Max(0.1f, reviveSeconds);

        if (now + 0.001d < completionTime)
            return;

        if (
            !TryResolveServerReviveTarget(
                serverTargetBodyNetworkObjectId,
                enforceMovementLimit: true,
                out DownedBodyObject body,
                out PlayerLifeState targetLifeState
            )
        )
        {
            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        if (targetLifeState.ServerHasBleedOutExpired(now))
        {
            targetLifeState.ServerSetDead(targetLifeState.LastAttackerClientId.Value);
            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        Vector3 requestedRevivePosition = body.ReviveAnchor.position;
        bool revived = targetLifeState.ServerTryReviveFromDowned(
            requestedRevivePosition,
            transform,
            logPlacementFailure: false
        );

        if (revived)
        {
            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        // A valid body can temporarily have no safe standing point while the ragdoll
        // or reviver settles. Keep the completed session alive through the configured
        // grace window and retry from ServerUpdateReviveSession.
        if (!targetLifeState.CanBeRevived)
        {
            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        serverWaitingForSafePlacement = true;
    }

    private void ServerUpdateReviveSession()
    {
        if (!serverSessionActive)
            return;

        double now = GetNetworkTime();
        double timeoutAt =
            serverReviveStartTime
            + Math.Max(0.1f, reviveSeconds)
            + Math.Max(0.1f, serverCompletionGraceSeconds);

        if (now > timeoutAt)
        {
            if (serverWaitingForSafePlacement)
            {
                Debug.LogWarning(
                    $"[PlayerReviver] Revive for body {serverTargetBodyNetworkObjectId} reached 100%, but no safe standing position became available during the completion grace window.",
                    this
                );
            }

            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        if (
            !TryResolveServerReviveTarget(
                serverTargetBodyNetworkObjectId,
                enforceMovementLimit: true,
                out _,
                out PlayerLifeState targetLifeState
            )
        )
        {
            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        if (targetLifeState.ServerHasBleedOutExpired(now))
        {
            targetLifeState.ServerSetDead(targetLifeState.LastAttackerClientId.Value);
            ServerEndSession(notifyOwner: true, requireRelease: true);
            return;
        }

        ServerTryCompleteRequestedRevive();
    }

    private bool TryResolveServerReviveTarget(
        ulong bodyNetworkObjectId,
        bool enforceMovementLimit,
        out DownedBodyObject body,
        out PlayerLifeState targetLifeState
    )
    {
        body = null;
        targetLifeState = null;

        if (!CanReviveOnServer())
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                bodyNetworkObjectId,
                out NetworkObject bodyNetworkObject
            )
        )
            return false;

        body = bodyNetworkObject.GetComponent<DownedBodyObject>();

        if (body == null || !body.IsRevivable)
            return false;

        if (body.DownedPlayerClientId.Value == OwnerClientId)
            return false;

        ulong sourcePlayerNetworkObjectId = body.SourcePlayerNetworkObjectId.Value;
        if (sourcePlayerNetworkObjectId == ulong.MaxValue)
            return false;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                sourcePlayerNetworkObjectId,
                out NetworkObject sourcePlayerNetworkObject
            )
        )
            return false;

        targetLifeState = sourcePlayerNetworkObject.GetComponent<PlayerLifeState>();

        if (targetLifeState == null || !targetLifeState.CanBeRevived)
            return false;

        if (targetLifeState.OwnerClientId != body.DownedPlayerClientId.Value)
            return false;

        if (targetLifeState.CurrentBodyNetworkObjectId.Value != bodyNetworkObjectId)
            return false;

        if (
            serverSessionActive
            && serverTargetPlayerNetworkObjectId != NoBodyNetworkObjectId
            && targetLifeState.NetworkObjectId != serverTargetPlayerNetworkObjectId
        )
            return false;

        MatchFlowManager matchFlow = GetMatchFlowManager();
        if (
            matchFlow != null
            && !matchFlow.ServerCanUseSurvivorInteraction(
                OwnerClientId,
                targetLifeState.OwnerClientId
            )
        )
            return false;

        Vector3 revivePosition = body.ReviveAnchor.position;
        if (
            (revivePosition - transform.position).sqrMagnitude
            > maxServerReviveDistance * maxServerReviveDistance
        )
            return false;

        if (enforceMovementLimit)
        {
            float maxMoveSqr = maxMovementWhileReviving * maxMovementWhileReviving;
            if ((transform.position - serverReviveStartPosition).sqrMagnitude > maxMoveSqr)
                return false;
        }

        Vector3 lineOfSightOrigin = transform.position + Vector3.up * serverLineOfSightOriginHeight;
        if (!HasClearReviveLineOfSight(lineOfSightOrigin, body))
            return false;

        return true;
    }

    private bool CanReviveOnServer()
    {
        if (!IsServer)
            return false;

        if (lifeState != null && !lifeState.CanUseSurvivorTools)
            return false;

        if (lifeState == null && playerSetup != null && playerSetup.IsBadGuy.Value)
            return false;

        if (
            sitAction != null
            && (sitAction.WantsSittingValue || sitAction.IsSittingOrTransitioning)
        )
            return false;

        MatchFlowManager matchFlow = GetMatchFlowManager();
        return matchFlow == null || matchFlow.ServerIsActiveRoundParticipant(OwnerClientId);
    }

    private MatchFlowManager GetMatchFlowManager()
    {
        if (LobbyManager.Instance != null && LobbyManager.Instance.MatchFlow != null)
            return LobbyManager.Instance.MatchFlow;

        return FindAnyObjectByType<MatchFlowManager>(FindObjectsInactive.Include);
    }

    private void ServerEndSession(bool notifyOwner, bool requireRelease)
    {
        uint endedRequestId = serverRequestId;
        ClearServerSession();

        if (notifyOwner && endedRequestId != NoReviveRequestId)
            SendSessionEndedToOwner(endedRequestId, requireRelease);
    }

    private void ClearServerSession()
    {
        serverSessionActive = false;
        serverRequestId = NoReviveRequestId;
        serverTargetBodyNetworkObjectId = NoBodyNetworkObjectId;
        serverTargetPlayerNetworkObjectId = NoBodyNetworkObjectId;
        serverReviveStartTime = 0d;
        serverReviveStartPosition = Vector3.zero;
        serverCompletionRequested = false;
        serverWaitingForSafePlacement = false;
    }

    private void SendSessionEndedToOwner(uint requestId, bool requireRelease)
    {
        if (!IsServer || requestId == NoReviveRequestId)
            return;

        ReviveSessionEndedClientRpc(
            requestId,
            requireRelease,
            BuildOwnerClientRpcParams()
        );
    }

    private ClientRpcParams BuildOwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { OwnerClientId },
            },
        };
    }

    [ClientRpc]
    private void ReviveSessionStartedClientRpc(
        uint requestId,
        ulong bodyNetworkObjectId,
        double serverStartTime,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (!IsOwner)
            return;

        if (
            localPhase != LocalRevivePhase.WaitingForServer
            || requestId != localRequestId
            || bodyNetworkObjectId != localTargetBodyNetworkObjectId
        )
            return;

        if (!reviveHeld || mustReleaseRevive || !IsLockedTargetStillValidLocally())
        {
            CancelLocalSession(requireRelease: true, notifyServer: true);
            return;
        }

        localServerStartTime = serverStartTime;
        localPhase = LocalRevivePhase.Active;
    }

    [ClientRpc]
    private void ReviveSessionEndedClientRpc(
        uint requestId,
        bool requireRelease,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (!IsOwner || requestId != localRequestId)
            return;

        ResetLocalState(requireRelease);
    }

    private double GetNetworkTime()
    {
        if (NetworkManager.Singleton == null)
            return 0d;

        return NetworkManager.Singleton.ServerTime.Time;
    }

    public void ResetForRound()
    {
        if (IsServer)
            ClearServerSession();

        ForceResetLocalForRound();
        warnedMissingReviveOrigin = false;
    }
}

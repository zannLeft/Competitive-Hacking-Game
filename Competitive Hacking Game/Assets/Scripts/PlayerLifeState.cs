using System;
using System.Collections;
using Unity.Netcode;
using Unity.Multiplayer.Samples.Utilities.ClientAuthority;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerLifeStateType : byte
{
    Alive = 0,
    Downed = 1,
    Dead = 2,
}

[DisallowMultipleComponent]
public class PlayerLifeState
    : NetworkBehaviour,
        IPlayerRoundResettable,
        IPlayerRoundServerResettable
{
    public const ulong NoAttackerClientId = ulong.MaxValue;
    public const ulong NoBodyNetworkObjectId = ulong.MaxValue;

    [Header("References")]
    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private InputManager inputManager;

    [SerializeField]
    private PlayerPhone phone;

    [SerializeField]
    private PlayerLaptopHacker laptopHacker;

    [SerializeField]
    private PlayerSitAction sitAction;

    [SerializeField]
    private PlayerLaptopVisual laptopVisual;

    [SerializeField]
    private PlayerLook playerLook;

    [SerializeField]
    private PlayerBodyVisibility bodyVisibility;

    [SerializeField]
    private PlayerMotor playerMotor;

    [SerializeField]
    private PlayerFlashlight playerFlashlight;

    [Header("Downed Body")]
    [Tooltip("Network prefab spawned when this player becomes Downed. Add DownedBodyObject + NetworkObject to the prefab and register it in NetworkManager Network Prefabs.")]
    [SerializeField]
    private DownedBodyObject downedBodyPrefab;

    [Tooltip("Small upward offset for spawning the placeholder body so it does not start slightly under the floor.")]
    [SerializeField]
    private float downedBodySpawnUpOffset = 0.05f;

    [Header("Bleed Out")]
    [Tooltip("Authoritative server duration before a Downed survivor becomes permanently Dead.")]
    [Min(1f)]
    [SerializeField]
    private float bleedOutDurationSeconds = 30f;

    [Header("Revive Placement")]
    [Tooltip("Layers considered when finding ground and checking standing room for a revived player.")]
    [SerializeField]
    private LayerMask revivePlacementMask = ~0;

    [Min(0.1f)]
    [SerializeField]
    private float reviveGroundProbeUp = 1.5f;

    [Min(0.1f)]
    [SerializeField]
    private float reviveGroundProbeDown = 3f;

    [Range(0f, 89f)]
    [SerializeField]
    private float reviveMaxGroundSlope = 55f;

    [Tooltip("Maximum horizontal distance searched around the ragdoll revive anchor when the exact point is blocked.")]
    [Min(0f)]
    [SerializeField]
    private float revivePlacementSearchRadius = 0.9f;

    [Tooltip("Small upward root offset above the detected ground point.")]
    [Min(0f)]
    [SerializeField]
    private float reviveGroundOffset = 0.02f;

    [Tooltip("Extra radius used when checking whether the standing CharacterController fits.")]
    [Min(0f)]
    [SerializeField]
    private float reviveCollisionPadding = 0.03f;

    [Header("Revive Presentation")]
    [Tooltip("Maximum time clients keep the restored player hidden while waiting for the authoritative revive teleport to settle.")]
    [Min(0.1f)]
    [SerializeField]
    private float reviveTeleportRevealTimeout = 2f;

    [Tooltip("How close a non-owner replica must be to the authoritative revive destination before its player mesh can be revealed.")]
    [Min(0.001f)]
    [SerializeField]
    private float reviveTeleportRevealDistance = 0.08f;

    [Tooltip("Maximum rotation difference allowed before a non-owner replica is revealed at the revive destination.")]
    [Range(0.1f, 45f)]
    [SerializeField]
    private float reviveTeleportRevealAngle = 5f;

    [Tooltip("Consecutive frames a non-owner replica must remain at the revive destination before it is revealed.")]
    [Min(1)]
    [SerializeField]
    private int reviveTeleportStableFrames = 2;

    [Header("Debug Testing")]
    [Tooltip("Optional temporary test keys. Leave OFF unless you want to test life states manually.")]
    [SerializeField]
    private bool enableDebugHotkeys = false;

    public NetworkVariable<PlayerLifeStateType> State = new NetworkVariable<PlayerLifeStateType>(
        PlayerLifeStateType.Alive,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Vector3> DownedPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> LastAttackerClientId = new NetworkVariable<ulong>(
        NoAttackerClientId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<double> DownedStartedServerTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<double> DownedExpiresServerTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> CurrentBodyNetworkObjectId = new NetworkVariable<ulong>(
        NoBodyNetworkObjectId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<PlayerLifeStateType, PlayerLifeStateType> OnLifeStateChanged;

    private static readonly Vector2[] RevivePlacementDirections =
    {
        new Vector2(1f, 0f),
        new Vector2(-1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(0f, -1f),
        new Vector2(0.7071068f, 0.7071068f),
        new Vector2(-0.7071068f, 0.7071068f),
        new Vector2(0.7071068f, -0.7071068f),
        new Vector2(-0.7071068f, -0.7071068f),
    };

    private readonly RaycastHit[] reviveGroundHits = new RaycastHit[32];
    private readonly Collider[] reviveOverlapColliders = new Collider[32];

    private DownedBodyObject currentDownedBody;
    private ClientNetworkTransform clientNetworkTransform;
    private Coroutine deferredReviveRestoreCoroutine;
    private bool reviveTeleportAppliedLocally;
    private bool waitingForReviveTeleportPresentation;
    private bool hasPendingReviveTeleportTarget;
    private Vector3 pendingReviveTeleportPosition;
    private Quaternion pendingReviveTeleportRotation = Quaternion.identity;
    private int reviveTeleportStableFrameCount;

    public PlayerLifeStateType CurrentState => State.Value;
    public bool IsWaitingForReviveTeleportPresentation => waitingForReviveTeleportPresentation;

    public bool IsAlive => State.Value == PlayerLifeStateType.Alive;
    public bool IsDowned => State.Value == PlayerLifeStateType.Downed;
    public bool IsDead => State.Value == PlayerLifeStateType.Dead;

    public bool IsBadGuy => playerSetup != null && playerSetup.IsBadGuy.Value;

    public bool CanMove => IsAlive;

    public bool CanUseSurvivorTools => IsAlive && !IsBadGuy;

    public bool CanAttackSurvivors => IsAlive && IsBadGuy;

    public bool CanBeAttacked => IsAlive && !IsBadGuy;

    public bool CanBeRevived => IsDowned && !IsBadGuy;

    public bool ShouldBeInSpectatorMode => IsDead;

    public bool ShouldSuppressGameplayInput => IsDowned || IsDead;

    public bool HasBody => CurrentBodyNetworkObjectId.Value != NoBodyNetworkObjectId;

    public double SecondsSinceDowned
    {
        get
        {
            if (!IsDowned)
                return 0d;

            double now = GetNetworkTime();
            return Math.Max(0d, now - DownedStartedServerTime.Value);
        }
    }

    public double SecondsUntilBleedOut
    {
        get
        {
            if (!IsDowned || DownedExpiresServerTime.Value <= 0d)
                return 0d;

            return Math.Max(0d, DownedExpiresServerTime.Value - GetNetworkTime());
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

        State.OnValueChanged += HandleLifeStateChanged;

        ApplyLifeStateSideEffects(State.Value, State.Value, force: true);
    }

    public override void OnNetworkDespawn()
    {
        State.OnValueChanged -= HandleLifeStateChanged;
        CancelDeferredReviveRestore();
        reviveTeleportAppliedLocally = false;
        ClearPendingReviveTeleportTarget();

        if (IsServer)
            ServerDespawnDownedBody();

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!enableDebugHotkeys)
            return;

        if (!IsOwner)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f7Key.wasPressedThisFrame)
            DebugRequestSetStateServerRpc(PlayerLifeStateType.Downed, transform.position);

        if (keyboard.f8Key.wasPressedThisFrame)
            DebugRequestSetStateServerRpc(PlayerLifeStateType.Dead, transform.position);

        if (keyboard.f9Key.wasPressedThisFrame)
            DebugRequestSetStateServerRpc(PlayerLifeStateType.Alive, transform.position);
    }

    private void CacheReferences()
    {
        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (inputManager == null)
            inputManager = GetComponent<InputManager>();

        if (phone == null)
            phone = GetComponent<PlayerPhone>();

        if (laptopHacker == null)
            laptopHacker = GetComponent<PlayerLaptopHacker>();

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (laptopVisual == null)
            laptopVisual = GetComponent<PlayerLaptopVisual>();

        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>();

        if (bodyVisibility == null)
            bodyVisibility = GetComponent<PlayerBodyVisibility>();

        if (playerMotor == null)
            playerMotor = GetComponent<PlayerMotor>();

        if (playerFlashlight == null)
            playerFlashlight = GetComponent<PlayerFlashlight>();

        if (clientNetworkTransform == null)
            clientNetworkTransform = GetComponent<ClientNetworkTransform>();
    }

    private void HandleLifeStateChanged(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState
    )
    {
        if (newState == PlayerLifeStateType.Downed)
        {
            CancelDeferredReviveRestore();
            reviveTeleportAppliedLocally = false;
            ClearPendingReviveTeleportTarget();
            TryCopyCurrentPoseToBodyLocal();
        }
        else if (newState == PlayerLifeStateType.Dead)
        {
            CancelDeferredReviveRestore();
            reviveTeleportAppliedLocally = false;
            ClearPendingReviveTeleportTarget();
        }

        ApplyLifeStateSideEffects(previousState, newState, force: false);
        OnLifeStateChanged?.Invoke(previousState, newState);
    }

    private void TryCopyCurrentPoseToBodyLocal()
    {
        if (CurrentBodyNetworkObjectId.Value == NoBodyNetworkObjectId)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                CurrentBodyNetworkObjectId.Value,
                out NetworkObject bodyNetworkObject
            )
        )
            return;

        DownedBodyObject body = bodyNetworkObject.GetComponent<DownedBodyObject>();

        if (body == null)
            return;

        body.CopyPoseAndActivateRagdollFromSourceTransform(transform);
    }

    private void ApplyLifeStateSideEffects(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState,
        bool force
    )
    {
        CacheReferences();

        if (!force && previousState == newState)
            return;

        bool isNetworkReviveRestore =
            !force
            && previousState == PlayerLifeStateType.Downed
            && newState == PlayerLifeStateType.Alive;

        if (isNetworkReviveRestore && !reviveTeleportAppliedLocally)
        {
            BeginDeferredReviveRestore();
            return;
        }

        CancelDeferredReviveRestore();
        ApplySharedStateCleanup(newState);
        ApplyOwnerStateCleanup(newState);

        if (newState == PlayerLifeStateType.Alive)
        {
            reviveTeleportAppliedLocally = false;
            ClearPendingReviveTeleportTarget();
        }
    }

    private void BeginDeferredReviveRestore()
    {
        CancelDeferredReviveRestore();

        waitingForReviveTeleportPresentation = true;

        // Keep the old downed presentation until this client has actually applied the
        // authoritative revive teleport. This prevents a one-frame flash at the hidden
        // original player root when the Alive NetworkVariable arrives before the RPC.
        bodyVisibility?.SetBodyVisible(false);
        playerMotor?.SetControllerCollisionEnabled(false);

        if (IsOwner)
            inputManager?.SetGameplaySuppressed(true);

        deferredReviveRestoreCoroutine = StartCoroutine(DeferredReviveRestoreRoutine());
    }

    private IEnumerator DeferredReviveRestoreRoutine()
    {
        float timeoutAt = Time.unscaledTime + Mathf.Max(0.1f, reviveTeleportRevealTimeout);

        while (
            State.Value == PlayerLifeStateType.Alive
            && !HasReviveTeleportSettledLocally()
            && Time.unscaledTime < timeoutAt
        )
        {
            yield return null;
        }

        deferredReviveRestoreCoroutine = null;
        waitingForReviveTeleportPresentation = false;

        if (State.Value != PlayerLifeStateType.Alive)
            yield break;

        if (!reviveTeleportAppliedLocally)
        {
            Debug.LogWarning(
                $"[PlayerLifeState] Timed out waiting for {name}'s revive teleport to settle. Restoring the player as a fallback.",
                this
            );
        }

        ApplySharedStateCleanup(PlayerLifeStateType.Alive);
        ApplyOwnerStateCleanup(PlayerLifeStateType.Alive);
        reviveTeleportAppliedLocally = false;
        ClearPendingReviveTeleportTarget();
    }

    private bool HasReviveTeleportSettledLocally()
    {
        if (reviveTeleportAppliedLocally)
            return true;

        if (!hasPendingReviveTeleportTarget)
            return false;

        float revealDistance = Mathf.Max(0.001f, reviveTeleportRevealDistance);
        float distanceSquared =
            (transform.position - pendingReviveTeleportPosition).sqrMagnitude;

        float revealAngle = Mathf.Max(0.1f, reviveTeleportRevealAngle);
        float rotationDifference = Quaternion.Angle(
            transform.rotation,
            pendingReviveTeleportRotation
        );

        if (
            distanceSquared > revealDistance * revealDistance
            || rotationDifference > revealAngle
        )
        {
            reviveTeleportStableFrameCount = 0;
            return false;
        }

        reviveTeleportStableFrameCount++;

        if (reviveTeleportStableFrameCount < Mathf.Max(1, reviveTeleportStableFrames))
            return false;

        reviveTeleportAppliedLocally = true;
        return true;
    }

    private void SetPendingReviveTeleportTarget(Vector3 position, Quaternion rotation)
    {
        hasPendingReviveTeleportTarget = true;
        pendingReviveTeleportPosition = position;
        pendingReviveTeleportRotation = rotation;
        reviveTeleportStableFrameCount = 0;
    }

    private void ClearPendingReviveTeleportTarget()
    {
        hasPendingReviveTeleportTarget = false;
        pendingReviveTeleportPosition = Vector3.zero;
        pendingReviveTeleportRotation = Quaternion.identity;
        reviveTeleportStableFrameCount = 0;
    }

    private void CancelDeferredReviveRestore()
    {
        if (deferredReviveRestoreCoroutine != null)
        {
            StopCoroutine(deferredReviveRestoreCoroutine);
            deferredReviveRestoreCoroutine = null;
        }

        waitingForReviveTeleportPresentation = false;
    }

    private void ApplySharedStateCleanup(PlayerLifeStateType newState)
    {
        bool isAlive = newState == PlayerLifeStateType.Alive;

        bodyVisibility?.SetBodyVisible(isAlive);
        playerMotor?.SetControllerCollisionEnabled(isAlive);

        if (isAlive)
            return;

        // This runs on every client because phone/laptop/sitting visuals are local scene objects,
        // not separate network objects. Without this, remotes can keep seeing stuck visuals.
        phone?.ForceResetPhoneLocal();
        laptopHacker?.ForceResetLocalForRound();
        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();
    }

    private void ApplyOwnerStateCleanup(PlayerLifeStateType newState)
    {
        if (!IsOwner)
            return;

        if (newState == PlayerLifeStateType.Alive)
        {
            inputManager?.ForceClearGameplaySuppression();

            if (playerLook != null)
                playerLook.enabled = true;

            return;
        }

        ClearOwnerGameplayActions();

        inputManager?.SetGameplaySuppressed(true);
    }

    private void ClearOwnerGameplayActions()
    {
        phone?.ForceResetPhoneLocal();

        laptopHacker?.SetHackHeld(false);
        laptopHacker?.ForceResetLocalForRound();

        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();

        if (playerLook != null)
        {
            playerLook.SetPhoneAim(false);
            playerLook.SetAimHeld(false);
        }
    }

    public void ServerSetAlive()
    {
        if (!IsServer)
            return;

        ServerDespawnDownedBody();

        SetStateServer(
            PlayerLifeStateType.Alive,
            transform.position,
            NoAttackerClientId
        );
    }

    public void ServerSetDowned(
        Vector3 downedPosition,
        ulong attackerClientId = NoAttackerClientId,
        Vector3 ragdollImpulse = default,
        Vector3 ragdollForcePosition = default
    )
    {
        if (!IsServer)
            return;

        if (IsBadGuy)
            return;

        if (State.Value != PlayerLifeStateType.Alive)
            return;

        if (!ServerCanChangeLifeStateDuringMatch())
            return;

        Vector3 safeDownedPosition = downedPosition;

        bool flashlightWasOn =
            playerFlashlight != null && playerFlashlight.ShouldCarryLitFlashlightToDownedBody();

        ServerSpawnOrRefreshDownedBody(
            safeDownedPosition,
            PlayerLifeStateType.Downed,
            flashlightWasOn,
            ragdollImpulse,
            ragdollForcePosition
        );

        SetStateServer(
            PlayerLifeStateType.Downed,
            safeDownedPosition,
            attackerClientId
        );

        sitAction?.ServerForceResetSitNetworkState();
    }

    public void ServerSetDead(
        ulong attackerClientId = NoAttackerClientId,
        Vector3 ragdollImpulse = default,
        Vector3 ragdollForcePosition = default
    )
    {
        if (!IsServer)
            return;

        if (IsBadGuy)
            return;

        if (State.Value == PlayerLifeStateType.Dead)
            return;

        if (!ServerCanChangeLifeStateDuringMatch())
            return;

        sitAction?.ServerForceResetSitNetworkState();

        Vector3 relevantPosition =
            State.Value == PlayerLifeStateType.Downed ? DownedPosition.Value : transform.position;

        if (!HasSpawnedDownedBody())
        {
            bool flashlightWasOn =
                playerFlashlight != null
                && playerFlashlight.ShouldCarryLitFlashlightToDownedBody();

            ServerSpawnOrRefreshDownedBody(
                relevantPosition,
                PlayerLifeStateType.Dead,
                flashlightWasOn,
                ragdollImpulse,
                ragdollForcePosition
            );
        }
        else
        {
            currentDownedBody.ServerSetBodyState(PlayerLifeStateType.Dead);
        }

        SetStateServer(PlayerLifeStateType.Dead, relevantPosition, attackerClientId);
    }

    public void ServerReviveFromDowned(Vector3 revivePosition)
    {
        ServerTryReviveFromDowned(revivePosition, null);
    }

    public bool ServerTryReviveFromDowned(
        Vector3 requestedRevivePosition,
        Transform reviverRoot = null,
        bool logPlacementFailure = true
    )
    {
        if (!IsServer)
            return false;

        if (State.Value != PlayerLifeStateType.Downed)
            return false;

        if (!ServerCanChangeLifeStateDuringMatch())
            return false;

        double now = GetNetworkTime();
        if (ServerHasBleedOutExpired(now))
        {
            ServerSetDead(LastAttackerClientId.Value);
            return false;
        }

        if (!HasSpawnedDownedBody())
            return false;

        if (CurrentBodyNetworkObjectId.Value != currentDownedBody.NetworkObjectId)
            return false;

        if (
            !TryFindSafeRevivePosition(
                requestedRevivePosition,
                reviverRoot,
                out Vector3 safeRevivePosition
            )
        )
        {
            if (logPlacementFailure)
            {
                Debug.LogWarning(
                    $"[PlayerLifeState] Could not find safe standing room near {name}'s revive anchor.",
                    this
                );
            }

            return false;
        }

        Quaternion safeReviveRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        ApplyServerReviveTeleport(safeRevivePosition, safeReviveRotation);
        ApplyReviveTeleportClientRpc(safeRevivePosition, safeReviveRotation);

        ServerDespawnDownedBody();

        SetStateServer(
            PlayerLifeStateType.Alive,
            safeRevivePosition,
            NoAttackerClientId
        );

        return true;
    }

    private bool TryFindSafeRevivePosition(
        Vector3 requestedRevivePosition,
        Transform reviverRoot,
        out Vector3 safeRevivePosition
    )
    {
        safeRevivePosition = requestedRevivePosition;

        CharacterController controller = GetComponent<CharacterController>();
        if (controller == null)
            return false;

        float searchRadius = Mathf.Max(0f, revivePlacementSearchRadius);
        Vector3 awayFromReviver = Vector3.zero;

        if (reviverRoot != null)
        {
            awayFromReviver = requestedRevivePosition - reviverRoot.position;
            awayFromReviver.y = 0f;

            if (awayFromReviver.sqrMagnitude > 0.0001f)
                awayFromReviver.Normalize();
        }

        if (
            TryEvaluateReviveCandidate(
                requestedRevivePosition,
                controller,
                null,
                out safeRevivePosition
            )
        )
            return true;

        if (
            TrySearchReviveCandidates(
                requestedRevivePosition,
                awayFromReviver,
                searchRadius,
                controller,
                null,
                out safeRevivePosition
            )
        )
            return true;

        // A reviver naturally stands very close to the body. As a last resort, ignore
        // only that reviver's own collider while still checking all world geometry and
        // every other player. Candidate order still prefers a point away from them.
        if (reviverRoot != null)
        {
            if (
                TrySearchReviveCandidates(
                    requestedRevivePosition,
                    awayFromReviver,
                    searchRadius,
                    controller,
                    reviverRoot,
                    out safeRevivePosition
                )
            )
                return true;

            if (
                TryEvaluateReviveCandidate(
                    requestedRevivePosition,
                    controller,
                    reviverRoot,
                    out safeRevivePosition
                )
            )
                return true;
        }

        return false;
    }

    private bool TrySearchReviveCandidates(
        Vector3 requestedRevivePosition,
        Vector3 awayFromReviver,
        float searchRadius,
        CharacterController controller,
        Transform additionalIgnoreRoot,
        out Vector3 safeRevivePosition
    )
    {
        safeRevivePosition = requestedRevivePosition;

        if (searchRadius <= 0f)
            return false;

        if (awayFromReviver.sqrMagnitude > 0.0001f)
        {
            for (int ring = 1; ring <= 2; ring++)
            {
                float ringRadius = searchRadius * (ring / 2f);
                Vector3 preferredCandidate =
                    requestedRevivePosition + awayFromReviver * ringRadius;

                if (
                    TryEvaluateReviveCandidate(
                        preferredCandidate,
                        controller,
                        additionalIgnoreRoot,
                        out safeRevivePosition
                    )
                )
                    return true;
            }
        }

        for (int ring = 1; ring <= 2; ring++)
        {
            float ringRadius = searchRadius * (ring / 2f);

            for (int i = 0; i < RevivePlacementDirections.Length; i++)
            {
                Vector2 direction = RevivePlacementDirections[i];
                Vector3 candidate = requestedRevivePosition
                    + new Vector3(direction.x, 0f, direction.y) * ringRadius;

                if (
                    TryEvaluateReviveCandidate(
                        candidate,
                        controller,
                        additionalIgnoreRoot,
                        out safeRevivePosition
                    )
                )
                    return true;
            }
        }

        return false;
    }

    private bool TryEvaluateReviveCandidate(
        Vector3 candidate,
        CharacterController controller,
        Transform additionalIgnoreRoot,
        out Vector3 safeRevivePosition
    )
    {
        safeRevivePosition = candidate;

        if (!TryFindReviveGround(candidate, out RaycastHit groundHit))
            return false;

        if (Vector3.Angle(groundHit.normal, Vector3.up) > reviveMaxGroundSlope)
            return false;

        Vector3 rootPosition = new Vector3(
            candidate.x,
            groundHit.point.y + reviveGroundOffset,
            candidate.z
        );

        Quaternion rootRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (!IsStandingCapsuleClear(rootPosition, rootRotation, controller, additionalIgnoreRoot))
            return false;

        safeRevivePosition = rootPosition;
        return true;
    }

    private bool TryFindReviveGround(Vector3 candidate, out RaycastHit groundHit)
    {
        groundHit = default;

        Vector3 origin = candidate + Vector3.up * reviveGroundProbeUp;
        float distance = reviveGroundProbeUp + reviveGroundProbeDown;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            reviveGroundHits,
            distance,
            revivePlacementMask,
            QueryTriggerInteraction.Ignore
        );

        SortHitsByDistance(reviveGroundHits, hitCount);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = reviveGroundHits[i].collider;

            if (hitCollider == null || ShouldIgnoreRevivePlacementCollider(hitCollider))
                continue;

            if (hitCollider.GetComponentInParent<PlayerLifeState>() != null)
                continue;

            groundHit = reviveGroundHits[i];
            return true;
        }

        return false;
    }

    private bool IsStandingCapsuleClear(
        Vector3 rootPosition,
        Quaternion rootRotation,
        CharacterController controller,
        Transform additionalIgnoreRoot
    )
    {
        float standingHeight =
            playerMotor != null ? playerMotor.StandingColliderHeight : controller.height;
        float standingCenterY =
            playerMotor != null ? playerMotor.StandingColliderCenterY : controller.center.y;

        float radius = Mathf.Max(0.05f, controller.radius + reviveCollisionPadding);
        float height = Mathf.Max(standingHeight, radius * 2f);

        Vector3 localCenter = new Vector3(
            controller.center.x,
            standingCenterY,
            controller.center.z
        );
        Vector3 worldCenter = rootPosition + rootRotation * localCenter;
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 bottom = worldCenter - Vector3.up * halfSegment;
        Vector3 top = worldCenter + Vector3.up * halfSegment;

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            radius,
            reviveOverlapColliders,
            revivePlacementMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = reviveOverlapColliders[i];

            if (
                overlap == null
                || ShouldIgnoreRevivePlacementCollider(overlap, additionalIgnoreRoot)
            )
                continue;

            return false;
        }

        return true;
    }

    private bool ShouldIgnoreRevivePlacementCollider(
        Collider collider,
        Transform additionalIgnoreRoot = null
    )
    {
        Transform colliderTransform = collider.transform;

        if (colliderTransform == transform || colliderTransform.IsChildOf(transform))
            return true;

        if (
            additionalIgnoreRoot != null
            && (
                colliderTransform == additionalIgnoreRoot
                || colliderTransform.IsChildOf(additionalIgnoreRoot)
            )
        )
            return true;

        DownedBodyObject body = collider.GetComponentInParent<DownedBodyObject>();
        if (body != null)
            return true;

        return false;
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

    private void ApplyServerReviveTeleport(Vector3 position, Quaternion rotation)
    {
        CharacterController controller = GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;

        if (controllerWasEnabled)
            controller.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        // On a host-owned player this instance is also the transform authority, so mark
        // the teleport as authoritative immediately. For a remote-owned player, the
        // owner ClientRpc below commits the ClientNetworkTransform teleport instead.
        if (IsClient && IsOwner)
        {
            CacheReferences();

            if (clientNetworkTransform != null)
            {
                clientNetworkTransform.TryTeleportOwnerAuthoritative(position, rotation);
            }

            reviveTeleportAppliedLocally = true;
        }
        else
        {
            // A host observing a remote-owned player must not reveal this server-side
            // transform assignment. Its ClientNetworkTransform can still replace it
            // with buffered owner snapshots before the authoritative teleport arrives.
            reviveTeleportAppliedLocally = false;
        }

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    [ClientRpc]
    private void ApplyReviveTeleportClientRpc(
        Vector3 position,
        Quaternion rotation,
        ClientRpcParams clientRpcParams = default
    )
    {
        CacheReferences();
        SetPendingReviveTeleportTarget(position, rotation);

        if (!IsOwner)
        {
            // Non-owners must not write directly to an owner-authoritative
            // ClientNetworkTransform. They remain hidden until the owner's teleport
            // snapshot reaches this replica and its actual transform has settled at
            // the destination.
            reviveTeleportAppliedLocally = false;
            return;
        }

        CharacterController controller = GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;

        if (controllerWasEnabled)
            controller.enabled = false;

        bool committedNetworkTeleport =
            clientNetworkTransform != null
            && clientNetworkTransform.TryTeleportOwnerAuthoritative(position, rotation);

        if (!committedNetworkTeleport)
        {
            // Fallback for an unexpected prefab without ClientNetworkTransform.
            transform.SetPositionAndRotation(position, rotation);
        }

        reviveTeleportAppliedLocally = true;

        if (controllerWasEnabled)
            controller.enabled = true;
    }

    public void ServerResetForRound()
    {
        if (!IsServer)
            return;

        sitAction?.ServerForceResetSitNetworkState();
        ServerDespawnDownedBody();

        SetStateServer(
            PlayerLifeStateType.Alive,
            transform.position,
            NoAttackerClientId
        );
    }

    public void ResetForRound()
    {
        CacheReferences();
        CancelDeferredReviveRestore();
        reviveTeleportAppliedLocally = false;
        ClearPendingReviveTeleportTarget();

        phone?.ForceResetPhoneLocal();
        laptopHacker?.ForceResetLocalForRound();
        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();
        bodyVisibility?.ForceShowBody();
        playerMotor?.SetControllerCollisionEnabled(true);

        if (!IsOwner)
            return;

        inputManager?.ForceClearGameplaySuppression();

        if (playerLook != null)
            playerLook.enabled = true;
    }

    private void SetStateServer(
        PlayerLifeStateType newState,
        Vector3 relevantPosition,
        ulong attackerClientId
    )
    {
        if (!IsServer)
            return;

        PlayerLifeStateType previousState = State.Value;

        DownedPosition.Value = relevantPosition;
        LastAttackerClientId.Value = attackerClientId;

        if (newState == PlayerLifeStateType.Downed)
        {
            double now = GetNetworkTime();
            DownedStartedServerTime.Value = now;
            DownedExpiresServerTime.Value = now + Math.Max(1d, bleedOutDurationSeconds);
        }
        else
        {
            DownedStartedServerTime.Value = 0d;
            DownedExpiresServerTime.Value = 0d;
        }

        State.Value = newState;

        if (previousState != newState)
            GetMatchFlowManager()?.ServerNotifyPlayerLifeStateChanged(this, previousState, newState);
    }

    public bool ServerHasBleedOutExpired(double serverTime)
    {
        if (!IsServer || State.Value != PlayerLifeStateType.Downed)
            return false;

        double expiresAt = DownedExpiresServerTime.Value;
        return expiresAt > 0d && serverTime >= expiresAt;
    }

    private bool ServerCanChangeLifeStateDuringMatch()
    {
        MatchFlowManager matchFlow = GetMatchFlowManager();
        return matchFlow == null || !matchFlow.HasCommittedMatchResult;
    }

    private MatchFlowManager GetMatchFlowManager()
    {
        if (LobbyManager.Instance != null && LobbyManager.Instance.MatchFlow != null)
            return LobbyManager.Instance.MatchFlow;

        return FindAnyObjectByType<MatchFlowManager>(FindObjectsInactive.Include);
    }

    private void ServerSpawnOrRefreshDownedBody(
        Vector3 bodyPosition,
        PlayerLifeStateType bodyState,
        bool flashlightWasOn,
        Vector3 ragdollImpulse = default,
        Vector3 ragdollForcePosition = default
    )
    {
        if (!IsServer)
            return;

        if (HasSpawnedDownedBody())
        {
            currentDownedBody.ServerSetBodyState(bodyState);
            currentDownedBody.ServerSetCarriedFlashlightOn(flashlightWasOn);
            CurrentBodyNetworkObjectId.Value = currentDownedBody.NetworkObjectId;
            return;
        }

        if (downedBodyPrefab == null)
        {
            Debug.LogWarning(
                $"[PlayerLifeState] {name} became {bodyState}, but no Downed Body Prefab is assigned."
            );
            CurrentBodyNetworkObjectId.Value = NoBodyNetworkObjectId;
            return;
        }

        Vector3 spawnPosition = bodyPosition + Vector3.up * downedBodySpawnUpOffset;
        Quaternion spawnRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        DownedBodyObject spawnedBody = Instantiate(
            downedBodyPrefab,
            spawnPosition,
            spawnRotation
        );

        NetworkObject bodyNetworkObject = spawnedBody.GetComponent<NetworkObject>();
        if (bodyNetworkObject == null)
        {
            Debug.LogError(
                "[PlayerLifeState] Downed Body Prefab must have a NetworkObject component."
            );
            Destroy(spawnedBody.gameObject);
            CurrentBodyNetworkObjectId.Value = NoBodyNetworkObjectId;
            return;
        }

        bodyNetworkObject.Spawn(true);

        int shirtIndex = playerSetup != null ? playerSetup.ShirtIndex.Value : 0;
        bool isBadGuyBody = playerSetup != null && playerSetup.IsBadGuy.Value;

        spawnedBody.InitializeServer(
            OwnerClientId,
            NetworkObjectId,
            bodyState,
            shirtIndex,
            isBadGuyBody,
            flashlightWasOn
        );

        currentDownedBody = spawnedBody;
        CurrentBodyNetworkObjectId.Value = bodyNetworkObject.NetworkObjectId;

        spawnedBody.ServerCopyPoseAndActivateRagdollFromSource(
            NetworkObject,
            ragdollImpulse,
            ragdollForcePosition
        );
    }

    private bool HasSpawnedDownedBody()
    {
        return currentDownedBody != null
            && currentDownedBody.NetworkObject != null
            && currentDownedBody.NetworkObject.IsSpawned;
    }

    private void ServerDespawnDownedBody()
    {
        if (!IsServer)
            return;

        if (HasSpawnedDownedBody())
            currentDownedBody.NetworkObject.Despawn(true);

        currentDownedBody = null;
        CurrentBodyNetworkObjectId.Value = NoBodyNetworkObjectId;
    }

    [ServerRpc]
    private void DebugRequestSetStateServerRpc(
        PlayerLifeStateType requestedState,
        Vector3 requestedPosition,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (!enableDebugHotkeys)
            return;

        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        switch (requestedState)
        {
            case PlayerLifeStateType.Alive:
                ServerSetAlive();
                break;

            case PlayerLifeStateType.Downed:
                ServerSetDowned(requestedPosition, NoAttackerClientId);
                break;

            case PlayerLifeStateType.Dead:
                ServerSetDead(NoAttackerClientId);
                break;
        }
    }

    private double GetNetworkTime()
    {
        if (NetworkManager.Singleton == null)
            return 0d;

        return NetworkManager.Singleton.ServerTime.Time;
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerBadGuyAttack : NetworkBehaviour, IPlayerRoundResettable
{
    private const float AttackRange = 7f;

    [Header("References")]
    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private PlayerLifeState lifeState;

    [Tooltip("Optional. If empty, the owner camera is used for attack direction.")]
    [SerializeField]
    private Transform attackOriginOverride;

    [Header("Attack")]
    [Tooltip("Half-width of the melee attack area. Bigger = easier to hit left/right.")]
    [SerializeField]
    private float attackRadius = 0.75f;

    [Tooltip("Vertical height of the melee attack area. Should cover sitting/crouching/standing players.")]
    [SerializeField]
    private float attackHeight = 2.0f;

    [Tooltip("Vertical center of the attack area above the bad guy root.")]
    [SerializeField]
    private float attackCenterHeight = 1.0f;

    [SerializeField]
    private float attackCooldownSeconds = 1.0f;

    [Tooltip("Layers the melee attack can hit. Usually leave as Everything for now.")]
    [SerializeField]
    private LayerMask attackHitMask = ~0;

    [Tooltip("How far the client-reported attack origin may be from the server player position before it is ignored.")]
    [SerializeField]
    private float maxClientOriginDistanceFromServer = 2.5f;

    private readonly Collider[] _hitBuffer = new Collider[64];
    private readonly HashSet<PlayerLifeState> _seenTargets = new HashSet<PlayerLifeState>();

    private Camera _ownerCamera;
    private double _nextServerAttackTime;
    private float _nextLocalAttackTime;

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

    private void CacheReferences()
    {
        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();
    }

    public void TryAttack()
    {
        if (!IsOwner)
            return;

        CacheReferences();

        if (!CanAttackLocally())
            return;

        if (Time.unscaledTime < _nextLocalAttackTime)
            return;

        _nextLocalAttackTime = Time.unscaledTime + attackCooldownSeconds;

        GetLocalAttackPose(out Vector3 origin, out Vector3 forward);
        RequestAttackServerRpc(origin, forward);
    }

    private bool CanAttackLocally()
    {
        if (lifeState != null)
            return lifeState.CanAttackSurvivors;

        return playerSetup != null && playerSetup.IsBadGuy.Value;
    }

    private bool CanAttackOnServer(ulong senderClientId)
    {
        if (senderClientId != OwnerClientId)
            return false;

        if (lifeState != null)
            return lifeState.CanAttackSurvivors;

        return playerSetup != null && playerSetup.IsBadGuy.Value;
    }

    private void GetLocalAttackPose(out Vector3 origin, out Vector3 forward)
    {
        Transform originTransform = attackOriginOverride;

        if (originTransform == null)
        {
            if (_ownerCamera == null)
                _ownerCamera = GetComponentInChildren<Camera>(true);

            if (_ownerCamera != null)
                originTransform = _ownerCamera.transform;
        }

        if (originTransform != null)
        {
            origin = originTransform.position;
            forward = originTransform.forward;
            return;
        }

        origin = transform.position + Vector3.up * attackCenterHeight;
        forward = transform.forward;
    }

    [ServerRpc]
    private void RequestAttackServerRpc(
        Vector3 clientOrigin,
        Vector3 clientForward,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (!CanAttackOnServer(serverRpcParams.Receive.SenderClientId))
            return;

        double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

        if (now < _nextServerAttackTime)
            return;

        _nextServerAttackTime = now + attackCooldownSeconds;

        Vector3 attackForward = GetHorizontalForward(clientForward);
        Vector3 attackCenter = GetServerAttackCenter(clientOrigin, attackForward);
        Quaternion attackRotation = Quaternion.LookRotation(attackForward, Vector3.up);

        PlayerLifeState target = FindBestAttackTarget(attackCenter, attackForward, attackRotation);

        if (target == null)
            return;

        target.ServerSetDowned(target.transform.position, OwnerClientId);
    }

    private Vector3 GetHorizontalForward(Vector3 requestedForward)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(requestedForward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = transform.forward;

        return flatForward.normalized;
    }

    private Vector3 GetServerAttackCenter(Vector3 clientOrigin, Vector3 attackForward)
    {
        Vector3 expectedClientOrigin = transform.position + Vector3.up * attackCenterHeight;

        float maxSqrDistance =
            maxClientOriginDistanceFromServer * maxClientOriginDistanceFromServer;

        bool clientOriginLooksValid =
            (clientOrigin - expectedClientOrigin).sqrMagnitude <= maxSqrDistance;

        Vector3 basePosition = clientOriginLooksValid
            ? new Vector3(clientOrigin.x, transform.position.y, clientOrigin.z)
            : transform.position;

        return basePosition
            + Vector3.up * attackCenterHeight
            + attackForward * (AttackRange * 0.5f);
    }

    private PlayerLifeState FindBestAttackTarget(
        Vector3 attackCenter,
        Vector3 attackForward,
        Quaternion attackRotation
    )
    {
        _seenTargets.Clear();

        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.05f, attackRadius),
            Mathf.Max(0.1f, attackHeight * 0.5f),
            AttackRange * 0.5f
        );

        int hitCount = Physics.OverlapBoxNonAlloc(
            attackCenter,
            halfExtents,
            _hitBuffer,
            attackRotation,
            attackHitMask,
            QueryTriggerInteraction.Ignore
        );

        PlayerLifeState bestTarget = null;
        float bestScore = float.MaxValue;

        Vector3 attackStart = attackCenter - attackForward * (AttackRange * 0.5f);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];

            if (hit == null)
                continue;

            PlayerLifeState candidateLifeState = hit.GetComponentInParent<PlayerLifeState>();

            if (candidateLifeState == null)
                continue;

            if (!_seenTargets.Add(candidateLifeState))
                continue;

            if (candidateLifeState == lifeState)
                continue;

            if (candidateLifeState.OwnerClientId == OwnerClientId)
                continue;

            if (!candidateLifeState.CanBeAttacked)
                continue;

            if (!ScoreTarget(hit, attackStart, attackForward, out float score))
                continue;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestTarget = candidateLifeState;
        }

        return bestTarget;
    }

    private bool ScoreTarget(
        Collider targetCollider,
        Vector3 attackStart,
        Vector3 attackForward,
        out float score
    )
    {
        score = float.MaxValue;

        Vector3 targetCenter = targetCollider.bounds.center;
        Vector3 toTarget = targetCenter - attackStart;

        float forwardDistance = Vector3.Dot(toTarget, attackForward);

        if (forwardDistance < -attackRadius)
            return false;

        if (forwardDistance > AttackRange + attackRadius)
            return false;

        Vector3 closestOnAttackLine =
            attackStart + attackForward * Mathf.Clamp(forwardDistance, 0f, AttackRange);

        Vector2 targetXZ = new Vector2(targetCenter.x, targetCenter.z);
        Vector2 lineXZ = new Vector2(closestOnAttackLine.x, closestOnAttackLine.z);

        float sideDistance = Vector2.Distance(targetXZ, lineXZ);

        score = forwardDistance + sideDistance * 0.5f;
        return true;
    }

    public void ResetForRound()
    {
        _nextLocalAttackTime = 0f;
        _nextServerAttackTime = 0d;
        _ownerCamera = null;
        _seenTargets.Clear();
    }
}
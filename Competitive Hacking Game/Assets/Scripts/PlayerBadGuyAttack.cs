using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerBadGuyAttack : NetworkBehaviour, IPlayerRoundResettable
{
    [Header("References")]
    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private PlayerLifeState lifeState;

    [Tooltip("Required. Assign the real PlayerCamera transform here.")]
    [SerializeField]
    private Transform attackOriginOverride;

    [Header("Attack")]
    [Tooltip("Forgiveness around the aim ray. 0.35-0.6 is usually good.")]

    [SerializeField]
    private float  AttackRange = 1.5f;

    [SerializeField]
    private float attackRadius = 0.45f;

    [SerializeField]
    private float attackCooldownSeconds = 1.0f;

    [Tooltip("Layers the attack can hit. Usually leave as Everything for now.")]
    [SerializeField]
    private LayerMask attackHitMask = ~0;

    [Tooltip("How far the assigned attack origin may be from the server player position before the attack is rejected.")]
    [SerializeField]
    private float maxAttackOriginDistanceFromServer = 2.5f;

    [Header("Ragdoll Impulse")]
    [SerializeField]
    private float ragdollImpulseStrength = 4.5f;

    [SerializeField]
    private float ragdollUpwardImpulse = 0.75f;

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[64];

    private double _nextServerAttackTime;
    private float _nextLocalAttackTime;
    private bool _warnedMissingAttackOrigin;

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

        if (attackOriginOverride == null)
        {
            WarnMissingAttackOriginOnce();
            return;
        }

        if (Time.unscaledTime < _nextLocalAttackTime)
            return;

        _nextLocalAttackTime = Time.unscaledTime + attackCooldownSeconds;

        Vector3 origin = attackOriginOverride.position;
        Vector3 forward = attackOriginOverride.forward;

        RequestAttackServerRpc(origin, forward);
    }

    private void WarnMissingAttackOriginOnce()
    {
        if (_warnedMissingAttackOrigin)
            return;

        _warnedMissingAttackOrigin = true;

        Debug.LogWarning(
            "[PlayerBadGuyAttack] Attack Origin Override is not assigned. Drag the real PlayerCamera transform into this field on the player prefab.",
            this
        );
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

    [ServerRpc]
    private void RequestAttackServerRpc(
        Vector3 clientOrigin,
        Vector3 clientForward,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (!CanAttackOnServer(serverRpcParams.Receive.SenderClientId))
            return;

        double now = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ServerTime.Time
            : 0d;

        if (now < _nextServerAttackTime)
            return;

        _nextServerAttackTime = now + attackCooldownSeconds;

        if (!IsClientOriginValid(clientOrigin))
            return;

        Vector3 attackForward = GetSafeForward(clientForward);

        PlayerLifeState target = FindFirstValidTarget(
            clientOrigin,
            attackForward,
            out RaycastHit targetHit
        );

        if (target == null)
            return;

        Vector3 impulse =
            attackForward.normalized * ragdollImpulseStrength
            + Vector3.up * ragdollUpwardImpulse;

        Vector3 forcePosition = targetHit.collider != null
            ? targetHit.point
            : target.transform.position + Vector3.up;

        target.ServerSetDowned(
            target.transform.position,
            OwnerClientId,
            impulse,
            forcePosition
        );
    }

    private bool IsClientOriginValid(Vector3 clientOrigin)
    {
        float maxSqrDistance =
            maxAttackOriginDistanceFromServer * maxAttackOriginDistanceFromServer;

        return (clientOrigin - transform.position).sqrMagnitude <= maxSqrDistance;
    }

    private Vector3 GetSafeForward(Vector3 requestedForward)
    {
        if (requestedForward.sqrMagnitude < 0.001f)
            return transform.forward;

        return requestedForward.normalized;
    }

    private PlayerLifeState FindFirstValidTarget(
        Vector3 origin,
        Vector3 forward,
        out RaycastHit targetHit
    )
    {
        targetHit = default;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            Mathf.Max(0.01f, attackRadius),
            forward,
            _hitBuffer,
            AttackRange,
            attackHitMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0)
            return null;

        SortHitsByDistance(hitCount);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];

            if (hit.collider == null)
                continue;

            PlayerLifeState candidate = hit.collider.GetComponentInParent<PlayerLifeState>();

            if (!IsValidTarget(candidate))
                continue;

            targetHit = hit;
            return candidate;
        }

        return null;
    }

    private void SortHitsByDistance(int hitCount)
    {
        for (int i = 0; i < hitCount - 1; i++)
        {
            int bestIndex = i;
            float bestDistance = _hitBuffer[i].distance;

            for (int j = i + 1; j < hitCount; j++)
            {
                if (_hitBuffer[j].distance >= bestDistance)
                    continue;

                bestIndex = j;
                bestDistance = _hitBuffer[j].distance;
            }

            if (bestIndex == i)
                continue;

            RaycastHit temp = _hitBuffer[i];
            _hitBuffer[i] = _hitBuffer[bestIndex];
            _hitBuffer[bestIndex] = temp;
        }
    }

    private bool IsValidTarget(PlayerLifeState candidate)
    {
        if (candidate == null)
            return false;

        if (candidate == lifeState)
            return false;

        if (candidate.OwnerClientId == OwnerClientId)
            return false;

        if (!candidate.CanBeAttacked)
            return false;

        return true;
    }

    public void ResetForRound()
    {
        _nextLocalAttackTime = 0f;
        _nextServerAttackTime = 0d;
        _warnedMissingAttackOrigin = false;
    }
}
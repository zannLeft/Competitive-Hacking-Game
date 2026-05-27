using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLaptopHacker : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private PlayerSitAction sitAction;

    [Tooltip("Usually the player camera or player root. Used for router distance checks.")]
    [SerializeField]
    private Transform rangeOrigin;

    [Header("Hack Rules")]
    [SerializeField]
    private float hackSeconds = 2.5f;

    [SerializeField]
    private int barCount = 5;

    [SerializeField]
    private int requiredBars = 5;

    [Tooltip("If ON, hacking only works once the laptop is actually open/focused.")]
    [SerializeField]
    private bool requireLaptopFocus = true;

    [Header("Target Refresh")]
    [SerializeField]
    private float targetRefreshInterval = 0.10f;

    private bool _hackHeld;
    private bool _mustReleaseHack;
    private float _hackProgress;
    private float _targetRefreshTimer;

    private RouterBox _currentTarget;

    public RouterBox CurrentTarget => _currentTarget;
    public string CurrentTargetName => _currentTarget != null ? _currentTarget.NetworkName : "";
    public float HackProgress01 => hackSeconds <= 0f ? 0f : Mathf.Clamp01(_hackProgress / hackSeconds);
    public bool IsHoldingHack => _hackHeld;
    public bool HasHackableTarget => IsLaptopUsable && _currentTarget != null;

    public Vector3 SignalOriginPosition
    {
        get
        {
            if (rangeOrigin != null)
                return rangeOrigin.position;

            return transform.position;
        }
    }

    public bool IsLaptopUsable
    {
        get
        {
            if (sitAction == null)
                return false;

            if (!requireLaptopFocus)
                return sitAction.IsSittingOrTransitioning;

            return sitAction.LaptopCameraFocus;
        }
    }

    void Reset()
    {
        sitAction = GetComponent<PlayerSitAction>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (rangeOrigin == null)
        {
            var look = GetComponent<PlayerLook>();
            if (look != null && look.cam != null)
                rangeOrigin = look.cam.transform;
            else
                rangeOrigin = transform;
        }

        if (IsServer)
            SyncCompletedNetworksToOwner();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        RefreshTargetTick();

        if (!IsLaptopUsable || _currentTarget == null)
        {
            ResetHackProgress();
            return;
        }

        if (!_hackHeld || _mustReleaseHack)
        {
            _hackProgress = 0f;
            return;
        }

        _hackProgress += Time.deltaTime;

        if (_hackProgress >= hackSeconds)
        {
            string completedName = _currentTarget.NetworkName;

            _hackProgress = 0f;
            _mustReleaseHack = true;

            RequestCompleteHackServerRpc(new FixedString128Bytes(completedName));
        }
    }

    public void SetHackHeld(bool held)
    {
        _hackHeld = held;

        if (!held)
        {
            _mustReleaseHack = false;
            _hackProgress = 0f;
        }
    }

    private void RefreshTargetTick()
    {
        _targetRefreshTimer -= Time.deltaTime;

        if (_targetRefreshTimer > 0f)
            return;

        _targetRefreshTimer = targetRefreshInterval;
        _currentTarget = FindBestHackableRouter();
    }

    private RouterBox FindBestHackableRouter()
    {
        if (!IsLaptopUsable)
            return null;

        RouterBox best = null;
        float bestStrength = -1f;

        Vector3 fromPos = SignalOriginPosition;
        var routers = RouterRegistry.Routers;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];

            if (router == null)
                continue;

            if (RouterHackState.IsCompleted(router.NetworkName))
                continue;

            float strength = router.GetStrength01(fromPos);

            if (!HasRequiredBars(strength))
                continue;

            if (strength > bestStrength)
            {
                bestStrength = strength;
                best = router;
            }
        }

        return best;
    }

    private bool HasRequiredBars(float strength01)
    {
        int bars = Mathf.RoundToInt(Mathf.Clamp01(strength01) * barCount);
        bars = Mathf.Clamp(bars, 0, barCount);

        return bars >= requiredBars;
    }

    private void ResetHackProgress()
    {
        _hackProgress = 0f;
        _mustReleaseHack = false;
    }

    [ServerRpc]
    private void RequestCompleteHackServerRpc(FixedString128Bytes networkName)
    {
        string name = networkName.ToString();

        if (RouterHackState.IsCompleted(name))
            return;

        RouterBox router = FindRouterByName(name);

        if (router == null)
        {
            Debug.LogWarning($"[Hack] Router '{name}' not found on server.");
            return;
        }

        // Basic server validation.
        float strength = router.GetStrength01(transform.position);

        if (!HasRequiredBars(strength))
        {
            Debug.LogWarning($"[Hack] Client tried to hack '{name}' without enough signal.");
            return;
        }

        RouterHackState.MarkCompleted(name);
        MarkNetworkCompletedClientRpc(networkName);
    }

    [ClientRpc]
    private void MarkNetworkCompletedClientRpc(FixedString128Bytes networkName)
    {
        RouterHackState.MarkCompleted(networkName.ToString());
    }

    private RouterBox FindRouterByName(string networkName)
    {
        var routers = RouterRegistry.Routers;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];

            if (router == null)
                continue;

            if (router.NetworkName == networkName)
                return router;
        }

        return null;
    }

    private void SyncCompletedNetworksToOwner()
    {
        var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId },
            },
        };

        foreach (string completed in RouterHackState.CompletedNetworks)
            SyncCompletedNetworkClientRpc(new FixedString128Bytes(completed), targetParams);
    }

    [ClientRpc]
    private void SyncCompletedNetworkClientRpc(
        FixedString128Bytes networkName,
        ClientRpcParams clientRpcParams = default
    )
    {
        RouterHackState.MarkCompleted(networkName.ToString());
    }
}
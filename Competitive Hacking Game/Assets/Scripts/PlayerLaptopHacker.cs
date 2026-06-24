using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLaptopHacker : NetworkBehaviour, IPlayerRoundResettable
{
    [Header("Refs")]
    [SerializeField]
    private PlayerSitAction sitAction;

    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private PlayerLifeState lifeState;

    [SerializeField]
    private LaptopScreenUI screenUI;

    [Tooltip("Usually the player camera or player root. Used for local router signal checks.")]
    [SerializeField]
    private Transform rangeOrigin;

    [Header("Signal Rules")]
    [SerializeField]
    private int barCount = 5;

    [SerializeField]
    private int requiredBars = 5;

    [Tooltip("If ON, the minigame appears only after the laptop focus animation event.")]
    [SerializeField]
    private bool requireLaptopFocus = true;

    [Header("Target Refresh")]
    [SerializeField, Min(0.02f)]
    private float targetRefreshInterval = 0.10f;

    private RouterHackCoordinator _coordinator;
    private RouterBox _currentTarget;
    private RouterHackRecord _currentRecord;
    private bool _targetLockedByMinigame;
    private bool _showingTerminalResult;
    private float _targetRefreshTimer;
    private string _lastPresentationKey = string.Empty;
    private Vector2 _navigationInput;
    private bool _primaryHeld;

    public RouterBox CurrentTarget => _currentTarget;
    public string CurrentTargetName =>
        _currentTarget != null ? _currentTarget.NetworkName : string.Empty;
    public string CurrentNetworkId =>
        _currentTarget != null ? _currentTarget.NetworkId : string.Empty;
    public bool HasHackableTarget => IsLaptopUsable && _currentTarget != null;
    public bool HasActiveMinigame =>
        _targetLockedByMinigame
        && screenUI != null
        && screenUI.HasActiveMinigame;

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
            if (!CanUseSurvivorTools())
                return false;

            if (sitAction == null)
                return false;

            if (!requireLaptopFocus)
                return sitAction.IsSittingOrTransitioning;

            return sitAction.LaptopCameraFocus;
        }
    }

    private void Reset()
    {
        sitAction = GetComponent<PlayerSitAction>();
        playerSetup = GetComponent<PlayerSetup>();
        lifeState = GetComponent<PlayerLifeState>();
        screenUI = GetComponentInChildren<LaptopScreenUI>(true);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (screenUI == null)
            screenUI = GetComponentInChildren<LaptopScreenUI>(true);

        if (rangeOrigin == null)
        {
            PlayerLook look = GetComponent<PlayerLook>();

            if (look != null && look.cam != null)
                rangeOrigin = look.cam.transform;
            else
                rangeOrigin = transform;
        }

        ForceResetLocalForRound();
        AttachCoordinatorIfNeeded();
    }

    public override void OnNetworkDespawn()
    {
        DetachCoordinator();
        AbortLocalMinigame(clearTarget: true);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        AttachCoordinatorIfNeeded();

        if (!CanUseSurvivorTools() || !IsLaptopUsable)
        {
            AbortLocalMinigame(clearTarget: true);
            _showingTerminalResult = false;
            _lastPresentationKey = string.Empty;
            return;
        }

        if (_showingTerminalResult)
            return;

        if (_coordinator == null || !_coordinator.AssignmentsReady)
        {
            AbortLocalMinigame(clearTarget: true);
            PresentOnce("waiting", () => screenUI?.ShowWaitingForAssignments());
            return;
        }

        if (_targetLockedByMinigame)
        {
            if (!IsCurrentTargetStillEligible())
            {
                AbortLocalMinigame(clearTarget: true);
                PresentOnce("no-network", () => screenUI?.ShowNoNetwork());
            }

            return;
        }

        _targetRefreshTimer -= Time.deltaTime;

        if (_targetRefreshTimer > 0f)
            return;

        _targetRefreshTimer = Mathf.Max(0.02f, targetRefreshInterval);
        RefreshAndPresentTarget();
    }

    public void ResetForRound()
    {
        ForceResetLocalForRound();
    }

    public void ForceResetLocalForRound()
    {
        AbortLocalMinigame(clearTarget: true);
        _showingTerminalResult = false;
        _targetRefreshTimer = 0f;
        _lastPresentationKey = string.Empty;
        _navigationInput = Vector2.zero;
        _primaryHeld = false;
    }

    public void SetNavigationInput(Vector2 input)
    {
        if (!IsOwner)
            return;

        _navigationInput = Vector2.ClampMagnitude(input, 1f);

        if (HasActiveMinigame)
            screenUI.SetNavigation(_navigationInput);
    }

    public void PrimaryPressed()
    {
        if (!IsOwner || !HasActiveMinigame || _primaryHeld)
            return;

        _primaryHeld = true;
        screenUI.PrimaryPressed();
    }

    public void PrimaryReleased()
    {
        if (!IsOwner || !_primaryHeld)
            return;

        _primaryHeld = false;

        if (screenUI != null)
            screenUI.PrimaryReleased();
    }

    public void ClearLocalInputState()
    {
        if (!IsOwner)
            return;

        _navigationInput = Vector2.zero;

        if (screenUI != null)
            screenUI.SetNavigation(Vector2.zero);

        PrimaryReleased();
    }

    // Compatibility for existing cleanup code. The old hold-to-hack system no longer uses this.
    public void SetHackHeld(bool held)
    {
        if (held)
            PrimaryPressed();
        else
            PrimaryReleased();
    }

    private void RefreshAndPresentTarget()
    {
        RouterBox bestTarget = FindBestHackableRouter();

        if (bestTarget == null)
        {
            ClearCurrentTarget();
            PresentOnce("no-network", () => screenUI?.ShowNoNetwork());
            return;
        }

        _currentTarget = bestTarget;

        if (!_coordinator.TryGetRecord(bestTarget.NetworkId, out _currentRecord))
        {
            PresentOnce(
                $"missing-record:{bestTarget.NetworkId}",
                () => screenUI?.ShowMissingAssignment(bestTarget.NetworkName)
            );
            return;
        }

        if (_currentRecord.Completed)
        {
            PresentOnce(
                $"completed:{bestTarget.NetworkId}",
                () => screenUI?.ShowCompletedElsewhere(bestTarget.NetworkName)
            );
            return;
        }

        if (
            !_coordinator.TryGetMinigameDefinition(
                _currentRecord.MinigameId,
                out LaptopMinigameDefinition definition
            )
        )
        {
            PresentOnce(
                $"missing-definition:{_currentRecord.MinigameId}",
                () =>
                    screenUI?.ShowMissingDefinition(
                        bestTarget.NetworkName,
                        _currentRecord.MinigameId
                    )
            );
            return;
        }

        LaptopMinigameContext context = new(
            bestTarget.NetworkId,
            bestTarget.NetworkName,
            _currentRecord.MinigameId,
            definition.DisplayName,
            _currentRecord.Difficulty,
            _currentRecord.Seed
        );

        string presentationKey =
            $"minigame:{bestTarget.NetworkId}:{_currentRecord.MinigameId}:"
            + $"{_currentRecord.Difficulty}:{_currentRecord.Seed}";

        if (_lastPresentationKey == presentationKey)
            return;

        _lastPresentationKey = presentationKey;

        if (screenUI == null)
        {
            Debug.LogError(
                "[PlayerLaptopHacker] No LaptopScreenUI is assigned or present beneath the player.",
                this
            );
            return;
        }

        bool started = screenUI.TryStartMinigame(
            definition,
            context,
            OnLocalMinigameCompleted,
            OnLocalMinigameFailed,
            OnLocalMinigameAlarmTriggered
        );

        _targetLockedByMinigame = started;

        if (started)
        {
            screenUI.SetNavigation(_navigationInput);

            if (_primaryHeld)
                screenUI.PrimaryPressed();
        }
    }

    private RouterBox FindBestHackableRouter()
    {
        if (!CanUseSurvivorTools() || !IsLaptopUsable || _coordinator == null)
            return null;

        RouterBox best = null;
        float bestStrength = -1f;
        Vector3 fromPosition = SignalOriginPosition;
        var routers = RouterRegistry.Routers;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];

            if (router == null)
                continue;

            if (!_coordinator.TryGetRecord(router.NetworkId, out RouterHackRecord record))
                continue;

            if (record.Completed)
                continue;

            float strength = router.GetStrength01(fromPosition);

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

    private bool IsCurrentTargetStillEligible()
    {
        if (_currentTarget == null || _coordinator == null)
            return false;

        if (!_coordinator.TryGetRecord(_currentTarget.NetworkId, out RouterHackRecord record))
            return false;

        if (record.Completed)
            return false;

        float strength = _currentTarget.GetStrength01(SignalOriginPosition);
        return HasRequiredBars(strength);
    }

    private bool HasRequiredBars(float strength01)
    {
        int safeBarCount = Mathf.Max(1, barCount);
        int bars = Mathf.RoundToInt(Mathf.Clamp01(strength01) * safeBarCount);
        bars = Mathf.Clamp(bars, 0, safeBarCount);
        int safeRequiredBars = Mathf.Clamp(requiredBars, 0, safeBarCount);
        return bars >= safeRequiredBars;
    }

    private void OnLocalMinigameCompleted()
    {
        if (!IsOwner)
            return;

        string displayName = CurrentTargetName;
        _targetLockedByMinigame = false;
        _showingTerminalResult = true;
        ClearLocalInputState();
        screenUI?.ShowSuccess(displayName);

        Debug.Log(
            $"[PlayerLaptopHacker] Local minigame completed for '{CurrentNetworkId}'. "
                + "Server completion will be connected in the authoritative stage.",
            this
        );
    }

    private void OnLocalMinigameFailed()
    {
        if (!IsOwner)
            return;

        string displayName = CurrentTargetName;
        _targetLockedByMinigame = false;
        _showingTerminalResult = true;
        ClearLocalInputState();
        screenUI?.ShowFailure(displayName);

        Debug.Log(
            $"[PlayerLaptopHacker] Local minigame failed for '{CurrentNetworkId}'. "
                + "The networked alarm will be connected in the audio/validation stage.",
            this
        );
    }

    private void OnLocalMinigameAlarmTriggered()
    {
        if (!IsOwner)
            return;

        Debug.Log(
            $"[PlayerLaptopHacker] Local minigame alarm triggered for '{CurrentNetworkId}'. "
                + "The positional networked sound will be connected in Stage 4.",
            this
        );
    }

    private void AbortLocalMinigame(bool clearTarget)
    {
        ClearLocalInputState();
        screenUI?.AbortActiveMinigame();
        _targetLockedByMinigame = false;

        if (clearTarget)
            ClearCurrentTarget();
    }

    private void ClearCurrentTarget()
    {
        _currentTarget = null;
        _currentRecord = default;
        _targetLockedByMinigame = false;
    }

    private void AttachCoordinatorIfNeeded()
    {
        RouterHackCoordinator instance = RouterHackCoordinator.Instance;

        if (_coordinator == instance)
            return;

        DetachCoordinator();
        _coordinator = instance;

        if (_coordinator != null)
            _coordinator.RecordsChanged += OnCoordinatorRecordsChanged;

        _targetRefreshTimer = 0f;
        _lastPresentationKey = string.Empty;
    }

    private void DetachCoordinator()
    {
        if (_coordinator != null)
            _coordinator.RecordsChanged -= OnCoordinatorRecordsChanged;

        _coordinator = null;
    }

    private void OnCoordinatorRecordsChanged()
    {
        if (!IsOwner)
            return;

        _targetRefreshTimer = 0f;
        _lastPresentationKey = string.Empty;

        if (
            _currentTarget != null
            && _coordinator != null
            && _coordinator.IsCompleted(_currentTarget.NetworkId)
        )
        {
            string displayName = _currentTarget.NetworkName;
            AbortLocalMinigame(clearTarget: true);
            _showingTerminalResult = true;
            screenUI?.ShowCompletedElsewhere(displayName);
        }
    }

    private void PresentOnce(string key, System.Action presentation)
    {
        if (_lastPresentationKey == key)
            return;

        _lastPresentationKey = key;
        presentation?.Invoke();
    }

    private bool CanUseSurvivorTools()
    {
        if (lifeState != null)
            return lifeState.CanUseSurvivorTools;

        return !IsBadGuy();
    }

    private bool IsBadGuy()
    {
        return playerSetup != null && playerSetup.IsBadGuy.Value;
    }
}

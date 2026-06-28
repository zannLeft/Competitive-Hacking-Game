using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLaptopHacker : NetworkBehaviour, IPlayerRoundResettable
{
    private enum AttemptRejectReason : byte
    {
        None = 0,
        InvalidOwner = 1,
        PlayerUnavailable = 2,
        NotSitting = 3,
        AssignmentsUnavailable = 4,
        AssignmentMismatch = 5,
        AlreadyCompleted = 6,
        OutOfRange = 7,
        NoActiveAttempt = 8,
        AttemptMismatch = 9,
        CompletedTooQuickly = 10,
        CompletionFailed = 11,
    }

    [Header("Refs")]
    [SerializeField]
    private PlayerSitAction sitAction;

    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private PlayerLifeState lifeState;

    [SerializeField]
    private LaptopScreenUI screenUI;

    [Header("Owner HUD")]
    [Tooltip("Normal gameplay HUD root. Leave empty to find the scene object named GameUI automatically.")]
    [SerializeField]
    private GameObject gameUIRoot;

    [SerializeField]
    private string gameUIRootName = "GameUI";

    [Tooltip("Hide the normal gameplay HUD while the laptop focus animation state is active.")]
    [SerializeField]
    private bool hideGameUIWhileLaptopFocused = true;

    [Header("Signal Rules")]
    [SerializeField]
    private int barCount = 5;

    [SerializeField]
    private int requiredBars = 5;

    [Tooltip("If ON, the minigame appears only after the laptop focus animation event.")]
    [SerializeField]
    private bool requireLaptopFocus = true;

    [Header("Laptop Minigame Performance")]
    [Tooltip("Prepare and pool the catalog minigames in the background before the laptop is opened.")]
    [SerializeField]
    private bool prewarmMinigames = true;

    [Tooltip("How many heavy UI cells a minigame may build before yielding to the next frame.")]
    [SerializeField, Range(1, 32)]
    private int prewarmOperationsPerFrame = 9;

    [Header("Target Refresh")]
    [SerializeField, Min(0.02f)]
    private float targetRefreshInterval = 0.10f;

    [Header("Networked Keypress Audio")]
    [Tooltip("Optional always-active transform for the positional sound. Leave empty to use the player root.")]
    [SerializeField]
    private Transform audioOrigin;

    [Tooltip("Optional preconfigured source, useful when routing through an Audio Mixer. A source is created automatically when empty.")]
    [SerializeField]
    private AudioSource keypressAudioSource;

    [Tooltip("Optional preconfigured source, useful when routing through an Audio Mixer. A source is created automatically when empty.")]
    [SerializeField]
    private AudioSource alarmAudioSource;

    [SerializeField]
    private AudioClip[] keypressClips = Array.Empty<AudioClip>();

    [SerializeField]
    private AudioClip alarmClip;

    [SerializeField, Range(0f, 1f)]
    private float keypressVolume = 0.55f;

    [SerializeField, Range(0.5f, 2f)]
    private float keypressPitchMin = 0.96f;

    [SerializeField, Range(0.5f, 2f)]
    private float keypressPitchMax = 1.04f;

    [SerializeField, Min(0.01f)]
    private float keypressMinDistance = 1.25f;

    [SerializeField, Min(0.02f)]
    private float keypressMaxDistance = 14f;

    [SerializeField]
    private AudioRolloffMode keypressRolloff = AudioRolloffMode.Logarithmic;

    [Header("Networked Failure Alarm")]
    [SerializeField, Range(0f, 1f)]
    private float alarmVolume = 1f;

    [Tooltip("Large minimum distance keeps the alarm loud throughout most of the office.")]
    [SerializeField, Min(0.01f)]
    private float alarmMinDistance = 18f;

    [Tooltip("Set this beyond the longest expected office dimension.")]
    [SerializeField, Min(0.02f)]
    private float alarmMaxDistance = 140f;

    [SerializeField]
    private AudioRolloffMode alarmRolloff = AudioRolloffMode.Linear;

    [Header("Server Validation / Rate Limits")]
    [SerializeField, Min(0.01f)]
    private float serverAttemptValidationInterval = 0.25f;

    [SerializeField, Min(0f)]
    private float minimumNetworkedKeypressInterval = 0.04f;

    [SerializeField, Min(0f)]
    private float minimumNetworkedAlarmInterval = 0.50f;

    [SerializeField, Min(0f)]
    private float rejectedMessageSeconds = 1.10f;

    private RouterHackCoordinator _coordinator;
    private RouterBox _currentTarget;
    private RouterHackRecord _currentRecord;
    private LaptopMinigameContext _activeContext;
    private bool _hasActiveContext;
    private bool _targetLockedByMinigame;
    private bool _clientAttemptOpen;
    private bool _completionPending;
    private bool _showingTerminalResult;
    private string _pendingCompletionNetworkId = string.Empty;
    private string _pendingCompletionDisplayName = string.Empty;
    private float _targetRefreshTimer;
    private string _lastPresentationKey = string.Empty;
    private Vector2 _navigationInput;
    private bool _jumpHeld;
    private bool _interactHeld;
    private ushort _localActionSequence;
    private Coroutine _rejectedMessageCoroutine;
    private bool _gameUIHiddenByLaptop;
    private bool _gameUIWasActiveBeforeLaptop;
    private Coroutine _minigamePrewarmRoutine;
    private LaptopMinigameCatalog _prewarmedCatalog;

    // Server-only attempt state. This lives on the player's existing NetworkObject.
    private bool _serverAttemptActive;
    private FixedString128Bytes _serverAttemptNetworkId;
    private ushort _serverAttemptMinigameId;
    private LaptopMinigameDifficulty _serverAttemptDifficulty;
    private int _serverAttemptSeed;
    private double _serverAttemptStartedAt;
    private double _serverLastKeypressAt = double.NegativeInfinity;
    private double _serverLastAlarmAt = double.NegativeInfinity;
    private float _serverAttemptValidationTimer;

    // Preserves the server-side elapsed-time validation when a resumable minigame is
    // paused by closing the laptop. Without this, reopening on round 2 or 3 could make
    // a legitimate completion look impossibly fast to the server.
    private bool _serverResumeTimeCached;
    private FixedString128Bytes _serverResumeNetworkId;
    private ushort _serverResumeMinigameId;
    private LaptopMinigameDifficulty _serverResumeDifficulty;
    private int _serverResumeSeed;
    private double _serverResumeElapsedSeconds;

    public event Action HackAlarmEmitted;

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

    // The player root is deliberately used by both owner and server. The previous
    // camera-based origin could disagree because PlayerLook only moves the camera
    // on the owning client.
    public Vector3 SignalOriginPosition => transform.position;

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
        audioOrigin = transform;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CacheReferences();
        EnsureAudioSources();
        ForceResetLocalForRound();
        AttachCoordinatorIfNeeded();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            RestoreGameUIAfterLaptop();

        StopMinigamePrewarm();
        DetachCoordinator();
        AbortLocalMinigame(clearTarget: true, notifyServer: false);
        screenUI?.DiscardAllMinigameProgress();
        ClearServerAttempt();
        StopLaptopAudio();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsServer)
            ServerUpdateActiveAttempt();

        if (!IsOwner)
            return;

        AttachCoordinatorIfNeeded();
        UpdateOwnerGameUIVisibility();

        bool canUseSurvivorTools = CanUseSurvivorTools();
        bool laptopUsable = IsLaptopUsable;

        if (!canUseSurvivorTools || !laptopUsable)
        {
            bool preserveProgress =
                canUseSurvivorTools
                && !laptopUsable
                && screenUI != null
                && screenUI.ActiveMinigame != null
                && screenUI.ActiveMinigame.SupportsSessionResume;

            AbortLocalMinigame(
                clearTarget: true,
                preserveProgress: preserveProgress
            );
            _showingTerminalResult = false;
            _lastPresentationKey = string.Empty;
            return;
        }

        if (_completionPending || _showingTerminalResult)
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
        if (IsOwner)
            RestoreGameUIAfterLaptop();

        AbortLocalMinigame(clearTarget: true);
        screenUI?.DiscardAllMinigameProgress();
        ClearServerAttempt();
        StopLaptopAudio();
        StopRejectedMessageCoroutine();
        _completionPending = false;
        _showingTerminalResult = false;
        _pendingCompletionNetworkId = string.Empty;
        _pendingCompletionDisplayName = string.Empty;
        _targetRefreshTimer = 0f;
        _lastPresentationKey = string.Empty;
        _navigationInput = Vector2.zero;
        _jumpHeld = false;
        _interactHeld = false;
        _localActionSequence = 0;
    }

    public void SetNavigationInput(Vector2 input)
    {
        if (!IsOwner)
            return;

        _navigationInput = Vector2.ClampMagnitude(input, 1f);

        if (HasActiveMinigame)
            screenUI.SetNavigation(_navigationInput);
    }

    public void JumpPressed()
    {
        if (!IsOwner || !HasActiveMinigame || _jumpHeld)
            return;

        _jumpHeld = true;
        screenUI.JumpPressed();
    }

    public void JumpReleased()
    {
        if (!IsOwner || !_jumpHeld)
            return;

        _jumpHeld = false;

        if (screenUI != null)
            screenUI.JumpReleased();
    }

    public void InteractPressed()
    {
        if (!IsOwner || !HasActiveMinigame || _interactHeld)
            return;

        _interactHeld = true;
        screenUI.InteractPressed();
    }

    public void InteractReleased()
    {
        if (!IsOwner || !_interactHeld)
            return;

        _interactHeld = false;

        if (screenUI != null)
            screenUI.InteractReleased();
    }

    // Compatibility aliases. New input routing keeps Space and E separate.
    public void PrimaryPressed() => InteractPressed();
    public void PrimaryReleased() => InteractReleased();

    public void ClearLocalInputState()
    {
        if (!IsOwner)
            return;

        _navigationInput = Vector2.zero;

        if (screenUI != null)
            screenUI.SetNavigation(Vector2.zero);

        JumpReleased();
        InteractReleased();
    }

    // Compatibility for existing cleanup code. The old hold-to-hack system no longer uses this.
    public void SetHackHeld(bool held)
    {
        if (held)
            InteractPressed();
        else
            InteractReleased();
    }

    private void CacheReferences()
    {
        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (screenUI == null)
            screenUI = GetComponentInChildren<LaptopScreenUI>(true);

        if (audioOrigin == null)
            audioOrigin = transform;

        if (IsOwner && gameUIRoot == null)
            gameUIRoot = FindSceneObjectByName(gameUIRootName);
    }

    private void UpdateOwnerGameUIVisibility()
    {
        if (!IsOwner)
            return;

        if (!hideGameUIWhileLaptopFocused)
        {
            RestoreGameUIAfterLaptop();
            return;
        }

        if (sitAction != null && sitAction.LaptopCameraFocus && CanUseSurvivorTools())
            HideGameUIForLaptop();
        else
            RestoreGameUIAfterLaptop();
    }

    private void HideGameUIForLaptop()
    {
        if (_gameUIHiddenByLaptop)
            return;

        if (gameUIRoot == null)
            gameUIRoot = FindSceneObjectByName(gameUIRootName);

        if (gameUIRoot == null)
            return;

        _gameUIWasActiveBeforeLaptop = gameUIRoot.activeSelf;
        _gameUIHiddenByLaptop = true;

        if (_gameUIWasActiveBeforeLaptop)
            gameUIRoot.SetActive(false);
    }

    private void RestoreGameUIAfterLaptop()
    {
        if (!_gameUIHiddenByLaptop)
            return;

        if (gameUIRoot != null)
            gameUIRoot.SetActive(_gameUIWasActiveBeforeLaptop);

        _gameUIHiddenByLaptop = false;
        _gameUIWasActiveBeforeLaptop = false;
    }

    private static GameObject FindSceneObjectByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject candidate = allObjects[i];

            if (candidate == null || candidate.name != objectName)
                continue;

            if (!candidate.scene.IsValid())
                continue;

            return candidate;
        }

        return null;
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
            OnLocalMinigameAlarmTriggered,
            OnLocalMinigameActionPerformed
        );

        _targetLockedByMinigame = started;

        if (!started)
            return;

        _activeContext = context;
        _hasActiveContext = true;
        _clientAttemptOpen = true;
        _completionPending = false;

        RequestBeginAttemptServerRpc(
            new FixedString128Bytes(context.NetworkId),
            context.MinigameId,
            context.Difficulty,
            context.Seed
        );

        screenUI.SetNavigation(_navigationInput);

        if (_jumpHeld)
            screenUI.JumpPressed();

        if (_interactHeld)
            screenUI.InteractPressed();
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

    private void OnLocalMinigameActionPerformed()
    {
        if (!IsOwner || !_clientAttemptOpen || !_hasActiveContext)
            return;

        _localActionSequence++;

        if (_localActionSequence == 0)
            _localActionSequence = 1;

        PlayKeypress(_localActionSequence);

        RequestMinigameActionAudioServerRpc(
            new FixedString128Bytes(_activeContext.NetworkId),
            _activeContext.MinigameId,
            _activeContext.Seed,
            _localActionSequence
        );
    }

    private void OnLocalMinigameCompleted()
    {
        if (!IsOwner || !_clientAttemptOpen || !_hasActiveContext)
            return;

        _targetLockedByMinigame = false;
        _completionPending = true;
        _showingTerminalResult = true;
        _pendingCompletionNetworkId = _activeContext.NetworkId;
        _pendingCompletionDisplayName = _activeContext.NetworkDisplayName;
        ClearLocalInputState();
        screenUI?.ShowVerifying(_pendingCompletionDisplayName);

        RequestCompleteAttemptServerRpc(
            new FixedString128Bytes(_activeContext.NetworkId),
            _activeContext.MinigameId,
            _activeContext.Seed
        );
    }

    private void OnLocalMinigameFailed()
    {
        if (!IsOwner)
            return;

        string displayName = _hasActiveContext
            ? _activeContext.NetworkDisplayName
            : CurrentTargetName;

        _targetLockedByMinigame = false;
        _showingTerminalResult = true;
        ClearLocalInputState();
        screenUI?.ShowFailure(displayName);
        EndClientAttemptAndNotifyServer();
    }

    private void OnLocalMinigameAlarmTriggered()
    {
        if (!IsOwner || !_clientAttemptOpen || !_hasActiveContext)
            return;

        PlayAlarmAndRaiseEvent();

        RequestMinigameAlarmServerRpc(
            new FixedString128Bytes(_activeContext.NetworkId),
            _activeContext.MinigameId,
            _activeContext.Seed
        );
    }

    private void AbortLocalMinigame(
        bool clearTarget,
        bool notifyServer = true,
        bool preserveProgress = false
    )
    {
        bool shouldNotifyServer =
            notifyServer
            && IsOwner
            && _clientAttemptOpen
            && CanSendRpc();

        ClearLocalInputState();
        screenUI?.AbortActiveMinigame(preserveProgress);
        _targetLockedByMinigame = false;
        _clientAttemptOpen = false;
        _completionPending = false;
        _hasActiveContext = false;
        _activeContext = default;
        _pendingCompletionNetworkId = string.Empty;
        _pendingCompletionDisplayName = string.Empty;

        if (shouldNotifyServer)
            RequestAbortAttemptServerRpc(preserveProgress);

        if (clearTarget)
            ClearCurrentTarget();
    }

    private void EndClientAttemptAndNotifyServer()
    {
        bool shouldNotify = IsOwner && _clientAttemptOpen && CanSendRpc();
        _clientAttemptOpen = false;
        _completionPending = false;
        _hasActiveContext = false;
        _activeContext = default;

        if (shouldNotify)
            RequestAbortAttemptServerRpc(preserveProgress: false);
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
        {
            TryStartMinigamePrewarm();
            return;
        }

        DetachCoordinator();
        _coordinator = instance;

        if (_coordinator != null)
            _coordinator.RecordsChanged += OnCoordinatorRecordsChanged;

        _targetRefreshTimer = 0f;
        _lastPresentationKey = string.Empty;
        TryStartMinigamePrewarm();
    }

    private void TryStartMinigamePrewarm()
    {
        if (!IsOwner || !prewarmMinigames || screenUI == null || _coordinator == null)
            return;

        LaptopMinigameCatalog catalog = _coordinator.MinigameCatalog;

        if (catalog == null || _prewarmedCatalog == catalog)
            return;

        StopMinigamePrewarm();
        _prewarmedCatalog = catalog;
        _minigamePrewarmRoutine = StartCoroutine(PrewarmMinigamesRoutine(catalog));
    }

    private IEnumerator PrewarmMinigamesRoutine(LaptopMinigameCatalog catalog)
    {
        IEnumerator routine = screenUI.PrewarmCatalog(
            catalog,
            Mathf.Max(1, prewarmOperationsPerFrame)
        );

        while (routine.MoveNext())
            yield return routine.Current;

        _minigamePrewarmRoutine = null;
    }

    private void StopMinigamePrewarm()
    {
        if (_minigamePrewarmRoutine == null)
            return;

        StopCoroutine(_minigamePrewarmRoutine);
        _minigamePrewarmRoutine = null;
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
            _currentTarget == null
            || _coordinator == null
            || !_coordinator.IsCompleted(_currentTarget.NetworkId)
        )
            return;

        if (
            _completionPending
            && string.Equals(
                _pendingCompletionNetworkId,
                _currentTarget.NetworkId,
                StringComparison.Ordinal
            )
        )
        {
            // The targeted server result will decide whether this owner sees
            // ACCESS GRANTED or a rejection message.
            return;
        }

        string displayName = _currentTarget.NetworkName;
        AbortLocalMinigame(clearTarget: true);
        _showingTerminalResult = true;
        screenUI?.ShowCompletedElsewhere(displayName);
    }

    private void PresentOnce(string key, Action presentation)
    {
        if (_lastPresentationKey == key)
            return;

        _lastPresentationKey = key;
        presentation?.Invoke();
    }

    [ServerRpc]
    private void RequestBeginAttemptServerRpc(
        FixedString128Bytes networkId,
        ushort minigameId,
        LaptopMinigameDifficulty difficulty,
        int seed,
        ServerRpcParams rpcParams = default
    )
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        AttemptRejectReason reason = ValidateServerAttemptRequest(
            networkId.ToString(),
            minigameId,
            difficulty,
            seed
        );

        if (reason != AttemptRejectReason.None)
        {
            SendBeginRejectedToOwner(networkId, reason);
            return;
        }

        bool resumeSameAttempt = ServerResumeContextMatches(
            networkId,
            minigameId,
            difficulty,
            seed
        );

        if (!resumeSameAttempt)
            ClearServerResumeTime();

        double now = GetServerTime();
        double resumedElapsed = resumeSameAttempt
            ? Math.Max(0d, _serverResumeElapsedSeconds)
            : 0d;

        _serverAttemptActive = true;
        _serverAttemptNetworkId = networkId;
        _serverAttemptMinigameId = minigameId;
        _serverAttemptDifficulty = difficulty;
        _serverAttemptSeed = seed;
        _serverAttemptStartedAt = now - resumedElapsed;
        _serverLastKeypressAt = double.NegativeInfinity;
        _serverLastAlarmAt = double.NegativeInfinity;
        _serverAttemptValidationTimer = Mathf.Max(0.02f, serverAttemptValidationInterval);
    }

    [ServerRpc]
    private void RequestAbortAttemptServerRpc(
        bool preserveProgress,
        ServerRpcParams rpcParams = default
    )
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (preserveProgress)
        {
            CacheServerAttemptTimeForResume();
            ClearServerAttempt(clearResumeTime: false);
        }
        else
        {
            ClearServerAttempt();
        }
    }

    [ServerRpc]
    private void RequestCompleteAttemptServerRpc(
        FixedString128Bytes networkId,
        ushort minigameId,
        int seed,
        ServerRpcParams rpcParams = default
    )
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (senderClientId != OwnerClientId)
            return;

        string id = networkId.ToString();
        AttemptRejectReason reason = ValidateActiveServerAttempt(
            id,
            minigameId,
            seed
        );

        if (reason == AttemptRejectReason.None)
        {
            if (
                _coordinator == null
                || !_coordinator.TryGetMinigameDefinition(
                    _serverAttemptMinigameId,
                    out LaptopMinigameDefinition definition
                )
            )
            {
                reason = AttemptRejectReason.AssignmentMismatch;
            }
            else
            {
                double elapsed = GetServerTime() - _serverAttemptStartedAt;
                float minimumSeconds = definition.GetMinimumCompletionSeconds(
                    _serverAttemptDifficulty
                );

                if (elapsed + 0.05d < minimumSeconds)
                    reason = AttemptRejectReason.CompletedTooQuickly;
            }
        }

        if (reason == AttemptRejectReason.None)
        {
            if (_coordinator == null || !_coordinator.ServerMarkCompleted(id))
                reason = AttemptRejectReason.CompletionFailed;
        }

        bool accepted = reason == AttemptRejectReason.None;
        ClearServerAttempt();
        SendCompletionResultToOwner(networkId, accepted, reason);
    }

    [ServerRpc]
    private void RequestMinigameActionAudioServerRpc(
        FixedString128Bytes networkId,
        ushort minigameId,
        int seed,
        ushort sequence,
        ServerRpcParams rpcParams = default
    )
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (
            ValidateActiveServerAttempt(networkId.ToString(), minigameId, seed)
            != AttemptRejectReason.None
        )
            return;

        double now = GetServerTime();

        if (now - _serverLastKeypressAt < minimumNetworkedKeypressInterval)
            return;

        _serverLastKeypressAt = now;
        BroadcastMinigameActionAudioClientRpc(sequence);
    }

    [ServerRpc]
    private void RequestMinigameAlarmServerRpc(
        FixedString128Bytes networkId,
        ushort minigameId,
        int seed,
        ServerRpcParams rpcParams = default
    )
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (
            ValidateActiveServerAttempt(networkId.ToString(), minigameId, seed)
            != AttemptRejectReason.None
        )
            return;

        double now = GetServerTime();

        if (now - _serverLastAlarmAt < minimumNetworkedAlarmInterval)
            return;

        _serverLastAlarmAt = now;
        BroadcastMinigameAlarmClientRpc();
    }

    [ClientRpc]
    private void BroadcastMinigameActionAudioClientRpc(ushort sequence)
    {
        // The owner already played it instantly when the minigame accepted the input.
        if (IsOwner)
            return;

        PlayKeypress(sequence);
    }

    [ClientRpc]
    private void BroadcastMinigameAlarmClientRpc()
    {
        // The owner already played it instantly on collision.
        if (IsOwner)
            return;

        PlayAlarmAndRaiseEvent();
    }

    [ClientRpc]
    private void BeginAttemptRejectedClientRpc(
        FixedString128Bytes networkId,
        byte reasonValue,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (!IsOwner || !_clientAttemptOpen || !_hasActiveContext)
            return;

        if (
            !string.Equals(
                _activeContext.NetworkId,
                networkId.ToString(),
                StringComparison.Ordinal
            )
        )
            return;

        string displayName = _activeContext.NetworkDisplayName;
        AbortLocalMinigame(clearTarget: false, notifyServer: false);
        ShowRejectedThenRetry(displayName, (AttemptRejectReason)reasonValue);
    }

    [ClientRpc]
    private void CompletionResultClientRpc(
        FixedString128Bytes networkId,
        bool accepted,
        byte reasonValue,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (!IsOwner || !_completionPending)
            return;

        string id = networkId.ToString();

        if (
            !string.Equals(
                _pendingCompletionNetworkId,
                id,
                StringComparison.Ordinal
            )
        )
            return;

        string displayName = _pendingCompletionDisplayName;
        _completionPending = false;
        _clientAttemptOpen = false;
        _hasActiveContext = false;
        _activeContext = default;
        _pendingCompletionNetworkId = string.Empty;
        _pendingCompletionDisplayName = string.Empty;

        if (accepted)
        {
            _showingTerminalResult = true;
            ClearCurrentTarget();
            screenUI?.ShowSuccess(displayName);
            return;
        }

        ShowRejectedThenRetry(displayName, (AttemptRejectReason)reasonValue);
    }

    private AttemptRejectReason ValidateServerAttemptRequest(
        string networkId,
        ushort minigameId,
        LaptopMinigameDifficulty difficulty,
        int seed
    )
    {
        AttachCoordinatorIfNeeded();

        if (!CanUseSurvivorTools())
            return AttemptRejectReason.PlayerUnavailable;

        if (
            sitAction == null
            || !sitAction.WantsSittingValue
            || !sitAction.BlocksGameplayMovement
        )
            return AttemptRejectReason.NotSitting;

        if (_coordinator == null || !_coordinator.AssignmentsReady)
            return AttemptRejectReason.AssignmentsUnavailable;

        if (!_coordinator.TryGetRecord(networkId, out RouterHackRecord record))
            return AttemptRejectReason.AssignmentMismatch;

        if (record.Completed)
            return AttemptRejectReason.AlreadyCompleted;

        if (
            record.MinigameId != minigameId
            || record.Difficulty != difficulty
            || record.Seed != seed
        )
            return AttemptRejectReason.AssignmentMismatch;

        if (!ServerHasRequiredSignal(networkId))
            return AttemptRejectReason.OutOfRange;

        return AttemptRejectReason.None;
    }

    private AttemptRejectReason ValidateActiveServerAttempt(
        string networkId,
        ushort minigameId,
        int seed
    )
    {
        if (!_serverAttemptActive)
            return AttemptRejectReason.NoActiveAttempt;

        if (
            !string.Equals(
                _serverAttemptNetworkId.ToString(),
                networkId,
                StringComparison.Ordinal
            )
            || _serverAttemptMinigameId != minigameId
            || _serverAttemptSeed != seed
        )
            return AttemptRejectReason.AttemptMismatch;

        return ValidateServerAttemptRequest(
            networkId,
            minigameId,
            _serverAttemptDifficulty,
            seed
        );
    }

    private bool ServerHasRequiredSignal(string networkId)
    {
        Vector3 origin = SignalOriginPosition;
        var routers = RouterRegistry.Routers;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];

            if (router == null)
                continue;

            if (!string.Equals(router.NetworkId, networkId, StringComparison.Ordinal))
                continue;

            if (HasRequiredBars(router.GetStrength01(origin)))
                return true;
        }

        return false;
    }

    private void ServerUpdateActiveAttempt()
    {
        if (!_serverAttemptActive)
            return;

        _serverAttemptValidationTimer -= Time.deltaTime;

        if (_serverAttemptValidationTimer > 0f)
            return;

        _serverAttemptValidationTimer = Mathf.Max(
            0.02f,
            serverAttemptValidationInterval
        );

        AttemptRejectReason reason = ValidateActiveServerAttempt(
            _serverAttemptNetworkId.ToString(),
            _serverAttemptMinigameId,
            _serverAttemptSeed
        );

        if (reason != AttemptRejectReason.None)
        {
            // Laptop close/stand-up can reach the periodic validator before the
            // owner's abort RPC. Preserve elapsed validation time for that same
            // assignment; explicit non-resume aborts clear it afterward.
            if (reason == AttemptRejectReason.NotSitting)
            {
                CacheServerAttemptTimeForResume();
                ClearServerAttempt(clearResumeTime: false);
            }
            else
            {
                ClearServerAttempt();
            }
        }
    }

    private void CacheServerAttemptTimeForResume()
    {
        if (!_serverAttemptActive)
            return;

        _serverResumeTimeCached = true;
        _serverResumeNetworkId = _serverAttemptNetworkId;
        _serverResumeMinigameId = _serverAttemptMinigameId;
        _serverResumeDifficulty = _serverAttemptDifficulty;
        _serverResumeSeed = _serverAttemptSeed;
        _serverResumeElapsedSeconds = Math.Max(
            0d,
            GetServerTime() - _serverAttemptStartedAt
        );
    }

    private bool ServerResumeContextMatches(
        FixedString128Bytes networkId,
        ushort minigameId,
        LaptopMinigameDifficulty difficulty,
        int seed
    )
    {
        return _serverResumeTimeCached
            && _serverResumeMinigameId == minigameId
            && _serverResumeDifficulty == difficulty
            && _serverResumeSeed == seed
            && string.Equals(
                _serverResumeNetworkId.ToString(),
                networkId.ToString(),
                StringComparison.Ordinal
            );
    }

    private void ClearServerResumeTime()
    {
        _serverResumeTimeCached = false;
        _serverResumeNetworkId = default;
        _serverResumeMinigameId = 0;
        _serverResumeDifficulty = LaptopMinigameDifficulty.Easy;
        _serverResumeSeed = 0;
        _serverResumeElapsedSeconds = 0d;
    }

    private void ClearServerAttempt(bool clearResumeTime = true)
    {
        _serverAttemptActive = false;
        _serverAttemptNetworkId = default;
        _serverAttemptMinigameId = 0;
        _serverAttemptDifficulty = LaptopMinigameDifficulty.Easy;
        _serverAttemptSeed = 0;
        _serverAttemptStartedAt = 0d;
        _serverLastKeypressAt = double.NegativeInfinity;
        _serverLastAlarmAt = double.NegativeInfinity;
        _serverAttemptValidationTimer = 0f;

        if (clearResumeTime)
            ClearServerResumeTime();
    }

    private void SendBeginRejectedToOwner(
        FixedString128Bytes networkId,
        AttemptRejectReason reason
    )
    {
        BeginAttemptRejectedClientRpc(
            networkId,
            (byte)reason,
            CreateOwnerClientRpcParams()
        );
    }

    private void SendCompletionResultToOwner(
        FixedString128Bytes networkId,
        bool accepted,
        AttemptRejectReason reason
    )
    {
        CompletionResultClientRpc(
            networkId,
            accepted,
            (byte)reason,
            CreateOwnerClientRpcParams()
        );
    }

    private ClientRpcParams CreateOwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId },
            },
        };
    }

    private void ShowRejectedThenRetry(
        string displayName,
        AttemptRejectReason reason
    )
    {
        StopRejectedMessageCoroutine();
        _showingTerminalResult = true;
        screenUI?.ShowCompletionRejected(displayName, GetRejectReasonText(reason));
        _rejectedMessageCoroutine = StartCoroutine(RejectedMessageRoutine());
    }

    private IEnumerator RejectedMessageRoutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, rejectedMessageSeconds));
        _rejectedMessageCoroutine = null;

        if (!IsOwner || !IsLaptopUsable || !CanUseSurvivorTools())
            yield break;

        _showingTerminalResult = false;
        ClearCurrentTarget();
        _lastPresentationKey = string.Empty;
        _targetRefreshTimer = 0f;
    }

    private void StopRejectedMessageCoroutine()
    {
        if (_rejectedMessageCoroutine == null)
            return;

        StopCoroutine(_rejectedMessageCoroutine);
        _rejectedMessageCoroutine = null;
    }

    private static string GetRejectReasonText(AttemptRejectReason reason)
    {
        return reason switch
        {
            AttemptRejectReason.PlayerUnavailable => "PLAYER CANNOT HACK",
            AttemptRejectReason.NotSitting => "LAPTOP SESSION IS NOT ACTIVE",
            AttemptRejectReason.AssignmentsUnavailable => "ROUTER DATA IS NOT READY",
            AttemptRejectReason.AssignmentMismatch => "MINIGAME ASSIGNMENT MISMATCH",
            AttemptRejectReason.AlreadyCompleted => "NETWORK ALREADY COMPLETED",
            AttemptRejectReason.OutOfRange => "SIGNAL LOST",
            AttemptRejectReason.NoActiveAttempt => "NO SERVER ATTEMPT FOUND",
            AttemptRejectReason.AttemptMismatch => "ATTEMPT DATA MISMATCH",
            AttemptRejectReason.CompletedTooQuickly => "BREACH COMPLETED TOO QUICKLY",
            AttemptRejectReason.CompletionFailed => "NETWORK COMPLETION FAILED",
            _ => "SERVER VALIDATION FAILED",
        };
    }

    private void EnsureAudioSources()
    {
        if (audioOrigin == null)
            audioOrigin = transform;

        if (keypressAudioSource == null)
            keypressAudioSource = audioOrigin.gameObject.AddComponent<AudioSource>();

        if (alarmAudioSource == null || alarmAudioSource == keypressAudioSource)
            alarmAudioSource = audioOrigin.gameObject.AddComponent<AudioSource>();

        ConfigureAudioSource(
            keypressAudioSource,
            keypressMinDistance,
            keypressMaxDistance,
            keypressRolloff,
            priority: 128
        );

        ConfigureAudioSource(
            alarmAudioSource,
            alarmMinDistance,
            alarmMaxDistance,
            alarmRolloff,
            priority: 32
        );
    }

    private static void ConfigureAudioSource(
        AudioSource source,
        float minDistance,
        float maxDistance,
        AudioRolloffMode rolloffMode,
        int priority
    )
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = rolloffMode;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, maxDistance);
        source.priority = Mathf.Clamp(priority, 0, 256);
    }

    private void PlayKeypress(ushort sequence)
    {
        EnsureAudioSources();

        if (keypressAudioSource == null || keypressClips == null || keypressClips.Length == 0)
            return;

        int clipIndex = sequence % keypressClips.Length;
        AudioClip clip = keypressClips[clipIndex];

        if (clip == null)
            return;

        float minPitch = Mathf.Min(keypressPitchMin, keypressPitchMax);
        float maxPitch = Mathf.Max(keypressPitchMin, keypressPitchMax);
        uint hash = (uint)(sequence * 1103515245u + 12345u);
        float pitchT = (hash & 0xFFFFu) / 65535f;
        keypressAudioSource.pitch = Mathf.Lerp(minPitch, maxPitch, pitchT);
        keypressAudioSource.PlayOneShot(clip, Mathf.Clamp01(keypressVolume));
    }

    private void PlayAlarmAndRaiseEvent()
    {
        EnsureAudioSources();

        if (alarmAudioSource != null && alarmClip != null)
        {
            alarmAudioSource.pitch = 1f;
            alarmAudioSource.PlayOneShot(alarmClip, Mathf.Clamp01(alarmVolume));
        }

        // A future through-wall red reveal component can subscribe to this event
        // on each player's PlayerLaptopHacker without changing the minigame API.
        HackAlarmEmitted?.Invoke();
    }

    private void StopLaptopAudio()
    {
        if (keypressAudioSource != null)
            keypressAudioSource.Stop();

        if (alarmAudioSource != null)
            alarmAudioSource.Stop();
    }

    private double GetServerTime()
    {
        if (NetworkManager.Singleton == null)
            return 0d;

        return NetworkManager.Singleton.ServerTime.Time;
    }

    private bool CanSendRpc()
    {
        return IsSpawned
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening;
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

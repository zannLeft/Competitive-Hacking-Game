using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class LaptopScreenUI : MonoBehaviour
{
    private sealed class PooledMinigame
    {
        public GameObject Instance;
        public LaptopMinigameBase Minigame;
    }

    [Header("Fallback / OS Message UI")]
    [SerializeField]
    private TMP_Text titleText;

    [SerializeField]
    private TMP_Text statusText;

    [SerializeField]
    private TMP_Text targetText;

    [SerializeField]
    private TMP_Text promptText;

    [Header("ZannOS Message Shell")]
    [Tooltip("Optional. Leave empty to use the TMP default font.")]
    [SerializeField]
    private TMP_FontAsset terminalFont;

    [SerializeField]
    private string shellName = "ZannOS";

    [Header("Minigame Host")]
    [Tooltip("Empty stretched RectTransform under LaptopUI. Minigame UI prefabs are instantiated beneath it.")]
    [SerializeField]
    private RectTransform minigameRoot;

    private LaptopMinigameBase _activeMinigame;
    private GameObject _activeMinigameObject;
    private Action _completedCallback;
    private Action _failedCallback;
    private Action _alarmTriggeredCallback;
    private Action _actionPerformedCallback;
    private LaptopTerminalMessageShell _messageShell;
    private readonly Dictionary<GameObject, PooledMinigame> _minigamePool = new();
    private bool _initialized;

    public bool HasActiveMinigame =>
        _activeMinigame != null && _activeMinigame.IsRunning;

    public LaptopMinigameBase ActiveMinigame => _activeMinigame;

    protected virtual void Awake()
    {
        EnsureInitialized();
    }

    protected virtual void Update()
    {
        _messageShell?.Tick(Time.unscaledTime);
    }

    protected virtual void OnDestroy()
    {
        DiscardAllMinigameProgress();
        _minigamePool.Clear();
    }

    /// <summary>
    /// Instantiates and prepares every catalog minigame while the laptop is still
    /// closed. The coroutine is deliberately run by PlayerLaptopHacker because this
    /// component normally lives below an inactive laptop root.
    /// </summary>
    public IEnumerator PrewarmCatalog(
        LaptopMinigameCatalog catalog,
        int operationsPerFrame
    )
    {
        EnsureInitialized();

        if (catalog == null || minigameRoot == null)
            yield break;

        IReadOnlyList<LaptopMinigameDefinition> definitions = catalog.Minigames;

        for (int i = 0; i < definitions.Count; i++)
        {
            LaptopMinigameDefinition definition = definitions[i];
            GameObject prefab = definition != null ? definition.UiPrefab : null;

            if (prefab == null || _minigamePool.ContainsKey(prefab))
                continue;

            PooledMinigame pooled = CreatePooledMinigame(prefab);

            if (pooled == null)
                continue;

            // Register immediately so an unusually fast laptop open reuses this same
            // instance instead of creating a second copy while prewarming is in flight.
            _minigamePool[prefab] = pooled;

            IEnumerator prepareRoutine = pooled.Minigame.PrepareIncrementally(
                Mathf.Max(1, operationsPerFrame)
            );

            while (prepareRoutine.MoveNext())
                yield return prepareRoutine.Current;

            if (_activeMinigameObject != pooled.Instance)
                pooled.Instance.SetActive(false);

            // Give the Canvas/TMP systems a frame between different modules.
            yield return null;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        BuildMessageShell();
        SetMessageObjectsVisible(true);

        if (minigameRoot != null)
            minigameRoot.gameObject.SetActive(false);
    }

    public void ShowWaitingForAssignments()
    {
        ShowMessage(
            LaptopTerminalMessageKind.Scanning,
            "> router.sync --assignments",
            "NETWORK SCAN",
            "SYNCING ROUTER DATA...",
            string.Empty,
            "PLEASE WAIT"
        );
    }

    public void ShowNoNetwork()
    {
        ShowMessage(
            LaptopTerminalMessageKind.NoNetwork,
            "> wlan.scan --hackable",
            "NO NETWORK IN RANGE",
            "NO HACKABLE ACCESS POINT DETECTED",
            string.Empty,
            "USE THE PHONE TO FIND A FULL-STRENGTH SIGNAL"
        );
    }

    public void ShowMissingAssignment(string networkDisplayName)
    {
        ShowMessage(
            LaptopTerminalMessageKind.Error,
            "> module.resolve --assignment",
            "NETWORK DATA ERROR",
            "NO MINIGAME ASSIGNMENT WAS FOUND",
            SafeNetworkLabel(networkDisplayName),
            "RETRYING ROUTER SYNC"
        );
    }

    public void ShowMissingDefinition(
        string networkDisplayName,
        ushort minigameId
    )
    {
        ShowMessage(
            LaptopTerminalMessageKind.Error,
            "> module.resolve --definition",
            "MINIGAME DATA ERROR",
            $"UNKNOWN MINIGAME ID: {minigameId}",
            SafeNetworkLabel(networkDisplayName),
            "MODULE LOAD ABORTED"
        );
    }

    public void ShowMissingPrefab(
        string networkDisplayName,
        string minigameDisplayName,
        LaptopMinigameDifficulty difficulty
    )
    {
        ShowMessage(
            LaptopTerminalMessageKind.Error,
            "> module.load --ui",
            minigameDisplayName,
            $"{difficulty.ToString().ToUpperInvariant()} MODULE READY",
            SafeNetworkLabel(networkDisplayName),
            "NO UI PREFAB IS ASSIGNED"
        );
    }

    public void ShowCompletedElsewhere(string networkDisplayName)
    {
        ShowMessage(
            LaptopTerminalMessageKind.Completed,
            "> node.status --completed",
            "ACCESS POINT COMPLETE",
            "THIS NETWORK HAS ALREADY BEEN HACKED",
            SafeNetworkLabel(networkDisplayName),
            "NO FURTHER ACTION REQUIRED"
        );
    }

    public void ShowVerifying(string networkDisplayName)
    {
        ShowMessage(
            LaptopTerminalMessageKind.Verifying,
            "> breach.verify --server",
            "VERIFYING BREACH",
            "WAITING FOR SERVER CONFIRMATION",
            SafeNetworkLabel(networkDisplayName),
            "DO NOT CLOSE THE SESSION"
        );
    }

    public void ShowSuccess(string networkDisplayName)
    {
        ShowMessage(
            LaptopTerminalMessageKind.Success,
            "> breach.commit --complete",
            "ACCESS GRANTED",
            "NETWORK BREACH COMPLETE",
            SafeNetworkLabel(networkDisplayName),
            "CREDENTIALS SYNCHRONIZED"
        );
    }

    public void ShowCompletionRejected(string networkDisplayName, string reason)
    {
        ShowMessage(
            LaptopTerminalMessageKind.Warning,
            "> breach.commit --retry",
            "BREACH REJECTED",
            string.IsNullOrWhiteSpace(reason) ? "SERVER VALIDATION FAILED" : reason,
            SafeNetworkLabel(networkDisplayName),
            "RECONNECTING..."
        );
    }

    public void ShowFailure(string networkDisplayName)
    {
        ShowMessage(
            LaptopTerminalMessageKind.Error,
            "> trace.alert --security",
            "TRACE DETECTED",
            "SECURITY ALARM TRIGGERED",
            SafeNetworkLabel(networkDisplayName),
            "SESSION ROUTE INVALIDATED"
        );
    }

    public bool TryStartMinigame(
        LaptopMinigameDefinition definition,
        LaptopMinigameContext context,
        Action completedCallback,
        Action failedCallback,
        Action alarmTriggeredCallback,
        Action actionPerformedCallback
    )
    {
        EnsureInitialized();
        AbortActiveMinigame();

        if (definition == null)
        {
            ShowMissingDefinition(context.NetworkDisplayName, context.MinigameId);
            return false;
        }

        if (definition.UiPrefab == null)
        {
            ShowMissingPrefab(
                context.NetworkDisplayName,
                definition.DisplayName,
                context.Difficulty
            );
            return false;
        }

        if (minigameRoot == null)
        {
            Debug.LogError(
                "[LaptopScreenUI] No Minigame Root is assigned.",
                this
            );

            ShowMessage(
                LaptopTerminalMessageKind.Error,
                "> ui.mount --minigame-root",
                "LAPTOP UI ERROR",
                "MINIGAME ROOT IS NOT ASSIGNED",
                SafeNetworkLabel(context.NetworkDisplayName),
                "CHECK LAPTOP PREFAB CONFIGURATION"
            );
            return false;
        }

        if (
            !_minigamePool.TryGetValue(
                definition.UiPrefab,
                out PooledMinigame pooled
            )
            || pooled == null
            || pooled.Instance == null
            || pooled.Minigame == null
        )
        {
            pooled = CreatePooledMinigame(definition.UiPrefab);

            if (pooled != null)
            {
                // This is only a fallback when the owner opens the laptop before the
                // background prewarm finishes. Normal starts reuse a prepared instance.
                pooled.Minigame.Prepare();
                pooled.Instance.SetActive(false);
                _minigamePool[definition.UiPrefab] = pooled;
            }
        }

        if (pooled == null || pooled.Instance == null || pooled.Minigame == null)
        {
            minigameRoot.gameObject.SetActive(false);

            ShowMessage(
                LaptopTerminalMessageKind.Error,
                "> module.load --component",
                "MINIGAME PREFAB ERROR",
                "LAPTOPMINIGAMEBASE COMPONENT MISSING",
                SafeNetworkLabel(context.NetworkDisplayName),
                "MODULE LOAD ABORTED"
            );
            return false;
        }

        minigameRoot.gameObject.SetActive(true);
        _activeMinigameObject = pooled.Instance;
        _activeMinigame = pooled.Minigame;
        FitMinigameToHost(_activeMinigameObject);
        _activeMinigameObject.SetActive(true);

        _completedCallback = completedCallback;
        _failedCallback = failedCallback;
        _alarmTriggeredCallback = alarmTriggeredCallback;
        _actionPerformedCallback = actionPerformedCallback;

        _activeMinigame.Completed += OnActiveMinigameCompleted;
        _activeMinigame.Failed += OnActiveMinigameFailed;
        _activeMinigame.AlarmTriggered += OnActiveMinigameAlarmTriggered;
        _activeMinigame.ActionPerformed += OnActiveMinigameActionPerformed;

        SetMessageObjectsVisible(false);
        _activeMinigame.Begin(context);
        return true;
    }

    public void SetNavigation(Vector2 input)
    {
        _activeMinigame?.SetNavigation(input);
    }

    public void JumpPressed()
    {
        _activeMinigame?.JumpPressed();
    }

    public void JumpReleased()
    {
        _activeMinigame?.JumpReleased();
    }

    public void InteractPressed()
    {
        _activeMinigame?.InteractPressed();
    }

    public void InteractReleased()
    {
        _activeMinigame?.InteractReleased();
    }

    // Compatibility aliases for older callers.
    public void PrimaryPressed() => InteractPressed();
    public void PrimaryReleased() => InteractReleased();

    public void AbortActiveMinigame(bool preserveProgress = false)
    {
        if (_activeMinigame != null)
        {
            _activeMinigame.Completed -= OnActiveMinigameCompleted;
            _activeMinigame.Failed -= OnActiveMinigameFailed;
            _activeMinigame.AlarmTriggered -= OnActiveMinigameAlarmTriggered;
            _activeMinigame.ActionPerformed -= OnActiveMinigameActionPerformed;

            if (preserveProgress && _activeMinigame.SupportsSessionResume)
                _activeMinigame.Suspend();
            else
                _activeMinigame.Abort();
        }

        _activeMinigame = null;
        _completedCallback = null;
        _failedCallback = null;
        _alarmTriggeredCallback = null;
        _actionPerformedCallback = null;

        if (_activeMinigameObject != null)
            _activeMinigameObject.SetActive(false);

        _activeMinigameObject = null;

        if (minigameRoot != null)
            minigameRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// Clears suspended progress kept inside pooled minigames. Use this for a real
    /// round reset, despawn, or assignment invalidation rather than a normal laptop close.
    /// </summary>
    public void DiscardAllMinigameProgress()
    {
        AbortActiveMinigame(preserveProgress: false);

        foreach (PooledMinigame pooled in _minigamePool.Values)
        {
            if (pooled?.Minigame != null && pooled.Minigame.IsSuspended)
                pooled.Minigame.Abort();
        }
    }

    private void OnActiveMinigameCompleted()
    {
        Action callback = _completedCallback;
        DetachActiveMinigameCallbacks();
        callback?.Invoke();
    }

    private void OnActiveMinigameFailed()
    {
        Action callback = _failedCallback;
        DetachActiveMinigameCallbacks();
        callback?.Invoke();
    }

    private void OnActiveMinigameAlarmTriggered()
    {
        _alarmTriggeredCallback?.Invoke();
    }

    private void OnActiveMinigameActionPerformed()
    {
        _actionPerformedCallback?.Invoke();
    }

    private void DetachActiveMinigameCallbacks()
    {
        if (_activeMinigame != null)
        {
            _activeMinigame.Completed -= OnActiveMinigameCompleted;
            _activeMinigame.Failed -= OnActiveMinigameFailed;
            _activeMinigame.AlarmTriggered -= OnActiveMinigameAlarmTriggered;
            _activeMinigame.ActionPerformed -= OnActiveMinigameActionPerformed;
        }

        _completedCallback = null;
        _failedCallback = null;
        _alarmTriggeredCallback = null;
        _actionPerformedCallback = null;
    }

    private void ShowMessage(
        LaptopTerminalMessageKind kind,
        string command,
        string title,
        string status,
        string target,
        string prompt
    )
    {
        AbortActiveMinigame();
        SetMessageObjectsVisible(true);

        if (_messageShell != null)
        {
            _messageShell.Show(kind, command, title, status, target, prompt);
            return;
        }

        SetText(titleText, title);
        SetText(statusText, status);
        SetText(targetText, target);
        SetText(promptText, prompt);
    }

    private void SetMessageObjectsVisible(bool visible)
    {
        bool useLegacyText = _messageShell == null;

        SetTextObjectVisible(titleText, visible && useLegacyText);
        SetTextObjectVisible(statusText, visible && useLegacyText);
        SetTextObjectVisible(targetText, visible && useLegacyText);
        SetTextObjectVisible(promptText, visible && useLegacyText);

        _messageShell?.SetVisible(visible);
    }

    private void BuildMessageShell()
    {
        RectTransform root = transform as RectTransform;
        if (root == null)
            return;

        try
        {
            _messageShell = LaptopTerminalMessageShell.Build(
                root,
                terminalFont,
                shellName
            );

            if (minigameRoot != null)
            {
                int minigameIndex = minigameRoot.GetSiblingIndex();
                _messageShell.Root.SetSiblingIndex(minigameIndex);
                minigameRoot.SetAsLastSibling();
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[LaptopScreenUI] Failed to build terminal message shell: {exception}",
                this
            );
            _messageShell = null;
        }
    }

    private PooledMinigame CreatePooledMinigame(GameObject prefab)
    {
        if (prefab == null || minigameRoot == null)
            return null;

        GameObject instance = Instantiate(
            prefab,
            minigameRoot,
            worldPositionStays: false
        );

        FitMinigameToHost(instance);

        LaptopMinigameBase minigame =
            instance.GetComponent<LaptopMinigameBase>()
            ?? instance.GetComponentInChildren<LaptopMinigameBase>(true);

        if (minigame == null)
        {
            Debug.LogError(
                $"[LaptopScreenUI] Minigame prefab '{prefab.name}' "
                    + "does not contain a LaptopMinigameBase component.",
                prefab
            );
            Destroy(instance);
            return null;
        }

        return new PooledMinigame
        {
            Instance = instance,
            Minigame = minigame,
        };
    }

    private static void FitMinigameToHost(GameObject minigameObject)
    {
        if (minigameObject == null)
            return;

        RectTransform rect = minigameObject.transform as RectTransform;

        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }

    private static void SetTextObjectVisible(TMP_Text text, bool visible)
    {
        if (text != null)
            text.gameObject.SetActive(visible);
    }

    private static string SafeNetworkLabel(string networkDisplayName)
    {
        return string.IsNullOrWhiteSpace(networkDisplayName)
            ? string.Empty
            : $"NETWORK: {networkDisplayName}";
    }
}

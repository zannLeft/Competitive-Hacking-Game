using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class LaptopScreenUI : MonoBehaviour
{
    [Header("Fallback / OS Message UI")]
    [SerializeField]
    private TMP_Text titleText;

    [SerializeField]
    private TMP_Text statusText;

    [SerializeField]
    private TMP_Text targetText;

    [SerializeField]
    private TMP_Text promptText;

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

    public bool HasActiveMinigame =>
        _activeMinigame != null && _activeMinigame.IsRunning;

    public LaptopMinigameBase ActiveMinigame => _activeMinigame;

    protected virtual void Awake()
    {
        SetMessageObjectsVisible(true);

        if (minigameRoot != null)
            minigameRoot.gameObject.SetActive(false);
    }

    protected virtual void OnDestroy()
    {
        AbortActiveMinigame();
    }

    public void ShowWaitingForAssignments()
    {
        ShowMessage(
            "NETWORK SCAN",
            "SYNCING ROUTER DATA...",
            string.Empty,
            string.Empty
        );
    }

    public void ShowNoNetwork()
    {
        ShowMessage(
            "NO NETWORK IN RANGE",
            "NO HACKABLE ACCESS POINT DETECTED",
            "USE THE PHONE TO LOCATE A FULL-STRENGTH SIGNAL",
            string.Empty
        );
    }

    public void ShowMissingAssignment(string networkDisplayName)
    {
        ShowMessage(
            "NETWORK DATA ERROR",
            "NO MINIGAME ASSIGNMENT WAS FOUND",
            SafeNetworkLabel(networkDisplayName),
            string.Empty
        );
    }

    public void ShowMissingDefinition(
        string networkDisplayName,
        ushort minigameId
    )
    {
        ShowMessage(
            "MINIGAME DATA ERROR",
            $"UNKNOWN MINIGAME ID: {minigameId}",
            SafeNetworkLabel(networkDisplayName),
            string.Empty
        );
    }

    public void ShowMissingPrefab(
        string networkDisplayName,
        string minigameDisplayName,
        LaptopMinigameDifficulty difficulty
    )
    {
        ShowMessage(
            minigameDisplayName,
            $"{difficulty.ToString().ToUpperInvariant()} MODULE READY",
            SafeNetworkLabel(networkDisplayName),
            "NO UI PREFAB IS ASSIGNED"
        );
    }

    public void ShowCompletedElsewhere(string networkDisplayName)
    {
        ShowMessage(
            "ACCESS POINT COMPLETE",
            "THIS NETWORK HAS ALREADY BEEN HACKED",
            SafeNetworkLabel(networkDisplayName),
            string.Empty
        );
    }

    public void ShowVerifying(string networkDisplayName)
    {
        ShowMessage(
            "VERIFYING BREACH",
            "WAITING FOR SERVER CONFIRMATION",
            SafeNetworkLabel(networkDisplayName),
            string.Empty
        );
    }

    public void ShowSuccess(string networkDisplayName)
    {
        ShowMessage(
            "ACCESS GRANTED",
            "NETWORK BREACH COMPLETE",
            SafeNetworkLabel(networkDisplayName),
            string.Empty
        );
    }

    public void ShowCompletionRejected(string networkDisplayName, string reason)
    {
        ShowMessage(
            "BREACH REJECTED",
            string.IsNullOrWhiteSpace(reason) ? "SERVER VALIDATION FAILED" : reason,
            SafeNetworkLabel(networkDisplayName),
            "RECONNECTING..."
        );
    }

    public void ShowFailure(string networkDisplayName)
    {
        ShowMessage(
            "TRACE DETECTED",
            "SECURITY ALARM TRIGGERED",
            SafeNetworkLabel(networkDisplayName),
            string.Empty
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
                "LAPTOP UI ERROR",
                "MINIGAME ROOT IS NOT ASSIGNED",
                SafeNetworkLabel(context.NetworkDisplayName),
                string.Empty
            );
            return false;
        }

        minigameRoot.gameObject.SetActive(true);

        _activeMinigameObject = Instantiate(
            definition.UiPrefab,
            minigameRoot,
            worldPositionStays: false
        );

        FitMinigameToHost(_activeMinigameObject);

        _activeMinigame =
            _activeMinigameObject.GetComponent<LaptopMinigameBase>()
            ?? _activeMinigameObject.GetComponentInChildren<LaptopMinigameBase>(true);

        if (_activeMinigame == null)
        {
            Debug.LogError(
                $"[LaptopScreenUI] Minigame prefab '{definition.UiPrefab.name}' "
                    + "does not contain a LaptopMinigameBase component.",
                definition.UiPrefab
            );

            Destroy(_activeMinigameObject);
            _activeMinigameObject = null;
            minigameRoot.gameObject.SetActive(false);

            ShowMessage(
                "MINIGAME PREFAB ERROR",
                "LAPTOPMINIGAMEBASE COMPONENT MISSING",
                SafeNetworkLabel(context.NetworkDisplayName),
                string.Empty
            );
            return false;
        }

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

    public void AbortActiveMinigame()
    {
        if (_activeMinigame != null)
        {
            _activeMinigame.Completed -= OnActiveMinigameCompleted;
            _activeMinigame.Failed -= OnActiveMinigameFailed;
            _activeMinigame.AlarmTriggered -= OnActiveMinigameAlarmTriggered;
            _activeMinigame.ActionPerformed -= OnActiveMinigameActionPerformed;
            _activeMinigame.Abort();
        }

        _activeMinigame = null;
        _completedCallback = null;
        _failedCallback = null;
        _alarmTriggeredCallback = null;
        _actionPerformedCallback = null;

        if (_activeMinigameObject != null)
            Destroy(_activeMinigameObject);

        _activeMinigameObject = null;

        if (minigameRoot != null)
            minigameRoot.gameObject.SetActive(false);
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
        string title,
        string status,
        string target,
        string prompt
    )
    {
        AbortActiveMinigame();
        SetMessageObjectsVisible(true);

        SetText(titleText, title);
        SetText(statusText, status);
        SetText(targetText, target);
        SetText(promptText, prompt);
    }

    private void SetMessageObjectsVisible(bool visible)
    {
        SetTextObjectVisible(titleText, visible);
        SetTextObjectVisible(statusText, visible);
        SetTextObjectVisible(targetText, visible);
        SetTextObjectVisible(promptText, visible);
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

using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class MatchResultHUD : MonoBehaviour
{
    [Header("Scene UI")]
    [Tooltip("Full-screen result panel. This should be a sibling of GameUI and SpectatorUI under the main Canvas.")]
    [SerializeField]
    private GameObject resultRoot;

    [SerializeField]
    private TMP_Text resultTitleText;

    [SerializeField]
    private TMP_Text resultMessageText;

    [Tooltip("Optional text such as 'Returning to lobby in 5'.")]
    [SerializeField]
    private TMP_Text returnCountdownText;

    [SerializeField]
    private GameObject gameUIRoot;

    [SerializeField]
    private GameObject spectatorUIRoot;

    [Header("Object Names (automatic fallback)")]
    [SerializeField]
    private string resultRootName = "MatchResultUI";

    [SerializeField]
    private string resultTitleTextName = "MatchResultTitleText";

    [SerializeField]
    private string resultMessageTextName = "MatchResultMessageText";

    [SerializeField]
    private string returnCountdownTextName = "MatchResultCountdownText";

    [SerializeField]
    private string gameUIRootName = "GameUI";

    [SerializeField]
    private string spectatorUIRootName = "SpectatorUI";

    [Header("Result Text")]
    [SerializeField]
    private string survivorsWonTitle = "THE SURVIVORS ESCAPED";

    [SerializeField]
    private string survivorsWonMessage = "The facility has been breached.";

    [SerializeField]
    private string badGuyWonTitle = "THE BAD GUY WON";

    [SerializeField]
    private string badGuyWonMessage = "Every hacker has been eliminated.";

    [SerializeField]
    private string abortedTitle = "MATCH ABORTED";

    [SerializeField]
    private string abortedMessage = "A required player disconnected.";

    [SerializeField]
    private string returningToLobbyFormat = "Returning to lobby in {0}";

    [Header("Binding")]
    [SerializeField]
    private float rebindInterval = 0.25f;

    private MatchFlowManager boundMatchFlow;
    private MatchResultType displayedResult = MatchResultType.None;
    private float rebindTimer;

    private void Awake()
    {
        ResolveSceneReferences();
        HideResultImmediate();
    }

    private void OnEnable()
    {
        rebindTimer = 0f;
        TryBindMatchFlow();
        RefreshFromMatchFlow(force: true);
    }

    private void OnDisable()
    {
        UnbindMatchFlow();
    }

    private void Update()
    {
        rebindTimer -= Time.unscaledDeltaTime;

        if (rebindTimer <= 0f)
        {
            rebindTimer = Mathf.Max(0.05f, rebindInterval);

            MatchFlowManager expected = LobbyManager.Instance != null
                ? LobbyManager.Instance.MatchFlow
                : null;

            if (boundMatchFlow != expected)
                TryBindMatchFlow();

            if (resultRoot == null || resultTitleText == null)
                ResolveSceneReferences();
        }

        RefreshFromMatchFlow(force: false);

        if (displayedResult != MatchResultType.None)
            EnforceResultVisibility();

        RefreshReturnCountdown();
    }

    private void TryBindMatchFlow()
    {
        MatchFlowManager next = LobbyManager.Instance != null
            ? LobbyManager.Instance.MatchFlow
            : null;

        if (boundMatchFlow == next)
            return;

        UnbindMatchFlow();
        boundMatchFlow = next;

        if (boundMatchFlow != null)
            boundMatchFlow.LocalMatchResultChanged += HandleLocalMatchResultChanged;
    }

    private void UnbindMatchFlow()
    {
        if (boundMatchFlow != null)
            boundMatchFlow.LocalMatchResultChanged -= HandleLocalMatchResultChanged;

        boundMatchFlow = null;
    }

    private void HandleLocalMatchResultChanged(
        MatchResultType result,
        double presentationEndsServerTime
    )
    {
        ApplyResult(result);
    }

    private void RefreshFromMatchFlow(bool force)
    {
        MatchResultType current = boundMatchFlow != null
            ? boundMatchFlow.CurrentMatchResult
            : MatchResultType.None;

        if (!force && current == displayedResult)
            return;

        ApplyResult(current);
    }

    private void ApplyResult(MatchResultType result)
    {
        displayedResult = result;

        if (result == MatchResultType.None)
        {
            HideResultImmediate();
            return;
        }

        ResolveSceneReferences();

        if (gameUIRoot != null)
            gameUIRoot.SetActive(false);

        if (spectatorUIRoot != null)
            spectatorUIRoot.SetActive(false);

        if (resultRoot != null)
            resultRoot.SetActive(true);

        switch (result)
        {
            case MatchResultType.SurvivorsWon:
                SetResultText(survivorsWonTitle, survivorsWonMessage);
                break;

            case MatchResultType.BadGuyWon:
                SetResultText(badGuyWonTitle, badGuyWonMessage);
                break;

            case MatchResultType.Aborted:
                SetResultText(abortedTitle, abortedMessage);
                break;

            default:
                SetResultText(string.Empty, string.Empty);
                break;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void EnforceResultVisibility()
    {
        if (gameUIRoot != null && gameUIRoot.activeSelf)
            gameUIRoot.SetActive(false);

        if (spectatorUIRoot != null && spectatorUIRoot.activeSelf)
            spectatorUIRoot.SetActive(false);

        if (resultRoot != null && !resultRoot.activeSelf)
            resultRoot.SetActive(true);
    }

    private void SetResultText(string title, string message)
    {
        if (resultTitleText != null)
            resultTitleText.text = title;

        if (resultMessageText != null)
            resultMessageText.text = message;
    }

    private void RefreshReturnCountdown()
    {
        if (returnCountdownText == null)
            return;

        if (displayedResult == MatchResultType.None || boundMatchFlow == null)
        {
            returnCountdownText.text = string.Empty;
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null || boundMatchFlow.MatchResultEndsServerTime <= 0d)
        {
            returnCountdownText.text = string.Empty;
            return;
        }

        double remaining = Math.Max(
            0d,
            boundMatchFlow.MatchResultEndsServerTime - nm.ServerTime.Time
        );

        int seconds = Mathf.Max(0, Mathf.CeilToInt((float)remaining));
        returnCountdownText.text = string.Format(returningToLobbyFormat, seconds);
    }

    private void HideResultImmediate()
    {
        if (resultRoot != null)
            resultRoot.SetActive(false);

        if (returnCountdownText != null)
            returnCountdownText.text = string.Empty;
    }

    private void ResolveSceneReferences()
    {
        Transform searchRoot = transform;

        if (resultRoot == null)
        {
            Transform found = FindDescendantByName(searchRoot, resultRootName);
            if (found != null)
                resultRoot = found.gameObject;
        }

        if (gameUIRoot == null)
        {
            Transform found = FindDescendantByName(searchRoot, gameUIRootName);
            if (found != null)
                gameUIRoot = found.gameObject;
        }

        if (spectatorUIRoot == null)
        {
            Transform found = FindDescendantByName(searchRoot, spectatorUIRootName);
            if (found != null)
                spectatorUIRoot = found.gameObject;
        }

        if (resultRoot == null)
            return;

        if (resultTitleText == null)
            resultTitleText = FindTmpTextByName(resultRoot.transform, resultTitleTextName);

        if (resultMessageText == null)
            resultMessageText = FindTmpTextByName(resultRoot.transform, resultMessageTextName);

        if (returnCountdownText == null)
            returnCountdownText = FindTmpTextByName(resultRoot.transform, returnCountdownTextName);
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendantByName(root.GetChild(i), targetName);

            if (found != null)
                return found;
        }

        return null;
    }

    private static TMP_Text FindTmpTextByName(Transform root, string targetName)
    {
        Transform found = FindDescendantByName(root, targetName);
        return found != null ? found.GetComponent<TMP_Text>() : null;
    }
}

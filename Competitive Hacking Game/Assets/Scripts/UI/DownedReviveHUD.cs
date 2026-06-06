using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class DownedReviveHUD : MonoBehaviour
{
    [Header("Downed Player UI")]
    [SerializeField]
    private GameObject downedRoot;

    [SerializeField]
    private TMP_Text downedTitleText;

    [SerializeField]
    private TMP_Text downedMessageText;

    [Header("Revive Prompt UI")]
    [SerializeField]
    private GameObject reviveRoot;

    [SerializeField]
    private TMP_Text revivePromptText;

    [SerializeField]
    private GameObject reviveProgressRoot;

    [SerializeField]
    private Image reviveProgressFill;

    [SerializeField]
    private TMP_Text reviveProgressText;

    [Header("Text")]
    [SerializeField]
    private string downedTitle = "DOWNED";

    [SerializeField]
    private string downedMessage = "Call for help! Wait for another survivor to revive you.";

    [SerializeField]
    private string revivePrompt = "HOLD E TO REVIVE";

    [SerializeField]
    private string revivingPrompt = "REVIVING...";

    [Header("Update")]
    [SerializeField]
    private float localPlayerRefreshInterval = 0.25f;

    private PlayerLifeState localLifeState;
    private PlayerReviver localReviver;
    private float localPlayerRefreshTimer;

    private void Awake()
    {
        HideAll();
    }

    private void OnEnable()
    {
        localPlayerRefreshTimer = 0f;
        ResolveLocalPlayerReferences();
        Refresh();
    }

    private void Update()
    {
        localPlayerRefreshTimer -= Time.deltaTime;

        if (localPlayerRefreshTimer <= 0f)
        {
            localPlayerRefreshTimer = localPlayerRefreshInterval;

            if (!HasValidLocalReferences())
                ResolveLocalPlayerReferences();
        }

        Refresh();
    }

    private bool HasValidLocalReferences()
    {
        if (localLifeState == null && localReviver == null)
            return false;

        if (localLifeState != null && !localLifeState.IsSpawned)
            return false;

        if (localReviver != null && !localReviver.IsSpawned)
            return false;

        return true;
    }

    private void ResolveLocalPlayerReferences()
    {
        localLifeState = null;
        localReviver = null;

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
            return;

        NetworkObject localPlayerObject = networkManager.LocalClient?.PlayerObject;

        if (localPlayerObject == null)
            return;

        localLifeState = localPlayerObject.GetComponent<PlayerLifeState>();
        localReviver = localPlayerObject.GetComponent<PlayerReviver>();
    }

    private void Refresh()
    {
        bool showDowned = localLifeState != null && localLifeState.IsDowned;
        bool showRevive = !showDowned && localReviver != null && localReviver.HasReviveTarget;

        RefreshDownedUI(showDowned);
        RefreshReviveUI(showRevive);
    }

    private void RefreshDownedUI(bool show)
    {
        if (downedRoot != null)
            downedRoot.SetActive(show);

        if (!show)
            return;

        if (downedTitleText != null)
            downedTitleText.text = downedTitle;

        if (downedMessageText != null)
            downedMessageText.text = downedMessage;
    }

    private void RefreshReviveUI(bool show)
    {
        if (reviveRoot != null)
            reviveRoot.SetActive(show);

        if (!show)
        {
            ClearReviveProgress();
            return;
        }

        float progress = localReviver != null ? localReviver.ReviveProgress01 : 0f;
        bool isReviving = localReviver != null && localReviver.IsHoldingRevive && progress > 0f;

        if (revivePromptText != null)
            revivePromptText.text = isReviving ? revivingPrompt : revivePrompt;

        if (reviveProgressRoot != null)
            reviveProgressRoot.SetActive(isReviving);

        if (reviveProgressFill != null)
            reviveProgressFill.fillAmount = progress;

        if (reviveProgressText != null)
            reviveProgressText.text = isReviving ? $"{Mathf.RoundToInt(progress * 100f)}%" : "";
    }

    private void ClearReviveProgress()
    {
        if (reviveProgressRoot != null)
            reviveProgressRoot.SetActive(false);

        if (reviveProgressFill != null)
            reviveProgressFill.fillAmount = 0f;

        if (reviveProgressText != null)
            reviveProgressText.text = "";
    }

    private void HideAll()
    {
        if (downedRoot != null)
            downedRoot.SetActive(false);

        if (reviveRoot != null)
            reviveRoot.SetActive(false);

        ClearReviveProgress();
    }
}

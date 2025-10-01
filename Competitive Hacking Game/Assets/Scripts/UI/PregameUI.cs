using UnityEngine;
using TMPro;

public class PregameUI : MonoBehaviour
{
    public static PregameUI Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI countdownText;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        Hide();
    }

    public void SetPregameUI()
    {
        var lobby = LobbyManager.Instance.GetLobby();
        if (lobby != null && lobbyCodeText != null)
            lobbyCodeText.text = lobby.Name + ": " + lobby.LobbyCode;

        PregameLobbyNetwork.Instance?.PushStateToUI();

        if (lobby != null && PregameLobbyNetwork.Instance != null)
            Show();
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }

    // Called by PregameLobbyNetwork on variable changes
    public void UpdatePlayerCount(int current, int max)
    {
        if (playerCountText != null) playerCountText.text = $"{current}/{max}";
    }

    public void UpdateStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    public void UpdateCountdown(int seconds)
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = seconds.ToString();
        }
    }

    public void HideCountdown()
    {
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }
}

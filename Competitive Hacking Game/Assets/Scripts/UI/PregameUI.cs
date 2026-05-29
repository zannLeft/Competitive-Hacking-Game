using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class PregameUI : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI lobbyCodeText;

    [SerializeField]
    private TextMeshProUGUI startStatusText;

    [SerializeField]
    private float refreshInterval = 0.25f;

    private float _timer;

    public void SetPregameUI()
    {
        Lobby lobby = LobbyManager.Instance != null ? LobbyManager.Instance.GetLobby() : null;

        if (lobbyCodeText != null)
        {
            if (lobby != null)
                lobbyCodeText.text = lobby.Name + ": " + lobby.LobbyCode;
            else
                lobbyCodeText.text = "";
        }

        RefreshStartStatus();
    }

    public void Show()
    {
        gameObject.SetActive(true);
        SetPregameUI();
    }

    private void Update()
    {
        _timer -= Time.deltaTime;

        if (_timer > 0f)
            return;

        _timer = refreshInterval;
        RefreshStartStatus();
    }

    private void RefreshStartStatus()
    {
        if (startStatusText == null)
            return;

        if (LobbyManager.Instance == null)
        {
            startStatusText.text = "";
            return;
        }

        startStatusText.text = LobbyManager.Instance.GetStartRequirementText();
    }
}
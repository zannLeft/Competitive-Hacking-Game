using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class PregameUI : MonoBehaviour {

    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    


    private void Start() {
        Hide();
    }

    public void SetPregameUI() {
        Lobby lobby = LobbyManager.Instance.GetLobby();
        lobbyCodeText.text = lobby.Name + ": " + lobby.LobbyCode;
    }

    public void Show() {
        gameObject.SetActive(true);
    }

    private void Hide() {
        gameObject.SetActive(false);
    }
}

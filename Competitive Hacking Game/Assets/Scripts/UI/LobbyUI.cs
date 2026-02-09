using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private Button exitButton;
    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Button quickJoinButton;
    [SerializeField] private Button joinCodeButton;
    [SerializeField] private TMP_InputField joinCodeInputField;

    [SerializeField] private LobbyCreateUI lobbyCreateUI;

    [SerializeField] private Transform lobbyContainer;
    [SerializeField] private Transform lobbyTemplate;

    private void Awake()
    {
        exitButton.onClick.AddListener(Application.Quit);

        createLobbyButton.onClick.AddListener(() =>
        {
            lobbyCreateUI.Show();
        });

        quickJoinButton.onClick.AddListener(() =>
        {
            LobbyManager.Instance.QuickJoin();
        });

        joinCodeButton.onClick.AddListener(() =>
        {
            LobbyManager.Instance.JoinWithCode(joinCodeInputField.text);
        });

        if (lobbyTemplate != null)
            lobbyTemplate.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Start visually empty (subscription happens in OnEnable)
        UpdateLobbyList(new List<Lobby>());
    }

    private void OnEnable()
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.OnLobbyListChanged += LobbyManager_OnLobbyListChanged;

        // Optional: start with an empty list when shown
        UpdateLobbyList(new List<Lobby>());
    }

    private void OnDisable()
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.OnLobbyListChanged -= LobbyManager_OnLobbyListChanged;
    }

    private void LobbyManager_OnLobbyListChanged(object sender, LobbyManager.OnLobbyListChangedEventArgs e)
    {
        // Debug.Log("[LobbyUI] Lobby list changed: " + (e.lobbyList != null ? e.lobbyList.Count : 0));
        UpdateLobbyList(e.lobbyList);
    }

    private void UpdateLobbyList(List<Lobby> lobbyList)
    {
        if (lobbyContainer == null || lobbyTemplate == null) return;
        if (!isActiveAndEnabled) return; // avoid touching UI while it’s inactive

        // Remove old rows (keep the template)
        for (int i = lobbyContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = lobbyContainer.GetChild(i);
            if (child == lobbyTemplate) continue;
            Destroy(child.gameObject);
        }

        if (lobbyList == null) return;

        // Add rows
        foreach (var lobby in lobbyList)
        {
            Transform row = Instantiate(lobbyTemplate, lobbyContainer);
            row.gameObject.SetActive(true);
            row.GetComponent<LobbyListSingleUI>().SetLobby(lobby);
        }
    }
}

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private GameObject root;              // The top-level canvas/panel to toggle
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private Button endMatchButton;        // Host-only, match-only

    public static PauseMenuUI Instance { get; private set; }
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        resumeButton.onClick.AddListener(Resume);
        leaveLobbyButton.onClick.AddListener(Leave);

        if (endMatchButton != null)
            endMatchButton.onClick.AddListener(OnEndMatchClicked);

        root.SetActive(false);
    }

    public void Toggle()
    {
        if (isOpen) Resume();
        else Show();
    }

    private void Show()
    {
        isOpen = true;
        root.SetActive(true);

        RefreshButtons();

        // Show mouse for UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Disable only local gameplay inputs
        var playerObj = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (playerObj != null)
        {
            var input = playerObj.GetComponent<InputManager>();
            if (input != null) input.SetGameplayEnabled(false);
        }
    }

    private void RefreshButtons()
    {
        if (endMatchButton == null) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        bool inMatch = LobbyManager.Instance != null && LobbyManager.Instance.IsMatchInProgress;

        // End Match button should appear only for host during match
        endMatchButton.gameObject.SetActive(isHost && inMatch);
    }

    public void Resume()
    {
        isOpen = false;
        root.SetActive(false);

        var playerObj = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (playerObj != null)
        {
            var input = playerObj.GetComponent<InputManager>();
            if (input != null) input.SetGameplayEnabled(true);
        }

        // Lock mouse back for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Leave()
    {
        // Close UI immediately
        isOpen = false;
        root.SetActive(false);

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        bool inMatch = LobbyManager.Instance != null && LobbyManager.Instance.IsMatchInProgress;

        if (isHost && inMatch)
        {
            // Host leaves during match: kick everyone out by shutting down session
            LobbyManager.Instance.EndGameToLobbyForEveryone();
        }
        else
        {
            // Otherwise: leave lobby normally (host deletes lobby, client removes self)
            LobbyManager.Instance.LeaveToLobbySelect();
        }

        // After leaving, we want cursor for menus (LobbySceneController will also enforce this)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnEndMatchClicked()
    {
        // Host-only: end match but keep everyone connected, return to waiting rooftop
        LobbyManager.Instance?.EndMatch();

        // Close pause right away after clicking
        isOpen = false;
        root.SetActive(false);
    }
}

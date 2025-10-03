using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private GameObject root;              // The top-level canvas/panel to toggle
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private Button endMatchButton;        // New button

    public static PauseMenuUI Instance { get; private set; }
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        resumeButton.onClick.AddListener(Resume);
        leaveLobbyButton.onClick.AddListener(Leave);

        // End match button only for host in GameScene
        if (endMatchButton != null)
        {
            endMatchButton.onClick.AddListener(OnEndMatchClicked);
            endMatchButton.gameObject.SetActive(
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsHost &&
                SceneManager.GetActiveScene().name == "GameScene"
            );
        }

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
        var current = SceneManager.GetActiveScene().name;
        if (current == "GameScene")
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                // Host ends the match for everyone
                LobbyManager.Instance.EndGameToLobbyForEveryone();
            }
            else
            {
                // Client leaves only themselves
                LobbyManager.Instance.LeaveToLobbySelect();
            }
        }
        else
        {
            isOpen = false;
            root.SetActive(false);
            // In LobbyScene, "Leave" just leaves the lobby
            Debug.Log("ok bitch you clicked this i'm going to run LeaveToLobbySelect");
            LobbyManager.Instance.LeaveToLobbySelect();
        }

        // In lobby UI we want the mouse
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnEndMatchClicked()
    {
        if (LobbyManager.Instance != null)
        {
            // Call the new EndMatch method that unlocks lobby, sets state, switches scene, and unpauses
            LobbyManager.Instance.EndMatch();
        }
    }
}

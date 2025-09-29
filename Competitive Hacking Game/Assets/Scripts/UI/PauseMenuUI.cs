using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private GameObject root;              // The top-level canvas/panel to toggle
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button leaveLobbyButton;

    public static PauseMenuUI Instance { get; private set; }
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        resumeButton.onClick.AddListener(Resume);
        leaveLobbyButton.onClick.AddListener(Leave);
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
        // Close UI first
        isOpen = false;
        root.SetActive(false);

        LobbyManager.Instance.LeaveToLobbySelect();

        // In lobby UI we want the mouse
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

using Unity.Netcode;
using UnityEngine;

public class LobbySceneController : MonoBehaviour
{
    [Header("Menu (Main Menu)")]
    [SerializeField]
    private GameObject lobbyCamera; // your overview camera GO

    [SerializeField]
    private GameObject lobbyUI; // main menu UI root (lobby list etc.)

    [SerializeField]
    private GameObject lobbyCreateUI; // create panel root (optional, can be child of lobbyUI)

    [Header("Waiting Playground (In Lobby)")]
    [SerializeField]
    private PregameUI pregameUI; // your waiting UI (code + players + start prompt)

    private bool _lastInWaitingState;

    private void Start()
    {
        ApplyState(force: true);
    }

    private void Update()
    {
        ApplyState(force: false);
    }

    private void ApplyState(bool force)
    {
        bool inWaiting = IsInWaitingState();

        if (!force && inWaiting == _lastInWaitingState)
            return;

        _lastInWaitingState = inWaiting;

        if (inWaiting)
            SetWaitingPlaygroundState();
        else
            SetMainMenuState();
    }

    private bool IsInWaitingState()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return false;
        if (!nm.IsConnectedClient)
            return false;

        // local player exists only when we’re actually “in the lobby session”
        return nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
    }

    private void SetMainMenuState()
    {
        if (lobbyCamera != null)
            lobbyCamera.SetActive(true);

        if (lobbyUI != null)
            lobbyUI.SetActive(true);
        if (lobbyCreateUI != null)
            lobbyCreateUI.SetActive(false);

        if (pregameUI != null)
            pregameUI.gameObject.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void SetWaitingPlaygroundState()
    {
        if (lobbyCamera != null)
            lobbyCamera.SetActive(false);

        if (lobbyUI != null)
            lobbyUI.SetActive(false);
        if (lobbyCreateUI != null)
            lobbyCreateUI.SetActive(false);

        if (pregameUI != null)
        {
            pregameUI.gameObject.SetActive(true);
            pregameUI.SetPregameUI();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}

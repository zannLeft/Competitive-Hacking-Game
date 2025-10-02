using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PregameLobbyNetwork : NetworkBehaviour
{
    public static PregameLobbyNetwork Instance { get; private set; }

    // ----------------- Networked state -----------------
    public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // -1 = inactive; >=0 = countdown seconds remaining
    public NetworkVariable<int> Countdown = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ----------------- Config -----------------
    [SerializeField] private string gameSceneName = "GameScene";   // set in Inspector
    [SerializeField] private int startCountdownSeconds = 5;        // tweakable

    private Coroutine countdownCoroutine;

    // ----------------- Lifecycle -----------------
    public override void OnNetworkSpawn()
    {
        Instance = this;

        // Server keeps PlayerCount up to date
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsList.Count;
        }

        // Everyone subscribes to scene-load notifications so we can show/hide the loading overlay
        if (NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.OnLoad += OnSceneLoadStarted;
            NetworkManager.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
        }

        // UI reflections while in lobby scene
        PlayerCount.OnValueChanged += (oldV, newV) => UpdateUI_PlayerCount(newV);
        Countdown.OnValueChanged   += (oldV, newV) => UpdateUI_Countdown(newV);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.OnLoad -= OnSceneLoadStarted;
            NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
        }

        if (Instance == this) Instance = null;
    }

    // ----------------- Player count tracking -----------------
    private void OnClientConnected(ulong _)
    {
        PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsList.Count;
    }

    private void OnClientDisconnected(ulong _)
    {
        PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsList.Count;

        // Optional: if only the host remains, cancel countdown
        if (Countdown.Value > 0 &&
            NetworkManager.Singleton.IsServer &&
            NetworkManager.Singleton.ConnectedClientsList.Count <= 1)
        {
            CancelCountdownServerSide();
        }
    }

    // ----------------- Host "ENTER" to start / cancel -----------------
    [ServerRpc(RequireOwnership = false)]
    public void ToggleStartServerRpc(ServerRpcParams rpcParams = default)
    {
        // Only the host's client may control the countdown
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId) return;

        // Require at least 2 players
        if (PlayerCount.Value < 1) return;

        if (Countdown.Value <= 0) StartCountdownServerSide(startCountdownSeconds);
        else                      CancelCountdownServerSide();
    }

    private void StartCountdownServerSide(int seconds)
    {
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(Co_Countdown(seconds));
    }

    private void CancelCountdownServerSide()
    {
        if (countdownCoroutine != null) { StopCoroutine(countdownCoroutine); countdownCoroutine = null; }
        Countdown.Value = -1;
    }

    private IEnumerator Co_Countdown(int seconds)
    {
        Countdown.Value = seconds;

        while (Countdown.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            Countdown.Value -= 1;
        }

        // Briefly show 0 ("Starting now")
        yield return new WaitForSeconds(0.1f);

        // Server: kick off the network scene load (single mode)
        if (IsServer)
        {
            // Show the full-screen overlay with "Loading..." before switching
            ConnectingOverlayUI.Instance?.Show("Loading...");
            NetworkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }

        // Reset local countdown state
        Countdown.Value = -1;
        countdownCoroutine = null;
    }

    // ----------------- Scene load overlay + post-load role pick -----------------
    // Fires for server and all clients when a load begins
    private void OnSceneLoadStarted(ulong clientId, string sceneName, LoadSceneMode mode, AsyncOperation op)
    {
        if (sceneName == gameSceneName)
        {
            // Ensure we show the overlay as soon as the load starts locally
            ConnectingOverlayUI.Instance?.Show("Loading...");
        }
    }

    // Fires (once) when *all* clients have finished the load
    private void OnSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameSceneName) return;

        // Hide the overlay now that everyone is in the game scene
        ConnectingOverlayUI.Instance?.Hide();

        // Enable the in-game HUD canvas if present
        TryEnableGameUI();

        // Server: choose exactly one "bad guy" and publish the flag
        if (IsServer) AssignRandomBadGuy();
    }

    private void AssignRandomBadGuy()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients == null || clients.Count == 0) return;

        int pick = UnityEngine.Random.Range(0, clients.Count);
        ulong badId = clients[pick].ClientId;

        foreach (var c in clients)
        {
            var setup = c.PlayerObject ? c.PlayerObject.GetComponent<PlayerSetup>() : null;
            if (setup != null)
                setup.IsBadGuy.Value = (c.ClientId == badId);
        }
    }

    private void TryEnableGameUI()
    {
        // Looks for a GameObject named "gameUI" (recommended) or "GameUI" and enables it.
        var go = GameObject.Find("gameUI");
        if (go == null) go = GameObject.Find("GameUI");
        if (go != null && !go.activeSelf) go.SetActive(true);
    }

    // ----------------- Lobby (pre-game) UI mirroring -----------------
    private void UpdateUI_PlayerCount(int newCount)
    {
        // If countdown is running, keep the current "Starting..." status
        if (Countdown.Value > 0) return;

        // Use LobbyManager's max if available
        int max = LobbyManager.Instance != null ? LobbyManager.Instance.MaxPlayers : 5;
        PregameUI.Instance?.UpdatePlayerCount(newCount, max);

        string status = "Waiting for players";
        if (newCount > 1)
        {
            bool amHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            status = amHost ? "Press ENTER to start" : "Waiting for host to start";
        }
        PregameUI.Instance?.UpdateStatus(status);
    }

    private void UpdateUI_Countdown(int newVal)
    {
        bool amHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (newVal < 0)
        {
            PregameUI.Instance?.HideCountdown();
            UpdateUI_PlayerCount(PlayerCount.Value);
        }
        else if (newVal == 0)
        {
            PregameUI.Instance?.UpdateCountdown(0);
            PregameUI.Instance?.UpdateStatus("Starting now");
        }
        else
        {
            PregameUI.Instance?.UpdateCountdown(newVal);
            PregameUI.Instance?.UpdateStatus(amHost ? "Starting... Press ENTER to cancel" : "Starting...");
        }
    }

    // ----------------- Public helper for UI bootstrap -----------------
    public void PushStateToUI()
    {
        UpdateUI_PlayerCount(PlayerCount.Value);
        UpdateUI_Countdown(Countdown.Value);
    }
}

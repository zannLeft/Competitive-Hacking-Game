using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PregameLobbyNetwork : NetworkBehaviour
{
    public static PregameLobbyNetwork Instance { get; private set; }

    // Server-authoritative. Everyone can read.
    public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // -1 means inactive. 0..n means countdown seconds left.
    public NetworkVariable<int> Countdown = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine countdownCoroutine;
    private const int MAX_PLAYERS = 5;
    private const int START_SECONDS = 5;

    public override void OnNetworkSpawn()
    {
        Instance = this;

        // server keeps PlayerCount updated
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsList.Count;
        }

        // update local UI whenever the vars change
        PlayerCount.OnValueChanged += (oldV, newV) => UpdateUIPlayerCount(newV);
        Countdown.OnValueChanged += (oldV, newV) => UpdateUICountdown(newV);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this) Instance = null;
    }

    private void OnClientConnected(ulong _)
    {
        PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsList.Count;
    }

    private void OnClientDisconnected(ulong _)
    {
        PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsList.Count;

        // Optional: if host left, cancel countdown
        if (Countdown.Value > 0 && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClientsList.Count <= 1)
        {
            CancelCountdownServerSide();
        }
    }

    // Called from clients when ENTER pressed. RequireOwnership = false so any client can call
    // Server checks that the sender is the host before acting.
    [ServerRpc(RequireOwnership = false)]
    public void ToggleStartServerRpc(ServerRpcParams rpcParams = default)
    {
        // Only allow the host (server-client) to control the countdown
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.Singleton.LocalClientId) return;

        // Require at least 2 players
        if (PlayerCount.Value < 2) return;

        if (Countdown.Value <= 0)
        {
            StartCountdownServerSide(START_SECONDS);
        }
        else
        {
            CancelCountdownServerSide();
        }
    }

    private void StartCountdownServerSide(int seconds)
    {
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(CountdownCoroutine(seconds));
    }

    private void CancelCountdownServerSide()
    {
        if (countdownCoroutine != null) { StopCoroutine(countdownCoroutine); countdownCoroutine = null; }
        Countdown.Value = -1;
    }

    private IEnumerator CountdownCoroutine(int seconds)
    {
        Countdown.Value = seconds;
        while (Countdown.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            Countdown.Value = Countdown.Value - 1;
        }

        // reached 0
        Debug.Log("loading scene...");
        // reset
        yield return new WaitForSeconds(0.1f);
        Countdown.Value = -1;
        countdownCoroutine = null;
    }

    // Helper: update UI text based on changes. Safe no-op if UI not present yet.
    private void UpdateUIPlayerCount(int newCount)
    {
        // If a countdown is running, don't overwrite the status text here.
        if (Countdown.Value > 0)
        {
            // keep the current "Starting..." status while countdown runs
            return;
        }

        PregameUI.Instance?.UpdatePlayerCount(newCount, MAX_PLAYERS);

        string status = "Waiting for players";
        if (newCount > 1)
        {
            bool amHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            status = amHost ? "Press ENTER to start" : "Waiting for host to start";
        }
        PregameUI.Instance?.UpdateStatus(status);
    }

    private void UpdateUICountdown(int newVal)
    {
        // Determine if this local client is the host
        bool amHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (newVal < 0)
        {
            // stop showing countdown and restore status
            PregameUI.Instance?.HideCountdown();
            UpdateUIPlayerCount(PlayerCount.Value);
        }
        else if (newVal == 0)
        {
            // show 0 briefly and a final message
            PregameUI.Instance?.UpdateCountdown(0);
            PregameUI.Instance?.UpdateStatus("Starting now");
        }
        else
        {
            // show numeric countdown
            PregameUI.Instance?.UpdateCountdown(newVal);

            // Host gets the cancel hint, clients just see a neutral message
            if (amHost)
                PregameUI.Instance?.UpdateStatus("Starting... Press ENTER to cancel");
            else
                PregameUI.Instance?.UpdateStatus("Starting...");
        }
    }

    // Call from UI when the UI is created to push current state into it (safe if Instance null)
    public void PushStateToUI()
    {
        UpdateUIPlayerCount(PlayerCount.Value);
        UpdateUICountdown(Countdown.Value);
    }
}

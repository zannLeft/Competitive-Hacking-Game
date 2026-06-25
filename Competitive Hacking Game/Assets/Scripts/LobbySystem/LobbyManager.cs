using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Config")]
    [SerializeField]
    private int maxPlayers = 5;

    [SerializeField]
    private int minPlayersToStart = 2;

    [Header("Debug")]
    [SerializeField]
    private bool allowF1SoloStart = true;

    [Header("Scene Names")]
    [SerializeField]
    private string lobbySceneName = "MainScene";

    [SerializeField]
    private string interiorSceneName = "Interior_01";

    [SerializeField]
    private string cityBaseSceneName = "City_Base";

    [Header("Teleport Tags")]
    [SerializeField]
    private string lobbySpawnTag = "LobbySpawn";

    [SerializeField]
    private string interiorSpawnTag = "InteriorSpawn";

    public LobbyServicesFacade Services { get; private set; }
    public RelayFacade Relay { get; private set; }
    public NetworkSessionManager Session { get; private set; }
    public TeleportService Teleport { get; private set; }
    public MatchFlowManager MatchFlow { get; private set; }
    public RoundRoleManager RoundRoles { get; private set; }
    public RoundResetManager RoundReset { get; private set; }
    public CosmeticsManager Cosmetics { get; private set; }
    public LobbySceneUIController SceneUI { get; private set; }

    public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;

    private Coroutine _hostLobbyTeleportCoroutine;

    public class OnLobbyListChangedEventArgs : EventArgs
    {
        public System.Collections.Generic.List<Lobby> lobbyList;
    }

    public bool IsMatchInProgress => MatchFlow != null && MatchFlow.IsMatchInProgress;
    public int MinPlayersToStart => Mathf.Max(1, minPlayersToStart);
    public int ConnectedPlayerCount => GetConnectedPlayerCount();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Services = GetOrAdd<LobbyServicesFacade>();
        Relay = GetOrAdd<RelayFacade>();
        Session = GetOrAdd<NetworkSessionManager>();
        Teleport = GetOrAdd<TeleportService>();
        RoundRoles = GetOrAdd<RoundRoleManager>();
        RoundReset = GetOrAdd<RoundResetManager>();
        MatchFlow = GetOrAdd<MatchFlowManager>();
        Cosmetics = GetOrAdd<CosmeticsManager>();
        SceneUI = GetOrAdd<LobbySceneUIController>();

        Services.SetLobbySceneName(lobbySceneName);
        MatchFlow.Configure(interiorSceneName, lobbySpawnTag, interiorSpawnTag);
        SceneUI.Configure(lobbySceneName, interiorSceneName);

        Services.LobbyListChanged += HandleLobbyListChanged;

        Session.ServerClientConnected += OnServerClientConnected;
        Session.LocalClientDisconnected += OnLocalClientDisconnected;

        SceneManager.sceneLoaded += OnSceneLoaded;

        Services.InitializeUnityAuthentication();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (Services != null)
            Services.LobbyListChanged -= HandleLobbyListChanged;

        if (Session != null)
        {
            Session.ServerClientConnected -= OnServerClientConnected;
            Session.LocalClientDisconnected -= OnLocalClientDisconnected;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == lobbySceneName)
            SceneUI.RebindIfNeeded(lobbySceneName);
    }

    private void Update()
    {
        bool inLobbyScene = SceneManager.GetActiveScene().name == lobbySceneName;
        Services.Tick(Time.deltaTime, inLobbyScene);

        HandleDebugSoloStartInput();
    }

    private void HandleDebugSoloStartInput()
    {
        if (!allowF1SoloStart)
            return;

        if (Keyboard.current == null || !Keyboard.current.f1Key.wasPressedThisFrame)
            return;

        if (Session == null || !Session.IsHost)
        {
            Debug.Log("[LobbyManager] DEBUG SOLO START ignored: this instance is not the host.");
            return;
        }

        StartGameAsHostInternal(ignorePlayerMinimum: true);
    }

    public Lobby GetLobby() => Services.CurrentLobby;

    public async void CreateLobby(string lobbyName, bool isPrivate)
    {
        PrepareForFreshNetworkSession();

        try
        {
            var lobby = await Services.CreateLobbyAsync(lobbyName, maxPlayers, isPrivate);

            var allocation = await Relay.AllocateRelayAsync(maxPlayers - 1);
            var joinCode = await Relay.GetRelayJoinCodeAsync(allocation);

            await Services.SetRelayJoinCodeAsync(lobby.Id, joinCode);

            var rsd = Relay.BuildRelayServerData(allocation, "dtls");
            Session.ConfigureTransport(rsd);

            Session.RegisterConnectionCallbacks();
            Session.StartHost();

            Teleport.RegisterHandlersIfNeeded();
            MatchFlow.RegisterNetworkHandlersIfNeeded();

            // City_Base is no longer loaded on the main menu.
            // The host loads it only after the lobby session starts, and Netcode syncs
            // this one copy to joining clients.
            LoadCityBase.LoadForLobbyAsHostIfNeeded(cityBaseSceneName);

            SceneUI.HideMenuUI();
            SceneUI.ShowPregameUI();

            TeleportHostToLobbySpawnAfterCityBaseLoads();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            CleanupLocalSessionState(clearLobby: true, shutdownNetwork: true);
            SceneUI.ShowLobbyScreen();
        }
    }

    public async void QuickJoin()
    {
        PrepareForFreshNetworkSession();

        try
        {
            var lobby = await Services.QuickJoinAsync();

            string joinCode = Services.GetRelayJoinCodeFromLobby(lobby);
            var joinAlloc = await Relay.JoinRelayAsync(joinCode);

            StartClientWithRelay(joinAlloc);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            CleanupLocalSessionState(clearLobby: true, shutdownNetwork: true);
            SceneUI.ShowLobbyScreen();
        }
    }

    public async void JoinWithId(string lobbyId)
    {
        PrepareForFreshNetworkSession();

        try
        {
            var lobby = await Services.JoinWithIdAsync(lobbyId);

            string joinCode = Services.GetRelayJoinCodeFromLobby(lobby);
            var joinAlloc = await Relay.JoinRelayAsync(joinCode);

            StartClientWithRelay(joinAlloc);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            CleanupLocalSessionState(clearLobby: true, shutdownNetwork: true);
            SceneUI.ShowLobbyScreen();
        }
    }

    public async void JoinWithCode(string lobbyCode)
    {
        PrepareForFreshNetworkSession();

        try
        {
            var lobby = await Services.JoinWithCodeAsync(lobbyCode);

            string joinCode = Services.GetRelayJoinCodeFromLobby(lobby);
            var joinAlloc = await Relay.JoinRelayAsync(joinCode);

            StartClientWithRelay(joinAlloc);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            CleanupLocalSessionState(clearLobby: true, shutdownNetwork: true);
            SceneUI.ShowLobbyScreen();
        }
    }

    private void StartClientWithRelay(Unity.Services.Relay.Models.JoinAllocation joinAlloc)
    {
        var rsd = Relay.BuildRelayServerData(joinAlloc, "dtls");
        Session.ConfigureTransport(rsd);

        // Clients should not bring a menu copy of City_Base into the network session.
        // They receive the host's lobby City_Base through Netcode scene sync.
        LoadCityBase.UnloadLocalIfLoaded(cityBaseSceneName);

        Session.RegisterConnectionCallbacks();
        Session.StartClient();

        Teleport.RegisterHandlersIfNeeded();
        MatchFlow.RegisterNetworkHandlersIfNeeded();

        SceneUI.HideMenuUI();
        SceneUI.ShowPregameUI();
    }

    public async void LeaveToLobbySelect()
    {
        try
        {
            await Services.LeaveOrDeleteLobbyAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Leave/delete lobby failed: {e}");
        }

        CleanupLocalSessionState(clearLobby: true, shutdownNetwork: true);
        SceneUI.ShowLobbyScreen();
    }

    public void StartGameAsHost()
    {
        StartGameAsHostInternal(ignorePlayerMinimum: false);
    }

    public void StartGameAsHostIgnoringPlayerMinimum()
    {
        StartGameAsHostInternal(ignorePlayerMinimum: true);
    }

    private async void StartGameAsHostInternal(bool ignorePlayerMinimum)
    {
        if (!CanStartMatch(out string reason, ignorePlayerMinimum))
        {
            Debug.LogWarning($"[LobbyManager] Cannot start match: {reason}");
            return;
        }

        if (ignorePlayerMinimum)
            Debug.LogWarning("[LobbyManager] DEBUG SOLO START: starting match while ignoring minimum player count.");

        try
        {
            await Services.SetLobbyStateAsync(isLocked: true, state: "in-game");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Failed to lock/unlist lobby: {e}");
        }

        SceneUI.ShowGameUI();
        MatchFlow.StartMatchAsHost();
    }

    public async void EndMatch()
    {
        if (!Session.IsHost)
            return;

        try
        {
            await Services.SetLobbyStateAsync(isLocked: false, state: "waiting");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Failed to unlock/set state: {e}");
        }

        SceneUI.ShowPregameUI();
        MatchFlow.EndMatchAsHost();

        var playerObj = Session.LocalPlayerObject;
        if (playerObj != null)
        {
            var input = playerObj.GetComponent<InputManager>();
            if (input != null)
                input.SetGameplayEnabled(true);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void EndMatchAfterCommittedResult()
    {
        if (!Session.IsHost)
            return;

        // Do not hold the result sequence open while waiting on the Lobby service.
        // Scene unload/reset starts immediately; the service state update completes independently.
        _ = SetLobbyWaitingStateAfterMatchAsync();

        MatchFlow.EndMatchAsHost();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private async Task SetLobbyWaitingStateAfterMatchAsync()
    {
        try
        {
            await Services.SetLobbyStateAsync(isLocked: false, state: "waiting");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Failed to unlock/set state after result: {e}");
        }
    }

    public void EndGameToLobbyForEveryone()
    {
        if (!Session.IsHost)
            return;

        MatchFlow.EndGameToLobbyForEveryone();
    }

    public Task DeleteLobbyIfHostAsync() => Services.DeleteLobbyIfHostAsync();

    private void PrepareForFreshNetworkSession()
    {
        CleanupLocalSessionState(clearLobby: true, shutdownNetwork: true);
    }

    private void CleanupLocalSessionState(bool clearLobby, bool shutdownNetwork)
    {
        MatchFlow?.UnregisterNetworkHandlersIfNeeded();
        Teleport?.UnregisterHandlersIfNeeded();
        Session?.UnregisterConnectionCallbacks();

        if (shutdownNetwork)
            Session?.ShutdownSession();

        if (MatchFlow != null)
        {
            MatchFlow.IsMatchInProgress = false;
            MatchFlow.UnloadInteriorLocalIfLoaded();
        }

        // Returning to the lobby selection/main menu should leave no City_Base loaded.
        LoadCityBase.UnloadLocalIfLoaded(cityBaseSceneName);

        if (RoundRoles != null)
            RoundRoles.ResetRoles();

        RouterHackState.Clear();

        if (clearLobby)
            Services?.ClearLocalLobby();
    }

    private void TeleportHostToLobbySpawnAfterCityBaseLoads()
    {
        if (_hostLobbyTeleportCoroutine != null)
            StopCoroutine(_hostLobbyTeleportCoroutine);

        _hostLobbyTeleportCoroutine = StartCoroutine(TeleportHostToLobbySpawnAfterCityBaseLoadsRoutine());
    }

    private IEnumerator TeleportHostToLobbySpawnAfterCityBaseLoadsRoutine()
    {
        const float timeoutSeconds = 8f;
        float remaining = timeoutSeconds;

        while (remaining > 0f && !LoadCityBase.IsLoaded(cityBaseSceneName))
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        _hostLobbyTeleportCoroutine = null;

        if (!LoadCityBase.IsLoaded(cityBaseSceneName))
        {
            Debug.LogWarning($"[LobbyManager] Timed out waiting for '{cityBaseSceneName}' before teleporting host to lobby spawn. Teleporting anyway.");
        }

        MatchFlow.ServerTeleportClientToLobbySpawnWhenReady(Session.LocalClientId);
    }

    public bool CanStartMatch(out string reason)
    {
        return CanStartMatch(out reason, ignorePlayerMinimum: false);
    }

    private bool CanStartMatch(out string reason, bool ignorePlayerMinimum)
    {
        reason = "";

        if (Session == null || !Session.IsHost)
        {
            reason = "Only the host can start the match.";
            return false;
        }

        if (IsMatchInProgress)
        {
            reason = "Match is already in progress.";
            return false;
        }

        int connectedPlayers = GetConnectedPlayerCount();

        if (!ignorePlayerMinimum && connectedPlayers < MinPlayersToStart)
        {
            reason =
                $"Need at least {MinPlayersToStart} players to start. Current players: {connectedPlayers}.";
            return false;
        }

        return true;
    }

    public string GetStartRequirementText()
    {
        int connectedPlayers = GetConnectedPlayerCount();

        if (connectedPlayers < MinPlayersToStart)
            return $"Waiting for players: {connectedPlayers}/{MinPlayersToStart}";

        if (Session != null && Session.IsHost)
            return "Ready. Press Start to begin.";

        return "Waiting for host to start.";
    }

    private int GetConnectedPlayerCount()
    {
        var nm = NetworkManager.Singleton;

        if (nm == null)
            return 0;

        if (nm.ConnectedClientsList != null)
            return nm.ConnectedClientsList.Count;

        return nm.IsConnectedClient ? 1 : 0;
    }

    public int AssignColorIndex() => Cosmetics.AssignColorIndex();

    public void ReleaseColorIndex(int idx) => Cosmetics.ReleaseColorIndex(idx);

    public Material GetShirtMaterial(int idx) => Cosmetics.GetShirtMaterial(idx);

    public Material BlackShirtMaterial => Cosmetics.BlackShirtMaterial;

    private void HandleLobbyListChanged(
        object sender,
        LobbyServicesFacade.LobbyListChangedEventArgs e
    )
    {
        OnLobbyListChanged?.Invoke(
            this,
            new OnLobbyListChangedEventArgs { lobbyList = e.lobbyList }
        );
    }

    private void OnServerClientConnected(ulong clientId)
    {
        if (!Session.IsServer)
            return;

        MatchFlow.ServerTeleportClientToLobbySpawnWhenReady(clientId);
    }

    private void OnLocalClientDisconnected(ulong clientId)
    {
        CleanupLocalSessionState(clearLobby: true, shutdownNetwork: false);
        SceneUI.ShowLobbyScreen();
    }

    private T GetOrAdd<T>()
        where T : Component
    {
        var c = GetComponent<T>();

        if (c == null)
            c = gameObject.AddComponent<T>();

        return c;
    }
}
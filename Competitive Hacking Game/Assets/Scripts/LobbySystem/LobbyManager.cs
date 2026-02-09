using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Config")]
    [SerializeField]
    private int maxPlayers = 5;

    [Header("Scene Names")]
    [SerializeField]
    private string lobbySceneName = "MainScene";

    [SerializeField]
    private string interiorSceneName = "Interior_01";

    [Header("Teleport Tags")]
    [SerializeField]
    private string lobbySpawnTag = "LobbySpawn";

    [SerializeField]
    private string interiorSpawnTag = "InteriorSpawn";

    // Components
    public LobbyServicesFacade Services { get; private set; }
    public RelayFacade Relay { get; private set; }
    public NetworkSessionManager Session { get; private set; }
    public TeleportService Teleport { get; private set; }
    public MatchFlowManager MatchFlow { get; private set; }
    public CosmeticsManager Cosmetics { get; private set; }
    public LobbySceneUIController SceneUI { get; private set; }

    // Compatibility event (LobbyUI expects this)
    public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;

    public class OnLobbyListChangedEventArgs : EventArgs
    {
        public System.Collections.Generic.List<Lobby> lobbyList;
    }

    public bool IsMatchInProgress => MatchFlow != null && MatchFlow.IsMatchInProgress;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Ensure components exist
        Services = GetOrAdd<LobbyServicesFacade>();
        Relay = GetOrAdd<RelayFacade>();
        Session = GetOrAdd<NetworkSessionManager>();
        Teleport = GetOrAdd<TeleportService>();
        MatchFlow = GetOrAdd<MatchFlowManager>();
        Cosmetics = GetOrAdd<CosmeticsManager>();
        SceneUI = GetOrAdd<LobbySceneUIController>();

        // Push shared config into components
        Services.SetLobbySceneName(lobbySceneName);
        MatchFlow.Configure(interiorSceneName, lobbySpawnTag, interiorSpawnTag);

        // Wire events
        Services.LobbyListChanged += HandleLobbyListChanged;

        Session.ServerClientConnected += OnServerClientConnected;
        Session.LocalClientDisconnected += OnLocalClientDisconnected;

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Kick off Unity Services auth once
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
        // Rebind scene-local refs when lobby scene loads
        if (scene.name == lobbySceneName)
            SceneUI.RebindIfNeeded(lobbySceneName);
    }

    private void Update()
    {
        // Heartbeat + lobby list polling only makes sense in lobby scene
        bool inLobbyScene = SceneManager.GetActiveScene().name == lobbySceneName;
        Services.Tick(Time.deltaTime, inLobbyScene);
    }

    // -------------------- Compatibility API (called by your UI) --------------------

    public Lobby GetLobby() => Services.CurrentLobby;

    public async void CreateLobby(string lobbyName, bool isPrivate)
    {
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

            SceneUI.HideMenuUI();
            SceneUI.ShowPregameUI();

            // Host catch-up teleport
            MatchFlow.ServerTeleportClientToLobbySpawnWhenReady(Session.LocalClientId);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public async void QuickJoin()
    {
        try
        {
            var lobby = await Services.QuickJoinAsync();

            string joinCode = Services.GetRelayJoinCodeFromLobby(lobby);
            var joinAlloc = await Relay.JoinRelayAsync(joinCode);

            var rsd = Relay.BuildRelayServerData(joinAlloc, "dtls");
            Session.ConfigureTransport(rsd);

            Session.RegisterConnectionCallbacks();
            Session.StartClient();

            Teleport.RegisterHandlersIfNeeded();

            SceneUI.HideMenuUI();
            SceneUI.ShowPregameUI();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public async void JoinWithId(string lobbyId)
    {
        try
        {
            var lobby = await Services.JoinWithIdAsync(lobbyId);

            string joinCode = Services.GetRelayJoinCodeFromLobby(lobby);
            var joinAlloc = await Relay.JoinRelayAsync(joinCode);

            var rsd = Relay.BuildRelayServerData(joinAlloc, "dtls");
            Session.ConfigureTransport(rsd);

            Session.RegisterConnectionCallbacks();
            Session.StartClient();

            Teleport.RegisterHandlersIfNeeded();

            SceneUI.HideMenuUI();
            SceneUI.ShowPregameUI();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public async void JoinWithCode(string lobbyCode)
    {
        try
        {
            var lobby = await Services.JoinWithCodeAsync(lobbyCode);

            string joinCode = Services.GetRelayJoinCodeFromLobby(lobby);
            var joinAlloc = await Relay.JoinRelayAsync(joinCode);

            var rsd = Relay.BuildRelayServerData(joinAlloc, "dtls");
            Session.ConfigureTransport(rsd);

            Session.RegisterConnectionCallbacks();
            Session.StartClient();

            Teleport.RegisterHandlersIfNeeded();

            SceneUI.HideMenuUI();
            SceneUI.ShowPregameUI();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public async void LeaveToLobbySelect()
    {
        try
        {
            await Services.LeaveOrDeleteLobbyAsync();

            Teleport.UnregisterHandlersIfNeeded();
            Session.ShutdownSession();

            Services.ClearLocalLobby();
            MatchFlow.IsMatchInProgress = false;
            MatchFlow.UnloadInteriorLocalIfLoaded();

            SceneUI.ShowLobbyScreen();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public async void StartGameAsHost()
    {
        if (!Session.IsHost)
            return;

        try
        {
            await Services.SetLobbyStateAsync(isLocked: true, state: "in-game");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Failed to lock/unlist lobby: {e}");
        }

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

        MatchFlow.EndMatchAsHost();

        // local unpause safety (like your old code)
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

    public void EndGameToLobbyForEveryone()
    {
        if (!Session.IsHost)
            return;
        MatchFlow.EndGameToLobbyForEveryone();
    }

    public Task DeleteLobbyIfHostAsync() => Services.DeleteLobbyIfHostAsync();

    // -------------------- Cosmetics compatibility helpers (optional) --------------------
    public int AssignColorIndex() => Cosmetics.AssignColorIndex();

    public void ReleaseColorIndex(int idx) => Cosmetics.ReleaseColorIndex(idx);

    public Material GetShirtMaterial(int idx) => Cosmetics.GetShirtMaterial(idx);

    public Material BlackShirtMaterial => Cosmetics.BlackShirtMaterial;

    // -------------------- Internal event handlers --------------------

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
        // Server decides initial spawn for each client joining
        if (!Session.IsServer)
            return;
        MatchFlow.ServerTeleportClientToLobbySpawnWhenReady(clientId);
    }

    private void OnLocalClientDisconnected(ulong clientId)
    {
        // Return to lobby menu state if THIS local client disconnected
        Services.ClearLocalLobby();
        MatchFlow.IsMatchInProgress = false;
        MatchFlow.UnloadInteriorLocalIfLoaded();
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

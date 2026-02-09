using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";
    private const int MAX_PLAYER_AMOUNT = 5;

    public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;
    public class OnLobbyListChangedEventArgs : EventArgs
    {
        public List<Lobby> lobbyList;
    }

    private Lobby joinedLobby;
    private float heartbeatTimer;
    private float listLobbiesTimer;

    [Header("Scene UI refs (MainScene)")]
    [SerializeField] private GameObject lobbyUI;
    [SerializeField] private PregameUI pregameUI;
    [SerializeField] private LobbyCreateUI lobbyCreateUI;
    [SerializeField] private GameObject cam; // Lobby overview cam

    public static LobbyManager Instance { get; private set; }

    private bool callbacksRegistered = false;
    private bool teleportHandlerRegistered = false;

    [Header("Shirt materials (assign in inspector)")]
    [SerializeField] public List<Material> shirtMaterials = new List<Material>();
    [SerializeField] public Material blackShirtMaterial;
    private HashSet<int> usedShirtIndices = new HashSet<int>();

    [Header("Scene Names")]
    [SerializeField] private string lobbySceneName = "MainScene";
    [SerializeField] private string interiorSceneName = "Interior_01";

    [Header("Teleport Tags")]
    [SerializeField] private string lobbySpawnTag = "LobbySpawn";
    [SerializeField] private string interiorSpawnTag = "InteriorSpawn";

    public bool IsMatchInProgress { get; private set; } = false;

    // ---- Custom message teleport (owner-authoritative safe) ----
    private const string MSG_TELEPORT = "LM_Teleport";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        DontDestroyOnLoad(gameObject);
        InitializeUnityAuthentication();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Rebind scene-local refs when MainScene loads
        if (scene.name == lobbySceneName)
        {
            if (lobbyUI == null)
            {
                var ui = FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
                if (ui != null) lobbyUI = ui.gameObject;
            }

            if (pregameUI == null)
                pregameUI = FindFirstObjectByType<PregameUI>(FindObjectsInactive.Include);

            if (lobbyCreateUI == null)
                lobbyCreateUI = FindFirstObjectByType<LobbyCreateUI>(FindObjectsInactive.Include);

            if (cam == null)
            {
                Camera camObj = null;
                var cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in cams)
                {
                    if (c.CompareTag("LobbyCamera"))
                    {
                        camObj = c;
                        break;
                    }
                }
                if (camObj != null) cam = camObj.gameObject;
            }
        }
    }

    private async void InitializeUnityAuthentication()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var initializationOptions = new InitializationOptions();
            initializationOptions.SetProfile(UnityEngine.Random.Range(0, 1000).ToString());
            await UnityServices.InitializeAsync(initializationOptions);

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private void Update()
    {
        HandleHeartbeat();
        HandlePeriodicListLobbies();
    }

    private void HandlePeriodicListLobbies()
    {
        if (SceneManager.GetActiveScene().name != lobbySceneName) return;

        if (joinedLobby == null && AuthenticationService.Instance.IsSignedIn)
        {
            listLobbiesTimer -= Time.deltaTime;
            if (listLobbiesTimer <= 0f)
            {
                listLobbiesTimer = 3f;
                ListLobbies();
            }
        }
    }

    private void HandleHeartbeat()
    {
        if (IsLobbyHost())
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = 15f;
                _ = LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
            }
        }
    }

    private bool IsLobbyHost()
    {
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    private async void ListLobbies()
    {
        try
        {
            QueryLobbiesOptions queryLobbiesOptions = new QueryLobbiesOptions
            {
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new QueryFilter(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.S1, "waiting", QueryFilter.OpOptions.EQ)
                }
            };
            QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync(queryLobbiesOptions);

            OnLobbyListChanged?.Invoke(this, new OnLobbyListChangedEventArgs
            {
                lobbyList = queryResponse.Results
            });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private async Task<Allocation> AllocateRelay()
    {
        try
        {
            return await RelayService.Instance.CreateAllocationAsync(MAX_PLAYER_AMOUNT - 1);
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
    }

    private async Task<string> GetRelayJoinCode(Allocation allocation)
    {
        try
        {
            return await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
    }

    private async Task<JoinAllocation> JoinRelay(string joinCode)
    {
        try
        {
            return await RelayService.Instance.JoinAllocationAsync(joinCode);
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
    }

    // ---------------------- CREATE / JOIN ----------------------

    public async void CreateLobby(string lobbyName, bool isPrivate)
    {
        try
        {
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(
                lobbyName, MAX_PLAYER_AMOUNT,
                new CreateLobbyOptions
                {
                    IsPrivate = isPrivate,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "state", new DataObject(DataObject.VisibilityOptions.Public, "waiting", DataObject.IndexOptions.S1) }
                    }
                });

            Allocation allocation = await AllocateRelay();
            string relayJoinCode = await GetRelayJoinCode(allocation);

            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> {
                    { KEY_RELAY_JOIN_CODE , new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            });

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // IMPORTANT: subscribe before StartHost so we catch host connect event
            RegisterConnectionCallbacks();

            NetworkManager.Singleton.StartHost();

            // Messaging manager is guaranteed after StartHost
            RegisterTeleportHandlerIfNeeded();

            HideMenuUI();
            lobbyCreateUI?.Hide();
            pregameUI?.Show();
            pregameUI?.SetPregameUI();

            // Catch-up teleport for host (in case timing differs on some platforms)
            StartCoroutine(TeleportClientToLobbySpawnWhenReady(NetworkManager.Singleton.LocalClientId));
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void QuickJoin()
    {
        try
        {
            var quickJoinOptions = new QuickJoinLobbyOptions
            {
                Filter = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new QueryFilter(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.S1, "waiting", QueryFilter.OpOptions.EQ)
                }
            };

            joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(quickJoinOptions);

            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            RegisterConnectionCallbacks();
            NetworkManager.Singleton.StartClient();
            RegisterTeleportHandlerIfNeeded();

            HideMenuUI();
            lobbyCreateUI?.Hide();
            pregameUI?.Show();
            pregameUI?.SetPregameUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public Lobby GetLobby() => joinedLobby;

    public async void JoinWithId(string lobbyId)
    {
        try
        {
            joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            RegisterConnectionCallbacks();
            NetworkManager.Singleton.StartClient();
            RegisterTeleportHandlerIfNeeded();

            HideMenuUI();
            lobbyCreateUI?.Hide();
            pregameUI?.Show();
            pregameUI?.SetPregameUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void JoinWithCode(string lobbyCode)
    {
        try
        {
            joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);

            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            RegisterConnectionCallbacks();
            NetworkManager.Singleton.StartClient();
            RegisterTeleportHandlerIfNeeded();

            HideMenuUI();
            lobbyCreateUI?.Hide();
            pregameUI?.Show();
            pregameUI?.SetPregameUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    // ---------------------- LEAVE / DISCONNECT ----------------------

    public async void LeaveToLobbySelect()
    {
        try
        {
            if (joinedLobby != null)
            {
                if (IsLobbyHost())
                    await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
                else
                    await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            }
        }
        catch (LobbyServiceException e) { Debug.Log(e); }

        if (NetworkManager.Singleton != null)
        {
            UnregisterConnectionCallbacks();
            UnregisterTeleportHandlerIfNeeded();

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
                NetworkManager.Singleton.Shutdown();
        }

        joinedLobby = null;
        IsMatchInProgress = false;

        UnloadInteriorLocalIfLoaded();

        ShowLobbyScreen();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // If THIS local client got disconnected, go back to menu state.
        if (NetworkManager.Singleton != null &&
            !NetworkManager.Singleton.IsServer &&
            clientId == NetworkManager.Singleton.LocalClientId)
        {
            PauseMenuUI.Instance?.Resume();

            joinedLobby = null;
            IsMatchInProgress = false;

            UnloadInteriorLocalIfLoaded();
            ShowLobbyScreen();
        }
    }

    private async void OnApplicationQuit()
    {
        try
        {
            if (IsLobbyHost() && joinedLobby != null)
                await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    // ---------------------- UI HELPERS ----------------------

    private void HideMenuUI()
    {
        if (lobbyUI != null) lobbyUI.SetActive(false);
        if (cam != null) cam.SetActive(false);
    }

    public void ShowLobbyScreen()
    {
        if (lobbyUI == null)
        {
            var ui = FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            if (ui != null) lobbyUI = ui.gameObject;
        }
        if (pregameUI == null) pregameUI = FindFirstObjectByType<PregameUI>(FindObjectsInactive.Include);
        if (lobbyCreateUI == null) lobbyCreateUI = FindFirstObjectByType<LobbyCreateUI>(FindObjectsInactive.Include);

        if (cam == null)
        {
            var camObj = GameObject.FindWithTag("LobbyCamera");
            if (camObj != null) cam = camObj;
        }

        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (lobbyCreateUI != null) lobbyCreateUI.gameObject.SetActive(false);
        if (pregameUI != null) pregameUI.gameObject.SetActive(false);
        if (cam != null) cam.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ---------------------- CONNECTION CALLBACKS (SAFE ORDER) ----------------------

    private void RegisterConnectionCallbacks()
    {
        if (callbacksRegistered) return;
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        callbacksRegistered = true;
    }

    private void UnregisterConnectionCallbacks()
    {
        if (!callbacksRegistered) return;

        if (NetworkManager.Singleton == null)
        {
            callbacksRegistered = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        callbacksRegistered = false;
    }

    private void OnClientConnected(ulong clientId)
    {
        // Only the server/host decides initial spawn placement
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        StartCoroutine(TeleportClientToLobbySpawnWhenReady(clientId));
    }

    private IEnumerator TeleportClientToLobbySpawnWhenReady(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) yield break;

        // Wait until server has a PlayerObject for that client
        float timeout = 8f;
        while (timeout > 0f)
        {
            if (nm.ConnectedClients != null &&
                nm.ConnectedClients.TryGetValue(clientId, out var client) &&
                client.PlayerObject != null)
            {
                break;
            }

            timeout -= Time.deltaTime;
            yield return null;
        }

        if (nm.ConnectedClients == null ||
            !nm.ConnectedClients.TryGetValue(clientId, out var cc) ||
            cc.PlayerObject == null)
        {
            Debug.LogWarning($"[LobbyManager] PlayerObject not ready for client {clientId}, cannot teleport to LobbySpawn.");
            yield break;
        }

        var spawns = GameObject.FindGameObjectsWithTag(lobbySpawnTag);
        if (spawns == null || spawns.Length == 0)
        {
            Debug.LogWarning($"[LobbyManager] No objects tagged '{lobbySpawnTag}' found. Client {clientId} will spawn at default.");
            yield break;
        }

        int idx = (int)(clientId % (ulong)spawns.Length);
        Transform t = spawns[idx].transform;

        // Owner-authoritative teleport: message the owning client (host teleports itself directly)
        SendTeleportToClient(clientId, t.position, t.rotation);
    }

    // ---------------------- SHIRTS ----------------------

    public int AssignColorIndex()
    {
        if (shirtMaterials == null || shirtMaterials.Count == 0)
        {
            Debug.LogWarning("[LobbyManager] No shirt materials configured. returning index 0");
            return 0;
        }

        for (int i = 0; i < shirtMaterials.Count; i++)
        {
            if (!usedShirtIndices.Contains(i))
            {
                usedShirtIndices.Add(i);
                return i;
            }
        }

        int fallback = UnityEngine.Random.Range(0, shirtMaterials.Count);
        Debug.LogWarning($"[LobbyManager] All shirt indices used, falling back to {fallback}");
        return fallback;
    }

    public void ReleaseColorIndex(int index)
    {
        if (usedShirtIndices.Contains(index))
            usedShirtIndices.Remove(index);
    }

    public Material GetShirtMaterial(int index)
    {
        if (shirtMaterials == null || shirtMaterials.Count == 0)
            return null;

        if (index < 0 || index >= shirtMaterials.Count)
            return shirtMaterials[0];

        return shirtMaterials[index];
    }

    public async Task DeleteLobbyIfHostAsync()
    {
        try
        {
            if (joinedLobby != null && IsLobbyHost())
                await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
        }
        catch (LobbyServiceException e) { Debug.Log(e); }
    }

    // ---------------------- MATCH FLOW (ADDITIVE INTERIOR) ----------------------

    public async void StartGameAsHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        if (joinedLobby != null)
        {
            try
            {
                joinedLobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = true,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "state", new DataObject(DataObject.VisibilityOptions.Public, "in-game", DataObject.IndexOptions.S1) }
                    }
                });
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] Failed to lock/unlist lobby: {e}");
            }
        }

        if (IsInteriorLoaded())
        {
            Debug.LogWarning("[LobbyManager] Interior already loaded; ignoring StartGameAsHost.");
            return;
        }

        IsMatchInProgress = true;

        var nsm = NetworkManager.Singleton.SceneManager;
        nsm.OnLoadEventCompleted += OnInteriorLoadCompleted;
        nsm.LoadScene(interiorSceneName, LoadSceneMode.Additive);
    }

    private void OnInteriorLoadCompleted(string sceneName, LoadSceneMode mode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != interiorSceneName) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnInteriorLoadCompleted;

        TeleportAllClients(interiorSpawnTag);
    }

    public async void EndMatch()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        if (joinedLobby != null)
        {
            try
            {
                joinedLobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "state", new DataObject(DataObject.VisibilityOptions.Public, "waiting", DataObject.IndexOptions.S1) }
                    }
                });
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] Failed to unlock/set state on lobby: {e}");
            }
        }

        if (!IsInteriorLoaded())
        {
            IsMatchInProgress = false;
            TeleportAllClients(lobbySpawnTag);
            return;
        }

        IsMatchInProgress = false;

        var nsm = NetworkManager.Singleton.SceneManager;
        nsm.OnUnloadEventCompleted += OnInteriorUnloadCompleted;

        Scene interior = SceneManager.GetSceneByName(interiorSceneName);
        if (interior.IsValid() && interior.isLoaded)
            nsm.UnloadScene(interior);
        else
        {
            nsm.OnUnloadEventCompleted -= OnInteriorUnloadCompleted;
            Debug.LogWarning("[LobbyManager] Interior scene not found/loaded when trying to unload.");
            TeleportAllClients(lobbySpawnTag);
        }

        // unpause local
        var playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (playerObj != null)
        {
            var input = playerObj.GetComponent<InputManager>();
            if (input != null) input.SetGameplayEnabled(true);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnInteriorUnloadCompleted(string sceneName, LoadSceneMode mode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != interiorSceneName) return;

        NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted -= OnInteriorUnloadCompleted;

        TeleportAllClients(lobbySpawnTag);
    }

    public void EndGameToLobbyForEveryone()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        if (IsInteriorLoaded())
        {
            var nsm = NetworkManager.Singleton.SceneManager;
            nsm.OnUnloadEventCompleted += OnUnloadThenShutdown;

            Scene interior = SceneManager.GetSceneByName(interiorSceneName);
            if (interior.IsValid() && interior.isLoaded)
                nsm.UnloadScene(interior);
            else
                StartCoroutine(ShutdownNextFrame());
        }
        else
        {
            StartCoroutine(ShutdownNextFrame());
        }
    }

    private void OnUnloadThenShutdown(string sceneName, LoadSceneMode mode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != interiorSceneName) return;

        NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted -= OnUnloadThenShutdown;
        StartCoroutine(ShutdownNextFrame());
    }

    private IEnumerator ShutdownNextFrame()
    {
        yield return null;

        IsMatchInProgress = false;

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
        {
            UnregisterConnectionCallbacks();
            UnregisterTeleportHandlerIfNeeded();
            NetworkManager.Singleton.Shutdown();
        }

        joinedLobby = null;

        UnloadInteriorLocalIfLoaded();
        ShowLobbyScreen();
    }

    private bool IsInteriorLoaded()
    {
        Scene s = SceneManager.GetSceneByName(interiorSceneName);
        return s.IsValid() && s.isLoaded;
    }

    private void UnloadInteriorLocalIfLoaded()
    {
        Scene s = SceneManager.GetSceneByName(interiorSceneName);
        if (s.IsValid() && s.isLoaded)
            SceneManager.UnloadSceneAsync(s);
    }

    // ---------------------- TELEPORT (MESSAGING) ----------------------

    private void RegisterTeleportHandlerIfNeeded()
    {
        if (teleportHandlerRegistered) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        var cmm = nm.CustomMessagingManager;
        if (cmm == null) return; // prevent null refs before NGO is started

        cmm.RegisterNamedMessageHandler(MSG_TELEPORT, OnTeleportMessage);
        teleportHandlerRegistered = true;
    }

    private void UnregisterTeleportHandlerIfNeeded()
    {
        if (!teleportHandlerRegistered) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) { teleportHandlerRegistered = false; return; }

        var cmm = nm.CustomMessagingManager;
        if (cmm != null)
            cmm.UnregisterNamedMessageHandler(MSG_TELEPORT);

        teleportHandlerRegistered = false;
    }

    private void TeleportAllClients(string spawnTag)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        var spawns = GameObject.FindGameObjectsWithTag(spawnTag);
        if (spawns == null || spawns.Length == 0)
        {
            Debug.LogWarning($"[LobbyManager] No spawn objects with tag '{spawnTag}' found.");
            return;
        }

        int i = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject;
            if (player == null) continue;

            Transform t = spawns[i % spawns.Length].transform;
            i++;

            SendTeleportToClient(client.ClientId, t.position, t.rotation);
        }
    }

    private void SendTeleportToClient(ulong targetClientId, Vector3 pos, Quaternion rot)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // If it's us (host), teleport immediately (avoids race with message handler registration)
        if (nm.IsConnectedClient && nm.LocalClientId == targetClientId)
        {
            TeleportLocalPlayer(pos, rot);
            return;
        }

        var cmm = nm.CustomMessagingManager;
        if (cmm == null)
        {
            Debug.LogWarning("[LobbyManager] CustomMessagingManager not ready; cannot send teleport yet.");
            return;
        }

        using var writer = new FastBufferWriter(sizeof(float) * 7, Allocator.Temp);
        writer.WriteValueSafe(pos);
        writer.WriteValueSafe(rot);

        cmm.SendNamedMessage(MSG_TELEPORT, targetClientId, writer);
    }

    private void OnTeleportMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Vector3 pos);
        reader.ReadValueSafe(out Quaternion rot);

        TeleportLocalPlayer(pos, rot);
    }

    private void TeleportLocalPlayer(Vector3 pos, Quaternion rot)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        var playerObj = nm.LocalClient?.PlayerObject;
        if (playerObj == null) return;

        var cc = playerObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerObj.transform.SetPositionAndRotation(pos, rot);

        if (cc != null) cc.enabled = true;
    }
}

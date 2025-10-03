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
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

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
    [SerializeField] private GameObject lobbyUI;
    [SerializeField] private PregameUI pregameUI;
    [SerializeField] private LobbyCreateUI lobbyCreateUI;
    [SerializeField] private GameObject cam;
    public static LobbyManager Instance { get; private set; }
    private bool callbacksRegistered = false;

    [Header("Shirt materials (assign in inspector)")]
    // list of predefined shirt materials. assign a material per color in the inspector.
    [SerializeField] public List<Material> shirtMaterials = new List<Material>();

    // special black material for the bad guy. assign in inspector.
    [SerializeField] public Material blackShirtMaterial;

    // internal set of used indices
    private HashSet<int> usedShirtIndices = new HashSet<int>();

    [SerializeField] private string lobbySceneName = "LobbyScene";
    [SerializeField] private string gameSceneName = "GameScene";

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeUnityAuthentication();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "LobbyScene")
        {
            // Lobby UI
            if (lobbyUI == null)
            {
                var ui = FindObjectOfType<LobbyUI>(true);
                if (ui != null) lobbyUI = ui.gameObject;
            }

            // Pregame UI
            if (pregameUI == null)
            {
                pregameUI = FindObjectOfType<PregameUI>(true);
            }

            // Lobby Create UI
            if (lobbyCreateUI == null)
            {
                lobbyCreateUI = FindObjectOfType<LobbyCreateUI>(true);
            }

            // Lobby Camera
            if (cam == null)
            {
                Camera camObj = null;
                foreach (var c in FindObjectsOfType<Camera>(true))
                {
                    if (c.CompareTag("LobbyCamera"))
                    {
                        camObj = c;
                        break;
                    }
                }

                if (camObj != null)
                {
                    cam = camObj.gameObject;
                    Debug.Log("its not null babes");
                }
            }
        }
    }


    private async void InitializeUnityAuthentication()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            InitializationOptions initializationOptions = new InitializationOptions();
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
                float heartbeatTimerMax = 15f;
                heartbeatTimer = heartbeatTimerMax;

                LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
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
                    new QueryFilter(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ), // hide locked
                    new QueryFilter(QueryFilter.FieldOptions.S1, "waiting", QueryFilter.OpOptions.EQ)      // only waiting
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
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYER_AMOUNT - 1);

            return allocation;
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
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return relayJoinCode;
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
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            return joinAllocation;
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
    }

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
                    { KEY_RELAY_JOIN_CODE , new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)}
                }
            });

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();

            RegisterNetworkCallbacks();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();

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


            NetworkManager.Singleton.StartClient();
            RegisterNetworkCallbacks();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();

        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public Lobby GetLobby()
    {
        return joinedLobby;
    }

    public async void JoinWithId(string lobbyId)
    {
        try
        {
            joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);


            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);


            NetworkManager.Singleton.StartClient();
            RegisterNetworkCallbacks();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();
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


            NetworkManager.Singleton.StartClient();
            RegisterNetworkCallbacks();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void LeaveToLobbySelect()
    {
        try
        {
            if (joinedLobby != null)
            {
                Debug.Log("joined lobby NOT NULL!!!" + joinedLobby.Id);
                if (IsLobbyHost())
                {
                    Debug.Log("BItch i am host i should deltee" + joinedLobby.Id);
                    await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
                }
                else
                {
                    Debug.Log("WTF i'm not hostP??" + joinedLobby.Id);
                    await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
                }

            }
            else
            {
                Debug.Log("WTF joinedlobby is null???");
            }
        }
        catch (LobbyServiceException e) { Debug.Log(e); }

        if (NetworkManager.Singleton != null)
        {
            UnregisterNetworkCallbacks();
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
                NetworkManager.Singleton.Shutdown();
        }

        joinedLobby = null;

        // If we are NOT in LobbyScene, go there first; once loaded, show the selection UI.
        if (SceneManager.GetActiveScene().name != lobbySceneName)
        {
            SceneManager.sceneLoaded += OnLobbySceneLoadedOnce;
            SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        }
        else
        {
            ShowLobbyScreen();
        }
    }

    private void OnLobbySceneLoadedOnce(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == lobbySceneName)
        {
            SceneManager.sceneLoaded -= OnLobbySceneLoadedOnce;
            ShowLobbyScreen();
        }
    }



    private void HideUI()
    {
        lobbyUI.SetActive(false);
        cam.SetActive(false);
    }

    private void RegisterNetworkCallbacks()
    {
        if (callbacksRegistered) return;
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        callbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!callbacksRegistered) return;
        if (NetworkManager.Singleton == null) { callbacksRegistered = false; return; }

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        callbacksRegistered = false;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null &&
            !NetworkManager.Singleton.IsServer &&
            clientId == NetworkManager.Singleton.LocalClientId)
        {
            PauseMenuUI.Instance?.Resume();
            LeaveToLobbySelect();
        }
    }

    // Best-effort: if host closes the app, try to delete the lobby so it doesn't linger.
    private async void OnApplicationQuit()
    {
        try
        {
            if (IsLobbyHost() && joinedLobby != null)
            {
                await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    // Called by PlayerSetup on server when a player object spawns
    public int AssignColorIndex()
    {
        if (shirtMaterials == null || shirtMaterials.Count == 0)
        {
            Debug.LogWarning("[LobbyManager] No shirt materials configured. returning index 0");
            return 0;
        }

        // pick first unused index
        for (int i = 0; i < shirtMaterials.Count; i++)
        {
            if (!usedShirtIndices.Contains(i))
            {
                usedShirtIndices.Add(i);
                return i;
            }
        }

        // If all used, fallback to picking a random index (or you could loop)
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
            {
                await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
            }
        }
        catch (LobbyServiceException e) { Debug.Log(e); }
    }

    //public void ClearJoinedLobby() => joinedLobby = null;

    // Call this after (re)loading LobbyScene to rebind scene refs and show the selection screen.
    public void ShowLobbyScreen()
    {
        // Rebind scene-local refs if they've been destroyed during scene changes
        if (lobbyUI == null)
        {
            var ui = FindObjectOfType<LobbyUI>(true);
            if (ui != null) lobbyUI = ui.gameObject;
        }
        if (pregameUI == null) pregameUI = FindObjectOfType<PregameUI>(true);

        if (cam == null)
        {
            // Tag your lobby camera as "LobbyCamera"
            var camObj = GameObject.FindWithTag("LobbyCamera");
            if (camObj != null) cam = camObj;
        }

        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (pregameUI != null) pregameUI.gameObject.SetActive(false);
        if (cam != null) cam.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public async void StartGameAsHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        if (joinedLobby != null)
        {
            try
            {
                // Lock (no new joins) and hide from browse queries during the match.
                joinedLobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = true,   // blocks join by code
                    Data = new Dictionary<string, DataObject>
                    {
                        // Optional: track state for your own UI / filters (indexed so it can be queried)
                        { "state", new DataObject(DataObject.VisibilityOptions.Public, "in-game", DataObject.IndexOptions.S1) }
                    }
                });
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] Failed to lock/unlist lobby: {e}");
            }
        }

        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    public void EndGameToLobbyForEveryone()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        // Bring everyone back to the lobby scene (still connected)
        NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);

        // After the scene load completes for everyone, shut the session down.
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnReturnToLobbyLoadedOnce;
    }

    private void OnReturnToLobbyLoadedOnce(string sceneName, LoadSceneMode mode,
        System.Collections.Generic.List<ulong> clientsCompleted,
        System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        if (sceneName != lobbySceneName) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnReturnToLobbyLoadedOnce;

        // Give NGO one frame to settle, then shut down the network.
        StartCoroutine(ShutdownNextFrame());
    }

    private System.Collections.IEnumerator ShutdownNextFrame()
    {
        yield return null;
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
            NetworkManager.Singleton.Shutdown();

        // Show lobby UI locally for the host (clients handle it in OnClientDisconnected below)
        ShowLobbyScreen();
    }
    
    public async void EndMatch()
    {
        // Only the host should execute this
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        // Unlock the lobby and change its state back to "waiting"
        if (joinedLobby != null)
        {
            try
            {
                Debug.Log("Ok i unlocked this lobby: " + joinedLobby.Id);
                joinedLobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = false, // UNLOCK lobby (allows join by code)
                    // We intentionally omit IsPrivate, so it stays as whatever it was before (true or false).
                    Data = new Dictionary<string, DataObject>
                    {
                        // Set state back to "waiting" so the lobby shows up in lists/quick-join if it was public, 
                        // or just to indicate to players that the match is over.
                        { "state", new DataObject(DataObject.VisibilityOptions.Public, "waiting", DataObject.IndexOptions.S1) } 
                    }
                });
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] Failed to unlock/set state on lobby: {e}");
            }
        }

        Debug.Log("Ok before scene load " + joinedLobby.Id);

        // Load the lobby scene for everyone, keeping connections alive
        GameManager.Instance.SetReturningFromMatch(true);
        NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);

        Debug.Log("ok after scene load " + joinedLobby.Id);

        // Unpause the local player and reset cursor... (rest of your logic)
        var playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (playerObj != null)
        {
            var input = playerObj.GetComponent<InputManager>();
            if (input != null) input.SetGameplayEnabled(true);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

}

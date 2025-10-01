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
    [SerializeField] public List<Material> shirtMaterials = new List<Material>();
    [SerializeField] public Material blackShirtMaterial;

    private Coroutine showPregameRoutine;
    public int MaxPlayers => MAX_PLAYER_AMOUNT;   // (optional) expose to others


    // internal set of used indices
    private HashSet<int> usedShirtIndices = new HashSet<int>();


    private void Awake()
    {
        Instance = this;

        DontDestroyOnLoad(gameObject);

        InitializeUnityAuthentication();
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
        if (joinedLobby == null && AuthenticationService.Instance.IsSignedIn)
        {
            listLobbiesTimer -= Time.deltaTime;
            if (listLobbiesTimer <= 0f)
            {
                float listLobbiesTimerMax = 3f;
                listLobbiesTimer = listLobbiesTimerMax;
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
                Filters = new List<QueryFilter> {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
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
        ConnectingOverlayUI.Instance?.Show("Creating lobby...");
        HideUI();

        try
        {
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MAX_PLAYER_AMOUNT, new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
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

            SpawnPregameNetworkObject();

            RegisterNetworkCallbacks();

            ShowPregameUIWhenReady();


        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            ConnectingOverlayUI.Instance?.Hide();
            ShowUI();
        }
    }

    public async void QuickJoin()
    {
        ConnectingOverlayUI.Instance?.Show("Quick joining...");
        HideUI();

        try
        {
            joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();


            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);


            NetworkManager.Singleton.StartClient();
            RegisterNetworkCallbacks();
            ShowPregameUIWhenReady();


        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            ConnectingOverlayUI.Instance?.Hide();
            ShowUI();
        }
    }

    public Lobby GetLobby()
    {
        return joinedLobby;
    }

    public async void JoinWithId(string lobbyId)
    {
        ConnectingOverlayUI.Instance?.Show("Joining Lobby...");
        HideUI();

        try
        {
            joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);


            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);


            NetworkManager.Singleton.StartClient();
            RegisterNetworkCallbacks();

            ShowPregameUIWhenReady();

        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            ConnectingOverlayUI.Instance?.Hide();
            ShowUI();
        }
    }

    public async void JoinWithCode(string lobbyCode)
    {
        ConnectingOverlayUI.Instance?.Show("Joining with code...");
        HideUI();

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
            ShowPregameUIWhenReady();

        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            ConnectingOverlayUI.Instance?.Hide();
            ShowUI();
        }
    }

    public async void LeaveToLobbySelect()
    {
        if (showPregameRoutine != null) { StopCoroutine(showPregameRoutine); showPregameRoutine = null; }

        ConnectingOverlayUI.Instance?.Show("Leaving...");

        SwitchToMenuAudioImmediate();

        try
        {
            if (joinedLobby != null)
            {
                if (IsLobbyHost())
                {
                    await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            ConnectingOverlayUI.Instance?.Hide();
        }

        if (NetworkManager.Singleton != null)
        {
            UnregisterNetworkCallbacks();
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        joinedLobby = null;

        // Restore lobby UI + main menu camera
        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (pregameUI != null) pregameUI.gameObject.SetActive(false);
        if (cam != null) cam.SetActive(true);

        // UI cursor for menus
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Hide overlay now that leave flow is finished
        ConnectingOverlayUI.Instance?.Hide();
    }




    private void HideUI()
    {
        lobbyUI.SetActive(false);
        lobbyCreateUI.Hide();
    }

    private void ShowUI()
    { 
        lobbyUI.SetActive(true);
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
            SwitchToMenuAudioImmediate();
            if (showPregameRoutine != null) { StopCoroutine(showPregameRoutine); showPregameRoutine = null; }

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

    private void SpawnPregameNetworkObject()
    {
        // only host (server) should spawn the network object
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        GameObject prefab = Resources.Load<GameObject>("PregameNetwork");
        if (prefab == null)
        {
            Debug.LogWarning("[LobbyManager] PregameNetwork prefab not found in Resources/PregameNetwork");
            return;
        }

        GameObject go = Instantiate(prefab);
        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
        else
        {
            Debug.LogWarning("[LobbyManager] PregameNetwork prefab missing NetworkObject component");
        }
    }

    public void HandOffAudioAndMenuCamera()
    {
        // 1) Turn OFF the menu AudioListener immediately to avoid overlap
        if (cam != null)
        {
            var al = cam.GetComponent<AudioListener>();
            if (al) al.enabled = false;
        }

        // 2) Next frame, disable the whole menu camera GameObject (no freeze/frame pop)
        StartCoroutine(DisableMenuCameraNextFrame());
    }

    private System.Collections.IEnumerator DisableMenuCameraNextFrame()
    {
        yield return null; // wait one frame so player camera is already rendering
        if (cam != null) cam.SetActive(false);
    }

    public void SwitchToMenuAudioImmediate()
    {
        // 1) Turn OFF the local player's listener if it exists
        var nm = NetworkManager.Singleton;
        var playerObj = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (playerObj != null)
        {
            // Find any listener under the local player (camera is usually a child)
            var playerListener = playerObj.GetComponentInChildren<AudioListener>(true);
            if (playerListener != null)
                playerListener.enabled = false;
        }

        // 2) Ensure the menu camera is active and has its listener ON
        if (cam != null)
        {
            cam.SetActive(true);
            var menuListener = cam.GetComponent<AudioListener>();
            if (menuListener != null) menuListener.enabled = true;
            // (If you truly don't have one on the menu camera, add one here:
            // if (menuListener == null) cam.AddComponent<AudioListener>();
            // but usually it's already present.)
        }
    }

    public void ShowPregameUIWhenReady()
    {
        if (showPregameRoutine != null) StopCoroutine(showPregameRoutine);
        showPregameRoutine = StartCoroutine(Co_ShowPregameWhenReady());
    }

    private System.Collections.IEnumerator Co_ShowPregameWhenReady()
    {
        // 1) Wait until networking is actually running (host or client)
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            yield return null;

        // 2) Wait for the local player's NetworkObject to exist
        while (NetworkManager.Singleton.LocalClient == null ||
            NetworkManager.Singleton.LocalClient.PlayerObject == null)
            yield return null;

        // 3) Wait for the pregame network brain to spawn (so UI has real data)
        while (PregameLobbyNetwork.Instance == null)
            yield return null;

        // 4) Now it's safe to show accurate UI
        pregameUI.SetPregameUI();        // this already calls Show() inside your script
        ConnectingOverlayUI.Instance?.Hide();
        showPregameRoutine = null;
    }


}

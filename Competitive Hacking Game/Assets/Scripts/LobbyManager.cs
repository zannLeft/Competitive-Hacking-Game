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

public class LobbyManager : MonoBehaviour {

    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";
    private const int MAX_PLAYER_AMOUNT = 5;
    public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;
    public class OnLobbyListChangedEventArgs : EventArgs {
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
    
    private void Awake() {
        Instance = this;

        DontDestroyOnLoad(gameObject);

        InitializeUnityAuthentication();
    }

    private async void InitializeUnityAuthentication() {
        if (UnityServices.State != ServicesInitializationState.Initialized) {
            InitializationOptions initializationOptions = new InitializationOptions();
            initializationOptions.SetProfile(UnityEngine.Random.Range(0, 1000).ToString());
            await UnityServices.InitializeAsync(initializationOptions);

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        } 
    }

    private void Update() {
        HandleHeartbeat();
        HandlePeriodicListLobbies();
    }

    private void HandlePeriodicListLobbies() {
        if (joinedLobby == null && AuthenticationService.Instance.IsSignedIn) {
            listLobbiesTimer -= Time.deltaTime; 
            if (listLobbiesTimer <= 0f) {
                float listLobbiesTimerMax = 3f;
                listLobbiesTimer = listLobbiesTimerMax;
                ListLobbies();
            }
        }
    }

    private void HandleHeartbeat() {
        if (IsLobbyHost()) {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0f) {
                float heartbeatTimerMax = 15f;
                heartbeatTimer = heartbeatTimerMax;

                LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
            }
        }
    }

    private bool IsLobbyHost() {
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }


    private async void ListLobbies() {
        try {
        QueryLobbiesOptions queryLobbiesOptions = new QueryLobbiesOptions {
            Filters = new List<QueryFilter> {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
            }
        };
        QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync(queryLobbiesOptions);

        OnLobbyListChanged?.Invoke(this, new OnLobbyListChangedEventArgs {
            lobbyList =  queryResponse.Results
        });
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    private async Task<Allocation> AllocateRelay() {
        try {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYER_AMOUNT - 1);

            return allocation;
        } catch (RelayServiceException e) {
            Debug.Log(e);

            return default;
        }
    }

    private async Task<string> GetRelayJoinCode(Allocation allocation) {
        try {
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return relayJoinCode;
        } catch (RelayServiceException e) {
            Debug.Log(e);
            return default;
        }
    }

    private async Task<JoinAllocation> JoinRelay(string joinCode) {
        try {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            return joinAllocation;
        } catch (RelayServiceException e) {
            Debug.Log(e);
            return default;
        }
    }

    public async void CreateLobby(string lobbyName, bool isPrivate) {
        try {
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MAX_PLAYER_AMOUNT, new CreateLobbyOptions {
                IsPrivate = isPrivate,
            });

            Allocation allocation = await AllocateRelay();
            string relayJoinCode = await GetRelayJoinCode(allocation);

            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions {
                Data = new Dictionary<string, DataObject> {
                    { KEY_RELAY_JOIN_CODE , new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)}
                }
            });

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();

        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public async void QuickJoin() {
        try {
            joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();

            
            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);


            NetworkManager.Singleton.StartClient();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();

        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public Lobby GetLobby() {
        return joinedLobby;
    }

    public async void JoinWithId(string lobbyId) {
        try {
            joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);


            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);


            NetworkManager.Singleton.StartClient();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public async void JoinWithCode(string lobbyCode) {
        try {
            joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);


            string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);


            NetworkManager.Singleton.StartClient();

            HideUI();
            lobbyCreateUI.Hide();
            pregameUI.Show();
            pregameUI.SetPregameUI();
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    private void HideUI() {
        lobbyUI.SetActive(false);
        cam.SetActive(false);
    }
}

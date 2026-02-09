using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

[DisallowMultipleComponent]
public class LobbyServicesFacade : MonoBehaviour
{
    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";

    public class LobbyListChangedEventArgs : EventArgs
    {
        public List<Lobby> lobbyList;
    }

    public event EventHandler<LobbyListChangedEventArgs> LobbyListChanged;

    public Lobby CurrentLobby { get; private set; }

    private float _heartbeatTimer;
    private float _listTimer;

    private string _lobbySceneName = "MainScene";

    public void SetLobbySceneName(string name) => _lobbySceneName = name;

    public async void InitializeUnityAuthentication()
    {
        if (
            UnityServices.State == ServicesInitializationState.Initialized
            && AuthenticationService.Instance.IsSignedIn
        )
            return;

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                options.SetProfile(UnityEngine.Random.Range(0, 100000).ToString());
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public void Tick(float dt, bool inLobbyScene)
    {
        HandleHeartbeat(dt);
        if (inLobbyScene)
            HandleLobbyListPolling(dt);
    }

    private void HandleHeartbeat(float dt)
    {
        if (!IsLobbyHost())
            return;
        if (CurrentLobby == null)
            return;

        _heartbeatTimer -= dt;
        if (_heartbeatTimer <= 0f)
        {
            _heartbeatTimer = 15f;
            _ = LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
        }
    }

    private void HandleLobbyListPolling(float dt)
    {
        if (CurrentLobby != null)
            return;
        if (!AuthenticationService.Instance.IsSignedIn)
            return;

        _listTimer -= dt;
        if (_listTimer <= 0f)
        {
            _listTimer = 3f;
            _ = ListLobbiesAsync();
        }
    }

    public async Task ListLobbiesAsync()
    {
        try
        {
            var opts = new QueryLobbiesOptions
            {
                Filters = new List<QueryFilter>
                {
                    new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ),
                    new(QueryFilter.FieldOptions.S1, "waiting", QueryFilter.OpOptions.EQ),
                },
            };

            QueryResponse resp = await LobbyService.Instance.QueryLobbiesAsync(opts);
            LobbyListChanged?.Invoke(
                this,
                new LobbyListChangedEventArgs { lobbyList = resp.Results }
            );
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning(e);
        }
    }

    public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, bool isPrivate)
    {
        var lobby = await LobbyService.Instance.CreateLobbyAsync(
            lobbyName,
            maxPlayers,
            new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Data = new Dictionary<string, DataObject>
                {
                    {
                        "state",
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            "waiting",
                            DataObject.IndexOptions.S1
                        )
                    },
                },
            }
        );

        CurrentLobby = lobby;
        return lobby;
    }

    public async Task<Lobby> QuickJoinAsync()
    {
        var opts = new QuickJoinLobbyOptions
        {
            Filter = new List<QueryFilter>
            {
                new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                new(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ),
                new(QueryFilter.FieldOptions.S1, "waiting", QueryFilter.OpOptions.EQ),
            },
        };

        CurrentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(opts);
        return CurrentLobby;
    }

    public async Task<Lobby> JoinWithIdAsync(string lobbyId)
    {
        CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
        return CurrentLobby;
    }

    public async Task<Lobby> JoinWithCodeAsync(string lobbyCode)
    {
        CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
        return CurrentLobby;
    }

    public async Task SetRelayJoinCodeAsync(string lobbyId, string relayJoinCode)
    {
        await LobbyService.Instance.UpdateLobbyAsync(
            lobbyId,
            new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {
                        KEY_RELAY_JOIN_CODE,
                        new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)
                    },
                },
            }
        );
    }

    public string GetRelayJoinCodeFromLobby(Lobby lobby)
    {
        if (lobby == null)
            return null;
        if (lobby.Data == null)
            return null;
        if (!lobby.Data.TryGetValue(KEY_RELAY_JOIN_CODE, out var obj))
            return null;
        return obj.Value;
    }

    public async Task SetLobbyStateAsync(bool isLocked, string state)
    {
        if (CurrentLobby == null)
            return;

        CurrentLobby = await LobbyService.Instance.UpdateLobbyAsync(
            CurrentLobby.Id,
            new UpdateLobbyOptions
            {
                IsLocked = isLocked,
                Data = new Dictionary<string, DataObject>
                {
                    {
                        "state",
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            state,
                            DataObject.IndexOptions.S1
                        )
                    },
                },
            }
        );
    }

    public async Task LeaveOrDeleteLobbyAsync()
    {
        if (CurrentLobby == null)
            return;

        try
        {
            if (IsLobbyHost())
                await LobbyService.Instance.DeleteLobbyAsync(CurrentLobby.Id);
            else
                await LobbyService.Instance.RemovePlayerAsync(
                    CurrentLobby.Id,
                    AuthenticationService.Instance.PlayerId
                );
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning(e);
        }
    }

    public async Task DeleteLobbyIfHostAsync()
    {
        try
        {
            if (CurrentLobby != null && IsLobbyHost())
                await LobbyService.Instance.DeleteLobbyAsync(CurrentLobby.Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning(e);
        }
    }

    public void ClearLocalLobby() => CurrentLobby = null;

    private bool IsLobbyHost()
    {
        return CurrentLobby != null
            && AuthenticationService.Instance.IsSignedIn
            && CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;
    }
}

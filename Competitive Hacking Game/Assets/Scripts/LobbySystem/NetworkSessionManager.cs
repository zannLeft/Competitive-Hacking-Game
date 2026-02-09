using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkSessionManager : MonoBehaviour
{
    public event Action<ulong> ServerClientConnected;
    public event Action<ulong> LocalClientDisconnected;

    private bool _callbacksRegistered;

    public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    public ulong LocalClientId =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

    public GameObject LocalPlayerObject =>
        NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClient?.PlayerObject?.gameObject
            : null;

    public void ConfigureTransport(RelayServerData relayServerData)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null)
            return;

        utp.SetRelayServerData(relayServerData);
    }

    public void RegisterConnectionCallbacks()
    {
        if (_callbacksRegistered)
            return;
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        _callbacksRegistered = true;
    }

    public void UnregisterConnectionCallbacks()
    {
        if (!_callbacksRegistered)
            return;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        _callbacksRegistered = false;
    }

    public void StartHost()
    {
        if (NetworkManager.Singleton == null)
            return;
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        if (NetworkManager.Singleton == null)
            return;
        NetworkManager.Singleton.StartClient();
    }

    public void ShutdownSession()
    {
        UnregisterConnectionCallbacks();

        if (
            NetworkManager.Singleton != null
            && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
        )
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // Only server/host should decide spawn placement
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            ServerClientConnected?.Invoke(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        // If THIS local client got disconnected, tell the manager to restore UI/menu state
        if (!nm.IsServer && clientId == nm.LocalClientId)
            LocalClientDisconnected?.Invoke(clientId);
    }
}

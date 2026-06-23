using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MatchFlowManager : MonoBehaviour
{
    public bool IsMatchInProgress { get; set; }

    private string _interiorSceneName = "Interior_01";
    private string _lobbySpawnTag = "LobbySpawn";
    private string _interiorSpawnTag = "InteriorSpawn";

    private TeleportService _teleport;
    private NetworkSessionManager _session;
    private RoundRoleManager _roles;
    private RoundResetManager _roundReset;
    private Coroutine _interiorInitializationRoutine;

    public void Configure(string interiorSceneName, string lobbySpawnTag, string interiorSpawnTag)
    {
        _interiorSceneName = interiorSceneName;
        _lobbySpawnTag = lobbySpawnTag;
        _interiorSpawnTag = interiorSpawnTag;
    }

    private void Awake()
    {
        _teleport = GetComponent<TeleportService>();
        _session = GetComponent<NetworkSessionManager>();

        _roles = GetComponent<RoundRoleManager>();
        if (_roles == null)
            _roles = gameObject.AddComponent<RoundRoleManager>();

        _roundReset = GetComponent<RoundResetManager>();
        if (_roundReset == null)
            _roundReset = gameObject.AddComponent<RoundResetManager>();
    }

    public void StartMatchAsHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        if (IsInteriorLoaded())
        {
            if (!IsMatchInProgress)
            {
                Debug.LogWarning(
                    "[MatchFlow] Interior scene was already loaded before match start. Treating it as stale and reloading cleanly."
                );

                StartCoroutine(UnloadStaleInteriorThenStartMatch());
                return;
            }

            Debug.LogWarning("[MatchFlow] Interior already loaded; ignoring StartMatch.");
            return;
        }

        BeginMatchStartSequence();
    }

    private IEnumerator UnloadStaleInteriorThenStartMatch()
    {
        var interior = SceneManager.GetSceneByName(_interiorSceneName);

        if (interior.IsValid() && interior.isLoaded)
        {
            AsyncOperation op = SceneManager.UnloadSceneAsync(interior);

            while (op != null && !op.isDone)
                yield return null;
        }

        yield return null;

        BeginMatchStartSequence();
    }

    private void BeginMatchStartSequence()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        IsMatchInProgress = true;

        _roundReset?.ResetAllPlayersForMatchStart();
        _roles?.ResetRoles();
        _roles?.AssignRandomBadGuy();

        var nsm = NetworkManager.Singleton.SceneManager;
        nsm.OnLoadEventCompleted += OnInteriorLoadCompleted;
        nsm.LoadScene(_interiorSceneName, LoadSceneMode.Additive);
    }

    private void OnInteriorLoadCompleted(
        string sceneName,
        LoadSceneMode mode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        if (sceneName != _interiorSceneName)
            return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnInteriorLoadCompleted;

        if (_interiorInitializationRoutine != null)
            StopCoroutine(_interiorInitializationRoutine);

        _interiorInitializationRoutine = StartCoroutine(
            InitializeInteriorThenTeleport()
        );
    }

    private IEnumerator InitializeInteriorThenTeleport()
    {
        // Let in-scene RouterBox and NetworkObject instances finish enabling/spawning.
        yield return null;

        const float coordinatorTimeoutSeconds = 5f;
        float remaining = coordinatorTimeoutSeconds;
        RouterHackCoordinator coordinator = null;

        while (remaining > 0f)
        {
            coordinator = RouterHackCoordinator.Instance;

            if (coordinator == null)
            {
                coordinator = FindAnyObjectByType<RouterHackCoordinator>(
                    FindObjectsInactive.Include
                );
            }

            if (coordinator != null && coordinator.IsSpawned)
                break;

            remaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (coordinator == null || !coordinator.IsSpawned)
        {
            Debug.LogError(
                "[MatchFlow] No spawned RouterHackCoordinator was found in the interior scene."
            );
        }
        else if (!coordinator.ServerInitializeAssignments())
        {
            Debug.LogError(
                "[MatchFlow] Router minigame assignments could not be initialized."
            );
        }

        TeleportAllClients(_interiorSpawnTag);
        _interiorInitializationRoutine = null;
    }

    public void EndMatchAsHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        IsMatchInProgress = false;

        _roundReset?.ResetAllPlayersForMatchEnd();
        _roles?.ResetRoles();

        if (!IsInteriorLoaded())
        {
            TeleportAllClients(_lobbySpawnTag);
            return;
        }

        var nsm = NetworkManager.Singleton.SceneManager;
        nsm.OnUnloadEventCompleted += OnInteriorUnloadCompleted;

        var interior = SceneManager.GetSceneByName(_interiorSceneName);
        if (interior.IsValid() && interior.isLoaded)
        {
            nsm.UnloadScene(interior);
        }
        else
        {
            nsm.OnUnloadEventCompleted -= OnInteriorUnloadCompleted;
            Debug.LogWarning("[MatchFlow] Interior scene not found/loaded when trying to unload.");
            TeleportAllClients(_lobbySpawnTag);
        }
    }

    private void OnInteriorUnloadCompleted(
        string sceneName,
        LoadSceneMode mode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        if (sceneName != _interiorSceneName)
            return;

        NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted -= OnInteriorUnloadCompleted;

        TeleportAllClients(_lobbySpawnTag);
    }

    public void EndGameToLobbyForEveryone()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        _roundReset?.ResetAllPlayersForMatchEnd();
        _roles?.ResetRoles();

        if (IsInteriorLoaded())
        {
            var nsm = NetworkManager.Singleton.SceneManager;
            nsm.OnUnloadEventCompleted += OnUnloadThenShutdown;

            var interior = SceneManager.GetSceneByName(_interiorSceneName);
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

    private void OnUnloadThenShutdown(
        string sceneName,
        LoadSceneMode mode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        if (sceneName != _interiorSceneName)
            return;

        NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted -= OnUnloadThenShutdown;

        StartCoroutine(ShutdownNextFrame());
    }

    private IEnumerator ShutdownNextFrame()
    {
        yield return null;

        IsMatchInProgress = false;

        var session = _session != null ? _session : GetComponent<NetworkSessionManager>();
        if (session != null)
            session.ShutdownSession();

        UnloadInteriorLocalIfLoaded();

        LobbyManager.Instance?.SceneUI?.ShowLobbyScreen();
        LobbyManager.Instance?.Services?.ClearLocalLobby();
    }

    public void ServerTeleportClientToLobbySpawnWhenReady(ulong clientId)
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
            return;

        StartCoroutine(TeleportClientToSpawnWhenReady(clientId, _lobbySpawnTag));
    }

    private IEnumerator TeleportClientToSpawnWhenReady(ulong clientId, string spawnTag)
    {
        var nm = NetworkManager.Singleton;

        if (nm == null)
            yield break;

        float timeout = 8f;

        while (timeout > 0f)
        {
            if (
                nm.ConnectedClients != null
                && nm.ConnectedClients.TryGetValue(clientId, out var client)
                && client.PlayerObject != null
            )
                break;

            timeout -= Time.deltaTime;
            yield return null;
        }

        if (
            nm.ConnectedClients == null
            || !nm.ConnectedClients.TryGetValue(clientId, out var cc)
            || cc.PlayerObject == null
        )
        {
            Debug.LogWarning(
                $"[MatchFlow] PlayerObject not ready for client {clientId}, cannot teleport."
            );
            yield break;
        }

        var spawns = GameObject.FindGameObjectsWithTag(spawnTag);

        if (spawns == null || spawns.Length == 0)
        {
            Debug.LogWarning($"[MatchFlow] No objects tagged '{spawnTag}' found.");
            yield break;
        }

        int idx = (int)(clientId % (ulong)spawns.Length);
        Transform t = spawns[idx].transform;

        var tp = _teleport != null ? _teleport : GetComponent<TeleportService>();
        tp?.SendTeleportToClient(clientId, t.position, t.rotation);
    }

    private void TeleportAllClients(string spawnTag)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        var spawns = GameObject.FindGameObjectsWithTag(spawnTag);

        if (spawns == null || spawns.Length == 0)
        {
            Debug.LogWarning($"[MatchFlow] No spawn objects with tag '{spawnTag}' found.");
            return;
        }

        var tp = _teleport != null ? _teleport : GetComponent<TeleportService>();

        if (tp == null)
            return;

        int i = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            Transform t = spawns[i % spawns.Length].transform;
            i++;

            tp.SendTeleportToClient(client.ClientId, t.position, t.rotation);
        }
    }

    private bool IsInteriorLoaded()
    {
        var s = SceneManager.GetSceneByName(_interiorSceneName);
        return s.IsValid() && s.isLoaded;
    }

    public void UnloadInteriorLocalIfLoaded()
    {
        var s = SceneManager.GetSceneByName(_interiorSceneName);

        if (s.IsValid() && s.isLoaded)
            SceneManager.UnloadSceneAsync(s);
    }
}
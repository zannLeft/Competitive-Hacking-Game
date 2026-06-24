using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MatchResultType : byte
{
    None = 0,
    SurvivorsWon = 1,
    BadGuyWon = 2,
    Aborted = 3,
}

[DisallowMultipleComponent]
public class MatchFlowManager : MonoBehaviour
{
    public bool IsMatchInProgress { get; set; }

    public MatchResultType CurrentMatchResult { get; private set; } = MatchResultType.None;
    public bool HasCommittedMatchResult => CurrentMatchResult != MatchResultType.None;

    /// <summary>
    /// Server-side only in Stage 1. A later stage can mirror this result to clients and UI.
    /// </summary>
    public event Action<MatchResultType> ServerMatchResultCommitted;

    private string _interiorSceneName = "Interior_01";
    private string _lobbySpawnTag = "LobbySpawn";
    private string _interiorSpawnTag = "InteriorSpawn";

    private TeleportService _teleport;
    private NetworkSessionManager _session;
    private RoundRoleManager _roles;
    private RoundResetManager _roundReset;
    private Coroutine _interiorInitializationRoutine;

    // Only clients present when the round starts are allowed to participate in that round.
    // This prevents a late joiner from being counted as a survivor or attacked mid-match.
    private readonly HashSet<ulong> _activeMatchParticipantClientIds = new();
    private readonly List<PlayerLifeState> _survivorBuffer = new();

    private bool _isEvaluatingSurvivors;
    private bool _survivorEvaluationRequested;

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

        if (_session != null)
            _session.ServerClientDisconnected += OnServerClientDisconnected;
    }

    private void OnDestroy()
    {
        if (_session != null)
            _session.ServerClientDisconnected -= OnServerClientDisconnected;
    }

    private void Update()
    {
        if (!CanRunServerMatchAuthority())
            return;

        ServerProcessExpiredBleedOuts();
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

        ResetServerMatchAuthorityForNewRound();
        IsMatchInProgress = true;

        _roundReset?.ResetAllPlayersForMatchStart();
        _roles?.ResetRoles();
        _roles?.AssignRandomBadGuy();
        CaptureActiveMatchParticipants();

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

    /// <summary>
    /// Server-authoritative attack entry point. It decides whether the target becomes Downed
    /// or skips directly to Dead under the last-living-survivor rule.
    /// </summary>
    public bool ServerTryApplySurvivorHit(
        PlayerLifeState target,
        ulong attackerClientId,
        Vector3 downedPosition,
        Vector3 ragdollImpulse,
        Vector3 ragdollForcePosition
    )
    {
        if (!CanRunServerMatchAuthority())
            return false;

        if (target == null || !target.IsSpawned || !target.CanBeAttacked)
            return false;

        if (!_activeMatchParticipantClientIds.Contains(attackerClientId))
            return false;

        if (!_activeMatchParticipantClientIds.Contains(target.OwnerClientId))
            return false;

        if (_roles == null || !_roles.IsClientBadGuy(attackerClientId))
            return false;

        int otherAliveSurvivors = CountAliveSurvivorsExcluding(target.OwnerClientId);

        if (otherAliveSurvivors <= 0)
        {
            target.ServerSetDead(
                attackerClientId,
                ragdollImpulse,
                ragdollForcePosition
            );
        }
        else
        {
            target.ServerSetDowned(
                downedPosition,
                attackerClientId,
                ragdollImpulse,
                ragdollForcePosition
            );
        }

        return true;
    }

    /// <summary>
    /// Called by PlayerLifeState after a server-owned state transition.
    /// </summary>
    public void ServerNotifyPlayerLifeStateChanged(
        PlayerLifeState player,
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState
    )
    {
        if (!CanRunServerMatchAuthority())
            return;

        if (player == null || !_activeMatchParticipantClientIds.Contains(player.OwnerClientId))
            return;

        if (player.IsBadGuy)
            return;

        ServerRequestSurvivorEvaluation();
    }

    private void ServerProcessExpiredBleedOuts()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        double now = nm.ServerTime.Time;
        CollectActiveSurvivors(_survivorBuffer);

        for (int i = 0; i < _survivorBuffer.Count; i++)
        {
            if (HasCommittedMatchResult)
                break;

            PlayerLifeState survivor = _survivorBuffer[i];

            if (survivor == null || !survivor.ServerHasBleedOutExpired(now))
                continue;

            survivor.ServerSetDead(survivor.LastAttackerClientId.Value);
        }
    }

    private void ServerRequestSurvivorEvaluation()
    {
        if (!CanRunServerMatchAuthority())
            return;

        if (_isEvaluatingSurvivors)
        {
            _survivorEvaluationRequested = true;
            return;
        }

        _isEvaluatingSurvivors = true;

        try
        {
            do
            {
                _survivorEvaluationRequested = false;
                ServerEvaluateSurvivorsOnce();
            } while (_survivorEvaluationRequested && !HasCommittedMatchResult);
        }
        finally
        {
            _isEvaluatingSurvivors = false;
        }
    }

    private void ServerEvaluateSurvivorsOnce()
    {
        CollectActiveSurvivors(_survivorBuffer);

        int aliveCount = 0;
        int downedCount = 0;
        int deadCount = 0;

        for (int i = 0; i < _survivorBuffer.Count; i++)
        {
            PlayerLifeState survivor = _survivorBuffer[i];

            if (survivor == null)
                continue;

            switch (survivor.CurrentState)
            {
                case PlayerLifeStateType.Alive:
                    aliveCount++;
                    break;

                case PlayerLifeStateType.Downed:
                    downedCount++;
                    break;

                case PlayerLifeStateType.Dead:
                    deadCount++;
                    break;
            }
        }

        // A living survivor can still revive downed teammates, so the match continues.
        if (aliveCount > 0)
            return;

        // Nobody remains capable of reviving. Convert every stranded Downed survivor
        // to permanent death before committing the bad-guy result.
        if (downedCount > 0)
        {
            PlayerLifeState[] strandedDowned = _survivorBuffer.ToArray();

            for (int i = 0; i < strandedDowned.Length; i++)
            {
                PlayerLifeState survivor = strandedDowned[i];

                if (survivor == null || !survivor.IsDowned)
                    continue;

                survivor.ServerSetDead(survivor.LastAttackerClientId.Value);
            }

            _survivorEvaluationRequested = true;
            return;
        }

        // Zero valid survivors also counts as a bad-guy win when a bad guy still exists.
        // If role assignment failed, use a neutral aborted result instead.
        bool hasValidBadGuy =
            _roles != null
            && _roles.HasBadGuy
            && _activeMatchParticipantClientIds.Contains(_roles.CurrentBadGuyClientId);

        ServerCommitMatchResult(
            hasValidBadGuy ? MatchResultType.BadGuyWon : MatchResultType.Aborted
        );
    }

    public bool ServerCommitMatchResult(MatchResultType result)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return false;

        if (!IsMatchInProgress || result == MatchResultType.None)
            return false;

        if (HasCommittedMatchResult)
            return false;

        CurrentMatchResult = result;

        Debug.Log($"[MatchFlow] Match result committed exactly once: {result}.");
        ServerMatchResultCommitted?.Invoke(result);
        return true;
    }

    private void OnServerClientDisconnected(ulong clientId)
    {
        if (!IsMatchInProgress || HasCommittedMatchResult)
            return;

        if (!_activeMatchParticipantClientIds.Contains(clientId))
            return;

        bool disconnectedBadGuy = _roles != null && _roles.IsClientBadGuy(clientId);
        _activeMatchParticipantClientIds.Remove(clientId);

        if (disconnectedBadGuy)
        {
            Debug.LogWarning(
                "[MatchFlow] The bad guy disconnected during the match. Stage 1 resolves this as an aborted match."
            );
            ServerCommitMatchResult(MatchResultType.Aborted);
            return;
        }

        ServerRequestSurvivorEvaluation();
    }

    private int CountAliveSurvivorsExcluding(ulong excludedClientId)
    {
        CollectActiveSurvivors(_survivorBuffer);

        int count = 0;

        for (int i = 0; i < _survivorBuffer.Count; i++)
        {
            PlayerLifeState survivor = _survivorBuffer[i];

            if (survivor == null || survivor.OwnerClientId == excludedClientId)
                continue;

            if (survivor.IsAlive)
                count++;
        }

        return count;
    }

    private void CollectActiveSurvivors(List<PlayerLifeState> results)
    {
        results.Clear();

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.ConnectedClients == null)
            return;

        foreach (ulong clientId in _activeMatchParticipantClientIds)
        {
            if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                continue;

            if (client == null || client.PlayerObject == null)
                continue;

            PlayerLifeState lifeState = GetPlayerLifeState(client.PlayerObject);

            if (lifeState == null || lifeState.IsBadGuy)
                continue;

            results.Add(lifeState);
        }
    }

    private void CaptureActiveMatchParticipants()
    {
        _activeMatchParticipantClientIds.Clear();

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.ConnectedClientsList == null)
            return;

        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            if (GetPlayerLifeState(client.PlayerObject) == null)
                continue;

            _activeMatchParticipantClientIds.Add(client.ClientId);
        }

        Debug.Log(
            $"[MatchFlow] Captured {_activeMatchParticipantClientIds.Count} active round participants."
        );
    }

    private PlayerLifeState GetPlayerLifeState(NetworkObject playerObject)
    {
        if (playerObject == null)
            return null;

        PlayerLifeState lifeState = playerObject.GetComponent<PlayerLifeState>();

        if (lifeState == null)
            lifeState = playerObject.GetComponentInChildren<PlayerLifeState>(true);

        return lifeState;
    }

    public bool ServerIsActiveRoundParticipant(ulong clientId)
    {
        if (!CanRunServerMatchAuthority())
            return false;

        if (!_activeMatchParticipantClientIds.Contains(clientId))
            return false;

        NetworkManager nm = NetworkManager.Singleton;
        return nm != null
            && nm.ConnectedClients != null
            && nm.ConnectedClients.ContainsKey(clientId);
    }

    public bool ServerCanUseSurvivorInteraction(ulong actorClientId, ulong targetClientId)
    {
        if (!ServerIsActiveRoundParticipant(actorClientId))
            return false;

        if (!ServerIsActiveRoundParticipant(targetClientId))
            return false;

        if (_roles != null && _roles.IsClientBadGuy(actorClientId))
            return false;

        return true;
    }

    private bool CanRunServerMatchAuthority()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer
            && IsMatchInProgress
            && !HasCommittedMatchResult;
    }

    private void ResetServerMatchAuthorityForNewRound()
    {
        CurrentMatchResult = MatchResultType.None;
        _activeMatchParticipantClientIds.Clear();
        _survivorBuffer.Clear();
        _isEvaluatingSurvivors = false;
        _survivorEvaluationRequested = false;
    }

    public void EndMatchAsHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        IsMatchInProgress = false;
        _activeMatchParticipantClientIds.Clear();

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

        IsMatchInProgress = false;
        _activeMatchParticipantClientIds.Clear();

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

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class RouterHackCoordinator : NetworkBehaviour
{
    public static RouterHackCoordinator Instance { get; private set; }

    [Header("Minigame Roster")]
    [SerializeField]
    private LaptopMinigameCatalog minigameCatalog;

    [Header("Debug")]
    [SerializeField]
    private bool logAssignments = true;

    private NetworkList<RouterHackRecord> _records;

    private readonly NetworkVariable<bool> _assignmentsReady = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action RecordsChanged;

    public bool AssignmentsReady => _assignmentsReady.Value;
    public int RecordCount => _records != null ? _records.Count : 0;
    public LaptopMinigameCatalog MinigameCatalog => minigameCatalog;

    private void Awake()
    {
        _records = new NetworkList<RouterHackRecord>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                "[RouterHackCoordinator] More than one coordinator is active.",
                this
            );
        }

        Instance = this;

        _records.OnListChanged += OnRecordsListChanged;
        _assignmentsReady.OnValueChanged += OnAssignmentsReadyChanged;

        if (!IsServer && AssignmentsReady && logAssignments)
            LogCurrentAssignments("initial synchronized state");
    }

    public override void OnNetworkDespawn()
    {
        _records.OnListChanged -= OnRecordsListChanged;
        _assignmentsReady.OnValueChanged -= OnAssignmentsReadyChanged;

        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }

    public bool ServerInitializeAssignments()
    {
        if (!IsServer)
        {
            Debug.LogWarning(
                "[RouterHackCoordinator] Only the server may initialize assignments.",
                this
            );
            return false;
        }

        _assignmentsReady.Value = false;
        _records.Clear();

        if (minigameCatalog == null)
        {
            Debug.LogError(
                "[RouterHackCoordinator] No LaptopMinigameCatalog is assigned.",
                this
            );
            return false;
        }

        List<string> networkIds = GatherUniqueNetworkIds();

        if (networkIds.Count == 0)
        {
            Debug.LogWarning(
                "[RouterHackCoordinator] No registered RouterBox networks were found.",
                this
            );

            _assignmentsReady.Value = true;
            return true;
        }

        var random = new System.Random(CreateMatchSeed());

        for (int i = 0; i < networkIds.Count; i++)
        {
            if (!minigameCatalog.TryChooseRandom(random, out var definition))
            {
                Debug.LogError(
                    "[RouterHackCoordinator] The catalog has no selectable minigames.",
                    minigameCatalog
                );

                _records.Clear();
                return false;
            }

            LaptopMinigameDifficulty difficulty = definition.ChooseDifficulty(random);
            int puzzleSeed = random.Next(1, int.MaxValue);

            _records.Add(
                new RouterHackRecord(
                    networkIds[i],
                    definition.MinigameId,
                    difficulty,
                    puzzleSeed
                )
            );
        }

        _assignmentsReady.Value = true;

        if (logAssignments)
            LogCurrentAssignments("server assignment");

        return true;
    }

    public bool TryGetRecord(string networkId, out RouterHackRecord record)
    {
        string normalizedId = NormalizeNetworkId(networkId);

        if (_records != null)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                RouterHackRecord candidate = _records[i];

                if (
                    string.Equals(
                        candidate.NetworkId.ToString(),
                        normalizedId,
                        StringComparison.Ordinal
                    )
                )
                {
                    record = candidate;
                    return true;
                }
            }
        }

        record = default;
        return false;
    }

    public bool IsCompleted(string networkId)
    {
        return TryGetRecord(networkId, out var record) && record.Completed;
    }

    public bool TryGetMinigameDefinition(
        ushort minigameId,
        out LaptopMinigameDefinition definition
    )
    {
        if (minigameCatalog == null)
        {
            definition = null;
            return false;
        }

        return minigameCatalog.TryGetById(minigameId, out definition);
    }

    public bool ServerMarkCompleted(string networkId)
    {
        if (!IsServer || _records == null)
            return false;

        string normalizedId = NormalizeNetworkId(networkId);

        for (int i = 0; i < _records.Count; i++)
        {
            RouterHackRecord record = _records[i];

            if (
                !string.Equals(
                    record.NetworkId.ToString(),
                    normalizedId,
                    StringComparison.Ordinal
                )
            )
                continue;

            if (record.Completed)
                return false;

            record.Completed = true;
            _records[i] = record;
            return true;
        }

        return false;
    }

    private static List<string> GatherUniqueNetworkIds()
    {
        var uniqueIds = new SortedSet<string>(StringComparer.Ordinal);
        var routers = RouterRegistry.Routers;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];

            if (router == null)
                continue;

            string networkId = NormalizeNetworkId(router.NetworkId);

            if (!string.IsNullOrEmpty(networkId))
                uniqueIds.Add(networkId);
        }

        return new List<string>(uniqueIds);
    }

    private static string NormalizeNetworkId(string networkId)
    {
        return string.IsNullOrWhiteSpace(networkId) ? string.Empty : networkId.Trim();
    }

    private int CreateMatchSeed()
    {
        unchecked
        {
            int seed = Environment.TickCount;
            seed = (seed * 397) ^ DateTime.UtcNow.Ticks.GetHashCode();
            seed = (seed * 397) ^ GetEntityId().GetHashCode();
            return seed;
        }
    }

    private void OnRecordsListChanged(
        NetworkListEvent<RouterHackRecord> changeEvent
    )
    {
        RecordsChanged?.Invoke();
    }

    private void OnAssignmentsReadyChanged(bool previousValue, bool newValue)
    {
        RecordsChanged?.Invoke();

        if (!IsServer && newValue && logAssignments)
            LogCurrentAssignments("client synchronized state");
    }

    private void LogCurrentAssignments(string source)
    {
        Debug.Log(
            $"[RouterHackCoordinator] {source}: {_records.Count} network assignment(s).",
            this
        );

        for (int i = 0; i < _records.Count; i++)
        {
            RouterHackRecord record = _records[i];
            string minigameName = record.MinigameId.ToString();

            if (
                minigameCatalog != null
                && minigameCatalog.TryGetById(record.MinigameId, out var definition)
            )
            {
                minigameName = definition.DisplayName;
            }

            Debug.Log(
                $"[RouterHackCoordinator] {record.NetworkId} -> "
                + $"{minigameName}, {record.Difficulty}, seed {record.Seed}, "
                + $"completed={record.Completed}",
                this
            );
        }
    }
}

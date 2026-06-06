using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class DownedBodyObject : NetworkBehaviour
{
    private static readonly List<DownedBodyObject> spawnedBodies = new List<DownedBodyObject>();

    public static IReadOnlyList<DownedBodyObject> SpawnedBodies => spawnedBodies;

    [Header("Optional Anchors")]
    [SerializeField]
    private Transform cameraAnchor;

    [SerializeField]
    private Transform reviveAnchor;

    public NetworkVariable<ulong> DownedPlayerClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> SourcePlayerNetworkObjectId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<PlayerLifeStateType> BodyState = new NetworkVariable<PlayerLifeStateType>(
        PlayerLifeStateType.Downed,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public Transform CameraAnchor => cameraAnchor != null ? cameraAnchor : transform;

    public Transform ReviveAnchor => reviveAnchor != null ? reviveAnchor : transform;

    public bool IsRevivable => BodyState.Value == PlayerLifeStateType.Downed;

    public bool IsDeadBody => BodyState.Value == PlayerLifeStateType.Dead;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RegisterBody(this);
    }

    public override void OnNetworkDespawn()
    {
        UnregisterBody(this);
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnregisterBody(this);
        base.OnDestroy();
    }

    public bool IsForPlayer(ulong clientId)
    {
        return DownedPlayerClientId.Value == clientId;
    }

    public void InitializeServer(
        ulong downedPlayerClientId,
        ulong sourcePlayerNetworkObjectId,
        PlayerLifeStateType bodyState
    )
    {
        if (!IsServer)
            return;

        DownedPlayerClientId.Value = downedPlayerClientId;
        SourcePlayerNetworkObjectId.Value = sourcePlayerNetworkObjectId;
        BodyState.Value = bodyState;
    }

    public void ServerSetBodyState(PlayerLifeStateType bodyState)
    {
        if (!IsServer)
            return;

        BodyState.Value = bodyState;
    }

    private static void RegisterBody(DownedBodyObject body)
    {
        if (body == null)
            return;

        if (!spawnedBodies.Contains(body))
            spawnedBodies.Add(body);
    }

    private static void UnregisterBody(DownedBodyObject body)
    {
        if (body == null)
            return;

        spawnedBodies.Remove(body);
    }
}
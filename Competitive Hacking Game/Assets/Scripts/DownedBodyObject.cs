using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class DownedBodyObject : NetworkBehaviour
{
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
}

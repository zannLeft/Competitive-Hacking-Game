using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class DownedBodyObject : NetworkBehaviour
{
    private static readonly List<DownedBodyObject> spawnedBodies = new List<DownedBodyObject>();

    public static IReadOnlyList<DownedBodyObject> SpawnedBodies => spawnedBodies;

    [Header("Renderers")]
    [SerializeField]
    private SkinnedMeshRenderer headRenderer;

    [SerializeField]
    private SkinnedMeshRenderer bodyRenderer;

    [Header("Material Slots")]
    [SerializeField]
    private int headShirtMaterialIndex = 2;

    [SerializeField]
    private int[] bodyShirtMaterialIndices = { 1, 2, 6, 7 };

    [Header("Pose Copy")]
    [Tooltip("Only transforms whose names start with this prefix will copy pose from the original player.")]
    [SerializeField]
    private string copiedBoneNamePrefix = "mixamorig:";

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

    public NetworkVariable<int> ShirtIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsBadGuyBody = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public Transform CameraAnchor => cameraAnchor != null ? cameraAnchor : transform;

    public Transform ReviveAnchor => reviveAnchor != null ? reviveAnchor : transform;

    public bool IsRevivable => BodyState.Value == PlayerLifeStateType.Downed;

    public bool IsDeadBody => BodyState.Value == PlayerLifeStateType.Dead;

    private void Reset()
    {
        CacheRendererReferences();
    }

    private void Awake()
    {
        CacheRendererReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        RegisterBody(this);

        ShirtIndex.OnValueChanged += HandleAppearanceChanged;
        IsBadGuyBody.OnValueChanged += HandleAppearanceChanged;

        ApplyAppearance();
    }

    public override void OnNetworkDespawn()
    {
        ShirtIndex.OnValueChanged -= HandleAppearanceChanged;
        IsBadGuyBody.OnValueChanged -= HandleAppearanceChanged;

        UnregisterBody(this);

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnregisterBody(this);
        base.OnDestroy();
    }

    private void CacheRendererReferences()
    {
        if (headRenderer == null)
        {
            Transform head = transform.Find("Head");
            if (head != null)
                headRenderer = head.GetComponent<SkinnedMeshRenderer>();
        }

        if (bodyRenderer == null)
        {
            Transform body = transform.Find("Botee");
            if (body != null)
                bodyRenderer = body.GetComponent<SkinnedMeshRenderer>();
        }
    }

    private void HandleAppearanceChanged<T>(T previousValue, T newValue)
    {
        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        CacheRendererReferences();

        if (LobbyManager.Instance == null)
            return;

        CosmeticsManager cosmetics = LobbyManager.Instance.Cosmetics;
        if (cosmetics == null)
            return;

        Material shirtMaterial = cosmetics.GetShirtMaterial(ShirtIndex.Value);

        if (IsBadGuyBody.Value && cosmetics.BlackShirtMaterial != null)
            shirtMaterial = cosmetics.BlackShirtMaterial;

        if (shirtMaterial == null)
            return;

        ApplyHeadMaterial(shirtMaterial);
        ApplyBodyMaterial(shirtMaterial);
    }

    private void ApplyHeadMaterial(Material shirtMaterial)
    {
        if (headRenderer == null)
            return;

        Material[] materials = headRenderer.materials;

        if (headShirtMaterialIndex >= 0 && materials.Length > headShirtMaterialIndex)
        {
            materials[headShirtMaterialIndex] = shirtMaterial;
            headRenderer.materials = materials;
        }
    }

    private void ApplyBodyMaterial(Material shirtMaterial)
    {
        if (bodyRenderer == null)
            return;

        Material[] materials = bodyRenderer.materials;

        if (bodyShirtMaterialIndices != null)
        {
            for (int i = 0; i < bodyShirtMaterialIndices.Length; i++)
            {
                int materialIndex = bodyShirtMaterialIndices[i];

                if (materialIndex >= 0 && materials.Length > materialIndex)
                    materials[materialIndex] = shirtMaterial;
            }
        }

        bodyRenderer.materials = materials;
    }

    public bool IsForPlayer(ulong clientId)
    {
        return DownedPlayerClientId.Value == clientId;
    }

    public void InitializeServer(
        ulong downedPlayerClientId,
        ulong sourcePlayerNetworkObjectId,
        PlayerLifeStateType bodyState,
        int shirtIndex,
        bool isBadGuyBody
    )
    {
        if (!IsServer)
            return;

        DownedPlayerClientId.Value = downedPlayerClientId;
        SourcePlayerNetworkObjectId.Value = sourcePlayerNetworkObjectId;
        BodyState.Value = bodyState;
        ShirtIndex.Value = shirtIndex;
        IsBadGuyBody.Value = isBadGuyBody;
    }

    public void ServerCopyPoseFromSource(NetworkObject sourcePlayerNetworkObject)
    {
        if (!IsServer)
            return;

        if (sourcePlayerNetworkObject == null)
            return;

        CopyPoseFromSourceRoot(sourcePlayerNetworkObject.transform);

        CopyPoseFromSourceClientRpc(sourcePlayerNetworkObject.NetworkObjectId);
    }

    [ClientRpc]
    private void CopyPoseFromSourceClientRpc(ulong sourcePlayerNetworkObjectId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                sourcePlayerNetworkObjectId,
                out NetworkObject sourcePlayerNetworkObject
            )
        )
            return;

        CopyPoseFromSourceRoot(sourcePlayerNetworkObject.transform);
    }

    private void CopyPoseFromSourceRoot(Transform sourceRoot)
    {
        if (sourceRoot == null)
            return;

        Dictionary<string, Transform> sourceBonesByName = BuildSourceBoneLookup(sourceRoot);

        Transform[] targetTransforms = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < targetTransforms.Length; i++)
        {
            Transform targetTransform = targetTransforms[i];

            if (targetTransform == null)
                continue;

            if (!ShouldCopyBone(targetTransform.name))
                continue;

            if (!sourceBonesByName.TryGetValue(targetTransform.name, out Transform sourceTransform))
                continue;

            targetTransform.position = sourceTransform.position;
            targetTransform.rotation = sourceTransform.rotation;
            targetTransform.localScale = sourceTransform.localScale;
        }
    }

    private Dictionary<string, Transform> BuildSourceBoneLookup(Transform sourceRoot)
    {
        Dictionary<string, Transform> lookup = new Dictionary<string, Transform>();

        Transform[] sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < sourceTransforms.Length; i++)
        {
            Transform sourceTransform = sourceTransforms[i];

            if (sourceTransform == null)
                continue;

            if (!ShouldCopyBone(sourceTransform.name))
                continue;

            lookup[sourceTransform.name] = sourceTransform;
        }

        return lookup;
    }

    private bool ShouldCopyBone(string transformName)
    {
        if (string.IsNullOrWhiteSpace(transformName))
            return false;

        if (string.IsNullOrWhiteSpace(copiedBoneNamePrefix))
            return false;

        return transformName.StartsWith(copiedBoneNamePrefix);
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
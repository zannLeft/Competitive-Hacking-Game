using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("Local Owner Visibility")]
    [SerializeField]
    private bool hideHeadForDownedOwner = true;

    [SerializeField]
    private ShadowCastingMode localOwnerHeadShadowMode = ShadowCastingMode.ShadowsOnly;

    [SerializeField]
    private ShadowCastingMode remoteHeadShadowMode = ShadowCastingMode.On;

    [Header("Material Slots")]
    [SerializeField]
    private int headShirtMaterialIndex = 2;

    [SerializeField]
    private int[] bodyShirtMaterialIndices = { 1, 2, 6, 7 };

    [Header("Pose Copy")]
    [Tooltip("Only transforms whose names start with this prefix will copy pose from the original player.")]
    [SerializeField]
    private string copiedBoneNamePrefix = "mixamorig:";

    [Header("Ragdoll")]
    [SerializeField]
    private DownedBodyRagdoll ragdoll;

    [SerializeField]
    private bool activateRagdollAfterPoseCopy = true;

    private bool poseCopiedAndRagdollActivated;
    private bool ragdollImpulseApplied;

    [Header("Optional Anchors")]
    [SerializeField]
    private Transform cameraAnchor;

    [SerializeField]
    private Transform reviveAnchor;

    [Header("Revive Anchor Follow")]
    [SerializeField]
    private bool followReviveAnchorToBone = true;

    [Header("Camera Anchor Follow")]
    [SerializeField]
    private bool followCameraAnchorToBone = true;

    [SerializeField]
    private Transform cameraAnchorFollowTarget;

    [SerializeField]
    private string autoCameraAnchorFollowTargetName = "mixamorig:Spine2";

    [SerializeField]
    private Vector3 cameraAnchorLocalOffset = new Vector3(0f, 0f, 0f);

    [SerializeField]
    private Vector3 cameraAnchorWorldOffset = new Vector3(0f, 0f, 0f);

    [SerializeField]
    private bool copyCameraAnchorRotation = false;

    [SerializeField]
    private Transform reviveAnchorFollowTarget;

    [SerializeField]
    private string autoReviveAnchorFollowTargetName = "mixamorig:Hips";

    [SerializeField]
    private Vector3 reviveAnchorWorldOffset = new Vector3(0f, 0.15f, 0f);

    [SerializeField]
    private bool copyReviveAnchorRotation = false;

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
        CacheRagdollReference();
        CacheAnchorFollowTargets();
    }

    private void Awake()
    {
        CacheRendererReferences();
        CacheRagdollReference();
        CacheAnchorFollowTargets();
    }

    private void LateUpdate()
    {
        UpdateReviveAnchorFollow();
        UpdateCameraAnchorFollow();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        RegisterBody(this);

        ShirtIndex.OnValueChanged += HandleAppearanceChanged;
        IsBadGuyBody.OnValueChanged += HandleAppearanceChanged;
        DownedPlayerClientId.OnValueChanged += HandleDownedPlayerClientIdChanged;

        ApplyAppearance();
        ApplyLocalHeadVisibility();
    }

    public override void OnNetworkDespawn()
    {
        ShirtIndex.OnValueChanged -= HandleAppearanceChanged;
        IsBadGuyBody.OnValueChanged -= HandleAppearanceChanged;
        DownedPlayerClientId.OnValueChanged -= HandleDownedPlayerClientIdChanged;

        UnregisterBody(this);

        poseCopiedAndRagdollActivated = false;
        ragdollImpulseApplied = false;

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnregisterBody(this);
        poseCopiedAndRagdollActivated = false;
        ragdollImpulseApplied = false;
        base.OnDestroy();
    }

    private void HandleDownedPlayerClientIdChanged(ulong previousValue, ulong newValue)
    {
        ApplyLocalHeadVisibility();
    }

    private void ApplyLocalHeadVisibility()
    {
        CacheRendererReferences();

        if (headRenderer == null)
            return;

        bool isLocalDownedOwner =
            NetworkManager.Singleton != null
            && DownedPlayerClientId.Value == NetworkManager.Singleton.LocalClientId;

        headRenderer.shadowCastingMode =
            hideHeadForDownedOwner && isLocalDownedOwner
                ? localOwnerHeadShadowMode
                : remoteHeadShadowMode;
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

    private void CacheRagdollReference()
    {
        if (ragdoll == null)
            ragdoll = GetComponent<DownedBodyRagdoll>();
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

    public void ServerCopyPoseAndActivateRagdollFromSource(
        NetworkObject sourcePlayerNetworkObject,
        Vector3 ragdollImpulse,
        Vector3 ragdollForcePosition
    )
    {
        if (!IsServer)
            return;

        if (sourcePlayerNetworkObject == null)
            return;

        CopyPoseAndActivateRagdollFromSourceTransform(sourcePlayerNetworkObject.transform);
        ApplyRagdollImpulse(ragdollImpulse, ragdollForcePosition);

        CopyPoseAndActivateRagdollFromSourceClientRpc(
            sourcePlayerNetworkObject.NetworkObjectId,
            ragdollImpulse,
            ragdollForcePosition
        );
    }

    public void CopyPoseFromSourceTransform(Transform sourceRoot)
    {
        CopyPoseFromSourceRoot(sourceRoot);
    }

    public void CopyPoseAndActivateRagdollFromSourceTransform(Transform sourceRoot)
    {
        if (poseCopiedAndRagdollActivated)
            return;

        if (sourceRoot == null)
            return;

        CacheRagdollReference();

        if (ragdoll != null)
            ragdoll.DeactivateRagdoll();

        CopyPoseFromSourceRoot(sourceRoot);

        if (activateRagdollAfterPoseCopy && ragdoll != null)
            ragdoll.SetRagdollActive(true);

        poseCopiedAndRagdollActivated = true;
    }

    [ClientRpc]
    private void CopyPoseAndActivateRagdollFromSourceClientRpc(
        ulong sourcePlayerNetworkObjectId,
        Vector3 ragdollImpulse,
        Vector3 ragdollForcePosition
    )
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

        CopyPoseAndActivateRagdollFromSourceTransform(sourcePlayerNetworkObject.transform);
        ApplyRagdollImpulse(ragdollImpulse, ragdollForcePosition);
    }

    private void ApplyRagdollImpulse(Vector3 ragdollImpulse, Vector3 ragdollForcePosition)
    {
        if (ragdollImpulseApplied)
            return;

        if (ragdollImpulse.sqrMagnitude <= 0.0001f)
            return;

        CacheRagdollReference();

        if (ragdoll == null)
            return;

        ragdoll.ActivateRagdoll(
            ragdollImpulse,
            ragdollForcePosition,
            ForceMode.Impulse
        );

        ragdollImpulseApplied = true;
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

    private void CacheAnchorFollowTargets()
    {
        if (reviveAnchorFollowTarget == null && !string.IsNullOrWhiteSpace(autoReviveAnchorFollowTargetName))
            reviveAnchorFollowTarget = FindChildRecursive(transform, autoReviveAnchorFollowTargetName);

        if (cameraAnchorFollowTarget == null && !string.IsNullOrWhiteSpace(autoCameraAnchorFollowTargetName))
            cameraAnchorFollowTarget = FindChildRecursive(transform, autoCameraAnchorFollowTargetName);
    }

    private void UpdateReviveAnchorFollow()
    {
        if (!followReviveAnchorToBone)
            return;

        if (reviveAnchor == null)
            return;

        if (reviveAnchorFollowTarget == null)
            CacheAnchorFollowTargets();

        if (reviveAnchorFollowTarget == null)
            return;

        reviveAnchor.position = reviveAnchorFollowTarget.position + reviveAnchorWorldOffset;

        if (copyReviveAnchorRotation)
            reviveAnchor.rotation = reviveAnchorFollowTarget.rotation;
    }

    private void UpdateCameraAnchorFollow()
    {
        if (!followCameraAnchorToBone)
            return;

        if (cameraAnchor == null)
            return;

        if (cameraAnchorFollowTarget == null)
            CacheAnchorFollowTargets();

        if (cameraAnchorFollowTarget == null)
            return;

        cameraAnchor.position =
            cameraAnchorFollowTarget.position
            + cameraAnchorFollowTarget.rotation * cameraAnchorLocalOffset
            + cameraAnchorWorldOffset;

        if (copyCameraAnchorRotation)
            cameraAnchor.rotation = cameraAnchorFollowTarget.rotation;
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), childName);

            if (found != null)
                return found;
        }

        return null;
    }
}
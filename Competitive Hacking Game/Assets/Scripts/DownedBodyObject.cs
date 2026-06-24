using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
    [Tooltip("When enabled, the local owner's ragdoll head uses the configured hidden shadow mode only while the body is Downed. Dead bodies always use the normal visible head mode.")]
    [SerializeField]
    private bool hideHeadForDownedOwner = true;

    [SerializeField]
    private ShadowCastingMode localOwnerHeadShadowMode = ShadowCastingMode.ShadowsOnly;

    [SerializeField]
    private ShadowCastingMode remoteHeadShadowMode = ShadowCastingMode.On;

    [Header("Carried Flashlight")]
    [SerializeField]
    private GameObject carriedFlashlightVisualRoot;

    [SerializeField]
    private Light carriedFlashlightLight;

    private UniversalAdditionalLightData carriedFlashlightAdditionalLightData;

    [SerializeField]
    private Behaviour carriedFlashlightLensFlare;

    [Tooltip("When true, the player who owns this downed body does not see their own carried lens flare, but other players still do.")]
    [SerializeField]
    private bool hideCarriedLensFlareForDownedOwner = true;

    [Header("Owner Carried Light Culling")]
    [Tooltip("When true, this downed body's own carried flashlight will not light/cast shadows on the local owner body layer on this client.")]
    [SerializeField]
    private bool excludeLocalPlayerLayerFromOwnCarriedLight = false;

    [Tooltip("Layer used by the local player's body renderers. Usually MyPlayer.")]
    [SerializeField]
    private string localPlayerLayerName = "MyPlayer";

    [Tooltip("For the local owner only, put this downed body's renderers on the local player layer so its own carried light can ignore them without preventing other players' lights from affecting them.")]
    [SerializeField]
    private bool setLocalDownedOwnerRenderersToLocalPlayerLayer = false;

    [Header("URP Rendering Layer Filtering")]
    [Tooltip("Assigns every renderer under this downed body, including the carried phone/flashlight visuals, to the downed player's unique PlayerBody rendering layer.")]
    [SerializeField]
    private bool assignOwnerRenderingLayer = true;

    [Tooltip("When true, this carried flashlight ignores only this body's owner PlayerBody rendering layer. This works on every client, not just the owner.")]
    [SerializeField]
    private bool useOwnerRenderingLayerFiltering = true;

    [Tooltip("Rendering Layer index for PlayerBody0. If you kept Default at index 0 and made PlayerBody0 index 1, set this to 1.")]
    [SerializeField]
    private int firstPlayerBodyRenderingLayerIndex = 1;

    [Tooltip("How many PlayerBody rendering layers exist. You said you made PlayerBody0-4, so this should be 5.")]
    [SerializeField]
    private int playerBodyRenderingLayerCount = 5;

    [SerializeField]
    private Renderer carriedLightBulbsRenderer;

    [SerializeField]
    private int carriedLightBulbsMaterialIndex = 1;

    [SerializeField]
    private Material carriedLightBulbsOffMaterial;

    [SerializeField]
    private Material carriedLightBulbsOnMaterial;

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

    private int originalCarriedFlashlightLightCullingMask;
    private bool hasCachedOriginalCarriedFlashlightLightCullingMask;

    private int originalCarriedFlashlightLightRenderingLayerMask;
    private bool hasCachedOriginalCarriedFlashlightLightRenderingLayerMask;

    private readonly Dictionary<Renderer, int> originalRendererLayersByRenderer = new Dictionary<Renderer, int>();

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

    public NetworkVariable<bool> CarriedFlashlightOn = new NetworkVariable<bool>(
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
        CacheOriginalCarriedFlashlightLightCullingMaskIfNeeded();
        CacheOriginalCarriedFlashlightLightRenderingLayerMaskIfNeeded();
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
        BodyState.OnValueChanged += HandleBodyStateChanged;
        CarriedFlashlightOn.OnValueChanged += HandleCarriedFlashlightChanged;

        CacheOriginalCarriedFlashlightLightCullingMaskIfNeeded();
        CacheOriginalCarriedFlashlightLightRenderingLayerMaskIfNeeded();
        ApplyOwnerRenderingLayerMaskToRenderers();
        ApplyLocalOwnerRendererLayers();
        ApplyAppearance();
        ApplyLocalHeadVisibility();
        ApplyCarriedFlashlightVisuals();
    }

    public override void OnNetworkDespawn()
    {
        ShirtIndex.OnValueChanged -= HandleAppearanceChanged;
        IsBadGuyBody.OnValueChanged -= HandleAppearanceChanged;
        DownedPlayerClientId.OnValueChanged -= HandleDownedPlayerClientIdChanged;
        BodyState.OnValueChanged -= HandleBodyStateChanged;
        CarriedFlashlightOn.OnValueChanged -= HandleCarriedFlashlightChanged;

        UnregisterBody(this);
        RestoreOriginalRendererLayers();

        poseCopiedAndRagdollActivated = false;
        ragdollImpulseApplied = false;

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnregisterBody(this);
        RestoreOriginalRendererLayers();
        poseCopiedAndRagdollActivated = false;
        ragdollImpulseApplied = false;
        base.OnDestroy();
    }

    private void HandleDownedPlayerClientIdChanged(ulong previousValue, ulong newValue)
    {
        ApplyOwnerRenderingLayerMaskToRenderers();
        ApplyLocalOwnerRendererLayers();
        ApplyLocalHeadVisibility();
        ApplyCarriedFlashlightVisuals();
    }

    private void HandleBodyStateChanged(
        PlayerLifeStateType previousValue,
        PlayerLifeStateType newValue
    )
    {
        ApplyLocalHeadVisibility();
    }

    private void ApplyLocalHeadVisibility()
    {
        CacheRendererReferences();

        if (headRenderer == null)
            return;

        bool shouldHideForLocalDownedOwner =
            hideHeadForDownedOwner
            && IsLocalDownedOwner()
            && BodyState.Value == PlayerLifeStateType.Downed;

        headRenderer.shadowCastingMode = shouldHideForLocalDownedOwner
            ? localOwnerHeadShadowMode
            : remoteHeadShadowMode;
    }

    private bool IsLocalDownedOwner()
    {
        return NetworkManager.Singleton != null
            && DownedPlayerClientId.Value == NetworkManager.Singleton.LocalClientId;
    }

    private void CacheOriginalCarriedFlashlightLightCullingMaskIfNeeded()
    {
        if (hasCachedOriginalCarriedFlashlightLightCullingMask)
            return;

        if (carriedFlashlightLight == null)
            return;

        originalCarriedFlashlightLightCullingMask = carriedFlashlightLight.cullingMask;
        hasCachedOriginalCarriedFlashlightLightCullingMask = true;
    }

    private void ApplyCarriedFlashlightLightCullingMask()
    {
        if (carriedFlashlightLight == null)
            return;

        CacheOriginalCarriedFlashlightLightCullingMaskIfNeeded();

        if (!hasCachedOriginalCarriedFlashlightLightCullingMask)
            return;

        int cullingMask = originalCarriedFlashlightLightCullingMask;

        if (excludeLocalPlayerLayerFromOwnCarriedLight && IsLocalDownedOwner())
        {
            int localPlayerLayer = LayerMask.NameToLayer(localPlayerLayerName);

            if (localPlayerLayer >= 0)
                cullingMask &= ~(1 << localPlayerLayer);
        }

        carriedFlashlightLight.cullingMask = cullingMask;
    }

    private void CacheOriginalCarriedFlashlightLightRenderingLayerMaskIfNeeded()
    {
        if (hasCachedOriginalCarriedFlashlightLightRenderingLayerMask)
            return;

        if (carriedFlashlightLight == null)
            return;

        CacheCarriedFlashlightAdditionalLightDataIfNeeded();

        // URP's Light inspector reads/writes UniversalAdditionalLightData.renderingLayers.
        // Cache that value first when available, then fall back to Light.renderingLayerMask.
        if (carriedFlashlightAdditionalLightData != null)
            originalCarriedFlashlightLightRenderingLayerMask = unchecked((int)carriedFlashlightAdditionalLightData.renderingLayers);
        else
            originalCarriedFlashlightLightRenderingLayerMask = carriedFlashlightLight.renderingLayerMask;

        hasCachedOriginalCarriedFlashlightLightRenderingLayerMask = true;
    }

    private void ApplyCarriedFlashlightLightRenderingLayerMask()
    {
        if (carriedFlashlightLight == null)
            return;

        CacheOriginalCarriedFlashlightLightRenderingLayerMaskIfNeeded();

        if (!hasCachedOriginalCarriedFlashlightLightRenderingLayerMask)
            return;

        int renderingLayerMask = originalCarriedFlashlightLightRenderingLayerMask;

        if (useOwnerRenderingLayerFiltering && DownedPlayerClientId.Value != ulong.MaxValue)
        {
            int defaultRenderingLayerMask = 1 << 0;
            int allPlayerBodyMasks = PlayerBodyRenderingLayers.GetAllPlayerBodyMasksInt(
                firstPlayerBodyRenderingLayerIndex,
                playerBodyRenderingLayerCount
            );
            int ownerBodyMask = PlayerBodyRenderingLayers.GetOwnerBodyMaskInt(
                DownedPlayerClientId.Value,
                firstPlayerBodyRenderingLayerIndex,
                playerBodyRenderingLayerCount
            );

            // Force Default + all player body rendering layers, then remove only this body's owner layer.
            renderingLayerMask = defaultRenderingLayerMask | allPlayerBodyMasks;
            renderingLayerMask &= ~ownerBodyMask;
        }

        ApplyRenderingLayerMaskToCarriedLight(renderingLayerMask);
    }

    private void CacheCarriedFlashlightAdditionalLightDataIfNeeded()
    {
        if (carriedFlashlightLight == null || carriedFlashlightAdditionalLightData != null)
            return;

        carriedFlashlightLight.TryGetComponent(out carriedFlashlightAdditionalLightData);

        if (carriedFlashlightAdditionalLightData == null)
            carriedFlashlightAdditionalLightData = carriedFlashlightLight.gameObject.AddComponent<UniversalAdditionalLightData>();
    }

    private void ApplyRenderingLayerMaskToCarriedLight(int renderingLayerMask)
    {
        if (carriedFlashlightLight == null)
            return;

        // Keep Unity's base Light value in sync for versions/tools that read it.
        carriedFlashlightLight.renderingLayerMask = renderingLayerMask;

        CacheCarriedFlashlightAdditionalLightDataIfNeeded();

        if (carriedFlashlightAdditionalLightData == null)
            return;

        uint mask = unchecked((uint)renderingLayerMask);

        // This is the value URP uses for the Light inspector's Rendering Layers field.
        carriedFlashlightAdditionalLightData.renderingLayers = mask;

        // Keep shadows using the same owner-excluding mask.
        carriedFlashlightAdditionalLightData.customShadowLayers = false;
        carriedFlashlightAdditionalLightData.shadowRenderingLayers = mask;
    }

    private void ApplyOwnerRenderingLayerMaskToRenderers()
    {
        if (!assignOwnerRenderingLayer)
            return;

        if (DownedPlayerClientId.Value == ulong.MaxValue)
            return;

        uint ownerRenderingLayerMask = PlayerBodyRenderingLayers.GetOwnerBodyMaskUInt(
            DownedPlayerClientId.Value,
            firstPlayerBodyRenderingLayerIndex,
            playerBodyRenderingLayerCount
        );

        if (ownerRenderingLayerMask == 0u)
            return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];

            if (renderer == null)
                continue;

            renderer.renderingLayerMask = ownerRenderingLayerMask;
        }
    }

    private void ApplyLocalOwnerRendererLayers()
    {
        if (!setLocalDownedOwnerRenderersToLocalPlayerLayer)
        {
            RestoreOriginalRendererLayers();
            return;
        }

        int localPlayerLayer = LayerMask.NameToLayer(localPlayerLayerName);

        if (localPlayerLayer < 0)
            return;

        bool isLocalDownedOwner = IsLocalDownedOwner();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];

            if (renderer == null)
                continue;

            if (!originalRendererLayersByRenderer.ContainsKey(renderer))
                originalRendererLayersByRenderer.Add(renderer, renderer.gameObject.layer);

            if (isLocalDownedOwner)
            {
                renderer.gameObject.layer = localPlayerLayer;
            }
            else if (originalRendererLayersByRenderer.TryGetValue(renderer, out int originalLayer))
            {
                renderer.gameObject.layer = originalLayer;
            }
        }
    }

    private void RestoreOriginalRendererLayers()
    {
        foreach (KeyValuePair<Renderer, int> entry in originalRendererLayersByRenderer)
        {
            if (entry.Key == null)
                continue;

            entry.Key.gameObject.layer = entry.Value;
        }
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
        bool isBadGuyBody,
        bool carriedFlashlightOn
    )
    {
        if (!IsServer)
            return;

        DownedPlayerClientId.Value = downedPlayerClientId;
        SourcePlayerNetworkObjectId.Value = sourcePlayerNetworkObjectId;
        BodyState.Value = bodyState;
        ShirtIndex.Value = shirtIndex;
        IsBadGuyBody.Value = isBadGuyBody;
        CarriedFlashlightOn.Value = carriedFlashlightOn;
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

    public void ServerSetCarriedFlashlightOn(bool isOn)
    {
        if (!IsServer)
            return;

        CarriedFlashlightOn.Value = isOn;
    }

    private void HandleCarriedFlashlightChanged(bool previousValue, bool newValue)
    {
        ApplyCarriedFlashlightVisuals();
    }

    private void ApplyCarriedFlashlightVisuals()
    {
        bool isOn = CarriedFlashlightOn.Value;

        ApplyCarriedFlashlightLightCullingMask();
        ApplyCarriedFlashlightLightRenderingLayerMask();

        if (carriedFlashlightVisualRoot != null)
            carriedFlashlightVisualRoot.SetActive(true);

        if (carriedFlashlightLight != null)
            carriedFlashlightLight.enabled = isOn;

        if (carriedFlashlightLensFlare != null)
            carriedFlashlightLensFlare.enabled = ShouldShowCarriedLensFlare(isOn);

        ApplyCarriedLightBulbsMaterial(isOn);
    }

    private bool ShouldShowCarriedLensFlare(bool isOn)
    {
        if (!isOn)
            return false;

        if (!hideCarriedLensFlareForDownedOwner)
            return true;

        return !IsLocalDownedOwner();
    }

    private void ApplyCarriedLightBulbsMaterial(bool isOn)
    {
        if (carriedLightBulbsRenderer == null)
            return;

        if (carriedLightBulbsMaterialIndex < 0)
            return;

        Material materialToUse = isOn ? carriedLightBulbsOnMaterial : carriedLightBulbsOffMaterial;

        if (materialToUse == null)
            return;

        Material[] materials = carriedLightBulbsRenderer.sharedMaterials;

        if (carriedLightBulbsMaterialIndex >= materials.Length)
            return;

        if (materials[carriedLightBulbsMaterialIndex] == materialToUse)
            return;

        materials[carriedLightBulbsMaterialIndex] = materialToUse;
        carriedLightBulbsRenderer.sharedMaterials = materials;
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
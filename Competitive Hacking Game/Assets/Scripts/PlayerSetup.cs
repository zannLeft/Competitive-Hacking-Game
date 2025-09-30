using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.Rendering;

public class PlayerSetup : NetworkBehaviour
{
    [Header("Renderers (assign in inspector)")]
    public SkinnedMeshRenderer headRenderer; // head mesh (3 materials; index 2 is shirt)
    public SkinnedMeshRenderer bodyRenderer; // body mesh (10 materials; indices 1,2,6,7 are shirt)

    // networked shirt index. Server is authoritative for assignment.
    public NetworkVariable<int> ShirtIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // later we'll add IsBadGuy; keep it ready for next steps.
    public NetworkVariable<bool> IsBadGuy = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Start()
    {
        // keep compatibility with your old Start that set shadow mode
        if (headRenderer != null)
        {
            if (IsOwner)
                headRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            else
                headRenderer.shadowCastingMode = ShadowCastingMode.On;
        }

        // layers logic from your original Start
        if (IsOwner)
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("MyPlayer"));
        else
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("OtherPlayers"));
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only the server assigns shirt indices when player object spawns on the server
        if (IsServer)
        {
            // Ask LobbyManager to assign next available index
            if (LobbyManager.Instance != null)
            {
                int idx = LobbyManager.Instance.AssignColorIndex();
                ShirtIndex.Value = idx;
                Debug.Log($"[Server] Assigned shirt index {idx} to client {OwnerClientId}");
            }
            else
            {
                Debug.LogWarning("[PlayerSetup] LobbyManager.Instance is null on server when assigning shirt index");
            }
        }

        // Always apply local materials when the network variable changes
        ShirtIndex.OnValueChanged += (oldVal, newVal) => ApplyShirtMaterial();
        IsBadGuy.OnValueChanged += (oldVal, newVal) => ApplyShirtMaterial();

        // If the variables already have values (e.g. after scene change), apply them now
        ApplyShirtMaterial();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        // release the index back to LobbyManager so it can be reused
        if (IsServer && LobbyManager.Instance != null)
        {
            LobbyManager.Instance.ReleaseColorIndex(ShirtIndex.Value);
        }
    }

    private void ApplyShirtMaterial()
    {
        // Defensive checks
        if (headRenderer == null || bodyRenderer == null) return;
        if (LobbyManager.Instance == null) return;

        Material shirtMaterial = LobbyManager.Instance.GetShirtMaterial(ShirtIndex.Value);

        // If this player is the bad guy, use the black material (will be set in LobbyManager inspector).
        if (IsBadGuy.Value && LobbyManager.Instance.blackShirtMaterial != null)
        {
            shirtMaterial = LobbyManager.Instance.blackShirtMaterial;
        }

        // Apply to head mesh: element 2
        var headMats = headRenderer.materials;
        if (headMats.Length > 2)
        {
            headMats[2] = shirtMaterial;
            headRenderer.materials = headMats;
        }
        else
        {
            Debug.LogWarning($"[PlayerSetup] headRenderer does not have element 2. Count: {headMats.Length}");
        }

        // Apply to body mesh: elements 1, 2, 6, 7
        var bodyMats = bodyRenderer.materials;
        int[] bodyIndices = new int[] { 1, 2, 6, 7 };
        foreach (int i in bodyIndices)
        {
            if (bodyMats.Length > i)
                bodyMats[i] = shirtMaterial;
            else
                Debug.LogWarning($"[PlayerSetup] bodyRenderer missing material index {i}. Count: {bodyMats.Length}");
        }
        bodyRenderer.materials = bodyMats;
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, newLayer);
    }
}

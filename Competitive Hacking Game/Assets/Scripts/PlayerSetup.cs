using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerSetup : NetworkBehaviour
{
    [Header("Renderers (assign in inspector)")]
    public SkinnedMeshRenderer headRenderer;
    public SkinnedMeshRenderer bodyRenderer;

    public NetworkVariable<int> ShirtIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsBadGuy = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Start()
    {
        if (headRenderer != null)
        {
            headRenderer.shadowCastingMode = IsOwner
                ? ShadowCastingMode.ShadowsOnly
                : ShadowCastingMode.On;
        }

        if (IsOwner)
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("MyPlayer"));
        else
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("OtherPlayers"));
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            var cosmetics = LobbyManager.Instance != null ? LobbyManager.Instance.Cosmetics : null;
            if (cosmetics != null)
            {
                ShirtIndex.Value = cosmetics.AssignColorIndex();
                Debug.Log(
                    $"[Server] Assigned shirt index {ShirtIndex.Value} to client {OwnerClientId}"
                );
            }
            else
            {
                Debug.LogWarning(
                    "[PlayerSetup] CosmeticsManager not found when assigning shirt index"
                );
            }
        }

        ShirtIndex.OnValueChanged += (_, __) => ApplyShirtMaterial();
        IsBadGuy.OnValueChanged += (_, __) => ApplyShirtMaterial();

        ApplyShirtMaterial();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && LobbyManager.Instance != null && LobbyManager.Instance.Cosmetics != null)
            LobbyManager.Instance.Cosmetics.ReleaseColorIndex(ShirtIndex.Value);
    }

    private void ApplyShirtMaterial()
    {
        if (headRenderer == null || bodyRenderer == null)
            return;
        if (LobbyManager.Instance == null)
            return;

        var cosmetics = LobbyManager.Instance.Cosmetics;
        if (cosmetics == null)
            return;

        Material shirtMaterial = cosmetics.GetShirtMaterial(ShirtIndex.Value);

        if (IsBadGuy.Value && cosmetics.BlackShirtMaterial != null)
            shirtMaterial = cosmetics.BlackShirtMaterial;

        var headMats = headRenderer.materials;
        if (headMats.Length > 2)
        {
            headMats[2] = shirtMaterial;
            headRenderer.materials = headMats;
        }

        var bodyMats = bodyRenderer.materials;
        int[] indices = { 1, 2, 6, 7 };
        foreach (var i in indices)
            if (bodyMats.Length > i)
                bodyMats[i] = shirtMaterial;

        bodyRenderer.materials = bodyMats;
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, newLayer);
    }
}

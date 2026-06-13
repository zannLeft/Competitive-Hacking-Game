using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerSetup : NetworkBehaviour
{
    [Header("Renderers (assign in inspector)")]
    public SkinnedMeshRenderer headRenderer;
    public SkinnedMeshRenderer bodyRenderer;

    [Header("Layer Preservation")]
    [SerializeField]
    private string laptopUiLayerName = "LaptopUI";

    [Header("URP Rendering Layers")]
    [Tooltip("Assigns every renderer under this player, including phone/flashlight visuals, to the owner's unique PlayerBody rendering layer.")]
    [SerializeField]
    private bool assignOwnerRenderingLayer = true;

    [Tooltip("Rendering Layer index for PlayerBody0. If you kept Default at index 0 and made PlayerBody0 index 1, set this to 1.")]
    [SerializeField]
    private int firstPlayerBodyRenderingLayerIndex = 0;

    [Tooltip("How many PlayerBody rendering layers exist. You said you made PlayerBody0-4, so this should be 5.")]
    [SerializeField]
    private int playerBodyRenderingLayerCount = 5;

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

    private int _laptopUiLayer = -1;

    private void Start()
    {
        _laptopUiLayer = LayerMask.NameToLayer(laptopUiLayerName);

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

        ApplyOwnerRenderingLayerMaskToRenderers();
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
        ApplyOwnerRenderingLayerMaskToRenderers();
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

    public uint GetOwnerRenderingLayerMask()
    {
        return PlayerBodyRenderingLayers.GetOwnerBodyMaskUInt(
            OwnerClientId,
            firstPlayerBodyRenderingLayerIndex,
            playerBodyRenderingLayerCount
        );
    }

    public void ApplyOwnerRenderingLayerMaskToRenderers()
    {
        if (!assignOwnerRenderingLayer)
            return;

        uint ownerRenderingLayerMask = GetOwnerRenderingLayerMask();

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

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null)
            return;

        // Preserve laptop RenderTexture UI. Otherwise the player's MyPlayer/OtherPlayers
        // layer assignment prevents LaptopUI_Cam from rendering the Canvas.
        if (_laptopUiLayer >= 0 && obj.layer == _laptopUiLayer)
            return;

        obj.layer = newLayer;

        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, newLayer);
    }
}
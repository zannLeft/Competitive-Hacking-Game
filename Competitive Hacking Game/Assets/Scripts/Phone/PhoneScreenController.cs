using UnityEngine;
using Unity.Netcode;

/// Controls the phone “screen” rendering.
/// Locals render a Canvas -> Camera -> RenderTexture pipeline into a per-instance screen material.
/// Remotes see a solid-color material (no UI camera cost).
/// Put this on the PHONE PREFAB ROOT and wire references in the inspector.
public class PhoneScreenController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera uiCam;            // PhoneUI_Cam
    [SerializeField] private Canvas uiCanvas;         // PhoneUI (Screen Space - Camera)
    [SerializeField] private MeshRenderer screenMR;   // Renderer that contains the screen sub-material
    [SerializeField] private int screenMaterialIndex = 1;

    [Header("Materials")]
    [SerializeField] private Material remoteSolidMat; // What remotes (and local when screen off) see (Unlit solid color)

    [Header("RenderTexture (local only)")]
    [SerializeField] private int rtWidth  = 1024;
    [SerializeField] private int rtHeight = 2048;
    [SerializeField] private bool useMipmaps = false;

    // Runtime
    private RenderTexture _rt;
    private Material _runtimeMat;   // per-instance copy so we never touch shared assets
    private PlayerPhone _ownerPhone;
    private bool _isOwner;

    void Awake()
    {
        if (uiCam)    uiCam.enabled = false;
        if (uiCanvas) uiCanvas.enabled = false;

        _ownerPhone = GetComponentInParent<PlayerPhone>();
        _isOwner = (_ownerPhone && _ownerPhone.IsOwner);

        ConfigureOnce();
    }

    private void ConfigureOnce()
    {
        if (!screenMR) return;

        // Defend against bad indices
        int idx = Mathf.Clamp(screenMaterialIndex, 0, screenMR.sharedMaterials.Length - 1);

        if (_isOwner)
        {
            // 1) Make a per-instance material for the screen sub-material
            var mats = screenMR.materials; // returns instanced array
            _runtimeMat = new Material(mats[idx]);
            mats[idx] = _runtimeMat;
            screenMR.materials = mats;

            // 2) Create a private RT and wire it up
            _rt = new RenderTexture(rtWidth, rtHeight, 0, RenderTextureFormat.ARGB32)
            {
                useMipMap = useMipmaps,
                autoGenerateMips = useMipmaps,
                antiAliasing = 1,
                anisoLevel = 4,
                name = _ownerPhone ? $"PhoneHUD_{_ownerPhone.OwnerClientId}" : "PhoneHUD"
            };

            if (uiCam) uiCam.targetTexture = _rt;

            // Assign to the material. Prefer BaseMap; also set EmissionMap if present.
            if (_runtimeMat.HasProperty("_BaseMap"))
                _runtimeMat.SetTexture("_BaseMap", _rt);

            if (_runtimeMat.HasProperty("_EmissionMap"))
            {
                _runtimeMat.SetTexture("_EmissionMap", _rt);
                _runtimeMat.EnableKeyword("_EMISSION");
            }
        }
        else
        {
            // Remotes: never render the UI camera; swap to a simple solid color material
            if (uiCam)    uiCam.enabled = false;
            if (uiCanvas) uiCanvas.enabled = false;

            if (remoteSolidMat)
            {
                var mats = screenMR.materials;
                mats[idx] = remoteSolidMat;
                screenMR.materials = mats;
            }
            else
            {
                // Fallback: push a black emission via MPB so we keep the shared material intact
                var mpb = new MaterialPropertyBlock();
                screenMR.GetPropertyBlock(mpb, idx);
                int emissionID = Shader.PropertyToID("_EmissionColor");
                mpb.SetColor(emissionID, Color.black);
                screenMR.SetPropertyBlock(mpb, idx);
            }
        }
    }

    /// <summary>
    /// Owner-only: turn the screen HUD on/off.
    /// ON: enable camera+canvas and show RT material.
    /// OFF: disable camera+canvas and show the solid “off” material.
    /// </summary>
    public void SetScreenOn(bool on)
    {
        if (!_isOwner) return;

        if (uiCam)    uiCam.enabled = on;
        if (uiCanvas) uiCanvas.enabled = on;

        if (!screenMR) return;

        int idx = Mathf.Clamp(screenMaterialIndex, 0, screenMR.materials.Length - 1);
        var mats = screenMR.materials;

        if (on && _runtimeMat != null)
        {
            mats[idx] = _runtimeMat;
        }
        else if (!on && remoteSolidMat != null)
        {
            mats[idx] = remoteSolidMat;
        }

        screenMR.materials = mats;
    }

    /// <summary>
    /// Owner-only convenience; does not swap materials (use SetScreenOn for that).
    /// </summary>
    public void SetRenderingActive(bool active)
    {
        if (!_isOwner) return;
        if (uiCam)    uiCam.enabled = active;
        if (uiCanvas) uiCanvas.enabled = active;
    }

    void OnDestroy()
    {
        if (_rt)
        {
            if (uiCam) uiCam.targetTexture = null;
            _rt.Release();
            Destroy(_rt);
        }
        if (_runtimeMat) Destroy(_runtimeMat);
    }
}

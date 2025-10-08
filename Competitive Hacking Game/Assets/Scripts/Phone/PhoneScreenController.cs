using UnityEngine;
using Unity.Netcode;

/// Controls the phone “screen”: local renders Canvas->RT; remotes show solid color.
/// Put this on the PHONE PREFAB ROOT. Assign refs in the inspector.
/// Screen mesh must use material element index = screenMaterialIndex (default 1).
public class PhoneScreenController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera uiCam;            // PhoneUI_Cam
    [SerializeField] private Canvas uiCanvas;         // PhoneUI (Screen Space - Camera)
    [SerializeField] private MeshRenderer screenMR;   // Mesh renderer that has the screen sub-material
    [SerializeField] private int screenMaterialIndex = 1;

    [Header("Materials")]
    [SerializeField] private Material remoteSolidMat; // What remotes see (Unlit solid color)

    [Header("RenderTexture (local only)")]
    [SerializeField] private int rtWidth  = 1024;
    [SerializeField] private int rtHeight = 2048;
    [SerializeField] private bool useMipmaps = false;

    // Runtime
    RenderTexture _rt;
    Material _runtimeMat;   // per-instance copy so we never touch shared assets
    PlayerPhone _ownerPhone;
    bool _isOwner;

    void Awake()
    {
        _ownerPhone = GetComponentInParent<PlayerPhone>();
        _isOwner = (_ownerPhone && _ownerPhone.IsOwner);

        // Default to disabled; PlayerPhone will toggle when phone is shown/hidden
        if (uiCam) uiCam.enabled = false;
        if (uiCanvas) uiCanvas.enabled = false;

        ConfigureOnce();
    }

    void ConfigureOnce()
    {
        if (!screenMR) return;

        if (_isOwner)
        {
            // 1) Make a per-instance material for the screen sub-material
            var mats = screenMR.materials;                     // creates array copy with instanced materials
            _runtimeMat = new Material(mats[screenMaterialIndex]);
            mats[screenMaterialIndex] = _runtimeMat;
            screenMR.materials = mats;

            // 2) Create a private RT and wire it up
            _rt = new RenderTexture(rtWidth, rtHeight, 0, RenderTextureFormat.ARGB32)
            {
                useMipMap = useMipmaps,
                autoGenerateMips = useMipmaps,
                antiAliasing = 1,
                anisoLevel = 4,
                name = $"PhoneHUD_{_ownerPhone.OwnerClientId}"
            };

            if (uiCam) uiCam.targetTexture = _rt;

            // Assign to the material. For Unlit: usually _BaseMap. For Standard emission: _EmissionMap + enable keyword.
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
            if (uiCam) uiCam.enabled = false;
            if (uiCanvas) uiCanvas.enabled = false;

            if (remoteSolidMat)
            {
                var mats = screenMR.materials;
                mats[screenMaterialIndex] = remoteSolidMat;
                screenMR.materials = mats;
            }
            else
            {
                // Fallback: push a black emission via MPB so we keep the shared material intact
                var mpb = new MaterialPropertyBlock();
                screenMR.GetPropertyBlock(mpb, screenMaterialIndex);
                if (Shader.PropertyToID("_EmissionColor") != 0)
                    mpb.SetColor("_EmissionColor", Color.black);
                screenMR.SetPropertyBlock(mpb, screenMaterialIndex);
            }
        }
    }

    /// Called by PlayerPhone when the phone shows/hides.
    public void SetRenderingActive(bool active)
    {
        if (!_isOwner) return;
        if (uiCam) uiCam.enabled = active;
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

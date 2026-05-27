using Unity.Netcode;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[DisallowMultipleComponent]
public class LaptopScreenController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Camera uiCam;

    [SerializeField]
    private Canvas uiCanvas;

    [SerializeField]
    private Renderer screenRenderer;

    [Tooltip("Your Screen mesh material slot. In your screenshot, Element 2 is the actual screen.")]
    [SerializeField]
    private int screenMaterialIndex = 2;

    [Header("RenderTexture")]
    [Tooltip("MacBook-like 16:10. 1920x1200 is a good starting point.")]
    [SerializeField]
    private int rtWidth = 1920;

    [SerializeField]
    private int rtHeight = 1200;

    [Tooltip("For testing only. OFF is better for performance later.")]
    [SerializeField]
    private bool renderUiForRemotes = false;

    [Header("Screen Look")]
    [SerializeField]
    private Color offColor = Color.black;

    [SerializeField]
    private Color remoteOnBaseColor = new Color(0.02f, 0.35f, 0.04f, 1f);

    [SerializeField]
    private Color remoteEmissionColor = Color.green;

    [SerializeField]
    private float remoteEmissionIntensity = 2.5f;

    [SerializeField]
    private float ownerEmissionIntensity = 1.75f;

    private RenderTexture _rt;
    private Material _screenMat;

    private bool _configured;
    private bool _configuredAsOwner;
    private bool _screenOn;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (uiCam != null)
            uiCam.enabled = false;

        if (uiCanvas != null)
            uiCanvas.enabled = false;
    }

    public void ConfigureForOwner(bool isOwner)
    {
        _configuredAsOwner = isOwner;
        ConfigureOnce();
        ApplyScreenState();
    }

    public void SetScreenOn(bool on)
    {
        _screenOn = on;
        ConfigureOnce();
        ApplyScreenState();
    }

    private void ConfigureOnce()
    {
        if (_configured)
            return;

        _configured = true;

        if (screenRenderer == null)
            return;

        var mats = screenRenderer.materials;
        if (mats == null || mats.Length == 0)
            return;

        int idx = Mathf.Clamp(screenMaterialIndex, 0, mats.Length - 1);

        _screenMat = new Material(mats[idx]);
        mats[idx] = _screenMat;
        screenRenderer.materials = mats;

        bool shouldRenderUi = _configuredAsOwner || renderUiForRemotes;

        if (shouldRenderUi)
            CreateRenderTexture();
    }

    private void CreateRenderTexture()
    {
        if (_rt != null)
            return;

        var colorFormat =
            QualitySettings.activeColorSpace == ColorSpace.Linear
                ? GraphicsFormat.R8G8B8A8_SRGB
                : GraphicsFormat.R8G8B8A8_UNorm;

        var desc = new RenderTextureDescriptor(rtWidth, rtHeight)
        {
            graphicsFormat = colorFormat,
            depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt,
            msaaSamples = 1,
            mipCount = 1,
            useMipMap = false,
            autoGenerateMips = false,
            sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
        };

        _rt = new RenderTexture(desc)
        {
            name = "LaptopScreenRT",
            anisoLevel = 1,
        };

        _rt.Create();

        if (uiCam != null)
            uiCam.targetTexture = _rt;

        AssignTextureToMaterial(_rt);
    }

    private void ApplyScreenState()
    {
        bool shouldRenderUi = _configuredAsOwner || renderUiForRemotes;

        if (uiCam != null)
            uiCam.enabled = _screenOn && shouldRenderUi;

        if (uiCanvas != null)
            uiCanvas.enabled = _screenOn && shouldRenderUi;

        if (_screenMat == null)
            return;

        if (!_screenOn)
        {
            SetMaterialColor(offColor);
            SetEmission(Color.black);
            return;
        }

        if (shouldRenderUi)
        {
            CreateRenderTexture();
            AssignTextureToMaterial(_rt);

            // White keeps the UI colors correct. The UI itself should be green/dark.
            SetMaterialColor(Color.white);
            SetEmission(Color.white * Mathf.Max(0f, ownerEmissionIntensity));
        }
        else
        {
            // Remote players see a cheap green emissive screen, not the full UI camera.
            SetMaterialColor(remoteOnBaseColor);
            SetEmission(remoteEmissionColor * Mathf.Max(0f, remoteEmissionIntensity));
        }
    }

    private void AssignTextureToMaterial(Texture texture)
    {
        if (_screenMat == null || texture == null)
            return;

        if (_screenMat.HasProperty(BaseMapId))
            _screenMat.SetTexture(BaseMapId, texture);

        if (_screenMat.HasProperty(MainTexId))
            _screenMat.SetTexture(MainTexId, texture);

        if (_screenMat.HasProperty(EmissionMapId))
        {
            _screenMat.SetTexture(EmissionMapId, texture);
            _screenMat.EnableKeyword("_EMISSION");
        }
    }

    private void SetMaterialColor(Color color)
    {
        if (_screenMat == null)
            return;

        if (_screenMat.HasProperty(BaseColorId))
            _screenMat.SetColor(BaseColorId, color);

        if (_screenMat.HasProperty(ColorId))
            _screenMat.SetColor(ColorId, color);
    }

    private void SetEmission(Color color)
    {
        if (_screenMat == null)
            return;

        if (_screenMat.HasProperty(EmissionColorId))
        {
            _screenMat.EnableKeyword("_EMISSION");
            _screenMat.SetColor(EmissionColorId, color);
        }
    }

    private void OnDestroy()
    {
        if (uiCam != null)
            uiCam.targetTexture = null;

        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
        }

        if (_screenMat != null)
            Destroy(_screenMat);
    }
}
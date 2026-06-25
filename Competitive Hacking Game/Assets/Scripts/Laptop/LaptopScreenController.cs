using System.Collections;
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

    [Header("Screen Light")]
    [Tooltip("Optional light attached to the laptop screen. It is enabled and disabled with the screen.")]
    [SerializeField]
    private Light laptopLight;

    [Tooltip("If no light is assigned, automatically look for a child light named LaptopLight.")]
    [SerializeField]
    private bool autoFindLaptopLight = true;

    [Header("Error Light Feedback")]
    [Tooltip("Temporarily applied whenever the laptop minigame alarm/error sound plays.")]
    [SerializeField]
    private Color errorLightColor = new Color32(255, 102, 115, 255);

    [Tooltip("How long the laptop light remains red after an error alarm.")]
    [SerializeField, Min(0.05f)]
    private float errorLightDuration = 0.65f;

    [Tooltip("Automatically listen to the owning PlayerLaptopHacker's alarm event.")]
    [SerializeField]
    private bool syncWithMinigameAlarm = true;

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

    private PlayerLaptopHacker _playerLaptopHacker;
    private Coroutine _errorLightRoutine;
    private Color _normalLaptopLightColor = new Color32(48, 196, 168, 255);
    private bool _hasCachedNormalLightColor;
    private bool _errorLightActive;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        ResolveLaptopLight();
        CacheNormalLaptopLightColor();

        if (uiCam != null)
            uiCam.enabled = false;

        if (uiCanvas != null)
            uiCanvas.enabled = false;

        if (laptopLight != null)
            laptopLight.enabled = false;
    }

    private void OnEnable()
    {
        SubscribeToLaptopAlarm();
    }

    private void OnDisable()
    {
        UnsubscribeFromLaptopAlarm();
        StopErrorLightPulse(restoreNormalColor: true);
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

        ResolveLaptopLight();
        CacheNormalLaptopLightColor();
        if (laptopLight != null)
        {
            laptopLight.enabled = _screenOn;
            laptopLight.color = _errorLightActive
                ? errorLightColor
                : _normalLaptopLightColor;
        }

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

    /// <summary>
    /// Flashes the laptop screen light with the configured error color, then restores
    /// the light's original inspector color. Repeated alarms restart the timer.
    /// </summary>
    public void FlashErrorLight()
    {
        ResolveLaptopLight();
        CacheNormalLaptopLightColor();

        if (laptopLight == null || !_screenOn)
            return;

        if (_errorLightRoutine != null)
            StopCoroutine(_errorLightRoutine);

        _errorLightRoutine = StartCoroutine(ErrorLightPulseRoutine());
    }

    private IEnumerator ErrorLightPulseRoutine()
    {
        _errorLightActive = true;

        if (laptopLight != null)
        {
            laptopLight.color = errorLightColor;
            laptopLight.enabled = _screenOn;
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, errorLightDuration));

        _errorLightRoutine = null;
        _errorLightActive = false;

        if (laptopLight != null)
        {
            laptopLight.color = _normalLaptopLightColor;
            laptopLight.enabled = _screenOn;
        }
    }

    private void StopErrorLightPulse(bool restoreNormalColor)
    {
        if (_errorLightRoutine != null)
        {
            StopCoroutine(_errorLightRoutine);
            _errorLightRoutine = null;
        }

        _errorLightActive = false;

        if (restoreNormalColor && laptopLight != null)
            laptopLight.color = _normalLaptopLightColor;
    }

    private void CacheNormalLaptopLightColor()
    {
        if (_hasCachedNormalLightColor || laptopLight == null)
            return;

        _normalLaptopLightColor = laptopLight.color;
        _hasCachedNormalLightColor = true;
    }

    private void SubscribeToLaptopAlarm()
    {
        if (!syncWithMinigameAlarm || _playerLaptopHacker != null)
            return;

        _playerLaptopHacker = GetComponentInParent<PlayerLaptopHacker>(true);
        if (_playerLaptopHacker != null)
            _playerLaptopHacker.HackAlarmEmitted += OnHackAlarmEmitted;
    }

    private void UnsubscribeFromLaptopAlarm()
    {
        if (_playerLaptopHacker == null)
            return;

        _playerLaptopHacker.HackAlarmEmitted -= OnHackAlarmEmitted;
        _playerLaptopHacker = null;
    }

    private void OnHackAlarmEmitted()
    {
        FlashErrorLight();
    }

    private void ResolveLaptopLight()
    {
        if (laptopLight != null || !autoFindLaptopLight)
            return;

        Light[] lights = GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light candidate = lights[i];
            if (candidate != null && candidate.name == "LaptopLight")
            {
                laptopLight = candidate;
                CacheNormalLaptopLightColor();
                return;
            }
        }

        // Safe fallback when the laptop contains only one light.
        if (lights.Length == 1)
        {
            laptopLight = lights[0];
            CacheNormalLaptopLightColor();
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
        UnsubscribeFromLaptopAlarm();
        StopErrorLightPulse(restoreNormalColor: true);

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
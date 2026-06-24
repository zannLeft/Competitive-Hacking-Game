using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class FirewallRunnerMinigame : LaptopMinigameBase
{
    [Serializable]
    private sealed class CourseSettings
    {
        [Min(5f)]
        public float courseSeconds = 14f;

        [Min(100f)]
        public float scrollSpeed = 390f;

        [Min(0.5f)]
        public float firstObstacleSeconds = 2.5f;

        [Min(0.5f)]
        public float minGapSeconds = 1.65f;

        [Min(0.5f)]
        public float maxGapSeconds = 2.30f;

        [Range(0f, 1f)]
        public float doubleStackChance = 0.26f;

        [Range(0f, 1f)]
        public float tripleStackChance = 0.08f;
    }

    private sealed class ObstacleRuntime
    {
        public float HitTime;
        public float Width;
        public float Height;
        public RectTransform Rect;
    }

    private enum RunState
    {
        Playing,
        FailureHold,
    }

    private struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = seed == 0 ? 0x6D2B79F5u : unchecked((uint)seed);
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float Next01()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }

        public float Range(float min, float max)
        {
            return Mathf.Lerp(min, max, Next01());
        }
    }

    [Header("Difficulty")]
    [SerializeField]
    private CourseSettings easy = new()
    {
        courseSeconds = 14f,
        scrollSpeed = 435f,
        firstObstacleSeconds = 2.2f,
        minGapSeconds = 1.45f,
        maxGapSeconds = 2.00f,
        doubleStackChance = 0.26f,
        tripleStackChance = 0.08f,
    };

    [SerializeField]
    private CourseSettings hard = new()
    {
        courseSeconds = 18f,
        scrollSpeed = 555f,
        firstObstacleSeconds = 1.95f,
        minGapSeconds = 1.08f,
        maxGapSeconds = 1.55f,
        doubleStackChance = 0.34f,
        tripleStackChance = 0.18f,
    };

    [Header("Packet Movement")]
    [SerializeField, Min(20f)]
    private float playerWidth = 86f;

    [SerializeField, Min(20f)]
    private float playerHeight = 86f;

    [SerializeField, Range(0.05f, 0.45f)]
    private float playerHorizontalPosition = 0.18f;

    [SerializeField, Min(0f)]
    private float groundPadding = 86f;

    [SerializeField, Min(100f)]
    private float jumpVelocity = 1225f;

    [SerializeField, Min(100f)]
    private float gravity = 2200f;

    [SerializeField, Range(0.1f, 1f)]
    private float releasedJumpVelocityMultiplier = 1f;

    [SerializeField, Min(0f)]
    private float jumpBufferSeconds = 0.14f;

    [SerializeField, Min(0f)]
    private float coyoteSeconds = 0.10f;

    [SerializeField, Min(0f)]
    private float collisionInset = 11f;

    [Header("Obstacles")]
    [SerializeField, Min(20f)]
    private float obstacleBlockSize = 82f;

    [SerializeField, Min(0f)]
    private float obstacleStackGap = 6f;

    [Header("Result Timing")]
    [SerializeField, Min(0f)]
    private float failureHoldSeconds = 0.90f;

    [Header("ASCII Terminal Visuals")]
    [Tooltip("Optional monospace TMP font. Falls back to the TMP default font when left empty.")]
    [SerializeField]
    private TMP_FontAsset terminalFont;

    [SerializeField]
    private Color backgroundColor = new(0.018f, 0.032f, 0.065f, 1f);

    [SerializeField]
    private Color accentColor = new(0.43f, 0.84f, 1f, 1f);

    [SerializeField]
    private Color textColor = new(0.91f, 0.97f, 1f, 1f);

    [SerializeField]
    private Color mutedTextColor = new(0.47f, 0.63f, 0.72f, 1f);

    [SerializeField]
    private Color obstacleColor = new(1f, 0.24f, 0.38f, 1f);

    [SerializeField]
    private string packetGlyph = "[>]";

    [SerializeField]
    private string obstacleGlyph = "[#]";

    [Header("Screen Scale")]
    [Tooltip("Scales the ASCII runner vertically and enlarges its glyphs without changing obstacle timing.")]
    [SerializeField, Range(1f, 1.8f)]
    private float gameplayVisualScale = 1.45f;

    [Header("Jump Presentation")]
    [SerializeField, Range(0f, 45f)]
    private float jumpTiltDegrees = 22f;

    [SerializeField, Min(0f)]
    private float jumpTiltSmoothing = 18f;

    [Header("Terminal Glow")]
    [SerializeField]
    private bool enableTextGlow = true;

    [SerializeField, Range(0f, 1f)]
    private float textGlowAlpha = 0.58f;

    [SerializeField, Range(0f, 1f)]
    private float textGlowOuter = 0.18f;

    [SerializeField, Range(0f, 1f)]
    private float textGlowPower = 0.82f;

    [Header("Separators")]
    [SerializeField, Range(2f, 20f)]
    private float separatorThickness = 8f;

    private readonly List<ObstacleRuntime> _obstacles = new();
    private readonly List<Material> _runtimeFontMaterials = new();

    private RectTransform _root;
    private RectTransform _gameArea;
    private RectTransform _obstacleLayer;
    private RectTransform _packetRect;
    private RectTransform _resultOverlayRect;
    private TMP_Text _packetText;
    private Image _resultOverlayImage;
    private TMP_Text _networkText;
    private TMP_Text _difficultyText;
    private TMP_Text _statusText;
    private TMP_Text _resultText;

    private LaptopMinigameContext _context;
    private CourseSettings _settings;
    private RunState _state;
    private float _elapsed;
    private float _verticalPosition;
    private float _verticalVelocity;
    private float _jumpBufferRemaining;
    private float _coyoteRemaining;
    private float _resultRemaining;
    private bool _grounded;
    private int _lastShownPercent = -1;

    private const float MaxFrameSimulationSeconds = 0.10f;
    private const float SimulationStepSeconds = 1f / 120f;
    private const int ProgressCharacterCount = 32;

    protected override void OnBegin(LaptopMinigameContext context)
    {
        EnsureDifficultySettingsExist();
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;

        SanitizeSettings(_settings);
        GenerateCourse(context.Seed);
        ResetRun(context);
    }

    protected override void OnPrimaryPressed()
    {
        if (_state != RunState.Playing)
            return;

        // This is the accepted player action for Firewall Runner.
        // PlayerLaptopHacker plays it immediately for the owner and relays it
        // to nearby players as a positional keyboard keypress.
        TriggerActionPerformed();
        _jumpBufferRemaining = Mathf.Max(_jumpBufferRemaining, jumpBufferSeconds);
    }

    protected override void OnPrimaryReleased()
    {
        if (_state != RunState.Playing)
            return;

        if (_verticalVelocity > 0f)
            _verticalVelocity *= releasedJumpVelocityMultiplier;
    }

    protected override void OnAbort()
    {
        _jumpBufferRemaining = 0f;
        _verticalVelocity = 0f;
    }

    private void OnDisable()
    {
        if (IsRunning)
            Abort();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _runtimeFontMaterials.Count; i++)
        {
            Material material = _runtimeFontMaterials[i];
            if (material != null)
                Destroy(material);
        }

        _runtimeFontMaterials.Clear();
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        float frameDelta = Mathf.Min(Time.deltaTime, MaxFrameSimulationSeconds);

        if (_state == RunState.Playing)
        {
            float remaining = frameDelta;

            while (remaining > 0f && _state == RunState.Playing)
            {
                float step = Mathf.Min(remaining, SimulationStepSeconds);
                SimulatePlayingStep(step);
                remaining -= step;
            }

            UpdatePlayingVisuals();
            return;
        }

        _resultRemaining -= frameDelta;

        if (_resultRemaining > 0f)
            return;

        ResetRun(_context);
    }

    private void SimulatePlayingStep(float deltaTime)
    {
        _elapsed += deltaTime;
        _jumpBufferRemaining = Mathf.Max(0f, _jumpBufferRemaining - deltaTime);

        if (_grounded)
            _coyoteRemaining = coyoteSeconds;
        else
            _coyoteRemaining = Mathf.Max(0f, _coyoteRemaining - deltaTime);

        if (_jumpBufferRemaining > 0f && (_grounded || _coyoteRemaining > 0f))
        {
            _jumpBufferRemaining = 0f;
            _coyoteRemaining = 0f;
            _grounded = false;
            _verticalVelocity = jumpVelocity;
        }

        if (!_grounded)
        {
            _verticalVelocity -= gravity * deltaTime;
            _verticalPosition += _verticalVelocity * deltaTime;

            if (_verticalPosition <= 0f)
            {
                _verticalPosition = 0f;
                _verticalVelocity = 0f;
                _grounded = true;
            }
        }

        if (CheckObstacleCollision())
        {
            BeginFailureHold();
            return;
        }

        if (_elapsed >= _settings.courseSeconds)
            CompleteMinigame();
    }

    private bool CheckObstacleCollision()
    {
        if (_gameArea == null)
            return false;

        float areaWidth = _gameArea.rect.width;
        float groundY = GetGroundY();
        float playerX = GetPlayerX();
        float inset = Mathf.Clamp(
            collisionInset,
            0f,
            Mathf.Min(playerWidth, playerHeight) * 0.45f
        );

        float visualScale = GetGameplayVisualScale();
        float scaledVerticalPosition = _verticalPosition * visualScale;
        float scaledPlayerHeight = playerHeight * visualScale;
        float scaledVerticalInset = inset * visualScale;

        float playerLeft = playerX - playerWidth * 0.5f + inset;
        float playerRight = playerX + playerWidth * 0.5f - inset;
        float playerBottom = groundY + scaledVerticalPosition + scaledVerticalInset;
        float playerTop =
            groundY
            + scaledVerticalPosition
            + scaledPlayerHeight
            - scaledVerticalInset;

        for (int i = 0; i < _obstacles.Count; i++)
        {
            ObstacleRuntime obstacle = _obstacles[i];
            float x = playerX + (obstacle.HitTime - _elapsed) * _settings.scrollSpeed;

            if (x - obstacle.Width * 0.5f > areaWidth * 0.5f + 100f)
                continue;

            if (x + obstacle.Width * 0.5f < -areaWidth * 0.5f - 100f)
                continue;

            float obstacleLeft = x - obstacle.Width * 0.5f + inset * 0.35f;
            float obstacleRight = x + obstacle.Width * 0.5f - inset * 0.35f;
            float obstacleBottom = groundY;
            float obstacleTop =
                groundY
                + obstacle.Height * visualScale
                - scaledVerticalInset * 0.25f;

            bool horizontalOverlap =
                playerRight > obstacleLeft && playerLeft < obstacleRight;
            bool verticalOverlap =
                playerTop > obstacleBottom && playerBottom < obstacleTop;

            if (horizontalOverlap && verticalOverlap)
                return true;
        }

        return false;
    }

    private void BeginFailureHold()
    {
        if (_state != RunState.Playing)
            return;

        _state = RunState.FailureHold;
        _resultRemaining = failureHoldSeconds;
        TriggerAlarm();
        ShowResultOverlay("!!! TRACE DETECTED !!!\nRESTARTING LINK...", obstacleColor);
        SetStatusText("LINK COMPROMISED");
        SetPacketColor(obstacleColor);
    }

    private void ResetRun(LaptopMinigameContext context)
    {
        Canvas.ForceUpdateCanvases();

        _state = RunState.Playing;
        _elapsed = 0f;
        _verticalPosition = 0f;
        _verticalVelocity = 0f;
        _jumpBufferRemaining = 0f;
        _coyoteRemaining = coyoteSeconds;
        _resultRemaining = 0f;
        _grounded = true;
        _lastShownPercent = -1;

        if (_networkText != null)
            _networkText.text = $"> target={context.NetworkDisplayName}";

        if (_difficultyText != null)
        {
            _difficultyText.text = $"[{context.Difficulty.ToString().ToUpperInvariant()}]";
            _difficultyText.color = accentColor;
        }

        if (_resultOverlayRect != null)
            _resultOverlayRect.gameObject.SetActive(false);

        SetPacketColor(accentColor);
        SetStatusText(BuildProgressText(0));
        UpdatePlayingVisuals();
    }

    private void GenerateCourse(int seed)
    {
        ClearObstacleViews();

        var random = new DeterministicRandom(seed);
        float hitTime = _settings.firstObstacleSeconds;
        int safety = 0;

        while (hitTime < _settings.courseSeconds - 0.75f && safety < 64)
        {
            float stackRoll = random.Next01();
            int stackCount;

            if (stackRoll < _settings.tripleStackChance)
                stackCount = 3;
            else if (
                stackRoll
                < _settings.tripleStackChance + _settings.doubleStackChance
            )
                stackCount = 2;
            else
                stackCount = 1;

            float width = obstacleBlockSize;
            float height =
                obstacleBlockSize * stackCount
                + obstacleStackGap * Mathf.Max(0, stackCount - 1);

            RectTransform obstacleRect = CreateObstacleView(
                _obstacles.Count,
                stackCount
            );

            _obstacles.Add(
                new ObstacleRuntime
                {
                    HitTime = hitTime,
                    Width = width,
                    Height = height,
                    Rect = obstacleRect,
                }
            );

            hitTime += random.Range(
                _settings.minGapSeconds,
                _settings.maxGapSeconds
            );
            safety++;
        }
    }

    private void UpdatePlayingVisuals()
    {
        if (_gameArea == null)
            return;

        float areaWidth = _gameArea.rect.width;
        float groundY = GetGroundY();
        float playerX = GetPlayerX();

        float visualScale = GetGameplayVisualScale();

        if (_packetRect != null)
        {
            _packetRect.sizeDelta = new Vector2(
                playerWidth * 1.28f,
                playerHeight * visualScale
            );
            _packetRect.anchoredPosition = new Vector2(
                playerX,
                groundY
                    + _verticalPosition * visualScale
                    + playerHeight * visualScale * 0.5f
            );

            float normalizedVerticalSpeed = Mathf.Clamp(
                _verticalVelocity / Mathf.Max(1f, jumpVelocity),
                -1f,
                1f
            );
            float targetTilt = _grounded
                ? 0f
                : -normalizedVerticalSpeed * jumpTiltDegrees;
            float tiltBlend = jumpTiltSmoothing <= 0f
                ? 1f
                : 1f - Mathf.Exp(-jumpTiltSmoothing * Time.deltaTime);
            float currentTilt = _packetRect.localEulerAngles.z;
            float tilt = Mathf.LerpAngle(currentTilt, targetTilt, tiltBlend);
            _packetRect.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }

        for (int i = 0; i < _obstacles.Count; i++)
        {
            ObstacleRuntime obstacle = _obstacles[i];

            if (obstacle.Rect == null)
                continue;

            float x = playerX + (obstacle.HitTime - _elapsed) * _settings.scrollSpeed;
            bool visible =
                x + obstacle.Width * 0.5f >= -areaWidth * 0.5f - 100f
                && x - obstacle.Width * 0.5f <= areaWidth * 0.5f + 100f;

            if (obstacle.Rect.gameObject.activeSelf != visible)
                obstacle.Rect.gameObject.SetActive(visible);

            if (!visible)
                continue;

            obstacle.Rect.sizeDelta = new Vector2(
                obstacle.Width * 1.20f,
                obstacle.Height * visualScale
            );
            obstacle.Rect.anchoredPosition = new Vector2(
                x,
                groundY + obstacle.Height * visualScale * 0.5f
            );
        }

        float progress = Mathf.Clamp01(_elapsed / _settings.courseSeconds);
        int percent = Mathf.RoundToInt(progress * 100f);

        if (percent != _lastShownPercent)
        {
            _lastShownPercent = percent;
            SetStatusText(BuildProgressText(percent));
        }
    }

    private static string BuildProgressText(int percent)
    {
        int clamped = Mathf.Clamp(percent, 0, 100);
        int filled = Mathf.RoundToInt(
            clamped / 100f * ProgressCharacterCount
        );
        filled = Mathf.Clamp(filled, 0, ProgressCharacterCount);

        return $"[{new string('#', filled)}{new string('.', ProgressCharacterCount - filled)}] {clamped:00}%";
    }

    private float GetPlayerX()
    {
        if (_gameArea == null)
            return 0f;

        return Mathf.Lerp(
            -_gameArea.rect.width * 0.5f,
            _gameArea.rect.width * 0.5f,
            playerHorizontalPosition
        );
    }

    private float GetGroundY()
    {
        if (_gameArea == null)
            return 0f;

        return -_gameArea.rect.height * 0.5f + groundPadding * GetGameplayVisualScale();
    }

    private void BuildUiIfNeeded()
    {
        if (_root != null)
            return;

        _root = (RectTransform)transform;
        StretchToParent(_root);

        Image background = CreateImage("Background", _root, backgroundColor);
        Stretch(background.rectTransform, Vector2.zero, Vector2.one);

        TMP_Text title = CreateText(
            "Title",
            _root,
            "> FIREWALL_RUNNER",
            48f,
            TextAlignmentOptions.MidlineLeft,
            textColor,
            FontStyles.Bold
        );
        Stretch(
            title.rectTransform,
            new Vector2(0.050f, 0.932f),
            new Vector2(0.66f, 0.982f)
        );

        _difficultyText = CreateText(
            "Difficulty",
            _root,
            "[EASY]",
            30f,
            TextAlignmentOptions.MidlineRight,
            accentColor,
            FontStyles.Bold
        );
        Stretch(
            _difficultyText.rectTransform,
            new Vector2(0.69f, 0.932f),
            new Vector2(0.950f, 0.982f)
        );

        _networkText = CreateText(
            "Network",
            _root,
            "> target=unknown",
            25f,
            TextAlignmentOptions.MidlineLeft,
            mutedTextColor,
            FontStyles.Normal
        );
        Stretch(
            _networkText.rectTransform,
            new Vector2(0.050f, 0.878f),
            new Vector2(0.62f, 0.930f)
        );

        _statusText = CreateText(
            "Status",
            _root,
            BuildProgressText(0),
            24f,
            TextAlignmentOptions.MidlineRight,
            accentColor,
            FontStyles.Normal
        );
        Stretch(
            _statusText.rectTransform,
            new Vector2(0.47f, 0.878f),
            new Vector2(0.950f, 0.930f)
        );

        Image headerDivider = CreateImage(
            "HeaderDivider",
            _root,
            WithAlpha(mutedTextColor, 0.58f)
        );
        ConfigureHorizontalSeparator(headerDivider.rectTransform, 0.858f);

        _gameArea = CreateRect("GameArea", _root);
        Stretch(
            _gameArea,
            new Vector2(0.018f, 0.115f),
            new Vector2(0.982f, 0.842f)
        );

        TMP_Text ground = CreateText(
            "Ground",
            _gameArea,
            new string('=', 220),
            30f,
            TextAlignmentOptions.Center,
            accentColor,
            FontStyles.Normal
        );
        ground.rectTransform.anchorMin = new Vector2(0f, 0f);
        ground.rectTransform.anchorMax = new Vector2(1f, 0f);
        ground.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        ground.rectTransform.sizeDelta = new Vector2(0f, 44f);
        ground.rectTransform.anchoredPosition = new Vector2(
            0f,
            groundPadding * GetGameplayVisualScale()
        );
        ConfigureAsciiLine(ground);

        _obstacleLayer = CreateRect("ObstacleLayer", _gameArea);
        Stretch(_obstacleLayer, Vector2.zero, Vector2.one);

        _packetText = CreateText(
            "PlayerPacket",
            _gameArea,
            packetGlyph,
            50f * GetGameplayVisualScale(),
            TextAlignmentOptions.Center,
            accentColor,
            FontStyles.Bold
        );
        _packetRect = _packetText.rectTransform;
        SetCenteredRect(_packetRect, new Vector2(playerWidth, playerHeight));
        _packetText.overflowMode = TextOverflowModes.Overflow;

        Image footerDivider = CreateImage(
            "FooterDivider",
            _root,
            WithAlpha(mutedTextColor, 0.58f)
        );
        ConfigureHorizontalSeparator(footerDivider.rectTransform, 0.099f);

        TMP_Text instruction = CreateText(
            "Instruction",
            _root,
            "[SPACE/E] JUMP                 [Q] DISCONNECT",
            27f,
            TextAlignmentOptions.Center,
            mutedTextColor,
            FontStyles.Bold
        );
        Stretch(
            instruction.rectTransform,
            new Vector2(0.045f, 0.018f),
            new Vector2(0.955f, 0.082f)
        );

        _resultOverlayImage = CreateImage(
            "ResultOverlay",
            _root,
            new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.96f)
        );
        _resultOverlayRect = _resultOverlayImage.rectTransform;
        Stretch(_resultOverlayRect, Vector2.zero, Vector2.one);

        _resultText = CreateText(
            "ResultText",
            _resultOverlayRect,
            string.Empty,
            72f,
            TextAlignmentOptions.Center,
            obstacleColor,
            FontStyles.Bold
        );
        Stretch(_resultText.rectTransform, Vector2.zero, Vector2.one);
        _resultOverlayRect.gameObject.SetActive(false);
    }

    private RectTransform CreateObstacleView(int index, int stackCount)
    {
        RectTransform root = CreateRect($"Firewall_{index:00}", _obstacleLayer);
        float visualScale = GetGameplayVisualScale();
        float visualBlockSize = obstacleBlockSize * visualScale;
        float visualStackGap = obstacleStackGap * visualScale;
        float totalHeight =
            visualBlockSize * stackCount
            + visualStackGap * Mathf.Max(0, stackCount - 1);
        SetCenteredRect(
            root,
            new Vector2(obstacleBlockSize * 1.20f, totalHeight)
        );

        for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
        {
            TMP_Text block = CreateText(
                $"Block_{stackIndex + 1}",
                root,
                obstacleGlyph,
                43f * GetGameplayVisualScale(),
                TextAlignmentOptions.Center,
                obstacleColor,
                FontStyles.Bold
            );
            RectTransform blockRect = block.rectTransform;
            SetCenteredRect(
                blockRect,
                new Vector2(obstacleBlockSize * 1.20f, visualBlockSize)
            );

            float bottom = stackIndex * (visualBlockSize + visualStackGap);
            blockRect.anchoredPosition = new Vector2(
                0f,
                -totalHeight * 0.5f + bottom + visualBlockSize * 0.5f
            );
            block.overflowMode = TextOverflowModes.Overflow;
        }

        return root;
    }

    private void ClearObstacleViews()
    {
        for (int i = 0; i < _obstacles.Count; i++)
        {
            if (_obstacles[i].Rect != null)
                Destroy(_obstacles[i].Rect.gameObject);
        }

        _obstacles.Clear();
    }

    private void ShowResultOverlay(string message, Color color)
    {
        if (_resultOverlayRect == null)
            return;

        _resultOverlayRect.gameObject.SetActive(true);

        if (_resultOverlayImage != null)
        {
            _resultOverlayImage.color = new Color(
                backgroundColor.r,
                backgroundColor.g,
                backgroundColor.b,
                0.96f
            );
        }

        if (_resultText != null)
        {
            _resultText.text = message;
            _resultText.color = color;
            UpdateTextGlowColor(_resultText, color);
        }
    }

    private void SetStatusText(string value)
    {
        if (_statusText != null)
            _statusText.text = value;
    }

    private void SetPacketColor(Color color)
    {
        if (_packetText == null)
            return;

        _packetText.color = color;
        UpdateTextGlowColor(_packetText, color);
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private Image CreateImage(
        string objectName,
        Transform parent,
        Color color
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles fontStyle
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        var text = gameObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = fontStyle;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.characterSpacing = 1f;

        if (terminalFont != null)
            text.font = terminalFont;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        ApplyTerminalGlow(text, color);
        return text;
    }

    private void ApplyTerminalGlow(TMP_Text text, Color sourceColor)
    {
        if (!enableTextGlow || text == null || text.fontSharedMaterial == null)
            return;

        var material = new Material(text.fontSharedMaterial)
        {
            name = $"{text.fontSharedMaterial.name} ({name} Glow)"
        };
        _runtimeFontMaterials.Add(material);
        text.fontMaterial = material;

        Color glowColor = sourceColor;
        glowColor.a = Mathf.Clamp01(sourceColor.a * textGlowAlpha * 1.18f);

        float effectiveGlowOuter = Mathf.Clamp01(textGlowOuter * 1.18f);
        float effectiveGlowPower = Mathf.Clamp01(textGlowPower * 1.08f);

        if (material.HasProperty("_GlowColor"))
            material.SetColor("_GlowColor", glowColor);
        if (material.HasProperty("_GlowOffset"))
            material.SetFloat("_GlowOffset", 0f);
        if (material.HasProperty("_GlowInner"))
            material.SetFloat("_GlowInner", 0.02f);
        if (material.HasProperty("_GlowOuter"))
            material.SetFloat("_GlowOuter", effectiveGlowOuter);
        if (material.HasProperty("_GlowPower"))
            material.SetFloat("_GlowPower", effectiveGlowPower);

        material.EnableKeyword("GLOW_ON");
        text.SetMaterialDirty();
        text.SetVerticesDirty();
    }

    private void UpdateTextGlowColor(TMP_Text text, Color sourceColor)
    {
        if (!enableTextGlow || text == null || text.fontSharedMaterial == null)
            return;

        Material material = text.fontSharedMaterial;
        if (!material.HasProperty("_GlowColor"))
            return;

        Color glowColor = sourceColor;
        glowColor.a = Mathf.Clamp01(sourceColor.a * textGlowAlpha * 1.18f);
        material.SetColor("_GlowColor", glowColor);
        text.SetMaterialDirty();
    }

    private void ConfigureHorizontalSeparator(RectTransform rect, float normalizedY)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, normalizedY);
        rect.anchorMax = new Vector2(1f, normalizedY);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(0f, separatorThickness);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Color WithAlpha(Color color, float alphaMultiplier)
    {
        color.a = Mathf.Clamp01(color.a * alphaMultiplier);
        return color;
    }

    private static void ConfigureAsciiLine(TMP_Text text)
    {
        if (text == null)
            return;

        text.overflowMode = TextOverflowModes.Masking;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.characterSpacing = 0f;
    }

    private static void StretchToParent(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax
    )
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void SanitizeSettings(CourseSettings settings)
    {
        if (settings == null)
            return;

        settings.courseSeconds = Mathf.Max(5f, settings.courseSeconds);
        settings.scrollSpeed = Mathf.Max(100f, settings.scrollSpeed);
        settings.firstObstacleSeconds = Mathf.Max(0.5f, settings.firstObstacleSeconds);
        settings.minGapSeconds = Mathf.Max(0.5f, settings.minGapSeconds);
        settings.maxGapSeconds = Mathf.Max(
            settings.minGapSeconds,
            settings.maxGapSeconds
        );
        settings.doubleStackChance = Mathf.Clamp01(settings.doubleStackChance);
        settings.tripleStackChance = Mathf.Clamp01(settings.tripleStackChance);

        float stackedChance =
            settings.doubleStackChance + settings.tripleStackChance;

        if (stackedChance > 0.95f)
        {
            float scale = 0.95f / stackedChance;
            settings.doubleStackChance *= scale;
            settings.tripleStackChance *= scale;
        }
    }

    private void EnsureDifficultySettingsExist()
    {
        easy ??= new CourseSettings
        {
            courseSeconds = 14f,
            scrollSpeed = 435f,
            firstObstacleSeconds = 2.2f,
            minGapSeconds = 1.45f,
            maxGapSeconds = 2.00f,
            doubleStackChance = 0.26f,
            tripleStackChance = 0.08f,
        };

        hard ??= new CourseSettings
        {
            courseSeconds = 18f,
            scrollSpeed = 555f,
            firstObstacleSeconds = 1.95f,
            minGapSeconds = 1.08f,
            maxGapSeconds = 1.55f,
            doubleStackChance = 0.34f,
            tripleStackChance = 0.18f,
        };
    }

    private float GetGameplayVisualScale()
    {
        return gameplayVisualScale > 0f ? gameplayVisualScale : 1.45f;
    }

    private void OnValidate()
    {
        EnsureDifficultySettingsExist();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        playerWidth = Mathf.Max(20f, playerWidth);
        playerHeight = Mathf.Max(20f, playerHeight);
        jumpVelocity = Mathf.Max(100f, jumpVelocity);
        gravity = Mathf.Max(100f, gravity);
        collisionInset = Mathf.Max(0f, collisionInset);
        obstacleBlockSize = Mathf.Max(20f, obstacleBlockSize);
        obstacleStackGap = Mathf.Max(0f, obstacleStackGap);
        gameplayVisualScale = Mathf.Clamp(gameplayVisualScale, 1f, 1.8f);
        jumpTiltDegrees = Mathf.Clamp(jumpTiltDegrees, 0f, 45f);
        jumpTiltSmoothing = Mathf.Max(0f, jumpTiltSmoothing);
        textGlowAlpha = Mathf.Clamp01(textGlowAlpha);
        textGlowOuter = Mathf.Clamp01(textGlowOuter);
        textGlowPower = Mathf.Clamp01(textGlowPower);
        separatorThickness = Mathf.Clamp(separatorThickness, 2f, 20f);

        if (string.IsNullOrWhiteSpace(packetGlyph))
            packetGlyph = "[>]";

        if (string.IsNullOrWhiteSpace(obstacleGlyph))
            obstacleGlyph = "[#]";
    }
}

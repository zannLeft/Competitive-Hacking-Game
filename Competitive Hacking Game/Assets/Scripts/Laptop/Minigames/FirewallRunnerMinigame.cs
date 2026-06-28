using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class FirewallRunnerMinigame : LaptopMinigameBase
{
    private const int CurrentTuningVersion = 32;

    [SerializeField, HideInInspector]
    private int tuningVersion;

    [Serializable]
    private sealed class CourseSettings
    {
        [Min(5f)]
        public float courseSeconds = 14f;

        [Min(100f)]
        public float scrollSpeed = 390f;

        [Tooltip("Multiplier applied to scroll speed at the end of the course. Speed ramps linearly from 1x to this value.")]
        [Range(1f, 2f)]
        public float endSpeedMultiplier = 1.28f;

        [Min(0.5f)]
        public float firstObstacleSeconds = 2.5f;

        [Min(0.5f)]
        public float minGapSeconds = 1.65f;

        [Min(0.5f)]
        public float maxGapSeconds = 2.30f;

        [Tooltip("Chance that an obstacle gap becomes an extra-long breather gap, creating less predictable jump rhythms.")]
        [Range(0f, 1f)]
        public float longGapChance = 0.24f;

        [Tooltip("Multiplier applied when an extra-long gap is selected.")]
        [Range(1f, 2f)]
        public float longGapMultiplier = 1.35f;

        [Range(0f, 1f)]
        public float doubleStackChance = 0.26f;

        [HideInInspector]
        public float tripleStackChance = 0f;

        [Tooltip("Chance that a ground challenge adds a second firewall after a short landing-and-jump window.")]
        [Range(0f, 1f)]
        public float clusteredObstacleChance = 0.42f;

        [Tooltip("Minimum time between sequential firewalls inside a clustered ground challenge.")]
        [Min(0.08f)]
        public float minClusterSpacingSeconds = 0.74f;

        [Tooltip("Maximum time between sequential firewalls inside a clustered ground challenge.")]
        [Min(0.08f)]
        public float maxClusterSpacingSeconds = 0.95f;

        [Tooltip("After a two-firewall sequence, chance to add a third firewall with another landing-and-jump window.")]
        [Range(0f, 1f)]
        public float thirdClusterObstacleChance = 0.10f;

        [Tooltip("Chance that the next challenge is a jumpable floor gap instead of a firewall stack.")]
        [Range(0f, 1f)]
        public float floorGapChance = 0.18f;

        [Tooltip("Chance that the next challenge is a Geometry-Dash-style elevated platform sequence over a data void.")]
        [Range(0f, 1f)]
        public float platformPatternChance = 0.14f;
    }

    private sealed class ObstacleRuntime
    {
        public float HitTime;
        public float Width;
        public float Height;
        public float BaseHeight;
        public int StackCount;
        public RectTransform Rect;
    }

    private sealed class GroundSegmentRuntime
    {
        public float CenterDistance;
        public float Width;
        public RectTransform Rect;
        public RawImage Track;
    }

    private sealed class PlatformRuntime
    {
        public float HitTime;
        public float Width;
        public float TopHeight;
        public RectTransform Rect;
        public RawImage Track;
    }

    private sealed class GapRuntime
    {
        public float CenterDistance;
        public float Width;
    }

    private enum RunState
    {
        AwaitingStart,
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
        courseSeconds = 26f,
        scrollSpeed = 650f,
        endSpeedMultiplier = 1.42f,
        firstObstacleSeconds = 2.25f,
        minGapSeconds = 0.82f,
        maxGapSeconds = 3.15f,
        longGapChance = 0.16f,
        longGapMultiplier = 1.42f,
        doubleStackChance = 0.30f,
        tripleStackChance = 0f,
        clusteredObstacleChance = 0.48f,
        minClusterSpacingSeconds = 0.74f,
        maxClusterSpacingSeconds = 1.02f,
        thirdClusterObstacleChance = 0.10f,
        floorGapChance = 0.17f,
        platformPatternChance = 0.22f,
    };

    [SerializeField]
    private CourseSettings hard = new()
    {
        courseSeconds = 34f,
        scrollSpeed = 860f,
        endSpeedMultiplier = 1.58f,
        firstObstacleSeconds = 2.00f,
        minGapSeconds = 0.68f,
        maxGapSeconds = 2.85f,
        longGapChance = 0.13f,
        longGapMultiplier = 1.38f,
        doubleStackChance = 0.46f,
        tripleStackChance = 0f,
        clusteredObstacleChance = 0.68f,
        minClusterSpacingSeconds = 0.70f,
        maxClusterSpacingSeconds = 0.98f,
        thirdClusterObstacleChance = 0.24f,
        floorGapChance = 0.20f,
        platformPatternChance = 0.31f,
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
    private float jumpVelocity = 1450f;

    [SerializeField, Min(100f)]
    private float gravity = 4800f;

    [SerializeField, Range(0.1f, 1f)]
    private float releasedJumpVelocityMultiplier = 1f;

    [SerializeField, Min(0f)]
    private float jumpBufferSeconds = 0.14f;

    [SerializeField, Min(0f)]
    private float coyoteSeconds = 0.10f;

    [SerializeField, Min(0f)]
    private float collisionInset = 4f;

    [Tooltip("Extra horizontal forgiveness when the square player collider lands on or remains supported by a moving platform.")]
    [SerializeField, Min(0f)]
    private float platformLandingHorizontalGrace = 10f;

    [Tooltip("Vertical crossing tolerance used when the player's feet pass through a platform top between simulation steps.")]
    [SerializeField, Min(0f)]
    private float platformLandingVerticalTolerance = 8f;

    [Tooltip("Keeps the packet supported until the middle of its square collider has actually moved over a floor opening, instead of failing from a tiny edge touch.")]
    [SerializeField, Range(0.05f, 0.45f)]
    private float groundSupportProbeWidthMultiplier = 0.18f;

    [Tooltip("Extra distance the whole packet must fall below the floor before the hole counts as a failure.")]
    [SerializeField, Min(0f)]
    private float holeDeathClearance = 8f;

    [Tooltip("Small multiplier applied only to ordinary floor-hole widths. Platform voids keep their authored dimensions.")]
    [SerializeField, Range(1f, 1.35f)]
    private float floorGapWidthMultiplier = 1.12f;

    [Header("Obstacles")]
    [Tooltip("Scales each firewall square relative to the player's logical width/height.")]
    [SerializeField, Range(1f, 1.6f)]
    private float obstacleSizeMultiplier = 1.30f;

    [Tooltip("Vertical space between stacked square firewall blocks.")]
    [SerializeField, Min(0f)]
    private float obstacleStackGap = 4f;

    [Tooltip("Collision size of each red firewall square relative to its logical visual tile. This compensates for the font glyph's internal padding so collisions match the square the player actually sees.")]
    [SerializeField, Range(0.45f, 1f)]
    private float obstacleCollisionScale = 0.72f;

    [Tooltip("Small visual/collision lift so firewall blocks sit on top of the terminal floor instead of overlapping it.")]
    [SerializeField, Min(0f)]
    private float obstacleGroundClearance = 4f;

    [Header("Geometry Dash Patterns")]
    [Tooltip("Height of the elevated cyan platform above the normal floor, measured in obstacle blocks.")]
    [SerializeField, Range(0.8f, 2.5f)]
    private float platformHeightInBlocks = 1.25f;

    [Tooltip("Thickness of the safe platform row relative to one firewall block.")]
    [SerializeField, Range(0.25f, 1f)]
    private float platformThicknessMultiplier = 1f;

    // Legacy serialized values retained so existing prefabs upgrade without
    // losing data. V19 no longer hides a continuous floor with overlay masks;
    // it renders real moving ground segments with actual empty holes.
    [SerializeField, HideInInspector]
    private float gapMaskExtraHeight = 16f;

    [SerializeField, HideInInspector]
    private float gapMaskHorizontalOverscan = 6f;

    [SerializeField, HideInInspector]
    private float gapMaskTopOverscan = 4f;

    [Header("Result Timing")]
    [SerializeField, Min(0f)]
    private float failureHoldSeconds = 0.90f;

    [Header("Resume")]
    [Tooltip("Brief local reorientation pause when reopening the laptop during an active run. Gameplay state is preserved exactly.")]
    [SerializeField, Min(0f)]
    private float resumeGraceSeconds = 0.90f;

    [Header("Hacker OS Visuals")]
    [Tooltip("Optional monospace TMP font. Iosevka Term Mono is a good fit.")]
    [SerializeField]
    private TMP_FontAsset terminalFont;

    [SerializeField]
    private string shellName = "ZannOS";

    [SerializeField]
    private Color backgroundColor = new(0.004f, 0.022f, 0.018f, 1f);

    [SerializeField]
    private Color surfaceColor = new(0.009f, 0.035f, 0.029f, 1f);

    [SerializeField]
    private Color raisedSurfaceColor = new(0.013f, 0.046f, 0.038f, 1f);

    [SerializeField]
    private Color structureColor = new(0.190f, 0.770f, 0.660f, 1f);

    [SerializeField]
    private Color accentColor = new(0.740f, 1.000f, 0.920f, 1f);

    [SerializeField]
    private Color objectiveColor = new(0.540f, 0.965f, 0.885f, 1f);

    [SerializeField]
    private Color textColor = new(0.910f, 0.995f, 0.960f, 1f);

    [SerializeField]
    private Color mutedTextColor = new(0.230f, 0.530f, 0.470f, 1f);

    [SerializeField]
    private Color obstacleColor = new(1.000f, 0.400f, 0.450f, 1f);

    [SerializeField, HideInInspector]
    private string packetGlyph = "□";

    [SerializeField]
    private string obstacleGlyph = "■";

    [Header("Hollow Square Track")]
    [SerializeField, HideInInspector]
    private string floorGlyph = "□";

    [Tooltip("Legacy TMP setting retained only for prefab migration.")]
    [SerializeField, HideInInspector]
    private float floorGlyphFontSize = 72f;

    [Tooltip("Legacy TMP setting retained only for prefab migration.")]
    [SerializeField, HideInInspector]
    private float floorGlyphCellWidthEm = 0.82f;

    [Tooltip("Exact UI tile size used by the normal floor and every elevated platform.")]
    [SerializeField, Range(36f, 96f)]
    private float floorGlyphVisualSize = 58f;

    [Tooltip("Visible spacing between neighbouring hollow floor/platform squares.")]
    [SerializeField, Range(0f, 18f)]
    private float floorGlyphGap = 3f;

    [Tooltip("Legacy TMP setting retained only for prefab migration.")]
    [SerializeField, HideInInspector]
    private float packetGlyphFontSize = 120f;

    [Tooltip("Exact square UI size of the player packet, capped to its gameplay collider size.")]
    [SerializeField, Range(36f, 120f)]
    private float packetGlyphVisualSize = 76f;

    [Tooltip("Tiny visual nudge used to make the hollow packet touch the active floor/platform instead of appearing to hover above it.")]
    [SerializeField, Range(-8f, 8f)]
    private float packetGroundContactOffset = -2f;

    [Header("Screen Scale")]
    [Tooltip("Scales the ASCII runner vertically and enlarges its glyphs without changing obstacle timing.")]
    [SerializeField, Range(1f, 1.8f)]
    private float gameplayVisualScale = 1.45f;

    [Header("Jump Presentation")]
    [SerializeField, Range(0f, 45f)]
    private float jumpTiltDegrees = 22f;

    [SerializeField, Min(0f)]
    private float jumpTiltSmoothing = 18f;

    [SerializeField, HideInInspector]
    private float separatorThickness = 8f;

    private readonly List<ObstacleRuntime> _obstacles = new();
    private readonly List<GroundSegmentRuntime> _groundSegments = new();
    private readonly List<PlatformRuntime> _platforms = new();
    private readonly List<GapRuntime> _gaps = new();

    private RectTransform _root;
    private RectTransform _gameArea;
    private RectTransform _groundLayer;
    private RectTransform _platformLayer;
    private RectTransform _obstacleLayer;
    private RectTransform _packetRect;
    private RawImage _packetImage;
    private Texture2D _hollowSquareTexture;
    private LaptopMinigameVisualShell _shell;

    private LaptopMinigameContext _context;
    private CourseSettings _settings;
    private RunState _state;
    private float _elapsed;
    private float _verticalPosition;
    private float _verticalVelocity;
    private float _jumpBufferRemaining;
    private float _coyoteRemaining;
    private float _resultRemaining;
    private float _supportHeight;
    private float _resumeGraceRemaining;
    private int _resumeCountdownStep = -1;
    private bool _grounded;
    private int _lastShownPercent = -1;

    private const float MaxFrameSimulationSeconds = 0.10f;
    private const float SimulationStepSeconds = 1f / 120f;
    private const float GroundVisualHeight = 86f;
    private const int HollowTextureResolution = 64;
    private const int HollowTextureBorderPixels = 5;

    public override bool SupportsSessionResume => true;

    protected override void OnPrepare()
    {
        EnsureDifficultySettingsExist();
        ApplyAsciiVisualMigration();
        ApplyVersionedTuning();
        BuildUiIfNeeded();
    }

    protected override void OnBegin(LaptopMinigameContext context)
    {
        EnsureDifficultySettingsExist();
        ApplyAsciiVisualMigration();
        ApplyVersionedTuning();
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;

        SanitizeSettings(_settings);
        Canvas.ForceUpdateCanvases();
        GenerateCourse(context.Seed);
        ResetRun(context, waitForLaunch: true);
    }

    protected override void OnResume(LaptopMinigameContext context)
    {
        EnsureDifficultySettingsExist();
        ApplyAsciiVisualMigration();
        ApplyVersionedTuning();
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;
        SanitizeSettings(_settings);

        string difficulty = context.Difficulty.ToString().ToUpperInvariant();
        _shell?.SetContext(context.NetworkDisplayName, difficulty);
        _shell?.SetFooterLeft("SURVIVE UNTIL ROUTE COMPLETION");

        _jumpBufferRemaining = 0f;
        _resumeGraceRemaining = 0f;
        _resumeCountdownStep = -1;

        if (_state == RunState.AwaitingStart)
        {
            _shell?.HideResult();
            _shell?.SetBriefingVisible(true);
            _shell?.SetProgress(0f);
            SetPacketColor(accentColor);
            SetStatusText("ROUTE 00%");
            UpdatePlayingVisuals();
            return;
        }

        _shell?.SetBriefingVisible(false);

        if (_state == RunState.FailureHold)
        {
            ShowResultOverlay(
                "TRACE DETECTED\nROUTE INVALIDATED\n\nREBUILDING SESSION...",
                obstacleColor
            );
            SetPacketColor(obstacleColor);
            UpdatePlayingVisuals();
            SetStatusText("SECURITY TRIGGERED");
            return;
        }

        _shell?.HideResult();
        SetPacketColor(accentColor);
        UpdatePlayingVisuals();

        _resumeGraceRemaining = Mathf.Max(0f, resumeGraceSeconds);
        if (_resumeGraceRemaining > 0f)
            UpdateResumePresentation();
        else
            RestoreProgressPresentation();
    }

    protected override void OnJumpPressed()
    {
        if (_resumeGraceRemaining > 0f && _state == RunState.Playing)
        {
            TriggerActionPerformed();
            _jumpBufferRemaining = Mathf.Max(
                _jumpBufferRemaining,
                jumpBufferSeconds
            );
            return;
        }

        if (_state == RunState.AwaitingStart)
        {
            TriggerActionPerformed();
            BeginRunFromBriefing();
            return;
        }

        if (_state != RunState.Playing)
            return;

        // This is the accepted player action for Firewall Runner.
        // PlayerLaptopHacker plays it immediately for the owner and relays it
        // to nearby players as a positional keyboard keypress.
        TriggerActionPerformed();
        _jumpBufferRemaining = Mathf.Max(_jumpBufferRemaining, jumpBufferSeconds);
    }

    protected override void OnJumpReleased()
    {
        if (_resumeGraceRemaining > 0f || _state != RunState.Playing)
            return;

        if (_verticalVelocity > 0f)
            _verticalVelocity *= releasedJumpVelocityMultiplier;
    }

    protected override void OnSuspend()
    {
        // Preserve the exact course and physics state. Only clear transient input
        // so reopening cannot fire a stale jump command.
        _jumpBufferRemaining = 0f;
        _resumeGraceRemaining = 0f;
        _resumeCountdownStep = -1;
    }

    protected override void OnAbort()
    {
        OnSuspend();
        _verticalVelocity = 0f;
        _resultRemaining = 0f;
    }

    private void OnDisable()
    {
        if (IsRunning)
            Abort();
    }

    private void OnDestroy()
    {
        if (_hollowSquareTexture != null)
        {
            Destroy(_hollowSquareTexture);
            _hollowSquareTexture = null;
        }
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        _shell?.Tick(Time.unscaledTime);

        if (_state == RunState.AwaitingStart)
            return;

        float frameDelta = Mathf.Min(Time.deltaTime, MaxFrameSimulationSeconds);

        if (_resumeGraceRemaining > 0f && _state == RunState.Playing)
        {
            _resumeGraceRemaining = Mathf.Max(
                0f,
                _resumeGraceRemaining - frameDelta
            );

            if (_resumeGraceRemaining > 0f)
                UpdateResumePresentation();
            else
                RestoreProgressPresentation();

            return;
        }

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

        ResetRun(_context, waitForLaunch: false);
    }

    private void SimulatePlayingStep(float deltaTime)
    {
        _elapsed += deltaTime;
        _jumpBufferRemaining = Mathf.Max(0f, _jumpBufferRemaining - deltaTime);

        if (_grounded)
        {
            bool lostElevatedSupport =
                _supportHeight > 0f
                && !HasSupportingPlatform(_supportHeight);
            bool lostGroundSupport =
                _supportHeight <= 0.01f
                && IsGapUnderPlayer();

            if (lostElevatedSupport || lostGroundSupport)
            {
                _grounded = false;
                _supportHeight = 0f;
            }
        }

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
            float visualScale = GetGameplayVisualScale();
            float previousFeetHeight = _verticalPosition * visualScale;

            _verticalVelocity -= gravity * deltaTime;
            _verticalPosition += _verticalVelocity * deltaTime;

            float currentFeetHeight = _verticalPosition * visualScale;

            if (
                _verticalVelocity <= 0f
                && TryLandOnPlatform(
                    previousFeetHeight,
                    currentFeetHeight,
                    out float platformTopHeight
                )
            )
            {
                _supportHeight = platformTopHeight;
                _verticalPosition = platformTopHeight / visualScale;
                _verticalVelocity = 0f;
                _grounded = true;
            }
            else if (
                _verticalPosition <= 0f
                && !IsGapUnderPlayer()
                && previousFeetHeight >= -platformLandingVerticalTolerance
            )
            {
                // Land only when the packet actually crosses the normal floor
                // from above (or within the tiny landing tolerance). Once it has
                // visibly fallen into a hole, the returning floor cannot snap it
                // back upward from below.
                _verticalPosition = 0f;
                _verticalVelocity = 0f;
                _supportHeight = 0f;
                _grounded = true;
            }
            else if (
                currentFeetHeight + GetPlayerCollisionSize()
                <= -holeDeathClearance
            )
            {
                // A floor opening is no longer an instant edge-touch failure.
                // The complete square packet must visibly disappear below the
                // floor before the alarm/retry state begins.
                BeginFailureHold();
                return;
            }
        }
        else
        {
            _verticalPosition = _supportHeight / GetGameplayVisualScale();
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
        float visualScale = GetGameplayVisualScale();
        float scaledVerticalPosition = _verticalPosition * visualScale;
        float playerColliderSize = GetPlayerCollisionSize();
        float playerHalfSize = playerColliderSize * 0.5f;

        float playerLeft = playerX - playerHalfSize;
        float playerRight = playerX + playerHalfSize;
        float playerBottom = groundY + scaledVerticalPosition + collisionInset;
        float playerTop = playerBottom + playerColliderSize;

        for (int i = 0; i < _obstacles.Count; i++)
        {
            ObstacleRuntime obstacle = _obstacles[i];
            float x = GetElementX(obstacle.HitTime);

            if (x - obstacle.Width * 0.5f > areaWidth * 0.5f + 100f)
                continue;

            if (x + obstacle.Width * 0.5f < -areaWidth * 0.5f - 100f)
                continue;

            // The solid-square TMP glyph occupies noticeably less than its
            // containing RectTransform. Use a smaller square hitbox per visible
            // block rather than treating the full layout cell (and the space
            // between stacked blocks) as dangerous.
            float blockSize = obstacle.Width;
            float hitboxSize = Mathf.Max(
                1f,
                blockSize * Mathf.Clamp(obstacleCollisionScale, 0.45f, 1f)
            );
            float hitboxHalfSize = hitboxSize * 0.5f;
            float obstacleLeft = x - hitboxHalfSize;
            float obstacleRight = x + hitboxHalfSize;

            bool horizontalOverlap =
                playerRight > obstacleLeft && playerLeft < obstacleRight;

            if (!horizontalOverlap)
                continue;

            float firstBlockBottom =
                groundY
                + obstacle.BaseHeight
                + obstacleGroundClearance;
            int stackCount = Mathf.Max(1, obstacle.StackCount);

            for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
            {
                float visualBlockCenter =
                    firstBlockBottom
                    + blockSize * 0.5f
                    + stackIndex * (blockSize + obstacleStackGap);
                float obstacleBottom = visualBlockCenter - hitboxHalfSize;
                float obstacleTop = visualBlockCenter + hitboxHalfSize;

                bool verticalOverlap =
                    playerTop > obstacleBottom && playerBottom < obstacleTop;

                if (verticalOverlap)
                    return true;
            }
        }

        return false;
    }

    private bool IsGapUnderPlayer()
    {
        if (_gameArea == null)
            return false;

        float playerX = GetPlayerX();
        float supportProbeHalfWidth =
            GetPlayerCollisionSize()
            * Mathf.Clamp(groundSupportProbeWidthMultiplier, 0.05f, 0.45f)
            * 0.5f;

        for (int i = 0; i < _gaps.Count; i++)
        {
            GapRuntime gap = _gaps[i];
            float x = GetDistanceElementX(gap.CenterDistance);
            float halfWidth = gap.Width * 0.5f;

            // Probe only the middle strip beneath the square packet. This lets
            // the leading/trailing edge hang slightly over a hole without being
            // treated as unsupported the instant it touches the opening.
            if (
                playerX + supportProbeHalfWidth > x - halfWidth
                && playerX - supportProbeHalfWidth < x + halfWidth
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool HasSupportingPlatform(float expectedTopHeight)
    {
        float playerX = GetPlayerX();
        float feetHalfWidth = GetPlayerPlatformSupportHalfWidth();

        for (int i = 0; i < _platforms.Count; i++)
        {
            PlatformRuntime platform = _platforms[i];

            if (Mathf.Abs(platform.TopHeight - expectedTopHeight) > 4f)
                continue;

            float x = GetElementX(platform.HitTime);
            float halfWidth = platform.Width * 0.5f;

            if (
                playerX + feetHalfWidth > x - halfWidth
                && playerX - feetHalfWidth < x + halfWidth
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool TryLandOnPlatform(
        float previousFeetHeight,
        float currentFeetHeight,
        out float topHeight
    )
    {
        topHeight = 0f;
        float playerX = GetPlayerX();
        float feetHalfWidth = GetPlayerPlatformSupportHalfWidth();
        float verticalTolerance = platformLandingVerticalTolerance;
        bool found = false;

        for (int i = 0; i < _platforms.Count; i++)
        {
            PlatformRuntime platform = _platforms[i];
            float x = GetElementX(platform.HitTime);
            float halfWidth = platform.Width * 0.5f;

            if (
                playerX + feetHalfWidth <= x - halfWidth
                || playerX - feetHalfWidth >= x + halfWidth
            )
            {
                continue;
            }

            float candidateTop = platform.TopHeight;

            if (
                previousFeetHeight + verticalTolerance < candidateTop
                || currentFeetHeight > candidateTop + verticalTolerance
            )
            {
                continue;
            }

            if (!found || candidateTop > topHeight)
            {
                found = true;
                topHeight = candidateTop;
            }
        }

        return found;
    }

    private float GetElementX(float hitTime)
    {
        return GetPlayerX() + GetTravelDistance(_elapsed, hitTime);
    }

    private float GetDistanceElementX(float centerDistance)
    {
        return GetPlayerX()
            + centerDistance
            - GetCumulativeTravelDistance(_elapsed);
    }

    private void BeginFailureHold()
    {
        if (_state != RunState.Playing)
            return;

        _state = RunState.FailureHold;
        _resultRemaining = failureHoldSeconds;
        TriggerAlarm();
        ShowResultOverlay(
            "TRACE DETECTED\nROUTE INVALIDATED\n\nREBUILDING SESSION...",
            obstacleColor
        );
        SetStatusText("SECURITY TRIGGERED");
        SetPacketColor(obstacleColor);
    }

    private void ResetRun(
        LaptopMinigameContext context,
        bool waitForLaunch
    )
    {
        Canvas.ForceUpdateCanvases();

        _state = waitForLaunch ? RunState.AwaitingStart : RunState.Playing;
        _elapsed = 0f;
        _verticalPosition = 0f;
        _verticalVelocity = 0f;
        _jumpBufferRemaining = 0f;
        _coyoteRemaining = coyoteSeconds;
        _resultRemaining = 0f;
        _supportHeight = 0f;
        _resumeGraceRemaining = 0f;
        _resumeCountdownStep = -1;
        _grounded = true;
        _lastShownPercent = -1;

        string difficulty = context.Difficulty.ToString().ToUpperInvariant();
        _shell?.SetContext(context.NetworkDisplayName, difficulty);
        _shell?.HideResult();
        _shell?.SetBriefingVisible(waitForLaunch);
        _shell?.SetFooterLeft("SURVIVE UNTIL ROUTE COMPLETION");
        _shell?.SetProgress(0f);

        SetPacketColor(accentColor);
        SetStatusText("ROUTE 00%");
        UpdatePlayingVisuals();
    }

    private void BeginRunFromBriefing()
    {
        if (_state != RunState.AwaitingStart)
            return;

        _state = RunState.Playing;
        _elapsed = 0f;
        _verticalPosition = 0f;
        _verticalVelocity = 0f;
        _jumpBufferRemaining = 0f;
        _coyoteRemaining = coyoteSeconds;
        _supportHeight = 0f;
        _resumeGraceRemaining = 0f;
        _resumeCountdownStep = -1;
        _grounded = true;
        _lastShownPercent = -1;

        _shell?.SetBriefingVisible(false);
        _shell?.SetProgress(0f);
        SetStatusText("ROUTE 00%");
        UpdatePlayingVisuals();
    }

    private void UpdateResumePresentation()
    {
        float segmentSeconds = Mathf.Max(0.05f, resumeGraceSeconds / 3f);
        int step = Mathf.Clamp(
            Mathf.CeilToInt(_resumeGraceRemaining / segmentSeconds),
            1,
            3
        );

        if (step == _resumeCountdownStep)
            return;

        _resumeCountdownStep = step;
        SetStatusText($"LINK RESTORED  ·  RESUMING {step}");
    }

    private void RestoreProgressPresentation()
    {
        _resumeGraceRemaining = 0f;
        _resumeCountdownStep = -1;
        _lastShownPercent = -1;
        UpdatePlayingVisuals();
    }

    private void GenerateCourse(int seed)
    {
        ClearCourseViews();

        var random = new DeterministicRandom(seed);
        float hitTime = _settings.firstObstacleSeconds;
        int safety = 0;

        while (hitTime < _settings.courseSeconds - 1.0f && safety < 96)
        {
            float patternRoll = random.Next01();
            float patternEndTime;

            if (patternRoll < _settings.platformPatternChance)
            {
                patternEndTime = AddPlatformPattern(ref random, hitTime);
            }
            else if (
                patternRoll
                < _settings.platformPatternChance + _settings.floorGapChance
            )
            {
                patternEndTime = AddFloorGap(ref random, hitTime);
            }
            else
            {
                patternEndTime = AddGroundObstacleCluster(ref random, hitTime);
            }

            hitTime = patternEndTime + ChoosePatternGap(ref random);
            safety++;
        }

        BuildGroundSegments();
    }

    private float AddGroundObstacleCluster(
        ref DeterministicRandom random,
        float hitTime
    )
    {
        AddObstacle(hitTime, ChooseStackCount(ref random), 0f);
        float lastHitTime = hitTime;

        if (random.Next01() < _settings.clusteredObstacleChance)
        {
            // These are intentionally far enough apart for the player to land
            // between them and jump again immediately. This creates a quick
            // two- or three-jump rhythm instead of an impossible same-jump pair.
            float spacing = random.Range(
                _settings.minClusterSpacingSeconds,
                _settings.maxClusterSpacingSeconds
            );
            lastHitTime += spacing;
            AddObstacle(lastHitTime, ChooseClusterStackCount(ref random), 0f);

            if (random.Next01() < _settings.thirdClusterObstacleChance)
            {
                spacing = random.Range(
                    _settings.minClusterSpacingSeconds,
                    _settings.maxClusterSpacingSeconds
                );
                lastHitTime += spacing;
                AddObstacle(lastHitTime, ChooseClusterStackCount(ref random), 0f);
            }
        }

        return lastHitTime;
    }

    private int ChooseClusterStackCount(ref DeterministicRandom random)
    {
        // Keep sequential clusters readable: double-height blocks still appear,
        // but single blocks remain common enough to produce varied silhouettes.
        float doubleChance = _settings.doubleStackChance * 0.72f;
        return random.Next01() < doubleChance ? 2 : 1;
    }

    private float AddFloorGap(
        ref DeterministicRandom random,
        float hitTime
    )
    {
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;
        float doubleGapChance = hardMode ? 0.42f : 0.28f;

        if (random.Next01() < doubleGapChance)
            return AddDoubleFloorGap(ref random, hitTime);

        return AddSingleFloorGap(ref random, hitTime);
    }

    private float AddSingleFloorGap(
        ref DeterministicRandom random,
        float hitTime
    )
    {
        float blockSize = GetObstacleBlockSize();
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;
        float minTiles = hardMode ? 1.85f : 1.55f;
        float maxTiles = hardMode ? 2.55f : 2.15f;
        float gapWidth =
            blockSize
            * random.Range(minTiles, maxTiles)
            * floorGapWidthMultiplier;
        AddGap(hitTime, gapWidth);

        return FindTimeAfterDistance(
            hitTime,
            gapWidth * 0.5f + blockSize * 0.65f
        );
    }

    private float AddDoubleFloorGap(
        ref DeterministicRandom random,
        float firstGapTime
    )
    {
        float blockSize = GetObstacleBlockSize();
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;

        float firstWidth =
            blockSize
            * random.Range(
                hardMode ? 1.45f : 1.30f,
                hardMode ? 1.95f : 1.75f
            )
            * floorGapWidthMultiplier;
        float secondWidth =
            blockSize
            * random.Range(
                hardMode ? 1.50f : 1.30f,
                hardMode ? 2.05f : 1.80f
            )
            * floorGapWidthMultiplier;
        float islandWidth = blockSize * random.Range(
            hardMode ? 1.65f : 1.85f,
            hardMode ? 2.25f : 2.45f
        );

        AddGap(firstGapTime, firstWidth);

        float secondCenterDistance =
            firstWidth * 0.5f
            + islandWidth
            + secondWidth * 0.5f;
        float secondGapTime = FindTimeAfterDistance(
            firstGapTime,
            secondCenterDistance
        );
        AddGap(secondGapTime, secondWidth);

        float fullPatternDistance =
            firstWidth * 0.5f
            + islandWidth
            + secondWidth
            + blockSize * 0.65f;

        return FindTimeAfterDistance(firstGapTime, fullPatternDistance);
    }

    private float AddPlatformPattern(
        ref DeterministicRandom random,
        float startTime
    )
    {
        float variant = random.Next01();

        if (variant < 0.20f)
            return AddSingleBridgePattern(ref random, startTime);

        if (variant < 0.43f)
            return AddLongBridgePattern(ref random, startTime);

        if (variant < 0.66f)
            return AddSteppedPlatformPattern(ref random, startTime, rising: true);

        if (variant < 0.84f)
            return AddSteppedPlatformPattern(ref random, startTime, rising: false);

        return AddThreeStepPlatformPattern(
            ref random,
            startTime,
            rising: random.Next01() < 0.68f
        );
    }

    private float AddSingleBridgePattern(
        ref DeterministicRandom random,
        float startTime
    )
    {
        float blockSize = GetObstacleBlockSize();
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;
        // Compact bridge proportions. These still read as a distinct
        // Geometry-Dash-style island, but no longer stretch across most of
        // the screen or create enormous voids beneath them.
        float totalWidth = blockSize * random.Range(
            hardMode ? 4.65f : 4.10f,
            hardMode ? 6.10f : 5.35f
        );
        float sideMargin = blockSize * random.Range(0.52f, 0.78f);
        float platformWidth = Mathf.Max(
            blockSize * 2.75f,
            totalWidth - sideMargin * 2f
        );
        float platformTop = blockSize * random.Range(0.98f, 1.18f);

        float gapCenterTime = FindTimeAfterDistance(startTime, totalWidth * 0.5f);
        float platformCenterTime = gapCenterTime;
        AddGap(gapCenterTime, totalWidth);
        AddPlatform(platformCenterTime, platformWidth, platformTop);

        // Some bridges are pure traversal; others contain one readable hazard.
        if (random.Next01() < (hardMode ? 0.74f : 0.58f))
        {
            float obstacleOffset = platformWidth * random.Range(0.10f, 0.22f);
            float obstacleTime = FindTimeAfterDistance(
                platformCenterTime,
                obstacleOffset
            );
            AddObstacle(obstacleTime, 1, platformTop);
        }

        return FindTimeAfterDistance(startTime, totalWidth);
    }

    private float AddLongBridgePattern(
        ref DeterministicRandom random,
        float startTime
    )
    {
        float blockSize = GetObstacleBlockSize();
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;

        float sideMargin = blockSize * random.Range(0.55f, 0.82f);
        float platformWidth = blockSize * random.Range(
            hardMode ? 6.10f : 5.65f,
            hardMode ? 8.20f : 7.40f
        );
        float totalWidth = sideMargin * 2f + platformWidth;
        float platformTop = blockSize * random.Range(1.08f, 1.38f);

        float gapCenterTime = FindTimeAfterDistance(startTime, totalWidth * 0.5f);
        AddGap(gapCenterTime, totalWidth);
        AddPlatform(gapCenterTime, platformWidth, platformTop);

        // Keep one readable hazard per continuous platform. Two hazards on
        // the same moving bridge could create a spacing window where neither a
        // single jump nor a land-and-jump sequence was physically possible.
        float normalizedPosition = random.Range(0.40f, 0.64f);
        float obstacleDistance = sideMargin + platformWidth * normalizedPosition;
        int stackCount = random.Next01() < (hardMode ? 0.30f : 0.14f) ? 2 : 1;

        AddObstacle(
            FindTimeAfterDistance(startTime, obstacleDistance),
            stackCount,
            platformTop
        );

        return FindTimeAfterDistance(startTime, totalWidth);
    }

    private float AddSteppedPlatformPattern(
        ref DeterministicRandom random,
        float startTime,
        bool rising
    )
    {
        float blockSize = GetObstacleBlockSize();
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;

        float entryMargin = blockSize * random.Range(0.48f, 0.72f);
        float exitMargin = blockSize * random.Range(0.50f, 0.76f);
        float betweenGap = blockSize * random.Range(0.42f, 0.68f);
        float firstWidth = blockSize * random.Range(2.20f, 3.10f);
        float secondWidth = blockSize * random.Range(2.25f, 3.20f);
        float totalWidth =
            entryMargin
            + firstWidth
            + betweenGap
            + secondWidth
            + exitMargin;

        float lowHeight = blockSize * random.Range(0.96f, 1.12f);
        float highHeight = lowHeight + blockSize * random.Range(0.58f, 0.78f);
        float firstHeight = rising ? lowHeight : highHeight;
        float secondHeight = rising ? highHeight : lowHeight;

        float gapCenterTime = FindTimeAfterDistance(startTime, totalWidth * 0.5f);
        AddGap(gapCenterTime, totalWidth);

        float firstCenterDistance = entryMargin + firstWidth * 0.5f;
        float secondCenterDistance =
            entryMargin + firstWidth + betweenGap + secondWidth * 0.5f;
        float firstCenterTime = FindTimeAfterDistance(startTime, firstCenterDistance);
        float secondCenterTime = FindTimeAfterDistance(startTime, secondCenterDistance);

        AddPlatform(firstCenterTime, firstWidth, firstHeight);
        AddPlatform(secondCenterTime, secondWidth, secondHeight);

        bool firstIsLower = firstHeight <= secondHeight;
        float lowerWidth = firstIsLower ? firstWidth : secondWidth;
        float lowerHeight = firstIsLower ? firstHeight : secondHeight;
        float lowerStartDistance = firstIsLower
            ? entryMargin
            : entryMargin + firstWidth + betweenGap;
        float obstacleChance = hardMode ? 0.66f : 0.46f;

        if (random.Next01() < obstacleChance && lowerWidth >= blockSize * 2.45f)
        {
            // One hazard per individual platform keeps every landing window
            // solvable. Multi-jump rhythms still come from separate platforms
            // and ground clusters, where there is guaranteed support between
            // hazards.
            AddObstacle(
                FindTimeAfterDistance(
                    startTime,
                    lowerStartDistance + lowerWidth * random.Range(0.38f, 0.64f)
                ),
                random.Next01() < (hardMode ? 0.24f : 0.10f) ? 2 : 1,
                lowerHeight
            );
        }

        return FindTimeAfterDistance(startTime, totalWidth);
    }

    private float AddThreeStepPlatformPattern(
        ref DeterministicRandom random,
        float startTime,
        bool rising
    )
    {
        float blockSize = GetObstacleBlockSize();
        bool hardMode = _context.Difficulty == LaptopMinigameDifficulty.Hard;

        float entryMargin = blockSize * random.Range(0.48f, 0.68f);
        float exitMargin = blockSize * random.Range(0.50f, 0.72f);
        float firstWidth = blockSize * random.Range(1.90f, 2.45f);
        float secondWidth = blockSize * random.Range(1.95f, 2.55f);
        float thirdWidth = blockSize * random.Range(2.00f, 2.65f);
        float firstGap = blockSize * random.Range(0.34f, 0.52f);
        float secondGap = blockSize * random.Range(0.36f, 0.56f);

        float totalWidth =
            entryMargin
            + firstWidth
            + firstGap
            + secondWidth
            + secondGap
            + thirdWidth
            + exitMargin;

        float lowHeight = blockSize * random.Range(0.86f, 0.98f);
        float stepHeight = blockSize * random.Range(0.40f, 0.50f);
        float middleHeight = lowHeight + stepHeight;
        float highHeight = middleHeight + stepHeight;

        float firstHeight = rising ? lowHeight : highHeight;
        float secondHeight = middleHeight;
        float thirdHeight = rising ? highHeight : lowHeight;

        float gapCenterTime = FindTimeAfterDistance(startTime, totalWidth * 0.5f);
        AddGap(gapCenterTime, totalWidth);

        float firstStart = entryMargin;
        float secondStart = firstStart + firstWidth + firstGap;
        float thirdStart = secondStart + secondWidth + secondGap;

        AddPlatform(
            FindTimeAfterDistance(startTime, firstStart + firstWidth * 0.5f),
            firstWidth,
            firstHeight
        );
        AddPlatform(
            FindTimeAfterDistance(startTime, secondStart + secondWidth * 0.5f),
            secondWidth,
            secondHeight
        );
        AddPlatform(
            FindTimeAfterDistance(startTime, thirdStart + thirdWidth * 0.5f),
            thirdWidth,
            thirdHeight
        );

        float hazardChance = hardMode ? 0.56f : 0.34f;
        if (random.Next01() < hazardChance)
        {
            float candidateStart;
            float candidateWidth;
            float candidateHeight;

            if (rising)
            {
                candidateStart = firstStart;
                candidateWidth = firstWidth;
                candidateHeight = firstHeight;
            }
            else
            {
                candidateStart = thirdStart;
                candidateWidth = thirdWidth;
                candidateHeight = thirdHeight;
            }

            AddObstacle(
                FindTimeAfterDistance(
                    startTime,
                    candidateStart + candidateWidth * random.Range(0.42f, 0.62f)
                ),
                random.Next01() < (hardMode ? 0.24f : 0.10f) ? 2 : 1,
                candidateHeight
            );
        }

        return FindTimeAfterDistance(startTime, totalWidth);
    }

    private float ChoosePatternGap(ref DeterministicRandom random)
    {
        float min = _settings.minGapSeconds;
        float max = _settings.maxGapSeconds;
        float range = Mathf.Max(0f, max - min);
        float roll = random.Next01();
        float gap;

        // Four clearly separated spacing bands produce recognisably different
        // rhythms: quick follow-ups, normal gaps, long preparation windows, and
        // occasional dramatic pauses.
        if (roll < 0.20f)
        {
            gap = random.Range(min, min + range * 0.18f);
        }
        else if (roll < 0.55f)
        {
            gap = random.Range(min + range * 0.26f, min + range * 0.50f);
        }
        else if (roll < 0.86f)
        {
            gap = random.Range(min + range * 0.58f, min + range * 0.80f);
        }
        else
        {
            gap = random.Range(min + range * 0.88f, max);
        }

        if (random.Next01() < _settings.longGapChance)
            gap *= _settings.longGapMultiplier;

        return gap;
    }

    private int ChooseStackCount(ref DeterministicRandom random)
    {
        return random.Next01() < _settings.doubleStackChance ? 2 : 1;
    }

    private void AddObstacle(
        float hitTime,
        int stackCount,
        float baseHeight
    )
    {
        float blockSize = GetObstacleBlockSize();
        float width = blockSize;
        float height =
            blockSize * stackCount
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
                BaseHeight = baseHeight,
                StackCount = Mathf.Max(1, stackCount),
                Rect = obstacleRect,
            }
        );
    }

    private void AddPlatform(
        float hitTime,
        float width,
        float topHeight
    )
    {
        float snappedWidth = SnapTrackWidthToWholeTiles(
            width,
            minimumTiles: 2,
            expand: false
        );
        float thickness = Mathf.Max(
            24f,
            GroundVisualHeight * platformThicknessMultiplier
        );
        RectTransform rect = CreatePlatformView(
            _platforms.Count,
            snappedWidth,
            thickness,
            out RawImage platformTrack
        );

        _platforms.Add(
            new PlatformRuntime
            {
                HitTime = hitTime,
                Width = snappedWidth,
                TopHeight = topHeight,
                Rect = rect,
                Track = platformTrack,
            }
        );
    }

    private void AddGap(float hitTime, float width)
    {
        float tileSize = GetTrackTileSize();
        float centerDistance = GetCumulativeTravelDistance(hitTime);
        float halfWidth = Mathf.Max(1f, width) * 0.5f;
        float start = Mathf.Floor((centerDistance - halfWidth) / tileSize)
            * tileSize;
        float end = Mathf.Ceil((centerDistance + halfWidth) / tileSize)
            * tileSize;

        _gaps.Add(
            new GapRuntime
            {
                CenterDistance = (start + end) * 0.5f,
                Width = Mathf.Max(tileSize, end - start),
            }
        );
    }

    private void BuildGroundSegments()
    {
        for (int i = 0; i < _groundSegments.Count; i++)
        {
            if (_groundSegments[i].Rect != null)
                Destroy(_groundSegments[i].Rect.gameObject);
        }

        _groundSegments.Clear();

        float areaWidth = _gameArea != null && _gameArea.rect.width > 1f
            ? _gameArea.rect.width
            : 1920f;
        float viewportPadding = Mathf.Max(areaWidth * 1.25f, 1600f);
        float courseEndDistance = GetCumulativeTravelDistance(
            _settings.courseSeconds
        );
        float tileSize = GetTrackTileSize();
        float worldStart = Mathf.Floor(-viewportPadding / tileSize) * tileSize;
        float worldEnd = Mathf.Ceil(
            (courseEndDistance + viewportPadding) / tileSize
        ) * tileSize;

        var intervals = new List<Vector2>(_gaps.Count);

        for (int i = 0; i < _gaps.Count; i++)
        {
            GapRuntime gap = _gaps[i];
            float halfWidth = gap.Width * 0.5f;
            float start = Mathf.Max(
                worldStart,
                gap.CenterDistance - halfWidth
            );
            float end = Mathf.Min(
                worldEnd,
                gap.CenterDistance + halfWidth
            );

            if (end > start)
                intervals.Add(new Vector2(start, end));
        }

        intervals.Sort((a, b) => a.x.CompareTo(b.x));

        float cursor = worldStart;

        for (int i = 0; i < intervals.Count; i++)
        {
            Vector2 interval = intervals[i];
            float gapStart = Mathf.Max(cursor, interval.x);

            if (gapStart > cursor + 0.5f)
                AddGroundSegment(cursor, gapStart);

            cursor = Mathf.Max(cursor, interval.y);

            if (cursor >= worldEnd)
                break;
        }

        if (cursor < worldEnd - 0.5f)
            AddGroundSegment(cursor, worldEnd);
    }

    private void AddGroundSegment(float startDistance, float endDistance)
    {
        float width = endDistance - startDistance;

        if (width <= 0.5f)
            return;

        RawImage track = CreateGroundSegmentView(_groundSegments.Count, width);
        RectTransform rect = track.rectTransform;

        _groundSegments.Add(
            new GroundSegmentRuntime
            {
                CenterDistance = (startDistance + endDistance) * 0.5f,
                Width = width,
                Rect = rect,
                Track = track,
            }
        );
    }

    private float FindTimeAfterDistance(float startTime, float distance)
    {
        if (distance <= 0f)
            return startTime;

        float low = startTime;
        float high = startTime + 3f;

        for (int i = 0; i < 18; i++)
        {
            float mid = (low + high) * 0.5f;
            float travelled = GetTravelDistance(startTime, mid);

            if (travelled < distance)
                low = mid;
            else
                high = mid;
        }

        return (low + high) * 0.5f;
    }

    private void UpdatePlayingVisuals()
    {
        if (_gameArea == null)
            return;

        float areaWidth = _gameArea.rect.width;
        float groundY = GetGroundY();
        float playerX = GetPlayerX();

        float visualScale = GetGameplayVisualScale();
        float currentDistance = GetCumulativeTravelDistance(_elapsed);
        float trackSize = GetTrackTileSize();
        float trackEdgePadding = GetHollowVisualEdgePadding(trackSize);

        for (int i = 0; i < _groundSegments.Count; i++)
        {
            GroundSegmentRuntime segment = _groundSegments[i];

            if (segment.Rect == null)
                continue;

            float x = playerX + segment.CenterDistance - currentDistance;
            bool visible =
                x + segment.Width * 0.5f >= -areaWidth * 0.5f - trackSize
                && x - segment.Width * 0.5f <= areaWidth * 0.5f + trackSize;

            if (segment.Rect.gameObject.activeSelf != visible)
                segment.Rect.gameObject.SetActive(visible);

            if (!visible)
                continue;

            segment.Rect.anchoredPosition = new Vector2(
                x,
                groundY - trackSize * 0.5f + trackEdgePadding
            );
        }

        if (_packetRect != null)
        {
            float packetSize = GetPacketVisualSize();
            float edgePadding = GetHollowVisualEdgePadding(packetSize);
            _packetRect.sizeDelta = new Vector2(packetSize, packetSize);
            _packetRect.anchoredPosition = new Vector2(
                playerX,
                groundY
                    + _verticalPosition * visualScale
                    + packetSize * 0.5f
                    - edgePadding
                    + packetGroundContactOffset
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

            float x = GetElementX(obstacle.HitTime);
            bool visible =
                x + obstacle.Width * 0.5f >= -areaWidth * 0.5f - 100f
                && x - obstacle.Width * 0.5f <= areaWidth * 0.5f + 100f;

            if (obstacle.Rect.gameObject.activeSelf != visible)
                obstacle.Rect.gameObject.SetActive(visible);

            if (!visible)
                continue;

            obstacle.Rect.sizeDelta = new Vector2(
                obstacle.Width,
                obstacle.Height
            );
            obstacle.Rect.anchoredPosition = new Vector2(
                x,
                groundY
                    + obstacle.BaseHeight
                    + obstacleGroundClearance
                    + obstacle.Height * 0.5f
            );
        }

        for (int i = 0; i < _platforms.Count; i++)
        {
            PlatformRuntime platform = _platforms[i];

            if (platform.Rect == null)
                continue;

            float x = GetElementX(platform.HitTime);
            bool visible =
                x + platform.Width * 0.5f >= -areaWidth * 0.5f - 100f
                && x - platform.Width * 0.5f <= areaWidth * 0.5f + 100f;

            if (platform.Rect.gameObject.activeSelf != visible)
                platform.Rect.gameObject.SetActive(visible);

            if (!visible)
                continue;

            UpdateHollowStrip(platform.Track, platform.Width);

            float edgePadding = GetHollowVisualEdgePadding(trackSize);
            platform.Rect.anchoredPosition = new Vector2(
                x,
                groundY
                    + platform.TopHeight
                    - trackSize * 0.5f
                    + edgePadding
            );
        }

        float progress = Mathf.Clamp01(_elapsed / _settings.courseSeconds);
        int percent = Mathf.RoundToInt(progress * 100f);

        if (percent != _lastShownPercent)
        {
            _lastShownPercent = percent;
            SetStatusText(BuildProgressText(percent));
            _shell?.SetProgress(progress);
        }
    }

    private static string BuildProgressText(int percent)
    {
        int clamped = Mathf.Clamp(percent, 0, 100);
        return $"ROUTE {clamped:00}%";
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

        var palette = new LaptopMinigameShellPalette(
            backgroundColor,
            surfaceColor,
            raisedSurfaceColor,
            structureColor,
            accentColor,
            objectiveColor,
            textColor,
            mutedTextColor,
            obstacleColor
        );

        _shell = LaptopMinigameVisualShell.Build(
            _root,
            terminalFont,
            palette,
            shellName,
            "intrusion-suite",
            "FIREWALL RUNNER",
            "Route an intrusion packet through active corporate security barriers.",
            "SURVIVE UNTIL THE ROUTE REACHES 100%",
            "SPACE  /  JUMP",
            "SURVIVE UNTIL ROUTE COMPLETION"
        );

        _gameArea = _shell.GameArea;

        _groundLayer = CreateRect("GroundLayer", _gameArea);
        Stretch(_groundLayer, Vector2.zero, Vector2.one);

        _platformLayer = CreateRect("PlatformLayer", _gameArea);
        Stretch(_platformLayer, Vector2.zero, Vector2.one);

        _obstacleLayer = CreateRect("ObstacleLayer", _gameArea);
        Stretch(_obstacleLayer, Vector2.zero, Vector2.one);

        _packetImage = CreateHollowSquareGraphic("PlayerPacket", _gameArea);
        _packetRect = _packetImage.rectTransform;
        float packetSize = GetPacketVisualSize();
        SetCenteredRect(_packetRect, new Vector2(packetSize, packetSize));
        _packetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
    }

    private RectTransform CreateObstacleView(int index, int stackCount)
    {
        RectTransform root = CreateRect($"Firewall_{index:00}", _obstacleLayer);
        float visualBlockSize = GetObstacleBlockSize();
        float visualStackGap = obstacleStackGap;
        float totalHeight =
            visualBlockSize * stackCount
            + visualStackGap * Mathf.Max(0, stackCount - 1);
        SetCenteredRect(root, new Vector2(visualBlockSize, totalHeight));

        for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
        {
            TMP_Text block = CreateText(
                $"Block_{stackIndex + 1}",
                root,
                obstacleGlyph,
                visualBlockSize * 1.22f,
                TextAlignmentOptions.Center,
                obstacleColor,
                FontStyles.Normal
            );
            RectTransform blockRect = block.rectTransform;
            SetCenteredRect(
                blockRect,
                new Vector2(visualBlockSize, visualBlockSize)
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

    private RawImage CreateGroundSegmentView(int index, float width)
    {
        RawImage track = CreateHollowSquareGraphic(
            $"GroundSegment_{index:00}",
            _groundLayer
        );
        track.color = structureColor;
        RectTransform rect = track.rectTransform;
        SetCenteredRect(rect, new Vector2(width, GetTrackTileSize()));
        UpdateHollowStrip(track, width);
        return track;
    }

    private RectTransform CreatePlatformView(
        int index,
        float width,
        float thickness,
        out RawImage platformTrack
    )
    {
        platformTrack = CreateHollowSquareGraphic(
            $"SafePlatform_{index:00}",
            _platformLayer
        );
        platformTrack.color = structureColor;
        RectTransform rect = platformTrack.rectTransform;
        SetCenteredRect(rect, new Vector2(width, GetTrackTileSize()));
        UpdateHollowStrip(platformTrack, width);
        return rect;
    }

    private RawImage CreateHollowSquareGraphic(
        string objectName,
        Transform parent
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        RawImage image = gameObject.GetComponent<RawImage>();
        image.texture = GetOrCreateHollowSquareTexture();
        image.color = accentColor;
        image.raycastTarget = false;
        image.uvRect = new Rect(0f, 0f, 1f, 1f);
        return image;
    }

    private Texture2D GetOrCreateHollowSquareTexture()
    {
        if (_hollowSquareTexture != null)
            return _hollowSquareTexture;

        int resolution = HollowTextureResolution;
        _hollowSquareTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            true
        )
        {
            name = "FirewallRunner_HollowSquare",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave,
        };

        float tileSize = GetTrackTileSize();
        int edgePadding = Mathf.Clamp(
            Mathf.RoundToInt(
                resolution
                * Mathf.Max(0f, floorGlyphGap)
                / Mathf.Max(1f, tileSize)
                * 0.5f
            ),
            0,
            resolution / 6
        );
        int border = Mathf.Clamp(
            HollowTextureBorderPixels,
            2,
            resolution / 4
        );

        Color32 transparent = new(255, 255, 255, 0);
        Color32 solid = new(255, 255, 255, 255);
        var pixels = new Color32[resolution * resolution];

        int min = edgePadding;
        int max = resolution - 1 - edgePadding;
        int innerMin = min + border;
        int innerMax = max - border;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                bool insideOuter = x >= min && x <= max && y >= min && y <= max;
                bool insideInner = x >= innerMin && x <= innerMax && y >= innerMin && y <= innerMax;
                pixels[y * resolution + x] = insideOuter && !insideInner
                    ? solid
                    : transparent;
            }
        }

        _hollowSquareTexture.SetPixels32(pixels);
        _hollowSquareTexture.Apply(false, true);
        return _hollowSquareTexture;
    }

    private void UpdateHollowStrip(RawImage image, float width)
    {
        if (image == null)
            return;

        float tileSize = GetTrackTileSize();
        float safeWidth = SnapTrackWidthToWholeTiles(
            width,
            minimumTiles: 1,
            expand: false
        );
        int tileCount = Mathf.Max(
            1,
            Mathf.RoundToInt(safeWidth / tileSize)
        );
        RectTransform rect = image.rectTransform;
        rect.sizeDelta = new Vector2(tileCount * tileSize, tileSize);
        image.uvRect = new Rect(0f, 0f, tileCount, 1f);
    }

    private float SnapTrackWidthToWholeTiles(
        float width,
        int minimumTiles,
        bool expand
    )
    {
        float tileSize = GetTrackTileSize();
        float safeWidth = Mathf.Max(tileSize, width);
        int tileCount = expand
            ? Mathf.CeilToInt(safeWidth / tileSize)
            : Mathf.RoundToInt(safeWidth / tileSize);
        tileCount = Mathf.Max(Mathf.Max(1, minimumTiles), tileCount);
        return tileCount * tileSize;
    }

    private float GetTrackTileSize()
    {
        return Mathf.Clamp(floorGlyphVisualSize, 36f, 96f);
    }

    private float GetPacketVisualSize()
    {
        return Mathf.Min(
            Mathf.Clamp(packetGlyphVisualSize, 36f, 120f),
            Mathf.Max(1f, GetPlayerCollisionSize())
        );
    }

    private float GetHollowVisualEdgePadding(float visualSize)
    {
        return Mathf.Max(0f, floorGlyphGap) * 0.5f;
    }

    private void ClearCourseViews()
    {
        for (int i = 0; i < _obstacles.Count; i++)
        {
            if (_obstacles[i].Rect != null)
                Destroy(_obstacles[i].Rect.gameObject);
        }

        for (int i = 0; i < _groundSegments.Count; i++)
        {
            if (_groundSegments[i].Rect != null)
                Destroy(_groundSegments[i].Rect.gameObject);
        }

        for (int i = 0; i < _platforms.Count; i++)
        {
            if (_platforms[i].Rect != null)
                Destroy(_platforms[i].Rect.gameObject);
        }

        _obstacles.Clear();
        _groundSegments.Clear();
        _platforms.Clear();
        _gaps.Clear();
    }

    private void ShowResultOverlay(string message, Color color)
    {
        _shell?.ShowResult(message, color);
    }

    private void SetStatusText(string value)
    {
        _shell?.SetStatus(value, textColor);
    }

    private void SetPacketColor(Color color)
    {
        if (_packetImage == null)
            return;

        _packetImage.color = color;
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

        return text;
    }

    private TMP_Text CreateDashSeparator(string objectName, float normalizedY)
    {
        string dividerText = new string('-', 320);
        Color dividerColor = WithAlpha(mutedTextColor, 0.92f);
        float dividerFontSize = 36f;
        float verticalOffset = Mathf.Max(1.5f, separatorThickness * 0.22f);

        TMP_Text lower = CreateText(
            $"{objectName}_Lower",
            _root,
            dividerText,
            dividerFontSize,
            TextAlignmentOptions.Center,
            dividerColor,
            FontStyles.Bold
        );
        ConfigureDashSeparatorRect(lower.rectTransform, normalizedY, -verticalOffset);
        lower.characterSpacing = -1.5f;
        lower.overflowMode = TextOverflowModes.Masking;
        lower.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_Text upper = CreateText(
            objectName,
            _root,
            dividerText,
            dividerFontSize,
            TextAlignmentOptions.Center,
            dividerColor,
            FontStyles.Bold
        );
        ConfigureDashSeparatorRect(upper.rectTransform, normalizedY, verticalOffset);
        upper.characterSpacing = -1.5f;
        upper.overflowMode = TextOverflowModes.Masking;
        upper.textWrappingMode = TextWrappingModes.NoWrap;
        return upper;
    }

    private void ConfigureDashSeparatorRect(
        RectTransform rect,
        float normalizedY,
        float verticalOffset
    )
    {
        rect.anchorMin = new Vector2(0f, normalizedY);
        rect.anchorMax = new Vector2(1f, normalizedY);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(
            0f,
            Mathf.Max(54f, separatorThickness * 7f)
        );
        rect.anchoredPosition = new Vector2(0f, verticalOffset);
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
        settings.endSpeedMultiplier = Mathf.Clamp(settings.endSpeedMultiplier, 1f, 2f);
        settings.firstObstacleSeconds = Mathf.Max(0.5f, settings.firstObstacleSeconds);
        settings.minGapSeconds = Mathf.Max(0.5f, settings.minGapSeconds);
        settings.maxGapSeconds = Mathf.Max(
            settings.minGapSeconds,
            settings.maxGapSeconds
        );
        settings.longGapChance = Mathf.Clamp01(settings.longGapChance);
        settings.longGapMultiplier = Mathf.Clamp(settings.longGapMultiplier, 1f, 2f);
        settings.doubleStackChance = Mathf.Clamp01(settings.doubleStackChance);
        settings.tripleStackChance = 0f;
        settings.clusteredObstacleChance = Mathf.Clamp01(
            settings.clusteredObstacleChance
        );
        settings.minClusterSpacingSeconds = Mathf.Max(
            0.08f,
            settings.minClusterSpacingSeconds
        );
        settings.maxClusterSpacingSeconds = Mathf.Max(
            settings.minClusterSpacingSeconds,
            settings.maxClusterSpacingSeconds
        );
        settings.thirdClusterObstacleChance = Mathf.Clamp01(
            settings.thirdClusterObstacleChance
        );
        settings.floorGapChance = Mathf.Clamp01(settings.floorGapChance);
        settings.platformPatternChance = Mathf.Clamp01(
            settings.platformPatternChance
        );

        float specialPatternChance =
            settings.floorGapChance + settings.platformPatternChance;

        if (specialPatternChance > 0.72f)
        {
            float scale = 0.72f / specialPatternChance;
            settings.floorGapChance *= scale;
            settings.platformPatternChance *= scale;
        }

        settings.doubleStackChance = Mathf.Min(
            settings.doubleStackChance,
            0.90f
        );
    }

    private void EnsureDifficultySettingsExist()
    {
        easy ??= new CourseSettings
        {
            courseSeconds = 26f,
            scrollSpeed = 650f,
            endSpeedMultiplier = 1.42f,
            firstObstacleSeconds = 2.25f,
            minGapSeconds = 0.82f,
            maxGapSeconds = 3.15f,
            longGapChance = 0.16f,
            longGapMultiplier = 1.42f,
            doubleStackChance = 0.30f,
            tripleStackChance = 0f,
            clusteredObstacleChance = 0.48f,
            minClusterSpacingSeconds = 0.74f,
            maxClusterSpacingSeconds = 1.02f,
            thirdClusterObstacleChance = 0.10f,
            floorGapChance = 0.17f,
            platformPatternChance = 0.22f,
        };

        hard ??= new CourseSettings
        {
            courseSeconds = 34f,
            scrollSpeed = 860f,
            endSpeedMultiplier = 1.58f,
            firstObstacleSeconds = 2.00f,
            minGapSeconds = 0.68f,
            maxGapSeconds = 2.85f,
            longGapChance = 0.13f,
            longGapMultiplier = 1.38f,
            doubleStackChance = 0.46f,
            tripleStackChance = 0f,
            clusteredObstacleChance = 0.68f,
            minClusterSpacingSeconds = 0.70f,
            maxClusterSpacingSeconds = 0.98f,
            thirdClusterObstacleChance = 0.24f,
            floorGapChance = 0.20f,
            platformPatternChance = 0.31f,
        };
    }

    private float GetPlayerCollisionSize()
    {
        float logicalSize = Mathf.Min(playerWidth, playerHeight);
        float inset = Mathf.Clamp(collisionInset, 0f, logicalSize * 0.45f);
        return Mathf.Max(1f, logicalSize - inset * 2f);
    }

    private float GetPlayerPlatformSupportHalfWidth()
    {
        // Platform support uses the complete visible/logical packet width rather
        // than the smaller obstacle hitbox. Any genuine edge overlap therefore
        // counts as a landing and remains supported instead of slipping through.
        float fullPacketWidth = Mathf.Max(
            GetPacketVisualSize(),
            Mathf.Min(playerWidth, playerHeight)
        );
        return fullPacketWidth * 0.5f + platformLandingHorizontalGrace;
    }

    private float GetObstacleBlockSize()
    {
        // The square glyph has generous internal font padding. Make its logical
        // tile larger than the player cell so the visible red square reads as a
        // proper hazard, while collision still matches the displayed tile.
        return Mathf.Max(
            64f,
            Mathf.Min(playerWidth, playerHeight) * obstacleSizeMultiplier
        );
    }

    private float GetTravelDistance(float fromTime, float toTime)
    {
        return GetCumulativeTravelDistance(toTime)
            - GetCumulativeTravelDistance(fromTime);
    }

    private float GetCumulativeTravelDistance(float time)
    {
        if (_settings == null)
            return 0f;

        float courseDuration = Mathf.Max(0.01f, _settings.courseSeconds);
        float startSpeed = Mathf.Max(100f, _settings.scrollSpeed);
        float endMultiplier = Mathf.Clamp(_settings.endSpeedMultiplier, 1f, 2f);
        float clampedTime = Mathf.Clamp(time, 0f, courseDuration);
        float acceleration = startSpeed * (endMultiplier - 1f) / courseDuration;
        float distance =
            startSpeed * clampedTime
            + 0.5f * acceleration * clampedTime * clampedTime;

        if (time > courseDuration)
            distance += startSpeed * endMultiplier * (time - courseDuration);
        else if (time < 0f)
            distance += startSpeed * time;

        return distance;
    }

    private float GetGameplayVisualScale()
    {
        return gameplayVisualScale > 0f ? gameplayVisualScale : 1.45f;
    }

    private void ApplyVersionedTuning()
    {
        if (tuningVersion >= CurrentTuningVersion)
            return;

        // Preserve custom Inspector tuning. Only migrate values that still
        // match the previous shipped defaults.
        if (easy != null)
        {
            if (Mathf.Approximately(easy.courseSeconds, 14f))
                easy.courseSeconds = 15f;
            if (Mathf.Approximately(easy.scrollSpeed, 435f))
                easy.scrollSpeed = 470f;
            if (Mathf.Approximately(easy.firstObstacleSeconds, 2.2f))
                easy.firstObstacleSeconds = 2.0f;
            if (Mathf.Approximately(easy.minGapSeconds, 1.45f))
                easy.minGapSeconds = 1.32f;
            if (Mathf.Approximately(easy.maxGapSeconds, 2.00f))
                easy.maxGapSeconds = 1.82f;
            if (Mathf.Approximately(easy.doubleStackChance, 0.26f))
                easy.doubleStackChance = 0.30f;
            if (Mathf.Approximately(easy.tripleStackChance, 0.08f))
                easy.tripleStackChance = 0.11f;
        }

        if (hard != null)
        {
            if (Mathf.Approximately(hard.courseSeconds, 18f))
                hard.courseSeconds = 19f;
            if (Mathf.Approximately(hard.scrollSpeed, 555f))
                hard.scrollSpeed = 600f;
            if (Mathf.Approximately(hard.firstObstacleSeconds, 1.95f))
                hard.firstObstacleSeconds = 1.75f;
            if (Mathf.Approximately(hard.minGapSeconds, 1.08f))
                hard.minGapSeconds = 1.00f;
            if (Mathf.Approximately(hard.maxGapSeconds, 1.55f))
                hard.maxGapSeconds = 1.40f;
            if (Mathf.Approximately(hard.doubleStackChance, 0.34f))
                hard.doubleStackChance = 0.38f;
            if (Mathf.Approximately(hard.tripleStackChance, 0.18f))
                hard.tripleStackChance = 0.22f;
        }

        if (tuningVersion < 2)
        {
            // Replace the wide two-cell obstacle with one true square terminal
            // glyph. Preserve any custom Inspector glyph.
            if (
                string.IsNullOrWhiteSpace(obstacleGlyph)
                || obstacleGlyph == "[#]"
                || obstacleGlyph == "#"
                || obstacleGlyph == "█"
                || obstacleGlyph == "██"
            )
                obstacleGlyph = "■";

            if (Mathf.Approximately(obstacleGroundClearance, 0f))
                obstacleGroundClearance = 10f;
        }

        if (tuningVersion < 3)
        {
            // Lengthen both courses and ramp speed throughout the run. Only
            // migrate the previously shipped defaults so custom Inspector
            // tuning remains untouched.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.courseSeconds, 15f))
                    easy.courseSeconds = 24f;
                easy.endSpeedMultiplier = 1.28f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.courseSeconds, 19f))
                    hard.courseSeconds = 30f;
                hard.endSpeedMultiplier = 1.35f;
            }

            obstacleSizeMultiplier = 1.30f;

            if (Mathf.Approximately(obstacleGroundClearance, 10f))
                obstacleGroundClearance = 4f;
        }

        if (tuningVersion < 4)
        {
            // Break up the old constant jump rhythm: faster travel, a much
            // broader timing range, and occasional extra-long breather gaps.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.scrollSpeed, 470f))
                    easy.scrollSpeed = 540f;
                if (Mathf.Approximately(easy.endSpeedMultiplier, 1.28f))
                    easy.endSpeedMultiplier = 1.32f;
                if (Mathf.Approximately(easy.firstObstacleSeconds, 2.0f))
                    easy.firstObstacleSeconds = 1.75f;
                if (Mathf.Approximately(easy.minGapSeconds, 1.32f))
                    easy.minGapSeconds = 1.40f;
                if (Mathf.Approximately(easy.maxGapSeconds, 1.82f))
                    easy.maxGapSeconds = 2.45f;
                easy.longGapChance = 0.28f;
                easy.longGapMultiplier = 1.35f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.scrollSpeed, 600f))
                    hard.scrollSpeed = 700f;
                if (Mathf.Approximately(hard.endSpeedMultiplier, 1.35f))
                    hard.endSpeedMultiplier = 1.42f;
                if (Mathf.Approximately(hard.firstObstacleSeconds, 1.75f))
                    hard.firstObstacleSeconds = 1.55f;
                if (Mathf.Approximately(hard.minGapSeconds, 1.00f))
                    hard.minGapSeconds = 1.15f;
                if (Mathf.Approximately(hard.maxGapSeconds, 1.40f))
                    hard.maxGapSeconds = 2.15f;
                hard.longGapChance = 0.22f;
                hard.longGapMultiplier = 1.30f;
            }

            if (Mathf.Approximately(jumpVelocity, 1225f))
                jumpVelocity = 1500f;
            if (Mathf.Approximately(gravity, 2200f))
                gravity = 3100f;
        }


        if (tuningVersion < 5)
        {
            // Turn Firewall Runner into a faster Geometry-Dash-style course.
            // Existing prefabs receive denser hazards, true floor gaps, and
            // elevated safe platforms without losing their assigned font.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.courseSeconds, 24f))
                    easy.courseSeconds = 26f;
                if (Mathf.Approximately(easy.scrollSpeed, 540f))
                    easy.scrollSpeed = 650f;
                if (Mathf.Approximately(easy.endSpeedMultiplier, 1.32f))
                    easy.endSpeedMultiplier = 1.42f;
                if (Mathf.Approximately(easy.firstObstacleSeconds, 1.75f))
                    easy.firstObstacleSeconds = 1.35f;
                if (Mathf.Approximately(easy.minGapSeconds, 1.40f))
                    easy.minGapSeconds = 0.90f;
                if (Mathf.Approximately(easy.maxGapSeconds, 2.45f))
                    easy.maxGapSeconds = 1.65f;
                if (Mathf.Approximately(easy.longGapChance, 0.28f))
                    easy.longGapChance = 0.16f;
                if (Mathf.Approximately(easy.longGapMultiplier, 1.35f))
                    easy.longGapMultiplier = 1.25f;
                if (Mathf.Approximately(easy.doubleStackChance, 0.30f))
                    easy.doubleStackChance = 0.38f;
                if (Mathf.Approximately(easy.tripleStackChance, 0.11f))
                    easy.tripleStackChance = 0.18f;
                if (easy.floorGapChance <= 0f)
                    easy.floorGapChance = 0.18f;
                if (easy.platformPatternChance <= 0f)
                    easy.platformPatternChance = 0.14f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.courseSeconds, 30f))
                    hard.courseSeconds = 34f;
                if (Mathf.Approximately(hard.scrollSpeed, 700f))
                    hard.scrollSpeed = 860f;
                if (Mathf.Approximately(hard.endSpeedMultiplier, 1.42f))
                    hard.endSpeedMultiplier = 1.58f;
                if (Mathf.Approximately(hard.firstObstacleSeconds, 1.55f))
                    hard.firstObstacleSeconds = 1.10f;
                if (Mathf.Approximately(hard.minGapSeconds, 1.15f))
                    hard.minGapSeconds = 0.72f;
                if (Mathf.Approximately(hard.maxGapSeconds, 2.15f))
                    hard.maxGapSeconds = 1.35f;
                if (Mathf.Approximately(hard.longGapChance, 0.22f))
                    hard.longGapChance = 0.10f;
                if (Mathf.Approximately(hard.longGapMultiplier, 1.30f))
                    hard.longGapMultiplier = 1.20f;
                if (Mathf.Approximately(hard.doubleStackChance, 0.38f))
                    hard.doubleStackChance = 0.42f;
                if (Mathf.Approximately(hard.tripleStackChance, 0.22f))
                    hard.tripleStackChance = 0.30f;
                if (hard.floorGapChance <= 0f)
                    hard.floorGapChance = 0.22f;
                if (hard.platformPatternChance <= 0f)
                    hard.platformPatternChance = 0.24f;
            }

            if (Mathf.Approximately(jumpVelocity, 1500f))
                jumpVelocity = 1750f;
            if (Mathf.Approximately(gravity, 3100f))
                gravity = 4300f;

            if (platformHeightInBlocks <= 0f)
                platformHeightInBlocks = 1.25f;
            if (platformThicknessMultiplier <= 0f)
                platformThicknessMultiplier = 0.55f;
            if (gapMaskExtraHeight <= 0f)
                gapMaskExtraHeight = 16f;
        }

        if (tuningVersion < 6)
        {
            // Give the player a readable opening, remove three-high stacks,
            // lower the jump, and replace metronomic spacing with clustered
            // two-obstacle patterns separated by varied breathing windows.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.firstObstacleSeconds, 1.35f))
                    easy.firstObstacleSeconds = 2.25f;
                if (Mathf.Approximately(easy.minGapSeconds, 0.90f))
                    easy.minGapSeconds = 1.20f;
                if (Mathf.Approximately(easy.maxGapSeconds, 1.65f))
                    easy.maxGapSeconds = 2.60f;
                if (Mathf.Approximately(easy.longGapChance, 0.16f))
                    easy.longGapChance = 0.20f;
                if (Mathf.Approximately(easy.longGapMultiplier, 1.25f))
                    easy.longGapMultiplier = 1.35f;
                if (Mathf.Approximately(easy.doubleStackChance, 0.38f))
                    easy.doubleStackChance = 0.28f;
                easy.tripleStackChance = 0f;
                easy.clusteredObstacleChance = 0.42f;
                easy.minClusterSpacingSeconds = 0.22f;
                easy.maxClusterSpacingSeconds = 0.34f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.firstObstacleSeconds, 1.10f))
                    hard.firstObstacleSeconds = 2.00f;
                if (Mathf.Approximately(hard.minGapSeconds, 0.72f))
                    hard.minGapSeconds = 1.00f;
                if (Mathf.Approximately(hard.maxGapSeconds, 1.35f))
                    hard.maxGapSeconds = 2.35f;
                if (Mathf.Approximately(hard.longGapChance, 0.10f))
                    hard.longGapChance = 0.16f;
                if (Mathf.Approximately(hard.longGapMultiplier, 1.20f))
                    hard.longGapMultiplier = 1.40f;
                hard.tripleStackChance = 0f;
                hard.clusteredObstacleChance = 0.62f;
                hard.minClusterSpacingSeconds = 0.17f;
                hard.maxClusterSpacingSeconds = 0.29f;
            }

            if (Mathf.Approximately(jumpVelocity, 1750f))
                jumpVelocity = 1275f;
            if (Mathf.Approximately(gravity, 4300f))
                gravity = 4400f;
        }


        if (tuningVersion < 7)
        {
            // Make lifted platforms visually identical to the floor, restore a
            // two-block-clearable jump, separate clustered hazards into rapid
            // landing-and-jump sequences, and add stepped platform variants.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.minGapSeconds, 1.20f))
                    easy.minGapSeconds = 0.82f;
                if (Mathf.Approximately(easy.maxGapSeconds, 2.60f))
                    easy.maxGapSeconds = 3.15f;
                if (Mathf.Approximately(easy.longGapChance, 0.20f))
                    easy.longGapChance = 0.16f;
                if (Mathf.Approximately(easy.longGapMultiplier, 1.35f))
                    easy.longGapMultiplier = 1.42f;
                if (Mathf.Approximately(easy.doubleStackChance, 0.28f))
                    easy.doubleStackChance = 0.30f;
                if (Mathf.Approximately(easy.clusteredObstacleChance, 0.42f))
                    easy.clusteredObstacleChance = 0.48f;
                if (Mathf.Approximately(easy.minClusterSpacingSeconds, 0.22f))
                    easy.minClusterSpacingSeconds = 0.74f;
                if (Mathf.Approximately(easy.maxClusterSpacingSeconds, 0.34f))
                    easy.maxClusterSpacingSeconds = 1.02f;
                easy.thirdClusterObstacleChance = 0.10f;
                if (Mathf.Approximately(easy.floorGapChance, 0.18f))
                    easy.floorGapChance = 0.17f;
                if (Mathf.Approximately(easy.platformPatternChance, 0.14f))
                    easy.platformPatternChance = 0.22f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.minGapSeconds, 1.00f))
                    hard.minGapSeconds = 0.68f;
                if (Mathf.Approximately(hard.maxGapSeconds, 2.35f))
                    hard.maxGapSeconds = 2.85f;
                if (Mathf.Approximately(hard.longGapChance, 0.16f))
                    hard.longGapChance = 0.13f;
                if (Mathf.Approximately(hard.longGapMultiplier, 1.40f))
                    hard.longGapMultiplier = 1.38f;
                if (Mathf.Approximately(hard.doubleStackChance, 0.42f))
                    hard.doubleStackChance = 0.46f;
                if (Mathf.Approximately(hard.clusteredObstacleChance, 0.62f))
                    hard.clusteredObstacleChance = 0.68f;
                if (Mathf.Approximately(hard.minClusterSpacingSeconds, 0.17f))
                    hard.minClusterSpacingSeconds = 0.70f;
                if (Mathf.Approximately(hard.maxClusterSpacingSeconds, 0.29f))
                    hard.maxClusterSpacingSeconds = 0.98f;
                hard.thirdClusterObstacleChance = 0.24f;
                if (Mathf.Approximately(hard.floorGapChance, 0.22f))
                    hard.floorGapChance = 0.20f;
                if (Mathf.Approximately(hard.platformPatternChance, 0.24f))
                    hard.platformPatternChance = 0.31f;
            }

            if (Mathf.Approximately(jumpVelocity, 1275f))
                jumpVelocity = 1450f;
            if (Mathf.Approximately(gravity, 4400f))
                gravity = 4800f;
            if (Mathf.Approximately(platformThicknessMultiplier, 0.55f))
                platformThicknessMultiplier = 1f;
        }

        if (tuningVersion < 8)
        {
            // V8 fixes oversized geometry patterns and makes the main floor
            // and lifted platforms use the same exact visual strip. Generation
            // ranges are code-driven, so no prefab reset is required.
            platformThicknessMultiplier = 1f;
        }

        if (tuningVersion < 9)
        {
            // V9 adds double holes, long bridges, taller stepped platforms,
            // multi-hazard platforms, and three-step staircase patterns.
            // A small jump increase keeps two-high platform hazards fair.
            if (Mathf.Approximately(jumpVelocity, 1450f))
                jumpVelocity = 1525f;
        }

        if (tuningVersion < 12)
        {
            // V12 removes all TMP glow, underlay halo, and UI graphic outlines.
            // Keep the shared hazard red identical to Trace Escape's pursuer.
            obstacleColor = new Color(1f, 0.24f, 0.38f, 1f);
        }

        if (tuningVersion < 13)
        {
            // V13 makes the packet collision box genuinely square and gives
            // platform landings a small amount of edge/crossing forgiveness.
            // Only migrate the old default inset so custom collision tuning is
            // preserved.
            if (Mathf.Approximately(collisionInset, 11f))
                collisionInset = 4f;

            if (platformLandingHorizontalGrace <= 0f)
                platformLandingHorizontalGrace = 10f;

            if (platformLandingVerticalTolerance <= 0f)
                platformLandingVerticalTolerance = 8f;
        }

        if (tuningVersion < 14)
        {
            // V14 lets the packet visibly fall into floor holes before failing,
            // ignores tiny edge touches, and makes ordinary holes slightly wider.
            if (groundSupportProbeWidthMultiplier <= 0f)
                groundSupportProbeWidthMultiplier = 0.18f;

            if (holeDeathClearance <= 0f)
                holeDeathClearance = 8f;

            if (floorGapWidthMultiplier <= 1f)
                floorGapWidthMultiplier = 1.12f;
        }

        if (tuningVersion < 15)
        {
            // V15 replaces the visually floating [>] packet and solid Image
            // tracks with properly aligned hollow terminal squares. The moving
            // packet, normal floor, and every platform now share the same glyph
            // family while red hazards remain solid squares.
            if (string.IsNullOrWhiteSpace(packetGlyph) || packetGlyph == "[>]")
                packetGlyph = "□";

            if (string.IsNullOrWhiteSpace(floorGlyph))
                floorGlyph = "□";

            if (floorGlyphFontSize <= 0f)
                floorGlyphFontSize = 72f;

            if (floorGlyphCellWidthEm <= 0f)
                floorGlyphCellWidthEm = 0.82f;

            if (packetGlyphFontSize <= 0f)
                packetGlyphFontSize = 78f;
        }


        if (tuningVersion < 16)
        {
            // V16 sizes the rendered hollow-square geometry from TMP's measured
            // bounds instead of relying on font point size. This makes the
            // packet, ground and every elevated platform large, square and
            // visually consistent with Iosevka Term Mono.
            if (floorGlyphVisualSize <= 0f)
                floorGlyphVisualSize = 58f;
            if (floorGlyphGap < 0f)
                floorGlyphGap = 3f;
            if (packetGlyphVisualSize <= 0f)
                packetGlyphVisualSize = 76f;
            if (Mathf.Approximately(packetGroundContactOffset, 0f))
                packetGroundContactOffset = -2f;
            if (Mathf.Approximately(packetGlyphFontSize, 78f))
                packetGlyphFontSize = 120f;
        }


        if (tuningVersion < 17)
        {
            // U+25A1 is a small geometric symbol inside a large font em box.
            // Replacing the TMP strip with a procedural tiled hollow square makes
            // the visible packet/floor/platform dimensions exact and identical.
            if (Mathf.Approximately(floorGlyphVisualSize, 58f))
                floorGlyphVisualSize = 68f;

            if (Mathf.Approximately(packetGlyphVisualSize, 76f))
                packetGlyphVisualSize = 82f;

            if (Mathf.Approximately(packetGroundContactOffset, -2f))
                packetGroundContactOffset = 0f;
        }

        if (tuningVersion < 18)
        {
            // V18 separates the packet from the environment by moving the floor
            // and platforms to the muted terminal color. Gap masks now extend
            // above and beyond each opening so the tiled track cannot leave a
            // one-pixel seam that TAA smears across the void.
            if (gapMaskHorizontalOverscan <= 0f)
                gapMaskHorizontalOverscan = 6f;

            if (gapMaskTopOverscan <= 0f)
                gapMaskTopOverscan = 4f;
        }

        if (tuningVersion < 19)
        {
            // V19 replaces the continuous masked floor with finite moving ground
            // segments. Holes are now actual empty spaces between track objects,
            // so there is no overlay edge for TAA to reveal or smear.
        }

        if (tuningVersion < 20)
        {
            // V20 aligns every hole boundary, ground run, and platform width to
            // whole hollow-square tiles. Repeating RawImages therefore never end
            // on a partial tile, eliminating clipped squares at platform and gap
            // edges while preserving the real moving-floor implementation.
        }

        if (tuningVersion < 21)
        {
            // V21 matches firewall collision to the visible solid-square glyph,
            // makes edge platform landings intentionally forgiving, and removes
            // same-platform double hazards that could generate impossible jumps.
            if (obstacleCollisionScale <= 0f || Mathf.Approximately(obstacleCollisionScale, 1f))
                obstacleCollisionScale = 0.72f;

            if (Mathf.Approximately(platformLandingHorizontalGrace, 10f))
                platformLandingHorizontalGrace = 18f;
        }

        if (tuningVersion < 22)
        {
            // V22 introduces the hacker-owned SABLE desktop shell, a compact
            // game-first layout, and the original purple/orange faction palette.
            backgroundColor = new Color(0.071f, 0.043f, 0.094f, 1f);
            surfaceColor = new Color(0.129f, 0.075f, 0.161f, 1f);
            raisedSurfaceColor = new Color(0.161f, 0.090f, 0.192f, 1f);
            structureColor = new Color(0.541f, 0.365f, 0.659f, 1f);
            accentColor = new Color(0.929f, 0.569f, 0.259f, 1f);
            objectiveColor = new Color(0.949f, 0.737f, 0.400f, 1f);
            textColor = new Color(0.933f, 0.910f, 0.894f, 1f);
            mutedTextColor = new Color(0.663f, 0.608f, 0.686f, 1f);
            obstacleColor = new Color(0.875f, 0.251f, 0.361f, 1f);

            if (string.IsNullOrWhiteSpace(shellName))
                shellName = "SABLE";
        }

        if (tuningVersion < 23)
        {
            // V23 refines the shell for readability on the in-world laptop,
            // removes the heavy purple cast, and switches to a cooler slate + warm
            // amber palette that complements the blue environment better.
            backgroundColor = new Color(0.043f, 0.059f, 0.090f, 1f);
            surfaceColor = new Color(0.067f, 0.090f, 0.129f, 1f);
            raisedSurfaceColor = new Color(0.094f, 0.125f, 0.180f, 1f);
            structureColor = new Color(0.255f, 0.333f, 0.439f, 1f);
            accentColor = new Color(0.941f, 0.604f, 0.290f, 1f);
            objectiveColor = new Color(0.949f, 0.792f, 0.486f, 1f);
            textColor = new Color(0.949f, 0.945f, 0.925f, 1f);
            mutedTextColor = new Color(0.576f, 0.631f, 0.694f, 1f);
            obstacleColor = new Color(0.890f, 0.325f, 0.431f, 1f);

            if (string.IsNullOrWhiteSpace(shellName))
                shellName = "SABLE";
        }

        if (tuningVersion < 24)
        {
            // V24 increases shell readability again, renames the OS to ZannOS,
            // shifts the palette a bit more toward cyan-blue, and refreshes the
            // Firewall Runner visuals so the laptop sits better inside the scene.
            backgroundColor = new Color(0.030f, 0.053f, 0.082f, 1f);
            surfaceColor = new Color(0.050f, 0.082f, 0.118f, 1f);
            raisedSurfaceColor = new Color(0.073f, 0.112f, 0.157f, 1f);
            structureColor = new Color(0.290f, 0.443f, 0.565f, 1f);
            accentColor = new Color(0.965f, 0.620f, 0.302f, 1f);
            objectiveColor = new Color(0.980f, 0.824f, 0.518f, 1f);
            textColor = new Color(0.960f, 0.954f, 0.935f, 1f);
            mutedTextColor = new Color(0.615f, 0.702f, 0.765f, 1f);
            obstacleColor = new Color(0.920f, 0.341f, 0.439f, 1f);
            shellName = "ZannOS";
        }


        if (tuningVersion < 25)
        {
            // V25 deepens the blue-cyan shell background and raises the contrast of
            // gameplay elements so the in-world laptop screen reads more clearly.
            backgroundColor = new Color(0.017f, 0.045f, 0.072f, 1f);
            surfaceColor = new Color(0.026f, 0.065f, 0.098f, 1f);
            raisedSurfaceColor = new Color(0.041f, 0.087f, 0.128f, 1f);
            structureColor = new Color(0.430f, 0.655f, 0.812f, 1f);
            accentColor = new Color(0.992f, 0.663f, 0.286f, 1f);
            objectiveColor = new Color(1.000f, 0.839f, 0.486f, 1f);
            textColor = new Color(0.972f, 0.968f, 0.949f, 1f);
            mutedTextColor = new Color(0.635f, 0.745f, 0.820f, 1f);
            obstacleColor = new Color(0.949f, 0.365f, 0.455f, 1f);
            shellName = "ZannOS";
        }


        if (tuningVersion < 26)
        {
            // V26 shifts the shell slightly further toward cyan and strengthens gameplay
            // contrast so Firewall Runner reads more clearly on the in-world laptop.
            backgroundColor = new Color(0.010f, 0.048f, 0.076f, 1f);
            surfaceColor = new Color(0.018f, 0.066f, 0.102f, 1f);
            raisedSurfaceColor = new Color(0.030f, 0.088f, 0.133f, 1f);
            structureColor = new Color(0.420f, 0.760f, 0.900f, 1f);
            accentColor = new Color(0.996f, 0.675f, 0.290f, 1f);
            objectiveColor = new Color(1.000f, 0.850f, 0.505f, 1f);
            textColor = new Color(0.975f, 0.972f, 0.955f, 1f);
            mutedTextColor = new Color(0.675f, 0.790f, 0.860f, 1f);
            obstacleColor = new Color(0.955f, 0.388f, 0.470f, 1f);
            shellName = "ZannOS";
        }


        if (tuningVersion < 27)
        {
            // V27 shifts Firewall Runner to a monochrome terminal-style shell with
            // phosphor-green UI, deeper black-green backgrounds, and stronger gameplay contrast.
            backgroundColor = new Color(0.012f, 0.030f, 0.020f, 1f);
            surfaceColor = new Color(0.022f, 0.050f, 0.034f, 1f);
            raisedSurfaceColor = new Color(0.032f, 0.072f, 0.048f, 1f);
            structureColor = new Color(0.360f, 0.910f, 0.650f, 1f);
            accentColor = new Color(0.520f, 1.000f, 0.760f, 1f);
            objectiveColor = new Color(0.760f, 1.000f, 0.700f, 1f);
            textColor = new Color(0.900f, 1.000f, 0.920f, 1f);
            mutedTextColor = new Color(0.470f, 0.720f, 0.560f, 1f);
            obstacleColor = new Color(1.000f, 0.360f, 0.420f, 1f);
            shellName = "ZannOS";
        }


        if (tuningVersion < 28)
        {
            // V28 refines the terminal hierarchy: darker green-black surfaces,
            // slightly dimmer topology, a brighter mint player accent, and a
            // separate lime objective color for keys and important data.
            backgroundColor = new Color(0.007f, 0.022f, 0.014f, 1f);
            surfaceColor = new Color(0.014f, 0.040f, 0.026f, 1f);
            raisedSurfaceColor = new Color(0.022f, 0.055f, 0.036f, 1f);
            structureColor = new Color(0.250f, 0.780f, 0.540f, 1f);
            accentColor = new Color(0.680f, 1.000f, 0.820f, 1f);
            objectiveColor = new Color(0.920f, 0.940f, 0.520f, 1f);
            textColor = new Color(0.900f, 0.990f, 0.920f, 1f);
            mutedTextColor = new Color(0.340f, 0.580f, 0.440f, 1f);
            obstacleColor = new Color(1.000f, 0.360f, 0.420f, 1f);
            shellName = "ZannOS";
        }



        if (tuningVersion < 29)
        {
            // V29 keeps the green terminal identity while replacing the warm lime
            // accents with cooler teal-mint values and improving the visual hierarchy
            // between shell, passive structure, player packet, objective data, and hazards.
            backgroundColor = new Color(0.006f, 0.022f, 0.015f, 1f);
            surfaceColor = new Color(0.012f, 0.038f, 0.029f, 1f);
            raisedSurfaceColor = new Color(0.018f, 0.052f, 0.040f, 1f);
            structureColor = new Color(0.220f, 0.720f, 0.560f, 1f);
            accentColor = new Color(0.660f, 1.000f, 0.840f, 1f);
            objectiveColor = new Color(0.460f, 0.940f, 0.780f, 1f);
            textColor = new Color(0.900f, 0.990f, 0.950f, 1f);
            mutedTextColor = new Color(0.340f, 0.600f, 0.500f, 1f);
            obstacleColor = new Color(1.000f, 0.400f, 0.450f, 1f);
            shellName = "ZannOS";
        }


        if (tuningVersion < 30)
        {
            // V30 finalizes the terminal hierarchy: darker and more neutral
            // green-black surfaces, dimmer passive structure, brighter active
            // mint highlights, and cooler seafoam objective accents. This keeps
            // the look clean while preserving readability from the in-world camera.
            backgroundColor = new Color(0.005f, 0.020f, 0.014f, 1f);
            surfaceColor = new Color(0.010f, 0.032f, 0.024f, 1f);
            raisedSurfaceColor = new Color(0.014f, 0.042f, 0.033f, 1f);
            structureColor = new Color(0.185f, 0.710f, 0.590f, 1f);
            accentColor = new Color(0.760f, 1.000f, 0.900f, 1f);
            objectiveColor = new Color(0.580f, 0.970f, 0.880f, 1f);
            textColor = new Color(0.910f, 0.995f, 0.960f, 1f);
            mutedTextColor = new Color(0.250f, 0.520f, 0.430f, 1f);
            obstacleColor = new Color(1.000f, 0.400f, 0.450f, 1f);
            platformThicknessMultiplier = 1.08f;
            shellName = "ZannOS";
        }


        if (tuningVersion < 32)
        {
            // V32 nudges the terminal backgrounds slightly further toward cyan-green
            // while keeping the current dark value range and clean readability.
            backgroundColor = new Color(0.004f, 0.022f, 0.018f, 1f);
            surfaceColor = new Color(0.009f, 0.035f, 0.029f, 1f);
            raisedSurfaceColor = new Color(0.013f, 0.046f, 0.038f, 1f);
            structureColor = new Color(0.190f, 0.770f, 0.660f, 1f);
            accentColor = new Color(0.740f, 1.000f, 0.920f, 1f);
            objectiveColor = new Color(0.540f, 0.965f, 0.885f, 1f);
            textColor = new Color(0.910f, 0.995f, 0.960f, 1f);
            mutedTextColor = new Color(0.230f, 0.530f, 0.470f, 1f);
            obstacleColor = new Color(1.000f, 0.400f, 0.450f, 1f);
        }

        tuningVersion = CurrentTuningVersion;
    }

    private void ApplyAsciiVisualMigration()
    {
        // Existing prefabs serialized the older [#] hazard glyph. Upgrade it
        // automatically so the assigned terminal font does not need a prefab Reset.
        if (
            string.IsNullOrWhiteSpace(obstacleGlyph)
            || obstacleGlyph == "[#]"
            || obstacleGlyph == "#"
            || obstacleGlyph == "█"
            || obstacleGlyph == "██"
        )
            obstacleGlyph = "■";

        if (string.IsNullOrWhiteSpace(packetGlyph) || packetGlyph == "[>]")
            packetGlyph = "□";

        if (string.IsNullOrWhiteSpace(floorGlyph))
            floorGlyph = "□";
    }

    private void OnValidate()
    {
        EnsureDifficultySettingsExist();
        ApplyAsciiVisualMigration();
        ApplyVersionedTuning();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        playerWidth = Mathf.Max(20f, playerWidth);
        playerHeight = Mathf.Max(20f, playerHeight);
        jumpVelocity = Mathf.Max(100f, jumpVelocity);
        gravity = Mathf.Max(100f, gravity);
        collisionInset = Mathf.Max(0f, collisionInset);
        obstacleCollisionScale = Mathf.Clamp(obstacleCollisionScale, 0.45f, 1f);
        platformLandingHorizontalGrace = Mathf.Max(
            0f,
            platformLandingHorizontalGrace
        );
        platformLandingVerticalTolerance = Mathf.Max(
            0f,
            platformLandingVerticalTolerance
        );
        groundSupportProbeWidthMultiplier = Mathf.Clamp(
            groundSupportProbeWidthMultiplier,
            0.05f,
            0.45f
        );
        holeDeathClearance = Mathf.Max(0f, holeDeathClearance);
        floorGapWidthMultiplier = Mathf.Clamp(
            floorGapWidthMultiplier,
            1f,
            1.35f
        );
        obstacleSizeMultiplier = Mathf.Clamp(obstacleSizeMultiplier, 1f, 1.6f);
        obstacleStackGap = Mathf.Max(0f, obstacleStackGap);
        obstacleGroundClearance = Mathf.Max(0f, obstacleGroundClearance);
        platformHeightInBlocks = Mathf.Clamp(platformHeightInBlocks, 0.8f, 2.5f);
        platformThicknessMultiplier = Mathf.Clamp(
            platformThicknessMultiplier,
            0.25f,
            1f
        );
        gapMaskExtraHeight = Mathf.Max(0f, gapMaskExtraHeight);
        gapMaskHorizontalOverscan = Mathf.Clamp(
            gapMaskHorizontalOverscan,
            0f,
            16f
        );
        gapMaskTopOverscan = Mathf.Clamp(gapMaskTopOverscan, 0f, 16f);
        gameplayVisualScale = Mathf.Clamp(gameplayVisualScale, 1f, 1.8f);
        jumpTiltDegrees = Mathf.Clamp(jumpTiltDegrees, 0f, 45f);
        jumpTiltSmoothing = Mathf.Max(0f, jumpTiltSmoothing);
        separatorThickness = Mathf.Clamp(separatorThickness, 2f, 20f);
        floorGlyphFontSize = Mathf.Clamp(floorGlyphFontSize, 36f, 96f);
        floorGlyphCellWidthEm = Mathf.Clamp(floorGlyphCellWidthEm, 0.35f, 1.10f);
        floorGlyphVisualSize = Mathf.Clamp(floorGlyphVisualSize, 36f, 96f);
        floorGlyphGap = Mathf.Clamp(floorGlyphGap, 0f, 18f);
        packetGlyphVisualSize = Mathf.Clamp(packetGlyphVisualSize, 36f, 120f);
        packetGroundContactOffset = Mathf.Clamp(packetGroundContactOffset, -8f, 8f);
        packetGlyphFontSize = Mathf.Clamp(packetGlyphFontSize, 36f, 240f);

        if (string.IsNullOrWhiteSpace(packetGlyph))
            packetGlyph = "□";

        if (string.IsNullOrWhiteSpace(obstacleGlyph))
            obstacleGlyph = "■";

        if (string.IsNullOrWhiteSpace(floorGlyph))
            floorGlyph = "□";
    }
}

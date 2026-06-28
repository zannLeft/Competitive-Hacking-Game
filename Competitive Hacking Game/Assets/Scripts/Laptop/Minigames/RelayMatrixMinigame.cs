using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class RelayMatrixMinigame : LaptopMinigameBase
{
    private const int CurrentTuningVersion = 10;
    [Flags]
    private enum Connection
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
    }

    private enum TileKind
    {
        Empty,
        Relay,
        Source,
        Target,
        Honeypot,
    }

    private enum GameState
    {
        AwaitingStart,
        Playing,
        SuccessHold,
        RoundTransition,
        FailureHold,
    }

    [Serializable]
    private sealed class RelaySettings
    {
        [Min(4)]
        public int width = 6;

        [Min(4)]
        public int height = 5;

        [Range(1, 5)]
        public int roundsRequired = 3;

        [Min(5f)]
        public float securitySweepSeconds = 31f;

        [Min(0f)]
        public float honeypotPenaltySeconds = 4.5f;

        [Range(0, 5)]
        public int honeypotCount = 1;

        [Range(0f, 1f)]
        public float decoyPieceChance = 0.82f;

        [Range(0, 48)]
        public int minimumSolutionTurns = 6;

        [Range(0, 64)]
        public int maximumSolutionTurns = 18;

        [Min(0.03f)]
        public float movementRepeatSeconds = 0.105f;

        [Min(0f)]
        public float movementInitialRepeatDelay = 0.20f;
    }

    private sealed class TileData
    {
        public Connection SolutionMask;
        public int Rotation;
        public TileKind Kind;
        public bool Rotatable;
        public bool IsSolutionPath;

        public Connection CurrentMask => RotateMask(SolutionMask, Rotation);
    }

    private sealed class CellView
    {
        public RectTransform Root;
        public Image Background;
        public Image North;
        public Image East;
        public Image South;
        public Image West;
        public Image Center;
        public Image[] SelectionBorders;
        public TMP_Text Marker;
    }

    private struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = seed == 0 ? 0xA341316Cu : unchecked((uint)seed);
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

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public float Next01()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }
    }

    [Header("Difficulty")]
    [SerializeField]
    private RelaySettings easy = new()
    {
        width = 7,
        height = 5,
        roundsRequired = 3,
        securitySweepSeconds = 33f,
        honeypotPenaltySeconds = 4.5f,
        honeypotCount = 1,
        decoyPieceChance = 0.82f,
        minimumSolutionTurns = 8,
        maximumSolutionTurns = 22,
        movementRepeatSeconds = 0.105f,
        movementInitialRepeatDelay = 0.20f,
    };

    [SerializeField]
    private RelaySettings hard = new()
    {
        width = 9,
        height = 6,
        roundsRequired = 3,
        securitySweepSeconds = 34f,
        honeypotPenaltySeconds = 5.5f,
        honeypotCount = 2,
        decoyPieceChance = 0.95f,
        minimumSolutionTurns = 13,
        maximumSolutionTurns = 32,
        movementRepeatSeconds = 0.09f,
        movementInitialRepeatDelay = 0.16f,
    };

    [Header("Input")]
    [SerializeField, Range(0.05f, 0.95f)]
    private float navigationDeadzone = 0.35f;

    [Header("Timing")]
    [SerializeField, Min(0f)]
    private float successfulConnectionHoldSeconds = 0.18f;

    [SerializeField, Min(0.02f)]
    private float connectionAnimationStepSeconds = 0.065f;

    [SerializeField, Min(0f)]
    private float roundTransitionSeconds = 0.55f;

    [SerializeField, Min(0f)]
    private float failureHoldSeconds = 1.0f;

    [SerializeField, Min(0f)]
    private float warningMessageSeconds = 1.35f;

    [Header("Hacker OS Visuals")]
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
    private Color dangerColor = new(1.000f, 0.400f, 0.450f, 1f);

    [Header("Board Presentation")]
    [SerializeField, Range(0.02f, 0.22f)]
    private float connectorThickness = 0.105f;

    [SerializeField, Range(0.15f, 0.55f)]
    private float centerNodeSize = 0.27f;

    [SerializeField, Range(0f, 8f)]
    private float cellPaddingPixels = 2f;

    [SerializeField, Range(0.2f, 1f)]
    private float passiveStructureAlpha = 0.72f;

    [SerializeField]
    private Color energizedPathColor = new(0.300f, 0.940f, 0.790f, 1f);

    [SerializeField, HideInInspector]
    private int tuningVersion;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };

    private static readonly Connection[] DirectionFlags =
    {
        Connection.North,
        Connection.East,
        Connection.South,
        Connection.West,
    };

    private static readonly Connection[] DecoyMasks =
    {
        Connection.North | Connection.South,
        Connection.East | Connection.West,
        Connection.North | Connection.East,
        Connection.East | Connection.South,
        Connection.South | Connection.West,
        Connection.West | Connection.North,
        Connection.North | Connection.East | Connection.South,
        Connection.East | Connection.South | Connection.West,
        Connection.South | Connection.West | Connection.North,
        Connection.West | Connection.North | Connection.East,
        Connection.North,
        Connection.East,
        Connection.South,
        Connection.West,
    };

    private readonly List<Vector2Int> _solutionPath = new();
    private readonly List<Vector2Int> _bestGeneratedPath = new();
    private readonly HashSet<Vector2Int> _solutionCells = new();
    private readonly HashSet<Vector2Int> _honeypotCells = new();
    private readonly Queue<Vector2Int> _reachabilityQueue = new();
    private readonly List<Vector2Int> _candidateCells = new();
    private readonly List<Vector2Int> _successAnimationPath = new();
    private readonly HashSet<Vector2Int> _successAnimatedCells = new();

    private RectTransform _root;
    private RectTransform _boardAreaRoot;
    private RectTransform _boardRoot;
    private AspectRatioFitter _boardAspectFitter;
    private RectTransform _sweepFill;
    private TMP_Text _sweepLabelText;
    private TMP_Text _sweepTimeText;
    private LaptopMinigameVisualShell _shell;
    private TileData[,] _tiles;
    private CellView[,] _views;
    private int _viewCapacityWidth;
    private int _viewCapacityHeight;
    private bool[,] _reachable;

    private LaptopMinigameContext _context;
    private RelaySettings _settings;
    private Vector2Int _sourcePosition;
    private Vector2Int _targetPosition;
    private Vector2Int _cursorPosition;
    private Vector2 _navigationInput;
    private Vector2Int _heldDirection;
    private float _movementRepeatTimer;
    private float _sweepElapsed;
    private float _successHoldTimer;
    private float _successAnimationTimer;
    private int _successAnimationRevealCount;
    private bool _successAnimationFinished;
    private float _roundTransitionTimer;
    private float _failureTimer;
    private float _warningTimer;
    private int _currentRoundIndex;
    private int _roundAttemptIndex;
    private bool _sweepActive;
    private bool _pendingFinalCompletion;
    private bool _honeypotWasConnected;
    private bool _suppressMovementUntilNeutral;
    private GameState _state;

    public override bool SupportsSessionResume => true;

    protected override void OnPrepare()
    {
        PrepareReusableUi();

        int maxWidth = Mathf.Max(easy.width, hard.width);
        int maxHeight = Mathf.Max(easy.height, hard.height);
        EnsureCellViewCapacity(maxWidth, maxHeight);
        SetAllCellViewsActive(false);
    }

    protected override IEnumerator OnPrepareIncrementally(int operationsPerFrame)
    {
        PrepareReusableUi();

        // Let the shell creation settle before constructing the large relay grid.
        yield return null;

        int maxWidth = Mathf.Max(easy.width, hard.width);
        int maxHeight = Mathf.Max(easy.height, hard.height);
        EnsureCellViewArrayCapacity(maxWidth, maxHeight);

        int operations = 0;
        int budget = Mathf.Max(1, operationsPerFrame);

        for (int y = 0; y < maxHeight; y++)
        {
            for (int x = 0; x < maxWidth; x++)
            {
                if (_views[x, y] == null)
                {
                    _views[x, y] = CreateCellView(x, y);
                    _views[x, y].Root.gameObject.SetActive(false);
                }

                operations++;

                if (operations >= budget)
                {
                    operations = 0;
                    yield return null;
                }
            }
        }

        SetAllCellViewsActive(false);
    }

    private void PrepareReusableUi()
    {
        EnsureSettingsExist();
        ApplyTuningMigrations();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        BuildUiIfNeeded();
    }

    protected override void OnBegin(LaptopMinigameContext context)
    {
        EnsureSettingsExist();
        ApplyTuningMigrations();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;

        _currentRoundIndex = 0;
        _roundAttemptIndex = 0;
        GenerateCurrentRound();
        ResetRoundState(requireNavigationRelease: false);

        _state = GameState.AwaitingStart;
        _shell?.SetBriefingVisible(true);
        _shell?.SetProgress(0f);
        _shell?.SetStatus("READY", textColor);
    }

    protected override void OnResume(LaptopMinigameContext context)
    {
        EnsureSettingsExist();
        ApplyTuningMigrations();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;

        _navigationInput = Vector2.zero;
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _suppressMovementUntilNeutral = false;

        string difficulty = context.Difficulty.ToString().ToUpperInvariant();
        _shell?.SetContext(context.NetworkDisplayName, difficulty);

        if (_state == GameState.AwaitingStart)
        {
            _shell?.SetBriefingVisible(true);
            _shell?.SetProgress(0f);
            _shell?.SetStatus("READY", textColor);
            EvaluateNetwork();
            RefreshBoardViews();
            return;
        }

        _shell?.SetBriefingVisible(false);

        if (_state == GameState.Playing)
            _shell?.HideResult();

        EvaluateNetwork();
        RefreshBoardViews();
        UpdateSweepPresentation();
    }

    protected override void OnJumpPressed()
    {
        if (_state == GameState.AwaitingStart)
        {
            TriggerActionPerformed();
            _state = GameState.Playing;
            _suppressMovementUntilNeutral =
                _navigationInput.sqrMagnitude >= navigationDeadzone * navigationDeadzone;
            _shell?.SetBriefingVisible(false);
            _sweepActive = true;
            RefreshEverything();
            return;
        }

        if (_state != GameState.Playing)
            return;

        RotateSelectedTile();
    }

    protected override void OnNavigationChanged(Vector2 input)
    {
        _navigationInput = Vector2.ClampMagnitude(input, 1f);
        Vector2Int direction = GetCardinalDirection(_navigationInput);

        if (direction == Vector2Int.zero)
        {
            _heldDirection = Vector2Int.zero;
            _movementRepeatTimer = 0f;
            _suppressMovementUntilNeutral = false;
            return;
        }

        if (_state != GameState.Playing || _suppressMovementUntilNeutral)
            return;

        if (direction == _heldDirection)
            return;

        _heldDirection = direction;
        _movementRepeatTimer = _settings.movementInitialRepeatDelay;
        TryMoveCursor(direction);
    }

    protected override void OnSuspend()
    {
        _navigationInput = Vector2.zero;
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _suppressMovementUntilNeutral = false;
    }

    protected override void OnAbort()
    {
        OnSuspend();
        _warningTimer = 0f;
        _sweepActive = false;
    }

    private void OnDisable()
    {
        if (IsRunning)
            Abort();
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        _shell?.Tick(Time.unscaledTime);

        float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);

        if (_state == GameState.AwaitingStart)
            return;

        if (_state == GameState.FailureHold)
        {
            _failureTimer -= deltaTime;

            if (_failureTimer <= 0f)
            {
                _roundAttemptIndex++;
                GenerateCurrentRound();
                ResetRoundState(requireNavigationRelease: true);
                _state = GameState.Playing;
                _sweepActive = true;
            }

            return;
        }

        if (_state == GameState.SuccessHold)
        {
            UpdateSuccessfulConnectionAnimation(deltaTime);
            return;
        }

        if (_state == GameState.RoundTransition)
        {
            _roundTransitionTimer -= deltaTime;

            if (_roundTransitionTimer <= 0f)
            {
                _roundAttemptIndex = 0;
                GenerateCurrentRound();
                ResetRoundState(requireNavigationRelease: true);
                _state = GameState.Playing;
                _sweepActive = true;
            }

            return;
        }

        UpdateHeldMovement(deltaTime);
        UpdateWarning(deltaTime);

        if (_sweepActive)
        {
            _sweepElapsed += deltaTime;

            if (_sweepElapsed >= _settings.securitySweepSeconds)
            {
                BeginFailureHold();
                return;
            }
        }

        UpdateSweepPresentation();
    }

    private void UpdateHeldMovement(float deltaTime)
    {
        if (_suppressMovementUntilNeutral || _heldDirection == Vector2Int.zero)
            return;

        _movementRepeatTimer -= deltaTime;

        if (_movementRepeatTimer > 0f)
            return;

        _movementRepeatTimer += Mathf.Max(0.03f, _settings.movementRepeatSeconds);
        TryMoveCursor(_heldDirection);
    }

    private void UpdateWarning(float deltaTime)
    {
        if (_warningTimer <= 0f)
            return;

        _warningTimer -= deltaTime;

        if (_warningTimer <= 0f)
            _shell?.SetFooterLeft("S SOURCE  ·  T TARGET  ·  ! HONEYPOT");
    }

    private void TryMoveCursor(Vector2Int direction)
    {
        Vector2Int next = _cursorPosition + direction;

        if (!IsInside(next))
            return;

        _cursorPosition = next;
        TriggerActionPerformed();
        RefreshBoardViews();
    }

    private void RotateSelectedTile()
    {
        if (!IsInside(_cursorPosition))
            return;

        TileData tile = _tiles[_cursorPosition.x, _cursorPosition.y];

        if (tile == null || !tile.Rotatable || tile.Kind == TileKind.Empty)
            return;

        tile.Rotation = (tile.Rotation + 1) & 3;
        _sweepActive = true;
        TriggerActionPerformed();

        bool honeypotConnected = EvaluateNetwork();

        if (honeypotConnected && !_honeypotWasConnected)
        {
            TriggerAlarm();
            _sweepElapsed += Mathf.Max(0f, _settings.honeypotPenaltySeconds);
            _warningTimer = Mathf.Max(0f, warningMessageSeconds);
            _shell?.SetFooterLeft("HONEYPOT CONTACT  ·  TRACE ACCELERATED");
        }

        _honeypotWasConnected = honeypotConnected;

        if (_sweepElapsed >= _settings.securitySweepSeconds)
        {
            BeginFailureHold();
            return;
        }

        if (_reachable[_targetPosition.x, _targetPosition.y] && !honeypotConnected)
        {
            CompleteCurrentRound();
            return;
        }

        RefreshEverything();
    }

    private bool EvaluateNetwork()
    {
        RebuildReachability();

        foreach (Vector2Int honeypot in _honeypotCells)
        {
            if (_reachable[honeypot.x, honeypot.y])
                return true;
        }

        return false;
    }

    private void CompleteCurrentRound()
    {
        if (_state != GameState.Playing)
            return;

        int completedRoundNumber = _currentRoundIndex + 1;
        int required = Mathf.Max(1, _settings.roundsRequired);

        _sweepActive = false;
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _suppressMovementUntilNeutral = true;

        _shell?.SetProgress(completedRoundNumber / (float)required);

        if (completedRoundNumber >= required)
        {
            _shell?.SetProgress(1f);
            CompleteMinigame();
            return;
        }

        _currentRoundIndex++;
        _state = GameState.RoundTransition;
        _roundTransitionTimer = Mathf.Max(0f, roundTransitionSeconds);

        _shell?.ShowResult(
            $"ROUTE {completedRoundNumber}/{required} STABLE\nPACKET DELIVERED\n\nLOADING NEXT RELAY...",
            accentColor
        );
    }

    private void UpdateSuccessfulConnectionAnimation(float deltaTime)
    {
        if (!_successAnimationFinished)
        {
            _successAnimationTimer -= deltaTime;

            while (
                _successAnimationTimer <= 0f
                && _successAnimationRevealCount < _successAnimationPath.Count
            )
            {
                RevealNextConnectionStep();
                _successAnimationTimer += Mathf.Max(0.02f, connectionAnimationStepSeconds);
            }

            if (_successAnimationRevealCount >= _successAnimationPath.Count)
            {
                _successAnimationFinished = true;
                _successHoldTimer = Mathf.Max(0f, successfulConnectionHoldSeconds);

                int completedRoundNumber = _currentRoundIndex + 1;
                int required = Mathf.Max(1, _settings.roundsRequired);

                _shell?.SetStatus("CONNECTED", accentColor);
                _shell?.SetFooterLeft(
                    _pendingFinalCompletion
                        ? "FINAL ROUTE CONNECTED  ·  PACKET DELIVERED"
                        : $"ROUTE {completedRoundNumber}/{required} CONNECTED  ·  PACKET DELIVERED"
                );

                RefreshBoardViews();

                if (_successHoldTimer <= 0f)
                    FinishSuccessfulConnectionHold();
            }

            return;
        }

        _successHoldTimer -= deltaTime;

        if (_successHoldTimer <= 0f)
            FinishSuccessfulConnectionHold();
    }

    private void RevealNextConnectionStep()
    {
        if (_successAnimationRevealCount >= _successAnimationPath.Count)
            return;

        Vector2Int cell = _successAnimationPath[_successAnimationRevealCount];
        _successAnimatedCells.Add(cell);
        _successAnimationRevealCount++;
        RefreshBoardViews();
    }

    private bool TryBuildCurrentConnectionPath(List<Vector2Int> output)
    {
        output.Clear();

        if (_tiles == null || !IsInside(_sourcePosition) || !IsInside(_targetPosition))
            return false;

        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);
        bool[,] visited = new bool[width, height];
        Vector2Int[,] parent = new Vector2Int[width, height];
        Queue<Vector2Int> queue = new();

        visited[_sourcePosition.x, _sourcePosition.y] = true;
        parent[_sourcePosition.x, _sourcePosition.y] = _sourcePosition;
        queue.Enqueue(_sourcePosition);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            if (current == _targetPosition)
                break;

            Connection currentMask = _tiles[current.x, current.y].CurrentMask;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Connection direction = DirectionFlags[i];

                if ((currentMask & direction) == 0)
                    continue;

                Vector2Int next = current + CardinalDirections[i];

                if (!IsInside(next) || visited[next.x, next.y])
                    continue;

                Connection neighbourMask = _tiles[next.x, next.y].CurrentMask;

                if ((neighbourMask & Opposite(direction)) == 0)
                    continue;

                visited[next.x, next.y] = true;
                parent[next.x, next.y] = current;
                queue.Enqueue(next);
            }
        }

        if (!visited[_targetPosition.x, _targetPosition.y])
            return false;

        Vector2Int step = _targetPosition;

        while (step != _sourcePosition)
        {
            output.Add(step);
            step = parent[step.x, step.y];
        }

        output.Add(_sourcePosition);
        output.Reverse();
        return output.Count > 0;
    }

    private void FinishSuccessfulConnectionHold()
    {
        if (_state != GameState.SuccessHold)
            return;

        int completedRoundNumber = _currentRoundIndex + 1;
        int required = Mathf.Max(1, _settings.roundsRequired);

        if (_pendingFinalCompletion)
        {
            _shell?.SetProgress(1f);
            CompleteMinigame();
            return;
        }

        _currentRoundIndex++;
        _state = GameState.RoundTransition;
        _roundTransitionTimer = Mathf.Max(0f, roundTransitionSeconds);

        _shell?.ShowResult(
            $"ROUTE {completedRoundNumber}/{required} STABLE\nPACKET DELIVERED\n\nLOADING NEXT RELAY...",
            accentColor
        );
    }

    private void BeginFailureHold()
    {
        if (_state != GameState.Playing)
            return;

        _state = GameState.FailureHold;
        _failureTimer = Mathf.Max(0f, failureHoldSeconds);
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        TriggerAlarm();
        _shell?.ShowResult(
            "SECURITY SWEEP COMPLETE\nSESSION PURGED\n\nREBUILDING MATRIX...",
            dangerColor
        );
    }

    private void ResetRoundState(bool requireNavigationRelease)
    {
        _sweepElapsed = 0f;
        _successHoldTimer = 0f;
        _successAnimationTimer = 0f;
        _successAnimationRevealCount = 0;
        _successAnimationFinished = false;
        _successAnimationPath.Clear();
        _successAnimatedCells.Clear();
        _sweepActive = false;
        _pendingFinalCompletion = false;
        _honeypotWasConnected = false;
        _warningTimer = 0f;
        _movementRepeatTimer = 0f;
        _heldDirection = Vector2Int.zero;
        _cursorPosition = FindInitialCursorPosition();
        _suppressMovementUntilNeutral =
            requireNavigationRelease
            && _navigationInput.sqrMagnitude >= navigationDeadzone * navigationDeadzone;

        string difficulty = _context.Difficulty.ToString().ToUpperInvariant();
        _shell?.SetContext(_context.NetworkDisplayName, difficulty);
        _shell?.HideResult();
        _shell?.SetFooterLeft("S SOURCE  ·  T TARGET  ·  ! HONEYPOT");
        EvaluateNetwork();
        RefreshEverything();
    }

    private Vector2Int FindInitialCursorPosition()
    {
        for (int i = 1; i < _solutionPath.Count - 1; i++)
        {
            Vector2Int position = _solutionPath[i];
            TileData tile = _tiles[position.x, position.y];

            if (tile != null && tile.Rotatable)
                return position;
        }

        return _sourcePosition;
    }

    private void GenerateCurrentRound()
    {
        int seed = DeriveRoundSeed(
            _context.Seed,
            _currentRoundIndex,
            _roundAttemptIndex
        );
        GenerateBoard(seed);
        RebuildCellViews();
    }

    private static int DeriveRoundSeed(int baseSeed, int roundIndex, int attemptIndex)
    {
        unchecked
        {
            uint value = (uint)baseSeed;
            value ^= 0x9E3779B9u * (uint)(roundIndex + 1);
            value ^= 0x85EBCA6Bu * (uint)(attemptIndex + 1);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private void GenerateBoard(int seed)
    {
        const int maximumGenerationAttempts = 24;

        for (int generationAttempt = 0; generationAttempt < maximumGenerationAttempts; generationAttempt++)
        {
            int attemptSeed = MixGenerationSeed(seed, generationAttempt);

            if (TryGenerateBoard(attemptSeed))
                return;
        }

        // This should only be reached if a future board-generation change breaks
        // the known solution. Build one final board and force a safe scramble so
        // the player can never receive an impossible round.
        Debug.LogWarning(
            "Relay Matrix could not produce a validated board within the normal attempt limit. "
            + "Using the guaranteed-safe fallback scramble."
        );

        BuildBoardLayout(seed);
        ForceSafeFallbackScramble();
    }

    private bool TryGenerateBoard(int seed)
    {
        BuildBoardLayout(seed);

        if (!ValidateSolutionPathStructure() || !ValidateKnownSolution())
            return false;

        var random = new DeterministicRandom(unchecked(seed ^ (int)0x6D2B79F5u));

        if (!ScrambleBoard(ref random))
            return false;

        // Re-run the proof after scrambling. This uses the actual generated board,
        // temporarily restores the canonical route, verifies target reachability and
        // honeypot safety, then restores the player's scrambled rotations.
        return ValidateSolutionPathStructure() && ValidateKnownSolution();
    }

    private void BuildBoardLayout(int seed)
    {
        int width = Mathf.Max(4, _settings.width);
        int height = Mathf.Max(4, _settings.height);
        _tiles = new TileData[width, height];
        _reachable = new bool[width, height];
        _solutionPath.Clear();
        _solutionCells.Clear();
        _honeypotCells.Clear();

        var random = new DeterministicRandom(seed);
        BuildBestPath(ref random, width, height);

        for (int i = 0; i < _solutionPath.Count; i++)
            _solutionCells.Add(_solutionPath[i]);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                bool onPath = _solutionCells.Contains(cell);
                _tiles[x, y] = new TileData
                {
                    Kind = onPath ? TileKind.Relay : TileKind.Empty,
                    Rotatable = onPath,
                    IsSolutionPath = onPath,
                    SolutionMask = Connection.None,
                    Rotation = 0,
                };
            }
        }

        for (int i = 0; i < _solutionPath.Count - 1; i++)
        {
            Vector2Int from = _solutionPath[i];
            Vector2Int to = _solutionPath[i + 1];
            Connection forward = DirectionToConnection(to - from);
            Connection backward = Opposite(forward);
            _tiles[from.x, from.y].SolutionMask |= forward;
            _tiles[to.x, to.y].SolutionMask |= backward;
        }

        _sourcePosition = _solutionPath[0];
        _targetPosition = _solutionPath[_solutionPath.Count - 1];
        _tiles[_sourcePosition.x, _sourcePosition.y].Kind = TileKind.Source;
        _tiles[_sourcePosition.x, _sourcePosition.y].Rotatable = false;
        _tiles[_targetPosition.x, _targetPosition.y].Kind = TileKind.Target;
        _tiles[_targetPosition.x, _targetPosition.y].Rotatable = false;

        FillDecoys(ref random, width, height);
        PlaceHoneypots(ref random, width, height);
    }

    private static int MixGenerationSeed(int seed, int attempt)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= 0x9E3779B9u * (uint)(attempt + 1);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private bool ValidateSolutionPathStructure()
    {
        if (_tiles == null || _solutionPath.Count < 2)
            return false;

        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);
        var visited = new HashSet<Vector2Int>();

        for (int i = 0; i < _solutionPath.Count; i++)
        {
            Vector2Int cell = _solutionPath[i];

            if (
                cell.x < 0
                || cell.y < 0
                || cell.x >= width
                || cell.y >= height
                || !visited.Add(cell)
            )
            {
                return false;
            }

            TileData tile = _tiles[cell.x, cell.y];

            if (tile == null || !tile.IsSolutionPath)
                return false;

            bool endpoint = i == 0 || i == _solutionPath.Count - 1;

            if (!endpoint && !tile.Rotatable)
                return false;
        }

        if (_solutionPath[0] != _sourcePosition)
            return false;

        if (_solutionPath[_solutionPath.Count - 1] != _targetPosition)
            return false;

        TileData source = _tiles[_sourcePosition.x, _sourcePosition.y];
        TileData target = _tiles[_targetPosition.x, _targetPosition.y];

        if (
            source.Kind != TileKind.Source
            || source.Rotatable
            || target.Kind != TileKind.Target
            || target.Rotatable
        )
        {
            return false;
        }

        for (int i = 0; i < _solutionPath.Count - 1; i++)
        {
            Vector2Int from = _solutionPath[i];
            Vector2Int to = _solutionPath[i + 1];
            Vector2Int delta = to - from;

            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                return false;

            Connection forward = DirectionToConnection(delta);

            if (forward == Connection.None)
                return false;

            Connection backward = Opposite(forward);

            if (
                (_tiles[from.x, from.y].SolutionMask & forward) == 0
                || (_tiles[to.x, to.y].SolutionMask & backward) == 0
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateKnownSolution()
    {
        if (_tiles == null || _solutionPath.Count < 2)
            return false;

        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);
        int[,] savedRotations = new int[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                TileData tile = _tiles[x, y];
                savedRotations[x, y] = tile.Rotation;

                if (tile.IsSolutionPath)
                    tile.Rotation = 0;
            }
        }

        bool honeypotConnected = EvaluateNetwork();
        bool targetConnected = _reachable[_targetPosition.x, _targetPosition.y];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                _tiles[x, y].Rotation = savedRotations[x, y];
        }

        // Restore reachability to match the scrambled board rather than leaving the
        // temporary solved-state traversal cached in _reachable.
        EvaluateNetwork();
        return targetConnected && !honeypotConnected;
    }

    private void BuildBestPath(
        ref DeterministicRandom random,
        int width,
        int height
    )
    {
        _bestGeneratedPath.Clear();

        int currentY = random.Range(0, height);
        int targetY = random.Range(0, height);
        Vector2Int current = new(0, currentY);
        _bestGeneratedPath.Add(current);

        for (int x = 0; x < width - 1; x++)
        {
            int remainingColumns = width - 1 - x;
            int desiredY;

            if (remainingColumns <= 1)
            {
                desiredY = targetY;
            }
            else
            {
                int towardTarget = Math.Sign(targetY - currentY);
                int randomDrift = random.Range(-2, 3);

                if (random.Next01() < 0.58f && towardTarget != 0)
                    randomDrift = towardTarget + random.Range(-1, 2);

                desiredY = Mathf.Clamp(currentY + randomDrift, 0, height - 1);
            }

            while (currentY != desiredY)
            {
                currentY += Math.Sign(desiredY - currentY);
                current = new Vector2Int(x, currentY);
                _bestGeneratedPath.Add(current);
            }

            current = new Vector2Int(x + 1, currentY);
            _bestGeneratedPath.Add(current);
        }

        while (currentY != targetY)
        {
            currentY += Math.Sign(targetY - currentY);
            current = new Vector2Int(width - 1, currentY);
            _bestGeneratedPath.Add(current);
        }

        _solutionPath.AddRange(_bestGeneratedPath);
    }

    private void FillDecoys(ref DeterministicRandom random, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                TileData tile = _tiles[x, y];

                if (tile.IsSolutionPath || random.Next01() > _settings.decoyPieceChance)
                    continue;

                tile.Kind = TileKind.Relay;
                tile.Rotatable = true;
                tile.SolutionMask = DecoyMasks[random.Range(0, DecoyMasks.Length)];
            }
        }
    }

    private void PlaceHoneypots(
        ref DeterministicRandom random,
        int width,
        int height
    )
    {
        _candidateCells.Clear();

        for (int i = 1; i < _solutionPath.Count - 1; i++)
        {
            Vector2Int pathCell = _solutionPath[i];

            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int candidate = pathCell + CardinalDirections[directionIndex];

                if (
                    candidate.x < 0
                    || candidate.y < 0
                    || candidate.x >= width
                    || candidate.y >= height
                    || _solutionCells.Contains(candidate)
                    || _candidateCells.Contains(candidate)
                )
                {
                    continue;
                }

                _candidateCells.Add(candidate);
            }
        }

        int requested = Mathf.Min(_settings.honeypotCount, _candidateCells.Count);

        for (int i = 0; i < requested; i++)
        {
            int index = random.Range(0, _candidateCells.Count);
            Vector2Int cell = _candidateCells[index];
            _candidateCells.RemoveAt(index);

            Vector2Int routeNeighbour = FindAdjacentSolutionCell(cell);

            if (routeNeighbour.x < 0)
                continue;

            Connection towardRoute = DirectionToConnection(routeNeighbour - cell);
            TileData tile = _tiles[cell.x, cell.y];
            tile.Kind = TileKind.Honeypot;
            tile.Rotatable = false;
            tile.IsSolutionPath = false;
            tile.SolutionMask = towardRoute;
            tile.Rotation = 0;
            _honeypotCells.Add(cell);
        }
    }

    private Vector2Int FindAdjacentSolutionCell(Vector2Int cell)
    {
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int neighbour = cell + CardinalDirections[i];

            if (IsInside(neighbour) && _solutionCells.Contains(neighbour))
                return neighbour;
        }

        return new Vector2Int(-1, -1);
    }

    private bool ScrambleBoard(ref DeterministicRandom random)
    {
        const int maximumScrambleAttempts = 256;
        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);
        int[,] bestRotations = new int[width, height];
        int bestSolutionTurns = -1;
        bool foundSafeScramble = false;

        int maximumPossibleTurns = Mathf.Max(1, (_solutionPath.Count - 2) * 3);
        int minimumTurns = Mathf.Clamp(
            _settings.minimumSolutionTurns,
            1,
            maximumPossibleTurns
        );
        int maximumTurns = Mathf.Clamp(
            Mathf.Max(minimumTurns, _settings.maximumSolutionTurns),
            minimumTurns,
            maximumPossibleTurns
        );

        for (int attempt = 0; attempt < maximumScrambleAttempts; attempt++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    TileData tile = _tiles[x, y];

                    if (tile.Rotatable)
                        tile.Rotation = random.Range(0, 4);
                    else
                        tile.Rotation = 0;
                }
            }

            bool honeypotConnected = EvaluateNetwork();
            bool targetConnected = _reachable[_targetPosition.x, _targetPosition.y];

            if (targetConnected || honeypotConnected)
                continue;

            int requiredTurns = CountRequiredSolutionTurns();

            if (requiredTurns > bestSolutionTurns)
            {
                bestSolutionTurns = requiredTurns;
                foundSafeScramble = true;
                CaptureRotations(bestRotations);
            }

            if (requiredTurns >= minimumTurns && requiredTurns <= maximumTurns)
                return true;
        }

        if (foundSafeScramble)
        {
            RestoreRotations(bestRotations);
            EvaluateNetwork();
            return true;
        }

        return false;
    }

    private int CountRequiredSolutionTurns()
    {
        int total = 0;

        for (int i = 1; i < _solutionPath.Count - 1; i++)
        {
            Vector2Int cell = _solutionPath[i];
            TileData tile = _tiles[cell.x, cell.y];

            if (tile != null && tile.Rotatable)
                total += GetClockwiseTurnsToSolution(tile);
        }

        return total;
    }

    private static int GetClockwiseTurnsToSolution(TileData tile)
    {
        for (int presses = 0; presses < 4; presses++)
        {
            Connection candidate = RotateMask(tile.CurrentMask, presses);

            if (candidate == tile.SolutionMask)
                return presses;
        }

        return 3;
    }

    private void CaptureRotations(int[,] destination)
    {
        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                destination[x, y] = _tiles[x, y].Rotation;
        }
    }

    private void RestoreRotations(int[,] source)
    {
        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                _tiles[x, y].Rotation = source[x, y];
        }
    }

    private void ForceSafeFallbackScramble()
    {
        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                _tiles[x, y].Rotation = 0;
        }

        // Break the known-safe route at its first internal tile by choosing an
        // orientation that cannot receive the source signal. Everything after it
        // is unreachable, so the target and every honeypot remain disconnected.
        if (_solutionPath.Count > 2)
        {
            Vector2Int previous = _solutionPath[0];
            Vector2Int cell = _solutionPath[1];
            TileData tile = _tiles[cell.x, cell.y];
            Connection incomingSide = DirectionToConnection(previous - cell);

            for (int rotation = 1; rotation < 4; rotation++)
            {
                Connection candidate = RotateMask(tile.SolutionMask, rotation);

                if ((candidate & incomingSide) != 0)
                    continue;

                tile.Rotation = rotation;
                break;
            }
        }

        EvaluateNetwork();
    }

    private void RebuildReachability()
    {
        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                _reachable[x, y] = false;
        }

        _reachabilityQueue.Clear();
        _reachable[_sourcePosition.x, _sourcePosition.y] = true;
        _reachabilityQueue.Enqueue(_sourcePosition);

        while (_reachabilityQueue.Count > 0)
        {
            Vector2Int current = _reachabilityQueue.Dequeue();
            Connection currentMask = _tiles[current.x, current.y].CurrentMask;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Connection direction = DirectionFlags[i];

                if ((currentMask & direction) == 0)
                    continue;

                Vector2Int next = current + CardinalDirections[i];

                if (!IsInside(next) || _reachable[next.x, next.y])
                    continue;

                Connection neighbourMask = _tiles[next.x, next.y].CurrentMask;

                if ((neighbourMask & Opposite(direction)) == 0)
                    continue;

                _reachable[next.x, next.y] = true;
                _reachabilityQueue.Enqueue(next);
            }
        }
    }

    private void RefreshEverything()
    {
        EvaluateNetwork();
        RefreshBoardViews();
        UpdateSweepPresentation();
    }

    private void RefreshBoardViews()
    {
        if (_views == null || _tiles == null)
            return;

        for (int y = 0; y < _tiles.GetLength(1); y++)
        {
            for (int x = 0; x < _tiles.GetLength(0); x++)
            {
                Vector2Int position = new(x, y);
                TileData tile = _tiles[x, y];
                CellView view = _views[x, y];
                Connection mask = tile.CurrentMask;
                bool selected = position == _cursorPosition;
                bool connected = _reachable[x, y];
                bool successAnimationActive = _state == GameState.SuccessHold;
                bool energizedForDisplay = successAnimationActive
                    ? _successAnimatedCells.Contains(position)
                    : connected;
                bool animationTip = successAnimationActive
                    && _successAnimationRevealCount > 0
                    && _successAnimationRevealCount <= _successAnimationPath.Count
                    && position == _successAnimationPath[_successAnimationRevealCount - 1];

                Color relayColor = energizedForDisplay
                    ? energizedPathColor
                    : WithAlpha(structureColor, passiveStructureAlpha);

                if (tile.Kind == TileKind.Target)
                    relayColor = energizedForDisplay ? energizedPathColor : objectiveColor;
                else if (tile.Kind == TileKind.Honeypot)
                    relayColor = dangerColor;
                else if (tile.Kind == TileKind.Source)
                    relayColor = energizedForDisplay ? energizedPathColor : objectiveColor;
                else if (tile.Kind == TileKind.Empty)
                    relayColor = WithAlpha(mutedTextColor, 0.24f);

                if (animationTip)
                    relayColor = accentColor;

                Color northColor = GetConnectorDisplayColor(
                    position,
                    Connection.North,
                    relayColor,
                    successAnimationActive,
                    tile.Kind
                );
                Color eastColor = GetConnectorDisplayColor(
                    position,
                    Connection.East,
                    relayColor,
                    successAnimationActive,
                    tile.Kind
                );
                Color southColor = GetConnectorDisplayColor(
                    position,
                    Connection.South,
                    relayColor,
                    successAnimationActive,
                    tile.Kind
                );
                Color westColor = GetConnectorDisplayColor(
                    position,
                    Connection.West,
                    relayColor,
                    successAnimationActive,
                    tile.Kind
                );

                SetConnectorVisible(view.North, (mask & Connection.North) != 0, northColor);
                SetConnectorVisible(view.East, (mask & Connection.East) != 0, eastColor);
                SetConnectorVisible(view.South, (mask & Connection.South) != 0, southColor);
                SetConnectorVisible(view.West, (mask & Connection.West) != 0, westColor);

                // Keep the center cap opaque so the connector arms visually terminate
                // behind the node instead of showing through its semi-transparent fill.
                Color centerColor = relayColor;
                centerColor.a = 1f;
                view.Center.color = centerColor;
                view.Center.enabled = tile.Kind != TileKind.Empty || mask != Connection.None;

                Color cellBackground = Color.clear;
                if (tile.Kind == TileKind.Source)
                    cellBackground = WithAlpha(energizedPathColor, 0.016f);
                else if (tile.Kind == TileKind.Target)
                    cellBackground = WithAlpha(objectiveColor, 0.018f);
                else if (tile.Kind == TileKind.Honeypot)
                    cellBackground = WithAlpha(dangerColor, 0.016f);

                view.Background.color = selected
                    ? WithAlpha(accentColor, 0.026f)
                    : cellBackground;

                for (int borderIndex = 0; borderIndex < view.SelectionBorders.Length; borderIndex++)
                {
                    view.SelectionBorders[borderIndex].enabled = selected;
                    view.SelectionBorders[borderIndex].color =
                        tile.Rotatable ? accentColor : WithAlpha(mutedTextColor, 0.72f);
                }

                string marker = string.Empty;
                Color markerColor = relayColor;

                if (tile.Kind == TileKind.Source)
                {
                    marker = "S";
                    markerColor = backgroundColor;
                }
                else if (tile.Kind == TileKind.Target)
                {
                    marker = "T";
                    markerColor = backgroundColor;
                }
                else if (tile.Kind == TileKind.Honeypot)
                {
                    marker = "!";
                    markerColor = backgroundColor;
                }

                view.Marker.text = marker;
                view.Marker.color = markerColor;
            }
        }
    }

    private Color GetConnectorDisplayColor(
        Vector2Int position,
        Connection direction,
        Color fallbackColor,
        bool successAnimationActive,
        TileKind tileKind
    )
    {
        if (!successAnimationActive)
            return fallbackColor;

        if (IsRevealedSuccessPathSegment(position, direction))
            return energizedPathColor;

        if (
            tileKind == TileKind.Source
            || tileKind == TileKind.Target
            || tileKind == TileKind.Honeypot
        )
        {
            return fallbackColor;
        }

        return WithAlpha(structureColor, passiveStructureAlpha);
    }

    private bool IsRevealedSuccessPathSegment(Vector2Int position, Connection direction)
    {
        Vector2Int offset = direction switch
        {
            Connection.North => Vector2Int.up,
            Connection.East => Vector2Int.right,
            Connection.South => Vector2Int.down,
            Connection.West => Vector2Int.left,
            _ => Vector2Int.zero,
        };

        if (offset == Vector2Int.zero)
            return false;

        Vector2Int neighbour = position + offset;

        if (
            !_successAnimatedCells.Contains(position)
            || !_successAnimatedCells.Contains(neighbour)
        )
        {
            return false;
        }

        int revealedCount = Mathf.Min(
            _successAnimationRevealCount,
            _successAnimationPath.Count
        );

        for (int i = 0; i < revealedCount - 1; i++)
        {
            Vector2Int first = _successAnimationPath[i];
            Vector2Int second = _successAnimationPath[i + 1];

            if (
                (first == position && second == neighbour)
                || (first == neighbour && second == position)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static void SetConnectorVisible(Image image, bool visible, Color color)
    {
        image.enabled = visible;
        image.color = color;
    }

    private void UpdateSweepPresentation()
    {
        float sweepNormalized = Mathf.Clamp01(
            _sweepElapsed / Mathf.Max(0.01f, _settings.securitySweepSeconds)
        );

        if (_sweepFill != null)
        {
            _sweepFill.anchorMin = Vector2.zero;
            _sweepFill.anchorMax = new Vector2(sweepNormalized, 1f);
            _sweepFill.offsetMin = Vector2.zero;
            _sweepFill.offsetMax = Vector2.zero;
        }

        int tracePercent = Mathf.RoundToInt(sweepNormalized * 100f);
        int required = Mathf.Max(1, _settings.roundsRequired);
        Color sweepColor = sweepNormalized >= 0.72f ? dangerColor : textColor;
        _shell?.SetStatus(
            $"R{_currentRoundIndex + 1}/{required}  SWEEP {tracePercent:00}%",
            sweepColor
        );

        float remainingSeconds = Mathf.Max(
            0f,
            _settings.securitySweepSeconds - _sweepElapsed
        );

        if (_sweepLabelText != null)
        {
            _sweepLabelText.text = "SECURITY SWEEP";
            _sweepLabelText.color = sweepNormalized >= 0.72f
                ? dangerColor
                : WithAlpha(mutedTextColor, 0.92f);
        }

        if (_sweepTimeText != null)
        {
            _sweepTimeText.text = $"T-{Mathf.CeilToInt(remainingSeconds):00}s";
            _sweepTimeText.color = sweepColor;
        }

        // Overall hack progress only advances when a relay round is completed.
        // It deliberately ignores correctly oriented individual pieces so the
        // player cannot use the bar as a hot/cold hint while blindly rotating.
        float completedRoundsProgress = _currentRoundIndex / (float)required;
        _shell?.SetProgress(completedRoundsProgress);
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
            dangerColor
        );

        _shell = LaptopMinigameVisualShell.Build(
            _root,
            terminalFont,
            palette,
            shellName,
            "intrusion-suite",
            "RELAY MATRIX",
            "Rotate relay nodes to carry the intrusion signal from its source to the marked access port.",
            "CONNECT THE SOURCE NODE [S] TO THE TARGET PORT [T]. NEVER CONNECT A RED HONEYPOT [!].",
            "WASD  /  MOVE     SPACE  /  ROTATE",
            "S SOURCE  ·  T TARGET  ·  ! HONEYPOT"
        );

        _boardAreaRoot = CreateRect("RelayBoardArea", _shell.GameArea);
        Stretch(
            _boardAreaRoot,
            new Vector2(0.035f, 0.145f),
            new Vector2(0.965f, 0.985f)
        );

        _boardRoot = CreateRect("RelayBoard", _boardAreaRoot);
        Stretch(_boardRoot, Vector2.zero, Vector2.one);
        _boardAspectFitter = _boardRoot.gameObject.AddComponent<AspectRatioFitter>();
        _boardAspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _boardAspectFitter.aspectRatio = 1f;

        _sweepLabelText = CreateText(
            "SecuritySweepLabel",
            _shell.GameArea,
            "SECURITY SWEEP",
            22f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(mutedTextColor, 0.92f),
            FontStyles.Bold
        );
        Stretch(
            _sweepLabelText.rectTransform,
            new Vector2(0.050f, 0.078f),
            new Vector2(0.380f, 0.120f)
        );

        _sweepTimeText = CreateText(
            "SecuritySweepTime",
            _shell.GameArea,
            "T-00s",
            22f,
            TextAlignmentOptions.MidlineRight,
            textColor,
            FontStyles.Bold
        );
        Stretch(
            _sweepTimeText.rectTransform,
            new Vector2(0.700f, 0.078f),
            new Vector2(0.950f, 0.120f)
        );

        RectTransform sweepTrack = CreateRect("SecuritySweepTrack", _shell.GameArea);
        Stretch(
            sweepTrack,
            new Vector2(0.050f, 0.045f),
            new Vector2(0.950f, 0.068f)
        );
        Image trackImage = sweepTrack.gameObject.AddComponent<Image>();
        trackImage.color = WithAlpha(structureColor, 0.20f);
        trackImage.raycastTarget = false;

        Image fillImage = CreateImage(
            "SecuritySweepFill",
            sweepTrack,
            WithAlpha(dangerColor, 0.88f)
        );
        _sweepFill = fillImage.rectTransform;
        Stretch(_sweepFill, Vector2.zero, Vector2.zero);
    }

    private void RebuildCellViews()
    {
        if (_boardRoot == null || _tiles == null)
            return;

        int width = _tiles.GetLength(0);
        int height = _tiles.GetLength(1);

        EnsureCellViewCapacity(width, height);

        if (_boardAspectFitter != null)
            _boardAspectFitter.aspectRatio = width / (float)Mathf.Max(1, height);

        for (int y = 0; y < _viewCapacityHeight; y++)
        {
            for (int x = 0; x < _viewCapacityWidth; x++)
            {
                CellView view = _views[x, y];

                if (view == null || view.Root == null)
                    continue;

                bool usedByCurrentBoard = x < width && y < height;
                view.Root.gameObject.SetActive(usedByCurrentBoard);

                if (usedByCurrentBoard)
                    ConfigureCellViewLayout(view, x, y, width, height);
            }
        }
    }

    private void EnsureCellViewCapacity(int width, int height)
    {
        EnsureCellViewArrayCapacity(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (_views[x, y] != null)
                    continue;

                _views[x, y] = CreateCellView(x, y);
                _views[x, y].Root.gameObject.SetActive(false);
            }
        }
    }

    private void EnsureCellViewArrayCapacity(int width, int height)
    {
        int requestedWidth = Mathf.Max(1, width);
        int requestedHeight = Mathf.Max(1, height);

        if (
            _views != null
            && _viewCapacityWidth >= requestedWidth
            && _viewCapacityHeight >= requestedHeight
        )
            return;

        int newWidth = Mathf.Max(requestedWidth, _viewCapacityWidth);
        int newHeight = Mathf.Max(requestedHeight, _viewCapacityHeight);
        var expanded = new CellView[newWidth, newHeight];

        if (_views != null)
        {
            for (int y = 0; y < _viewCapacityHeight; y++)
            {
                for (int x = 0; x < _viewCapacityWidth; x++)
                    expanded[x, y] = _views[x, y];
            }
        }

        _views = expanded;
        _viewCapacityWidth = newWidth;
        _viewCapacityHeight = newHeight;
    }

    private void SetAllCellViewsActive(bool active)
    {
        if (_views == null)
            return;

        for (int y = 0; y < _viewCapacityHeight; y++)
        {
            for (int x = 0; x < _viewCapacityWidth; x++)
            {
                CellView view = _views[x, y];

                if (view != null && view.Root != null)
                    view.Root.gameObject.SetActive(active);
            }
        }
    }

    private void ConfigureCellViewLayout(
        CellView view,
        int x,
        int y,
        int width,
        int height
    )
    {
        RectTransform cellRoot = view.Root;
        cellRoot.anchorMin = new Vector2(x / (float)width, y / (float)height);
        cellRoot.anchorMax = new Vector2((x + 1f) / width, (y + 1f) / height);
        cellRoot.pivot = new Vector2(0.5f, 0.5f);
        cellRoot.offsetMin = new Vector2(cellPaddingPixels, cellPaddingPixels);
        cellRoot.offsetMax = new Vector2(-cellPaddingPixels, -cellPaddingPixels);
        cellRoot.localScale = Vector3.one;
        cellRoot.localRotation = Quaternion.identity;
    }

    private CellView CreateCellView(int x, int y)
    {
        RectTransform cellRoot = CreateRect($"Cell_{x}_{y}", _boardRoot);
        cellRoot.pivot = new Vector2(0.5f, 0.5f);
        cellRoot.localScale = Vector3.one;
        cellRoot.localRotation = Quaternion.identity;

        Image background = cellRoot.gameObject.AddComponent<Image>();
        background.color = Color.clear;
        background.raycastTarget = false;

        float halfThickness = connectorThickness * 0.5f;
        float halfCenter = centerNodeSize * 0.5f;

        Image north = CreateImage("North", cellRoot, structureColor);
        Stretch(
            north.rectTransform,
            new Vector2(0.5f - halfThickness, 0.5f),
            new Vector2(0.5f + halfThickness, 1f)
        );

        Image east = CreateImage("East", cellRoot, structureColor);
        Stretch(
            east.rectTransform,
            new Vector2(0.5f, 0.5f - halfThickness),
            new Vector2(1f, 0.5f + halfThickness)
        );

        Image south = CreateImage("South", cellRoot, structureColor);
        Stretch(
            south.rectTransform,
            new Vector2(0.5f - halfThickness, 0f),
            new Vector2(0.5f + halfThickness, 0.5f)
        );

        Image west = CreateImage("West", cellRoot, structureColor);
        Stretch(
            west.rectTransform,
            new Vector2(0f, 0.5f - halfThickness),
            new Vector2(0.5f, 0.5f + halfThickness)
        );

        Image center = CreateImage("Center", cellRoot, structureColor);
        Stretch(
            center.rectTransform,
            new Vector2(0.5f - halfCenter, 0.5f - halfCenter),
            new Vector2(0.5f + halfCenter, 0.5f + halfCenter)
        );

        TMP_Text marker = CreateText(
            "Marker",
            cellRoot,
            string.Empty,
            34f,
            TextAlignmentOptions.Center,
            textColor,
            FontStyles.Bold
        );
        Stretch(marker.rectTransform, Vector2.zero, Vector2.one);
        marker.enableAutoSizing = true;
        marker.fontSizeMin = 16f;
        marker.fontSizeMax = 42f;

        Image[] borders = new Image[4];
        borders[0] = CreateImage("SelectionTop", cellRoot, accentColor);
        Stretch(borders[0].rectTransform, new Vector2(0f, 0.984f), Vector2.one);
        borders[1] = CreateImage("SelectionRight", cellRoot, accentColor);
        Stretch(borders[1].rectTransform, new Vector2(0.984f, 0f), Vector2.one);
        borders[2] = CreateImage("SelectionBottom", cellRoot, accentColor);
        Stretch(borders[2].rectTransform, Vector2.zero, new Vector2(1f, 0.016f));
        borders[3] = CreateImage("SelectionLeft", cellRoot, accentColor);
        Stretch(borders[3].rectTransform, Vector2.zero, new Vector2(0.016f, 1f));

        for (int i = 0; i < borders.Length; i++)
            borders[i].enabled = false;

        return new CellView
        {
            Root = cellRoot,
            Background = background,
            North = north,
            East = east,
            South = south,
            West = west,
            Center = center,
            SelectionBorders = borders,
            Marker = marker,
        };
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles style
    )
    {
        GameObject gameObject = new(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
        text.text = value ?? string.Empty;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;

        if (terminalFont != null)
            text.font = terminalFont;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject gameObject = new(objectName, typeof(RectTransform));
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject gameObject = new(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private bool IsInside(Vector2Int position)
    {
        return _tiles != null
            && position.x >= 0
            && position.y >= 0
            && position.x < _tiles.GetLength(0)
            && position.y < _tiles.GetLength(1);
    }

    private static Connection DirectionToConnection(Vector2Int direction)
    {
        if (direction == Vector2Int.up)
            return Connection.North;
        if (direction == Vector2Int.right)
            return Connection.East;
        if (direction == Vector2Int.down)
            return Connection.South;
        if (direction == Vector2Int.left)
            return Connection.West;

        return Connection.None;
    }

    private static Connection Opposite(Connection direction)
    {
        return direction switch
        {
            Connection.North => Connection.South,
            Connection.East => Connection.West,
            Connection.South => Connection.North,
            Connection.West => Connection.East,
            _ => Connection.None,
        };
    }

    private static Connection RotateMask(Connection mask, int clockwiseQuarterTurns)
    {
        int turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
        Connection rotated = mask;

        for (int turn = 0; turn < turns; turn++)
        {
            Connection next = Connection.None;

            if ((rotated & Connection.North) != 0)
                next |= Connection.East;
            if ((rotated & Connection.East) != 0)
                next |= Connection.South;
            if ((rotated & Connection.South) != 0)
                next |= Connection.West;
            if ((rotated & Connection.West) != 0)
                next |= Connection.North;

            rotated = next;
        }

        return rotated;
    }

    private static Vector2Int GetCardinalDirection(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
            return Vector2Int.zero;

        if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
            return input.x >= 0f ? Vector2Int.right : Vector2Int.left;

        return input.y >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void StretchToParent(RectTransform rect)
    {
        Stretch(rect, Vector2.zero, Vector2.one);
    }

    private void EnsureSettingsExist()
    {
        easy ??= new RelaySettings();
        hard ??= new RelaySettings();
    }

    private void ApplyTuningMigrations()
    {
        if (tuningVersion >= CurrentTuningVersion)
            return;

        if (tuningVersion < 2)
        {
            // V2 corrects the board aspect so each relay cell is square, adds one
            // column to each difficulty to use the widescreen space cleanly, and
            // gives relay centers an opaque cap so connector arms do not show through.
            if (easy != null && easy.width == 6)
                easy.width = 7;

            if (hard != null && hard.width == 8)
                hard.width = 9;

            if (Mathf.Approximately(connectorThickness, 0.115f))
                connectorThickness = 0.105f;

            if (Mathf.Approximately(centerNodeSize, 0.30f))
                centerNodeSize = 0.27f;

            if (passiveStructureAlpha <= 0.64f)
                passiveStructureAlpha = 0.72f;
        }

        if (tuningVersion < 3)
        {
            // V3 makes both profiles more forgiving and holds the completed route
            // visibly on the board before transitioning to the next relay.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.securitySweepSeconds, 32f))
                    easy.securitySweepSeconds = 38f;
                if (Mathf.Approximately(easy.honeypotPenaltySeconds, 4f))
                    easy.honeypotPenaltySeconds = 3.25f;
                if (Mathf.Approximately(easy.decoyPieceChance, 0.76f))
                    easy.decoyPieceChance = 0.65f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.securitySweepSeconds, 27f))
                    hard.securitySweepSeconds = 34f;
                if (Mathf.Approximately(hard.honeypotPenaltySeconds, 5f))
                    hard.honeypotPenaltySeconds = 4f;
                if (hard.honeypotCount == 2)
                    hard.honeypotCount = 1;
                if (Mathf.Approximately(hard.decoyPieceChance, 0.92f))
                    hard.decoyPieceChance = 0.82f;
            }

            if (Mathf.Approximately(roundTransitionSeconds, 0.8f))
                roundTransitionSeconds = 0.55f;

            if (successfulConnectionHoldSeconds <= 0f)
                successfulConnectionHoldSeconds = 0.9f;
        }


        if (tuningVersion < 4)
        {
            // V4 restores Hard's original challenge structure. Hard keeps its two
            // honeypots, original penalty, and original decoy density; the only
            // difficulty relief retained is the longer security sweep timer.
            if (hard != null)
            {
                if (hard.honeypotCount == 1)
                    hard.honeypotCount = 2;
                if (Mathf.Approximately(hard.honeypotPenaltySeconds, 4f))
                    hard.honeypotPenaltySeconds = 5f;
                if (Mathf.Approximately(hard.decoyPieceChance, 0.82f))
                    hard.decoyPieceChance = 0.92f;
                if (Mathf.Approximately(hard.securitySweepSeconds, 27f))
                    hard.securitySweepSeconds = 34f;
            }
        }


        if (tuningVersion < 5)
        {
            // V5 introduced an animated success connection.
            if (Mathf.Approximately(successfulConnectionHoldSeconds, 0.9f))
                successfulConnectionHoldSeconds = 0.18f;

            if (connectionAnimationStepSeconds <= 0f)
                connectionAnimationStepSeconds = 0.065f;
        }

        if (tuningVersion < 6)
        {
            // V6 restores immediate successful-route transitions. The animation
            // settings remain serialized only for backwards prefab compatibility.
        }

        if (tuningVersion < 7)
        {
            // V7 makes Easy meaningfully less forgiving while keeping it below Hard,
            // and adds generation validation plus a bounded solution-turn target so
            // every board is guaranteed solvable and Easy boards cannot roll trivial.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.securitySweepSeconds, 38f))
                    easy.securitySweepSeconds = 35f;
                if (Mathf.Approximately(easy.honeypotPenaltySeconds, 3.25f))
                    easy.honeypotPenaltySeconds = 4f;
                if (Mathf.Approximately(easy.decoyPieceChance, 0.65f))
                    easy.decoyPieceChance = 0.76f;
                easy.minimumSolutionTurns = 6;
                easy.maximumSolutionTurns = 18;
            }

            if (hard != null)
            {
                hard.minimumSolutionTurns = 10;
                hard.maximumSolutionTurns = 28;
            }
        }

        if (tuningVersion < 8)
        {
            // V8 raises both difficulty profiles moderately and changes the shell's
            // overall progress bar to advance only after completed rounds. This
            // removes the per-piece alignment hint that encouraged blind rotation.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.securitySweepSeconds, 35f))
                    easy.securitySweepSeconds = 33f;
                if (Mathf.Approximately(easy.honeypotPenaltySeconds, 4f))
                    easy.honeypotPenaltySeconds = 4.5f;
                if (Mathf.Approximately(easy.decoyPieceChance, 0.76f))
                    easy.decoyPieceChance = 0.82f;
                if (easy.minimumSolutionTurns == 6)
                    easy.minimumSolutionTurns = 8;
                if (easy.maximumSolutionTurns == 18)
                    easy.maximumSolutionTurns = 22;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.securitySweepSeconds, 34f))
                    hard.securitySweepSeconds = 31f;
                if (Mathf.Approximately(hard.honeypotPenaltySeconds, 5f))
                    hard.honeypotPenaltySeconds = 5.5f;
                if (Mathf.Approximately(hard.decoyPieceChance, 0.92f))
                    hard.decoyPieceChance = 0.95f;
                if (hard.minimumSolutionTurns == 10)
                    hard.minimumSolutionTurns = 13;
                if (hard.maximumSolutionTurns == 28)
                    hard.maximumSolutionTurns = 32;
            }
        }

        if (tuningVersion < 9)
        {
            // V9 adds stronger generated-path verification and resumable sessions.
            // No difficulty values are changed.
        }

        if (tuningVersion < 10)
        {
            // V10 makes Hard slightly more forgiving only by extending the
            // security sweep. Board size, route complexity, decoys, penalties,
            // and both honeypots remain unchanged.
            if (hard != null && Mathf.Approximately(hard.securitySweepSeconds, 31f))
                hard.securitySweepSeconds = 34f;
        }

        tuningVersion = CurrentTuningVersion;
    }

    private static void SanitizeSettings(RelaySettings settings)
    {
        settings.width = Mathf.Clamp(settings.width, 4, 12);
        settings.height = Mathf.Clamp(settings.height, 4, 9);
        settings.roundsRequired = Mathf.Clamp(settings.roundsRequired, 1, 5);
        settings.securitySweepSeconds = Mathf.Max(5f, settings.securitySweepSeconds);
        settings.honeypotPenaltySeconds = Mathf.Max(0f, settings.honeypotPenaltySeconds);
        settings.honeypotCount = Mathf.Clamp(settings.honeypotCount, 0, 5);
        settings.decoyPieceChance = Mathf.Clamp01(settings.decoyPieceChance);
        settings.minimumSolutionTurns = Mathf.Clamp(settings.minimumSolutionTurns, 0, 48);
        settings.maximumSolutionTurns = Mathf.Clamp(
            Mathf.Max(settings.minimumSolutionTurns, settings.maximumSolutionTurns),
            settings.minimumSolutionTurns,
            64
        );
        settings.movementRepeatSeconds = Mathf.Max(0.03f, settings.movementRepeatSeconds);
        settings.movementInitialRepeatDelay = Mathf.Max(0f, settings.movementInitialRepeatDelay);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureSettingsExist();
        ApplyTuningMigrations();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        navigationDeadzone = Mathf.Clamp(navigationDeadzone, 0.05f, 0.95f);
        connectorThickness = Mathf.Clamp(connectorThickness, 0.02f, 0.22f);
        centerNodeSize = Mathf.Clamp(centerNodeSize, 0.15f, 0.55f);
        cellPaddingPixels = Mathf.Clamp(cellPaddingPixels, 0f, 8f);
        passiveStructureAlpha = Mathf.Clamp(passiveStructureAlpha, 0.05f, 1f);
        successfulConnectionHoldSeconds = Mathf.Max(0f, successfulConnectionHoldSeconds);
        connectionAnimationStepSeconds = Mathf.Max(0.02f, connectionAnimationStepSeconds);
        roundTransitionSeconds = Mathf.Max(0f, roundTransitionSeconds);
        failureHoldSeconds = Mathf.Max(0f, failureHoldSeconds);
        warningMessageSeconds = Mathf.Max(0f, warningMessageSeconds);
    }
#endif
}

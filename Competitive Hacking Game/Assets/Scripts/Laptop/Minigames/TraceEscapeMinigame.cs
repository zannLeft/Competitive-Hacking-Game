using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TraceEscapeMinigame : LaptopMinigameBase
{
    private const int CurrentTuningVersion = 32;

    [SerializeField, HideInInspector]
    private int tuningVersion;

    [Serializable]
    private sealed class TraceSettings
    {
        [Min(9)]
        public int mazeWidth = 25;

        [Min(9)]
        public int mazeHeight = 13;

        [Min(1)]
        public int accessKeyCount = 2;

        [Min(0f)]
        public float traceStartDelay = 2.8f;

        [Min(0.03f)]
        public float traceStepSeconds = 0.18f;

        [Min(0.03f)]
        public float movementRepeatSeconds = 0.105f;

        [Min(0f)]
        public float movementInitialRepeatDelay = 0.18f;

        [Range(0, 8)]
        public int visibleTraceTrailLength = 3;

        [Range(0, 24)]
        public int loopOpenings = 6;

        [Min(2)]
        public int maximumKeyDetour = 12;

        [Min(1)]
        public int minimumKeySpacing = 5;
    }

    private enum GameState
    {
        AwaitingStart,
        Playing,
        FailureHold,
        RoundTransition,
    }

    private enum CellVisualKind
    {
        None,
        Wall,
        Player,
        Trace,
        TraceTrail,
        Key,
        ExitLocked,
        ExitOpen,
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

    private readonly struct KeyCandidate
    {
        public KeyCandidate(Vector2Int cell, int startDistance, int detour)
        {
            Cell = cell;
            StartDistance = startDistance;
            Detour = detour;
        }

        public Vector2Int Cell { get; }
        public int StartDistance { get; }
        public int Detour { get; }
    }

    [Header("Difficulty")]
    [SerializeField]
    private TraceSettings easy = new()
    {
        mazeWidth = 23,
        mazeHeight = 15,
        accessKeyCount = 2,
        traceStartDelay = 3.0f,
        traceStepSeconds = 0.185f,
        movementRepeatSeconds = 0.105f,
        movementInitialRepeatDelay = 0.18f,
        visibleTraceTrailLength = 3,
        loopOpenings = 10,
        maximumKeyDetour = 10,
        minimumKeySpacing = 4,
    };

    [SerializeField]
    private TraceSettings hard = new()
    {
        mazeWidth = 29,
        mazeHeight = 17,
        accessKeyCount = 2,
        traceStartDelay = 2.65f,
        traceStepSeconds = 0.17f,
        movementRepeatSeconds = 0.09f,
        movementInitialRepeatDelay = 0.15f,
        visibleTraceTrailLength = 4,
        loopOpenings = 9,
        maximumKeyDetour = 13,
        minimumKeySpacing = 4,
    };

    [Header("Input")]
    [SerializeField, Range(0.05f, 0.95f)]
    private float navigationDeadzone = 0.35f;

    [Header("Series")]
    [Tooltip("Number of deterministic mazes that must be cleared before the router hack completes.")]
    [SerializeField, Range(1, 5)]
    private int roundsRequired = 3;

    [SerializeField, Min(0f)]
    private float roundTransitionSeconds = 0.75f;

    [Header("Result Timing")]
    [SerializeField, Min(0f)]
    private float failureHoldSeconds = 0.9f;

    [Header("Hacker OS Visuals")]
    [Tooltip("Assign the same monospace TMP font used by Firewall Runner.")]
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

    [SerializeField]
    private string playerGlyph = "■";

    [SerializeField, Min(-0.5f)]
    private float playerGlyphVerticalOffsetEm = 0.08f;

    [SerializeField]
    private string wallGlyph = "□";

    [SerializeField]
    private string keyGlyph = "K";

    [SerializeField]
    private string lockedExitGlyph = "X";

    [SerializeField]
    private string openExitGlyph = ">";

    [SerializeField]
    private string traceGlyph = "#";

    [SerializeField]
    private string traceTrailGlyph = ":";

    [Header("Layout")]
    [SerializeField, HideInInspector]
    private float separatorThickness = 8f;

    [SerializeField, Range(20f, 72f)]
    private float mazeMinimumFontSize = 30f;

    [SerializeField, Range(24f, 96f)]
    private float mazeMaximumFontSize = 64f;

    [SerializeField, Range(-30f, 30f)]
    private float mazeLineSpacing = 0f;

    [Tooltip("Forces every maze cell to use the same wider horizontal advance, keeping vertical corridors visually open and the terminal grid aligned.")]
    [SerializeField, Range(0.4f, 1.2f)]
    private float mazeCellWidthEm = 0.82f;

    [Tooltip("Forces a stable line height so block glyphs cannot visually spill into neighbouring maze rows.")]
    [SerializeField, Range(60f, 140f)]
    private float mazeLineHeightPercent = 75f;

    [Tooltip("Enlarges only the hollow wall glyph inside each fixed terminal cell so adjacent squares read more like continuous maze walls.")]
    [SerializeField, Range(100f, 175f)]
    private float wallGlyphScalePercent = 150f;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };

    private readonly List<Vector2Int> _openCells = new();
    private readonly List<Vector2Int> _keyPositions = new();
    private readonly HashSet<Vector2Int> _remainingKeys = new();
    private readonly List<Vector2Int> _traceHistory = new();
    private readonly Queue<Vector2Int> _generationStack = new();
    private readonly Queue<Vector2Int> _cycleSearchQueue = new();
    private readonly HashSet<Vector2Int> _cycleVisited = new();
    private readonly Queue<Vector2Int> _traceSearchQueue = new();
    private readonly Dictionary<Vector2Int, Vector2Int> _traceParents = new();

    private RectTransform _root;
    private TMP_Text _mazeText;
    private LaptopMinigameVisualShell _shell;

    private LaptopMinigameContext _context;
    private TraceSettings _settings;
    private bool[,] _walls;
    private Vector2Int _startPosition;
    private Vector2Int _exitPosition;
    private Vector2Int _playerPosition;
    private Vector2Int _tracePosition;
    private Vector2 _navigationInput;
    private Vector2Int _heldDirection;
    private float _elapsed;
    private float _traceStepTimer;
    private float _movementRepeatTimer;
    private float _failureTimer;
    private float _roundTransitionTimer;
    private int _currentRoundIndex;
    private bool _traceVisible;
    private bool _traceActivatedByPlayerMove;
    private bool _suppressMovementUntilNeutral;
    private GameState _state;

    private string _wallColorTag;
    private string _playerColorTag;
    private string _traceColorTag;
    private string _traceTrailColorTag;
    private string _keyColorTag;
    private string _lockedExitColorTag;
    private string _openExitColorTag;

    protected override void OnBegin(LaptopMinigameContext context)
    {
        EnsureDifficultySettingsExist();
        ApplyVersionedTuning();
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;

        SanitizeSettings(_settings);
        _currentRoundIndex = 0;
        StartCurrentRound(requireNavigationRelease: false);
        _state = GameState.AwaitingStart;
        _shell?.SetBriefingVisible(true);
        _shell?.SetProgress(0f);
        _shell?.SetStatus("READY", textColor);
    }

    protected override void OnJumpPressed()
    {
        if (_state != GameState.AwaitingStart)
            return;

        TriggerActionPerformed();
        ResetRun(requireNavigationRelease: true);
        _shell?.SetBriefingVisible(false);
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
        TryMovePlayer(direction);
    }

    protected override void OnAbort()
    {
        _navigationInput = Vector2.zero;
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _roundTransitionTimer = 0f;
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

        if (_state == GameState.AwaitingStart)
            return;

        float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);

        if (_state == GameState.FailureHold)
        {
            _failureTimer -= deltaTime;

            if (_failureTimer <= 0f)
                ResetRun(requireNavigationRelease: true);

            return;
        }

        if (_state == GameState.RoundTransition)
        {
            _roundTransitionTimer -= deltaTime;

            if (_roundTransitionTimer <= 0f)
                StartCurrentRound(requireNavigationRelease: true);

            return;
        }

        _elapsed += deltaTime;
        UpdateHeldMovement(deltaTime);
        UpdateTrace(deltaTime);
    }

    private void UpdateHeldMovement(float deltaTime)
    {
        if (_suppressMovementUntilNeutral || _heldDirection == Vector2Int.zero)
            return;

        _movementRepeatTimer -= deltaTime;

        if (_movementRepeatTimer > 0f)
            return;

        _movementRepeatTimer += Mathf.Max(0.03f, _settings.movementRepeatSeconds);
        TryMovePlayer(_heldDirection);
    }

    private void UpdateTrace(float deltaTime)
    {
        if (!_traceActivatedByPlayerMove)
            return;

        if (_elapsed < _settings.traceStartDelay)
            return;

        _traceStepTimer -= deltaTime;

        while (_traceStepTimer <= 0f && _state == GameState.Playing)
        {
            _traceStepTimer += Mathf.Max(0.03f, _settings.traceStepSeconds);
            AdvanceTrace();
        }
    }

    private void AdvanceTrace()
    {
        if (_tracePosition == _playerPosition)
        {
            _traceVisible = true;
            BeginFailureHold();
            return;
        }

        Vector2Int next = FindNextShortestPathStep(
            _tracePosition,
            _playerPosition
        );

        if (next == _tracePosition)
            return;

        _tracePosition = next;
        _traceVisible = true;
        _traceHistory.Add(_tracePosition);

        int historyLimit = Mathf.Max(
            8,
            _settings.visibleTraceTrailLength + 6
        );

        if (_traceHistory.Count > historyLimit)
            _traceHistory.RemoveAt(0);

        if (_tracePosition == _playerPosition)
        {
            BeginFailureHold();
            return;
        }

        RefreshMazeText();
    }

    private Vector2Int FindNextShortestPathStep(
        Vector2Int origin,
        Vector2Int destination
    )
    {
        if (origin == destination)
            return origin;

        _traceSearchQueue.Clear();
        _traceParents.Clear();
        _traceSearchQueue.Enqueue(origin);
        _traceParents[origin] = origin;

        bool found = false;

        while (_traceSearchQueue.Count > 0 && !found)
        {
            Vector2Int current = _traceSearchQueue.Dequeue();

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int next = current + CardinalDirections[i];

                if (!IsOpen(next) || _traceParents.ContainsKey(next))
                    continue;

                _traceParents[next] = current;

                if (next == destination)
                {
                    found = true;
                    break;
                }

                _traceSearchQueue.Enqueue(next);
            }
        }

        if (!found)
            return origin;

        Vector2Int step = destination;

        while (
            _traceParents.TryGetValue(step, out Vector2Int parent)
            && parent != origin
            && parent != step
        )
        {
            step = parent;
        }

        return step;
    }

    private void TryMovePlayer(Vector2Int direction)
    {
        if (_state != GameState.Playing || direction == Vector2Int.zero)
            return;

        Vector2Int destination = _playerPosition + direction;

        if (!IsOpen(destination))
            return;

        _playerPosition = destination;

        if (!_traceActivatedByPlayerMove)
        {
            // The security trace stays completely dormant until the player makes
            // their first successful grid step. Its normal start delay begins at
            // that moment, so waiting on the opening cell cannot consume the grace period.
            _traceActivatedByPlayerMove = true;
            _elapsed = 0f;
            _traceStepTimer = Mathf.Max(0.03f, _settings.traceStepSeconds);
        }

        // Each accepted grid step is a physical laptop keypress. The existing
        // PlayerLaptopHacker audio path plays it instantly for the owner and
        // relays it positionally to the other clients.
        TriggerActionPerformed();

        if (_remainingKeys.Remove(_playerPosition))
            UpdateStatusText();

        if (_traceVisible && _playerPosition == _tracePosition)
        {
            BeginFailureHold();
            return;
        }

        if (_playerPosition == _exitPosition && _remainingKeys.Count == 0)
        {
            CompleteCurrentRound();
            return;
        }

        RefreshMazeText();
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
        ShowResultOverlay(
            "TRACE COLLISION\nIDENTITY ROUTE EXPOSED\n\nREBUILDING SESSION...",
            dangerColor
        );
    }

    private void CompleteCurrentRound()
    {
        if (_state != GameState.Playing)
            return;

        int completedRoundNumber = _currentRoundIndex + 1;

        if (completedRoundNumber >= roundsRequired)
        {
            _shell?.SetProgress(1f);
            CompleteMinigame();
            return;
        }

        _currentRoundIndex++;
        _state = GameState.RoundTransition;
        _roundTransitionTimer = Mathf.Max(0f, roundTransitionSeconds);
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _suppressMovementUntilNeutral = true;

        _shell?.SetProgress(completedRoundNumber / (float)roundsRequired);
        ShowResultOverlay(
            $"ACCESS LAYER {completedRoundNumber}/{roundsRequired} CLEARED\n\nMAPPING NEXT LAYER...",
            accentColor
        );
    }

    private void StartCurrentRound(bool requireNavigationRelease)
    {
        GenerateMaze(DeriveRoundSeed(_context.Seed, _currentRoundIndex));
        ResetRun(requireNavigationRelease);
    }

    private static int DeriveRoundSeed(int baseSeed, int roundIndex)
    {
        if (roundIndex <= 0)
            return baseSeed;

        unchecked
        {
            uint value = (uint)baseSeed;
            value ^= 0x9E3779B9u * (uint)(roundIndex + 1);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private void ResetRun(bool requireNavigationRelease)
    {
        _state = GameState.Playing;
        _playerPosition = _startPosition;
        _tracePosition = _startPosition;
        _traceVisible = false;
        _traceActivatedByPlayerMove = false;
        _elapsed = 0f;
        _traceStepTimer = Mathf.Max(0.03f, _settings.traceStepSeconds);
        _movementRepeatTimer = 0f;
        _failureTimer = 0f;
        _roundTransitionTimer = 0f;
        _heldDirection = Vector2Int.zero;
        _suppressMovementUntilNeutral =
            requireNavigationRelease
            && _navigationInput.sqrMagnitude >= navigationDeadzone * navigationDeadzone;

        _traceHistory.Clear();
        _traceHistory.Add(_tracePosition);

        _remainingKeys.Clear();
        for (int i = 0; i < _keyPositions.Count; i++)
            _remainingKeys.Add(_keyPositions[i]);

        string difficulty = _context.Difficulty.ToString().ToUpperInvariant();
        _shell?.SetContext(_context.NetworkDisplayName, difficulty);
        _shell?.HideResult();
        _shell?.SetFooterLeft("COLLECT KEYS  ·  REACH THE EXIT");

        UpdateStatusText();
        RefreshMazeText();
    }

    private void GenerateMaze(int seed)
    {
        int width = MakeOdd(Mathf.Max(9, _settings.mazeWidth));
        int height = MakeOdd(Mathf.Max(9, _settings.mazeHeight));
        _walls = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                _walls[x, y] = true;
        }

        var random = new DeterministicRandom(seed);
        _startPosition = new Vector2Int(1, 1);
        CarvePerfectMaze(random, width, height);
        AddLoopOpenings(random, width, height, _settings.loopOpenings);
        RebuildOpenCellList(width, height);

        Dictionary<Vector2Int, int> distances = BuildDistances(_startPosition);
        _exitPosition = FindFarthestCell(distances, _startPosition);
        PlaceAccessKeys(random, distances, _settings.accessKeyCount);
        CacheColorTags();
    }

    private void CarvePerfectMaze(
        DeterministicRandom random,
        int width,
        int height
    )
    {
        var stack = new List<Vector2Int>(width * height / 4);
        Vector2Int current = _startPosition;
        _walls[current.x, current.y] = false;
        stack.Add(current);

        var candidates = new List<Vector2Int>(4);

        while (stack.Count > 0)
        {
            current = stack[stack.Count - 1];
            candidates.Clear();

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int candidate = current + CardinalDirections[i] * 2;

                if (
                    candidate.x <= 0
                    || candidate.y <= 0
                    || candidate.x >= width - 1
                    || candidate.y >= height - 1
                )
                {
                    continue;
                }

                if (_walls[candidate.x, candidate.y])
                    candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Vector2Int next = candidates[random.Range(0, candidates.Count)];
            Vector2Int between = (current + next) / 2;
            _walls[between.x, between.y] = false;
            _walls[next.x, next.y] = false;
            stack.Add(next);
        }
    }

    private void AddLoopOpenings(
        DeterministicRandom random,
        int width,
        int height,
        int requestedOpenings
    )
    {
        if (requestedOpenings <= 0)
            return;

        var candidates = new List<Vector2Int>();

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (!_walls[x, y])
                    continue;

                bool horizontalConnector =
                    !_walls[x - 1, y]
                    && !_walls[x + 1, y]
                    && _walls[x, y - 1]
                    && _walls[x, y + 1];

                bool verticalConnector =
                    !_walls[x, y - 1]
                    && !_walls[x, y + 1]
                    && _walls[x - 1, y]
                    && _walls[x + 1, y];

                if (horizontalConnector || verticalConnector)
                    candidates.Add(new Vector2Int(x, y));
            }
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Range(0, i + 1);
            (candidates[i], candidates[swapIndex]) =
                (candidates[swapIndex], candidates[i]);
        }

        int opened = 0;

        for (int i = 0; i < candidates.Count && opened < requestedOpenings; i++)
        {
            Vector2Int cell = candidates[i];

            if (!_walls[cell.x, cell.y])
                continue;

            bool horizontalConnector =
                !_walls[cell.x - 1, cell.y]
                && !_walls[cell.x + 1, cell.y];
            bool verticalConnector =
                !_walls[cell.x, cell.y - 1]
                && !_walls[cell.x, cell.y + 1];

            if (!horizontalConnector && !verticalConnector)
                continue;

            _walls[cell.x, cell.y] = false;
            opened++;
        }
    }

    private void RebuildOpenCellList(int width, int height)
    {
        _openCells.Clear();

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (!_walls[x, y])
                    _openCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private Dictionary<Vector2Int, int> BuildDistances(Vector2Int origin)
    {
        var distances = new Dictionary<Vector2Int, int>(_openCells.Count)
        {
            [origin] = 0,
        };

        _generationStack.Clear();
        _generationStack.Enqueue(origin);

        while (_generationStack.Count > 0)
        {
            Vector2Int current = _generationStack.Dequeue();
            int nextDistance = distances[current] + 1;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int next = current + CardinalDirections[i];

                if (!IsOpen(next) || distances.ContainsKey(next))
                    continue;

                distances[next] = nextDistance;
                _generationStack.Enqueue(next);
            }
        }

        return distances;
    }

    private static Vector2Int FindFarthestCell(
        Dictionary<Vector2Int, int> distances,
        Vector2Int fallback
    )
    {
        Vector2Int farthest = fallback;
        int farthestDistance = -1;

        foreach (KeyValuePair<Vector2Int, int> pair in distances)
        {
            if (pair.Value <= farthestDistance)
                continue;

            farthestDistance = pair.Value;
            farthest = pair.Key;
        }

        return farthest;
    }

    private void PlaceAccessKeys(
        DeterministicRandom random,
        Dictionary<Vector2Int, int> distancesFromStart,
        int requestedCount
    )
    {
        _keyPositions.Clear();

        int availableCount = Mathf.Max(1, _openCells.Count - 2);
        int count = Mathf.Clamp(requestedCount, 1, availableCount);
        List<Vector2Int> solutionPath = BuildSolutionPath(distancesFromStart);
        var directPathCells = new HashSet<Vector2Int>(solutionPath);
        Dictionary<Vector2Int, int> distancesToExit = BuildDistances(_exitPosition);

        int directDistance = distancesFromStart.TryGetValue(
            _exitPosition,
            out int foundDirectDistance
        )
            ? foundDirectDistance
            : Mathf.Max(1, solutionPath.Count - 1);

        var candidates = new List<KeyCandidate>();
        CollectKeyCandidates(
            candidates,
            distancesFromStart,
            distancesToExit,
            directPathCells,
            directDistance,
            requireCycle: true,
            maximumDetour: _settings.maximumKeyDetour
        );

        // Small custom mazes may not contain enough loop cells. Expand the
        // search gradually while still preferring off-route, non-dead-end cells.
        if (candidates.Count < count)
        {
            CollectKeyCandidates(
                candidates,
                distancesFromStart,
                distancesToExit,
                directPathCells,
                directDistance,
                requireCycle: false,
                maximumDetour: _settings.maximumKeyDetour * 2
            );
        }

        candidates.Sort((a, b) =>
        {
            int distanceCompare = a.StartDistance.CompareTo(b.StartDistance);
            if (distanceCompare != 0)
                return distanceCompare;

            int detourCompare = a.Detour.CompareTo(b.Detour);
            if (detourCompare != 0)
                return detourCompare;

            int xCompare = a.Cell.x.CompareTo(b.Cell.x);
            return xCompare != 0 ? xCompare : a.Cell.y.CompareTo(b.Cell.y);
        });

        // Spread the keys outward through the maze. Each selected key is off the
        // direct route when possible, lies on a loop when possible, and adds only
        // a bounded detour. This makes exploration meaningful without requiring
        // an impossible retreat through a one-cell dead end.
        for (int keyIndex = 0; keyIndex < count && candidates.Count > 0; keyIndex++)
        {
            float targetFraction = (keyIndex + 1f) / (count + 1f);
            int targetDistance = Mathf.RoundToInt(directDistance * targetFraction);
            int selectedIndex = FindBestKeyCandidateIndex(
                candidates,
                targetDistance,
                _settings.minimumKeySpacing,
                ref random
            );

            if (selectedIndex < 0)
                selectedIndex = 0;

            _keyPositions.Add(candidates[selectedIndex].Cell);
            candidates.RemoveAt(selectedIndex);
        }

        // Final fallback: keep the game playable even with deliberately tiny or
        // heavily customized maze settings.
        for (int i = 0; _keyPositions.Count < count && i < solutionPath.Count; i++)
        {
            Vector2Int cell = solutionPath[i];

            if (
                cell == _startPosition
                || cell == _exitPosition
                || _keyPositions.Contains(cell)
                || !IsFarEnoughFromSelected(cell, 2)
            )
            {
                continue;
            }

            _keyPositions.Add(cell);
        }
    }

    private void CollectKeyCandidates(
        List<KeyCandidate> destination,
        Dictionary<Vector2Int, int> distancesFromStart,
        Dictionary<Vector2Int, int> distancesToExit,
        HashSet<Vector2Int> directPathCells,
        int directDistance,
        bool requireCycle,
        int maximumDetour
    )
    {
        var existing = new HashSet<Vector2Int>();

        for (int i = 0; i < destination.Count; i++)
            existing.Add(destination[i].Cell);

        for (int i = 0; i < _openCells.Count; i++)
        {
            Vector2Int cell = _openCells[i];

            if (
                cell == _startPosition
                || cell == _exitPosition
                || directPathCells.Contains(cell)
                || existing.Contains(cell)
                || CountOpenNeighbours(cell) < 2
                || !distancesFromStart.TryGetValue(cell, out int startDistance)
                || !distancesToExit.TryGetValue(cell, out int exitDistance)
                || startDistance < 4
                || exitDistance < 3
            )
            {
                continue;
            }

            int detour = startDistance + exitDistance - directDistance;

            if (detour < 1 || detour > maximumDetour)
                continue;

            if (requireCycle && !IsCellOnCycle(cell))
                continue;

            destination.Add(new KeyCandidate(cell, startDistance, detour));
            existing.Add(cell);
        }
    }

    private int FindBestKeyCandidateIndex(
        List<KeyCandidate> candidates,
        int targetDistance,
        int minimumSpacing,
        ref DeterministicRandom random
    )
    {
        int bestIndex = -1;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            KeyCandidate candidate = candidates[i];

            if (!IsFarEnoughFromSelected(candidate.Cell, minimumSpacing))
                continue;

            float score =
                Mathf.Abs(candidate.StartDistance - targetDistance)
                + candidate.Detour * 0.35f
                + random.Next01() * 0.75f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex >= 0)
            return bestIndex;

        // Relax only the spacing requirement, never the reachability checks that
        // produced the candidate list.
        for (int i = 0; i < candidates.Count; i++)
        {
            KeyCandidate candidate = candidates[i];
            float score =
                Mathf.Abs(candidate.StartDistance - targetDistance)
                + candidate.Detour * 0.35f
                + random.Next01() * 0.75f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        return bestIndex;
    }

    private bool IsFarEnoughFromSelected(Vector2Int cell, int minimumSpacing)
    {
        int squaredMinimum = minimumSpacing * minimumSpacing;

        for (int i = 0; i < _keyPositions.Count; i++)
        {
            Vector2Int difference = _keyPositions[i] - cell;

            if (difference.sqrMagnitude < squaredMinimum)
                return false;
        }

        return true;
    }

    private bool IsCellOnCycle(Vector2Int cell)
    {
        var neighbours = new List<Vector2Int>(4);

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int neighbour = cell + CardinalDirections[i];

            if (IsOpen(neighbour))
                neighbours.Add(neighbour);
        }

        if (neighbours.Count < 2)
            return false;

        for (int first = 0; first < neighbours.Count - 1; first++)
        {
            for (int second = first + 1; second < neighbours.Count; second++)
            {
                if (AreConnectedWithoutCell(neighbours[first], neighbours[second], cell))
                    return true;
            }
        }

        return false;
    }

    private bool AreConnectedWithoutCell(
        Vector2Int origin,
        Vector2Int destination,
        Vector2Int blockedCell
    )
    {
        _cycleSearchQueue.Clear();
        _cycleVisited.Clear();
        _cycleSearchQueue.Enqueue(origin);
        _cycleVisited.Add(origin);

        while (_cycleSearchQueue.Count > 0)
        {
            Vector2Int current = _cycleSearchQueue.Dequeue();

            if (current == destination)
                return true;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int next = current + CardinalDirections[i];

                if (
                    next == blockedCell
                    || !IsOpen(next)
                    || !_cycleVisited.Add(next)
                )
                {
                    continue;
                }

                _cycleSearchQueue.Enqueue(next);
            }
        }

        return false;
    }

    private List<Vector2Int> BuildSolutionPath(
        Dictionary<Vector2Int, int> distances
    )
    {
        var reversePath = new List<Vector2Int>();
        Vector2Int current = _exitPosition;
        reversePath.Add(current);

        while (current != _startPosition)
        {
            if (!distances.TryGetValue(current, out int currentDistance))
                break;

            bool foundPrevious = false;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int previous = current + CardinalDirections[i];

                if (
                    distances.TryGetValue(previous, out int previousDistance)
                    && previousDistance == currentDistance - 1
                )
                {
                    current = previous;
                    reversePath.Add(current);
                    foundPrevious = true;
                    break;
                }
            }

            if (!foundPrevious)
                break;
        }

        reversePath.Reverse();
        return reversePath;
    }

    private int CountOpenNeighbours(Vector2Int cell)
    {
        int count = 0;

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (IsOpen(cell + CardinalDirections[i]))
                count++;
        }

        return count;
    }

    private Vector2Int GetCardinalDirection(Vector2 input)
    {
        if (input.sqrMagnitude < navigationDeadzone * navigationDeadzone)
            return Vector2Int.zero;

        if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
            return input.x >= 0f ? Vector2Int.right : Vector2Int.left;

        return input.y >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private bool IsOpen(Vector2Int position)
    {
        if (_walls == null)
            return false;

        int width = _walls.GetLength(0);
        int height = _walls.GetLength(1);

        return position.x >= 0
            && position.y >= 0
            && position.x < width
            && position.y < height
            && !_walls[position.x, position.y];
    }

    private void UpdateStatusText()
    {
        int total = _keyPositions.Count;
        int collected = total - _remainingKeys.Count;
        bool exitOpen = _remainingKeys.Count == 0;

        _shell?.SetStatus(
            $"R{_currentRoundIndex + 1}/{roundsRequired}  KEYS {collected}/{total}",
            exitOpen ? accentColor : textColor
        );
        _shell?.SetFooterLeft(
            exitOpen
                ? "EXIT OPEN  ·  REACH THE GATE"
                : "COLLECT KEYS  ·  REACH THE EXIT"
        );

        float keyProgress = total > 0 ? collected / (float)total : 0f;
        float roundProgress = exitOpen ? 0.88f : keyProgress * 0.82f;
        float totalProgress =
            (_currentRoundIndex + roundProgress)
            / Mathf.Max(1f, roundsRequired);
        _shell?.SetProgress(totalProgress);
    }

    private void RefreshMazeText()
    {
        if (_mazeText == null || _walls == null)
            return;

        int width = _walls.GetLength(0);
        int height = _walls.GetLength(1);
        var builder = new StringBuilder(width * height * 8);
        CellVisualKind activeKind = CellVisualKind.None;

        // U+2588 can have different glyph metrics when TMP retrieves it from a
        // dynamic atlas or fallback. Force the entire board onto an explicit
        // terminal cell grid so walls, spaces, @, K, X and trace characters
        // always occupy exactly one logical column and one logical row.
        builder.Append("<mspace=");
        builder.Append(mazeCellWidthEm.ToString("0.###", CultureInfo.InvariantCulture));
        builder.Append("em><line-height=");
        builder.Append(mazeLineHeightPercent.ToString("0.###", CultureInfo.InvariantCulture));
        builder.Append("%>");

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                GetCellVisual(cell, out string glyph, out CellVisualKind kind);

                if (kind != activeKind)
                {
                    if (activeKind != CellVisualKind.None)
                        builder.Append("</color>");

                    builder.Append(GetColorTag(kind));
                    activeKind = kind;
                }

                if (kind == CellVisualKind.Wall && glyph != " ")
                {
                    builder.Append("<size=");
                    builder.Append(
                        wallGlyphScalePercent.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture
                        )
                    );
                    builder.Append("%>");
                    builder.Append(glyph);
                    builder.Append("</size>");
                }
                else
                {
                    builder.Append(glyph);
                }
            }

            if (activeKind != CellVisualKind.None)
            {
                builder.Append("</color>");
                activeKind = CellVisualKind.None;
            }

            if (y > 0)
                builder.Append('\n');
        }

        builder.Append("</line-height></mspace>");
        _mazeText.text = builder.ToString();
    }

    private void GetCellVisual(
        Vector2Int cell,
        out string glyph,
        out CellVisualKind kind
    )
    {
        if (cell == _playerPosition)
        {
            glyph = WrapGlyphWithVerticalOffset(playerGlyph, playerGlyphVerticalOffsetEm);
            kind = CellVisualKind.Player;
            return;
        }

        if (_traceVisible && cell == _tracePosition)
        {
            glyph = traceGlyph;
            kind = CellVisualKind.Trace;
            return;
        }

        if (IsTraceTrailCell(cell))
        {
            glyph = traceTrailGlyph;
            kind = CellVisualKind.TraceTrail;
            return;
        }

        if (_remainingKeys.Contains(cell))
        {
            glyph = keyGlyph;
            kind = CellVisualKind.Key;
            return;
        }

        if (cell == _exitPosition)
        {
            bool unlocked = _remainingKeys.Count == 0;
            glyph = unlocked ? openExitGlyph : lockedExitGlyph;
            kind = unlocked ? CellVisualKind.ExitOpen : CellVisualKind.ExitLocked;
            return;
        }

        if (_walls[cell.x, cell.y])
        {
            glyph = wallGlyph;
            kind = CellVisualKind.Wall;
            return;
        }

        glyph = " ";
        kind = CellVisualKind.Wall;
    }

    private bool IsTraceTrailCell(Vector2Int cell)
    {
        if (
            !_traceVisible
            || _settings.visibleTraceTrailLength <= 0
            || _traceHistory.Count <= 1
        )
        {
            return false;
        }

        int lastExclusive = _traceHistory.Count - 1;
        int firstIndex = Mathf.Max(
            0,
            lastExclusive - _settings.visibleTraceTrailLength
        );

        for (int i = firstIndex; i < lastExclusive; i++)
        {
            if (_traceHistory[i] == cell)
                return true;
        }

        return false;
    }

    private static string WrapGlyphWithVerticalOffset(string glyph, float offsetEm)
    {
        if (string.IsNullOrEmpty(glyph))
            return string.Empty;

        if (Mathf.Abs(offsetEm) <= 0.0001f)
            return glyph;

        return string.Concat(
            "<voffset=",
            offsetEm.ToString("0.###", CultureInfo.InvariantCulture),
            "em>",
            glyph,
            "</voffset>"
        );
    }

    private string GetColorTag(CellVisualKind kind)
    {
        return kind switch
        {
            CellVisualKind.Player => _playerColorTag,
            CellVisualKind.Trace => _traceColorTag,
            CellVisualKind.TraceTrail => _traceTrailColorTag,
            CellVisualKind.Key => _keyColorTag,
            CellVisualKind.ExitLocked => _lockedExitColorTag,
            CellVisualKind.ExitOpen => _openExitColorTag,
            _ => _wallColorTag,
        };
    }

    private void CacheColorTags()
    {
        _wallColorTag = BuildColorTag(structureColor);
        _playerColorTag = BuildColorTag(accentColor);
        _traceColorTag = BuildColorTag(dangerColor);
        _traceTrailColorTag = BuildColorTag(WithAlpha(dangerColor, 0.58f));
        _keyColorTag = BuildColorTag(objectiveColor);
        _lockedExitColorTag = BuildColorTag(WithAlpha(mutedTextColor, 0.70f));
        _openExitColorTag = BuildColorTag(accentColor);
    }

    private static string BuildColorTag(Color color)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>";
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
            "TRACE ESCAPE",
            "Map a route before corporate security closes in on the session.",
            "COLLECT K KEYS. X BECOMES >. REACH THE EXIT.",
            "WASD  /  MOVE",
            "COLLECT K KEYS  ·  REACH THE EXIT"
        );


        _mazeText = CreateText(
            "Maze",
            _shell.GameArea,
            string.Empty,
            mazeMaximumFontSize,
            TextAlignmentOptions.Center,
            structureColor,
            FontStyles.Normal
        );
        Stretch(
            _mazeText.rectTransform,
            new Vector2(0.008f, 0.008f),
            new Vector2(0.992f, 0.992f)
        );
        _mazeText.enableAutoSizing = true;
        _mazeText.fontSizeMin = mazeMinimumFontSize;
        _mazeText.fontSizeMax = mazeMaximumFontSize;
        _mazeText.lineSpacing = mazeLineSpacing;
        _mazeText.characterSpacing = 0f;
        _mazeText.overflowMode = TextOverflowModes.Masking;
        _mazeText.textWrappingMode = TextWrappingModes.NoWrap;
        _mazeText.richText = true;
    }

    private void ShowResultOverlay(string message, Color color)
    {
        _shell?.ShowResult(message, color);
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private Image CreateImage(string objectName, Transform parent, Color color)
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

    private static int MakeOdd(int value)
    {
        return (value & 1) == 0 ? value + 1 : value;
    }

    private static void SanitizeSettings(TraceSettings settings)
    {
        if (settings == null)
            return;

        settings.mazeWidth = Mathf.Clamp(MakeOdd(Mathf.Max(9, settings.mazeWidth)), 9, 45);
        settings.mazeHeight = Mathf.Clamp(MakeOdd(Mathf.Max(9, settings.mazeHeight)), 9, 25);
        settings.accessKeyCount = Mathf.Max(1, settings.accessKeyCount);
        settings.traceStartDelay = Mathf.Max(0f, settings.traceStartDelay);
        settings.traceStepSeconds = Mathf.Max(0.03f, settings.traceStepSeconds);
        settings.movementRepeatSeconds = Mathf.Max(0.03f, settings.movementRepeatSeconds);
        settings.movementInitialRepeatDelay = Mathf.Max(0f, settings.movementInitialRepeatDelay);
        settings.visibleTraceTrailLength = Mathf.Clamp(settings.visibleTraceTrailLength, 0, 8);
        settings.loopOpenings = Mathf.Clamp(settings.loopOpenings, 0, 24);
        settings.maximumKeyDetour = Mathf.Max(2, settings.maximumKeyDetour);
        settings.minimumKeySpacing = Mathf.Max(1, settings.minimumKeySpacing);
    }

    private void EnsureDifficultySettingsExist()
    {
        easy ??= new TraceSettings
        {
            mazeWidth = 23,
            mazeHeight = 15,
            accessKeyCount = 2,
            traceStartDelay = 3.0f,
            traceStepSeconds = 0.185f,
            movementRepeatSeconds = 0.105f,
            movementInitialRepeatDelay = 0.18f,
            visibleTraceTrailLength = 3,
            loopOpenings = 10,
            maximumKeyDetour = 10,
            minimumKeySpacing = 4,
        };

        hard ??= new TraceSettings
        {
            mazeWidth = 29,
            mazeHeight = 17,
            accessKeyCount = 2,
            traceStartDelay = 2.65f,
            traceStepSeconds = 0.17f,
            movementRepeatSeconds = 0.09f,
            movementInitialRepeatDelay = 0.15f,
            visibleTraceTrailLength = 4,
            loopOpenings = 9,
            maximumKeyDetour = 13,
            minimumKeySpacing = 4,
        };
    }

    private void ApplyVersionedTuning()
    {
        if (tuningVersion >= CurrentTuningVersion)
            return;

        // Preserve custom Inspector tuning: only migrate values that still match
        // the original Trace Escape release defaults.
        if (hard != null)
        {
            if (Mathf.Approximately(hard.traceStartDelay, 1.9f))
                hard.traceStartDelay = 2.35f;

            if (Mathf.Approximately(hard.traceStepSeconds, 0.135f))
                hard.traceStepSeconds = 0.16f;
        }

        // Iosevka Term includes both solid and hollow square terminal glyphs.
        // The hollow square keeps the maze readable without filling the screen
        // with one solid block of accent color.
        if (string.IsNullOrEmpty(wallGlyph) || wallGlyph == "#")
            wallGlyph = "□";

        if (tuningVersion < 2)
        {
            // The original negative spacing looked fine with '#', but a full
            // block fills the complete glyph box and visibly overlapped the
            // rows above and below. Preserve custom values and only migrate the
            // original release value.
            if (Mathf.Approximately(mazeLineSpacing, -8f))
                mazeLineSpacing = 0f;

            if (mazeCellWidthEm <= 0f)
                mazeCellWidthEm = 0.62f;

            if (mazeLineHeightPercent <= 0f)
                mazeLineHeightPercent = 100f;
        }

        if (tuningVersion < 3)
        {
            // The first aligned version used the natural narrow monospace
            // advance. That made one-cell vertical corridors look pinched
            // compared with horizontal corridors. A wider cell advance makes
            // each logical maze tile much closer to square.
            if (Mathf.Approximately(mazeCellWidthEm, 0.62f))
                mazeCellWidthEm = 0.82f;
        }

        if (tuningVersion < 4)
        {
            // Make the pursuing trace easier to identify at a glance while
            // preserving any custom glyph chosen in the Inspector.
            if (string.IsNullOrEmpty(traceGlyph) || traceGlyph == "!")
                traceGlyph = "#";

            // Now that keys are placed along the guaranteed route, both
            // difficulties can apply slightly more pressure without forcing
            // unfair dead-end backtracking. Only migrate the previous defaults.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.traceStartDelay, 2.8f))
                    easy.traceStartDelay = 2.55f;

                if (Mathf.Approximately(easy.traceStepSeconds, 0.18f))
                    easy.traceStepSeconds = 0.17f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.traceStartDelay, 2.35f))
                    hard.traceStartDelay = 2.15f;

                if (Mathf.Approximately(hard.traceStepSeconds, 0.16f))
                    hard.traceStepSeconds = 0.15f;
            }
        }

        if (tuningVersion < 5)
        {
            // The terminal cell advance is intentionally wider than a normal
            // monospace glyph. The first pass reduced row height, but that made
            // horizontal wall rows visually crowd the corridor between them.
            if (Mathf.Approximately(mazeLineHeightPercent, 100f))
                mazeLineHeightPercent = 92f;
        }

        if (tuningVersion < 6)
        {
            // Give each logical row enough vertical advance that an empty cell
            // reads close to the same size as the 0.82em horizontal cell.
            // Auto-sizing keeps the complete maze inside the gameplay region.
            if (Mathf.Approximately(mazeLineHeightPercent, 92f))
                mazeLineHeightPercent = 112f;
        }

        if (tuningVersion < 7)
        {
            // The 112% pass made vertical corridors slightly more open than
            // horizontal corridors. Pull the row advance back just enough to
            // make the logical cells read square without returning to overlap.
            if (Mathf.Approximately(mazeLineHeightPercent, 112f))
                mazeLineHeightPercent = 106f;
        }

        if (tuningVersion < 8)
        {
            // Trace Escape is now a three-link breach. Existing prefabs may
            // deserialize the newly added field as zero, so migrate only that
            // unconfigured state and preserve deliberate custom values.
            if (roundsRequired <= 1)
                roundsRequired = 3;

            if (roundTransitionSeconds <= 0f)
                roundTransitionSeconds = 0.75f;
        }

        if (tuningVersion < 9)
        {
            // The security trace now actively hunts the packet by taking one
            // breadth-first-search step along the current shortest route every
            // trace tick. The existing timing values remain unchanged; the
            // smarter pursuit itself is the difficulty increase.
        }

        if (tuningVersion < 12)
        {
            // V12 removes all TMP glow and underlay halo. The trace now uses
            // the exact same hazard red as Firewall Runner obstacles.
            dangerColor = new Color(1f, 0.24f, 0.38f, 1f);
        }

        if (tuningVersion < 13)
        {
            // V13 adds only a small amount of extra pressure to the existing
            // shortest-path pursuer. Preserve deliberate custom values and
            // migrate only the prior defaults.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.traceStartDelay, 2.55f))
                    easy.traceStartDelay = 2.45f;

                if (Mathf.Approximately(easy.traceStepSeconds, 0.17f))
                    easy.traceStepSeconds = 0.165f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.traceStartDelay, 2.15f))
                    hard.traceStartDelay = 2.05f;

                if (Mathf.Approximately(hard.traceStepSeconds, 0.15f))
                    hard.traceStepSeconds = 0.145f;
            }
        }

        if (tuningVersion < 14)
        {
            // V14 adds looped mazes and bounded side-route key placement so the
            // objectives require deliberate detours without becoming impossible.
            if (wallGlyph == "█")
                wallGlyph = "□";

            if (easy != null)
            {
                if (easy.loopOpenings <= 0)
                    easy.loopOpenings = 7;
                if (easy.maximumKeyDetour <= 0)
                    easy.maximumKeyDetour = 12;
                if (easy.minimumKeySpacing <= 0)
                    easy.minimumKeySpacing = 5;
            }

            if (hard != null)
            {
                if (hard.loopOpenings <= 0)
                    hard.loopOpenings = 7;
                if (hard.maximumKeyDetour <= 0)
                    hard.maximumKeyDetour = 16;
                if (hard.minimumKeySpacing <= 0)
                    hard.minimumKeySpacing = 5;
            }
        }

        if (tuningVersion < 15)
        {
            // Keep the new looped maze and safe detour generation, but restore
            // the more aggressive security daemon that recomputes the shortest
            // route to the player on every trace tick. Slightly enlarge only the
            // hollow wall glyph so the cells almost join without changing the
            // logical maze spacing or collision grid.
            if (wallGlyphScalePercent <= 100f)
                wallGlyphScalePercent = 116f;
        }


        if (tuningVersion < 16)
        {
            // V16 makes the hollow wall squares substantially larger inside the
            // same logical cells. The grid and collisions do not change, but the
            // □ glyphs nearly meet so corridors read as solid terminal walls.
            if (wallGlyphScalePercent <= 116f)
                wallGlyphScalePercent = 150f;
        }


        if (tuningVersion < 17)
        {
            // The hollow square is much shorter than a normal terminal line.
            // Reducing the row advance makes the logical cells genuinely square
            // instead of leaving large vertical gaps. The extra vertical room is
            // spent on taller odd-sized mazes, keeping the board's on-screen
            // height close to the previous version while adding more routes.
            if (Mathf.Approximately(mazeLineHeightPercent, 106f))
                mazeLineHeightPercent = 84f;

            if (easy != null && easy.mazeHeight == 13)
                easy.mazeHeight = 17;

            if (hard != null && hard.mazeHeight == 15)
                hard.mazeHeight = 19;
        }

        if (tuningVersion < 18)
        {
            // V18 pulls the remaining row pitch in slightly so hollow cells read
            // as an even grid. Walls return to the muted terminal color, leaving
            // the player and open exit as the stronger accent elements.
            if (Mathf.Approximately(mazeLineHeightPercent, 84f))
                mazeLineHeightPercent = 80f;
        }

        if (tuningVersion < 19)
        {
            // V19 makes the final small row-pitch correction requested after the
            // taller mazes were introduced. Horizontal cell width is unchanged.
            if (Mathf.Approximately(mazeLineHeightPercent, 80f))
                mazeLineHeightPercent = 77f;
        }

        if (tuningVersion < 20)
        {
            // V20 makes the final two-percent row-pitch adjustment so the hollow
            // wall grid reads as evenly spaced vertically and horizontally.
            if (Mathf.Approximately(mazeLineHeightPercent, 77f))
                mazeLineHeightPercent = 75f;
        }

        if (tuningVersion < 21)
        {
            // V21 introduces the shared hacker-owned SABLE shell, the compact
            // game-first layout, and the purple/orange faction palette.
            backgroundColor = new Color(0.071f, 0.043f, 0.094f, 1f);
            surfaceColor = new Color(0.129f, 0.075f, 0.161f, 1f);
            raisedSurfaceColor = new Color(0.161f, 0.090f, 0.192f, 1f);
            structureColor = new Color(0.541f, 0.365f, 0.659f, 1f);
            accentColor = new Color(0.929f, 0.569f, 0.259f, 1f);
            objectiveColor = new Color(0.949f, 0.737f, 0.400f, 1f);
            textColor = new Color(0.933f, 0.910f, 0.894f, 1f);
            mutedTextColor = new Color(0.663f, 0.608f, 0.686f, 1f);
            dangerColor = new Color(0.875f, 0.251f, 0.361f, 1f);

            if (string.IsNullOrWhiteSpace(shellName))
                shellName = "SABLE";

            if (string.IsNullOrWhiteSpace(playerGlyph) || playerGlyph == "@" || playerGlyph == "◆")
                playerGlyph = "■";
        }

        if (tuningVersion < 22)
        {
            // V22 refines the shell for readability on the in-world laptop,
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
            dangerColor = new Color(0.890f, 0.325f, 0.431f, 1f);

            if (string.IsNullOrWhiteSpace(shellName))
                shellName = "SABLE";
        }

        if (tuningVersion < 23)
        {
            // V23 increases shell readability again, renames the OS to ZannOS,
            // updates the Trace Escape briefing copy, and refreshes its palette
            // so it matches the newer cyan-leaning slate look.
            backgroundColor = new Color(0.030f, 0.053f, 0.082f, 1f);
            surfaceColor = new Color(0.050f, 0.082f, 0.118f, 1f);
            raisedSurfaceColor = new Color(0.073f, 0.112f, 0.157f, 1f);
            structureColor = new Color(0.290f, 0.443f, 0.565f, 1f);
            accentColor = new Color(0.965f, 0.620f, 0.302f, 1f);
            objectiveColor = new Color(0.980f, 0.824f, 0.518f, 1f);
            textColor = new Color(0.960f, 0.954f, 0.935f, 1f);
            mutedTextColor = new Color(0.615f, 0.702f, 0.765f, 1f);
            dangerColor = new Color(0.920f, 0.341f, 0.439f, 1f);
            shellName = "ZannOS";

            if (Mathf.Approximately(mazeMinimumFontSize, 28f))
                mazeMinimumFontSize = 30f;

            if (Mathf.Approximately(mazeMaximumFontSize, 60f))
                mazeMaximumFontSize = 64f;
        }


        if (tuningVersion < 24)
        {
            // V24 deepens the blue-cyan shell background, raises the contrast of the
            // maze presentation, and shortens the briefing objective so it fits cleanly.
            backgroundColor = new Color(0.017f, 0.045f, 0.072f, 1f);
            surfaceColor = new Color(0.026f, 0.065f, 0.098f, 1f);
            raisedSurfaceColor = new Color(0.041f, 0.087f, 0.128f, 1f);
            structureColor = new Color(0.430f, 0.655f, 0.812f, 1f);
            accentColor = new Color(0.992f, 0.663f, 0.286f, 1f);
            objectiveColor = new Color(1.000f, 0.839f, 0.486f, 1f);
            textColor = new Color(0.972f, 0.968f, 0.949f, 1f);
            mutedTextColor = new Color(0.635f, 0.745f, 0.820f, 1f);
            dangerColor = new Color(0.949f, 0.365f, 0.455f, 1f);
            shellName = "ZannOS";

            if (Mathf.Approximately(mazeMinimumFontSize, 30f))
                mazeMinimumFontSize = 32f;

            if (Mathf.Approximately(mazeMaximumFontSize, 64f))
                mazeMaximumFontSize = 68f;
        }


        if (tuningVersion < 25)
        {
            // V25 shifts the shell slightly further toward cyan, strengthens contrast,
            // and switches the Trace Escape player marker to a solid square.
            backgroundColor = new Color(0.010f, 0.048f, 0.076f, 1f);
            surfaceColor = new Color(0.018f, 0.066f, 0.102f, 1f);
            raisedSurfaceColor = new Color(0.030f, 0.088f, 0.133f, 1f);
            structureColor = new Color(0.420f, 0.760f, 0.900f, 1f);
            accentColor = new Color(0.996f, 0.675f, 0.290f, 1f);
            objectiveColor = new Color(1.000f, 0.850f, 0.505f, 1f);
            textColor = new Color(0.975f, 0.972f, 0.955f, 1f);
            mutedTextColor = new Color(0.675f, 0.790f, 0.860f, 1f);
            dangerColor = new Color(0.955f, 0.388f, 0.470f, 1f);
            shellName = "ZannOS";
            playerGlyph = "■";
        }


        if (tuningVersion < 26)
        {
            // V26 shifts Trace Escape to a monochrome terminal-style shell with
            // phosphor-green UI, deeper black-green backgrounds, and higher ASCII-console contrast.
            backgroundColor = new Color(0.012f, 0.030f, 0.020f, 1f);
            surfaceColor = new Color(0.022f, 0.050f, 0.034f, 1f);
            raisedSurfaceColor = new Color(0.032f, 0.072f, 0.048f, 1f);
            structureColor = new Color(0.360f, 0.910f, 0.650f, 1f);
            accentColor = new Color(0.520f, 1.000f, 0.760f, 1f);
            objectiveColor = new Color(0.760f, 1.000f, 0.700f, 1f);
            textColor = new Color(0.900f, 1.000f, 0.920f, 1f);
            mutedTextColor = new Color(0.470f, 0.720f, 0.560f, 1f);
            dangerColor = new Color(1.000f, 0.360f, 0.420f, 1f);
            shellName = "ZannOS";
            playerGlyph = "■";
        }

        if (tuningVersion < 27)
        {
            // V27 nudges the player square upward slightly so it sits visually centered
            // inside the ASCII cell instead of touching the bottom wall.
            if (Mathf.Abs(playerGlyphVerticalOffsetEm) <= 0.0001f)
                playerGlyphVerticalOffsetEm = 0.08f;
        }


        if (tuningVersion < 28)
        {
            // V28 refines the terminal hierarchy: darker green-black surfaces,
            // slightly dimmer maze topology, a brighter mint player marker, and
            // a separate lime objective color for access keys.
            backgroundColor = new Color(0.007f, 0.022f, 0.014f, 1f);
            surfaceColor = new Color(0.014f, 0.040f, 0.026f, 1f);
            raisedSurfaceColor = new Color(0.022f, 0.055f, 0.036f, 1f);
            structureColor = new Color(0.250f, 0.780f, 0.540f, 1f);
            accentColor = new Color(0.680f, 1.000f, 0.820f, 1f);
            objectiveColor = new Color(0.920f, 0.940f, 0.520f, 1f);
            textColor = new Color(0.900f, 0.990f, 0.920f, 1f);
            mutedTextColor = new Color(0.340f, 0.580f, 0.440f, 1f);
            dangerColor = new Color(1.000f, 0.360f, 0.420f, 1f);
            shellName = "ZannOS";
            playerGlyph = "■";
        }



        if (tuningVersion < 29)
        {
            // V29 keeps the green terminal identity while replacing the warm lime
            // accents with cooler teal-mint values and improving the visual hierarchy
            // between shell, maze walls, player marker, keys, exits, and trace hazards.
            backgroundColor = new Color(0.006f, 0.022f, 0.015f, 1f);
            surfaceColor = new Color(0.012f, 0.038f, 0.029f, 1f);
            raisedSurfaceColor = new Color(0.018f, 0.052f, 0.040f, 1f);
            structureColor = new Color(0.220f, 0.720f, 0.560f, 1f);
            accentColor = new Color(0.660f, 1.000f, 0.840f, 1f);
            objectiveColor = new Color(0.460f, 0.940f, 0.780f, 1f);
            textColor = new Color(0.900f, 0.990f, 0.950f, 1f);
            mutedTextColor = new Color(0.340f, 0.600f, 0.500f, 1f);
            dangerColor = new Color(1.000f, 0.400f, 0.450f, 1f);
            shellName = "ZannOS";
            playerGlyph = "■";
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
            dangerColor = new Color(1.000f, 0.400f, 0.450f, 1f);
            playerGlyph = "■";
            shellName = "ZannOS";
        }

        if (tuningVersion < 31)
        {
            // V31 makes both difficulty profiles more forgiving while preserving
            // the core chase pressure. The trace waits longer and moves more slowly,
            // mazes are slightly smaller and more open, and hard uses two keys instead
            // of three so it remains challenging without becoming overwhelming.
            if (easy != null)
            {
                if (easy.mazeWidth == 25)
                    easy.mazeWidth = 23;
                if (easy.mazeHeight == 17)
                    easy.mazeHeight = 15;
                if (Mathf.Approximately(easy.traceStartDelay, 2.45f))
                    easy.traceStartDelay = 3.0f;
                if (Mathf.Approximately(easy.traceStepSeconds, 0.165f))
                    easy.traceStepSeconds = 0.185f;
                if (easy.loopOpenings == 7)
                    easy.loopOpenings = 10;
                if (easy.maximumKeyDetour == 12)
                    easy.maximumKeyDetour = 10;
                if (easy.minimumKeySpacing == 5)
                    easy.minimumKeySpacing = 4;
            }

            if (hard != null)
            {
                if (hard.mazeWidth == 31)
                    hard.mazeWidth = 29;
                if (hard.mazeHeight == 19)
                    hard.mazeHeight = 17;
                if (hard.accessKeyCount == 3)
                    hard.accessKeyCount = 2;
                if (Mathf.Approximately(hard.traceStartDelay, 2.05f))
                    hard.traceStartDelay = 2.65f;
                if (Mathf.Approximately(hard.traceStepSeconds, 0.145f))
                    hard.traceStepSeconds = 0.17f;
                if (hard.loopOpenings == 7)
                    hard.loopOpenings = 9;
                if (hard.maximumKeyDetour == 16)
                    hard.maximumKeyDetour = 13;
                if (hard.minimumKeySpacing == 5)
                    hard.minimumKeySpacing = 4;
            }
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
            dangerColor = new Color(1.000f, 0.400f, 0.450f, 1f);
        }

        tuningVersion = CurrentTuningVersion;
    }

    private void OnValidate()
    {
        EnsureDifficultySettingsExist();
        ApplyVersionedTuning();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        navigationDeadzone = Mathf.Clamp(navigationDeadzone, 0.05f, 0.95f);
        roundsRequired = Mathf.Clamp(roundsRequired, 1, 5);
        roundTransitionSeconds = Mathf.Max(0f, roundTransitionSeconds);
        failureHoldSeconds = Mathf.Max(0f, failureHoldSeconds);
        separatorThickness = Mathf.Clamp(separatorThickness, 2f, 20f);
        mazeMinimumFontSize = Mathf.Clamp(mazeMinimumFontSize, 20f, 72f);
        mazeMaximumFontSize = Mathf.Max(mazeMinimumFontSize, mazeMaximumFontSize);
        mazeLineSpacing = Mathf.Clamp(mazeLineSpacing, -30f, 30f);
        mazeCellWidthEm = Mathf.Clamp(mazeCellWidthEm, 0.4f, 1.2f);
        mazeLineHeightPercent = Mathf.Clamp(mazeLineHeightPercent, 60f, 140f);
        wallGlyphScalePercent = Mathf.Clamp(wallGlyphScalePercent, 100f, 175f);

        if (string.IsNullOrEmpty(playerGlyph))
            playerGlyph = "■";
        playerGlyphVerticalOffsetEm = Mathf.Clamp(playerGlyphVerticalOffsetEm, -0.5f, 0.5f);
        if (string.IsNullOrEmpty(wallGlyph))
            wallGlyph = "□";
        if (string.IsNullOrEmpty(keyGlyph))
            keyGlyph = "K";
        if (string.IsNullOrEmpty(lockedExitGlyph))
            lockedExitGlyph = "X";
        if (string.IsNullOrEmpty(openExitGlyph))
            openExitGlyph = ">";
        if (string.IsNullOrEmpty(traceGlyph))
            traceGlyph = "#";
        if (string.IsNullOrEmpty(traceTrailGlyph))
            traceTrailGlyph = ":";
    }
}
